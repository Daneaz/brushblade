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
    }

    /// <summary>单条效果:伤害/护盾走生克结算,灼烧层数为平值。</summary>
    public sealed class EffectDef
    {
        public EffectKind Kind { get; }
        public int Value { get; }

        /// <summary>伤害类:目标带灼烧时基础值翻倍(灼,10.3.1)。</summary>
        public bool DoubleVsBurning { get; }

        /// <summary>护盾类:豁免一次回合末全清(堡,10.3.6)。</summary>
        public bool PersistOnce { get; }

        public EffectDef(EffectKind kind, int value,
            bool doubleVsBurning = false, bool persistOnce = false)
        {
            Kind = kind;
            Value = value;
            DoubleVsBurning = doubleVsBurning;
            PersistOnce = persistOnce;
        }
    }
}
