using System.Collections.Generic;

namespace Brushblade.Core
{
    /// <summary>出字效果类型(第 3 章 3.2.1;按流派需要逐步扩展)。</summary>
    public enum EffectKind
    {
        DamageSingle, // 伤害(打谁由 EffectDef.Shape 定;全体 = Shape All。原 DamageAll 已于
                      // spec v7 §11.6 退役 —— 字表按名字解析,但 OpeningEffect.Kind 以 int 进存档,
                      // 所以删值/改序会让旧存档的序号错位;删值不留占位是因为项目未上线、存档不需兼容)
        BurnSingle,   // 单体灼烧(叠层)
        BurnAll,      // 全体灼烧(叠层)
        Shield,       // 护盾:自身或指定一只召唤物(2026-08-26 起目标可选)
        ShieldAll,    // 群体护盾:玩家 + 全部存活召唤物各一份(2026-09-05,崩)。
                      // 与 HealSelf/HealAll 那一对同构 —— 崩的攻面是全体伤害,护面就该对称成全体。
        BurnPotency,  // 本场每层灼烧结算伤害 +Value(可叠加,10.3.1)
        HealSelf,     // 治疗自身(不超上限;水系主打,2026-07-19 拍板)
        Summon,       // 召唤前排单位(Value=血量;木系主打,2026-07-19 拍板)
        Bleed,        // 流血:每回合固定伤害,无属性、不走生克(2026-08-03)
        HealAll,        // 群体治疗:玩家 + 全部召唤物(2026-08-03)
        HealOverTime,   // 持续治疗:每回合 Value,持续 Turns 回合;TargetAll 则含召唤物
        Freeze,       // 冻结:目标跳过 Value 个回合(2026-08-03;藤的「束缚」也走这个)
        Slow,         // 减速:半速,每 2 回合才行动一次,持续 Value 回合(2026-08-03)
        DefenseBuff,  // 护甲增益:自身护甲 +Value **点**,限时、可叠加(2026-09-16 起;此前同字不叠、段内持久)
                      // (2026-08-03 起名 DamageReduction 走乘法减伤,2026-08-12 E-b4 T3 改点数并改名。
                      //  EffectKind 只从 chars.json 按**名字**解析、从不进存档,故可以就地改名)
        ArmorBreak,   // 破甲:目标护甲 −Value **点**,本场持久、可叠加
                      // (2026-08-05 曾实现为「承伤 +25%,持续 Value 回合」——那是引擎里还没有护甲
                      //  点数时的代偿;2026-08-12 E-b4 T3 复原成第 10 章 :56 的原始设计)
        Dispel,       // 驱散:清敌方增益。Value = 条数(−1 = 全部);TargetAll = 全体各清(2026-08-06)
        Cleanse,      // 净化:清玩家自身全部减益(Value 不用,2026-08-06)
        Immunity,     // 免疫:完全挡下 Value 次伤害,先于护盾消耗(2026-08-06)
        Revive,       // 复活:救回 Value 名阵亡召唤物,各回半血(2026-08-06)
        Blind,        // 致盲:目标命中率 −Value%,持续 Turns 回合;TargetAll = 全体(2026-08-07)
        Silence,      // 玩家可见名「封禁」(枚举仍叫 Silence 未跟着改名——EffectKind 只从
                      // chars.json 按名字解析、从不进存档,枚举名/展示名允许错位,但下一个读
                      // 这个值的人得知道对应的是哪张牌)。2026-08-07 原语义仅「目标的主动机制
                      // 哑火」;2026-09-05 裁定扩成「特殊能力全部失效」:杂兵护甲归零 + 被动
                      // 不触发 + 主动机制哑火,**Boss 降级**为只削「护甲减半」,大招与被动不受
                      // 影响(同斩杀对 Boss 从直杀降为吃双倍一个纪律)。持续 Turns 回合(Value 不用)
        Reflect,      // 反弹:受到的伤害按 Value% 照回攻击者,持续 Turns 回合(2026-08-07)
        BurnNoDecay,  // 不灭:目标灼烧本场不衰减(Value 不用,2026-08-09)
        BurnSettleNow, // 立即结算一次灼烧(与回合末同公式;Value 不用,2026-08-09)
        Detonate,     // 引爆:把目标剩余灼烧层数的全部未来伤害一次打出并清空(Value 不用,2026-08-09)
        Empower,      // 本场攻击力 +Value(剡;可叠加,2026-08-12)
        Morale,       // 战意 +Value 层,每层 +10 攻击,上限 5 层(战/戮;本场持久,2026-08-12)
        ApBoost,      // 本场每回合 AP 上限 +Value(利;可叠加,2026-08-12)
        CritBuff,     // 本场暴击率 +Value 个百分点(锋;可叠加、上限由 EffectiveCrit 钳,2026-08-12)
        PierceBuff,   // 本场穿透 +Value **点**(锐;可叠加、本场持久,2026-08-12 E-b4 T5)
                      // 与伤害效果自带的一次性 Pierce 进同一个减数(EffectiveEnemyDefense),区别只在存续:
                      // 这条挂在玩家身上、往后每次结算都吃,Pierce 只作用于它所在的那一次攻击。
        SpendHeft,   // 厚积薄发(2026-09-02,土):清空全部厚,对全体造成 层数 × Value 伤害。
                         // 0 层时空转 —— 不发事件、零伤害,但**同字的其他效果照常生效**
                         // (崩 = 全体伤害 + 厚积薄发,0 层时 AOE 那一半不能被吞掉)。
        SpendWellspring, // 涌泉相报(2026-09-02,水):清空全部泉,对全体造成 层数 × Value 伤害。
                         // 规则与 SpendHeft 一字不差,只是读另一个资源。
        Charm,        // 魅惑(2026-09-05,花):目标本回合改为攻击**自己阵营**,持续 Turns 回合。
                      // Value 不用。场上只剩它一只时空转(不自伤、也不打玩家)——
                      // 否则魅惑在单敌局面会退化成一发纯伤害,与它 0.40 的定价不符。
                      // 定价 0.40 高于冻结 0.35:冻结只是让敌人少一次出手,魅惑是把那次
                      // 出手转成对敌方自己的伤害 —— 控制与输出两头都占。
        Quench,       // 蓄热(2026-09-16,火,热):清空目标全部灼烧层数,
                      // 每清 1 层给玩家 BurnPotency += Value(本场永久)。Value = 每层的增威点数。
                      //
                      // 与 Detonate 的分界:引爆是把剩余层数的未来伤害**一次兑现**,
                      // 蓄热是把层数**转成本场永久的 BurnPotency** —— 不打伤害,只夺层数。
                      //
                      // ⚠ 纯夺火,**不自带挂层**(2026-09-16 用户裁定):目标无灼烧时这张字
                      // 只剩伤害面,空转是接受的代价。因此它是继 炸 之后第二个
                      // 「语义例外:不挂 DOT」,火系印记的免计价层不适用。
        Haste,        // 加速 / 急速(2026-09-16,水):给**被治疗的那个目标**挂正的 SpeedModifier,
                      // 量 = 目标**基础速度** × Value%,持续 Turns 回合。
                      // Value 50 = 加速、100 = 急速 —— 同一条载体两档,别拆成两个 kind。
                      //
                      // 换算基数是目标自己的基础速度(SummonState.Speed / config.PlayerSpeed),
                      // 所以 150 底速的召唤物吃加速是 +75、100 底速的是 +50 —— 符合「×50%」的字面。
                      //
                      // ⚠ SpeedModifier 此前**只出现负值**。这是本字段第一次取正——凡是按
                      // 「有 SpeedModifier 就是被减速了」判断的地方都必须收紧成 `< 0`
                      // (BattleEngine.ConditionMet 的 Controlled 判据、Targeting 的 preferUnslowed
                      // 早已是 `< 0`/`>= 0`,2026-09-16 复核过一遍,全部正确)。
        Unseal,       // 解封(2026-09-16,水):被治疗的那只召唤物的属性在 6 类中纯随机
                      // 重掷一次,永久。Value 不用。
                      //
                      // 纯随机 = 含当前属性、含 Heart(2026-09-05 用户裁定):可能没变、
                      // 可能变差(本来克敌人的变成被克)。这是设计,不是 bug —— 它是赌。
                      //
                      // 落到玩家槽位时**空转**:玩家没有五行属性,也不该有。⚠ 空转分支必须
                      // 写在 BattleEngine.ApplyEffects 摇随机数**之前** return —— _random 是
                      // 带种子的全局流,摇了不用的一次也会平移掉后面全部依赖种子的既有测试
                      // (与 AttackHits 的 hitRate ≥ 100 短路同一条纪律)。
                      //
                      // ⚠ 重掷后择敌(Targeting.KeTier / TopKeTier)、生克结算(WuxingResolver.
                      // KeMultiplier 系)、UI 显色(BattleView.DrawSummons / SummonInfo)三条路径
                      // 全部现读 SummonState.Element,不需要各自接线——但改完要 grep 复查,
                      // 别假设只有这三条。
        Block,        // 格挡(spec v7 §3.1,铠):Value = 次数(离散量,不吃卡等级);下一次敌人挥击 −40% 并反击。
                      // 反击伤害 = 本字攻击面首条 DamageSingle(吃等级)× 30%,出字时定死。
        Amplify,      // 本字修饰器(D1 Task 3,附录 M1):Value = 百分点,作用范围 EffectDef.Scope,
                      // 可带条件 EffectDef.OnlyIf。出字前由 TraitRules.Fold 折叠成目标效果上的 AmpTerms,
                      // **不进结算循环**;同轴多条相加(spec §6.1.3)。百分点不吃卡等级。
        Reshape,      // 本字修饰器(D1 Task 3,附录 M2/M3):改本面**第一条** DamageSingle 的形状 /
                      // 击数 / 每击百分比 / 伤害标记 —— Reshape 上非缺省的字段覆盖原值。本面没有
                      // DamageSingle 时空转。同样由 Fold 折叠,不进结算循环。
        Augment,      // 本字叠加修饰器(D1 Task 4,附录 M4):Value = 加多少,AugmentKind / AugmentField 指明
                      // 加在本面**第一条**该 Kind 效果的哪个字段(次数 / 回合 / 跳数)。找不到同 Kind 空转。
                      // 由 Fold 折叠,不进结算循环;离散量,不吃卡等级。
        Weaken,       // 减攻(D1 Task 5,附录 M5):给目标挂 StatusKind.Curse(攻击 −Value%),Turns 回合,
                      // SourceId = 字 ID,同源刷新取较强值与较长回合。玩家可见名「减攻」(枚举不改名)。
                      // Value 是百分点,吃卡等级;Turns 不吃。支持 Pick / OnlyIf。
        Seed,         // 种(D1 Task 6,附录 M6):给目标敌人挂 StatusKind.Seed —— 该敌人每次行动开始时,
                      // 我方生命比例最低的单位回复 Value。Value = 回复量(吃卡等级、吃 Amplify scope Seed),
                      // Turns = 回合(离散量,不吃等级)。SourceId = 字 ID,同源刷新取强。支持 Pick / OnlyIf。
        Vulnerable,   // 标记(D1 Task 6,附录 M11):给目标敌人挂 StatusKind.Vulnerable —— 受到的伤害 +Value%。
                      // Value = 百分点(吃卡等级),Turns = 回合(不吃)。Turns == 0 且 Pick == FrozenByThisCast
                      // 时回合数 = 该目标本次冻结的回合数(冰缚);其余缺 turns 兜 1 回合。支持 Pick / OnlyIf。
        // ---- D1 Task 7:我方侧(附录 M12/M13/M14/M16/M18、扎根)。⚠ 只在末尾追加 ----
        DamageCut,      // 本回合减伤:给玩家挂 StatusKind.DamageCut(TurnsLeft 1),Value = 百分点(离散,不吃等级)。
                        // 与格挡的 40% 合计后钳到 CombatCaps.NonArmorReductionPercent。
        CounterBoost,   // 反击增强:给玩家挂 StatusKind.CounterBoost(TurnsLeft 1),格挡反击 ×(100+Value)/100;仍受 60% 反伤预算。
        Endure,         // 保命:给召唤物挂 StatusKind.Endure(一次性)。Pick = SummonedThisCast → 本次出字召出的召唤物;
                        // Primary → allySlot 指的那只召唤物(点玩家空转)。Value 不用。
        SummonSapling,  // 幼苗:额外召 SummonCount 只「苗」,血 / 攻 = 本次出字召出的第一只 × Value%,无本命;
                        // 只占空槽 / 尸体槽,没有空位就不召(不顶替)。本次出字没召出任何召唤物时空转。
        HealSummons,    // 群疗(仅召唤物):每只存活召唤物回复 Value;PercentOfMax = true 时回复 MaxHp × Value%。
        ShieldSummons,  // 群盾(仅召唤物):每只存活召唤物 +Value 护盾(走 AddSummonShield,吃上限)。
        SummonStrike,   // 群刺:每只存活召唤物各对选定目标攻击一次,伤害 = 有效攻击 × Value%(暴击走 _random,D5)。
        ShieldFromHeal, // 治疗转盾:本次出字内**实际**治疗量(溢出不算)× Value% 转为玩家护盾(不吃护盾 Amplify / 筑垒)。
                        // 只统计排在它之前的治疗。
        AddWellspring,  // 直接 +Value 层泉(走 AddPlayerCounter + CapFor,不经治疗折算)
        AddHeft,        // 直接 +Value 层厚(同上)
        // ---- D1 Task 9:附着载体(附录 M9 / D9)。⚠ 只在末尾追加 ----
        ShieldRecoil,   // 反震:本次出字给玩家加了护盾(实际入账 > 0)时,给玩家挂 StatusKind.ShieldRecoil(Magnitude = Value%)。
                        // 护盾吸收敌人挥击时按吸收量 × Value% 反弹,每回合 1 次,与镜 / 格挡反击共用 60% 反伤预算。
                        // Value 是百分比,离散(不吃卡等级)。
        // ---- D2-0 Task 2:嘲讽。⚠ 只在末尾追加 ----
        Taunt,          // 嘲讽:Value = 回合数(0 = 本场),离散不吃等级。必须写 pick:Self → 玩家(敌人的单体攻击一律打玩家);
                        // SummonedThisCast → 本次出字召出的木灵;AllSummons → 全部存活木灵。同源刷新。
        // ---- D2-火 Task 2:灼操作族(附录 N1 / N2)。⚠ 只在末尾追加 ----
        BurnScale,      // 灼层数按百分比缩放(焦土 / 炎炎 / 灿然 = 200 翻倍):新层数 = ⌊层数 × Value / 100⌋,钳到 CombatCaps.BurnStacks;
                        // 只升不降(Value ≥ 100,ConfigLoader 拦);0 层空转。火力取 max(原火力, 本字火力)。离散,不吃卡等级。支持 Pick / OnlyIf。
        BurnEqualize,   // 拉平(火烧连营):取存活敌人的最高灼层数 M,每名存活敌人补到 M(只升不降,0 层也补);新层火力取 max(原, 本字)。
                        // Value 不用;不选目标、不认选择器。全场 0 层空转。
        // ---- D2-火 Task 3:灼附着族(附录 N5 / N6)。⚠ 只在末尾追加 ----
        // 前四个与 BurnBacklash 一律写 riderOf Burn(ConfigLoader 拦不写的):只挂在带本次出字所上之灼的目标上,
        // 灼移除时一并移除(DropRiders)。Value 离散,不吃卡等级。支持 Pick / OnlyIf(数据写 pick BurnedByThisCast)。
        HealBlock,      // 干涸:挂 StatusKind.HealBlock —— 该敌人无法回血(涂改给它回血 / 缺笔妖自补全的回血都被挡下)。Value 不用。
        BurnGrow,       // 上炎:挂 StatusKind.BurnGrow(Magnitude = Value 层,TurnsLeft = Turns)—— 该敌人每次行动开始、灼结算前 +Value 层。
        BurnHold,       // 四火:挂 StatusKind.BurnHold(一次性)—— 该敌人下一次会减层的灼结算不减层,随后移除;重新上灼时刷新(G10)。Value 不用。
        BurnBurst,      // 焚城:带 riderOf = 挂 StatusKind.BurnBurstMark;该敌人死亡时(ResolveDefeat,非反应里打死,R4)入队一条不带 riderOf 的
                        // BurnBurst(Value = 死者剩余灼层数 S),对全体存活敌人按灼烧公式各扣一次血(火力 = 死者灼的火力),不改层数。
        BurnBacklash,   // 焚身:挂 StatusKind.BurnBacklashMark(载体)。带它的敌人每次攻击前先受一次灼烧结算(每回合 2 次,D2-火 Task 4)。Value 不用。
        // ---- D2-火 Task 4:敌人出手前与受击挂点(附录 N7 / N8)。⚠ 只在末尾追加 ----
        Mine,           // 埋雷:给目标挂 StatusKind.Mine(Magnitude = ScaleByAttack(Value),出字时定死,同流血的快照语义;同源取大)。
                        // 该敌人下一次攻击(普攻 / Boss 技能;冻结跳过、被魅惑那一击不算)前爆炸:自己受 Magnitude 点伤害(心属性、
                        // 无视护甲),地雷移除;被炸死则取消这次出手(G4)。Value 吃卡等级;可写 bodyPercent(E5)。支持 Pick / OnlyIf。
        Retaliate,      // 受击回敬(跨计划 Q23 通用形态,火:烈焰护身):给玩家挂 StatusKind.Retaliate(TurnsLeft 1,玩家下回合开始到期),
                        // 本回合我方(玩家 / 召唤物)每被敌人的挥击命中一次(免疫挡下也算,打空不算,铁画反噬不算),
                        // 就对**攻击者**结算一次 PerHit 里的效果(作为特性反应入队,下一个安全点兑现,R4)。
                        // Value = 每回合触发上限(0 = 不限),离散。PerHit 里不能有伤害(§5.2 第 3 律的 60% 反伤预算因此不涉及)。
        // ---- D2-火 Task 5:其余单点效果(附录 N9 / N10 / N11)。⚠ 只在末尾追加 ----
        ExtraStrike,    // 追加一击(星火 / 烈焚):Value = 本体百分比(离散)。伤害 = 本面本体首条 DamageSingle(吃等级;本面没有则取攻击面,
                        // Task 4 Ruling 1)× Value% × 攻击力,火 / 心按来源字元素走 DamageEnemy(过生克 / 护甲,照常摇暴击 _random)。
                        // 支持 Pick / OnlyIf(Random 走 _traitRandom);PerBurningHit = 本次出字命中过的、出字前带灼的敌人每名一发。
        Thaw,           // 解冻(水火相激):移除目标的冻结 / 减速(负 SpeedModifier)/ 冰滞。冻结按「结束」挂等长霜抗(R1),
                        // 冰滞照自然结束给 N+1。Value 不用。支持 Pick / OnlyIf。
        SelfCost,       // 自损(玉石俱焚,G9):出字开头失去当前生命 Value%(向下取整),不走护盾 / 护甲、不算受击(R4,不发 PlayerHit),
                        // 但会触发 50% 阈值;至少留 1 点,不会致死。Value 离散。
        Reveal,         // 揭示(光耀):通假字现形(RevealDisguise)、生僻字直接被读懂(ApparentElement = Element),发 EnemyRevealed;
                        // 其余目标空转。Value 不用。支持 Pick / OnlyIf。
        // ---- D2-金 Task 1:格挡修饰器(附录 E12)。⚠ 只在末尾追加 ----
        BlockMod,       // 格挡修饰器(剑意 / 千锤 / 金刚 / 双金合璧):Fold 时作用于本面**第一条** Block,非缺省字段覆盖 ——
                        // CounterPercent(反击 = 本体 × N%,缺省 BattleConfig.BlockCounterPercent 30)、
                        // ScaleBy Morale + ScaleMin(次数 = max(下限, 结算那一刻的战意层数))。本面没有 Block 时空转。
                        // 多条按出现(槽位)顺序折叠,后者覆盖。Value 不用;不进结算循环。
                        // Task 2(J1)追加运行时字段:CounterColumn / CounterHits / CounterExecuteBelow / BlockBleed /
                        // BlockMorale / KillRefundAp(非缺省覆盖,同上)。
        // ---- D2-金 Task 3:致命(附录 J5)。⚠ 只在末尾追加 ----
        Doom,           // 致命(割喉):给目标敌人挂 StatusKind.Doom,Value = 回合数(离散,不吃卡等级)。杂兵生命 < 30% 即斩杀
                        // (施加时已低于则立即斩杀;斩杀走 ResolveDefeat source Execute,触发斩杀时特性);Boss 首次 DamageEnemy ×2。
                        // 支持 Pick / OnlyIf。
    }

