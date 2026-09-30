using System;
using System.Collections;
using System.Collections.Generic;
using Brushblade.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>招式动效(2026-09-30,「招式动效」demo 拍板后落地)。
    ///
    /// 两段:**出手段**(<see cref="CastAttack"/>:刀光扫过去、火球飞过去、石块抛过去……
    /// 打到之后才回调外层开始结算)与**落点段**(<see cref="CastImpact"/>:结算里每一记伤害
    /// 按招式补一个落点 —— 刀口、火焰、冰屑、碎石)。剁的两段伤害各落一刀,角度交替。
    ///
    /// 另含召唤物的三种专属出手(藤缠绕 / 楸甩叶 / 荆带刺)与敌人远程按五行分的弹道。
    /// 全部由现有的刀光 / 箭 / 叶三张贴图 + 圆 / 圆角块程序拼成,不新增美术;
    /// 时间全走 _rate(按住屏幕 / 固定加速一起快)。</summary>
    public sealed partial class Juice
    {
        private static readonly Color SteelEdge = new(0.97f, 0.94f, 0.82f);    // 刀光:浅金白
        private static readonly Color IceCore = new(0.86f, 0.95f, 1f);
        private static readonly Color IceEdge = new(0.55f, 0.78f, 0.93f);
        private static readonly Color FireCore = new(1f, 0.76f, 0.29f);
        private static readonly Color FireDeep = new(0.84f, 0.29f, 0.14f);
        private static readonly Color WaterBlue = new(0.31f, 0.58f, 0.82f);
        private static readonly Color RockBrown = new(0.48f, 0.32f, 0.19f);
        private static readonly Color InkSealColor = new(0.14f, 0.24f, 0.40f);
        private static readonly Color PetalPink = new(0.93f, 0.55f, 0.66f);
        private static readonly Color VineGreen = new(0.18f, 0.48f, 0.24f);

        // ---- 小工具 ----

        private Vector2 Local(Vector3 world) => _shakeTarget.InverseTransformPoint(world);
        private Vector3 World(Vector2 local) => _shakeTarget.TransformPoint(local);

        /// <summary>挂一块特效:fx = 贴图名(slash / arrow / leaf),null = 圆角块;circle 为真时换成正圆。</summary>
        private RectTransform Bit(string fx, Vector2 size, Color color, Vector2 local, out Image image, bool circle = false)
        {
            var rect = FxImage(fx, size, color, World(local), out image);
            if (circle)
            {
                image.sprite = Theme.Circle;
                image.type = Image.Type.Simple;
                rect.sizeDelta = size;
            }
            return rect;
        }

        private IEnumerator FadeAndKill(RectTransform rect, Image image, float duration, float grow = 0f)
        {
            if (rect == null) yield break;
            Color from = image.color;
            Vector3 scale = rect.localScale;
            yield return Tween(duration, k =>
            {
                if (rect == null) return;
                image.color = new Color(from.r, from.g, from.b, from.a * (1f - k));
                rect.localScale = scale * (1f + grow * k);
            });
            if (rect != null) Destroy(rect.gameObject);
        }

        /// <summary>二次贝塞尔上的点。</summary>
        private static Vector2 Bez(Vector2 a, Vector2 c, Vector2 b, float t)
        {
            float u = 1f - t;
            return u * u * a + 2f * u * t * c + t * t * b;
        }

        /// <summary>一批碎片从 at 迸开(本地坐标):朝 [angleMin, angleMax] 度随机,带重力,边飞边淡。</summary>
        private void Shards(Vector2 at, int count, Color color, Vector2 sizeRange, float speedMin, float speedMax,
            float gravity, float angleMin = 0f, float angleMax = 360f, string fx = null, bool circle = false)
        {
            for (int n = 0; n < count; n++)
            {
                float size = UnityEngine.Random.Range(sizeRange.x, sizeRange.y);
                var rect = Bit(fx, new Vector2(size, size), color, at, out var image, circle);
                float a = UnityEngine.Random.Range(angleMin, angleMax) * Mathf.Deg2Rad;
                var v = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * UnityEngine.Random.Range(speedMin, speedMax);
                float spin = UnityEngine.Random.Range(-540f, 540f);
                StartCoroutine(ShardRoutine(rect, image, at, v, gravity, spin));
            }
        }

        private IEnumerator ShardRoutine(RectTransform rect, Image image, Vector2 at, Vector2 v, float gravity, float spin)
        {
            Color from = image.color;
            float t = 0f;
            const float life = 0.5f;
            while (t < life && rect != null)
            {
                t += Dt;
                float k = t / life;
                rect.localPosition = at + v * t + new Vector2(0f, -0.5f * gravity * t * t);
                rect.localRotation = Quaternion.Euler(0f, 0f, spin * t);
                image.color = new Color(from.r, from.g, from.b, 1f - k * k);
                yield return null;
            }
            if (rect != null) Destroy(rect.gameObject);
        }

        /// <summary>一道带转角与镜像的刀口(双刀的两刀角度交替;重斩竖着劈)。</summary>
        private void SlashAtAngle(Vector3 at, float size, Color color, float angle, float mirror)
        {
            var rect = FxImage("slash", new Vector2(size, size), color, at, out var image);
            rect.localRotation = Quaternion.Euler(0f, 0f, angle);
            StartCoroutine(SlashRoutine(rect, image, color, mirror));
        }

        private static float SizeOf(RectTransform rect) =>
            rect == null ? 100f : Mathf.Max(rect.rect.width, rect.rect.height);

        // ---- 出手段 ----

        /// <summary>我方出字的出手段:按招式把「打过去」演完再回调 onArrive(外层据此开始结算)。
        /// targets = 本次伤害 / 灼烧落到的敌人格,首个是主目标;为空时直接回调。
        /// rarity 决定档位(<see cref="TierOf"/>,JuiceTiers);glyph 是绝技写大字要写的字;
        /// flourish 为假时绝技跳过写大字(同一回合第二次出同一张字,由调用方判)。</summary>
        public void CastAttack(CastStyle style, CardRarity rarity, string glyph, bool flourish, Vector3 from,
            IReadOnlyList<RectTransform> targets, Action onArrive)
        {
            if (_shakeTarget == null || targets == null || targets.Count == 0) { onArrive?.Invoke(); return; }
            _castHit = 0;
            SetCastTier(rarity);
            StartCoroutine(CastRoutine(style, glyph, flourish, from, targets, onArrive));
        }

        private IEnumerator CastRoutine(CastStyle style, string glyph, bool flourish, Vector3 fromWorld,
            IReadOnlyList<RectTransform> targets, Action onArrive)
        {
            Vector2 from = Local(fromWorld);
            var primary = targets[0];
            Vector2 to = primary != null ? Local(primary.position) : from + Vector2.up * 300f;
            yield return TierPrelude(from, glyph, flourish);   // 华彩蓄势 / 奥义巨笔 / 绝技写大字
            if (_tier == CastTier.Arcane && (style == CastStyle.Fireball || style == CastStyle.Blast))
                CompanionOrbs(from, to, 34f * TS, 0.3f);           // 奥义:火球分身夹击
            switch (style)
            {
                case CastStyle.Sweep: yield return SweepLead(primary, to); break;
                case CastStyle.Thrust: yield return ThrustLead(from, to); break;
                case CastStyle.Fireball: yield return OrbLead(from, to, FireCore, FireDeep, 34f * TS, 0.3f, embers: true); break;
                case CastStyle.Blast:
                    yield return OrbLead(from, to, FireCore, FireDeep, 30f * TS, 0.26f, embers: true);
                    RingAt(World(to), FireCore);
                    ScreenFlash(0.14f, FireCore);
                    break;
                case CastStyle.FireWave: yield return WallLead(FirePaletteFor, 0.5f); break;
                case CastStyle.Wave: yield return WallLead(WaterPaletteFor, 0.5f); break;
                case CastStyle.IceShard: yield return ShardLead(from, to); break;
                case CastStyle.Frost: yield return MistLead(from, to); break;
                case CastStyle.WaterChain: yield return ChainLead(from, targets); break;
                case CastStyle.InkSeal: yield return SealLead(to, SizeOf(primary)); break;
                case CastStyle.Rock: yield return RockLead(from, to, 0.4f); break;
                case CastStyle.RockVolley:
                {
                    var second = targets.Count > 1 && targets[1] != null ? Local(targets[1].position) : to;
                    StartCoroutine(RockLead(from, second, 0.4f, delay: 0.1f));
                    yield return RockLead(from, to, 0.4f);
                    break;
                }
                case CastStyle.Quake: yield return QuakeLead(targets); break;
                case CastStyle.Petals: yield return PetalLead(from, to); break;
                case CastStyle.HeavyChop: yield return Beat(0.1f); break;
                default: yield return Beat(0.06f); break; // Slash / DoubleChop:刀口在落点段出
            }
            onArrive?.Invoke();
        }

        /// <summary>横扫:一道拉长的刀光沿主目标那一排从左扫到右,后面拖两道残影。</summary>
        private IEnumerator SweepLead(RectTransform primary, Vector2 to)
        {
            var area = _shakeTarget.rect;
            float size = Mathf.Clamp(SizeOf(primary) * 1.2f, 90f, 170f) * TS;
            float x0 = area.xMin + area.width * 0.06f, x1 = area.xMax - area.width * 0.06f;
            var blades = new List<(RectTransform rect, Image image, float lag, float alpha)>();
            float[] lags = { 0f, size * 0.22f, size * 0.44f };
            float[] alphas = { 1f, 0.5f, 0.28f };
            for (int i = 0; i < lags.Length; i++)
            {
                var rect = Bit("slash", new Vector2(size, size), SteelEdge, new Vector2(x0, to.y), out var image);
                rect.localRotation = Quaternion.Euler(0f, 0f, -90f);
                rect.localScale = new Vector3(1.5f, 0.8f, 1f);
                blades.Add((rect, image, lags[i], alphas[i]));
            }
            yield return Tween(0.26f, k =>
            {
                float x = Mathf.Lerp(x0, x1, k * k);
                float fade = k < 0.1f ? k / 0.1f : k > 0.9f ? (1f - k) / 0.1f : 1f;
                foreach (var b in blades)
                {
                    if (b.rect == null) continue;
                    b.rect.localPosition = new Vector2(x - b.lag, to.y);
                    b.image.color = new Color(SteelEdge.r, SteelEdge.g, SteelEdge.b, b.alpha * fade);
                }
            });
            foreach (var b in blades) if (b.rect != null) Destroy(b.rect.gameObject);
        }

        /// <summary>突刺:一根细长的枪芒从出手处刺穿主目标,再往后透一截(贯穿同一列)。</summary>
        private IEnumerator ThrustLead(Vector2 from, Vector2 to)
        {
            Vector2 d = (to - from).normalized;
            float len = (to - from).magnitude + 120f;
            var rect = Bit(null, new Vector2(10f, 8f), SteelEdge, from, out var image);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.localPosition = from;
            rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            yield return Tween(0.12f, k => { if (rect != null) rect.sizeDelta = new Vector2(Mathf.Lerp(10f, len, k * k), 8f); });
            StartCoroutine(FadeAndKill(rect, image, 0.18f));
        }

        /// <summary>弹体(火球 / 爆破):一颗圆沿微弧线飞到落点,沿途落火星。</summary>
        private IEnumerator OrbLead(Vector2 from, Vector2 to, Color core, Color deep, float size, float duration, bool embers)
        {
            var orb = Bit(null, new Vector2(size, size), core, from, out var orbImage, circle: true);
            var halo = Bit(null, new Vector2(size * 1.8f, size * 1.8f), new Color(deep.r, deep.g, deep.b, 0.45f), from, out _, circle: true);
            halo.SetAsFirstSibling();
            orb.SetAsLastSibling();
            float arc = (to - from).magnitude * 0.12f;
            float next = 0f, t = 0f;
            yield return Tween(duration, k =>
            {
                Vector2 p = Vector2.Lerp(from, to, k * k) + Vector2.up * (Mathf.Sin(k * Mathf.PI) * arc);
                if (orb != null) orb.localPosition = p;
                if (halo != null) halo.localPosition = p;
                t = k * duration;
                if (embers && t >= next)
                {
                    next = t + TrailInterval;
                    var emberColor = _tier >= CastTier.Splendid && UnityEngine.Random.value < 0.5f ? _tierColor   // 华彩起:金色拖尾
                        : UnityEngine.Random.value < 0.5f ? core : deep;
                    var e = Bit(null, new Vector2(7f, 7f) * TS, emberColor, p, out var eImage, circle: true);
                    StartCoroutine(RiseAndFade(e, eImage, 22f, 0.35f));
                }
            });
            if (orb != null) Destroy(orb.gameObject);
            if (halo != null) Destroy(halo.gameObject);
        }

        private IEnumerator RiseAndFade(RectTransform rect, Image image, float rise, float duration)
        {
            Vector2 start = rect.localPosition;
            Color from = image.color;
            yield return Tween(duration, k =>
            {
                if (rect == null) return;
                rect.localPosition = start + new Vector2(0f, rise * k);
                rect.localScale = Vector3.one * (1f - 0.6f * k);
                image.color = new Color(from.r, from.g, from.b, 1f - k);
            });
            if (rect != null) Destroy(rect.gameObject);
        }

        private static Color FirePaletteFor(int i) => i % 3 == 0 ? new Color(1f, 0.86f, 0.5f) : i % 3 == 1 ? FireCore : FireDeep;
        private static Color WaterPaletteFor(int i) => i % 3 == 0 ? new Color(0.78f, 0.9f, 0.98f) : i % 3 == 1 ? WaterBlue : new Color(0.2f, 0.42f, 0.66f);

        /// <summary>火浪 / 浪潮:一排舌头从我方一侧往上卷过整个敌阵,边走边抖。</summary>
        private IEnumerator WallLead(Func<int, Color> palette, float duration)
        {
            var area = _shakeTarget.rect;
            const int n = 16;
            float w = area.width / n * 1.5f, h = area.height * 0.18f;
            float y0 = area.yMin + area.height * 0.2f, y1 = area.yMax + h * 0.3f;
            var tongues = new List<(RectTransform rect, Image image, float phase, float x)>();
            for (int i = 0; i < n; i++)
            {
                float x = area.xMin + area.width * (i + 0.5f) / n;
                var rect = Bit(null, new Vector2(w, h), palette(i), new Vector2(x, y0), out var image);
                image.sprite = Theme.Rounded(Mathf.RoundToInt(w * 0.5f));
                rect.pivot = new Vector2(0.5f, 0f);
                tongues.Add((rect, image, UnityEngine.Random.Range(0f, 6.28f), x));
            }
            yield return Tween(duration, k =>
            {
                float y = Mathf.Lerp(y0, y1, k * k);
                float fade = k > 0.8f ? (1f - k) / 0.2f : 1f;
                foreach (var tg in tongues)
                {
                    if (tg.rect == null) continue;
                    tg.rect.localPosition = new Vector2(tg.x, y - h);
                    tg.rect.localScale = new Vector3(1f, 0.8f + 0.35f * Mathf.Abs(Mathf.Sin(k * 22f + tg.phase)), 1f);
                    var c = tg.image.color;
                    tg.image.color = new Color(c.r, c.g, c.b, 0.9f * fade);
                }
            });
            foreach (var tg in tongues) if (tg.rect != null) Destroy(tg.rect.gameObject);
        }

        /// <summary>冰刺:一根冰棱直射过去,身后拖一串冰屑。</summary>
        private IEnumerator ShardLead(Vector2 from, Vector2 to)
        {
            Vector2 d = (to - from).normalized;
            var shard = Bit(null, new Vector2(46f, 10f) * TS, IceCore, from, out _);
            shard.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            float next = 0f;
            yield return Tween(0.18f, k =>
            {
                Vector2 p = Vector2.Lerp(from, to, k * k);
                if (shard != null) shard.localPosition = p;
                if (k >= next)
                {
                    next = k + 0.15f;
                    var dotRect = Bit(null, new Vector2(6f, 6f), IceEdge, p, out var dotImage);
                    StartCoroutine(FadeAndKill(dotRect, dotImage, 0.2f));
                }
            });
            if (shard != null) Destroy(shard.gameObject);
        }

        /// <summary>寒雾:几团淡白的雾从出手处飘到目标,边飘边胀开。</summary>
        private IEnumerator MistLead(Vector2 from, Vector2 to)
        {
            var puffs = new List<(RectTransform rect, Image image, Vector2 offset, float delay)>();
            for (int i = 0; i < 5; i++)
            {
                var rect = Bit(null, new Vector2(34f, 34f), new Color(IceCore.r, IceCore.g, IceCore.b, 0f), from, out var image, circle: true);
                puffs.Add((rect, image, UnityEngine.Random.insideUnitCircle * 26f, i * 0.05f));
            }
            yield return Tween(0.38f, k =>
            {
                foreach (var p in puffs)
                {
                    if (p.rect == null) continue;
                    float kk = Mathf.Clamp01((k - p.delay) / (1f - p.delay));
                    p.rect.localPosition = Vector2.Lerp(from, to, 1f - (1f - kk) * (1f - kk)) + p.offset * kk;
                    p.rect.localScale = Vector3.one * (0.6f + 1.2f * kk);
                    p.image.color = new Color(IceCore.r, IceCore.g, IceCore.b, 0.7f * Mathf.Sin(kk * Mathf.PI * 0.9f + 0.1f));
                }
            });
            foreach (var p in puffs) if (p.rect != null) StartCoroutine(FadeAndKill(p.rect, p.image, 0.2f, 0.3f));
        }

        /// <summary>水弹连跳:一颗水珠依次弹过每个目标。</summary>
        private IEnumerator ChainLead(Vector2 from, IReadOnlyList<RectTransform> targets)
        {
            var orb = Bit(null, new Vector2(24f, 24f), WaterBlue, from, out _, circle: true);
            Vector2 at = from;
            foreach (var t in targets)
            {
                if (t == null) continue;
                Vector2 next = Local(t.position);
                Vector2 start = at;
                float arc = (next - start).magnitude * 0.25f;
                yield return Tween(0.14f, k =>
                {
                    if (orb != null) orb.localPosition = Vector2.Lerp(start, next, k) + Vector2.up * (Mathf.Sin(k * Mathf.PI) * arc);
                });
                WaterSplash(t);
                at = next;
            }
            if (orb != null) Destroy(orb.gameObject);
        }

        /// <summary>静默水印:一枚方印从上方盖下来,落定后收拢。</summary>
        private IEnumerator SealLead(Vector2 to, float targetSize)
        {
            float size = Mathf.Clamp(targetSize * 0.8f, 60f, 120f);
            var seal = Bit(null, new Vector2(size, size), new Color(InkSealColor.r, InkSealColor.g, InkSealColor.b, 0f), to, out var image);
            image.sprite = Theme.Rounded(12);
            yield return Tween(0.2f, k =>
            {
                if (seal == null) return;
                seal.localScale = Vector3.one * Mathf.Lerp(2f, 1f, k * k);
                image.color = new Color(InkSealColor.r, InkSealColor.g, InkSealColor.b, 0.55f * k);
            });
            StartCoroutine(FadeAndKill(seal, image, 0.3f, -0.4f));
        }

        /// <summary>落石:一块石头边转边抛过去,砸在落点。</summary>
        private IEnumerator RockLead(Vector2 from, Vector2 to, float duration, float delay = 0f)
        {
            if (delay > 0f) yield return Beat(delay);
            var rock = Bit(null, new Vector2(34f, 30f) * TS, RockBrown, from, out var image);
            image.sprite = Theme.Rounded(9);
            float lift = Mathf.Max(120f, (to - from).magnitude * 0.45f);
            yield return Tween(duration, k =>
            {
                if (rock == null) return;
                rock.localPosition = Vector2.Lerp(from, to, k) + Vector2.up * (Mathf.Sin(k * Mathf.PI) * lift);
                rock.localRotation = Quaternion.Euler(0f, 0f, 320f * k);
                rock.localScale = Vector3.one * (1f + 0.25f * Mathf.Sin(k * Mathf.PI));
            });
            if (rock != null) Destroy(rock.gameObject);
            Shards(to, 6, new Color(0.62f, 0.55f, 0.46f, 0.6f), new Vector2(12f, 20f), 30f, 70f, -20f, 0f, 180f, circle: true);
        }

        /// <summary>地震:整屏一震,每个目标头顶各砸下一块石头。</summary>
        private IEnumerator QuakeLead(IReadOnlyList<RectTransform> targets)
        {
            StartCoroutine(Shake(16f, Vector2.up));
            var area = _shakeTarget.rect;
            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i] == null) continue;
                Vector2 to = Local(targets[i].position);
                StartCoroutine(RockLead(new Vector2(to.x + UnityEngine.Random.Range(-30f, 30f), area.yMax + 40f), to, 0.28f, i * 0.05f));
            }
            yield return Beat(0.3f + targets.Count * 0.05f);
        }

        /// <summary>花瓣:几片粉色花瓣各走一条弯弧旋向目标。</summary>
        private IEnumerator PetalLead(Vector2 from, Vector2 to)
        {
            Vector2 d = to - from, n = new Vector2(-d.y, d.x).normalized;
            var petals = new List<(RectTransform rect, Vector2 ctrl, float delay)>();
            float[] bends = { 0.4f, 0.2f, 0f, -0.2f, -0.4f };
            for (int i = 0; i < bends.Length; i++)
            {
                var rect = Bit("leaf", new Vector2(22f, 22f), PetalPink, from, out _);
                petals.Add((rect, (from + to) * 0.5f + n * d.magnitude * bends[i], i * 0.04f));
            }
            yield return Tween(0.36f, k =>
            {
                foreach (var p in petals)
                {
                    if (p.rect == null) continue;
                    float kk = Mathf.Clamp01((k - p.delay) / (1f - p.delay));
                    p.rect.localPosition = Bez(from, p.ctrl, to, kk);
                    p.rect.localRotation = Quaternion.Euler(0f, 0f, 540f * kk);
                }
            });
            foreach (var p in petals) if (p.rect != null) Destroy(p.rect.gameObject);
            LeafBurst(World(to), PetalPink, 6);
        }

        // ---- 落点段:结算里每一记伤害按招式补一个落点 ----

        private int _castHit; // 本次出字已落的第几记(双刀按它交替角度)

        /// <summary>一记伤害的落点装饰。Glyph(敌人回合、召唤物、非出字结算)不画任何东西。</summary>
        private void CastImpact(CastStyle style, RectTransform target)
        {
            if (target == null || _shakeTarget == null || style == CastStyle.Glyph) return;
            float size = Mathf.Clamp(SizeOf(target) * 1.15f, 80f, 160f) * TS;
            Vector2 at = Local(target.position);
            int index = _castHit++;
            TierImpact(target);   // 档位加码:光环 / 金屑 / 屏闪 / 顿帧推镜(JuiceTiers)
            switch (style)
            {
                case CastStyle.Slash:
                case CastStyle.Sweep:
                case CastStyle.Thrust:
                    SlashAtAngle(target.position, size, SteelEdge, UnityEngine.Random.Range(-25f, 25f), 1f);
                    break;
                case CastStyle.DoubleChop:
                    SlashAtAngle(target.position, size, SteelEdge, index % 2 == 0 ? -35f : 35f, index % 2 == 0 ? 1f : -1f);
                    break;
                case CastStyle.HeavyChop:
                    SlashAtAngle(target.position, size * 1.3f, SteelEdge, 90f, 1f);
                    ScreenFlash(0.1f, SteelEdge);
                    break;
                case CastStyle.Fireball:
                case CastStyle.FireWave:
                    FlameBurst(target);
                    break;
                case CastStyle.Blast:
                    FlameBurst(target);
                    Sparks(target.position);
                    break;
                case CastStyle.IceShard:
                    Shards(at, 8, IceCore, new Vector2(6f, 11f), 120f, 220f, 260f);
                    Ring(target, IceEdge);
                    break;
                case CastStyle.Frost:
                    Ring(target, IceCore);
                    break;
                case CastStyle.WaterChain:
                case CastStyle.Wave:
                    WaterSplash(target);
                    break;
                case CastStyle.InkSeal:
                    Ring(target, InkSealColor, inward: true);
                    break;
                case CastStyle.Rock:
                case CastStyle.RockVolley:
                case CastStyle.Quake:
                    RockBurst(target);
                    break;
                case CastStyle.Petals:
                    LeafBurst(target.position, PetalPink, 4);
                    break;
            }
        }

        // ---- 召唤物专属出手 ----

        /// <summary>藤:藤蔓(一串圆节)沿弯弧甩到目标,再在目标身上缠两圈;命中那一刻返回,
        /// 缠住的藤停一会儿再淡掉(不阻塞时间线)。</summary>
        private IEnumerator SummonVine(RectTransform from, RectTransform to)
        {
            if (from == null || to == null || _shakeTarget == null) yield break;
            SummonViewOf(from)?.PlayAttack();
            Vector2 a = Local(from.position), b = Local(to.position);
            Vector2 d = b - a, n = new Vector2(-d.y, d.x).normalized;
            Vector2 ctrl = (a + b) * 0.5f + n * d.magnitude * 0.3f;
            var parts = new List<(RectTransform rect, Image image)>();
            const int whipSegs = 22, coilSegs = 30;
            var whip = new Vector2[whipSegs];
            for (int i = 0; i < whipSegs; i++) whip[i] = Bez(a, ctrl, b, (i + 1f) / whipSegs);
            float rx = Mathf.Max(40f, SizeOf(to) * 0.42f), ry = rx * 0.38f, tall = SizeOf(to) * 0.5f;
            var coil = new Vector2[coilSegs];
            for (int i = 0; i < coilSegs; i++)
            {
                float t = (i + 1f) / coilSegs, ang = t * Mathf.PI * 4f - Mathf.PI / 2f;
                coil[i] = b + new Vector2(Mathf.Cos(ang) * rx, Mathf.Sin(ang) * ry + (0.5f - t) * tall);
            }

            int shown = 0;
            yield return Tween(0.22f, k =>
            {
                int upto = Mathf.CeilToInt(k * whipSegs);
                for (; shown < upto; shown++)
                {
                    float w = Mathf.Lerp(11f, 6f, shown / (float)whipSegs);
                    parts.Add((Bit(null, new Vector2(w, w), VineGreen, whip[shown], out var img, circle: true), img));
                    if (shown % 7 == 3)
                    {
                        var leaf = Bit("leaf", new Vector2(18f, 18f), VineGreen, whip[shown] + n * 8f, out var leafImg);
                        leaf.localRotation = Quaternion.Euler(0f, 0f, UnityEngine.Random.Range(0f, 360f));
                        parts.Add((leaf, leafImg));
                    }
                }
            });
            shown = 0;
            yield return Tween(0.2f, k =>
            {
                int upto = Mathf.CeilToInt(k * coilSegs);
                for (; shown < upto; shown++)
                {
                    bool front = Mathf.Sin((shown + 1f) / coilSegs * Mathf.PI * 4f - Mathf.PI / 2f) > 0f;
                    var c = front ? VineGreen : new Color(VineGreen.r, VineGreen.g, VineGreen.b, 0.45f);
                    parts.Add((Bit(null, new Vector2(front ? 9f : 6f, front ? 9f : 6f), c, coil[shown], out var img, circle: true), img));
                }
            });
            PlayClip(_hitClip, 0.5f, 0.9f);
            StartCoroutine(VineRelease(parts));
        }

        private IEnumerator VineRelease(List<(RectTransform rect, Image image)> parts)
        {
            yield return Beat(0.5f);
            foreach (var p in parts) if (p.rect != null) StartCoroutine(FadeAndKill(p.rect, p.image, 0.25f));
        }

        /// <summary>楸:三片叶扇形甩出、各走一条弯弧,在目标身上汇合;首片到达即命中返回。</summary>
        private IEnumerator SummonLeaves(RectTransform from, RectTransform to, Element element)
        {
            if (from == null || to == null || _shakeTarget == null) yield break;
            SummonViewOf(from)?.PlayAttack();
            var color = Theme.GlyphColor(element);
            Vector2 a = Local(from.position), b = Local(to.position), d = b - a, n = new Vector2(-d.y, d.x).normalized;
            float[] bends = { 0.45f, 0f, -0.45f };
            var leaves = new List<(RectTransform rect, Vector2 ctrl, float delay)>();
            for (int i = 0; i < bends.Length; i++)
                leaves.Add((Bit("leaf", new Vector2(30f, 30f), color, a, out _), (a + b) * 0.5f + n * d.magnitude * bends[i], i * 0.06f));
            PlayClip(_hitClip, 0.45f, 1.3f);
            const float flight = 0.32f;
            float elapsed = 0f;
            void Step()
            {
                foreach (var l in leaves)
                {
                    if (l.rect == null) continue;
                    float kk = Mathf.Clamp01((elapsed - l.delay) / flight);
                    l.rect.localPosition = Bez(a, l.ctrl, b, kk);
                    l.rect.localRotation = Quaternion.Euler(0f, 0f, 720f * kk);
                    if (kk >= 1f) Destroy(l.rect.gameObject);
                }
            }
            while (elapsed < flight) { elapsed += Dt; Step(); yield return null; }
            LeafBurst(to.position, color, 5);
            StartCoroutine(LeavesFinish(leaves, a, b, elapsed, flight));
        }

        /// <summary>首片命中后,晚出发的两片接着飞完、到了就消失。</summary>
        private IEnumerator LeavesFinish(List<(RectTransform rect, Vector2 ctrl, float delay)> leaves,
            Vector2 a, Vector2 b, float elapsed, float flight)
        {
            float end = flight + 0.2f;
            while (elapsed < end)
            {
                elapsed += Dt;
                foreach (var l in leaves)
                {
                    if (l.rect == null) continue;
                    float kk = Mathf.Clamp01((elapsed - l.delay) / flight);
                    l.rect.localPosition = Bez(a, l.ctrl, b, kk);
                    l.rect.localRotation = Quaternion.Euler(0f, 0f, 720f * kk);
                    if (kk >= 1f) Destroy(l.rect.gameObject);
                }
                yield return null;
            }
            foreach (var l in leaves) if (l.rect != null) Destroy(l.rect.gameObject);
        }

        /// <summary>荆:真身冲脸,落点再迸一圈荆刺。</summary>
        private IEnumerator SummonThornStrike(RectTransform from, RectTransform to, Element element)
        {
            var color = Theme.GlyphColor(element);
            SummonViewOf(from)?.PlayAttack();
            yield return BodyStrike(from, to, AnchorPoint(to), color, slash: true);
            if (to != null)
            {
                Shards(Local(to.position), 10, VineGreen, new Vector2(5f, 9f), 140f, 230f, 120f);
                Ring(to, VineGreen, inward: true);
            }
            PlayClip(_hitClip, 0.55f, 1.15f);
        }

        // ---- 敌人远程:按五行分弹道 ----

        /// <summary>火 = 火球、水 = 水珠、土 = 抛石、金 = 飞旋的刀、木 = 叶;其余(心 / 未现形)仍是墨弹。</summary>
        private IEnumerator EnemyBolt(RectTransform attacker, Vector3 point, Element? element, Color color)
        {
            Vector2 from = Local(attacker.position), to = Local(point);
            Vector2 home = HomeOf(attacker);
            int token = NextMoveToken(attacker);
            Vector2 dir = LocalDir(attacker, point);
            yield return Tween(ShootDraw, k => { if (attacker != null) attacker.anchoredPosition = home - dir * (10f * k); });
            if (attacker == null) yield break;
            StartCoroutine(ReturnHome(attacker, home, StrikeRecover, token));
            PlayClip(_hitClip, 0.45f, 1.2f);
            switch (element)
            {
                case Element.Fire:
                    yield return OrbLead(from, to, FireCore, FireDeep, 26f, 0.26f, embers: true);
                    FlameBurstAt(point);
                    break;
                case Element.Water:
                    yield return OrbLead(from, to, new Color(0.78f, 0.9f, 0.98f), WaterBlue, 20f, 0.22f, embers: false);
                    break;
                case Element.Earth:
                    yield return RockLead(from, to, 0.34f);
                    break;
                case Element.Metal:
                    yield return SpinLead("slash", from, to, SteelEdge, 40f, 0.22f);
                    break;
                case Element.Wood:
                    yield return SpinLead("leaf", from, to, color, 28f, 0.26f);
                    break;
                default:
                    yield return OrbLead(from, to, color, Theme.Ink, EnemyBoltSize, ShootFlight, embers: false);
                    break;
            }
        }

        /// <summary>飞旋的贴图弹(金的刀、木的叶)。</summary>
        private IEnumerator SpinLead(string fx, Vector2 from, Vector2 to, Color color, float size, float duration)
        {
            var rect = Bit(fx, new Vector2(size, size), color, from, out _);
            yield return Tween(duration, k =>
            {
                if (rect == null) return;
                rect.localPosition = Vector2.Lerp(from, to, k * (0.6f + 0.4f * k));
                rect.localRotation = Quaternion.Euler(0f, 0f, 900f * k);
            });
            if (rect != null) Destroy(rect.gameObject);
        }

        /// <summary>FlameBurst 的按点位版本(打玩家时没有目标格)。</summary>
        private void FlameBurstAt(Vector3 point) =>
            Shards(Local(point), 6, FireCore, new Vector2(9f, 15f), 40f, 90f, -120f, 30f, 150f, circle: true);
    }
}
