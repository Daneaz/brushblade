using Brushblade.Core;
using Brushblade.Data;

namespace Brushblade.Presentation
{
    /// <summary>技能节点的文案:名称 + 一句效果描述。逐节点/逐枝显式落到字符串表的字面 key ——
    /// <see cref="StringsTableTests"/> 的对账只认字面量,`Strings.T($"...")` 这种动态 key
    /// 会被判孤儿(见 CLAUDE.md「字符串表检查只认字面量」)。43 个节点因此是 43 个 case,
    /// 不是循环拼 key —— 啰嗦但每一条都过得了对账。</summary>
    public static class PerkInfo
    {
        /// <summary>节点名(如「砺刃」)。按 <see cref="PerkNodeDef.NodeKey"/> 取:三段节点的三段
        /// 共用一个名字(2026-10-02;单段节点 NodeKey == Id)。</summary>
        public static string Name(PerkNodeDef def) => def.NodeKey switch
        {
            "metal_1" => Strings.T("perk.node.metal_1.name"),
            "metal_2" => Strings.T("perk.node.metal_2.name"),
            "metal_3" => Strings.T("perk.node.metal_3.name"),
            "metal_4" => Strings.T("perk.node.metal_4.name"),
            "wood_1" => Strings.T("perk.node.wood_1.name"),
            "wood_2" => Strings.T("perk.node.wood_2.name"),
            "wood_3" => Strings.T("perk.node.wood_3.name"),
            "wood_4" => Strings.T("perk.node.wood_4.name"),
            "water_1" => Strings.T("perk.node.water_1.name"),
            "water_2" => Strings.T("perk.node.water_2.name"),
            "water_3" => Strings.T("perk.node.water_3.name"),
            "water_4" => Strings.T("perk.node.water_4.name"),
            "fire_1" => Strings.T("perk.node.fire_1.name"),
            "fire_2" => Strings.T("perk.node.fire_2.name"),
            "fire_3" => Strings.T("perk.node.fire_3.name"),
            "fire_4" => Strings.T("perk.node.fire_4.name"),
            "earth_1" => Strings.T("perk.node.earth_1.name"),
            "earth_2" => Strings.T("perk.node.earth_2.name"),
            "earth_3" => Strings.T("perk.node.earth_3.name"),
            "earth_4" => Strings.T("perk.node.earth_4.name"),
            "vigor_1" => Strings.T("perk.node.vigor_1.name"),
            "vigor_2" => Strings.T("perk.node.vigor_2.name"),
            "vigor_3" => Strings.T("perk.node.vigor_3.name"),
            "power_1" => Strings.T("perk.node.power_1.name"),
            "power_2" => Strings.T("perk.node.power_2.name"),
            "power_3" => Strings.T("perk.node.power_3.name"),
            "edge_1" => Strings.T("perk.node.edge_1.name"),
            "edge_2" => Strings.T("perk.node.edge_2.name"),
            "edge_3" => Strings.T("perk.node.edge_3.name"),
            "guard_1" => Strings.T("perk.node.guard_1.name"),
            "guard_2" => Strings.T("perk.node.guard_2.name"),
            "guard_3" => Strings.T("perk.node.guard_3.name"),
            "lore_1" => Strings.T("perk.node.lore_1.name"),
            "lore_2" => Strings.T("perk.node.lore_2.name"),
            "wide_1" => Strings.T("perk.node.wide_1.name"),
            "wide_2" => Strings.T("perk.node.wide_2.name"),
            "insight_1" => Strings.T("perk.node.insight_1.name"),
            "insight_2" => Strings.T("perk.node.insight_2.name"),
            "qi_1" => Strings.T("perk.node.qi_1.name"),
            "qi_2" => Strings.T("perk.node.qi_2.name"),
            // 跨树三个(2026-09-08)。少了这三条不是编译错,是节点面与详情弹窗标题
            // 直接印出英文 id「cross_vigor」——兜底那一支本来就是给「理论不可达」留的。
            "cross_vigor" => Strings.T("perk.node.cross_vigor.name"),
            "cross_edge" => Strings.T("perk.node.cross_edge.name"),
            "cross_draw" => Strings.T("perk.node.cross_draw.name"),
            _ => def.NodeKey, // 兜底:43 个 NodeKey 已穷举,理论不可达
        };

