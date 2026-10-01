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
        public void Grant_StopsWhenLost_ThenResumes()
        {
            var time = new FakeTime();
            var meta = AtLevel(8); // 欠 Lv.2..8 共 7 只;箱位 4 + 暂存 1 = 5
            var grants = LevelRewardRules.GrantLevelChests(meta, Pool, time);

            Assert.That(grants.Count, Is.EqualTo(ChestRules.SlotLimit + ChestRules.PendingLimit));
            Assert.That(grants.TrueForAll(g => g.Award != ChestAward.Lost), Is.True, "Lost 的那只不该出现在结果里");
            Assert.That(meta.LevelRewardGranted, Is.EqualTo(1 + grants.Count));
            Assert.That(LevelRewardRules.OwedChests(meta), Is.EqualTo(2));

            // 腾两只位:清掉一只箱位 + 暂存进位
            meta.Chests.RemoveAt(0);
            ChestRules.DrainPendingChests(meta, Pool, time);
            meta.Chests.RemoveAt(0);
            var more = LevelRewardRules.GrantLevelChests(meta, Pool, time);
            Assert.That(more.Count, Is.EqualTo(2));
            Assert.That(LevelRewardRules.OwedChests(meta), Is.EqualTo(0));
            Assert.That(more[0].Level, Is.EqualTo(meta.LevelRewardGranted - 1));
        }

        [Test]
        public void Summary_FirstRun_SeedsSilently()
        {
            var meta = AtLevel(20); // LastSeenLevel 缺省 0
            Assert.That(LevelRewardRules.TakeLevelUpSummary(meta), Is.Null);
            Assert.That(meta.LastSeenLevel, Is.EqualTo(20));
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
