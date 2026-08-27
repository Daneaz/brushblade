using System.Collections.Generic;

namespace Brushblade.Core
{
    /// <summary>战斗内断点存档(2026-07-27):把 BattleEngine / RunEngine 的可变状态整个摊平成
    /// POCO,由 Data 层直接 JSON 序列化。配置侧的东西(字表、敌表定义、卡等级、层段生成)不进快照
    /// —— 那些用同一颗 Seed 重建即可,只有摇过的随机流位置(RandomState)必须存,否则续爬会分叉。
    ///
    /// ⚠️ 加新的可变状态时,同步加进这里 + Capture/Restore + RoundTrip 测试。
    /// 漏一个字段不会报错,只会让玩家挂起再进时状态悄悄回退。</summary>
    public sealed class BattleSnapshot
    {
        public int PlayerHp { get; set; }
        public int Ap { get; set; }
        public int Turn { get; set; }
        public BattlePhase Phase { get; set; }
        public int ShieldNormal { get; set; }
        public int ShieldPersist { get; set; }
        public int BurnPerStack { get; set; }   // 炽可抬高,本场累计
        public uint RandomState { get; set; }
        public List<string> Library { get; set; } = new();
        public List<string> Pool { get; set; } = new();
        public List<EnemySnapshot> Enemies { get; set; } = new();
        public List<SummonSnapshot> Summons { get; set; } = new();
        public List<StatusEffect> PlayerStatuses { get; set; } = new(); // HoT / 减伤(2026-08-04 统一容器)
        public string PendingDrop { get; set; } // 待决议的掉落字(DropChoice 阶段,2026-08-04)

        /// <summary>SourceId 自增序号(2026-08-04):HoT 与 AttackBuff 共用同一个计数器
        /// (见 BattleEngine._statusSerial)。不存的话续爬后计数器归零,新施加的状态
        /// SourceId 可能撞上快照里恢复的条目,撞上就被意外覆盖(可叠语义失效)。</summary>
        public int StatusSerial { get; set; }

        /// <summary>玩家的行动计量器(2026-08-15,ATB 改造)。不存会让续爬后玩家的节拍从 0 重来,
        /// 而敌人侧的 ActionMeter 早就在存了 —— 这条是补齐。恒非负,进出快照都不需要任何特例。</summary>
        public int PlayerActionMeter { get; set; }

        /// <summary>战意首回合宽限标记(2026-08-18):见 BattleEngine._moraleGraceTurn。
        /// 不存的话续爬会在「从 0 层起手」的那一回合白掉一层。</summary>
        public bool MoraleGraceTurn { get; set; }
    }

    /// <summary>字怪的战中状态。DefId 用来找回配置侧的 EnemyDef(分裂出的克隆共用同一个 Def)。</summary>
    public sealed class EnemySnapshot
    {
        public string DefId { get; set; }
        public int Hp { get; set; }
        public int MaxHp { get; set; }
        public Element Element { get; set; }
        public Element? ApparentElement { get; set; } // null = 生僻字未被读懂
        public List<StatusEffect> Statuses { get; set; } = new();
        public int ActionMeter { get; set; }
        public int BaseAttack { get; set; }
        // ⚠ 曾有一个承伤系数 DamageTaken,随乘法减伤层删除(2026-08-12,E-b4 T3)后无人读写,
        // 已在 T6 的存档迁移里一并删掉:整个登塔快照随 MetaState.Endless → EndlessV2 作废,
        // 旧 JSON 里的 damageTaken 变成未知键被忽略,零风险。敌人护甲是不可变属性,按 DefId 查回。
        public int PhaseIndex { get; set; }
        public int[] PhaseBounds { get; set; }        // Boss 换阶阈值:开场摇的,重算会变
        public int RegrowProgress { get; set; }
        public bool HasSplit { get; set; }
        public int HitsTaken { get; set; }
        public int ChargeCounter { get; set; }   // Boss 蓄力进度(spec 2026-07-28)
        public bool IsCharging { get; set; }     // 蓄力中:读档后要照常放大招
        public BossSkill ChargingSkill { get; set; } // 蓄力锁定的技能:预告什么就放什么
        public EnemyRow Row { get; set; }   // 实际站位(2026-08-20)
        public int Column { get; set; } // 实际列(2026-08-22)
    }

