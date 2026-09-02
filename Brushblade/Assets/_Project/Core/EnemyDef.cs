using System;
using System.Collections.Generic;

namespace Brushblade.Core
{
    /// <summary>字怪特殊能力(第 8 章 8.3):骚扰拆合/压迫机制。</summary>
    public enum EnemyAbility
    {
        None,
        Regrow, // 缺笔妖:每敌方回合自补全(攻+2/回3血),第 3 次补全完成(攻×2、血回满)
        Split,  // 叠字怪:首次受击存活后分裂成两个半血(场上敌人 <4 时)
        Buff,   // 标点小妖:有同伴时每回合给其他存活字怪攻击 +50%(本场累计不回滚,
                // 优先级目标);场上只剩自己时改为亲自攻击(2026-07-22)
        Disguise, // 通假字:真身与伪装每次遭遇现摇(必不相同),首次行动后现形(信息隐藏)
        Obscure,  // 生僻字:属性隐藏("?"),受击两次后被"读懂"
        Scorch,   // 焦痕:每次被击中且存活,攻 +50%(基础攻 4 时即 +2,越磨越烫,宜速杀)
        Sear,     // 灯花:每次攻击给玩家挂 1 层灼烧(2026-08-06;玩家侧减益的来源之一)
        Mend,     // 涂改:每个敌方回合给**伤得最重的一只同伴**回血(回复量 = 自身攻击力),自己不回;
                  // 没有受伤的同伴时改为亲自出手(与 Buff 的落单口径一致,2026-08-29)。
                  // 敌方第一个治疗单位 —— 它躲在后排,逼玩家想办法够到后排(贯穿/连发/远程召唤物)
        Barb,     // 铁画:受击存活时把**实际打进身体的伤害**按百分比反噬给玩家(2026-08-29)。
                  // 只有我方主动挥击会触发,回敬类的伤害(镜反弹 / 荆反伤)不触发 ——
                  // 否则「镜 × 铁画」会互相激发成一条来回衰减的长链,玩家还看不懂账
    }

    /// <summary>站位(2026-08-20)。敌我各 3 前 3 后。前排未清空时,单体直接伤害够不到后排。
    /// 排位只决定**能不能被够到**,不决定这只单位自己能不能出手——后排照常攻击。</summary>
    public enum EnemyRow { Front, Back }

    /// <summary>攻击距离(2026-08-20)。Ranged 无视对方前排。
    /// 与 <see cref="EnemyAbility"/> 正交:做成 Ability 的取值会与灯花/焦痕互斥,
    /// 而「远程的灯花」是完全合理的组合。</summary>
    public enum AttackRange { Melee, Ranged }

    /// <summary>够得着玩家时打谁(2026-08-20)。
    /// Default = 在「对方存活后排 ∪ 玩家」里均匀随机;Player = 死盯玩家。</summary>
    public enum AttackFocus { Default, Player }

    /// <summary>Boss 阶段技能(spec 2026-07-28):蓄力一回合后释放。
    /// Bulwark 为被动标签,行为与 None 相同(靠 <see cref="BossPhaseDef.Defense"/> 的高护甲吃伤),
    /// 分开只为可读性——Bulwark = 设计上就该是肉墙,None = 这字还没配技能。</summary>
    public enum BossSkill
    {
        None,
        Deluge, // 淹没:玩家 + 全部召唤物各挨一下(群攻)
        Impale, // 洞穿:最前召唤物挨一下 + 玩家挨双倍(穿透)
                // 2026-08-22 从 Pierce 改名 —— 「贯穿」这个中文名让给了 TargetShape.Skewer,
                // 而代码名 Pierce 同时还是 EffectDef.Pierce(护甲穿透点数),一名三用读不清
        Topple, // 倾覆:伤害 + 清空护盾 + 下回合 AP −1(剥夺)
        Devour, // 吞噬:消灭最前召唤物(不回血);无召唤物则普攻玩家
        Bulwark, // 坚壁:被动高护甲,该阶段不蓄力
    }

