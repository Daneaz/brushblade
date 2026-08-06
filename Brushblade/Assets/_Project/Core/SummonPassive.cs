namespace Brushblade.Core
{
    /// <summary>召唤物被动(2026-08-05,子项目 C):随 Summon 效果整体拷进 SummonState,
    /// 伴随召唤物生死并跟着存档往返。
    ///
    /// 属性用 { get; set; } 而不是只读 —— 它同时是存档 POCO,Newtonsoft 得写得进去
    /// (与 StatusEffect 同构)。运行时从不修改它,Clone() 只为杜绝快照与实体共享引用。
    ///
    /// 「光环」与「攻击附灼烧」是同一个字段对:烓(攻 0、OnHitBurn 3、全体)、
    /// 灶(攻 0、OnHitBurn 2、单体)、楸(攻 6、OnHitBurn 1、单体)。攻 0 的召唤物
    /// 照常走出手循环,输出全靠 OnHitBurn。</summary>
    public sealed class SummonPassive
    {
        /// <summary>基础速度。≤0 视为 100(缺省与老存档兜底都走这条)。桤 = 150。</summary>
        public int Speed { get; set; }

        /// <summary>被打时反弹给攻击者的固定伤害,不走生克。荆 = 3。</summary>
        public int Thorns { get; set; }

        /// <summary>每回合给玩家 + 全部存活召唤物回血,与出手无关。桃 = 3。</summary>
        public int HealAlly { get; set; }

        /// <summary>出手时额外挂的灼烧层数。烓 = 3 / 灶 = 2 / 楸 = 1。</summary>
        public int OnHitBurn { get; set; }

        /// <summary>true = OnHitBurn 挂给全部存活敌人;false = 只挂被打的那只。烓 = true。</summary>
        public bool OnHitBurnAll { get; set; }

        /// <summary>出手时给目标挂的诅咒减攻百分比。槐 = 25。</summary>
        public int OnHitCurse { get; set; }

        public SummonPassive Clone() => new()
        {
            Speed = Speed, Thorns = Thorns, HealAlly = HealAlly,
            OnHitBurn = OnHitBurn, OnHitBurnAll = OnHitBurnAll, OnHitCurse = OnHitCurse,
        };
    }
}
