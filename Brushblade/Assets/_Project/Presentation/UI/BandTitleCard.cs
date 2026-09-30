using System.Collections;
using Brushblade.Core;
using Brushblade.Data;
using UnityEngine;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>进段标题卡(2026-09-30,十层一主题):踏入新层段时全屏淡入「段名 + 层数·属性 + 一句风味」,
    /// 停一拍自动淡出,点一下可跳过。不带任何奖励 —— 首破奖励在打赢本段主题 Boss 时发(安全层弹窗)。
    ///
    /// 盖在战斗视图最上层并吃掉点击:淡出前战场不可操作,免得玩家隔着卡片误出字。
    /// 用 unscaledDeltaTime:战斗加速改的是 timeScale,标题卡的节奏不该跟着变快。</summary>
    public sealed class BandTitleCard : MonoBehaviour
    {
        private const float FadeIn = 0.35f;
        private const float Hold = 1.6f;
        private const float FadeOut = 0.5f;

        private CanvasGroup _group;
        private bool _skipped;

        public static void Show(Transform root, BandDef band, int bossEvery)
        {
            var go = Ui.Panel(root, "BandTitleCard");
            go.AddComponent<LayoutElement>().ignoreLayout = true;
            Ui.Stretch((RectTransform)go.transform);
            go.transform.SetAsLastSibling();
            var image = go.AddComponent<Image>();
            image.color = new Color(Theme.PanelPaper.r, Theme.PanelPaper.g, Theme.PanelPaper.b, 0.94f);
            var card = go.AddComponent<BandTitleCard>();
            card._group = go.AddComponent<CanvasGroup>();
            card._group.alpha = 0f;
            var button = go.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = image;
            button.onClick.AddListener(() => card._skipped = true);

            var stack = Ui.VStack(go.transform, "Stack", 18);
            Ui.Stretch((RectTransform)stack.transform);
            stack.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;

            Ui.ThemedLabel(stack.transform, band.Name, 132, Theme.GlyphColor(band.Element), Theme.TitleFont);
            // 两支各写各的 Strings.T(字面量 key):StringsTableTests 只认紧跟在 T( 后面的字面量
            string subtitle = band.Element is { } element
                ? Strings.T("band.card.subtitle", ("from", band.FromDepth),
                    ("to", band.FromDepth + bossEvery * 2 - 1), ("element", CharInfo.ElementName(element)))
                : Strings.T("band.card.subtitle_mixed", ("from", band.FromDepth));
            Ui.ThemedLabel(stack.transform, subtitle, 30, Theme.TextMain);
            if (!string.IsNullOrEmpty(band.Flavor))
                Ui.ThemedLabel(stack.transform, band.Flavor, 26, Theme.TextDim);
            Ui.ThemedLabel(stack.transform, Strings.T("band.card.tap_to_skip"), 18, Theme.LockGray);

            card.StartCoroutine(card.Play());
        }

        private IEnumerator Play()
        {
            for (float t = 0f; t < FadeIn && !_skipped; t += Time.unscaledDeltaTime)
            {
                _group.alpha = t / FadeIn;
                yield return null;
            }
            _group.alpha = 1f;
            for (float t = 0f; t < Hold && !_skipped; t += Time.unscaledDeltaTime)
                yield return null;
            _group.blocksRaycasts = false;   // 淡出时就把战场还给玩家
            float from = _group.alpha;
            for (float t = 0f; t < FadeOut; t += Time.unscaledDeltaTime)
            {
                _group.alpha = from * (1f - t / FadeOut);
                yield return null;
            }
            Destroy(gameObject);
        }
    }
}
