using System;
using System.Collections.Generic;

namespace Brushblade.Core
{
    /// <summary>可信时间源(19.9):Core 不直接取系统时间;实现方负责防作弊(服务端校时)。</summary>
    public interface ITimeSource
    {
        long NowUnixSeconds { get; }
    }

    /// <summary>宝箱七级(19.5.1):数值索引 1~7,与 <see cref="CardRarity"/> 的七档色阶一一对应。
    /// 2026-08-29 补上朱漆匣(橙)—— 此前只有六档,鎏金注成「橙」而实际取的是金色,
    /// 橙这一档在箱子侧根本不存在。赤霄由 6 挪到 7,旧存档里的赤霄箱会被读成朱漆(未上线,不做迁移)。</summary>
    public enum ChestTier
    {
        Paper = 1,     // 素纸匣(白)
        Bamboo = 2,    // 竹简匣(绿)
        Celadon = 3,   // 青瓷匣(蓝)
        Rosewood = 4,  // 紫檀匣(紫)
        Gilded = 5,    // 鎏金匣(金)
        Vermilion = 6, // 朱漆匣(橙)
        Crimson = 7,   // 赤霄匣(红)
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

        /// <summary>各级开启时长(秒):5m/30m/2h/4h/8h/10h/12h。索引 = tier−1。</summary>
        public static readonly long[] DurationSeconds = { 300, 1800, 7200, 14400, 28800, 36000, 43200 };

        /// <summary>各级单次广告缩短(秒):即开/即开/40m/60m/90m/105m/120m。</summary>
        public static readonly long[] AdReductionSeconds = { 300, 1800, 2400, 3600, 5400, 6300, 7200 };

        /// <summary>各级产出卡数:3/4/6/8/12/14/16(19.5.1)。</summary>
        public static readonly int[] CardCount = { 3, 4, 6, 8, 12, 14, 16 };

        /// <summary>各级产出墨锭(首版基准)。</summary>
        public static readonly int[] InkReward = { 15, 30, 60, 120, 250, 320, 400 };

        // 档位权重表:每 5 级一档向高档偏移(19.5.3 首版基准;行 = 等级段,列 = tier)
        private static readonly int[][] TierWeightBands =
        {
            new[] { 55, 30, 10, 4, 1, 0, 0 },   // Lv 1~5
            new[] { 35, 35, 18, 8, 3, 1, 0 },   // Lv 6~10
            new[] { 20, 32, 25, 14, 6, 2, 1 },  // Lv 11~15
            new[] { 10, 25, 28, 20, 11, 4, 2 }, // Lv 16~20
            new[] { 5, 18, 26, 24, 17, 7, 3 },  // Lv 21~25
            new[] { 2, 12, 22, 26, 22, 11, 5 }, // Lv 26+
        };

        public static string TierName(ChestTier tier) => tier switch
        {
            ChestTier.Paper => "素纸匣",
            ChestTier.Bamboo => "竹简匣",
            ChestTier.Celadon => "青瓷匣",
            ChestTier.Rosewood => "紫檀匣",
            ChestTier.Gilded => "鎏金匣",
            ChestTier.Vermilion => "朱漆匣",
            ChestTier.Crimson => "赤霄匣",
            _ => "?",
        };

        /// <summary>该角色等级下七档宝箱的掉落权重(索引 = tier−1)。</summary>
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
            return (ChestTier)Math.Min(tier, (int)ChestTier.Crimson);
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
                : DrawWeighted(meta, eligible, chest.Tier, random, CardCount[tierIndex], graph);

            meta.Ink += ink;
            foreach (var card in cards)
                MetaRules.AcquireCard(meta, card);
            meta.Chests.RemoveAt(index);

            rewards = new ChestRewards(ink, cards);
            return true;
        }

        /// <summary>权重单位是**千分比**(2026-08-29):红在鎏金匣只有 0.1%,百分比装不下。</summary>
        public const int RarityWeightTotal = 1000;