    /// <summary>成语 Boss 的单个阶段(8.5:四字成语,四个字 = 四个阶段)。</summary>
    public sealed class BossPhaseDef
    {
        public string Char { get; }
        public Element Element { get; }
        public int MaxHp { get; }
        public int Attack { get; }
        /// <summary>该阶段的护甲**点数**(2026-08-12,E-b4)。每记挥击减这么多,下钳 0。
        /// T3 起它是**守方唯一的一层**:承伤系数(乘法减伤)已删除,守方侧没有任何乘数。
        /// 坚壁「山」= 60、翻江倒海「江」/ 雷霆万钧「钧」= 30。</summary>
        public int Defense { get; }
        /// <summary>该阶段的蓄力技能(spec 2026-07-28);由字表决定,None = 纯普攻。</summary>
        public BossSkill Skill { get; }

        public BossPhaseDef(string phaseChar, Element element, int maxHp, int attack,
            BossSkill skill = BossSkill.None, int defense = 0)
        {
            Char = phaseChar;
            Element = element;
            MaxHp = maxHp;
            Attack = attack;
            Skill = skill;
            Defense = defense;
        }
    }

    /// <summary>字怪定义(第 8 章)。Phases 非空即成语 Boss,首阶段覆盖基础数值。</summary>
    public sealed class EnemyDef
    {
        public string Id { get; }
        public Element Element { get; }
        public int MaxHp { get; }
        public int Attack { get; }
        public EnemyAbility Ability { get; }
        public IReadOnlyList<BossPhaseDef> Phases { get; }

        /// <summary>护甲**点数**(2026-08-12,E-b4):小怪级。Boss 走阶段级
        /// <see cref="BossPhaseDef.Defense"/>。全 13 只小怪里只有墨渍带甲(20)——
        /// 「带甲怪不成群」是 AOE 保护的配置口径,守卫测试 RealConfig_ArmoredEnemiesAreRare。</summary>
        public int Defense { get; }

        /// <summary>配置的基础速度(2026-08-17,spec §5.8)。`0` = 未配置,由
        /// <see cref="EnemyState"/> 回落到基准 100 —— 眼下**全部字怪都走回落**,
        /// `enemies.json` 里没有 speed 字段,本次也不加。
        ///
        /// 打开这个通道是为了让速度能在**构造之前**定下来:自 2026-08-17 起
        /// `BattleEngine` 构造函数就会跑开场推进,而「先构造、后改 Enemies[0].Speed」
        /// 的写法会让敌人以默认 100 参与开场那一拍(spec §5.8 的实例)。
        ///
        /// ⚠ 这条通道眼下只接了一半:`Data/ConfigLoader.cs` 的 `EnemyDto` 没有 speed 字段,
        /// 传给 `EnemyDef` 构造函数时也没传 speed(约 `:390`)。以后谁在 `enemies.json` 里写
        /// `"speed": 200` 会被**静默忽略**——接 JSON 还要同时改 `EnemyDto` + 那处构造调用。
        ///
        /// ⚠ 给敌人真正配上差异化速度(即本字段不再全 0)之前,还有两条限制要一起解决
        /// (2026-08-18,详见 BattleView.OpeningRoutine 的文档注释):
        ///   1. 开场回放不接 Boss 蓄力播报——`OpeningRoutine` 没调 `AppendBossSkillMessage`
        ///      (它读的是 `Battle.LastEvents` 而非逐拍的 `step.Events`)。当前配速下 Boss
        ///      不可能在开场蓄力,所以无影响;敌人一旦配速就要补。
        ///   2. 开场中途死掉的召唤物在回放里连头像格都不画——`DrawSummons` 靠 `_summonAnimHp`
        ///      决定画不画,而 `SnapshotPreHp` 只在开场结束后跑一次、只登记 `Alive` 为真的
        ///      召唤物(敌人侧有 `_dyingEnemies` 兜底,召唤物没有)。当前不可达——开场期间
        ///      没有任何非玩家单位能伤害召唤物;敌人一旦配速就变可达,要同时修。</summary>
        public int Speed { get; }

