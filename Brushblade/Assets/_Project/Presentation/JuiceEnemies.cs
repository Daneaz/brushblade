using System;
using System.Collections;
using System.Collections.Generic;
using Brushblade.Core;
using Brushblade.Data;
using UnityEngine;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>敌方攻击分层(2026-09-30,「敌方攻击分层」「小妖出手家族」两份 demo 拍板)。
    ///
    /// 一、**小妖家族**:32 只小妖按名字字意归 15 个家族,每家族一种落点 / 出手(批改红叉、断笔、
    ///     标点砸、重影叠撞、跃起砸地、迷雾、墨斑、焦印、灯花、钩针、平压、盖印、飞白、泼洒、涂改)。
    ///     近战仍冲脸、远程仍放五行弹道,换的是「打在身上是什么」;少数家族换掉整个出手动作。
    /// 二、**首领**:普攻加前摇(墨气收拢、暗红内收环)、冲刺墨色残影、命中顿帧 + 推镜 + 墨溅;
    ///     蓄力回合有预警(暗红内收环 ×3、屏幕泛红、飘「蓄力 · 技能名」);放大招先压暗、用墨红写出
    ///     这一阶的字,再按技能演(淹没 / 洞穿 / 倾覆 / 吞噬);坚壁是被动,演在我方打它时。
    ///     大招后面跟着的各目标受击事件**不再冲脸**(<see cref="_skillCaster"/>),只留命中反馈。</summary>
    public sealed partial class Juice
    {
        /// <summary>第 i 只敌人(BattleView 注入)。拿不到时退回无家族、非首领的老样子。</summary>
        public Func<int, EnemyState> EnemyAt { get; set; }

        private enum MinionFamily
        {
            None, Cross, Broken, Punct, Double, Leap, Fog, Blot, Scorch, Wick, Hook, Press, Seal, DryBrush, Splash, White,
        }

        /// <summary>小妖 → 家族。新加小妖在这里挑一个挂上;没登记的走老样子(冲脸 + 刀口 / 五行弹道)。</summary>
        private static readonly Dictionary<string, MinionFamily> Families = new()
        {
            { "错字鬼", MinionFamily.Cross },
            { "缺笔妖", MinionFamily.Broken }, { "衍文", MinionFamily.Broken },
            { "标点小妖", MinionFamily.Punct },
            { "叠字怪", MinionFamily.Double }, { "拓片", MinionFamily.Double }, { "通假字", MinionFamily.Double },
            { "夯土妖", MinionFamily.Leap }, { "砚台", MinionFamily.Leap },
            { "生僻字", MinionFamily.Fog }, { "铭文", MinionFamily.Fog },
            { "墨渍", MinionFamily.Blot }, { "宿墨", MinionFamily.Blot }, { "洇痕", MinionFamily.Blot }, { "晕染", MinionFamily.Blot },
            { "焦痕", MinionFamily.Scorch }, { "炭笔", MinionFamily.Scorch },
            { "灯花", MinionFamily.Wick }, { "窑变", MinionFamily.Wick },
            { "铁画", MinionFamily.Hook }, { "悬针", MinionFamily.Hook }, { "刻刀", MinionFamily.Hook },
            { "镇纸", MinionFamily.Press }, { "版牍", MinionFamily.Press },
            { "火漆", MinionFamily.Seal }, { "印泥", MinionFamily.Seal }, { "铜钤", MinionFamily.Seal },
            { "枯笔", MinionFamily.DryBrush }, { "败笔", MinionFamily.DryBrush },
            { "泼墨", MinionFamily.Splash }, { "墨溅", MinionFamily.Splash },
            { "涂改", MinionFamily.White },
        };

        private static readonly Color InkBlack = new(0.1f, 0.11f, 0.14f);
        private static readonly Color BloodRed = new(0.7f, 0.14f, 0.16f);
        private static readonly Color MarkRed = new(0.76f, 0.23f, 0.17f);
        private static readonly Color SealRed = new(0.7f, 0.14f, 0.16f);
        private static readonly Color FogGrey = new(0.55f, 0.55f, 0.59f, 0.75f);

        /// <summary>刚放完大招的那只首领:同一批里紧随其后的受击事件不再冲脸(大招本身已经演过「打过去」)。</summary>
        private int _skillCaster = -1;

        private static MinionFamily FamilyOf(EnemyState s) =>
            s != null && !s.IsBoss && Families.TryGetValue(s.Def.Id, out var f) ? f : MinionFamily.None;

        // ---- 分派 ----

        private IEnumerator EnemySwingTiered(RectTransform attacker, RectTransform target, EnemyState state,
            Element? element, bool ranged, bool slash)
        {
            if (attacker == null) yield break;
            var color = Theme.GlyphColor(element);
            Vector3 point = AnchorPoint(target);
            float size = target != null ? SizeOf(target) : PlayerTargetSize;

            if (state != null && state.IsBoss)
            {
                yield return BossWindup(attacker);
                if (ranged) yield return EnemyBolt(attacker, point, element, color);
                else yield return BossStrike(attacker, target, point, slash);
                yield break;
            }

            var family = FamilyOf(state);
            switch (family)
            {
                case MinionFamily.Leap: yield return LeapStrike(attacker, point, size); yield break;
                case MinionFamily.Press: yield return PressStrike(attacker, target, point, size); yield break;
                case MinionFamily.Double: yield return DoubleStrike(attacker, target, point, color, slash); yield break;
                case MinionFamily.Fog: yield return FogStrike(attacker, point, size, slash); yield break;
                case MinionFamily.Punct: yield return PunctDrop(attacker, point, size); yield break;
                case MinionFamily.Splash: yield return SplashThrow(attacker, point, size); yield break;
                case MinionFamily.White: yield return WhiteBlob(attacker, point, size, slash); yield break;
            }
            if (ranged) yield return EnemyBolt(attacker, point, element, color);
            else yield return BodyStrike(attacker, target, point, color, slash && family == MinionFamily.None);
            if (slash && family != MinionFamily.None) FamilyImpact(family, point, size);
        }

        // ---- 落点家族(打在身上是什么)----

        private void FamilyImpact(MinionFamily family, Vector3 point, float size)
        {
            Vector2 at = Local(point);
            float r = Mathf.Clamp(size * 0.5f, 40f, 90f);
            switch (family)
            {
                case MinionFamily.Cross:   // 批改红叉:两笔红线交叉
                    StartCoroutine(Stroke(at + new Vector2(-r, r), at + new Vector2(r, -r), 9f, MarkRed, 0.11f, 0f, 0.5f));
                    StartCoroutine(Stroke(at + new Vector2(r, r), at + new Vector2(-r, -r), 9f, MarkRed, 0.11f, 0.09f, 0.5f));
                    break;
                case MinionFamily.Broken:  // 断笔:一笔中间缺一截,缺的那段掉下来
                {
                    Vector2 p0 = at + new Vector2(-r * 1.1f, -r * 0.5f), p1 = at + new Vector2(r * 1.1f, r * 0.5f);
                    Vector2 g0 = Vector2.Lerp(p0, p1, 0.36f), g1 = Vector2.Lerp(p0, p1, 0.64f);
                    StartCoroutine(Stroke(p0, g0, 10f, InkBlack, 0.08f, 0f, 0.4f));
                    StartCoroutine(Stroke(g1, p1, 10f, InkBlack, 0.08f, 0.06f, 0.4f));
                    var piece = Bit(null, new Vector2((g1 - g0).magnitude, 10f), new Color(0.56f, 0.53f, 0.46f), (g0 + g1) * 0.5f, out var img);
                    piece.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2((g1 - g0).y, (g1 - g0).x) * Mathf.Rad2Deg);
                    StartCoroutine(Fall(piece, img, size * 0.9f, 70f));
                    break;
                }
                case MinionFamily.Blot:    // 墨斑:一团墨晕开,往下滴
                {
                    var blot = Bit(null, new Vector2(r * 1.8f, r * 1.8f), new Color(0.08f, 0.1f, 0.14f, 0.85f), at, out var img, circle: true);
                    StartCoroutine(Bloom(blot, img, 0.2f, 1.15f, 0.9f));
                    for (int i = 0; i < 4; i++)
                    {
                        var drip = Bit(null, new Vector2(6f, 9f), InkBlack, at + new Vector2(UnityEngine.Random.Range(-r * 0.6f, r * 0.6f), -r * 0.3f), out var dImg, circle: true);
                        StartCoroutine(Fall(drip, dImg, r * 1.2f, 0f, 0.2f + i * 0.09f));
                    }
                    break;
                }
                case MinionFamily.Scorch:  // 焦印:焦黑印记 + 冒烟 + 火星
                {
                    var mark = Bit(null, new Vector2(r * 1.4f, r * 1.1f), new Color(0.15f, 0.08f, 0.05f, 0.85f), at, out var img, circle: true);
                    StartCoroutine(Bloom(mark, img, 0.3f, 1f, 1.1f));
                    for (int i = 0; i < 5; i++)
                    {
                        var smoke = Bit(null, new Vector2(r * 0.7f, r * 0.7f), new Color(0.35f, 0.35f, 0.37f, 0.5f),
                            at + new Vector2(UnityEngine.Random.Range(-r * 0.5f, r * 0.5f), 0f), out var sImg, circle: true);
                        StartCoroutine(RiseAndFade(smoke, sImg, r * 1.4f, 0.8f));
                    }
                    Shards(at, 6, FireCore, new Vector2(3f, 5f), 40f, 90f, -120f, 30f, 150f, circle: true);
                    break;
                }
                case MinionFamily.Wick:    // 灯花爆裂:一闪白光 + 一圈放射火丝
                {
                    var flash = Bit(null, new Vector2(r * 1.6f, r * 1.6f), new Color(1f, 0.97f, 0.85f), at, out var img, circle: true);
                    StartCoroutine(Bloom(flash, img, 0.3f, 1.3f, 0.26f));
                    for (int i = 0; i < 12; i++)
                    {
                        float ang = i * 30f + UnityEngine.Random.Range(-8f, 8f);
                        Vector2 dir = Quaternion.Euler(0f, 0f, ang) * Vector2.right;
                        StartCoroutine(Stroke(at, at + dir * r * UnityEngine.Random.Range(0.9f, 1.4f), 2.5f, new Color(1f, 0.84f, 0.5f), 0.12f, 0f, 0.25f));
                    }
                    break;
                }
                case MinionFamily.Hook:    // 钩针:一笔铁画银钩(竖 + 钩)
                {
                    const int segs = 16;
                    var pts = new Vector2[segs + 1];
                    for (int i = 0; i <= segs; i++)
                    {
                        float t = i / (float)segs;
                        pts[i] = t < 0.7f
                            ? at + new Vector2(-r * 0.2f, r - t / 0.7f * r * 1.55f)
                            : at + new Vector2(-r * 0.2f - (t - 0.7f) / 0.3f * r * 0.6f, -r * 0.55f + Mathf.Sin((t - 0.7f) / 0.3f * Mathf.PI * 0.5f) * r * 0.35f);
                    }
                    var hook = new VineStroke(this, new Color(0.15f, 0.15f, 0.16f), new Color(0.36f, 0.36f, 0.38f), new Color(0.91f, 0.89f, 0.81f));
                    StartCoroutine(HookStroke(hook, pts));
                    break;
                }
                case MinionFamily.Seal:    // 盖印:一枚朱红方印盖下,留印再淡掉
                {
                    float s = r * 1.25f;
                    var seal = Bit(null, new Vector2(s, s), SealRed, at, out var img);
                    img.sprite = Theme.Rounded(Mathf.RoundToInt(s * 0.1f));
                    var rim = Bit(null, new Vector2(s * 0.78f, s * 0.78f), new Color(0.96f, 0.95f, 0.91f), at, out var rimImg);
                    rimImg.sprite = Theme.Rounded(Mathf.RoundToInt(s * 0.08f));
                    rimImg.fillCenter = false;
                    seal.localRotation = rim.localRotation = Quaternion.Euler(0f, 0f, -6f);
                    StartCoroutine(StampIn(seal, img, 0.7f));
                    StartCoroutine(StampIn(rim, rimImg, 0.7f));
                    break;
                }
                case MinionFamily.DryBrush: // 飞白扫:几缕断续的干笔毛丝斜扫过去
                    for (int i = 0; i < 4; i++)
                    {
                        Vector2 off = new Vector2(0f, (i - 1.5f) * 7f);
                        StartCoroutine(Stroke(at + off + new Vector2(-r * 1.2f, r * 0.5f), at + off + new Vector2(r * 1.2f, -r * 0.5f),
                            i % 2 == 0 ? 4f : 2.5f, new Color(0.1f, 0.11f, 0.14f, i % 2 == 0 ? 0.85f : 0.55f), 0.13f, i * 0.015f, 0.4f));
                    }
                    break;
            }
        }

        /// <summary>一笔粗线从 a 写到 b(按长度伸出),停 hold 秒再淡掉。</summary>
        private IEnumerator Stroke(Vector2 a, Vector2 b, float width, Color color, float duration, float delay, float hold)
        {
            if (delay > 0f) yield return Beat(delay);
            Vector2 d = b - a;
            var bar = Bit(null, new Vector2(1f, width), color, a, out var img);
            img.sprite = Theme.Rounded(Mathf.Max(1, Mathf.RoundToInt(width * 0.5f)));
            bar.pivot = new Vector2(0f, 0.5f);
            bar.localPosition = a;
            bar.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            yield return Tween(duration, k => { if (bar != null) bar.sizeDelta = new Vector2(Mathf.Max(1f, d.magnitude * (1f - (1f - k) * (1f - k))), width); });
            yield return Beat(hold);
            if (bar != null) yield return FadeAndKill(bar, img, 0.22f);
        }

        private IEnumerator HookStroke(VineStroke hook, Vector2[] pts)
        {
            int segs = pts.Length - 1;
            yield return Tween(0.18f, k => hook.Grow(pts, k, i => Mathf.Lerp(10f, 5f, i / (float)segs), _ => true));
            hook.Grow(pts, 1f, i => Mathf.Lerp(10f, 5f, i / (float)segs), _ => true);
            yield return Beat(0.18f);
            yield return Tween(0.24f, k => hook.Fade(1f - k));
            hook.Destroy();
        }

        /// <summary>从 scaleFrom 胀到 scaleTo,同时淡出(墨斑晕开 / 焦印 / 闪光)。</summary>
        private IEnumerator Bloom(RectTransform rect, Image image, float scaleFrom, float scaleTo, float duration)
        {
            Color c = image.color;
            yield return Tween(duration, k =>
            {
                if (rect == null) return;
                float e = 1f - (1f - k) * (1f - k);
                rect.localScale = Vector3.one * Mathf.Lerp(scaleFrom, scaleTo, e);
                image.color = new Color(c.r, c.g, c.b, c.a * (k < 0.3f ? 1f : 1f - (k - 0.3f) / 0.7f));
            });
            if (rect != null) Destroy(rect.gameObject);
        }

        private IEnumerator Fall(RectTransform rect, Image image, float drop, float spin, float delay = 0f)
        {
            if (delay > 0f) yield return Beat(delay);
            if (rect == null) yield break;
            Vector2 start = rect.localPosition;
            float rot = rect.localEulerAngles.z;
            Color c = image.color;
            yield return Tween(0.6f, k =>
            {
                if (rect == null) return;
                rect.localPosition = start + new Vector2(8f * k, -drop * k * k);
                rect.localRotation = Quaternion.Euler(0f, 0f, rot + spin * k);
                image.color = new Color(c.r, c.g, c.b, c.a * (1f - k));
            });
            if (rect != null) Destroy(rect.gameObject);
        }

        private IEnumerator StampIn(RectTransform rect, Image image, float hold)
        {
            yield return Tween(0.16f, k => { if (rect != null) rect.localScale = Vector3.one * Mathf.Lerp(2.2f, 1f, k * k); });
            StartCoroutine(Shake(6f, Vector2.down));
            yield return Beat(hold);
            if (rect != null) yield return FadeAndKill(rect, image, 0.25f, 0.05f);
        }

        // ---- 换掉整个出手动作的家族 ----

        /// <summary>跃起砸地:沿抛物线跳到目标头上砸下,震地冲击环 + 扬尘。</summary>
        private IEnumerator LeapStrike(RectTransform attacker, Vector3 point, float size)
        {
            var space = attacker.parent as RectTransform;
            Vector2 home = HomeOf(attacker);
            int token = NextMoveToken(attacker);
            Vector2 delta = space != null ? (Vector2)space.InverseTransformVector(point - attacker.position) : Vector2.zero;
            delta -= delta.normalized * size * 0.35f;
            float lift = Mathf.Max(120f, delta.magnitude * 0.45f);
            var lifted = LiftAbove(attacker);
            yield return Tween(0.4f, k =>
            {
                if (attacker == null) return;
                attacker.anchoredPosition = home + delta * k + Vector2.up * (Mathf.Sin(k * Mathf.PI) * lift);
                attacker.localScale = Vector3.one * (1f + 0.15f * Mathf.Sin(k * Mathf.PI));
            });
            if (attacker != null) attacker.localScale = Vector3.one;
            RingAt(point, new Color(0.51f, 0.33f, 0.16f));
            Shards(Local(point) + new Vector2(0f, -size * 0.35f), 10, new Color(0.6f, 0.52f, 0.42f, 0.55f), new Vector2(12f, 22f), 30f, 80f, -20f, 0f, 180f, circle: true);
            StartCoroutine(Shake(16f, Vector2.down));
            PlayClip(_thudClip, 0.9f, 0.75f);
            StartCoroutine(BodyReturn(attacker, home, token, lifted));
        }

        /// <summary>平压:升到目标头顶变扁,整块压下,目标被压扁,尘土往两边挤出。</summary>
        private IEnumerator PressStrike(RectTransform attacker, RectTransform target, Vector3 point, float size)
        {
            var space = attacker.parent as RectTransform;
            Vector2 home = HomeOf(attacker);
            int token = NextMoveToken(attacker);
            Vector2 above = space != null ? (Vector2)space.InverseTransformVector(point - attacker.position) + Vector2.up * size * 1.1f : Vector2.zero;
            var lifted = LiftAbove(attacker);
            yield return Tween(0.28f, k =>
            {
                if (attacker == null) return;
                float e = 1f - (1f - k) * (1f - k);
                attacker.anchoredPosition = home + above * e;
                attacker.localScale = new Vector3(Mathf.Lerp(1f, 1.25f, e), Mathf.Lerp(1f, 0.75f, e), 1f);
            });
            yield return Tween(0.1f, k =>
            {
                if (attacker == null) return;
                attacker.anchoredPosition = home + above + Vector2.down * size * 0.45f * k * k;
                attacker.localScale = new Vector3(Mathf.Lerp(1.25f, 1.35f, k), Mathf.Lerp(0.75f, 0.6f, k), 1f);
            });
            if (target != null) StartCoroutine(Squash(target));
            Vector2 at = Local(point) + new Vector2(0f, -size * 0.35f);
            Shards(at, 5, new Color(0.6f, 0.52f, 0.42f, 0.55f), new Vector2(10f, 18f), 60f, 120f, 0f, 160f, 200f, circle: true);
            Shards(at, 5, new Color(0.6f, 0.52f, 0.42f, 0.55f), new Vector2(10f, 18f), 60f, 120f, 0f, -20f, 20f, circle: true);
            StartCoroutine(Shake(12f, Vector2.down));
            PlayClip(_thudClip, 0.85f, 0.7f);
            StartCoroutine(UnsquashAndReturn(attacker, home, token, lifted));
        }

        private IEnumerator Squash(RectTransform target)
        {
            yield return Tween(0.36f, k =>
            {
                if (target == null) return;
                float s = k < 0.25f ? k / 0.25f : 1f - (k - 0.25f) / 0.75f;
                target.localScale = new Vector3(1f + 0.25f * s, 1f - 0.4f * s, 1f);
            });
            if (target != null) target.localScale = Vector3.one;
        }

        private IEnumerator UnsquashAndReturn(RectTransform attacker, Vector2 home, int token, Canvas lifted)
        {
            yield return Tween(0.2f, k => { if (attacker != null) attacker.localScale = Vector3.Lerp(new Vector3(1.35f, 0.6f, 1f), Vector3.one, k); });
            yield return BodyReturn(attacker, home, token, lifted);
        }

        /// <summary>重影叠撞:一道半透明重影先撞上,本体随后再撞一下。</summary>
        private IEnumerator DoubleStrike(RectTransform attacker, RectTransform target, Vector3 point, Color color, bool slash)
        {
            var ghost = Instantiate(attacker.gameObject, _shakeTarget, true);
            ghost.name = "EchoGhost";
            foreach (var g in ghost.GetComponentsInChildren<Graphic>(true)) g.raycastTarget = false;
            var layout = ghost.GetComponent<LayoutElement>() ?? ghost.AddComponent<LayoutElement>();
            layout.ignoreLayout = true;
            var group = ghost.GetComponent<CanvasGroup>() ?? ghost.AddComponent<CanvasGroup>();
            group.alpha = 0.45f;
            group.blocksRaycasts = false;
            var gr = (RectTransform)ghost.transform;
            Vector3 start = attacker.position, stop = Vector3.Lerp(start, point, 0.8f);
            yield return Tween(0.13f, k => { if (gr != null) gr.position = Vector3.Lerp(start, stop, k * k); });
            if (slash && target != null) SlashAt(point, SizeOf(target) * 0.8f, color);
            StartCoroutine(FadeGroup(ghost, group, 0.22f));
            yield return BodyStrike(attacker, target, point, color, slash);
        }

        /// <summary>迷雾:先吐几团雾罩住目标,再从雾里一记斩出(本体不动)。</summary>
        private IEnumerator FogStrike(RectTransform attacker, Vector3 point, float size, bool slash)
        {
            Vector2 from = Local(attacker.position), to = Local(point);
            for (int i = 0; i < 7; i++)
            {
                var puff = Bit(null, new Vector2(size * 0.55f, size * 0.55f), FogGrey, from, out var img, circle: true);
                StartCoroutine(FogPuff(puff, img, from, to + UnityEngine.Random.insideUnitCircle * size * 0.35f, i * 0.03f));
            }
            PlayClip(_shieldClip, 0.4f, 0.7f);
            yield return Beat(0.42f);
            if (slash) SlashAt(point, size, new Color(0.36f, 0.39f, 0.44f));
            PlayClip(_hitClip, 0.55f, 0.95f);
        }

        private IEnumerator FogPuff(RectTransform puff, Image image, Vector2 from, Vector2 to, float delay)
        {
            if (delay > 0f) yield return Beat(delay);
            Color c = image.color;
            yield return Tween(1.0f, k =>
            {
                if (puff == null) return;
                float move = Mathf.Clamp01(k / 0.45f);
                puff.localPosition = Vector2.Lerp(from, to, 1f - (1f - move) * (1f - move));
                puff.localScale = Vector3.one * Mathf.Lerp(0.3f, 1.4f, k);
                image.color = new Color(c.r, c.g, c.b, c.a * (k < 0.75f ? 1f : (1f - k) / 0.25f));
            });
            if (puff != null) Destroy(puff.gameObject);
        }

        /// <summary>标点砸(远程):原地一蹦,一个大「!」从天砸下,碎成几个逗号句号蹦开。</summary>
        private IEnumerator PunctDrop(RectTransform attacker, Vector3 point, float size)
        {
            Vector2 home = HomeOf(attacker);
            int token = NextMoveToken(attacker);
            yield return Tween(0.22f, k => { if (attacker != null) attacker.anchoredPosition = home + Vector2.up * (Mathf.Sin(k * Mathf.PI) * 24f); });
            StartCoroutine(ReturnHome(attacker, home, 0.05f, token));
            Vector2 to = Local(point);
            var bang = GlyphBit(Strings.T("juice.fx.punct_big"), size * 0.9f, InkBlack);
            bang.GetComponent<Outline>().effectColor = new Color(1f, 1f, 1f, 0.6f);
            Vector2 top = to + Vector2.up * _shakeTarget.rect.height * 0.6f;
            yield return Tween(0.2f, k => { if (bang != null) bang.localPosition = Vector2.Lerp(top, to, k * k); });
            StartCoroutine(FadeGlyph(bang, 0.24f));
            string bits = Strings.T("juice.fx.punct_bits");
            for (int i = 0; i < 6; i++)
            {
                var bit = GlyphBit(bits.Substring(i % bits.Length, 1), 22f, InkBlack);
                bit.localPosition = to;
                float ang = UnityEngine.Random.Range(20f, 160f) * Mathf.Deg2Rad;
                StartCoroutine(GlyphFly(bit, to, new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * UnityEngine.Random.Range(120f, 200f)));
            }
            StartCoroutine(Shake(10f, Vector2.down));
            PlayClip(_thudClip, 0.7f, 1.1f);
        }

        private IEnumerator FadeGlyph(RectTransform rect, float duration)
        {
            var text = rect != null ? rect.GetComponent<Text>() : null;
            if (text == null) yield break;
            Color c = text.color;
            yield return Tween(duration, k =>
            {
                if (rect == null) return;
                rect.localScale = new Vector3(1f, 1f - 0.5f * k, 1f);
                text.color = new Color(c.r, c.g, c.b, 1f - k);
            });
            if (rect != null) Destroy(rect.gameObject);
        }

        private IEnumerator GlyphFly(RectTransform rect, Vector2 at, Vector2 v)
        {
            var text = rect.GetComponent<Text>();
            Color c = text.color;
            float t = 0f;
            while (t < 0.55f && rect != null)
            {
                t += Dt;
                rect.localPosition = at + v * t + new Vector2(0f, -0.5f * 380f * t * t);
                rect.localRotation = Quaternion.Euler(0f, 0f, 300f * t);
                text.color = new Color(c.r, c.g, c.b, 1f - t / 0.55f);
                yield return null;
            }
            if (rect != null) Destroy(rect.gameObject);
        }

        /// <summary>泼洒:身子一甩,扇形泼出一片墨点,落在目标周围溅开。</summary>
        private IEnumerator SplashThrow(RectTransform attacker, Vector3 point, float size)
        {
            Vector2 from = Local(attacker.position), to = Local(point);
            HitReact(attacker, 0.3f);
            PlayClip(_hitClip, 0.5f, 0.8f);
            for (int i = 0; i < 12; i++)
            {
                Vector2 land = to + new Vector2(UnityEngine.Random.Range(-size * 0.7f, size * 0.7f), UnityEngine.Random.Range(-size * 0.45f, size * 0.45f));
                float s = UnityEngine.Random.Range(7f, 14f);
                var drop = Bit(null, new Vector2(s, s), InkBlack, from, out var img, circle: true);
                StartCoroutine(InkArc(drop, img, from, land, i * 0.014f));
            }
            yield return Beat(0.3f);
        }

        private IEnumerator InkArc(RectTransform drop, Image image, Vector2 from, Vector2 to, float delay)
        {
            if (delay > 0f) yield return Beat(delay);
            yield return Tween(0.28f, k => { if (drop != null) drop.localPosition = Vector2.Lerp(from, to, k) + Vector2.up * (Mathf.Sin(k * Mathf.PI) * 40f); });
            if (drop == null) yield break;
            Color c = image.color;
            yield return Tween(0.34f, k =>
            {
                if (drop == null) return;
                drop.localScale = new Vector3(Mathf.Lerp(1f, 1.8f, k), Mathf.Lerp(1f, 0.7f, k), 1f);
                image.color = new Color(c.r, c.g, c.b, 1f - k);
            });
            if (drop != null) Destroy(drop.gameObject);
        }

        /// <summary>涂改(远程):射一团修正液,在目标身上抹出一道白痕。</summary>
        private IEnumerator WhiteBlob(RectTransform attacker, Vector3 point, float size, bool slash)
        {
            yield return Shoot(attacker, point, Color.white, null, new Vector2(24f, 24f));
            if (!slash) yield break;
            Vector2 at = Local(point);
            StartCoroutine(Stroke(at + new Vector2(-size * 0.55f, size * 0.1f), at + new Vector2(size * 0.55f, -size * 0.15f),
                size * 0.26f, new Color(1f, 1f, 1f, 0.95f), 0.17f, 0f, 0.45f));
        }

        // ---- 首领 ----

        /// <summary>首领前摇:墨气从四周往身上收,暗红内收环。</summary>
        private IEnumerator BossWindup(RectTransform attacker)
        {
            Vector2 c = Local(attacker.position);
            float r = SizeOf(attacker) * 0.75f;
            Ring(attacker, BloodRed, inward: true);
            for (int i = 0; i < 10; i++)
            {
                float a = i / 10f * Mathf.PI * 2f;
                Vector2 start = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                var dot = Bit(null, new Vector2(10f, 10f), InkBlack, start, out var img, circle: true);
                StartCoroutine(Converge(dot, img, start, c, i * 0.01f));
            }
            PlayClip(_thudClip, 0.5f, 0.6f);
            yield return Beat(0.28f);
        }

        /// <summary>首领近战:冲刺带墨色残影;命中墨色刀口 + 墨溅 + 暗红环 + 顿帧推镜。</summary>
        private IEnumerator BossStrike(RectTransform attacker, RectTransform target, Vector3 point, bool slash)
        {
            bool g1 = false, g2 = false;
            _dashHook = (k, world) =>
            {
                if (!g1 && k >= 0.45f) { g1 = true; Afterimage(attacker, InkBlack); }
                if (!g2 && k >= 0.75f) { g2 = true; Afterimage(attacker, InkBlack); }
            };
            yield return BodyStrike(attacker, target, point, InkBlack, false);
            _dashHook = null;
            if (!slash) yield break;
            float size = target != null ? SizeOf(target) : PlayerTargetSize;
            SlashAtAngle(point, Mathf.Clamp(size * 1.6f, 110f, 220f), new Color(0.16f, 0.08f, 0.09f), UnityEngine.Random.Range(-25f, 25f), 1f);
            SlashAtAngle(point, Mathf.Clamp(size * 1.3f, 90f, 180f), new Color(0.91f, 0.89f, 0.81f), UnityEngine.Random.Range(-25f, 25f), -1f);
            Shards(Local(point), 12, InkBlack, new Vector2(6f, 13f), 120f, 240f, 220f, circle: true);
            RingAt(point, BloodRed);
            FreezeFrame(0.12f);
            StartCoroutine(Shake(14f, Vector2.down));
        }

        /// <summary>蓄力预警:暗红内收环 ×3、屏幕泛红一闪、飘「蓄力 · 技能名」。</summary>
        private IEnumerator BossChargeTelegraph(RectTransform boss, BossSkill skill)
        {
            if (boss == null) yield break;
            ScreenFlash(0.18f, BloodRed);
            Popup(Strings.T("battle.label.charging_next_turn", ("skillName", SkillName(skill))), BloodRed, boss, small: true);
            for (int i = 0; i < 3; i++)
            {
                if (boss == null) yield break;
                Ring(boss, BloodRed, inward: true);
                yield return Beat(0.24f);
            }
        }

        /// <summary>技能名(字面量逐个写出 —— 字符串表孤儿检查只认 Strings.T( 后面的字面量)。</summary>
        private static string SkillName(BossSkill skill) => skill switch
        {
            BossSkill.Deluge => Strings.T("enemy.skill.deluge.name"),
            BossSkill.Impale => Strings.T("enemy.skill.impale.name"),
            BossSkill.Topple => Strings.T("enemy.skill.topple.name"),
            BossSkill.Devour => Strings.T("enemy.skill.devour.name"),
            BossSkill.Bulwark => Strings.T("enemy.skill.bulwark.name"),
            _ => "",
        };

        /// <summary>大招:压暗写出这一阶的字(墨红),再按技能演「打过去」。frontSummon = 吞噬 / 洞穿要打的最前一只召唤物。</summary>
        private IEnumerator BossSkillCast(RectTransform boss, EnemyState state, BossSkill skill, RectTransform frontSummon)
        {
            if (boss == null || _shakeTarget == null) yield break;
            string glyph = state != null && state.Def.Phases.Count > 0 ? state.Def.Phases[Mathf.Clamp(state.PhaseIndex, 0, state.Def.Phases.Count - 1)].Char : "";
            if (!string.IsNullOrEmpty(glyph)) yield return InkGlyph(glyph);
            Vector2 from = Local(boss.position);
            Vector3 playerPoint = AnchorPoint(null);
            switch (skill)
            {
                case BossSkill.Deluge: yield return DelugeWave(); break;
                case BossSkill.Impale: yield return ImpaleSpear(from, Local(playerPoint)); break;
                case BossSkill.Topple: yield return ToppleTilt(Local(playerPoint)); break;
                case BossSkill.Devour: if (frontSummon != null) yield return DevourJaws(frontSummon); break;
            }
        }

        /// <summary>敌方的「写大字」:压暗,用墨红从上往下写出这一阶的字,墨点四溅,停一拍放大淡出。</summary>
        private IEnumerator InkGlyph(string glyph)
        {
            var area = _shakeTarget.rect;
            var dim = Bit(null, area.size, new Color(0.04f, 0.05f, 0.08f, 0f), area.center, out var dimImage);
            float size = area.height * 0.6f;
            var maskGo = new GameObject("InkGlyphMask", typeof(RectTransform), typeof(RectMask2D));
            maskGo.transform.SetParent(_shakeTarget, false);
            var mask = (RectTransform)maskGo.transform;
            mask.pivot = new Vector2(0.5f, 1f);
            mask.sizeDelta = new Vector2(size, 0f);
            mask.localPosition = new Vector2(area.center.x, area.center.y + size * 0.5f + area.height * 0.04f);
            var label = new GameObject("Glyph", typeof(RectTransform)).AddComponent<Text>();
            label.transform.SetParent(mask, false);
            var lr = label.rectTransform;
            lr.anchorMin = lr.anchorMax = new Vector2(0.5f, 1f);
            lr.pivot = new Vector2(0.5f, 1f);
            lr.sizeDelta = new Vector2(size, size);
            lr.anchoredPosition = Vector2.zero;
            label.font = Theme.TitleFont;
            label.fontSize = Mathf.RoundToInt(size * 0.86f);
            label.fontStyle = FontStyle.Bold;
            label.text = glyph;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = new Color(0.1f, 0.05f, 0.05f);
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            var outline = label.gameObject.AddComponent<Outline>();
            outline.effectColor = BloodRed;
            outline.effectDistance = new Vector2(3f, -3f);
            PlayClip(_thudClip, 0.7f, 0.5f);
            yield return Tween(0.14f, k => { if (dimImage != null) dimImage.color = new Color(0.04f, 0.05f, 0.08f, 0.55f * k); });
            yield return Tween(0.34f, k => { if (mask != null) mask.sizeDelta = new Vector2(size, size * (1f - (1f - k) * (1f - k))); });
            Shards(area.center, 12, new Color(0.23f, 0.05f, 0.06f), new Vector2(5f, 12f), 160f, 300f, 0f, circle: true);
            StartCoroutine(Shake(8f, Vector2.down));
            yield return Beat(0.2f);
            yield return Tween(0.22f, k =>
            {
                if (mask != null) mask.localScale = Vector3.one * (1f + 0.3f * k * k);
                if (label != null) label.color = new Color(label.color.r, label.color.g, label.color.b, 1f - k);
                if (dimImage != null) dimImage.color = new Color(0.04f, 0.05f, 0.08f, 0.55f * (1f - k));
            });
            if (mask != null) Destroy(mask.gameObject);
            if (dim != null) Destroy(dim.gameObject);
        }

        /// <summary>淹没:一道墨浪从战场中线往下盖过我方全体。</summary>
        private IEnumerator DelugeWave()
        {
            var area = _shakeTarget.rect;
            const int n = 14;
            float w = area.width / n * 1.5f, h = area.height * 0.3f;
            var bands = new List<(RectTransform rect, Image image, float x, float phase)>();
            for (int i = 0; i < n; i++)
            {
                float x = area.xMin + area.width * (i + 0.5f) / n;
                var rect = Bit(null, new Vector2(w, h), i % 2 == 0 ? new Color(0.1f, 0.15f, 0.23f, 0.9f) : new Color(0.18f, 0.36f, 0.55f, 0.85f),
                    new Vector2(x, area.center.y), out var img);
                img.sprite = Theme.Rounded(Mathf.RoundToInt(w * 0.5f));
                rect.pivot = new Vector2(0.5f, 0f);
                bands.Add((rect, img, x, UnityEngine.Random.Range(0f, 6.28f)));
            }
            PlayClip(_thudClip, 0.9f, 0.55f);
            float y0 = area.center.y + h * 0.3f, y1 = area.yMin - h;
            yield return Tween(0.5f, k =>
            {
                float y = Mathf.Lerp(y0, y1, k * k);
                foreach (var b in bands)
                {
                    if (b.rect == null) continue;
                    b.rect.localPosition = new Vector2(b.x, y);
                    b.rect.localScale = new Vector3(1f, 0.85f + 0.3f * Mathf.Abs(Mathf.Sin(k * 18f + b.phase)), 1f);
                }
            });
            foreach (var b in bands) if (b.rect != null) Destroy(b.rect.gameObject);
            Shards(new Vector2(area.center.x, area.yMin + area.height * 0.2f), 18, new Color(0.44f, 0.7f, 0.88f), new Vector2(6f, 12f), 120f, 260f, 300f, 30f, 150f, circle: true);
        }

        /// <summary>洞穿:头顶凝出一根巨型墨枪,先长出枪头,再一下刺穿到玩家。</summary>
        private IEnumerator ImpaleSpear(Vector2 from, Vector2 to)
        {
            Vector2 d = to - from;
            float len = d.magnitude + 30f;
            var spear = Bit(null, new Vector2(1f, 22f), InkBlack, from, out var img);
            img.sprite = Theme.Rounded(8);
            spear.pivot = new Vector2(0f, 0.5f);
            spear.localPosition = from;
            spear.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            var tip = Bit(null, new Vector2(30f, 30f), BloodRed, from, out var tipImg);
            tip.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg + 45f);
            yield return Tween(0.16f, k => { if (spear != null) spear.sizeDelta = new Vector2(len * 0.15f * k, 22f); if (tip != null) tip.localPosition = from + d.normalized * len * 0.15f * k; });
            PlayClip(_hitClip, 0.9f, 0.7f);
            yield return Tween(0.14f, k =>
            {
                float l = Mathf.Lerp(len * 0.15f, len, k * k);
                if (spear != null) spear.sizeDelta = new Vector2(l, 22f);
                if (tip != null) tip.localPosition = from + d.normalized * l;
            });
            FreezeFrame(0.12f);
            StartCoroutine(FadeAndKill(spear, img, 0.3f));
            StartCoroutine(FadeAndKill(tip, tipImg, 0.3f));
        }

        /// <summary>倾覆:整屏一歪再摆回,玩家的护盾碎成冰屑。</summary>
        private IEnumerator ToppleTilt(Vector2 playerAt)
        {
            PlayClip(_thudClip, 0.9f, 0.6f);
            Shards(playerAt + Vector2.up * 20f, 14, new Color(0.62f, 0.78f, 0.92f), new Vector2(5f, 10f), 120f, 240f, 300f);
            yield return Tween(0.6f, k =>
            {
                if (_shakeTarget == null) return;
                float ang = k < 0.3f ? Mathf.Lerp(0f, -4f, k / 0.3f) : k < 0.6f ? Mathf.Lerp(-4f, 2f, (k - 0.3f) / 0.3f) : Mathf.Lerp(2f, 0f, (k - 0.6f) / 0.4f);
                _shakeTarget.localRotation = Quaternion.Euler(0f, 0f, ang);
            });
            if (_shakeTarget != null) _shakeTarget.localRotation = Quaternion.identity;
        }

        /// <summary>吞噬:一张墨色巨口在最前那只召唤物身上合拢,把它吞掉。</summary>
        private IEnumerator DevourJaws(RectTransform summon)
        {
            Vector2 at = Local(summon.position);
            float s = SizeOf(summon) * 1.5f, open = SizeOf(summon) * 0.9f;
            var top = Bit(null, new Vector2(s, s * 0.5f), InkBlack, at + Vector2.up * open, out var topImg);
            var bot = Bit(null, new Vector2(s, s * 0.5f), InkBlack, at + Vector2.down * open, out var botImg);
            topImg.sprite = botImg.sprite = Theme.Rounded(Mathf.RoundToInt(s * 0.25f));
            yield return Tween(0.16f, k =>
            {
                float y = Mathf.Lerp(open, s * 0.25f, k * k);
                if (top != null) top.localPosition = at + Vector2.up * y;
                if (bot != null) bot.localPosition = at + Vector2.down * y;
            });
            StartCoroutine(Shake(12f, Vector2.down));
            PlayClip(_killClip, 0.9f, 0.6f);
            Shards(at, 14, InkBlack, new Vector2(7f, 13f), 100f, 200f, 120f, circle: true);
            yield return Beat(0.2f);
            StartCoroutine(FadeAndKill(top, topImg, 0.26f));
            StartCoroutine(FadeAndKill(bot, botImg, 0.26f));
        }

        // ---- 坚壁(被动):我方打它时的反应 ----

        private static bool InBulwark(EnemyState s) =>
            s != null && s.IsBoss && s.Def.Phases.Count > 0
            && s.Def.Phases[Mathf.Clamp(s.PhaseIndex, 0, s.Def.Phases.Count - 1)].Skill == BossSkill.Bulwark;

        /// <summary>坚壁挡下:护甲片一闪、一道金属光泽扫过、火花四溅。</summary>
        private void BulwarkClank(RectTransform boss)
        {
            if (boss == null) return;
            Vector2 at = Local(boss.position);
            float s = SizeOf(boss) * 0.9f;
            var plate = Bit(null, new Vector2(s, s), new Color(0.9f, 0.86f, 0.75f, 0.45f), at, out var plateImg);
            plateImg.sprite = Theme.Rounded(Mathf.RoundToInt(s * 0.22f));
            StartCoroutine(Bloom(plate, plateImg, 0.9f, 1f, 0.5f));
            var sheen = Bit(null, new Vector2(s * 0.22f, s * 1.1f), new Color(1f, 1f, 1f, 0.8f), at + Vector2.left * s * 0.5f, out var sheenImg);
            sheen.localRotation = Quaternion.Euler(0f, 0f, -20f);
            StartCoroutine(Sweep(sheen, sheenImg, at + Vector2.left * s * 0.5f, at + Vector2.right * s * 0.5f));
            Shards(at, 12, new Color(1f, 0.88f, 0.54f), new Vector2(3f, 6f), 140f, 240f, 160f, circle: true);
            PlayClip(_shieldClip, 0.8f, 1.6f);
        }

        private IEnumerator Sweep(RectTransform rect, Image image, Vector2 from, Vector2 to)
        {
            Color c = image.color;
            yield return Tween(0.34f, k =>
            {
                if (rect == null) return;
                rect.localPosition = Vector2.Lerp(from, to, k);
                image.color = new Color(c.r, c.g, c.b, c.a * (1f - k));
            });
            if (rect != null) Destroy(rect.gameObject);
        }
    }
}
