using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using Brushblade.Data;
using UnityEngine;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>词条气泡(Plan E4,稿 <c>drafts/abilitytext</c>,附录 §C 4):点特性行 / 单术语数值格开一个
    /// 262pt 定宽的小气泡,列出这一行的全部术语(词名 + 说明)。
    ///
    /// · 父节点是弹窗的 overlay,不是行:两面区与右栏都在 ScrollList 里(有 Mask),挂在行下面会被裁掉。
    /// · 气泡下垫一层全屏透明「接点击」层,点任意处关(R8);同一时刻只开一个,点另一行要先关再开(C-Q10 接受两下)。
    /// · 默认挂在行下方、箭头对准行首;下方放不下翻到上方、箭头到底边(R10)。左右夹进 .safe 框(R11)。
    /// · 被点的行加 3 宽 ink-soft 内描环(R9),关气泡时一并销毁。
    /// · 阴影不做(C-Q9);箭头 = 17 见方转 45° 的小方块,外圈垫一块描边色的大一号方块。
    /// 只读:战斗长按版也能点,点了只开气泡,不出字(C-Q1)。</summary>
    public static class GlossaryBubble
    {
        // 稿 pt × 2.093(traits.css .gl / CardSheet.dc.html)
        private const float Width = 548f;        // 262pt(abilitytext 稿:248 → 262,给图标让位)
        private const int PadV = 17, PadH = 21;  // 8 / 10pt
        private const float Gap = 13f;           // 行距 6pt
        private const int TitleFont = 17;        // 8pt
        private const int NameFont = 23;         // 11pt 宋体
        private const int DescFont = 20;         // 9.5pt
        private const float ChipSize = 27f;      // 13pt
        private const float ChipGap = 13f;       // 6pt
        private const int AliasFont = 17;        // 8pt
        private const float ColGap = 13f;        // 词名与说明之间
        private const float Offset = 17f;        // 与行的间距 8pt
        private const float ArrowSize = 17f;     // 8pt
        private const float ArrowBorder = 2f;
        private const int Radius = 19;           // 9pt
        private const int RingThickness = 3;     // 1.5pt
        private const float ArrowInsetMin = 25f, ArrowInsetMax = 460f;   // 12 / 220pt
        private const float ArrowLead = 50f;     // 箭头对准行首往右 24pt

        private static GameObject _root;
        private static GameObject _ring;

        /// <summary>开气泡。<paramref name="host"/> = 弹窗 overlay(全屏);<paramref name="target"/> = 被点的行 / 格;
        /// <paramref name="radius"/> = 那一行的圆角(选中描环跟着它);<paramref name="title"/> = 行名或格名。</summary>
        public static void Show(RectTransform host, RectTransform target, int radius, string title,
            IReadOnlyList<GlossaryTerm> terms, Element? element = null)
        {
            Close();
            if (host == null || target == null || terms == null || terms.Count == 0) return;

            // 选中描环:挂在行上(随行滚动 / 裁剪),不参与布局
            _ring = Ui.Panel(target, "GlossaryRing");
            _ring.AddComponent<LayoutElement>().ignoreLayout = true;
            var ringImage = _ring.AddComponent<Image>();
            ringImage.sprite = Theme.RoundedRing(radius, RingThickness);
            ringImage.type = Image.Type.Sliced;
            ringImage.color = Theme.InkSoft;
            ringImage.raycastTarget = false;
            Ui.Stretch((RectTransform)_ring.transform);

            // 接点击层(全屏透明):气泡与箭头都是它的子节点,点在气泡上同样冒泡到这里 → 关
            _root = new GameObject("GlossaryBubble", typeof(RectTransform), typeof(Image));
            _root.transform.SetParent(host, false);
            Ui.Stretch((RectTransform)_root.transform);
            var catcher = _root.GetComponent<Image>();
            catcher.color = new Color(0f, 0f, 0f, 0f);
            var button = _root.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = catcher;
            button.onClick.AddListener(Close);

            // ---- 先算高(纯函数),再摆位 ----
            var names = terms.Select(GlossaryText.Name).ToList();
            var aliases = terms.Select(t => GlossaryText.AliasShown(t) is { } a ? Strings.T("glossary.alias", ("alias", a)) : null).ToList();
            var descs = terms.Select(GlossaryText.Desc).ToList();
            float NameW(int i) => Ui.EstimateWidth(names[i], NameFont)
                + (aliases[i] != null ? 4f + Ui.EstimateWidth(aliases[i], AliasFont) : 0f);
            float nameCol = Enumerable.Range(0, terms.Count).Max(NameW);
            float descW = Width - 2f * PadH - ChipSize - ChipGap - nameCol - ChipGap;
            float nameLineH = Mathf.Ceil(NameFont * 1.35f);
            float titleH = Mathf.Ceil(TitleFont * 1.35f);
            var rowHs = descs.Select(d => Mathf.Max(nameLineH, Ui.WrappedTextHeight(d, DescFont, descW))).ToList();
            float height = 2f * PadV + titleH + rowHs.Sum() + Gap * terms.Count;

            var hostRect = host.rect;
            var corners = new Vector3[4];
            target.GetWorldCorners(corners);   // 0 左下、1 左上、2 右上、3 右下
            Vector2 bl = (Vector2)host.InverseTransformPoint(corners[0]) - hostRect.center;
            Vector2 tl = (Vector2)host.InverseTransformPoint(corners[1]) - hostRect.center;
            // 以下坐标都相对 host 中心(锚点放在中心)
            float halfW = hostRect.width / 2f, halfH = hostRect.height / 2f;
            float frame = Mathf.Min(hostRect.width, SafeArea.FrameWidth());
            float left = Mathf.Clamp(tl.x, -frame / 2f, frame / 2f - Width);
            float bottomLine = -halfH + SafeArea.BottomInset;
            float top = bl.y - Offset;
            bool flip = top - height < bottomLine;
            if (flip) top = Mathf.Min(tl.y + Offset + height, halfH);

            float arrowX = left + Mathf.Clamp(tl.x + ArrowLead - left, ArrowInsetMin, ArrowInsetMax);
            float arrowY = flip ? top - height : top;
            // 描边色的大一号方块垫在气泡下;气泡盖住它的内半边,外半边露出一圈描线
            Arrow(_root.transform, "ArrowBorder", new Vector2(arrowX, arrowY), ArrowSize + 2f * ArrowBorder, Theme.PanelBorder);

            var panel = Ui.OutlinedPanel(_root.transform, "Bubble", Theme.PanelPaper, Theme.PanelBorder, Radius, 2f);
            var rect = (RectTransform)panel.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(left, top);
            rect.sizeDelta = new Vector2(Width, height);

            // 纸色小方块盖在气泡描边上,把箭头根部的那段边线抹掉
            Arrow(_root.transform, "ArrowFill", new Vector2(arrowX, arrowY), ArrowSize, Theme.PanelPaper);

            var stack = Ui.VStack(panel.transform, "Stack", Gap);
            var layout = stack.GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.padding = new RectOffset(PadH, PadH, PadV, PadV);
            Ui.Stretch((RectTransform)stack.transform);

            var titleLabel = Ui.ThemedLabel(stack.transform, Strings.T("glossary.title", ("name", title)), TitleFont, Theme.LockGray);
            titleLabel.alignment = TextAnchor.MiddleLeft;
            Ui.Sized(titleLabel.gameObject, Width - 2f * PadH, titleH);

            Color nameColor = Theme.ElementSoftFg(element);   // 词名 = 正文里的词条色(本系 -fg)
            for (int i = 0; i < terms.Count; i++)
            {
                var row = Ui.Row(stack.transform, "Term", ChipGap);
                row.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.UpperLeft;
                Ui.Sized(row, Width - 2f * PadH, rowHs[i]);
                // 图标 chip(无图标留 13pt 空位,名字对齐)
                if (GlossaryRules.ChipOf(terms[i]) is { } chip) CardSheetParts.IconChip(row.transform, chip.Icon, chip.Tone, ChipSize);
                else Ui.Sized(Ui.Panel(row.transform, "ChipBlank"), ChipSize, ChipSize);

                var nameCell = Ui.Row(row.transform, "Name", 4f);
                nameCell.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.UpperLeft;
                Ui.Sized(nameCell, nameCol, nameLineH);
                var name = Ui.ThemedLabel(nameCell.transform, names[i], NameFont, nameColor, Theme.TitleFont);
                name.fontStyle = FontStyle.Bold;
                name.alignment = TextAnchor.UpperLeft;
                if (aliases[i] != null)
                {
                    var alias = Ui.ThemedLabel(nameCell.transform, aliases[i], AliasFont, Theme.LockGray);
                    alias.alignment = TextAnchor.MiddleLeft;
                }

                var desc = Ui.ThemedLabel(row.transform, descs[i], DescFont, Theme.TextQuiet);
                desc.alignment = TextAnchor.UpperLeft;
                desc.horizontalOverflow = HorizontalWrapMode.Wrap;
                Ui.Sized(desc.gameObject, descW, rowHs[i]);
            }
        }

        /// <summary>关气泡并撤掉选中描环。弹窗整体销毁时两者随之销毁(Unity 假 null),这里判空即可。</summary>
        public static void Close()
        {
            if (_root != null) Object.Destroy(_root);
            if (_ring != null) Object.Destroy(_ring);
            _root = null;
            _ring = null;
        }

        private static void Arrow(Transform parent, string name, Vector2 center, float size, Color color)
        {
            var rect = PlaceBox(parent, name, center, size);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            rect.localRotation = Quaternion.Euler(0f, 0f, 45f);
        }

        private static RectTransform PlaceBox(Transform parent, string name, Vector2 center, float size)
        {
            var rect = (RectTransform)Ui.Panel(parent, name).transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = center;
            rect.sizeDelta = new Vector2(size, size);
            return rect;
        }
    }
}
