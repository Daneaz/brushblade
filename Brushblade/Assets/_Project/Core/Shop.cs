using System;
using System.Collections.Generic;

namespace Brushblade.Core
{
    /// <summary>每日商城货架状态(19.6,存档持久)。</summary>
    public sealed class ShopState
    {
        public long DayStamp { get; set; } = -1;            // 货架所属 UTC 日
        public List<string> CardSlots { get; set; } = new(); // 卡位:4–8 格,随角色等级解锁(2026-09-30)
        public List<bool> CardSold { get; set; } = new();
        public List<int> CardDiscountRoll { get; set; } = new(); // 与 CardSlots 一一对应的折扣掷点(见 ShopRules.DiscountPercent)
        /// <summary>看广告领字卡(2026-09-30):下标 0/1/2 = 绿 / 蓝 / 紫三档,""= 这一档今天没得出。</summary>
        public List<string> AdOffers { get; set; } = new();
        public List<bool> AdOfferClaimed { get; set; } = new();
        public ChestTier ChestSlot { get; set; }
        public bool ChestSold { get; set; }
        public int ChestDiscountRoll { get; set; } = -1;     // 宝箱位折扣掷点;−1 = 旧存档未掷
        public bool InkAdClaimed { get; set; }               // 墨锭广告位(每日一次)
        public int InkAdAmount { get; set; }                 // 墨锭广告位本期档额(0 = 旧存档未掷,见 EnsureShelf)
        public bool AdRefreshUsed { get; set; }              // 广告刷新(每日一次)
        public long VisitedDayStamp { get; set; } = -1;      // 最后一次进商城的 UTC 日(主界面红点用)
    }

    /// <summary>每日商城规则(19.6 首版基准)。货架卡池由调用方按已解锁章节合成(F3)。</summary>
    public static class ShopRules
    {
        // ---- 墨锭广告位按档随机(2026-10-01 用户拍板):30~400 ----
        // 档位走宝箱同一张「按角色等级」权重表(ChestRules.TierWeightsFor),一档一额;
        // 刷新时未领的那一档跟着重掷,已领的不动。
        public static readonly int[] InkAdAmounts = { 30, 50, 80, 120, 200, 300, 400 };

        // ---- 字摊按稀有度打包卖(2026-09-30 用户拍板)----
        //
        // 升级的瓶颈是张数不是墨锭(白卡 1→10 共要 1081 张),一张一卖后期根本买不动。
        // 锚点是商城紫檀箱:400 墨锭开 8 张随机卡外加返还 120,折合约 35 墨锭一张随机稀有度的卡。
        // 白→紫每份价落在 200–260 的同一带 —— 每一格都是「值得掂量一下」的一笔;低档打折更深,
        // 因为低档升级要的张数最多。金及以上一张原价(19.6 旧表的 260/400/600)。
        // 单张价 10 / 24 / 50 / 120 / 260 / 400 / 600 严格递增(ShopBundleTests 钉着);
        // 每份价不单调(紫 240 < 蓝 250)是刻意的。
        private static readonly int[] BundleSizes = { 20, 10, 5, 2, 1, 1, 1 };
        private static readonly int[] BundlePrices = { 200, 240, 250, 240, 260, 400, 600 };

        /// <summary>一份几张(索引 = 稀有度)。</summary>
        public static int BundleSizeFor(CardRarity rarity) => BundleSizes[(int)rarity - 1];

        /// <summary>一份的价格。</summary>
        public static int BundlePriceFor(CardRarity rarity) => BundlePrices[(int)rarity - 1];

        // ---- 货架槽位随角色等级解锁(2026-09-30):Lv1/8/15/25/35/45 → 4/5/6/7/8/9 格 ----
        // 9 格 + 3 个字卡广告位 = 货架 2 行 × 6 列铺满左侧(2026-09-30 用户要求字卡广告位并入字摊)
        public const int MaxCardSlots = 9;
        private static readonly int[] SlotUnlockLevel = { 1, 1, 1, 1, 8, 15, 25, 35, 45 };

        public static int SlotCountFor(int level)
        {
            int count = 0;
            foreach (int unlock in SlotUnlockLevel) if (level >= unlock) count++;
            return count;
        }

        /// <summary>第 slot 格在几级解锁(表现层在锁着的格子上印它)。</summary>
        public static int UnlockLevelForSlot(int slot) =>
            SlotUnlockLevel[Math.Clamp(slot, 0, SlotUnlockLevel.Length - 1)];

