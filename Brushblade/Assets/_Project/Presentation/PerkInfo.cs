using Brushblade.Core;
using Brushblade.Data;

namespace Brushblade.Presentation
{
    /// <summary>技能节点的文案:名称 + 一句效果描述。逐节点/逐枝显式落到字符串表的字面 key ——
    /// <see cref="StringsTableTests"/> 的对账只认字面量,`Strings.T($"...")` 这种动态 key
    /// 会被判孤儿(见 CLAUDE.md「字符串表检查只认字面量」)。40 个节点因此是 40 个 case,
    /// 不是循环拼 key —— 啰嗦但每一条都过得了对账。</summary>
    public static class PerkInfo
    {
        /// <summary>节点名(如「披沙」)。</summary>
        public static string Name(PerkNodeDef def) => def.Id switch
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
            _ => def.Id, // 兜底:40 个 id 已穷举,理论不可达
        };

        /// <summary>一句效果描述,数值一律从 <see cref="PerkNodeDef.Value"/> 取、模板里用占位符
        /// (不硬编码效果值)。被动/机制树里同一枝三层(或两层)效果同构、只是 Value 不同的,
        /// 共用同一条模板 key——不是遗漏,是同一句话配不同的数。</summary>
        public static string Desc(PerkNodeDef def) => def.Id switch
        {
            // 五行 L1:该系起手格抽取次数(效果同构,措辞各含自己的系名,不能共用模板)
            "metal_1" => Strings.T("perk.node.metal_1.desc", ("value", def.Value)),
            "wood_1" => Strings.T("perk.node.wood_1.desc", ("value", def.Value)),
            "water_1" => Strings.T("perk.node.water_1.desc", ("value", def.Value)),
            "fire_1" => Strings.T("perk.node.fire_1.desc", ("value", def.Value)),
            "earth_1" => Strings.T("perk.node.earth_1.desc", ("value", def.Value)),
            // 五行 L2:战利品候选保底
            "metal_2" => Strings.T("perk.node.metal_2.desc", ("value", def.Value)),
            "wood_2" => Strings.T("perk.node.wood_2.desc", ("value", def.Value)),
            "water_2" => Strings.T("perk.node.water_2.desc", ("value", def.Value)),
            "fire_2" => Strings.T("perk.node.fire_2.desc", ("value", def.Value)),
            "earth_2" => Strings.T("perk.node.earth_2.desc", ("value", def.Value)),
            // 五行 L3:该系字效果值 +N%
            "metal_3" => Strings.T("perk.node.metal_3.desc", ("value", def.Value)),
            "wood_3" => Strings.T("perk.node.wood_3.desc", ("value", def.Value)),
            "water_3" => Strings.T("perk.node.water_3.desc", ("value", def.Value)),
            "fire_3" => Strings.T("perk.node.fire_3.desc", ("value", def.Value)),
            "earth_3" => Strings.T("perk.node.earth_3.desc", ("value", def.Value)),
            // 五行 L4:各系专属天花板,效果各不相同
            "metal_4" => Strings.T("perk.node.metal_4.desc", ("value", def.Value)),
            "wood_4" => Strings.T("perk.node.wood_4.desc", ("value", def.Value)),
            "water_4" => Strings.T("perk.node.water_4.desc", ("value", def.Value)),
            "fire_4" => Strings.T("perk.node.fire_4.desc", ("value", def.Value)),
            "earth_4" => Strings.T("perk.node.earth_4.desc", ("value", def.Value)),
            // 被动树:同枝三层共用一条模板
            "vigor_1" or "vigor_2" or "vigor_3" =>
                Strings.T("perk.info.effect.max_hp", ("value", def.Value)),
            "power_1" or "power_2" or "power_3" =>
                Strings.T("perk.node.power.desc", ("value", def.Value)),
            "edge_1" or "edge_2" or "edge_3" =>
                Strings.T("perk.node.edge.desc", ("value", def.Value)),
            "guard_1" or "guard_2" or "guard_3" =>
                Strings.T("perk.node.guard.desc", ("value", def.Value)),
            // 机制树:博闻/广纳/一气同枝两层同构共用模板;慧眼两层效果不同,各写各的
            "lore_1" or "lore_2" =>
                Strings.T("perk.info.effect.library", ("value", def.Value)),
            "wide_1" or "wide_2" =>
                Strings.T("perk.node.wide.desc", ("value", def.Value)),
            "insight_1" => Strings.T("perk.node.insight_1.desc", ("value", def.Value)),
            "insight_2" => Strings.T("perk.node.insight_2.desc", ("value", def.Value)),
            "qi_1" or "qi_2" =>
                Strings.T("perk.info.effect.ap", ("value", def.Value)),
            _ => Strings.T("perk.info.effect.generic", ("value", def.Value)), // 兜底,理论不可达
        };

