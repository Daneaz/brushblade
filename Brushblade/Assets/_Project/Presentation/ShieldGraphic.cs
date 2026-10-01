using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>一面骑士盾(2026-09-30,柘「举盾」用;用户报一版的盾「看着就是一个椭圆体」)。
    /// 自己出网格:平顶、两侧弧线收成尖底的盾形,分四层 —— 深铜外缘 / 上亮下暗的金色盾面 /
    /// 一竖一横两道深金纹带 / 盾心铜钉。<see cref="Flash"/> 0–1 把整面往白里推(挨打那一下)。
    ///
    /// 轮廓是凸的,所以每层从中心扇形三角化即可。</summary>
    public sealed class ShieldGraphic : MaskableGraphic
    {
        private static readonly Color Rim = new(0.42f, 0.29f, 0.12f);
        private static readonly Color FaceTop = new(0.98f, 0.9f, 0.62f);
        private static readonly Color FaceBottom = new(0.72f, 0.54f, 0.2f);
        private static readonly Color Band = new(0.6f, 0.43f, 0.15f);
        private static readonly Color Boss = new(0.5f, 0.34f, 0.13f);

        private float _flash;
        public float Flash
        {
            get => _flash;
            set { _flash = Mathf.Clamp01(value); SetVerticesDirty(); }
        }

        private static List<Vector2> _outline;

        /// <summary>单位盾形(x、y ∈ [-0.5, 0.5]):平顶带一点外鼓,两侧从肩部弧线收到底尖。</summary>
        private static List<Vector2> Outline()
        {
            if (_outline != null) return _outline;
            var pts = new List<Vector2>();
            const int arc = 12;
            for (int i = 0; i <= 6; i++) pts.Add(new Vector2(-0.5f + i / 6f, 0.5f + 0.03f * Mathf.Sin(i / 6f * Mathf.PI)));
            for (int i = 1; i <= arc; i++)   // 右侧:肩 (0.5, 0.12) → 底尖 (0, -0.5)
            {
                float t = i / (float)arc, u = 1f - t;
                Vector2 p0 = new(0.5f, 0.12f), c = new(0.47f, -0.3f), p1 = new(0f, -0.5f);
                pts.Add(u * u * p0 + 2f * u * t * c + t * t * p1);
            }
            for (int i = arc - 1; i >= 1; i--)   // 左侧镜像
            {
                float t = i / (float)arc, u = 1f - t;
                Vector2 p0 = new(-0.5f, 0.12f), c = new(-0.47f, -0.3f), p1 = new(0f, -0.5f);
                pts.Add(u * u * p0 + 2f * u * t * c + t * t * p1);
            }
            pts.Add(new Vector2(-0.5f, 0.12f));
            _outline = pts;
            return pts;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = rectTransform.rect;
            Vector2 size = r.size, center = r.center;
            Vector2 P(Vector2 unit, float scale, float lift = 0f) =>
                center + new Vector2(unit.x * size.x * scale, (unit.y * scale + lift) * size.y);

            var outline = Outline();
            Color Tint(Color c) => Color.Lerp(c, Color.white, _flash) * new Color(1f, 1f, 1f, color.a);

            // 外缘
            Fan(vh, outline, u => P(u, 1f), _ => Tint(Rim));
            // 盾面:内缩一圈,上亮下暗
            Fan(vh, outline, u => P(u, 0.86f, 0.01f), u => Tint(Color.Lerp(FaceBottom, FaceTop, u.y + 0.5f)));
            // 纹带:一竖一横
            Quad(vh, center + new Vector2(-size.x * 0.06f, -size.y * 0.3f), center + new Vector2(size.x * 0.06f, size.y * 0.4f), Tint(Band));
            Quad(vh, center + new Vector2(-size.x * 0.38f, size.y * 0.1f), center + new Vector2(size.x * 0.38f, size.y * 0.21f), Tint(Band));
            // 盾心铜钉:八边形
            var boss = new List<Vector2>();
            for (int i = 0; i < 8; i++)
            {
                float a = (i + 0.5f) / 8f * Mathf.PI * 2f;
                boss.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 0.11f);
            }
            Vector2 bossAt = center + new Vector2(0f, size.y * 0.155f);
            Fan(vh, boss, u => bossAt + new Vector2(u.x * size.x, u.y * size.x), _ => Tint(Boss));
            Fan(vh, boss, u => bossAt + new Vector2(u.x * size.x * 0.5f, u.y * size.x * 0.5f) + new Vector2(-size.x * 0.015f, size.x * 0.015f),
                _ => Tint(Color.Lerp(Boss, FaceTop, 0.6f)));
        }

        private static void Fan(VertexHelper vh, List<Vector2> poly, System.Func<Vector2, Vector2> place, System.Func<Vector2, Color> tint)
        {
            Vector2 c = Vector2.zero;
            foreach (var p in poly) c += p;
            c /= poly.Count;
            int start = vh.currentVertCount;
            vh.AddVert(place(c), tint(c), Vector2.zero);
            foreach (var p in poly) vh.AddVert(place(p), tint(p), Vector2.zero);
            for (int i = 0; i < poly.Count; i++)
                vh.AddTriangle(start, start + 1 + i, start + 1 + (i + 1) % poly.Count);
        }

        private static void Quad(VertexHelper vh, Vector2 min, Vector2 max, Color c)
        {
            int i = vh.currentVertCount;
            vh.AddVert(new Vector2(min.x, min.y), c, Vector2.zero);
            vh.AddVert(new Vector2(min.x, max.y), c, Vector2.zero);
            vh.AddVert(new Vector2(max.x, max.y), c, Vector2.zero);
            vh.AddVert(new Vector2(max.x, min.y), c, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i, i + 2, i + 3);
        }
    }
}
