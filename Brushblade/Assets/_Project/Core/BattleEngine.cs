using System;
using System.Collections.Generic;
using System.Linq;

namespace Brushblade.Core
{
    public enum BattlePhase
    {
        PlayerTurn,
        Won,
        Lost,
        DropChoice, // 回合掉字遇满库:停下让玩家替换或跳过(2026-08-04)
    }

    /// <summary>开场推进的一拍(2026-08-17)。构造函数在开场就把推进跑完了,表现层靠这些
    /// 数据把过程逐拍播出来 —— 否则玩家只会看到「进战斗即已打完」(spec §5.7)。
    /// 字段与表现层每播一拍所需的东西一一对应:谁动、跨了几拍(定条动画时长)、
    /// 推进后全场计量器(条的终点)、这一拍产生的事件(交给 Juice 播)。</summary>
    public readonly struct OpeningStep
    {
        public ActorRef Actor { get; }
        public int Ticks { get; }
        public int PlayerMeter { get; }
        public IReadOnlyList<int> SummonMeters { get; }
        public IReadOnlyList<int> EnemyMeters { get; }
        public IReadOnlyList<BattleEvent> Events { get; }

        public OpeningStep(ActorRef actor, int ticks, int playerMeter,
            IReadOnlyList<int> summonMeters, IReadOnlyList<int> enemyMeters,
            IReadOnlyList<BattleEvent> events)
        {
            Actor = actor;
            Ticks = ticks;
            PlayerMeter = playerMeter;
            SummonMeters = summonMeters;
            EnemyMeters = enemyMeters;
            Events = events;
        }
    }

    public enum BattleError
    {
        None,
        BattleOver,
        NotEnoughAp,
        NotCastable,   // 字不在字库(且不是池中可直出的部件)
        InvalidTarget,
        ForgeFailed,   // 拆/合被拆合引擎拒绝(细节见 LastForgeError)
        SummonCapFull, // 前排召唤已满(2026-07-25 强阻断):不吃 AP、不消耗字,由 UI 确认后带 replaceSummon 重出
    }

    /// <summary>召唤槽的占用状态(2026-08-20)。表现层据此决定点该槽的后果:
    /// Empty / Corpse 直接落位,Alive 要先弹顶替确认。</summary>
    public enum SlotState
    {
        Empty,   // 空槽
        Corpse,  // 尸体占着(可被覆盖,也可被「复活」就地救回)
        Alive,   // 存活召唤物占着
    }

    /// <summary>战斗规则参数(基准值来自第 10 章 10.1)。</summary>
    public sealed class BattleConfig
    {
        /// ⚠ 缺省 50 是**旧量级**的遗留,与 <c>MetaRules.MaxHpFor(1) = 500</c> 差一个数量级
        /// (2026-08-12 T1 量级 ×10 时刻意没跟着抬)。生产侧 <c>GameRoot</c> 与两个工装
        /// 都显式注入,缺省只服务测试夹具 —— 那些夹具的怪攻也还是旧量级的合成值,与 50 自洽,
        /// 一起抬会把约 25 条断言变成 500−5=495 这种非机械改动。
        /// 走缺省 + 新量级敌人的话玩家一回合暴毙,是**响亮**的失败不是静默的,所以留着。
        /// T3 配值时会连同夹具一起重做。
        public int PlayerMaxHp { get; set; } = 50;

        /// <summary>攻击力基准。<see cref="PlayerAttack"/> 等于此值时,
        /// 伤害与引入攻击力之前**逐字节相同** —— 这是 E-b1 的验收硬线。</summary>
        public const int AttackBaseline = 100;

        /// <summary>玩家攻击力(19.2.1 角色属性)。由 GameRoot 按
        /// <c>MetaRules.AttackFor(角色等级)</c> 注入;工装与测试不给就取基准值。</summary>
        public int PlayerAttack { get; set; } = AttackBaseline;

        /// <summary>暴击伤害倍率(百分比)。150 = ×1.5(2026-08-12 用户裁定;E-b5 重平衡时再调)。
        /// 做成常量而不是配置字段:它只有一个取值来源,做成字段就要进快照或靠 config 传,
        /// 是给单次使用造抽象。</summary>
        public const int CritMultiplierPercent = 150;

        /// <summary>玩家暴击率(百分点,19.2.1 角色属性)。**基准恒 0** —— 2026-08-12 用户裁定:
        /// 暴击**不随角色等级成长**,只靠字(锋)与将来的养成技能给,所以 MetaRules 没有对应的
        /// CritFor 曲线,GameRoot 也不注入。
        ///
        /// 默认 0 是 E-b2 的验收硬线:<see cref="BattleEngine.RollCrit"/> 在 ≤0 时直接短路,
        /// 一次随机都不摇 → 随机流逐位不变 → 伤害与引入暴击之前逐字节相同。
        /// 留这个字段是给将来的被动技能注入用,与 <see cref="PlayerAttack"/> 并排。</summary>
        public int PlayerCritChance { get; set; }

        /// <summary>玩家护甲**点数**(19.2.1 角色属性,2026-08-12,E-b4 T2)。
        /// 敌人每记挥击从伤害里减这么多,下钳 0。
        ///
        /// **基准恒 0** 是 T2 的验收硬线:<c>max(0, x − max(0, 0 − 0)) == x</c> ——
        /// 点数层全场为 0 时逐字节恒等,于是「把点数层接进去」这一步不需要改任何既有断言。
        ///
        /// ⚠ **战斗中永不被写**(spec §4.5.3),与 <see cref="EnemyState.Defense"/> 同一条硬约束:
        /// 局内的增(DefenseBuff)/ 减(ArmorBreak)全部走 <c>_playerStatuses</c>,
        /// 而它本来就进 BattleSnapshot.PlayerStatuses —— 零新增快照字段。
        /// T3 起由 GameRoot 按 <c>MetaRules.DefenseFor(角色等级)</c> 注入,与 <see cref="PlayerAttack"/> 并排。</summary>
        public int PlayerDefense { get; set; }

        /// <summary>玩家闪避(**百分点**,19.2.1 角色属性,2026-08-12,E-b4 T4)。
        /// 敌人打玩家的命中率 = <c>100 − 攻击者致盲 − 本值</c>。
        ///
        /// **不对称**(用户拍板):玩家攻击**永远必中**,敌人**没有**闪避 ——
        /// 所以「命中」不是玩家属性,玩家打敌人那条链根本不调 <see cref="BattleEngine.AttackHits"/>。
        ///
        /// **基准恒 0** 是 T4 的验收硬线:0 时命中率 = 100,<c>AttackHits</c> 走
        /// <c>hitRate ≥ 100</c> 短路,**一次随机都不摇** —— _random 的消费方只有回合掉字、
        /// AttackHits、EnemyState 构造抖动三处,无条件摇会平移掉落序列让依赖种子的测试全红。
        ///
        /// ⚠ **战斗中永不被写**(spec §4.5.3),同 <see cref="PlayerDefense"/>:
        /// 局内增益全部走 <see cref="StatusKind.DodgeBuff"/> 进 <c>_playerStatuses</c>,
        /// 而它本来就进 BattleSnapshot.PlayerStatuses —— 零新增快照字段。
        /// 由 GameRoot 按 <c>MetaRules.DodgeFor(角色等级)</c> 注入,与 <see cref="PlayerAttack"/> 并排。</summary>
        public int PlayerDodge { get; set; }

        /// <summary>玩家基础速度(2026-08-15,ATB 改造)。基准 100 = 与敌人同速 = 一人一手。
        /// 有效速度 = 本值 + 所有 SpeedModifier 之和,再由 TurnScheduler.ClampSpeed 钳到 [25,400]。
        ///
        /// ⚠ **战斗中永不被写**,同 <see cref="PlayerDefense"/> / <see cref="PlayerDodge"/>:
        /// 局内的加速/减速全部走 <see cref="StatusKind.SpeedModifier"/> 进 _playerStatuses,
        /// 而它本来就进 BattleSnapshot.PlayerStatuses —— 零新增快照字段。
        /// T4 起由 MetaRules.BuildBattleConfig 按 SpeedFor(角色等级) 注入。</summary>
        public int PlayerSpeed { get; set; } = 100;

        public int ApPerTurn { get; set; } = 3;
        public int LibraryCapacity { get; set; } = 6;  // 2026-07-06 拍板;局内广告可 +2
        public int PoolCapacity { get; set; } = 10;    // 同上
        public int DropsPerTurn { get; set; } = 1; // 回合掉字数(2026-08-04:由「掉 2 部件」改为「掉 1 字」)
        public int BossPhaseJitterPercent { get; set; } = 8; // Boss 换阶阈值浮动幅度(±总血%,2026-07-19)
        // 阶段内第 N 个敌方回合进入蓄力,下回合释放(计数每阶段重开,见 EnemyState.ApplyPhaseStats)。
        // 2 = 普攻、蓄力、释放 —— 阶段撑满 3 个敌方回合才吃得到大招(2026-07-29)
        public int BossChargeEvery { get; set; } = 2;
        /// <summary>历史遗留字段(2026-08-04):回合掉落已改为从 <see cref="UnlockedChars"/> 掉字
        /// (见 StartTurn),此字段不再有任何读取方,只剩调用方仍在赋值。留着未删是因为跨
        /// Core/Data/Presentation/配置校验四处引用,清理超出掉落改造本次范围。</summary>
        public IReadOnlyList<string> DropTable { get; set; } = Array.Empty<string>();

        /// <summary>可合成的字集合 = 玩家的出阵列表(2026-07-20 拍板:没编入出阵就合不出来,
        /// 与战利品同源);null = 不限(工装与旧调用)。</summary>
        public IReadOnlyCollection<string> UnlockedChars { get; set; }

        /// <summary>同配置、只换血量上限的副本(局内上限奇遇用,2026-08-04)。
        /// 浅拷贝:调用方拿到独立实例,改它不会波及传进来的那份。</summary>
        public BattleConfig WithPlayerMaxHp(int playerMaxHp)
        {
            var copy = (BattleConfig)MemberwiseClone();
            copy.PlayerMaxHp = playerMaxHp;
            return copy;
        }
    }

    /// <summary>结算事件(供表现层做打击感,13.3;架构:表现监听 Core 事件,不反向驱动)。</summary>
    public enum BattleEventKind
    {
        Damage,      // 我方对敌伤害(TargetIndex = 敌人下标)
        Burn,        // 施加灼烧层数
        Shield,      // 获得护盾(TargetIndex = −1 玩家)
        BurnTick,    // 回合末灼烧结算伤害
        BleedTick,   // 回合末流血结算伤害(无属性,不走生克;2026-08-04)
        EnemyDied,   // 敌人被消灭
        EnemyAttack, // 敌方对玩家伤害(Amount = 总伤,含被护盾吸收部分;TargetIndex = 攻击者敌人下标,驱动冲刺动效)
        EnemySplit,  // 叠字怪分裂(TargetIndex = 原体下标)
        BossPhase,   // 成语 Boss 进入新阶段(Amount = 新阶段下标)
        Heal,        // 治疗自身(Amount = 实际回复量,2026-07-19)
        Summon,      // 召唤前排单位(Amount = 血量;SecondIndex = 被顶替的槽位,新增则 −1)
        SummonHit,   // 召唤物替玩家承伤(Amount = 伤害;TargetIndex = 攻击者敌人下标,驱动冲刺动效)
        SummonAttack,     // 召唤物反击敌人(TargetIndex = 敌人下标;仅驱动动效,伤害走 Damage)
        EnemyBuff,   // 加攻(标点小妖给同伴 / 焦痕受击自燃;TargetIndex = 被加成的敌人)
        EnemyRevealed, // 通假字现形/生僻字被读懂(TargetIndex = 该敌人)
        BossCharging,   // Boss 进入蓄力回合(Amount = 即将释放的 BossSkill;驱动预警 UI)
        BossSkillCast,  // Boss 释放技能(Amount = BossSkill);随后是各目标的受击事件
        ShieldBroken,   // 护盾被倾覆清空(TargetIndex = −1,Amount = 清掉的总量)
        Regrow,      // 缺笔妖自补全(TargetIndex = 该敌人,Amount = 实际回血,SecondIndex = 补全进度 1~3)。
                     // 原先是**静默**结算的:模型瞬时回血、表现层只在末次重绘看到结果,
                     // 于是玩家看到的是「召唤物砸上去不掉血」「还没打就满血」(2026-07-29 实测)
        // 2026-08-06 M2:Dispel/Cleanse/Immunity 三个事件曾经发出但全代码库没有任何读取方
        // (与诅咒同型——表现层直接读敌人/玩家的 Statuses 画 chip,再加事件是多余的),已删除。
        // ImmunityBlocked 不在此列:它确实有消费方(Juice.cs 飘「免」字)。
        ImmunityBlocked, // 免疫挡下一记(TargetIndex = 攻击者敌人下标,Amount = 挡掉的伤害;2026-08-06)
        Missed,      // 攻击被打空(TargetIndex = 攻击者敌人下标,SecondIndex = 被打空的召唤物下标,玩家为 −1;2026-08-07)
        Detonate,    // 灼烧引爆(TargetIndex = 被引爆的敌人,Amount = 引爆伤害;2026-08-09)
        ActorActed,  // 阶段分隔:每个行动者的事件段以此开头(TargetIndex = 行动者下标,Amount = (int)ActorKind;
                     // 逐格驱动后表现层不再需要猜段边界,2026-08-16 换掉 EnemyTurnBegan)
    }

    public readonly struct BattleEvent
    {
        public BattleEventKind Kind { get; }
        public int TargetIndex { get; }  // 敌人下标;玩家侧为 −1
        public int Amount { get; }
        public int SecondIndex { get; }  // 关联召唤物下标(SummonAttack=发起者 / SummonHit=承伤者 / Summon=被顶替槽位;其余 −1)
        public int Absorbed { get; }     // EnemyAttack:Amount 中被护盾吃掉的部分(其余 = 实际掉血);别的事件 0

        /// <summary>Damage 事件专用:这一记是不是暴击(2026-08-12,E-b2);其余事件恒 false。
        /// 刻意**不新增 BattleEventKind.Crit** —— 单独发一条暴击事件会逼表现层做事件配对
        /// (「刚才那条 Damage 是不是这条 Crit 的」),而这套代码库已经在配对判据上栽过两次
        /// (EnemyDied 必须紧跟致死伤害、lastDamageTarget 不能只看紧邻)。
        /// 暴击是那一记伤害的**属性**,就该长在那条事件上。</summary>
        public bool Crit { get; }

        public BattleEvent(BattleEventKind kind, int targetIndex, int amount, int secondIndex = -1,
            int absorbed = 0, bool crit = false)
        {
            Kind = kind;
            TargetIndex = targetIndex;
            Amount = amount;
            SecondIndex = secondIndex;
            Absorbed = absorbed;
            Crit = crit;
        }
    }

    /// <summary>战斗状态机(第 3 章 3.5 回合流程 / 3.7 结算顺序)。</summary>
    public sealed class BattleEngine
    {
        private readonly RecipeGraph _graph;
        private readonly BattleConfig _config;
        private readonly GameRandom _random;
        private readonly List<EnemyState> _enemies = new();
        /// <summary>召唤物槽位(2026-08-20):**定长 6,下标即槽位**。0/1/2 = 前排,3/4/5 = 后排。
        /// null = 空槽;Hp &lt;= 0 = 尸体,仍占槽,可被复活就地救回(引擎从不移除阵亡召唤物)。
        /// 选定长数组而非「紧凑 List + Slot 字段」的理由见 spec §3.1:事件的 SecondIndex、
        /// 表现层的血条引用、存档下标现在三者是同一个数,槽位化后仍是同一个数。</summary>
        private readonly SummonState[] _summons = new SummonState[SummonCap];
        private const int SummonCap = 6;      // 场上召唤物槽位数(2026-08-03:4 → 6)
        private const int FrontRowSize = 3;   // 前排槽位数(2026-08-20):槽 [0, FrontRowSize) 为前排
        private const int EnemyCap = 6;  // 场上敌人上限(2026-08-03),分裂怪据此守闸
        // 每排敌人上限(2026-08-20)。转引 Targeting.RowCapacity 而不是再写一个 3 ——
        // 表现层按列取固定格位(BattleView.DrawEnemies),两处上限一旦分叉,
        // 分配出来的 Column 就会越过表现层的格位数组,静默变成一次崩溃(2026-08-22 评审)。
        private const int EnemyRowCap = Targeting.RowCapacity;
        // 焦痕受击存活的加攻(**百分点**,2026-08-12 由「+2 点」换算而来:焦痕 BaseAttack = 4,
        // 50% × 4 = 2,对任意层数逐位等价 —— AttackBuffUnitTests 的焦痕序列守着这条零行为变化)
        private const int ScorchGain = 50;
        // 标点小妖给同伴的加攻(百分点,2026-08-12 用户拍板)。改动前送的是「施加者自身攻击力」
        // = 固定 +2,而敌人平均攻击 ≈ 4,取 50% 恰好保住平均值,同时修掉「加给攻 2 的怪是 +100%、
        // 加给攻 8 的怪只有 +25%」这个 4 倍偏差。
        private const int PunctuationBuffPercent = 50;
        private const int SearStacks = 1;  // 灯花每次攻击给玩家挂的灼烧层数(2026-08-06)
        private const int CurseTurns = 2;          // 诅咒持续回合(2026-08-05)
        private const string CurseSourceId = "诅咒"; // 全局同源:多只召唤物重复施加只刷新不叠
        private const int MoralePerStack = 10;  // 战意每层的攻击加成(2026-08-12)
        private const int MoraleMaxStacks = 5;  // 战意层数上限:满层 +50 攻击,刚好追平剡单张的量

        private ForgeState _forge;
        private readonly IReadOnlyDictionary<string, int> _cardLevels; // 局外卡等级(19.3.2;null = 全 1 级)
        private int _burnPerStack = 20;     // 灼烧每层结算伤害(10.2;炽 +10,可叠加;2026-08-12 随全表量级 ×10)
        private int _shieldNormal;          // 普通护盾:关间/段间都延续,整场爬塔通吃(2026-07-26)
        private int _shieldPersist;         // 豁免桶护盾(堡):吸伤时垫在普通桶之后

        /// <summary>玩家的行动计量器(2026-08-15,ATB 改造):与敌人/召唤物同走一套模型,
        /// 攒满 TurnScheduler.Threshold 就轮到玩家。进 BattleSnapshot。恒非负——开局与所有人
        /// 一样从 0 起步,不需要任何先手/负债/懒消费之类的特例(2026-08-18 第六次审查订正:
        /// 病根不是 tie-break 方向本身,是「玩家优先 + 构造函数给的免费先手」这个组合;
        /// 免费先手已随本次改造删除,前四轮那些记账手法因此一个都不需要,完整推理见
        /// BuildSlots 的优先级注释)。</summary>
        public int PlayerActionMeter { get; private set; }

        // 回合掉字遇满库时挂起的那个字;Phase == DropChoice 期间非 null
        private string _pendingDrop;

        /// <summary>战意首回合宽限(2026-08-18):本回合是「从 0 层起手」的那一回合,
        /// 回合末免一次递减。<see cref="AddPlayerCounter"/> 新建战意时置起,
        /// <see cref="TickPlayerStatuses"/> 消费掉。要进快照 —— 不存的话续爬后
        /// 起手那一回合会白掉一层。</summary>
        private bool _moraleGraceTurn;

        /// <summary>玩家侧状态容器(HoT / 减伤,2026-08-04 统一迁入状态容器)。减伤 SourceId = 字
        /// ID,同字覆盖 = 只刷新不叠加;TurnsLeft = -1 段内持久,跨战斗携带见 RunEngine._carriedStatuses。</summary>
        private readonly StatusBag _playerStatuses = new();

        /// <summary>SourceId 自增序号(2026-08-04):HoT(技能机制详表「滋」)与 AttackBuff
        /// (标点小妖加攻、焦痕受击自燃,Task 5 后接入)都允许同字/同源叠加,靠每次施放给一个
        /// 独一无二的 SourceId 绕开 Apply() 的同源覆盖。要进快照——续爬后计数器归零会与快照里
        /// 恢复的条目撞号,撞上就被意外覆盖。</summary>
        private int _statusSerial;

        /// <summary>本场生效的玩家攻击力 = 角色属性(config)+ 局内增益 + 战意。
        /// 局内增益复用 <see cref="StatusKind.AttackBuff"/> —— 敌人侧的标点小妖加攻、
        /// 焦痕受击自燃早就在用同一个 Kind,不新增枚举值;2026-08-12 起两侧的**单位也统一**
        /// 成百分点(敌人侧见 <see cref="EnemyState.Attack"/>),这里的加数就是那边的同一个比值。
        /// 战意(2026-08-12,战/戮)单开一个 Kind:它的 Magnitude 是**层数**不是加成值,
        /// 混进 AttackBuff 会既丢掉层数上限又让 +1 层被当成 +1 攻击。
        /// 钳到 ≥0 与 <see cref="EnemyState.Attack"/> 同口径:负攻击力会打出负伤害,
        /// 等于给敌人回血,且全程无声。</summary>
        public int EffectiveAttack =>
            Math.Max(0, _config.PlayerAttack
                + _playerStatuses.TotalMagnitude(StatusKind.AttackBuff)
                + _playerStatuses.TotalMagnitude(StatusKind.Morale) * MoralePerStack);