    /// <summary>计数缩放的计数口径(D2-火 Task 2,附录 N4,G2)。Amplify 读出字前快照(R3,条件类);HealSelf 读结算那一刻(产出量)。</summary>
    public enum ScaleBasis
    {
        None,           // 不缩放(缺省)
        BurnStack,      // Amplify:这一击的目标出字前的灼层数;HealSelf:存活敌人的灼层数之和
        BurningEnemy,   // 带灼的存活敌人数(Amplify:出字前;HealSelf:结算那一刻)
        // ---- D2-金 Task 1(附录 E10)。⚠ 只在末尾追加 ----
        Morale,         // 战意层数(结算那一刻):DamageSingle 击数 = HitCount + 战意(大卸八块,进 DamageSingle 时取一次);
                        // Block 次数 = max(ScaleMin, 战意)(双金合璧,由 BlockMod 写入,出字后的值)
        ExtraHitTarget, // Morale 的值 × (本次出字 HitTargets 去重数 − 1)(横扫千军;跨排 Boss 只算 1 名)
    }

    /// <summary><see cref="EffectKind.Augment"/> 加在目标效果的哪个字段。</summary>
    public enum AugmentField
    {
        Count,  // 次数(Block 的次数,在 Value 上)
        Turns,  // 回合(Freeze / Slow 在 Value 上,DefenseBuff / ArmorBreak / HealOverTime 在 Turns 上,见 TraitRules.TurnsOf)
        Shots,  // 跳数 / 发数(Shots)
    }

