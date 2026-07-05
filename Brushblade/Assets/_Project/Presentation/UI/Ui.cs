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
                foreach (var name in new[] { "PingFang SC", "Microsoft YaHei", "Noto Sans CJK SC", "Hiragino Sans GB" })
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
            label.color = Color.white;
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

        public static void Clear(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(parent.GetChild(i).gameObject);
        }
    }
}