        /// <summary>站位偏好(2026-08-20,缺省前排)。实际站位由 <see cref="EnemyState.Row"/> 决定
        /// ——每排上限 3,偏好排满了会被改判到另一排。</summary>
        public EnemyRow Row { get; }
        public AttackRange Range { get; }
        public AttackFocus Focus { get; }

        /// <summary>占几列(2026-08-30)。默认 1 = 占一格,与本字段引入之前逐字节等价。
        /// 三只 Boss 配 4(占满整排);将来想让护法站在 Boss 旁边就把 Boss 配成 2 或 3。
        ///
        /// <see cref="EnemyState.Column"/> 的语义因此收紧为「**起始列(左端)**」,
        /// 实际占据 [Column, Column + ColumnSpan)。Skewer/Cleave/Chain 三处裁定
        /// 全部按区间算(Targeting),Span = 1 时与旧写法逐字节相同。
        ///
        /// **不进快照**:由 Def 推得,与 <see cref="EnemyState.Defense"/> 做成计算属性
        /// 同一条理由(见那条注释里「零新增快照字段」的推理)。</summary>
        public int ColumnSpan { get; }

        /// <summary>最早出现层(2026-09-02);0 = 不限。
        ///
        /// 低阶护甲怪配 6:前 5 层是新手教学区,带甲怪会让还没有破甲手段的玩家直接卡死,
        /// 而层段(band)整段共用一个 enemyPool,表达不了段内的深度差。</summary>
        public int MinDepth { get; }

        public EnemyDef(string id, Element element, int maxHp, int attack,
            EnemyAbility ability = EnemyAbility.None, IReadOnlyList<BossPhaseDef> phases = null,
            int defense = 0, int speed = 0,
            EnemyRow row = EnemyRow.Front, AttackRange range = AttackRange.Melee,
            AttackFocus focus = AttackFocus.Default, int columnSpan = 1, int minDepth = 0)
        {
            Id = id;
            Element = element;
            MaxHp = maxHp;
            Attack = attack;
            Ability = ability;
            Phases = phases ?? System.Array.Empty<BossPhaseDef>();
            Defense = defense;
            Speed = speed;
            Row = row;
            Range = range;
            Focus = focus;
            ColumnSpan = columnSpan < 1 ? 1 : columnSpan;
            MinDepth = minDepth;
        }
    }

    /// <summary>玩家侧召唤物(木系,2026-07-19 拍板):顶前排替玩家承伤,回合末反击。</summary>
    public sealed class SummonState
    {
        public string Char { get; }
        public Element Element { get; }
        public int Hp { get; internal set; }
        public int MaxHp { get; }
        public int Attack { get; }
        public bool Alive => Hp > 0;

        /// <summary>基础速度(2026-08-04)。默认 100 = 每回合恰好一次,与旧的"固定反击一次"等价;
        /// 带被动的取被动值(桤 150)。两个构造函数之外无赋值点,收成只读(2026-08-06 M7)。</summary>
        public int Speed { get; }

        /// <summary>行动计量器:回合末累积速度,每满 100 行动一次(与敌人同走一套模型)。</summary>
        public int ActionMeter { get; internal set; }

        /// <summary>召唤物护盾(2026-08-05,桂):一次性额外血条,先于血量吸伤,吸完即无、
        /// 不刷新、不随回合清空。</summary>
        public int Shield { get; internal set; }

        /// <summary>被动(2026-08-05)。null = 无被动。</summary>
        public SummonPassive Passive { get; }

        /// <summary>召唤物身上的状态容器(2026-08-26)。与 <see cref="EnemyState.Statuses"/> 同型。
        ///
        /// 目前唯一的来源是灯花(Sear)—— 它打谁就烧谁,此前无论打到谁都只烧玩家。
        /// 结算与递减在 <c>BattleEngine.ActSummonTurn</c>:先结算自身灼烧,出手之后再递减回合数,
        /// 与 <c>ActEnemyTurn</c> 的六步同构。</summary>
        public StatusBag Statuses { get; } = new();