        // ---- 看广告领字卡(2026-09-30):绿 ×10 / 蓝 ×5 / 紫 ×1,每档每日一次 ----
        // 绿/蓝只出**已拥有**的字(是升级材料);紫档 1 张且**不要求已拥有** —— 没有就直接解锁,
        // 是除宝箱之外唯一一条拿新字的路。
        public const int AdOfferCount = 3;
        public static readonly CardRarity[] AdOfferRarity = { CardRarity.Green, CardRarity.Blue, CardRarity.Purple };
        public static readonly int[] AdOfferCards = { 10, 5, 1 };
        private static readonly bool[] AdOfferNeedsOwned = { true, true, false };

        /// <summary>宝箱位底价(索引 = tier−1;2026-10-01)= 该箱开出的张数按字摊单张价(每份价 ÷ 每份张数)折算,
        /// 取整到十位(ShopDiscountTests 对账)。实付 = 底价 × 当日折扣。</summary>
        public static readonly int[] ChestBasePrice = { 230, 460, 700, 1180, 2850, 3850, 4870 };

        // ---- 每日随机打折(2026-10-01 用户拍板)----
        // 字摊每份与宝箱位各自掷一档:常规 3~7 折,橙/红字与朱漆/赤霄宝箱 6~8 折。整折为一档。
        // 存的是掷点而不是折扣本身:字摊格的稀有度要查字表(Core 这里拿不到),
        // 掷点 → 折扣的映射推迟到知道稀有度的那一刻,且同一掷点永远映射到同一折。
        private static readonly int[] RegularDiscounts = { 30, 40, 50, 60, 70 };
        private static readonly int[] PremiumDiscounts = { 60, 70, 80 };
        private const int DiscountRollRange = 60; // 5 与 3 的公倍数:两张表在掷点上都均匀

        public static int DiscountPercent(bool premium, int roll)
        {
            var table = premium ? PremiumDiscounts : RegularDiscounts;
            return table[Math.Abs(roll) % table.Length];
        }

        public static bool IsPremium(CardRarity rarity) => rarity >= CardRarity.Orange;

        public static bool IsPremium(ChestTier tier) => tier >= ChestTier.Vermilion;

        /// <summary>字摊第 slot 格今日实付(按该格字的稀有度)。</summary>
        public static int CardPrice(MetaState meta, int slot, CardRarity rarity)
        {
            var rolls = meta.Shop.CardDiscountRoll;
            int roll = slot < rolls.Count ? rolls[slot] : 0;
            return BundlePriceFor(rarity) * DiscountPercent(IsPremium(rarity), roll) / 100;
        }

        /// <summary>宝箱位今日实付。</summary>
        public static int ChestPrice(MetaState meta)
        {
            var tier = meta.Shop.ChestSlot;
            return ChestBasePrice[(int)tier - 1]
                * DiscountPercent(IsPremium(tier), Math.Max(0, meta.Shop.ChestDiscountRoll)) / 100;
        }

        /// <summary>确保货架是今日的:跨日则重掷(卡位/宝箱位/字卡广告位/各每日标记复位)。
        /// 同日内升级跨过槽位解锁线,则**只补新格**,已摆的不动(玩家可能已经买了其中一格)。
        /// 返回货架有没有变化(调用方据此存盘)。
        ///
        /// rarityOf / allCards 给字卡广告位用:rarityOf 查稀有度,allCards = 紫档的候选(全部可收集字)。
        /// 不传(老调用方与只测货架的用例)则不摆字卡广告位。</summary>
        public static bool EnsureShelf(MetaState meta, IReadOnlyList<string> unlockedPool,
            ITimeSource time, GameRandom random,
            Func<string, CardRarity> rarityOf = null, IReadOnlyList<string> allCards = null)
        {
            long today = time.NowUnixSeconds / 86400;
            if (meta.Shop.DayStamp == today)
            {
                bool changed = false;
                // 版本更新当天:旧货架没有折扣掷点,补掷一次。必须在 TopUpSlots 之前补 ——
                // 后者给新格追加掷点,先补格后补点会让掷点与格错位
                while (meta.Shop.CardDiscountRoll.Count < meta.Shop.CardSlots.Count)
                {
                    meta.Shop.CardDiscountRoll.Add(random.Next(DiscountRollRange));
                    changed = true;
                }
                changed |= TopUpSlots(meta, unlockedPool, random);
                if (meta.Shop.ChestDiscountRoll < 0)
                {
                    meta.Shop.ChestDiscountRoll = random.Next(DiscountRollRange);
                    changed = true;
                }
                // 版本更新当天:旧货架没有墨锭档,补掷一次
                if (meta.Shop.InkAdAmount <= 0)
                {
                    RollInkAd(meta, random);
                    changed = true;
                }
                // 版本更新当天:旧货架没有字卡广告位,补摆一次(不必等到明天)
                if (meta.Shop.AdOffers.Count == 0 && rarityOf != null)
                {
                    RollAdOffers(meta, unlockedPool, random, rarityOf, allCards, keepClaimed: false);
                    changed = true;
                }
                return changed;
            }

            meta.Shop.DayStamp = today;
            meta.Shop.InkAdClaimed = false;
            meta.Shop.AdRefreshUsed = false;
            RollShelf(meta, unlockedPool, random);
            if (rarityOf != null)
                RollAdOffers(meta, unlockedPool, random, rarityOf, allCards, keepClaimed: false);
            RollInkAd(meta, random);
            return true;
        }