        /// <summary>按玩家攻击力缩放一个输出值。**整数除**:
        /// <c>EffectiveAttack == AttackBaseline</c> 时 <c>value * 100 / 100 == value</c>,逐字节恒等。
        ///
        /// 刻意不用 <c>ceil</c>:<c>ceil(7 × 1.02) = 8</c> 等于 +14%,低数值字反而超额收益,
        /// 方向是错的。低数值字在攻击成长前期没反应是已知副作用,
        /// 真解法是 E-b5 抬高字表数值量级(见 spec 第十节)。</summary>
        private int ScaleByAttack(int value) => value * EffectiveAttack / BattleConfig.AttackBaseline;

        /// <summary>本场生效的暴击率(百分点)= 角色属性(config)+ 局内增益(锋),钳到 [0,100]。
        ///
        /// 单开 <see cref="StatusKind.CritBuff"/> 而不复用 AttackBuff:攻击加成与暴击率是两个
        /// 语义,挤进同一个 Kind 会让 TotalMagnitude(AttackBuff) 同时被两边读到。
        ///
        /// 上钳到 100 而不是放任溢出:100 以上摇不出更多暴击,但钳住才能让 <see cref="RollCrit"/>
        /// 的 ≥100 短路成为一条**必然到达**的路径(锋 叠满时靠它省掉摇点)。
        /// 下钳到 0 是防御性的,理由同 <see cref="AttackHits"/> 那条钳位。</summary>
        public int EffectiveCrit =>
            Math.Clamp(_config.PlayerCritChance + _playerStatuses.TotalMagnitude(StatusKind.CritBuff), 0, 100);

        /// <summary>一次暴击判定。**两端都短路,一次随机都不摇**。
        ///
        /// 下端(≤0)是 E-b2 的恒等性硬线:_random 的既有消费方只有回合掉字、
        /// <see cref="AttackHits"/>、EnemyState 构造时的 Boss 阈值浮动 —— 无条件摇会平移整条流,
        /// 让所有依赖种子的既有测试全红(与 AttackHits 的 hitRate ≥ 100 是同一个坑、同一个解法)。
        /// **E-b2 不得新增第四个无条件消费方。**
        ///
        /// 上端(≥100)对称处理:必暴时摇不摇结果都一样,不摇能让「暴击叠满」的玩法路径
        /// 同样不扰动随机流,也让测试可以在不注入 RNG 的前提下断言必暴。
        ///
        /// 比较式抄 AttackHits:Next(100) 吐 [0,99],chance = 1 即 1%、99 即 99%,无偏。</summary>
        private bool RollCrit()
        {
            int chance = EffectiveCrit;
            if (chance <= 0) return false;
            if (chance >= 100) return true;
            return _random.Next(100) < chance;
        }

        /// <summary>给玩家挂一层攻击增益。E-b3 的 剡/战意 会走正规的效果分支,
        /// 在那之前这是局内改变攻击力的唯一入口,现阶段只有测试在用。
        ///
        /// internal 而非 public(2026-08-11 用户裁定):它只为测试存在,不该出现在生产 API 面上。
        /// 见同目录 AssemblyInfo.cs 的 InternalsVisibleTo。
        /// SourceId 每次唯一(同 HoT / 焦痕自燃的做法):Apply() 会按 SourceId 覆盖同源条目,
        /// 不给唯一 id 的话第二次挂增益会覆盖第一次而不是叠加。</summary>
        internal void ApplyPlayerAttackBuff(int amount)
        {
            _playerStatuses.Apply(new StatusEffect
            {
                Kind = StatusKind.AttackBuff, Polarity = StatusPolarity.Buff,
                Magnitude = amount, TurnsLeft = -1,
                SourceId = $"debug-atk#{_statusSerial++}",
            });
        }

        /// <summary>这一记挥击面对的敌人有效护甲(点数,2026-08-12,E-b4)。
        ///
        /// <c>max(0, 基础护甲 − 破甲总量 − 穿透总量)</c> —— **一个钳位,两项都从同一个基础护甲里减**
        /// (spec §4.1.2)。护甲只有一层厚度:破甲削掉的与穿透穿过的合起来算,不嵌套、不重复扣;
        /// 外层 <c>max(0, …)</c> 保证削过头只是归零,绝不倒贴成增伤。
        ///
        /// <paramref name="pierce"/> = 本次效果自带的穿透(<see cref="EffectDef.Pierce"/>),
        /// 再加上玩家身上本场持续的 <see cref="StatusKind.PierceBuff"/>。
        ///
        /// 破甲(2026-08-12,T3 接入)是**目标身上**的持续状态,穿透是**攻击者**的本次视角 ——
        /// 两者削的是同一层厚度,故相加后一起减。破甲可叠、本场持久,所以读 TotalMagnitude
        /// 而不是 Find():多张破甲字接力削光一个坚壁 Boss 是它的设计玩法(战例二)。</summary>
        private int EffectiveEnemyDefense(EnemyState enemy, int pierce) => Math.Max(0,
            enemy.Defense
            - enemy.Statuses.TotalMagnitude(StatusKind.ArmorBreak)
            - (pierce + _playerStatuses.TotalMagnitude(StatusKind.PierceBuff)));

        /// <summary>玩家挨一记时的有效护甲(点数,2026-08-12,E-b4 T2)= 角色属性 + 局内护甲增益
        /// − 身上的破甲,下钳 0。与 <see cref="EffectiveAttack"/> / <see cref="EffectiveCrit"/> 同形:
        /// **基础值来自 config(战中不可变),变动量全在 <c>_playerStatuses</c> 里**。
        ///
        /// 敌人没有穿透通道(<see cref="EffectDef.Pierce"/> 是出字效果的字段),但破甲这条通道
        /// 本批就打通:第八章配「敌人破甲」时给玩家挂 <see cref="StatusKind.ArmorBreak"/> 即可,
        /// 引擎侧不需要再改(spec §4.5.4)。</summary>
        public int EffectivePlayerDefense => Math.Max(0,
            _config.PlayerDefense
            + _playerStatuses.TotalMagnitude(StatusKind.DefenseBuff)
            - _playerStatuses.TotalMagnitude(StatusKind.ArmorBreak));

        /// <summary>本场生效的玩家速度 = 角色属性(config)+ 局内 SpeedModifier,钳到 [25,400]。
        /// 与敌人侧 <see cref="EnemyState"/> 的算法同一条 —— 钳位统一收在
        /// <see cref="TurnScheduler.ClampSpeed"/>,两侧不许各写一份。</summary>
        public int EffectivePlayerSpeed => TurnScheduler.ClampSpeed(
            _config.PlayerSpeed + _playerStatuses.TotalMagnitude(StatusKind.SpeedModifier));

        /// <summary>本场生效的玩家闪避(百分点,2026-08-12,E-b4 T4)= 角色属性(config)
        /// + 局内增益(<see cref="StatusKind.DodgeBuff"/>),钳到 [0,100]。与
        /// <see cref="EffectiveAttack"/> / <see cref="EffectiveCrit"/> / <see cref="EffectivePlayerDefense"/>
        /// 同形:**基础值来自 config(战中不可变),变动量全在 <c>_playerStatuses</c> 里**。
        ///
        /// 敌人没有闪避这条轴(用户拍板的不对称口径):它只减少玩家挨打,不影响玩家的输出。
        /// 上钳 100 让 <see cref="AttackHits"/> 的 <c>hitRate ≤ 0</c> 短路成为一条**必然到达**
        /// 的路径(叠满时靠它省掉摇点);下钳 0 是防御性的,理由同 <see cref="AttackHits"/> 的钳位。</summary>
        public int EffectiveDodge =>
            Math.Clamp(_config.PlayerDodge + _playerStatuses.TotalMagnitude(StatusKind.DodgeBuff), 0, 100);

        public BattleEngine(RecipeGraph graph, BattleConfig config,
            IReadOnlyList<string> startingLibrary, IReadOnlyList<string> startingPool,
            IReadOnlyList<EnemyDef> enemies, int seed, int? startingHp = null,
            IReadOnlyDictionary<string, int> cardLevels = null,
            int startingNormalShield = 0, int startingPersistShield = 0,
            IReadOnlyList<SummonSnapshot> startingSummons = null,
            IReadOnlyList<StatusEffect> startingStatuses = null)
        {
            _graph = graph;
            _config = config;
            _cardLevels = cardLevels;
            _random = new GameRandom(seed);
            _forge = new ForgeState(new List<string>(startingLibrary), new List<string>(startingPool));
            foreach (var def in enemies)
                _enemies.Add(new EnemyState(def, config.BossPhaseJitterPercent, _random));
            AssignSlots();

            PlayerHp = startingHp ?? config.PlayerMaxHp;
            _shieldNormal = startingNormalShield;
            _shieldPersist = startingPersistShield;
            // 召唤物跨战斗保留(2026-08-03):与普通盾同口径,上一层活下来的原样入场(残血不回满)。
            // 携带的召唤物按原槽位落位(2026-08-20)。Slot 越界或撞车一律回落到最小空槽 ——
            // 携带态来源受控,这条只是防越界,不是会触发的分支。
            if (startingSummons != null)
                foreach (var summon in startingSummons)
                    PlaceCarried(SummonState.Restore(summon), summon.Slot);
            // 减伤跨战斗保留(2026-08-04):与普通盾同口径,段内持久,到段末才清。
            if (startingStatuses != null)
                _playerStatuses.CopyFrom(startingStatuses);

            Phase = BattlePhase.PlayerTurn;
            // 开场走调度(2026-08-17):不再直接开玩家回合 —— 那是改造前 AP 制的遗留,
            // 等于给玩家一次免费先手,而全场计量器此刻都还是 0。现在全场从 0 攒,
            // 谁先满谁先动;轮到玩家时 AdvanceOnce 的玩家分支会跑
            // BeginPlayerTurn → StartTurn(AP / 发牌 / Turn+1),所以这里不必直接调 StartTurn。
            //
            // 每拍都记进 _openingSteps:AdvanceOnce 每次开头都 _events.Clear(),不记的话
            // 开场抢先行动的单位(携带的满格召唤物、以后的高速敌人)表现全丢,玩家只会
            // 看到「进战斗即已打完」。见 spec §5.7。
            //
            // ⚠ 这个循环可能让战斗在构造函数返回前就分出胜负(携带满格召唤物秒掉弱敌),
            // Phase 会是 Won —— 表现层必须兜住(spec §5.7)。
            bool more;
            do
            {
                more = AdvanceOnce();
                _openingSteps.Add(CaptureOpeningStep());
            } while (more);
        }

        /// <summary>断点存档专用构造:不发牌、不开回合,状态全部由 <see cref="Restore"/> 灌进来。</summary>
        private BattleEngine(RecipeGraph graph, BattleConfig config,
            IReadOnlyDictionary<string, int> cardLevels, GameRandom random)
        {
            _graph = graph;
            _config = config;
            _cardLevels = cardLevels;
            _random = random;
            _forge = new ForgeState(new List<string>(), new List<string>());
        }

        /// <summary>战斗内断点存档(2026-07-27):摊平全部可变状态。
        /// 配置侧(字表/敌表定义/卡等级)不进快照,复原时由外层照原样传回。</summary>
        public BattleSnapshot Capture()
        {
            var snapshot = new BattleSnapshot
            {
                PlayerHp = PlayerHp,
                Ap = Ap,
                Turn = Turn,
                Phase = Phase,
                ShieldNormal = _shieldNormal,
                ShieldPersist = _shieldPersist,
                BurnPerStack = _burnPerStack,
                RandomState = _random.State,
                Library = new List<string>(_forge.Library),
                Pool = new List<string>(_forge.Pool),
                PendingDrop = _pendingDrop,
                StatusSerial = _statusSerial,
                PlayerActionMeter = PlayerActionMeter,
                MoraleGraceTurn = _moraleGraceTurn,
            };
            foreach (var enemy in _enemies) snapshot.Enemies.Add(enemy.Capture());
            for (int s = 0; s < SummonCap; s++)
                if (_summons[s] != null) snapshot.Summons.Add(_summons[s].Capture(s));
            foreach (var s in _playerStatuses.All) snapshot.PlayerStatuses.Add(s.Clone());
            return snapshot;
        }

        /// <summary>从断点存档复原。enemyDefs:id → 定义(分裂出的克隆与本体共用一个 Def,
        /// 所以按 id 查而不是按遭遇下标取)。</summary>
        public static BattleEngine Restore(BattleSnapshot snapshot, RecipeGraph graph, BattleConfig config,
            IReadOnlyDictionary<string, int> cardLevels, IReadOnlyDictionary<string, EnemyDef> enemyDefs)
        {
            var engine = new BattleEngine(graph, config, cardLevels, GameRandom.FromState(snapshot.RandomState))
            {
                PlayerHp = snapshot.PlayerHp,
                Ap = snapshot.Ap,
                Turn = snapshot.Turn,
                Phase = snapshot.Phase,
                _shieldNormal = snapshot.ShieldNormal,
                _shieldPersist = snapshot.ShieldPersist,
                _burnPerStack = snapshot.BurnPerStack,
                _pendingDrop = snapshot.PendingDrop,
                _statusSerial = snapshot.StatusSerial,
                PlayerActionMeter = snapshot.PlayerActionMeter,
                _moraleGraceTurn = snapshot.MoraleGraceTurn,
            };
            engine._forge = new ForgeState(new List<string>(snapshot.Library), new List<string>(snapshot.Pool));
            foreach (var enemy in snapshot.Enemies)
            {
                if (!enemyDefs.TryGetValue(enemy.DefId, out var def))
                    throw new InvalidOperationException($"存档里的字怪「{enemy.DefId}」不在本层遭遇定义中");
                engine._enemies.Add(EnemyState.Restore(enemy, def));
            }
            foreach (var summon in snapshot.Summons)
                engine.PlaceCarried(SummonState.Restore(summon), summon.Slot);
            engine._playerStatuses.CopyFrom(snapshot.PlayerStatuses ?? new List<StatusEffect>());
            return engine;
        }

        public BattlePhase Phase { get; private set; }
        public int Turn { get; private set; }
        public int Ap { get; private set; }
        /// <summary>每回合 AP 上限(UI 满格数 / 提示文案用;一气技能与局内的 利 都会抬高)。
        /// 必须与 <see cref="StartTurn"/> 那句同源:只改一边会出现「UI 画 3 格但实际有 4 AP」。
        /// 这里不减封字 —— 封字是**下回合**的临时扣减,不是上限本身。</summary>
        public int ApPerTurn => _config.ApPerTurn + _playerStatuses.TotalMagnitude(StatusKind.ApBoost);
        public int PlayerHp { get; private set; }
        public int MaxHp => _config.PlayerMaxHp;     // 本场生效的血量上限(局内奇遇可抬高,2026-08-04)

        /// <summary>待决议的掉落字(满库时挂起);无待决议时为 null。</summary>
        public string PendingDrop => _pendingDrop;
        public int PlayerShield => _shieldNormal + _shieldPersist;
        public int ShieldNormal => _shieldNormal;
        public int ShieldPersist => _shieldPersist;

        /// <summary>玩家侧状态容器(HoT / 减伤),供战斗结束时取回跨战斗延续(2026-08-04)。</summary>
        public StatusBag PlayerStatuses => _playerStatuses;
        public IReadOnlyList<string> Library => _forge.Library;
        public IReadOnlyList<string> Pool => _forge.Pool;
        public int LibraryCapacity => _config.LibraryCapacity;
        public int PoolCapacity => _config.PoolCapacity;

        /// <summary>广告扩容同步(2026-08-18):战斗持有的是 config 副本(RunEngine.BattleConfigForRun
        /// 恒拷贝),RunEngine.TryExpand* 抬完自己的原对象后须经此把本场战斗的上限一并抬起,
        /// 否则本场的掉字/合成/容量显示仍按旧上限走。</summary>
        internal void RaiseLibraryCapacity(int bonus)
        {
            _config.LibraryCapacity += bonus;
            // 挂起的掉字是在**旧上限**下判满库才停下的(StartTurn 里焊住 DropChoice)。上限抬高后
            // 若已放得下,就直接收下并回到玩家回合 —— 否则玩家看着 7/9 仍被要求「换掉哪一张」,
            // 广告白看(2026-08-18)。收下的口径与 ResolveDrop 一致:入库、清挂起、回玩家回合。
            if (Phase == BattlePhase.DropChoice && _forge.Library.Count < _config.LibraryCapacity)
            {
                var library = new List<string>(_forge.Library) { _pendingDrop };
                _forge = new ForgeState(library, _forge.Pool);
                _pendingDrop = null;
                Phase = BattlePhase.PlayerTurn;
            }
        }

        internal void RaisePoolCapacity(int bonus) => _config.PoolCapacity += bonus;

        /// <summary>可合成字集(= 出阵列表);null = 不限。表现层的拆合台提示按此过滤。</summary>
        public IReadOnlyCollection<string> UnlockedChars => _config.UnlockedChars;
        public IReadOnlyList<EnemyState> Enemies => _enemies;
        public IReadOnlyList<SummonState> Summons => _summons;
        public int SummonCapacity => SummonCap;
        public int AliveSummonCount => AliveSummons();

        /// <summary>前排槽位数(2026-08-20):槽 [0, FrontRow) 为前排,其余为后排。</summary>
        public int FrontRow => FrontRowSize;

        public SlotState SlotOccupancy(int slot)
        {
            if (slot < 0 || slot >= SummonCap || _summons[slot] == null) return SlotState.Empty;
            return _summons[slot].Alive ? SlotState.Alive : SlotState.Corpse;
        }

        public ForgeError LastForgeError { get; private set; }

        private readonly List<BattleEvent> _events = new();

        /// <summary>最近一次动作(Cast/EndTurn)产生的结算事件,动作开始时清空。</summary>
        public IReadOnlyList<BattleEvent> LastEvents => _events;

        /// <summary>拆(免 AP,2026-08-03 拍板)。</summary>
        public BattleError Dismantle(string charId)
        {
            if (Phase != BattlePhase.PlayerTurn) return BattleError.BattleOver;

            var result = ForgeEngine.TryDismantle(charId, _graph, _forge, _config.PoolCapacity, _config.LibraryCapacity);
            if (!result.Success)
            {
                LastForgeError = result.Error;
                return BattleError.ForgeFailed;
            }
            _forge = result.State;
            return BattleError.None;
        }

        /// <summary>合(1 AP)。</summary>
        public BattleError Compose(string charId)
        {
            if (Phase != BattlePhase.PlayerTurn) return BattleError.BattleOver;
            if (Ap < 1) return BattleError.NotEnoughAp;

            var result = ForgeEngine.TryCompose(charId, _graph, _forge, _config.LibraryCapacity,
                _config.UnlockedChars);
            if (!result.Success)
            {
                LastForgeError = result.Error;
                return BattleError.ForgeFailed;
            }
            _forge = result.State;
            Ap -= 1;
            return BattleError.None;
        }

