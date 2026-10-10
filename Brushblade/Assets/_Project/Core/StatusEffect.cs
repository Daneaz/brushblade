using System.Collections.Generic;
using System.Linq;

namespace Brushblade.Core
{
    /// <summary>状态种类(2026-08-04)。护盾不在此列——它是资源不是状态,
    /// 有独立吸伤顺序与跨段规则,驱散/净化本就不该碰它。
    ///
    /// ⚠ **新值一律追加在末尾**(2026-08-12,E-b2):StatusEffect 随
    /// EndlessSaveState.CarriedStatuses / BattleSnapshot.PlayerStatuses 进 JSON,
    /// 而 SaveSerializer 没有注册 StringEnumConverter —— Newtonsoft 默认把枚举
    /// 序列化成**整数**。在中间插值会让所有旧存档里的状态整体错位(减伤变成破甲之类),
    /// 而且是静默的:单元测试全都建新对象,没有一条读旧 JSON 字节。
    /// 序号由 CritStatTests.StatusKindOrdinals_AreLockedForSaveCompatibility 锁住。</summary>
    public enum StatusKind
    {
        Burn,             // 灼烧层数
        Bleed,            // 流血:每回合固定伤害
        Freeze,           // 冻结:跳过行动
        SpeedModifier,    // 速度增减(点数,可正可负)
        HealOverTime,     // 持续治疗
        ObsoleteDamageReduction, // ⚠ 废弃占位,序号 5,**不得删除、不得复用**(2026-08-12,E-b4 T3)。
                          // 原为「减伤百分比」的乘法层,已随点数护甲上线删除,载体换成末尾的
                          // DefenseBuff(18)。不就地改名复用是刻意的:单位从**百分点**变成**点数**
                          // 而序号不变,正是静默存档损坏的那一类 —— 旧存档里的「5, 20」会被读成
                          // 「护甲 +20 点」而不是「减伤 20%」。删掉它则 6 以后全部前移,更糟。
        AttackBuff,       // 攻击加成:**百分点,敌我一致**(2026-08-12 统一;敌人 = BaseAttack 的百分比,
                          // 玩家 = 以 AttackBaseline 100 为基准的百分点)。量级 ×10 时它是比值,永不跟着乘
        ArmorBreak,       // 破甲:目标护甲 −Magnitude **点**,本场持久(TurnsLeft = -1)、**可叠加**
                          // (2026-08-05 曾是「承伤 +25%,不叠层」的乘法代偿;2026-08-12 E-b4 T3 复原。
                          //  名字与序号 7 一动不动 —— 语义变了但载体是同一条,存档安全)
        Curse,            // 诅咒:攻击 −Magnitude%,不叠层只刷新(2026-08-05)
        Seal,             // 封字:玩家下回合 AP −Magnitude(2026-08-06,Boss 倾覆)
        Immunity,         // 免疫:完全挡下 Magnitude 次伤害(2026-08-06)
        Blind,            // 致盲:该敌人攻击的命中率 −Magnitude%(2026-08-07)
        Silence,          // 玩家可见名「封禁」(枚举名/序号不动——同上面 ObsoleteDamageReduction
                          // 那条纪律,StatusKind 序号锁存档兼容,语义变了载体不变)。2026-08-07
                          // 原语义仅「该敌人的主动机制全部哑火」;2026-09-05 裁定扩成「特殊能力
                          // 全部失效」:杂兵护甲归零 + 被动不触发 + 主动机制哑火,**Boss 降级**
                          // 为只削「护甲减半」,大招与被动不受影响(同斩杀对 Boss 从直杀降为
                          // 吃双倍一个纪律)
        Reflect,          // 反弹:把打到玩家的伤害按 Magnitude% 照回攻击者(2026-08-07)
        BurnNoDecay,      // 不灭:该敌人身上的灼烧层数不再每回合衰减(2026-08-09)
        Morale,           // 战意:Magnitude = **层数**(不是攻击加成值),每层 **+10% 攻击**,上限 5 层
                          // (2026-08-12 上线时是 +10 点;2026-08-25 用户拍板改成百分比)
        ApBoost,          // 玩家每回合 AP 上限加成(2026-08-12,利)
        CritBuff,         // 玩家暴击率加成(百分点,2026-08-12,锋)
        DefenseBuff,      // 玩家护甲加成(**点数**,2026-08-12,E-b4 T2):进 EffectiveDefense 的加数
        PierceBuff,       // 玩家穿透加成(点数,2026-08-12,E-b4 T2):进 EffectiveDefense 的减数,本场持续
        DodgeBuff,        // 玩家闪避加成(**百分点**,2026-08-12,E-b4 T4):进 EffectiveDodge 的加数。
                          // 序号 20 而非 spec §11.3 预留的 18 —— T2 的 DefenseBuff/PierceBuff 先合流,
                          // 按「新值一律追加在末尾」顺延,锁值测试写的是实际值
        Heft,         // 厚(2026-09-02,土):Magnitude = **层数**,每层 +5% 伤害,上限 10(可由土脉 L4 抬到 14)。
                          // 来源是**获得护盾的量**(不是挨打),每攒够 MaxHp/10 涨一层。
                          // 与 Morale 同型:Magnitude 是层数不是加成值,进 EffectiveAttack 的
                          // 百分比乘区;混进 AttackBuff 会既丢层数上限又让 +1 层被当成 +1 攻击。
        Wellspring,       // 泉(2026-09-02,水):Magnitude = **层数**,每层 +5% 治疗,上限 10(可由水脉 L4 抬到 14)。
                          // 来源是治疗的**名义值**(不是实际回血)——满血溢出照样攒,
                          // 这正是「满血奶自己不亏」那条诉求的落点。
        Charm,            // 魅惑:持有者攻击自己阵营(2026-09-05,花)。Magnitude 不用,只看 TurnsLeft。
        Block,            // 格挡(spec v7 §3.1,铠):Magnitude = **剩余次数**,CounterDamage = 每次反击的伤害,
                          // TurnsLeft = -1(本场有效,用完为止)。下一次敌人挥击 −40% 并反击;同类取最强不叠加。
        FrostResist,      // 霜抗(spec v7 R1,仅敌人):冻结结束后挂上,TurnsLeft = 刚结束那次冻结的回合数,按敌人行动递减;
                          // 期间不能被冻结。Freeze 的 Magnitude 同时记下冻结时长(= 施加时的 TurnsLeft),供结束时发霜抗。
        IceStall,         // 冰滞(spec v7 R1b,仅 Boss):Boss 被冻结时改挂本状态 —— 行动条后退半格(可为负)、
                          // 下次行动前受伤 +15%。Magnitude = 本该冻结的回合数 N,TurnsLeft = -1;
                          // Boss 下次行动开始时移除并挂霜抗 N+1(本拍末尾 TickTurns 减 1)。
        Seed,             // 种(spec v7 §3.1,D1 Task 6,仅敌人):Magnitude = 每次回复量,TurnsLeft = 回合,SourceId = 字 ID。
                          // 该敌人每次行动开始(含被冻结 / 冰滞跳过的那一拍),我方生命**比例**最低的单位回复 Magnitude。
                          // 同源刷新取较大量、较长回合;不同来源并存、各治一次。
        Vulnerable,       // 标记(spec v7 §3.1,D1 Task 6,仅敌人):受到的 DamageEnemy 伤害 +Magnitude%(多个来源只取最强的一份),
                          // TurnsLeft 按该敌人行动递减。在冰滞易伤之后、护甲之前,分别整数取整;灼烧 / 流血不走 DamageEnemy,不吃。
                          // 同源刷新取较强值与较长回合。
        DamageCut,        // 本回合减伤(D1 Task 7,挂在玩家身上,作用于玩家**与全部召唤物**):Magnitude = 百分点,
                          // TurnsLeft = 1(玩家回合开始的 tick 到期)。多个来源只取最强的一份(spec §5.2 第 1 律);
                          // 玩家侧与格挡 40% 合计、召唤物侧单独,都钳到 CombatCaps.NonArmorReductionPercent。
        CounterBoost,     // 反击增强(D1 Task 7,仅玩家):格挡反击 ×(100 + Magnitude)/100,TurnsLeft = 1;
                          // 多个来源只取最强的一份;仍在 60% 反伤预算内钳。
        Endure,           // 保命(D1 Task 7,仅召唤物):DamageSummon 致命一击留 1 血并移除本状态,TurnsLeft = -1。吞噬不吃。
        TraitRider,       // 附着载体(D1 Task 9,附录 M9,隐藏,不画 chip):SourceId = 字 ID,TraitKey = 特性键,
                          // Magnitude = 载体 StatusKind 的 int(D1 只有 Burn)。载体从单位身上移除时
                          // (BattleEngine.DropRiders)连同 SourceId + TraitKey 相同的附带状态一起移除。
                          // 极性记 Debuff:敌人侧驱散只清 Buff,载体不能被驱散单独剥掉。
        ShieldRecoil,     // 反震(D1 Task 9,D9,仅玩家,隐藏):Magnitude = 反弹吸收量的百分比,TurnsLeft = -1,
                          // TraitKey = 每回合次数阀的键。同类取最强(只留一条);两桶护盾归零 / 倾覆清盾时移除。
        Taunt,            // 嘲讽(D2-0 Task 2,spec §3.1,E11,玩家与木灵):敌人只能攻击带嘲讽的单位。Magnitude 不用,
                          // TurnsLeft = 回合数(-1 = 本场),SourceId = 字 ID(同源刷新)。玩家身上按玩家回合递减,木灵身上按木灵自己那一拍递减。
        // ---- D2-火 Task 3:灼附着族(附录 N5,仅敌人;全部挂在灼上,SourceId = 字 ID、TraitKey = 特性键,灼移除时随 DropRiders 移除) ----
        HealBlock,        // 干涸(可见;chip 待 designer 稿,V3):该敌人无法回血。Magnitude 不用。
        BurnGrow,         // 上炎(隐藏载体):该敌人每次行动开始、灼结算前 +Magnitude 层;TurnsLeft = 自己的回合数(按敌人行动递减)。
        BurnHold,         // 四火(隐藏载体,一次性):下一次会减层的灼结算不减层,随后移除。
        BurnBurstMark,    // 焚城(隐藏载体):该敌人死亡时对全体结算一次它剩下的灼(EffectKind.BurnBurst 反应)。
        BurnBacklashMark, // 焚身(隐藏载体):出手前先受一次灼烧结算(D2-火 Task 4 接线,每回合 2 次,按 TraitKey 计)。
        // ---- D2-火 Task 4:出手前与受击挂点(附录 N7 / N8) ----
        Mine,             // 埋雷(仅敌人,可见;chip 待 designer 稿,V3):Magnitude = 爆炸伤害(出字时定死),TurnsLeft = -1,
                          // SourceId = 字 ID、TraitKey = 特性键(同源取大)。该敌人下一次攻击前爆炸并移除。
        Retaliate,        // 受击回敬(仅玩家,作用于玩家与全部召唤物;chip 待 designer 稿):OnHit = 对攻击者结算的效果,
                          // Magnitude = 每回合触发上限(0 = 不限,按 TraitKey 计),TurnsLeft = 1(玩家回合开始到期),SourceId = 字 ID。
        // ---- D2-金 Task 2(附录 J2) ----
        ApRefund,         // AP 返还(得利,仅玩家,隐藏载体):格挡反击 / 立威击杀时挂上,Magnitude = 下回合开始多给的 AP,
                          // TurnsLeft = -1;StartTurn 算完 AP 后加上并移除。已挂着时不再挂 —— 每轮至多一次(Q5)。
        // ---- D2-金 Task 3(附录 J5) ----
        Doom,             // 致命(割喉,仅敌人,可见;chip 按 traits StatusChips 拍板稿,Task 5 接):Magnitude 不用(1),
                          // TurnsLeft = 回合(按该敌人行动递减),SourceId = 字 ID(同源刷新取长)。杂兵:每次掉血后生命 < 30%
                          // 直接斩杀(BattleEngine.AfterEnemyHpLoss,施加那一刻也判);Boss:不斩杀,下一次 DamageEnemy 伤害 ×2
                          // (与标记相乘),用掉即移除全部致命。
        // ---- D2-金 Task 4(附录 J8) ----
        MoraleArmor,      // 富甲(玩家,隐藏载体):EffectivePlayerDefense 与木灵护甲各 +战意层数 × Magnitude(随战意即时变化,不快照);
                          // TurnsLeft = -1,本场持续(IsBattleScoped),同类取最强。
        MoraleShield,     // 金气(仅玩家,隐藏载体):每个玩家回合开始(清盾之后、TurnStarted 之前),战意 ≥ MoraleCap 则加盾 Magnitude;
                          // TurnsLeft = -1,本场持续(IsBattleScoped),同类取最强。
        // ---- D2-水 Task 2:冻结附着族(附录 W1,仅敌人,隐藏载体,不画 chip;全部挂在冻结上,SourceId = 字 ID、
        // TraitKey = 特性键,TurnsLeft = -1;冻结结束 / 被解冻时 OnFreezeEnd 结算后随 DropRiders 移除) ----
        FrostBite,        // 怀山:冻结中每次行动开始受 Magnitude 点伤害(出字时定死)。
        ThawStrike,       // 寒彻:冻结结束(自然到期 / 被解冻)时受 Magnitude 点伤害一次;死亡不算。
        ThawSlow,         // 冰水:冻结结束时(霜抗之后)挂减速 Magnitude 回合。
        // ---- D2-水 Task 3:拦截族(附录 W3 / W4;可见,chip 待 designer 稿,V5 门控:Core 照做、chip 不画) ----
        BuffBlock,        // 洗尽铅华(仅敌人,Debuff 极性):期间 ApplyStatus 拦下该敌人身上一切 Buff 极性的施加(返回 false、不发
                          // StatusApplied),**霜抗除外**(Q21,R1 安全网)。Magnitude 不用,TurnsLeft = 回合(按该敌人行动递减),
                          // SourceId = 字 ID(同源刷新)。
        DebuffWard,       // 濯身 / 浇熄(玩家或木灵,Buff 极性):期间 ApplyStatus 拦下落在该单位身上的减益(灼按 RefreshBurn 增量计层,
                          // 拦下后不改层)。Magnitude = 每拦 1 层 / 1 条给该单位的护盾(0 = 不转),WardOf = 只拦这一种(null = 全部减益),
                          // WardCount = 剩余次数(0 = 期间不限,>0 用尽即移除;土·杜绝),TurnsLeft = 回合(玩家按玩家回合、木灵按木灵那一拍递减)。
        // ---- D2-水 Task 4:我方受击 / 回合挂点(附录 W5 / W6 / W7,仅玩家,TurnsLeft 按玩家回合递减) ----
        HurtHeal,         // 栉风沐雨(可见,chip 待 designer 稿,V5):Magnitude = 回复百分比,TurnsLeft = 回合,SourceId = 字 ID。
                          // 多条取最强;每回合 1 次(次数阀键「受击回复」)。Q26:土·堡垒的护盾载荷以后复用同一挂点。
        ShieldFrost,      // 冰晶(隐藏载体):Magnitude = 冻结回合,OnHit = 打破时对攻击者结算的效果(本 plan 恒为 [Freeze N];
                          // Q26:土·碎玉的伤害载荷以后放这里,须进 60% 反伤预算),TurnsLeft = -1,随两桶护盾归零移除。
        TurnPulse,        // 大雨滂沱(可见,chip 待 designer 稿,V5):OnHit = 每个玩家回合开始结算的效果(未缩放,OpeningEffect 形态),
                          // TurnsLeft = 回合(施加当回合不触发,之后 N 次),SourceId = 字 ID、TraitKey = 特性键。
    }

