using System;
using System.Collections;
using System.Collections.Generic;
using Brushblade.Core;
using Brushblade.Data;
using UnityEngine;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>召唤物出手(2026-09-30,「近战召唤物出手」demo 拍板)。
    ///
    /// 一、**招牌动作**:冲脸骨架不变,按字意换落点 —— 森砸树干、桂花雨、藻双鞭、四木合围生根;
    ///     藤缠绕、楸甩叶在 JuiceAttacks;其余近战(林……)走冲脸 + 刀口 + 叶。
    /// 二、**减半版档位**(召唤物每回合自动出手、频率高,所以**不顿帧、不写大字**):
    ///     紫起落点一圈稀有度色光环;金起冲刺留光尾;橙起再加两道残影、粒子 ×1.5,
    ///     并照四木的套路多一个小蓄势和一个收尾;红(四木)粒子 ×2 + 光环脉冲。
    /// 三、**防守型**:在敌人打召唤物那一支演 嘲讽 → 挨打 → 盾反 三拍
    ///     (<see cref="GuardBegin"/> / <see cref="GuardHit"/> / <see cref="ThornsFly"/>)。
    ///     嘲讽那一拍看 Taunt(柘 / 荆);举盾、盾面迎击、回弹波看「盾反」(Thorns,2026-10-02 起
    ///     不再是柘独有 —— 𣛧 / 森 / 荆 / 桂 / 柘 凡带盾反的都举盾)。</summary>
    public sealed partial class Juice
    {
        /// <summary>字 → 稀有度(召唤物的档位按召出它的那张字算)。BattleView 初始化时注入;
        /// 没注入时一律按白档,只少了档位加码,不会报错。</summary>
        public Func<string, CardRarity> RarityOf { get; set; }

        /// <summary>四木(𣛧)在字表里落在私用区(增补平面字 → PUA,见 export_chars._output_id)。</summary>
        private const string SimuId = "";

        private static readonly Color TauntRed = new(0.82f, 0.33f, 0.23f);
        private static readonly Color ShieldGold = new(0.9f, 0.82f, 0.6f);
        private static readonly Color Osmanthus = new(0.95f, 0.76f, 0.31f);
        private static readonly Color TrunkBrown = new(0.45f, 0.29f, 0.14f);
        private static readonly Color WeedGreen = new(0.18f, 0.49f, 0.36f);

        private Action<float, Vector3> _dashHook;
        private float _dashReach = 1f;

        /// <summary>这一场里已经放过完整版的四木(按 SummonState 实例认:新一场的召唤物都是新实例)。</summary>
        private readonly HashSet<SummonState> _simuFullShown = new();

        private CardRarity RarityOfSummon(SummonState s) =>
            s == null || RarityOf == null ? CardRarity.White : RarityOf(s.SourceChar ?? s.Char);

        private static float SummonMore(CardRarity r) => r >= CardRarity.Red ? 2f : r >= CardRarity.Orange ? 1.5f : 1f;

        // ---- 分派 ----

        private IEnumerator SummonSignature(SummonState attacker, RectTransform from, RectTransform to, Element element)
        {
            var rarity = RarityOfSummon(attacker);
            switch (attacker?.Char)
            {
                case "藤": yield return SummonVine(from, to); break;
                case "楸": yield return SummonLeaves(from, to, element); break;
                case "森": yield return SenStrike(from, to, element, rarity); break;
                case "桂": yield return GuiStrike(from, to, element, rarity); break;
                case "藻": yield return ZaoStrike(from, to, element, rarity); break;
                case SimuId: yield return SimuStrike(attacker, from, to, element, rarity); break;
                default:
                    if (attacker?.Passive?.Ranged ?? false) yield return SummonShoot(from, to, element);
                    else yield return TieredMelee(from, to, element, rarity, leafCount: 6);
                    break;
            }
        }

        // ---- 减半版档位 ----

        /// <summary>带档位光尾 / 残影 / 光环脉冲的冲脸;extraHook 给招牌动作挂自己的路上效果(桂的香尾)。</summary>
        private IEnumerator TieredDash(RectTransform from, RectTransform to, Color color, CardRarity rarity, bool slash,
            float reach = 1f, Action<float, Vector3> extraHook = null)
        {
            var tint = Theme.RarityColor(rarity);
            float nextDot = 0f;
            bool ghost1 = false, ghost2 = false, pulse1 = false, pulse2 = false;
            _dashReach = reach;
            _dashHook = (k, world) =>
            {
                extraHook?.Invoke(k, world);
                Vector2 p = Local(world);
                if (rarity >= CardRarity.Gold && k >= nextDot)
                {
                    nextDot = k + 0.12f;
                    var dot = Bit(null, new Vector2(8f, 8f), tint, p, out var img, circle: true);
                    StartCoroutine(FadeAndKill(dot, img, 0.32f, -0.6f));
                }
                if (rarity >= CardRarity.Orange && from != null)
                {
                    if (!ghost1 && k >= 0.5f) { ghost1 = true; Afterimage(from, tint); }
                    if (!ghost2 && k >= 0.8f) { ghost2 = true; Afterimage(from, tint); }
                }
                if (rarity >= CardRarity.Red)
                {
                    if (!pulse1 && k >= 0.25f) { pulse1 = true; RingAt(world, tint); }
                    if (!pulse2 && k >= 0.75f) { pulse2 = true; RingAt(world, new Color(0.49f, 0.77f, 0.5f)); }
                }
            };
            yield return BodyStrike(from, to, AnchorPoint(to), color, slash);
            _dashHook = null;
            _dashReach = 1f;
            if (to != null && rarity >= CardRarity.Purple) Ring(to, tint);
        }

        /// <summary>残影:本体当前这一帧的淡影,原地淡出。</summary>
        private void Afterimage(RectTransform from, Color tint)
        {
            var go = Instantiate(from.gameObject, _shakeTarget, true);
            go.name = "Afterimage";
            foreach (Transform child in go.transform) if (child.name == "Chip") Destroy(child.gameObject);
            foreach (var g in go.GetComponentsInChildren<Graphic>(true)) g.raycastTarget = false;
            var layout = go.TryGetComponent<LayoutElement>(out var existingLayout) ? existingLayout : go.AddComponent<LayoutElement>();
            layout.ignoreLayout = true;
            var canvas = go.GetComponent<Canvas>();
            if (canvas != null) Destroy(canvas);   // 冲刺置顶那个临时 Canvas 别跟着复制过来
            var group = go.TryGetComponent<CanvasGroup>(out var existingGroup) ? existingGroup : go.AddComponent<CanvasGroup>();
            group.alpha = 0.35f;
            group.blocksRaycasts = false;
            StartCoroutine(FadeGroup(go, group, 0.26f));
        }

        private IEnumerator FadeGroup(GameObject go, CanvasGroup group, float duration)
        {
            float start = group.alpha;
            yield return Tween(duration, k => { if (group != null) group.alpha = start * (1f - k); });
            if (go != null) Destroy(go);
        }

        private IEnumerator TieredMelee(RectTransform from, RectTransform to, Element element, CardRarity rarity, int leafCount)
        {
            var color = Theme.GlyphColor(element);
            SummonViewOf(from)?.PlayAttack();
            yield return TieredDash(from, to, color, rarity, slash: true);
            if (to != null) LeafBurst(to.position, color, Mathf.RoundToInt(leafCount * SummonMore(rarity)));
            PlayClip(_hitClip, 0.55f, 1.15f);
        }

        /// <summary>橙档小蓄势:count 个环绕物绕本体转半圈(参照四木,规模小一半)。返回它们,调用方决定去向。</summary>
        private IEnumerator OrbitCharge(RectTransform from, int count, Func<int, RectTransform> make, float radius,
            List<RectTransform> into, CardRarity rarity)
        {
            if (from == null) yield break;
            Vector2 c = Local(from.position);
            float r = SizeOf(from) * radius;
            RingAt(from.position, Theme.RarityColor(rarity));
            for (int i = 0; i < count; i++) into.Add(make(i));
            yield return Tween(0.2f, k =>
            {
                for (int i = 0; i < into.Count; i++)
                {
                    if (into[i] == null) continue;
                    float t = k * Mathf.PI + i / (float)count * Mathf.PI * 2f;
                    into[i].localPosition = c + new Vector2(Mathf.Cos(t) * r, Mathf.Sin(t) * r * 0.6f);
                }
            });
        }

        /// <summary>一个宋体小字当特效(森的「木」、四木的四个「木」)。</summary>
        private RectTransform GlyphBit(string glyph, float size, Color color)
        {
            var go = new GameObject("GlyphBit", typeof(RectTransform));
            go.transform.SetParent(_shakeTarget, false);
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(size, size);
            var label = go.AddComponent<Text>();
            label.font = Theme.TitleFont;
            label.fontSize = Mathf.RoundToInt(size * 0.9f);
            label.fontStyle = FontStyle.Bold;
            label.text = glyph;
            label.color = color;
            label.alignment = TextAnchor.MiddleCenter;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;
            var outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(0.8f, 0.15f, 0.18f, 0.8f);
            return rect;
        }

        // ---- 森:三木绕身 → 主干从天砸下、三截小树干一同落下 → 橙环 + 扬尘 ----

        private IEnumerator SenStrike(RectTransform from, RectTransform to, Element element, CardRarity rarity)
        {
            if (from == null || to == null) yield break;
            var color = Theme.GlyphColor(element);
            SummonViewOf(from)?.PlayAttack();
            var seeds = new List<RectTransform>();
            if (rarity >= CardRarity.Orange)
                yield return OrbitCharge(from, 3, _ => GlyphBit("木", 30f, new Color(1f, 0.85f, 0.63f)), 0.7f, seeds, rarity);
            yield return TieredDash(from, to, color, rarity, slash: false);
            foreach (var s in seeds) if (s != null) Destroy(s.gameObject);
            if (to == null) yield break;
            Vector2 b = Local(to.position);
            float size = SizeOf(to);
            StartCoroutine(TrunkDrop(b, size * 0.45f, size * 1.3f, 0f));
            if (rarity >= CardRarity.Orange)
            {
                StartCoroutine(TrunkDrop(b + new Vector2(-size * 0.55f, 0f), size * 0.22f, size * 0.7f, 0.04f));
                StartCoroutine(TrunkDrop(b + new Vector2(size * 0.55f, 0f), size * 0.22f, size * 0.7f, 0.08f));
            }
            yield return Beat(0.14f);
            StartCoroutine(Shake(18f, Vector2.down));
            PlayClip(_thudClip, 0.9f, 0.7f);
            Shards(b + new Vector2(0f, -size * 0.3f), Mathf.RoundToInt(8 * SummonMore(rarity)), new Color(0.6f, 0.52f, 0.42f, 0.55f),
                new Vector2(14f, 24f), 30f, 80f, -20f, 0f, 180f, circle: true);
            if (rarity >= CardRarity.Orange) RingAt(to.position, Theme.RarityColor(rarity));
            LeafBurst(to.position, color, Mathf.RoundToInt(5 * SummonMore(rarity)));
        }

        /// <summary>一截树干从目标上方砸下,落地后压扁淡出。</summary>
        private IEnumerator TrunkDrop(Vector2 at, float width, float height, float delay)
        {
            if (delay > 0f) yield return Beat(delay);
            var trunk = Bit(null, new Vector2(width, height), TrunkBrown, at + new Vector2(0f, height * 1.4f), out var image);
            image.sprite = Theme.Rounded(Mathf.RoundToInt(width * 0.4f));
            Vector2 top = at + new Vector2(0f, height * 1.4f), land = at + new Vector2(0f, height * 0.25f);
            yield return Tween(0.14f, k => { if (trunk != null) trunk.localPosition = Vector2.Lerp(top, land, k * k); });
            StartCoroutine(FadeAndKill(trunk, image, 0.26f, -0.1f));
        }

        // ---- 桂:一圈桂花绕身 → 冲刺留香尾 → 八瓣花形光环 + 花雨 ----

        private IEnumerator GuiStrike(RectTransform from, RectTransform to, Element element, CardRarity rarity)
        {
            if (from == null || to == null) yield break;
            SummonViewOf(from)?.PlayAttack();
            var blooms = new List<RectTransform>();
            if (rarity >= CardRarity.Orange)
                yield return OrbitCharge(from, 6, i => Bit(null, new Vector2(10f, 10f), i % 2 == 0 ? Osmanthus : new Color(1f, 0.95f, 0.76f),
                    Local(from.position), out _, circle: true), 0.62f, blooms, rarity);
            foreach (var bl in blooms) if (bl != null) Destroy(bl.gameObject);
            float nextPetal = 0f;
            yield return TieredDash(from, to, Osmanthus, rarity, slash: true, extraHook: (k, world) =>
            {
                if (k < nextPetal) return;
                nextPetal = k + 0.1f;
                var p = Bit(null, new Vector2(7f, 7f), Osmanthus, Local(world), out var img, circle: true);
                StartCoroutine(Drift(p, img, new Vector2(UnityEngine.Random.Range(-10f, 10f), -18f), 0.7f));
            });
            if (to == null) yield break;
            Vector2 b = Local(to.position);
            float size = SizeOf(to);
            Ring(to, Osmanthus);
            if (rarity >= CardRarity.Orange)
                for (int i = 0; i < 8; i++)   // 八瓣花形光环
                {
                    float ang = i / 8f * Mathf.PI * 2f;
                    var petal = Bit("leaf", new Vector2(size * 0.3f, size * 0.3f), i % 2 == 0 ? Osmanthus : Theme.RarityColor(rarity), b, out var img);
                    StartCoroutine(PetalOut(petal, img, b, b + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang) * 0.75f) * size * 0.8f, ang * Mathf.Rad2Deg));
                }
            int rain = Mathf.RoundToInt(10 * SummonMore(rarity));
            for (int i = 0; i < rain; i++)   // 花雨:慢慢飘落
            {
                Vector2 at = b + new Vector2(UnityEngine.Random.Range(-size * 0.7f, size * 0.7f), size * 0.6f);
                var p = Bit(null, new Vector2(8f, 8f), i % 3 == 0 ? new Color(1f, 0.95f, 0.76f) : Osmanthus, at, out var img, circle: true);
                StartCoroutine(Drift(p, img, new Vector2(UnityEngine.Random.Range(-20f, 20f), -size * 1.3f), 0.9f, UnityEngine.Random.Range(0f, 0.2f)));
            }
            PlayClip(_healClip, 0.5f, 1.2f);
        }

        private IEnumerator Drift(RectTransform rect, Image image, Vector2 by, float duration, float delay = 0f)
        {
            if (delay > 0f) yield return Beat(delay);
            if (rect == null) yield break;
            Vector2 start = rect.localPosition;
            Color c = image.color;
            yield return Tween(duration, k =>
            {
                if (rect == null) return;
                rect.localPosition = start + by * k + new Vector2(Mathf.Sin(k * 9f) * 5f, 0f);
                image.color = new Color(c.r, c.g, c.b, k < 0.2f ? k / 0.2f : 1f - (k - 0.2f) / 0.8f);
            });
            if (rect != null) Destroy(rect.gameObject);
        }

        private IEnumerator PetalOut(RectTransform rect, Image image, Vector2 from, Vector2 to, float angle)
        {
            Color c = image.color;
            yield return Tween(0.5f, k =>
            {
                if (rect == null) return;
                float e = 1f - (1f - k) * (1f - k);
                rect.localPosition = Vector2.Lerp(from, to, e);
                rect.localRotation = Quaternion.Euler(0f, 0f, angle + 90f * e);
                rect.localScale = Vector3.one * Mathf.Lerp(0.2f, 1f, e);
                image.color = new Color(c.r, c.g, c.b, 1f - k);
            });
            if (rect != null) Destroy(rect.gameObject);
        }

        // ---- 藻:水环收拢 → 冲半程甩藻鞭 → 反方向再抽一鞭,两次溅水 + 橙环 ----

        private IEnumerator ZaoStrike(RectTransform from, RectTransform to, Element element, CardRarity rarity)
        {
            if (from == null || to == null) yield break;
            SummonViewOf(from)?.PlayAttack();
            if (rarity >= CardRarity.Orange)
            {
                Ring(from, WaterBlue, inward: true);
                Ring(from, Theme.RarityColor(rarity), inward: true);
                yield return Beat(0.2f);
            }
            yield return TieredDash(from, to, WeedGreen, rarity, slash: false, reach: 0.45f);
            if (from == null || to == null) yield break;
            yield return WeedLash(from, to, 1f);
            WaterSplash(to);
            PlayClip(_hitClip, 0.5f, 1.25f);
            if (rarity >= CardRarity.Orange)
            {
                yield return Beat(0.14f);
                if (from == null || to == null) yield break;
                yield return WeedLash(from, to, -1f);
                WaterSplash(to);
                RingAt(to.position, Theme.RarityColor(rarity));
            }
        }

        /// <summary>一道起伏的藻鞭从本体甩到目标(实心描边线,与藤同一套 VineStroke),抽完淡掉。</summary>
        private IEnumerator WeedLash(RectTransform from, RectTransform to, float side)
        {
            Vector2 a = Local(from.position), b = Local(to.position), d = b - a, n = new Vector2(-d.y, d.x).normalized;
            const int segs = 26;
            var pts = new Vector2[segs + 1];
            for (int i = 0; i <= segs; i++)
            {
                float t = i / (float)segs, amp = Mathf.Sin(t * Mathf.PI) * d.magnitude * 0.07f;
                pts[i] = a + d * t + n * side * Mathf.Sin(t * Mathf.PI * 3f) * amp;
            }
            var lash = new VineStroke(this, new Color(0.08f, 0.26f, 0.2f), WeedGreen, new Color(0.62f, 0.84f, 0.72f));
            yield return Tween(0.14f, k => lash.Grow(pts, k, i => Mathf.Lerp(9f, 5f, i / (float)segs), _ => true));
            lash.Grow(pts, 1f, i => Mathf.Lerp(9f, 5f, i / (float)segs), _ => true);
            StartCoroutine(LashFade(lash));
        }

        private IEnumerator LashFade(VineStroke lash)
        {
            yield return Beat(0.08f);
            yield return Tween(0.24f, k => lash.Fade(1f - k));
            lash.Destroy();
        }

        // ---- 四木(红):四木合围,生根成林 ----
        // 完整版:四个「木」绕身蓄势 → 冲刺(光环脉冲)→ 飞到目标四角合围四斩、交成米字 → 破土三道粗根 + 叶旋 + 红绿双环。
        // **每场第一次出手才放完整版**(召唤物每回合自动出手,每次 2 秒会拖节奏);之后只放冲刺 + 合围四斩。

        private IEnumerator SimuStrike(SummonState attacker, RectTransform from, RectTransform to, Element element, CardRarity rarity)
        {
            if (from == null || to == null) yield break;
            bool full = attacker != null && _simuFullShown.Add(attacker);
            var red = Theme.RarityColor(rarity);
            SummonViewOf(from)?.PlayAttack();
            var seeds = new List<RectTransform>();
            if (full)
            {
                RingAt(from.position, red);
                yield return OrbitCharge(from, 4, _ => GlyphBit("木", 34f, new Color(1f, 0.82f, 0.48f)), 0.75f, seeds, rarity);
            }
            yield return TieredDash(from, to, element == Element.Wood ? new Color(0.66f, 0.88f, 0.63f) : Theme.GlyphColor(element), rarity, slash: false);
            if (to == null) { foreach (var s in seeds) if (s != null) Destroy(s.gameObject); yield break; }
            Vector2 b = Local(to.position);
            float size = SizeOf(to);

            // 合围:四角各一道刀光同时向内斩 + 一横一竖两道白刀光,交成米字
            Vector2[] corners = { new(-1f, 1f), new(1f, 1f), new(1f, -1f), new(-1f, -1f) };
            if (seeds.Count == 4)
            {
                var starts = new Vector2[4];
                for (int i = 0; i < 4; i++) starts[i] = seeds[i] != null ? (Vector2)seeds[i].localPosition : b;
                yield return Tween(0.14f, k =>
                {
                    for (int i = 0; i < 4; i++)
                        if (seeds[i] != null) seeds[i].localPosition = Vector2.Lerp(starts[i], b + Vector2.Scale(corners[i], new Vector2(size * 0.66f, size * 0.52f)), 1f - (1f - k) * (1f - k));
                });
                foreach (var s in seeds) if (s != null) Destroy(s.gameObject);
            }
            float cut = Mathf.Clamp(size * 1.5f, 110f, 220f);
            for (int i = 0; i < 4; i++)
                SlashAtAngle(to.position, cut, i % 2 == 0 ? new Color(0.66f, 0.88f, 0.63f) : new Color(1f, 0.82f, 0.48f),
                    Mathf.Atan2(corners[i].y, corners[i].x) * Mathf.Rad2Deg + 90f, 1f);
            SlashAtAngle(to.position, cut * 1.07f, Color.white, 0f, 1f);
            SlashAtAngle(to.position, cut * 1.07f, Color.white, 90f, 1f);
            StartCoroutine(Shake(16f, Vector2.up));
            PlayClip(_thudClip, 0.9f, 0.8f);
            LeafBurst(to.position, new Color(0.49f, 0.77f, 0.5f), Mathf.RoundToInt(6 * SummonMore(rarity)));
            if (full) StartCoroutine(SimuRoots(to, red));
        }

        /// <summary>生根:目标脚下破土三道粗根往上缠 + 叶片绕目标卷一圈半再炸开 + 红绿双环。命中之后演,不阻塞时间线。</summary>
        private IEnumerator SimuRoots(RectTransform to, Color red)
        {
            yield return Beat(0.12f);
            if (to == null) yield break;
            Vector2 b = Local(to.position);
            float size = SizeOf(to), baseY = b.y - size * 0.55f;
            StartCoroutine(Shake(10f, Vector2.up));
            Shards(new Vector2(b.x, baseY), 10, new Color(0.47f, 0.37f, 0.24f, 0.55f), new Vector2(10f, 18f), 30f, 70f, -20f, 0f, 180f, circle: true);
            var roots = new List<VineStroke>();
            float[] offs = { -0.35f, 0f, 0.35f };
            foreach (float o in offs)
            {
                const int segs = 14;
                var pts = new Vector2[segs + 1];
                for (int i = 0; i <= segs; i++)
                {
                    float t = i / (float)segs;
                    pts[i] = new Vector2(b.x + o * size + Mathf.Sin(t * Mathf.PI * 2f + o * 5f) * size * 0.16f * (1f - t * 0.3f), baseY + t * size * 1.05f);
                }
                var root = o == 0f
                    ? new VineStroke(this, new Color(0.22f, 0.13f, 0.06f), new Color(0.42f, 0.27f, 0.14f), new Color(0.6f, 0.44f, 0.26f))
                    : new VineStroke(this, new Color(0.13f, 0.25f, 0.1f), new Color(0.31f, 0.48f, 0.2f), new Color(0.52f, 0.7f, 0.4f));
                roots.Add(root);
                StartCoroutine(GrowRoot(root, pts, o == 0f ? 12f : 9f));
            }
            yield return Beat(0.28f);
            if (to == null) yield break;
            // 叶片旋涡
            for (int i = 0; i < 12; i++)
            {
                var leaf = Bit("leaf", new Vector2(22f, 22f), new Color(0.43f, 0.7f, 0.42f), b, out var img);
                StartCoroutine(LeafSpiral(leaf, img, b, size, i / 12f * Mathf.PI * 2f));
            }
            RingAt(to.position, red);
            yield return Beat(0.12f);
            RingAt(to.position, new Color(0.49f, 0.77f, 0.5f));
            yield return Beat(0.5f);
            foreach (var r in roots) StartCoroutine(RootFade(r));
        }

        private IEnumerator GrowRoot(VineStroke root, Vector2[] pts, float width)
        {
            int segs = pts.Length - 1;
            yield return Tween(0.26f, k => root.Grow(pts, k, i => Mathf.Lerp(width, width * 0.5f, i / (float)segs), _ => true));
            root.Grow(pts, 1f, i => Mathf.Lerp(width, width * 0.5f, i / (float)segs), _ => true);
        }

        private IEnumerator RootFade(VineStroke root)
        {
            yield return Tween(0.3f, k => root.Fade(1f - k));
            root.Destroy();
        }

        private IEnumerator LeafSpiral(RectTransform leaf, Image image, Vector2 center, float size, float phase)
        {
            Color c = image.color;
            yield return Tween(0.7f, k =>
            {
                if (leaf == null) return;
                float ang = phase + k * Mathf.PI * 3f, r = size * (0.35f + k * k * 0.9f);
                leaf.localPosition = center + new Vector2(Mathf.Cos(ang) * r, Mathf.Sin(ang) * r * 0.7f + k * size * 0.2f);
                leaf.localRotation = Quaternion.Euler(0f, 0f, k * 540f);
                image.color = new Color(c.r, c.g, c.b, k < 0.85f ? 1f : (1f - k) / 0.15f);
            });
            if (leaf != null) Destroy(leaf.gameObject);
        }

        // ---- 防守型:嘲讽 → 挨打 → 盾反 ----

        /// <summary>防守动效的状态:带盾反的召唤物举起的盾(只嘲讽、无盾反的没有盾)。</summary>
        private sealed class Guard
        {
            public RectTransform Tank;
            public RectTransform Shield;
            public ShieldGraphic ShieldGraphic;
            public Color Tint;
        }

        /// <summary>这只召唤物带不带嘲讽(柘 / 荆;将来谁带了嘲讽也走这一套)。</summary>
        private static bool IsGuardian(SummonState s) => s != null && (s.Passive?.Taunt ?? false);

        /// <summary>这只召唤物带不带盾反(Thorns > 0):挨打时举盾,反弹时打回金色回弹波。</summary>
        private static bool HasShieldCounter(SummonState s) => s != null && (s.Passive?.Thorns ?? 0) > 0;

        /// <summary>① 嘲讽(Taunt):挑衅光环 + 飘「嘲讽」+ 敌人到召唤物之间闪一道锁定虚线;
        /// 盾反(Thorns):举盾挡在身前。两样各看各的,都带就两样都演。</summary>
        private IEnumerator GuardBegin(SummonState state, RectTransform tank, RectTransform attacker, Action<Guard> ready)
        {
            var guard = new Guard { Tank = tank, Tint = Theme.RarityColor(RarityOfSummon(state)) };
            if (tank == null || attacker == null || _shakeTarget == null) { ready(guard); yield break; }
            Vector2 a = Local(attacker.position), b = Local(tank.position), d = b - a;
            if (IsGuardian(state))
            {
                Ring(tank, TauntRed);
                Popup(Strings.T("juice.popup.taunt"), TauntRed, tank, small: true);
                for (int i = 1; i < 10; i += 2)   // 锁定虚线:一截截短线
                {
                    var dash = Bit(null, new Vector2(d.magnitude / 12f, 3f), TauntRed, a + d * (i / 10f), out var img);
                    dash.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
                    StartCoroutine(FadeAndKill(dash, img, 0.5f));
                }
            }
            if (HasShieldCounter(state))
            {
                // 一面立着的骑士盾(ShieldGraphic):挡在召唤物身前、偏向来敌那一侧;从下往上抬起、放大到位,
                // 一道高光斜扫过盾面。不跟着来敌方向转 —— 转歪了的盾一眼认不出是盾(一版就是那样)
                float w = SizeOf(tank);
                Vector2 dir = (-d).normalized;
                Vector2 at = b + dir * w * 0.32f;
                var go = new GameObject("Shield", typeof(RectTransform));
                go.transform.SetParent(_shakeTarget, false);
                guard.Shield = (RectTransform)go.transform;
                guard.Shield.sizeDelta = new Vector2(w * 1.05f, w * 1.2f);
                guard.Shield.localPosition = at;
                guard.ShieldGraphic = go.AddComponent<ShieldGraphic>();
                guard.ShieldGraphic.raycastTarget = false;
                var shield = guard.Shield;
                Vector2 rise = at + Vector2.down * w * 0.35f;
                PlayClip(_shieldClip, 0.6f, 0.9f);
                yield return Tween(0.16f, k =>
                {
                    if (shield == null) return;
                    float e = 1f - (1f - k) * (1f - k);
                    shield.localPosition = Vector2.Lerp(rise, at, e);
                    shield.localScale = Vector3.one * Mathf.Lerp(0.4f, 1f, e);
                });
                // 高光:一道白条斜扫过盾面
                var gleam = Bit(null, new Vector2(w * 0.16f, w * 1.3f), new Color(1f, 1f, 1f, 0.75f), at + Vector2.left * w * 0.45f, out var gleamImg);
                gleam.localRotation = Quaternion.Euler(0f, 0f, -20f);
                StartCoroutine(Sweep(gleam, gleamImg, at + Vector2.left * w * 0.45f, at + Vector2.right * w * 0.45f));
            }
            else
            {
                HitReact(tank, 0.4f);   // 只嘲讽、无盾反:整只一抖
                yield return Beat(0.14f);
            }
            ready(guard);
        }

        /// <summary>② 挨打:盾面迎击(白闪 + 火花两侧弹开)+ 稀有度色环。</summary>
        private void GuardHit(Guard guard)
        {
            if (guard?.Tank == null) return;
            if (guard.Shield != null)
            {
                StartCoroutine(ShieldFlash(guard));
                Vector2 at = guard.Shield.localPosition;
                Shards(at, 6, new Color(1f, 0.88f, 0.54f), new Vector2(4f, 7f), 140f, 220f, 120f, 60f, 120f, circle: true);
                Shards(at, 6, new Color(1f, 0.88f, 0.54f), new Vector2(4f, 7f), 140f, 220f, 120f, 240f, 300f, circle: true);
            }
            Ring(guard.Tank, guard.Tint);
        }

        /// <summary>盾面迎击:整面白闪、往后一仰再回正;停一拍后缩小淡出。</summary>
        private IEnumerator ShieldFlash(Guard guard)
        {
            var shield = guard.Shield;
            var graphic = guard.ShieldGraphic;
            yield return Tween(0.28f, k =>
            {
                if (shield == null) return;
                graphic.Flash = 1f - k;
                shield.localRotation = Quaternion.Euler(0f, 0f, 9f * Mathf.Sin(k * Mathf.PI));
                shield.localScale = Vector3.one * (1f + 0.08f * Mathf.Sin(k * Mathf.PI));
            });
            yield return Beat(0.35f);
            if (shield == null) yield break;
            yield return Tween(0.24f, k =>
            {
                if (shield == null) return;
                shield.localScale = Vector3.one * (1f - 0.25f * k);
                graphic.color = new Color(1f, 1f, 1f, 1f - k);
            });
            if (shield != null) Destroy(shield.gameObject);
        }

        /// <summary>③ 盾反:一道金色回弹波从挨打的召唤物沿原路打回攻击者。命中那一刻返回。</summary>
        private IEnumerator ThornsFly(RectTransform tank, RectTransform attacker)
        {
            if (tank == null || attacker == null || _shakeTarget == null) yield break;
            Vector2 a = Local(tank.position), b = Local(attacker.position), d = b - a;
            float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            float size = Mathf.Clamp(SizeOf(tank) * 1.1f, 80f, 150f);
            var wave = Bit("slash", new Vector2(size, size), ShieldGold, a, out _);
            wave.localRotation = Quaternion.Euler(0f, 0f, ang + 90f);
            yield return Tween(0.22f, k => { if (wave != null) { wave.localPosition = Vector2.Lerp(a, b, k * k); wave.localScale = new Vector3(Mathf.Lerp(0.6f, 1.2f, k), 0.85f, 1f); } });
            if (wave != null) Destroy(wave.gameObject);
            Ring(attacker, ShieldGold);
            PlayClip(_hitClip, 0.6f, 1.3f);
        }
    }
}