        /// <summary>出字(ApCost):字库中的字,或池中可直出的部件(4.5 第二层,防卡手地板)。
        /// replaceSummon:前排满员时顶掉最前的召唤物入场(UI 弹窗确认后才置位),否则满员直接拒出。
        /// attackMode:把字拖到敌人身上出手(2026-07-26),水/土 改走 AttackEffects。
        /// libraryIndex:玩家点的卡位(2026-08-17)——同字多张时消耗这一张而非第一张;
        /// −1 或与 charId 不符(陈旧下标)退回删首张的旧口径。
        /// summonSlots:玩家为本次召唤指定的槽位,第 n 只落 summonSlots[n]。
        /// null = 未指定,按 NextEmptySlot() 依次填(测试与自动路径走这条)。
        /// 指定到存活槽 = 顶替,与「六槽全满」同口径,需要 replaceSummon 确认。
        /// allySlot:单体治疗(2026-08-22)治谁——默认玩家(Targeting.PlayerTarget),
        /// 或指定某个召唤物槽位。</summary>
        public BattleError Cast(string charId, int targetIndex = -1, bool replaceSummon = false,
            bool attackMode = false, int libraryIndex = -1, IReadOnlyList<int> summonSlots = null,
            int allySlot = Targeting.PlayerTarget)
        {
            if (Phase != BattlePhase.PlayerTurn) return BattleError.BattleOver;
            if (!_graph.TryGet(charId, out var def)) return BattleError.NotCastable;

            bool fromLibrary = _forge.Library.Contains(charId);
            bool fromPool = !fromLibrary && def.IsLeaf && _forge.Pool.Contains(charId);
            if (!fromLibrary && !fromPool) return BattleError.NotCastable;
            if (Ap < def.ApCost) return BattleError.NotEnoughAp;

            // 单体效果需要有效的存活目标;未指定或不合法时,**合法目标**恰好一个则自动锁定
            // (3.8.3 单敌免选;2026-08-20 从「存活目标」改口径为「合法目标」——前排还剩一只时
            //  点后排的字应当直接锁那一只,而不是弹一次没得选的选目标)
            if (NeedsTarget(def, attackMode))
            {
                bool restricted = RestrictedToFrontRow(def, attackMode);
                bool legal = targetIndex >= 0 && targetIndex < _enemies.Count && _enemies[targetIndex].Alive
                    && (!restricted || Targeting.CanPlayerHit(_enemies, targetIndex, ignoresRow: false));
                if (!legal)
                {
                    int sole = -1;
                    for (int i = 0; i < _enemies.Count; i++)
                    {
                        if (!_enemies[i].Alive) continue;
                        if (restricted && !Targeting.CanPlayerHit(_enemies, i, ignoresRow: false)) continue;
                        if (sole >= 0) { sole = -1; break; } // 合法目标多于一个:交给 UI 去选
                        sole = i;
                    }
                    if (sole < 0) return BattleError.InvalidTarget;
                    targetIndex = sole;
                }
            }

            // 友方目标合法性(2026-08-22,spec §8.1)。免选口径与「单敌免选」同型:
            // 场上没有存活召唤物时自动锁玩家,不让 UI 弹一次没得选的选择。
            // 但尸体拒治优先于免选——allySlot 点着的是一具占槽的尸体(哪怕它是场上唯一
            // 一只召唤物),这是玩家明确点错了目标,不能被「反正没得选」悄悄改判成治玩家。
            if (NeedsAllyTarget(def, attackMode))
            {
                bool corpseSlot = allySlot >= 0 && allySlot < SummonCap
                    && _summons[allySlot] != null && !_summons[allySlot].Alive;
                if (corpseSlot) return BattleError.InvalidTarget;
                if (AliveSummons() == 0) allySlot = Targeting.PlayerTarget;
                else if (!CanHealSlot(allySlot)) return BattleError.InvalidTarget;
            }

            // 前排放不下就强阻断(2026-07-25):在扣 AP/消耗字之前拒出,交 UI 弹「是否替换?」。
            // 不只看满员——3/4 时召 2 只同样溢出,也得先问过玩家
            if (!replaceSummon && SummonReplaceCountOf(def, attackMode, summonSlots) > 0) return BattleError.SummonCapFull;

            _events.Clear();
            Ap -= def.ApCost;

            // 出字即消耗(3.8.1 v0.7 拍板,无回归):字从库移除,部件从池中消耗
            if (fromLibrary)
            {
                var library = new List<string>(_forge.Library);
                if (libraryIndex >= 0 && libraryIndex < library.Count && library[libraryIndex] == charId)
                    library.RemoveAt(libraryIndex);
                else
                    library.Remove(charId);
                _forge = new ForgeState(library, _forge.Pool);
            }
            else
            {
                var pool = new List<string>(_forge.Pool);
                pool.Remove(charId);
                _forge = new ForgeState(_forge.Library, pool);
            }

            ApplyEffects(def, targetIndex, replaceSummon, attackMode, summonSlots, allySlot);
            CheckWin();
            return BattleError.None;
        }

        /// <summary>丢弃(3.8.2 防卡手):从字库或部件池移除,免 AP;字库丢弃本关不回归。
        /// libraryIndex 语义同 <see cref="Cast"/>:同字多张时丢玩家点的那张。</summary>
        public BattleError Discard(string charId, int libraryIndex = -1)
        {
            if (Phase != BattlePhase.PlayerTurn) return BattleError.BattleOver;

            if (_forge.Library.Contains(charId))
            {
                var library = new List<string>(_forge.Library);
                if (libraryIndex >= 0 && libraryIndex < library.Count && library[libraryIndex] == charId)
                    library.RemoveAt(libraryIndex);
                else
                    library.Remove(charId);
                _forge = new ForgeState(library, _forge.Pool);
                return BattleError.None;
            }
            if (_forge.Pool.Contains(charId))
            {
                var pool = new List<string>(_forge.Pool);
                pool.Remove(charId);
                _forge = new ForgeState(_forge.Library, pool);
                return BattleError.None;
            }
            return BattleError.NotCastable;
        }

        /// <summary>广告复活(spec §4.3.1,2026-08-16 改判:满血站起来,**时间轴原地继续** ——
        /// 不重置调度器、不跳过剩余行动者、不补任何递减。
        /// 旧实现调 StartTurn() 开新一拍,等于白送「剩下的怪本回合不再出手」;
        /// 玩家买到的是满血,不是无敌一轮。
        /// ⚠ 去掉 StartTurn() 后,复活当拍玩家不会拿到新 AP —— 这是正确的:AP 在轮到玩家时
        /// 由 BeginPlayerTurn 给,不该由 Revive 越俎代庖。
        /// 补给(字)由 RunEngine 复活流程经 GrantLibraryChar 注入(部件补给已随掉落改造删除)。</summary>
        public void Revive()
        {
            if (Phase != BattlePhase.Lost) return;
            PlayerHp = _config.PlayerMaxHp;
            Phase = BattlePhase.PlayerTurn;
        }

        /// <summary>掉落决议:用待决议字换掉字库第 <paramref name="replaceIndex"/> 张
        /// (被换的字永久移除,与战利品 PickRewardReplacing 同口径)。</summary>
        public BattleError ResolveDrop(int replaceIndex)
        {
            if (Phase != BattlePhase.DropChoice) return BattleError.BattleOver;
            if (replaceIndex < 0 || replaceIndex >= _forge.Library.Count)
                return BattleError.NotCastable;

            var library = new List<string>(_forge.Library);
            library[replaceIndex] = _pendingDrop;
            _forge = new ForgeState(library, _forge.Pool);
            _pendingDrop = null;
            Phase = BattlePhase.PlayerTurn;
            return BattleError.None;
        }

        /// <summary>掉落决议:弃掉这次掉落,字库不变。</summary>
        public BattleError SkipDrop()
        {
            if (Phase != BattlePhase.DropChoice) return BattleError.BattleOver;
            _pendingDrop = null;
            Phase = BattlePhase.PlayerTurn;
            return BattleError.None;
        }

        /// <summary>复活补给:把一个字加入当前战斗字库;满库返回 false 不入(守容量上限)。</summary>
        public bool GrantLibraryChar(string charId)
        {
            if (_forge.Library.Count >= _config.LibraryCapacity) return false;
            var library = new List<string>(_forge.Library) { charId };
            _forge = new ForgeState(library, _forge.Pool);
            return true;
        }

        /// <summary>满库时的补给去处:换掉字库第 <paramref name="index"/> 张(被换的字永久移除)。
        /// 与 ResolveDrop 同口径,但字由调用方给 —— 那个绑定的是回合掉字挂起的 _pendingDrop。</summary>
        public bool ReplaceLibraryChar(int index, string charId)
        {
            if (index < 0 || index >= _forge.Library.Count) return false;
            var library = new List<string>(_forge.Library);
            library[index] = charId;
            _forge = new ForgeState(library, _forge.Pool);
            return true;
        }

        /// <summary>复活补给:把一个部件加入当前战斗部件池;满池返回 false 不入(守容量上限)。</summary>
        public bool GrantPoolComponent(string componentId)
        {
            if (_forge.Pool.Count >= _config.PoolCapacity) return false;
            var pool = new List<string>(_forge.Pool) { componentId };
            _forge = new ForgeState(_forge.Library, pool);
            return true;
        }

        /// <summary>兜底一击(4.5 第二层防卡手地板):无效果的部件/字出手时的弱效果,永不 brick。</summary>
        private static readonly EffectDef[] FallbackEffects = { new(EffectKind.DamageSingle, 30) };

        /// <summary>该字的实际出字效果:攻击模式下优先用 AttackEffects(水/土 的第二用法),
        /// 没有第二用法就照常;都没有效果的用兜底一击。</summary>
        private static IReadOnlyList<EffectDef> EffectsOf(CharDef def, bool attackMode = false)
        {
            if (attackMode && def.AttackEffects.Count > 0) return def.AttackEffects;
            return def.Effects.Count > 0 ? def.Effects : FallbackEffects;
        }

        /// <summary>这张字的**单体直伤形状**(2026-08-22,供表现层预览覆盖范围用)。
        /// 建在 <see cref="EffectsOf"/> 之上而不是让表现层自己挑效果列表 —— 与 CanTarget 同一条
        /// 理由:玩家看到会打到哪几格、和引擎实际打到哪几格,一旦分头推导迟早失配。尤其是
        /// 两个效果列表都空的字,实际打出去的是 <see cref="FallbackEffects"/> 那一发兜底一击,
        /// 表现层自己重写选取逻辑必然漏掉这一支(2026-08-22 评审 Finding 2 命中的正是这条)。
        ///
        /// 只取**第一条** DamageSingle 的 Shape/Shots——与 NeedsTarget/RestrictedToFrontRow
        /// 一样只看首条,不聚合多条直伤(混合多形状直伤字眼下不存在,真出现时预览会只显示
        /// 第一发,是已知的当前局限而非本次改动引入的新账)。没有单体直伤则返回 (Single, 0)。</summary>
        public static (TargetShape Shape, int Shots) AttackShapeOf(CharDef def, bool attackMode = false)
        {
            foreach (var effect in EffectsOf(def, attackMode))
                if (effect.Kind == EffectKind.DamageSingle)
                    return (effect.Shape, effect.Shots);
            return (TargetShape.Single, 0);
        }

        /// <summary>玩家选定起始槽后,这次召唤实际的落位表(2026-08-23 用户拍板)。
        ///
        /// 从 <paramref name="startSlot"/> 起**环绕**扫一圈,先收空槽与尸体槽;
        /// 站着人的位子**跳过**,只有空位真的凑不满 <paramref name="count"/> 时才回头收它们
        /// —— 那时它们才是真正要顶替的,顺序同样从选定格起顺延。
        /// 于是「选定格恰好有人、旁边还空着」不再弹顶替确认,**只有六格全满才弹**。
        ///
        /// 改前是「环上取 N 个连续位」,不管占没占,所以点在有人的格上必弹一次替换,
        /// 哪怕隔壁空着 —— 那是玩家实机反馈的那个毛病。
        ///
        /// 尸体槽算可用:它本来就能被直接覆盖(<see cref="SlotState.Corpse"/>),
        /// 当成要顶替的会让「打死一只再召一只」白白多弹一次确认。
        ///
        /// **保证两条不变式**:返回长度恰好 count、下标互不重复。
        /// <see cref="ApplyEffects"/> 的落位循环依赖它们 —— 破坏任一条,第二只会写进
        /// 同一个槽或被静默吞掉,而那时 Cast 已经返回 None、AP 也已经扣了。</summary>
        public IReadOnlyList<int> PlanSummonSlots(int startSlot, int count)
        {
            var plan = new List<int>(count);
            if (count <= 0) return plan;
            if (startSlot < 0 || startSlot >= SummonCap) startSlot = 0;

            // 第一轮:空槽与尸体槽
            for (int n = 0; n < SummonCap && plan.Count < count; n++)
            {
                int slot = (startSlot + n) % SummonCap;
                if (SlotOccupancy(slot) != SlotState.Alive) plan.Add(slot);
            }
            // 第二轮:空位凑不满才顶替站着的人,同样从选定格起顺延
            for (int n = 0; n < SummonCap && plan.Count < count; n++)
            {
                int slot = (startSlot + n) % SummonCap;
                if (SlotOccupancy(slot) == SlotState.Alive) plan.Add(slot);
            }
            // 两轮的集合互斥且并集是全部六格,所以 count ≤ SummonCap 时必然填满。
            // SummonCountOf 已经把只数封顶到 SummonCap,走不到填不满的分支。
            return plan;
        }

        /// <summary>本次召唤会顶掉几只**存活**召唤物(0 = 不顶人,可以直接出)。
        /// 指定了槽位就数这些槽里有几个是 Alive;没指定就退回「超出上限的部分」。</summary>
        public int SummonReplaceCountOf(CharDef def, bool attackMode = false,
            IReadOnlyList<int> summonSlots = null)
        {
            int count = SummonCountOf(def, attackMode);
            if (count <= 0) return 0;
            if (summonSlots == null)
                return Math.Max(0, AliveSummons() + count - SummonCap);
            int replaced = 0;
            for (int n = 0; n < count && n < summonSlots.Count; n++)
                if (SlotOccupancy(summonSlots[n]) == SlotState.Alive) replaced++;
            return replaced;
        }

        /// <summary>这张字一次会召出几只(多条召唤效果累加,封顶到前排上限)。
        /// 满员替换时即「从最前一只起顶掉几只」,供 UI 文案用。</summary>
        public int SummonCountOf(CharDef def, bool attackMode = false)
        {
            int count = 0;
            foreach (var effect in EffectsOf(def, attackMode))
                if (effect.Kind == EffectKind.Summon) count += effect.SummonCount;
            return Math.Min(count, SummonCap);
        }

        /// <summary>该字的效果是否需要指定单体目标(供 UI 进入选目标模式;攻击模式看第二用法)。
        ///
        /// 连发(Volley)**不需要选目标** —— 它的目标全自动(后排优先循环补足),
        /// 交互上与 AOE 同档:点了就打。漏掉这条会让玩家对着一张自动字白点一次选目标。
        ///
        /// ⚠ 2026-08-06 C1 那次崩溃是靠 `_enemies[-1]` 越界抛异常才被发现的(见上面提到的旧账);
        /// 但目标形状改造(2026-08-22)之后,ApplyEffects 走的是 Targeting.ExpandTargets ——
        /// primaryIndex 越界(含 −1)时它直接返回空表,循环体一次不进,不再抛异常。也就是说
        /// 这条白名单如果将来又漏了哪个新 Kind,不会再有响亮的崩溃把它带回评审台面,只会悄悄
        /// 变成「点了没反应」。别以为「没崩就是漏判已经堵上了」。</summary>
        public static bool NeedsTarget(CharDef def, bool attackMode = false)
        {
            foreach (var effect in EffectsOf(def, attackMode))
                if ((effect.Kind == EffectKind.DamageSingle && effect.Shape != TargetShape.Volley)
                    || effect.Kind == EffectKind.BurnSingle
                    || effect.Kind == EffectKind.Bleed || effect.Kind == EffectKind.Freeze
                    || effect.Kind == EffectKind.Slow || effect.Kind == EffectKind.ArmorBreak
                    // 2026-08-06 C1:单体驱散(灭/削/湮)漏在白名单外——UI 判定成「不需要选目标」,
                    // targetIndex 停在 -1,ApplyEffects 里 _enemies[-1] 直接越界崩溃。
                    // 必须排除 TargetAll(淡):那支是全体驱散,本就不需要选目标。
                    || (effect.Kind == EffectKind.Dispel && !effect.TargetAll)
                    || (effect.Kind == EffectKind.Blind && !effect.TargetAll)
                    || effect.Kind == EffectKind.Silence
                    || effect.Kind == EffectKind.BurnNoDecay
                    || effect.Kind == EffectKind.BurnSettleNow
                    || effect.Kind == EffectKind.Detonate)
                    return true;
            return false;
        }

        /// <summary>该字是否需要指定**友方**目标(2026-08-22,spec §8.1)。
        /// 单体治疗(HealSelf / HealOverTime)从此可以选治玩家还是某只召唤物。
        ///
        /// 群治(HealAll)不在内 —— 它覆盖全体,本就无从选起,保持免选。</summary>
        public static bool NeedsAllyTarget(CharDef def, bool attackMode = false)
        {
            foreach (var effect in EffectsOf(def, attackMode))
                if (effect.Kind == EffectKind.HealSelf || effect.Kind == EffectKind.HealOverTime)
                    return true;
            return false;
        }

        /// <summary>这个槽位现在能不能作为治疗目标(表现层据此置灰;引擎在 Cast 里用同一条判据)。
        /// 玩家(−1)恒可治;召唤物要活着 —— 尸体归复活管,治疗救不回来。</summary>
        public bool CanHealSlot(int slot)
        {
            if (slot == Targeting.PlayerTarget) return true;
            return slot >= 0 && slot < SummonCap && _summons[slot] != null && _summons[slot].Alive;
        }

        /// <summary>本次出字是否受敌方前排阻挡(2026-08-20,spec §4.2)。
        ///
        /// **只有 DamageSingle 受限**:控制、减益、灼烧、AOE 一律不受排位限制
        /// ——「打不到后面,但够得着冻住、破甲、下毒」。
        ///
        /// 混合字按最严的算:效果里只要含一条 DamageSingle 就受限(如湮 = 直伤 + 全体驱散)。
        /// 但只要有任一条直伤标了偷袭,整张字就是偷袭字——偷袭是字的身份,不是单条效果的属性。
        ///
        /// 连发(Volley)不受限——它是远程形状,与偷袭一样越阵。</summary>
        public static bool RestrictedToFrontRow(CharDef def, bool attackMode = false)
        {
            bool hasDirectDamage = false;
            foreach (var effect in EffectsOf(def, attackMode))
            {
                if (effect.Kind != EffectKind.DamageSingle) continue;
                if (effect.CanStrikeBackline) return false;
                // 连发是远程,天然越阵(2026-08-22,spec §3.4)。横扫/顺劈/贯穿照旧受限 ——
                // 它们只判主目标,溅到的必然在主目标够得着的范围内
                if (effect.Shape == TargetShape.Volley) return false;
                hasDirectDamage = true;
            }
            return hasDirectDamage;
        }

        /// <summary>这张字现在能不能点这只敌人(表现层据此置灰;引擎在 Cast 里用同一条判据)。</summary>
        public bool CanTarget(CharDef def, int enemyIndex, bool attackMode = false)
        {
            if (enemyIndex < 0 || enemyIndex >= _enemies.Count || !_enemies[enemyIndex].Alive) return false;
            if (!RestrictedToFrontRow(def, attackMode)) return true;
            return Targeting.CanPlayerHit(_enemies, enemyIndex, ignoresRow: false);
        }

        /// <summary>最近一次 AdvanceOnce 执行的行动者(表现层据此高亮行动条那一格)。</summary>
        public ActorRef LastActor { get; private set; } = ActorRef.Player;

        /// <summary>最近一次 AdvanceOnce 推进了多少拍(2026-08-17,每单位行动条)。
        /// 表现层用它定行动条动画时长:时长 = LastAdvanceTicks × BaseMs。
        /// **为 0 的唯一情形是「已有人满格」**(TurnScheduler.Advance 的 FirstFull 分支):
        /// 那一格不需要推进时间,条不该动,表现层据此跳过条动画。
        ///
        /// ⚠ 不是「战斗刚开始为 0」(2026-08-17 订正):构造函数现在开场就跑推进,拿到引擎时
        /// 它已经是开场那一拍的拍数 —— 同速开局 1,全场速度 25 的慢局 4。开场的逐拍回放走
        /// <see cref="OpeningSteps"/>(每条自带 Ticks),不要拿本属性去判断「有没有推进过」。</summary>
        public int LastAdvanceTicks { get; private set; }

        private readonly List<OpeningStep> _openingSteps = new();

        /// <summary>开场每一拍的回放数据,按发生顺序(2026-08-17)。同速开局只有一条
        /// (玩家自己那一拍);携带满格召唤物、或玩家一拍攒不满而别人能时会有多条
        /// (2026-08-18 订正:「敌人更快」不是准确条件,见 BuildSlots 的优先级注释)。
        /// **不进快照** —— 断点续爬恢复的是战斗中途,没有「开场」可回放。</summary>
        public IReadOnlyList<OpeningStep> OpeningSteps => _openingSteps;

        /// <summary>把当前这一拍的状态拷成回放数据。必须逐个拷值 ——
        /// `_events` 下一拍开头就被 Clear,存引用会拿到空列表。</summary>
        private OpeningStep CaptureOpeningStep()
        {
            var summons = new int[SummonCap];
            for (int i = 0; i < SummonCap; i++) summons[i] = _summons[i]?.ActionMeter ?? 0;
            var enemies = new int[_enemies.Count];
            for (int i = 0; i < enemies.Length; i++) enemies[i] = _enemies[i].ActionMeter;
            return new OpeningStep(LastActor, LastAdvanceTicks, PlayerActionMeter,
                summons, enemies, new List<BattleEvent>(_events));
        }

