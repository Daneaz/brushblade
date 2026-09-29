using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>商城字摊改版(2026-09-30 用户拍板):按稀有度打包卖、三个看广告领字卡位、
    /// 货架槽位随角色等级解锁。
    ///
    /// 定价依据:升级的瓶颈是张数不是墨锭(白卡 1→10 共 1081 张),一张一卖后期买不动;
    /// 锚点是商城紫檀箱 —— 400 墨锭开 8 张随机卡外加返还 120,折合约 35 墨锭一张。
    /// 白→紫每份价落在 200–260 的同一带,低档打折更深(要的张数最多),金及以上一张原价。</summary>
    public class ShopBundleTests
    {
        private sealed class FakeTime : ITimeSource
        {
            public long NowUnixSeconds { get; set; } = 1_000_000;
        }

        // 各档一个假字,稀有度由 RarityOf 查
        private static readonly Dictionary<string, CardRarity> Rarity = new()
        {
            ["白甲"] = CardRarity.White, ["绿甲"] = CardRarity.Green, ["绿乙"] = CardRarity.Green,
            ["蓝甲"] = CardRarity.Blue, ["紫甲"] = CardRarity.Purple, ["紫乙"] = CardRarity.Purple,
            ["金甲"] = CardRarity.Gold,
        };

        private static CardRarity RarityOf(string id) => Rarity[id];
        private static readonly string[] AllCards = Rarity.Keys.ToArray();

        /// <summary>到 Lv L 的累计经验:100(L−1) + 25(L−1)(L−2)(Meta.CharacterLevel 的曲线)。</summary>
        private static int XpFor(int level) => 100 * (level - 1) + 25 * (level - 1) * (level - 2);

        private static MetaState Meta(int level = 1, params string[] owned)
        {
            var meta = new MetaState { CharacterXp = XpFor(level), Ink = 10_000 };
            meta.OwnedCards.Clear();
            meta.OwnedCards.AddRange(owned);
            return meta;
        }

        // ---- 打包张数与定价 ----

        [TestCase(CardRarity.White, 20, 200)]
        [TestCase(CardRarity.Green, 10, 240)]
        [TestCase(CardRarity.Blue, 5, 250)]
        [TestCase(CardRarity.Purple, 2, 240)]
        [TestCase(CardRarity.Gold, 1, 260)]
        [TestCase(CardRarity.Orange, 1, 400)]
        [TestCase(CardRarity.Red, 1, 600)]
        public void Bundle_SizeAndPrice_ByRarity(CardRarity rarity, int size, int price)
        {
            Assert.That(ShopRules.BundleSizeFor(rarity), Is.EqualTo(size));
            Assert.That(ShopRules.BundlePriceFor(rarity), Is.EqualTo(price));
        }

        [Test]
        public void Bundle_PerCopyPrice_RisesWithRarity()
        {
            // 每份价不单调(紫 240 < 蓝 250)是刻意的 —— 单调的是**单张价**
            for (var r = CardRarity.White; r < CardRarity.Red; r++)
            {
                double here = (double)ShopRules.BundlePriceFor(r) / ShopRules.BundleSizeFor(r);
                double next = (double)ShopRules.BundlePriceFor(r + 1) / ShopRules.BundleSizeFor(r + 1);
                Assert.That(next, Is.GreaterThan(here), $"{r + 1} 单张价应高于 {r}");
            }
        }

        [Test]
        public void BuyCard_GrantsTheWholeBundle()
        {
            var meta = Meta(1, "绿甲");
            ShopRules.EnsureShelf(meta, new[] { "绿甲" }, new FakeTime(), new GameRandom(1));
            int ink = meta.Ink;
            Assert.That(ShopRules.TryBuyCard(meta, 0, CardRarity.Green), Is.True);
            Assert.That(meta.CardCopies["绿甲"], Is.EqualTo(10), "绿卡 10 张一份");
            Assert.That(ink - meta.Ink, Is.EqualTo(240));
        }

        [Test]
        public void BuyCard_Unowned_FirstCopyUnlocks_RestBecomeCopies()
        {
            // 货架只摆已拥有的字,这条守 TryBuyCard 自身的记账:第一张是「解锁」,其余才是重复卡
            var meta = Meta(1);
            ShopRules.EnsureShelf(meta, new[] { "蓝甲" }, new FakeTime(), new GameRandom(1));
            Assert.That(ShopRules.TryBuyCard(meta, 0, CardRarity.Blue), Is.True);
            Assert.That(meta.OwnedCards.Contains("蓝甲"), Is.True);
            Assert.That(meta.CardCopies["蓝甲"], Is.EqualTo(4), "5 张 = 1 张解锁 + 4 张重复卡");
        }

        [Test]
        public void BuyCard_NotEnoughInk_ChangesNothing()
        {
            var meta = Meta(1, "绿甲");
            meta.Ink = 239;
            ShopRules.EnsureShelf(meta, new[] { "绿甲" }, new FakeTime(), new GameRandom(1));
            Assert.That(ShopRules.TryBuyCard(meta, 0, CardRarity.Green), Is.False);
            Assert.That(meta.CardCopies.ContainsKey("绿甲"), Is.False);
            Assert.That(meta.Ink, Is.EqualTo(239));
        }

        // ---- 槽位随等级解锁 ----

        [TestCase(1, 4)]
        [TestCase(7, 4)]
        [TestCase(8, 5)]
        [TestCase(14, 5)]
        [TestCase(15, 6)]
        [TestCase(25, 7)]
        [TestCase(34, 7)]
        [TestCase(35, 8)]
        [TestCase(200, 8)]
        public void SlotCount_UnlocksByLevel(int level, int slots)
        {
            Assert.That(ShopRules.SlotCountFor(level), Is.EqualTo(slots));
        }

        [Test]
        public void UnlockLevelForSlot_AgreesWithSlotCount()
        {
            for (int slot = 0; slot < ShopRules.MaxCardSlots; slot++)
            {
                int level = ShopRules.UnlockLevelForSlot(slot);
                Assert.That(ShopRules.SlotCountFor(level), Is.GreaterThan(slot), $"槽 {slot} 该在 Lv{level} 开");
                if (level > 1)
                    Assert.That(ShopRules.SlotCountFor(level - 1), Is.LessThanOrEqualTo(slot), $"槽 {slot} 报晚了");
            }
        }

        [Test]
        public void EnsureShelf_RollsAsManySlotsAsTheLevelAllows()
        {
            var meta = Meta(25, "白甲");
            ShopRules.EnsureShelf(meta, new[] { "白甲" }, new FakeTime(), new GameRandom(1));
            Assert.That(meta.Shop.CardSlots.Count, Is.EqualTo(7));
            Assert.That(meta.Shop.CardSold.Count, Is.EqualTo(7));
        }

        [Test]
        public void EnsureShelf_SameDayLevelUp_TopsUpWithoutRerolling()
        {
            // 当天升级跨过解锁线:新格当场补上,已摆的四格不动(玩家可能已经买了其中一格)
            var time = new FakeTime();
            var meta = Meta(7, "白甲", "绿甲");
            ShopRules.EnsureShelf(meta, new[] { "白甲", "绿甲" }, time, new GameRandom(1));
            ShopRules.TryBuyCard(meta, 0, RarityOf(meta.Shop.CardSlots[0]));
            var before = meta.Shop.CardSlots.ToList();

            meta.CharacterXp = XpFor(8);
            Assert.That(ShopRules.EnsureShelf(meta, new[] { "白甲", "绿甲" }, time, new GameRandom(2)), Is.True,
                "补了一格,货架有变化,调用方要存盘");
            Assert.That(meta.Shop.CardSlots.Count, Is.EqualTo(5));
            Assert.That(meta.Shop.CardSlots.Take(4), Is.EqualTo(before), "已摆的不重掷");
            Assert.That(meta.Shop.CardSold[0], Is.True, "已售标记不复位");
            Assert.That(meta.Shop.CardSold[4], Is.False);
        }

        // ---- 看广告领字卡:绿 ×10 / 蓝 ×5 / 紫 ×1 ----

        private static MetaState WithOffers(int seed = 1, params string[] owned)
        {
            var meta = Meta(1, owned.Length > 0 ? owned : new[] { "绿甲", "绿乙", "蓝甲" });
            ShopRules.EnsureShelf(meta, meta.OwnedCards.ToArray(), new FakeTime(), new GameRandom(seed),
                RarityOf, AllCards);
            return meta;
        }

        [Test]
        public void AdOffers_OnePerTier_GreenBlueFromOwned_PurpleFromAll()
        {
            for (int seed = 1; seed <= 20; seed++)
            {
                var meta = WithOffers(seed);
                Assert.That(meta.Shop.AdOffers.Count, Is.EqualTo(ShopRules.AdOfferCount));
                Assert.That(RarityOf(meta.Shop.AdOffers[0]), Is.EqualTo(CardRarity.Green));
                Assert.That(meta.OwnedCards.Contains(meta.Shop.AdOffers[0]), Is.True, "绿档只出已拥有的字");
                Assert.That(meta.Shop.AdOffers[1], Is.EqualTo("蓝甲"), "蓝档只出已拥有的字(只有蓝甲)");
                Assert.That(RarityOf(meta.Shop.AdOffers[2]), Is.EqualTo(CardRarity.Purple));
                Assert.That(meta.OwnedCards.Contains(meta.Shop.AdOffers[2]), Is.False,
                    "紫档不要求已拥有(这组夹具一张紫都没有)");
            }
        }

        [Test]
        public void AdOffer_Green_GrantsTen()
        {
            var meta = WithOffers();
            string card = meta.Shop.AdOffers[0];
            Assert.That(ShopRules.TryClaimCardAd(meta, 0), Is.True);
            Assert.That(meta.CardCopies[card], Is.EqualTo(10));
            Assert.That(ShopRules.TryClaimCardAd(meta, 0), Is.False, "每日一次");
        }

        [Test]
        public void AdOffer_Blue_GrantsFive()
        {
            var meta = WithOffers();
            Assert.That(ShopRules.TryClaimCardAd(meta, 1), Is.True);
            Assert.That(meta.CardCopies["蓝甲"], Is.EqualTo(5));
        }

        [Test]
        public void AdOffer_Purple_UnlocksAnUnownedCard()
        {
            var meta = WithOffers();
            string card = meta.Shop.AdOffers[2];
            int inkBefore = meta.Ink;
            Assert.That(ShopRules.TryClaimCardAd(meta, 2), Is.True);
            Assert.That(meta.OwnedCards.Contains(card), Is.True, "紫档 1 张,没有就直接解锁");
            Assert.That(meta.UnseenCards.Contains(card), Is.True, "新解锁的字照常标新");
            Assert.That(meta.CardCopies.ContainsKey(card), Is.False, "只有这 1 张,解锁即用掉");
            Assert.That(meta.Ink, Is.EqualTo(inkBefore), "广告位不收墨锭");
        }

        [Test]
        public void AdOffer_NoOwnedCardOfThatTier_IsEmptyAndUnclaimable()
        {
            var meta = WithOffers(1, "蓝甲");   // 一张绿都没有
            Assert.That(meta.Shop.AdOffers[0], Is.EqualTo(""));
            Assert.That(ShopRules.TryClaimCardAd(meta, 0), Is.False);
        }

        [Test]
        public void AdRefresh_RerollsUnclaimedOffers_KeepsClaimedOnes()
        {
            var meta = WithOffers();
            ShopRules.TryClaimCardAd(meta, 2);
            string claimedPurple = meta.Shop.AdOffers[2];
            Assert.That(ShopRules.TryAdRefresh(meta, meta.OwnedCards.ToArray(), new GameRandom(5),
                RarityOf, AllCards), Is.True);
            Assert.That(meta.Shop.AdOffers[2], Is.EqualTo(claimedPurple), "领过的不重掷,也不因此又能领一次");
            Assert.That(meta.Shop.AdOfferClaimed[2], Is.True);
            Assert.That(ShopRules.TryClaimCardAd(meta, 2), Is.False);
        }

        [Test]
        public void EnsureShelf_NextDay_ResetsOfferClaims()
        {
            var time = new FakeTime();
            var meta = Meta(1, "绿甲", "蓝甲");
            ShopRules.EnsureShelf(meta, meta.OwnedCards.ToArray(), time, new GameRandom(1), RarityOf, AllCards);
            ShopRules.TryClaimCardAd(meta, 0);
            time.NowUnixSeconds += 86400;
            ShopRules.EnsureShelf(meta, meta.OwnedCards.ToArray(), time, new GameRandom(2), RarityOf, AllCards);
            Assert.That(meta.Shop.AdOfferClaimed.All(c => !c), Is.True);
        }

        [Test]
        public void RedDot_OnWhileACardAdIsUnclaimed()
        {
            var time = new FakeTime();
            var meta = Meta(1, "绿甲", "蓝甲");
            ShopRules.EnsureShelf(meta, meta.OwnedCards.ToArray(), time, new GameRandom(1), RarityOf, AllCards);
            ShopRules.TryClaimInkAd(meta);
            ShopRules.TryAdRefresh(meta, meta.OwnedCards.ToArray(), new GameRandom(2), RarityOf, AllCards);
            ShopRules.MarkVisited(meta, time);
            Assert.That(ShopRules.HasRedDot(meta, time), Is.True, "三个字卡广告位还没领");
            for (int i = 0; i < ShopRules.AdOfferCount; i++) ShopRules.TryClaimCardAd(meta, i);
            Assert.That(ShopRules.HasRedDot(meta, time), Is.False);
        }
    }
}
