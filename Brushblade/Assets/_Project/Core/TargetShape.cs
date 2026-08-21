namespace Brushblade.Core
{
    /// <summary>伤害的目标形状(2026-08-22,spec §3)。与 <see cref="EffectKind"/> **正交** ——
    /// 它回答「打谁」,EffectKind 回答「做什么」。
    ///
    /// 做成修饰字段而不是五个新的 EffectKind:那样 ApplyEffects 里含斩杀/暴击/护甲/多段
    /// 四层逻辑的伤害循环要复制五份,而 NeedsTarget / RestrictedToFrontRow / CanTarget
    /// 三处白名单要各加一笔 —— 2026-08-06 单体驱散漏在白名单外导致 _enemies[-1] 越界崩溃,
    /// 记的就是这类账(BattleEngine.cs 的 NeedsTarget 注释)。
    ///
    /// ⚠ 没有 All:「全体」是 <see cref="EffectKind.DamageAll"/> 这个独立 kind,不并进来。
    /// 21 张全体字因此一个字节不改,改造期间「现有伤害逐位不变」这条守卫才始终可断言。</summary>
    public enum TargetShape
    {
        Single, // 单体:只打主目标(缺省)
        Sweep,  // 横扫:主目标所在整排(≤3)
        Cleave, // 顺劈:主目标 + 同排左右相邻(≤3);打边格只溅一侧
        Skewer, // 贯穿:主目标所在整列,前排 + 后排(≤2)。中文叫「贯穿」,代码不叫 Pierce ——
                // EffectDef.Pierce 是护甲穿透点数,两者在同一个类上并存极易读错
        Volley, // 连发:后排优先按列序取,不足 N 则**循环补足**。无主目标,不进选目标态
    }
}