        /// <summary>当前参战单位的调度槽位。**顺序固定**:玩家、召唤物(下标升序)、敌人(下标升序)
        /// —— Forecast 与 Advance 返回的 Meters 与本列表同序,写回时按同一顺序。
        /// 死掉的单位不进调度(它们不再行动,也不该占预测格子)。
        ///
        /// ⚠ 优先级(并列时的排序主键,小者先)是**玩家 0 → 召唤物 1 → Buff 敌 2 → 其余敌 3**
        /// ——玩家排**最先**(2026-08-17 用户拍板,推翻 2026-08-15 的反向口径)。
        ///
        /// 2026-08-15 那次把玩家定成最后,并留下「方向不要再调回去」的警告,理由是
        /// 「玩家优先会让它每次推进都抢在敌人前面把行动权收回去」。**那个诊断把病根归错了**:
        /// 病根是「玩家优先 + 构造函数给的免费先手」这个**组合**,不是玩家优先本身。
        /// 实测(spec §2.2,各跑一次全量 1062 条):
        ///   只反转 priority(保留免费先手)→ 红 199 条,确实复现了那个现象
        ///   只去掉免费先手(保留玩家最后)→ 红 537 条(AP=0、字库空)
        ///   两个一起                      → 红 32 条,且序列变成干净的「玩家 → 召唤 → 敌」
        /// 当初三种「开局记账」(创建时先手 / 玩家记负债 / 消费一拍)全是在抵消那次免费先手;
        /// 把免费先手删掉(见构造函数),它们一个都不需要。
        ///
        /// 这里只管**同速并列**。速度不同时,ticks(TicksUntilAnyFull)是**全场共用的
        /// 最小值**(2026-08-18 第六次审查订正,推翻此前「敌人速度高于玩家时它先动」的说法——
        /// 那是错的):只要玩家一拍就能攒满(speed >= Threshold),敌人再快也会同拍满格,
        /// 落回本级 tie-break,还是玩家赢。「敌人先动」的真实条件是**玩家一拍攒不满而敌人能**
        /// (如玩家 25 要 4 拍、敌人 400 只要 1 拍)。速度更快在这个模型里体现为**出手更频繁**
        /// (多轮累积),不是抢第一拍——玩家 100 / 敌人 200 是「玩家先动,然后敌人连动两次」。
        /// 反例见 AtbTimingTests.Opening_FasterEnemyButPlayerFullInOneTick_PlayerStillActsFirst
        /// (敌人 400、玩家 100,玩家仍先动)。这层表达力此前被旧的免费先手压平了。</summary>
        private List<SchedulerSlot> BuildSlots()
        {
            var slots = new List<SchedulerSlot>
            {
                new(ActorRef.Player, EffectivePlayerSpeed, PlayerActionMeter, 0),
            };
            for (int s = 0; s < SummonCap; s++)
            {
                if (_summons[s] == null || !_summons[s].Alive) continue;
                slots.Add(new SchedulerSlot(new ActorRef(ActorKind.Summon, s),
                    _summons[s].Speed, _summons[s].ActionMeter, 1));
            }
            for (int i = 0; i < _enemies.Count; i++)
            {
                var enemy = _enemies[i];
                if (!enemy.Alive) continue;
                int speed = enemy.Speed + enemy.Statuses.TotalMagnitude(StatusKind.SpeedModifier);
                // Buff 能力的敌人排在普通敌人之前:保住「辅助先摇旗、同伴才带着加成出手」
                // 这个既有节拍。被减速时它自然排到后面 —— 那正是新系统该有的行为。
                int priority = enemy.Def.Ability == EnemyAbility.Buff ? 2 : 3;
                slots.Add(new SchedulerSlot(new ActorRef(ActorKind.Enemy, i), speed,
                    enemy.ActionMeter, priority));
            }
            return slots;
        }

        /// <summary>把一次推进后的计量器写回各单位。</summary>
        private void WriteBackMeters(List<SchedulerSlot> slots, IReadOnlyList<int> meters)
        {
            for (int i = 0; i < slots.Count; i++)
            {
                var actor = slots[i].Actor;
                switch (actor.Kind)
                {
                    case ActorKind.Player: PlayerActionMeter = meters[i]; break;
                    case ActorKind.Summon: _summons[actor.Index].ActionMeter = meters[i]; break;
                    case ActorKind.Enemy: _enemies[actor.Index].ActionMeter = meters[i]; break;
                }
            }
        }

        /// <summary>向前预测 count 个行动者(表现层的行动条用)。场面一变就会偏离,不许缓存。</summary>
        public IReadOnlyList<ActorRef> Forecast(int count) =>
            TurnScheduler.Forecast(BuildSlots(), count);

        // 2026-08-16 全分支终审 Important 4:PeekNextActor() 已删除——全仓库零消费方的死代码。
        // 2026-08-17:顶部行动条(TurnBar)一并废止,改为每单位自己一条读 ActionMeter 的条。
        // Forecast 因此暂时没有消费方,但保留 —— 它是 Core 公共 API,零维护成本,
        // 以后做「接下来谁动」的提示随时能用。

        /// <summary>玩家让出行动权,交由 AdvanceOnce 逐个推进(2026-08-16 全分支终审 Important 1
        /// 之后:本方法不再做玩家侧状态递减——那一步挪到了 BeginPlayerTurn 尾部,见其注释)。</summary>
        public void YieldTurn()
        {
            if (Phase != BattlePhase.PlayerTurn) return;
            _events.Clear();
        }

        /// <summary>推进并执行**一个**非玩家行动者。轮到玩家时不执行,改为跑 BeginPlayerTurn()、
        /// 置 Phase = PlayerTurn 并返回 false —— 调用方据此停止循环。
        ///
        /// _events 在每次调用开头清空:逐格驱动时表现层每次拿到的就是**这一个单位**产生的事件。
        /// 玩家分支也要清空(2026-08-15 控制者裁定):不清的话 BeginPlayerTurn() 产生的事件
        /// (比如玩家灼烧的 BurnTick)会附着在上一个敌人的事件批次里,表现层会把玩家的效果
        /// 播在敌人那一段。EndTurn 包装靠循环外那次 AddRange 收集玩家这批,清空后依然正确。</summary>
        public bool AdvanceOnce()
        {
            if (Phase != BattlePhase.PlayerTurn && Phase != BattlePhase.DropChoice)
            {
                LastAdvanceTicks = 0; // 防御性:不产生 step 就不该留着上一次的陈旧值
                return false;   // Won / Lost:战斗已结束
            }
            if (Phase == BattlePhase.DropChoice)
            {
                LastAdvanceTicks = 0; // 防御性:同上
                return false; // 等玩家决议
            }

            var slots = BuildSlots();
            var step = TurnScheduler.Advance(slots);
            // 两个分支都要发 ActorActed:每批事件都以它开头,表现层才能用统一规则识别这批
            // 事件属于谁(玩家/召唤/敌人),不必为玩家单独写一条特例分支。
            if (step.Actor.Kind == ActorKind.Player)
            {
                _events.Clear();
                _events.Add(new BattleEvent(BattleEventKind.ActorActed, -1, (int)ActorKind.Player));
                WriteBackMeters(slots, step.Meters);
                LastActor = ActorRef.Player;
                LastAdvanceTicks = step.Ticks;
                BeginPlayerTurn();
                return false;
            }

            _events.Clear();
            _events.Add(new BattleEvent(BattleEventKind.ActorActed,
                step.Actor.Index, (int)step.Actor.Kind));
            WriteBackMeters(slots, step.Meters);
            LastActor = step.Actor;
            LastAdvanceTicks = step.Ticks;
            if (step.Actor.Kind == ActorKind.Summon) ActSummonTurn(step.Actor.Index);
            else ActEnemyTurn(step.Actor.Index);
            return Phase == BattlePhase.PlayerTurn;
        }

        /// <summary>结束回合(2026-08-15,ATB 改造接线):退化为对 AdvanceOnce 的包装,时序归属
        /// 仍是旧的,只是换成调度器逐格驱动。跨段累积 _events,调用方拿到的仍是完整一整轮。</summary>
        public void EndTurn()
        {
            if (Phase != BattlePhase.PlayerTurn) return;
            YieldTurn();
            var accumulated = new List<BattleEvent>(_events);
            // YieldTurn() 现在只清事件、不结算(2026-08-16 全分支终审 Important 1 之后,状态递减
            // 挪到了 BeginPlayerTurn),下面这条 alreadyOver 判断理论上不会被 YieldTurn 本身触发;
            // 保留是防御性写法——循环里第一次 AdvanceOnce 若已经分出胜负,不会重复叠加事件。
            bool alreadyOver = Phase != BattlePhase.PlayerTurn && Phase != BattlePhase.DropChoice;
            while (AdvanceOnce())
            {
                accumulated.AddRange(_events);
            }
            // AdvanceOnce 返回 false 的那一次(轮到玩家,或这一击直接终结了战斗)也可能产生
            // 新事件(BeginPlayerTurn 里的缺笔妖补全等),需要再收一次——但仅当循环真的跑到
            // 了那一步(清过 _events)。
            if (!alreadyOver)
                accumulated.AddRange(_events);
            _events.Clear();
            _events.AddRange(accumulated);
        }

        /// <summary>玩家灼烧(2026-08-16 从原 SettlePlayerTurnEnd 拆出,归属挪到 BeginPlayerTurn):
        /// 层数 × 系数掉血,然后 −1 层。玩家没有五行属性,所以**不走生克**
        /// —— 敌人侧那条 KeMultiplier(Fire, enemy.Element) 不适用。</summary>
        private void SettlePlayerBurn()
        {
            var playerBurn = _playerStatuses.Find(StatusKind.Burn);
            if (playerBurn != null && playerBurn.Magnitude > 0)
            {
                int playerTick = playerBurn.Magnitude * _burnPerStack;
                PlayerHp = Math.Max(0, PlayerHp - playerTick);
                playerBurn.Magnitude -= 1;
                if (playerBurn.Magnitude <= 0) _playerStatuses.Remove(StatusKind.Burn);
                _events.Add(new BattleEvent(BattleEventKind.BurnTick, -1, playerTick)); // −1 = 玩家
            }

            // 玩家灼烧是这里第一个能把 PlayerHp 归零的点,必须在这里立刻收口
            // (2026-08-06 全分支终审 C2):归零即死,持续治疗救不回来 —— 若照旧把判负推迟到
            // 回合尾部,中间的 HoT 循环会先把血救回去,CheckWin() 也可能被同回合的召唤物
            // 清场抢先判成 Won(PlayerHp=0 却「胜利」,还带着 0 血过关)。
            // ⚠️ 2026-08-16(全分支终审 Important 1):这里早退成 Lost 不会漏掉本拍的状态递减——
            // TickPlayerStatuses() 挪到了 BeginPlayerTurn 里紧跟 SettlePlayerHots 之后**无条件**
            // 执行(见 BeginPlayerTurn),即便走的是这条早退也照样会跑到那一句,不需要在这里补跑。
            if (PlayerHp <= 0)
            {
                Phase = BattlePhase.Lost;
            }
        }

        /// <summary>玩家持续治疗(2026-08-16 从原 SettlePlayerTurnEnd 拆出,归属挪到 BeginPlayerTurn):
        /// 回合数递减现在紧跟在本方法之后由 BeginPlayerTurn 统一调用 TickPlayerStatuses 处理,
        /// 这里只结算不写 TurnsLeft,避免本回合刚施加的 HoT 被立刻多减一次。</summary>
        private void SettlePlayerHots()
        {
            for (int i = _playerStatuses.All.Count - 1; i >= 0; i--)
            {
                var hot = _playerStatuses.All[i];
                if (hot.Kind != StatusKind.HealOverTime) continue;
                if (hot.TargetAll) { HealPlayerAndSummons(hot.Magnitude); continue; }

                // 目标召唤物死了就当场移除,不空转到期(2026-08-22)——空转会让玩家的
                // 一次出字被隐形浪费。玩家(TargetSlot == PlayerTarget)不会阵亡到这里
                // 还没被 SettlePlayerBurn 拦下,故不需要同样的判活。
                if (hot.TargetSlot != Targeting.PlayerTarget
                    && (hot.TargetSlot < 0 || hot.TargetSlot >= SummonCap
                        || _summons[hot.TargetSlot] == null || !_summons[hot.TargetSlot].Alive))
                {
                    _playerStatuses.RemoveEntry(hot);
                    continue;
                }

                HealAlly(hot.TargetSlot, hot.Magnitude);
            }
        }

        /// <summary>一只召唤物的完整一拍(2026-08-16,ATB 时序归属搬迁,spec §4.3):光环治疗
        /// (2026-08-05,桃)从「玩家回合末全体召唤物集体先治疗」挪到这里,变成「该召唤物自己
        /// 那拍先治疗再出手」——与出手无关,场上没有敌人可打时也照常回血。→ 出手 → 自身状态
        /// 递减。召唤物目前没有状态容器(SummonState 无 Statuses 字段),所以"自身状态递减"
        /// 这一步暂无内容——不要为此新增字段,等真有召唤物状态时再加。</summary>
        private void ActSummonTurn(int s)
        {
            var summon = _summons[s];
            if (summon == null || !summon.Alive) return;

            int heal = summon.Passive?.HealAlly ?? 0;
            if (heal > 0) HealPlayerAndSummons(heal);

            if (_enemies.Any(e => e.Alive)) StrikeOnceWithSummon(s);
            CheckWin();
        }

        /// <summary>一个敌人的完整一拍(2026-08-15,ATB 时序归属搬迁,spec §4.3「每个敌人那一拍」
        /// 六步):自身灼烧 → 自身流血 → 缺笔妖自补全 → 加攻/出手 → 自身状态递减。任何一步打死它
        /// 都当场早退——DOT 从「玩家回合末全场统一结算」改成「它自己动之前结算」,谁补最后一刀的
        /// 边界因此改变(已知归属变化,记录见 task-8-red-list.md)。
        /// ⚠ SettleBurnOn / SettleBleedOn 命中致死时各自已经调用过 ResolveDefeat(发 EnemyDied),
        /// 这里只补 CheckWin() 判胜——不重复调用 ResolveDefeat,否则同一次死亡会发两条 EnemyDied。</summary>
        private void ActEnemyTurn(int enemyIndex)
        {
            var enemy = _enemies[enemyIndex];
            if (!enemy.Alive) return;

            SettleBurnOn(enemyIndex);
            if (!enemy.Alive) { CheckWin(); return; }

            SettleBleedOn(enemyIndex);
            if (!enemy.Alive) { CheckWin(); return; }

            RegrowOneEnemy(enemyIndex);

            // 冻结(2026-08-16 口径 6):照常上行动条,轮到就跳过并 −1,不出手。
            // 旧语义是「计量器冻住不累积」——那在 CTB 下会死锁:冻结单位若被排除出调度,
            // 就永远轮不到 → 自身状态永远不递减 → 冻结永远解不了。跳过语义还多一个好处:
            // 行动条 UI 能直接画出「它下一拍会被跳过」。
            //
            // 2026-08-16 裁定:下面这行 TickTurns() 不豁免 SpeedModifier(即不写成
            // TickTurns(Has(Freeze) ? SpeedModifier : null))是有意的,不是搬家搬丢——旧豁免
            // 配的是旧的「冻结时计量器不累积」;新模型下计量器照常推进,减速倒计时也就该
            // 照常走,不需要再暂停。
            if (enemy.Statuses.Has(StatusKind.Freeze))
            {
                enemy.Statuses.TickTurns();
                return;
            }

            if (enemy.Def.Ability == EnemyAbility.Buff && !IsSilenced(enemy) && HasOtherAliveEnemy(enemy))
                ApplyEnemyBuffAura(enemyIndex);
            else
                ActOneEnemy(enemyIndex, 1);

            // 这一拍即便刚把玩家打死(Phase 已变 Lost),它自身的状态递减也照常执行——
            // 不能因为玩家阵亡就早退跳过(2026-08-05 Important 3 锁定的口径:阵亡当回合状态也要
            // 照常递减,不能拖到复活后才补,见 Revive_DoesNotGrantExtraStatusTurn)。
            // 真正需要早退的是「剩余敌人不再出手」——那由 AdvanceOnce 末尾的
            // `Phase == BattlePhase.PlayerTurn` 早退天然覆盖,不需要在这里重复拦一次。
            enemy.Statuses.TickTurns();
        }

        /// <summary>轮到玩家(2026-08-16,ATB 时序归属搬迁,spec §4.3 玩家那一拍):玩家灼烧 →
        /// 玩家 HoT → 玩家侧状态回合递减 → 判负 → 判胜 → 开新一拍(StartTurn)。DOT 与 AP 补给从
        /// 「上一拍让出行动权时」挪到这里 —— 玩家的一拍从「自己开始」算起。
        ///
        /// ⚠ 2026-08-16(全分支终审 Important 1):状态回合递减(TickPlayerStatuses)原先错放在
        /// 上一拍 YieldTurn() 里,相对玩家自己的结算(灼烧/HoT)是**先递减后结算**——与
        /// ActEnemyTurn(结算在前、递减在后)方向相反,静默改动了三处数值(沐 HoT 回合数从
        /// 3 次变 2 次、铸反弹覆盖从 2 拍变 1 拍、倾覆封字从罚 1 拍变罚 2 拍)。现挪到这里、
        /// 紧跟在 SettlePlayerHots 之后,与敌人那一拍结构同构。
        ///
        /// ⚠ TickPlayerStatuses 必须**无条件**执行,即便 SettlePlayerBurn 已经把玩家烧死
        /// (Phase = Lost)——玩家可能被复活,阵亡当回合若漏掉这次递减,状态会在复活后凭空
        /// 多续一轮(与 ActEnemyTurn 结尾「阵亡当回合状态也要照常递减」同一条口径,见
        /// PlayerBurn_KillsWithoutSkippingStatusTick)。SettlePlayerHots 本身仍照旧只在
        /// 玩家未被灼烧烧死时才跑——死人不用治。</summary>
        private void BeginPlayerTurn()
        {
            SettlePlayerBurn();
            if (Phase != BattlePhase.Lost)
            {
                SettlePlayerHots();
                if (PlayerHp <= 0) Phase = BattlePhase.Lost;
            }

            TickPlayerStatuses();
            if (Phase == BattlePhase.Lost) return;

            // 反伤可能在敌方段里打死最后一只敌人(2026-08-05):敌方段以前从不杀敌,
            // 所以这里原本没有判胜,不补的话会带着满地尸体走进 StartTurn。
            // 排在 Lost 早退之后 = 同归于尽时玩家阵亡优先,与既有口径一致。
            CheckWin();
            if (Phase != BattlePhase.PlayerTurn) return;

            StartTurn();
        }

        /// <summary>一只召唤物的一次出手(2026-08-15 提取,行为与提取前逐字节一致)。
        /// 攻 0 的召唤物(烓/灶)照常出手但不走 DamageEnemy —— 见提取前的原注释。</summary>
        private void StrikeOnceWithSummon(int summonIndex)
        {
            var summon = _summons[summonIndex];
            if (summon == null) return;
            var passive = summon.Passive;
            // 近战打敌方前排、远程优先打后排(2026-08-20)。全部敌人默认前排时,
            // 本行与改前的「从 0 扫到第一个存活」逐位等价 —— 既有战斗零行为变化。
            int target = Targeting.PickEnemyTargetForSummon(_enemies, passive?.Ranged ?? false);
            var shape = passive?.Shape ?? TargetShape.Single;
            // 连发没有主目标,选不到主目标也照打(它自己会排候选);其余形状要有主目标
            if (target < 0 && shape != TargetShape.Volley) return;

            // 形状展开(2026-08-22,spec §7):与玩家侧共用同一个几何函数,不写第二份
            var hits = Targeting.ExpandTargets(_enemies, target, shape, passive?.Shots ?? 0);
            int percent = passive == null || passive.ShapePercent <= 0 ? 100 : passive.ShapePercent;
            for (int t = 0; t < hits.Count; t++)
            {
                int tgt = hits[t];
                if (!_enemies[tgt].Alive) continue;
                int damage = summon.Attack;
                // 连发每发全额;形状类的非主目标按 ShapePercent 折算
                if (t > 0 && shape != TargetShape.Volley && percent != 100)
                    damage = damage * percent / 100;
                _events.Add(new BattleEvent(BattleEventKind.SummonAttack, tgt, damage, summonIndex));
                if (damage > 0)
                    DamageEnemy(tgt, damage, Array.Empty<Element>(), summon.Element);
                ApplySummonOnHit(summon, tgt);
            }
        }

        /// <summary>标点小妖给其他存活字怪加攻的那一拍(2026-08-15 提取,行为与提取前逐字节一致)。
        /// actionCount[i]==0 的冻结/计量器不足判断留在 EndTurn 的调用处——那个数组是 EndTurn 的局部变量,
        /// 没有随 enemyIndex 一起传进来。</summary>
        private void ApplyEnemyBuffAura(int enemyIndex)
        {
            var enemy = _enemies[enemyIndex];
            if (!enemy.Alive || enemy.Def.Ability != EnemyAbility.Buff || IsSilenced(enemy)) return;
            if (!HasOtherAliveEnemy(enemy)) return; // 无人可加 → 交给下面的行动循环
            for (int j = 0; j < _enemies.Count; j++)
            {
                var other = _enemies[j];
                if (!other.Alive || other == enemy) continue;
                // 加成本场累计、回合末不回滚(既有语义)。SourceId 必须每次唯一——用回合数做
                // 后缀不够:场上若有两只同字标点小妖同回合各给同一目标加一次,回合数后缀会撞车
                // 变成互相覆盖而非累加(与 Task 4 的 HoT SourceId 教训同型)。
                other.Statuses.Apply(new StatusEffect
                {
                    Kind = StatusKind.AttackBuff, Polarity = StatusPolarity.Buff,
                    Magnitude = PunctuationBuffPercent, TurnsLeft = -1,
                    SourceId = $"{enemy.Def.Id}#{_statusSerial++}",
                });
                _events.Add(new BattleEvent(BattleEventKind.EnemyBuff, j, PunctuationBuffPercent));
            }
        }