    /// <summary>状态的分类规则。</summary>
    public static class StatusRules
    {
        /// <summary>「本场」状态:木灵战后带进下一场之前剥离(spec §3.3,E3)。按 Kind 判定,不看 TurnsLeft ——
        /// 入场护甲(DefenseBuff,TurnsLeft = -1)要跨场保留。</summary>
        public static bool IsBattleScoped(StatusKind kind) =>
            kind == StatusKind.Taunt || kind == StatusKind.Block || kind == StatusKind.Endure
            || kind == StatusKind.DamageCut || kind == StatusKind.CounterBoost
            || kind == StatusKind.Retaliate   // D2-火 Task 4:受击回敬只管本回合(挂在玩家身上,列进来是防御性的)
            || kind == StatusKind.MoraleArmor || kind == StatusKind.MoraleShield   // D2-金 Task 4:战意光环只管本场
            || kind == StatusKind.DebuffWard   // D2-水 Task 3:免疫减益只管本场(防御性,同 Retaliate)
            || kind == StatusKind.HurtHeal || kind == StatusKind.ShieldFrost || kind == StatusKind.TurnPulse;   // D2-水 Task 4(防御性)
    }

    public enum StatusPolarity { Buff, Debuff }

    /// <summary>一条状态。Magnitude 按 Kind 解读:Burn=层数、Bleed/HealOverTime=每回合量、
    /// SpeedModifier=速度点数、
    /// AttackBuff=攻击加成的**百分点,敌我同一单位**(2026-08-12 统一:
    ///   <see cref="StatusKind.AttackBuff"/> 的注释是这条的出处;
    ///   敌人侧 EnemyState.Attack 拿它乘 BaseAttack,玩家侧 EffectiveAttack 拿它加在基准 100 上,
    ///   两边都是比值 —— 量级 ×10 只乘数量,比值一律不乘)、
    /// ArmorBreak=**削减的护甲点数**(EffectiveEnemyDefense / EffectivePlayerDefense 的减数)、
    /// Curse=减攻百分比,与 AttackBuff **同一根轴**,EnemyState.Attack 里直接相减(±50% 精确相消)、
    /// Seal=AP 扣减量(StartTurn 读它)、
    /// Blind=命中降低百分比(AttackHits 读它)、
    /// Morale=战意层数(EffectiveAttack 按 MoralePercentPerStack 折成百分比才是攻击加成)、
    /// ApBoost=每回合 AP 上限加成(StartTurn 与 ApPerTurn 两侧都读它)、
    /// CritBuff=暴击率加成的百分点(EffectiveCrit 读它)、
    /// DefenseBuff=护甲**点数**(EffectivePlayerDefense 的加数)、
    /// PierceBuff=穿透点数(EffectiveEnemyDefense 的减数)、
    /// DodgeBuff=闪避加成的**百分点**(EffectiveDodge 读它;它是比值,量级 ×10 永不跟着乘)。
    ///
    /// ⚠ 护甲这一层的**变动量一律走状态**(2026-08-12,E-b4 §4.5.3):基础护甲是不可变属性
    /// (BattleConfig.PlayerDefense / EnemyState.Defense),增(DefenseBuff)、减(ArmorBreak)、
    /// 穿(PierceBuff)全部是本类的条目 —— StatusBag 本来就进快照,这是「零新增快照字段」的全部依据。</summary>
    public sealed class StatusEffect
    {
        public StatusKind Kind { get; set; }
        public StatusPolarity Polarity { get; set; }
        public int Magnitude { get; set; }
        // -1 = 战内持久,不随回合递减(2026-08-06 M5 改准确:是否跨战斗延续到下一场是另一件事,
        // 取决于 RunEngine 的携带态白名单——目前只有 DefenseBuff 会被带过去,免疫/玩家灼烧/
        // 封字等其余 TurnsLeft=-1 的状态都在每场战斗结束时丢弃,称「段内」持久并不准确)。
        public int TurnsLeft { get; set; }

