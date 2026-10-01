using System.Collections.Generic;

namespace Brushblade.Core
{
    public readonly struct MilestoneDef
    {
        public int Level { get; }
        public int Ink { get; }
        public CardRarity Rarity { get; }

        public MilestoneDef(int level, int ink, CardRarity rarity)
        {
            Level = level; Ink = ink; Rarity = rarity;
        }
    }

    /// <summary>等级里程碑(spec 2026-10-02 §2.2 / §5.3):每 5 级一档到 Lv.50,之后每 10 级一档无限延续。
    /// 奖励 = 墨锭 + 该稀有度字卡 3 选 1。3 选 1 **不走开箱、不推进保底计数**。</summary>
    public static class MilestoneRules
    {
        public const int OfferSize = 3;
        public const int TableEnd = 50;
        public const int AfterStep = 10;
        private static readonly MilestoneDef After = new(0, 3000, CardRarity.Orange);

        private static readonly MilestoneDef[] Table =
        {
            new(5, 300, CardRarity.Blue),
            new(10, 500, CardRarity.Purple),
            new(15, 800, CardRarity.Purple),
            new(20, 1200, CardRarity.Gold),
            new(25, 1500, CardRarity.Gold),
            new(30, 2000, CardRarity.Gold),
            new(35, 2500, CardRarity.Orange),
            new(40, 3000, CardRarity.Orange),
            new(45, 3500, CardRarity.Orange),
            new(50, 5000, CardRarity.Red),
        };

        public static MilestoneDef? ForLevel(int level)
        {
            foreach (var m in Table)
                if (m.Level == level) return m;
            if (level > TableEnd && (level - TableEnd) % AfterStep == 0)
                return new MilestoneDef(level, After.Ink, After.Rarity);
            return null;
        }

        public static List<MilestoneDef> MilestonesUpTo(int level)
        {
            var list = new List<MilestoneDef>();
            foreach (var m in Table)
                if (m.Level <= level) list.Add(m);
            for (int lv = TableEnd + AfterStep; lv <= level; lv += AfterStep)
                list.Add(new MilestoneDef(lv, After.Ink, After.Rarity));
            return list;
        }
    }
}
