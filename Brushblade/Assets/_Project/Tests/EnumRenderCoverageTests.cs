using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Brushblade.Core;

namespace Brushblade.Core.Tests
{
    /// <summary>全枚举的表现层渲染覆盖(2026-09-06,P1)。
    ///
    /// 为什么要这张网:2026-09-05 新增 EffectKind.Charm / StatusKind.Charm 之后,
    /// 卡面会直接印英文「Charm」、状态详情整条凭空消失、战斗画面零提示 ——
    /// 而 **11 轮任务评审 + 一次全分支终审都没抓到**,离线编译也是绿的。
    /// 根因:C# 的 switch 对未覆盖的枚举值不报编译错,相关 switch 又都带 default
    /// 兜底,连警告都没有;而仓库里从来没有一条测试枚举这两个 Kind 要求它们被渲染。
    /// CardFaceCoverageTests 覆盖的是 EffectDef 的**字段**与 TargetShape 的**枚举值**,
    /// 盖不到这两个。
    ///
    /// ⚠ **2026-09-06 review 打回一次**:初版判据是裸 `src.Contains($"Xxx.{n}")`,被三个
    /// 真实反例证实是假绿——`CharInfo.cs` 注释里一句「与 BattleEngine 的 EffectKind.Dispel
    /// 分支同口径」、`StatusText.cs` 类头文档提一句 `StatusKind.ApBoost`、`BattleView.cs`
    /// 里给玩家召唤物用的 `AddSummonStatusChips` 也提了 `StatusKind.Curse`——删掉真实分支,
    /// 三条护栏全部照样绿。收紧成两件事:① <see cref="StripComments"/> 剥掉注释再匹配;
    /// ② `EveryEnemyDebuff_HasBattleViewChip` 用 <see cref="MethodBody"/> 把敌人格 chip
    /// 的方法体抠出来单独匹配,不再搜整个文件。收紧后当场多红出两个真实的零显示缺陷
    /// (Bleed、ArmorBreak),已在本任务一并修掉——见 task-1-report.md。
    ///
    /// 与 CardFaceCoverageTests 同一套手法:读源码文本而不是反射 Presentation ——
    /// Tests asmdef 是 overrideReferences 且只放行 nunit + Core + Data。</summary>
    public sealed class EnumRenderCoverageTests
    {
        /// <summary>玩家永远看不到卡面文案的 EffectKind → 理由。P0 休眠的那批
        /// (PierceBuff/Cleanse/Dispel/ApBoost/BurnNoDecay)不算豁免 —— 它们只是当前
        /// 字表无载体,CharInfo 里仍然有渲染分支,P2 复活时不需要再补线。</summary>
        private static readonly Dictionary<string, string> EffectKindExempt = new Dictionary<string, string>();

        /// <summary>玩家永远看不到状态详情的 StatusKind → 理由。</summary>
        private static readonly Dictionary<string, string> StatusKindExempt = new Dictionary<string, string>
        {
            [nameof(StatusKind.ObsoleteDamageReduction)] =
                "废弃占位,序号锁定不得删除/复用(StatusEffect.cs 原注释);引擎按约定不得再把它" +
                "构造进真实 StatusBag,玩家不可能在游戏中撞见这个值",
        };

        /// <summary>手工列「会挂在敌人身上」的 StatusKind —— 不推导,写死一张诚实的表。
        /// 这份列表本身就是权威(引擎里没有对应的分类),不是从别处派生。
        ///
        /// ⚠ **刻意不列入 `StatusKind.AttackBuff`**,尽管引擎确实会把它施加到敌人身上
        /// (标点小妖 `BattleEngine.cs:1887` 起给同伴加攻、焦痕 `BattleEngine.cs:3300` 起
        /// 受击自燃加攻)——2026-09-06 review 抓到过一次「以为是玩家专属所以不列」的错误
        /// 判断,这里重新给出理由而不是简单不列:敌人格头行的攻击力数字本身就实时读
        /// `enemy.Attack`(`EnemyDef.cs` 的计算属性,percent 项已经把 AttackBuff 算进去),
        /// 玩家能直接看到这只怪变强了,不像玩家自己的 AttackBuff——战斗界面完全不显示玩家
        /// 攻击力(见 `BattleView.cs:1674` 那条注释),不出 chip 就是彻底零反馈,两者不同处境。
        /// 两个来源各自还有常驻的能力 chip(`enemy.ability.buff.chip` / scorch / sear 图标)
        /// 解释「为什么变强」,加多少的 delta 由 `EnemyInfo.BuildFigures` 的 attackNote 在
        /// 详情弹窗里说全,战场速览没有必要为同一件事再开一条 chip。</summary>
        private static readonly string[] EnemyFacingStatuses =
        {
            nameof(StatusKind.Burn),
            nameof(StatusKind.Bleed),
            nameof(StatusKind.Freeze),
            nameof(StatusKind.SpeedModifier), // 减速(magnitude < 0 那一支)
            nameof(StatusKind.Blind),
            nameof(StatusKind.Silence),
            nameof(StatusKind.Curse),
            nameof(StatusKind.ArmorBreak),
            nameof(StatusKind.BurnNoDecay),
            nameof(StatusKind.Charm),
        };

