using System;
using UnityEngine;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>代码驱动 uGUI 的构建工具。原型期不做美术,布局全走 LayoutGroup。</summary>
    public static class Ui
    {
        private static Font _font;

        /// <summary>CJK 可用的动态字体:默认字体无中文字形,从系统字体加载。</summary>
        public static Font Font
        {
            get
            {
                if (_font != null) return _font;
                // 覆盖 iOS/macOS(PingFang)、Windows(YaHei)、Android(Noto/Droid);
                // 真机若仍缺字形,则内嵌开源 CJK 字体子集(移动端适配 TODO)
                foreach (var name in new[] { "PingFang SC", "Microsoft YaHei", "Noto Sans CJK SC",
                    "Noto Sans SC", "Source Han Sans SC", "Droid Sans Fallback", "Hiragino Sans GB" })
                {
                    _font = UnityEngine.Font.CreateDynamicFontFromOSFont(name, 28);
                    if (_font != null) return _font;
                }
                _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return _font;
            }
        }

        public static GameObject Panel(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        public static GameObject Row(Transform parent, string name, float spacing = 8)
        {
            var go = Panel(parent, name);
            var layout = go.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            return go;
        }

        public static Text Label(Transform parent, string text, int size = 24, TextAnchor align = TextAnchor.MiddleCenter)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var label = go.AddComponent<Text>();
            label.font = Font;
            label.fontSize = size;
            label.text = text;
            label.alignment = align;
            label.color = Theme.TextMain;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            return label;
        }

        public static Button TextButton(Transform parent, string text, Action onClick,
            Color? background = null, int fontSize = 26, Vector2? size = null)
        {
            var go = new GameObject("Button", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.color = background ?? new Color(0.22f, 0.22f, 0.28f);

            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            if (onClick != null) button.onClick.AddListener(() => onClick());

            var label = Label(go.transform, text, fontSize);
            label.color = Color.white;
            Stretch(label.rectTransform);

            var element = go.AddComponent<LayoutElement>();
            var s = size ?? new Vector2(120, 64);
            element.preferredWidth = s.x;
            element.preferredHeight = s.y;
            return button;
        }

        public static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        public static void Anchor(RectTransform rect, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }

        /// <summary>稀有度底色(白绿蓝紫橙红,19.3.1)——设计板色值。</summary>
        public static Color RarityColor(Brushblade.Core.CardRarity rarity) => Theme.RarityColor(rarity);

        public static void Clear(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(parent.GetChild(i).gameObject);
        }

        // ================= 设计板主题原语(2026-07-12) =================

        public static Text ThemedLabel(Transform parent, string text, int size, Color color,
            Font font = null, TextAnchor align = TextAnchor.MiddleCenter)
        {
            var label = Label(parent, text, size, align);
            label.color = color;
            label.font = font ?? Theme.BodyFont;
            return label;
        }

        /// <summary>白底圆角卡(调用方自行 Anchor/LayoutElement)。</summary>
        public static Image CardPanel(Transform parent, string name, Color? color = null, int radius = 20)
        {
            var go = Panel(parent, name);
            var image = go.AddComponent<Image>();
            image.sprite = Theme.Rounded(radius);
            image.type = Image.Type.Sliced;
            image.color = color ?? Theme.CardWhite;
            return image;
        }

        public static Button RoundButton(Transform parent, string text, Action onClick,
            Color bg, Color fg, int fontSize = 22, Vector2? size = null, int radius = 10)
        {
            var go = new GameObject("Button", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.sprite = Theme.Rounded(radius);
            image.type = Image.Type.Sliced;
            image.color = bg;
            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            if (onClick != null) button.onClick.AddListener(() => onClick());
            var label = ThemedLabel(go.transform, text, fontSize, fg);
            Stretch(label.rectTransform);
            var element = go.AddComponent<LayoutElement>();
            var s = size ?? new Vector2(120, 56);
            element.preferredWidth = s.x;
            element.preferredHeight = s.y;
            return button;
        }

        public static Button PillButton(Transform parent, string text, Action onClick,
            Color bg, Color fg, int fontSize = 22, Vector2? size = null) =>
            RoundButton(parent, text, onClick, bg, fg, fontSize, size, 24);

        /// <summary>胶囊小标签(宽度按 CJK 字宽估算)。</summary>
        public static GameObject Chip(Transform parent, string text, Color bg, Color fg, int fontSize = 14)
        {
            var go = Panel(parent, "Chip");
            var image = go.AddComponent<Image>();
            image.sprite = Theme.Rounded(14);
            image.type = Image.Type.Sliced;
            image.color = bg;
            var label = ThemedLabel(go.transform, text, fontSize, fg);
            Stretch(label.rectTransform);
            var element = go.AddComponent<LayoutElement>();
            element.preferredWidth = text.Length * fontSize + 18;
            element.preferredHeight = fontSize + 12;
            return go;
        }

        /// <summary>进度条:PaperDim 底 + 填充色,圆角胶囊。</summary>
        public static void Bar(Transform parent, float frac, Color fill, Vector2 size)
        {
            var back = Panel(parent, "Bar");
            var backImage = back.AddComponent<Image>();
            backImage.sprite = Theme.Rounded(10);
            backImage.type = Image.Type.Sliced;
            backImage.color = Theme.PaperDim;
            var element = back.AddComponent<LayoutElement>();
            element.preferredWidth = size.x;
            element.preferredHeight = size.y;

            var front = Panel(back.transform, "Fill");
            var frontImage = front.AddComponent<Image>();
            frontImage.sprite = Theme.Rounded(10);
            frontImage.type = Image.Type.Sliced;
            frontImage.color = fill;
            Anchor((RectTransform)front.transform, Vector2.zero,
                new Vector2(Mathf.Clamp01(frac), 1), Vector2.zero, Vector2.zero);
        }

        /// <summary>墨锭图标 + 文本(gold=true 用于价格标签)。</summary>
        public static GameObject IngotLabel(Transform parent, string text, int fontSize = 20, bool gold = false)
        {
            var row = Row(parent, "Ingot", 6);
            var icon = Panel(row.transform, "Icon");
            var image = icon.AddComponent<Image>();
            image.sprite = Theme.Ingot;
            image.color = gold ? Theme.IngotGold : Theme.IngotDark;
            var iconElement = icon.AddComponent<LayoutElement>();
            iconElement.preferredWidth = fontSize * 1.4f;
            iconElement.preferredHeight = fontSize * 0.85f;
            ThemedLabel(row.transform, text, fontSize, Theme.TextMain);
            return row;
        }

        /// <summary>字牌(设计板字库卡):稀有度顶条 + 属性色宋体大字 + 拼音 + 费用;选中态墨色描环。</summary>
        public static Button GlyphTile(Transform parent, Brushblade.Core.CharDef def, string costText,
            bool selected, Action onClick, Vector2? size = null)
        {
            var s = size ?? new Vector2(96, 118);
            var go = new GameObject($"Tile_{def.Id}", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var ring = go.AddComponent<Image>();
            ring.sprite = Theme.Rounded(14);
            ring.type = Image.Type.Sliced;
            ring.color = selected ? Theme.Ink : Theme.Shadow;
            var element = go.AddComponent<LayoutElement>();
            element.preferredWidth = s.x;
            element.preferredHeight = s.y;

            var inner = Panel(go.transform, "Face");
            var face = inner.AddComponent<Image>();
            face.sprite = Theme.Rounded(12);
            face.type = Image.Type.Sliced;
            face.color = Theme.CardWhite;
            Anchor((RectTransform)inner.transform, Vector2.zero, Vector2.one,
                new Vector2(2.5f, 2.5f), new Vector2(-2.5f, -2.5f));

            var strip = Panel(inner.transform, "Rarity");
            var stripImage = strip.AddComponent<Image>();
            stripImage.color = Theme.RarityColor(def.Rarity);
            Anchor((RectTransform)strip.transform, new Vector2(0.08f, 1f), new Vector2(0.92f, 1f),
                new Vector2(0, -6), new Vector2(0, -2));

            var glyph = ThemedLabel(inner.transform, def.Id, Mathf.RoundToInt(s.y * 0.34f),
                Theme.ElementColor(def.Element), Theme.TitleFont);
            Anchor(glyph.rectTransform, new Vector2(0, 0.36f), new Vector2(1, 0.9f), Vector2.zero, Vector2.zero);

            var pinyin = ThemedLabel(inner.transform, def.Pinyin ?? "", 12, Theme.TextDim);
            Anchor(pinyin.rectTransform, new Vector2(0, 0.2f), new Vector2(1, 0.36f), Vector2.zero, Vector2.zero);

            var cost = ThemedLabel(inner.transform, costText, 13, Theme.TextDim);
            Anchor(cost.rectTransform, new Vector2(0, 0.02f), new Vector2(1, 0.2f), Vector2.zero, Vector2.zero);

            var button = go.AddComponent<Button>();
            button.targetGraphic = face;
            if (onClick != null) button.onClick.AddListener(() => onClick());
            return button;
        }

        /// <summary>奖励式广告位:绿边圆角 + 播放三角 + 绿字。</summary>
        public static Button AdBadge(Transform parent, string text, Action onClick, Vector2? size = null)
        {
            var s = size ?? new Vector2(130, 40);
            var go = new GameObject("AdBadge", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var border = go.AddComponent<Image>();
            border.sprite = Theme.Rounded(10);
            border.type = Image.Type.Sliced;
            border.color = Theme.AdGreen;
            var element = go.AddComponent<LayoutElement>();
            element.preferredWidth = s.x;
            element.preferredHeight = s.y;

            var inner = Panel(go.transform, "Face");
            var face = inner.AddComponent<Image>();
            face.sprite = Theme.Rounded(10);
            face.type = Image.Type.Sliced;
            face.color = Theme.AdGreenBg;
            Anchor((RectTransform)inner.transform, Vector2.zero, Vector2.one,
                new Vector2(1.5f, 1.5f), new Vector2(-1.5f, -1.5f));

            var row = Row(inner.transform, "Content", 5);
            Stretch((RectTransform)row.transform);
            var icon = Panel(row.transform, "Play");
            var iconImage = icon.AddComponent<Image>();
            iconImage.sprite = Theme.Triangle;
            iconImage.color = Theme.AdGreen;
            var iconElement = icon.AddComponent<LayoutElement>();
            iconElement.preferredWidth = 9;
            iconElement.preferredHeight = 11;
            ThemedLabel(row.transform, text, 15, Theme.AdGreenText, Theme.TitleFont);

            var button = go.AddComponent<Button>();
            button.targetGraphic = face;
            if (onClick != null) button.onClick.AddListener(() => onClick());
            return button;
        }

        public static GameObject VStack(Transform parent, string name, float spacing = 4)
        {
            var go = Panel(parent, name);
            var layout = go.AddComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            return go;
        }

        public static string ChineseNumber(int n) => n switch
        {
            1 => "一", 2 => "二", 3 => "三", 4 => "四", 5 => "五", _ => n.ToString(),
        };
    }
}
