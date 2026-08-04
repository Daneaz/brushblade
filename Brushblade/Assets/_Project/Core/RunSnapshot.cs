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
        public List<HotSnapshot> Hots { get; set; } = new();
        public Dictionary<string, int> DamageReductions { get; set; } = new(); // 减伤来源(2026-08-03)
        public string PendingDrop { get; set; } // 待决议的掉落字(DropChoice 阶段,2026-08-04)
    }

    /// <summary>字怪的战中状态。DefId 用来找回配置侧的 EnemyDef(分裂出的克隆共用同一个 Def)。</summary>
    public sealed class EnemySnapshot
    {
        public string DefId { get; set; }
        public int Hp { get; set; }
        public int MaxHp { get; set; }
        public Element Element { get; set; }
        public Element? ApparentElement { get; set; } // null = 生僻字未被读懂
        public int Burn { get; set; }
        public int Bleed { get; set; }
        public int BleedTurns { get; set; }
        public int FreezeTurns { get; set; }
        public int SlowTurns { get; set; }
        public bool SlowActs { get; set; }
        public int Attack { get; set; }
        public float DamageTaken { get; set; }
        public int PhaseIndex { get; set; }
        public int[] PhaseBounds { get; set; }        // Boss 换阶阈值:开场摇的,重算会变
        public int RegrowProgress { get; set; }
        public bool HasSplit { get; set; }
        public int HitsTaken { get; set; }
        public int ChargeCounter { get; set; }   // Boss 蓄力进度(spec 2026-07-28)
        public bool IsCharging { get; set; }     // 蓄力中:读档后要照常放大招
        public BossSkill ChargingSkill { get; set; } // 蓄力锁定的技能:预告什么就放什么
    }

    public sealed class SummonSnapshot
    {
        public string Char { get; set; }
        public Element Element { get; set; }
        public int Hp { get; set; }
        public int MaxHp { get; set; }
        public int Attack { get; set; }
    }

    /// <summary>持续治疗快照(2026-08-03)。</summary>
    public sealed class HotSnapshot
    {
        public int Amount { get; set; }
        public int Turns { get; set; }
        public bool All { get; set; }
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

        /// <summary>减伤跨战斗延续(2026-08-03):段内持久,段末清空。</summary>
        public Dictionary<string, int> CarriedDamageReductions { get; set; } = new();
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
