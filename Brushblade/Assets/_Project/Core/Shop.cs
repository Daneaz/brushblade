using System;
using System.Collections.Generic;

namespace Brushblade.Core
{
    /// <summary>每日商城货架状态(19.6,存档持久)。</summary>
    public sealed class ShopState
    {
        public long DayStamp { get; set; } = -1;            // 货架所属 UTC 日
        public List<string> CardSlots { get; set; } = new(); // 卡位 ×4
        public List<bool> CardSold { get; set; } = new();
        public ChestTier ChestSlot { get; set; }
        public bool ChestSold { get; set; }
        public bool InkAdClaimed { get; set; }               // 墨锭广告位(每日一次)
        public bool AdRefreshUsed { get; set; }              // 广告刷新(每日一次)
    }

    /// <summary>每日商城规则(19.6 首版基准)。货架卡池由调用方按已解锁章节合成(F3)。</summary>
    public static class ShopRules
    {
        public const int CardSlotCount = 4;
        public const int CardPrice = 40;          // 白卡基准价
        public const int InkAdAmount = 30;        // 墨锭广告位领取量

        private static readonly int[] CardPrices = { 40, 60, 100, 160, 260, 400 };

        /// <summary>卡价按稀有度分档(19.6)。</summary>
        public static int CardPriceFor(CardRarity rarity) => CardPrices[(int)rarity - 1];

        /// <summary>宝箱位价格(索引 = tier−1)。</summary>
        public static readonly int[] ChestPrice = { 30, 80, 200, 400, 800, 1500 };

        /// <summary>确保货架是今日的:跨日则重掷(卡位/宝箱位/各每日标记复位)。返回是否发生了重掷。</summary>
        public static bool EnsureShelf(MetaState meta, IReadOnlyList<string> unlockedPool,
            ITimeSource time, GameRandom random)
        {
            long today = time.NowUnixSeconds / 86400;
            if (meta.Shop.DayStamp == today)
                return false;

            meta.Shop.DayStamp = today;
            meta.Shop.InkAdClaimed = false;
            meta.Shop.AdRefreshUsed = false;
            RollShelf(meta, unlockedPool, random);
            return true;
        }

        private static void RollShelf(MetaState meta, IReadOnlyList<string> unlockedPool, GameRandom random)
        {
            meta.Shop.CardSlots.Clear();
            meta.Shop.CardSold.Clear();
            for (int i = 0; i < CardSlotCount && unlockedPool.Count > 0; i++)
            {
                meta.Shop.CardSlots.Add(random.Pick(unlockedPool));
                meta.Shop.CardSold.Add(false);
            }
            meta.Shop.ChestSlot = ChestRules.RollTier(
                MetaRules.CharacterLevel(meta.CharacterXp), random);
            meta.Shop.ChestSold = false;
        }

        /// <summary>购卡:未售出且墨锭足够 → 扣费(按稀有度)、入收集、标记已售。</summary>
        public static bool TryBuyCard(MetaState meta, int slotIndex, CardRarity rarity = CardRarity.White)
        {
            int price = CardPriceFor(rarity);
            if (meta.Shop.CardSold[slotIndex] || meta.Ink < price)
                return false;
            meta.Ink -= price;
            MetaRules.AcquireCard(meta, meta.Shop.CardSlots[slotIndex]);
            meta.Shop.CardSold[slotIndex] = true;
            return true;
        }

        /// <summary>购宝箱:未售出、墨锭足够且箱位有空 → 扣费、掉入箱位(卡池 = 已解锁章节池)。</summary>
        public static bool TryBuyChest(MetaState meta, IReadOnlyList<string> unlockedPool, ITimeSource time)
        {
            int price = ChestPrice[(int)meta.Shop.ChestSlot - 1];
            if (meta.Shop.ChestSold || meta.Ink < price || meta.Chests.Count >= ChestRules.SlotLimit)
                return false;
            if (!ChestRules.TryAwardChest(meta, meta.Shop.ChestSlot, unlockedPool, time))
                return false;
            meta.Ink -= price;
            meta.Shop.ChestSold = true;
            return true;
        }

        /// <summary>墨锭广告位:每日一次,领 InkAdAmount。</summary>
        public static bool TryClaimInkAd(MetaState meta)
        {
            if (meta.Shop.InkAdClaimed)
                return false;
            meta.Ink += InkAdAmount;
            meta.Shop.InkAdClaimed = true;
            return true;
        }

        /// <summary>广告刷新:每日一次,重掷卡位与宝箱位(墨锭广告位不复位)。</summary>
        public static bool TryAdRefresh(MetaState meta, IReadOnlyList<string> unlockedPool, GameRandom random)
        {
            if (meta.Shop.AdRefreshUsed)
                return false;
            RollShelf(meta, unlockedPool, random);
            meta.Shop.AdRefreshUsed = true;
            return true;
        }
    }
}
