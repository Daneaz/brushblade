using System;
using System.Collections.Generic;
using Brushblade.Data;
using UnityEngine;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>代码驱动 uGUI 的构建工具。原型期不做美术,布局全走 LayoutGroup。</summary>
    public static class Ui
    {
        private static Font _font;

        /// <summary>CJK 可用的动态字体:默认字体无中文字形,从系统字体加载。
        /// ⚠️ TODO(Q22 / R15,上线前必验):系统字体全取不到时会落到 LegacyRuntime.ttf,
        /// 那里没有 CJK 字形 —— Android 真机上中文会变豆腐块,且 PUA 叠字(四木/四金)任何
        /// 系统字体都没有。新代码一律用 Theme.TitleFont / Theme.BodyFont(项目子集字体);
        /// 存量用点(Juice.Popup 的战斗飘字)待迁,详见 docs/design/第18章 R15。</summary>
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

        /// <summary>描边圆角卡:外层描边色垫底 + 内层填充色内缩,留出 <paramref name="thickness"/> 的边线。
        ///
        /// 稿上每块面板、每个页签都有一条 1pt 的浅描边(#DED7C9)—— 没有它,浅色卡片会直接
        /// 融进同样是浅色的宣纸底,四个页签看上去就是一片平地(2026-08-28 反馈)。
        ///
        /// ⚠ 返回的是**外层**:LayoutElement 与内容都往它身上挂。内容是比内层更靠后的兄弟节点,
        /// 所以画在填充之上,不会被盖住。做成按钮时 <c>targetGraphic</c> 要指
        /// <paramref name="face"/> 而不是外层 —— 染色染在那条边线上几乎看不见。</summary>
        public static Image OutlinedPanel(Transform parent, string name, Color fill, Color border,
            int radius, float thickness, out Image face)
        {
            var outer = CardPanel(parent, name, border, radius);
            face = CardPanel(outer.transform, "Face", fill, radius);
            face.raycastTarget = false;
            Anchor((RectTransform)face.transform, Vector2.zero, Vector2.one,
                new Vector2(thickness, thickness), new Vector2(-thickness, -thickness));
            return outer;
        }

        public static Image OutlinedPanel(Transform parent, string name, Color fill, Color border,
            int radius = 20, float thickness = 2f) =>
            OutlinedPanel(parent, name, fill, border, radius, thickness, out _);

        /// <summary>统一浮层外壳(2026-09-01,轮三 Task 1):遮罩 + 宣纸描边卡 + 带内边距的竖排容器。
        ///
        /// `Dialogs.dc.html` 定死了一条:全站弹窗是**同一套**外壳 —— 半透遮罩 + 宣纸圆角卡
        /// **带 1pt 描边**(#DED7C9)+ 内容。没有那条描边,浅色卡会直接融进同样浅色的宣纸底。
        /// 此前 <see cref="ModalShell"/> 用的是无边 <see cref="CardPanel"/>,而 UnitSheet 自己
        /// 手写了一份带描边的 —— 两套外壳各写各的,正是这次要收掉的东西。
        ///
        /// ⚠ 2026-09-02 review 修(Critical):「同屏只留一个」**只对显式共用同一个 <paramref
        /// name="name"/> 的调用方生效**,不是全站任意两个浮层互斥——稿上「新的弹出前先销毁
        /// 旧的,否则叠成一摞、点不到底下那层」说的是**同一族流程弹窗**排队,不是不同族的
        /// 浮层也要互相清场。此前 <see cref="ModalShell"/> 把 name 硬编码成 <c>"Modal"</c>
        /// 且无条件走销毁分支,导致当时的 11 个既有调用点互相残杀:战利品弹窗(<c>_rewardModal</c>)
        /// 与长按预览(<c>_modal</c>)是<c>BattleView</c>刻意分层、要同屏共存的两张浮层,
        /// 都经 <see cref="ModalShell"/> 建在同一个 <c>"Modal"</c> 名下,预览一弹出就把
        /// 战利品弹窗自己销毁了(而且没有回调重建)。修法是加一个开关:调用方自己决定
        /// 是否参与「按名互斥」——<see cref="ModalShell"/> 传 false(维持轮三之前「从不销毁」
        /// 的既有行为),<see cref="UnitSheet.Show"/> 传 true(它的滚动位置保留机制本就
        /// 建立在「销毁上一个同名实例」上)。后续 Task 2/3 的战斗流程浮层会共用同一个名字
        /// <c>"BattleSheet"</c> 并传 true,那条「同族排队」的规矩落在那里,不落在这里。
        ///
        /// ⚠ 吃点击的 Button 必须挂在**外层**、targetGraphic 指向 <c>face</c>:
        /// <see cref="OutlinedPanel"/> 对 face 无条件设了 raycastTarget = false,
        /// 挂在 face 身上的 Button 永远吃不到点击,点击会穿透下去命中遮罩的关闭按钮。</summary>
        /// <param name="dismissable">点遮罩是否关闭。必须做出选择的流程(战利品、换字)传 false。</param>
        /// <param name="replaceSameName">true 时才会在建之前销毁 <paramref name="root"/> 下的
        /// 同名节点——只给「显式共用同一个 name、要按名互斥排队」的调用方(如
        /// <see cref="UnitSheet.Show"/>)传 true;不同族、要同屏共存的浮层各用各的 name,
        /// 或者干脆传 false,不参与这条互斥。</param>
        public static GameObject Sheet(Transform root, string name, float width, float height,
            bool dismissable, bool replaceSameName, Color scrim, out Transform content) =>
            Sheet(root, name, width, height, dismissable, replaceSameName, scrim, 0f, out content);

        /// <param name="lift">卡片相对屏幕中心上抬多少(逻辑单位)。默认 0 = 居中。
        /// 字卡详情传正值,为的是把**被长按的那张牌**留在浮层下面看得见 ——
        /// 「我按的是哪张」与「这张字什么用」得能对上(稿 CharSheet.dc.html)。</param>
        public static GameObject Sheet(Transform root, string name, float width, float height,
            bool dismissable, bool replaceSameName, Color scrim, float lift, out Transform content)
        {
            if (replaceSameName)
            {
                var stale = root.Find(name);
                if (stale != null) UnityEngine.Object.Destroy(stale.gameObject);
            }

            var overlay = new GameObject(name, typeof(RectTransform), typeof(Image));
            overlay.transform.SetParent(root, false);
            var mask = overlay.GetComponent<Image>();
            mask.color = scrim;
            Stretch((RectTransform)overlay.transform);
            var maskButton = overlay.AddComponent<Button>();
            maskButton.targetGraphic = mask;
            if (dismissable)
                maskButton.onClick.AddListener(() => UnityEngine.Object.Destroy(overlay));

            var outer = OutlinedPanel(overlay.transform, "Card", Theme.PanelPaper, Theme.PanelBorder,
                SheetRadius, SheetBorder, out var face);
            Anchor((RectTransform)outer.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-width / 2f, -height / 2f + lift), new Vector2(width / 2f, height / 2f + lift));
            // 这个 Button 只为**吃掉**落在卡片本体上的点击(不让它穿透到遮罩去关掉弹窗),
            // 不是一个可按的控件 —— 必须关掉过渡效果。Button 默认 ColorTint:按下整张卡面
            // 乘 0.78、抬手后停在 selectedColor 0.96,于是点一下卡片本体、卡面就闪暗一下,
            // 全站弹窗(含 UnitSheet / 两张 BattleSheet)一处不落。同款写法见 BattleView 的
            // Backdrop(那块也是纯粹的「吃点击层」)。(2026-09-02 收尾波)
            var faceButton = outer.gameObject.AddComponent<Button>();
            faceButton.transition = Selectable.Transition.None;
            faceButton.targetGraphic = face;

            var stack = VStack(face.transform, "Stack", SheetSpacing);
            Stretch((RectTransform)stack.transform);
            var layout = stack.GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.padding = new RectOffset(SheetPad, SheetPad, SheetPad, SheetPad);
            content = stack.transform;
            return overlay;
        }

        public static GameObject Sheet(Transform root, string name, float width, float height,
            bool dismissable, bool replaceSameName, out Transform content) =>
            Sheet(root, name, width, height, dismissable, replaceSameName, Theme.Scrim, out content);

        private const int SheetRadius = 18;    // 稿 9pt 圆角
        // 这两个是 internal 而非 private:调用方要按「浮层还剩多少净宽」反算内容尺寸时
        // (见 BattleView.DrawReplaceSheet 按字库张数反算牌宽),必须扣掉描边内缩与内边距。
        // 抄一份常数到调用方那边会两边各改各的、悄悄漂开,索性让它们读同一个数。
        internal const float SheetBorder = 1.5f; // 稿 1pt 描边(左右各内缩一次)
        private const float SheetSpacing = 14f;
        internal const int SheetPad = 24;        // 内容容器左右内边距(各一次)

        /// <summary>模态外壳:坐在 <see cref="Sheet"/> 上,标题写进内容容器。
        /// dismissable = 点遮罩是否关闭——必须做出选择的流程(战利品)传 false。
        /// ⚠ 2026-09-02 review 修:对 <see cref="Sheet"/> 传 <c>replaceSameName: false</c>——
        /// 所有调用点共用同一个 name("Modal"),但互相之间并不是「同族排队」关系
        /// (战利品弹窗与长按预览要同屏共存),按名互斥会把其中一个误杀,详见 Sheet 的文档。
        /// 数目:出问题那会儿(53ee2bf)直连本方法的是 11 处;轮三把战斗流程浮层(选字/换字)
        /// 全迁去 Ui.Sheet 之后,今天只剩 5 处(PerkView / CollectionView / CharPreview /
        /// EnemyPreview / 本文件的 Ui.Modal)。原注释写的「13」两处都不对(2026-09-02 收尾波)。</summary>
        public static GameObject ModalShell(Transform root, string title, Vector2 halfSize,
            bool dismissable, out Transform content)
        {
            var overlay = Sheet(root, "Modal", halfSize.x * 2f, halfSize.y * 2f,
                dismissable, replaceSameName: false, out content);
            // 内容改回垂直居中(2026-09-02 试玩反馈:「回合结束弹窗没有居中显示」)。
            // Sheet 的默认是 UpperCenter,那是给**内容撑满整张卡**的那一类浮层定的
            // (UnitSheet 的立绘+两列+底部提示行、战斗流程浮层的选字/换字页)——它们靠内容
            // 自上而下排,居中反而会让整块内容在卡里浮起来。而 ModalShell 的这几个是
            // 「标题 + 一两行正文 + 一排钮」的小弹窗,卡是按 halfSize 定死的、内容远填不满:
            // 贴顶就变成上面挤、下面空一大块(620×300 的 Ui.Alert 内容约 135,底下空 114)。
            // 重构前 ModalShell 直接用 VStack,而 VStack 的默认就是 MiddleCenter,所以这是
            // 抽 Ui.Sheet 时被无声改掉的旧行为,不是新口径。
            content.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
            ThemedLabel(content, title, 24, Theme.TextMain, Theme.TitleFont);
            return overlay;
        }

        public static GameObject Modal(Transform root, string title, string body,
            params (string label, Action onClick, Color bg, Color fg)[] buttons) =>
            Modal(root, title, body, new Vector2(310, 150), buttons);

        /// <summary>指定尺寸的弹窗:正文多行时放大(如升级 preview 的前后对比)。</summary>
        public static GameObject Modal(Transform root, string title, string body, Vector2 halfSize,
            params (string label, Action onClick, Color bg, Color fg)[] buttons)
        {
            var overlay = ModalShell(root, title, halfSize, dismissable: true, out var stack);
            ThemedLabel(stack, body, 17, Theme.TextDim);
            var row = Row(stack, "Buttons", 14);
            foreach (var (label, onClick, bg, fg) in buttons)
            {
                var action = onClick;
                PillButton(row.transform, label, () =>
                {
                    UnityEngine.Object.Destroy(overlay);
                    action?.Invoke();
                }, bg, fg, 18, new Vector2(150, 52));
            }
            return overlay;
        }

        /// <summary>单按钮告知弹窗:操作被拒类提示统一走这里(2026-07-19 拍板)。</summary>
        public static GameObject Alert(Transform root, string title, string body) =>
            Modal(root, title, body, (Strings.T("common.ok"), null, Theme.LockedBg, Theme.TextMain));

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

        /// <summary>胶囊小标签(宽度按 CJK 字宽估算)。padX/padY 只给挤不下的地方调窄用
        /// (敌人格 chip 行),默认值即原尺寸,其余 20 多个调用点不受影响。
        ///
        /// iconKey 非空时在文字左侧画图标(2026-08-17):PNG 有就画图,没有就画
        /// <see cref="Icons.Fallback"/> 的汉字 —— 两条路占同样的宽,布局不受资产有无影响。</summary>
        public static GameObject Chip(Transform parent, string text, Color bg, Color fg,
            int fontSize = 14, int padX = ChipPadX, int padY = ChipPadY, string iconKey = null)
        {
            var go = Panel(parent, "Chip");
            var image = go.AddComponent<Image>();
            image.sprite = Theme.Rounded(14);
            image.type = Image.Type.Sliced;
            image.color = bg;

            float iconSpan = 0f;
            if (iconKey != null)
            {
                iconSpan = Icons.Size + (!string.IsNullOrEmpty(text) ? Icons.Gap : 0f);
                var sprite = Icons.Get(iconKey);
                if (sprite != null)
                {
                    var iconGo = Panel(go.transform, "Icon");
                    var iconImage = iconGo.AddComponent<Image>();
                    iconImage.sprite = sprite;
                    iconImage.color = fg;                 // 图形是白的,用前景色染
                    iconImage.preserveAspect = true;
                    Anchor((RectTransform)iconGo.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                        new Vector2(padX / 2f, -Icons.Size / 2f),
                        new Vector2(padX / 2f + Icons.Size, Icons.Size / 2f));
                }
                else
                {
                    // 兜底:同样占 Icons.Size 的宽,布局与有图时完全一致
                    var glyph = ThemedLabel(go.transform, Icons.Fallback(iconKey), fontSize, fg);
                    Anchor(glyph.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                        new Vector2(padX / 2f, -Icons.Size / 2f),
                        new Vector2(padX / 2f + Icons.Size, Icons.Size / 2f));
                }
            }

            if (!string.IsNullOrEmpty(text))
            {
                var label = ThemedLabel(go.transform, text, fontSize, fg);
                if (iconSpan > 0f)
                    Anchor(label.rectTransform, Vector2.zero, Vector2.one,
                        new Vector2(iconSpan, 0f), Vector2.zero);
                else
                    Stretch(label.rectTransform);
            }

            var element = go.AddComponent<LayoutElement>();
            element.preferredWidth = ChipWidth(text, fontSize, padX) + iconSpan;
            element.preferredHeight = ChipHeight(fontSize, padY);
            return go;
        }

        public const int ChipPadX = 18;
        public const int ChipPadY = 12;

        /// <summary>chip 尺寸是文本的纯函数 —— <see cref="ChipFlow"/> 要在建对象之前就把行排好,
        /// 靠的就是这两个函数,不需要任何测量或等一帧布局。</summary>
        public static float ChipWidth(string text, int fontSize, int padX = ChipPadX) =>
            text.Length * fontSize + padX;

        public static float ChipHeight(int fontSize, int padY = ChipPadY) => fontSize + padY;

        /// <summary>一段开了 Wrap 的文字在给定宽度下大约占多高。与 <see cref="ChipWidth"/> 同一
        /// 性质:**纯函数**,不测量、不等一帧布局。
        ///
        /// 为什么不用 <c>Text.preferredHeight</c>:那个要先知道自己 rect 的宽度才算得出,
        /// 而 rect 宽度又由布局决定 —— 首帧读到的是上一帧的旧宽。凡是「内容一变就重建」的
        /// 东西(详情弹窗的能力卡、拆合台的整句提示),首帧算错就等于常态算错。
        ///
        /// 估法:汉字约占一个字号宽(与 ChipWidth 同口径),ASCII 偏窄会让行数估多 —— 宁可
        /// 多留一行空白,也不让文字被卡片高度截掉。行高按 1.35 倍字号。</summary>
        public static float WrappedTextHeight(string text, int fontSize, float width)
        {
            if (string.IsNullOrEmpty(text)) return 0f;
            int perLine = Mathf.Max(1, Mathf.FloorToInt(width / fontSize));
            int lines = 0;
            foreach (var segment in text.Split('\n'))   // 显式换行也要各占至少一行
                lines += Mathf.Max(1, Mathf.CeilToInt(segment.Length / (float)perLine));
            return lines * fontSize * 1.35f;
        }

        /// <summary>带图标的 chip 宽度。图标占 <see cref="Icons.Size"/>,后面还有 Gap;
        /// 纯图标(无量值)时不留 Gap。无图标时与 <see cref="ChipWidth(string,int,int)"/> 等价。
        ///
        /// ⚠ 必须与 <see cref="Chip"/> 的实际布局一致 —— ChipFlow 在**建对象之前**就靠这个数
        /// 把行排好(见 ChipWidth 的注释),两者一旦不一致,排出来的行会溢出或留白。</summary>
        public static float ChipWidth(ChipSpec chip, int fontSize, int padX = ChipPadX)
        {
            float width = ChipWidth(chip.Text, fontSize, padX);
            if (chip.IconKey == null) return width;
            return width + Icons.Size + (!string.IsNullOrEmpty(chip.Text) ? Icons.Gap : 0f);
        }

        /// <summary>一个待排的 chip:文字与配色。<see cref="ChipFlow"/> 要先看全部文字才能分行,
        /// 所以调用方先攒成 spec 列表,而不是逐个 <see cref="Chip"/> 直接建对象。</summary>
        public readonly struct ChipSpec
        {
            public readonly string Text;
            public readonly Color Bg;
            public readonly Color Fg;

            /// <summary>非空则在文字左侧画一个 <see cref="Icons"/> 图标(2026-08-17)。
            /// 战斗界面的状态类 chip 用它把「灼烧 3」压成「[炎] 3」,宽度让给行动条。
            /// 为 null 时行为与从前**逐字节相同** —— 既有五个消费方一行不用改。</summary>
            public readonly string IconKey;

            /// <summary>这是 ChipFlow 自己补的「+N」计数,不是调用方给的真 chip ——
            /// 它用更紧的内边距(<see cref="ChipCountPadX"/>)。靠标记而不是认文本前缀:
            /// 「+N」长得像普通 chip,靠字符串猜迟早会误伤真 chip。</summary>
            internal readonly bool IsCount;

            public ChipSpec(string text, Color bg, Color fg) : this(text, bg, fg, null, false) { }

            public ChipSpec(string text, Color bg, Color fg, string iconKey)
                : this(text, bg, fg, iconKey, false) { }

            internal ChipSpec(string text, Color bg, Color fg, string iconKey, bool isCount)
            {
                Text = text;
                Bg = bg;
                Fg = fg;
                IconKey = iconKey;
                IsCount = isCount;
            }
        }

        /// <summary>可换行的胶囊区:外层 VStack,每行一个 Row。装不下 <paramref name="maxLines"/> 行时
        /// 从尾部丢弃,并在末尾补一个「+N」说明还有几个没显示。
        ///
        /// 为什么要它:HorizontalLayoutGroup 装不下时会把整行**等比压扁**,于是一个 chip 溢出会让
        /// 同行每个 chip 都跟着糊(敌人格加「不灭」后实测压 ~14%)。宽度是文本纯函数
        /// (见 <see cref="ChipWidth"/>),所以分行可以在建对象之前算完,不必测量也不用等一帧。</summary>
        public static GameObject ChipFlow(Transform parent, string name, IReadOnlyList<ChipSpec> chips,
            float width, int fontSize, int maxLines,
            int padX = ChipPadX, int padY = ChipPadY, float spacing = 5f, float lineSpacing = 3f)
        {
            var stack = VStack(parent, name, lineSpacing);
            var lines = PackChips(chips, chips.Count, null, width, maxLines, fontSize, padX, spacing);
            if (lines.Count > maxLines)
            {
                // 需要截断。**计数优先于多显示一个 chip** —— 「+N」的全部意义就是让玩家知道
                // 有东西被藏了,若为了多塞一个真 chip 而丢掉计数,等于实现成了「静默丢弃」。
                // 所以先在「带计数」的前提下找最大的 take,找不到才退到不带。
                lines = null;
                for (int take = chips.Count - 1; take >= 0 && lines == null; take--)
                {
                    var trial = PackChips(chips, take, $"+{chips.Count - take}",
                        width, maxLines, fontSize, padX, spacing);
                    if (trial.Count <= maxLines) lines = trial;
                }
                // 带计数怎么排都超行(末行被一个近满宽的 chip 占住)才走这里
                for (int take = chips.Count - 1; take >= 0 && lines == null; take--)
                {
                    var trial = PackChips(chips, take, null, width, maxLines, fontSize, padX, spacing);
                    if (trial.Count <= maxLines) lines = trial;
                }
                lines ??= new List<List<ChipSpec>>();
            }
            foreach (var line in lines)
            {
                var row = Row(stack.transform, "Line", spacing);
                foreach (var chip in line)
                    Chip(row.transform, chip.Text, chip.Bg, chip.Fg, fontSize,
                        chip.IsCount ? ChipCountPadX : padX, padY, chip.IconKey);
            }
            return stack;
        }

        /// <summary>「+N」计数 chip 的内边距:比普通 chip 紧得多。它是标记不是标签,
        /// 而按普通内边距它要占 ~34px —— 差不多一个真 chip 的宽,会把自己想报告的东西挤掉
        /// (实测 Boss 那格:蓄力 155 + 计数 34 超宽,收到 26 才排得进同一行)。</summary>
        public const int ChipCountPadX = 4;

        /// <summary>贪心装行:放不下就另起一行。单个宽过 width 的 chip 自成一行并横向溢出 ——
        /// 截断它比让玩家读半个词更糟,交由调用方用足够的 width 保证不发生。
        ///
        /// extra(「+N」)非空时,**末行要预先给它留位**:否则会排出「前面都塞满、计数被挤到
        /// 下一行」,外层只能再砍一个真 chip,砍到最后计数反而永远显示不出来。</summary>
        private static List<List<ChipSpec>> PackChips(IReadOnlyList<ChipSpec> chips, int take,
            string extra, float width, int maxLines, int fontSize, int padX, float spacing)
        {
            var lines = new List<List<ChipSpec>>();
            var current = new List<ChipSpec>();
            float x = 0f;
            float reserve = extra == null ? 0f : spacing + ChipWidth(extra, fontSize, ChipCountPadX);

            void Place(ChipSpec chip, float w, bool isExtra)
            {
                float limit = !isExtra && reserve > 0f && lines.Count == maxLines - 1
                    ? width - reserve
                    : width;
                if (current.Count > 0 && x + spacing + w > limit)
                {
                    lines.Add(current);
                    current = new List<ChipSpec>();
                    x = w;
                }
                else
                {
                    x += current.Count == 0 ? w : spacing + w;
                }
                current.Add(chip);
            }

            for (int i = 0; i < take; i++)
                Place(chips[i], ChipWidth(chips[i], fontSize, padX), false);
            if (extra != null)
                Place(new ChipSpec(extra, Theme.PaperDim, Theme.TextMain, null, isCount: true),
                    ChipWidth(extra, fontSize, ChipCountPadX), true);
            if (current.Count > 0) lines.Add(current);
            return lines;
        }

        /// <summary>给节点挂一个 <see cref="LayoutElement"/>。宽/高传负数 = 这一维不指定,
        /// 由布局组按内容算(LayoutUtility 会跳过负值,轮到优先级更低的布局组自己报数)。
        ///
        /// ⚠ flexWidth/flexHeight 默认是 0f,不是 -1f —— 这两个默认值**不对称**是故意的:
        /// 0 是显式压制,不是「不指定」。rail 因为父级 HorizontalLayoutGroup 开了
        /// childForceExpandWidth,本来会被强抬出 flexibleWidth = 1;是这个 priority 1、
        /// 值为 0 的 LayoutElement 把它压下去,142 定宽才守得住。如果哪天为了跟 width/height
        /// 「统一」把默认值改成 -1f,rail 会立刻开始跟着中区一起伸缩 —— 这个坑编译不报错,
        /// 也没有任何测试盖得住,只能全屏逐栏眼看着比对。</summary>
        public static LayoutElement Sized(GameObject go,
            float width = -1f, float height = -1f, float flexWidth = 0f, float flexHeight = 0f)
        {
            var element = go.AddComponent<LayoutElement>();
            element.preferredWidth = width;
            element.preferredHeight = height;
            element.flexibleWidth = flexWidth;
            element.flexibleHeight = flexHeight;
            return element;
        }

        /// <summary>进度条:PaperDim 底 + 填充色,圆角胶囊。</summary>
        public static GameObject Bar(Transform parent, float frac, Color fill, Vector2 size)
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
            return back; // 调用方可在其上叠加文本(如召唤物血值)
        }

        /// <summary>战场单位块:立绘方块在左、信息列在右(稿 .foe / .ally / .me 同构)。
        /// 三条(血/盾/行动)由调用方按需塞进 info —— 敌人的血条带叠字、我方的不带,
        /// 那是稿本身的结构差异(.foe .hpb 有 &lt;u&gt; 而 .ally/.me 没有),不是可以统一掉的东西。
        ///
        /// 抽出来是因为轮二的详情弹窗是第四个调用点。前三处在轮一各写各的,
        /// 已经长出三套盾条刻度 —— 再来一处就收不住了。
        ///
        /// <paramref name="portrait"/> 是一个已按 <paramref name="portraitSize"/> 定好宽高的空挂载点——
        /// 内容自定形状的立绘(<see cref="CircleGlyph"/>/<see cref="RoundButton"/>)建在它下面后
        /// 记得 <see cref="Stretch"/> 铺满;直接在挂载点本体上加组件(如 MobView)则不需要。
        /// <paramref name="info"/> 已是配好 <paramref name="infoWidth"/> 定宽的 <see cref="VStack"/>——
        /// 需要横向撑满(玩家条 flexWidth:1 那种)的话,调用方在拿到后自己改它的
        /// <see cref="LayoutElement"/>。</summary>
        public static GameObject UnitBlock(Transform parent, string name,
            float portraitSize, float infoWidth, float spacing,
            out Transform portrait, out Transform info)
        {
            var shell = Panel(parent, name);
            Stretch((RectTransform)shell.transform);
            var row = shell.AddComponent<HorizontalLayoutGroup>();
            row.spacing = spacing;
            row.childAlignment = TextAnchor.MiddleLeft;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;

            var portraitGo = Panel(shell.transform, "Portrait");
            Sized(portraitGo, width: portraitSize, height: portraitSize);
            portrait = portraitGo.transform;

            var infoGo = VStack(shell.transform, "Info");
            Sized(infoGo, width: infoWidth);
            info = infoGo.transform;

            return shell;
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

        /// <summary>墨锭图标 + 数字,并把那枚数字交出来 —— 调用方要拿它做动效。
        /// <see cref="IngotLabel"/> 只返回整行,而翻牌动效要转的是数字自己(转整行会把
        /// 图标一起卷进去,看着像整块牌在抖)。</summary>
        private static Text IngotLabelText(Transform parent, string text, int fontSize)
        {
            var row = Row(parent, "Ingot", 6);
            var icon = Panel(row.transform, "Icon");
            var image = icon.AddComponent<Image>();
            image.sprite = Theme.Ingot;
            image.color = Theme.IngotDark;
            var iconElement = icon.AddComponent<LayoutElement>();
            iconElement.preferredWidth = fontSize * 1.4f;
            iconElement.preferredHeight = fontSize * 0.85f;
            return ThemedLabel(row.transform, text, fontSize, Theme.TextMain);
        }

        /// <summary>玩家余额计数器 = 墨锭 + 数字 + 增减翻牌动效(2026-08-29;08-30 由飘字改翻牌)。
        /// 外层五个页签的顶栏与局内右上都走它(2026-08-30:半额取消后塔内预算与账户同源)。
        /// <b>只传余额</b> —— 结算面板上的「这趟挣了 N」、安全层累计、商品价签仍走 IngotLabel,
        /// 那些数字不是同一个账本,混进来会翻出凭空的增减(InkPulse 的注释)。</summary>
        public static GameObject InkCounter(Transform parent, int ink, int fontSize = 20)
        {
            var label = IngotLabelText(parent, ink.ToString(), fontSize);
            InkPulse.Observe(label, ink);
            return label.transform.parent.gameObject;
        }

        /// <summary>字牌(设计板字库卡):稀有度框 + 属性色宋体大字 + 拼音;选中态墨色描环。
        ///
        /// 2026-08-21:去掉牌底那条费用带。<see cref="Brushblade.Core.CharDef.ApCostFor"/>
        /// 自 2026-08-03 起**一律返回 1**(AP 与稀有度解耦),于是每张牌上都印一遍「1 AP」
        /// 是零信息量的噪音,却占着牌面 19% 的高度(原 cost 带 0.0–0.19)。腾出来的份额
        /// 分给字形与拼音,牌本身因此也能整体缩小 —— 战斗字库牌 105 → 85。</summary>
        /// <summary><paramref name="locked"/> = 未拥有(2026-09-03,收集页把没拿到的字也列出来):
        /// 牌面褪成宣纸灰、字形压浅、动效整套不挂 —— 但稀有度框仍留三成色相(角标那边同理),
        /// 「那张红卡我还没拿到」是收集页最该说清的一件事,全灰掉就说不出来了。
        /// 字形仍读得清:玩家要认得出这是哪个字。</summary>
        public static Button GlyphTile(Transform parent, Brushblade.Core.CharDef def,
            bool selected, Action onClick, Vector2? size = null, bool locked = false)
        {
            var s = size ?? new Vector2(96, 120); // 默认对齐素材 0.8 竖版比例
            var go = new GameObject($"Tile_{def.Id}", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var ring = go.AddComponent<Image>();
            ring.sprite = Theme.Rounded(14);
            ring.type = Image.Type.Sliced;
            ring.color = selected ? Theme.Ink : Theme.Shadow;
            var element = go.AddComponent<LayoutElement>();
            element.preferredWidth = s.x;
            element.preferredHeight = s.y;

            // 有稀有度框素材就整张按比例缩放,否则回落纯色圆角(素材可一级一级地上)。
            // 不用 9-slice:这批框的**边中段也有花**(回纹带、点线刻度、星芒),9-slice 会把中段拉花;
            // 而牌面恒定 0.8 竖版比例时,等比缩放与「9-slice + 边框同比缩放」逐像素等价,徒增切角风险。
            var frameSprite = CardFrames.Frame(def.Rarity);
            var inner = Panel(go.transform, "Face");
            var face = inner.AddComponent<Image>();
            if (frameSprite != null)
            {
                face.sprite = frameSprite;
                // Image.color 是**相乘**:未拥有时乘一层暖灰,框上的花纹与稀有度色相原样保留、
                // 只是整张退到宣纸背后。换成纯色板会把框也盖掉,那就分不出稀有度了
                face.color = locked ? Theme.LockedPaper : Color.white; // 素材自带牌面底色,不再染色
            }
            else
            {
                face.sprite = Theme.Rounded(12);
                face.type = Image.Type.Sliced;
                face.color = locked ? Theme.LockedPaper : Theme.CardWhite;
            }
            // 左右 2.5、上下 3.125:留边本身也得守 0.8,否则牌面被压扁、四角纹样跟着变形
            Anchor((RectTransform)inner.transform, Vector2.zero, Vector2.one,
                new Vector2(2.5f, 3.125f), new Vector2(-2.5f, -3.125f));

            // 层序(§4.2):属性层在下、材质光效在上、字在最上 —— 字要读得清,这条压倒一切
            var motes = Panel(inner.transform, "Motes");
            Stretch((RectTransform)motes.transform);

            // 光效层(蓝级以上):独立一层,由 CardFrameView 驱动扫光/呼吸/流光/星芒
            Image glow = null;
            var glowSprite = CardFrames.Glow(def.Rarity);
            if (glowSprite != null)
            {
                var glowGo = Panel(inner.transform, "Glow");
                glow = glowGo.AddComponent<Image>();
                glow.sprite = glowSprite;
                glow.raycastTarget = false;
                Stretch((RectTransform)glowGo.transform);
            }

            // 内容区按各档边框厚度让位:紫檀木框比素纸厚得多,内容不缩进会压到框上
            var (insetX, insetY) = CardFrames.ContentInset(def.Rarity);
            var content = Panel(inner.transform, "Content");
            Anchor((RectTransform)content.transform,
                new Vector2(insetX, insetY), new Vector2(1f - insetX, 1f - insetY),
                Vector2.zero, Vector2.zero);

            // 属性识别只靠字形颜色(2026-07-28 拍板移除顶条):字形已是加深过的属性专用色板,
            // 再加一条色带是冗余。金系原色对浅底只有 2.48:1,故字形必走 GlyphColor 而非 ElementColor
            var glyph = ThemedLabel(content.transform, def.Id, Mathf.RoundToInt(s.y * 0.34f),
                locked ? Theme.LockedGlyph : Theme.GlyphColor(def.Element), Theme.TitleFont);
            // 费用带撤销后的重新分配(2026-08-21):字形 0.36–0.94 → 0.30–0.95,拼音 0.19–0.36 → 0.06–0.30。
            // 字号仍是 s.y * 0.34 —— 比例不动,是为了不改动其它调用方(图鉴 144×180、跑图 76×95)的观感;
            // 战斗字库牌靠**把牌整体调小**来缩,而不是靠改这个比例。
            Anchor(glyph.rectTransform, new Vector2(0, 0.30f), new Vector2(1, 0.95f), Vector2.zero, Vector2.zero);

            var pinyin = ThemedLabel(content.transform, def.Pinyin ?? "", 12,
                locked ? Theme.LockedGlyph : Theme.TextDim);
            Anchor(pinyin.rectTransform, new Vector2(0, 0.06f), new Vector2(1, 0.30f), Vector2.zero, Vector2.zero);

            // 动效(§4):属性决定动什么、稀有度决定动多少。素材缺失时 Init 里自行退化为不动。
            // 未拥有不挂:稿上「未拥有不发光」—— 一屏几十张没拿到的字全在动,会盖过真正到手的那些
            if (!locked)
                go.AddComponent<CardFrameView>().Init(def.Rarity, def.Element,
                    new Vector2(s.x - 5f, s.y - 6.25f), motes.transform, face, glow, selected);
            else if (glow != null)
                glow.enabled = false;

            var button = go.AddComponent<Button>();
            button.targetGraphic = face;
            if (onClick != null) button.onClick.AddListener(() => onClick());
            return button;
        }

        /// <summary>圆形字头像:实色圆底 + 居中单字。战斗怪物与图鉴怪牌共用,保证形象一致。</summary>
        public static GameObject CircleGlyph(Transform parent, string face, Color faceColor, Color glyphColor, float diameter)
        {
            var go = Panel(parent, "Portrait");
            var image = go.AddComponent<Image>();
            image.sprite = Theme.Circle;
            image.color = faceColor;
            var element = go.AddComponent<LayoutElement>();
            element.preferredWidth = diameter;
            element.preferredHeight = diameter;
            var glyph = ThemedLabel(go.transform, face, Mathf.RoundToInt(diameter * 0.44f), glyphColor, Theme.TitleFont);
            Stretch(glyph.rectTransform);
            return go;
        }

        /// <summary>奖励式广告位:绿边圆角 + 播放三角 + 绿字。
        /// 内部字号/图标/圆角/间距原先是定死的 15/9×11/10/5——轮三 Task 4 把战利品弹窗那枚从
        /// (190,44) 撑到 (280,63)(稿 .adbadge height 30pt→63)时没跟着改,撑大的胶囊里蜷着一行
        /// 小字。改为按 <paramref name="size"/> 的高度相对稿本身的比例缩放:基准取稿 .adbadge
        /// 自己的换算值——font-size 10pt→21、border-radius 15pt→31、gap 5pt→10、播放三角
        /// svg 7×8pt→15×17,对应基准高度 30pt→63;其它调用点(战利品广告位以外的商城/地图/
        /// 复活入口,高度都不是 63)按自己传入的高度与 63 的比例整体缩放,不传 size 时走的默认
        /// (130,40) 同样落在这条缩放公式里,不会缺分支(2026-09-02)。</summary>
        public static Button AdBadge(Transform parent, string text, Action onClick, Vector2? size = null)
        {
            var s = size ?? new Vector2(130, 40);
            const float RefHeight = 63f; // 稿 .adbadge height 30pt→63,做缩放基准
            const float RefFont = 21f;   // 稿 font-size 10pt→21
            const float RefRadius = 31f; // 稿 border-radius 15pt→31
            const float RefGap = 10f;    // 稿 gap 5pt→10
            const float RefIconW = 15f;  // 稿播放三角 svg width 7pt→15
            const float RefIconH = 17f;  // 稿播放三角 svg height 8pt→17
            float scale = s.y / RefHeight;
            int fontSize = Mathf.RoundToInt(RefFont * scale);
            int radius = Mathf.RoundToInt(RefRadius * scale);
            float gap = RefGap * scale;
            float iconW = RefIconW * scale;
            float iconH = RefIconH * scale;

            var go = new GameObject("AdBadge", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var border = go.AddComponent<Image>();
            border.sprite = Theme.Rounded(radius);
            border.type = Image.Type.Sliced;
            border.color = Theme.AdGreen;
            var element = go.AddComponent<LayoutElement>();
            element.preferredWidth = s.x;
            element.preferredHeight = s.y;

            var inner = Panel(go.transform, "Face");
            var face = inner.AddComponent<Image>();
            face.sprite = Theme.Rounded(radius);
            face.type = Image.Type.Sliced;
            face.color = Theme.AdGreenBg;
            Anchor((RectTransform)inner.transform, Vector2.zero, Vector2.one,
                new Vector2(1.5f, 1.5f), new Vector2(-1.5f, -1.5f));

            var row = Row(inner.transform, "Content", gap);
            Stretch((RectTransform)row.transform);
            var icon = Panel(row.transform, "Play");
            var iconImage = icon.AddComponent<Image>();
            iconImage.sprite = Theme.Triangle;
            iconImage.color = Theme.AdGreen;
            var iconElement = icon.AddComponent<LayoutElement>();
            iconElement.preferredWidth = iconW;
            iconElement.preferredHeight = iconH;
            ThemedLabel(row.transform, text, fontSize, Theme.AdGreenText, Theme.TitleFont);

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

        /// <summary>竖直可滚动列表(2026-08-31,拆合台可合成列表首用):项目里第一处 ScrollRect,
        /// 后续要做别的滚动列表照这个结构抄,别再发明第二套。
        ///
        /// 结构是标准 uGUI 三层:返回的外层挂 <see cref="ScrollRect"/>(调用方在它身上挂
        /// LayoutElement 决定这块区域占多高)→ Viewport(<see cref="RectMask2D"/> 裁剪,不用
        /// <see cref="Mask"/> 是因为那个要求一张 Graphic 陪衬,平白多一次 overdraw)→
        /// Content(<paramref name="content"/>,VerticalLayoutGroup + ContentSizeFitter 按
        /// 子物体撑高)。调用方只管 <c>Ui.Clear(content)</c> 再往里塞东西,和其余 Draw* 方法
        /// 同一套用法,不用关心 ScrollRect 内部怎么接。</summary>
        public static GameObject ScrollList(Transform parent, string name, float spacing, out Transform content)
        {
            var root = Panel(parent, name);
            var scroll = root.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            var viewport = Panel(root.transform, "Viewport");
            Stretch((RectTransform)viewport.transform);
            viewport.AddComponent<RectMask2D>();
            // ⚠ 视口要有一张**全透明但接射线**的图,否则「按在空白处拖不动」(2026-09-03 实机反馈)。
            // uGUI 的拖拽是从**按下的那个物件**往上冒泡找 IDragHandler 的:按在列表项上能冒泡到
            // ScrollRect,按在项与项之间的缝、最后一行下面的空白、或没铺满的那半行上,
            // 射线什么都没打中 —— 事件根本不会产生,列表就纹丝不动。
            // 卡组网格尤其明显:5 列牌之间全是缝,而拇指最自然的落点就是缝。
            // alpha = 0 的 Graphic 照常参与射线检测(raycastTarget 才是开关),所以这张图
            // 只兜住空白处;列表项画在它之上,点击照旧先命中列表项。
            var catcher = viewport.AddComponent<Image>();
            catcher.color = new Color(0, 0, 0, 0);

            var contentGo = VStack(viewport.transform, "Content", spacing);
            var contentLayout = contentGo.GetComponent<VerticalLayoutGroup>();
            contentLayout.childAlignment = TextAnchor.UpperCenter;
            contentLayout.childForceExpandWidth = true;   // 列表项铺满列表宽度(稿 .cr { width: 100% })
            contentLayout.childForceExpandHeight = false; // 每项按自己的高度摞,不分摊富余
            var contentRect = (RectTransform)contentGo.transform;
            // 锚顶、随内容向下长——ContentSizeFitter 算出的高度是「顶部固定、往下撑」,
            // 锚点/pivot 都钉在顶边,否则内容变化时会从中心往两边长,滚动位置会跟着跳。
            contentRect.anchorMin = new Vector2(0, 1);
            contentRect.anchorMax = new Vector2(1, 1);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            // 横向 sizeDelta 显式归零(2026-09-01):anchorMin.x/anchorMax.x 已经是 0/1,
            // 宽度 = 视口宽 + sizeDelta.x —— 这一项没写死的话就吃 RectTransform 的构造
            // 缺省值,一旦不是 0,Content 会比视口宽出那么多、再按 pivot 0.5 居中,
            // 左右两端各被 RectMask2D 裁掉一半差额(表现是列表项左边缘缺一块)。
            // y 交给 ContentSizeFitter,这里给 0 只是占位。
            contentRect.sizeDelta = Vector2.zero;
            contentGo.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = (RectTransform)viewport.transform;
            scroll.content = contentRect;
            content = contentGo.transform;
            return root;
        }
    }
}
