using System.Collections.Generic;

namespace Brushblade.Core
{
    /// <summary>技能效果类别(第 A 章:等级被动技能系统)。</summary>
    public enum PerkEffect { MaxHp, Shield, Library, Ap }

    /// <summary>单条技能定义:效果 = 等级 × PerLevelValue;角色等级只 gate 初解锁(0→1)。</summary>
    public sealed class PerkDef
    {
        public string Id { get; }
        public string Name { get; }
        public PerkEffect Effect { get; }
        public int PerLevelValue { get; }
        public int UnlockLevel { get; }              // 初次解锁所需角色等级
        public IReadOnlyList<int> InkCosts { get; }  // 索引=目标等级−1;长度=升级上限

        public PerkDef(string id, string name, PerkEffect effect, int perLevelValue,
            int unlockLevel, int[] inkCosts)
        {
            Id = id; Name = name; Effect = effect; PerLevelValue = perLevelValue;
            UnlockLevel = unlockLevel; InkCosts = inkCosts;
        }

        public int MaxLevel => InkCosts.Count;
    }

    /// <summary>技能表与聚合(首版基准,数值可调)。纯函数,状态进出。</summary>
    public static class PerkRules
    {
        public static readonly IReadOnlyList<PerkDef> All = new[]
        {
            new PerkDef("yangyuan", "养元", PerkEffect.MaxHp,  10, 2, new[] { 200, 400, 700, 1100, 1600, 2200 }),
            new PerkDef("jintang",  "金汤", PerkEffect.Shield,  2, 4, new[] { 400, 700, 1100, 1600, 2200 }),
            new PerkDef("bowen",    "博闻", PerkEffect.Library,  1, 6, new[] { 600, 1200, 2000 }),
            new PerkDef("yiqi",     "一气", PerkEffect.Ap,       1, 6, new[] { 1500, 4000 }), // 上限 2:平衡硬线
        };

        private static readonly Dictionary<string, PerkDef> ById = BuildIndex();

        private static Dictionary<string, PerkDef> BuildIndex()
        {
            var map = new Dictionary<string, PerkDef>();
            foreach (var p in All) map[p.Id] = p;
            return map;
        }

        public static PerkDef Get(string id) => ById[id];

        public static int PerkLevel(MetaState meta, string id) =>
            meta.PerkLevels.TryGetValue(id, out var lvl) ? lvl : 0;

        private static int BonusOf(MetaState meta, PerkEffect effect)
        {
            int sum = 0;
            foreach (var p in All)
                if (p.Effect == effect)
                    sum += PerkLevel(meta, p.Id) * p.PerLevelValue;
            return sum;
        }

        public static int ApBonus(MetaState meta)      => BonusOf(meta, PerkEffect.Ap);
        public static int HpBonus(MetaState meta)      => BonusOf(meta, PerkEffect.MaxHp);
        public static int LibraryBonus(MetaState meta) => BonusOf(meta, PerkEffect.Library);
        public static int ShieldBonus(MetaState meta)  => BonusOf(meta, PerkEffect.Shield);

        public static bool CanUpgradePerk(MetaState meta, string id)
        {
            var def = Get(id);
            int lvl = PerkLevel(meta, id);
            if (lvl >= def.MaxLevel) return false;                                    // 已满
            if (lvl == 0 && MetaRules.CharacterLevel(meta.CharacterXp) < def.UnlockLevel)
                return false;                                                          // 初解锁角色等级不足
            return meta.Ink >= def.InkCosts[lvl];                                      // 墨锭足够
        }

        public static bool TryUpgradePerk(MetaState meta, string id)
        {
            if (!CanUpgradePerk(meta, id)) return false;
            var def = Get(id);
            int lvl = PerkLevel(meta, id);
            meta.Ink -= def.InkCosts[lvl];
            meta.PerkLevels[id] = lvl + 1;
            return true;
        }
    }
}
