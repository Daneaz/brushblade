using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>宝箱系统(19.5 首版基准)。FakeTime 驱动,验证计时/广告/上限/开箱产卡。</summary>
    public class ChestTests
    {
        private sealed class FakeTime : ITimeSource
        {
            public long NowUnixSeconds { get; set; } = 1_000_000;
        }

        private static readonly string[] Pool = { "灯", "炎", "烧" };

        private static MetaState Award(FakeTime time, ChestTier tier = ChestTier.Paper)
        {
            var meta = new MetaState();
            Assert.That(ChestRules.TryAwardChest(meta, tier, Pool, time), Is.True);
            return meta;
        }

        // ---- 掉箱与上限 ----

        [Test]
        public void AwardChest_AddsIdleChest_WithCardPoolSnapshot()
        {
            var meta = Award(new FakeTime());
            Assert.That(meta.Chests.Count, Is.EqualTo(1));
            Assert.That(meta.Chests[0].Timing, Is.False);
            Assert.That(meta.Chests[0].CardPool, Is.EqualTo(Pool)); // F3:章节池快照
        }

        [Test]
        public void AwardChest_SlotLimit4_Rejected()
        {
            var time = new FakeTime();
            var meta = new MetaState();
            for (int i = 0; i < ChestRules.SlotLimit; i++)
                Assert.That(ChestRules.TryAwardChest(meta, ChestTier.Paper, Pool, time), Is.True);
            Assert.That(ChestRules.TryAwardChest(meta, ChestTier.Paper, Pool, time), Is.False);
        }

        [Test]
        public void AwardChest_DailyLimit8_ResetsNextDay()
        {
            var time = new FakeTime();
            var meta = new MetaState();
            for (int i = 0; i < ChestRules.DailyDropLimit; i++)
            {
                Assert.That(ChestRules.TryAwardChest(meta, ChestTier.Paper, Pool, time), Is.True);
                meta.Chests.Clear(); // 腾出箱位,单测每日计数
            }
            Assert.That(ChestRules.TryAwardChest(meta, ChestTier.Paper, Pool, time), Is.False);

            time.NowUnixSeconds += 86400; // 次日重置
            Assert.That(ChestRules.TryAwardChest(meta, ChestTier.Paper, Pool, time), Is.True);
        }

        // ---- 档位随角色等级(19.5.3) ----

        [Test]
        public void TierWeights_ShiftHigherWithLevel()
        {
            double Expected(int level)
            {
                var weights = ChestRules.TierWeightsFor(level);
                double sum = 0, total = 0;
                for (int i = 0; i < weights.Count; i++) { sum += (i + 1) * weights[i]; total += weights[i]; }
                return sum / total;
            }
            Assert.That(Expected(1), Is.LessThan(Expected(10)));
            Assert.That(Expected(10), Is.LessThan(Expected(30)));
        }

        [Test]
        public void RollTier_BossFirstClear_BumpsOneTier_Capped()
        {
            var normal = ChestRules.RollTier(1, new GameRandom(7));
            var bumped = ChestRules.RollTier(1, new GameRandom(7), bossFirstClear: true);
            Assert.That((int)bumped, Is.EqualTo(System.Math.Min(6, (int)normal + 1)));

            var capped = ChestRules.RollTier(999, new GameRandom(7), bossFirstClear: true);
            Assert.That((int)capped, Is.LessThanOrEqualTo(6));
        }

        // ---- 计时(单箱串行)与广告(每箱一次) ----

        [Test]
        public void StartOpening_OnlyOneChestTimingAtOnce()
        {
            var time = new FakeTime();
            var meta = new MetaState();
            ChestRules.TryAwardChest(meta, ChestTier.Paper, Pool, time);
            ChestRules.TryAwardChest(meta, ChestTier.Paper, Pool, time);

            Assert.That(ChestRules.TryStartOpening(meta, 0, time), Is.True);
            Assert.That(ChestRules.TryStartOpening(meta, 1, time), Is.False); // 已有箱在计时
        }

        [Test]
        public void Chest_ReadyAfterDuration()
        {
            var time = new FakeTime();
            var meta = Award(time, ChestTier.Bamboo); // 30 分钟
            ChestRules.TryStartOpening(meta, 0, time);
            Assert.That(ChestRules.IsReady(meta.Chests[0], time), Is.False);
            Assert.That(ChestRules.RemainingSeconds(meta.Chests[0], time), Is.EqualTo(1800));

            time.NowUnixSeconds += 1800;
            Assert.That(ChestRules.IsReady(meta.Chests[0], time), Is.True);
        }

        [Test]
        public void AdBoost_OncePerChest()
        {
            var time = new FakeTime();
            var meta = Award(time, ChestTier.Celadon); // 2h,广告 −40m
            ChestRules.TryStartOpening(meta, 0, time);

            Assert.That(ChestRules.TryApplyAdBoost(meta.Chests[0]), Is.True);
            Assert.That(ChestRules.RemainingSeconds(meta.Chests[0], time), Is.EqualTo(7200 - 2400));
            Assert.That(ChestRules.TryApplyAdBoost(meta.Chests[0]), Is.False); // 每箱仅一次
        }

        [Test]
        public void AdBoost_LowTier_OpensImmediately()
        {
            var time = new FakeTime();
            var meta = Award(time, ChestTier.Bamboo);
            ChestRules.TryStartOpening(meta, 0, time);
            ChestRules.TryApplyAdBoost(meta.Chests[0]); // 竹简:缩短量 = 全时长
            Assert.That(ChestRules.IsReady(meta.Chests[0], time), Is.True);
        }

        [Test]
        public void AdBoost_RequiresTiming()
        {
            var meta = Award(new FakeTime());
            Assert.That(ChestRules.TryApplyAdBoost(meta.Chests[0]), Is.False); // 未开始计时
        }

        // ---- 墨锭加速(1 墨锭 / 2 分钟,向上取整,最少 1) ----

        [TestCase(120, 1)]
        [TestCase(121, 2)]
        [TestCase(7200, 60)]
        [TestCase(1, 1)]
        public void InkCostToSkip_CeilPerTwoMinutes(long remaining, int cost)
        {
            Assert.That(ChestRules.InkCostToSkip(remaining), Is.EqualTo(cost));
        }

        [Test]
        public void SkipWithInk_DeductsAndMakesReady()
        {
            var time = new FakeTime();
            var meta = Award(time, ChestTier.Bamboo); // 30m → 15 墨锭
            meta.Ink = 20;
            ChestRules.TryStartOpening(meta, 0, time);

            Assert.That(ChestRules.TrySkipWithInk(meta, 0, time), Is.True);
            Assert.That(meta.Ink, Is.EqualTo(5));
            Assert.That(ChestRules.IsReady(meta.Chests[0], time), Is.True);
        }

        [Test]
        public void SkipWithInk_InsufficientInk_Fails()
        {
            var time = new FakeTime();
            var meta = Award(time, ChestTier.Bamboo);
            meta.Ink = 3;
            ChestRules.TryStartOpening(meta, 0, time);
            Assert.That(ChestRules.TrySkipWithInk(meta, 0, time), Is.False);
            Assert.That(meta.Ink, Is.EqualTo(3));
        }

        // ---- 开箱结算 ----

        [Test]
        public void Open_NotReady_Fails()
        {
            var time = new FakeTime();
            var meta = Award(time, ChestTier.Bamboo);
            ChestRules.TryStartOpening(meta, 0, time);
            Assert.That(ChestRules.TryOpen(meta, 0, time, new GameRandom(1), out _), Is.False);
            Assert.That(meta.Chests.Count, Is.EqualTo(1));
        }

        [Test]
        public void Open_GrantsInkAndCards_RemovesChest()
        {
            var time = new FakeTime();
            var meta = Award(time, ChestTier.Paper); // 3 卡 + 15 墨锭
            ChestRules.TryStartOpening(meta, 0, time);
            time.NowUnixSeconds += 300;

            Assert.That(ChestRules.TryOpen(meta, 0, time, new GameRandom(1), out var rewards), Is.True);
            Assert.That(rewards.Ink, Is.EqualTo(15));
            Assert.That(rewards.Cards.Count, Is.EqualTo(3));
            Assert.That(rewards.Cards, Is.All.AnyOf("灯", "炎", "烧")); // 有放回抽取,重复=升级材料
            Assert.That(meta.Ink, Is.EqualTo(15));
            Assert.That(meta.Chests, Is.Empty);
            // 卡入收集:首张 owned,重复转 copies
            int owned = rewards.Cards.Distinct().Count();
            Assert.That(meta.OwnedCards.Count, Is.EqualTo(owned));
        }

        [Test]
        public void Open_AllowsNextChestToStart()
        {
            var time = new FakeTime();
            var meta = new MetaState();
            ChestRules.TryAwardChest(meta, ChestTier.Paper, Pool, time);
            ChestRules.TryAwardChest(meta, ChestTier.Paper, Pool, time);
            ChestRules.TryStartOpening(meta, 0, time);
            time.NowUnixSeconds += 300;
            ChestRules.TryOpen(meta, 0, time, new GameRandom(1), out _);

            Assert.That(ChestRules.TryStartOpening(meta, 0, time), Is.True); // 剩下的箱顶上
        }
    }
}