        /// <summary>这一拍的有效攻击力(2026-08-28,增益改单体)= 基础攻击 + 自己袋子里的攻击增益。
        ///
        /// 与玩家侧 <c>BattleEngine.EffectiveAttack</c> 同形,但刻意**不含战意那个乘区**:
        /// 战意是玩家专属(连续出字的节奏奖励,召唤物不由玩家逐张出字驱动),用户 2026-08-28
        /// 明确留在玩家侧。点数直接照搬、不按血量比缩放,也是同一次拍板。
        /// 钳到 ≥0:负攻击力会打出负伤害 = 给敌人回血,且全程无声。
        ///
        /// 长在这里而不是 BattleEngine 里:详情弹窗(Presentation.SummonInfo)要显示同一个数,
        /// 而表现层不该自己再推一遍规则 —— 那正是两处口径分叉的起点。</summary>
        public int EffectiveAttack => System.Math.Max(0,
            Attack + Statuses.TotalMagnitude(StatusKind.AttackBuff));

        /// <summary>挨一记时的有效护甲(点数,2026-08-28)。召唤物**没有基础护甲**(没这个字段,
        /// 被动也不给),所以完全来自玩家挂上去的增益 —— 无 buff 时恒 0、减法退化成不减。
        /// 破甲一并读进来:眼下没有「敌人破召唤物甲」的通道,但口径与玩家侧
        /// <c>EffectivePlayerDefense</c> 对齐,将来配了那种敌人不用再改。</summary>
        public int EffectiveDefense => System.Math.Max(0,
            Statuses.TotalMagnitude(StatusKind.DefenseBuff)
            - Statuses.TotalMagnitude(StatusKind.ArmorBreak));

        public SummonState(string summonChar, Element element, int hp, int attack,
            SummonPassive passive = null)
        {
            Char = summonChar;
            Element = element;
            Hp = hp;
            MaxHp = hp;
            Attack = attack;
            Passive = passive;
            Speed = EffectiveSpeed(passive?.Speed ?? 0);
        }

        /// <summary>断点存档:MaxHp 与 Hp 会脱钩(挨过打),故分开存。</summary>
        private SummonState(string summonChar, Element element, int hp, int maxHp, int attack,
            int actionMeter, int speed, int shield, SummonPassive passive)
        {
            Char = summonChar;
            Element = element;
            Hp = hp;
            MaxHp = maxHp;
            Attack = attack;
            ActionMeter = actionMeter;
            Shield = shield;
            Passive = passive;
            Speed = EffectiveSpeed(speed);
        }

        /// <summary>速度兜底:0 或负数一律回 100。子项目 0 加 Speed 时漏了存档接线,
        /// 老存档没有这个字段 → Newtonsoft 填 0 → 召唤物永远攒不满计量器,一辈子不出手。</summary>
        private static int EffectiveSpeed(int speed) => speed > 0 ? speed : 100;

        /// <summary>槽位由持有者传入 —— SummonState 自己不知道它站在哪一格
        /// (槽位是 BattleEngine._summons 的数组下标,不是这只召唤物的属性,
        /// 存成字段会有与下标失配的风险)。</summary>
        public SummonSnapshot Capture(int slot)
        {
            // Statuses 深拷贝:条目是引用对象,浅拷会让快照与实体共享同一条状态
            // (与 EnemyState.Capture 同一条 2026-08-04 的教训)
            var statuses = new List<StatusEffect>();
            foreach (var s in Statuses.All) statuses.Add(s.Clone());
            return new SummonSnapshot
            {
                Slot = slot,
                Char = Char, Element = Element, Hp = Hp, MaxHp = MaxHp, Attack = Attack,
                ActionMeter = ActionMeter, Speed = Speed, Shield = Shield,
                Passive = Passive?.Clone(), Statuses = statuses,
            };
        }

        internal static SummonState Restore(SummonSnapshot s)
        {
            var state = new SummonState(s.Char, s.Element, s.Hp, s.MaxHp, s.Attack, s.ActionMeter,
                s.Speed, s.Shield, s.Passive?.Clone());
            state.Statuses.CopyFrom(s.Statuses ?? new List<StatusEffect>());
            return state;
        }
    }

