using System;
using System.Collections.Generic;

namespace Brushblade.Core
{
    public readonly struct LevelChestGrant
    {
        public int Level { get; }
        public ChestTier Tier { get; }
        public ChestAward Award { get; }

        public LevelChestGrant(int level, ChestTier tier, ChestAward award)
        {
            Level = level; Tier = tier; Award = award;
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

        /// <summary>把 LevelRewardGranted+1 .. 当前等级的宝箱逐只发出。箱位与暂存都满(Lost)时
        /// **停住、不推进账目** —— 那一级及之后欠着,下次调用再补。与战后掉箱「满了就作废」刻意不同:
        /// 升级奖励是确定的事。返回本次真正发出的那几只(不含 Lost)。</summary>
        public static List<LevelChestGrant> GrantLevelChests(MetaState meta,
            IReadOnlyList<string> cardPool, ITimeSource time)
        {
            var grants = new List<LevelChestGrant>();
            int level = MetaRules.CharacterLevel(meta.CharacterXp);
            for (int lv = meta.LevelRewardGranted + 1; lv <= level; lv++)
            {
                var tier = TierForLevel(lv);
                if (meta.Chests.Count >= ChestRules.SlotLimit && meta.PendingChests.Count >= ChestRules.PendingLimit)
                    break; // 先判再发:AwardOrHold 返回 Lost 时不会改状态,但这里提前停更直白
                var award = ChestRules.AwardOrHold(meta, tier, cardPool, time);
                grants.Add(new LevelChestGrant(lv, tier, award));
                meta.LevelRewardGranted = lv;
            }
            return grants;
        }

        public static int OwedChests(MetaState meta) =>
            Math.Max(0, MetaRules.CharacterLevel(meta.CharacterXp) - meta.LevelRewardGranted);

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
