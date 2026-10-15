using System;
using System.Collections;
using System.Collections.Generic;
using Brushblade.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>一条特性飘名要的全部信息:哪张字(字形色 / 面印的五行)、哪条特性(名字 / 面)。
    /// 由 BattleView 从图谱里找(<see cref="CharInfo.TraitCueOf"/> / <see cref="CharInfo.NatureTraitCue"/>),取不到为 null。</summary>
    public readonly struct TraitCue
    {
        public readonly CharDef Def;
        public readonly TraitDef Trait;

        public TraitCue(CharDef def, TraitDef trait)
        {
            Def = def;
            Trait = trait;
        }
    }

    /// <summary>TriggerFx 全套样式(Plan E4 Task 3,traits/TriggerFx 稿 2026-10 拍板)。三种飘出物里本文件管前两种:
    ///
    /// · **特性名签**(<see cref="TraitLabel"/>):纸底小签 + s 档面印 + 宋体名,字色 = 特性所在字的字形色;
    /// · **状态名签**(<see cref="StatusLabel"/>):战场 chip 色实底 + 图标 + 白字黑体;被挡下的(拦截)整体 60% + 白横线;
    /// · 数字照旧走 <see cref="Popup"/>(稿「现状不变」)。
    ///
    /// 两种签共用一条 1.3s 曲线(<see cref="FloatTag"/>):从载体顶边中点弹出、上浮约半个签高、78% 后淡出,不加水平抖动;
    /// 同一载体上连飘按 0.8s 时窗往上叠一层(与数字的 0.55s 分开记,B-Qa7)。
    /// 载体闪(<see cref="FlashCarrier"/>):0.45s,25% 处放大 1.4、往白推、外套白描环 + 赭金发光,只改 scale 不动布局;
    /// 立绘版(<see cref="FlashPortrait"/>)不缩放,只加亮 + 赭金发光。</summary>
    public sealed partial class Juice
    {
        // ---- 尺寸(稿 pt × 2.093)----
        private const float TagHeight = 33.5f;       // 16pt
        private const float TagPadLeft = 5.2f;       // 2.5pt
        private const float TagPadRight = 12.6f;     // 6pt
        private const float TagGap = 6.3f;           // 3pt
        private const int TagFontSize = 22;          // 10.5pt
        private const int TagRadius = 8;             // 4pt
        private const float StatusTagHeight = 31.4f; // 15pt
        private const float StatusPadLeft = 8.4f;    // 4pt
        private const float StatusIcon = 21f;        // 10pt
        private const int StatusFontSize = 19;       // 9pt ≈ 18.8
        private const float StruckTagAlpha = 0.6f;

        // ---- 时序 ----
        private const float TagDuration = 1.3f;
        private const float TagStackWindow = 0.8f;   // 1.3s × 0.6(B-Qa7)
        private const float TagStackStep = 36f;      // 略高于签高 33.5
        private const int TagStackMax = 4;
        private const float CarrierFlashDuration = 0.45f;

        /// <summary>稿的赭金外发光 rgba(201,169,74,.7)。</summary>
        private static readonly Color FlashGlow = new(0.788f, 0.663f, 0.290f, 0.7f);

        /// <summary>稿的签阴影 rgba(22,27,34,.28)。Unity 的 Shadow 没有模糊,只取下移 2 的近似。</summary>
        private static readonly Color TagShadow = new(0.086f, 0.106f, 0.133f, 0.28f);

        private readonly Dictionary<RectTransform, (float time, int slot)> _tagSlots = new();
        private (float time, int slot) _playerTagSlot;

        /// <summary>特性名签;cue 为 null 或名字为空时不飘、返回 null。<paramref name="carrier"/> null = 玩家侧(屏幕中下)。</summary>
        public RectTransform TraitCueAt(TraitCue? cue, RectTransform carrier) =>
            cue is { } c && c.Def != null && c.Trait != null && !string.IsNullOrEmpty(c.Trait.Name)
                ? TraitLabel(c, carrier)
                : null;

        /// <summary>特性名签(稿 .lbl.trait):纸底 α .96、圆角 8、面印 s 档 27 + 宋体 22 粗,字色 = 字的字形色。</summary>
        public RectTransform TraitLabel(TraitCue cue, RectTransform carrier) =>
            SealTag(parent => Ui.TraitSeal(parent, cue.Def, cue.Trait, Ui.FaceSealS), cue.Trait.Name,
                Theme.GlyphColor(cue.Def.Element), carrier);

        /// <summary>纸底名签本体:<paramref name="seal"/> 在签内建一枚 s 档面印,后接宋体名。特性名签与嫁接(落点关键词,
        /// 印与名不来自特性表)共用。</summary>
        private RectTransform SealTag(Func<Transform, GameObject> seal, string name, Color color, RectTransform carrier)
        {
            float width = TagPadLeft + Ui.FaceSealS + TagGap + Ui.EstimateWidth(name, TagFontSize) + TagPadRight;
            var root = TagRoot("TraitTag", width, TagHeight, carrier, out var group);

            var bg = root.gameObject.AddComponent<Image>();
            bg.sprite = Theme.Rounded(TagRadius);
            bg.type = Image.Type.Sliced;
            bg.color = new Color(Theme.PanelPaper.r, Theme.PanelPaper.g, Theme.PanelPaper.b, 0.96f);
            bg.raycastTarget = false;
            var shadow = root.gameObject.AddComponent<Shadow>();
            shadow.effectColor = TagShadow;
            shadow.effectDistance = new Vector2(0f, -2f);

            var stamp = (RectTransform)seal(root).transform;
            stamp.anchorMin = stamp.anchorMax = new Vector2(0f, 0.5f);
            stamp.anchoredPosition = new Vector2(TagPadLeft + Ui.FaceSealS / 2f, 0f);

            var label = Ui.ThemedLabel(root, name, TagFontSize, color, Theme.TitleFont,
                TextAnchor.MiddleLeft);
            label.fontStyle = FontStyle.Bold;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.raycastTarget = false;
            Ui.Anchor(label.rectTransform, Vector2.zero, Vector2.one,
                new Vector2(TagPadLeft + Ui.FaceSealS + TagGap, 0f), new Vector2(-TagPadRight, 0f));

            StartCoroutine(FloatTag(root, group, 1f));
            return root;
        }

        /// <summary>状态名签(稿 .lbl.status):战场 chip 色实底(<see cref="BattleView.BattleChipColor"/>,与 chip 列表同一出处)、
        /// 全圆角、图标 21 + 白字黑体 19 粗。<paramref name="struck"/> = 被挡下(拦截):整体 60%、压一条白横线。
        /// 名取不到(StatusText 对隐藏载体返回空)不飘、返回 null。</summary>
        public RectTransform StatusLabel(StatusKind kind, int mag, bool struck, RectTransform carrier, bool isPlayer = false)
        {
            var info = StatusText.Of(kind, mag, 0, isPlayer);
            if (string.IsNullOrEmpty(info.Name)) return null;
            bool hasIcon = info.IconKey != null;
            float textLeft = StatusPadLeft + (hasIcon ? StatusIcon + TagGap : 0f);
            float textWidth = Ui.EstimateWidth(info.Name, StatusFontSize);
            float width = textLeft + textWidth + TagPadRight;
            var root = TagRoot("StatusTag", width, StatusTagHeight, carrier, out var group);

            var bg = root.gameObject.AddComponent<Image>();
            bg.sprite = Theme.Rounded(Mathf.RoundToInt(StatusTagHeight / 2f));
            bg.type = Image.Type.Sliced;
            bg.color = BattleView.BattleChipColor(kind, mag);
            bg.raycastTarget = false;

            if (hasIcon)
            {
                var sprite = Icons.Get(info.IconKey);
                Graphic icon;
                if (sprite != null)
                {
                    var image = Ui.Panel(root, "Icon").AddComponent<Image>();
                    image.sprite = sprite;
                    image.preserveAspect = true;
                    image.color = Color.white;
                    icon = image;
                }
                else icon = Ui.ThemedLabel(root, Icons.Fallback(info.IconKey), StatusFontSize, Color.white);
                icon.raycastTarget = false;
                Ui.Anchor(icon.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(StatusPadLeft, -StatusIcon / 2f), new Vector2(StatusPadLeft + StatusIcon, StatusIcon / 2f));
            }

            var label = Ui.ThemedLabel(root, info.Name, StatusFontSize, Color.white, Theme.BodyFont, TextAnchor.MiddleLeft);
            label.fontStyle = FontStyle.Bold;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.raycastTarget = false;
            Ui.Anchor(label.rectTransform, Vector2.zero, Vector2.one, new Vector2(textLeft, 0f), new Vector2(-TagPadRight, 0f));

            if (struck)
            {
                // 删除线(旧 Text 不认 <s>):白色,横贯图标到字尾,高 2
                var line = Ui.Panel(root, "Strike").AddComponent<Image>();
                line.color = Color.white;
                line.raycastTarget = false;
                Ui.Anchor(line.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(StatusPadLeft - 2f, -1f), new Vector2(textLeft + textWidth + 2f, 1f));
            }

            StartCoroutine(FloatTag(root, group, struck ? StruckTagAlpha : 1f));
            return root;
        }

        /// <summary>签的根:挂在震屏层,底边中点贴载体顶边中点(pivot 0.5,0),按 0.8s 时窗往上叠层。</summary>
        private RectTransform TagRoot(string name, float width, float height, RectTransform carrier, out CanvasGroup group)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasGroup));
            go.transform.SetParent(_shakeTarget, false);
            var rect = (RectTransform)go.transform;
            rect.pivot = new Vector2(0.5f, 0f);
            rect.sizeDelta = new Vector2(width, height);
            int slot = NextTagSlot(carrier);
            if (carrier != null)
            {
                var r = carrier.rect;
                rect.position = carrier.TransformPoint(new Vector3(r.center.x, r.yMax, 0f));
                rect.anchoredPosition += new Vector2(0f, 4f + slot * TagStackStep);
            }
            else   // 玩家侧:与 Popup 的 null 口径同一个点
            {
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.32f);
                rect.anchoredPosition = new Vector2(0f, slot * TagStackStep);
            }
            group = go.GetComponent<CanvasGroup>();   // 新建物件,typeof 里带上的
            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.interactable = false;
            return rect;
        }

        private int NextTagSlot(RectTransform carrier)
        {
            float now = UnityEngine.Time.unscaledTime;
            if (carrier == null)
            {
                _playerTagSlot = AdvanceTag(_playerTagSlot, now);
                return _playerTagSlot.slot;
            }
            if (_tagSlots.Count > 64) _tagSlots.Clear();   // 载体随重绘销毁,时窗早过,整表清掉即可
            _tagSlots.TryGetValue(carrier, out var state);
            state = AdvanceTag(state, now);
            _tagSlots[carrier] = state;
            return state.slot;
        }

        private static (float time, int slot) AdvanceTag((float time, int slot) state, float now) =>
            (now, now - state.time > TagStackWindow ? 0 : Mathf.Min(state.slot + 1, TagStackMax - 1));

        /// <summary>稿 @keyframes lb(1.3s):0% 透明 · scale .7 · 低 0.2 签高 → 14% 不透明 · 1.08 · 就位 → 24% scale 1
        /// → 78% 仍不透明、上浮 0.3 签高 → 100% 淡出、上浮 0.5 签高。CanvasGroup 统一淡出(签是复合体)。</summary>
        private static IEnumerator FloatTag(RectTransform rect, CanvasGroup group, float baseAlpha)
        {
            float h = rect.sizeDelta.y;
            Vector2 home = rect.anchoredPosition;
            float t = 0f;
            while (t < TagDuration && rect != null)
            {
                t += UnityEngine.Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / TagDuration);
                float y, s, a;
                if (k < 0.14f)
                {
                    float u = k / 0.14f;
                    y = Mathf.Lerp(-0.2f * h, 0f, u);
                    s = Mathf.Lerp(0.7f, 1.08f, u);
                    a = u;
                }
                else if (k < 0.78f)
                {
                    y = Mathf.Lerp(0f, 0.3f * h, (k - 0.14f) / 0.64f);
                    s = k < 0.24f ? Mathf.Lerp(1.08f, 1f, (k - 0.14f) / 0.1f) : 1f;
                    a = 1f;
                }
                else
                {
                    float u = (k - 0.78f) / 0.22f;
                    y = Mathf.Lerp(0.3f * h, 0.5f * h, u);
                    s = 1f;
                    a = 1f - u;
                }
                rect.anchoredPosition = home + new Vector2(0f, y);
                rect.localScale = new Vector3(s, s, 1f);
                if (group != null) group.alpha = a * baseAlpha;
                yield return null;
            }
            if (rect != null) Destroy(rect.gameObject);
        }

        /// <summary>载体闪(稿 .flash,0.45s):25% 处 scale 1.4、往白推(≈ brightness 1.55)、白描环 3 + 赭金发光,再收回。
        /// 只改 localScale,描环与发光 ignoreLayout,不挤动同排 chip。同一载体在闪时不重入(<see cref="_flashing"/>)。
        /// 返回载体本身(飘名锚点用),null 原样返回。</summary>
        /// <param name="glow">外发光色;缺省赭金。格挡反击的格挡 chip 用铜绿(counterfx 稿 .flash)。</param>
        public RectTransform FlashCarrier(RectTransform carrier, Color? glow = null)
        {
            if (carrier != null && _flashing.Add(carrier)) StartCoroutine(CarrierFlashRoutine(carrier, true, glow ?? FlashGlow));
            return carrier;
        }

        /// <summary>立绘版载体闪(稿 .pt.flash):不缩放,只加亮(≈ brightness 1.5)+ 赭金外发光,0.45s。</summary>
        public void FlashPortrait(RectTransform portrait)
        {
            if (portrait != null && _flashing.Add(portrait)) StartCoroutine(CarrierFlashRoutine(portrait, false, FlashGlow));
        }

        private IEnumerator CarrierFlashRoutine(RectTransform carrier, bool chip, Color glowColor)
        {
            var images = carrier.GetComponentsInChildren<Image>(true);
            var original = new Color[images.Length];
            for (int i = 0; i < images.Length; i++) original[i] = images[i].color;
            float size = Mathf.Min(carrier.rect.width, carrier.rect.height);
            var glow = FlashRing(carrier, "FlashGlow", glowColor, chip ? 6f : 8f, 6, size);
            var ring = chip ? FlashRing(carrier, "FlashRing", Color.white, 1.5f, 3, size) : null;
            float push = chip ? 0.35f : 0.33f;

            float t = 0f;
            while (t < CarrierFlashDuration && carrier != null)
            {
                t += UnityEngine.Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / CarrierFlashDuration);
                float w = k < 0.25f ? k / 0.25f : 1f - (k - 0.25f) / 0.75f;
                if (chip)
                {
                    float s = (1f + 0.4f * w) * ShrinkOf(carrier);   // 格挡 chip 扣到 0 时同时在缩没(JuiceCounter)
                    carrier.localScale = new Vector3(s, s, 1f);
                }
                for (int i = 0; i < images.Length; i++)
                    if (images[i] != null)
                    {
                        var c = Color.Lerp(original[i], Color.white, push * w);
                        c.a = original[i].a;
                        images[i].color = c;
                    }
                if (glow != null) glow.color = new Color(glowColor.r, glowColor.g, glowColor.b, glowColor.a * w);
                if (ring != null) ring.color = new Color(1f, 1f, 1f, w);
                yield return null;
            }
            if (carrier != null && chip) { float m = ShrinkOf(carrier); carrier.localScale = new Vector3(m, m, 1f); }
            for (int i = 0; i < images.Length; i++)
                if (images[i] != null) images[i].color = original[i];
            if (glow != null) Destroy(glow.gameObject);
            if (ring != null) Destroy(ring.gameObject);
            _flashing.Remove(carrier);
        }

        private float ShrinkOf(RectTransform carrier) => _chipShrink.TryGetValue(carrier, out var mul) ? mul : 1f;

        // ---- 主动特性出字飘名(B-Qa1,TriggerFx A)----

        private const float CastCueLead = 0.26f;   // 稿:t0 飘名,+260 落效果
        private readonly List<TraitCue> _castCues = new();
        private RectTransform _castCueAnchor;

        /// <summary>出字前由 BattleView 算好(<see cref="TraitCueRules.CastAnnounced"/>)交过来:下一段 <see cref="Play"/> 开头在
        /// <paramref name="anchor"/>(目标敌人格;null = 玩家侧)上飘这几条名签,停一拍再落效果。只在出字成功、起演出时调,
        /// 播放开头即清,不会漏进下一段。</summary>
        public void QueueCastTraits(IEnumerable<TraitCue> cues, RectTransform anchor)
        {
            _castCues.Clear();
            _castCues.AddRange(cues);
            _castCueAnchor = anchor;
        }

        private IEnumerator PlayCastTraits()
        {
            if (_castCues.Count == 0) yield break;
            var cues = new List<TraitCue>(_castCues);
            var anchor = _castCueAnchor;
            _castCues.Clear();
            _castCueAnchor = null;
            bool any = false;
            foreach (var cue in cues) any |= TraitCueAt(cue, anchor) != null;
            if (any) yield return Beat(CastCueLead);
        }

        // ---- 冰滞结霜(TriggerFx E)----

        private const float FrostSnapDuration = 0.35f;

        /// <summary>冰滞挂上那一下(稿 .atb.snap):行动条外圈一道水字形色描边发光,0.35s 亮起再收,不动布局。</summary>
        public void FrostSnap(RectTransform bar)
        {
            if (bar != null) StartCoroutine(FrostSnapRoutine(bar));
        }

        private IEnumerator FrostSnapRoutine(RectTransform bar)
        {
            var water = Theme.GlyphColor(Element.Water);
            var ring = FlashRing(bar, "FrostSnap", water, 3f, 2, Mathf.Min(bar.rect.width, bar.rect.height));
            float t = 0f;
            while (t < FrostSnapDuration && ring != null)
            {
                t += UnityEngine.Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / FrostSnapDuration);
                ring.color = new Color(water.r, water.g, water.b, k < 0.3f ? k / 0.3f : 1f - (k - 0.3f) / 0.7f);
                yield return null;
            }
            if (ring != null) Destroy(ring.gameObject);
        }

        /// <summary>载体外圈的一道描环(空心圆角,外扩 <paramref name="outset"/>),不参与布局、不挡点击。</summary>
        private static Image FlashRing(RectTransform carrier, string name, Color color, float outset, int thickness, float size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            go.transform.SetParent(carrier, false);
            go.GetComponent<LayoutElement>().ignoreLayout = true;
            var image = go.GetComponent<Image>();
            int radius = Mathf.Max(thickness + 1, Mathf.RoundToInt((size + outset * 2f) / 2f));
            image.sprite = Theme.RoundedRing(radius, thickness);
            image.type = Image.Type.Sliced;
            image.raycastTarget = false;
            image.color = new Color(color.r, color.g, color.b, 0f);
            Ui.Anchor(image.rectTransform, Vector2.zero, Vector2.one,
                new Vector2(-outset, -outset), new Vector2(outset, outset));
            return image;
        }
    }
}