    /// <summary>战斗中的字怪状态。成语 Boss 为一条总血池,按血量阈值切换阶段
    /// (2026-07-19 拍板:阈值带种子浮动,同一 Boss 每次体验不同;原独立血量四连战废止)。</summary>
    public sealed class EnemyState
    {
        public EnemyDef Def { get; }
        public int Hp { get; internal set; }
        public int MaxHp { get; internal set; }          // 当前阶段上限
        public Element Element { get; internal set; }    // 当前属性(Boss 换阶段会变)

        /// <summary>敌人身上的状态容器(2026-08-04:统一状态容器迁移),现装五种:
        /// Burn/Bleed/Freeze/SpeedModifier 四个减益 + AttackBuff 一个增益(标点小妖加攻/焦痕自燃)。
        /// Burn 用 TurnsLeft = -1(段内持久),靠灼烧结算段自减 Magnitude;
        /// Bleed/Freeze/SpeedModifier 用 TurnsLeft 正常回合递减。</summary>
        public StatusBag Statuses { get; } = new();

        /// <summary>敌人护盾(2026-08-30):一次性额外血条,在护甲减法**之后**、扣血**之前**吸收;
        /// 吸完即无、不刷新、不随回合清空。与 <see cref="SummonState.Shield"/> 同型。
        ///
        /// ⚠ **眼下没有来源**(用户 2026-08-30 拍板):enemies.json 不配、也没有结盾技能。
        /// 将来的来源是「加盾辅助怪给同伴挂 buff」。所以真机上它恒为 0,
        /// 吸收逻辑只有 EnemyShieldTests 看得见 —— 改这一块别指望试玩能发现问题。
        ///
        /// **进快照**:与 <see cref="Hp"/> 同类,是战中可变状态。这一条与
        /// <see cref="Defense"/> 那条「零新增快照字段」的推理不冲突 ——
        /// 那条说的是**不可变**的基础属性做成计算属性,而护盾是会被打掉的。</summary>
        public int Shield { get; internal set; }

        /// <summary>基础速度(2026-08-04)。有效速度 = Speed + 所有 SpeedModifier 之和,下限 0。
        /// 基数用本字段而非常量 100:将来若有天生快/慢的字怪,写死 100 会让它们的修正算错。</summary>
        public int Speed { get; set; } = 100;

        /// <summary>实际站位(2026-08-20)。开场由 BattleEngine 按每排上限 3 分配:
        /// 优先吃 Def.Row,该排满了改判到另一排。**进快照** —— 同一个 Id 的两只怪
        /// 可能站不同排,而 Restore 是按 Id 查 Def 的,不存就会在读档时被合并。</summary>
        public EnemyRow Row { get; internal set; }

        /// <summary>实际列(2026-08-22,spec §6.1):同排内 0..2,由 BattleEngine 开场分配。
        /// **进快照** —— 与 <see cref="Row"/> 同一条理由:同一个 Id 的两只怪可能站不同列,
        /// 而 Restore 是按 Id 查 Def 的,不存就会在读档时被合并。
        ///
        /// 贯穿形状(<see cref="TargetShape.Skewer"/>)按它取「同一列的前后两只」,
        /// 表现层按它决定画进哪一格。
        ///
        /// 2026-08-30 起语义收紧为「**起始列(左端)**」——占几列由 <see cref="ColumnSpan"/>
        /// 决定,实际占据 [Column, ColumnEnd)。Span = 1 的怪(眼下除 Boss 外全部)不受影响。</summary>
        public int Column { get; internal set; }

        /// <summary>占几列(2026-08-30),转发 <see cref="EnemyDef.ColumnSpan"/>。
        /// 不存快照 —— Def 里就有。</summary>
        public int ColumnSpan => Def.ColumnSpan;

        /// <summary>占位区间的**右开端**= Column + ColumnSpan。裁定一律用 [Column, ColumnEnd)。</summary>
        public int ColumnEnd => Column + Def.ColumnSpan;

        /// <summary>行动计量器:回合末累积有效速度,每满 100 行动一次。</summary>
        public int ActionMeter { get; internal set; }

