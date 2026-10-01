using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>宝箱改「成捆」开出(2026-10-01 用户拍板):每箱 S 捆,每捆按档位权重抽一个稀有度、
    /// 一个字,张数 = 商城字摊同档每份张数(白 20 / 绿 10 / 蓝 5 / 紫 2 / 金橙红 1)。
    /// 高档箱也出低档字;每箱金/橙/红的期望张数与改版前一致(用户:「保持不变」)。</summary>
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

        [Test]
        public void StackCount_IsPinned()
        {
            Assert.That(ChestRules.StackCount, Is.EqualTo(new[] { 1, 2, 3, 5, 12, 16, 20 }));
        }

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

        /// <summary>改版前每箱金/橙/红期望张数 = 旧卡数 × 旧权重(3/4/6/8/12/14/16 × 2026-08-29 拍板表)。
        /// 金橙红每捆 1 张,所以新期望 = 捆数 × 新权重;四舍五入到 ‰ 后误差 ≤ 0.01 张。</summary>
        [TestCase(ChestTier.Celadon, 0.06, 0.0, 0.0)]
        [TestCase(ChestTier.Rosewood, 0.16, 0.04, 0.0)]
        [TestCase(ChestTier.Gilded, 0.60, 0.12, 0.012)]
        [TestCase(ChestTier.Vermilion, 0.98, 0.28, 0.07)]
        [TestCase(ChestTier.Crimson, 1.60, 0.48, 0.16)]
        public void HighRarityExpectationPerChest_Unchanged(ChestTier tier, double gold, double orange, double red)
        {
            var w = ChestRules.CardRarityWeightsFor(tier);
            int s = ChestRules.StackCount[(int)tier - 1];
            Assert.That(s * w[4] / 1000.0, Is.EqualTo(gold).Within(0.01), "金");
            Assert.That(s * w[5] / 1000.0, Is.EqualTo(orange).Within(0.01), "橙");
            Assert.That(s * w[6] / 1000.0, Is.EqualTo(red).Within(0.01), "红");
        }

        [Test]
        public void Open_YieldsOneEntryPerStack_WithBundleSizedCounts()
        {
            var graph = RarityGraph();
            for (int tier = 1; tier <= 7; tier++)
            {
                var rewards = Open(new MetaState(), (ChestTier)tier, tier * 7, graph);
                Assert.That(rewards.Cards.Count, Is.EqualTo(ChestRules.StackCount[tier - 1]), $"tier {tier}");
                Assert.That(rewards.Counts.Count, Is.EqualTo(rewards.Cards.Count));
                for (int i = 0; i < rewards.Cards.Count; i++)
                    Assert.That(rewards.Counts[i],
                        Is.EqualTo(ShopRules.BundleSizeFor(graph.Get(rewards.Cards[i]).Rarity)));
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
