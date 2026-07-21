using Brushblade.Core;
using UnityEngine;

namespace Brushblade.Presentation
{
    /// <summary>字卡详情弹窗(2026-07-21):放大卡面 + 等级化数值,收集/商城/战斗/战利品四处共用。</summary>
    public static class CharPreview
    {
        public static GameObject Show(Transform root, CharDef def, RecipeGraph graph, int cardLevel = 1)
        {
            var overlay = Ui.ModalShell(root, cardLevel > 1 ? $"字卡 Lv.{cardLevel}" : "字卡",
                new Vector2(330, 250), dismissable: true, out var stack);
            Ui.GlyphTile(stack, def, $"{def.ApCost}AP", false, null, new Vector2(150, 180));
            Ui.ThemedLabel(stack, CharInfo.Detail(def, graph, cardLevel), 17, Theme.TextDim);
            Ui.PillButton(stack, "知道了", () => Object.Destroy(overlay),
                Theme.LockedBg, Theme.TextMain, 18, new Vector2(150, 48));
            return overlay;
        }
    }
}
