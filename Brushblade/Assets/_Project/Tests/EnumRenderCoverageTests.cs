using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        /// 这份列表本身就是权威(引擎里没有对应的分类),不是从别处派生。</summary>
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

        private static string Source(string relative) =>
            File.ReadAllText(Path.Combine(Root(), "Brushblade/Assets/_Project/Presentation", relative));

        /// <summary>每个 EffectKind 都要在 CharInfo.EffectsText 里有分支,或进豁免名单。
        ///
        /// 漏一个的表现不是报错,是卡面上**直接印英文枚举名**(EffectsText 的兜底是
        /// `_ => e.Kind.ToString()`)—— 2026-09-02 的势/水势、2026-09-05 的 Charm 都是这样。</summary>
        [Test]
        public void EveryEffectKind_IsRenderedInCharInfoOrExempt()
        {
            var src = Source("CharInfo.cs");
            var missing = Enum.GetNames(typeof(EffectKind))
                .Where(n => !EffectKindExempt.ContainsKey(n) && !src.Contains($"EffectKind.{n}"))
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
                .Where(n => !StatusKindExempt.ContainsKey(n) && !src.Contains($"StatusKind.{n}"))
                .OrderBy(n => n).ToArray();
            Assert.That(missing, Is.Empty,
                "这些 StatusKind 在 StatusText.Of 里没有 case,详情面板会整条不显示:\n  "
                + string.Join("\n  ", missing)
                + "\n补上文案,或加进 StatusKindExempt 并写明理由。");
        }

        /// <summary>敌人身上会出现的减益,要在 BattleView 的敌人格 chip 列表里有提示。
        ///
        /// 那是一条**手写的 if 链**,不在里面就不显示 —— 冻结/减速当年零显示踩的就是这个。
        /// 只查会挂在敌人身上的那些:玩家专属增益(战意/厚/泉…)不该出现在敌人格上。</summary>
        [Test]
        public void EveryEnemyDebuff_HasBattleViewChip()
        {
            var src = Source("BattleView.cs");
            var missing = EnemyFacingStatuses
                .Where(n => !src.Contains($"StatusKind.{n}"))
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