        /// <summary>来源标识,两种相反用法并存,加新状态时先想清楚要哪种(2026-08-05 M3):
        /// 1) **去重键**——直接传字 ID(如 "花"):同字再放视为同一来源,Apply() 覆盖刷新不叠加。
        /// 2) **铸唯一序号使其可叠**——传 "字#序号"(如 "滋#7",序号取自 BattleEngine._statusSerial /
        ///    RunSnapshot.StatusSerial):每次施放序号不同,天然绕开 Apply() 的同源覆盖,叠加而非刷新
        ///    (HealOverTime、AttackBuff、ArmorBreak、<see cref="StatusKind.DefenseBuff"/> 走这条)。
        /// 忘记铸序号、误传裸字 ID 会让本该可叠的状态静默退化成刷新——Task 4 的 Critical 就是这么踩的。
        ///
        /// ⚠ DefenseBuff 2026-09-16 从用法 1) 挪到用法 2):护甲改百分比减伤
        /// (DR = 甲/(甲+100))之后,DR 永远到不了 100%,「同字不叠」这条约束当初存在
        /// 只是为了防止点数减法叠满即无敌,理由随之消失,遂放开可叠。</summary>
        public string SourceId { get; set; }
        public bool TargetAll { get; set; }  // 仅 HealOverTime 用

