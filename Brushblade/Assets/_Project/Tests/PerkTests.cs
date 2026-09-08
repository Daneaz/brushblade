using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    public class PerkTests
    {
        /// <summary>够等级、够墨锭,但同枝上一层没点 —— 前置是硬门。</summary>
        [Test]
        public void CanUnlock_FalseWhenPrerequisiteNotOwned()
        {
            var meta = new MetaState { CharacterXp = 100000, Ink = 100000 };
            Assert.That(PerkRules.CanUnlock(meta, "metal_1"), Is.True, "夹具自检:第一层无前置");
            Assert.That(PerkRules.CanUnlock(meta, "metal_2"), Is.False);
        }

        [Test]
        public void CanUnlock_TrueOncePrerequisiteOwned()
        {
            var meta = new MetaState { CharacterXp = 100000, Ink = 100000 };
            Assert.That(PerkRules.TryUnlock(meta, "metal_1"), Is.True);
            Assert.That(PerkRules.CanUnlock(meta, "metal_2"), Is.True);
        }

        /// <summary>前置只看**同一枝**,不跨枝 —— 点了金脉不该开木脉第二层。</summary>
        [Test]
        public void CanUnlock_PrerequisiteIsPerBranch()
        {
            var meta = new MetaState { CharacterXp = 100000, Ink = 100000 };
            PerkRules.TryUnlock(meta, "metal_1");
            Assert.That(PerkRules.CanUnlock(meta, "wood_2"), Is.False);
        }

        // ---- 门槛 ----

        [Test]
        public void CanUnlock_FalseWhenCharacterLevelTooLow()
        {
            var meta = new MetaState { CharacterXp = 0, Ink = 100000 }; // Lv.1
            Assert.That(PerkRules.CanUnlock(meta, "metal_1"), Is.False, "金脉 L1 需 Lv.4");
            Assert.That(PerkRules.CanUnlock(meta, "vigor_1"), Is.False, "元 L1 需 Lv.2");
        }

        /// <summary>门槛表本身:步长 6、三树错开 2 级、Lv22 全开。
        /// 写死在测试里是刻意的 —— 曲线一改这条就该红,让人重新想一遍节奏。</summary>
        [Test]
        public void UnlockLevels_FollowTheStaggeredSchedule()
        {
            Assert.That(PerkRules.Get("vigor_1").UnlockLevel, Is.EqualTo(2));
            Assert.That(PerkRules.Get("metal_1").UnlockLevel, Is.EqualTo(4));
            Assert.That(PerkRules.Get("lore_1").UnlockLevel, Is.EqualTo(6));
            Assert.That(PerkRules.Get("vigor_2").UnlockLevel, Is.EqualTo(8));
            Assert.That(PerkRules.Get("metal_2").UnlockLevel, Is.EqualTo(10));
            Assert.That(PerkRules.Get("lore_2").UnlockLevel, Is.EqualTo(12));
            Assert.That(PerkRules.Get("vigor_3").UnlockLevel, Is.EqualTo(14));
            Assert.That(PerkRules.Get("metal_3").UnlockLevel, Is.EqualTo(16));
            Assert.That(PerkRules.Get("metal_4").UnlockLevel, Is.EqualTo(22));
        }

        [Test]
        public void AllNodes_UnlockByLevel22()
        {
            foreach (var def in PerkRules.Nodes)
                Assert.That(def.UnlockLevel, Is.LessThanOrEqualTo(22),
                    $"{def.Id} 的门槛超过 Lv.22,全开层就不是 22 了");
        }

        // ---- 墨锭 ----

        [Test]
        public void CanUnlock_FalseWhenInkInsufficient()
        {
            var meta = new MetaState { CharacterXp = 100000, Ink = 299 };
            Assert.That(PerkRules.CanUnlock(meta, "metal_1"), Is.False, "金脉 L1 要 300");
        }

        [Test]
        public void TryUnlock_SpendsInkAndRecordsNode()
        {
            var meta = new MetaState { CharacterXp = 100000, Ink = 1000 };
            Assert.That(PerkRules.TryUnlock(meta, "metal_1"), Is.True);
            Assert.That(meta.Ink, Is.EqualTo(700));
            Assert.That(meta.UnlockedPerks.Contains("metal_1"), Is.True);
        }

        [Test]
        public void TryUnlock_FalseWhenAlreadyOwned()
        {
            var meta = new MetaState { CharacterXp = 100000, Ink = 100000 };
            Assert.That(PerkRules.TryUnlock(meta, "metal_1"), Is.True);
            int inkAfterFirst = meta.Ink;
            Assert.That(PerkRules.TryUnlock(meta, "metal_1"), Is.False, "重复点不该再扣钱");
            Assert.That(meta.Ink, Is.EqualTo(inkAfterFirst));
        }

        [Test]
        public void TotalCost_MatchesTheSpecBudget()
        {
            int total = 0;
            foreach (var def in PerkRules.Nodes) total += def.InkCost;
            Assert.That(total, Is.EqualTo(46600), "全树总价(spec 2026-09-08 §6)");
        }

        // ---- 表的形状 ----

        [Test]
        public void Nodes_AreFortyThreeAcrossFourCategories()
        {
            Assert.That(PerkRules.Nodes.Count, Is.EqualTo(43));
            int wuxing = 0, passive = 0, mechanic = 0, cross = 0;
            foreach (var def in PerkRules.Nodes)
            {
                if (def.Tree == PerkTree.Wuxing) wuxing++;
                else if (def.Tree == PerkTree.Passive) passive++;
                else if (def.Tree == PerkTree.Mechanic) mechanic++;
                else cross++;
            }
            Assert.That(wuxing, Is.EqualTo(20));
            Assert.That(passive, Is.EqualTo(12));
            Assert.That(mechanic, Is.EqualTo(8));
            Assert.That(cross, Is.EqualTo(3), "跨树节点不属于任何一棵树(spec 2026-09-08 §3.0)");
        }

        /// <summary>五行树每一枝都要绑一个元素,其余两棵树都不许绑 ——
        /// ElementBonus 靠这个字段筛选,绑错会静默串系。</summary>
        [Test]
        public void OnlyWuxingNodes_CarryAnElement()
        {
            foreach (var def in PerkRules.Nodes)
                Assert.That(def.Element.HasValue, Is.EqualTo(def.Tree == PerkTree.Wuxing),
                    $"{def.Id} 的 Element 与所属树不一致");
        }

        [Test]
        public void EveryBranch_IsAContiguousChainFromDepthOne()
        {
            var byBranch = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<int>>();
            foreach (var def in PerkRules.Nodes)
            {
                if (!byBranch.TryGetValue(def.Branch, out var depths))
                    byBranch[def.Branch] = depths = new System.Collections.Generic.List<int>();
                depths.Add(def.Depth);
            }
            foreach (var pair in byBranch)
            {
                pair.Value.Sort();
                for (int i = 0; i < pair.Value.Count; i++)
                    Assert.That(pair.Value[i], Is.EqualTo(i + 1),
                        $"枝 {pair.Key} 的层数不连续,前置推导会断");
            }
        }

        // ---- 聚合 ----

        [Test]
        public void Bonus_IsZeroOnABrandNewSave()
        {
            var meta = new MetaState();
            Assert.That(PerkRules.Bonus(meta, PerkEffect.MaxHp), Is.EqualTo(0));
            Assert.That(PerkRules.Bonus(meta, PerkEffect.Ap), Is.EqualTo(0));
            Assert.That(PerkRules.Bonus(meta, PerkEffect.AttackPercent), Is.EqualTo(0));
        }

        [Test]
        public void Bonus_SumsTheOwnedNodesOfThatEffect()
        {
            var meta = new MetaState();
            meta.UnlockedPerks.Add("vigor_1"); // +100
            meta.UnlockedPerks.Add("vigor_2"); // +200
            Assert.That(PerkRules.Bonus(meta, PerkEffect.MaxHp), Is.EqualTo(300));
        }

        [Test]
        public void ElementBonus_DoesNotLeakAcrossElements()
        {
            var meta = new MetaState();
            meta.UnlockedPerks.Add("metal_3"); // 金系字效果 +15%
            Assert.That(PerkRules.ElementBonus(meta, PerkEffect.ElementEffectPercent, Element.Metal),
                Is.EqualTo(15));
            Assert.That(PerkRules.ElementBonus(meta, PerkEffect.ElementEffectPercent, Element.Wood),
                Is.EqualTo(0), "点金脉不该给木系加成");
        }

        [Test]
        public void ElementBonus_IgnoresUnknownIdsLeftInAnOldSave()
        {
            var meta = new MetaState();
            meta.UnlockedPerks.Add("a_node_that_no_longer_exists");
            Assert.That(PerkRules.ElementBonus(meta, PerkEffect.MoraleCap, Element.Metal),
                Is.EqualTo(0), "未知 id 必须跳过,不能抛");
            Assert.That(PerkRules.Bonus(meta, PerkEffect.MaxHp), Is.EqualTo(0));
        }

        // ---- 红点 ----

        [Test]
        public void HasUpgradable_FalseForBrandNewSave()
        {
            Assert.That(PerkRules.HasUpgradable(new MetaState()), Is.False);
        }

        [Test]
        public void HasUpgradable_TrueOnceOneNodeIsAffordable()
        {
            var meta = new MetaState { CharacterXp = 100000, Ink = 300 };
            Assert.That(PerkRules.CanUnlock(meta, "metal_1"), Is.True, "夹具自检");
            Assert.That(PerkRules.HasUpgradable(meta), Is.True);
        }

        [Test]
        public void HasUpgradable_FalseWhenEveryNodeIsOwned()
        {
            var meta = new MetaState { CharacterXp = 100000, Ink = 100000 };
            foreach (var def in PerkRules.Nodes) meta.UnlockedPerks.Add(def.Id);
            Assert.That(PerkRules.HasUpgradable(meta), Is.False);
        }
    }
}