        private static void RollInkAd(MetaState meta, GameRandom random)
        {
            var tier = ChestRules.RollTier(MetaRules.CharacterLevel(meta.CharacterXp), random);
            meta.Shop.InkAdAmount = InkAdAmounts[(int)tier - 1];
        }

        private static void RollShelf(MetaState meta, IReadOnlyList<string> unlockedPool, GameRandom random)
        {
            meta.Shop.CardSlots.Clear();
            meta.Shop.CardSold.Clear();
            meta.Shop.CardDiscountRoll.Clear();
            TopUpSlots(meta, unlockedPool, random);
            meta.Shop.ChestSlot = ChestRules.RollTier(
                MetaRules.CharacterLevel(meta.CharacterXp), random);
            meta.Shop.ChestSold = false;
            meta.Shop.ChestDiscountRoll = random.Next(DiscountRollRange);
        }

        /// <summary>把卡位补到当前等级允许的格数;返回有没有补。</summary>
        private static bool TopUpSlots(MetaState meta, IReadOnlyList<string> unlockedPool, GameRandom random)
        {
            int target = SlotCountFor(MetaRules.CharacterLevel(meta.CharacterXp));
            bool added = false;
            while (meta.Shop.CardSlots.Count < target && unlockedPool.Count > 0)
            {
                meta.Shop.CardSlots.Add(random.Pick(unlockedPool));
                meta.Shop.CardSold.Add(false);
                meta.Shop.CardDiscountRoll.Add(random.Next(DiscountRollRange));
                added = true;
            }
            return added;
        }

        /// <summary>摆三档字卡广告位。keepClaimed:广告刷新时领过的那档原样保留(不重掷、也不复位)。</summary>
        private static void RollAdOffers(MetaState meta, IReadOnlyList<string> unlockedPool, GameRandom random,
            Func<string, CardRarity> rarityOf, IReadOnlyList<string> allCards, bool keepClaimed)
        {
            var shop = meta.Shop;
            while (shop.AdOffers.Count < AdOfferCount) shop.AdOffers.Add("");
            while (shop.AdOfferClaimed.Count < AdOfferCount) shop.AdOfferClaimed.Add(false);
            for (int tier = 0; tier < AdOfferCount; tier++)
            {
                if (keepClaimed && shop.AdOfferClaimed[tier]) continue;
                var source = AdOfferNeedsOwned[tier] ? unlockedPool : (allCards ?? unlockedPool);
                var candidates = new List<string>();
                foreach (var id in source)
                    if (rarityOf(id) == AdOfferRarity[tier] && !candidates.Contains(id)) candidates.Add(id);
                shop.AdOffers[tier] = candidates.Count > 0 ? random.Pick(candidates) : "";
                shop.AdOfferClaimed[tier] = false;
            }
        }

        /// <summary>记下「今天来过商城了」(主界面红点用)。进商城时调用,同日重复调用无副作用。</summary>
        public static void MarkVisited(MetaState meta, ITimeSource time) =>
            meta.Shop.VisitedDayStamp = time.NowUnixSeconds / 86400;

