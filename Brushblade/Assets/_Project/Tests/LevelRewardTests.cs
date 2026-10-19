using System.Collections.Generic;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>每级宝箱(spec 2026-10-02 §2.1 / §5.1)。</summary>
    public class LevelRewardTests
    {
        private sealed class FakeTime : ITimeSource
        {
            public long NowUnixSeconds { get; set; } = 1_000_000;
        }

        private static readonly string[] Pool = { "x" };

        private static MetaState AtLevel(int level) => new() { CharacterXp = MetaRules.XpToReach(level) };

        [TestCase(2, ChestTier.Bamboo)]
        [TestCase(5, ChestTier.Bamboo)]
        [TestCase(6, ChestTier.Celadon)]
        [TestCase(10, ChestTier.Celadon)]
        [TestCase(11, ChestTier.Rosewood)]
        [TestCase(20, ChestTier.Rosewood)]
        [TestCase(21, ChestTier.Gilded)]
        [TestCase(30, ChestTier.Gilded)]
        [TestCase(31, ChestTier.Vermilion)]
        [TestCase(40, ChestTier.Vermilion)]
        [TestCase(41, ChestTier.Crimson)]
        [TestCase(99, ChestTier.Crimson)]
        public void TierForLevel_Bands(int level, ChestTier expected) =>
            Assert.That(LevelRewardRules.TierForLevel(level), Is.EqualTo(expected));

        /// <summary>升级弹窗的档位 chip「Lv.11–20」读这里,必须与 TierForLevel 同源。</summary>
        [TestCase(ChestTier.Bamboo, 1, 5)]
        [TestCase(ChestTier.Celadon, 6, 10)]
        [TestCase(ChestTier.Rosewood, 11, 20)]
        [TestCase(ChestTier.Gilded, 21, 30)]
        [TestCase(ChestTier.Vermilion, 31, 40)]
        [TestCase(ChestTier.Crimson, 41, int.MaxValue)]
        public void TryGetLevelBand_Bands(ChestTier tier, int min, int max)
        {
            Assert.That(LevelRewardRules.TryGetLevelBand(tier, out int gotMin, out int gotMax), Is.True);
            Assert.That(gotMin, Is.EqualTo(min));
            Assert.That(gotMax, Is.EqualTo(max));
        }

        [Test]
        public void TryGetLevelBand_PaperIsNeverALevelReward() =>
            Assert.That(LevelRewardRules.TryGetLevelBand(ChestTier.Paper, out _, out _), Is.False);

        [Test]
        public void TryGetLevelBand_AgreesWithTierForLevel()
        {
            for (int lv = 1; lv <= 200; lv++)
            {
                var tier = LevelRewardRules.TierForLevel(lv);
                Assert.That(LevelRewardRules.TryGetLevelBand(tier, out int min, out int max), Is.True, $"Lv.{lv}");
                Assert.That(lv >= min && lv <= max, Is.True, $"Lv.{lv} 不在 {tier} 的区间 {min}–{max}");
            }
        }

        [Test]
        public void XpToReach_MatchesCharacterLevel()
        {
            for (int lv = 1; lv <= 60; lv++)
            {
                Assert.That(MetaRules.CharacterLevel(MetaRules.XpToReach(lv)), Is.EqualTo(lv));
                if (lv > 1) Assert.That(MetaRules.CharacterLevel(MetaRules.XpToReach(lv) - 1), Is.EqualTo(lv - 1));
            }
        }

        private static void ClaimChests(MetaState meta, int from, int to)
        {
            for (int lv = from; lv <= to; lv++) meta.ClaimedLevelChests.Add(lv);
        }

        [Test]
        public void HasChest_StartsAtLevel2()
        {
            Assert.That(LevelRewardRules.HasChest(1), Is.False);
            Assert.That(LevelRewardRules.HasChest(2), Is.True);
        }

        [Test]
        public void PendingChestCount_CountsUnclaimedUpToLevel()
        {
            var meta = AtLevel(7);
            Assert.That(LevelRewardRules.PendingChestCount(meta), Is.EqualTo(6));
            meta.ClaimedLevelChests.Add(2);
            meta.ClaimedLevelChests.Add(5);
            Assert.That(LevelRewardRules.PendingChestCount(meta), Is.EqualTo(4));
        }

        [Test]
        public void PendingChestCount_IgnoresClaimsAboveLevel()
        {
            // 手改存档混入高于当前等级的已领值:不得少算、不得为负
            var meta = AtLevel(4);
            meta.ClaimedLevelChests.AddRange(new[] { 3, 9, 10 });
            Assert.That(LevelRewardRules.PendingChestCount(meta), Is.EqualTo(2));
        }

        [Test]
        public void TryClaimChest_AnyOrder_SlotsTierOfThatLevel()
        {
            var time = new FakeTime();
            var meta = AtLevel(12);
            Assert.That(LevelRewardRules.TryClaimChest(meta, 11, Pool, time), Is.True);
            Assert.That(meta.Chests[0].Tier, Is.EqualTo(ChestTier.Rosewood));
            Assert.That(LevelRewardRules.TryClaimChest(meta, 3, Pool, time), Is.True);
            Assert.That(meta.Chests[1].Tier, Is.EqualTo(ChestTier.Bamboo));
            Assert.That(meta.ClaimedLevelChests.Contains(11), Is.True);
            Assert.That(meta.ClaimedLevelChests.Contains(3), Is.True);
            Assert.That(LevelRewardRules.PendingChestCount(meta), Is.EqualTo(9));
        }

        [Test]
        public void TryClaimChest_SlotsFull_ChangesNothing()
        {
            // 每级宝箱从不占暂存位:暂存位留给爬塔结算箱(终审 C1)
            var time = new FakeTime();
            var meta = AtLevel(5);
            for (int i = 0; i < ChestRules.SlotLimit; i++)
                ChestRules.TryAwardChest(meta, ChestTier.Bamboo, Pool, time);

            Assert.That(LevelRewardRules.TryClaimChest(meta, 3, Pool, time), Is.False);
            Assert.That(meta.Chests.Count, Is.EqualTo(ChestRules.SlotLimit));
            Assert.That(meta.PendingChests.Count, Is.EqualTo(0), "每级宝箱不许占暂存位");
            Assert.That(meta.ClaimedLevelChests.Count, Is.EqualTo(0));
            Assert.That(ChestRules.AwardOrHold(meta, ChestTier.Gilded, Pool, time), Is.EqualTo(ChestAward.Held),
                "结算箱仍能拿到暂存位");
        }

        [Test]
        public void TryClaimChest_Twice_SecondFails()
        {
            var time = new FakeTime();
            var meta = AtLevel(3);
            Assert.That(LevelRewardRules.TryClaimChest(meta, 2, Pool, time), Is.True);
            Assert.That(LevelRewardRules.TryClaimChest(meta, 2, Pool, time), Is.False);
            Assert.That(meta.Chests.Count, Is.EqualTo(1));
        }

        [Test]
        public void TryClaimChest_NotReachedOrNoChest_Fails()
        {
            var time = new FakeTime();
            var meta = AtLevel(5);
            Assert.That(LevelRewardRules.TryClaimChest(meta, 6, Pool, time), Is.False);
            Assert.That(LevelRewardRules.TryClaimChest(meta, 1, Pool, time), Is.False);
            Assert.That(LevelRewardRules.TryClaimChest(meta, 0, Pool, time), Is.False);
            Assert.That(meta.Chests.Count, Is.EqualTo(0));
            Assert.That(meta.ClaimedLevelChests.Count, Is.EqualTo(0));
        }

        [Test]
        public void HasClaimableReward_ChestOrMilestone()
        {
            Assert.That(LevelRewardRules.HasClaimableReward(AtLevel(1)), Is.False);
            Assert.That(LevelRewardRules.HasClaimableReward(AtLevel(3)), Is.True);

            var meta = AtLevel(5);
            ClaimChests(meta, 2, 5);
            Assert.That(LevelRewardRules.HasClaimableReward(meta), Is.True, "里程碑 5 未领");
            meta.ClaimedMilestones.Add(5);
            Assert.That(LevelRewardRules.HasClaimableReward(meta), Is.False);
        }

        [TestCase(1, 6)]
        [TestCase(3, 8)]
        [TestCase(9, 14)]
        [TestCase(48, 53)]
        [TestCase(50, 60)]
        [TestCase(57, 62)]
        public void TrackEnd_Cases(int level, int expected) =>
            Assert.That(LevelRewardRules.TrackEnd(AtLevel(level)), Is.EqualTo(expected));

        [Test]
        public void FocusLevel_FirstPendingElseCurrent()
        {
            var meta = AtLevel(8);
            Assert.That(LevelRewardRules.FocusLevel(meta), Is.EqualTo(2));
            ClaimChests(meta, 2, 8);
            Assert.That(LevelRewardRules.FocusLevel(meta), Is.EqualTo(5));
            meta.ClaimedMilestones.Add(5);
            Assert.That(LevelRewardRules.FocusLevel(meta), Is.EqualTo(8));
        }

        [Test]
        public void FocusLevel_EitherRewardPending()
        {
            var a = AtLevel(10); // 宝箱全领,里程碑 10 未领
            ClaimChests(a, 2, 10);
            a.ClaimedMilestones.Add(5);
            Assert.That(LevelRewardRules.FocusLevel(a), Is.EqualTo(10));

            var b = AtLevel(10); // 里程碑全领,宝箱 10 未领
            ClaimChests(b, 2, 9);
            b.ClaimedMilestones.AddRange(new[] { 5, 10 });
            Assert.That(LevelRewardRules.FocusLevel(b), Is.EqualTo(10));

            var c = AtLevel(10); // 里程碑全领,宝箱只剩 6
            ClaimChests(c, 2, 10);
            c.ClaimedLevelChests.Remove(6);
            c.ClaimedMilestones.AddRange(new[] { 5, 10 });
            Assert.That(LevelRewardRules.FocusLevel(c), Is.EqualTo(6));
        }

        [Test]
        public void Summary_FirstRun_SeedsSilently()
        {
            var meta = AtLevel(20); // LastSeenLevel 缺省 0
            Assert.That(LevelRewardRules.TakeLevelUpSummary(meta), Is.Null);
            Assert.That(meta.LastSeenLevel, Is.EqualTo(20));
        }

        [Test]
        public void SeedLastSeenLevel_FirstRun_SetsCurrentLevel()
        {
            var meta = AtLevel(12);
            LevelRewardRules.SeedLastSeenLevel(meta);
            Assert.That(meta.LastSeenLevel, Is.EqualTo(12));
        }

        [Test]
        public void SeedLastSeenLevel_AlreadySeeded_KeepsUnshownLevelUp()
        {
            // 升级后弹窗还没弹就被杀进程:重启不能把这次升级静默吞掉(终审 I2)
            var meta = AtLevel(9);
            meta.LastSeenLevel = 7;
            LevelRewardRules.SeedLastSeenLevel(meta);
            Assert.That(meta.LastSeenLevel, Is.EqualTo(7));
            var summary = LevelRewardRules.TakeLevelUpSummary(meta);
            Assert.That(summary.FromLevel, Is.EqualTo(7));
            Assert.That(summary.ToLevel, Is.EqualTo(9));
        }

        [Test]
        public void Summary_ReportsRangeAndCrossedMilestones_ThenEmpty()
        {
            var meta = AtLevel(19);
            meta.LastSeenLevel = 19;
            meta.CharacterXp = MetaRules.XpToReach(26);

            var summary = LevelRewardRules.TakeLevelUpSummary(meta);

            Assert.That(summary.FromLevel, Is.EqualTo(19));
            Assert.That(summary.ToLevel, Is.EqualTo(26));
            Assert.That(summary.Milestones, Is.EqualTo(new List<int> { 20, 25 }));
            Assert.That(LevelRewardRules.TakeLevelUpSummary(meta), Is.Null);
        }
    }
}