    /// <summary><see cref="EffectKind.Amplify"/> 的作用范围。Damage 缺省。</summary>
    public enum AmpScope
    {
        Damage,   // DamageSingle
        Heal,     // HealSelf / HealAll / HealOverTime
        Shield,   // Shield / ShieldAll
        Seed,     // 种的回复量(D1 Task 6 接上)
        Counter,  // 格挡反击量(Block 写 CounterDamage 时乘)
        All,      // 以上全部(D2-火 起含 Burn)
        Burn,     // 本字 BurnSingle / BurnAll 施加的灼的**火力**(Potency × (100+Σ)/100),不改层数(D2-火 G3)
    }

    /// <summary>单条效果:伤害/护盾/治疗走生克结算,灼烧层数为平值。</summary>
    public sealed class EffectDef
    {
        public EffectKind Kind { get; }
        public int Value { get; }

        /// <summary>伤害类:目标带某状态时基础值翻倍(2026-08-25 由 DoubleVsBurning 泛化)。
        /// <see cref="DamageCondition.None"/> = 无条件。</summary>
        public DamageCondition DoubleVs { get; }

        /// <summary>护盾类:本次护盾进留存桶(留存护盾,spec v7 §3.1)——普通护盾在玩家下一回合开始时清空,留存护盾不清。</summary>
        public bool PersistOnce { get; }

