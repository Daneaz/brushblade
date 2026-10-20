using System;
using System.Linq;
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
            Assert.That(PerkRules.CanUnlock(meta, "metal_1_s1"), Is.True, "夹具自检:第一层无前置");
            Assert.That(PerkRules.CanUnlock(meta, "metal_2_s1"), Is.False);
        }

        [Test]
        public void CanUnlock_TrueOncePrerequisiteOwned()
        {
            var meta = new MetaState { CharacterXp = 100000, Ink = 100000 };
            Assert.That(PerkRules.TryUnlock(meta, "metal_1_s1"), Is.True);
            Assert.That(PerkRules.CanUnlock(meta, "metal_2_s1"), Is.True);
        }

        /// <summary>前置只看**同一枝**,不跨枝 —— 点了金脉不该开木脉第二层。</summary>
        [Test]
        public void CanUnlock_PrerequisiteIsPerBranch()
        {
            var meta = new MetaState { CharacterXp = 100000, Ink = 100000 };
            PerkRules.TryUnlock(meta, "metal_1_s1");
            Assert.That(PerkRules.CanUnlock(meta, "wood_2_s1"), Is.False);
        }

        // ---- 门槛 ----

        [Test]
        public void CanUnlock_FalseWhenCharacterLevelTooLow()
        {
            var meta = new MetaState { CharacterXp = 0, Ink = 100000 }; // Lv.1
            Assert.That(PerkRules.CanUnlock(meta, "metal_1_s1"), Is.False, "金脉 L1 需 Lv.4");
            Assert.That(PerkRules.CanUnlock(meta, "vigor_1_s1"), Is.False, "元 L1 需 Lv.2");
        }

        /// <summary>门槛表本身:步长 6、三树错开 2 级(全开层见 AllNodes_UnlockByLevel24)。
        /// 写死在测试里是刻意的 —— 曲线一改这条就该红,让人重新想一遍节奏。</summary>
        [Test]
        public void UnlockLevels_FollowTheStaggeredSchedule()
        {
            Assert.That(PerkRules.Get("vigor_1_s1").UnlockLevel, Is.EqualTo(2));
            Assert.That(PerkRules.Get("metal_1_s1").UnlockLevel, Is.EqualTo(4));
            Assert.That(PerkRules.Get("lore_1").UnlockLevel, Is.EqualTo(6));
            Assert.That(PerkRules.Get("vigor_1_s2").UnlockLevel, Is.EqualTo(8));
            Assert.That(PerkRules.Get("metal_2_s1").UnlockLevel, Is.EqualTo(10));
            Assert.That(PerkRules.Get("lore_2").UnlockLevel, Is.EqualTo(12));
            Assert.That(PerkRules.Get("vigor_1_s3").UnlockLevel, Is.EqualTo(14));
            Assert.That(PerkRules.Get("metal_3").UnlockLevel, Is.EqualTo(16));
            Assert.That(PerkRules.Get("metal_4").UnlockLevel, Is.EqualTo(22));
        }

        [Test]
        public void AllNodes_UnlockByLevel24()
        {
            foreach (var def in PerkRules.Nodes)
                Assert.That(def.UnlockLevel, Is.LessThanOrEqualTo(24),
                    $"{def.Id} 的门槛超过 Lv.24,全开层就不是 24 了(2026-10-18 由 22 放宽)");
        }

        // ---- 墨锭 ----

        [Test]
        public void CanUnlock_FalseWhenInkInsufficient()
        {
            var meta = new MetaState { CharacterXp = 100000, Ink = 99 };
            Assert.That(PerkRules.CanUnlock(meta, "metal_1_s1"), Is.False, "金脉 L1 第一段要 100");
        }

        [Test]
        public void TryUnlock_SpendsInkAndRecordsNode()
        {
            var meta = new MetaState { CharacterXp = 100000, Ink = 1000 };
            Assert.That(PerkRules.TryUnlock(meta, "metal_1_s1"), Is.True);
            Assert.That(meta.Ink, Is.EqualTo(900));
            Assert.That(meta.UnlockedPerks.Contains("metal_1_s1"), Is.True);
        }

        [Test]
        public void TryUnlock_FalseWhenAlreadyOwned()
        {
            var meta = new MetaState { CharacterXp = 100000, Ink = 100000 };
            Assert.That(PerkRules.TryUnlock(meta, "metal_1_s1"), Is.True);
            int inkAfterFirst = meta.Ink;
            Assert.That(PerkRules.TryUnlock(meta, "metal_1_s1"), Is.False, "重复点不该再扣钱");
            Assert.That(meta.Ink, Is.EqualTo(inkAfterFirst));
        }

        [Test]
        public void TotalCost_MatchesTheSpecBudget()
        {
            int total = 0;
            foreach (var def in PerkRules.Nodes) total += def.InkCost;
            // 2026-10-02:五行 L1/L2 三段式(600 + 1200)+ L3 1200 + L4 2400 = 5400/枝;
            // 被动 2100/枝;机制四枝统一 800/1600(一气特价随 AP 取消);跨树 3000。
            // 2026-10-18 终极技:融会(900)删除、广纳枝回到 2 层;斩获终极技两级 900 + 1800。
            Assert.That(total, Is.EqualTo(48000 - 900 + 900 + 1800), "全树总价(spec 2026-10-02 + 2026-10-18 终极技 §1)");
        }

        // ---- 表的形状 ----

        [Test]
        public void Nodes_AreSixtyThreeStagesAcrossFourCategories()
        {
            // 2026-10-02:五行 L1/L2 改三段,每枝 3 + 3 + 1 + 1 = 8 段;面(NodeKey)仍是 43 个
            Assert.That(PerkRules.Nodes.Count, Is.EqualTo(64), "2026-10-18 终极技:−斩获(wide_3) −融会 +终极技两级");
            int wuxing = 0, passive = 0, mechanic = 0, cross = 0;
            foreach (var def in PerkRules.Nodes)
            {
                if (def.Tree == PerkTree.Wuxing) wuxing++;
                else if (def.Tree == PerkTree.Passive) passive++;
                else if (def.Tree == PerkTree.Mechanic) mechanic++;
                else cross++;
            }
            Assert.That(wuxing, Is.EqualTo(40));
            Assert.That(passive, Is.EqualTo(12));
            Assert.That(mechanic, Is.EqualTo(8));
            Assert.That(cross, Is.EqualTo(4), "跨树相济/博采 + 斩获终极技两级,都不属于任何一棵树(spec 2026-09-08 §3.0)");
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
            // 数**面**(Faces,每个 NodeKey 一个)而不是段:三段节点三段同 Depth
            var byBranch = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<int>>();
            foreach (var def in PerkRules.Faces)
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
            Assert.That(PerkRules.Bonus(meta, PerkEffect.AttackPercent), Is.EqualTo(0));
        }

        [Test]
        public void Bonus_SumsTheOwnedNonPassiveNodesOfThatEffect()
        {
            // 被动树 2026-10-02 起不叠加(见 PassiveTree_DoesNotStack_*);机制树照旧求和
            var meta = new MetaState();
            meta.UnlockedPerks.Add("lore_1"); // +1
            meta.UnlockedPerks.Add("lore_2"); // +1
            Assert.That(PerkRules.Bonus(meta, PerkEffect.LibraryCapacity), Is.EqualTo(2));
        }

        [Test]
        public void ElementBonus_DoesNotLeakAcrossElements()
        {
            var meta = new MetaState();
            meta.UnlockedPerks.Add("metal_2_s1"); // 金系字效果 +5%
            meta.UnlockedPerks.Add("metal_2_s2"); // +5%
            meta.UnlockedPerks.Add("metal_2_s3"); // +5%(合计 15%)
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
            Assert.That(PerkRules.ElementBonus(meta, PerkEffect.MoraleOnCrit, Element.Metal),
                Is.EqualTo(0), "未知 id 必须跳过,不能抛");
            Assert.That(PerkRules.Bonus(meta, PerkEffect.MaxHp), Is.EqualTo(0));
        }

        // ---- 三段式节点(spec 2026-10-02 §1)----

        [Test]
        public void WuxingTierOneAndTwo_AreThreeStageNodes()
        {
            foreach (var branch in new[] { "metal", "wood", "water", "fire", "earth" })
            {
                for (int depth = 1; depth <= 2; depth++)
                {
                    var stages = PerkRules.StagesOf($"{branch}_{depth}");
                    Assert.That(stages.Count, Is.EqualTo(3), $"{branch}_{depth}");
                    for (int s = 0; s < 3; s++)
                    {
                        Assert.That(stages[s].Id, Is.EqualTo($"{branch}_{depth}_s{s + 1}"));
                        Assert.That(stages[s].Stage, Is.EqualTo(s + 1));
                        Assert.That(stages[s].StageCount, Is.EqualTo(3));
                        Assert.That(stages[s].NodeKey, Is.EqualTo($"{branch}_{depth}"));
                    }
                }
                Assert.That(PerkRules.StagesOf($"{branch}_3").Count, Is.EqualTo(1));
                Assert.That(PerkRules.Get($"{branch}_3").NodeKey, Is.EqualTo($"{branch}_3"));
            }
        }

        [Test]
        public void StagedGatesAndCosts_FollowTheSpec()
        {
            int[] l1Lv = { 4, 6, 8 }, l1Ink = { 100, 200, 300 };
            int[] l2Lv = { 10, 12, 14 }, l2Ink = { 200, 400, 600 };
            for (int s = 0; s < 3; s++)
            {
                Assert.That(PerkRules.Get($"metal_1_s{s + 1}").UnlockLevel, Is.EqualTo(l1Lv[s]));
                Assert.That(PerkRules.Get($"metal_1_s{s + 1}").InkCost, Is.EqualTo(l1Ink[s]));
                Assert.That(PerkRules.Get($"metal_2_s{s + 1}").UnlockLevel, Is.EqualTo(l2Lv[s]));
                Assert.That(PerkRules.Get($"metal_2_s{s + 1}").InkCost, Is.EqualTo(l2Ink[s]));
            }
            Assert.That(PerkRules.Get("metal_3").UnlockLevel, Is.EqualTo(16));
            Assert.That(PerkRules.Get("metal_3").InkCost, Is.EqualTo(1200));
            Assert.That(PerkRules.Get("metal_4").UnlockLevel, Is.EqualTo(22));
            Assert.That(PerkRules.Get("metal_4").InkCost, Is.EqualTo(2400));
        }

        [Test]
        public void Stage_RequiresPreviousStageOfSameNode()
        {
            var meta = new MetaState { CharacterXp = 100000, Ink = 100000 };
            Assert.That(PerkRules.CanUnlock(meta, "metal_1_s2"), Is.False, "没点 s1 不能跳到 s2");
            Assert.That(PerkRules.TryUnlock(meta, "metal_1_s1"), Is.True);
            Assert.That(PerkRules.CanUnlock(meta, "metal_1_s2"), Is.True);
            Assert.That(PerkRules.CanUnlock(meta, "metal_1_s3"), Is.False, "s3 要 s2");
        }

        [Test]
        public void NextTier_RequiresAnyStageOfPreviousTier()
        {
            var meta = new MetaState { CharacterXp = 100000, Ink = 100000 };
            Assert.That(PerkRules.CanUnlock(meta, "metal_2_s1"), Is.False, "L1 一段都没点");
            PerkRules.TryUnlock(meta, "metal_1_s1");
            Assert.That(PerkRules.CanUnlock(meta, "metal_2_s1"), Is.True, "L1 ≥1 段即可");
            Assert.That(PerkRules.CanUnlock(meta, "metal_3"), Is.False, "L2 一段都没点");
            PerkRules.TryUnlock(meta, "metal_2_s1");
            Assert.That(PerkRules.CanUnlock(meta, "metal_3"), Is.True);
            Assert.That(PerkRules.CanUnlock(meta, "wood_2_s1"), Is.False, "前置只看同枝");
        }

        [Test]
        public void StagedValues_AccumulateToTheFinalValue()
        {
            var meta = new MetaState();
            meta.UnlockedPerks.Add("fire_1_s1");
            meta.UnlockedPerks.Add("fire_1_s2");
            Assert.That(PerkRules.ElementBonus(meta, PerkEffect.EnemyBurnBonus, Element.Fire), Is.EqualTo(6));
            meta.UnlockedPerks.Add("fire_1_s3");
            Assert.That(PerkRules.ElementBonus(meta, PerkEffect.EnemyBurnBonus, Element.Fire), Is.EqualTo(10));
            Assert.That(PerkRules.CumulativeValue(PerkRules.Get("fire_1_s2")), Is.EqualTo(6));
            Assert.That(PerkRules.CumulativeValue(PerkRules.Get("metal_2_s3")), Is.EqualTo(15));
        }

        [Test]
        public void CurrentStage_IsFirstUnownedThenLast()
        {
            var meta = new MetaState();
            Assert.That(PerkRules.CurrentStage(meta, "earth_1").Id, Is.EqualTo("earth_1_s1"));
            meta.UnlockedPerks.Add("earth_1_s1");
            Assert.That(PerkRules.CurrentStage(meta, "earth_1").Id, Is.EqualTo("earth_1_s2"));
            Assert.That(PerkRules.OwnedStageCount(meta, "earth_1"), Is.EqualTo(1));
            meta.UnlockedPerks.Add("earth_1_s2");
            meta.UnlockedPerks.Add("earth_1_s3");
            Assert.That(PerkRules.CurrentStage(meta, "earth_1").Id, Is.EqualTo("earth_1_s3"));
            Assert.That(PerkRules.CurrentStage(meta, "metal_4").Id, Is.EqualTo("metal_4"));
        }

        [Test]
        public void Faces_HaveOneEntryPerNodeKey()
        {
            var keys = new System.Collections.Generic.HashSet<string>();
            foreach (var face in PerkRules.Faces)
            {
                Assert.That(face.Stage, Is.EqualTo(1));
                Assert.That(keys.Add(face.NodeKey), Is.True, $"重复 {face.NodeKey}");
            }
            // 五行 5×4 + 被动 4×1 + 机制 4×2 + 跨树 3(相济、博采、斩获;斩获两段只占一个节点,2026-10-19;
            // 被动四枝 2026-10-20 各合为一个三段节点)
            Assert.That(PerkRules.Faces.Count, Is.EqualTo(35));
        }

        // ---- 被动树:每枝一个三段节点(2026-10-20 用户:参照「深根」),段值是增量、累计 = 原各档 ----

        [Test]
        public void PassiveBranches_AreOneThreeStageNodeEach()
        {
            foreach (var branch in new[] { "vigor", "power", "edge", "guard" })
            {
                var stages = PerkRules.StagesOf($"{branch}_1");
                Assert.That(stages.Count, Is.EqualTo(3), branch);
                for (int k = 0; k < 3; k++)
                {
                    Assert.That(stages[k].Id, Is.EqualTo($"{branch}_1_s{k + 1}"), branch);
                    Assert.That(stages[k].Depth, Is.EqualTo(1), branch);
                    Assert.That(stages[k].StageCount, Is.EqualTo(3), branch);
                }
            }
        }

        [Test]
        public void PassiveStage_NeedsThePreviousStage()
        {
            var meta = new MetaState { CharacterXp = 100000, Ink = 100000 };
            Assert.That(PerkRules.CanUnlock(meta, "vigor_1_s2"), Is.False, "第 2 段要第 1 段");
            meta.UnlockedPerks.Add("vigor_1_s1");
            Assert.That(PerkRules.CanUnlock(meta, "vigor_1_s2"), Is.True);
        }

        [Test]
        public void PassiveStages_AccumulateToTheOldTopTier()
        {
            var meta = new MetaState();
            meta.UnlockedPerks.Add("vigor_1_s1");
            meta.UnlockedPerks.Add("vigor_1_s2");
            Assert.That(PerkRules.Bonus(meta, PerkEffect.MaxHp), Is.EqualTo(500), "300 + 200");
            meta.UnlockedPerks.Add("vigor_1_s3");
            Assert.That(PerkRules.Bonus(meta, PerkEffect.MaxHp), Is.EqualTo(1000));
            meta.UnlockedPerks.Add("power_1_s1");
            meta.UnlockedPerks.Add("power_1_s2");
            meta.UnlockedPerks.Add("power_1_s3");
            Assert.That(PerkRules.Bonus(meta, PerkEffect.AttackPercent), Is.EqualTo(15));
            meta.UnlockedPerks.Add("guard_1_s1");
            meta.UnlockedPerks.Add("guard_1_s2");
            Assert.That(PerkRules.Bonus(meta, PerkEffect.Defense), Is.EqualTo(15));
        }

        [Test]
        public void PassiveMax_StillAddsCrossNodeOnTop()
        {
            // 相济(cross_vigor)给攻击,不属于被动树 → 与力枝累计值相加
            var meta = new MetaState();
            meta.UnlockedPerks.Add("power_1_s1");
            meta.UnlockedPerks.Add("power_1_s2");
            meta.UnlockedPerks.Add("metal_3");          // 相济按五行深层节点数缩放:1 个
            meta.UnlockedPerks.Add("cross_vigor");      // 8 + 3×1 = 11
            Assert.That(PerkRules.Bonus(meta, PerkEffect.AttackPercent), Is.EqualTo(10 + 11));
        }

        [Test]
        public void TierTable_MatchesTheSpec()
        {
            (string id, PerkEffect effect, int value)[] rows =
            {
                ("metal_1_s1", PerkEffect.ElementCritChance, 5),
                ("wood_1_s1", PerkEffect.SummonHpPercent, 10),
                ("water_1_s1", PerkEffect.HealPercent, 10),
                ("fire_1_s3", PerkEffect.EnemyBurnBonus, 4),
                ("earth_1_s1", PerkEffect.ShieldPercent, 10),
                ("metal_2_s1", PerkEffect.ElementEffectPercent, 5),
                ("metal_3", PerkEffect.MoraleOnCrit, 1),
                ("wood_3", PerkEffect.SummonDeathHealPercent, 30),
                ("water_3", PerkEffect.OverhealDamagePercent, 50),
                ("fire_3", PerkEffect.BurnSpreadPercent, 100),
                ("earth_3", PerkEffect.ShieldCarryPercent, 25),
                ("metal_4", PerkEffect.MoraleRelease, 300),
                ("wood_4", PerkEffect.CounterTargeting, 1),
                ("water_4", PerkEffect.WellspringSpendPercent, 150),
                ("fire_4", PerkEffect.BurnSpreadAdjacent, 1),
                ("earth_4", PerkEffect.HeftSpendPercent, 150),
                // 被动三段存本段增量:累计 300/500/1000、5/10/15、10/15/25
                ("vigor_1_s1", PerkEffect.MaxHp, 300), ("vigor_1_s2", PerkEffect.MaxHp, 200), ("vigor_1_s3", PerkEffect.MaxHp, 500),
                ("power_1_s3", PerkEffect.AttackPercent, 5), ("edge_1_s3", PerkEffect.CritChance, 5),
                ("guard_1_s2", PerkEffect.Defense, 5), ("guard_1_s3", PerkEffect.Defense, 10),
                ("wide_1", PerkEffect.EmptyLibraryDraws, 1), ("wide_2", PerkEffect.LowLibraryDraws, 1),
                ("ult_killdraw_s1", PerkEffect.KillDraw, 1), ("ult_killdraw_s2", PerkEffect.KillDraw, 2),
                ("insight_1", PerkEffect.RewardOptions, 1), ("insight_2", PerkEffect.RewardRerolls, 1),
                ("qi_1", PerkEffect.VictoryHealPercent, 5), ("qi_2", PerkEffect.PreBattleHealPercent, 5),
            };
            foreach (var (id, effect, value) in rows)
            {
                Assert.That(PerkRules.Get(id).Effect, Is.EqualTo(effect), id);
                Assert.That(PerkRules.Get(id).Value, Is.EqualTo(value), id);
            }
            Assert.That(PerkRules.Get("qi_1").InkCost, Is.EqualTo(800));
            Assert.That(PerkRules.Get("qi_2").InkCost, Is.EqualTo(1600));
        }

        /// <summary>2026-10-18 终极技 §1:斩获移出广纳枝,广纳枝回到 2 层(广纳 → 集腋)。</summary>
        [Test]
        public void WideBranch_HasTwoLayers()
        {
            Assert.That((int)PerkEffect.LowLibraryDraws, Is.EqualTo((int)PerkEffect.KillDraw + 1), "枚举只在末尾追加");
            Assert.That((int)PerkEffect.PreBattleHealPercent, Is.EqualTo((int)PerkEffect.LowLibraryDraws + 1), "吐纳 2026-10-20 追加");
            Assert.That(Enum.GetValues(typeof(PerkEffect)).Cast<int>().Max(), Is.EqualTo((int)PerkEffect.PreBattleHealPercent));
            Assert.That(PerkRules.StagesOf("wide_3").Count, Is.EqualTo(0), "wide_3 已删");
            var w2 = PerkRules.Get("wide_2");
            Assert.That((w2.Depth, w2.UnlockLevel, w2.InkCost), Is.EqualTo((2, 12, 1600)));
            foreach (var def in PerkRules.Nodes)
                if (def.Tree == PerkTree.Mechanic)
                    Assert.That(def.Depth, Is.LessThanOrEqualTo(2), $"{def.Id}:机制树每枝都是 2 层");
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
            Assert.That(PerkRules.CanUnlock(meta, "metal_1_s1"), Is.True, "夹具自检");
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
