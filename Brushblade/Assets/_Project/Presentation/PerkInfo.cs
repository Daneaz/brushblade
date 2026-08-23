using System.Text;
using Brushblade.Core;
using Brushblade.Data;

namespace Brushblade.Presentation
{
    /// <summary>技能简述:从定义机械生成(效果短语 / 详情作用)。牌面与详情弹窗共用。</summary>
    public static class PerkInfo
    {
        /// <summary>牌面底部一行:每级效果短语(如「AP +1」)。</summary>
        public static string ShortEffect(PerkDef def) => def.Effect switch
        {
            PerkEffect.MaxHp => Strings.T("perk.info.effect.max_hp", ("value", def.PerLevelValue)),
            PerkEffect.Shield => Strings.T("perk.info.effect.shield", ("value", def.PerLevelValue)),
            PerkEffect.Library => Strings.T("perk.info.effect.library", ("value", def.PerLevelValue)),
            PerkEffect.Ap => Strings.T("perk.info.effect.ap", ("value", def.PerLevelValue)),
            _ => "",
        };

        /// <summary>详情弹窗:类别 + 当前/上限等级 + 作用说明 + 具体数值。</summary>
        public static string Detail(PerkDef def, int level)
        {
            var text = new StringBuilder();
            text.Append('「').Append(def.Name).Append("」· ").Append(Category(def)).Append('\n');
            text.Append("Lv").Append(level).Append('/').Append(def.MaxLevel).Append('\n');
            text.Append(Action(def)).Append('\n');
            text.Append(Strings.T("perk.info.detail.per_level_current",
                ("perLevel", def.PerLevelValue), ("current", level * def.PerLevelValue)));
            if (level < def.MaxLevel)
                text.Append('\n').Append(Strings.T("perk.info.detail.next_level",
                        ("nextValue", (level + 1) * def.PerLevelValue), ("cost", def.InkCosts[level])))
                    .Append(Strings.T("perk.info.unit.ink"));
            else
                text.Append('\n').Append(Strings.T("perk.info.detail.max_level"));
            return text.ToString();
        }

        private static string Category(PerkDef def) => def.Effect switch
        {
            PerkEffect.MaxHp => Strings.T("perk.info.category.max_hp"),
            PerkEffect.Shield => Strings.T("perk.info.category.shield"),
            PerkEffect.Library => Strings.T("perk.info.category.library"),
            PerkEffect.Ap => Strings.T("perk.info.category.ap"),
            _ => "",
        };

        private static string Action(PerkDef def) => def.Effect switch
        {
            PerkEffect.MaxHp => Strings.T("perk.info.action.max_hp"),
            PerkEffect.Shield => Strings.T("perk.info.action.shield"),
            PerkEffect.Library => Strings.T("perk.info.action.library"),
            PerkEffect.Ap => Strings.T("perk.info.action.ap"),
            _ => "",
        };
    }
}
