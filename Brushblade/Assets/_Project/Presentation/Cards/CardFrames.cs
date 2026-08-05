using System.Collections.Generic;
using Brushblade.Core;
using UnityEngine;

namespace Brushblade.Presentation
{
    /// <summary>字牌边框素材(《字牌形象关键词包》§2/§3):七档稀有度各一张框(2026-08-04 接入金卡素材后
    /// 由六档增至七档)+ 蓝级以上一张光效层。素材画布 192×240(0.8 竖版),牌面按同比例整体缩放,**不做 9-slice**。</summary>
    public static class CardFrames
    {
        /// <summary>设计稿基准画布,内容缩进按此换算成比例。</summary>
        public const float CanvasWidth = 192f;
        public const float CanvasHeight = 240f;

        /// <summary>稀有度 → 边框素材 slug(与 Theme.RarityColor / CharInfo.RarityName 同一套):
        /// 枚举名 = 素材名 = 视觉层级 白→绿→蓝→紫→金→橙→红。</summary>
        private static readonly Dictionary<CardRarity, string> Slugs = new()
        {
            { CardRarity.White, "white" },
            { CardRarity.Green, "green" },
            { CardRarity.Blue, "blue" },
            { CardRarity.Purple, "purple" },
            { CardRarity.Gold, "gold" },
            { CardRarity.Orange, "orange" },
            { CardRarity.Red, "red" },
        };

        /// <summary>各档内容区距牌边的净空(基准画布像素)—— 取该档最内圈描边再留一点余量。
        /// 素纸/竹青的框只是两三条细线,朱漆的暗地带 + 双金线足足吃掉 19px,内容不让位就压到框上。</summary>
        private static readonly Dictionary<CardRarity, float> InsetPx = new()
        {
            { CardRarity.White, 15f },   // 最内细线 12.5
            { CardRarity.Green, 14f },   // 最内细线 11.5
            { CardRarity.Blue, 17f },    // 最内细线 14.6
            { CardRarity.Purple, 15f },  // 木框内窗 12
            { CardRarity.Gold, 19f },    // card_gold_frame:最内朱红线 16
            { CardRarity.Orange, 19f },  // card_orange_frame:最内细线 16
            { CardRarity.Red, 22f },     // card_red_frame:最内金线 19.2
        };

        private static readonly Dictionary<string, Sprite> Cache = new();

        private static Sprite Load(string key)
        {
            if (Cache.TryGetValue(key, out var cached)) return cached;
            var texture = Resources.Load<Texture2D>(key);
            var sprite = texture == null
                ? null
                : Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f), 100f);
            Cache[key] = sprite;
            return sprite;
        }

        /// <summary>该稀有度的框;没有素材返回 null(调用方回落纯色圆角)。</summary>
        public static Sprite Frame(CardRarity rarity) =>
            Slugs.TryGetValue(rarity, out var slug) ? Load($"card_{slug}_frame") : null;

        /// <summary>该稀有度的光效层;没有则 null(素纸/竹青本就不该有)。</summary>
        public static Sprite Glow(CardRarity rarity) =>
            Slugs.TryGetValue(rarity, out var slug) ? Load($"card_{slug}_glow") : null;

        private static readonly Dictionary<Element, string> ElementSlugs = new()
        {
            { Core.Element.Fire, "fire" },
            { Core.Element.Water, "water" },
            { Core.Element.Wood, "wood" },
            { Core.Element.Metal, "metal" },
            { Core.Element.Earth, "earth" },
            { Core.Element.Heart, "heart" },
        };

        /// <summary>六系属性动效元件(§4.3):墨色画的,运行时按属性色染。无元件的系不跑属性动效。</summary>
        public static Sprite Element(Element? element) =>
            element.HasValue && ElementSlugs.TryGetValue(element.Value, out var slug)
                ? Load($"elem_{slug}")
                : null;

        /// <summary>内容区(字/拼音/AP)相对整牌的缩进比例。</summary>
        public static (float x, float y) ContentInset(CardRarity rarity)
        {
            float px = InsetPx.TryGetValue(rarity, out var v) ? v : 12f;
            return (px / CanvasWidth, px / CanvasHeight);
        }
    }
}
