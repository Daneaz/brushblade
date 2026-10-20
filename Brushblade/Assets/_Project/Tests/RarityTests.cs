using System.Linq;
using Brushblade.Core;
using Brushblade.Data;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>稀有度体系(19.3.1/19.3.3/19.5/19.6 首版基准)。纯测试。</summary>
    public class RarityTests
    {
        private sealed class FakeTime : ITimeSource
        {
            public long NowUnixSeconds { get; set; } = 1_000_000;
        }

        // ---- 配置解析 ----

        [Test]
        public void LoadGraph_ParsesRarity_DefaultsWhite()
        {
            var graph = ConfigLoader.LoadGraph(@"{
                ""chars"": [
                    { ""id"": ""燚"", ""element"": ""Fire"", ""rarity"": ""Red"" },
                    { ""id"": ""灯"", ""element"": ""Fire"" }
                ]
            }");
            Assert.That(graph.Get("燚").Rarity, Is.EqualTo(CardRarity.Red));
            Assert.That(graph.Get("灯").Rarity, Is.EqualTo(CardRarity.White));
        }

        [Test]
        public void LoadGraph_UnknownRarity_Throws()
        {
            var ex = Assert.Throws<ConfigException>(() => ConfigLoader.LoadGraph(
                @"{ ""chars"": [ { ""id"": ""謎"", ""rarity"": ""Rainbow"" } ] }"));
            Assert.That(ex.Message, Does.Contain("謎"));
        }

        // ---- 升级成本按稀有度分档(19.3.3:越稀有需卡更少、墨锭更贵) ----

        /// <summary>2026-10-20 拍板:同名卡改等比(1→2 需 1 张,每级 × 档位倍率,逐级向上取整),
        /// 墨锭基准砍半、档位系数改 1.0~4.0 每档 +0.5(向下取整)。整张表钉死,改值必须改这里。</summary>
        [TestCase(CardRarity.White, new[] { 1, 3, 9, 27, 81, 243, 729, 2187, 6561 })]
        [TestCase(CardRarity.Green, new[] { 1, 3, 7, 16, 40, 98, 245, 611, 1526 })]
        [TestCase(CardRarity.Blue, new[] { 1, 3, 7, 16, 40, 98, 245, 611, 1526 })]
        [TestCase(CardRarity.Purple, new[] { 1, 2, 4, 8, 16, 32, 64, 128, 256 })]
        [TestCase(CardRarity.Gold, new[] { 1, 2, 4, 8, 16, 32, 64, 128, 256 })]
        [TestCase(CardRarity.Orange, new[] { 1, 2, 3, 4, 6, 8, 12, 18, 26 })]
        [TestCase(CardRarity.Red, new[] { 1, 2, 3, 4, 6, 8, 12, 18, 26 })]
        public void CopiesRequired_FullTable(CardRarity rarity, int[] expected)
        {
            var actual = Enumerable.Range(1, 9).Select(l => MetaRules.CopiesRequired(l, rarity)).ToArray();
            Assert.That(actual, Is.EqualTo(expected), rarity.ToString());
        }

        [TestCase(CardRarity.White, new[] { 10, 25, 60, 150, 350, 750, 1500, 3000, 6000 })]
        [TestCase(CardRarity.Green, new[] { 15, 37, 90, 225, 525, 1125, 2250, 4500, 9000 })]
        [TestCase(CardRarity.Blue, new[] { 20, 50, 120, 300, 700, 1500, 3000, 6000, 12000 })]
        [TestCase(CardRarity.Purple, new[] { 25, 62, 150, 375, 875, 1875, 3750, 7500, 15000 })]
        [TestCase(CardRarity.Gold, new[] { 30, 75, 180, 450, 1050, 2250, 4500, 9000, 18000 })]
        [TestCase(CardRarity.Orange, new[] { 35, 87, 210, 525, 1225, 2625, 5250, 10500, 21000 })]
        [TestCase(CardRarity.Red, new[] { 40, 100, 240, 600, 1400, 3000, 6000, 12000, 24000 })]
        public void InkRequired_FullTable(CardRarity rarity, int[] expected)
        {
            var actual = Enumerable.Range(1, 9).Select(l => MetaRules.InkRequired(l, rarity)).ToArray();
            Assert.That(actual, Is.EqualTo(expected), rarity.ToString());
        }

        [Test]
        public void CopiesRequired_MonotonicAcrossRarity()
        {
            for (var rarity = CardRarity.White; rarity < CardRarity.Red; rarity++)
                Assert.That(MetaRules.CopiesRequired(5, rarity + 1),
                    Is.LessThanOrEqualTo(MetaRules.CopiesRequired(5, rarity)), rarity.ToString());
        }

        [Test]
        public void TryUpgradeCard_UsesRarityScaledCosts()
        {
            var meta = new MetaState { Ink = 40 };
            MetaRules.AddCardCopies(meta, "燚", 1);
            // 红卡升 2 级只需 1 张重复卡 + 40 墨锭
            Assert.That(MetaRules.TryUpgradeCard(meta, "燚", CardRarity.Red), Is.True);
            Assert.That(meta.Ink, Is.EqualTo(0));
            Assert.That(MetaRules.CardLevel(meta, "燚"), Is.EqualTo(2));
        }

        // ---- 商城价格按稀有度(19.6) ----

        // 2026-09-30 改为按稀有度打包卖:逐档定价与「单张价随稀有度递增」见 ShopBundleTests

        [Test]
        public void BuyCard_ChargesByRarity()
        {
            var graph = MixedGraph();
            var meta = new MetaState { Ink = 1000 };
            ShopRules.EnsureShelf(meta, new[] { "燚" }, new FakeTime(), new GameRandom(1)); // 货架全是红卡
            Assert.That(ShopRules.TryBuyCard(meta, 0, CardRarity.Red), Is.True);
            Assert.That(meta.Ink, Is.EqualTo(1000 - 600 * ShopRules.DiscountPercent(true, meta.Shop.CardDiscountRoll[0]) / 100),
                "红一份底价 600,按当日 6~8 折扣");
        }

        // ---- 宝箱抽取:稀有度权重 + 保底(19.5.1) ----

        private static RecipeGraph MixedGraph() => ConfigLoader.LoadGraph(@"{
            ""chars"": [
                { ""id"": ""灯"", ""rarity"": ""White"" },
                { ""id"": ""烧"", ""rarity"": ""Green"" },
                { ""id"": ""壁"", ""rarity"": ""Blue"" },
                { ""id"": ""焚"", ""rarity"": ""Purple"" },
                { ""id"": ""焱"", ""rarity"": ""Gold"" },
                { ""id"": ""燚"", ""rarity"": ""Red"" }
            ]
        }");

        private static MetaState OpenChest(ChestTier tier, RecipeGraph graph, int seed, out ChestRewards rewards)
        {
            var time = new FakeTime();
            var meta = new MetaState();
            ChestRules.TryAwardChest(meta, tier,
                new[] { "灯", "烧", "壁", "焚", "焱", "燚" }, time);
            ChestRules.TryStartOpening(meta, 0, time);
            time.NowUnixSeconds += ChestRules.DurationSeconds[(int)tier - 1];
            ChestRules.TryOpen(meta, 0, time, new GameRandom(seed), out rewards, graph);
            return meta;
        }

        [Test]
        public void BambooChest_NeverDropsRed() // 竹简匣红卡权重 0
        {
            var graph = MixedGraph();
            for (int seed = 0; seed < 50; seed++)
            {
                OpenChest(ChestTier.Bamboo, graph, seed, out var rewards);
                Assert.That(rewards.Cards, Does.Not.Contain("燚"), $"seed={seed}");
            }
        }

        [Test]
        public void CeladonChest_GuaranteesAtLeastOneBluePlus() // 青瓷保底 1 蓝+
        {
            var graph = MixedGraph();
            for (int seed = 0; seed < 50; seed++)
            {
                OpenChest(ChestTier.Celadon, graph, seed, out var rewards);
                bool hasBluePlus = rewards.Cards.Any(c => graph.Get(c).Rarity >= CardRarity.Blue);
                Assert.That(hasBluePlus, Is.True, $"seed={seed}");
            }
        }

        [Test]
        public void CrimsonChest_GuaranteesAtLeastOnePurplePlus() // 保底钳到紫(首发仅绿蓝紫)
        {
            var graph = MixedGraph();
            for (int seed = 0; seed < 50; seed++)
            {
                OpenChest(ChestTier.Crimson, graph, seed, out var rewards);
                bool hasPurplePlus = rewards.Cards.Any(c => graph.Get(c).Rarity >= CardRarity.Purple);
                Assert.That(hasPurplePlus, Is.True, $"seed={seed}");
            }
        }

        [Test]
        public void HighTierChests_AlsoDropWhite() // 2026-10-01 起高档箱也出白(白卡升满要 1081 张)
        {
            var graph = MixedGraph();
            bool white = false;
            for (int seed = 0; seed < 50 && !white; seed++)
            {
                OpenChest(ChestTier.Gilded, graph, seed, out var rewards);
                white = rewards.Cards.Contains("灯");
            }
            Assert.That(white, Is.True, "鎏金 12 捆 × 白 197‰,50 箱不该一张白都没有");
        }

        [Test]
        public void Guarantee_FallsBackToHighestAvailable() // 池中无达标稀有度时取最高可得
        {
            var graph = ConfigLoader.LoadGraph(@"{
                ""chars"": [ { ""id"": ""灯"", ""rarity"": ""White"" }, { ""id"": ""烧"", ""rarity"": ""Green"" } ]
            }");
            var time = new FakeTime();
            var meta = new MetaState();
            ChestRules.TryAwardChest(meta, ChestTier.Crimson, new[] { "灯", "烧" }, time);
            ChestRules.TryStartOpening(meta, 0, time);
            time.NowUnixSeconds += ChestRules.DurationSeconds[(int)ChestTier.Crimson - 1];
            Assert.That(ChestRules.TryOpen(meta, 0, time, new GameRandom(3), out var rewards, graph), Is.True);
            Assert.That(rewards.Cards, Does.Contain("烧")); // 最高可得 = 绿
        }

        [Test]
        public void ChestWithTopRarityCards_DoesNotThrow() // 宝箱卡池含最高档红卡时不越界
        {
            var graph = ConfigLoader.LoadGraph(@"{
                ""chars"": [
                    { ""id"": ""灯"", ""rarity"": ""White"" },
                    { ""id"": ""烧"", ""rarity"": ""Green"" },
                    { ""id"": ""壁"", ""rarity"": ""Blue"" },
                    { ""id"": ""焚"", ""rarity"": ""Purple"" },
                    { ""id"": ""焱"", ""rarity"": ""Gold"" },
                    { ""id"": ""焔"", ""rarity"": ""Orange"" },
                    { ""id"": ""燚"", ""rarity"": ""Red"" }
                ]
            }");
            var time = new FakeTime();
            var meta = new MetaState();
            // 任意宝箱、包含最高档红卡的池
            ChestRules.TryAwardChest(meta, ChestTier.Celadon,
                new[] { "灯", "烧", "壁", "焚", "焱", "燚", "焔" }, time);
            ChestRules.TryStartOpening(meta, 0, time);
            time.NowUnixSeconds += ChestRules.DurationSeconds[(int)ChestTier.Celadon - 1];
            // 开箱不应抛异常
            Assert.That(ChestRules.TryOpen(meta, 0, time, new GameRandom(42), out var rewards, graph), Is.True);
            Assert.That(rewards.Cards.Count, Is.GreaterThan(0));
        }

        [Test]
        public void TopRarity_AllRarityIndexedArrays_DoNotThrow()
        {
            Assert.That(MetaRules.CopiesRequired(1, CardRarity.Red), Is.GreaterThan(0));
            Assert.That(MetaRules.InkRequired(1, CardRarity.Red), Is.GreaterThan(0));
            Assert.That(ShopRules.BundlePriceFor(CardRarity.Red), Is.GreaterThan(0));
            Assert.That(ShopRules.BundleSizeFor(CardRarity.Red), Is.GreaterThan(0));
        }

        [Test]
        public void TopRarity_IsRarestTier() // 红(7)比次高的橙(6)更稀有更贵
        {
            Assert.That(MetaRules.CopiesRequired(1, CardRarity.Red),
                Is.LessThanOrEqualTo(MetaRules.CopiesRequired(1, CardRarity.Orange)));
            Assert.That(MetaRules.InkRequired(1, CardRarity.Red),
                Is.GreaterThan(MetaRules.InkRequired(1, CardRarity.Orange)));
            Assert.That(ShopRules.BundlePriceFor(CardRarity.Red),
                Is.GreaterThan(ShopRules.BundlePriceFor(CardRarity.Orange)));
        }
    }
}
