namespace Brushblade.Core
{
    /// <summary>出字效果类型(第 3 章 3.2.1;首批为阶段 1 火/土需要的子集,按流派逐步扩展)。</summary>
    public enum EffectKind
    {
        DamageSingle, // 单体伤害
        DamageAll,    // 全体伤害(AOE)
        BurnSingle,   // 单体灼烧(叠层)
        BurnAll,      // 全体灼烧(叠层)
        Shield,       // 自身护盾
    }

    /// <summary>单条效果:伤害/护盾走生克结算,灼烧层数为平值。</summary>
    public sealed class EffectDef
    {
        public EffectKind Kind { get; }
        public int Value { get; }

        public EffectDef(EffectKind kind, int value)
        {
            Kind = kind;
            Value = value;
        }
    }
}