        /// <summary>基础攻击(缺笔妖补全会直接抬高它 —— 那是形态变化不是增益,故不可驱散)。</summary>
        public int BaseAttack { get; internal set; }

        /// <summary>当前攻击 = 基础攻击 × (100 + 攻击增益% − 诅咒%) ÷ 100,向下取整、下限 0。
        ///
        /// AttackBuff 与 Curse 都是**百分点**,同一根轴上直接加减(2026-08-12,E-b4 T0.5)——
        /// 与玩家侧 <c>BattleEngine.EffectiveAttack</c> 同形,敌我两侧的 AttackBuff 从此是同一个单位。
        /// 这是为量级 ×10 铺路:规则只剩「数量乘、比值永不乘」,乘的只有 BaseAttack 一处,
        /// 不需要「敌人的加攻乘、玩家的不乘」这种迟早被忘掉的特例。
        /// 代价是**刻意的语义变化**:+50% 与 −50% 从此精确相消,不再是旧式子那种
        /// (基础 + 加数) × (1 − 诅咒%) 的乘法交互。
        ///
        /// **钳的是最终值,不是单项** —— 与 <c>BattleEngine.AttackHits</c> 那条钳位同型
        /// (那段 2026-08-08 订正过一次,原注释的推理是反的)。旧写法把诅咒单项钳到 ≤100,
        /// 换到加减轴上就成了「诅咒 120% 被削成 100%,+50% 的增益反而净赚」。
        /// 净百分比转负由 <c>Math.Max(0, …)</c> 兜住,与旧式子在「诅咒 ≥100 且无增益」时同样得 0。
        ///
        /// 整数算式(2026-08-06 M1 起的纪律):必须是 <c>BaseAttack × percent ÷ 100</c>,
        /// 不许写成 <c>BaseAttack × (1 + (percent − 100) / 100f)</c> 这类浮点 —— 会把 floor 拉低 1 点。
        /// ⚠ M1 当年举的 curse = 10 / 30 这组例子在 .NET 8 上其实测不出差别(乘法那步又舍了回来),
        /// 真正的分歧点见 AttackBuffUnitTests.IntegerMath_HasNoFloatPrecisionLoss,别拿旧例子做变异检查。
        /// Boss 大招也读这个属性,所以诅咒/加攻自动对大招生效,不需要额外接线。</summary>
        public int Attack
        {
            get
            {
                int percent = 100
                    + Statuses.TotalMagnitude(StatusKind.AttackBuff)
                    - Statuses.TotalMagnitude(StatusKind.Curse);
                return Math.Max(0, BaseAttack * percent / 100);
            }
        }
        /// <summary>护甲**点数**(2026-08-12,E-b4):每记挥击从伤害里减这么多,下钳 0。
        ///
        /// ⚠ **硬约束:战斗中永不被写**(spec §4.5.3)。它是不可变的基础属性,所以刻意做成
        /// **计算属性**而不是带 internal setter 的字段 —— 类型系统兜住,不靠人记得。
        /// 一切对护甲的改变(增 DefenseBuff / 减 ArmorBreak / 穿 PierceBuff)全部是
        /// <see cref="Statuses"/> 里的条目,而 StatusBag 本来就进快照。
        /// **这是「零新增快照字段」的全部依据**:给它加 setter 会让敌人护甲变成战中可变状态,
        /// 必须补一个 EnemySnapshot 字段,而漏补是静默的(RunSnapshot.cs:9 那条警告说的就是这种事)。
        ///
        /// Boss 换阶段时读数会变,但那是 <see cref="PhaseIndex"/> 变了、不是本属性被写 ——
        /// PhaseIndex 早就在快照里,复原后自然读回同一个值。
        /// 守卫测试:<c>Defense_IsNeverMutatedDuringBattle</c>。</summary>
        public int Defense => Def.Phases.Count > 0 ? Def.Phases[PhaseIndex].Defense : Def.Defense;
        public int PhaseIndex { get; internal set; }     // 成语 Boss 当前阶段(0 起)
        public int RegrowProgress { get; internal set; } // 补全进度 0~3
        public bool HasSplit { get; internal set; }
        public int HitsTaken { get; internal set; }      // 受击计数(生僻字"读懂"用)
        /// <summary>蓄力计数(spec 2026-07-28):满 BossChargeEvery 即进入蓄力回合。</summary>
        public int ChargeCounter { get; internal set; }
        /// <summary>蓄力中:本回合已不出手,下个敌方回合释放 ChargingSkill。</summary>
        public bool IsCharging { get; internal set; }

