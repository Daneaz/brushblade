using System.Text;
using Brushblade.Core;

namespace Brushblade.Presentation
{
    /// <summary>技能简述:从定义机械生成(效果短语 / 详情作用)。牌面与详情弹窗共用。</summary>
    public static class PerkInfo
    {
        /// <summary>牌面底部一行:每级效果短语(如「AP +1」)。</summary>
        public static string ShortEffect(PerkDef def) => def.Effect switch
        {
            PerkEffect.MaxHp => $"生命 +{def.PerLevelValue}",
            PerkEffect.Shield => $"护盾 +{def.PerLevelValue}",
            PerkEffect.Library => $"字库 +{def.PerLevelValue}",
            PerkEffect.Ap => $"AP +{def.PerLevelValue}",
            _ => "",
        };

        /// <summary>详情弹窗:类别 + 当前/上限等级 + 作用说明 + 具体数值。</summary>
        public static string Detail(PerkDef def, int level)
        {
            var text = new StringBuilder();
            text.Append('「').Append(def.Name).Append("」· ").Append(Category(def)).Append('\n');
            text.Append("Lv").Append(level).Append('/').Append(def.MaxLevel).Append('\n');
            text.Append(Action(def)).Append('\n');
            text.Append("每级 +").Append(def.PerLevelValue)
                .Append(" · 当前 +").Append(level * def.PerLevelValue);
            if (level < def.MaxLevel)
                text.Append('\n').Append("下一级 +").Append((level + 1) * def.PerLevelValue)
                    .Append(" · ").Append(def.InkCosts[level]).Append('墨');
            else
                text.Append('\n').Append("已满级");
            return text.ToString();
        }

        private static string Category(PerkDef def) => def.Effect switch
        {
            PerkEffect.MaxHp => "起始生命上限",
            PerkEffect.Shield => "段初护盾",
            PerkEffect.Library => "字库容量",
            PerkEffect.Ap => "每回合行动点",
            _ => "",
        };

        private static string Action(PerkDef def) => def.Effect switch
        {
            PerkEffect.MaxHp => "提升登塔时的最大生命上限。",
            PerkEffect.Shield => "每段战斗开局附带护盾。",
            PerkEffect.Library => "提升字库可持有字数上限。",
            PerkEffect.Ap => "提升每回合可用行动点(AP)上限。",
            _ => "",
        };
    }
}
