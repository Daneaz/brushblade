namespace Brushblade.Core
{
    /// <summary>战斗硬上限集中地(spec v7 §5.2 / §11.7)。
    /// 新上限由使用它的任务各自追加到这里,不在引擎里散落字面量。</summary>
    public static class CombatCaps
    {
        public const int MoraleStacks = 5;
        public const int HeftStacks = 10;
        public const int WellspringStacks = 10;

        /// <summary>非护甲减伤合计上限(百分点,spec v7 §5.2.4)。格挡的 −40% 取 min(40, 本值)。</summary>
        public const int NonArmorReductionPercent = 60;

        /// <summary>反伤总量上限(百分点,2026-09-05;2026-09-06 纳入荆棘 Thorns)。
        ///
        /// 此前刻意不钳位,理由是「字表只有一个 Reflect 字,多来源叠加现实不可达」;
        /// P2 让 壁(绿 30%)与 圭(金 50%)同时存在,那条前提失效。
        /// 60 的依据:30 层一轮敌方总伤 936,×60% = 562 ≈ 红档单攻锚点 600 ——
        /// 「站着挨满一整轮」的反伤收益约等于一张红档输出字(设计稿 §1.6)。
        ///
        /// 2026-09-06 前只钳了 Reflect,漏了召唤物的荆棘(DamageSummon 里独立的第二次弹射)——
        /// 「玩家壁 + 召唤物壁(钳到 60)+ 荆棘 50%」这条打召唤物的管道仍能反弹 > 100%。
        /// 现在两者合占同一份 60%,分配顺序「荆棘先扣满,反弹拿剩余」见 DamageSummon 里的注释。</summary>
        public const int ReflectPercent = 60;
    }
}
