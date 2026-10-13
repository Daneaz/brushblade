namespace Brushblade.Core
{
    /// <summary>词条表的术语(spec 2026-10-03 §3,42 条,按 §3.1 关键词 32 → §3.2 范围 7 → §3.3 存续 3 的顺序)。
    /// 名称取字符串表 key 的 ASCII id(<c>term.&lt;id&gt;.name / .desc</c>)转 PascalCase;说明文案在字符串表,不在这里。
    /// ordinal 不进存档,但 <see cref="GlossaryRules.Group"/> 依赖三组连续排列 —— 新增时插在所属组的末尾。</summary>
    public enum GlossaryTerm
    {
        // ---- §3.1 关键词 ----
        Burn,          // 灼
        Detonate,      // 引爆
        Freeze,        // 冻结
        FrostResist,   // 霜抗
        IceStall,      // 冰滞
        Slow,          // 减速
        Morale,        // 战意
        Block,         // 格挡
        Wellspring,    // 泉
        Heft,          // 厚
        Shield,        // 护盾
        Armor,         // 护甲
        ArmorBreak,    // 破甲
        Hot,           // 润泽
        Seed,          // 种
        Spirit,        // 木灵
        Nature,        // 本命
        Sapling,       // 幼苗
        Graft,         // 嫁接
        Taunt,         // 嘲讽
        Execute,       // 斩杀
        Bleed,         // 流血
        Doom,          // 致命
        Charm,         // 魅惑
        Mark,          // 标记
        Weaken,        // 减攻
        DamageCut,     // 减伤
        CounterBoost,  // 反击加倍
        Endure,        // 保命
        Thorns,        // 荆棘
        Crit,          // 暴击
        // ---- §3.2 范围 ----
        AreaSingle,    // 单体
        AreaAdjacent,  // 溅射
        AreaRow,       // 横扫
        AreaColumn,    // 贯穿
        AreaScatter,   // 散射
        AreaChain,     // 弹射
        AreaAll,       // 全体
        // ---- §3.3 存续 ----
        Battle,        // 本场
        Carry,         // 跨场
        Band,          // 跨段
    }

    /// <summary>词条的纯规则:分组与「数值格 → 术语」映射(附录 §C R6)。</summary>
    public static class GlossaryRules
    {
        /// <summary>0 = 关键词、1 = 范围、2 = 存续(spec §3.1 / §3.2 / §3.3)。</summary>
        public static int Group(GlossaryTerm term) =>
            term <= GlossaryTerm.Crit ? 0 : term <= GlossaryTerm.AreaAll ? 1 : 2;

        /// <summary>字卡详情数值格对应的术语;null = 这一格不带术语(伤害 / 治疗 / 召唤攻击 / 反击 / 持续),不开气泡。</summary>
        public static GlossaryTerm? TermOf(FaceStatKind kind) => kind switch
        {
            FaceStatKind.Shield => GlossaryTerm.Shield,
            FaceStatKind.SummonHp => GlossaryTerm.Spirit,
            FaceStatKind.Burn => GlossaryTerm.Burn,
            FaceStatKind.Morale => GlossaryTerm.Morale,
            FaceStatKind.Block => GlossaryTerm.Block,
            FaceStatKind.Weaken => GlossaryTerm.Weaken,
            FaceStatKind.Armor => GlossaryTerm.Armor,
            FaceStatKind.Freeze => GlossaryTerm.Freeze,
            FaceStatKind.Slow => GlossaryTerm.Slow,
            FaceStatKind.Seed => GlossaryTerm.Seed,
            FaceStatKind.Charm => GlossaryTerm.Charm,
            FaceStatKind.HealOverTime => GlossaryTerm.Hot,
            FaceStatKind.ArmorBreak => GlossaryTerm.ArmorBreak,
            FaceStatKind.Nature => GlossaryTerm.Nature,
            FaceStatKind.SpendWellspring => GlossaryTerm.Wellspring,
            FaceStatKind.SpendHeft => GlossaryTerm.Heft,
            _ => null,
        };
    }
}
