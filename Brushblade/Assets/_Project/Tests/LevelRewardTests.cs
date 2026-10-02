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

        [Test]
        public void XpToReach_MatchesCharacterLevel()
        {
            for (int lv = 1; lv <= 60; lv++)
            {
                Assert.That(MetaRules.CharacterLevel(MetaRules.XpToReach(lv)), Is.EqualTo(lv));
                if (lv > 1) Assert.That(MetaRules.CharacterLevel(MetaRules.XpToReach(lv) - 1), Is.EqualTo(lv - 1));
            }
        }

        [Test]
        public void Grant_OneLevel_OneChest()
        {
            var meta = AtLevel(2);
            var grants = LevelRewardRules.GrantLevelChests(meta, Pool, new FakeTime());
            Assert.That(grants.Count, Is.EqualTo(1));
            Assert.That(grants[0].Level, Is.EqualTo(2));
            Assert.That(grants[0].Tier, Is.EqualTo(ChestTier.Bamboo));
            Assert.That(meta.Chests.Count, Is.EqualTo(1));
            Assert.That(meta.LevelRewardGranted, Is.EqualTo(2));
        }

        [Test]
        public void Grant_MultiLevel_EachUsesOwnLevelTier()
        {
            var meta = AtLevel(1);
            meta.LevelRewardGranted = 9;
            meta.CharacterXp = MetaRules.XpToReach(11);
            var grants = LevelRewardRules.GrantLevelChests(meta, Pool, new FakeTime());
            Assert.That(grants.Count, Is.EqualTo(2));
            Assert.That(grants[0].Tier, Is.EqualTo(ChestTier.Celadon));  // Lv.10
            Assert.That(grants[1].Tier, Is.EqualTo(ChestTier.Rosewood)); // Lv.11
        }

        [Test]
        public void Grant_Idempotent()
        {
            var meta = AtLevel(3);
            LevelRewardRules.GrantLevelChests(meta, Pool, new FakeTime());
            var again = LevelRewardRules.GrantLevelChests(meta, Pool, new FakeTime());
            Assert.That(again.Count, Is.EqualTo(0));
            Assert.That(meta.Chests.Count, Is.EqualTo(2));
        }

        [Test]
        public void Grant_StopsWhenSlotsFull_ThenResumes()
        {
            var time = new FakeTime();
            var meta = AtLevel(8); // 欠 Lv.2..8 共 7 只;只填空箱位(4 格),从不占暂存
            var grants = LevelRewardRules.GrantLevelChests(meta, Pool, time);

            Assert.That(grants.Count, Is.EqualTo(ChestRules.SlotLimit));
            Assert.That(meta.Chests.Count, Is.EqualTo(ChestRules.SlotLimit));
            Assert.That(meta.PendingChests.Count, Is.EqualTo(0), "每级宝箱不许占暂存位");
            Assert.That(meta.LevelRewardGranted, Is.EqualTo(1 + grants.Count));
            Assert.That(LevelRewardRules.OwedChests(meta), Is.EqualTo(3));

            // 腾两只位
            meta.Chests.RemoveAt(0);
            meta.Chests.RemoveAt(0);
            var more = LevelRewardRules.GrantLevelChests(meta, Pool, time);
            Assert.That(more.Count, Is.EqualTo(2));
            Assert.That(LevelRewardRules.OwedChests(meta), Is.EqualTo(1));
            Assert.That(more[0].Level, Is.EqualTo(meta.LevelRewardGranted - 1));
            Assert.That(meta.PendingChests.Count, Is.EqualTo(0));
        }

        [Test]
        public void Grant_SlotsFull_NeverTakesPendingSlot()
        {
            // 暂存位留给爬塔结算箱:每级宝箱占了它,结算箱就会作废(终审 C1)
            var time = new FakeTime();
            var meta = AtLevel(5);
            for (int i = 0; i < ChestRules.SlotLimit; i++)
                ChestRules.TryAwardChest(meta, ChestTier.Bamboo, Pool, time);

            var first = LevelRewardRules.GrantLevelChests(meta, Pool, time);
            var second = LevelRewardRules.GrantLevelChests(meta, Pool, time);

            Assert.That(first.Count, Is.EqualTo(0));
            Assert.That(second.Count, Is.EqualTo(0));
            Assert.That(meta.PendingChests.Count, Is.EqualTo(0));
            Assert.That(meta.LevelRewardGranted, Is.EqualTo(1));
            Assert.That(LevelRewardRules.OwedChests(meta), Is.EqualTo(4));
            Assert.That(ChestRules.AwardOrHold(meta, ChestTier.Gilded, Pool, time), Is.EqualTo(ChestAward.Held),
                "结算箱仍能拿到暂存位");
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
