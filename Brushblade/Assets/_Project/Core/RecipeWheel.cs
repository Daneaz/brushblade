using System;
using System.Collections.Generic;
using System.Linq;

namespace Brushblade.Core
{
    /// <summary>一次配方转盘的结果(spec 2026-10-19 §2):格子、哪一格是高档、落在哪一格。
    /// 进 RunSnapshot —— 停在战后选字页挂起再进,表现层仍拿得到这次的结果。</summary>
    public sealed class RecipeWheelResult
    {
        /// <summary>格子里的字(低档在前,高档格在 <see cref="HighIndex"/>)。正常 5 格,候选不足时更少。</summary>
        public List<string> Slots { get; set; } = new();

        /// <summary>高档格下标;没有高档格时 −1。</summary>
        public int HighIndex { get; set; } = -1;

        /// <summary>转盘落定的格子下标。</summary>
        public int Landed { get; set; }

        /// <summary>落中的配方目标字。</summary>
        public string Target => Slots[Landed];
    }

    /// <summary>配方转盘(spec 2026-10-19 §2):5 层起 Boss / 精英层战后转一次,4 低档 + 1 高档,
    /// 高档格 10%、低档格平分其余;落中的字进本局配方集并记为生疏(§3)。纯函数,随机只吃传进来的流。</summary>
    public static class RecipeWheel
    {
        public const int FromDepth = 5;   // 用户 10-20:11 → 5,分档不变(5–20 蓝 / 紫)
        public const int LowSlots = 4;
        public const int HighChancePercent = 10;

        /// <summary>这一层战后转不转:depth ≥ 5 且是 Boss 层(精英 5/15/25… 与主题 Boss 10/20/30… 都算,
        /// 判定沿用 <see cref="EndlessConfig.IsBossDepth"/>)。</summary>
        public static bool AppliesAt(EndlessConfig config, int depth) =>
            depth >= FromDepth && config.IsBossDepth(depth);

        /// <summary>分档表:5–20 蓝 / 紫,21–30 紫 / 金,31–40 金 / 橙,41 起 橙 / 红。</summary>
        public static (CardRarity Low, CardRarity High) TiersFor(int depth) => depth switch
        {
            <= 20 => (CardRarity.Blue, CardRarity.Purple),
            <= 30 => (CardRarity.Purple, CardRarity.Gold),
            <= 40 => (CardRarity.Gold, CardRarity.Orange),
            _ => (CardRarity.Orange, CardRarity.Red),
        };

        /// <summary>转盘专属随机流:同 (塔种子, 层) 永远同结果,不碰 RunEngine 的主序列,
        /// 与层编成流 <see cref="EndlessGenerator.FloorRandom"/> 错开(加了盐)。</summary>
        public static GameRandom StreamFor(int seed, int depth) =>
            new(unchecked(seed * 131 + depth * 7919 + 0x5EED));

        /// <summary>生成一次转盘;一格都凑不出返回 null(跳过转盘)。
        ///
        /// 候选 = 该档未收集、不在配方集、有配方的可出牌字;同档内「配方原料都在当前可合成集里
        /// (同系偏旁互替)」的排前面,各自洗牌。低档不足 4 格用低一档补;高档没有字用低档(其次低一档)剩下的补。
        /// 只补相邻一档,不往下递归。</summary>
        public static RecipeWheelResult Spin(int depth, RecipeGraph graph, IReadOnlyCollection<string> owned,
            IReadOnlyCollection<string> runRecipes, GameRandom random)
        {
            owned ??= Array.Empty<string>();
            runRecipes ??= Array.Empty<string>();
            var obtainable = new HashSet<string>(ForgeEngine.ComposableSet(graph, owned));
            obtainable.UnionWith(runRecipes);

            var (low, high) = TiersFor(depth);
            var below = low > CardRarity.White ? (CardRarity?)(low - 1) : null;
            var taken = new HashSet<string>();

            var lows = Take(Ordered(graph, low, owned, runRecipes, obtainable, random), LowSlots, taken);
            if (lows.Count < LowSlots && below is { } b)
                lows.AddRange(Take(Ordered(graph, b, owned, runRecipes, obtainable, random), LowSlots - lows.Count, taken));

            var highs = Take(Ordered(graph, high, owned, runRecipes, obtainable, random), 1, taken);
            if (highs.Count == 0)
                highs = Take(Ordered(graph, low, owned, runRecipes, obtainable, random), 1, taken);
            if (highs.Count == 0 && below is { } b2)
                highs = Take(Ordered(graph, b2, owned, runRecipes, obtainable, random), 1, taken);

            if (lows.Count + highs.Count == 0) return null;

            var result = new RecipeWheelResult { Slots = lows.Concat(highs).ToList() };
            result.HighIndex = highs.Count > 0 ? lows.Count : -1;
            if (result.HighIndex < 0) result.Landed = random.Next(lows.Count);
            else if (lows.Count == 0) result.Landed = result.HighIndex;
            else result.Landed = random.Next(100) < HighChancePercent ? result.HighIndex : random.Next(lows.Count);
            return result;
        }

        private static List<string> Take(IEnumerable<string> ordered, int count, HashSet<string> taken)
        {
            var picked = new List<string>();
            foreach (var id in ordered)
            {
                if (picked.Count >= count) break;
                if (taken.Add(id)) picked.Add(id);
            }
            return picked;
        }

        /// <summary>某档候选:可凑齐的(洗牌)在前,其余(洗牌)在后。按 id 排序后再洗,不依赖字典遍历序。</summary>
        private static List<string> Ordered(RecipeGraph graph, CardRarity rarity, IReadOnlyCollection<string> owned,
            IReadOnlyCollection<string> runRecipes, HashSet<string> obtainable, GameRandom random)
        {
            var pool = graph.All
                .Where(d => d.Rarity == rarity && !d.IsComponent && !d.IsLeaf
                    && !owned.Contains(d.Id) && !runRecipes.Contains(d.Id))
                .OrderBy(d => d.Id, StringComparer.Ordinal)
                .ToList();
            var ready = Shuffle(pool.Where(d => d.Recipe.All(i => Obtainable(i, obtainable))).Select(d => d.Id).ToList(), random);
            var rest = Shuffle(pool.Where(d => !d.Recipe.All(i => Obtainable(i, obtainable))).Select(d => d.Id).ToList(), random);
            ready.AddRange(rest);
            return ready;
        }

        private static bool Obtainable(string ingredient, HashSet<string> obtainable)
        {
            if (obtainable.Contains(ingredient)) return true;
            return ComponentKin.TryGetGroup(ingredient, out var group) && group.Any(obtainable.Contains);
        }

        private static List<string> Shuffle(List<string> list, GameRandom random)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
            return list;
        }
    }
}