        /// <summary>效果描述里的 {value}:三段节点印**累计到本段**的值(第 2 段的金系暴击印 +10,
        /// 不是本段增量 +5),单段节点就是 <see cref="PerkNodeDef.Value"/>(2026-10-02)。</summary>
        private static int ShownValue(PerkNodeDef def) =>
            def.StageCount > 1 ? PerkRules.CumulativeValue(def) : def.Value;

        /// <summary>一句效果描述,数值一律从节点定义取、模板里用占位符(不硬编码效果值)。
        /// 被动树里同一枝三层效果同构、只是 Value 不同的,共用同一条模板 key——不是遗漏,
        /// 是同一句话配不同的数。
        ///
        /// 2026-10-02 层序:L1 新专精(三段)/ L2 本系效果值(三段)/ L3 专属机制 / L4 天花板。</summary>
        public static string Desc(PerkNodeDef def) => def.NodeKey switch
        {
            // 五行 L1(三段):各系专精,措辞各含自己的系名,不能共用模板
            "metal_1" => Strings.T("perk.node.metal_1.desc", ("value", ShownValue(def))),
            "wood_1" => Strings.T("perk.node.wood_1.desc", ("value", ShownValue(def))),
            "water_1" => Strings.T("perk.node.water_1.desc", ("value", ShownValue(def))),
            "fire_1" => Strings.T("perk.node.fire_1.desc", ("value", ShownValue(def))),
            "earth_1" => Strings.T("perk.node.earth_1.desc", ("value", ShownValue(def))),
            // 五行 L2(三段):该系字效果值 +N%
            "metal_2" => Strings.T("perk.node.metal_2.desc", ("value", ShownValue(def))),
            "wood_2" => Strings.T("perk.node.wood_2.desc", ("value", ShownValue(def))),
            "water_2" => Strings.T("perk.node.water_2.desc", ("value", ShownValue(def))),
            "fire_2" => Strings.T("perk.node.fire_2.desc", ("value", ShownValue(def))),
            "earth_2" => Strings.T("perk.node.earth_2.desc", ("value", ShownValue(def))),
            // 五行 L3:各系专属机制(spec 2026-09-13;2026-10-02 由 L2 挪来)
            "metal_3" => Strings.T("perk.node.metal_3.desc", ("value", def.Value)),
            "wood_3" => Strings.T("perk.node.wood_3.desc", ("value", def.Value)),
            "water_3" => Strings.T("perk.node.water_3.desc", ("value", def.Value)),
            // 火脉的 100 是「全额」,模板里没有 {value}(印成「转移 100%」反而费解)。
            "fire_3" => Strings.T("perk.node.fire_3.desc"),
            "earth_3" => Strings.T("perk.node.earth_3.desc", ("value", def.Value)),
            // 五行 L4:各系专属天花板,效果各不相同;择伐、燎原是开关,模板里没有 {value}
            "metal_4" => Strings.T("perk.node.metal_4.desc", ("value", def.Value)),
            "wood_4" => Strings.T("perk.node.wood_4.desc"),
            "water_4" => Strings.T("perk.node.water_4.desc", ("value", def.Value)),
            "fire_4" => Strings.T("perk.node.fire_4.desc"),
            "earth_4" => Strings.T("perk.node.earth_4.desc", ("value", def.Value)),
            // 被动树:同枝三层共用一条模板(2026-10-02 起不叠加,取已点最高档)
            "vigor_1" or "vigor_2" or "vigor_3" =>
                Strings.T("perk.node.vigor.desc", ("value", def.Value)),
            "power_1" or "power_2" or "power_3" =>
                Strings.T("perk.node.power.desc", ("value", def.Value)),
            "edge_1" or "edge_2" or "edge_3" =>
                Strings.T("perk.node.edge.desc", ("value", def.Value)),
            "guard_1" or "guard_2" or "guard_3" =>
                Strings.T("perk.node.guard.desc", ("value", def.Value),
                    ("percent", StatusText.DefenseToReductionPercent(def.Value))),
            // 机制树:博闻同枝两层同构共用模板;广纳/慧眼/调息两层措辞不同,各写各的
            "lore_1" or "lore_2" =>
                Strings.T("perk.info.effect.library", ("value", def.Value)),
            "wide_1" => Strings.T("perk.node.wide_1.desc", ("value", def.Value)),
            "wide_2" => Strings.T("perk.node.wide_2.desc", ("value", def.Value)),
            "insight_1" => Strings.T("perk.node.insight_1.desc", ("value", def.Value)),
            "insight_2" => Strings.T("perk.node.insight_2.desc", ("value", def.Value)),
            "qi_1" => Strings.T("perk.node.qi_1.desc", ("value", def.Value)),
            "qi_2" => Strings.T("perk.node.qi_2.desc", ("value", def.Value)),
            // 跨树三个:它们是**缩放器**,一句话里同时要保底值与每级增量,所以两个占位符
            // 都得给(其余节点只有 {value})。走 BaseValue/Value 而不是把算好的合计
            // 印上去 —— 合计随存档变,拆开的这两个数才是节点自身的定义(spec §5.1)。
            "cross_vigor" => Strings.T("perk.node.cross_vigor.desc",
                ("base", def.BaseValue), ("value", def.Value)),
            "cross_edge" => Strings.T("perk.node.cross_edge.desc",
                ("base", def.BaseValue), ("value", def.Value)),
            "cross_draw" => Strings.T("perk.node.cross_draw.desc",
                ("base", def.BaseValue), ("value", def.Value)),
            _ => Strings.T("perk.info.effect.generic", ("value", def.Value)), // 兜底,理论不可达
        };

