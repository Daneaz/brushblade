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
        public Dictionary<string, int> PerkLevels { get; set; } = new();  // 技能 id → 等级;缺省 0=未解锁
        public Dictionary<string, int> CardCopies { get; set; } = new();  // 待消耗重复卡
        public List<string> OwnedCards { get; set; } = new();             // 收集(首次获得即入)
        public List<string> Deck { get; set; } = new();                   // 出阵卡组(≤4,19.3.4)
        public List<int> ClearedStages { get; set; } = new();             // 每章已通关数
        public List<ChestState> Chests { get; set; } = new();             // 箱位队列(≤4,19.5.2)
        public List<ChestTier> PendingChests { get; set; } = new();        // 结算时箱位满的暂存箱,开箱腾位后入位(2026-07-22)
        public ShopState Shop { get; set; } = new();                      // 每日商城(19.6)
        public int BestDepth { get; set; }                                // 无尽最高层数(20.5)
        public List<string> BandMilestones { get; set; } = new();         // 已领首破奖励的层段(20.3)
        public EndlessSaveState Endless { get; set; }                     // 断点续爬快照;null=无进行中登塔(20.6)
        public List<string> DefeatedEnemies { get; set; } = new();        // 图鉴已解锁(击败即入)
        public List<string> ClaimedBestiary { get; set; } = new();        // 图鉴已查阅领赏(主动点开才发)
    }

    /// <summary>养成规则(19.2/19.3 首版基准)。纯函数,状态进出。</summary>
    public static class MetaRules
    {
        public const int MaxCardLevel = 10;
        public const int DeckLimit = 15;          // 出阵列表上限(2026-07-19 拍板:5×3,后续可调)
        public const int DeckMinimum = 5;         // 出阵下限(2026-07-19:起手不得少于 5 字)
        public const int DeckPerElementLimit = 5; // 每属性最多 5 字(属性种类不限,3 系上限已废止)
        public const int StartingLibrarySize = 6; // 起手字库数量
        // 字库容量比起手多一格,留给回合掉字(2026-08-04):否则开局即满库,第一回合必弹 DropChoice
        public const int LibraryCapacitySlack = 1;

        /// <summary>初始收集(2026-08-05 拍板):五系 × 白/绿/蓝各一张,共 15 张 = DeckLimit。
        /// 此前是五系 2 叠紫档字(鍂/林/沝/炎/圭)——玩家一开局就握着紫档,升阶目标感缺失。
        /// 全部要求有配方(可拆可合),否则拆了回不来。</summary>
        public static readonly IReadOnlyList<string> StartingCollection = new[]
        {
            "割", "锯", "剑", // 金:单体斩杀线(白 6 伤 / 绿 流血 / 蓝 13 伤)
            "梅", "松", "柏", // 木:召唤线(召 6 / 12 / 16 血)
            "冷", "润", "冻", // 水:控制与治疗(减速 / 持续治疗 / 冻结)
            "灼", "烧", "爆", // 火:灼烧线(6 伤 / 灼烧 / 全体 7 伤)
            "碾", "墙", "城", // 土:防御线(6 伤 / 盾 7 / 盾 10)
        };

        /// <summary>默认出阵 = 五系蓝档各一张。恰好 5 张(≥ DeckMinimum 且 ≤ StartingLibrarySize),
        /// 所以默认出阵全部进起手字库——教程要拆的字必在手上(见 <see cref="Tutorial.DemoChar"/>)。
        /// 其余 10 张留在收集里,玩家自行换上(出阵上限 15 格装得下全部)。</summary>
        public static readonly IReadOnlyList<string> StartingDeck = new[]
        {
            "剑", "柏", "冻", "爆", "城",
        };

        /// <summary>出阵表所需的部件(2026-08-05 拍板):部件的一切来源都从这里取,
        /// 不再是固定金木水火土——出阵表换了,能掉的部件跟着换,拿到的部件永远拼得出手里的字。
        /// 只收叶子(部件);配方里的低阶字不算,那是靠合成得来的。</summary>
        public static IEnumerable<string> DeckComponents(
            IReadOnlyList<string> deck, RecipeGraph graph)
        {
            var seen = new List<string>();
            foreach (var card in deck)
            {
                if (!graph.TryGet(card, out var def)) continue;
                foreach (var part in def.Recipe)
                    if (graph.TryGet(part, out var partDef) && partDef.IsLeaf && !seen.Contains(part))
                        seen.Add(part);
            }
            return seen;
        }

        /// <summary>登塔初始部件池:从出阵表所需部件里随机掷 <paramref name="count"/> 个(可重复)。
        /// 出阵表拼不出任何部件时返回空池——不回退到五行,那会把死牌塞回来。</summary>
        public static IReadOnlyList<string> RollStartingPool(IReadOnlyList<string> deck,
            RecipeGraph graph, GameRandom random, int count = StartingPoolSize)
        {
            var choices = new List<string>(DeckComponents(deck, graph));
            var pool = new List<string>();
            if (choices.Count == 0) return pool;
            for (int i = 0; i < count; i++)
                pool.Add(choices[random.Next(choices.Count)]);
            return pool;
        }

        public const int StartingPoolSize = 2; // 登塔起手部件数(沿用旧的两个)

        /// <summary>战斗字库容量 = 起手数量 + 掉字缓冲 + 博闻加成。「容量比起手多一格」这个关系
        /// 只在这一处定义——GameRoot 接线时调这个,不要在那边散写 +1。</summary>
        public static int LibraryCapacityFor(MetaState meta) =>
            StartingLibrarySize + LibraryCapacitySlack + PerkRules.LibraryBonus(meta);

        /// <summary>集卡升级需求(升到下一级所需同名卡,白卡基准,19.3.3)。索引 = 当前等级 − 1。</summary>
        public static readonly int[] CopiesToUpgrade = { 2, 4, 10, 20, 40, 80, 150, 300, 500 };

        /// <summary>升级墨锭成本(白卡基准)。索引 = 当前等级 − 1。</summary>
        public static readonly int[] InkToUpgrade = { 20, 50, 120, 300, 700, 1500, 3000, 6000, 12000 };

        // 稀有度成本系数(索引 = rarity−1,白→红;19.3.3:越稀有需卡越少、墨锭越贵)
        private static readonly double[] CopiesMultiplier = { 1.0, 0.7, 0.4, 0.25, 0.15, 0.1, 0.05 };
        private static readonly double[] InkMultiplier = { 1.0, 1.5, 2.0, 3.0, 4.0, 5.0, 6.0 };

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

        /// <summary>攻击成长:100 + 2×(等级−1),上限 150(19.2.1 角色属性)。
        /// 与 <see cref="MaxHpFor"/> 同形同封顶级(26 级)——两条角色属性曲线口径一致。
        /// 基准 100:<c>伤害 = 值 × ATK ÷ 100</c>,1 级时恒等于引入攻击力之前。</summary>
        public static int AttackFor(int level) => Math.Min(150, 100 + 2 * (level - 1));

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

        /// <summary>设置出阵列表(2026-07-19 拍板):5~15 字、每属性≤5、全部已收集、无重复,
        /// 否则 false 不动状态。属性种类不限(3 系上限已废止)。无属性字计作心系一类。</summary>
        public static bool TrySetDeck(MetaState meta, IReadOnlyList<string> cards, RecipeGraph graph)
        {
            if (cards.Count > DeckLimit || cards.Count < DeckMinimum)
                return false;
            var seen = new HashSet<string>();
            var perElement = new Dictionary<Element, int>();
            foreach (var card in cards)
            {
                if (!meta.OwnedCards.Contains(card) || !seen.Add(card))
                    return false;
                var element = graph.Get(card).Element ?? Element.Heart;
                perElement.TryGetValue(element, out var count);
                perElement[element] = count + 1;
                if (perElement[element] > DeckPerElementLimit)
                    return false;
            }

            meta.Deck.Clear();
            meta.Deck.AddRange(cards);
            return true;
        }

        /// <summary>登塔起手字库:出阵列表按等级取前 6(StartingLibrarySize,起手数量);
        /// 字库基础容量是 6+1=7(LibraryCapacityFor,多出的 1 格是掉字缓冲),起手不占满。
        /// 只带自选出阵的字——自动补齐已废止(2026-07-19 拍板:没选就不上场)。</summary>
        public static IReadOnlyList<string> StartingLibrary(MetaState meta)
        {
            var roster = new List<string>();
            foreach (var card in meta.Deck)
                if (meta.OwnedCards.Contains(card) && !roster.Contains(card))
                    roster.Add(card);
            SortByLevelDesc(meta, roster);

            int startingCap = StartingLibrarySize + PerkRules.LibraryBonus(meta); // 博闻:+1 格/级(这里是起手数量上限,不吃 LibraryCapacitySlack)
            var library = new List<string>();
            foreach (var card in roster)
            {
                if (library.Count >= startingCap) break;
                library.Add(card);
            }
            return library;
        }

        private static void SortByLevelDesc(MetaState meta, List<string> cards)
        {
            cards.Sort((a, b) =>
            {
                int byLevel = CardLevel(meta, b).CompareTo(CardLevel(meta, a));
                return byLevel != 0 ? byLevel : string.CompareOrdinal(a, b);
            });
        }

        /// <summary>字表裁剪后的存档清洗:移除一切引用已下架字的条目,防启动崩溃。
        /// 货架含下架字 → 整架作废(DayStamp 重置触发重摆);奖池清空的宝箱一并移除。</summary>
        public static void PruneUnknownCards(MetaState meta, RecipeGraph graph)
        {
            bool Known(string id) => graph.TryGet(id, out _);

            meta.OwnedCards.RemoveAll(id => !Known(id));
            meta.Deck.RemoveAll(id => !Known(id));
            RemoveUnknownKeys(meta.CardLevels, Known);
            RemoveUnknownKeys(meta.CardCopies, Known);

            if (meta.Shop.CardSlots.Exists(id => !Known(id)))
            {
                meta.Shop.CardSlots.Clear();
                meta.Shop.CardSold.Clear();
                meta.Shop.DayStamp = -1;
            }

            foreach (var chest in meta.Chests)
                chest.CardPool.RemoveAll(id => !Known(id));
            meta.Chests.RemoveAll(chest => chest.CardPool.Count == 0);

            if (meta.Endless != null)
            {
                meta.Endless.Library.RemoveAll(id => !Known(id));
                meta.Endless.Pool.RemoveAll(id => !Known(id));
            }
        }

        private static void RemoveUnknownKeys(Dictionary<string, int> map, Func<string, bool> known)
        {
            var stale = new List<string>();
            foreach (var key in map.Keys)
                if (!known(key))
                    stale.Add(key);
            foreach (var key in stale)
                map.Remove(key);
        }

        /// <summary>卡等级数值系数:基础值 × (1 + 0.1 × (等级 − 1)),向上取整(19.3.2;
        /// 2026-07-19 floor→ceiling:低数值字升 1 级即 +1,升级可感)。</summary>
        public static int ScaleByCardLevel(int baseValue, int cardLevel)
        {
            if (cardLevel <= 1) return baseValue;
            return (int)Math.Ceiling(baseValue * (1 + 0.1 * (cardLevel - 1)));
        }
    }
}