        /// <summary>格挡每次反击的伤害(spec v7 §4,仅 <see cref="StatusKind.Block"/> 用;出字时定死,不吃攻击力)。</summary>
        public int CounterDamage { get; set; }

        /// <summary>灼的火力(spec v7 §4,仅 <see cref="StatusKind.Burn"/> 用):每层伤害的百分比系数,
        /// = 给该目标上过灼的火字中最高的等级系数(<c>MetaRules.CardLevelPercent</c>)。0(缺省 / 旧存档)视为 100。</summary>
        public int Potency { get; set; }

        /// <summary>特性键(D1 Task 9,附录 M9):<see cref="StatusKind.TraitRider"/> 与它附带的状态共用同一个键
        /// (<c>BattleEngine.TraitKey</c> 产出的「字/槽/面」),<see cref="StatusKind.ShieldRecoil"/> 用它查每回合次数。
        /// 普通状态恒为 null —— 去重键因此逐位不变。</summary>
        public string TraitKey { get; set; }

        /// <summary>持续治疗的落点槽位(2026-08-22,spec §8.3)。−1 = 玩家,0..5 = 召唤物槽。
        /// 与 <see cref="TargetAll"/> 同构:HoT 始终挂在**玩家的** StatusBag 上,
        /// 结算时按这个槽位分发。
        ///
        /// 为什么不给 SummonState 加状态容器:那要新增 SummonSnapshot 字段,而漏补是静默的
        /// (RunSnapshot.cs 那条警告)。全字表只有 1 张 HoT 字,不值得为它引入一整套
        /// 召唤物状态系统。</summary>
        public int TargetSlot { get; set; } = -1;

