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
        /// <summary>不需要出现在卡面上的属性 → 理由。改动这张表等于改设计决定,请写清理由。</summary>
        private static readonly Dictionary<string, string> Exempt = new Dictionary<string, string>
        {
            // EffectDef
            ["Kind"] = "效果种类本身,由 EffectsText 的 switch 分派,不是要印的字段",
            ["Value"] = "数值,每个分支各自印(shown 变量)",
            ["Turns"] = "回合数,由需要它的分支各自印(致盲/HoT/反弹)",
            ["TargetAll"] = "由需要它的分支各自印(全体驱散/群体治疗/全体致盲)",
            ["SummonCount"] = "召唤只数,Summon 分支印",
            ["SummonAttack"] = "召唤物攻击,Summon 分支印",
            ["SummonChar"] = "召唤物字形,Summon 分支印(与施法字同名时省略)",
            ["ShapePercent"] = "由 ShapeSuffix 印(等于 100 时省略)",
            ["Shots"] = "由 ShapeSuffix 印(仅 Volley)",
            // SummonPassive
            ["Speed"] = "召唤物被动,由 PassiveText 印",
            ["Thorns"] = "召唤物被动,由 PassiveText 印",
            ["HealAlly"] = "召唤物被动,由 PassiveText 印",
            ["OnHitBurn"] = "召唤物被动,由 PassiveText 印",
            ["OnHitBurnAll"] = "召唤物被动,由 PassiveText 印",
            ["OnHitCurse"] = "召唤物被动,由 PassiveText 印",
            ["Dodge"] = "召唤物被动,由 PassiveText 印",
            ["Ranged"] = "召唤物被动,由 PassiveText 印",
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

        [Test]
        public void EveryEffectDefField_IsRenderedOrExempt()
        {
            var src = CharInfoSource();
            var missing = PropertyNames(typeof(EffectDef))
                .Where(n => !Exempt.ContainsKey(n) && !src.Contains(n))
                .OrderBy(n => n)
                .ToArray();
            Assert.That(missing, Is.Empty,
                "EffectDef 有字段既没在 CharInfo 里渲染、也没进豁免名单:\n  "
                + string.Join("\n  ", missing)
                + "\n补上卡面文案,或把它加进 Exempt 并写明为什么玩家不该看到它。");
        }

        [Test]
        public void EverySummonPassiveField_IsRenderedOrExempt()
        {
            var src = CharInfoSource();
            var missing = PropertyNames(typeof(SummonPassive))
                .Where(n => !Exempt.ContainsKey(n) && !src.Contains(n))
                .OrderBy(n => n)
                .ToArray();
            Assert.That(missing, Is.Empty,
                "SummonPassive 有字段既没在 CharInfo 里渲染、也没进豁免名单:\n  "
                + string.Join("\n  ", missing)
                + "\n补上卡面文案,或把它加进 Exempt 并写明为什么玩家不该看到它。");
        }

        [Test]
        public void ExemptList_HasNoStaleEntries()
        {
            // 豁免名单里若留着已经删掉的属性名,说明名单在腐烂 —— 下次读它的人会被误导
            var live = new HashSet<string>(
                PropertyNames(typeof(EffectDef)).Concat(PropertyNames(typeof(SummonPassive))));
            var stale = Exempt.Keys.Where(k => !live.Contains(k)).OrderBy(k => k).ToArray();
            Assert.That(stale, Is.Empty,
                "豁免名单里有 EffectDef / SummonPassive 上已不存在的属性:\n  "
                + string.Join("\n  ", stale));
        }

        [Test]
        public void ExemptReasons_AreNotBlank()
        {
            var blank = Exempt.Where(kv => string.IsNullOrWhiteSpace(kv.Value))
                .Select(kv => kv.Key).OrderBy(k => k).ToArray();
            Assert.That(blank, Is.Empty,
                "豁免名单的理由不能留空(空理由等于没做决定):\n  " + string.Join("\n  ", blank));
        }
    }
}
