using System;
using System.Collections.Generic;

namespace Brushblade.Core
{
    /// <summary>一只真正入了箱位的每级宝箱。</summary>
    public readonly struct LevelChestGrant
    {
        public int Level { get; }
        public ChestTier Tier { get; }

        public LevelChestGrant(int level, ChestTier tier)
        {
            Level = level; Tier = tier;
        }
    }

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
        public static ChestTier TierForLevel(int level) => level switch
        {
            <= 5 => ChestTier.Bamboo,
            <= 10 => ChestTier.Celadon,
            <= 20 => ChestTier.Rosewood,
            <= 30 => ChestTier.Gilded,
            <= 40 => ChestTier.Vermilion,
            _ => ChestTier.Crimson,
        };

        /// <summary>把 LevelRewardGranted+1 .. 当前等级的宝箱逐只发出,**只填空箱位、从不占暂存位**
        /// —— 暂存位留给爬塔结算箱(结算箱优先;每级箱占了它,结算箱就会作废)。箱位满了就
        /// **停住、不推进账目**,那一级及之后欠着,下次调用再补。与战后掉箱「满了就作废」刻意不同:
        /// 升级奖励是确定的事。返回本次真正发出的那几只。</summary>
        public static List<LevelChestGrant> GrantLevelChests(MetaState meta,
            IReadOnlyList<string> cardPool, ITimeSource time)
        {
            var grants = new List<LevelChestGrant>();
            int level = MetaRules.CharacterLevel(meta.CharacterXp);
            for (int lv = meta.LevelRewardGranted + 1; lv <= level && meta.Chests.Count < ChestRules.SlotLimit; lv++)
            {
                var tier = TierForLevel(lv);
                ChestRules.TryAwardChest(meta, tier, cardPool, time); // 循环条件已保证有空位,必成功
                grants.Add(new LevelChestGrant(lv, tier));
                meta.LevelRewardGranted = lv;
            }
            return grants;
        }

        public static int OwedChests(MetaState meta) =>
            Math.Max(0, MetaRules.CharacterLevel(meta.CharacterXp) - meta.LevelRewardGranted);

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
