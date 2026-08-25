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

        /// <summary>被攻击时的闪避百分比(2026-08-07,柳)。与攻击者的致盲相加后
        /// 一起从命中率里扣。</summary>
        public int Dodge { get; set; }

        /// <summary>入场冻结(2026-08-25,藤):这张字召唤落位时,冻结**一个**随机存活敌人
        /// N 回合;0 = 无。
        ///
        /// 与本类其余被动的触发时机都不同 —— 它们是「出手时 / 被打时 / 每回合」,只有它是
        /// **入场一次性**。所以它挂在召唤物身上纯粹是为了随配置读进来,运行期召唤物自己
        /// 不再读它;真正的触发点在 <c>BattleEngine.ApplyEffects</c> 的 Summon 分支末尾。
        ///
        /// ⚠ 一张字召多只时**只触发一次** —— 冻结是「这张字」的效果,不是每只各冻一个。
        /// 否则 藤 将来若升到召 2 只,会静默变成群控。</summary>
        public int OnSummonFreeze { get; set; }

        /// <summary>出手时冻结目标的**概率**(百分点,2026-08-25,藤)。0 = 不冻。
        /// 与 <see cref="OnSummonFreeze"/> 是两回事:那个是入场一次性,这个是每次出手都摇。
        ///
        /// ⚠ 这个字段与下面 OnHitSlow 两项**吃卡等级** —— 推翻了 2026-08-05 的
        /// 「被动数值不吃卡等级」(那条把反伤/灼烧层/减攻百分比定为不随等级变的「节奏」)。
        /// 2026-08-25 用户拍板:藤的冻结概率、蕉的减速幅度都要随卡等级成长。
        /// 缩放在**召唤那一刻**做完并写进这份拷贝(与 Attack 同为快照语义),
        /// 运行期不再看等级 —— 之后再升级卡,已在场的这只不变。</summary>
        public int OnHitFreezeChance { get; set; }

        /// <summary>出手冻结命中时的回合数。≤0 视为 1。</summary>
        public int OnHitFreezeTurns { get; set; }

        /// <summary>出手时给目标挂的减速幅度(速度点数,正数;施加时取负,2026-08-25,蕉)。0 = 不减速。</summary>
        public int OnHitSlowPercent { get; set; }

        /// <summary>出手减速的持续回合数。≤0 视为 1。</summary>
        public int OnHitSlowTurns { get; set; }

        /// <summary>远程(2026-08-20):出手时无视敌方前排,优先打后排。灶 / 烓 = true。
        /// 与「站哪一槽」无关——排位只决定被不被够到,后排的近战召唤物照常打前排。</summary>
        public bool Ranged { get; set; }

        /// <summary>出手时的目标形状(2026-08-22,spec §7)。缺省 Single = 只打一只,
        /// 与改造前逐位等价。
        ///
        /// 与 <see cref="Ranged"/> **正交**:Ranged 管「能不能越过前排」,Shape 管「打几个」。
        /// 合并成一个枚举会让「远程的顺劈」这种合理组合表达不出来 ——
        /// 与 EnemyAbility 当年把 Range 塞进去的教训同型(EnemyDef.cs 的注释)。</summary>
        public TargetShape Shape { get; set; }

        /// <summary>非主目标的伤害百分比。≤0 视为 100。</summary>
        public int ShapePercent { get; set; }

        /// <summary>连发发数(Shape = Volley 时有意义)。</summary>
        public int Shots { get; set; }

        public SummonPassive Clone() => new()
        {
            Speed = Speed, Thorns = Thorns, HealAlly = HealAlly,
            OnHitBurn = OnHitBurn, OnHitBurnAll = OnHitBurnAll, OnHitCurse = OnHitCurse,
            Dodge = Dodge, Ranged = Ranged, OnSummonFreeze = OnSummonFreeze,
            OnHitFreezeChance = OnHitFreezeChance, OnHitFreezeTurns = OnHitFreezeTurns,
            OnHitSlowPercent = OnHitSlowPercent, OnHitSlowTurns = OnHitSlowTurns,
            Shape = Shape, ShapePercent = ShapePercent, Shots = Shots,
        };
    }
}
