using System.Collections.Generic;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>商城每日随机打折(2026-10-01 用户拍板):宝箱底价 = 字摊等价值;
    /// 字摊每份与宝箱每日随机 3~7 折,橙/红字与朱漆/赤霄宝箱 6~8 折。整折为一档。</summary>
    public class ShopDiscountTests
    {
        private sealed class FakeTime : ITimeSource
        {
            public long NowUnixSeconds { get; set; } = 1_000_000;
        }

        private static readonly string[] Pool = { "灯", "炎", "烧", "燃", "圭" };

        private static MetaState Fresh(int seed = 1, FakeTime time = null)
        {
            var meta = new MetaState { Ink = 100_000 };
            ShopRules.EnsureShelf(meta, Pool, time ?? new FakeTime(), new GameRandom(seed));
            return meta;
        }

        [Test]
        public void ChestBasePrice_IsPinned()
        {
            Assert.That(ShopRules.ChestBasePrice, Is.EqualTo(new[] { 230, 460, 700, 1180, 2850, 3850, 4870 }));
        }

        /// <summary>底价 = 该箱期望开出的张数按字摊单张价(每份价 ÷ 每份张数)折算(误差 ≤ 3%,底价取整到十位)。
        /// 改了种数、总张数或权重而不改底价,这条会红。</summary>
        [Test]
        public void ChestBasePrice_MatchesBundleEquivalentValue()
        {
            for (int tier = 1; tier <= 7; tier++)
            {
                var copies = ChestBundleTests.ExpectedCopies((ChestTier)tier);
                double value = 0;
                for (int r = 0; r < 7; r++)
                {
                    var rarity = (CardRarity)(r + 1);
                    value += copies[r] * ShopRules.BundlePriceFor(rarity) / ShopRules.BundleSizeFor(rarity);
                }
                Assert.That(ShopRules.ChestBasePrice[tier - 1], Is.EqualTo(value).Within(value * 0.03), $"tier {tier}");
            }
        }

        [Test]
        public void DiscountRanges_RegularAndPremium()
        {
            var regular = new HashSet<int>();
            var premium = new HashSet<int>();
            for (int roll = 0; roll < 60; roll++)
            {
                regular.Add(ShopRules.DiscountPercent(false, roll));
                premium.Add(ShopRules.DiscountPercent(true, roll));
            }
            Assert.That(regular, Is.EquivalentTo(new[] { 30, 40, 50, 60, 70 }));
            Assert.That(premium, Is.EquivalentTo(new[] { 60, 70, 80 }));
        }

        [TestCase(CardRarity.White, false)]
        [TestCase(CardRarity.Gold, false)]
        [TestCase(CardRarity.Orange, true)]
        [TestCase(CardRarity.Red, true)]
        public void CardPremium_IsOrangeAndRed(CardRarity rarity, bool premium)
        {
            Assert.That(ShopRules.IsPremium(rarity), Is.EqualTo(premium));
        }

        [TestCase(ChestTier.Gilded, false)]
        [TestCase(ChestTier.Vermilion, true)]
        [TestCase(ChestTier.Crimson, true)]
        public void ChestPremium_IsVermilionAndCrimson(ChestTier tier, bool premium)
        {
            Assert.That(ShopRules.IsPremium(tier), Is.EqualTo(premium));
        }

        [Test]
        public void EverySlot_HasADiscountRoll()
        {
            var meta = Fresh();
            Assert.That(meta.Shop.CardDiscountRoll.Count, Is.EqualTo(meta.Shop.CardSlots.Count));
            Assert.That(meta.Shop.ChestDiscountRoll, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void BuyCard_ChargesDiscountedPrice()
        {
            var meta = Fresh();
            int price = ShopRules.CardPrice(meta, 0, CardRarity.Blue);
            Assert.That(price, Is.EqualTo(ShopRules.BundlePriceFor(CardRarity.Blue)
                * ShopRules.DiscountPercent(false, meta.Shop.CardDiscountRoll[0]) / 100));
            Assert.That(ShopRules.TryBuyCard(meta, 0, CardRarity.Blue), Is.True);
            Assert.That(meta.Ink, Is.EqualTo(100_000 - price));
        }

        [Test]
        public void BuyChest_ChargesDiscountedPrice()
        {
            var time = new FakeTime();
            var meta = Fresh(1, time);
            int price = ShopRules.ChestPrice(meta);
            var tier = meta.Shop.ChestSlot;
            Assert.That(price, Is.EqualTo(ShopRules.ChestBasePrice[(int)tier - 1]
                * ShopRules.DiscountPercent(ShopRules.IsPremium(tier), meta.Shop.ChestDiscountRoll) / 100));
            Assert.That(ShopRules.TryBuyChest(meta, Pool, time), Is.True);
            Assert.That(meta.Ink, Is.EqualTo(100_000 - price));
        }

        [Test]
        public void Discounts_VaryAcrossDays()
        {
            var seen = new HashSet<int>();
            for (int seed = 0; seed < 40; seed++)
                seen.Add(ShopRules.DiscountPercent(false, Fresh(seed).Shop.CardDiscountRoll[0]));
            Assert.That(seen.Count, Is.GreaterThan(2));
        }

        [Test]
        public void AdRefresh_RerollsDiscounts()
        {
            bool changed = false;
            for (int seed = 0; seed < 30 && !changed; seed++)
            {
                var meta = Fresh(seed);
                int before = meta.Shop.ChestDiscountRoll;
                ShopRules.TryAdRefresh(meta, Pool, new GameRandom(seed + 500));
                changed = meta.Shop.ChestDiscountRoll != before;
            }
            Assert.That(changed, Is.True);
        }

        [Test]
        public void SameDayShelfWithoutRolls_GetsPadded()
        {
            // 版本更新当天:旧货架没有折扣,补掷一次,不等到明天
            var time = new FakeTime();
            var meta = Fresh(1, time);
            meta.Shop.CardDiscountRoll.Clear();
            meta.Shop.ChestDiscountRoll = -1;
            Assert.That(ShopRules.EnsureShelf(meta, Pool, time, new GameRandom(2)), Is.True);
            Assert.That(meta.Shop.CardDiscountRoll.Count, Is.EqualTo(meta.Shop.CardSlots.Count));
            Assert.That(meta.Shop.ChestDiscountRoll, Is.GreaterThanOrEqualTo(0));
        }
    }
}