        /// <summary>节点详情弹窗(<see cref="PerkNodeSheet"/>)的「说明」段:解释这条效果实际
        /// 意味着什么(review 举的例子:鏖战「战意每层 +10% 攻击,满层由 +50% 抬到 +70%。
        /// 战意只有金系字给得出,所以这一条不会外溢到别的流派」)。
        ///
        /// 节点按 <see cref="PerkEffect"/> 分类各写一条——同一种效果(如三枝被动树
        /// 各自的三层)结构完全相同、只是数值不同,共用一条说明比 40 条各写各的**更不容易过时**
        /// (卡面那句机械描述才需要每节点各写各的,这里不需要)。五行本系效果值
        /// (<see cref="PerkEffect.ElementEffectPercent"/>)横跨五个元素,
        /// 用 {element} 占位符填该系名词;五行 L1、L3、L4 五枝各不相同,
        /// 各占一条 PerkEffect,不共用模板(2026-10-02 层序)。
        ///
        /// ⚠ 跨树三条**按 id 取词、不走 <see cref="PerkEffect"/>**:它们复用了普通节点的效果类型
        /// (相济 = AttackPercent、融会 = CritChance、博采 = DrawRolls),按效果取会拿到被动树
        /// 那三条说明 —— 讲的是「固定百分比加成」,而跨树节点讲的是「按你已投资量放大」,
        /// 两件事。逐条字面 key,不拼 $"perk.detail.{def.Id}"(拼出来的 key 会被
        /// StringsTableTests 判成孤儿)。</summary>
        public static string DetailText(PerkNodeDef def) =>
            def.Tree == PerkTree.Cross ? CrossDetailText(def) : EffectDetailText(def);

        private static string CrossDetailText(PerkNodeDef def) => def.Id switch
        {
            "cross_vigor" => Strings.T("perk.detail.cross_vigor"),
            "cross_edge" => Strings.T("perk.detail.cross_edge"),
            "cross_draw" => Strings.T("perk.detail.cross_draw"),
            _ => Strings.T("perk.info.effect.generic", ("value", def.Value)), // 兜底,理论不可达(表里只有这三条跨树)
        };