        // 各箱等级的卡稀有度权重(行 = tier−1,列 = rarity−1 白→绿→蓝→紫→金→橙→红),每行合计 1000‰。
        // 2026-08-29 重写:此前白/金/橙/红四列写死 0,而 8-25 字表重构后这四档共 37 个字
        // (占可收集字的一半)—— 白字整档掉不出来,金橙红只能从保底口子漏。
        // 加粗的九个数是用户拍板值:金 青瓷 10‰ / 紫檀 20‰ / 鎏金 50‰;
        // 橙 紫檀 5‰ / 鎏金 10‰ / 朱漆 20‰;红 鎏金 1‰ / 朱漆 5‰ / 赤霄 10‰。
        private static readonly int[][] CardRarityWeights =
        {
            //       白    绿    蓝    紫    金   橙  红
            new[] { 400, 500, 100,   0,   0,  0,  0 },  // 素纸
            new[] { 250, 500, 220,  30,   0,  0,  0 },  // 竹简
            new[] { 120, 450, 330,  90,  10,  0,  0 },  // 青瓷
            new[] {  50, 330, 380, 215,  20,  5,  0 },  // 紫檀
            new[] {   0, 219, 360, 360,  50, 10,  1 },  // 鎏金
            new[] {   0, 150, 320, 435,  70, 20,  5 },  // 朱漆
            new[] {   0, 100, 260, 500, 100, 30, 10 },  // 赤霄
        };

        /// <summary>该档宝箱的卡稀有度权重(千分比,索引 = rarity−1)。</summary>
        public static IReadOnlyList<int> CardRarityWeightsFor(ChestTier tier)
            => CardRarityWeights[(int)tier - 1];

        /// <summary>单箱保底:青瓷保蓝、紫檀及以上保紫。金/橙/红不在这里 —— 它们走
        /// <see cref="PityRules"/> 的跨箱计数保底,两套叠在一起会让高稀有度过量。</summary>
        private static readonly CardRarity?[] GuaranteedRarity =
        {
            null, null, CardRarity.Blue, CardRarity.Purple,
            CardRarity.Purple, CardRarity.Purple, CardRarity.Purple,
        };

        /// <summary>计数保底(2026-08-29 拍板):开满 Threshold 只 ≥MinTier 的箱还没见过该稀有度,
        /// 下一箱强制替一张进来。高档箱推进全部低档计数(赤霄箱同时是一次橙、一次金的进度),
        /// 出了就归零 —— 自然掉出的也算,所以保底只兜极端非酋,不抬总产出。
        /// 顺序从高到低:同一箱多条同时触发时,先放红再放橙,后面的替换只会挑更低的那张。</summary>
        public static readonly (CardRarity Rarity, int Threshold, ChestTier MinTier)[] PityRules =
        {
            (CardRarity.Red, 20, ChestTier.Crimson),
            (CardRarity.Orange, 10, ChestTier.Vermilion),
            (CardRarity.Gold, 5, ChestTier.Gilded),
        };

        /// <summary>候选池 = 前置已满足的字;滤空时回退未过滤的原池
        /// (2026-08-15 用户拍板:出满数优先,限制让路)。
        ///
        /// 这是有意的取舍,不是漏洞:
        /// - 滤空时回退的是**未过滤的原池**,此时前置限制对本次开箱**确实完全失效**。
        ///   两害相权:「隐藏限制」的首要目标是玩家无感知,开箱出 0 张是明显的 bug 感,
        ///   比"这一箱限制没生效"更糟——所以选择让限制让路,而不是让产出数缩水。
        /// - 不需要"配方只含部件的字"这一中间层:这类字对
        ///   <see cref="MetaRules.PrerequisitesMet"/> 恒为 true(该方法只检查非叶子原料,
        ///   全叶子配方没有要检查的),所以它们本就是第 1 级 eligible 的子集 —— eligible
        ///   为空时它必然也空,单独写一层判定是数学上不可达的死代码(已删除;原实现叫
        ///   IsComponentOnlyRecipe)。
        /// - 当前真实数据下这条回退路径基本不会触发:ChestCardPool() 覆盖全部 105 个
        ///   非叶子字,其中 82 个是纯部件配方、恒合格,任何未被人为收窄的池总有合格字可选。
        ///   真触发了,大概率是**宝箱池配置有误**(比如误配了一个清一色高阶叠字的池),
        ///   应该去配置层修,而不是靠运行时兜底掩盖配置问题。
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

