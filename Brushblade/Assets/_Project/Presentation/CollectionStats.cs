using System.Collections.Generic;
using Brushblade.Core;
using Brushblade.Data;
using UnityEngine;

namespace Brushblade.Presentation
{
    /// <summary>卡组详情的数值格(口径 = <c>docs/design/ui/scenes/StatMapping.dc.html</c>)。
    ///
    /// 字表里字卡本身**没有攻/血字段**,只有效果 —— 四格数值全是从 effects 现算的。
    /// 有则出、无则整格不占位:绝不显示「攻击 —」。焦(只有灼烧)这类字一格都不出,
    /// 全部让给下面的「字卡功能」整句。
    ///
    /// 数值已按 <see cref="MetaRules.ScaleByCardLevel"/> 缩放到当前卡等级 ——
    /// 与战斗结算取同一个函数,卡面上的数字就是这一级真正打出来的数。</summary>
    public static class CollectionStats
    {
        public readonly struct Stat
        {
            public readonly string Label;
            public readonly int Value;
            public readonly string Note;
            public readonly Color Color;

            public Stat(string label, int value, string note, Color color)
            {
                Label = label;
                Value = value;
                Note = note;
                Color = color;
            }
        }

        private const int MaxBoxes = 3;

        /// <summary>这张字召几只(不是召唤字返回 0)。详情页靠它决定要不要出「召唤」那一段,
        /// 以及后面几段的标题要不要点名「(召唤物)」。</summary>
        public static int SummonCount(CharDef def)
        {
            foreach (var effect in def.Effects)
                if (effect.Kind == EffectKind.Summon) return effect.SummonCount;
            return 0;
        }

        /// <summary>取这张字在 <paramref name="cardLevel"/> 级的数值格,至多三格。
        ///
        /// 双方向字(水/土)把 <see cref="CharDef.AttackEffects"/> 也扫进来 ——
        /// 那一面在游戏里是真能出的,只扫 Effects 会让「攻」那半在详情页彻底不可见
        /// (与 <see cref="CharInfo.EffectsText"/> 同一条理由)。同名格只取先出现的那个。</summary>
        public static List<Stat> Of(CharDef def, int cardLevel)
        {
            var stats = new List<Stat>();
            var seen = new HashSet<string>();
            Scan(def.Effects, cardLevel, stats, seen);
            Scan(def.AttackEffects, cardLevel, stats, seen);
            return stats;
        }

        private static void Scan(IReadOnlyList<EffectDef> effects, int cardLevel,
            List<Stat> stats, HashSet<string> seen)
        {
            foreach (var effect in effects)
            {
                if (stats.Count >= MaxBoxes) return;
                switch (effect.Kind)
                {
                    case EffectKind.DamageSingle:
                        Add(stats, seen, "collection.stat.attack",
                            Strings.T("collection.stat.attack"),
                            MetaRules.ScaleByCardLevel(effect.Value, cardLevel),
                            Strings.T("collection.stat.note.single"), Theme.GlyphColor(Element.Fire));
                        break;
                    case EffectKind.DamageAll:
                        Add(stats, seen, "collection.stat.attack",
                            Strings.T("collection.stat.attack"),
                            MetaRules.ScaleByCardLevel(effect.Value, cardLevel),
                            Strings.T("collection.stat.note.all"), Theme.GlyphColor(Element.Fire));
                        break;
                    case EffectKind.Summon:
                        // 召唤字是唯一同时有攻和血的一类,两格必然成对出现
                        Add(stats, seen, "collection.stat.attack",
                            Strings.T("collection.stat.attack"),
                            MetaRules.ScaleByCardLevel(effect.SummonAttack, cardLevel),
                            Strings.T("collection.stat.note.summon_hit"), Theme.GlyphColor(Element.Fire));
                        Add(stats, seen, "collection.stat.hp",
                            Strings.T("collection.stat.hp"),
                            MetaRules.ScaleByCardLevel(effect.Value, cardLevel),
                            Strings.T("collection.stat.note.summon_count", ("count", effect.SummonCount)),
                            Theme.GlyphColor(Element.Wood));
                        break;
                    case EffectKind.Shield:
                        Add(stats, seen, "collection.stat.shield",
                            Strings.T("collection.stat.shield"),
                            MetaRules.ScaleByCardLevel(effect.Value, cardLevel),
                            Strings.T("collection.stat.note.shield"), Theme.GlyphColor(Element.Earth));
                        break;
                    case EffectKind.HealSelf:
                    case EffectKind.HealAll:
                    case EffectKind.HealOverTime:
                        Add(stats, seen, "collection.stat.heal",
                            Strings.T("collection.stat.heal"),
                            MetaRules.ScaleByCardLevel(effect.Value, cardLevel),
                            Strings.T("collection.stat.note.heal"), Theme.GlyphColor(Element.Water));
                        break;
                }
            }
        }

        private static void Add(List<Stat> stats, HashSet<string> seen,
            string key, string label, int value, string note, Color color)
        {
            if (stats.Count >= MaxBoxes || !seen.Add(key)) return;
            stats.Add(new Stat(label, value, note, color));
        }
    }
}
