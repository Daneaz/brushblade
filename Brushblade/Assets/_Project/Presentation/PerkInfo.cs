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
    }
}