        private static List<string> DrawWeighted(MetaState meta, IReadOnlyList<string> cardPool,
            ChestTier tier, GameRandom random, int count, RecipeGraph graph)
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
                    cards[0] = PickAtLeast(byRarity, minRarity, weights, random);
            }

            ApplyPity(meta, tier, cards, byRarity, random, graph);
            return cards;
        }

        /// <summary>计数保底(见 <see cref="PityRules"/>)。每条规则:够档的箱先给计数 +1,
        /// 结果里没有该稀有度且计数到阈值就替掉**最低**的那张,最后按「这一箱到底有没有」归零。
        /// 池里根本没有该稀有度的字时不替换、计数也不清 —— 下一箱接着攒。</summary>
        private static void ApplyPity(MetaState meta, ChestTier tier, List<string> cards,
            Dictionary<CardRarity, List<string>> byRarity, GameRandom random, RecipeGraph graph)
        {
            if (cards.Count == 0)
                return;

            foreach (var rule in PityRules)
            {
                if (tier < rule.MinTier)
                    continue; // 低档箱不推进这条:金只数鎏金及以上、橙只数朱漆及以上、红只数赤霄

                int counter = GetPity(meta, rule.Rarity) + 1;
                bool hit = HasRarity(cards, rule.Rarity, graph);

                if (!hit && counter >= rule.Threshold
                    && byRarity.TryGetValue(rule.Rarity, out var group) && group.Count > 0)
                {
                    cards[LowestRarityIndex(cards, graph)] = random.Pick(group);
                    hit = true;
                }
                SetPity(meta, rule.Rarity, hit ? 0 : counter);
            }
        }

        private static bool HasRarity(List<string> cards, CardRarity rarity, RecipeGraph graph)
        {
            foreach (var card in cards)
                if (graph.Get(card).Rarity == rarity) return true;
            return false;
        }

        private static int LowestRarityIndex(List<string> cards, RecipeGraph graph)
        {
            int index = 0;
            var lowest = graph.Get(cards[0]).Rarity;
            for (int i = 1; i < cards.Count; i++)
            {
                var rarity = graph.Get(cards[i]).Rarity;
                if (rarity < lowest) { lowest = rarity; index = i; }
            }
            return index;
        }

        private static int GetPity(MetaState meta, CardRarity rarity) => rarity switch
        {
            CardRarity.Gold => meta.GoldPity,
            CardRarity.Orange => meta.OrangePity,
            CardRarity.Red => meta.RedPity,
            _ => 0,
        };

        private static void SetPity(MetaState meta, CardRarity rarity, int value)
        {
            switch (rarity)
            {
                case CardRarity.Gold: meta.GoldPity = value; break;
                case CardRarity.Orange: meta.OrangePity = value; break;
                case CardRarity.Red: meta.RedPity = value; break;
            }
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

        /// <summary>保底补位:在 ≥floor 的档里**按权重**抽(2026-08-29)。
        /// 原先是把 ≥floor 的字混成一锅均匀抽,于是保底比正常抽还慷慨 —— 金橙红字数多于紫,
        /// 触发保底反而更常吐高稀有度,越高档的箱越明显。权重全零(池里 ≥floor 的档
        /// 在这档箱上都不该出)才退回均匀抽,否则一张都给不出来。</summary>
        private static string PickAtLeast(Dictionary<CardRarity, List<string>> byRarity,
            CardRarity minRarity, int[] weights, GameRandom random)
        {
            var best = CardRarity.White;
            foreach (var pair in byRarity)
                if (pair.Key > best) best = pair.Key;
            var floor = minRarity <= best ? minRarity : best; // 池中无达标 → 取最高可得档

            int total = 0;
            foreach (var pair in byRarity)
                if (pair.Key >= floor) total += weights[(int)pair.Key - 1];

            if (total <= 0)
            {
                var candidates = new List<string>();
                foreach (var pair in byRarity)
                    if (pair.Key >= floor) candidates.AddRange(pair.Value);
                return random.Pick(candidates);
            }

            int roll = random.Next(total);
            foreach (var pair in byRarity)
            {
                if (pair.Key < floor) continue;
                roll -= weights[(int)pair.Key - 1];
                if (roll < 0)
                    return random.Pick(pair.Value);
            }
            throw new InvalidOperationException("unreachable");
        }
    }
}
