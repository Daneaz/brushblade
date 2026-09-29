using UnityEngine;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>一笔墨迹(2026-09-30,近战「残影突进 + 笔锋拖尾」)。二次贝塞尔一条,起笔重、
    /// 收笔出锋;收笔段带几缕断续的毛丝(飞白)。<see cref="Progress"/> 0→1 决定已经落到哪儿,
    /// 由 Juice 逐帧推 —— 笔尖和残影走同一条缓动,看起来是残影拖着笔在走。
    ///
    /// 做成 Graphic 自己出网格而不是一串 Image 圆点:一笔要 80 段主干 + 7 缕毛丝,
    /// 用 GameObject 堆就是几百个物件,一次出手就要建一遍再销毁一遍。</summary>
    public sealed class BrushStroke : MaskableGraphic
    {
        private Vector2 _p0, _p1, _p2;
        private float _maxWidth;
        private float _progress;
        private float[] _bristleOffset;
        private bool[] _bristleGaps;   // [缕 × 段]:收笔段哪几段断开

        private const int Segments = 48;
        private const int Bristles = 7;

        /// <summary>端点都在本物件父节点的本地坐标里(Juice 挂在震屏层上)。</summary>
        public void Setup(Vector2 from, Vector2 control, Vector2 to, float maxWidth)
        {
            _p0 = from; _p1 = control; _p2 = to;
            _maxWidth = maxWidth;
            _bristleOffset = new float[Bristles];
            _bristleGaps = new bool[Bristles * (Segments + 1)];
            for (int b = 0; b < Bristles; b++)
            {
                _bristleOffset[b] = Random.Range(-1f, 1f);
                float keep = Random.Range(0.85f, 1f);
                for (int s = 0; s <= Segments; s++)
                    _bristleGaps[b * (Segments + 1) + s] = s > Segments * 0.55f && Random.value > keep;
            }
            raycastTarget = false;
            SetVerticesDirty();
        }

        public float Progress
        {
            get => _progress;
            set
            {
                float v = Mathf.Clamp01(value);
                if (Mathf.Approximately(v, _progress)) return;
                _progress = v;
                SetVerticesDirty();
            }
        }

        private Vector2 At(float t)
        {
            float u = 1f - t;
            return u * u * _p0 + 2f * u * t * _p1 + t * t * _p2;
        }

        /// <summary>笔宽:起笔 12% 内由 55% 压到满,之后一路收到 8% 出锋。</summary>
        private float WidthAt(float t) =>
            _maxWidth * (t < 0.12f ? 0.55f + t / 0.12f * 0.45f : 1f - (t - 0.12f) / 0.88f * 0.92f);

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            int last = Mathf.FloorToInt(_progress * Segments);
            if (last < 1 || _bristleOffset == null) return;

            var main = color;
            var hair = color; hair.a *= 0.6f;
            AddStrip(vh, last, 0f, 1f, main, null);
            for (int b = 0; b < Bristles; b++)
                AddStrip(vh, last, _bristleOffset[b] * 0.7f, 0.18f, hair, b);
        }

        /// <summary>沿曲线铺一条三角带。offset / widthScale 以笔宽为单位;bristle 非 null 时按断笔表跳段。</summary>
        private void AddStrip(VertexHelper vh, int last, float offset, float widthScale, Color c, int? bristle)
        {
            Vector2 prev = At(0f);
            for (int s = 1; s <= last; s++)
            {
                float t0 = (s - 1) / (float)Segments, t1 = s / (float)Segments;
                Vector2 a = prev, b = At(t1);
                prev = b;
                if (bristle.HasValue && _bristleGaps[bristle.Value * (Segments + 1) + s]) continue;
                Vector2 dir = b - a;
                if (dir.sqrMagnitude < 1e-6f) continue;
                Vector2 n = new Vector2(-dir.y, dir.x).normalized;
                float w0 = WidthAt(t0), w1 = WidthAt(t1);
                Vector2 c0 = a + n * (offset * w0), c1 = b + n * (offset * w1);
                float h0 = w0 * widthScale * 0.5f, h1 = w1 * widthScale * 0.5f;
                int i = vh.currentVertCount;
                vh.AddVert(c0 + n * h0, c, Vector2.zero);
                vh.AddVert(c0 - n * h0, c, Vector2.zero);
                vh.AddVert(c1 - n * h1, c, Vector2.zero);
                vh.AddVert(c1 + n * h1, c, Vector2.zero);
                vh.AddTriangle(i, i + 1, i + 2);
                vh.AddTriangle(i, i + 2, i + 3);
            }
        }
    }
}
