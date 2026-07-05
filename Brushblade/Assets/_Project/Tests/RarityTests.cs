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

        [Test]
        public void UpgradeCosts_ScaleByRarity()
        {
            // 白卡 1 级:2 卡 / 20 墨锭(基准)
            Assert.That(MetaRules.CopiesRequired(1, CardRarity.White), Is.EqualTo(2));
            Assert.That(MetaRules.InkRequired(1, CardRarity.White), Is.EqualTo(20));
            // 红卡需卡 ≈ 白的 1/10(向上取整,最少 1),墨锭 ×5
            Assert.That(MetaRules.CopiesRequired(1, CardRarity.Red), Is.EqualTo(1));
            Assert.That(MetaRules.InkRequired(1, CardRarity.Red), Is.EqualTo(100));
            // 高等级同样成立:白 9 级 500 卡 → 红 50 卡
            Assert.That(MetaRules.CopiesRequired(9, CardRarity.Red), Is.EqualTo(50));
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
            var meta = new MetaState { Ink = 100 };
            MetaRules.AddCardCopies(meta, "燚", 1);
            // 红卡升 2 级只需 1 张重复卡 + 100 墨锭
            Assert.That(MetaRules.TryUpgradeCard(meta, "燚", CardRarity.Red), Is.True);
            Assert.That(meta.Ink, Is.EqualTo(0));
            Assert.That(MetaRules.CardLevel(meta, "燚"), Is.EqualTo(2));
        }

        // ---- 商城价格按稀有度(19.6) ----

        [Test]
        public void ShopPrice_ScalesByRarity()
        {
            Assert.That(ShopRules.CardPriceFor(CardRarity.White), Is.EqualTo(40));
            Assert.That(ShopRules.CardPriceFor(CardRarity.Red), Is.EqualTo(400));
            for (var rarity = CardRarity.White; rarity < CardRarity.Red; rarity++)
                Assert.That(ShopRules.CardPriceFor(rarity + 1),
                    Is.GreaterThan(ShopRules.CardPriceFor(rarity)));
        }

        [Test]
        public void BuyCard_ChargesByRarity()
        {
            var graph = MixedGraph();
            var meta = new MetaState { Ink = 1000 };
            ShopRules.EnsureShelf(meta, new[] { "燚" }, new FakeTime(), new GameRandom(1)); // 货架全是红卡
            Assert.That(ShopRules.TryBuyCard(meta, 0, CardRarity.Red), Is.True);
            Assert.That(meta.Ink, Is.EqualTo(1000 - 400));
        }

        // ---- 宝箱抽取:稀有度权重 + 保底(19.5.1) ----

        private static RecipeGraph MixedGraph() => ConfigLoader.LoadGraph(@"{
            ""chars"": [
                { ""id"": ""灯"", ""rarity"": ""White"" },
                { ""id"": ""烧"", ""rarity"": ""Green"" },
                { ""id"": ""壁"", ""rarity"": ""Blue"" },
                { ""id"": ""焚"", ""rarity"": ""Purple"" },
                { ""id"": ""焱"", ""rarity"": ""Orange"" },
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
        public void PaperChest_NeverDropsRed() // 一级箱红卡权重 0
        {
            var graph = MixedGraph();
            for (int seed = 0; seed < 50; seed++)
            {
                OpenChest(ChestTier.Paper, graph, seed, out var rewards);
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
        public void CrimsonChest_GuaranteesAtLeastOneRed() // 赤霄保底 1 红
        {
            var graph = MixedGraph();
            OpenChest(ChestTier.Crimson, graph, 7, out var rewards);
            Assert.That(rewards.Cards, Does.Contain("燚"));
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
            time.NowUnixSeconds += ChestRules.DurationSeconds[5];
            Assert.That(ChestRules.TryOpen(meta, 0, time, new GameRandom(3), out var rewards, graph), Is.True);
            Assert.That(rewards.Cards, Does.Contain("烧")); // 最高可得 = 绿
        }
    }
}
