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
        Buff,   // 标点小妖:有同伴时每回合给其他存活字怪攻击 +Attack(本场累计不回滚,
                // 优先级目标);场上只剩自己时改为亲自攻击(2026-07-22)
        Disguise, // 通假字:真身与伪装每次遭遇现摇(必不相同),首次行动后现形(信息隐藏)
        Obscure,  // 生僻字:属性隐藏("?"),受击两次后被"读懂"
        Scorch,   // 焦痕:每次被击中且存活,攻 +2(越磨越烫,宜速杀)
    }

    /// <summary>Boss 阶段技能(spec 2026-07-28):蓄力一回合后释放。
    /// Bulwark 为被动标签,行为与 None 相同(靠 DamageTaken 减伤),
    /// 分开只为可读性——Bulwark = 设计上就该是肉墙,None = 这字还没配技能。</summary>
    public enum BossSkill
    {
        None,
        Deluge, // 淹没:玩家 + 全部召唤物各挨一下(群攻)
        Pierce, // 贯穿:最前召唤物挨一下 + 玩家挨双倍(穿透)
        Topple, // 倾覆:伤害 + 清空护盾 + 下回合 AP −1(剥夺)
        Devour, // 吞噬:消灭最前召唤物(不回血);无召唤物则普攻玩家
        Bulwark, // 坚壁:被动减伤,该阶段不蓄力
    }

    /// <summary>成语 Boss 的单个阶段(8.5:四字成语,四个字 = 四个阶段)。</summary>
    public sealed class BossPhaseDef
    {
        public string Char { get; }
        public Element Element { get; }
        public int MaxHp { get; }
        public int Attack { get; }
        /// <summary>承伤系数(如「山」0.5 = 超高防御),向下取整。</summary>
        public float DamageTaken { get; }
        /// <summary>该阶段的蓄力技能(spec 2026-07-28);由字表决定,None = 纯普攻。</summary>
        public BossSkill Skill { get; }

        public BossPhaseDef(string phaseChar, Element element, int maxHp, int attack,
            float damageTaken = 1f, BossSkill skill = BossSkill.None)
        {
            Char = phaseChar;
            Element = element;
            MaxHp = maxHp;
            Attack = attack;
            DamageTaken = damageTaken;
            Skill = skill;
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

        /// <summary>承伤系数(&lt;1 即减伤;小怪级如墨渍。Boss 走阶段级 BossPhaseDef.DamageTaken)。</summary>
        public float DamageTaken { get; }

        public EnemyDef(string id, Element element, int maxHp, int attack,
            EnemyAbility ability = EnemyAbility.None, IReadOnlyList<BossPhaseDef> phases = null,
            float damageTaken = 1f)
        {
            Id = id;
            Element = element;
            MaxHp = maxHp;
            Attack = attack;
            Ability = ability;
            Phases = phases ?? System.Array.Empty<BossPhaseDef>();
            DamageTaken = damageTaken;
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

        /// <summary>基础速度(2026-08-04)。默认 100 = 每回合恰好一次,与旧的"固定反击一次"等价。</summary>
        public int Speed { get; internal set; } = 100;

        /// <summary>行动计量器:回合末累积速度,每满 100 行动一次(与敌人同走一套模型)。</summary>
        public int ActionMeter { get; internal set; }

        internal SummonState(string summonChar, Element element, int hp, int attack)
        {
            Char = summonChar;
            Element = element;
            Hp = hp;
            MaxHp = hp;
            Attack = attack;
        }

        /// <summary>断点存档:MaxHp 与 Hp 会脱钩(挨过打),故分开存。</summary>
        private SummonState(string summonChar, Element element, int hp, int maxHp, int attack, int actionMeter)
        {
            Char = summonChar;
            Element = element;
            Hp = hp;
            MaxHp = maxHp;
            Attack = attack;
            ActionMeter = actionMeter;
        }

        internal SummonSnapshot Capture() => new()
        {
            Char = Char, Element = Element, Hp = Hp, MaxHp = MaxHp, Attack = Attack, ActionMeter = ActionMeter,
        };

        internal static SummonState Restore(SummonSnapshot s) =>
            new(s.Char, s.Element, s.Hp, s.MaxHp, s.Attack, s.ActionMeter);
    }

    /// <summary>战斗中的字怪状态。成语 Boss 为一条总血池,按血量阈值切换阶段
    /// (2026-07-19 拍板:阈值带种子浮动,同一 Boss 每次体验不同;原独立血量四连战废止)。</summary>
    public sealed class EnemyState
    {
        public EnemyDef Def { get; }
        public int Hp { get; internal set; }
        public int MaxHp { get; internal set; }          // 当前阶段上限
        public Element Element { get; internal set; }    // 当前属性(Boss 换阶段会变)

        /// <summary>Burn/Bleed/Freeze 三个减益的容器(2026-08-04:统一状态容器迁移)。
        /// Burn 用 TurnsLeft = -1(段内持久),靠灼烧结算段自减 Magnitude;
        /// Bleed/Freeze 用 TurnsLeft 正常回合递减。</summary>
        public StatusBag Statuses { get; } = new();

        /// <summary>基础速度(2026-08-04)。有效速度 = Speed + 所有 SpeedModifier 之和,下限 0。
        /// 基数用本字段而非常量 100:将来若有天生快/慢的字怪,写死 100 会让它们的修正算错。</summary>
        public int Speed { get; set; } = 100;

        /// <summary>行动计量器:回合末累积有效速度,每满 100 行动一次。</summary>
        public int ActionMeter { get; internal set; }

        public int Attack { get; internal set; }         // 当前攻击(缺笔妖会成长)
        public float DamageTaken { get; internal set; } = 1f; // 承伤系数(「山」阶段 0.5)
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
                Statuses = statuses,
                ActionMeter = ActionMeter,
                Attack = Attack,
                DamageTaken = DamageTaken,
                PhaseIndex = PhaseIndex,
                PhaseBounds = (int[])PhaseBounds.Clone(),
                RegrowProgress = RegrowProgress,
                HasSplit = HasSplit,
                HitsTaken = HitsTaken,
                ChargeCounter = ChargeCounter,
                IsCharging = IsCharging,
                ChargingSkill = ChargingSkill,
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
                ActionMeter = snapshot.ActionMeter,
                Attack = snapshot.Attack,
                DamageTaken = snapshot.DamageTaken,
                PhaseIndex = snapshot.PhaseIndex,
                PhaseBounds = snapshot.PhaseBounds ?? Array.Empty<int>(),
                RegrowProgress = snapshot.RegrowProgress,
                HasSplit = snapshot.HasSplit,
                HitsTaken = snapshot.HitsTaken,
                ChargeCounter = snapshot.ChargeCounter,
                IsCharging = snapshot.IsCharging,
                ChargingSkill = snapshot.ChargingSkill,
            };
            state.Statuses.CopyFrom(snapshot.Statuses ?? new List<StatusEffect>());
            return state;
        }

        internal EnemyState(EnemyDef def, int phaseJitterPercent, GameRandom random)
        {
            Def = def;
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
                Attack = def.Attack;
                DamageTaken = def.DamageTaken; // 小怪级承伤减免(墨渍)
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

        /// <summary>换阶段:属性/攻击/承伤切换、灼烧清零;血量连续不重置。</summary>
        internal void ApplyPhaseStats(int index)
        {
            var phase = Def.Phases[index];
            PhaseIndex = index;
            Element = phase.Element;
            ApparentElement = phase.Element; // Boss 阶段属性明示
            Attack = phase.Attack;
            DamageTaken = phase.DamageTaken;
            Statuses.Remove(StatusKind.Burn); // 新字新体,灼烧清零

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
