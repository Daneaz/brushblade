namespace Brushblade.Core
{
    /// <summary>出字效果类型(第 3 章 3.2.1;按流派需要逐步扩展)。</summary>
    public enum EffectKind
    {
        DamageSingle, // 单体伤害
        DamageAll,    // 全体伤害(AOE)
        BurnSingle,   // 单体灼烧(叠层)
        BurnAll,      // 全体灼烧(叠层)
        Shield,       // 自身护盾
        BurnPotency,  // 本场每层灼烧结算伤害 +Value(炽,可叠加,10.3.1)
        HealSelf,     // 治疗自身(不超上限;水系主打,2026-07-19 拍板)
        Summon,       // 召唤前排单位(Value=血量;木系主打,2026-07-19 拍板)
        Bleed,        // 流血:每回合固定伤害,无属性、不走生克(2026-08-03)
        HealAll,        // 群体治疗:玩家 + 全部召唤物(2026-08-03)
        HealOverTime,   // 持续治疗:每回合 Value,持续 Turns 回合;TargetAll 则含召唤物
        Freeze,       // 冻结:目标跳过 Value 个回合(2026-08-03;藤的「束缚」也走这个)
        Slow,         // 减速:半速,每 2 回合才行动一次,持续 Value 回合(2026-08-03)
        DamageReduction,  // 减伤:受伤 −Value%,乘法叠加、同字不叠、段内持久(2026-08-03)
        ArmorBreak,   // 破甲:目标承伤 +25%,持续 Value 回合。不叠层,重复施加只刷新(2026-08-05)
        Dispel,       // 驱散:清敌方增益。Value = 条数(−1 = 全部);TargetAll = 全体各清(2026-08-06)
        Cleanse,      // 净化:清玩家自身全部减益(Value 不用,2026-08-06)
        Immunity,     // 免疫:完全挡下 Value 次伤害,先于护盾消耗(2026-08-06)
        Revive,       // 复活:救回 Value 名阵亡召唤物,各回半血(2026-08-06)
        Blind,        // 致盲:目标命中率 −Value%,持续 Turns 回合;TargetAll = 全体(2026-08-07)
        Silence,      // 沉默:目标的主动机制哑火,持续 Turns 回合(Value 不用,2026-08-07)
        Reflect,      // 反弹:受到的伤害按 Value% 照回攻击者,持续 Turns 回合(2026-08-07)
        BurnNoDecay,  // 不灭:目标灼烧本场不衰减(Value 不用,2026-08-09)
        BurnSettleNow, // 立即结算一次灼烧(与回合末同公式;Value 不用,2026-08-09)
        Detonate,     // 引爆:把目标剩余灼烧层数的全部未来伤害一次打出并清空(Value 不用,2026-08-09)
        Empower,      // 本场攻击力 +Value(剡;可叠加,2026-08-12)
        Morale,       // 战意 +Value 层,每层 +10 攻击,上限 5 层(战/戮;本场持久,2026-08-12)
        ApBoost,      // 本场每回合 AP 上限 +Value(利;可叠加,2026-08-12)
    }

    /// <summary>单条效果:伤害/护盾/治疗走生克结算,灼烧层数为平值。</summary>
    public sealed class EffectDef
    {
        public EffectKind Kind { get; }
        public int Value { get; }

        /// <summary>伤害类:目标带灼烧时基础值翻倍(灼,10.3.1)。</summary>
        public bool DoubleVsBurning { get; }

        /// <summary>护盾类:豁免一次回合末全清(堡,10.3.6)。</summary>
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

        /// <summary>伤害类:无视目标的承伤减免,并额外 +15% 伤害(穿甲,2026-08-05)。
        /// 只忽略减免(&lt;1),不忽略破甲加成 —— 「无视防御」≠「无视一切修正」。</summary>
        public bool IgnoreArmor { get; }

        /// <summary>召唤类:召唤物被动(2026-08-05)。null = 无被动。</summary>
        public SummonPassive Passive { get; }

        /// <summary>召唤类:出字瞬间给**全场存活召唤物**各 +N 点护盾(桂 = 6)。
        /// 不放进 Passive —— 它作用于出字时已在场的其他召唤物,不是这只召唤物自带的。</summary>
        public int SummonShield { get; }

        /// <summary>斩杀:目标 HP 百分比低于此值时触发(0 = 不启用)。**打之前**判血——
        /// 让玩家看着血条就能决定出哪张;打之后判定虽然更像补刀,但结果不可预期。</summary>
        public int ExecuteBelowPercent { get; }

        /// <summary>true = 命中阈值直接击杀(Boss 免疫);false = 命中阈值伤害 ×2(对 Boss 照常生效)。</summary>
        public bool ExecuteKills { get; }

        /// <summary>伤害分几段打(剁 = 2)。默认 1。每段完全独立:各自过生克、破甲、穿甲,
        /// 也各自过斩杀的「打之前判血」——所以「第一段把敌人打进阈值、第二段触发处决」
        /// 是真会发生的涌现,不是 bug。</summary>
        public int HitCount { get; }

        public EffectDef(EffectKind kind, int value,
            bool doubleVsBurning = false, bool persistOnce = false,
            int summonCount = 1, int summonAttack = 0, string summonChar = "木",
            int turns = 0, bool targetAll = false, bool ignoreArmor = false,
            SummonPassive passive = null, int summonShield = 0,
            int executeBelowPercent = 0, bool executeKills = false,
            int hitCount = 1)
        {
            Kind = kind;
            Value = value;
            DoubleVsBurning = doubleVsBurning;
            PersistOnce = persistOnce;
            SummonCount = summonCount;
            SummonAttack = summonAttack;
            SummonChar = summonChar;
            Turns = turns;
            TargetAll = targetAll;
            IgnoreArmor = ignoreArmor;
            Passive = passive;
            SummonShield = summonShield;
            ExecuteBelowPercent = executeBelowPercent;
            ExecuteKills = executeKills;
            HitCount = hitCount <= 0 ? 1 : hitCount;
        }
    }
}
