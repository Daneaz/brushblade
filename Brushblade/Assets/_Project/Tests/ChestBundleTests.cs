using System;
using System.Collections.Generic;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>宝箱「每箱 N 种、共 M 张」(2026-10-02 用户拍板,取代 10-01 的固定成捆):
    /// 每箱抽 N 种字(3/4/6/8/12/14/16),每种的稀有度按档位权重掷;金/橙/红每种 1 张,
    /// 其余张数按 白20 : 绿10 : 蓝5 : 紫2 的份额分给白~紫(每种至少 1 张),总数 = M。
    /// 白~紫权重重配到「每箱各稀有度期望张数 ≈ 10-01 成捆版」,经济与商城底价不动。</summary>
    public class ChestBundleTests
    {
        private sealed class FakeTime : ITimeSource
        {
            public long NowUnixSeconds { get; set; } = 1_000_000;
        }

        private static RecipeGraph RarityGraph() => new(new[]
        {
            new CharDef("c1", Element.Fire),
            new CharDef("c2", Element.Water),
            new CharDef("r1", Element.Fire, new[] { "c1", "c2" }, rarity: CardRarity.White),
            new CharDef("r2", Element.Fire, new[] { "c1", "c2" }, rarity: CardRarity.Green),
            new CharDef("r3", Element.Fire, new[] { "c1", "c2" }, rarity: CardRarity.Blue),
            new CharDef("r4", Element.Fire, new[] { "c1", "c2" }, rarity: CardRarity.Purple),
            new CharDef("r5", Element.Fire, new[] { "c1", "c2" }, rarity: CardRarity.Gold),
            new CharDef("r6", Element.Fire, new[] { "c1", "c2" }, rarity: CardRarity.Orange),
            new CharDef("r7", Element.Fire, new[] { "c1", "c2" }, rarity: CardRarity.Red),
        });

        private static readonly string[] Pool = { "r1", "r2", "r3", "r4", "r5", "r6", "r7" };

        private static ChestRewards Open(MetaState meta, ChestTier tier, int seed, RecipeGraph graph)
        {
            var time = new FakeTime();
            ChestRules.TryAwardChest(meta, tier, Pool, time);
            int index = meta.Chests.Count - 1;
            ChestRules.TryStartOpening(meta, index, time);
            time.NowUnixSeconds += ChestRules.DurationSeconds[(int)tier - 1];
            Assert.That(ChestRules.TryOpen(meta, index, time, new GameRandom(seed), out var rewards, graph), Is.True);
            return rewards;
        }

        /// <summary>该档每箱各稀有度的**精确**期望张数(保底前):枚举 N 种字的全部稀有度组合,
        /// 按多项分布加权,每个组合的张数走 <see cref="ChestRules.SplitCopies"/> 本身。</summary>
        internal static double[] ExpectedCopies(ChestTier tier)
        {
            var weights = ChestRules.CardRarityWeightsFor(tier);
            int kinds = ChestRules.KindCount[(int)tier - 1];
            int total = ChestRules.TotalCards[(int)tier - 1];
            var result = new double[7];
            var counts = new int[7];

            void Recurse(int rarity, int left)
            {
                if (rarity == 6)
                {
                    counts[6] = left;
                    double p = Factorial(kinds);
                    var drawn = new List<CardRarity>();
                    for (int r = 0; r < 7; r++)
                    {
                        if (counts[r] > 0 && weights[r] == 0) return;
                        p *= Math.Pow(weights[r] / 1000.0, counts[r]) / Factorial(counts[r]);
                        for (int i = 0; i < counts[r]; i++) drawn.Add((CardRarity)(r + 1));
                    }
                    var split = ChestRules.SplitCopies(drawn, total);
                    for (int i = 0; i < drawn.Count; i++)
                        result[(int)drawn[i] - 1] += p * split[i];
                    return;
                }
                for (int n = 0; n <= left; n++)
                {
                    counts[rarity] = n;
                    Recurse(rarity + 1, left - n);
                }
            }

            Recurse(0, kinds);
            return result;
        }

        private static double Factorial(int n)
        {
            double f = 1;
            for (int i = 2; i <= n; i++) f *= i;
            return f;
        }

        [Test]
        public void KindCountAndTotalCards_ArePinned()
        {
            Assert.That(ChestRules.KindCount, Is.EqualTo(new[] { 3, 4, 6, 8, 12, 14, 16 }));
            Assert.That(ChestRules.TotalCards, Is.EqualTo(new[] { 13, 22, 27, 40, 91, 115, 136 }));
        }

        [TestCase(ChestTier.Paper, 13)]
        [TestCase(ChestTier.Crimson, 136)]
        public void ExpectedCards_IsTotalCards(ChestTier tier, int expected)
        {
            Assert.That(ChestRules.ExpectedCards(tier), Is.EqualTo(expected));
        }

        // ---- 张数分配 ----

        [Test]
        public void Split_WhiteGreenBlue_ByShare()
        {
            var split = ChestRules.SplitCopies(new[] { CardRarity.White, CardRarity.Green, CardRarity.Blue }, 13);
            Assert.That(split, Is.EqualTo(new[] { 7, 4, 2 }), "每种先给 1,余 10 张按 20:10:5 分");
        }

        [Test]
        public void Split_TwoWhitesOneGreen()
        {
            var split = ChestRules.SplitCopies(new[] { CardRarity.White, CardRarity.Green, CardRarity.White }, 13);
            Assert.That(split, Is.EqualTo(new[] { 5, 3, 5 }));
        }

        [Test]
        public void Split_GoldAndAboveGetExactlyOne()
        {
            var split = ChestRules.SplitCopies(
                new[] { CardRarity.Gold, CardRarity.White, CardRarity.Red, CardRarity.Orange }, 13);
            Assert.That(split, Is.EqualTo(new[] { 1, 10, 1, 1 }), "金橙红各 1,剩下的全归白");
        }

        [Test]
        public void Split_AllHighRarity_FallsShortOfTotal()
        {
            var split = ChestRules.SplitCopies(new[] { CardRarity.Gold, CardRarity.Orange }, 13);
            Assert.That(split, Is.EqualTo(new[] { 1, 1 }), "没有白~紫可分时不凑数");
        }

        [Test]
        public void Split_EveryKindAtLeastOne_AndSumsToTotal()
        {
            var kinds = new List<CardRarity>();
            for (int i = 0; i < 16; i++) kinds.Add(i % 2 == 0 ? CardRarity.White : CardRarity.Purple);
            var split = ChestRules.SplitCopies(kinds, 136);
            int sum = 0;
            foreach (var n in split)
            {
                Assert.That(n, Is.GreaterThanOrEqualTo(1));
                sum += n;
            }
            Assert.That(sum, Is.EqualTo(136));
        }

        /// <summary>余数平局:先给低稀有度,同稀有度按抽出先后。白+绿+绿共 5 张:余 2 张按 20:10:10
        /// = 1 / 0.5 / 0.5,白拿 1,剩 1 张在两个绿之间平局,给先抽出的那个。</summary>
        [Test]
        public void Split_TieGoesToEarlierDraw()
        {
            var split = ChestRules.SplitCopies(new[] { CardRarity.White, CardRarity.Green, CardRarity.Green }, 5);
            Assert.That(split, Is.EqualTo(new[] { 2, 2, 1 }));
        }

        // ---- 期望 ----

        [Test]
        public void EveryTier_StillYieldsWhiteAndGreen()
        {
            for (int tier = 1; tier <= 7; tier++)
            {
                var w = ChestRules.CardRarityWeightsFor((ChestTier)tier);
                Assert.That(w[0], Is.GreaterThan(0), $"tier {tier} 白");
                Assert.That(w[1], Is.GreaterThan(0), $"tier {tier} 绿");
            }
        }

        /// <summary>每箱金/橙/红期望张数 = 旧卡数 × 旧权重(3/4/6/8/12/14/16 × 2026-08-29 拍板表)。
        /// 金橙红每种 1 张,所以期望 = 种数 × 权重。</summary>
        [TestCase(ChestTier.Celadon, 0.06, 0.0, 0.0)]
        [TestCase(ChestTier.Rosewood, 0.16, 0.04, 0.0)]
        [TestCase(ChestTier.Gilded, 0.60, 0.12, 0.012)]
        [TestCase(ChestTier.Vermilion, 0.98, 0.28, 0.07)]
        [TestCase(ChestTier.Crimson, 1.60, 0.48, 0.16)]
        public void HighRarityExpectationPerChest_Unchanged(ChestTier tier, double gold, double orange, double red)
        {
            var w = ChestRules.CardRarityWeightsFor(tier);
            int n = ChestRules.KindCount[(int)tier - 1];
            Assert.That(n * w[4] / 1000.0, Is.EqualTo(gold).Within(0.001), "金");
            Assert.That(n * w[5] / 1000.0, Is.EqualTo(orange).Within(0.001), "橙");
            Assert.That(n * w[6] / 1000.0, Is.EqualTo(red).Within(0.001), "红");
        }

        /// <summary>白~紫每箱期望张数 ≈ 10-01 成捆版(捆数 × 每捆权重 × 每捆张数),误差 ≤ 3% 或 0.1 张。
        /// 用户 2026-10-02 拍板「调白~紫权重对齐」:改开法不改产出,升级节奏与商城底价都不动。</summary>
        [TestCase(ChestTier.Paper, 8.0, 4.0, 1.0, 0.0)]
        [TestCase(ChestTier.Bamboo, 12.0, 7.0, 2.5, 0.4)]
        [TestCase(ChestTier.Celadon, 13.62, 7.74, 4.34, 1.24)]
        [TestCase(ChestTier.Rosewood, 21.70, 8.55, 6.58, 3.09)]
        [TestCase(ChestTier.Gilded, 47.28, 19.44, 15.30, 7.80)]
        [TestCase(ChestTier.Vermilion, 59.52, 24.16, 19.52, 10.75)]
        [TestCase(ChestTier.Crimson, 69.20, 27.60, 23.10, 13.84)]
        public void LowRarityExpectationPerChest_MatchesBundleEra(ChestTier tier,
            double white, double green, double blue, double purple)
        {
            var e = ExpectedCopies(tier);
            var target = new[] { white, green, blue, purple };
            var names = new[] { "白", "绿", "蓝", "紫" };
            for (int r = 0; r < 4; r++)
                Assert.That(e[r], Is.EqualTo(target[r]).Within(Math.Max(0.1, target[r] * 0.03)), names[r]);
        }

        // ---- 开箱 ----

        [Test]
        public void Open_YieldsKindCountEntries_SummingToTotal()
        {
            var graph = RarityGraph();
            for (int tier = 1; tier <= 7; tier++)
            {
                var rewards = Open(new MetaState(), (ChestTier)tier, tier * 7, graph);
                Assert.That(rewards.Cards.Count, Is.EqualTo(ChestRules.KindCount[tier - 1]), $"tier {tier}");
                Assert.That(rewards.Counts.Count, Is.EqualTo(rewards.Cards.Count));

                var rarities = new List<CardRarity>();
                foreach (var card in rewards.Cards) rarities.Add(graph.Get(card).Rarity);
                Assert.That(rewards.Counts, Is.EqualTo(ChestRules.SplitCopies(rarities, ChestRules.TotalCards[tier - 1])),
                    $"tier {tier}:张数按保底替换后的最终稀有度分");
            }
        }

        [Test]
        public void Open_Paper_AlwaysThreeKindsThirteenCards()
        {
            var graph = RarityGraph();
            for (int seed = 1; seed <= 20; seed++)
            {
                var rewards = Open(new MetaState(), ChestTier.Paper, seed, graph);
                Assert.That(rewards.Cards.Count, Is.EqualTo(3));
                int sum = 0;
                foreach (var n in rewards.Counts) sum += n;
                Assert.That(sum, Is.EqualTo(13), $"seed {seed}");
            }
        }

        [Test]
        public void Open_GrantsEveryCopy()
        {
            var graph = RarityGraph();
            var meta = new MetaState();
            var rewards = Open(meta, ChestTier.Crimson, 3, graph);

            int granted = 0;
            foreach (var count in rewards.Counts) granted += count;
            int held = meta.OwnedCards.Count;
            foreach (var copies in meta.CardCopies.Values) held += copies;
            Assert.That(held, Is.EqualTo(granted), "首张入收藏,其余全进同名卡");
        }

        [Test]
        public void Open_WithoutGraph_CountsAreOne()
        {
            var meta = new MetaState();
            var time = new FakeTime();
            ChestRules.TryAwardChest(meta, ChestTier.Rosewood, Pool, time);
            ChestRules.TryStartOpening(meta, 0, time);
            time.NowUnixSeconds += ChestRules.DurationSeconds[3];
            ChestRules.TryOpen(meta, 0, time, new GameRandom(1), out var rewards);
            Assert.That(rewards.Counts, Has.All.EqualTo(1));
        }
    }
}