        /// <summary>主界面商城页签要不要亮红点(2026-08-28 拍板):
        /// **今日刷新后还没进过**,或者**还有没用完的广告位**。
        ///
        /// ⚠ 「今日还没来过」这一条不能省。跨日之后 <see cref="EnsureShelf"/> 要等玩家真进商城
        /// 才跑,在那之前两个广告标记仍留着昨天的 true —— 只看广告标记的话,新一天的货架
        /// 摆好了红点却是灭的。</summary>
        public static bool HasRedDot(MetaState meta, ITimeSource time) =>
            meta.Shop.VisitedDayStamp != time.NowUnixSeconds / 86400
            || !meta.Shop.InkAdClaimed
            || !meta.Shop.AdRefreshUsed
            || HasUnclaimedCardAd(meta);

        private static bool HasUnclaimedCardAd(MetaState meta)
        {
            for (int i = 0; i < meta.Shop.AdOffers.Count; i++)
                if (meta.Shop.AdOffers[i] != "" && !(i < meta.Shop.AdOfferClaimed.Count && meta.Shop.AdOfferClaimed[i]))
                    return true;
            return false;
        }

        /// <summary>购卡:未售出且墨锭足够 → 扣一份的当日折后价、收下一整份(张数按稀有度)、标记已售。</summary>
        public static bool TryBuyCard(MetaState meta, int slotIndex, CardRarity rarity = CardRarity.White)
        {
            int price = CardPrice(meta, slotIndex, rarity);
            if (meta.Shop.CardSold[slotIndex] || meta.Ink < price)
                return false;
            MetaRules.SpendInk(meta, price);
            Grant(meta, meta.Shop.CardSlots[slotIndex], BundleSizeFor(rarity));
            meta.Shop.CardSold[slotIndex] = true;
            return true;
        }

        /// <summary>看广告领字卡(tier 0/1/2 = 绿/蓝/紫):这一档今天有货且没领过 → 收下 N 张。</summary>
        public static bool TryClaimCardAd(MetaState meta, int tier)
        {
            var shop = meta.Shop;
            if (tier < 0 || tier >= shop.AdOffers.Count || shop.AdOffers[tier] == "") return false;
            if (tier < shop.AdOfferClaimed.Count && shop.AdOfferClaimed[tier]) return false;
            Grant(meta, shop.AdOffers[tier], AdOfferCards[tier]);
            shop.AdOfferClaimed[tier] = true;
            return true;
        }

        /// <summary>收下 count 张:第一张走 AcquireCard(未拥有 = 解锁 + 标新),其余是重复卡。</summary>
        private static void Grant(MetaState meta, string cardId, int count)
        {
            MetaRules.AcquireCard(meta, cardId);
            if (count > 1) MetaRules.AddCardCopies(meta, cardId, count - 1);
        }

        /// <summary>购宝箱:未售出、墨锭足够且箱位有空 → 扣费、掉入箱位(卡池 = 已解锁章节池)。</summary>
        public static bool TryBuyChest(MetaState meta, IReadOnlyList<string> unlockedPool, ITimeSource time)
        {
            int price = ChestPrice(meta);
            if (meta.Shop.ChestSold || meta.Ink < price || meta.Chests.Count >= ChestRules.SlotLimit)
                return false;
            if (!ChestRules.TryAwardChest(meta, meta.Shop.ChestSlot, unlockedPool, time))
                return false;
            MetaRules.SpendInk(meta, price);
            meta.Shop.ChestSold = true;
            return true;
        }

        /// <summary>墨锭广告位:每日一次,领本期掷出的 Shop.InkAdAmount。</summary>
        public static bool TryClaimInkAd(MetaState meta)
        {
            if (meta.Shop.InkAdClaimed)
                return false;
            MetaRules.GainInk(meta, meta.Shop.InkAdAmount);
            meta.Shop.InkAdClaimed = true;
            return true;
        }

        /// <summary>广告刷新:每日一次,重掷卡位、宝箱位与**没领过的**字卡广告位(墨锭广告位不复位)。</summary>
        public static bool TryAdRefresh(MetaState meta, IReadOnlyList<string> unlockedPool, GameRandom random,
            Func<string, CardRarity> rarityOf = null, IReadOnlyList<string> allCards = null)
        {
            if (meta.Shop.AdRefreshUsed)
                return false;
            RollShelf(meta, unlockedPool, random);
            if (rarityOf != null)
                RollAdOffers(meta, unlockedPool, random, rarityOf, allCards, keepClaimed: true);
            if (!meta.Shop.InkAdClaimed) RollInkAd(meta, random);
            meta.Shop.AdRefreshUsed = true;
            return true;
        }
    }
}