        /// <summary>一个敌人本回合的全部出手(2026-08-15 提取,行为与提取前逐字节一致)。
        /// actionCount 由 EndTurn 按 actionCount[i] 传入,!Alive / 冻结 / 加攻互斥等守卫仍留在调用处。</summary>
        private void ActOneEnemy(int enemyIndex, int actionCount)
        {
            var enemy = _enemies[enemyIndex];
            for (int act = 0; act < actionCount; act++)
            {
                if (!enemy.Alive) break; // 反伤可能在两次行动之间打死它

                if (enemy.IsBoss && ResolveBossTurn(enemyIndex, enemy))
                    continue; // 已蓄力或已放大招,本回合不走普攻

                int damage = enemy.Attack; // 减护甲(点数)在 DamagePlayerDirect 里,护盾吸收再在其后
                // 目标裁定(2026-08-20):近战被我方前排拦下;前排清空后在「后排 ∪ 玩家」里均匀随机;
                // 远程无视前排;Focus.Player 的够得着玩家时死盯玩家。规则全在 Targeting,这里只执行。
                int tankIdx = Targeting.PickAllyTarget(enemy.Def.Range, enemy.Def.Focus,
                    _summons, FrontRowSize, _random);
                // hit:这次攻击有没有命中(2026-08-08)。打空为 false,免疫挡下也算 true——
                // 见 DamagePlayerDirect/DamageSummon 的返回值口径注释。下面的灯花用它 gate。
                bool hit;
                if (tankIdx != Targeting.PlayerTarget)
                {
                    // 召唤物带属性:敌人打召唤走五行(金克木 ×1.5、木反克土 ×0.5)
                    hit = DamageSummon(enemyIndex, tankIdx, damage, enemy.Element);
                }
                else
                {
                    hit = DamagePlayerDirect(enemyIndex, damage);
                }

                // 通假字:首次行动后现形(8.3)。现形只看「敌人是否出手了」,与命中判定无关——
                // 敌人确实动了,打空不影响这条(2026-08-08 明确:不受 hit 影响)。
                RevealDisguise(enemyIndex);

                // 灯花(2026-08-06):每次攻击给玩家挂 1 层灼烧。TurnsLeft = -1 段内持久,
                // 靠上方的玩家灼烧结算段自减 Magnitude,不受 TickTurns 影响(与敌人侧同口径)。
                // 走 RefreshBurn 刷新到 N 层而非累加(2026-08-06 I1):BuildFloor 有放回抽取,
                // 同场可能出现多只灯花,累加语义会导致 N 只灯花净 +(N−1)层/回合,雪球失控
                // (实测 4 只第 6 回合单灼烧 38 伤/回合)。刷新语义下,单只与多只稳态都是 1 层。
                // hit 门槛(2026-08-08):打空 = 攻击没落到身上,附带效果不该触发;免疫挡下
                // 仍算命中(hit=true),灼烧照挂——免疫挡的是伤害,不是攻击本身。
                if (hit && enemy.Def.Ability == EnemyAbility.Sear && !IsSilenced(enemy))
                {
                    RefreshBurn(_playerStatuses, SearStacks);
                    _events.Add(new BattleEvent(BattleEventKind.Burn, -1, SearStacks)); // −1 = 玩家
                }

                // 立即判负(Task 12):这一下已经打死玩家 —— 不再走下一次行动
                // (actionCount 目前恒为 1,这里是防御性收口,不是当前会触发的分支)。
                if (Phase != BattlePhase.PlayerTurn) break;
            }
        }

        /// <summary>通假字现形(8.3 + 2026-08-15 口径 7):挨打或出手,先到先触发。
        /// 「出手但打空也现形」的旧口径不变 —— 敌人确实动了,与命中判定无关。</summary>
        private void RevealDisguise(int enemyIndex)
        {
            var enemy = _enemies[enemyIndex];
            if (enemy.Def.Ability != EnemyAbility.Disguise) return;
            if (enemy.ApparentElement == enemy.Element) return;
            enemy.ApparentElement = enemy.Element;
            _events.Add(new BattleEvent(BattleEventKind.EnemyRevealed, enemyIndex, 0));
        }

        /// <summary>一只缺笔妖的自补全(2026-08-15 提取,行为与提取前逐字节一致)。</summary>
        private void RegrowOneEnemy(int enemyIndex)
        {
            var enemy = _enemies[enemyIndex];
            // 本回合已被灼烧/召唤物打死的不许回血 —— 死了还补就成了打不死的怪
            if (!enemy.Alive) return;
            if (enemy.Def.Ability != EnemyAbility.Regrow || IsSilenced(enemy) || enemy.RegrowProgress >= 3) return;

            int before = enemy.Hp;
            enemy.RegrowProgress += 1;
            enemy.BaseAttack += 20; // 补全成长(形态变化,非增益):不可驱散(2026-08-12 随全表量级 ×10)
            // 上限取 enemy.MaxHp(当前阶段上限)而非 Def.MaxHp:缺笔妖眼下不分阶段,
            // 两者相等,但语义上该跟随阶段 —— 免得日后给它加阶段时回血直接越过阶段上限
            enemy.Hp = Math.Min(enemy.MaxHp, enemy.Hp + 30); // 2026-08-12 随全表量级 ×10
            if (enemy.RegrowProgress == 3)
            {
                // ×2 翻的是 BaseAttack(形态变化)。2026-08-12 AttackBuff 统一成百分点后,
                // 外部增益是 BaseAttack 的比值,于是**会**跟着一起放大 —— 这不是回退,是
                // 「比值就该跟着基数走」的直接后果(旧的 2026-08-05 裁定建立在加数语义上,已失效)。
                enemy.BaseAttack *= 2;
                enemy.Hp = enemy.MaxHp;
            }
            _events.Add(new BattleEvent(BattleEventKind.Regrow, enemyIndex,
                enemy.Hp - before, enemy.RegrowProgress));
        }

        /// <summary>玩家侧状态回合递减(2026-08-04,抽成方法见 2026-08-06 C2;敌人侧已在
        /// 2026-08-15 的 ATB 时序归属搬迁中挪到 <see cref="ActEnemyTurn"/> 各自那一拍自行递减,
        /// 不再在这里统一处理,方法因此改名)。2026-08-16(全分支终审 Important 1):唯一调用点
        /// 挪到了 <see cref="BeginPlayerTurn"/> 里紧跟 SettlePlayerBurn/SettlePlayerHots 之后
        /// ——曾经错放在上一拍 YieldTurn() 里,导致玩家侧变成「先递减后结算」,与敌人侧的
        /// 「结算在前、递减在后」方向相反,静默改动了三处数值(见 BeginPlayerTurn 注释)。</summary>
        private void TickPlayerStatuses()
        {
            // 玩家侧没有冻结概念,整袋统一递减即可(HoT 到期移除;减伤 TurnsLeft = -1 段内持久,不受影响)。
            _playerStatuses.TickTurns();

            // 战意每回合末消减一层(2026-08-15 拍板,原为本场持久)。
            // 单独处理而不是走 TickTurns:战意是**计数器式**状态 —— TurnsLeft = -1、层数记在
            // Magnitude 上,TickTurns 只认 TurnsLeft,碰不到它。同理 ApBoost / CritBuff /
            // PierceBuff / Empower 仍是本场持久,不在这里衰减。
            // 排在本回合全部结算之后:当回合出的 战 先按 3 层生效,回合末才掉到 2。
            //
            // 首回合宽限(2026-08-18 拍板):**从 0 层起手的那一回合不递减**,第二回合起才开始掉。
            // 身上已有战意时再叠,则照常当回合递减。没有这条的话 戮 那一层等于白给 ——
            // 当回合生效、同一个回合末就归零。标记由 AddPlayerCounter 在「新建」那一支置起。
            var morale = _playerStatuses.Find(StatusKind.Morale);
            if (_moraleGraceTurn)
            {
                _moraleGraceTurn = false;
            }
            else if (morale != null)
            {
                morale.Magnitude -= 1;
                if (morale.Magnitude <= 0) _playerStatuses.Remove(StatusKind.Morale);
            }
        }

        private void StartTurn()
        {
            Turn += 1;
            // 封字(2026-08-06):AP 扣减从裸字段改成 StatusKind.Seal —— 这样它可被净化、
            // 可被免疫,并且跟着 PlayerStatuses 进存档(裸字段从来没进过 BattleSnapshot,
            // 倾覆后存档续爬会白丢惩罚)。到期移除由统一的状态回合递减负责,这里不清。
            // 顺序:先加 ApBoost(利,2026-08-12)再减封字,最后才钳保底 —— 反过来
            // (先钳后加)会让重封字下的 利 白白多给一点 AP。
            Ap = Math.Max(1, ApPerTurn - _playerStatuses.TotalMagnitude(StatusKind.Seal));

            // 回合掉字(2026-08-04):从出战牌组掉 N 个字入库,满库则停下让玩家决议。
            // 部件不再掉落 —— 五行部件只能靠拆字获得(拆免 AP 是这条的对冲)。
            if (_config.UnlockedChars != null && _config.UnlockedChars.Count > 0)
            {
                var deck = new List<string>(_config.UnlockedChars);
                for (int i = 0; i < _config.DropsPerTurn; i++)
                {
                    string pick = deck[_random.Next(deck.Count)];
                    if (_forge.Library.Count >= _config.LibraryCapacity)
                    {
                        _pendingDrop = pick;
                        Phase = BattlePhase.DropChoice;
                        return; // 决议完才继续;剩余份额本回合作废
                    }
                    var library = new List<string>(_forge.Library) { pick };
                    _forge = new ForgeState(library, _forge.Pool);
                }
            }
        }

