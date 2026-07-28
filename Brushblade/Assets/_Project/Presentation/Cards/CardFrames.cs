using System.Collections.Generic;
using Brushblade.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>字牌边框素材(《字牌形象关键词包》§2/§3):按稀有度分档的 9-slice 框。
    /// 缺档的稀有度回落到现有纯色圆角框,所以六套可以一级一级地上。</summary>
    public static class CardFrames
    {
        /// <summary>设计稿基准画布;9-slice 的 border 与 pixelsPerUnitMultiplier 都以此为准。</summary>
        public const float CanvasWidth = 192f;

        private static readonly Dictionary<CardRarity, string> Slugs = new()
        {
            { CardRarity.White, "white" },
            { CardRarity.Green, "green" },
            { CardRarity.Blue, "blue" },
            { CardRarity.Purple, "purple" },
            { CardRarity.Orange, "orange" },
            { CardRarity.Red, "red" },
        };

        /// <summary>各档的 9-slice border(左/下/右/上,基准画布像素)与内容区缩进(占牌宽比例)。
        /// border 要盖住圆角(rx=12)与边框装饰,否则拉伸会把四角拉花。
        /// border 必须盖住**四角的全部装饰**,不只是边框宽度 —— 紫檀的螺钿点圆心在 (12,13)、
        /// 半径 5.6,一直探到 17.6,border 给 13 会把它切在可拉伸区里,拉出来是扁的(实测)。
        /// 故紫檀取 19;白框角饰只是几根 0.8px 淡线,超出部分变形无感,取 14 够用。
        /// (2026-07-28 第二版设计稿:边框 12px = 6.25% 牌宽,木纹三层,螺钿反而放大。)</summary>
        private static readonly Dictionary<CardRarity, (Vector4 border, float insetX, float insetY)> Metrics = new()
        {
            { CardRarity.White, (new Vector4(14, 14, 14, 14), 0.045f, 0.040f) },
            { CardRarity.Purple, (new Vector4(19, 19, 19, 19), 0.078f, 0.070f) },
        };

        private static readonly Dictionary<string, Sprite> Cache = new();

        private static Sprite Load(string key, Vector4 border)
        {
            if (Cache.TryGetValue(key, out var cached)) return cached;
            var texture = Resources.Load<Texture2D>(key);
            var sprite = texture == null
                ? null
                : Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
            Cache[key] = sprite;
            return sprite;
        }

        /// <summary>该稀有度的框;没有素材返回 null(调用方回落纯色圆角)。</summary>
        public static Sprite Frame(CardRarity rarity)
        {
            if (!Slugs.TryGetValue(rarity, out var slug)) return null;
            var border = Metrics.TryGetValue(rarity, out var m) ? m.border : Vector4.zero;
            return Load($"card_{slug}_frame", border);
        }

        /// <summary>该稀有度的光效层;没有则 null(蓝级以下本就不该有)。</summary>
        public static Sprite Glow(CardRarity rarity)
        {
            if (!Slugs.TryGetValue(rarity, out var slug)) return null;
            var border = Metrics.TryGetValue(rarity, out var m) ? m.border : Vector4.zero;
            return Load($"card_{slug}_glow", border);
        }

        /// <summary>内容区(字/拼音/AP)相对整牌的缩进比例 —— 各档边框厚度不同,内容要跟着让位。</summary>
        public static (float x, float y) ContentInset(CardRarity rarity) =>
            Metrics.TryGetValue(rarity, out var m) ? (m.insetX, m.insetY) : (0.06f, 0.05f);

        /// <summary>9-slice 的边框是**固定像素**,不随牌缩放:border 24 在 192 宽时占 12.5%,
        /// 到 76 宽就占 31.6%,中心只剩 28px 连字都放不下。pixelsPerUnitMultiplier 正是解这个的 ——
        /// 按牌宽反比缩放边框绘制尺寸,使边框占比在所有尺寸下恒定。</summary>
        public static void FitBorder(Image image, float tileWidth)
        {
            if (image != null && tileWidth > 0f)
                image.pixelsPerUnitMultiplier = Mathf.Max(1f, CanvasWidth / tileWidth);
        }
    }
}
