using System.Text;
using Brushblade.Core;

namespace Brushblade.Presentation
{
    /// <summary>字卡简述:从定义机械生成(拼音/释义/属性/稀有度/AP/效果/配方/相生)。</summary>
    public static class CharInfo
    {
        public static string Summary(CharDef def, RecipeGraph graph)
        {
            var text = new StringBuilder();
            text.Append('「').Append(def.Id).Append('」');
            if (def.Pinyin != null)
                text.Append(def.Pinyin).Append(' ');
            if (!string.IsNullOrEmpty(def.Gloss))
                text.Append(def.Gloss).Append('|');
            text.Append(RarityName(def.Rarity)).Append('·')
                .Append(def.Element is { } element ? ElementName(element) + "系" : "中性")
                .Append('·').Append(def.ApCost).Append("AP");

            if (!def.IsLeaf)
                text.Append("|配方:").Append(string.Join("+", def.Recipe));

            text.Append('|').Append(EffectsText(def));

            if (WuxingResolver.ShengMultiplier(graph.RecipeElements(def.Id)) == 3)
                text.Append("|相生:效果×3");

            return text.ToString();
        }

        private static string EffectsText(CharDef def)
        {
            if (def.Effects.Count == 0)
                return "无战斗效果(可兜底一击:单体3伤,或作合成材料)";

            var parts = new StringBuilder();
            for (int i = 0; i < def.Effects.Count; i++)
            {
                if (i > 0) parts.Append(',');
                var e = def.Effects[i];
                parts.Append(e.Kind switch
                {
                    EffectKind.DamageSingle => $"单体{e.Value}伤" + (e.DoubleVsBurning ? "(对灼烧目标翻倍)" : ""),
                    EffectKind.DamageAll => $"全体{e.Value}伤" + (e.DoubleVsBurning ? "(对灼烧目标翻倍)" : ""),
                    EffectKind.BurnSingle => $"单体灼烧+{e.Value}",
                    EffectKind.BurnAll => $"全体灼烧+{e.Value}",
                    EffectKind.Shield => $"护盾{e.Value}" + (e.PersistOnce ? "(豁免一次回合末清空)" : ""),
                    EffectKind.BurnPotency => $"本场灼烧每层结算+{e.Value}",
                    _ => e.Kind.ToString(),
                });
            }
            return parts.ToString();
        }

        public static string ElementName(Element element) => element switch
        {
            Element.Wood => "木",
            Element.Fire => "火",
            Element.Earth => "土",
            Element.Metal => "金",
            Element.Water => "水",
            Element.Heart => "心",
            _ => "?",
        };

        public static string RarityName(CardRarity rarity) => rarity switch
        {
            CardRarity.White => "白",
            CardRarity.Green => "绿",
            CardRarity.Blue => "蓝",
            CardRarity.Purple => "紫",
            CardRarity.Orange => "橙",
            CardRarity.Red => "红",
            _ => "?",
        };
    }
}