        private void ApplyEffects(CharDef def, int targetIndex, bool replaceSummon = false, bool attackMode = false,
            IReadOnlyList<int> summonSlots = null, int allySlot = Targeting.PlayerTarget)
        {
            var recipeElements = _graph.RecipeElements(def.Id);
            var attacker = def.Element ?? Element.Heart; // 中性字视作心(全 1.0x)
            int cardLevel = _cardLevels != null && _cardLevels.TryGetValue(def.Id, out var level) ? level : 1;
            // 未指定槽位(summonSlots == null)且顶替时的旧口径兜底:从最前一只存活起逐只
            // 后移,一次召多只不会重复顶掉刚进场的自己。只有真没空位/尸体槽可占(NextEmptySlot()
            // 返回 −1)才会用到 —— 指定槽位的路径不吃这个游标。
            // 声明在方法头部(而不是 Summon 的 case 块内):同一个 CharDef 若有两条独立的
            // EffectKind.Summon 效果(SummonCountOf 文档说的"多条召唤效果累加"),case 会命中
            // 两次;声明在 case 块内会让游标每次从 0 重新起算,顶掉第一条效果刚放进去的那只 ——
            // 这是 2026-08-20 review 抓出的收窄作用域回归,SummonSlotTests 的
            // Cast_MultiEffectSummon_ReplaceMode_AdvancesAcrossEffects 钉住这个语义。
            int replaceCursor = 0;

            // 玩家指定槽位时的落位游标(2026-08-20 review I-2):**同样声明在方法头部**。
            // 它和 replaceCursor 是同一个 bug 的两半 —— 此前这里直接用内层的 `n`,而 `n` 是
            // 每条 effect 各自从 0 起算的,两条各召 1 只的 Summon 效果会双双取 summonSlots[0]:
            // 第二条进来时该槽已被第一只占住且活着,occupiedByAlive && !replaceSummon → break,
            // 第二只静默蒸发,而 AP 已扣、字已消耗。当前字表里没有多效果召唤字(15 张全是
            // 单条 effect 用 count 表示只数),但半个家族的修法比不修更误导后来者。
            int summonCursor = 0;

            foreach (var effect in EffectsOf(def, attackMode))
            {
                int value = MetaRules.ScaleByCardLevel(effect.Value, cardLevel); // 19.3.2:等级先作用于基础值
                switch (effect.Kind)
                {
                    case EffectKind.DamageSingle:
                    {
                        // 形状展开(2026-08-22,spec §5):目标表首项是主目标,只有它吃
                        // 斩杀/多段/穿透;其余按 ShapePercent 折算。Shape 缺省 Single 时
                        // 表长恒为 1,整段逐位等价于改造前 —— 恒等性硬线就落在这里。
                        var shapeTargets = Targeting.ExpandTargets(
                            _enemies, targetIndex, effect.Shape, effect.Shots);
                        for (int t = 0; t < shapeTargets.Count; t++)
                        {
                            int tgt = shapeTargets[t];
                            bool primary = t == 0;
                            // 连发每一发都是全额:它没有「主目标 + 溅射」的结构,
                            // N 发是发数不是衰减(spec §3.3)
                            int percent = primary || effect.Shape == TargetShape.Volley
                                ? 100 : effect.ShapePercent;
                            int hits = primary ? effect.HitCount : 1;
                            // 多段(2026-08-07,剁):每段完全独立 —— 各自判存活、各自过斩杀阈值、
                            // 各自过生克与破甲。目标中途死了就停,不对尸体发事件
                            for (int hit = 0; hit < hits; hit++)
                            {
                                if (!_enemies[tgt].Alive) break;
                                if (primary && TryExecuteKill(effect, tgt)) break; // 处决:击杀后无需再打
                                // ATK 缩放在最外层:先过卡等级 → 灼烧翻倍 → 残血加伤,最后整体乘攻击力,
                                // 再交给 DamageEnemy 过生克与减伤。放在里层会与那几个 ×2 的取整互相干扰
                                // 暴击每段独立摇(2026-08-12),且摇点排在上面两条守卫**之后** ——
                                // 目标死了 / 被处决了都不该白摇一次,否则「这一发消耗几个随机数」
                                // 会取决于目标的血量,复现与调试都会变成噩梦
                                int baseValue = BaseValue(effect, value, _enemies[tgt]);
                                if (primary) baseValue = ExecuteBonus(effect, tgt, baseValue);
                                int damage = ScaleByAttack(baseValue);
                                // percent == 100 时**不做乘除**:x * 100 / 100 在整数下虽然等于 x,
                                // 但跳过它才能让「缺省路径与改前逐字节相同」成为结构性保证而非算术巧合
                                if (percent != 100) damage = damage * percent / 100;
                                DamageEnemy(tgt, damage, recipeElements, attacker,
                                    crit: RollCrit(),
                                    pierce: primary ? effect.Pierce : 0); // 多段:每段各减一次护甲(裁定 4)
                            }
                        }
                        break;
                    }
                    case EffectKind.DamageAll:
                        int aoeCount = _enemies.Count; // 分裂产生的新怪不吃同一发 AOE
                        for (int i = 0; i < aoeCount; i++)
                        {
                            if (!_enemies[i].Alive) continue;
                            if (TryExecuteKill(effect, i)) continue; // 斩杀对每个目标分别判定
                            // 暴击逐个目标独立摇(2026-08-12),同样排在存活/处决两条守卫之后
                            // AOE:逐目标各减各自的护甲(spec §4.4(a)),不是总量只减一次 ——
                            // 「把总量摊成多份」对点数甲天然有惩罚,这正是「AOE 清杂兵、单体破装甲」
                            // 那条战术分工的具体形状;代价靠配置口径(带甲怪不成群)兜
                            DamageEnemy(i,
                                ScaleByAttack(ExecuteBonus(effect, i, BaseValue(effect, value, _enemies[i]))),
                                recipeElements, attacker, crit: RollCrit(),
                                pierce: effect.Pierce);
                        }
                        break;
                    case EffectKind.BurnSingle:
                        if (_enemies[targetIndex].Alive)
                        {
                            ApplyBurn(targetIndex, value);
                            _events.Add(new BattleEvent(BattleEventKind.Burn, targetIndex, value));
                        }
                        break;
                    case EffectKind.Bleed:
                        if (_enemies[targetIndex].Alive)
                        {
                            _enemies[targetIndex].Statuses.Apply(new StatusEffect
                            {
                                Kind = StatusKind.Bleed, Polarity = StatusPolarity.Debuff,
                                // 出牌时吃攻击力:Magnitude 本来就是施加时定死的,套上即为快照语义
                                Magnitude = ScaleByAttack(value), TurnsLeft = 3,   // 固定 3 回合
                            });
                        }
                        break;
                    case EffectKind.Freeze:
                        if (_enemies[targetIndex].Alive)
                            _enemies[targetIndex].Statuses.Apply(new StatusEffect
                            {
                                // Magnitude 不赋值(2026-08-05 M1):全代码库没有任何地方读它,
                                // 赋了反而是语义为空的垃圾值,TotalMagnitude(Freeze) 会返回它。
                                Kind = StatusKind.Freeze, Polarity = StatusPolarity.Debuff,
                                TurnsLeft = value,
                            });
                        break;
                    case EffectKind.Slow:
                        if (_enemies[targetIndex].Alive)
                        {
                            _enemies[targetIndex].Statuses.Apply(new StatusEffect
                            {
                                Kind = StatusKind.SpeedModifier, Polarity = StatusPolarity.Debuff,
                                Magnitude = -50, TurnsLeft = value, SourceId = def.Id,
                            });
                        }
                        break;
                    case EffectKind.ArmorBreak:
                        // 破甲 = 削目标护甲 Value **点**(2026-08-12,E-b4 T3 复原原始设计)。
                        // TurnsLeft = -1:**本场持久**,依据第 10 章 :56「破甲永久降护甲」。
                        // SourceId 铸唯一序号 → **可叠加**:不叠只刷新的话六个破甲字互相排斥,
                        // 先出削 20 的再出削 10 的会变弱,而战例二的「三张接力削光坚壁 Boss」
                        // 整套玩法就建立在叠加上。上限由 EffectiveEnemyDefense 的 max(0,…) 天然给出。
                        _enemies[targetIndex].Statuses.Apply(new StatusEffect
                        {
                            Kind = StatusKind.ArmorBreak,
                            Polarity = StatusPolarity.Debuff,
                            Magnitude = value,
                            TurnsLeft = -1,
                            SourceId = $"{def.Id}#{_statusSerial++}",
                        });
                        break;
                    case EffectKind.Dispel:
                        // 条数用 effect.Value 而不是 value —— 驱散条数不吃卡等级(与召唤被动同口径:
                        // 「资源」随等级涨,「节奏」不涨),而且 −1 这个哨兵值过 ScaleByCardLevel 会算歪
                        if (effect.TargetAll)
                        {
                            // 与 DamageAll 那句注释不同:这里取值点在本次 ApplyEffects 调用里前面的
                            // 伤害效果已经触发过分裂之后(如湮:DamageSingle 20 + 驱散全部)——分裂
                            // 产生的新怪这时已经在列表里,会被这发驱散扫到。行为上无差别(克隆的
                            // Statuses 是空袋,没有可驱散的增益),纯粹是旧注释说反了(2026-08-06 M8)。
                            int count = _enemies.Count;
                            for (int i = 0; i < count; i++)
                                if (_enemies[i].Alive) DispelFrom(i, effect.Value);
                        }
                        // targetIndex >= 0 兜底(2026-08-06 C1):NeedsTarget 漏判时 targetIndex 会
                        // 停在 -1,_enemies[-1] 直接越界;修好 NeedsTarget 后这条不该再触发,但留作
                        // 双保险,免得将来又冒出一条新字踩中同类疏漏。
                        else if (targetIndex >= 0 && _enemies[targetIndex].Alive)
                        {
                            DispelFrom(targetIndex, effect.Value);
                        }
                        break;

                    case EffectKind.Cleanse:
                        // 不发事件(2026-08-06 M2):与诅咒同口径——表现层直接读 PlayerStatuses 画 chip,
                        // 没有任何消费方读 Cleanse 事件,发了也是死代码。
                        _playerStatuses.RemoveAll(StatusPolarity.Debuff);
                        break;
                    case EffectKind.Immunity:
                        // SourceId 用字 ID:同字再出只刷新,不无限叠层数;
                        // 不同字之间可叠(塞 1 + 杜 2 = 3 次),因为它们是不同来源。
                        // 不发事件(2026-08-06 M2):没有任何消费方读 Immunity 事件,理由同 Cleanse。
                        _playerStatuses.Apply(new StatusEffect
                        {
                            Kind = StatusKind.Immunity, Polarity = StatusPolarity.Buff,
                            Magnitude = value, TurnsLeft = -1, SourceId = def.Id,
                        });
                        break;
                    case EffectKind.Revive:
                        for (int n = 0; n < value; n++)
                        {
                            // 死尸占着槽位,复活不新增条目但存活数 +1 —— 满员时停手,免得超上限
                            if (AliveSummons() >= SummonCap) break;
                            int slot = FirstDeadSummonIndex();
                            if (slot < 0) break; // 没有阵亡召唤物 → 空放(与无敌人时出 AOE 同口径)
                            var revived = _summons[slot];
                            revived.Hp = (revived.MaxHp + 1) / 2; // 半血,向上取整
                            revived.ActionMeter = 0;              // 重新攒节拍,不继承死前余额
                            revived.Shield = 0;                   // 盾不跟着复活
                            // Passive 是只读属性,天然保留 —— 它是这只召唤物的身份
                            _events.Add(new BattleEvent(BattleEventKind.Summon, -1, revived.Hp, slot));
                        }
                        break;
                    case EffectKind.Blind:
                        // SourceId 用字 ID:同字再出只刷新,不无限叠命中惩罚
                        if (effect.TargetAll)
                        {
                            int blindCount = _enemies.Count; // 分裂产生的新怪不吃同一发(与 DamageAll 同口径)
                            for (int i = 0; i < blindCount; i++)
                                if (_enemies[i].Alive) ApplyBlind(i, value, effect.Turns, def.Id);
                        }
                        else if (targetIndex >= 0 && _enemies[targetIndex].Alive)
                        {
                            ApplyBlind(targetIndex, value, effect.Turns, def.Id);
                        }
                        break;
                    case EffectKind.Silence:
                        if (targetIndex >= 0 && _enemies[targetIndex].Alive)
                        {
                            _enemies[targetIndex].Statuses.Apply(new StatusEffect
                            {
                                Kind = StatusKind.Silence, Polarity = StatusPolarity.Debuff,
                                Magnitude = 1, TurnsLeft = effect.Turns, SourceId = def.Id,
                            });
                            // 沉默要在挂上的当下就打断蓄力(评审 Important 1,2026-08-08):
                            // ResolveBossTurn 开头那处短路只在敌人真的行动(actionCount>0)时才跑,
                            // 蓄力期间恰好被冻结/减速卡住不动的话,沉默会一路挂满到期都没触发,
                            // 一解冻/解速立刻放出大招——与「锁住的是正在攒的那一下」的语义正相反。
                            var target = _enemies[targetIndex];
                            if (target.IsCharging)
                            {
                                target.IsCharging = false;
                                target.ChargeCounter = 0;
                            }
                        }
                        break;
                    case EffectKind.Reflect:
                        _playerStatuses.Apply(new StatusEffect
                        {
                            Kind = StatusKind.Reflect, Polarity = StatusPolarity.Buff,
                            Magnitude = value, TurnsLeft = effect.Turns, SourceId = def.Id,
                        });
                        break;
                    case EffectKind.BurnNoDecay:
                        // SourceId 用字 ID:同字再出只刷新,不挂两条
                        if (targetIndex >= 0 && _enemies[targetIndex].Alive)
                            _enemies[targetIndex].Statuses.Apply(new StatusEffect
                            {
                                Kind = StatusKind.BurnNoDecay, Polarity = StatusPolarity.Debuff,
                                Magnitude = 1, TurnsLeft = -1, SourceId = def.Id,
                            });
                        break;
                    case EffectKind.BurnSettleNow:
                        // 复用回合末那一套(SettleBurnOn 自带存活与空层守卫),不留两份实现
                        if (targetIndex >= 0) SettleBurnOn(targetIndex);
                        break;
                    case EffectKind.Detonate:
                        if (targetIndex >= 0) Detonate(targetIndex);
                        break;
                    case EffectKind.Empower:
                        // 剡(2026-08-12):本场攻击 +Value,复用 AttackBuff。
                        // SourceId 铸唯一序号(用法 2)才能叠 —— 传裸字 ID 会让第二张剡
                        // 覆盖第一张,静默退化成刷新。
                        _playerStatuses.Apply(new StatusEffect
                        {
                            Kind = StatusKind.AttackBuff, Polarity = StatusPolarity.Buff,
                            Magnitude = value, TurnsLeft = -1,
                            SourceId = $"{def.Id}#{_statusSerial++}",
                        });
                        break;
                    case EffectKind.Morale:
                        // 战/戮(2026-08-12):战意是一条**带上限的计数器**,战与戮往同一条上加。
                        // 所以既不能铸唯一序号(各挂各的会绕开上限),也不能走 Apply() 的
                        // 同源覆盖(那是刷新,出两张战还是 3 层)—— 只能就地累加再钳。
                        AddPlayerCounter(StatusKind.Morale, value, MoraleMaxStacks);
                        break;
                    case EffectKind.CritBuff:
                        // 锋(2026-08-12,E-b2):本场暴击率 +Value 个百分点。
                        // 与 剡 的 Empower 同款:SourceId 铸唯一序号(用法 2)才能叠 ——
                        // 传裸字 ID 会让第二张锋覆盖第一张,静默退化成刷新。
                        // 不在这里钳上限,由 EffectiveCrit 的 Clamp 统一负责:钳在施加处的话
                        // 「90 + 20」会被存成 100,后来驱散掉一条反而看不出原本该剩多少。
                        _playerStatuses.Apply(new StatusEffect
                        {
                            Kind = StatusKind.CritBuff, Polarity = StatusPolarity.Buff,
                            Magnitude = value, TurnsLeft = -1,
                            SourceId = $"{def.Id}#{_statusSerial++}",
                        });
                        break;
                    case EffectKind.ApBoost:
                        // 利(2026-08-12):AP 是「节奏/经济」不是「资源」,与驱散条数、
                        // 召唤被动同口径**不吃卡等级** —— 用 effect.Value 而不是 value:
                        // Lv.10 系数 1.9 会把 +1 AP 算成 ceil(1×1.9) = 2,每回合翻倍的是
                        // 整个出牌预算,不是一条效果的数值。
                        AddPlayerCounter(StatusKind.ApBoost, effect.Value, int.MaxValue);
                        break;
                    case EffectKind.DefenseBuff:
                        // 护甲 +Value **点**(2026-08-12,E-b4 T3):多字**加法**叠加(旧乘法层是
                        // 连乘,天然趋近但不达 0;点数是直接相加),同字仍按 SourceId 覆盖 = 只刷新。
                        _playerStatuses.Apply(new StatusEffect
                        {
                            Kind = StatusKind.DefenseBuff, Polarity = StatusPolarity.Buff,
                            Magnitude = value, TurnsLeft = -1, SourceId = def.Id, // 段内持久
                        });
                        break;
                    case EffectKind.PierceBuff:
                        // 锐(2026-08-12,E-b4 T5):本场穿透 +Value 点,由 EffectiveEnemyDefense 读走。
                        // 与 剡 的 Empower / 锋 的 CritBuff 同款:SourceId 铸唯一序号(用法 2)才能叠 ——
                        // 传裸字 ID 会让第二张锐覆盖第一张,静默退化成刷新。
                        // 不在这里钳上限:穿过头由 EffectiveEnemyDefense 的 max(0, …) 兜住,
                        // 钳在施加处会让「穿透 50 打 DEF 10」把多出来的 40 也存丢,换个敌人就亏了。
                        _playerStatuses.Apply(new StatusEffect
                        {
                            Kind = StatusKind.PierceBuff, Polarity = StatusPolarity.Buff,
                            Magnitude = value, TurnsLeft = -1,
                            SourceId = $"{def.Id}#{_statusSerial++}",
                        });
                        break;
                    case EffectKind.BurnAll:
                        for (int i = 0; i < _enemies.Count; i++)
                            if (_enemies[i].Alive)
                            {
                                ApplyBurn(i, value);
                                _events.Add(new BattleEvent(BattleEventKind.Burn, i, value));
                            }
                        break;
                    case EffectKind.Shield:
                        int shield = WuxingResolver.ResolveEffect(value, recipeElements, attacker);
                        if (effect.PersistOnce) _shieldPersist += shield;
                        else _shieldNormal += shield;
                        _events.Add(new BattleEvent(BattleEventKind.Shield, -1, shield));
                        break;
                    case EffectKind.BurnPotency:
                        _burnPerStack += value;
                        break;
                    case EffectKind.HealSelf: // 水系主治疗(2026-07-19 拍板);走生克(相生组合可增益)
                        // 目标可选(2026-08-22,spec §8):生克算的是配方内部的元素关系,
                        // 与目标是谁无关 —— 治召唤物与治玩家同值
                        int heal = WuxingResolver.ResolveEffect(value, recipeElements, attacker);
                        HealAlly(allySlot, heal);
                        break;
                    case EffectKind.HealAll:
                        HealPlayerAndSummons(WuxingResolver.ResolveEffect(value, recipeElements, attacker));
                        break;
                    case EffectKind.HealOverTime:
                        // 可叠(2026-08-04,技能机制详表「滋」):SourceId 用自增序号而非字 ID,
                        // 让 Apply() 永远走新增分支——同字连放两次得到两条独立倒计时,与老代码
                        // 无条件 List.Add 的口径一致。不能用回合数做后缀:一回合 3 AP,同一回合
                        // 内完全可能连放两次,会被回合数误判成同一来源又变回刷新。
                        _playerStatuses.Apply(new StatusEffect
                        {
                            Kind = StatusKind.HealOverTime, Polarity = StatusPolarity.Buff,
                            Magnitude = WuxingResolver.ResolveEffect(value, recipeElements, attacker),
                            TurnsLeft = effect.Turns, TargetAll = effect.TargetAll,
                            TargetSlot = allySlot,
                            SourceId = $"{def.Id}#{_statusSerial++}",
                        });
                        break;
                    case EffectKind.Summon: // 木系主召唤(2026-07-19 拍板):前排抗伤+回合末反击
                        for (int n = 0; n < effect.SummonCount; n++)
                        {
                            // 被动数值不吃卡等级(2026-08-05):只有血/攻/盾这些"资源"随等级涨,
                            // 反伤/灼烧层/减攻百分比这些"节奏"保持不变,免得档位失控
                            // 召唤时吃攻击力:只作用于攻击力,血量(value)是防御资源不吃。
                            // SummonState.Attack 本来就是创建时常量,套上即为快照语义 ——
                            // 之后再抬攻击力,已在场的这只不变
                            // 新召唤物**上场即满格**(2026-08-17 用户拍板):召唤术的价值就在
                            // 「立刻有个肉盾并反击」,从 0 起攒会让它召出那一轮完全不动。
                            //
                            // 这是恢复 2026-08-15 删掉的那个头寸。当时删它的理由是「那是在给
                            // 反向的 tie-break 打补丁」—— 那句话在当时是对的(priority 刚被改成
                            // 「玩家最后」,召唤物排最先,从 0 起攒也能同回合出手)。2026-08-17
                            // 把方向调回「玩家最先」之后,玩家插到了召唤物前面,这个头寸就重新
                            // 成为必需:实测它让失败测试从 32 条降到 16 条(spec §2.3)。
                            // 它不是补丁,是「玩家优先」那一整套设计的组成部分。
                            var newborn = new SummonState(effect.SummonChar, attacker, value,
                                ScaleByAttack(MetaRules.ScaleByCardLevel(effect.SummonAttack, cardLevel)),
                                effect.Passive);
                            newborn.ActionMeter = TurnScheduler.Threshold;

                            // 落位:玩家指定优先,未指定退回最小空槽(与 Task 1 等价)。
                            // 下标取 summonCursor 而非内层的 n —— 见方法头部那条注释
                            int slot;
                            if (summonSlots != null && summonCursor < summonSlots.Count)
                            {
                                slot = summonSlots[summonCursor];
                            }
                            else
                            {
                                slot = NextEmptySlot();
                                // 空槽/尸体槽都没有了:只有「压根没指定槽位」的旧调用点才退回
                                // 逐只顶替的旧口径(见上方 replaceCursor 注释);指定了槽位的
                                // 调用点这里不兜底,越界检查会在下面拦下。
                                if (slot < 0 && replaceSummon && summonSlots == null)
                                {
                                    slot = NextAliveSummonIndex(replaceCursor);
                                    if (slot >= 0) replaceCursor = slot + 1;
                                }
                            }
                            if (slot < 0 || slot >= SummonCap) break;          // 越界兜底
                            bool occupiedByAlive = _summons[slot] != null && _summons[slot].Alive;
                            if (occupiedByAlive && !replaceSummon) break;      // 已在 Cast 拒出,走不到这

                            // SecondIndex 一律报落位槽:新增与顶替都要让表现层知道画哪一格。
                            // 「是不是顶替」表现层自己看该槽原来有没有活着的召唤物,不靠事件区分。
                            _summons[slot] = newborn;
                            summonCursor++; // 每落一只推进一格,跨 effect 持续累加
                            _events.Add(new BattleEvent(BattleEventKind.Summon, -1, value, slot));
                        }
                        // 桂(2026-08-05):护盾发给出字时**全场**存活召唤物,含刚召出的这几只。
                        // 它是一次性额外血条 —— 吸完即无、不刷新、不随回合清空(召唤物本身就是
                        // 消耗品,再加个衰减太碎)。盾是"资源",跟血/攻一样吃卡等级
                        if (effect.SummonShield > 0)
                        {
                            int shieldGrant = MetaRules.ScaleByCardLevel(effect.SummonShield, cardLevel);
                            foreach (var summon in _summons)
                                if (summon != null && summon.Alive) summon.Shield += shieldGrant;
                        }
                        break;
                }
            }
        }

        /// <summary>玩家侧的「累加型计数器」状态(战意 / AP 上限加成,2026-08-12):
        /// 全场只有一条,重复施加往上加并钳到 cap,而不是新挂一条也不是刷新。
        ///
        /// 不能复用 <see cref="StatusBag.Apply"/> 的任一既有语义:铸唯一序号会各挂各的、
        /// 让 TotalMagnitude 绕开上限;裸字 ID 去重则是覆盖刷新(出两张战还是 3 层)。
        /// SourceId 留 null:按 Kind 唯一,驱散/净化照常按极性认它。</summary>
        private void AddPlayerCounter(StatusKind kind, int amount, int cap)
        {
            var existing = _playerStatuses.Find(kind);
            if (existing != null)
            {
                existing.Magnitude = Math.Min(existing.Magnitude + amount, cap);
                return;
            }
            // 从 0 起手:战意本回合免一次递减(2026-08-18,见 TickPlayerStatuses)。
            // 只对战意置标记 —— ApBoost 走同一个方法但本来就不递减。
            if (kind == StatusKind.Morale) _moraleGraceTurn = true;
            _playerStatuses.Apply(new StatusEffect
            {
                Kind = kind, Polarity = StatusPolarity.Buff,
                Magnitude = Math.Min(amount, cap), TurnsLeft = -1, SourceId = null,
            });
        }

        /// <summary>驱散一名敌人的增益:count &lt; 0 清全部,否则从头清至多 count 条。
        /// 现存的唯一靶子是 AttackBuff(标点小妖给同伴加攻、焦痕受击自燃)——两者都是
        /// TurnsLeft = -1 的永久增益且本场累计,所以驱散是它们唯一的解法。
        /// 不发事件(2026-08-06 M2):没有任何消费方读 Dispel 事件,与诅咒同口径。</summary>
        private void DispelFrom(int enemyIndex, int count)
        {
            var statuses = _enemies[enemyIndex].Statuses;
            if (count < 0) statuses.RemoveAll(StatusPolarity.Buff);
            else statuses.RemoveFirst(StatusPolarity.Buff, count);
        }

        /// <summary>对一名敌人结算一次灼烧(2026-08-09 抽出):层数 × 系数 × 克制 掉血,然后 −1 层。
        /// 回合末逐个调用;燥 的 BurnSettleNow(Task 3)复用这里 —— 不留两份实现。
        ///
        /// 灼烧属火(2026-08-03):只结算克制,不结算相生 —— 层数是平值,
        /// 相生已在施加时由 WuxingResolver 体现过。</summary>
        private void SettleBurnOn(int enemyIndex)
        {
            var enemy = _enemies[enemyIndex];
            if (!enemy.Alive) return;
            var burn = enemy.Statuses.Find(StatusKind.Burn);
            if (burn == null || burn.Magnitude <= 0) return;
            // 攻击力**结算时读**,回溯生效 —— 与炽/BurnPotency 同口径:每层伤害从来不是
            // 出牌时冻结的量,它是 _burnPerStack 这个全局标量。层数(Magnitude)不吃攻击力。
            // 不复用 ScaleByAttack:那是整数除(早截断),这里要插进既有的浮点式子里晚截断,
            // 才能在基准值下保住逐字节恒等
            int tick = (int)Math.Floor(burn.Magnitude * _burnPerStack
                * (EffectiveAttack / (double)BattleConfig.AttackBaseline)
                * WuxingResolver.KeMultiplier(Element.Fire, enemy.Element));
            enemy.Hp = Math.Max(0, enemy.Hp - tick);
            RevealDisguise(enemyIndex); // 通假字:灼烧扣血也算挨打(2026-08-15 口径 7)
            // 不灭(2026-08-09,炑):带 BurnNoDecay 时层数不衰减 —— 伤害算式一个字不动,
            // 只挡这一步。Task 3 的 BurnSettleNow 同样复用这里,所以「免费兑现」
            // (立即结算也不掉层)也一并生效——这是规格 §4.2 那条爆发链的根
            if (!enemy.Statuses.Has(StatusKind.BurnNoDecay))
            {
                burn.Magnitude -= 1;
                if (burn.Magnitude <= 0) enemy.Statuses.Remove(StatusKind.Burn);
            }
            _events.Add(new BattleEvent(BattleEventKind.BurnTick, enemyIndex, tick));
            if (!enemy.Alive)
                ResolveDefeat(enemyIndex);
            else
                CheckBossPhase(enemyIndex);
        }

        /// <summary>对一名敌人结算一次流血(2026-08-15,ATB 时序归属搬迁:从 SettlePlayerTurnEnd
        /// 那段回合末循环体拆出单只版本,搬进 ActEnemyTurn 各自那一拍,行为不变)。
        /// 流血无属性(2026-08-03),不乘任何生克系数;不吃攻击力 —— 施加 Bleed 时
        /// Magnitude 已经用 ScaleByAttack 定死,是快照语义,这里只读不再二次缩放。
        /// TurnsLeft 只读不写:回合数递减挪到 ActEnemyTurn 末尾统一处理(与 SettleBurnOn 同口径,
        /// 避免本回合刚施加的流血被立刻多减一次)。</summary>
        private void SettleBleedOn(int enemyIndex)
        {
            var enemy = _enemies[enemyIndex];
            if (!enemy.Alive) return;
            var bleedStatus = enemy.Statuses.Find(StatusKind.Bleed);
            if (bleedStatus == null || bleedStatus.TurnsLeft <= 0) return;
            int bleed = bleedStatus.Magnitude;
            enemy.Hp = Math.Max(0, enemy.Hp - bleed);
            RevealDisguise(enemyIndex); // 通假字:流血扣血也算挨打(2026-08-15 口径 7)
            _events.Add(new BattleEvent(BattleEventKind.BleedTick, enemyIndex, bleed));
            if (!enemy.Alive)
                ResolveDefeat(enemyIndex);
            else
                CheckBossPhase(enemyIndex);
        }

        /// <summary>引爆(2026-08-09,灱):把剩余层数的**全部未来伤害**一次打出,然后清空层数。
        ///
        /// N 层正常烧完是 N + (N−1) + … + 1 = N(N+1)/2 个「层·回合」,所以总量口径就是那个和
        /// 乘系数 —— **只改兑现时机,不改总量**。价值在抢杀,以及防止敌人被别的牌提前打死
        /// 而浪费层数。
        ///
        /// 与回合末结算同口径:属火、只算克制不算相生。
        /// 清的是灼烧层数,**不动 BurnNoDecay** —— 之后重新点燃仍然不衰减。</summary>
        private void Detonate(int enemyIndex)
        {
            var enemy = _enemies[enemyIndex];
            if (!enemy.Alive) return;
            var burn = enemy.Statuses.Find(StatusKind.Burn);
            if (burn == null || burn.Magnitude <= 0) return;
            int stacks = burn.Magnitude;
            // 与 SettleBurnOn 同口径吃攻击力:引爆是把剩余层数一次性兑现,
            // 每层伤害用的是同一个量,不能只有一边吃
            int damage = (int)Math.Floor(stacks * (stacks + 1) / 2.0 * _burnPerStack
                * (EffectiveAttack / (double)BattleConfig.AttackBaseline)
                * WuxingResolver.KeMultiplier(Element.Fire, enemy.Element));
            enemy.Statuses.Remove(StatusKind.Burn);
            enemy.Hp = Math.Max(0, enemy.Hp - damage);
            _events.Add(new BattleEvent(BattleEventKind.Detonate, enemyIndex, damage));
            if (!enemy.Alive)
                ResolveDefeat(enemyIndex);
            else
                CheckBossPhase(enemyIndex);
        }

