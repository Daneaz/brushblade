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
        public int Attack { get; set; }
        public float DamageTaken { get; set; }
        public int PhaseIndex { get; set; }
        public int[] PhaseBounds { get; set; }        // Boss 换阶阈值:开场摇的,重算会变
        public int RegrowProgress { get; set; }
        public bool HasSplit { get; set; }
        public int HitsTaken { get; set; }
    }

    public sealed class SummonSnapshot
    {
        public string Char { get; set; }
        public Element Element { get; set; }
        public int Hp { get; set; }
        public int MaxHp { get; set; }
        public int Attack { get; set; }
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
        public int CarriedNormalShield { get; set; }
        public int CarriedPersistShield { get; set; }
        public int CharPicksLeft { get; set; }
        public int ComponentPicksLeft { get; set; }
        public List<string> RewardOptions { get; set; } = new();
        public List<string> ComponentOptions { get; set; } = new();
        public string CurrentEventId { get; set; } // 停在奇遇页挂起时非空
        public int EarnedInk { get; set; }
        public bool LibraryExpanded { get; set; }
        public bool PoolExpanded { get; set; }
        public bool Revived { get; set; }
        public List<string> DefeatedEnemyIds { get; set; } = new();
        public BattleSnapshot Battle { get; set; }
    }
}