    public sealed class SummonSnapshot
    {
        public string Char { get; set; }
        public Element Element { get; set; }
        public int Hp { get; set; }
        public int MaxHp { get; set; }
        public int Attack { get; set; }
        public int ActionMeter { get; set; }

        /// <summary>基础速度(2026-08-05 补接线)。老存档没有这个字段,恢复时由
        /// SummonState.Restore 兜底回 100 —— 0 会让召唤物永远不出手。</summary>
        public int Speed { get; set; }
        public int Shield { get; set; }
        public SummonPassive Passive { get; set; }

        /// <summary>身上的状态(2026-08-26)。与 <see cref="EnemySnapshot.Statuses"/> 同型;
        /// 老存档没有这个字段 → Newtonsoft 填 null → Restore 兜底成空表。</summary>
        public List<StatusEffect> Statuses { get; set; } = new();

        /// <summary>槽位 0..5(2026-08-20):0/1/2 = 前排,3/4/5 = 后排。
        /// 携带过场与断点续爬都按它原样落位 —— 玩家布的阵不该被系统打乱。</summary>
        public int Slot { get; set; }
    }

    /// <summary>挂在存档上的「段中断点」(2026-07-27):除了 run 自身的状态,还要记住
    /// 重建这一段所需的上下文 —— 层段生成是纯函数,但得用同样的入参才能重建出同一段。</summary>
    public sealed class InProgressRun
    {
        public int FromDepth { get; set; }          // 本段起始层;不能拿 Depth 顶替(它随层清算前进)
        public bool FirstTowerSegment { get; set; } // 首塔特调段(BuildFirstTowerSegment)
        public int CommittedEventInk { get; set; }  // 已即时入账的字摊净额;不存会重复入账
        public RunSnapshot Run { get; set; }
    }

    /// <summary>一段连战的完整进度(含当前战斗)。</summary>
    public sealed class RunSnapshot
    {
        public RunPhase Phase { get; set; }
        public int BattleIndex { get; set; }
        public int ClearedBattleIndex { get; set; }
        public uint RandomState { get; set; }
        public List<string> CarriedLibrary { get; set; } = new();
        public List<string> CarriedPool { get; set; } = new();
        public int CarriedHp { get; set; }
        public int MaxHpBonus { get; set; } // 局内血量上限加成(奇遇累加,2026-08-04)
        public int CarriedNormalShield { get; set; }
        public int CarriedPersistShield { get; set; }
        public List<SummonSnapshot> CarriedSummons { get; set; } = new(); // 召唤物延续(2026-08-03)

        /// <summary>护甲增益跨战斗延续(2026-08-04):段内持久,段末清空;只承载 DefenseBuff,
        /// HoT 不跨战斗(见 RunEngine.AdvanceAfterBattle 的过滤)。</summary>
        public List<StatusEffect> CarriedStatuses { get; set; } = new();
        public int CharPicksLeft { get; set; }
        public List<string> RewardOptions { get; set; } = new();
        public List<string> ComponentOptions { get; set; } = new();
        public string CurrentEventId { get; set; } // 停在奇遇页挂起时非空
        public int EarnedInk { get; set; }
        public bool LibraryExpanded { get; set; }
        public bool PoolExpanded { get; set; }
        public bool Revived { get; set; }
        public int ReviveCharPicksLeft { get; set; } // 复活补给本轮剩余选字次数(2026-08-04)
        public int ReviveRoundsLeft { get; set; } // 复活补给剩余重抽轮数(2026-08-04)
        public List<string> DefeatedEnemyIds { get; set; } = new();
        public BattleSnapshot Battle { get; set; }
    }
}