        /// <summary>叠加灼烧层数(TurnsLeft = -1:段内持久,靠结算段自减 Magnitude,不受 TickTurns 影响)。
        /// 出字的灼烧字用这条:一次性施加,层数自然衰减到 0,累加是既有语义,不受光环影响。</summary>
        private void ApplyBurn(int enemyIndex, int value)
        {
            var enemy = _enemies[enemyIndex];
            int newBurn = (enemy.Statuses.Find(StatusKind.Burn)?.Magnitude ?? 0) + value;
            enemy.Statuses.Apply(new StatusEffect
            {
                Kind = StatusKind.Burn, Polarity = StatusPolarity.Debuff,
                Magnitude = newBurn, TurnsLeft = -1,
            });
        }

        /// <summary>给一名敌人挂致盲。TurnsLeft 直接用配置的回合数 —— 致盲是玩家在自己回合
        /// 挂上的,不像 Boss 倾覆那样在敌方段挂(那种要 +1 才能熬过同回合的状态递减)。</summary>
        private void ApplyBlind(int enemyIndex, int percent, int turns, string sourceId)
        {
            _enemies[enemyIndex].Statuses.Apply(new StatusEffect
            {
                Kind = StatusKind.Blind, Polarity = StatusPolarity.Debuff,
                Magnitude = percent, TurnsLeft = turns, SourceId = sourceId,
            });
        }

        /// <summary>刷新灼烧层数到 N 层(取现有层数与 N 的较大值,而非像 ApplyBurn 那样累加,
        /// 2026-08-06 I1):光环式来源(烓/灶,以及玩家侧的灯花 Sear)是每回合重复施加,若复用
        /// ApplyBurn 的累加语义,每回合净增长 = 挂层数 − 衰减 1 层,没有上界(烓 全体挂 3、衰减 1,
        /// 净 +2,十回合后失控;灯花本身单只是净 0,但 BuildFloor 有放回抽取,同场可能出现
        /// 多只灯花,N 只就净 +(N−1)/回合)。
        /// Math.Max 保证:①连续多回合刷新不会累积;②不会削低出字灼烧已经堆起来的更高层数。
        /// 接 <see cref="StatusBag"/> 而非敌人下标——玩家侧(灯花)与敌人侧(烓/灶)共用同一份实现。</summary>
        private static void RefreshBurn(StatusBag statuses, int stacks)
        {
            int current = statuses.Find(StatusKind.Burn)?.Magnitude ?? 0;
            statuses.Apply(new StatusEffect
            {
                Kind = StatusKind.Burn, Polarity = StatusPolarity.Debuff,
                Magnitude = Math.Max(current, stacks), TurnsLeft = -1,
            });
        }

        /// <summary>召唤物出手的附带效果(2026-08-05,子项目 C):挂灼烧 / 挂诅咒。
        /// 攻 0 的召唤物(烓/灶)照样走到这里 —— 它们的输出全靠这一步,
        /// 所以上面的出手循环绝不能因为 Attack &lt;= 0 就提前跳过。
        /// 挂灼烧发 BattleEventKind.Burn 事件复用既有飘字;诅咒不发事件——
        /// 表现层直接读敌人的 Statuses 画 chip,再加个只有一处消费的事件是多余的。</summary>
        private void ApplySummonOnHit(SummonState summon, int targetIndex)
        {
            var passive = summon.Passive;
            if (passive == null) return;

            if (passive.OnHitBurn > 0)
            {
                if (passive.OnHitBurnAll)
                {
                    // 不取快照(2026-08-06 M4):这里没有哪一步会触发分裂——分裂只在 DamageEnemy
                    // 里判定,而这个循环体内只调 RefreshBurn,不会扩表,直接读 _enemies.Count 即可。
                    for (int i = 0; i < _enemies.Count; i++)
                    {
                        if (!_enemies[i].Alive) continue;
                        RefreshBurn(_enemies[i].Statuses, passive.OnHitBurn); // 光环:刷新到 N 层,不是累加(I1)
                        _events.Add(new BattleEvent(BattleEventKind.Burn, i, passive.OnHitBurn));
                    }
                }
                else if (_enemies[targetIndex].Alive)
                {
                    RefreshBurn(_enemies[targetIndex].Statuses, passive.OnHitBurn); // 光环:刷新到 N 层,不是累加(I1)
                    _events.Add(new BattleEvent(BattleEventKind.Burn, targetIndex, passive.OnHitBurn));
                }
            }

            if (passive.OnHitCurse > 0 && _enemies[targetIndex].Alive)
            {
                _enemies[targetIndex].Statuses.Apply(new StatusEffect
                {
                    Kind = StatusKind.Curse, Polarity = StatusPolarity.Debuff,
                    Magnitude = passive.OnHitCurse, TurnsLeft = CurseTurns,
                    SourceId = CurseSourceId,
                });
            }
        }

        /// <summary>群体治疗:玩家 + 全部存活召唤物,各回 amount(玩家不超上限)。</summary>
        private void HealPlayerAndSummons(int amount)
        {
            int healed = Math.Min(_config.PlayerMaxHp - PlayerHp, amount);
            PlayerHp += healed;
            _events.Add(new BattleEvent(BattleEventKind.Heal, -1, healed));
            foreach (var summon in _summons)
            {
                if (summon == null || !summon.Alive) continue;
                summon.Hp = Math.Min(summon.MaxHp, summon.Hp + amount);
            }
        }

        /// <summary>把治疗打到一个友方目标上(2026-08-22)。slot = −1 治玩家,否则治该槽召唤物。
        /// 溢出部分丢弃。事件的 SecondIndex 带槽位 —— 与 Summon 事件报落位槽同一套写法,
        /// 不为治疗新增事件类型。</summary>
        private void HealAlly(int slot, int amount)
        {
            if (slot == Targeting.PlayerTarget)
            {
                int healed = Math.Min(_config.PlayerMaxHp - PlayerHp, amount);
                PlayerHp += healed;
                _events.Add(new BattleEvent(BattleEventKind.Heal, -1, healed, Targeting.PlayerTarget));
                return;
            }
            var summon = _summons[slot];
            if (summon == null || !summon.Alive) return; // Cast 已拦下,这里是纵深防御
            int given = Math.Min(summon.MaxHp - summon.Hp, amount);
            summon.Hp += given;
            _events.Add(new BattleEvent(BattleEventKind.Heal, -1, given, slot));
        }

        /// <summary>场上除 self 外还有存活敌人吗(辅助型据此决定加攻还是出手)。</summary>
        private bool HasOtherAliveEnemy(EnemyState self)
        {
            foreach (var enemy in _enemies)
                if (enemy != self && enemy.Alive) return true;
            return false;
        }

        /// <summary>该敌人是否被沉默(2026-08-07,锁)。压的是**主动机制** ——
        /// Boss 大招、缺笔妖补全、叠字分裂、标点加攻、焦痕自燃、灯花灼身。
        /// 通假/生僻不在其列:那两个是信息隐藏,锁一下就看穿了不符合「锁」的语义。</summary>
        private static bool IsSilenced(EnemyState enemy) => enemy.Statuses.Has(StatusKind.Silence);

        private int AliveSummons()
        {
            int alive = 0;
            foreach (var summon in _summons)
                if (summon != null && summon.Alive) alive++;
            return alive;
        }

        /// <summary>按每排上限 3 给场上敌人定实际站位与列(2026-08-20 排,2026-08-22 列)。
        /// 按 _enemies 顺序依次分:先满足 Def.Row 的偏好,该排已满则改判到另一排;
        /// 列号在**落定的那一排**里现算 —— 被改判的那只要从新排的计数起算,
        /// 沿用原排的计数会撞号。
        /// 两排都满走不到 —— EnemyCap 6 = 3 + 3,列表长度天然受限。</summary>
        private void AssignSlots()
        {
            int front = 0, back = 0;
            foreach (var enemy in _enemies)
            {
                bool wantsBack = enemy.Def.Row == EnemyRow.Back;
                if (wantsBack && back < EnemyRowCap) { enemy.Row = EnemyRow.Back; enemy.Column = back++; }
                else if (!wantsBack && front < EnemyRowCap) { enemy.Row = EnemyRow.Front; enemy.Column = front++; }
                else if (front < EnemyRowCap) { enemy.Row = EnemyRow.Front; enemy.Column = front++; }
                else { enemy.Row = EnemyRow.Back; enemy.Column = back++; }
            }
        }

        /// <summary>第一具尸体的槽位;没有返回 −1。引擎从不移除阵亡召唤物
        /// (表现层只是不画它们),所以复活直接就地救回。null 不是尸体,
        /// 复活救不回一个从未存在过的召唤物。</summary>
        private int FirstDeadSummonIndex()
        {
            for (int s = 0; s < SummonCap; s++)
                if (_summons[s] != null && !_summons[s].Alive) return s;
            return -1;
        }

        private int NextAliveSummonIndex(int from)
        {
            for (int s = from; s < SummonCap; s++)
                if (_summons[s] != null && _summons[s].Alive) return s;
            return -1;
        }

        /// <summary>最小的可落位槽:优先真正的空槽,其次尸体槽;全被存活者占满返回 −1。
        /// Task 1 阶段这是唯一的落位策略(等价于改前的「List 尾部追加」);Task 3 起
        /// 玩家可以指定槽位,本函数退化为「玩家没指定时的兜底」。</summary>
        private int NextEmptySlot()
        {
            for (int s = 0; s < SummonCap; s++)
                if (_summons[s] == null) return s;
            for (int s = 0; s < SummonCap; s++)
                if (!_summons[s].Alive) return s;
            return -1;
        }

        /// <summary>把携带/读档来的召唤物放回它记下的槽位;槽位非法或已被占则回落到最小空槽。</summary>
        private void PlaceCarried(SummonState summon, int slot)
        {
            if (slot < 0 || slot >= SummonCap || _summons[slot] != null)
                slot = NextEmptySlot();
            if (slot < 0) return; // 六槽全满:携带态来源受上限约束,走不到这;留作越界兜底
            _summons[slot] = summon;
        }

        /// <summary>条件基础值:灼类效果对带灼烧目标翻倍(10.3.1),再进生克结算。</summary>
        private static int BaseValue(EffectDef effect, int scaledValue, EnemyState target)
        {
            return effect.DoubleVsBurning && target.Statuses.Has(StatusKind.Burn) ? scaledValue * 2 : scaledValue;
        }

        /// <summary>目标现血是否低于斩杀阈值。MaxHp 取 EnemyState.MaxHp(Boss 的**总血池**——
        /// 全部阶段血量之和;ApplyPhaseStats 换阶不会改它,它永远不是「当前阶段上限」),
        /// 不是 Def.MaxHp —— 后者对分阶段 Boss 是错的(2026-08-06 M1,原注释说反了)。</summary>
        private bool BelowExecuteThreshold(EffectDef effect, int enemyIndex)
        {
            if (effect.ExecuteBelowPercent <= 0) return false;
            var enemy = _enemies[enemyIndex];
            return enemy.Alive && enemy.Hp * 100 < enemy.MaxHp * effect.ExecuteBelowPercent;
        }

        /// <summary>处决:命中阈值且非 Boss 则直接击杀,返回 true(调用方不要再走伤害)。
        /// Boss 是一条总血池,25% 也是很大一截,一刀没掉太破坏节奏,故免疫。</summary>
        private bool TryExecuteKill(EffectDef effect, int enemyIndex)
        {
            if (!effect.ExecuteKills || !BelowExecuteThreshold(effect, enemyIndex)) return false;
            var enemy = _enemies[enemyIndex];
            if (enemy.IsBoss) return false;
            int lost = enemy.Hp;              // 报实际抹掉的血量,别报 0 —— 0 会让表现层飘「-0」
            enemy.Hp = 0;
            _events.Add(new BattleEvent(BattleEventKind.Damage, enemyIndex, lost));
            ResolveDefeat(enemyIndex);
            return true;
        }

        /// <summary>残血加伤:命中阈值则该次基础值 ×2。**对 Boss 照常生效** ——
        /// 免疫的只是「直接击杀」,不是「残血加伤」。</summary>
        private int ExecuteBonus(EffectDef effect, int enemyIndex, int baseValue) =>
            !effect.ExecuteKills && BelowExecuteThreshold(effect, enemyIndex) ? baseValue * 2 : baseValue;

        /// <summary>对敌人结算一记伤害。
        ///
        /// <paramref name="crit"/> 默认 false 是刻意的(2026-08-12,E-b2):本方法有 6 个调用点,
        /// 只有出牌那两记(DamageSingle / DamageAll)该暴击,另外 4 个 —— 召唤物反击、
        /// DamagePlayerDirect 的镜反弹、DamageSummon 的荆反伤与镜反弹 —— 都不是「玩家的一次挥击」。
        /// 所以暴击判定**绝不能写进本方法内部**(那样 6 条全会暴),只能由调用点显式传进来;
        /// 默认 false 让另外 4 个调用点一个字都不用改,这本身也是恒等性的一部分。
        /// 「吃不吃暴击」不存在白名单数据结构,纯粹取决于哪些调用点传了 true ——
        /// 守它的只有 CritStatTests 里那批 FullCrit_DoesNotCrit* 负向测试。
        ///
        /// <paramref name="bypassDefense"/> 同款(2026-08-12,E-b4 T2),但**名单不同**:
        /// 点数护甲挡的是「一次挥击」,所以出牌那两记、以及召唤物出手都照常吃敌人的护甲
        /// (spec §4.2),只有 3 个**回敬**类调用点不吃 —— DamagePlayerDirect 的镜反弹、
        /// DamageSummon 的荆反伤与镜反弹。理由:它们是把已经落到我方身上的伤害原样折返,
        /// 不是我方发起的挥击,再让对方的皮厚度挡一次是错位(与 DOT 不吃护甲同一条道理)。
        /// 默认 false = 吃护甲,所以漏传的后果是「多挡了一次」而不是「静默穿透」。</summary>
        private void DamageEnemy(int enemyIndex, int baseValue,
            IReadOnlyCollection<Element> recipeElements, Element attacker,
            bool crit = false, int pierce = 0, bool bypassDefense = false)
        {
            var enemy = _enemies[enemyIndex];
            int damage = WuxingResolver.ResolveEffect(baseValue, recipeElements, attacker, enemy.Element);
            // 2026-08-12(E-b4 T3):这里原先有一整段乘法减伤 —— 承伤系数 enemy.DamageTaken、
            // 「减免遭克制失效」的补丁、穿甲的「只忽略减免 + 无条件 +15%」、破甲的「承伤 +25%」。
            // 四个乘数全部删除,守方侧从此没有任何乘数,只剩下面那一句点数减法(spec §4.1)。
            //
            // 那条「减免遭克制失效」的补丁**不需要替代品**:它当年存在是因为乘法层会按比例
            // 抽走克制的收益(100×1.5×0.5 = 75,而无甲时 100×1.5 = 150)。减法对乘法是透明的 ——
            // 基础 100 对 DEF 30 的敌人:不克制打 70,克制 ×1.5 打 120,净收益 +50,
            // **与无甲时完全相同**。打对属性的奖励天然不被护甲侵蚀(spec §4.3),
            // 守卫测试 Defense_DoesNotEatCounterBonus。
            // 暴击排在**最末**(2026-08-12,E-b2):它是「这一记最终打出去的伤害翻倍」,
            // 不是某一层的基础值加成。放最后同时把截断损失压到最小 —— 放在 ScaleByAttack 旁边
            // (生克之前)的话,相生 ×3 会把整数除丢掉的那部分放大三倍(基础 5 → 暴击 7 → ×3 = 21,
            // 而正解是 22)。
            // 走**整数除**而不是浮点乘(`damage *= 1.5f`):口径一致 —— E-b1 已裁定直接伤害
            // 这条链走整数除(ScaleByAttack),浮点晚截断只留给灼烧那条既有的浮点式子。
            // 浮点系数在这条链上出过事:EnemyState.Attack 的诅咒算式因为 1 − 0.1f = 0.89999997
            // 被 floor 拉低过 1 点(2026-08-06 M1)。暴击落在直接伤害链上,就跟直接伤害的口径。
            if (crit) damage = damage * BattleConfig.CritMultiplierPercent / 100;
            // 点数护甲(2026-08-12,E-b4 T2):**全部乘法算完之后,最后减**。
            // 结算式 = floor(基础 × 生克 × 暴击) − max(0, 护甲 − 破甲 − 穿透)。
            //
            // ⚠ 这一句的**位置**是规格,不是随手放的(spec §4.1):
            // 乘法描述「这一击有多重」,点数描述「这层皮有多厚」。放到暴击**之前**就等价于
            // 「暴击时护甲变薄」(暴击会把护甲的削减也放大 1.5 倍);放到生克之前同理 ——
            // 克制方 ×1.5 会连带放大护甲,同一件护甲对不同属性的攻击者厚度不同,无从解释。
            // 守卫测试 Order_CritBeforeDefense。⚠ 这条搬错**今天不会有任何测试变红**
            // (T2 全场护甲为 0),所以只能靠那条显式构造非 0 护甲的测试兜。
            //
            // 下钳 0 不下钳 1(裁定 10):堆甲把小怪普攻打到 0 是防御流应得的兑现,
            // 且 max(1, …) 会让穿透在残局失去意义。多段与 AOE 天然每记各减一次 ——
            // 本方法就是「一记」的粒度,每段/每目标各调一次。
            // 相克即破甲(2026-08-13 用户裁定):攻方属性克守方时,守方护甲**整层失效**。
            //
            // ⚠ 这不是上面那条「减免遭克制失效」补丁的复活 —— 那条是乘法层的代偿,修的是被按比例
            // 抽走的克制收益;点数制下克制收益本来就一点没少(守卫测试 Defense_DoesNotEatCounterBonus),
            // 所以本条是一条**额外奖励**的新规则,不是修复。是开关不是减数:护甲再厚也照样归零。
            //
            // 判定用 > 1f 而不是 == 1.5f:躲开浮点等值比较,且语义就是「这一击吃到了克制加成」。
            // 本方法是所有对敌伤害的唯一收口,所以出牌单体/AOE(每目标各调一次)、召唤物出手
            // (attacker = summon.Element)全部自动跟着走,不需要各自接线。
            //
            // 对称性备查:敌人侧今天无处落地 —— 玩家没有五行属性(DamagePlayerDirect 收的是
            // 算好的 enemy.Attack,不过 KeMultiplier),召唤物走五行但 SummonState 没有护甲字段。
            // 哪天给召唤物加了护甲,这条规则要一并在 DamageSummon 里补上。
            bool counters = WuxingResolver.KeMultiplier(attacker, enemy.Element) > 1f;
            if (!bypassDefense && !counters)
                damage = Math.Max(0, damage - EffectiveEnemyDefense(enemy, pierce));
            enemy.Hp = Math.Max(0, enemy.Hp - damage);
            _events.Add(new BattleEvent(BattleEventKind.Damage, enemyIndex, damage, crit: crit));

            enemy.HitsTaken += 1;
            RevealDisguise(enemyIndex); // 通假字:挨打也现形(2026-08-15 口径 7),先到先触发

            // 死亡先结算:EnemyDied 必须紧跟致死伤害,表现层据此判定「这记是否击杀」
            // (击杀不白闪、让位给置灰)。中间插任何事件都会打断判定 → 白闪抢色 + 置灰错拍
            if (!enemy.Alive)
            {
                ResolveDefeat(enemyIndex);
                // 立即判胜(Task 12):这一记(含反弹/反伤这类回敬)可能就是清场的最后一击,
                // 不等到下一次 BeginPlayerTurn 才收口。CheckWin() 内部会挡住「玩家同时也死了」
                // 的情形——同归于尽时玩家阵亡优先,既有口径不变。
                CheckWin();
                return;
            }

            // 生僻字:受击两次后被"读懂"(8.3);打死了就无所谓读不读得懂
            if (enemy.Def.Ability == EnemyAbility.Obscure && enemy.ApparentElement == null && enemy.HitsTaken >= 2)
            {
                enemy.ApparentElement = enemy.Element;
                _events.Add(new BattleEvent(BattleEventKind.EnemyRevealed, enemyIndex, 0));
            }
            CheckBossPhase(enemyIndex);

            // 焦痕:受击存活即自燃加攻(越磨越烫,宜速杀)
            if (enemy.Def.Ability == EnemyAbility.Scorch && !IsSilenced(enemy))
            {
                // 一回合内可能连续多次命中同一目标(玩家多张牌接力打同一敌人),SourceId 必须
                // 每次唯一,否则同回合第二次自燃会覆盖第一次而非叠加(Task 4 的 HoT 教训同型)。
                enemy.Statuses.Apply(new StatusEffect
                {
                    Kind = StatusKind.AttackBuff, Polarity = StatusPolarity.Buff,
                    Magnitude = ScorchGain, TurnsLeft = -1,
                    SourceId = $"{enemy.Def.Id}#{_statusSerial++}",
                });
                _events.Add(new BattleEvent(BattleEventKind.EnemyBuff, enemyIndex, ScorchGain));
            }

            // 叠字怪:首次受击存活 → 分裂成两个半血(8.3)。2026-08-20:克隆继承母体排位;
            // 母体那排满了就落另一排;两排都满(= 场上 6 只)才不分裂。
            if (enemy.Def.Ability == EnemyAbility.Split && !IsSilenced(enemy) && !enemy.HasSplit && _enemies.Count < EnemyCap)
            {
                var cloneRow = RowWithSpace(enemy.Row);
                int cloneColumn = FreeColumnIn(cloneRow);
                // 找不到空列则不分裂(spec §6.1)。当前理论不可达——RowWithSpace 只会返回
                // 一排未满的排,同排列号又互不相同,必有空列——但代码得照 spec 说的话讲,
                // 不能靠"反正走不到"当隐性前提(2026-08-22)。
                if (cloneColumn >= 0)
                {
                    int half = (enemy.Hp + 1) / 2;
                    enemy.Hp = half;
                    enemy.HasSplit = true;
                    var clone = new EnemyState(enemy.Def)
                    {
                        Hp = half,
                        BaseAttack = enemy.Attack, // 一次性快照,不是活的引用——分裂出的怪不继承驱散来源
                        HasSplit = true,
                        Row = cloneRow,
                        Column = cloneColumn,
                    };
                    _enemies.Add(clone);
                    _events.Add(new BattleEvent(BattleEventKind.EnemySplit, enemyIndex, half));
                }
            }
        }