        private static string EffectDetailText(PerkNodeDef def) => def.Effect switch
        {
            PerkEffect.MaxHp => Strings.T("perk.detail.max_hp"),
            PerkEffect.AttackPercent => Strings.T("perk.detail.attack_percent"),
            PerkEffect.CritChance => Strings.T("perk.detail.crit_chance"),
            PerkEffect.Defense => Strings.T("perk.detail.defense", ("value", def.Value),
                ("percent", StatusText.DefenseToReductionPercent(def.Value))),
            PerkEffect.LibraryCapacity => Strings.T("perk.detail.library_capacity"),
            PerkEffect.ElementEffectPercent => Strings.T("perk.detail.element_effect_percent",
                ("element", ElementNameOf(def)), ("value", ShownValue(def))),
            // 五行 L1(三段,2026-10-02):{value} 与卡面一样印累计到本段的值
            PerkEffect.ElementCritChance =>
                Strings.T("perk.detail.element_crit", ("value", ShownValue(def))),
            PerkEffect.SummonHpPercent =>
                Strings.T("perk.detail.summon_hp", ("value", ShownValue(def))),
            PerkEffect.HealPercent =>
                Strings.T("perk.detail.heal_percent", ("value", ShownValue(def))),
            PerkEffect.ShieldPercent =>
                Strings.T("perk.detail.shield_percent", ("value", ShownValue(def))),
            PerkEffect.EnemyBurnBonus =>
                Strings.T("perk.detail.enemy_burn_bonus", ("value", ShownValue(def))),
            // 五行 L3:五条各系专属机制(spec 2026-09-13)。**逐条字面量 key**,
            // 不要拼动态 key —— 拼出来的会被 StringsTableTests 判成孤儿。
            PerkEffect.OverhealDamagePercent =>
                Strings.T("perk.detail.overheal_damage", ("value", def.Value)),
            PerkEffect.BurnSpreadPercent => Strings.T("perk.detail.burn_spread"),
            PerkEffect.MoraleOnCrit =>
                Strings.T("perk.detail.morale_on_crit", ("value", def.Value)),
            PerkEffect.SummonDeathHealPercent =>
                Strings.T("perk.detail.summon_death_heal", ("value", def.Value)),
            PerkEffect.ShieldCarryPercent =>
                Strings.T("perk.detail.shield_carry", ("value", def.Value)),
            // 五行 L4
            PerkEffect.MoraleRelease =>
                Strings.T("perk.detail.morale_release", ("value", def.Value)),
            PerkEffect.CounterTargeting => Strings.T("perk.detail.counter_targeting"),
            PerkEffect.WellspringSpendPercent =>
                Strings.T("perk.detail.spend_wellspring", ("value", def.Value)),
            PerkEffect.BurnSpreadAdjacent => Strings.T("perk.detail.burn_spread_adjacent"),
            PerkEffect.HeftSpendPercent =>
                Strings.T("perk.detail.spend_heft", ("value", def.Value)),
            // 机制树(2026-10-02)
            PerkEffect.EmptyLibraryDraws =>
                Strings.T("perk.detail.empty_library_draws", ("value", def.Value)),
            PerkEffect.RewardOptions =>
                Strings.T("perk.detail.reward_options", ("value", def.Value)),
            PerkEffect.RewardRerolls =>
                Strings.T("perk.detail.reward_rerolls", ("value", def.Value)),
            PerkEffect.VictoryHealPercent =>
                Strings.T("perk.detail.victory_heal", ("value", def.Value)),
            // DrawRolls 只剩跨树博采在用,它走 CrossDetailText,到不了这里
            _ => Strings.T("perk.info.effect.generic", ("value", def.Value)), // 兜底,理论不可达
        };

        private static string ElementNameOf(PerkNodeDef def) =>
            def.Element is { } el ? CharInfo.ElementName(el) : "";
    }
}
