using System;
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
        public void DrainPending_FillsFreedSlots_KeepsRest()
        {
            var time = new FakeTime();
            var meta = new MetaState();
            for (int i = 0; i < ChestRules.SlotLimit; i++) // 4 位全满
                ChestRules.TryAwardChest(meta, ChestTier.Paper, Pool, time);
            meta.PendingChests.Add(ChestTier.Gilded);
            meta.PendingChests.Add(ChestTier.Crimson);

            Assert.That(ChestRules.DrainPendingChests(meta, Pool, time), Is.EqualTo(0)); // 无空位,不动
            Assert.That(meta.PendingChests.Count, Is.EqualTo(2));

            meta.Chests.RemoveAt(0); // 开掉一只腾位
            Assert.That(ChestRules.DrainPendingChests(meta, Pool, time), Is.EqualTo(1)); // 入一只
            Assert.That(meta.Chests.Count, Is.EqualTo(ChestRules.SlotLimit));
            Assert.That(meta.PendingChests, Is.EqualTo(new[] { ChestTier.Crimson })); // 先进先出,鎏金已入
        }

        [Test]
        public void DrainPending_Empty_NoOp()
        {
            var meta = new MetaState();
            Assert.That(ChestRules.DrainPendingChests(meta, Pool, new FakeTime()), Is.EqualTo(0));
        }

        [Test]
        public void PendingChests_SurviveSaveRoundTrip()
        {
            var meta = new MetaState();
            meta.PendingChests.Add(ChestTier.Rosewood);
            var restored = Data.SaveSerializer.FromJson(Data.SaveSerializer.ToJson(meta));
            Assert.That(restored.PendingChests, Is.EqualTo(new[] { ChestTier.Rosewood }));
        }

        [Test]
        public void AwardChest_NoDailyLimit() // 2026-07-05 拍板:取消每日上限,节奏只由箱位与计时约束
        {
            var time = new FakeTime();
            var meta = new MetaState();
            for (int i = 0; i < 20; i++)
            {
                Assert.That(ChestRules.TryAwardChest(meta, ChestTier.Paper, Pool, time), Is.True);
                meta.Chests.Clear(); // 腾出箱位
            }
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
        public void StartOpening_AllowedWhilePreviousChestIsReadyButUncollected()
        {
            var time = new FakeTime();
            var meta = new MetaState();
            ChestRules.TryAwardChest(meta, ChestTier.Bamboo, Pool, time); // 30 分钟
            ChestRules.TryAwardChest(meta, ChestTier.Bamboo, Pool, time);

            Assert.That(ChestRules.TryStartOpening(meta, 0, time), Is.True);
            time.NowUnixSeconds += 1800;
            Assert.That(ChestRules.IsReady(meta.Chests[0], time), Is.True); // 就绪但没领

            // 占位的是「正在计时」而非「已就绪待领」:下一只该能开始计时
            Assert.That(ChestRules.TryStartOpening(meta, 1, time), Is.True);
            Assert.That(ChestRules.IsReady(meta.Chests[0], time), Is.True); // 前一只仍就绪可领
            Assert.That(ChestRules.RemainingSeconds(meta.Chests[1], time), Is.EqualTo(1800));

            // 对已就绪的箱再点「开始」要被拒,否则会把它的计时重置掉
            Assert.That(ChestRules.TryStartOpening(meta, 0, time), Is.False);
            Assert.That(ChestRules.IsReady(meta.Chests[0], time), Is.True);
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
            Assert.That(rewards.Cards, // 有放回抽取,重复=升级材料;Unity 版 NUnit 无 AnyOf
                Has.All.Matches<string>(c => System.Array.IndexOf(Pool, c) >= 0));
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

        // ---- 叠字前置解锁(spec 2026-08-15 Part 2)----

        private static RecipeGraph PrereqGraph() => new(new[]
        {
            new CharDef("土", Element.Earth),
            new CharDef("木", Element.Wood),
            new CharDef("杜", Element.Wood, new[] { "木", "土" }, rarity: CardRarity.Green),
            new CharDef("圭", Element.Earth, new[] { "土", "土" }, rarity: CardRarity.Purple),
            new CharDef("垚", Element.Earth, new[] { "土", "圭" }, rarity: CardRarity.Purple),
            new CharDef("桂", Element.Wood, new[] { "木", "圭" }, rarity: CardRarity.Purple),
            new CharDef("㙓", Element.Earth, new[] { "土", "垚" }, rarity: CardRarity.Purple),
        });

        [Test]
        public void Prerequisites_BlockThirdStack_UntilSecondStackOwned()
        {
            var graph = PrereqGraph();
            Assert.That(MetaRules.PrerequisitesMet("垚", graph, new[] { "杜" }), Is.False);
            Assert.That(MetaRules.PrerequisitesMet("垚", graph, new[] { "圭" }), Is.True);
        }

        /// <summary>桂 = 木+圭 不是叠字,但配方含二叠字,同样受限(需求原文举的例子)。</summary>
        [Test]
        public void Prerequisites_AlsoBlockNonStackCharsThatNeedAStackedIngredient()
        {
            Assert.That(MetaRules.PrerequisitesMet("桂", PrereqGraph(), new[] { "杜" }), Is.False);
            Assert.That(MetaRules.PrerequisitesMet("桂", PrereqGraph(), new[] { "圭" }), Is.True);
        }

        /// <summary>㙓 只看直接原料 垚;链式约束靠"拿 垚 本身就得先有 圭"自然成立。</summary>
        [Test]
        public void Prerequisites_OnlyChecksDirectIngredients()
        {
            Assert.That(MetaRules.PrerequisitesMet("㙓", PrereqGraph(), new[] { "圭" }), Is.False);
            Assert.That(MetaRules.PrerequisitesMet("㙓", PrereqGraph(), new[] { "垚" }), Is.True);
        }

        /// <summary>部件原料不参与判定:杜 = 木+土 两个都是部件,永远开得出。</summary>
        [Test]
        public void Prerequisites_IgnoreComponentIngredients()
        {
            Assert.That(MetaRules.PrerequisitesMet("杜", PrereqGraph(), Array.Empty<string>()), Is.True);
        }

        private static readonly string[] PrereqPool = { "杜", "圭", "垚", "桂", "㙓" };

        /// <summary>开箱流程与既有测试同款:TryAwardChest → TryStartOpening → 推进 FakeTime → TryOpen。
        /// FakeTime 只有 NowUnixSeconds 可写(见本文件顶部),没有 Advance 方法。</summary>
        private static ChestRewards OpenWith(string[] cardPool, string[] owned,
            ChestTier tier, int seed, RecipeGraph graph)
        {
            var time = new FakeTime();
            var meta = new MetaState();
            foreach (var card in owned) meta.OwnedCards.Add(card);
            ChestRules.TryAwardChest(meta, tier, cardPool, time);
            ChestRules.TryStartOpening(meta, 0, time);
            time.NowUnixSeconds += ChestRules.DurationSeconds[(int)tier - 1];
            Assert.That(ChestRules.TryOpen(meta, 0, time, new GameRandom(seed), out var rewards, graph), Is.True);
            return rewards;
        }

        /// <summary>开箱不会产出前置未满足的字:只该出 杜(原料全是部件)与 圭(同上);
        /// 垚(要圭)/桂(要圭)/㙓(要垚)在 OwnedCards 只有 杜 时全被挡。</summary>
        [Test]
        public void Open_DoesNotYieldCardsWithUnmetPrerequisites()
        {
            var rewards = OpenWith(PrereqPool, new[] { "杜" }, ChestTier.Paper, 7, PrereqGraph());
            Assert.That(rewards.Cards, Has.All.Matches<string>(c => c == "杜" || c == "圭"));
        }

        /// <summary>解锁 圭 之后 垚/桂 才可能出现(与上一条互为对照)。</summary>
        [Test]
        public void Open_YieldsGatedCards_OncePrerequisiteOwned()
        {
            var rewards = OpenWith(PrereqPool, new[] { "圭" }, ChestTier.Crimson, 3, PrereqGraph());
            Assert.That(rewards.Cards, Has.All.Matches<string>(c => c != "㙓"),
                "㙓 要 垚,仍该被挡");
            Assert.That(rewards.Cards.Contains("垚") || rewards.Cards.Contains("桂"), Is.True,
                "赤霄匣 16 张里应至少出现一次刚解锁的 垚 或 桂");
        }

        /// <summary>滤空兜底:一张都没解锁时仍出足数(隐藏限制不该让玩家看出来)。</summary>
        [Test]
        public void Open_StillYieldsFullCount_WhenEverythingIsFilteredOut()
        {
            var rewards = OpenWith(new[] { "垚", "桂", "㙓" }, Array.Empty<string>(),
                ChestTier.Paper, 7, PrereqGraph());
            Assert.That(rewards.Cards.Count, Is.EqualTo(ChestRules.CardCount[0]));
        }

        /// <summary>确定性:同种子 + 同 OwnedCards → 同结果。</summary>
        [Test]
        public void Open_IsDeterministic_UnderPrerequisiteFiltering()
        {
            var first = OpenWith(PrereqPool, new[] { "圭" }, ChestTier.Bamboo, 11, PrereqGraph());
            var second = OpenWith(PrereqPool, new[] { "圭" }, ChestTier.Bamboo, 11, PrereqGraph());
            Assert.That(first.Cards, Is.EqualTo(second.Cards));
        }
    }
}