        /// <summary>蓄力时锁定的技能:预告什么就放什么,期间换阶也不改写(2026-07-29)。</summary>
        public BossSkill ChargingSkill { get; internal set; }

        /// <summary>UI 应显示的属性:null = 未知("?");结算永远用真实 Element。</summary>
        public Element? ApparentElement { get; internal set; }

        public bool Alive => Hp > 0;
        public bool IsBoss => Def.Phases.Count > 0;

        /// <summary>血量阈值(降序):Hp ≤ [i] 即进入阶段 i+1。阶段血量占比为基准,±浮动。</summary>
        internal int[] PhaseBounds { get; set; } = Array.Empty<int>();

        /// <summary>参与生克的五行(不含「心」);通假字的真身/伪装都从这里摇。</summary>
        private static readonly Element[] Wuxing =
            { Element.Wood, Element.Fire, Element.Earth, Element.Metal, Element.Water };

        internal EnemyState(EnemyDef def) : this(def, 0, null) { }

        /// <summary>断点存档:摊平成 POCO(2026-07-27)。Statuses 深拷贝——条目是引用对象,
        /// 浅拷会让恢复后的两个敌人共享同一条状态(2026-08-04)。</summary>
        internal EnemySnapshot Capture()
        {
            var statuses = new List<StatusEffect>();
            foreach (var s in Statuses.All) statuses.Add(s.Clone());
            return new EnemySnapshot
            {
                DefId = Def.Id,
                Hp = Hp,
                MaxHp = MaxHp,
                Element = Element,
                ApparentElement = ApparentElement,
                Shield = Shield,
                Statuses = statuses,
                ActionMeter = ActionMeter,
                BaseAttack = BaseAttack,
                PhaseIndex = PhaseIndex,
                PhaseBounds = (int[])PhaseBounds.Clone(),
                RegrowProgress = RegrowProgress,
                HasSplit = HasSplit,
                HitsTaken = HitsTaken,
                ChargeCounter = ChargeCounter,
                IsCharging = IsCharging,
                ChargingSkill = ChargingSkill,
                Row = Row,
                Column = Column,
            };
        }

        /// <summary>从存档复原:全部字段照抄,不重摇任何随机量(伪装属性、Boss 阈值都是开场摇的)。
        /// Statuses 走 CopyFrom 深拷贝,同样是为了不与源共享条目引用。</summary>
        internal static EnemyState Restore(EnemySnapshot snapshot, EnemyDef def)
        {
            var state = new EnemyState(def)
            {
                Hp = snapshot.Hp,
                MaxHp = snapshot.MaxHp,
                Element = snapshot.Element,
                ApparentElement = snapshot.ApparentElement,
                Shield = snapshot.Shield,
                ActionMeter = snapshot.ActionMeter,
                BaseAttack = snapshot.BaseAttack,
                PhaseIndex = snapshot.PhaseIndex,
                PhaseBounds = snapshot.PhaseBounds ?? Array.Empty<int>(),
                RegrowProgress = snapshot.RegrowProgress,
                HasSplit = snapshot.HasSplit,
                HitsTaken = snapshot.HitsTaken,
                ChargeCounter = snapshot.ChargeCounter,
                IsCharging = snapshot.IsCharging,
                ChargingSkill = snapshot.ChargingSkill,
                Row = snapshot.Row,
                Column = snapshot.Column,
            };
            state.Statuses.CopyFrom(snapshot.Statuses ?? new List<StatusEffect>());
            return state;
        }

