using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>卡面渲染覆盖率(2026-08-23)。
    ///
    /// **这条测试存在的理由**:`EffectDef` 每加一个字段,`BattleEngine` 一定会被改到
    /// (不然功能不生效),但 `CharInfo` 的卡面渲染是否跟进**全凭当次记不记得** ——
    /// 而没有任何既有机制会发现漏掉:Core 单测测的是引擎行为,离线编译只管编译过,
    /// 三条对账测试只保证「用到的 key 在表里」。
    ///
    /// 结果是斩杀(铡/镰/剿)、召唤物被动(荆的荆棘/柳的闪避/桤的速度…)、桂的全场加盾、
    /// 剁的两段、刺的偷袭 —— 5 类字段 12 张字,玩家在卡面上一个字都看不到。
    /// 柳(血80/攻30)与松(血120/攻30)因此在卡面上除血量外毫无区别,而柳实际带 50% 闪避。
    /// 斩杀那次的提交范围写着 `feat(core)`,压根没打算碰 Presentation。
    ///
    /// 所以本测试把「凭记性」换成「机器守着」:`EffectDef` / `SummonPassive` 的每个属性,
    /// 要么在 `CharInfo.cs` 里有渲染,要么在下面的豁免名单里写明**为什么不该显示**。
    /// 加新字段时两条路都得主动选一条,没有「忘了」这个选项。
    ///
    /// ⚠ 用**反射**拿属性名而不是手写一张 json→C# 的映射表:映射表本身会漏,
    /// 而漏掉的那一项恰恰不会报错(它压根不在被检查的集合里)。反射拿到的是真实属性全集。
    ///
    /// ⚠ 读 `CharInfo.cs` 的**源码文本**而不是调用它:Tests asmdef 是 overrideReferences,
    /// 只放行 Core / Data / nunit,引不到 Presentation 程序集(与 StringsTableTests 同一个约束)。</summary>
    public sealed class CardFaceCoverageTests
    {
        /// <summary>EffectDef 侧不需要出现在卡面上的属性 → 理由。改动这张表等于改设计决定,请写清理由。</summary>
        private static readonly Dictionary<string, string> EffectExempt = new Dictionary<string, string>
        {
            ["Kind"] = "效果种类本身,由 EffectsText 的 switch 分派,不是要印的字段",
            ["Value"] = "数值,每个分支各自印(shown 变量)",
            ["Turns"] = "回合数,由需要它的分支各自印(致盲/HoT/反弹)",
            ["TargetAll"] = "由需要它的分支各自印(全体驱散/群体治疗/全体致盲)",
            ["SummonCount"] = "召唤只数,Summon 分支印",
            ["SummonAttack"] = "召唤物攻击,Summon 分支印",
            ["SummonChar"] = "召唤物字形,Summon 分支印(与施法字同名时省略)",
            ["ShapePercent"] = "由 ShapeSuffix 印(等于 100 时省略)",
            ["Shots"] = "由 ShapeSuffix 印(Volley 报发数 / Chain 报跳数)",
        };

        /// <summary>SummonPassive 侧的豁免。与 EffectDef 侧**分开两张表**(2026-08-29):
        /// 同名属性在两侧的渲染责任人不同 —— `Shape` / `ShapePercent` / `Shots` 在效果侧由
        /// ShapeSuffix 印、在被动侧必须由 PassiveText 印。合成一张表时,effect 侧写的
        /// 「由 ShapeSuffix 印」会连带把被动侧一起豁免掉,而 ShapeSuffix 根本不在
        /// PassiveText 的调用链上 —— 剑(横扫)/枪(贯穿)/锥(连发)三只召唤物的形状因此
        /// 在卡面上一个字都没有,与柳的闪避是同型的漏印。</summary>
        private static readonly Dictionary<string, string> PassiveExempt = new Dictionary<string, string>
        {
            ["OnHitFreezeTurns"] = "回合数,与 OnHitFreezeChance 合成一句由 PassiveText 印",
            ["OnHitSlowTurns"] = "回合数,与 OnHitSlowPercent 合成一句由 PassiveText 印",
            ["ShapePercent"] = "非主目标百分比,与 Shape 合成一句由 PassiveText 印",
            ["Shots"] = "发数/跳数,与 Shape 合成一句由 PassiveText 印",
        };

        private static string CharInfoSource()
        {
            var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Brushblade")))
                dir = dir.Parent;
            Assert.That(dir, Is.Not.Null, "找不到含 Brushblade/ 的仓库根");
            var path = Path.Combine(dir.FullName, "Brushblade", "Assets", "_Project",
                "Presentation", "CharInfo.cs");
            Assert.That(File.Exists(path), Is.True, $"找不到 {path}");
            return File.ReadAllText(path);
        }

        private static IEnumerable<string> PropertyNames(Type type) =>
            type.GetProperties().Select(p => p.Name);

        /// <summary>按大括号配平抠出一个方法的**方法体**。全文 Contains 之所以不够:
        /// SummonPassive.Shape 与 EffectDef.Shape 同名,效果侧的 `e.Shape` 让全文检查
        /// 误判被动侧也已渲染 —— 漏印了才是真相。被动侧只认 PassiveText 里的引用。</summary>
        private static string MethodBody(string src, string signatureFragment)
        {
            int at = src.IndexOf(signatureFragment, StringComparison.Ordinal);
            Assert.That(at, Is.GreaterThanOrEqualTo(0), $"CharInfo.cs 里找不到 {signatureFragment}");
            int open = src.IndexOf('{', at);
            Assert.That(open, Is.GreaterThanOrEqualTo(0), $"{signatureFragment} 后找不到方法体");
            int depth = 0;
            for (int i = open; i < src.Length; i++)
            {
                if (src[i] == '{') depth++;
                else if (src[i] == '}' && --depth == 0) return src.Substring(open, i - open + 1);
            }
            Assert.Fail($"{signatureFragment} 的大括号没配平");
            return "";
        }

        [Test]
        public void EveryEffectDefField_IsRenderedOrExempt()
        {
            var src = CharInfoSource();
            var missing = PropertyNames(typeof(EffectDef))
                .Where(n => !EffectExempt.ContainsKey(n) && !src.Contains(n))
                .OrderBy(n => n)
                .ToArray();
            Assert.That(missing, Is.Empty,
                "EffectDef 有字段既没在 CharInfo 里渲染、也没进豁免名单:\n  "
                + string.Join("\n  ", missing)
                + "\n补上卡面文案,或把它加进 EffectExempt 并写明为什么玩家不该看到它。");
        }

        [Test]
        public void EverySummonPassiveField_IsRenderedOrExempt()
        {
            // 只看 PassiveText 的方法体 —— 召唤物被动的卡面文案全在这一个函数里出
            var body = MethodBody(CharInfoSource(), "string PassiveText(SummonPassive");
            var missing = PropertyNames(typeof(SummonPassive))
                .Where(n => !PassiveExempt.ContainsKey(n) && !body.Contains(n))
                .OrderBy(n => n)
                .ToArray();
            Assert.That(missing, Is.Empty,
                "SummonPassive 有字段既没在 CharInfo.PassiveText 里渲染、也没进豁免名单:\n  "
                + string.Join("\n  ", missing)
                + "\n补上卡面文案,或把它加进 PassiveExempt 并写明为什么玩家不该看到它。");
        }

        /// <summary>目标形状是**枚举值**级的漏印,字段级检查盖不到(2026-08-29):
        /// `ShapeLabel` 的 switch 少一个分支不会编译报错,只会静默落到 `_ => 单体` ——
        /// 溃(弹射 3 跳、每跳 ×50%)因此在卡面上写着「单体 30 伤」。</summary>
        [Test]
        public void EveryTargetShape_HasLabelAndSuffixCase()
        {
            var src = CharInfoSource();
            // 认「只吃 TargetShape」的那两个重载 —— 它们是形状文案的唯一出处,
            // 吃 EffectDef 的重载只是转发(表达式体,没有自己的方法体可抠)
            var label = MethodBody(src, "string ShapeLabel(TargetShape");
            var suffix = MethodBody(src, "string ShapeSuffix(TargetShape");
            var missing = Enum.GetNames(typeof(TargetShape))
                .Where(n => n != nameof(TargetShape.Single))   // Single 是 ShapeLabel 的 _ 兜底
                .Where(n => !label.Contains(n) || !suffix.Contains(n))
                .OrderBy(n => n)
                .ToArray();
            Assert.That(missing, Is.Empty,
                "TargetShape 有取值在 ShapeLabel / ShapeSuffix 里没有分支:\n  "
                + string.Join("\n  ", missing)
                + "\n没有分支不会编译报错,只会静默显示成「单体」。");
        }

        /// <summary>召唤物被动侧(枪 = 贯穿、剑 = 横扫、锥 = 连发)必须走**同一对**
        /// ShapeLabel / ShapeSuffix,而不是自己再抄一份 switch:抄一份就意味着加新形状时
        /// 要记得改两处,而「记得」正是本文件存在的理由。共用了就天然覆盖全部取值,
        /// 上面那条枚举测试同时守住两侧。</summary>
        [Test]
        public void PassiveText_ReusesSharedShapeRenderers()
        {
            var body = MethodBody(CharInfoSource(), "string PassiveText(SummonPassive");
            Assert.That(body.Contains("ShapeLabel("), Is.True,
                "PassiveText 没有调用 ShapeLabel —— 召唤物被动的形状要么没印,要么抄了第二份 switch");
            Assert.That(body.Contains("ShapeSuffix("), Is.True,
                "PassiveText 没有调用 ShapeSuffix —— 溅射百分比 / 发数 / 跳数会漏印");
        }

        [Test]
        public void ExemptList_HasNoStaleEntries()
        {
            // 豁免名单里若留着已经删掉的属性名,说明名单在腐烂 —— 下次读它的人会被误导
            var stale = EffectExempt.Keys
                .Where(k => !PropertyNames(typeof(EffectDef)).Contains(k))
                .Concat(PassiveExempt.Keys
                    .Where(k => !PropertyNames(typeof(SummonPassive)).Contains(k)))
                .OrderBy(k => k).ToArray();
            Assert.That(stale, Is.Empty,
                "豁免名单里有 EffectDef / SummonPassive 上已不存在的属性:\n  "
                + string.Join("\n  ", stale));
        }

        [Test]
        public void ExemptReasons_AreNotBlank()
        {
            var blank = EffectExempt.Concat(PassiveExempt)
                .Where(kv => string.IsNullOrWhiteSpace(kv.Value))
                .Select(kv => kv.Key).OrderBy(k => k).ToArray();
            Assert.That(blank, Is.Empty,
                "豁免名单的理由不能留空(空理由等于没做决定):\n  " + string.Join("\n  ", blank));
        }
    }
}
