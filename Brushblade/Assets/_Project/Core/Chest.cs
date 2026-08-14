using System;
using System.Collections.Generic;

namespace Brushblade.Core
{
    /// <summary>可信时间源(19.9):Core 不直接取系统时间;实现方负责防作弊(服务端校时)。</summary>
    public interface ITimeSource
    {
        long NowUnixSeconds { get; }
    }

    /// <summary>宝箱六级(19.5.1):数值索引 1~6,对应色阶白→红。</summary>
    public enum ChestTier
    {
        Paper = 1,    // 素纸匣(白)
        Bamboo = 2,   // 竹简匣(绿)
        Celadon = 3,  // 青瓷匣(蓝)
        Rosewood = 4, // 紫檀匣(紫)
        Gilded = 5,   // 鎏金匣(橙)
        Crimson = 6,  // 赤霄匣(红)
    }

    /// <summary>箱位中的一只宝箱(存档友好:纯数据)。</summary>
    public sealed class ChestState
    {
        public ChestTier Tier { get; set; }
        public List<string> CardPool { get; set; } = new();  // 掉落时按当前章节奖励池快照(F3)
        public long StartedAtUnix { get; set; } = -1;         // −1 = 未开始计时
        public long ReducedSeconds { get; set; }              // 广告/道具累计缩短
        public bool AdUsed { get; set; }                      // 每箱仅一次广告(2026-07-05 拍板)
        public bool Timing => StartedAtUnix >= 0;
    }

    /// <summary>宝箱规则(19.5,首版基准)。开箱奖励。</summary>
    public readonly struct ChestRewards
    {
        public int Ink { get; }
        public IReadOnlyList<string> Cards { get; }

        public ChestRewards(int ink, IReadOnlyList<string> cards)
        {
            Ink = ink;
            Cards = cards;
        }
    }

    public static class ChestRules
    {
        public const int SlotLimit = 4; // 箱位数(19.5.2);节奏阀 = 箱位 + 计时,无每日上限(2026-07-05 拍板)

        /// <summary>各级开启时长(秒):5m/30m/2h/4h/8h/12h。索引 = tier−1。</summary>
        public static readonly long[] DurationSeconds = { 300, 1800, 7200, 14400, 28800, 43200 };

        /// <summary>各级单次广告缩短(秒):即开/即开/40m/60m/90m/120m。</summary>
        public static readonly long[] AdReductionSeconds = { 300, 1800, 2400, 3600, 5400, 7200 };

        /// <summary>各级产出卡数:3/4/6/8/12/16(19.5.1)。</summary>
        public static readonly int[] CardCount = { 3, 4, 6, 8, 12, 16 };

        /// <summary>各级产出墨锭(首版基准)。</summary>
        public static readonly int[] InkReward = { 15, 30, 60, 120, 250, 400 };

        // 档位权重表:每 5 级一档向高档偏移(19.5.3 首版基准;行 = 等级段,列 = tier)
        private static readonly int[][] TierWeightBands =
        {
            new[] { 55, 30, 10, 4, 1, 0 },   // Lv 1~5
            new[] { 35, 35, 18, 8, 3, 1 },   // Lv 6~10
            new[] { 20, 32, 25, 14, 6, 3 },  // Lv 11~15
            new[] { 10, 25, 28, 20, 11, 6 }, // Lv 16~20
            new[] { 5, 18, 26, 24, 17, 10 }, // Lv 21~25
            new[] { 2, 12, 22, 26, 22, 16 }, // Lv 26+
        };

        public static string TierName(ChestTier tier) => tier switch
        {
            ChestTier.Paper => "素纸匣",
            ChestTier.Bamboo => "竹简匣",
            ChestTier.Celadon => "青瓷匣",
            ChestTier.Rosewood => "紫檀匣",
            ChestTier.Gilded => "鎏金匣",
            ChestTier.Crimson => "赤霄匣",
            _ => "?",
        };

        /// <summary>该角色等级下六档宝箱的掉落权重(索引 = tier−1)。</summary>
        public static IReadOnlyList<int> TierWeightsFor(int characterLevel)
        {
            int band = Math.Min((characterLevel - 1) / 5, TierWeightBands.Length - 1);
            return TierWeightBands[Math.Max(0, band)];
        }

        /// <summary>按角色等级掷宝箱档位:权重随等级向高档偏移(19.5.3);Boss 首通再 +1 档(封顶)。</summary>
        public static ChestTier RollTier(int characterLevel, GameRandom random, bool bossFirstClear = false)
        {
            var weights = TierWeightsFor(characterLevel);
            int total = 0;
            foreach (var weight in weights) total += weight;

            int roll = random.Next(total);
            int tier = weights.Count;
            for (int i = 0; i < weights.Count; i++)
            {
                roll -= weights[i];
                if (roll < 0) { tier = i + 1; break; }
            }
            if (bossFirstClear) tier += 1;
            return (ChestTier)Math.Min(tier, 6);
        }