        /// <summary>门槛(D2-火 Task 3,炽焰):&gt; 0 时这条状态只在持有者自身灼 ≥ MinBurn 层时生效
        /// (目前只有 <see cref="StatusKind.Curse"/> 读:EnemyState.Attack)。0 = 无门槛(缺省,逐位恒等)。</summary>
        public int MinBurn { get; set; }

        /// <summary>受击回敬(D2-火 Task 4,<see cref="StatusKind.Retaliate"/>):我方被命中时对攻击者结算的效果。
        /// 用 <see cref="OpeningEffect"/> 当可序列化的效果形态(EffectDef 只读、进不了存档 JSON),BattlesLeft 不用。
        /// 其余状态恒为 null。</summary>
        public List<OpeningEffect> OnHit { get; set; }

        // ---- 格挡附带(D2-金 Task 2,附录 J1;仅 <see cref="StatusKind.Block"/> 用,出字时由 BlockMod 定死)----
        // 缺省全 0 / false / null = 原格挡,逐位恒等。同类合并(Q4):数值取大、开关取并、ExecuteSourceCharId 跟最近一次带立威的施加。

        /// <summary>贯穿反击:打完攻击者再打同列其余存活敌人(每击 70%),共用 60% 预算。</summary>
        public bool CounterColumn { get; set; }