        /// <summary>召唤类:召几个(林 = 2)。</summary>
        public int SummonCount { get; }

        /// <summary>召唤类:召唤物攻击力(回合末反击)。</summary>
        public int SummonAttack { get; }

        /// <summary>召唤类:召唤物显示字(林 → 木)。</summary>
        public string SummonChar { get; }

        /// <summary>持续类效果的回合数(HoT 用)。</summary>
        public int Turns { get; }

        /// <summary>治疗类:true = 覆盖玩家与全部召唤物。</summary>
        public bool TargetAll { get; }

        /// <summary>伤害类:本次攻击的穿透**点数**(2026-08-12,E-b4 T2)。
        /// 进 <c>EffectiveEnemyDefense</c> 的减数,与目标身上的破甲从同一个基础护甲里一起减
        /// (合并相减、不嵌套、不重复扣),外层 <c>max(0, …)</c> 保证穿过头只是归零、绝不倒贴增伤。
        ///
        /// **只作用于这一次结算**,不留状态;要「本场持续的穿透」走
        /// <see cref="StatusKind.PierceBuff"/>。
        ///
        /// 2026-08-12 E-b4 T3:三个穿甲字(錰 30 / 刺 15 / 锥 10)迁到这里,原先的 bool
        /// <c>IgnoreArmor</c> 删除。旧标记做两件事 ——「无视承伤减免」由穿透点数接管,
        /// 「无条件 +15% 伤害」是与防御无关的常量乘数,已**固化进这三个字的基础值**
        /// (400→460 / 130→150 / 90→105),对无甲目标一分不差,模型少一个常量。</summary>
        public int Pierce { get; }

