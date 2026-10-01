using System.Collections.Generic;

namespace Brushblade.Core
{
    /// <summary>跨局累计统计(spec 2026-10-02 §3)。纯展示,不影响玩法;新字段缺省 0 = 从这一版起开始计。
    /// 保底进度**不在这里** —— 角色页直接读 MetaState.GoldPity/OrangePity/RedPity,与真实保底同源。</summary>
    public sealed class StatsState
    {
        public Dictionary<ChestTier, int> ChestsOpened { get; set; } = new();
        /// <summary>按**张数**(捆 × 每捆张数),不是捆数。</summary>
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
    }
}