        /// <summary>胜利掉箱:箱位满返回 false(不掉箱、无折算)。</summary>
        public static bool TryAwardChest(MetaState meta, ChestTier tier,
            IReadOnlyList<string> cardPool, ITimeSource time)
        {
            if (meta.Chests.Count >= SlotLimit)
                return false;

            meta.Chests.Add(new ChestState { Tier = tier, CardPool = new List<string>(cardPool) });
            return true;
        }

        /// <summary>把暂存箱(结算时箱位满而挂起的)先进先出补进空出的箱位;返回入位数量
        /// (2026-07-22:一场爬塔唯一宝箱不能凭空蒸发,满位则暂存,开箱腾位后由此入位)。</summary>
        public static int DrainPendingChests(MetaState meta, IReadOnlyList<string> cardPool, ITimeSource time)
        {
            int drained = 0;
            while (meta.PendingChests.Count > 0 && meta.Chests.Count < SlotLimit)
            {
                var tier = meta.PendingChests[0];
                meta.PendingChests.RemoveAt(0);
                TryAwardChest(meta, tier, cardPool, time); // 有空位必成功
                drained++;
            }
            return drained;
        }

        /// <summary>开始计时:同一时间仅允许一只箱**正在**计时。已就绪待领的箱不占位
        /// (2026-07-21:此前它也算占位,导致领之前后面的箱开不了计时,而 UI 的
        /// AnyChestTiming 已按「正在计时」判定,按钮亮着却点不动)。</summary>
        public static bool TryStartOpening(MetaState meta, int index, ITimeSource time)
        {
            if (meta.Chests[index].Timing)
                return false; // 自己已在计时/已就绪,不重置进度
            foreach (var chest in meta.Chests)
                if (chest.Timing && !IsReady(chest, time))
                    return false;
            meta.Chests[index].StartedAtUnix = time.NowUnixSeconds;
            return true;
        }

        /// <summary>剩余秒数(未计时返回全时长)。</summary>
        public static long RemainingSeconds(ChestState chest, ITimeSource time)
        {
            long duration = DurationSeconds[(int)chest.Tier - 1];
            if (!chest.Timing)
                return duration;
            long elapsed = time.NowUnixSeconds - chest.StartedAtUnix + chest.ReducedSeconds;
            return Math.Max(0, duration - elapsed);
        }

        public static bool IsReady(ChestState chest, ITimeSource time)
            => chest.Timing && RemainingSeconds(chest, time) <= 0;

        /// <summary>广告缩短:每箱仅一次;需在计时中。</summary>
        public static bool TryApplyAdBoost(ChestState chest)
        {
            if (chest.AdUsed || !chest.Timing)
                return false;
            chest.ReducedSeconds += AdReductionSeconds[(int)chest.Tier - 1];
            chest.AdUsed = true;
            return true;
        }

        /// <summary>墨锭加速成本:1 墨锭 / 2 分钟,向上取整,最少 1(首版基准)。</summary>
        public static int InkCostToSkip(long remainingSeconds)
            => Math.Max(1, (int)((remainingSeconds + 119) / 120));

        /// <summary>墨锭直接完成计时:扣费并将剩余时长清零。</summary>
        public static bool TrySkipWithInk(MetaState meta, int index, ITimeSource time)
        {
            var chest = meta.Chests[index];
            if (!chest.Timing)
                return false;
            long remaining = RemainingSeconds(chest, time);
            if (remaining <= 0)
                return false;
            int cost = InkCostToSkip(remaining);
            if (meta.Ink < cost)
                return false;
            meta.Ink -= cost;
            chest.ReducedSeconds += remaining;
            return true;
        }

        /// <summary>开箱:就绪后结算奖励(墨锭入账、卡入收集),移除该箱。
        /// 传入 graph 时按稀有度权重抽取并执行保底(青瓷+保底对应色阶,19.5.1);否则均匀抽取。</summary>
        public static bool TryOpen(MetaState meta, int index, ITimeSource time, GameRandom random,
            out ChestRewards rewards, RecipeGraph graph = null)
        {
            rewards = default;
            var chest = meta.Chests[index];
            if (!IsReady(chest, time))
                return false;

            int tierIndex = (int)chest.Tier - 1;
            int ink = InkReward[tierIndex];
            // 叠字前置(2026-08-15):前置未满足的字不进候选池。graph 为 null 的老调用点
            // 无从查配方,跳过过滤保持旧行为。
            var eligible = EligiblePool(chest.CardPool, graph, meta.OwnedCards);
            var cards = graph == null
                ? DrawUniform(eligible, random, CardCount[tierIndex])
                : DrawWeighted(eligible, chest.Tier, random, CardCount[tierIndex], graph);

            meta.Ink += ink;
            foreach (var card in cards)
                MetaRules.AcquireCard(meta, card);
            meta.Chests.RemoveAt(index);

            rewards = new ChestRewards(ink, cards);
            return true;
        }

