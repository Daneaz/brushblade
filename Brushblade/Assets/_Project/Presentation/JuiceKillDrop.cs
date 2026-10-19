using System;
using System.Collections;
using System.Collections.Generic;
using Brushblade.Core;
using Brushblade.Data;
using UnityEngine;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>击杀得字飞入(spec 2026-10-18 斩获终极技 §2,drafts/killdrop 10-18 拍板)。
    ///
    /// 斩获 / 折桂 / 锋刃回鞘的 <see cref="BattleEventKind.CharGranted"/>:① 冒 —— 阵亡那一刻 token(就是 <see cref="FlyGlyph"/> 那枚)
    /// 从立绘中心弹出、上移 30,赭金环扩散,同格飘名签;② 停 —— token 停在震屏层(BattleView 根节点,重绘不清)等本批收尾;
    /// ③ 飞 / ④ 落由 BattleView 在 OnAnimDone 重绘之后调 <see cref="LandGrantTokens"/>(它负责持锁与藏牌,同 DealRoutine)。
    /// 字库满(<see cref="BattleEventKind.CharGrantBlocked"/>)不冒 token,只飘划线的「斩获 · 字库满」,
    /// 批尾的字库计数闪由 BattleView 调 <see cref="FlashCount"/>。全部时长 ÷ 倍率(走 Dt)。</summary>
    public sealed partial class Juice
    {
        // ---- 时长(秒,1×;稿 kd.js T)----
        private const float GrantPopDuration = 0.18f;   // ① 冒
        private const float GrantRise = 30f;            //    上移量(逻辑单位)
        private const float GrantParkMin = 0.25f;       // ② 停:冒出后至少停这么久
        private const float GrantFlyDuration = 0.45f;   // ③ 飞(同回合掉字 DealFlyDuration)
        private const float GrantArc = 46f;             //    弧高
        private const float GrantStagger = 0.12f;       //    多张错峰(同 DealStagger)
        private const float GrantFadeDuration = 0.30f;  //    当拍胜利:原地淡出
        private const float GrantRingDuration = 0.30f;  //    赭金环扩散
        private const float GrantRingDelay = GrantPopDuration * 0.6f;
        private const float GrantRingSize = 71f;        //    34pt
        private const int GrantRingWidth = 3;           //    1.5pt
        private const float CountFlashDuration = 0.45f; // 字库计数闪:scale 1.25 回弹
        private const float CountFlashHold = 0.9f;      //    朱砂色保持(稿 .cap.flash 0.9s 后摘掉)

        // ---- 无面印名签(技能树来源;稿 .lbl:高 16pt、左右 5pt、字 ink-soft)----
        private const float PerkTagPad = 10.5f;         // 5pt
        private const int PerkTagSmallFont = 16;        // 7.5pt
        private const float PerkTagStrike = 2.5f;       // 删除线 1.2pt

        /// <summary>字库下标 → 那张字(BattleView 设);token 的字形与底色由它取。</summary>
        public Func<int, CharDef> LibraryCharAt { get; set; }

        private sealed class GrantToken
        {
            public RectTransform Rect;   // null = 没冒(找不到阵亡格):落位时直接现身,不飞
            public string Glyph;
            public Color Color;
            public int LibraryIndex;
            public float ReadyAt;        // _pacedClock:冒完 + 至少停 0.25s
        }

        /// <summary>本批冒出、等着批尾飞入的 token。震屏层不随重绘清,所以跨得过 AdvanceRoutine 每拍的 Refresh。</summary>
        private readonly List<GrantToken> _grantTokens = new();

        public bool HasGrantTokens => _grantTokens.Count > 0;

        /// <summary>BattleView 藏牌用:这一批要落位的字库下标。</summary>
        public IEnumerable<int> GrantTokenIndices()
        {
            foreach (var t in _grantTokens) yield return t.LibraryIndex;
        }

        /// <summary>Play 里遇到 CharGranted:飘名签、冒 token、登记。<paramref name="cell"/> = 阵亡敌人格(取不到为 null)。</summary>
        private void PlayCharGranted(BattleEvent e, RectTransform cell)
        {
            if (e.Source == EffectSource.KillDraw) PerkCueAt(Strings.T("perk.node.ult_killdraw.name"), cell);
            else if (e.Source == EffectSource.EdgeSheathe) PerkCueAt(Strings.T("juice.popup.edge_sheathe"), cell);   // 锋刃回鞘:本张字回库
            else TraitCueAt(EffectCueOf?.Invoke(EffectKind.GrantChar), cell);   // 折桂:「攻」印 + 折桂(特性名签原样)

            var def = LibraryCharAt?.Invoke(e.Amount);
            if (def == null) return;
            var token = new GrantToken
            {
                Glyph = def.Id, Color = Theme.ElementColor(def.Element), LibraryIndex = e.Amount,
                ReadyAt = _pacedClock + GrantPopDuration + GrantParkMin,
            };
            _grantTokens.Add(token);
            if (cell == null || _shakeTarget == null) return;

            var portrait = PortraitOf(cell);
            var start = portrait.TransformPoint(portrait.rect.center);
            float unit = _shakeTarget.lossyScale.y;
            // 停点:上移 30,夹在本格顶边以下半个 token(后排格顶 = 战场顶,稿「出画」条)
            float cellTop = cell.TransformPoint(new Vector3(0f, cell.rect.yMax, 0f)).y;
            float parkY = Mathf.Min(start.y + GrantRise * unit, cellTop - 28f * unit);
            var park = new Vector3(start.x, Mathf.Max(start.y, parkY), start.z);
            token.Rect = GlyphToken(token.Glyph, token.Color, start);
            StartCoroutine(PopTokenRoutine(token.Rect, start, park));
            StartCoroutine(GrantRingRoutine(park));
        }

        /// <summary>字库满挡下斩获:不冒 token,阵亡格飘划线的「斩获」+「字库满」(同「被挡下」样式:整体 α .6 + 删除线)。</summary>
        private void PlayGrantBlocked(BattleEvent e, RectTransform cell) =>
            PerkCueAt(Strings.T("perk.node.ult_killdraw.name"), cell, Strings.T("juice.popup.kill_draw_full"));

        /// <summary>阵亡格里的立绘(MobView);纯字头像格退回整格。显式判空:Unity 假 null 过不了 ?.</summary>
        private static RectTransform PortraitOf(RectTransform cell)
        {
            var mob = cell.GetComponentInChildren<MobView>();
            return mob != null ? (RectTransform)mob.transform : cell;
        }

        /// <summary>① 冒(稿:0 → 70% 处上移 1.1 倍、scale .3 → 1.18、α 0 → 1 → 100% 就位 scale 1;ease-out 驱动)。</summary>
        private IEnumerator PopTokenRoutine(RectTransform rect, Vector3 start, Vector3 park)
        {
            var group = rect.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.alpha = 0f;
            rect.localScale = Vector3.one * 0.3f;
            yield return Tween(GrantPopDuration, k =>
            {
                if (rect == null) return;
                float e = 1f - (1f - k) * (1f - k) * (1f - k);
                float rise, scale, alpha;
                if (e < 0.7f)
                {
                    float u = e / 0.7f;
                    rise = 1.1f * u; scale = Mathf.Lerp(0.3f, 1.18f, u); alpha = u;
                }
                else
                {
                    float u = (e - 0.7f) / 0.3f;
                    rise = Mathf.Lerp(1.1f, 1f, u); scale = Mathf.Lerp(1.18f, 1f, u); alpha = 1f;
                }
                rect.position = Vector3.LerpUnclamped(start, park, rise);
                rect.localScale = Vector3.one * scale;
                group.alpha = alpha;
            });
            if (rect == null) yield break;
            rect.position = park;
            rect.localScale = Vector3.one;
            group.alpha = 1f;
        }

        /// <summary>冒字时的一圈赭金环(0.11s 起,0.3s 内 scale .5 → 1.5、α .95 → 0)。</summary>
        private IEnumerator GrantRingRoutine(Vector3 at)
        {
            if (_shakeTarget == null) yield break;
            yield return Beat(GrantRingDelay);
            var go = new GameObject("GrantRing", typeof(RectTransform));
            go.transform.SetParent(_shakeTarget, false);
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(GrantRingSize, GrantRingSize);
            rect.position = at;
            var image = go.AddComponent<Image>();
            image.sprite = Theme.RoundedRing(Mathf.RoundToInt(GrantRingSize / 2f), GrantRingWidth);
            image.raycastTarget = false;
            var gold = Theme.Gold;
            yield return Tween(GrantRingDuration, k =>
            {
                if (rect == null) return;
                float e = 1f - (1f - k) * (1f - k);
                rect.localScale = Vector3.one * Mathf.Lerp(0.5f, 1.5f, e);
                image.color = new Color(gold.r, gold.g, gold.b, 0.95f * (1f - e));
            });
            if (rect != null) Destroy(rect.gameObject);
        }

        /// <summary>③ 飞 / ④ 落:按落位顺序逐张从停点弧线飞到 <paramref name="tileAt"/> 给的牌,到达把牌 scale 恢复 1 + PopTile,
        /// 再回调 <paramref name="onLand"/>(BattleView 记新到手光晕)。牌取不到(当拍胜利,结算不画字库)= 原地淡出,不飞。
        /// 每张都至少停到冒完 + 0.25s;多张错开 0.12s。调用方负责持锁与先藏牌(scale 0)、等一帧布局。</summary>
        public IEnumerator LandGrantTokens(Func<int, RectTransform> tileAt, Action<int, RectTransform> onLand)
        {
            var tokens = new List<GrantToken>(_grantTokens);
            _grantTokens.Clear();
            tokens.Sort((a, b) => a.LibraryIndex.CompareTo(b.LibraryIndex));
            int pending = 0;
            bool flown = false;
            foreach (var tk in tokens)
            {
                var tile = tileAt(tk.LibraryIndex);
                if (tk.Rect == null)   // 没冒的:直接现身
                {
                    Reveal(tile);
                    onLand?.Invoke(tk.LibraryIndex, tile);
                    continue;
                }
                while (_pacedClock < tk.ReadyAt && tk.Rect != null) yield return null;   // ② 停
                if (tk.Rect == null) { Reveal(tile); onLand?.Invoke(tk.LibraryIndex, tile); continue; }
                tile = tileAt(tk.LibraryIndex);   // 停的这几帧里可能重绘过,现取
                if (tile == null)
                {
                    pending++;
                    StartCoroutine(FadeTokenRoutine(tk.Rect, () => pending--));
                    onLand?.Invoke(tk.LibraryIndex, null);
                    continue;
                }
                if (flown) yield return Beat(GrantStagger);
                flown = true;
                var from = tk.Rect.position;
                Destroy(tk.Rect.gameObject);
                pending++;
                int index = tk.LibraryIndex;
                FlyGlyph(tk.Glyph, tk.Color, from, tile.position, () =>
                {
                    Reveal(tile);
                    onLand?.Invoke(index, tile);
                    pending--;
                }, GrantFlyDuration, easeOut: false, arc: GrantArc);
            }
            while (pending > 0) yield return null;
        }

        /// <summary>落位:真牌现身 + 弹一下。跨帧持有的 RectTransform 一律判空。</summary>
        private void Reveal(RectTransform tile)
        {
            if (tile == null) return;
            tile.localScale = Vector3.one;
            PopTile(tile);
        }

        /// <summary>当拍胜利:停点原地 0.3s 淡出、略上浮 6。</summary>
        private IEnumerator FadeTokenRoutine(RectTransform rect, Action done)
        {
            var group = rect != null ? rect.GetComponent<CanvasGroup>() : null;
            var home = rect != null ? rect.position : Vector3.zero;
            float unit = _shakeTarget != null ? _shakeTarget.lossyScale.y : 1f;
            yield return Tween(GrantFadeDuration, k =>
            {
                if (rect == null) return;
                rect.position = home + new Vector3(0f, 6f * unit * k, 0f);
                if (group != null) group.alpha = 1f - k;
            });
            if (rect != null) Destroy(rect.gameObject);
            done?.Invoke();
        }

        /// <summary>批尾字库计数闪(稿 .cap.flash):朱砂深色加粗 0.9s,其间 0.45s scale 1 → 1.25 → 1。</summary>
        public void FlashCount(Text label)
        {
            if (label != null) StartCoroutine(FlashCountRoutine(label));
        }

        private IEnumerator FlashCountRoutine(Text label)
        {
            var color = label.color;
            var style = label.fontStyle;
            label.color = Theme.CinnabarDark;
            label.fontStyle = FontStyle.Bold;
            var rect = label.rectTransform;
            yield return Tween(CountFlashDuration, k =>
            {
                if (rect != null) rect.localScale = Vector3.one * (1f + 0.25f * Mathf.Sin(k * Mathf.PI));
            });
            if (rect != null) rect.localScale = Vector3.one;
            yield return Beat(CountFlashHold - CountFlashDuration);
            if (label == null) yield break;
            label.color = color;
            label.fontStyle = style;
        }

        /// <summary>无面印名签(技能树来源,稿 .lbl):纸底 α .96、宋体 22 粗、字 ink-soft,与特性名签同一条飘法(走 _rate)。
        /// <paramref name="blockedNote"/> 非空 = 被挡下:整体 α .6、名字压删除线、后接黑体小字(「字库满」)。</summary>
        private RectTransform PerkCueAt(string name, RectTransform carrier, string blockedNote = null)
        {
            if (string.IsNullOrEmpty(name)) return null;
            bool struck = !string.IsNullOrEmpty(blockedNote);
            float nameWidth = Ui.EstimateWidth(name, TagFontSize);
            float noteWidth = struck ? Ui.EstimateWidth(blockedNote, PerkTagSmallFont) : 0f;
            float width = PerkTagPad + nameWidth + (struck ? TagGap + noteWidth : 0f) + PerkTagPad;
            var root = TagRoot("PerkTag", width, TagHeight, carrier, out var group);

            var bg = root.gameObject.AddComponent<Image>();
            bg.sprite = Theme.Rounded(TagRadius);
            bg.type = Image.Type.Sliced;
            bg.color = new Color(Theme.PanelPaper.r, Theme.PanelPaper.g, Theme.PanelPaper.b, 0.96f);
            bg.raycastTarget = false;
            var shadow = root.gameObject.AddComponent<Shadow>();
            shadow.effectColor = TagShadow;
            shadow.effectDistance = new Vector2(0f, -2f);

            var label = Ui.ThemedLabel(root, name, TagFontSize, Theme.InkSoft, Theme.TitleFont, TextAnchor.MiddleLeft);
            label.fontStyle = FontStyle.Bold;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.raycastTarget = false;
            Ui.Anchor(label.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(PerkTagPad, 0f), new Vector2(PerkTagPad + nameWidth, 0f));

            if (struck)
            {
                // 删除线(旧 Text 不认 <s>):ink-soft,只压名字
                var line = Ui.Panel(root, "Strike").AddComponent<Image>();
                line.color = Theme.InkSoft;
                line.raycastTarget = false;
                Ui.Anchor(line.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(PerkTagPad - 1f, -PerkTagStrike / 2f), new Vector2(PerkTagPad + nameWidth + 1f, PerkTagStrike / 2f));
                var note = Ui.ThemedLabel(root, blockedNote, PerkTagSmallFont, Theme.TextDim, Theme.BodyFont, TextAnchor.MiddleLeft);
                note.horizontalOverflow = HorizontalWrapMode.Overflow;
                note.raycastTarget = false;
                Ui.Anchor(note.rectTransform, Vector2.zero, Vector2.one,
                    new Vector2(PerkTagPad + nameWidth + TagGap, 0f), new Vector2(-PerkTagPad, 0f));
            }

            StartCoroutine(FloatTag(root, group, struck ? StruckTagAlpha : 1f, paced: true));
            return root;
        }
    }
}
