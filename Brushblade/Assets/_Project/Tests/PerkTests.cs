using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    public class PerkTests
    {
        // ---- 主界面技能红点(2026-08-28):有任意一条现在就能升 ----

        [Test]
        public void HasUpgradable_FalseForBrandNewSave() // 1 级、0 墨:一条都够不着
        {
            Assert.That(PerkRules.HasUpgradable(new MetaState()), Is.False);
        }

        [Test]
        public void HasUpgradable_TrueOnceOnePerkIsAffordable()
        {
            var meta = new MetaState { CharacterXp = 100, Ink = 200 }; // 2 级 + 养元首级 200 墨
            Assert.That(PerkRules.CanUpgradePerk(meta, "yangyuan"), Is.True, "夹具自检");
            Assert.That(PerkRules.HasUpgradable(meta), Is.True);
        }

        [Test]
        public void HasUpgradable_FalseWhenInkIsOneShort() // 只差 1 墨也不亮
        {
            var meta = new MetaState { CharacterXp = 100, Ink = 199 };
            Assert.That(PerkRules.HasUpgradable(meta), Is.False);
        }

        [Test]
        public void HasUpgradable_FalseWhenLevelGateBlocksTheOnlyAffordableOne()
        {
            // 墨锭管够,但 1 级角色一条都没解锁(最低的养元要 2 级)
            var meta = new MetaState { Ink = 999_999 };
            Assert.That(PerkRules.HasUpgradable(meta), Is.False);
        }

        [Test]
        public void HasUpgradable_FalseWhenEveryPerkIsMaxed()
        {
            var meta = new MetaState { CharacterXp = 999_999, Ink = 999_999 };
            foreach (var def in PerkRules.All)
                meta.PerkLevels[def.Id] = def.MaxLevel;
            Assert.That(PerkRules.HasUpgradable(meta), Is.False);
        }

        [Test]
        public void PerkLevel_DefaultsToZero()
        {
            var meta = new MetaState();
            Assert.That(PerkRules.PerkLevel(meta, "yangyuan"), Is.EqualTo(0));
        }

        [Test]
        public void Bonus_EqualsLevelTimesPerLevelValue()
        {
            var meta = new MetaState();
            meta.PerkLevels["yangyuan"] = 3;  // 养元 +100/级
            meta.PerkLevels["yiqi"] = 2;      // 一气 +1/级
            Assert.That(PerkRules.HpBonus(meta), Is.EqualTo(300));
            Assert.That(PerkRules.ApBonus(meta), Is.EqualTo(2));
            Assert.That(PerkRules.ShieldBonus(meta), Is.EqualTo(0));
        }

        [Test]
        public void Yiqi_MaxLevelIsTwo() // 平衡硬线:AP 上限 2
        {
            Assert.That(PerkRules.Get("yiqi").MaxLevel, Is.EqualTo(2));
        }

        [Test]
        public void Upgrade_RejectedBelowUnlockLevel() // 角色等级不足→拒(仅 0→1 校验)
        {
            var meta = new MetaState { Ink = 9999, CharacterXp = 0 }; // 1 级
            Assert.That(PerkRules.TryUpgradePerk(meta, "yiqi"), Is.False); // 一气需 6 级
            Assert.That(PerkRules.PerkLevel(meta, "yiqi"), Is.EqualTo(0));
            Assert.That(meta.Ink, Is.EqualTo(9999)); // 拒绝不扣墨锭
        }

        [Test]
        public void Upgrade_RejectedWithoutInk()
        {
            var meta = new MetaState { Ink = 100, CharacterXp = 100 }; // 2 级,养元解锁 200
            Assert.That(PerkRules.TryUpgradePerk(meta, "yangyuan"), Is.False);
            Assert.That(PerkRules.PerkLevel(meta, "yangyuan"), Is.EqualTo(0));
        }

        [Test]
        public void Upgrade_SucceedsAndDeductsInk()
        {
            var meta = new MetaState { Ink = 300, CharacterXp = 100 }; // 2 级
            Assert.That(PerkRules.TryUpgradePerk(meta, "yangyuan"), Is.True); // 解锁到 1 级,扣 200
            Assert.That(PerkRules.PerkLevel(meta, "yangyuan"), Is.EqualTo(1));
            Assert.That(meta.Ink, Is.EqualTo(100));
        }

        [Test]
        public void Upgrade_RejectedAtMaxLevel()
        {
            var meta = new MetaState { Ink = 99999, CharacterXp = 100 };
            meta.PerkLevels["yiqi"] = 2; // 一气已满(上限 2)
            Assert.That(PerkRules.TryUpgradePerk(meta, "yiqi"), Is.False);
            Assert.That(meta.Ink, Is.EqualTo(99999));
        }

        // StartingLibrary_GrowsWithBowen 已删除(2026-09-06,Task 2):它调用的
        // MetaRules.StartingLibrary(meta) 单参重载已被三参重载取代,其规则(博闻每级 +1 格)
        // 由 MetaTests.StartingLibrary_BowenPerkAppendsExtraDraws 等价覆盖。

        /// <summary>五系齐全是这条断言成立的前提:StartingLibrary 前 5 张按元素各抽一张,
        /// 元素不全就凑不满 6 张,「容量 − 起手数量恒为 1」这条断言就会假摔。
        /// 池子沿用旧测试的 {火,木,水,金,土,心,林,炎}(五系本就齐全,详情见对应元素),
        /// 但要挂进一张 <see cref="RecipeGraph"/> ——旧的单参 StartingLibrary 不需要图,
        /// 三参版本需要用图查元素/稀有度,得给这 8 个字配上配方(否则被判成部件滤掉)。</summary>
        private static RecipeGraph BowenCapacityGraph() => new(new[]
        {
            new CharDef("丶", null, new string[0], isComponent: true),
            new CharDef("丿", null, new string[0], isComponent: true),
            new CharDef("火", Element.Fire, new[] { "丶", "丿" }),
            new CharDef("木", Element.Wood, new[] { "丶", "丿" }),
            new CharDef("水", Element.Water, new[] { "丶", "丿" }),
            new CharDef("金", Element.Metal, new[] { "丶", "丿" }),
            new CharDef("土", Element.Earth, new[] { "丶", "丿" }),
            new CharDef("心", Element.Heart, new[] { "丶", "丿" }),
            new CharDef("林", Element.Wood, new[] { "丶", "丿" }),
            new CharDef("炎", Element.Fire, new[] { "丶", "丿" }),
        });

        [Test]
        public void LibraryCapacity_StaysOneAboveStarting_WithBowen() // 容量比起手多一格(2026-08-04);差值不被博闻吃掉
        {
            var graph = BowenCapacityGraph();
            var meta = new MetaState();
            foreach (var c in new[] { "火", "木", "水", "金", "土", "心", "林", "炎" })
                meta.OwnedCards.Add(c);
            var random = new GameRandom(11);
            Assert.That(MetaRules.LibraryCapacityFor(meta) - MetaRules.StartingLibrary(meta, graph, random).Count,
                Is.EqualTo(1));
            meta.PerkLevels["bowen"] = 1;
            Assert.That(MetaRules.LibraryCapacityFor(meta) - MetaRules.StartingLibrary(meta, graph, random).Count,
                Is.EqualTo(1));
        }
    }
}
