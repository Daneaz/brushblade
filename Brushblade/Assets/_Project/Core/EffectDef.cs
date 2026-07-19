namespace Brushblade.Core
{
    /// <summary>出字效果类型(第 3 章 3.2.1;按流派需要逐步扩展)。</summary>
    public enum EffectKind
    {
        DamageSingle, // 单体伤害
        DamageAll,    // 全体伤害(AOE)
        BurnSingle,   // 单体灼烧(叠层)
        BurnAll,      // 全体灼烧(叠层)
        Shield,       // 自身护盾
        BurnPotency,  // 本场每层灼烧结算伤害 +Value(炽,可叠加,10.3.1)
        HealSelf,     // 治疗自身(不超上限;水系主打,2026-07-19 拍板)
        Summon,       // 召唤前排单位(Value=血量;木系主打,2026-07-19 拍板)
    }

    /// <summary>单条效果:伤害/护盾/治疗走生克结算,灼烧层数为平值。</summary>
    public sealed class EffectDef
    {
        public EffectKind Kind { get; }
        public int Value { get; }

        /// <summary>伤害类:目标带灼烧时基础值翻倍(灼,10.3.1)。</summary>
        public bool DoubleVsBurning { get; }

        /// <summary>护盾类:豁免一次回合末全清(堡,10.3.6)。</summary>
        public bool PersistOnce { get; }

        /// <summary>召唤类:召几个(林 = 2)。</summary>
        public int SummonCount { get; }

        /// <summary>召唤类:召唤物攻击力(回合末反击)。</summary>
        public int SummonAttack { get; }

        /// <summary>召唤类:召唤物显示字(林 → 木)。</summary>
        public string SummonChar { get; }

        public EffectDef(EffectKind kind, int value,
            bool doubleVsBurning = false, bool persistOnce = false,
            int summonCount = 1, int summonAttack = 0, string summonChar = "木")
        {
            Kind = kind;
            Value = value;
            DoubleVsBurning = doubleVsBurning;
            PersistOnce = persistOnce;
            SummonCount = summonCount;
            SummonAttack = summonAttack;
            SummonChar = summonChar;
        }
    }
}
