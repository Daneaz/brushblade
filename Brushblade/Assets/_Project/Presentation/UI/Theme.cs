using System.Collections.Generic;
using Brushblade.Core;
using UnityEngine;

namespace Brushblade.Presentation
{
    /// <summary>设计板主题(docs/design/字斗设计板.html):语义调色板 + 程序化 sprite + 双字体。
    /// 色值为设计板 oklch 预转 sRGB;View 层只准引用这里的语义色。</summary>
    public static class Theme
    {
        // ---- 调色板 ----
        public static readonly Color Paper = new(0.966f, 0.946f, 0.905f);       // 宣纸底
        public static readonly Color PaperDim = new(0.869f, 0.842f, 0.788f);    // 进度条底/分隔
        public static readonly Color Ink = new(0.066f, 0.087f, 0.122f);         // 墨黑(深底)
        public static readonly Color InkSoft = new(0.239f, 0.304f, 0.41f);      // 深灰蓝按钮
        public static readonly Color TextMain = new(0.088f, 0.105f, 0.132f);
        public static readonly Color TextDim = new(0.363f, 0.391f, 0.435f);
        public static readonly Color CardWhite = Color.white;
        public static readonly Color Cinnabar = new(0.772f, 0.211f, 0.215f);    // 朱砂
        public static readonly Color CinnabarDark = new(0.607f, 0.117f, 0.135f);
        public static readonly Color Jade = new(0.264f, 0.58f, 0.347f);         // 翠玉
        public static readonly Color Gold = new(0.791f, 0.617f, 0.199f);        // 赭金
        public static readonly Color GoldBorder = new(0.56f, 0.421f, 0.037f);
        public static readonly Color GoldText = new(0.251f, 0.161f, 0.0f);
        public static readonly Color AdGreen = new(0.232f, 0.586f, 0.332f);
        public static readonly Color AdGreenBg = new(0.892f, 0.955f, 0.901f);
        public static readonly Color AdGreenText = new(0.044f, 0.364f, 0.165f);
        public static readonly Color ExitPink = new(0.477f, 0.246f, 0.362f);
        public static readonly Color ShopNav = new(0.654f, 0.349f, 0.241f);
        public static readonly Color LockedBg = new(0.856f, 0.843f, 0.816f);
        public static readonly Color LockGray = new(0.534f, 0.563f, 0.611f);
        public static readonly Color DoneGreen = new(0.161f, 0.525f, 0.276f);
        public static readonly Color NeutralPart = new(0.309f, 0.336f, 0.379f); // 中性部件底
        public static readonly Color IngotDark = new(0.1f, 0.122f, 0.17f);      // 墨锭图标
        public static readonly Color IngotGold = new(0.615f, 0.481f, 0.166f);   // 金锭图标(价格)
        public static readonly Color SplitBlue = new(0.098f, 0.311f, 0.506f);   // 「拆」按钮
        public static readonly Color UpgradeText = new(0.107f, 0.333f, 0.173f);
        public static readonly Color Shadow = new(0.088f, 0.105f, 0.132f, 0.08f);
        public static readonly Color Scrim = new(0.088f, 0.105f, 0.132f, 0.55f);  // 模态遮罩

        // 层段背景基色(20.2 每段换景):字林竹绿/词渊黛蓝/文山赭石/墨海墨青
        private static readonly Color[] BandInks =
        {
            new(0.42f, 0.58f, 0.38f),
            new(0.36f, 0.48f, 0.64f),
            new(0.66f, 0.52f, 0.34f),
            new(0.28f, 0.32f, 0.42f),
        };

        private static Color BandInk(int bandIndex) =>
            BandInks[Mathf.Min(bandIndex, BandInks.Length - 1)];

        /// <summary>层段宣纸底:随层段换基色,同层段内逐段(每 5 层)加深——进新段有体感。</summary>
        public static Color BandPaper(int bandIndex, int segmentInBand) =>
            Color.Lerp(Paper, BandInk(bandIndex),
                Mathf.Min(0.22f, 0.09f + 0.035f * segmentInBand));

        /// <summary>薄宣纸卡(拆合台等浮层):半透白,透出层段染色自动同调,水印隐约可见。</summary>
        public static readonly Color PaperCard = new(1f, 0.995f, 0.975f, 0.62f);

        /// <summary>层段巨字水印色(背景大字,近乎透明的墨痕)。</summary>
        public static Color BandWatermark(int bandIndex)
        {
            var ink = BandInk(bandIndex) * 0.55f;
            ink.a = 0.10f;
            return ink;
        }

        public static Color RarityColor(CardRarity rarity) => rarity switch
        {
            CardRarity.Green => new Color(0.181f, 0.621f, 0.323f),
            CardRarity.Blue => new Color(0.06f, 0.455f, 0.771f),
            CardRarity.Purple => new Color(0.475f, 0.269f, 0.669f),
            CardRarity.Orange => new Color(0.883f, 0.473f, 0.106f),
            CardRarity.Red => new Color(0.802f, 0.151f, 0.181f),
            _ => new Color(0.632f, 0.62f, 0.594f), // 白
        };

