using System;
using System.Collections;
using System.Collections.Generic;
using Brushblade.Core;
using Brushblade.Data;
using UnityEngine;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>配方转盘弹窗(spec 2026-10-19 §2,拍板稿 drafts/recipe-wheel/Wheel.dc.html)。
    ///
    /// **结果由 Core 先定好**(<see cref="RunEngine.LastRecipeWheel"/>,战后结算同一步已入账),
    /// 这里只负责把转盘转到那一格:点轮毂「转」→ 转 3.6s 落在目标扇区中段 ±30% → 中奖扇区高亮 0.7s →
    /// 转盘缩到 0.72 靠左、结果卡 0.3s 滑入 →「继续 · 选字」交回调用方。
    ///
    /// 尺寸一律稿上 pt × 2.093:外壳 420 × 320pt → 879 × 670,转盘直径 208pt → 435。
    /// 内部排版与屏宽无关(16:9 无刘海净宽 1354 ≥ 879,两档都放得下)。
    ///
    /// 角度约定同稿:从正上方起**顺时针**量(CSS rotate 正向);Unity 的 z 角逆时针为正,落到 Transform 时取负。</summary>
    public sealed class RecipeWheelSheet : MonoBehaviour
    {
        private const float SheetW = 879f;           // 420pt
        private const float SheetH = 670f;           // 320pt
        private const float BodyW = SheetW - 2f * Ui.SheetBorder - 2f * PadX;   // 卡内 384pt ≈ 803
        private const int PadX = 38;                 // 稿 padding 18pt
        private const int PadTop = 25;               // 12pt
        private const int PadBottom = 29;            // 14pt
        private const float BoxSize = 494f;          // .wheelbox 236pt
        private const float Radius = 217.5f;         // 转盘半径 104pt
        private const float RingInner = 226f;        // .ring 外描:半径 108pt 起
        private const float RingOuter = 232f;        // 3pt ink → 111pt
        private const float DividerW = 2.5f;         // 扇区分界 1.2pt ink
        private const float HighRimW = 6.3f;         // 高档格 gold 3pt 描边
        private const float WinRimW = 6.7f;          // 落定 cta 3.2pt 描边
        private const float HubD = 100f;             // 轮毂 48pt
        private const float HubPaperD = 113f;        // + 3pt panel-paper 环
        private const float HubInkD = 119f;          // + 1.5pt ink 环;命中区 ≥ 稿 56pt(117)
        private const float PointerSize = 38f;       // 18pt 倒三角
        private const float PointerDot = 12.6f;      // 6pt gold 小圆
        private const float ResultScale = 0.72f;     // 结果卡出现时转盘缩到 0.72 靠左
        private const float ResultColW = BodyW - BoxSize * ResultScale;   // ≈ 447(稿 ≈ 200pt)

        private const float SpinSeconds = 3.6f;
        private const float LandSeconds = 0.7f;
        private const float ResultSeconds = 0.3f;
        private const int SpinTurns = 5;             // 顺时针 5 圈多
        private const float IdleAngle = -12f;        // 待转时略偏一点,不让任何一格正对指针
        private const float JitterSpan = 0.6f;       // 落点在扇区中段 ±30%

        private RecipeWheelResult _wheel;
        private Action _onContinue;
        private RectTransform _box;
        private RectTransform _rotor;
        private Button _hub;
        private Image _hubFace;
        private Text _hubLabel;
        private RectTransform _resultCol;
        private CanvasGroup _resultGroup;
        private readonly List<Slot> _slots = new();
        private Slot _win;
        private float _angle = IdleAngle;
        private bool _spun;
        private int _depth;

        private sealed class Slot
        {
            public int Index;       // Core 的格下标(RecipeWheelResult.Slots)
            public bool High;
            public float Percent;
            public float From, Span; // 顺时针角度(度)
            public float Mid => From + Span / 2f;
            public Image Fill;
            public Text Glyph;
            public Color GlyphColor;
            public GameObject WinRim;
        }

        /// <summary>弹出转盘。点遮罩无效(结果卡出来之前误关会丢掉「落在哪」这一拍);
        /// <paramref name="onContinue"/> 在点「继续 · 选字」后调用,弹窗随即销毁。</summary>
        public static GameObject Show(Transform root, RecipeWheelResult wheel, RecipeGraph graph,
            int depth, bool themeBoss, Action onContinue)
        {
            var overlay = Ui.Sheet(root, "RecipeWheel", SheetW, SheetH, dismissable: false, replaceSameName: true,
                Theme.Scrim, out var content);
            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(PadX, PadX, PadTop, PadBottom);
            layout.spacing = 9f;                      // 稿 gap 8pt,副标 margin-top −4pt
            layout.childAlignment = TextAnchor.UpperCenter;
            var sheet = overlay.AddComponent<RecipeWheelSheet>();
            sheet._wheel = wheel;
            sheet._onContinue = onContinue;
            sheet._depth = depth;
            sheet.Build(content, graph, depth, themeBoss);
            return overlay;
        }

        private void Build(Transform content, RecipeGraph graph, int depth, bool themeBoss)
        {
            Ui.KickerRow(content, Strings.T("wheel.title"), 25, Theme.Cta);       // 稿 .kick 11.94pt
            var (low, high) = RecipeWheel.TiersFor(depth);
            Ui.ThemedLabel(content, Strings.T("wheel.sub", ("depth", depth),
                ("kind", themeBoss ? Strings.T("enemy.tier.boss") : Strings.T("enemy.tier.elite")),
                ("low", CharInfo.RarityName(low)), ("high", CharInfo.RarityName(high))), 19, Theme.TextDim);   // 9.08pt

            var body = Ui.Panel(content, "Body");
            Ui.Sized(body, BodyW, flexHeight: 1);
            var bodyRect = (RectTransform)body.transform;

            // 转盘盒:锚在 body 左中、pivot 左中 —— 缩到 0.72 时以左缘为原点(稿 transform-origin: left center)
            _box = (RectTransform)Ui.Panel(body.transform, "WheelBox").transform;
            _box.anchorMin = _box.anchorMax = new Vector2(0f, 0.5f);
            _box.pivot = new Vector2(0f, 0.5f);
            _box.sizeDelta = new Vector2(BoxSize, BoxSize);
            _box.anchoredPosition = new Vector2((BodyW - BoxSize) / 2f, 0f);

            BuildWheel(graph);
            BuildResult(bodyRect, graph);
        }

        // ---------------- 转盘 ----------------

        private void BuildWheel(RecipeGraph graph)
        {
            _rotor = Centered(_box, "Rotor", 2f * Radius);
            Layout();

            foreach (var s in _slots)
            {
                var fill = Centered(_rotor, $"Sector_{s.Index}", 2f * Radius).gameObject.AddComponent<Image>();
                fill.sprite = Disc;
                fill.type = Image.Type.Filled;
                fill.fillMethod = Image.FillMethod.Radial360;
                fill.fillOrigin = (int)Image.Origin360.Top;
                fill.fillClockwise = true;
                fill.fillAmount = s.Span / 360f;
                fill.color = Theme.RaritySoft(graph.Get(_wheel.Slots[s.Index]).Rarity);
                fill.raycastTarget = false;
                fill.rectTransform.localEulerAngles = new Vector3(0, 0, -s.From);
                s.Fill = fill;
            }
            foreach (var s in _slots)
                Spoke(_rotor, s.From, Radius, DividerW, Theme.Ink);
            foreach (var s in _slots)
            {
                if (s.High) Rim(_rotor, s, HighRimW, Theme.Gold);
                s.WinRim = Rim(_rotor, s, WinRimW, Theme.Cta, new Color(1f, 1f, 1f, 0.18f));   // 稿 rgba(255,255,255,.18) 提亮
                s.WinRim.SetActive(false);
            }
            foreach (var s in _slots)
            {
                var def = graph.Get(_wheel.Slots[s.Index]);
                s.GlyphColor = Theme.GlyphColor(def.Element);
                s.Glyph = Polar(_rotor, def.Id, s.Mid, Radius * 0.6f, s.High ? 38 : 46, s.GlyphColor, Theme.TitleFont, 63f);
                s.Glyph.fontStyle = FontStyle.Bold;
                string odds = s.High ? Strings.T("wheel.rare", ("p", Pct(s.Percent))) : Strings.T("wheel.odds", ("p", Pct(s.Percent)));
                var label = Polar(_rotor, odds, s.Mid, Radius * 0.86f, 14, s.High ? Theme.GoldDeep : Theme.LockGray, Theme.BodyFont, 84f);
                if (s.High) label.fontStyle = FontStyle.Bold;
            }
            _rotor.localEulerAngles = new Vector3(0, 0, -_angle);

            // 外圈 ink 环(稿 .ring:直径 216pt + 3pt 外描,与扇区之间留 4pt 纸色)
            var ring = Centered(_box, "Ring", 2f * RingOuter).gameObject.AddComponent<Image>();
            ring.sprite = Ring(RingInner / RingOuter);
            ring.color = Theme.Ink;
            ring.raycastTarget = false;

            // 指针:正上方 ink 倒三角 + gold 小圆
            var pointer = (RectTransform)Ui.Panel(_box, "Pointer").transform;
            pointer.anchorMin = pointer.anchorMax = new Vector2(0.5f, 1f);
            pointer.pivot = new Vector2(0.5f, 1f);
            pointer.sizeDelta = new Vector2(PointerSize, PointerSize);
            pointer.anchoredPosition = Vector2.zero;
            var tri = Ui.Panel(pointer, "Tri").AddComponent<Image>();
            tri.sprite = Theme.Triangle;         // 素材朝右,转 −90° 朝下
            tri.color = Theme.Ink;
            tri.raycastTarget = false;
            tri.rectTransform.sizeDelta = new Vector2(PointerSize, PointerSize);
            tri.rectTransform.localEulerAngles = new Vector3(0, 0, -90f);
            var dot = Ui.Panel(pointer, "Dot").AddComponent<Image>();
            dot.sprite = Theme.Circle;
            dot.color = Theme.Gold;
            dot.raycastTarget = false;
            dot.rectTransform.anchorMin = dot.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            dot.rectTransform.sizeDelta = new Vector2(PointerDot, PointerDot);
            dot.rectTransform.anchoredPosition = new Vector2(0f, -10.5f);   // 圆心距盒顶 5pt

            // 轮毂 =「转」钮:ink 环 → paper 环 → cta 实底白字宋体
            var hubRoot = Centered(_box, "Hub", HubInkD);
            var inkRing = hubRoot.gameObject.AddComponent<Image>();
            inkRing.sprite = Disc;
            inkRing.color = Theme.Ink;
            var paper = Centered(hubRoot, "Paper", HubPaperD).gameObject.AddComponent<Image>();
            paper.sprite = Disc;
            paper.color = Theme.PanelPaper;
            paper.raycastTarget = false;
            _hubFace = Centered(hubRoot, "Face", HubD).gameObject.AddComponent<Image>();
            _hubFace.sprite = Disc;
            _hubFace.color = Theme.Cta;
            _hubFace.raycastTarget = false;
            _hubLabel = Ui.ThemedLabel(_hubFace.transform, Strings.T("wheel.spin"), 31, Color.white, Theme.TitleFont);
            _hubLabel.fontStyle = FontStyle.Bold;
            _hubLabel.raycastTarget = false;
            Ui.Stretch(_hubLabel.rectTransform);
            _hub = hubRoot.gameObject.AddComponent<Button>();
            _hub.transition = Selectable.Transition.None;
            _hub.targetGraphic = inkRing;
            _hub.onClick.AddListener(OnSpin);
        }

        /// <summary>扇区几何:高档格 10%(<see cref="RecipeWheel.HighChancePercent"/>),低档格平分其余;角度 = 概率 × 360°。
        /// 顺时针排「低 低 高 低 低」(稿 rw.js slots):高档夹在中间,转动时一次次从指针下掠过。
        /// 候选不足时格数变少,概率口径照 Core:没有高档格 = 低档平分 100%,只有高档格 = 100%。</summary>
        private void Layout()
        {
            var order = new List<int>();
            int lowCount = _wheel.HighIndex >= 0 ? _wheel.Slots.Count - 1 : _wheel.Slots.Count;
            for (int i = 0; i < _wheel.Slots.Count; i++)
                if (i != _wheel.HighIndex) order.Add(i);
            if (_wheel.HighIndex >= 0) order.Insert(Math.Min(2, order.Count), _wheel.HighIndex);

            float highPct = _wheel.HighIndex < 0 ? 0f : lowCount == 0 ? 100f : RecipeWheel.HighChancePercent;
            float lowPct = lowCount == 0 ? 0f : (100f - highPct) / lowCount;
            float a = 0f;
            foreach (int i in order)
            {
                bool high = i == _wheel.HighIndex;
                float pct = high ? highPct : lowPct;
                var s = new Slot { Index = i, High = high, Percent = pct, From = a, Span = pct * 3.6f };
                a += s.Span;
                _slots.Add(s);
                if (i == _wheel.Landed) _win = s;
            }
        }

        private static string Pct(float p) => p.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);

        // ---------------- 交互与动画 ----------------

        private void OnSpin()
        {
            if (_spun) return;
            _spun = true;
            SetHub(Strings.T("wheel.spinning"));
            StartCoroutine(Play());
        }

        private void SetHub(string text)
        {
            _hubFace.color = Theme.LockedBg;          // 稿 .hub.off:locked-bg 底 + text-faint 字
            _hubLabel.color = Theme.LockGray;
            _hubLabel.text = text;
            _hub.interactable = false;
        }

        private IEnumerator Play()
        {
            // 落点:目标扇区中段 ±30%(不压线)。抖动只是观感,种子取自结果本身,不碰任何游戏随机流
            var jitterRandom = new GameRandom(_wheel.Landed * 7919 + _wheel.Slots.Count * 131 + _depth);
            float jitter = (jitterRandom.Next(1001) / 1000f - 0.5f) * _win.Span * JitterSpan;
            float target = _win.Mid + jitter;
            float start = _angle;
            float baseTurn = start + 360f * SpinTurns;
            // 顺时针转到「目标角 + 转角 ≡ 0」:目标扇区停在正上方的指针下
            float end = baseTurn + Mathf.Repeat(-target - baseTurn, 360f);

            for (float t = 0f; t < SpinSeconds; t += Time.unscaledDeltaTime)
            {
                _angle = Mathf.LerpUnclamped(start, end, SpinEase(t / SpinSeconds));
                _rotor.localEulerAngles = new Vector3(0, 0, -_angle);
                yield return null;
            }
            _angle = end;
            _rotor.localEulerAngles = new Vector3(0, 0, -_angle);

            // 落定 0.7s:中奖扇区 cta 描边 + 提亮,其余压灰;轮毂显示「中」
            _hubLabel.text = Strings.T("wheel.hit");
            foreach (var s in _slots)
            {
                if (s == _win) { s.WinRim.SetActive(true); continue; }
                s.Fill.color = Theme.PaperDim;
                s.Glyph.color = Theme.TextMute;
            }
            for (float t = 0f; t < LandSeconds; t += Time.unscaledDeltaTime) yield return null;

            // 结果卡 0.3s:转盘缩到 0.72 靠左,右侧滑入
            _resultCol.gameObject.SetActive(true);
            var from = _box.anchoredPosition;
            var slideFrom = new Vector2(21f, 0f);   // 稿 translateX(10pt)
            for (float t = 0f; t < ResultSeconds; t += Time.unscaledDeltaTime)
            {
                float k = 1f - (1f - t / ResultSeconds) * (1f - t / ResultSeconds);   // ease-out
                _box.anchoredPosition = Vector2.Lerp(from, Vector2.zero, k);
                _box.localScale = Vector3.one * Mathf.Lerp(1f, ResultScale, k);
                _resultCol.anchoredPosition = Vector2.Lerp(slideFrom, Vector2.zero, k);
                _resultGroup.alpha = k;
                yield return null;
            }
            _box.anchoredPosition = Vector2.zero;
            _box.localScale = Vector3.one * ResultScale;
            _resultCol.anchoredPosition = Vector2.zero;
            _resultGroup.alpha = 1f;
            _resultGroup.interactable = true;
            _resultGroup.blocksRaycasts = true;
        }

        /// <summary>稿 <c>cubic-bezier(.12,.62,.1,1)</c>:起步快、长尾慢停。按 x 二分求参数 u,再取 y。</summary>
        private static float SpinEase(float x)
        {
            if (x <= 0f) return 0f;
            if (x >= 1f) return 1f;
            const float x1 = 0.12f, y1 = 0.62f, x2 = 0.1f, y2 = 1f;
            float lo = 0f, hi = 1f, u = x;
            for (int i = 0; i < 24; i++)
            {
                u = (lo + hi) / 2f;
                if (Bezier(u, x1, x2) < x) lo = u; else hi = u;
            }
            return Bezier(u, y1, y2);
        }

        private static float Bezier(float u, float p1, float p2)
        {
            float v = 1f - u;
            return 3f * v * v * u * p1 + 3f * v * u * u * p2 + u * u * u;
        }

        // ---------------- 结果卡 ----------------

        private void BuildResult(RectTransform body, RecipeGraph graph)
        {
            var def = graph.Get(_wheel.Target);
            var col = Ui.VStack(body, "Result", 13);  // 稿 .res gap 6pt
            _resultCol = (RectTransform)col.transform;
            _resultCol.anchorMin = new Vector2(1f, 0f);
            _resultCol.anchorMax = new Vector2(1f, 1f);
            _resultCol.pivot = new Vector2(1f, 0.5f);
            _resultCol.sizeDelta = new Vector2(ResultColW, 0f);
            _resultCol.anchoredPosition = Vector2.zero;
            _resultGroup = col.AddComponent<CanvasGroup>();
            _resultGroup.alpha = 0f;
            _resultGroup.interactable = false;
            _resultGroup.blocksRaycasts = false;

            // 大字牌:稀有度牌框 + 拼音(稿 .bt 66 × 82.5pt)
            Ui.GlyphTile(col.transform, def, false, null, new Vector2(138f, 173f));

            // 字 +「金 · 水 · 稀有格」
            var name = Ui.Row(col.transform, "Name", 13);
            var glyph = Ui.ThemedLabel(name.transform, def.Id, 29, Theme.TextMain, Theme.TitleFont);   // 14pt
            glyph.fontStyle = FontStyle.Bold;
            string element = def.Element is { } e ? CharInfo.ElementName(e) : Strings.T("char.element.neutral");
            bool high = _wheel.Landed == _wheel.HighIndex;
            Ui.ThemedLabel(name.transform, high
                ? Strings.T("wheel.result_meta_rare", ("rarity", CharInfo.RarityName(def.Rarity)), ("element", element))
                : Strings.T("wheel.result_meta", ("rarity", CharInfo.RarityName(def.Rarity)), ("element", element)),
                21, Theme.TextDim);   // 10pt

            // 配方「氵 + 木 → 沐」:部件用部件池方块样式(本系淡底 + 深色字),出字为字牌底 + 字形色描边
            var recipe = Ui.Row(col.transform, "Recipe", 10);   // 稿 gap 5pt
            for (int i = 0; i < def.Recipe.Count; i++)
            {
                if (i > 0) Ui.ThemedLabel(recipe.transform, "+", 23, Theme.TextWarm, Theme.TitleFont);
                var part = graph.Get(def.Recipe[i]);
                var tile = Ui.CardPanel(recipe.transform, "Part", Theme.ElementSoft(part?.Element), 10);
                Ui.Sized(tile.gameObject, 54f, 65f);      // 26 × 31pt
                var label = Ui.ThemedLabel(tile.transform, def.Recipe[i], 29, Theme.ElementSoftFg(part?.Element), Theme.TitleFont);
                Ui.Stretch(label.rectTransform);
            }
            Ui.ThemedLabel(recipe.transform, "→", 23, Theme.TextWarm, Theme.TitleFont);
            var output = Ui.OutlinedPanel(recipe.transform, "Out", Theme.CardFace, Theme.GlyphColor(def.Element), 10, 2.5f, out var outFace);
            Ui.Sized(output.gameObject, 54f, 65f);
            var outLabel = Ui.ThemedLabel(outFace.transform, def.Id, 29, Theme.GlyphColor(def.Element), Theme.TitleFont);
            Ui.Stretch(outLabel.rectTransform);

            // jade 勾「已记入本局配方」
            var got = Ui.Row(col.transform, "Got", 10);
            var check = Ui.Panel(got.transform, "Check").AddComponent<Image>();
            check.sprite = Disc;
            check.color = Theme.Jade;
            check.raycastTarget = false;
            Ui.Sized(check.gameObject, 27f, 27f);      // 13pt
            if (Icons.Get("check") is { } tick)
            {
                var icon = Ui.Panel(check.transform, "Tick").AddComponent<Image>();
                icon.sprite = tick;
                icon.color = Color.white;
                icon.preserveAspect = true;
                icon.raycastTarget = false;
                icon.rectTransform.sizeDelta = new Vector2(17f, 17f);
            }
            var gotLabel = Ui.ThemedLabel(got.transform, Strings.T("wheel.recorded"), 21, Theme.UpgradeText);
            gotLabel.fontStyle = FontStyle.Bold;

            // 「继续 · 选字」cta(稿 26pt 高、左右 22pt)
            string cont = Strings.T("wheel.continue");
            var button = Ui.PillButton(col.transform, cont, OnContinue, Theme.Cta, Color.white, 23,
                new Vector2(Ui.EstimateWidth(cont, 23) + 92f, 54f));

            col.SetActive(false);
        }

        private void OnContinue()
        {
            var callback = _onContinue;
            _onContinue = null;
            Destroy(gameObject);
            callback?.Invoke();
        }

        // ---------------- 几何小件 ----------------

        private static RectTransform Centered(Transform parent, string name, float size)
        {
            var rect = (RectTransform)Ui.Panel(parent, name).transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(size, size);
            rect.anchoredPosition = Vector2.zero;
            return rect;
        }

        /// <summary>圆心发出的一根辐条(角度顺时针从正上方量)。</summary>
        private static void Spoke(Transform parent, float angle, float length, float width, Color color)
        {
            var line = Ui.Panel(parent, "Spoke").AddComponent<Image>();
            line.color = color;
            line.raycastTarget = false;
            var rect = line.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.sizeDelta = new Vector2(width, length);
            rect.anchoredPosition = Vector2.zero;
            rect.localEulerAngles = new Vector3(0, 0, -angle);
        }

        /// <summary>扇区描边:两条半径 + 一段外弧(弧用环形贴图径向填充),描边骑在扇区边线上(SVG stroke 居中)。
        /// <paramref name="tint"/> 给了就在扇区上再铺一层(落定的「提亮」)。</summary>
        private static GameObject Rim(Transform parent, Slot s, float width, Color color, Color? tint = null)
        {
            var root = Centered(parent, "Rim", 2f * Radius + width);
            if (tint is { } t)
            {
                var wash = Centered(root, "Wash", 2f * Radius).gameObject.AddComponent<Image>();
                wash.sprite = Disc;
                wash.type = Image.Type.Filled;
                wash.fillMethod = Image.FillMethod.Radial360;
                wash.fillOrigin = (int)Image.Origin360.Top;
                wash.fillClockwise = true;
                wash.fillAmount = s.Span / 360f;
                wash.color = t;
                wash.raycastTarget = false;   // 父节点已转到 From,这里不再转
            }
            var arc = root.gameObject.AddComponent<Image>();
            float outer = Radius + width / 2f;
            arc.sprite = Ring((Radius - width / 2f) / outer);
            arc.type = Image.Type.Filled;
            arc.fillMethod = Image.FillMethod.Radial360;
            arc.fillOrigin = (int)Image.Origin360.Top;
            arc.fillClockwise = true;
            arc.fillAmount = s.Span / 360f;
            arc.color = color;
            arc.raycastTarget = false;
            root.localEulerAngles = new Vector3(0, 0, -s.From);
            // 两条半径:root 已转到 From,这里按相对角摆
            Spoke(root, 0f, outer, width, color);
            Spoke(root, s.Span, outer, width, color);
            return root.gameObject;
        }

        /// <summary>沿径向摆一行字:圆心距 <paramref name="r"/>、字头朝外(稿 rotate(mid))。</summary>
        private static Text Polar(Transform parent, string text, float angle, float r, int size, Color color, Font font, float width)
        {
            var label = Ui.ThemedLabel(parent, text, size, color, font);
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            var rect = label.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(width, size * 1.3f);
            float rad = angle * Mathf.Deg2Rad;
            rect.anchoredPosition = new Vector2(Mathf.Sin(rad) * r, Mathf.Cos(rad) * r);
            rect.localEulerAngles = new Vector3(0, 0, -angle);
            return label;
        }

        // ---------------- 程序化贴图(大尺寸:转盘直径 435,Theme.Circle 的 64px 放大会糊边) ----------------

        private const int TexSize = 512;
        private static Sprite _disc;
        private static readonly Dictionary<int, Sprite> RingCache = new();

        private static Sprite Disc => _disc ??= Annulus(0f);

        /// <summary>环:内径 / 外径 = <paramref name="innerRatio"/>(按千分位缓存)。</summary>
        private static Sprite Ring(float innerRatio)
        {
            int key = Mathf.RoundToInt(innerRatio * 1000f);
            if (RingCache.TryGetValue(key, out var cached)) return cached;
            var sprite = Annulus(key / 1000f);
            RingCache[key] = sprite;
            return sprite;
        }

        private static Sprite Annulus(float innerRatio)
        {
            var tex = new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave,
            };
            float c = TexSize / 2f, outer = c - 1f, inner = outer * innerRatio;
            var pixels = new Color32[TexSize * TexSize];
            for (int y = 0; y < TexSize; y++)
                for (int x = 0; x < TexSize; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(c, c));
                    float a = Mathf.Clamp01(outer - d + 0.5f);
                    if (innerRatio > 0f) a *= Mathf.Clamp01(d - inner + 0.5f);
                    pixels[y * TexSize + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            tex.SetPixels32(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, TexSize, TexSize), new Vector2(0.5f, 0.5f), 100f,
                0, SpriteMeshType.FullRect);
        }
    }
}
