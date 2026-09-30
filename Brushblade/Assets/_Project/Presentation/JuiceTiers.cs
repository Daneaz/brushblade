using System.Collections;
using System.Collections.Generic;
using Brushblade.Core;
using Brushblade.Data;
using UnityEngine;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>招式动效按稀有度分五档(2026-09-30,「稀有度动效分档」demo 拍板)。
    /// 同一招式,档位越高越隆重;招式本身只写一份,档位只往上叠:
    ///
    ///   常规(白绿蓝)现在的样子
    ///   精良(紫)    只加一点量(×1.15)+ 落点一圈紫色光环
    ///   华彩(金)    出手前蓄势、金色拖尾;命中顿帧 + 镜头推近、金屑、屏闪 —— 与精良拉开的主要一刀
    ///   奥义(橙)    巨笔横扫全屏(不写大字);火球类分身夹击;最后一击后补收尾
    ///   绝技(红)    压暗全屏写出这个字的大字、墨点四溅、落一枚朱印,再蓄势出招;收尾再大一档
    ///
    /// 收尾(横斩 / 十字 / 冲击环 / 火雨)**只是视觉**,不多算伤害 —— 伤害事件的条数由 Core 定。</summary>
    public sealed partial class Juice
    {
        public enum CastTier { Common, Fine, Splendid, Arcane, Ultimate }

        public static CastTier TierOf(CardRarity rarity) => rarity switch
        {
            CardRarity.Purple => CastTier.Fine,
            CardRarity.Gold => CastTier.Splendid,
            CardRarity.Orange => CastTier.Arcane,
            CardRarity.Red => CastTier.Ultimate,
            _ => CastTier.Common,
        };

        private static readonly float[] TierScale = { 1f, 1.15f, 1.5f, 1.7f, 1.9f };
        private static readonly float[] TierFreeze = { 0f, 0f, 0.11f, 0.14f, 0.17f };

        private CastTier _tier = CastTier.Common;
        private Color _tierColor = Color.white;
        private RectTransform _lastImpact;

        /// <summary>当前出字的放大系数(非出字演出恒为 1)。</summary>
        private float TS => TierScale[(int)_tier];

        private void SetCastTier(CardRarity rarity)
        {
            _tier = TierOf(rarity);
            _tierColor = Theme.RarityColor(rarity);
            _lastImpact = null;
        }

        /// <summary>一段出字结算演完就复位:敌人回合 / 召唤物出手共用那几个弹道函数,不能吃到上一张字的档位。</summary>
        private void ResetCastTier()
        {
            _tier = CastTier.Common;
            _lastImpact = null;
        }

        // ---- 出手前 ----

        /// <summary>出手前的档位加码:华彩蓄势;奥义巨笔横扫;绝技压暗写大字(flourish 为假时跳过)再蓄势。</summary>
        private IEnumerator TierPrelude(Vector2 from, string glyph, bool flourish)
        {
            switch (_tier)
            {
                case CastTier.Splendid:
                    yield return Charge(from);
                    break;
                case CastTier.Arcane:
                    yield return BrushSwipe();
                    break;
                case CastTier.Ultimate:
                    if (flourish) yield return UltimateGlyph(glyph);
                    yield return Charge(from);
                    break;
            }
        }

        /// <summary>蓄势:出手处一圈稀有度色光环外扩,八粒光点从四周收拢过来。</summary>
        private IEnumerator Charge(Vector2 from)
        {
            RingAt(World(from), _tierColor);
            for (int i = 0; i < 8; i++)
            {
                float a = i / 8f * Mathf.PI * 2f;
                Vector2 start = from + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 90f;
                var dot = Bit(null, new Vector2(8f, 8f), _tierColor, start, out var image, circle: true);
                StartCoroutine(Converge(dot, image, start, from, i * 0.012f));
            }
            PlayClip(_shieldClip, 0.5f, 1.4f);
            yield return Beat(0.24f);
        }

        private IEnumerator Converge(RectTransform dot, Image image, Vector2 start, Vector2 to, float delay)
        {
            if (delay > 0f) yield return Beat(delay);
            Color c = image.color;
            yield return Tween(0.22f, k =>
            {
                if (dot == null) return;
                float e = k * k;
                dot.localPosition = Vector2.Lerp(start, to, e);
                dot.localScale = Vector3.one * (1f - 0.6f * e);
                image.color = new Color(c.r, c.g, c.b, k < 0.3f ? k / 0.3f : 1f - (k - 0.3f) / 0.7f * 0.8f);
            });
            if (dot != null) Destroy(dot.gameObject);
        }

        /// <summary>奥义:一支巨笔从左到右横扫整个战场(墨色笔身 + 上下两道橙边),笔尾甩出墨点。</summary>
        private IEnumerator BrushSwipe()
        {
            var area = _shakeTarget.rect;
            float h = area.height * 0.26f, y = area.center.y;
            var parts = new List<(RectTransform rect, Image image)>();
            RectTransform Band(float height, float yOff, Color color)
            {
                var rect = Bit(null, new Vector2(area.width, height), color, new Vector2(area.xMin, y + yOff), out var image);
                image.sprite = Theme.Rounded(Mathf.RoundToInt(height * 0.5f));
                rect.pivot = new Vector2(0f, 0.5f);
                rect.localPosition = new Vector2(area.xMin, y + yOff);
                rect.localScale = new Vector3(0f, 1f, 1f);
                parts.Add((rect, image));
                return rect;
            }
            var edge = new Color(_tierColor.r, _tierColor.g, _tierColor.b, 0.55f);
            Band(h * 1.12f, 0f, edge);
            Band(h, 0f, new Color(0.07f, 0.09f, 0.12f, 0.9f));
            Band(h * 0.22f, h * 0.18f, new Color(0.16f, 0.19f, 0.25f, 0.9f)); // 笔身里一道浅色毛丝
            PlayClip(_thudClip, 0.6f, 0.7f);
            yield return Tween(0.26f, k =>
            {
                float e = 1f - (1f - k) * (1f - k);
                foreach (var p in parts) if (p.rect != null) p.rect.localScale = new Vector3(e, 1f, 1f);
            });
            Shards(new Vector2(area.xMax - area.width * 0.06f, y), 12, new Color(0.07f, 0.09f, 0.12f), new Vector2(6f, 13f),
                120f, 260f, 200f, -60f, 60f, circle: true);
            StartCoroutine(Shake(9f, Vector2.right));
            foreach (var p in parts) StartCoroutine(FadeAndKill(p.rect, p.image, 0.42f));
            yield return Beat(0.12f);
        }

        /// <summary>绝技:压暗全屏,中央把这个字从左往右「写」出来(RectMask2D 展开),
        /// 写完墨点四溅、右下角落一枚朱印;停一拍后大字放大淡出、压暗撤掉。</summary>
        private IEnumerator UltimateGlyph(string glyph)
        {
            var area = _shakeTarget.rect;
            var dim = Bit(null, area.size, new Color(0.04f, 0.05f, 0.08f, 0f), area.center, out var dimImage);
            dim.SetAsLastSibling();

            float size = area.height * 0.62f;
            var maskGo = new GameObject("GlyphMask", typeof(RectTransform), typeof(RectMask2D));
            maskGo.transform.SetParent(_shakeTarget, false);
            var mask = (RectTransform)maskGo.transform;
            mask.pivot = new Vector2(0f, 0.5f);
            mask.sizeDelta = new Vector2(0f, size);
            mask.localPosition = new Vector2(area.center.x - size / 2f, area.center.y + area.height * 0.04f);
            var label = new GameObject("Glyph", typeof(RectTransform)).AddComponent<Text>();
            label.transform.SetParent(mask, false);
            var lr = label.rectTransform;
            lr.anchorMin = lr.anchorMax = new Vector2(0f, 0.5f);
            lr.pivot = new Vector2(0f, 0.5f);
            lr.sizeDelta = new Vector2(size, size);
            lr.anchoredPosition = Vector2.zero;
            label.font = Theme.TitleFont;
            label.fontSize = Mathf.RoundToInt(size * 0.86f);
            label.fontStyle = FontStyle.Bold;
            label.text = glyph;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = new Color(0.965f, 0.945f, 0.906f);
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            var glow = label.gameObject.AddComponent<Outline>();
            glow.effectColor = new Color(_tierColor.r, _tierColor.g, _tierColor.b, 0.7f);
            glow.effectDistance = new Vector2(3f, -3f);

            PlayClip(_thudClip, 0.7f, 0.6f);
            yield return Tween(0.14f, k => { if (dimImage != null) dimImage.color = new Color(0.04f, 0.05f, 0.08f, 0.72f * k); });
            yield return Tween(0.38f, k =>
            {
                if (mask == null) return;
                float e = 1f - (1f - k) * (1f - k);
                mask.sizeDelta = new Vector2(size * e, size);
                label.transform.localScale = Vector3.one * Mathf.Lerp(1.12f, 1f, e);
            });

            // 墨点四溅(压暗底上用宣纸色)+ 朱印
            Vector2 c = new Vector2(area.center.x, area.center.y + area.height * 0.04f);
            Shards(c, 14, new Color(0.965f, 0.945f, 0.906f, 0.9f), new Vector2(5f, 12f), 160f, 320f, 0f, circle: true);
            float sealSize = size * 0.22f;
            var seal = Bit(null, new Vector2(sealSize, sealSize), Theme.Cinnabar, c + new Vector2(size * 0.42f, -size * 0.34f), out var sealImage);
            seal.localRotation = Quaternion.Euler(0f, 0f, -6f);
            var sealText = Ui.ThemedLabel(seal, Strings.T("battle.fx.ultimate_seal"), Mathf.RoundToInt(sealSize * 0.56f),
                new Color(0.965f, 0.945f, 0.906f), Theme.TitleFont);
            Ui.Stretch(sealText.rectTransform);
            StartCoroutine(Stamp(seal));
            StartCoroutine(Shake(7f, Vector2.down));
            PlayClip(_killClip, 0.6f, 0.8f);
            yield return Beat(0.22f);

            // 大字放大淡出,压暗撤掉
            yield return Tween(0.22f, k =>
            {
                if (mask != null) mask.localScale = Vector3.one * (1f + 0.35f * k * k);
                if (label != null) label.color = new Color(label.color.r, label.color.g, label.color.b, 1f - k);
                if (dimImage != null) dimImage.color = new Color(0.04f, 0.05f, 0.08f, 0.72f * (1f - k));
                if (sealImage != null) sealImage.color = new Color(sealImage.color.r, sealImage.color.g, sealImage.color.b, 1f - k);
                if (sealText != null) sealText.color = new Color(sealText.color.r, sealText.color.g, sealText.color.b, 1f - k);
            });
            if (mask != null) Destroy(mask.gameObject);
            if (dim != null) Destroy(dim.gameObject);
            if (seal != null) Destroy(seal.gameObject);
        }

        private IEnumerator Stamp(RectTransform seal)
        {
            yield return Tween(0.16f, k => { if (seal != null) seal.localScale = Vector3.one * Mathf.Lerp(2f, 1f, k * k); });
        }

        // ---- 落点加码 ----

        /// <summary>每一记伤害的档位加码:精良起一圈稀有度色光环;华彩起再加金屑、屏闪、顿帧 + 镜头推近。</summary>
        private void TierImpact(RectTransform target)
        {
            _lastImpact = target;
            if (_tier == CastTier.Common || target == null) return;
            Ring(target, _tierColor);
            if (_tier < CastTier.Splendid) return;
            Shards(Local(target.position), 8, _tierColor, new Vector2(6f, 10f), 120f, 220f, 60f);
            ScreenFlash(0.12f, new Color(1f, 0.96f, 0.86f));
            FreezeFrame(TierFreeze[(int)_tier]);
        }

        /// <summary>顿帧 + 镜头推近:走 Tween / Beat 的演出按住 seconds,战场整体放大 5% 再回来。</summary>
        private void FreezeFrame(float seconds)
        {
            if (seconds <= 0f) return;
            _freezeUntil = Mathf.Max(_freezeUntil, UnityEngine.Time.unscaledTime + seconds / Mathf.Max(1f, _rate));
            StartCoroutine(ZoomPunch(seconds + 0.2f));
        }

        private IEnumerator ZoomPunch(float duration)
        {
            if (_shakeTarget == null) yield break;
            float t = 0f;
            while (t < duration && _shakeTarget != null)
            {
                t += UnityEngine.Time.unscaledDeltaTime * _rate;   // 推镜本身不吃顿帧,否则停住期间看不出在推
                float k = Mathf.Clamp01(t / duration);
                float s = 1f + 0.05f * (k < 0.35f ? k / 0.35f : 1f - (k - 0.35f) / 0.65f);
                _shakeTarget.localScale = new Vector3(s, s, 1f);
                yield return null;
            }
            if (_shakeTarget != null) _shakeTarget.localScale = Vector3.one;
        }

        // ---- 收尾(奥义 / 绝技,最后一击之后,纯视觉)----

        private IEnumerator CastFinisher(CastStyle style)
        {
            if (_tier < CastTier.Arcane || _lastImpact == null) yield break;
            var target = _lastImpact;
            float size = Mathf.Clamp(SizeOf(target) * 1.15f, 80f, 160f) * TS;
            yield return Beat(0.2f);
            if (target == null) yield break;
            bool blade = style == CastStyle.Slash || style == CastStyle.DoubleChop || style == CastStyle.HeavyChop
                || style == CastStyle.Sweep || style == CastStyle.Thrust;
            if (blade)
            {
                // 奥义补一道横斩;绝技再补一道竖斩,收成十字
                SlashAtAngle(target.position, size * 1.4f, SteelEdge, 0f, 1f);
                Ring(target, _tierColor);
                ScreenFlash(0.16f, new Color(1f, 0.92f, 0.78f));
                FreezeFrame(TierFreeze[(int)_tier]);
                PlayClip(_hitClip, 0.8f, 0.8f);
                if (_tier == CastTier.Ultimate)
                {
                    yield return Beat(0.2f);
                    if (target == null) yield break;
                    SlashAtAngle(target.position, size * 1.5f, new Color(1f, 0.89f, 0.89f), 90f, 1f);
                    Ring(target, _tierColor);
                    ScreenFlash(0.2f, new Color(1f, 0.85f, 0.85f));
                    FreezeFrame(TierFreeze[(int)_tier]);
                    PlayClip(_thudClip, 0.9f, 0.7f);
                }
            }
            else
            {
                // 其余招式:按档位叠冲击环(奥义两圈、绝技三圈);火系绝技再落一场火雨
                int rings = _tier == CastTier.Ultimate ? 3 : 2;
                for (int i = 0; i < rings; i++)
                {
                    if (target == null) yield break;
                    RingAt(target.position, i == rings - 1 ? _tierColor : FireCore);
                    yield return Beat(0.09f);
                }
                ScreenFlash(0.16f, new Color(1f, 0.9f, 0.75f));
                bool fire = style == CastStyle.Fireball || style == CastStyle.FireWave || style == CastStyle.Blast;
                if (_tier == CastTier.Ultimate && fire) FireRain();
            }
            yield return Beat(0.15f);
        }

        /// <summary>火雨:从战场顶上往下洒一片火星。</summary>
        private void FireRain()
        {
            var area = _shakeTarget.rect;
            for (int i = 0; i < 22; i++)
            {
                Vector2 at = new Vector2(Random.Range(area.xMin + area.width * 0.1f, area.xMax - area.width * 0.1f), area.yMax + 10f);
                var rect = Bit(null, new Vector2(8f, 13f), i % 2 == 0 ? FireCore : FireDeep, at, out var image, circle: true);
                StartCoroutine(FallAndFade(rect, image, at, Random.Range(area.height * 0.35f, area.height * 0.7f), Random.Range(0f, 0.25f)));
            }
        }

        private IEnumerator FallAndFade(RectTransform rect, Image image, Vector2 at, float drop, float delay)
        {
            if (delay > 0f) yield return Beat(delay);
            Color c = image.color;
            yield return Tween(0.42f, k =>
            {
                if (rect == null) return;
                rect.localPosition = at + new Vector2(-12f * k, -drop * k * k);
                image.color = new Color(c.r, c.g, c.b, 1f - k * k);
            });
            if (rect != null) Destroy(rect.gameObject);
        }

        // ---- 奥义:火球类分身夹击 ----

        /// <summary>左右两颗小一号的火球从两侧绕弧飞来,与主火球同时到。</summary>
        private void CompanionOrbs(Vector2 from, Vector2 to, float size, float duration)
        {
            var area = _shakeTarget.rect;
            foreach (float side in new[] { -1f, 1f })
            {
                Vector2 start = new Vector2(from.x + side * area.width * 0.3f, from.y + area.height * 0.05f);
                var orb = Bit(null, new Vector2(size * 0.7f, size * 0.7f), new Color(1f, 0.7f, 0.28f), start, out _, circle: true);
                StartCoroutine(CompanionFly(orb, start, to, side, duration));
            }
        }

        private IEnumerator CompanionFly(RectTransform orb, Vector2 start, Vector2 to, float side, float duration)
        {
            float bulge = (to - start).magnitude * 0.25f;
            yield return Tween(duration, k =>
            {
                if (orb == null) return;
                orb.localPosition = Vector2.Lerp(start, to, k * k) + new Vector2(side * Mathf.Sin(k * Mathf.PI) * 30f, Mathf.Sin(k * Mathf.PI) * bulge);
            });
            if (orb != null) Destroy(orb.gameObject);
        }
    }
}
