namespace Brushblade.Core
{
    /// <summary>特性在面印位上印哪一枚(Plan E4 附录 §B a3 / e3):攻 / 五行面 / 两面通用,
    /// 字形特性(拆字 / 成字)改印墨蓝「拆」/「合」—— 它们不是某一面的效果。</summary>
    public enum TraitSealKind
    {
        Attack,
        Feature,
        Both,
        Dismantle,
        Compose,
    }

    /// <summary>特性触发反馈的纯规则(飘字名签、升级预览、字卡详情两面条共用)。</summary>
    public static class TraitCueRules
    {
        /// <summary>判定顺序:拆字时 → Dismantle;成字时 → Compose;否则按 <see cref="TraitDef.Face"/>。</summary>
        public static TraitSealKind SealOf(TraitDef t)
        {
            if (t.Trigger == TraitTrigger.OnDismantle) return TraitSealKind.Dismantle;
            if (t.Trigger == TraitTrigger.OnCompose) return TraitSealKind.Compose;
            return t.Face switch
            {
                TraitFace.Attack => TraitSealKind.Attack,
                TraitFace.Feature => TraitSealKind.Feature,
                _ => TraitSealKind.Both,
            };
        }
    }
}
