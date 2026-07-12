using System;
using System.Collections.Generic;

namespace Brushblade.Core
{
    /// <summary>局外养成状态(第 19 章):角色经验/墨锭/卡等级/关卡进度。可序列化为存档。</summary>
    public sealed class MetaState
    {
        public int CharacterXp { get; set; }
        public int Ink { get; set; }                                    // 墨锭
        public Dictionary<string, int> CardLevels { get; set; } = new();  // 缺省 1 级
        public Dictionary<string, int> CardCopies { get; set; } = new();  // 待消耗重复卡
        public List<string> OwnedCards { get; set; } = new();             // 收集(首次获得即入)
        public List<string> Deck { get; set; } = new();                   // 出阵卡组(≤4,19.3.4)
        public List<int> ClearedStages { get; set; } = new();             // 每章已通关数
        public List<ChestState> Chests { get; set; } = new();             // 箱位队列(≤4,19.5.2)
        public ShopState Shop { get; set; } = new();                      // 每日商城(19.6)
    }

    /// <summary>养成规则(19.2/19.3 首版基准)。纯函数,状态进出。</summary>
    public static class MetaRules
    {
        public const int MaxCardLevel = 10;
        public const int DeckLimit = 4; // 出阵卡组上限(19.3.4)

        /// <summary>集卡升级需求(升到下一级所需同名卡,白卡基准,19.3.3)。索引 = 当前等级 − 1。</summary>
        public static readonly int[] CopiesToUpgrade = { 2, 4, 10, 20, 40, 80, 150, 300, 500 };

        /// <summary>升级墨锭成本(白卡基准)。索引 = 当前等级 − 1。</summary>
        public static readonly int[] InkToUpgrade = { 20, 50, 120, 300, 700, 1500, 3000, 6000, 12000 };

        // 稀有度成本系数(索引 = rarity−1,白→红;19.3.3:越稀有需卡越少、墨锭越贵)
        private static readonly double[] CopiesMultiplier = { 1.0, 0.7, 0.4, 0.25, 0.15, 0.1 };
        private static readonly double[] InkMultiplier = { 1.0, 1.5, 2.0, 3.0, 4.0, 5.0 };

        /// <summary>升到下一级所需同名卡(按稀有度分档:越稀有越少,向上取整,最少 1)。</summary>
        public static int CopiesRequired(int currentLevel, CardRarity rarity)
            => Math.Max(1, (int)Math.Ceiling(
                CopiesToUpgrade[currentLevel - 1] * CopiesMultiplier[(int)rarity - 1]));

        /// <summary>升到下一级所需墨锭(按稀有度分档:越稀有越贵)。</summary>
        public static int InkRequired(int currentLevel, CardRarity rarity)
            => (int)(InkToUpgrade[currentLevel - 1] * InkMultiplier[(int)rarity - 1]);

        /// <summary>角色等级:升到 n+1 级需 100 + 50×(n−1) 经验(19.2.1)。</summary>
        public static int CharacterLevel(int xp)
        {
            int level = 1;
            int cost = 100;
            while (xp >= cost)
            {
                xp -= cost;
                level += 1;
                cost += 50;
            }
            return level;
        }

        /// <summary>生命成长:50 + 2×(等级−1),上限 100。</summary>
        public static int MaxHpFor(int level) => Math.Min(100, 50 + 2 * (level - 1));

        /// <summary>关卡解锁:章内顺序解锁;下一章需上一章全通。</summary>
        public static bool IsStageUnlocked(MetaState meta, CampaignConfig campaign, int chapter, int stage)
        {
            for (int c = 0; c < chapter; c++)
                if (ClearedIn(meta, c) < campaign.Chapters[c].Stages.Count)
                    return false;
            return stage <= ClearedIn(meta, chapter);
        }

        /// <summary>通关结算:首通 +50 经验并推进进度,重复 +10。返回是否首通。</summary>
        public static bool ApplyStageCleared(MetaState meta, int chapter, int stage)
        {
            bool firstClear = stage == ClearedIn(meta, chapter);
            if (firstClear)
            {
                while (meta.ClearedStages.Count <= chapter)
                    meta.ClearedStages.Add(0);
                meta.ClearedStages[chapter] += 1;
            }
            meta.CharacterXp += firstClear ? 50 : 10;
            return firstClear;
        }

