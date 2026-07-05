using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>每日商城(19.6 首版基准)。FakeTime 驱动。</summary>
    public class ShopTests
    {
        private sealed class FakeTime : ITimeSource
        {
            public long NowUnixSeconds { get; set; } = 1_000_000;
        }

        private static readonly string[] Pool = { "灯", "炎", "烧", "燃", "圭" };

        private static MetaState Fresh(FakeTime time)
        {
            var meta = new MetaState();
            ShopRules.EnsureShelf(meta, Pool, time, new GameRandom(1));
            return meta;
        }

        [Test]
        public void EnsureShelf_RollsOncePerDay()
        {
            var time = new FakeTime();
            var meta = Fresh(time);
            Assert.That(meta.Shop.CardSlots.Count, Is.EqualTo(ShopRules.CardSlotCount));
            Assert.That(meta.Shop.CardSlots, Is.All.AnyOf(Pool));

            var before = new System.Collections.Generic.List<string>(meta.Shop.CardSlots);
            Assert.That(ShopRules.EnsureShelf(meta, Pool, time, new GameRandom(999)), Is.False); // 当日不重掷
            Assert.That(meta.Shop.CardSlots, Is.EqualTo(before));

            time.NowUnixSeconds += 86400;
            Assert.That(ShopRules.EnsureShelf(meta, Pool, time, new GameRandom(2)), Is.True); // 跨日重掷
        }

        [Test]
        public void NewDay_ResetsDailyFlags()
        {
            var time = new FakeTime();
            var meta = Fresh(time);
            meta.Ink = 1000;
            ShopRules.TryBuyCard(meta, 0);
            ShopRules.TryClaimInkAd(meta);
            ShopRules.TryAdRefresh(meta, Pool, new GameRandom(3));

            time.NowUnixSeconds += 86400;
            ShopRules.EnsureShelf(meta, Pool, time, new GameRandom(4));
            Assert.That(meta.Shop.CardSold, Is.All.False);
            Assert.That(meta.Shop.InkAdClaimed, Is.False);
            Assert.That(meta.Shop.AdRefreshUsed, Is.False);
            Assert.That(meta.Shop.ChestSold, Is.False);
        }

        [Test]
        public void BuyCard_DeductsAcquiresMarksSold()
        {
            var meta = Fresh(new FakeTime());
            meta.Ink = 100;
            string card = meta.Shop.CardSlots[1];

            Assert.That(ShopRules.TryBuyCard(meta, 1), Is.True);
            Assert.That(meta.Ink, Is.EqualTo(100 - ShopRules.CardPrice));
            Assert.That(meta.OwnedCards, Does.Contain(card));
            Assert.That(ShopRules.TryBuyCard(meta, 1), Is.False); // 已售
        }

        [Test]
        public void BuyCard_InsufficientInk_Fails()
        {
            var meta = Fresh(new FakeTime());
            meta.Ink = ShopRules.CardPrice - 1;
            Assert.That(ShopRules.TryBuyCard(meta, 0), Is.False);
            Assert.That(meta.Ink, Is.EqualTo(ShopRules.CardPrice - 1));
        }

        [Test]
        public void BuyChest_AwardsIdleChest()
        {
            var time = new FakeTime();
            var meta = Fresh(time);
            meta.Ink = 5000;
            int price = ShopRules.ChestPrice[(int)meta.Shop.ChestSlot - 1];

            Assert.That(ShopRules.TryBuyChest(meta, Pool, time), Is.True);
            Assert.That(meta.Ink, Is.EqualTo(5000 - price));
            Assert.That(meta.Chests.Count, Is.EqualTo(1));
            Assert.That(meta.Chests[0].Timing, Is.False);
            Assert.That(ShopRules.TryBuyChest(meta, Pool, time), Is.False); // 已售
        }

        [Test]
        public void BuyChest_SlotsFull_Fails()
        {
            var time = new FakeTime();
            var meta = Fresh(time);
            meta.Ink = 5000;
            for (int i = 0; i < ChestRules.SlotLimit; i++)
                ChestRules.TryAwardChest(meta, ChestTier.Paper, Pool, time);

            Assert.That(ShopRules.TryBuyChest(meta, Pool, time), Is.False);
            Assert.That(meta.Ink, Is.EqualTo(5000)); // 不扣费
        }

        [Test]
        public void InkAd_OncePerDay()
        {
            var meta = Fresh(new FakeTime());
            Assert.That(ShopRules.TryClaimInkAd(meta), Is.True);
            Assert.That(meta.Ink, Is.EqualTo(ShopRules.InkAdAmount));
            Assert.That(ShopRules.TryClaimInkAd(meta), Is.False);
        }

        [Test]
        public void AdRefresh_OncePerDay_RerollsCards_KeepsInkAdFlag()
        {
            var meta = Fresh(new FakeTime());
            ShopRules.TryClaimInkAd(meta);
            meta.Ink = 100;
            ShopRules.TryBuyCard(meta, 0);

            Assert.That(ShopRules.TryAdRefresh(meta, Pool, new GameRandom(77)), Is.True);
            Assert.That(meta.Shop.CardSold, Is.All.False);          // 新货架可再购
            Assert.That(meta.Shop.InkAdClaimed, Is.True);           // 墨锭位不复位
            Assert.That(ShopRules.TryAdRefresh(meta, Pool, new GameRandom(78)), Is.False); // 每日一次
        }

        [Test]
        public void ShopState_SurvivesSaveRoundTrip()
        {
            var meta = Fresh(new FakeTime());
            meta.Ink = 100;
            ShopRules.TryBuyCard(meta, 2);

            var restored = Brushblade.Data.SaveSerializer.FromJson(
                Brushblade.Data.SaveSerializer.ToJson(meta));
            Assert.That(restored.Shop.CardSlots, Is.EqualTo(meta.Shop.CardSlots));
            Assert.That(restored.Shop.CardSold[2], Is.True);
            Assert.That(restored.Shop.DayStamp, Is.EqualTo(meta.Shop.DayStamp));
        }
    }
}