        /// <summary>节点详情弹窗(<see cref="PerkNodeSheet"/>)的「说明」段:解释这条效果实际
        /// 意味着什么(review 举的例子:鏖战「战意每层 +10% 攻击,满层由 +50% 抬到 +70%。
        /// 战意只有金系字给得出,所以这一条不会外溢到别的流派」)。
        ///
        /// 40 个节点按 <see cref="PerkEffect"/> 分类只写 17 条——同一种效果(如三枝被动树
        /// 各自的三层)结构完全相同、只是数值不同,共用一条说明比 40 条各写各的**更不容易过时**
        /// (卡面那句机械描述才需要每节点各写各的,这里不需要)。三条五行 L1/L2/L3
        /// (<see cref="PerkEffect.ElementDrawRolls"/> 等)横跨五个元素,用 {element} 占位符
        /// 填该系名词,而不是拆成五条元素各写各的。</summary>
        public static string DetailText(PerkNodeDef def) => def.Effect switch
        {
            PerkEffect.MaxHp => Strings.T("perk.detail.max_hp"),
            PerkEffect.AttackPercent => Strings.T("perk.detail.attack_percent"),
            PerkEffect.CritChance => Strings.T("perk.detail.crit_chance"),
            PerkEffect.Defense => Strings.T("perk.detail.defense"),
            PerkEffect.LibraryCapacity => Strings.T("perk.detail.library_capacity"),
            PerkEffect.StartingCards => Strings.T("perk.detail.starting_cards"),
            PerkEffect.DrawRolls => Strings.T("perk.detail.draw_rolls", ("value", def.Value)),
            PerkEffect.LootDrawRolls => Strings.T("perk.detail.loot_draw_rolls", ("value", def.Value)),
            PerkEffect.Ap => Strings.T("perk.detail.ap", ("value", def.Value)),
            PerkEffect.ElementDrawRolls => Strings.T("perk.detail.element_draw_rolls",
                ("element", ElementNameOf(def)), ("value", def.Value)),
            PerkEffect.ElementLootGuarantee => Strings.T("perk.detail.element_loot_guarantee",
                ("element", ElementNameOf(def))),
            PerkEffect.ElementEffectPercent => Strings.T("perk.detail.element_effect_percent",
                ("element", ElementNameOf(def)), ("value", def.Value)),
            PerkEffect.MoraleCap => MoraleCapText(def),
            PerkEffect.SummonSpeed => SummonSpeedText(def),
            PerkEffect.WellspringCap => WellspringCapText(def),
            PerkEffect.BurnPerStack => BurnPerStackText(def),
            PerkEffect.HeftCap => HeftCapText(def),
            _ => Strings.T("perk.info.effect.generic", ("value", def.Value)), // 兜底,理论不可达(17 个 case 已穷举 PerkEffect 全部成员)
        };

        private static string ElementNameOf(PerkNodeDef def) =>
            def.Element is { } el ? CharInfo.ElementName(el) : "";

        // 下面五个:「加成前 → 加成后」的每一个数字都从 BattleConfig 的常量 + def.Value 现算,
        // 不手算焊死(2026-09-07 收尾波修复项——review 抓到旧版把换算结果焊成字面文本,
        // 策划调 Core/Perk.cs 里 AddWuxing 的数值会被弹窗静默显示旧数字)。

        private static string MoraleCapText(PerkNodeDef def)
        {
            int b = BattleConfig.BaseMoraleCap;
            int after = b + def.Value;
            int rate = BattleConfig.MoralePercentPerStack;
            return Strings.T("perk.detail.morale_cap",
                ("rate", rate), ("base", b), ("after", after),
                ("basePct", b * rate), ("afterPct", after * rate));
        }

        private static string HeftCapText(PerkNodeDef def)
        {
            int b = BattleConfig.BaseHeftCap;
            int after = b + def.Value;
            int rate = BattleConfig.HeftPercentPerStack;
            return Strings.T("perk.detail.heft_cap",
                ("base", b), ("after", after), ("basePct", b * rate), ("afterPct", after * rate));
        }

        private static string WellspringCapText(PerkNodeDef def)
        {
            int b = BattleConfig.BaseWellspringCap;
            int after = b + def.Value;
            int rate = BattleConfig.WellspringPercentPerStack;
            return Strings.T("perk.detail.wellspring_cap",
                ("base", b), ("after", after), ("basePct", b * rate), ("afterPct", after * rate));
        }

        private static string BurnPerStackText(PerkNodeDef def)
        {
            int b = BattleConfig.BaseBurnPerStack;
            int after = b + def.Value;
            double multiplier = (double)after / b;
            return Strings.T("perk.detail.burn_per_stack",
                ("base", b), ("after", after), ("value", def.Value),
                ("multiplier", multiplier.ToString("0.0")));
        }

        private static string SummonSpeedText(PerkNodeDef def)
        {
            int b = BattleConfig.BaseSummonSpeed;
            int after = b + def.Value;
            return Strings.T("perk.detail.summon_speed",
                ("value", def.Value), ("base", b), ("after", after));
        }
    }
}