        private static string Root()
        {
            // 只能用 TestContext.TestDirectory:AppContext.BaseDirectory 在 Unity Test Runner
            // 下指向编辑器安装目录,往上永远找不到含 Brushblade/ 的父目录。
            var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Brushblade")))
                dir = dir.Parent;
            Assert.That(dir, Is.Not.Null, "找不到含 Brushblade/ 的仓库根");
            return dir.FullName;
        }

        private static string RawSource(string relative) =>
            File.ReadAllText(Path.Combine(Root(), "Brushblade/Assets/_Project/Presentation", relative));

        /// <summary>剥掉 <c>//</c> 行注释与 <c>/* */</c> 块注释(<c>///</c> 文档注释是行注释的
        /// 特例,同样被剥),字符串/字符字面量内部原样保留、不误删。
        ///
        /// 这是本轮 review 打回的根因修复:注释里提一句「与 XxxKind.Yyy 同口径」,不剥注释的话
        /// 会让下面的 `Contains` 判据把「早就被删掉的真实分支」误判成「还在」——
        /// `CharInfo.cs` 的 Dispel、`StatusText.cs` 的 ApBoost 都是真实踩过的案例。</summary>
        private static string StripComments(string src)
        {
            var sb = new StringBuilder(src.Length);
            bool inLineComment = false, inBlockComment = false, inString = false, inChar = false;
            for (int i = 0; i < src.Length; i++)
            {
                char c = src[i];
                char next = i + 1 < src.Length ? src[i + 1] : '\0';
                if (inLineComment)
                {
                    if (c == '\n') { inLineComment = false; sb.Append(c); }
                    continue;
                }
                if (inBlockComment)
                {
                    if (c == '*' && next == '/') { inBlockComment = false; i++; }
                    continue;
                }
                if (inString)
                {
                    sb.Append(c);
                    if (c == '\\' && i + 1 < src.Length) { sb.Append(src[++i]); continue; }
                    if (c == '"') inString = false;
                    continue;
                }
                if (inChar)
                {
                    sb.Append(c);
                    if (c == '\\' && i + 1 < src.Length) { sb.Append(src[++i]); continue; }
                    if (c == '\'') inChar = false;
                    continue;
                }
                if (c == '"') { inString = true; sb.Append(c); continue; }
                if (c == '\'') { inChar = true; sb.Append(c); continue; }
                if (c == '/' && next == '/') { inLineComment = true; i++; continue; }
                if (c == '/' && next == '*') { inBlockComment = true; i++; continue; }
                sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>整词匹配 <c>"Prefix.Name"</c>,不用裸 `Contains` —— 枚举名互为前缀时
        /// (`StatusKind.Burn` 是 `StatusKind.BurnNoDecay` 的前缀子串)裸 `Contains` 会把
        /// 「只剩 BurnNoDecay 分支、Burn 本身已被删」误判成「Burn 还在」,同样是假绿,
        /// 2026-09-06 review 收紧判据时顺带查出。</summary>
        private static bool ContainsKindRef(string strippedSrc, string enumPrefix, string name) =>
            Regex.IsMatch(strippedSrc, Regex.Escape($"{enumPrefix}.{name}") + @"\b");

        private static string Source(string relative) => StripComments(RawSource(relative));

        /// <summary>按大括号配平抠出一个方法的**方法体**(与 `CardFaceCoverageTests.MethodBody`
        /// 同手法)。`EveryEnemyDebuff_HasBattleViewChip` 用它把敌人格 chip 的方法体单独抠出来,
        /// 不再对整个 `BattleView.cs` 做全文 `Contains`——`BattleView.cs` 里还有一个专给玩家
        /// 召唤物用的 `AddSummonStatusChips`,同样会提到 `StatusKind.Curse` 等一大批状态,
        /// 全文匹配会被这个不相干的方法喂饱,2026-09-06 review 就是这样抓到假绿的。</summary>
        private static string MethodBody(string src, string signatureFragment)
        {
            int at = src.IndexOf(signatureFragment, StringComparison.Ordinal);
            Assert.That(at, Is.GreaterThanOrEqualTo(0), $"源码里找不到 {signatureFragment}");
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

        /// <summary>每个 EffectKind 都要在 CharInfo.EffectsText 里有分支,或进豁免名单。
        ///
        /// 漏一个的表现不是报错,是卡面上**直接印英文枚举名**(EffectsText 的兜底是
        /// `_ => e.Kind.ToString()`)—— 2026-09-02 的势/水势、2026-09-05 的 Charm 都是这样。</summary>
        [Test]
        public void EveryEffectKind_IsRenderedInCharInfoOrExempt()
        {
            var src = Source("CharInfo.cs");
            var missing = Enum.GetNames(typeof(EffectKind))
                .Where(n => !EffectKindExempt.ContainsKey(n) && !ContainsKindRef(src, "EffectKind", n))
                .OrderBy(n => n).ToArray();
            Assert.That(missing, Is.Empty,
                "这些 EffectKind 在 CharInfo 里没有渲染分支,卡面会印出英文枚举名:\n  "
                + string.Join("\n  ", missing)
                + "\n补上卡面文案,或加进 EffectKindExempt 并写明为什么玩家不该看到它。");
        }

        /// <summary>每个 StatusKind 都要在 StatusText.Of 里有 case,或进豁免名单。
        ///
        /// 漏一个的表现是**整条状态在详情面板里凭空消失**(Of 的 default 返回 None,
        /// 四个字段全 null),不是显示错误文案 —— 比印错字更难发现。</summary>
        [Test]
        public void EveryStatusKind_IsRenderedInStatusTextOrExempt()
        {
            var src = Source("UI/StatusText.cs");
            var missing = Enum.GetNames(typeof(StatusKind))
                .Where(n => !StatusKindExempt.ContainsKey(n) && !ContainsKindRef(src, "StatusKind", n))
                .OrderBy(n => n).ToArray();
            Assert.That(missing, Is.Empty,
                "这些 StatusKind 在 StatusText.Of 里没有 case,详情面板会整条不显示:\n  "
                + string.Join("\n  ", missing)
                + "\n补上文案,或加进 StatusKindExempt 并写明理由。");
        }

        /// <summary>敌人身上会出现的减益,要在 BattleView 敌人格 chip **那个方法体**里有提示。
        ///
        /// 那是一条**手写的 if 链**,不在里面就不显示 —— 冻结/减速当年零显示踩的就是这个。
        /// 只查会挂在敌人身上的那些:玩家专属增益(战意/厚/泉…)不该出现在敌人格上。
        ///
        /// 只在 `DrawEnemies()` 的方法体里找,不搜整个文件——理由见 <see cref="MethodBody"/>
        /// 的注释。</summary>
        [Test]
        public void EveryEnemyDebuff_HasBattleViewChip()
        {
            var full = Source("BattleView.cs");
            var body = MethodBody(full, "private void DrawEnemies()");
            var missing = EnemyFacingStatuses
                .Where(n => !ContainsKindRef(body, "StatusKind", n))
                .OrderBy(n => n).ToArray();
            Assert.That(missing, Is.Empty,
                "这些会挂在敌人身上的状态,战斗画面的敌人格没有任何提示:\n  "
                + string.Join("\n  ", missing));
        }

        /// <summary>豁免名单不许有已经不存在的枚举名(改名/删除后的残留)。</summary>
        [Test]
        public void ExemptLists_HaveNoStaleEntries()
        {
            var effectNames = Enum.GetNames(typeof(EffectKind));
            var statusNames = Enum.GetNames(typeof(StatusKind));
            var stale = EffectKindExempt.Keys.Where(k => !effectNames.Contains(k))
                .Concat(StatusKindExempt.Keys.Where(k => !statusNames.Contains(k)))
                .OrderBy(k => k).ToArray();
            Assert.That(stale, Is.Empty, "豁免名单里有已不存在的枚举名:\n  " + string.Join("\n  ", stale));
        }
    }
}
