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

        /// <summary>模态弹窗(2026-07-19 拍板:提示统一弹窗):墨色遮罩 + 宣纸卡 + 按钮行。
        /// 点按钮或遮罩即关闭(按钮先关再执行动作);返回根节点供外部提前销毁。</summary>
        /// <summary>模态外壳:墨遮罩 + 宣纸卡 + 标题,返回内容容器供调用方自由填充。
        /// dismissable = 点遮罩是否关闭——必须做出选择的流程(战利品)传 false。</summary>
        public static GameObject ModalShell(Transform root, string title, Vector2 halfSize,
            bool dismissable, out Transform content)
        {
            var overlay = new GameObject("Modal", typeof(RectTransform), typeof(Image));
            overlay.transform.SetParent(root, false);
            var mask = overlay.GetComponent<Image>();
            mask.color = new Color(0.12f, 0.10f, 0.08f, 0.55f); // 墨色半透遮罩
            Stretch((RectTransform)overlay.transform);
            var maskButton = overlay.AddComponent<Button>();
            maskButton.targetGraphic = mask;
            if (dismissable)
                maskButton.onClick.AddListener(() => UnityEngine.Object.Destroy(overlay));

            var card = CardPanel(overlay.transform, "Dialog");
            Anchor((RectTransform)card.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                -halfSize, halfSize);

            var stack = VStack(card.transform, "Stack", 12);
            Stretch((RectTransform)stack.transform);
            ThemedLabel(stack.transform, title, 24, Theme.TextMain, Theme.TitleFont);
            content = stack.transform;
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
        public static Button GlyphTile(Transform parent, Brushblade.Core.CharDef def,
            bool selected, Action onClick, Vector2? size = null)
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
                face.color = Color.white; // 素材自带牌面底色,不再染色
            }
            else
            {
                face.sprite = Theme.Rounded(12);
                face.type = Image.Type.Sliced;
                face.color = Theme.CardWhite;
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
                Theme.GlyphColor(def.Element), Theme.TitleFont);
            // 费用带撤销后的重新分配(2026-08-21):字形 0.36–0.94 → 0.30–0.95,拼音 0.19–0.36 → 0.06–0.30。
            // 字号仍是 s.y * 0.34 —— 比例不动,是为了不改动其它调用方(图鉴 144×180、跑图 76×95)的观感;
            // 战斗字库牌靠**把牌整体调小**来缩,而不是靠改这个比例。
            Anchor(glyph.rectTransform, new Vector2(0, 0.30f), new Vector2(1, 0.95f), Vector2.zero, Vector2.zero);

            var pinyin = ThemedLabel(content.transform, def.Pinyin ?? "", 12, Theme.TextDim);
            Anchor(pinyin.rectTransform, new Vector2(0, 0.06f), new Vector2(1, 0.30f), Vector2.zero, Vector2.zero);

            // 动效(§4):属性决定动什么、稀有度决定动多少。素材缺失时 Init 里自行退化为不动
            go.AddComponent<CardFrameView>().Init(def.Rarity, def.Element,
                new Vector2(s.x - 5f, s.y - 6.25f), motes.transform, face, glow, selected);

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
    }
}
