using System;

namespace Brushblade.Core
{
    /// <summary>五行相克结算。规则唯一来源:docs/design/wuxing-reference.md。
    ///
    /// ⚠ **相生 ×3 已于 2026-09-02 取消**(用户拍板)。全表 74 张字里只有 4 张吃得到
    /// (焚/蒸/沏/刲,土 0/13、木 0/14),是条空转规则:最大的乘区对两个系完全不可达。
    /// 那 4 张字的基础值已直接写成 ×3 后的实战值,战斗结果一分不差。
    /// 收益是配置值 = 实战值 —— 读字表不用再心算。</summary>
    public static class WuxingResolver
    {
        // 相克环:木克土,土克水,水克火,火克金,金克木(心不在环内)
        private static readonly System.Collections.Generic.Dictionary<Element, Element> Ke = new()
        {
            { Element.Wood, Element.Earth },
            { Element.Earth, Element.Water },
            { Element.Water, Element.Fire },
            { Element.Fire, Element.Metal },
            { Element.Metal, Element.Wood },
        };

        /// <summary>这一系克谁(心不在环内 → null)。卡组页详情印「克 X ×1.5」用。
        /// 从同一张 <c>Ke</c> 表读 —— 表现层另抄一份相克环,是这个项目栽过的那类分叉。</summary>
        public static Element? Victim(Element attacker) =>
            Ke.TryGetValue(attacker, out var victim) ? victim : (Element?)null;

        /// <summary>这一系被谁克(心不在环内 → null)。</summary>
        public static Element? Counter(Element defender)
        {
            foreach (var pair in Ke)
                if (pair.Value == defender)
                    return pair.Key;
            return null;
        }

        /// <summary>相克倍率:克制 1.5,被克 0.5,其余(含心)1.0。</summary>
        public static float KeMultiplier(Element attacker, Element defender)
        {
            if (Ke.TryGetValue(attacker, out var victim) && victim == defender)
                return 1.5f;
            if (Ke.TryGetValue(defender, out var counter) && counter == attacker)
                return 0.5f;
            return 1.0f;
        }

        /// <summary>效果结算:floor(基础值 × 相克)。</summary>
        public static int ResolveEffect(int baseValue, Element attacker, Element defender) =>
            (int)Math.Floor(baseValue * KeMultiplier(attacker, defender));

        /// <summary>无对抗目标的效果结算(护盾/治疗等)。相生取消后这是恒等函数,
        /// 保留它只为让调用点读起来仍然「过了一道五行结算」,将来要加别的规则有落点。</summary>
        public static int ResolveEffect(int baseValue) => baseValue;
    }
}
