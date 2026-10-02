using System.Collections.Generic;

namespace Brushblade.Core
{
    /// <summary>跨局累计统计(spec 2026-10-02 §3)。纯展示,不影响玩法;新字段缺省 0 = 从这一版起开始计。
    /// 保底进度**不在这里** —— 角色页直接读 MetaState.GoldPity/OrangePity/RedPity,与真实保底同源。</summary>
    public sealed class StatsState
    {
        public Dictionary<ChestTier, int> ChestsOpened { get; set; } = new();
        /// <summary>按**张数**(每种的张数之和),不是种数。</summary>
        public Dictionary<CardRarity, int> CardsFromChests { get; set; } = new();
        public int Climbs { get; set; }          // 开新塔次数,续爬不算
        public int FloorsCleared { get; set; }
        public int BossesDefeated { get; set; }
        public int Deaths { get; set; }          // 弃塔不算
        public Dictionary<string, int> CardPlays { get; set; } = new();
        public int Composes { get; set; }
        public int Dismantles { get; set; }
        public int MaxHit { get; set; }
        public int InkEarned { get; set; }
        public int InkSpent { get; set; }
        public int AdRewards { get; set; }
    }

    /// <summary>本场(或本段)战斗计数。BattleEngine 只往这里记,不认识 MetaState;
    /// 由 Presentation 在结算点调 <see cref="StatsRules.FoldTally"/> 折进存档并清空。</summary>
    public sealed class BattleTally
    {
        public Dictionary<string, int> Plays { get; } = new();
        public int Composes { get; set; }
        public int Dismantles { get; set; }
        public int MaxHit { get; set; }

        public void Clear()
        {
            Plays.Clear();
            Composes = 0;
            Dismantles = 0;
            MaxHit = 0;
        }
    }

    /// <summary>统计累加(spec 2026-10-02 §4)。纯函数,状态进出。</summary>
    public static class StatsRules
    {
        public static void RecordChestOpened(MetaState meta, ChestTier tier,
            IReadOnlyList<CardRarity> rarities, IReadOnlyList<int> counts)
        {
            var stats = meta.Stats;
            stats.ChestsOpened.TryGetValue(tier, out int opened);
            stats.ChestsOpened[tier] = opened + 1;
            for (int i = 0; i < rarities.Count; i++)
            {
                stats.CardsFromChests.TryGetValue(rarities[i], out int n);
                stats.CardsFromChests[rarities[i]] = n + counts[i];
            }
        }

        public static void FoldTally(MetaState meta, BattleTally tally)
        {
            if (tally == null) return;
            var stats = meta.Stats;
            foreach (var kv in tally.Plays)
            {
                stats.CardPlays.TryGetValue(kv.Key, out int n);
                stats.CardPlays[kv.Key] = n + kv.Value;
            }
            stats.Composes += tally.Composes;
            stats.Dismantles += tally.Dismantles;
            if (tally.MaxHit > stats.MaxHit) stats.MaxHit = tally.MaxHit;
            tally.Clear();
        }

        public static void RecordClimbStart(MetaState meta) => meta.Stats.Climbs++;

        public static void RecordFloorCleared(MetaState meta, bool isBoss)
        {
            meta.Stats.FloorsCleared++;
            if (isBoss) meta.Stats.BossesDefeated++;
        }

        public static void RecordDeath(MetaState meta) => meta.Stats.Deaths++;

        public static void RecordAdReward(MetaState meta) => meta.Stats.AdRewards++;

        /// <summary>出手最多的前 <paramref name="count"/> 个字。图谱里没有的 id(字表删字后的幽灵)跳过。</summary>
        public static List<(string Id, int Plays)> TopPlays(MetaState meta, RecipeGraph graph, int count)
        {
            var all = new List<(string Id, int Plays)>();
            foreach (var kv in meta.Stats.CardPlays)
                if (graph.TryGet(kv.Key, out _))
                    all.Add((kv.Key, kv.Value));
            all.Sort((a, b) => a.Plays != b.Plays
                ? b.Plays.CompareTo(a.Plays)
                : string.CompareOrdinal(a.Id, b.Id));
            if (all.Count > count) all.RemoveRange(count, all.Count - count);
            return all;
        }
    }
}