        private static int ClearedIn(MetaState meta, int chapter) =>
            chapter < meta.ClearedStages.Count ? meta.ClearedStages[chapter] : 0;

        /// <summary>卡等级(缺省 1)。</summary>
        public static int CardLevel(MetaState meta, string cardId) =>
            meta.CardLevels.TryGetValue(cardId, out var level) ? level : 1;

        public static void AddCardCopies(MetaState meta, string cardId, int count)
        {
            meta.CardCopies.TryGetValue(cardId, out var current);
            meta.CardCopies[cardId] = current + count;
        }

        /// <summary>纯判定:当前重复卡与墨锭是否足以升级(UI 红点/排序用),不动状态。</summary>
        public static bool CanUpgradeCard(MetaState meta, string cardId, CardRarity rarity = CardRarity.White)
        {
            int level = CardLevel(meta, cardId);
            meta.CardCopies.TryGetValue(cardId, out var copies);
            return level < MaxCardLevel
                && copies >= CopiesRequired(level, rarity)
                && meta.Ink >= InkRequired(level, rarity);
        }

        /// <summary>集满 + 墨锭足够 → 消耗并升 1 级;否则返回 false 不动状态。成本按稀有度分档。</summary>
        public static bool TryUpgradeCard(MetaState meta, string cardId, CardRarity rarity = CardRarity.White)
        {
            int level = CardLevel(meta, cardId);
            if (level >= MaxCardLevel)
                return false;

            int copiesNeeded = CopiesRequired(level, rarity);
            int inkNeeded = InkRequired(level, rarity);
            meta.CardCopies.TryGetValue(cardId, out var copies);
            if (copies < copiesNeeded || meta.Ink < inkNeeded)
                return false;

            meta.CardCopies[cardId] = copies - copiesNeeded;
            meta.Ink -= inkNeeded;
            meta.CardLevels[cardId] = level + 1;
            return true;
        }

        /// <summary>收下一张卡:首次获得入收集,再次获得转升级重复卡(19.3.4)。</summary>
        public static void AcquireCard(MetaState meta, string cardId)
        {
            if (!meta.OwnedCards.Contains(cardId))
            {
                meta.OwnedCards.Add(cardId);
                return;
            }
            AddCardCopies(meta, cardId, 1);
        }

        /// <summary>设置出阵卡组:≤DeckLimit、全部已收集、无重复,否则 false 不动状态。</summary>
        public static bool TrySetDeck(MetaState meta, IReadOnlyList<string> cards)
        {
            if (cards.Count > DeckLimit)
                return false;
            var seen = new HashSet<string>();
            foreach (var card in cards)
                if (!meta.OwnedCards.Contains(card) || !seen.Add(card))
                    return false;

            meta.Deck.Clear();
            meta.Deck.AddRange(cards);
            return true;
        }

        /// <summary>每关起手字库:卡组有效条目 + 按等级最高从收集自动补齐至上限(19.3.4)。</summary>
        public static IReadOnlyList<string> StartingLibrary(MetaState meta)
        {
            var library = new List<string>();
            foreach (var card in meta.Deck)
                if (meta.OwnedCards.Contains(card) && !library.Contains(card))
                    library.Add(card);

            var fillers = new List<string>(meta.OwnedCards);
            fillers.Sort((a, b) =>
            {
                int byLevel = CardLevel(meta, b).CompareTo(CardLevel(meta, a));
                return byLevel != 0 ? byLevel : string.CompareOrdinal(a, b);
            });
            foreach (var card in fillers)
            {
                if (library.Count >= DeckLimit) break;
                if (!library.Contains(card)) library.Add(card);
            }
            return library;
        }

        /// <summary>卡等级数值系数:基础值 × (1 + 0.1 × (等级 − 1)),向下取整(19.3.2)。</summary>
        public static int ScaleByCardLevel(int baseValue, int cardLevel)
        {
            if (cardLevel <= 1) return baseValue;
            return (int)Math.Floor(baseValue * (1 + 0.1 * (cardLevel - 1)));
        }
    }
}
