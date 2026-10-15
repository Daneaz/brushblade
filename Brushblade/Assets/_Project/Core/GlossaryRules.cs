namespace Brushblade.Core
{
    /// <summary>词条表的术语(spec 2026-10-03 §3,2026-10-15 spec B 追加涓 / 涌 / 苏 / 速度、同日厚积薄发 / 涌泉相报单列后共 48 条(关键词 38 / 范围 7 / 存续 3),按 §3.1 关键词 → §3.2 范围 → §3.3 存续的顺序)。
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
        Weaken,        // 慑(原「减攻」)
        DamageCut,     // 减伤
        CounterBoost,  // 反击加倍
        Endure,        // 保命
        Thorns,        // 荆棘
        Crit,          // 暴击
        Trickle,       // 涓(润泽之一:多回合少量,spec B §3)
        Surge,         // 涌(润泽之一:单回合大量)
        Revive,        // 苏(润泽之一:复活木灵)
        Speed,         // 速度(林风 / 森风 / 浮藻 / 木疾 的 Lv5 被动)
        SpendHeft,     // 厚积薄发(2026-10-15 用户:特殊词条,单独成条)
        SpendWellspring, // 涌泉相报(同上)
        Kindle,        // 燃意(spec C §5.3:火五行面)
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

    /// <summary>词条图标 chip 的底色分组 = 战场状态 chip 的四组(UnitDetailChip.ColorFor):
    /// 威胁(朱砂)/ 控制(墨蓝)/ 守御(翠绿)/ 增益(赭金)。Presentation 把它映射成颜色。</summary>
    public enum ChipTone
    {
        Threat,
        Control,
        Guard,
        Boost,
    }

    /// <summary>词条的纯规则:分组与「数值格 → 术语」映射(附录 §C R6)。</summary>
    public static class GlossaryRules
    {
        /// <summary>0 = 关键词、1 = 范围、2 = 存续(spec §3.1 / §3.2 / §3.3)。</summary>
        public static int Group(GlossaryTerm term) =>
            term <= GlossaryTerm.Kindle ? 0 : term <= GlossaryTerm.AreaAll ? 1 : 2;

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
            FaceStatKind.SpendWellspring => GlossaryTerm.SpendWellspring,
            FaceStatKind.SpendHeft => GlossaryTerm.SpendHeft,
            FaceStatKind.ReviveHp => GlossaryTerm.Revive,
            FaceStatKind.Kindle => GlossaryTerm.Kindle,
            _ => null,
        };

        /// <summary>按格取术语(spec B §1.3):治疗格按来源分涓(持续)/ 涌(立即),其余同 <see cref="TermOf(FaceStatKind)"/>。</summary>
        public static GlossaryTerm? TermOf(FaceStat stat) => stat.Kind switch
        {
            FaceStatKind.Heal when stat.Source == EffectKind.HealOverTime => GlossaryTerm.Trickle,
            FaceStatKind.Heal when stat.Source == EffectKind.HealSelf || stat.Source == EffectKind.HealAll => GlossaryTerm.Surge,
            _ => TermOf(stat.Kind),
        };

        /// <summary>词条的图标 chip(spec B §1.5 / §3):= 战场上那枚状态 chip 的图标与底色分组;没有图标 = null(行头 / 气泡不画 chip)。
        /// 图标 id 是 tools/icons 管线产物的 key(icon_&lt;id&gt;.png),这里只引用现有的(执行期决定 D1)。</summary>
        public static (string Icon, ChipTone Tone)? ChipOf(GlossaryTerm term) => term switch
        {
            GlossaryTerm.Burn => ("burn", ChipTone.Threat),
            GlossaryTerm.Freeze => ("freeze", ChipTone.Control),
            GlossaryTerm.FrostResist => ("frostguard", ChipTone.Control),
            GlossaryTerm.IceStall => ("chill", ChipTone.Control),
            GlossaryTerm.Slow => ("slow", ChipTone.Control),
            GlossaryTerm.Morale => ("morale", ChipTone.Boost),
            GlossaryTerm.Kindle => ("burn", ChipTone.Boost),   // 燃意:增益组(赭金),借用灼图标(spec C §5.3)
            GlossaryTerm.Block => ("block", ChipTone.Guard),
            GlossaryTerm.Shield => ("shield", ChipTone.Guard),
            GlossaryTerm.Armor => ("defense", ChipTone.Guard),
            GlossaryTerm.ArmorBreak => ("armorbreak", ChipTone.Control),
            GlossaryTerm.Hot => ("heal", ChipTone.Guard),
            GlossaryTerm.Seed => ("seed", ChipTone.Guard),
            GlossaryTerm.Taunt => ("taunt", ChipTone.Control),
            GlossaryTerm.Bleed => ("bleed", ChipTone.Threat),
            GlossaryTerm.Doom => ("mortal", ChipTone.Threat),
            GlossaryTerm.Mark => ("mark", ChipTone.Threat),
            GlossaryTerm.Weaken => ("curse", ChipTone.Control),
            GlossaryTerm.DamageCut => ("bulwark", ChipTone.Guard),
            GlossaryTerm.CounterBoost => ("riposte", ChipTone.Boost),
            GlossaryTerm.Endure => ("lastlife", ChipTone.Guard),
            GlossaryTerm.Thorns => ("thorns", ChipTone.Threat),   // 本命 chip 一律朱砂
            GlossaryTerm.Crit => ("crit", ChipTone.Boost),
            GlossaryTerm.Trickle => ("heal", ChipTone.Guard),
            GlossaryTerm.Surge => ("heal", ChipTone.Guard),
            GlossaryTerm.Revive => ("lastlife", ChipTone.Guard),
            GlossaryTerm.Speed => ("speed", ChipTone.Guard),       // 战场速度加成是守御组(D2)
            _ => null,
        };
    }
}
