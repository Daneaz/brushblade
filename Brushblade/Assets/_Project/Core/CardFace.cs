namespace Brushblade.Core
{
    /// <summary>字卡的两面(spec v7 §2.1):Attack = 攻击面(拖到敌人),Feature = 本系特色面。
    /// 数据上 Attack ↔ CharDef.AttackEffects,Feature ↔ CharDef.Effects。</summary>
    public enum CardFace
    {
        Feature,
        Attack,
    }
}
