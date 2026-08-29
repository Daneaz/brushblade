using System.Collections.Generic;
using Brushblade.Core;
using UnityEngine;

namespace Brushblade.Presentation
{
    /// <summary>宝箱立绘的查找与加载(<c>docs/design/ui/scenes/Chests.dc.html</c>)。
    /// 与 <see cref="MobAssets"/> 同构:前缀表 + 分层 + 缓存,取不到就返回 null 让调用方回落。
    ///
    /// 七只箱是**七种材质**不是七种颜色 —— 此前格子里画的是一个 <c>Theme.ChestColor</c> 色块
    /// 加「素」「竹」这样的首字,七档只有色相之差,而它们的等待时长差着 5 分钟到 12 小时。
    ///
    /// 只两层(箱子是器物不是活物,不需要 mob 那套 face/wisp):
    /// <list type="bullet">
    /// <item><c>body</c> —— 底图,三态共用。计时中由调用方压到 45%,一张图两用</item>
    /// <item><c>seam</c> —— 盖缝透出来的光,只有「已就绪」点亮;缝的位置各档不同故逐档出</item>
    /// </list>
    /// 另有两张与箱型无关的叠加层,套在任何一只箱上都成立:<c>chest_fx_ready</c> 与
    /// <c>chest_fx_timing</c>。
    ///
    /// ⚠ 素材由 <c>tools/design/build_chests.py</c> 出,slug 表与那边的 TIERS 必须逐个对应
    /// (<c>tools/design/tests/test_chests.py</c> 守着这条)。</summary>
    public static class ChestAssets
    {
        /// <summary>档位 → 资产 slug。顺序 = <see cref="ChestTier"/> 的 1~7。</summary>
        private static readonly Dictionary<ChestTier, string> Slugs = new()
        {
            { ChestTier.Paper, "paper" },
            { ChestTier.Bamboo, "bamboo" },
            { ChestTier.Celadon, "celadon" },
            { ChestTier.Rosewood, "rosewood" },
            { ChestTier.Gilded, "gilded" },
            { ChestTier.Vermilion, "vermilion" },
            { ChestTier.Crimson, "crimson" },
        };

        private static readonly Dictionary<string, Sprite> Cache = new();

        /// <summary>取一层的 Sprite;资产不存在返回 null(调用方回落到色块 + 首字)。
        /// 走 Texture2D + Sprite.Create 而不是 Resources.Load&lt;Sprite&gt; —— 后者依赖 PNG 的
        /// textureType 导入设置,没写对 .meta 时会取不到(同 MobAssets / Icons / CardFrames)。</summary>
        public static Sprite Layer(ChestTier tier, string layer)
        {
            if (!Slugs.TryGetValue(tier, out var slug)) return null;
            return Load($"chest_{slug}_{layer}");
        }

        /// <summary>与箱型无关的叠加层:<c>fx_ready</c>(金光晕)/ <c>fx_timing</c>(沙漏角标)。</summary>
        public static Sprite Effect(string key) => Load("chest_" + key);

        /// <summary>这一档有没有立绘(至少有主体层)。</summary>
        public static bool Has(ChestTier tier) => Layer(tier, "body") != null;

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
    }
}