        /// <summary>优先返回 preferred 排(未满时),否则另一排。调用方已保证场上未满 EnemyCap,
        /// 所以必有一排有空位。</summary>
        private EnemyRow RowWithSpace(EnemyRow preferred)
        {
            int count = 0;
            foreach (var e in _enemies)
                if (e.Row == preferred) count++;
            return count < EnemyRowCap ? preferred : (preferred == EnemyRow.Front ? EnemyRow.Back : EnemyRow.Front);
        }

        /// <summary>该排里没被占用的最小列号(2026-08-22)。分裂出的克隆用它落位。
        /// 全占满返回 −1 —— 分裂本就被「场上敌人 < 4」的守卫挡在前面,
        /// 但守卫在别处,这里仍要能表达「没地方站」而不是撞号叠在别人身上。</summary>
        private int FreeColumnIn(EnemyRow row)
        {
            for (int col = 0; col < EnemyRowCap; col++)
            {
                bool taken = false;
                foreach (var e in _enemies)
                    if (e.Row == row && e.Column == col) { taken = true; break; }
                if (!taken) return col;
            }
            return -1;
        }

        private void ResolveDefeat(int enemyIndex)
        {
            _events.Add(new BattleEvent(BattleEventKind.EnemyDied, enemyIndex, 0));
        }

        /// <summary>命中判定(2026-08-07):命中率 = 100 − 攻击者致盲 − 目标闪避,钳到 [0,100]。
        ///
        /// **钳的是最终命中率,不是单项**(2026-08-08 订正:原注释这里的推理是反的)。
        /// 按当前的比较式 `_random.Next(100) < hitRate`,`Next(100)` 只吐 [0,99],负的 hitRate
        /// 本就恒为 false = 必空,不钳也不会变成必中,和钳到 0 逐位相同 —— 钳位不是为了修正
        /// 这一条既有比较式的行为,是**防御性**的:防日后有人把比较式改成 `<=`、或改成
        /// `_random.Next(hitRate)` 这类写法时,负数传进去直接炸异常或产生意外行为。
        ///
        /// **两端都短路,一次随机都不摇**(下端 ≤0 是 2026-08-12 E-b4 T4 补的,与 <see cref="RollCrit"/> 对称)。
        /// 命中率 ≥ 100 那一端是恒等性硬线:_random 的既有消费方只有 StartTurn 的回合掉字、
        /// 本方法、EnemyState 构造时的 Boss 阈值浮动,无条件摇会平移掉落序列,
        /// 让所有依赖种子的既有测试全红。玩家闪避默认 0、既有战斗里也没有致盲,
        /// 于是走的都是这条短路,行为逐位不变。
        /// 命中率 ≤ 0 那一端同理:必空时摇不摇结果都一样,不摇能让「闪避叠满」这条玩法路径
        /// 同样不扰动随机流,也让测试可以在不注入 RNG 的前提下断言必空。
        ///
        /// ⚠ **玩家打敌人不走这里**(用户拍板的不对称口径):玩家攻击永远必中,敌人没有闪避。
        /// 本方法只服务「敌人打玩家」(<see cref="DamagePlayerDirect"/>)与「敌人打召唤物」两条链。</summary>
        private bool AttackHits(int enemyIndex, int dodgePercent)
        {
            int blind = _enemies[enemyIndex].Statuses.TotalMagnitude(StatusKind.Blind);
            int hitRate = Math.Clamp(100 - blind - dodgePercent, 0, 100);
            if (hitRate >= 100) return true;
            if (hitRate <= 0) return false;
            return _random.Next(100) < hitRate;
        }

        /// <summary>对玩家造成伤害:护盾先吸收(普通桶先扣,豁免桶垫后)。
        /// 大招走这条 = 不经召唤物顶前排(spec 3.3 总则)。
        ///
        /// 返回值(2026-08-08):这次攻击有没有「落到身上」——只有 AttackHits 判定打空才是
        /// false;免疫挡下算 true。反直觉但刻意:免疫挡的是「伤害」,不是「攻击是否发生」——
        /// 攻击确实命中了,只是伤害被完全吸收。灯花(Sear)之类「出手就触发」的攻击附带效果
        /// 靠这个返回值 gate(见攻击循环):打空 = 攻击没发生,附带效果不该触发;
        /// 免疫挡下 = 攻击发生了,附带效果照常。</summary>
        private bool DamagePlayerDirect(int enemyIndex, int damage)
        {
            // 命中判定(2026-08-07):打空则什么都不发生 —— 免疫不消耗、护盾不掉、反弹不触发。
            // 命中率 = 100 − 攻击者致盲 − 玩家闪避(2026-08-12,E-b4 T4:闪避从写死的 0 接进来)。
            // 闪避 0(1 级角色的缺省)时命中率 100,AttackHits 直接短路不摇随机数。
            if (!AttackHits(enemyIndex, EffectiveDodge))
            {
                _events.Add(new BattleEvent(BattleEventKind.Missed, enemyIndex, 0));
                return false;
            }

            // 点数护甲(2026-08-12,E-b4 T2):**先减护甲,再走免疫 / 护盾 / 血量**(spec §4.1)。
            // 位置口径:护甲决定这一记有多少真正落到身上,护盾是把落下来的那部分吃掉的资源 ——
            // 两者是「厚度」与「缓冲」的关系,顺序反过来会让护盾替护甲挡掉本就不该进来的伤害。
            // 免疫排在护甲之后不特判「已经减到 0」:免疫挡的是这一记攻击,不是这一记的数值。
            // 下钳 0(裁定 10),下方的反弹因此按**实际打过来的伤害**折返,与「护盾吸掉的也照样反」
            // 同口径 —— 反的是落到我身上的量,不是敌人名义上的攻击力。
            damage = Math.Max(0, damage - EffectivePlayerDefense);

            // 免疫(2026-08-06):先于护盾消耗 —— 免疫是稀缺的一次性资源,让它去挡一记小伤
            // 而把护盾留着更亏;玩家的预期是「免疫牌打出去,下一记不管多重都不疼」。
            // 完全挡下,不是减免。召唤物承伤走 DamageSummon,不经这里,所以免疫只保护玩家。
            if (ConsumeImmunity())
            {
                _events.Add(new BattleEvent(BattleEventKind.ImmunityBlocked, enemyIndex, damage));
                return true;
            }

            int fromNormal = Math.Min(_shieldNormal, damage);
            _shieldNormal -= fromNormal;
            int fromPersist = Math.Min(_shieldPersist, damage - fromNormal);
            _shieldPersist -= fromPersist;
            int absorbed = fromNormal + fromPersist;
            PlayerHp = Math.Max(0, PlayerHp - (damage - absorbed));
            _events.Add(new BattleEvent(BattleEventKind.EnemyAttack, enemyIndex, damage, -1, absorbed));

            // 立即判负(Task 12,spec §4.3.1):归零即当场收口,不推迟到下一次 BeginPlayerTurn ——
            // 逐格驱动下,推迟意味着已经死了还要陪剩下的怪把动画读完才弹结算。
            if (PlayerHp <= 0) Phase = BattlePhase.Lost;

            // 反弹(2026-08-07,镜):按**打过来的总伤害**照回去,不是按实际掉血 ——
            // 护盾吸掉的那部分也照样反。「镜」是把东西原样反射,不管你挡没挡住,
            // 与召唤物 荆 的反伤同口径(被打死的那一击也照样扎)。
            // 命中判定打空与免疫完全挡下都在方法更早处 return 了,走不到这里 —— 没吃到就没得反。
            // attacker 传 Element.Heart:心对全属性都是 1.0x,等价于「不走生克」。
            // 刻意不钳位(评审 Minor 2,2026-08-08):眼下字表只有「映」一个 Reflect 字,同字
            // 再放走 SourceId 去重只刷新,多来源叠加现实不可达。日后加第二张反弹字之前,
            // 先想清楚上限——两张 60% 同在身会反弹 120%,比挨的还多。
            int reflect = _playerStatuses.TotalMagnitude(StatusKind.Reflect);
            if (reflect > 0 && _enemies[enemyIndex].Alive)
            {
                int bounced = damage * reflect / 100;
                if (bounced > 0)
                    DamageEnemy(enemyIndex, bounced, Array.Empty<Element>(), Element.Heart,
                        bypassDefense: true); // 反弹不吃敌人护甲(spec §4.2):折返不是挥击
            }
            return true;
        }

        /// <summary>消耗一层免疫;成功返回 true。袋子里可能同时有多条(不同字来源可叠),
        /// 所以从第一条非零的扣 1,扣到 0 就移除那一条,而不是按 Kind 一把清。</summary>
        private bool ConsumeImmunity()
        {
            var all = _playerStatuses.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].Kind != StatusKind.Immunity || all[i].Magnitude <= 0) continue;
                all[i].Magnitude -= 1;
                if (all[i].Magnitude <= 0) _playerStatuses.RemoveEntry(all[i]);
                return true;
            }
            return false;
        }

        /// <summary>对召唤物造成伤害:走五行(与普攻打召唤同规则),护盾先吸收(2026-08-05)。
        /// SummonHit 的 Amount 仍报吃到的总伤害,吸收量走第 5 个参数 —— 与 DamagePlayerDirect
        /// 发 EnemyAttack 的口径一致,表现层才能一套逻辑画两边。
        ///
        /// 返回值口径同 DamagePlayerDirect(2026-08-08):打空为 false,其余(含护盾吸收)为 true。</summary>
        private bool DamageSummon(int enemyIndex, int summonIndex, int damage, Element attacker)
        {
            var summon = _summons[summonIndex];
            // 命中判定(2026-08-07):召唤物的闪避与攻击者的致盲一起从命中率里扣
            if (!AttackHits(enemyIndex, summon.Passive?.Dodge ?? 0))
            {
                _events.Add(new BattleEvent(BattleEventKind.Missed, enemyIndex, 0, summonIndex));
                return false;
            }

            int taken = WuxingResolver.ResolveEffect(damage, Array.Empty<Element>(), attacker, summon.Element);
            int absorbed = Math.Min(summon.Shield, taken);
            summon.Shield -= absorbed;
            summon.Hp = Math.Max(0, summon.Hp - (taken - absorbed));
            _events.Add(new BattleEvent(BattleEventKind.SummonHit, enemyIndex, taken, summonIndex, absorbed));

            // 反伤(2026-08-05,荆):固定值、不走生克(与 Bleed 同口径,可预期)。
            // 荆棘扎人不看自己死没死 —— 被打死的那一击照样反弹。
            // attacker 传 Element.Heart:心对全属性都是 1.0x,等价于"不走生克"。
            int thorns = summon.Passive?.Thorns ?? 0;
            if (thorns > 0 && _enemies[enemyIndex].Alive)
                DamageEnemy(enemyIndex, thorns, Array.Empty<Element>(), Element.Heart,
                    bypassDefense: true); // 荆的反伤不吃敌人护甲(spec §4.2),与不走生克同一条口径

            // 反弹(2026-08-08,修复波 Important:镜 × 召唤物顶前排):用户裁定——挡在前排的
            // 伤害同样算「打到了我方」,DamagePlayerDirect 末尾那段反弹不该只管玩家直接挨打
            // 的那一路,召唤物顶着承伤时玩家身上的反弹也要结算,否则「柳(闪避召唤)+ 镜」
            // 这类组合会与全部召唤字互斥,花 1 AP 一张蓝卡零收益。
            // 结算点排在荆的反伤**之后**,基数用 taken(过完生克、护盾吸收之前的那个值)——
            // 召唤物承伤本来就走五行(上面 ResolveEffect 那句),所以「总伤害」在这一侧
            // 就是 taken,与玩家侧 DamagePlayerDirect 用 damage(护盾吸收之前)同口径:
            // 「按打过来的总伤害反,护盾吸掉的也反」。
            // _enemies[enemyIndex].Alive 守卫必须有:荆的反伤可能先把敌人打死,此时不能
            // 再反,否则会对死尸补刀,走进 DamageEnemy 触发第二次 ResolveDefeat,发出重复的
            // EnemyDied 事件(与 Reflect_DoesNotDuplicateDeathWhenBossDiesToThornsBeforePierceLands
            // 那条守的是同一类问题)。bounced > 0 守卫同样必须有:0 伤反弹会推进
            // enemy.HitsTaken,白送生僻字现形 / 焦痕加攻 / 叠字分裂(与玩家侧同一条注释解释过)。
            // attacker 传 Element.Heart:心对全属性都是 1.0x,等价于「不走生克」,与玩家侧一致。
            int reflect = _playerStatuses.TotalMagnitude(StatusKind.Reflect);
            if (reflect > 0 && _enemies[enemyIndex].Alive)
            {
                int bounced = taken * reflect / 100;
                if (bounced > 0)
                    DamageEnemy(enemyIndex, bounced, Array.Empty<Element>(), Element.Heart,
                        bypassDefense: true); // 同玩家侧:反弹不吃敌人护甲(spec §4.2)
            }
            return true;
        }

        /// <summary>Boss 回合三态(spec 2026-07-28):释放 / 蓄力 / 交回普攻。
        /// 返回 true = 本回合已处理,调用方跳过普通攻击。</summary>
        private bool ResolveBossTurn(int index, EnemyState enemy)
        {
            // 沉默(2026-08-07):锁住的是「正在攒的那一下」——蓄力当场取消、计数清零,
            // 解锁后从头攒,而不是解锁即放
            if (IsSilenced(enemy))
            {
                enemy.IsCharging = false;
                enemy.ChargeCounter = 0;
                return false; // 交回普攻
            }

            if (enemy.IsCharging)
            {
                enemy.IsCharging = false;
                enemy.ChargeCounter = 0;
                CastBossSkill(index, enemy, enemy.ChargingSkill);
                return true;
            }

            enemy.ChargeCounter += 1;

            var skill = enemy.Def.Phases[enemy.PhaseIndex].Skill;
            if (skill == BossSkill.None || skill == BossSkill.Bulwark)
                return false; // 坚壁/无技能阶段没大招可放,但照常攒数:
                              // 冻结的话,最耗回合的坚壁段(承伤 0.5)会把节奏整个吃掉
            if (enemy.ChargeCounter < _config.BossChargeEvery)
                return false;

            enemy.IsCharging = true;
            enemy.ChargingSkill = skill; // 锁定:预告什么就放什么,期间换阶也不改写
            _events.Add(new BattleEvent(BattleEventKind.BossCharging, index, (int)skill));
            return true; // 蓄力回合不出手
        }

        /// <summary>释放当前阶段字的技能。先发 BossSkillCast 再发各目标受击事件,
        /// 表现层据此把大招动效与后续伤害分开播。
        /// 玩家份伤害统一 Attack×2(2026-07-29 修正,Devour 空放除外):三个敌方回合一轮里
        /// 1 普攻 + 1 蓄力不出手 + 1 释放,若玩家份只按 Attack 结算,总投放只有 2×Attack,
        /// 反而低于没有技能的纯普攻 Boss(3×Attack)——技能变成了减伤。抬到 ×2 后释放回合
        /// 单独顶两个普攻的量,三回合投放追平无技能 Boss(2026-07-30 修正:原注释误记成
        /// 「四个敌方回合里 2 普攻…」,方向刚好相反,实际节拍是 3 回合一轮,见 Finding 1)。</summary>
        private void CastBossSkill(int index, EnemyState enemy, BossSkill skill)
        {
            _events.Add(new BattleEvent(BattleEventKind.BossSkillCast, index, (int)skill));

            switch (skill)
            {
                case BossSkill.Deluge: // 淹没:玩家挨双倍,召唤物各挨一下(不翻倍,仍是分摊主力)
                    // 2026-08-12(E-b4 T3):不再套乘法减伤 —— 玩家份的点数护甲在
                    // DamagePlayerDirect 里减,召唤物份**不减**(召唤物没有护甲,也不借玩家的,spec §4.2)
                    DamagePlayerDirect(index, enemy.Attack * 2);
                    for (int s = 0; s < SummonCap; s++)
                        if (_summons[s] != null && _summons[s].Alive)
                            DamageSummon(index, s, enemy.Attack, enemy.Element);
                    break;

                case BossSkill.Impale: // 洞穿:一击穿过前排,同时打中后面的玩家(本就是 ×2)
                {
                    int front = Targeting.FrontmostSummon(_summons, FrontRowSize);
                    if (front >= 0)
                        DamageSummon(index, front, enemy.Attack, enemy.Element);
                    DamagePlayerDirect(index, enemy.Attack * 2);
                    break;
                }

                case BossSkill.Topple: // 倾覆:先按常规吸伤(玩家挨双倍),再把剩余护盾整个掀掉
                {
                    DamagePlayerDirect(index, enemy.Attack * 2);
                    int broken = _shieldNormal + _shieldPersist;
                    if (broken > 0)
                    {
                        _shieldNormal = 0;
                        _shieldPersist = 0;
                        _events.Add(new BattleEvent(BattleEventKind.ShieldBroken, -1, broken));
                    }
                    // TurnsLeft = 2(2026-08-06 定的值)。Seal 由敌人的攻击动作在敌方段挂上,
                    // 玩家侧状态递减(TickPlayerStatuses)紧跟在 BeginPlayerTurn 的 SettlePlayerHots
                    // 之后、StartTurn 之前(2026-08-16 全分支终审 Important 1 订正:曾经错放在
                    // 上一拍 YieldTurn 里,导致本条要多续一轮,已修正)——挂上后紧接着的
                    // BeginPlayerTurn 就会把它减到 1、StartTurn 读到仍非零而扣 1 点 AP,
                    // 下一次 BeginPlayerTurn 再减到 0 移除,AP 罚满整整一个玩家回合。
                    _playerStatuses.Apply(new StatusEffect
                    {
                        Kind = StatusKind.Seal, Polarity = StatusPolarity.Debuff,
                        Magnitude = 1, TurnsLeft = 2, SourceId = "倾覆",
                    });
                    break;
                }

                case BossSkill.Devour: // 吞噬:无视血量必杀最前一只(不回血);没得吞就普攻(设计明确不 ×2,唯一例外)
                {
                    int front = Targeting.FrontmostSummon(_summons, FrontRowSize);
                    if (front >= 0)
                    {
                        var victim = _summons[front];
                        int lost = victim.Hp;
                        victim.Hp = 0;
                        _events.Add(new BattleEvent(BattleEventKind.SummonHit, index, lost, front));
                    }
                    else
                    {
                        // 没得吞退化成普攻:与普攻同口径过点数护甲;秒杀分支本身无数值可减,不动
                        DamagePlayerDirect(index, enemy.Attack);
                    }
                    break;
                }
            }
        }

        /// <summary>Boss 血池换阶(8.5 v0.7):跨过阈值即切阶段(一击可连跨多阶),血量连续不重置。</summary>
        private void CheckBossPhase(int enemyIndex)
        {
            var enemy = _enemies[enemyIndex];
            if (!enemy.IsBoss) return;
            while (enemy.PhaseIndex < enemy.Def.Phases.Count - 1 && enemy.Hp <= enemy.PhaseBounds[enemy.PhaseIndex])
            {
                enemy.ApplyPhaseStats(enemy.PhaseIndex + 1);
                _events.Add(new BattleEvent(BattleEventKind.BossPhase, enemyIndex, enemy.PhaseIndex));
            }
        }

        private void CheckWin()
        {
            // 同归于尽玩家阵亡优先(既有口径,不变,2026-08-16 补充):Lost 已经落定就不再
            // 被清场反弹/反伤这类回敬链路翻成 Won —— 这条守卫是 DamageEnemy 死亡分支新补的
            // CheckWin() 调用点专用(Task 12),其余既有调用点本就排在 Lost 早退之后,不受影响。
            if (Phase == BattlePhase.Lost) return;
            foreach (var enemy in _enemies)
                if (enemy.Alive)
                    return;
            Phase = BattlePhase.Won;
        }
    }
}