        /// <summary>召唤类:召唤物被动(2026-08-05)。null = 无被动。</summary>
        public SummonPassive Passive { get; }

        /// <summary>召唤类:出字瞬间给**全场存活召唤物**各 +N 点护盾(桂 = 6)。
        /// 不放进 Passive —— 它作用于出字时已在场的其他召唤物,不是这只召唤物自带的。</summary>
        public int SummonShield { get; }

        /// <summary>召唤类:**本次召出的**召唤物入场自带护甲 N 点(2026-09-08,塔 = 7)。
        ///
        /// 与 <see cref="SummonShield"/> 的两处区别:那个发给**全场**存活召唤物(桂 的光环),
        /// 这条只落在这一效果新召出的那几只身上 —— 它是那只单位自己的属性,不是场上的光环。
        /// 落地方式是往那只召唤物的状态袋里挂一条 <c>StatusKind.DefenseBuff</c>,由现成的
        /// <c>SummonState.EffectiveDefense</c> 读走,**不新建减伤路径**。
        ///
        /// 它是全字表唯一**不限时**的护甲(TurnsLeft = -1):2026-09-08「所有 buff 必须带回合数」
        /// 约束的是场上飘着的玩家增益,而这条随单位存在、单位死了就没了。</summary>
        public int SummonDefense { get; }

        /// <summary>斩杀:目标 HP 百分比低于此值时触发(0 = 不启用)。**打之前**判血——
        /// 让玩家看着血条就能决定出哪张;打之后判定虽然更像补刀,但结果不可预期。</summary>
        public int ExecuteBelowPercent { get; }

        /// <summary>true = 命中阈值直接击杀(Boss 免疫);false = 命中阈值伤害 ×2(对 Boss 照常生效)。</summary>
        public bool ExecuteKills { get; }

        /// <summary>伤害分几段打(剁 = 2)。默认 1。每段完全独立:各自过生克、各自减一次护甲,
        /// 也各自过斩杀的「打之前判血」——所以「第一段把敌人打进阈值、第二段触发处决」
        /// 是真会发生的涌现,不是 bug。</summary>
        public int HitCount { get; }

        /// <summary>目标形状(2026-08-22,spec §3)。缺省 <see cref="TargetArea.Single"/> ——
        /// 缺省值即恒等性:现有 87 张伤害字不写这个字段,展开后目标表长度恒为 1,
        /// 结算路径与改造前逐位相同。对 <see cref="EffectKind.DamageSingle"/> 有意义;
        /// 2026-09-16 起 <see cref="EffectKind.HealSelf"/> 配 <see cref="TargetArea.Chain"/>
        /// 也有意义(治疗弹射,见 BattleEngine 的 HealSelf 分支)。</summary>
        public TargetArea Shape { get; }

        /// <summary>非主目标的伤害百分比(2026-08-22)。主目标恒 100%。
        /// 横扫/贯穿建议配 100,溅射建议 50。<see cref="TargetArea.Scatter"/> **不吃这个值**
        /// ——连发每一发都是全额(spec §5)。<see cref="TargetArea.All"/> 同样不吃:全体每个目标都是主目标。
        ///
        /// ≤0 兜回 100:配置漏写时 JSON 会填 0,那会让两侧一分不伤,静默失效比报错更难查
        /// (与 <see cref="HitCount"/> 的 `≤0 → 1` 同型)。</summary>
        public int ShapePercent { get; }

        /// <summary>连发的发数(2026-08-22)。只对 <see cref="TargetArea.Scatter"/> 有意义。</summary>
        public int Shots { get; }

        /// <summary>碾(2026-09-16,土):本次伤害**完全跳过目标的护甲减伤**;仍吃护盾。
        ///
        /// 与 <see cref="Pierce"/> 是**两档**,别合并:穿透削一部分甲值再算 DR,
        /// 碾直接跳过整条 DR。土系攻面对偶「免疫」的那一条(spec §5)。
        ///
        /// 落地不新建减伤路径 —— 直接进 <c>DamageEnemy</c> 既有的 <c>bypassDefense</c> 参数,
        /// 与「相克即破甲」「反弹不吃甲」走同一个开关。</summary>
        public bool TrueDamage { get; }

        /// <summary>镇压(2026-09-16,土):本次攻击额外打出**自己有效护甲点数的 N%** 作为伤害。
        /// 0 = 不启用。
        ///
        /// 不走生克、不吃目标减伤 —— 与反弹(<see cref="EffectKind.Reflect"/>)同口径:
        /// 折返/加码都不是挥击。土系攻面对偶「反弹」的那一条(spec §5),
        /// 两档 50% / 30% 与反伤的两档一一对齐。
        ///
        /// 读的是 <c>BattleEngine.EffectivePlayerDefense</c>(含 DefenseBuff 与破甲),
        /// **不是**角色基础护甲 —— 「越肥打得越疼」这条流派靠的正是局内堆起来的那部分。</summary>
        public int ArmorStrikePercent { get; }

        /// <summary>Amplify 的作用范围(D1 Task 3)。其余 kind 不读。</summary>
        public AmpScope Scope { get; }

        /// <summary>条件门(D1 Task 3,附录 M1/M24)。Amplify 读:条件满足时这一条加成才算;
        /// D1 Task 5 起 <see cref="EffectPickRules.Supports"/> 的非伤害效果也读:每个被选中的目标各判一次,不满足就跳过。
        /// 按 R3 出字前快照判定;目标相关的条件按「这一击的目标」判定。None = 无条件。</summary>
        public DamageCondition OnlyIf { get; }

        /// <summary>多段时每一段的伤害百分比(D1 Task 3,连斩「2 击各 60%」)。缺省 100 = 不折算;
        /// ≤0 兜回 100(与 ShapePercent 同型)。</summary>
        public int HitPercent { get; }

        /// <summary>必定暴击(D1 Task 3,附录 M3):暴击判定走 chance 100 短路,不摇号。</summary>
        public bool ForceCrit { get; }