        // 各箱等级的卡稀有度权重(行 = tier−1,列 = rarity−1 白→绿→蓝→紫→金→橙→红)
        // 首发仅绿/蓝/紫三档,白/金/橙/红列留 0 待扩展;绿蓝紫三列合计 100 = 实际出卡百分比。
        private static readonly int[][] CardRarityWeights =
        {
            new[] { 0, 92, 8, 0, 0, 0, 0 },   // 素纸
            new[] { 0, 85, 14, 1, 0, 0, 0 },  // 竹简
            new[] { 0, 76, 22, 2, 0, 0, 0 },  // 青瓷
            new[] { 0, 68, 28, 4, 0, 0, 0 },  // 紫檀
            new[] { 0, 58, 34, 8, 0, 0, 0 },  // 鎏金
            new[] { 0, 48, 38, 14, 0, 0, 0 }, // 赤霄
        };

        /// <summary>保底稀有度:青瓷保底蓝,紫檀及以上保底紫(首发仅到紫,原橙/红保底钳到紫)。</summary>
        private static readonly CardRarity?[] GuaranteedRarity =
            { null, null, CardRarity.Blue, CardRarity.Purple, CardRarity.Purple, CardRarity.Purple };

        /// <summary>候选池 = 前置已满足的字。滤空则回退到原池(2026-08-15 拍板的兜底):
        /// 开箱永远出足数,玩家不该感知到自己被隐藏限制挡了。
        /// graph 为 null 时不过滤(老调用点)。</summary>
        private static IReadOnlyList<string> EligiblePool(IReadOnlyList<string> cardPool,
            RecipeGraph graph, IReadOnlyCollection<string> ownedCards)
        {
            if (graph == null) return cardPool;
            var eligible = new List<string>();
            foreach (var id in cardPool)
                if (MetaRules.PrerequisitesMet(id, graph, ownedCards))
                    eligible.Add(id);
            return eligible.Count > 0 ? eligible : cardPool;
        }

        private static List<string> DrawUniform(IReadOnlyList<string> cardPool, GameRandom random, int count)
        {
            var cards = new List<string>();
            for (int i = 0; i < count && cardPool.Count > 0; i++)
                cards.Add(random.Pick(cardPool));
            return cards;
        }

        private static List<string> DrawWeighted(IReadOnlyList<string> cardPool, ChestTier tier,
            GameRandom random, int count, RecipeGraph graph)
        {
            // 池按稀有度分组(池外/图谱外的 id 忽略)
            var byRarity = new Dictionary<CardRarity, List<string>>();
            foreach (var id in cardPool)
            {
                if (!graph.TryGet(id, out var def)) continue;
                if (!byRarity.TryGetValue(def.Rarity, out var group))
                    byRarity[def.Rarity] = group = new List<string>();
                group.Add(id);
            }
            if (byRarity.Count == 0)
                return new List<string>();

            var weights = CardRarityWeights[(int)tier - 1];
            var cards = new List<string>();
            for (int i = 0; i < count; i++)
                cards.Add(DrawOne(byRarity, weights, random));

            // 保底:抽取结果中无达标稀有度 → 换入一张(池中无达标时取最高可得)。
            // byRarity 已是**前置过滤后**的池,所以"前置优先、保底降级"由 PickAtLeast
            // 既有的 floor 逻辑自动成立(spec §2.3 第 2 条),这里不需要额外接线。
            var guaranteed = GuaranteedRarity[(int)tier - 1];
            if (guaranteed is { } minRarity && cards.Count > 0)
            {
                bool satisfied = false;
                foreach (var card in cards)
                    if (graph.Get(card).Rarity >= minRarity) { satisfied = true; break; }
                if (!satisfied)
                    cards[0] = PickAtLeast(byRarity, minRarity, random);
            }
            return cards;
        }

        private static string DrawOne(Dictionary<CardRarity, List<string>> byRarity,
            int[] weights, GameRandom random)
        {
            int total = 0;
            foreach (var pair in byRarity)
                total += weights[(int)pair.Key - 1];
            if (total <= 0) // 权重全零(如低档箱只配了高稀有池):均匀兜底
            {
                var all = new List<string>();
                foreach (var group in byRarity.Values) all.AddRange(group);
                return random.Pick(all);
            }

            int roll = random.Next(total);
            foreach (var pair in byRarity)
            {
                roll -= weights[(int)pair.Key - 1];
                if (roll < 0)
                    return random.Pick(pair.Value);
            }
            throw new InvalidOperationException("unreachable");
        }

        private static string PickAtLeast(Dictionary<CardRarity, List<string>> byRarity,
            CardRarity minRarity, GameRandom random)
        {
            var candidates = new List<string>();
            var best = CardRarity.White;
            foreach (var pair in byRarity)
                if (pair.Key > best) best = pair.Key;

            var floor = minRarity <= best ? minRarity : best; // 池中无达标 → 取最高可得档
            foreach (var pair in byRarity)
                if (pair.Key >= floor)
                    candidates.AddRange(pair.Value);
            return random.Pick(candidates);
        }
    }
}
