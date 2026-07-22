using Brushblade.Core;
using UnityEngine;

namespace Brushblade.Presentation
{
    /// <summary>五行速查(2026-07-22):战斗页常驻小图标,点开看环图。
    /// 数值口径同 WuxingResolver(docs/design/wuxing-reference.md)。</summary>
    public static class WuxingChart
    {
        // 相克环:木克土,土克水,水克火,火克金,金克木(心不在环内)
        private static readonly Element[] KeChain =
            { Element.Wood, Element.Earth, Element.Water, Element.Fire, Element.Metal, Element.Wood };

        // 相生环:木生火,火生土,土生金,金生水,水生木
        private static readonly Element[] ShengChain =
            { Element.Wood, Element.Fire, Element.Earth, Element.Metal, Element.Water, Element.Wood };

        public static GameObject ShowKe(Transform root) => Show(root, "相 克",
            KeChain, "克",
            "出字属性克敌 → 伤害 ×1.5,被克 ×0.5,其余 ×1.0\n「心」不在环内,恒 ×1.0");

        public static GameObject ShowSheng(Transform root) => Show(root, "相 生",
            ShengChain, "生",
            "配方属性去重后含相生有序对 → 效果 ×3\n多对不叠乘,只算一次");

        private static GameObject Show(Transform root, string title, Element[] chain,
            string verb, string note)
        {
            var overlay = Ui.ModalShell(root, title, new Vector2(320, 150), dismissable: true, out var stack);

            var row = Ui.Row(stack, "Chain", 4);
            for (int i = 0; i < chain.Length; i++)
            {
                if (i > 0)
                    Ui.ThemedLabel(row.transform, verb, 14, Theme.TextDim);
                Ui.Chip(row.transform, CharInfo.ElementName(chain[i]),
                    Theme.ElementColor(chain[i]), Color.white, 17);
            }

            Ui.ThemedLabel(stack, note, 16, Theme.TextDim);
            Ui.PillButton(stack, "知道了", () => Object.Destroy(overlay),
                Theme.LockedBg, Theme.TextMain, 18, new Vector2(150, 46));
            return overlay;
        }
    }
}