        public static Color ElementColor(Element? element) => element switch
        {
            Element.Fire => new Color(0.772f, 0.211f, 0.215f),
            Element.Water => new Color(0.06f, 0.455f, 0.771f),
            Element.Wood => new Color(0.204f, 0.561f, 0.309f),
            Element.Earth => new Color(0.6f, 0.486f, 0.235f),
            Element.Metal => new Color(0.702f, 0.638f, 0.507f),
            Element.Heart => new Color(0.592f, 0.312f, 0.655f),
            _ => NeutralPart,
        };

        /// <summary>属性淡底(部件池方块)。</summary>
        public static Color ElementSoft(Element? element) => element switch
        {
            Element.Fire => new Color(0.995f, 0.823f, 0.806f),
            Element.Water => new Color(0.785f, 0.883f, 0.986f),
            Element.Wood => new Color(0.801f, 0.902f, 0.817f),
            Element.Earth => new Color(0.925f, 0.864f, 0.741f),
            Element.Metal => new Color(0.933f, 0.892f, 0.81f),
            Element.Heart => new Color(0.925f, 0.835f, 0.945f),
            _ => LockedBg,
        };

        public static Color ElementSoftFg(Element? element) => element switch
        {
            Element.Fire => new Color(0.525f, 0.151f, 0.149f),
            Element.Water => new Color(0.056f, 0.31f, 0.526f),
            Element.Wood => new Color(0.097f, 0.36f, 0.18f),
            Element.Earth => new Color(0.39f, 0.283f, 0.0f),
            Element.Metal => new Color(0.394f, 0.324f, 0.174f),
            Element.Heart => new Color(0.403f, 0.212f, 0.446f),
            _ => TextDim,
        };

        // ---- 字体 ----
        private static Font _title, _body;

        /// <summary>思源宋体子集:标题/字牌大字/怪物字。缺资源时回退 Ui.Font。</summary>
        public static Font TitleFont => _title ??= Resources.Load<Font>("NotoSerifSC-Subset") ?? Ui.Font;

        /// <summary>思源黑体子集:按钮/正文。</summary>
        public static Font BodyFont => _body ??= Resources.Load<Font>("NotoSansSC-Subset") ?? Ui.Font;

        // ---- 程序化 sprite(生成一次,静态缓存) ----
        private static readonly Dictionary<int, Sprite> _rounded = new();
        private static Sprite _circle, _ingot, _triangle;

        /// <summary>圆角矩形 9-slice。radius 按设计板:卡 20 / 牌 14 / 按钮 10 / 胶囊 24。</summary>
        public static Sprite Rounded(int radius)
        {
            if (_rounded.TryGetValue(radius, out var cached)) return cached;
            int size = radius * 2 + 8;
            var tex = NewTex(size, size);
            float r = radius;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    // 到圆角矩形边界的有符号距离(圆角圆心为四角内缩 r)
                    float dx = Mathf.Max(0, Mathf.Max(r - x - 0.5f, x + 0.5f - (size - r)));
                    float dy = Mathf.Max(0, Mathf.Max(r - y - 0.5f, y + 0.5f - (size - r)));
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    tex.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(r - dist + 0.5f)));
                }
            tex.Apply();
            var border = radius + 2;
            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f),
                100, 0, SpriteMeshType.FullRect, new Vector4(border, border, border, border));
            _rounded[radius] = sprite;
            return sprite;
        }

        public static Sprite Circle
        {
            get
            {
                if (_circle != null) return _circle;
                const int size = 64;
                var tex = NewTex(size, size);
                const float r = size / 2f - 1f;
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f),
                            new Vector2(size / 2f, size / 2f));
                        tex.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(r - dist + 0.5f)));
                    }
                tex.Apply();
                _circle = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
                return _circle;
            }
        }

        /// <summary>墨锭六边形(设计板 clip-path: 14%,0 86%,0 100%,50% 86%,100% 14%,100% 0,50%)。</summary>
        public static Sprite Ingot => _ingot ??= Convex(56, 34, new[]
        {
            new Vector2(0.14f, 0f), new Vector2(0.86f, 0f), new Vector2(1f, 0.5f),
            new Vector2(0.86f, 1f), new Vector2(0.14f, 1f), new Vector2(0f, 0.5f),
        });

        /// <summary>播放三角(广告位标)。</summary>
        public static Sprite Triangle => _triangle ??= Convex(24, 28, new[]
        {
            new Vector2(0f, 0f), new Vector2(1f, 0.5f), new Vector2(0f, 1f),
        });

        private static Sprite Convex(int w, int h, Vector2[] points)
        {
            var tex = NewTex(w, h);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var p = new Vector2((x + 0.5f) / w, (y + 0.5f) / h);
                    float minEdge = float.MaxValue;
                    for (int i = 0; i < points.Length; i++)
                    {
                        var a = points[i];
                        var b = points[(i + 1) % points.Length];
                        var edge = b - a;
                        // 逆时针多边形:内侧为左侧;像素级 AA 用法线距离
                        float cross = edge.x * (p.y - a.y) - edge.y * (p.x - a.x);
                        float dist = cross / edge.magnitude * h; // 像素近似
                        minEdge = Mathf.Min(minEdge, dist);
                    }
                    tex.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(minEdge + 0.5f)));
                }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f));
        }

        private static Texture2D NewTex(int w, int h) =>
            new(w, h, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave,
            };
    }
}
