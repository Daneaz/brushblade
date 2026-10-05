namespace Brushblade.Core
{
    /// <summary>效果目标选择器(D1 Task 5,附录 M10):一条敌方侧效果「落在谁身上」。
    /// 与 <see cref="TargetArea"/>(伤害的形状)正交,也与玩家点选的主目标正交:
    /// Primary = 沿用现行为(玩家选的那只);其余几档不需要玩家选目标。
    /// ⚠ 序数只追加(chars.json 按名字解析、不进存档,但仍保持追加习惯)。</summary>
    public enum EffectPick
    {
        Primary,            // 缺省:玩家选的主目标(现行为)
        All,                // 全部存活敌人(与旧 TargetAll 标志等价)
        Random,             // 随机一名存活敌人(摇特性随机流 _traitRandom)
        HitTargets,         // 本次出字被 DamageSingle 命中的敌人,按命中顺序去重
        MostBurn,           // 灼层最高的存活敌人,同层取下标小;没人带灼 = 空
        FrozenByThisCast,   // 本次出字真正冻结成功的敌人(Boss 的冰滞不算冻结)
    }

    /// <summary>哪些效果 Kind 认 <see cref="EffectDef.Pick"/> / 条件门 <see cref="EffectDef.OnlyIf"/>。
    /// 引擎、ConfigLoader、<c>EffectNeedsTarget</c> 共用这一份名单。</summary>
    public static class EffectPickRules
    {
        public static bool Supports(EffectKind kind) => kind switch
        {
            EffectKind.BurnSingle or EffectKind.Bleed or EffectKind.Freeze or EffectKind.Slow
                or EffectKind.ArmorBreak or EffectKind.Blind or EffectKind.Weaken
                or EffectKind.BurnSettleNow or EffectKind.Detonate => true,
            _ => false,
        };

        /// <summary>这条效果实际生效的选择器:旧 TargetAll 标志(Blind / Detonate 在用)视为 All。</summary>
        public static EffectPick Effective(EffectDef e) =>
            e.Pick != EffectPick.Primary ? e.Pick
            : e.TargetAll && Supports(e.Kind) ? EffectPick.All
            : EffectPick.Primary;
    }
}
