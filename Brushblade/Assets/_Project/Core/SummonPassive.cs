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

        /// <summary>被打时反弹给攻击者的伤害,单位是**受到伤害的百分点**(2026-08-25 由固定
        /// 点数改成百分比,与玩家侧 StatusKind.Reflect 同口径)。荆 = 50、碉 = 20。
        /// 反弹本身不走生克,也不吃敌人护甲。</summary>
        public int Thorns { get; set; }

        /// <summary>每回合给玩家 + 全部存活召唤物回血,与出手无关。桃 = 3。</summary>
        public int HealAlly { get; set; }

        /// <summary>每回合给**自己**回血(2026-09-05,藻)。0 = 无。
        ///
        /// 与 <see cref="HealAlly"/> 是一对,别合并:那个是「给玩家 + 全部存活召唤物」的光环,
        /// 这条只作用于持有者本身。藻 由用户指定做自愈型召唤,不外溢。
        ///
        /// 不吃卡等级 —— 回归 2026-08-05 的「被动数值是节奏,不随等级变」缺省
        /// (吃等级的只有 OnHitFreezeChance / OnHitSlow 两项,是 2026-08-25 的显式例外)。</summary>
        public int Regen { get; set; }

        /// <summary>攻击光环:给场上**全部**召唤物 +N 攻,**含自己**(2026-09-05,𣛧)。
        ///
        /// 「含自己」是必要的配套:2026-09-05 召唤只数收归 1 只后,只加别人的光环
        /// 在场上没有作用对象、纯空转。同场打出别的召唤字时光环照常外溢。
        ///
        /// 与 <see cref="EffectDef.SummonShield"/> 的区别:那个是**召唤当时**发一次性护盾,
        /// 这条是**持续**加成 —— 由 <c>SummonState.EffectiveAttack</c> 每次出手时现读,
        /// 所以后来入场的召唤物也吃得到。
        ///
        /// 不吃卡等级 —— 同 <see cref="Regen"/>,回归「被动数值是节奏,不随等级变」的缺省。</summary>
        public int AuraAttack { get; set; }

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

        /// <summary>嘲讽(2026-08-25,荆/堡):在场时敌人的攻击**强制**落到它身上。
        ///
        /// 这条是「攻 0、靠挨打反伤输出」那套 build 的前提 —— 没有它,反伤肉盾挨不挨打
        /// 完全由 <see cref="Targeting.PickAllyTarget"/> 决定(2026-09-13 起:够得着的那一段里
        /// 嘲讽优先、段内均匀随机),输出量不受玩家控制。
        ///
        /// **排位在先,嘲讽在后**(2026-09-03 用户拍板,推翻 2026-08-25 的「压过一切排位」):
        /// 嘲讽只在「排位已经放行的那一段」里生效 —— 近战前排有人时就只在前排里挑,
        /// 后排的嘲讽者要等前排清空才够得着。它压过的只剩 Focus.Player 的死盯玩家。
        /// 同一段里有多只嘲讽物才走均匀随机。</summary>
        public bool Taunt { get; set; }

        /// <summary>远程(2026-08-20):出手时无视敌方前排,优先打后排。灶 / 烓 = true。
        /// 与「站哪一槽」无关——排位只决定被不被够到,后排的近战召唤物照常打前排。</summary>
        public bool Ranged { get; set; }

        /// <summary>出手时的目标形状(2026-08-22,spec §7)。缺省 Single = 只打一只,
        /// 与改造前逐位等价。
        ///
        /// 与 <see cref="Ranged"/> **正交**:Ranged 管「能不能越过前排」,Shape 管「打几个」。
        /// 合并成一个枚举会让「远程的溅射」这种合理组合表达不出来 ——
        /// 与 EnemyAbility 当年把 Range 塞进去的教训同型(EnemyDef.cs 的注释)。</summary>
        public TargetArea Shape { get; set; }

        /// <summary>非主目标的伤害百分比。≤0 视为 100。</summary>
        public int ShapePercent { get; set; }

        /// <summary>连发发数(Shape = Volley 时有意义)。</summary>
        public int Shots { get; set; }

        // ---- D2-0 Task 6:本命新字段(spec §9 木,缺省 0 = 无;存档 POCO,Clone 必须同步)----

        /// <summary>远射·强化:出手打到后排目标时伤害 +N%。</summary>
        public int BackRowBonusPercent { get; set; }

        /// <summary>成林:每有 1 只**其他**存活木灵,有效攻击 +N%(按基础攻击折算,与 AttackBuff、光环同在加法段)。</summary>
        public int PerAllyAttackPercent { get; set; }

        /// <summary>坚木:入场自带护甲(DefenseBuff,随单位存在)。吃卡等级,同 EffectDef.SummonDefense。</summary>
        public int Armor { get; set; }

        /// <summary>桂香·强化:<see cref="HealAlly"/> 每回合结算的次数;≤0 视为 1。</summary>
        public int HealAllyTimes { get; set; }

        /// <summary>丛生:本木灵每拍开始时分裂 1 只小藻,血 / 攻 = 本木灵 × N%。</summary>
        public int SproutPercent { get; set; }

        /// <summary>丛生:本木灵名下同时存活的小藻上限。</summary>
        public int SproutMax { get; set; }

        /// <summary>森然:入场时附带的幼苗只数(属性 = 本木灵 × 20%)。</summary>
        public int EntrySaplings { get; set; }

        /// <summary>迷香:出手魅惑目标 1 回合的概率(百分点)。吃卡等级,钳 100;字段为 0 时绝不摇骰。</summary>
        public int OnHitCharmChance { get; set; }

        // ---- D2-木 Task 1:本命新字段(预留,Ruling 17;缺省 0 / false = 无;结算归 Task 2–4,附录 M2–M5 / M11 / E37)----
        // 写法统一是 `Summon 0`(value 0 = 0 血/攻 0,count 1)+ 字段 token,由 TraitRules.Fold 的本命追加(E37,只认 value == 0)并进本体召唤。

        /// <summary>参天之力:有效攻击 + 基础攻击 × N%(与成林、M1 光环同在加法段;SummonState.TraitAttackBonus 读)。</summary>
        public int AttackPercent { get; set; }
        /// <summary>馥郁:桂香治疗量 × (100 + N)/100(ActSummonTurn 读)。</summary>
        public int HealAllyPercent { get; set; }
        /// <summary>水草:顶层出字的实际治疗量(不含给藻灵自己的)× N% 给本藻灵。Task 4</summary>
        public int HealMirror { get; set; }
        /// <summary>披荆:荆棘反弹 &gt; 0 时给攻击者挂流血 N(召唤时吃卡等级)。Task 3</summary>
        public int ThornsBleed { get; set; }
        /// <summary>蟾宫:我方暴击率 + N(取在场最大值;木灵暴击以此为闸门,BattleEngine.SummonCritAura 读)。</summary>
        public int CritAura { get; set; }
        /// <summary>林荫:我方减伤 = min(GroveCutCap, N × 存活木灵数),与 DamageCut 取最强(BattleEngine.WoodCutPercent 读)。</summary>
        public int GroveCut { get; set; }
        /// <summary>林荫:减伤上限;0 = 不设上限(仍受 60% 非护甲减伤封顶)。</summary>
        public int GroveCutCap { get; set; }
        /// <summary>森严:存活木灵数 ≥ ThicketMin 时木灵受伤 −N%(只给木灵),与 DamageCut / 林荫取最强。</summary>
        public int ThicketCut { get; set; }
        /// <summary>森严:生效所需的存活木灵数(含森灵自己与幼苗)。</summary>
        public int ThicketMin { get; set; }
        /// <summary>守护:玩家受到的挥击伤害 × N% 转给本木灵。Task 3</summary>
        public int Guard { get; set; }
        /// <summary>良弓:被挥击命中后对攻击者造成 SummonDefense × N%。Task 3</summary>
        public int ReturnFire { get; set; }
        /// <summary>落木:阵亡时对全体敌人造成有效攻击 × N%。Task 3</summary>
        public int DeathBlast { get; set; }
        /// <summary>藻荇:名下小藻阵亡时给 killer 挂减速 N 回合。Task 3</summary>
        public int SproutDeathSlow { get; set; }
        /// <summary>秋实:阵亡时部件池 +木(满池不入)。Task 3</summary>
        public bool DeathPart { get; set; }

        public SummonPassive Clone() => new()
        {
            Speed = Speed, Thorns = Thorns, HealAlly = HealAlly, Regen = Regen,
            AuraAttack = AuraAttack,
            OnHitBurn = OnHitBurn, OnHitBurnAll = OnHitBurnAll, OnHitCurse = OnHitCurse,
            Dodge = Dodge, Ranged = Ranged, Taunt = Taunt, OnSummonFreeze = OnSummonFreeze,
            OnHitFreezeChance = OnHitFreezeChance, OnHitFreezeTurns = OnHitFreezeTurns,
            OnHitSlowPercent = OnHitSlowPercent, OnHitSlowTurns = OnHitSlowTurns,
            Shape = Shape, ShapePercent = ShapePercent, Shots = Shots,
            BackRowBonusPercent = BackRowBonusPercent, PerAllyAttackPercent = PerAllyAttackPercent,
            Armor = Armor, HealAllyTimes = HealAllyTimes, SproutPercent = SproutPercent, SproutMax = SproutMax,
            EntrySaplings = EntrySaplings, OnHitCharmChance = OnHitCharmChance,
            AttackPercent = AttackPercent, HealAllyPercent = HealAllyPercent, HealMirror = HealMirror, ThornsBleed = ThornsBleed,
            CritAura = CritAura, GroveCut = GroveCut, GroveCutCap = GroveCutCap, ThicketCut = ThicketCut, ThicketMin = ThicketMin,
            Guard = Guard, ReturnFire = ReturnFire, DeathBlast = DeathBlast, SproutDeathSlow = SproutDeathSlow, DeathPart = DeathPart,
        };
    }
}
