using System.Text;
using Brushblade.Core;

namespace Brushblade.Presentation
{
    /// <summary>字卡简述:从定义机械生成(拼音/释义/属性/稀有度/AP/效果/配方/相生)。</summary>
    public static class CharInfo
    {
        /// <summary>cardLevel:局外卡等级,效果数值按 MetaRules.ScaleByCardLevel 缩放后显示,
        /// 与战斗结算取同一函数(2026-07-20:此前恒显示基础值,升级看不出变化)。</summary>
        public static string Summary(CharDef def, RecipeGraph graph, int cardLevel = 1)
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

            if (cardLevel > 1)
                text.Append("|Lv.").Append(cardLevel);

            text.Append('|').Append(EffectsText(def, cardLevel));

            if (WuxingResolver.ShengMultiplier(graph.RecipeElements(def.Id)) == 3)
                text.Append("|相生:效果×3");

            return text.ToString();
        }

        /// <summary>详情弹窗用:Summary 的分行版。</summary>
        public static string Detail(CharDef def, RecipeGraph graph, int cardLevel = 1) =>
            Summary(def, graph, cardLevel).Replace("|", "\n");

        /// <summary>效果串(升级 preview 取前后两级各调一次)。</summary>
        public static string EffectsText(CharDef def, int cardLevel = 1)
        {
            if (def.Effects.Count == 0)
                return "无战斗效果(可兜底一击:单体3伤,或作合成材料)";

            var parts = new StringBuilder();
            for (int i = 0; i < def.Effects.Count; i++)
            {
                if (i > 0) parts.Append(',');
                var e = def.Effects[i];
                int v = MetaRules.ScaleByCardLevel(e.Value, cardLevel);
                parts.Append(e.Kind switch
                {
                    EffectKind.DamageSingle => $"单体{v}伤" + (e.DoubleVsBurning ? "(对灼烧目标翻倍)" : ""),
                    EffectKind.DamageAll => $"全体{v}伤" + (e.DoubleVsBurning ? "(对灼烧目标翻倍)" : ""),
                    EffectKind.BurnSingle => $"单体灼烧+{v}",
                    EffectKind.BurnAll => $"全体灼烧+{v}",
                    EffectKind.Shield => $"护盾{v}" + (e.PersistOnce ? "(豁免一次回合末清空)" : ""),
                    EffectKind.BurnPotency => $"本场灼烧每层结算+{v}",
                    EffectKind.HealSelf => $"治疗{v}",
                    EffectKind.Summon => $"召{e.SummonCount}×「{e.SummonChar}」" +
                        $"(血{v}攻{MetaRules.ScaleByCardLevel(e.SummonAttack, cardLevel)},顶前排)",
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
