using System.Collections.Generic;
using UnityEngine;

namespace Brushblade.Presentation
{
    /// <summary>召唤物形象 + 召唤物攻击动效贴图的查找与加载。
    /// 资产由 tools/design/build_summons.py 生成;召唤字 → slug 这张表与脚本的 SLUGS 逐条相同
    /// (tools/design/tests/test_summon_assets.py 守着,改一处漏一处会红)。</summary>
    public static class SummonAssets
    {
        /// <summary>层序 = 叠放次序(先画的在下)。body 墨枝不着色,leaf 白图运行时按属性着色。</summary>
        public static readonly string[] Layers = { "body", "leaf" };

        // 键 = 字表 Summon 效果的 summonChar。U+E625 是 PUA 四叠木(见 subset_fonts.py 的 STACKED)
        private static readonly Dictionary<string, string> Slugs = new()
        {
            { "林", "lin" },
            { "森", "sen" },
            { "", "simu" },
            { "箭", "jian" },
            { "楸", "qiu" },
            { "荆", "jing" },
            { "藻", "zao" },
            { "桂", "gui" },
            { "柘", "zhe" },
            { "藤", "teng" },
        };

        /// <summary>该召唤字的资产前缀;没有形象返回 null —— 调用方回落到纯字牌格。</summary>
        public static string PrefixFor(string summonChar) =>
            summonChar != null && Slugs.TryGetValue(summonChar, out var slug) ? "summon_" + slug : null;

        /// <summary>取一层;资产不存在返回 null。</summary>
        public static Sprite Layer(string prefix, string layer) =>
            string.IsNullOrEmpty(prefix) ? null : Load(prefix + "_" + layer);

        /// <summary>攻击动效贴图:slash / arrow / leaf。取不到返回 null,Juice 回落到圆角色块。</summary>
        public static Sprite Fx(string name) => Load("summon_fx_" + name);

        private static readonly Dictionary<string, Sprite> Cache = new();

        // 与 MobAssets.Layer 同一条理由:走 Texture2D + Sprite.Create,不依赖 PNG 的 textureType
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