        /// <summary>反击击数(0 / 1 = 一击):每击 = CounterDamage,逐击扣预算。</summary>
        public int CounterHits { get; set; }

        /// <summary>立威阈值(百分比,0 = 无):反击前攻击者生命 &lt; N% 时斩杀(杂兵,不吃预算);Boss 改为本次反击 ×2。</summary>
        public int CounterExecuteBelow { get; set; }

        /// <summary>格挡流血量(出字时已按卡等级与攻击力定死,0 = 无):每次格挡被消耗时挂给攻击者,3 回合。</summary>
        public int BlockBleed { get; set; }

        /// <summary>格挡加战意(0 = 无):每次格挡被消耗时玩家战意 +N。</summary>
        public int BlockMorale { get; set; }

        /// <summary>反击击杀返还的 AP(0 = 无):挂 <see cref="StatusKind.ApRefund"/>。</summary>
        public int KillRefundAp { get; set; }

        /// <summary>立威的施加者字 ID(供 Task 3 铁则回查;本任务只存不用)。没有立威时 null。</summary>
        public string ExecuteSourceCharId { get; set; }

        /// <summary>仅在减速中(D2-水 E25,淋漓;仅 <see cref="StatusKind.Seed"/> 用):true 时持有者不在减速中(无负 SpeedModifier)
        /// 就不触发。缺省 false = 原种,逐位恒等。</summary>
        public bool WhileSlowed { get; set; }