        internal EnemyState(EnemyDef def, int phaseJitterPercent, GameRandom random)
        {
            Def = def;
            // 配置速度优先,未配置(0)回落基准 100(2026-08-17,spec §5.8)。
            // Speed 保留 setter 只为让测试直接改速度(engine.Enemies[0].Speed = ...);
            // Speed **不进快照**(EnemySnapshot 没有这个字段),Restore 走 new EnemyState(def)
            // 由本条条件赋值现算 —— 与 Defense 同一套模式。减速走 SpeedModifier 状态不碰这里。
            if (def.Speed > 0) Speed = def.Speed;
            if (def.Phases.Count > 0)
            {
                int total = 0;
                foreach (var phase in def.Phases) total += phase.MaxHp;
                Hp = total;
                MaxHp = total;
                PhaseBounds = RollPhaseBounds(def.Phases, total, phaseJitterPercent, random);
                ApplyPhaseStats(0);
            }
            else
            {
                Hp = def.MaxHp;
                MaxHp = def.MaxHp;
                Element = def.Element;
                BaseAttack = def.Attack;
                if (def.Ability == EnemyAbility.Disguise && random != null)
                {
                    // 通假字(2026-07-26):真身与伪装每次遭遇都现摇,配置里的 element 对它不作数。
                    // 两者必不相同(撞车了伪装就没意义),且都不取「心」(心不参与生克,骗不到人)
                    Element = Wuxing[random.Next(Wuxing.Length)];
                    int fake = random.Next(Wuxing.Length - 1);
                    if (fake >= Array.IndexOf(Wuxing, Element)) fake++; // 跳过真身那一格:均匀且必不撞车
                    ApparentElement = Wuxing[fake];
                }
                else
                {
                    ApparentElement = def.Ability == EnemyAbility.Obscure ? null : def.Element; // 生僻字:属性隐藏
                }
            }
        }

        /// <summary>换阶段:属性/攻击切换;血量连续不重置,**身上的状态一条都不清**。
        /// 护甲不在这里赋值 —— <see cref="Defense"/> 是读 PhaseIndex 的计算属性(§4.5.3:战中永不被写)。</summary>
        internal void ApplyPhaseStats(int index)
        {
            var phase = Def.Phases[index];
            PhaseIndex = index;
            Element = phase.Element;
            ApparentElement = phase.Element; // Boss 阶段属性明示
            BaseAttack = phase.Attack;
            // 2026-08-18 拍板:换阶不清 debuff。血是一条连续的总池,阶段只换属性与攻击,
            // 「新字新体」那套说辞(曾据此清灼烧与不灭,2026-08-09)与血量连续本就不自洽,
            // 实感上则是玩家铺好的持续伤害被换阶白白抹掉。**不灭一并保留** —— 它是挂在
            // Boss 身上的 debuff,与灼烧同口径;代价是一张 炑 能覆盖整场 Boss 战。

            // 蓄力完全不受换阶影响(2026-07-29)。理由见 spec 3.2:阶段血量 12~16 在玩家输出面前
            // 只够 1~2 回合,任何"换阶打断蓄力"的写法都会让大招几乎放不出来——实测阶段血量抬到
            // 4 倍、DPS30 依然一次不放。预告的技能在 ChargingSkill 里记着,换阶不改写它:
            // UI 说了"下回合淹没"就得放淹没。
        }

        private static int[] RollPhaseBounds(IReadOnlyList<BossPhaseDef> phases, int total,
            int jitterPercent, GameRandom random)
        {
            var bounds = new int[phases.Count - 1];
            int cumulative = 0;
            int previous = total;
            for (int i = 0; i < bounds.Length; i++)
            {
                cumulative += phases[i].MaxHp;
                int bound = total - cumulative;
                if (random != null && jitterPercent > 0)
                {
                    int span = total * jitterPercent / 100;
                    bound += random.Next(2 * span + 1) - span; // ±span 均匀浮动
                }
                bound = Math.Min(bound, previous - 1);         // 保持严格降序
                bound = Math.Max(bound, bounds.Length - i);    // 给后续阶段留至少 1 血
                bounds[i] = bound;
                previous = bound;
            }
            return bounds;
        }
    }
}
