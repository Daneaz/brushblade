using System;
using System.Collections.Generic;

namespace Brushblade.Core
{
    /// <summary>升级弹窗要说的那件事:从几级升到几级、途中跨过了哪些里程碑。</summary>
    public sealed class LevelUpSummary
    {
        public int FromLevel { get; set; }
        public int ToLevel { get; set; }
        public List<int> Milestones { get; set; } = new();
    }

    /// <summary>每级宝箱(spec 2026-10-02 §2.1 / §5.1)。升到的那一级落在哪个区间就给哪档,不掷随机。</summary>
    public static class LevelRewardRules
    {
        /// <summary>各档的等级上限(含),按档位升序;末档开放到无穷。TierForLevel 与 TryGetLevelBand 同读这张表。</summary>
        private static readonly (ChestTier Tier, int MaxLevel)[] Bands =
        {
            (ChestTier.Bamboo, 5),
            (ChestTier.Celadon, 10),
            (ChestTier.Rosewood, 20),
            (ChestTier.Gilded, 30),
            (ChestTier.Vermilion, 40),
            (ChestTier.Crimson, int.MaxValue),
        };

        public static ChestTier TierForLevel(int level)
        {
            foreach (var band in Bands)
                if (level <= band.MaxLevel) return band.Tier;
            return Bands[Bands.Length - 1].Tier; // 不可达:末档上限是 int.MaxValue
        }

        /// <summary>这一档每级宝箱对应的等级区间(升级弹窗的「Lv.11–20」chip)。末档 max = int.MaxValue
        /// (开放);不作每级宝箱的档(素纸匣)返回 false。</summary>
        public static bool TryGetLevelBand(ChestTier tier, out int min, out int max)
        {
            min = 1;
            foreach (var band in Bands)
            {
                if (band.Tier == tier)
                {
                    max = band.MaxLevel;
                    return true;
                }
                min = band.MaxLevel + 1;
            }
            min = max = 0;
            return false;
        }

        /// <summary>每级宝箱从 Lv.2 起发;等级奖励轨道也从这一级起画。</summary>
        public const int TrackStart = 2;

        /// <summary>轨道末端至少比当前等级多出这么多格。</summary>
        private const int TrackLookahead = 5;

        public static bool HasChest(int level) => level >= TrackStart;

        public static bool IsChestClaimable(MetaState meta, int level) =>
            HasChest(level)
            && level <= MetaRules.CharacterLevel(meta.CharacterXp)
            && !meta.ClaimedLevelChests.Contains(level);

        /// <summary>Lv.2..当前等级里还没领的每级宝箱只数。已领集合里高于当前等级的值(手改存档)不计。</summary>
        public static int PendingChestCount(MetaState meta)
        {
            int level = MetaRules.CharacterLevel(meta.CharacterXp);
            int count = 0;
            for (int lv = TrackStart; lv <= level; lv++)
                if (!meta.ClaimedLevelChests.Contains(lv)) count++;
            return count;
        }

        /// <summary>领一只每级宝箱(spec 2026-10-19 §3),顺序任意。**只填空箱位、从不占暂存位**
        /// —— 暂存位留给爬塔结算箱。不可领或箱位已满时返回 false、不改任何状态。</summary>
        public static bool TryClaimChest(MetaState meta, int level,
            IReadOnlyList<string> cardPool, ITimeSource time)
        {
            if (!IsChestClaimable(meta, level)) return false;
            if (meta.Chests.Count >= ChestRules.SlotLimit) return false;
            if (!ChestRules.TryAwardChest(meta, TierForLevel(level), cardPool, time)) return false;
            meta.ClaimedLevelChests.Add(level);
            return true;
        }

        /// <summary>地图红点:有可领里程碑,或有待领宝箱。箱位满时照样算可领。</summary>
        public static bool HasClaimableReward(MetaState meta) =>
            MilestoneRules.HasClaimable(meta) || PendingChestCount(meta) > 0;

        /// <summary>轨道末端 = max(里程碑表末 Lv.50, 当前等级 + 5, 严格大于当前等级的下一个里程碑)。
        /// 表末兜底:低等级时也要看得到一路到 Lv.50 的里程碑与宝箱(2026-10-19 试玩)。</summary>
        public static int TrackEnd(MetaState meta)
        {
            int level = MetaRules.CharacterLevel(meta.CharacterXp);
            int next = level + 1;
            while (!MilestoneRules.ForLevel(next).HasValue) next++; // 50 级后每 10 级一个,必终止
            return Math.Max(MilestoneRules.TableEnd, Math.Max(level + TrackLookahead, next));
        }

        /// <summary>首次打开轨道时聚焦的等级:Lv.2 起第一个宝箱或里程碑可领的等级;都领完返回当前等级。</summary>
        public static int FocusLevel(MetaState meta)
        {
            int level = MetaRules.CharacterLevel(meta.CharacterXp);
            for (int lv = TrackStart; lv <= level; lv++)
                if (IsChestClaimable(meta, lv) || MilestoneRules.IsClaimable(meta, lv)) return lv;
            return level;
        }

        /// <summary>启动时调用:只在 LastSeenLevel 尚未初始化(0,老存档 / 新号)时对齐到当前等级;
        /// 已初始化则不动 —— 升级后弹窗没来得及弹就被挂起/杀进程,那次升级要留到下次弹窗再说。</summary>
        public static void SeedLastSeenLevel(MetaState meta)
        {
            if (meta.LastSeenLevel == 0)
                meta.LastSeenLevel = MetaRules.CharacterLevel(meta.CharacterXp);
        }

        public static LevelUpSummary TakeLevelUpSummary(MetaState meta)
        {
            int level = MetaRules.CharacterLevel(meta.CharacterXp);
            if (meta.LastSeenLevel == 0 || meta.LastSeenLevel >= level)
            {
                meta.LastSeenLevel = Math.Max(meta.LastSeenLevel, level);
                return null;
            }
            var summary = new LevelUpSummary { FromLevel = meta.LastSeenLevel, ToLevel = level };
            foreach (var m in MilestoneRules.MilestonesUpTo(level))
                if (m.Level > meta.LastSeenLevel) summary.Milestones.Add(m.Level);
            meta.LastSeenLevel = level;
            return summary;
        }
    }
}
