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
        // ---- D1 Task 7:我方侧选择器(不进 Supports;合法组合见 EffectPickRules.Allows) ----
        Self,               // 玩家自身(Cleanse:攻击面「我方清 1 个减益」,不要求友方目标)
        SummonedThisCast,   // 本次出字召出的召唤物(Endure:扎根)
        AllSummons,         // 全部存活召唤物(D2-0 Task 2,Taunt)
        // ---- D2-火 Task 1(附录 E2):敌方侧 ----
        Row,                // 主目标所在一排的存活敌人(主目标在前,其余按下标);以主目标为中心,**仍要选目标**
        Adjacent,           // 主目标 + 同排左右相邻(同 TargetArea.Adjacent 溅射,spec §3.2;不含上下排);同样要选目标
        BurnedByThisCast,   // 本次出字的 BurnSingle / BurnAll 落到过的敌人(烟熏、干涸等「带本字灼」)
    }

    /// <summary>哪些效果 Kind 认 <see cref="EffectDef.Pick"/> / 条件门 <see cref="EffectDef.OnlyIf"/>。
    /// 引擎、ConfigLoader、<c>EffectNeedsTarget</c> 共用这一份名单。</summary>
    public static class EffectPickRules
    {
        public static bool Supports(EffectKind kind) => kind switch
        {
            EffectKind.BurnSingle or EffectKind.Bleed or EffectKind.Freeze or EffectKind.Slow
                or EffectKind.ArmorBreak or EffectKind.Blind or EffectKind.Weaken
                or EffectKind.Seed or EffectKind.Vulnerable
                or EffectKind.BurnSettleNow or EffectKind.Detonate => true,
            _ => false,
        };

        /// <summary>这条效果能不能写这个选择器(ConfigLoader 校验用)。Primary 恒可;敌方侧选择器只给 Supports 列出的 kind;
        /// Self 给 Cleanse / Taunt;SummonedThisCast 给 Endure / Taunt;AllSummons 只给 Taunt;Taunt 必须写 pick。</summary>
        public static bool Allows(EffectKind kind, EffectPick pick) => pick switch
        {
            // 保命必须写 SummonedThisCast(Ruling 10):Primary 写法选不到召唤物(Endure 不在友方目标名单),会静默空转
            EffectPick.Primary => kind != EffectKind.Endure && kind != EffectKind.Taunt,
            EffectPick.Self => kind == EffectKind.Cleanse || kind == EffectKind.Taunt,
            EffectPick.SummonedThisCast => kind == EffectKind.Endure || kind == EffectKind.Taunt,
            EffectPick.AllSummons => kind == EffectKind.Taunt,
            // Reshape 带敌方侧选择器 = 重选目标(D2-火 E3):本面没有伤害时把主目标效果换成该选择器
            _ => Supports(kind) || kind == EffectKind.Reshape,
        };

        /// <summary>这条效果实际生效的选择器:旧 TargetAll 标志(Blind / Detonate 在用)视为 All。</summary>
        public static EffectPick Effective(EffectDef e) =>
            e.Pick != EffectPick.Primary ? e.Pick
            : e.TargetAll && Supports(e.Kind) ? EffectPick.All
            : EffectPick.Primary;
    }
}
