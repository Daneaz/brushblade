using System;
using System.Collections.Generic;

namespace Brushblade.Core
{
    /// <summary>五行生克结算。规则唯一来源:docs/design/wuxing-reference.md。</summary>
    public static class WuxingResolver
    {
        // 相克环:木克土,土克水,水克火,火克金,金克木(心不在环内)
        private static readonly Dictionary<Element, Element> Ke = new()
        {
            { Element.Wood, Element.Earth },
            { Element.Earth, Element.Water },
            { Element.Water, Element.Fire },
            { Element.Fire, Element.Metal },
            { Element.Metal, Element.Wood },
        };

        // 相生环:木生火,火生土,土生金,金生水,水生木
        private static readonly Dictionary<Element, Element> Sheng = new()
        {
            { Element.Wood, Element.Fire },
            { Element.Fire, Element.Earth },
            { Element.Earth, Element.Metal },
            { Element.Metal, Element.Water },
            { Element.Water, Element.Wood },
        };

        /// <summary>相克倍率:克制 1.5,被克 0.5,其余(含心)1.0。</summary>
        public static float KeMultiplier(Element attacker, Element defender)
        {
            if (Ke.TryGetValue(attacker, out var victim) && victim == defender)
                return 1.5f;
            if (Ke.TryGetValue(defender, out var counter) && counter == attacker)
                return 0.5f;
            return 1.0f;
        }

        /// <summary>相生倍率:配方属性去重后含相生有序对 → 3,多对不叠乘;否则 1。</summary>
        public static int ShengMultiplier(IEnumerable<Element> recipeElements)
        {
            var set = new HashSet<Element>(recipeElements);
            foreach (var mother in set)
            {
                if (Sheng.TryGetValue(mother, out var child) && set.Contains(child))
                    return 3;
            }
            return 1;
        }

        /// <summary>效果结算:floor(基础值 × 相生 × 相克)。</summary>
        public static int ResolveEffect(int baseValue, IEnumerable<Element> recipeElements, Element attacker, Element defender)
        {
            return (int)Math.Floor(
                baseValue * ShengMultiplier(recipeElements) * KeMultiplier(attacker, defender));
        }

        /// <summary>无对抗目标的效果结算(护盾/治疗等):floor(基础值 × 相生)。</summary>
        public static int ResolveEffect(int baseValue, IEnumerable<Element> recipeElements)
        {
            return baseValue * ShengMultiplier(recipeElements);
        }
    }
}