        /// <summary>无视目标有效护甲的百分比(D1 Task 3,重斩 50)。0 = 不启用。
        /// 作用在 <c>EffectiveEnemyDefense</c> 算完(含破甲 / 穿透)之后:剩下的甲再打 (100 − N)% 折。</summary>
        public int ArmorIgnorePercent { get; }

        /// <summary>按我方当前护盾加伤(D1 Task 3,崩岩 40):加在**每个主目标的第一段**上,
        /// 额外 + 玩家护盾(两桶之和,出手那一刻)× N%;全体(All)时每个目标都是主目标。0 = 不启用。</summary>
        public int ShieldStrikePercent { get; }

        /// <summary>Augment 的目标效果 Kind(D1 Task 4)。其余 kind 不读。</summary>
        public EffectKind AugmentKind { get; }

        /// <summary>Augment 加在目标效果的哪个字段(D1 Task 4)。其余 kind 不读。</summary>
        public AugmentField AugmentField { get; }

        /// <summary>Fold 挂上来的加成项:(百分点, 条件)。字表对象恒为空表;只有 Fold 产出的副本非空。
        /// internal:表现层不读它(卡面读的是 Amplify 效果本身)。</summary>
        /// <summary>效果目标选择器(D1 Task 5,附录 M10)。缺省 Primary = 玩家选的主目标。
        /// 只有 <see cref="EffectPickRules.Supports"/> 列出的 kind 读它;旧 <see cref="TargetAll"/> 等价于 All。</summary>
        public EffectPick Pick { get; }

        /// <summary>BurnSettleNow:结算一次但**不减层**(D1 Task 5,附录 M8,引燃)。其余 kind 不读。</summary>
        public bool KeepStacks { get; }

        /// <summary>HealSummons:true = 回复量按每只召唤物的 MaxHp × Value% 算(D1 Task 7,灵荫)。其余 kind 不读。</summary>
        public bool PercentOfMax { get; }

        /// <summary>附着载体(D1 Task 9,附录 M9,烟熏):非 null 时这条效果挂在目标身上的该载体上 ——
        /// 只对带本次出字上的灼的目标施加,回合数改为 -1,并挂一条隐藏的 <see cref="StatusKind.TraitRider"/>;
        /// 载体移除时一并移除。D1 只支持 Blind + Burn(ConfigLoader 拦其余组合)。</summary>
        public StatusKind? RiderOf { get; }

        /// <summary>这条效果来自哪条特性(<c>BattleEngine.TraitKey</c>,「字/槽/面」);由 <see cref="TraitRules.ForCast"/>
        /// 在出字折叠(Fold)与反应入队时打上:附着类效果(RiderOf / ShieldRecoil)一律打,其余非 Lv1 / Lv3 槽的特性效果也打
        /// (D2-火 G11,Weaken / Blind / Vulnerable 按 SourceId + TraitKey 分来源)。字表对象恒为 null。</summary>
        internal string TraitKey { get; private set; }
        /// <summary>本体百分比(D2-火 E5,连爆):&gt; 0 时这条特性效果的 Value = 本面本体**首条** DamageSingle 的 Value × N%,
        /// 在 <see cref="TraitRules.Fold"/>(出字时机)/ 入队反应前(暴击时 / 击杀时)解析成具体 Value;卡等级照常在结算时套。
        /// 本面没有 DamageSingle 时解析成 0。0 = 不启用。</summary>
        public int BodyPercent { get; }
        /// <summary>开局登记(D2-火 G13 / N12,炎炎、星星之火):&gt; 0 时这条效果**本场不执行**,出字时登记为开局效果,
        /// 之后 N 场每场开局对全场结算一次(<c>BattleEngine.RegisterOpening</c>,同类取最强)。0 = 普通效果。</summary>
        public int OpeningBattles { get; }

        /// <summary>引爆后保留的层数百分比(D2-火 N3 / G5,惊爆 50、焚天 34):&gt; 0 时全额伤害照打,之后层数设为 ⌊原层数 × N%⌋
        /// (0 层则清空),火力与附着不变;目标被打死残层清零。只给 Detonate;0 = 不保留(缺省,全部清空)。</summary>
        public int RetainPercent { get; }

        /// <summary>部分引爆的百分比(D2-火 N3 / G5,燥火攻心 50):只引爆 k = ⌊N × P%⌋ 层,伤害 = tri(N) − tri(N−k)
        /// (与「引爆只改兑现时机、不改总量」同口径),剩 N − k 层;k = 0 不引爆。只给 Detonate;缺省 100 = 全部引爆。≤0 兜回 100。</summary>
        public int PortionPercent { get; }

        /// <summary>计数缩放(D2-火 N4):Amplify 的百分点 / HealSelf 的回复量 × 计数。None = 不缩放。
        /// D2-金 E10:DamageSingle / Reshape 写 Morale(击数 + 战意)、Block / BlockMod 写 Morale(次数 = 战意,配 ScaleMin)、
        /// Morale 写 ExtraHitTarget(值 × 多命中的敌人数)。哪个 Kind 认哪档由 ConfigLoader 拦。</summary>
        public ScaleBasis ScaleBy { get; }

        /// <summary>计数缩放后的上限(只给 Amplify,单位 = 百分点;燥裂 50)。0 = 不设上限。</summary>
        public int ScaleCap { get; }

        /// <summary>每击附带(D2-火 N4b,跨计划 Q23 通用形态):DamageSingle 每打出一击之后,对**这一击的目标**依次结算这些效果
        /// (targetIndex = 这一击的落点;我方侧效果照常作用于我方)。不经新的 ApplyEffects,不触发重入守卫;等级 / 五行 L3 照常套。
        /// 火:炎刃(BurnSettleNow keep)、四炎 / 火花四溅(BurnSingle 1);金:每击破甲 / 流血 / 战意。空表 = 不附带(缺省)。</summary>
        public IReadOnlyList<EffectDef> PerHit { get; }

        /// <summary>每击附带从第几击起(1 起算;按**这个目标**身上的击序,金·剁骨「从第 N 击起」)。缺省 1 = 每一击;≤0 兜回 1。</summary>
        public int PerHitFrom { get; }

        /// <summary>散射每一发的伤害百分比(D2-火 N4b,火花四溅 50):Shape == Scatter 时每一发(含首发)都打这个折。缺省 100 不做乘除。</summary>
        public int ShotPercent { get; }