        /// <summary>只拦这一种减益(D2-水 W4,浇熄 = Burn;仅 <see cref="StatusKind.DebuffWard"/> 用)。null = 全部减益(濯身)。</summary>
        public StatusKind? WardOf { get; set; }

        /// <summary>剩余可拦次数(D2-水 W4,土·杜绝预留;仅 <see cref="StatusKind.DebuffWard"/> 用)。0 = 期间不限;&gt; 0 时每拦一次 −1,
        /// 减到 0 即移除这条。</summary>
        public int WardCount { get; set; }

        public StatusEffect Clone() => new()
        {
            Kind = Kind, Polarity = Polarity, Magnitude = Magnitude,
            TurnsLeft = TurnsLeft, SourceId = SourceId, TargetAll = TargetAll,
            TargetSlot = TargetSlot, CounterDamage = CounterDamage, Potency = Potency, TraitKey = TraitKey,
            MinBurn = MinBurn, OnHit = OnHit?.Select(o => o.Clone()).ToList(),
            CounterColumn = CounterColumn, CounterHits = CounterHits, CounterExecuteBelow = CounterExecuteBelow,
            BlockBleed = BlockBleed, BlockMorale = BlockMorale, KillRefundAp = KillRefundAp,
            ExecuteSourceCharId = ExecuteSourceCharId, WhileSlowed = WhileSlowed,
            WardOf = WardOf, WardCount = WardCount,
        };
    }

    /// <summary>单位身上的状态集合。封装「同字不叠只刷新」与按极性批量清除,
    /// 供 P1~P3 的 Dispel/Cleanse 直接使用。</summary>
    public sealed class StatusBag
    {
        private readonly List<StatusEffect> _list = new();

        public IReadOnlyList<StatusEffect> All => _list;

        public bool Has(StatusKind kind) => Find(kind) != null;

        public StatusEffect Find(StatusKind kind)
        {
            foreach (var e in _list)
                if (e.Kind == kind) return e;
            return null;
        }

        /// <summary>该种类的量值合计(减伤多源叠加、速度多条修正求和都靠它)。</summary>
        public int TotalMagnitude(StatusKind kind)
        {
            int sum = 0;
            foreach (var e in _list)
                if (e.Kind == kind) sum += e.Magnitude;
            return sum;
        }

