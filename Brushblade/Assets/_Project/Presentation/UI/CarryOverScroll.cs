using System;
using System.Collections;
using System.Collections.Generic;
using Brushblade.Core;
using Brushblade.Data;
using UnityEngine;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>跨场开局卷轴 + 顶栏「跨场 N」气泡(Plan E4 Task 6,稿 <c>drafts/traits/CarryOver.dc.html</c>,附录 §D 一)。
    ///
    /// 三拍(R1/R2):① <see cref="Unroll"/> 展开(0.35s,自上而下揭开 + 透明度 .4→1),停到约 0.9s;
    /// ② <see cref="Settle"/> 副题改「已结算」、pill 换成减一后,<see cref="Stamp"/> 每行金描光晕 0.4s + pill 放大 0.45s,
    /// 同时 BattleView 播开局效果那一拍的事件(卷轴降到 α 0.85,飘字是之后建的根节点兄弟,天然盖在卷轴上,D-Q5);
    /// ③ <see cref="FadeOut"/> 0.3s 淡出(D-Q6),顶栏 chip 弹出。
    ///
    /// D-Q1 方案 A:不出灰显行,规则句只有「同类只算最强的一份。」。
    /// 全部节拍吃 Juice 的帧间隔(按住快进 / 固定加速同样生效),由调用方传 <c>dt</c>。
    /// 尺寸 = 稿 pt × 2.093;效果栏按 <see cref="Ui.FitSingleLine"/> 降字号 / 截断(D-Q7)。</summary>
    internal sealed class CarryOverScroll
    {
        // ---- 卷轴(稿 .scroll / .co / .left / .rule) ----
        public const float Width = 712f;                 // 340pt
        private const int PadTop = 17, PadH = 21, PadBottom = 19;   // 8 / 10 / 9pt
        private const float Gap = 8f;                    // 行距 4pt
        private const int Radius = 21;                   // 10pt
        private const int TitleFont = 25;                // 12pt 宋体 600
        private const int SubFont = 18;                  // 8.5pt
        private const int RuleFont = 17;                 // 8pt
        private const float TitleGap = 13f;              // 标题与副题 6pt

        // ---- 行(卷轴与气泡共用) ----
        public const float RowH = 52f;                   // 25pt
        private const int RowPad = 13;                   // 0 6pt
        private const float RowGap = 13f;                // 6pt
        private const int RowRadius = 15;                // 7pt
        private const float SrcW = 40f, SrcH = 48f;      // 19×23pt
        private const int NameFont = 22;                 // 10.5pt 宋体 600
        private const int EffectFont = 18;               // 8.8pt
        private const int EffectMinFont = 16;            // D-Q7
        private const int PillFont = 17;                 // 8pt
        private const float PillH = 31f;                 // 15pt
        private const int PillPad = 13;                  // 0 6pt
        private const int PillRadius = 15;

        // ---- 气泡(稿 .pop2) ----
        public const float PopWidth = 561f;              // 268pt
        private const int PopPadV = 17, PopPadH = 19;    // 8 / 9pt
        private const int PopRadius = 19;                // 9pt
        private const int PopTitleFont = 23;             // 11pt 宋体
        private const int PopSubFont = 17;               // 8pt
        private const float ArrowSize = 17f;             // 8pt
        private const float ArrowBorder = 2f;
        private const float ArrowFromRight = 84f;        // 40pt:箭头离气泡右缘

        // ---- 节拍 ----
        public const float UnrollSeconds = 0.35f;
        public const float StampSeconds = 0.4f;
        public const float TickSeconds = 0.45f;
        public const float FadeSeconds = 0.3f;
        public const float SettleAlpha = 0.85f;          // D-Q5

        private sealed class RowRefs
        {
            public OpeningRow Row;
            public Transform PillSlot;
            public Image Background;
            public GameObject Pill;
        }

        private readonly GameObject _root;
        private readonly CanvasGroup _group;
        private readonly RectMask2D _mask;
        private readonly Text _sub;
        private readonly List<RowRefs> _rows = new();
        private readonly float _height;

        public GameObject Root => _root;

        /// <param name="parent">BattleView 根节点(不在任何布局组里)。</param>
        /// <param name="bottomCenter">卷轴底边中点,parent 本地坐标(手牌带底边、中区居中)。</param>
        public CarryOverScroll(RectTransform parent, Vector2 bottomCenter, IReadOnlyList<OpeningRow> rows,
            Func<string, CharDef> defOf, Func<string, int> levelOf)
        {
            float titleH = Mathf.Ceil(TitleFont * 1.35f);
            float ruleH = Mathf.Ceil(RuleFont * 1.5f);
            _height = PadTop + titleH + Gap + rows.Count * (RowH + Gap) + ruleH + PadBottom;

            _root = Ui.Panel(parent, "CarryOverScroll");
            var rect = (RectTransform)_root.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = bottomCenter - parent.rect.center;
            rect.sizeDelta = new Vector2(Width, _height);
            _group = _root.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;   // 只读:期间本来就在动画锁里
            _mask = _root.AddComponent<RectMask2D>();

            var panel = Ui.OutlinedPanel(_root.transform, "Panel", Theme.PanelPaper, Theme.PanelBorder, Radius, 2f);
            panel.raycastTarget = false;
            Ui.Stretch((RectTransform)panel.transform);

            var stack = Ui.VStack(panel.transform, "Stack", Gap);
            var layout = stack.GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.padding = new RectOffset(PadH, PadH, PadTop, PadBottom);
            Ui.Stretch((RectTransform)stack.transform);
            float inner = Width - 2f * PadH;

            var head = Ui.Row(stack.transform, "Title", TitleGap);
            head.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.LowerLeft;
            Ui.Sized(head, inner, titleH);
            string title = Strings.T("battle.carryover.scroll.title");
            var titleLabel = Ui.ThemedLabel(head.transform, title, TitleFont, Theme.TextMain, Theme.TitleFont, TextAnchor.LowerLeft);
            titleLabel.fontStyle = FontStyle.Bold;
            Ui.Sized(titleLabel.gameObject, Ui.EstimateWidth(title, TitleFont), titleH);
            _sub = Ui.ThemedLabel(head.transform, Strings.T("battle.carryover.scroll.sub.start", ("count", rows.Count)),
                SubFont, Theme.LockGray, align: TextAnchor.LowerLeft);
            Ui.Sized(_sub.gameObject, inner - Ui.EstimateWidth(title, TitleFont) - TitleGap, titleH);

            foreach (var row in rows)
                _rows.Add(BuildRow(stack.transform, row, settled: false, inner, defOf, levelOf));

            var rule = Ui.ThemedLabel(stack.transform, Strings.T("battle.carryover.scroll.rule"), RuleFont, Theme.LockGray,
                align: TextAnchor.MiddleLeft);
            Ui.Sized(rule.gameObject, inner, ruleH);

            _group.alpha = 0.4f;
            _mask.padding = new Vector4(0f, _height, 0f, 0f);   // 起手全遮:自上而下揭开
        }

        /// <summary>① 拍展开:RectMask2D 的下内边距从整高收到 0(= clip-path inset 自上而下),透明度 .4→1,ease-out。</summary>
        public IEnumerator Unroll(Func<float> dt)
        {
            for (float t = 0f; t < UnrollSeconds; t += dt())
            {
                float k = EaseOut(t / UnrollSeconds);
                if (_root == null) yield break;
                _mask.padding = new Vector4(0f, _height * (1f - k), 0f, 0f);
                _group.alpha = Mathf.Lerp(0.4f, 1f, k);
                yield return null;
            }
            if (_root == null) yield break;
            _mask.padding = Vector4.zero;
            _group.alpha = 1f;
        }

        /// <summary>② 拍开头:副题改「已结算」,每行 pill 换成减一后的三态(在原位重建,不动别的格),卷轴降到 α 0.85。</summary>
        public void Settle()
        {
            if (_root == null) return;
            _sub.text = Strings.T("battle.carryover.scroll.sub.settled");
            foreach (var r in _rows)
            {
                if (r.Pill != null) UnityEngine.Object.Destroy(r.Pill);
                r.Pill = BuildPill(r.PillSlot, OpeningRules.Pill(r.Row, settled: true));
            }
            _group.alpha = SettleAlpha;
        }

        /// <summary>② 拍的行动效(装饰,调用方 StartCoroutine 即可):每行金色描光晕 0.4s(稿 stamp)+ pill 放大 1.35 并闪金底 0.45s(稿 tick)。
        /// 光晕用 Outline(只改描边,不动布局);pill 只改 scale。</summary>
        public IEnumerator Stamp(Func<float> dt)
        {
            var glows = new List<Outline>();
            foreach (var r in _rows)
            {
                if (r.Background == null) continue;
                var glow = r.Background.gameObject.AddComponent<Outline>();
                glow.effectDistance = new Vector2(4f, 4f);
                glow.effectColor = Clear(Theme.Gold);
                glows.Add(glow);
            }
            float total = Mathf.Max(StampSeconds, TickSeconds);
            for (float t = 0f; t < total; t += dt())
            {
                if (_root == null) yield break;
                float s = Mathf.Clamp01(t / StampSeconds);
                float glowK = s < 0.35f ? s / 0.35f : 1f - (s - 0.35f) / 0.65f;
                foreach (var glow in glows)
                    if (glow != null) glow.effectColor = new Color(Theme.Gold.r, Theme.Gold.g, Theme.Gold.b, 0.8f * glowK);
                float p = Mathf.Clamp01(t / TickSeconds);
                float tickK = p < 0.4f ? p / 0.4f : 1f - (p - 0.4f) / 0.6f;
                foreach (var r in _rows)
                    if (r.Pill != null) r.Pill.transform.localScale = Vector3.one * Mathf.Lerp(1f, 1.35f, tickK);
                yield return null;
            }
            foreach (var glow in glows)
                if (glow != null) UnityEngine.Object.Destroy(glow);
            foreach (var r in _rows)
                if (r.Pill != null) r.Pill.transform.localScale = Vector3.one;
        }

        /// <summary>③ 拍收起:0.3s 淡出(D-Q6),播完销毁。</summary>
        public IEnumerator FadeOut(Func<float> dt)
        {
            float from = _group != null ? _group.alpha : 1f;
            for (float t = 0f; t < FadeSeconds; t += dt())
            {
                if (_root == null) yield break;
                _group.alpha = Mathf.Lerp(from, 0f, t / FadeSeconds);
                yield return null;
            }
            Destroy();
        }

        public void Destroy()
        {
            if (_root != null) UnityEngine.Object.Destroy(_root);
        }

        /// <summary>卷轴总高(纯函数,供两档屏比验算):标题 + 行 + 规则句 + 内边距。</summary>
        public static float HeightFor(int rows) =>
            PadTop + Mathf.Ceil(TitleFont * 1.35f) + Gap + rows * (RowH + Gap) + Mathf.Ceil(RuleFont * 1.5f) + PadBottom;

        // ================= 气泡 =================

        /// <summary>顶栏「跨场 N」点开的气泡(R14):全屏透明接点击层(点任意处关)+ 561 宽气泡,箭头对准 chip。
        /// <paramref name="chipCenter"/> / <paramref name="chipBottom"/> 是 chip 在 host 本地坐标里的中心 x 与下缘 y。
        /// 只列 LeftAfter &gt; 0 的行(D-Q2/D-Q3),pill 按减一后。返回接点击层(销毁它即关)。</summary>
        public static GameObject ShowPop(RectTransform host, float chipCenter, float chipBottom,
            IReadOnlyList<OpeningRow> live, Func<string, CharDef> defOf, Func<string, int> levelOf, Action close)
        {
            var root = new GameObject("CarryOverPop", typeof(RectTransform), typeof(Image));
            root.transform.SetParent(host, false);
            Ui.Stretch((RectTransform)root.transform);
            var catcher = root.GetComponent<Image>();
            catcher.color = new Color(0f, 0f, 0f, 0f);
            var button = root.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = catcher;
            button.onClick.AddListener(() => close());

            float titleH = Mathf.Ceil(PopTitleFont * 1.35f);
            float height = 2f * PopPadV + titleH + live.Count * (RowH + Gap);
            Vector2 c = host.rect.center;
            float arrowX = chipCenter - c.x;
            float right = arrowX + ArrowFromRight + ArrowSize / 2f;
            float halfW = host.rect.width / 2f;
            right = Mathf.Min(right, halfW);   // 夹进屏内(chip 右边还有回合 / 速度 / 设置 / ? / 退出,实际够不着)
            float top = chipBottom - c.y - ArrowSize;   // 箭头半截探到气泡上缘之上,尖对着 chip 下缘

            Arrow(root.transform, "ArrowBorder", new Vector2(arrowX, top), ArrowSize + 2f * ArrowBorder, Theme.PanelBorder);
            var panel = Ui.OutlinedPanel(root.transform, "Bubble", Theme.PanelPaper, Theme.PanelBorder, PopRadius, 2f);
            var rect = (RectTransform)panel.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(right, top);
            rect.sizeDelta = new Vector2(PopWidth, height);
            Arrow(root.transform, "ArrowFill", new Vector2(arrowX, top), ArrowSize, Theme.PanelPaper);

            var stack = Ui.VStack(panel.transform, "Stack", Gap);
            var layout = stack.GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.padding = new RectOffset(PopPadH, PopPadH, PopPadV, PopPadV);
            Ui.Stretch((RectTransform)stack.transform);
            float inner = PopWidth - 2f * PopPadH;

            var head = Ui.Row(stack.transform, "Title", TitleGap);
            head.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.LowerLeft;
            Ui.Sized(head, inner, titleH);
            string title = Strings.T("battle.carryover.pop.title");
            var titleLabel = Ui.ThemedLabel(head.transform, title, PopTitleFont, Theme.TextMain, Theme.TitleFont, TextAnchor.LowerLeft);
            titleLabel.fontStyle = FontStyle.Bold;
            Ui.Sized(titleLabel.gameObject, Ui.EstimateWidth(title, PopTitleFont), titleH);
            var sub = Ui.ThemedLabel(head.transform, Strings.T("battle.carryover.pop.sub"), PopSubFont, Theme.LockGray,
                align: TextAnchor.LowerLeft);
            Ui.Sized(sub.gameObject, inner - Ui.EstimateWidth(title, PopTitleFont) - TitleGap, titleH);

            foreach (var row in live)
                BuildRow(stack.transform, row, settled: true, inner, defOf, levelOf);
            return root;
        }

        // ================= 行 =================

        /// <summary>一行(R7):来源小牌(稀有度框 + 本系字色)+ 面印 + 特性名(宋体)+ 效果(text-dim,数字 text-main,单行适配)+ 剩余场数 pill。
        /// 效果栏宽 = 行宽 − 两侧内边距 − 小牌 − 面印 − 特性名 − pill 两态里较宽者 − 4 道间距,两态切换时效果栏不跳。</summary>
        private static RowRefs BuildRow(Transform parent, OpeningRow row, bool settled, float width,
            Func<string, CharDef> defOf, Func<string, int> levelOf)
        {
            var o = row.Effect;
            var def = defOf(o.SourceCharId);
            var trait = def != null ? OpeningRules.SourceTrait(def, o) : null;
            int level = levelOf(o.SourceCharId);

            var bg = Ui.OutlinedPanel(parent, "CarryRow", Theme.CardWhite, Theme.PanelBorder, RowRadius, 2f);
            bg.raycastTarget = false;
            Ui.Sized(bg.gameObject, width, RowH);
            var line = Ui.Row(bg.transform, "Line", RowGap);
            var lineLayout = line.GetComponent<HorizontalLayoutGroup>();
            lineLayout.childAlignment = TextAnchor.MiddleLeft;
            lineLayout.padding = new RectOffset(RowPad, RowPad, 0, 0);
            Ui.Stretch((RectTransform)line.transform);

            // 来源小牌
            if (def != null) Ui.MiniGlyphTile(line.transform, def, new Vector2(SrcW, SrcH));
            else Ui.Sized(Ui.Panel(line.transform, "NoSrc"), SrcW, SrcH);
            // 面印(按挂这条开局效果的特性所在面;查不到特性 = 五行面,开局效果全挂在五行面上)
            var sealSlot = Ui.Panel(line.transform, "SealSlot");
            Ui.Sized(sealSlot, Ui.FaceSealXs, Ui.FaceSealXs);
            if (def != null)
            {
                if (trait != null) Ui.TraitSeal(sealSlot.transform, def, trait, Ui.FaceSealXs);
                else Ui.FaceSeal(sealSlot.transform, def, CardFace.Feature, Ui.FaceSealXs);
            }
            // 特性名
            string traitName = trait?.Name ?? o.SourceCharId;
            var name = Ui.ThemedLabel(line.transform, traitName, NameFont, Theme.TextMain, Theme.TitleFont, TextAnchor.MiddleLeft);
            name.fontStyle = FontStyle.Bold;
            float nameW = Ui.EstimateWidth(traitName, NameFont);
            Ui.Sized(name.gameObject, nameW, RowH);

            // pill 预留宽:两态(结算前 / 后)取较宽者
            var before = OpeningRules.Pill(row, settled: false);
            var after = OpeningRules.Pill(row, settled: true);
            float pillW = Mathf.Max(PillWidth(PillText(before)), PillWidth(PillText(after)));

            float effW = width - 2f * RowPad - SrcW - Ui.FaceSealXs - nameW - pillW - 4f * RowGap;
            var (plain, rich) = CarryOverText.Effect(o, level);
            var fit = Ui.FitSingleLine(plain, EffectFont, EffectMinFont, effW);
            var effect = Ui.ThemedLabel(line.transform, fit.Fitted ? rich : fit.Text, fit.Size, Theme.TextDim,
                align: TextAnchor.MiddleLeft);
            Ui.Sized(effect.gameObject, Mathf.Max(0f, effW), RowH);

            // pill 占一个定宽槽(两态较宽者),本体在槽里靠右:换态时整行不跳
            var slot = Ui.Panel(line.transform, "PillSlot");
            Ui.Sized(slot, pillW, PillH);
            return new RowRefs
            {
                Row = row, PillSlot = slot.transform, Background = bg,
                Pill = BuildPill(slot.transform, settled ? after : before),
            };
        }

        private static string PillText(OpeningPill pill)
        {
            switch (pill.Kind)
            {
                case OpeningPillKind.Last: return Strings.T("battle.carryover.left.last");
                case OpeningPillKind.Done: return Strings.T("battle.carryover.left.done");
                default: return Strings.T("battle.carryover.left.n", ("n", pill.Count));
            }
        }

        private static float PillWidth(string text) => Mathf.Ceil(Ui.EstimateWidth(text, PillFont)) + 2f * PillPad;

        /// <summary>pill(R8):剩 N 场 = panel-inset 底 text-dim;最后一场 = 警示浅底(稿 warn-bg / warn-text,现名 danger-bg / danger-text);
        /// 用完 = 透明底 + paper-dim 细描环 + text-faint。槽里靠右放,不进布局(ignoreLayout),scale 动效不挤邻格。</summary>
        private static GameObject BuildPill(Transform slot, OpeningPill pill)
        {
            string text = PillText(pill);
            float w = PillWidth(text);
            var go = Ui.Panel(slot, "Pill");
            go.AddComponent<LayoutElement>().ignoreLayout = true;
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(-w / 2f, 0f);
            rect.sizeDelta = new Vector2(w, PillH);
            var image = go.AddComponent<Image>();
            image.raycastTarget = false;
            image.type = Image.Type.Sliced;
            Color fg;
            switch (pill.Kind)
            {
                case OpeningPillKind.Last:
                    image.sprite = Theme.Rounded(PillRadius);
                    image.color = Theme.DangerBg;
                    fg = Theme.DangerText;
                    break;
                case OpeningPillKind.Done:
                    image.sprite = Theme.RoundedRing(PillRadius, 2);
                    image.color = Theme.PaperDim;
                    fg = Theme.LockGray;
                    break;
                default:
                    image.sprite = Theme.Rounded(PillRadius);
                    image.color = Theme.PanelInset;
                    fg = Theme.TextDim;
                    break;
            }
            var label = Ui.ThemedLabel(go.transform, text, PillFont, fg);
            label.raycastTarget = false;
            Ui.Stretch(label.rectTransform);
            return go;
        }

        private static void Arrow(Transform parent, string name, Vector2 center, float size, Color color)
        {
            var rect = (RectTransform)Ui.Panel(parent, name).transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = center;
            rect.sizeDelta = new Vector2(size, size);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            rect.localRotation = Quaternion.Euler(0f, 0f, 45f);
        }

        private static float EaseOut(float k)
        {
            k = Mathf.Clamp01(k);
            return 1f - (1f - k) * (1f - k);
        }

        private static Color Clear(Color c) => new Color(c.r, c.g, c.b, 0f);
    }
}