        /// <summary>门槛(D2-火 Task 3,炽焰):附着在灼上的减攻(Weaken + RiderOf)只在目标自身灼 ≥ N 层时生效
        /// (写进 <see cref="StatusEffect.MinBurn"/>,EnemyState.Attack 读)。0 = 无门槛(缺省)。只给附着的 Weaken。</summary>
        public int MinBurn { get; }

        /// <summary>追加一击的发数口径(D2-火 N9,星火):true = 本次出字命中过(HitTargets)的敌人里,出字前带灼的每名各追加一发
        /// (每发各自选目标);false = 一发(缺省)。只给 ExtraStrike。</summary>
        public bool PerBurningHit { get; }

        /// <summary>焚城结算的火力(D2-火 N6):只在 ResolveDefeat 入队的 BurnBurst 反应上非 0 —— 死者灼的火力
        /// (StatusEffect.Potency)。字表对象恒为 0。</summary>
        internal int BurstPotency { get; private set; }

        // ---- D2-金 Task 1 ----

        /// <summary>补满(E9,千锤 / 金刚 / 金玉满堂):只给 Morale —— 战意直接设为上限(不算溢出、顶满不发事件)。Value 不用。</summary>
        public bool Fill { get; }

        /// <summary>反击百分比覆盖(E12):Block / BlockMod 读。反击 = 攻击面本体(吃等级)× N%。0 = 缺省 BattleConfig.BlockCounterPercent。</summary>
        public int CounterPercent { get; }

        /// <summary>计数缩放的下限(E10,双金合璧「至少 2」):只给 ScaleBy == Morale 的 Block / BlockMod。0 = 不设。</summary>
        public int ScaleMin { get; }

        /// <summary>按被杀者最大生命回复(E13,割取):只给 HealSelf —— 回复量 = 反应目标(死者)MaxHp × Value%;
        /// 百分比不吃卡等级 / 五行 L3 / 攻击力(Q19),照常吃 Amplify Heal 与泉。</summary>
        public bool OfVictimMaxHp { get; }

        // ---- D2-金 Task 2:格挡附带(附录 J1)。只写在 BlockMod 上,Fold 时搬到本面第一条 Block;
        //      出字时再搬进 Block 状态(StatusEffect 同名字段)。全缺省 = 原格挡,逐位恒等 ----

        /// <summary>贯穿反击(锥立,token `counterShape Column`):反击打完攻击者后,再打同列其余存活敌人(每击本体反击的 70%),共用 60% 预算。</summary>
        public bool CounterColumn { get; }

        /// <summary>反击击数(剁截,`counterHits N`):每击 = 反击量,逐击扣预算,攻击者中途死亡就停。0 / 1 = 一击。</summary>
        public int CounterHits { get; }

        /// <summary>立威(`counterExecute N`):反击前攻击者生命 &lt; N% 时直接斩杀(杂兵,不吃预算);Boss 改为本次反击 ×2(吃预算)。</summary>
        public int CounterExecuteBelow { get; }

        /// <summary>格挡流血(刀山 / 匿锋,`blockBleed N`):每次格挡被消耗时给攻击者挂流血;量 = N(吃卡等级)× 攻击力,出字时定死。</summary>
        public int BlockBleed { get; }

        /// <summary>格挡加战意(坚营,`blockMorale N`):每次格挡被消耗时玩家战意 +N(木灵格挡也加给玩家)。</summary>
        public int BlockMorale { get; }

        /// <summary>反击击杀返还 AP(得利,`killRefund N`):反击 / 立威击杀时玩家挂 ApRefund,下回合开始 +N AP,每轮至多一次。</summary>
        public int KillRefundAp { get; }

        // ---- D2-金 Task 3 ----

        /// <summary>斩杀溅射(J4,铡刀落,token `executeSplash N`):配 <see cref="ExecuteKills"/>,TryExecuteKill 斩杀成功后
        /// (Boss 不会被斩杀,故不触发)对死者同排左右存活敌人各打「死者 MaxHp × N%」—— 本字元素、过生克与护甲、不暴击、
        /// source = ExecuteSplash;溅射造成的击杀不入队击杀时 / 斩杀时(R4)。Reshape 携带时 Fold 照抄(E7)。0 = 不溅射。</summary>
        public int ExecuteSplashPercent { get; }

        internal IReadOnlyList<(int Percent, DamageCondition If, ScaleBasis Per, int Cap)> AmpTerms { get; private set; } = NoAmpTerms;

        /// <summary>是否被 Fold 挂上了 Amplify 加成。AmpTerms 是 internal,Data 层(ConfigLoader)只能经由这里判断 ——
        /// Data 是独立程序集,看不到 Core 的 internal(工装把两层编在一起,发现不了)。</summary>
        public bool HasAmpTerms() => AmpTerms.Count > 0;

        private static readonly (int, DamageCondition, ScaleBasis, int)[] NoAmpTerms = new (int, DamageCondition, ScaleBasis, int)[0];
        private static readonly EffectDef[] NoPerHit = new EffectDef[0];