        /// <summary>该种类各条目量值的最大值(没有 = 0)。「同类取最强」的状态(减伤 / 反击增强)用它。</summary>
        public int MaxMagnitude(StatusKind kind)
        {
            int max = 0;
            foreach (var e in _list)
                if (e.Kind == kind && e.Magnitude > max) max = e.Magnitude;
            return max;
        }

        /// <summary>施加一条。同 Kind 且同 SourceId 视为同一来源,覆盖刷新而非叠加
        /// (口径来自 P0:同字减伤不叠加,重复施放只刷新——2026-09-16 起 DefenseBuff 已移出这条,
        /// 见 <see cref="StatusEffect.SourceId"/> 的用法说明)。SourceId 为 null 时按 Kind 去重。
        /// 要允许同源可叠(如 HoT/AttackBuff),调用方得给 SourceId 铸唯一序号——见
        /// <see cref="StatusEffect.SourceId"/> 的两种用法说明。</summary>
        public void Apply(StatusEffect effect)
        {
            for (int i = 0; i < _list.Count; i++)
            {
                if (_list[i].Kind != effect.Kind) continue;
                if (_list[i].SourceId != effect.SourceId) continue;
                if (_list[i].TraitKey != effect.TraitKey) continue;   // 附着带出的状态不覆盖同字的普通状态(普通状态恒 null,恒等)
                _list[i] = effect;
                return;
            }
            _list.Add(effect);
        }

        public void Remove(StatusKind kind) => _list.RemoveAll(e => e.Kind == kind);

        /// <summary>按极性批量清除,返回移除条数(驱散/净化用)。<paramref name="except"/> 可选豁免一个种类。</summary>
        public int RemoveAll(StatusPolarity polarity, StatusKind? except = null)
        {
            int before = _list.Count;
            _list.RemoveAll(e => e.Polarity == polarity && !(except.HasValue && e.Kind == except.Value));
            return before - _list.Count;
        }

        /// <summary>按极性从头移除至多 count 条,返回实际移除条数(计数式驱散用)。<paramref name="except"/> 可选豁免一个种类。</summary>
        public int RemoveFirst(StatusPolarity polarity, int count, StatusKind? except = null)
        {
            int removed = 0;
            for (int i = 0; i < _list.Count && removed < count; )
            {
                if (_list[i].Polarity != polarity || (except.HasValue && _list[i].Kind == except.Value)) { i++; continue; }
                _list.RemoveAt(i);
                removed++;
            }
            return removed;
        }

        public void Clear() => _list.Clear();

        /// <summary>移除指定的那一条(按引用)。免疫消耗到 0 时用——袋子里可能有多条
        /// 同 Kind 不同来源的免疫,不能用按 Kind 的 Remove 一把全清。</summary>
        public void RemoveEntry(StatusEffect effect) => _list.Remove(effect);

        /// <summary>回合数递减,归零即移除;TurnsLeft &lt; 0 表示段内持久,不受影响。
        /// <paramref name="except"/> 可选豁免一个种类不递减(冻结中 SpeedModifier 暂停用,
        /// 2026-08-05:黑名单式豁免——新加的有限时长状态默认照常递减,不会像原先的白名单
        /// 那样悄悄漏减)。</summary>
        public void TickTurns(StatusKind? except = null)
        {
            for (int i = _list.Count - 1; i >= 0; i--)
            {
                if (_list[i].TurnsLeft < 0) continue;
                if (except.HasValue && _list[i].Kind == except.Value) continue;
                _list[i].TurnsLeft -= 1;
                if (_list[i].TurnsLeft <= 0) _list.RemoveAt(i);
            }
        }

        /// <summary>深拷贝(快照恢复用):条目是引用对象,浅拷会让两个单位共享同一条状态。</summary>
        public void CopyFrom(IEnumerable<StatusEffect> source)
        {
            _list.Clear();
            foreach (var e in source) _list.Add(e.Clone());
        }
    }
}
