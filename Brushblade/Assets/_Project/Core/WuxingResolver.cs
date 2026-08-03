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

        /// <summary>相生倍率「他生我」:配方原料里含生 <paramref name="self"/> 的属性 → 3,多个不叠乘;否则 1。
        /// 本字去生原料(我生他)不算 —— 如 灶(火系,火+土)的火生土。规格见 wuxing-reference.md。</summary>
        public static int ShengMultiplier(IEnumerable<Element> recipeElements, Element self)
        {
            foreach (var mother in recipeElements)
            {
                if (Sheng.TryGetValue(mother, out var child) && child == self)
                    return 3;
            }
            return 1;
        }

        /// <summary>效果结算:floor(基础值 × 相生 × 相克)。<paramref name="attacker"/> 即本字属性。</summary>
        public static int ResolveEffect(int baseValue, IEnumerable<Element> recipeElements, Element attacker, Element defender)
        {
            return (int)Math.Floor(
                baseValue * ShengMultiplier(recipeElements, attacker) * KeMultiplier(attacker, defender));
        }

        /// <summary>无对抗目标的效果结算(护盾/治疗等):floor(基础值 × 相生)。</summary>
        public static int ResolveEffect(int baseValue, IEnumerable<Element> recipeElements, Element self)
        {
            return baseValue * ShengMultiplier(recipeElements, self);
        }
    }
}