        public EffectDef(EffectKind kind, int value,
            DamageCondition doubleVs = DamageCondition.None, bool persistOnce = false,
            int summonCount = 1, int summonAttack = 0, string summonChar = "木",
            int turns = 0, bool targetAll = false,
            SummonPassive passive = null, int summonShield = 0, int summonDefense = 0,
            int executeBelowPercent = 0, bool executeKills = false,
            int hitCount = 1, int pierce = 0,
            TargetArea shape = TargetArea.Single, int shapePercent = 100, int shots = 0,
            bool trueDamage = false, int armorStrikePercent = 0,
            AmpScope scope = AmpScope.Damage, DamageCondition onlyIf = DamageCondition.None,
            int hitPercent = 100, bool forceCrit = false, int armorIgnorePercent = 0, int shieldStrikePercent = 0,
            EffectKind augmentKind = EffectKind.DamageSingle, AugmentField augmentField = AugmentField.Count,
            EffectPick pick = EffectPick.Primary, bool keepStacks = false, bool percentOfMax = false,
            StatusKind? riderOf = null, int bodyPercent = 0, int openingBattles = 0,
            int retainPercent = 0, int portionPercent = 100, ScaleBasis scaleBy = ScaleBasis.None, int scaleCap = 0,
            IReadOnlyList<EffectDef> perHit = null, int perHitFrom = 1, int shotPercent = 100, int minBurn = 0,
            bool perBurningHit = false,
            bool fill = false, int counterPercent = 0, int scaleMin = 0, bool ofVictimMaxHp = false,
            bool counterColumn = false, int counterHits = 0, int counterExecuteBelow = 0,
            int blockBleed = 0, int blockMorale = 0, int killRefundAp = 0,
            int executeSplashPercent = 0)
        {
            Kind = kind;
            Value = value;
            DoubleVs = doubleVs;
            PersistOnce = persistOnce;
            SummonCount = summonCount;
            SummonAttack = summonAttack;
            SummonChar = summonChar;
            Turns = turns;
            TargetAll = targetAll;
            Passive = passive;
            SummonShield = summonShield;
            SummonDefense = summonDefense;
            ExecuteBelowPercent = executeBelowPercent;
            ExecuteKills = executeKills;
            HitCount = hitCount <= 0 ? 1 : hitCount;
            Pierce = pierce;
            Shape = shape;
            ShapePercent = shapePercent <= 0 ? 100 : shapePercent;
            Shots = shots;
            TrueDamage = trueDamage;
            ArmorStrikePercent = armorStrikePercent;
            Scope = scope;
            OnlyIf = onlyIf;
            HitPercent = hitPercent <= 0 ? 100 : hitPercent;
            ForceCrit = forceCrit;
            ArmorIgnorePercent = armorIgnorePercent;
            ShieldStrikePercent = shieldStrikePercent;
            AugmentKind = augmentKind;
            AugmentField = augmentField;
            Pick = pick;
            KeepStacks = keepStacks;
            PercentOfMax = percentOfMax;
            RiderOf = riderOf;
            BodyPercent = bodyPercent;
            OpeningBattles = openingBattles;
            RetainPercent = retainPercent;
            PortionPercent = portionPercent <= 0 ? 100 : portionPercent;
            ScaleBy = scaleBy;
            ScaleCap = scaleCap;
            PerHit = perHit ?? NoPerHit;
            PerHitFrom = perHitFrom <= 0 ? 1 : perHitFrom;
            ShotPercent = shotPercent <= 0 ? 100 : shotPercent;
            MinBurn = minBurn;
            PerBurningHit = perBurningHit;
            Fill = fill;
            CounterPercent = counterPercent;
            ScaleMin = scaleMin;
            OfVictimMaxHp = ofVictimMaxHp;
            CounterColumn = counterColumn;
            CounterHits = counterHits;
            CounterExecuteBelow = counterExecuteBelow;
            BlockBleed = blockBleed;
            BlockMorale = blockMorale;
            KillRefundAp = killRefundAp;
            ExecuteSplashPercent = executeSplashPercent;
        }

        /// <summary>焚城的结算效果(D2-火 N6,只由 ResolveDefeat 入队):对全体存活敌人按灼烧公式结算 <paramref name="stacks"/> 层一次。</summary>
        internal static EffectDef BurnBurstOf(int stacks, int potency, string traitKey) =>
            new EffectDef(EffectKind.BurnBurst, stacks, pick: EffectPick.All) { BurstPotency = potency, TraitKey = traitKey };

        /// <summary>带覆盖字段的复制(只给 <see cref="TraitRules.Fold"/> 用;Task 4 起可覆盖 Value / Turns):字表里的 EffectDef 是多张字 / 多场战斗
        /// 共享的不可变对象,折叠一律产出新对象,绝不改原件。null = 沿用原值。</summary>
        internal EffectDef With(TargetArea? shape = null, int? shapePercent = null, int? shots = null,
            int? hitCount = null, int? hitPercent = null, bool? forceCrit = null,
            int? armorIgnorePercent = null, int? shieldStrikePercent = null, int? armorStrikePercent = null,
            IReadOnlyList<(int Percent, DamageCondition If, ScaleBasis Per, int Cap)> ampTerms = null,
            int? value = null, int? turns = null, string traitKey = null, SummonPassive passive = null,
            EffectPick? pick = null, IReadOnlyList<EffectDef> perHit = null, int? perHitFrom = null, int? shotPercent = null,
            int? executeBelowPercent = null, bool? executeKills = null, ScaleBasis? scaleBy = null, int? scaleMin = null,
            int? counterPercent = null,
            bool? counterColumn = null, int? counterHits = null, int? counterExecuteBelow = null,
            int? blockBleed = null, int? blockMorale = null, int? killRefundAp = null,
            int? executeSplashPercent = null) =>
            new EffectDef(Kind, value ?? Value, DoubleVs, PersistOnce, SummonCount, SummonAttack, SummonChar,
                turns ?? Turns, TargetAll, passive ?? Passive, SummonShield, SummonDefense,
                executeBelowPercent ?? ExecuteBelowPercent, executeKills ?? ExecuteKills,
                hitCount ?? HitCount, Pierce, shape ?? Shape, shapePercent ?? ShapePercent, shots ?? Shots,
                TrueDamage, armorStrikePercent ?? ArmorStrikePercent, Scope, OnlyIf,
                hitPercent ?? HitPercent, forceCrit ?? ForceCrit,
                armorIgnorePercent ?? ArmorIgnorePercent, shieldStrikePercent ?? ShieldStrikePercent,
                AugmentKind, AugmentField, pick ?? Pick, KeepStacks, PercentOfMax, RiderOf, BodyPercent, OpeningBattles,
                RetainPercent, PortionPercent, scaleBy ?? ScaleBy, ScaleCap, perHit ?? PerHit, perHitFrom ?? PerHitFrom, shotPercent ?? ShotPercent,
                MinBurn, PerBurningHit, Fill, counterPercent ?? CounterPercent, scaleMin ?? ScaleMin, OfVictimMaxHp,
                counterColumn ?? CounterColumn, counterHits ?? CounterHits, counterExecuteBelow ?? CounterExecuteBelow,
                blockBleed ?? BlockBleed, blockMorale ?? BlockMorale, killRefundAp ?? KillRefundAp,
                executeSplashPercent ?? ExecuteSplashPercent)
            {
                AmpTerms = ampTerms ?? AmpTerms,
                TraitKey = traitKey ?? TraitKey,
                BurstPotency = BurstPotency,
            };
    }
}
