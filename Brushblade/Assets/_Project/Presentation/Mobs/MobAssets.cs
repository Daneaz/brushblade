using System.Collections.Generic;
using Brushblade.Core;
using UnityEngine;

namespace Brushblade.Presentation
{
    /// <summary>字怪形象资产的查找与加载(《敌人形象关键词包》§2 分层规范)。
    /// 战斗代码里 EnemyDef.Id 是中文,资产名是拼音——这里是唯一的对照表,
    /// 与 tools/design/rasterize_mobs.py 的同名表保持一致。</summary>
    public static class MobAssets
    {
        /// <summary>层序 = 叠放次序(先画的在下)。属性气场并在 wisp 层里,没有独立 aura 层。</summary>
        public static readonly string[] Layers = { "body", "face", "wisp" };

        private static readonly Dictionary<string, string> MinionSlugs = new()
        {
            { "错字鬼", "cuozigui" },
            { "缺笔妖", "quebiyao" },
            { "标点小妖", "biaodianxiaoyao" },
            { "叠字怪", "dieziguai" },
            { "夯土妖", "hangtuyao" },
            { "通假字", "tongjiazi" },
            { "生僻字", "shengpizi" },
            { "墨渍", "mozi" },
            { "焦痕", "jiaohen" },
        };

        /// <summary>Boss 形象按阶段出:四个阶段是四套图。「倒」「海」复用排山倒海的稿。</summary>
        private static readonly Dictionary<string, string[]> BossStages = new()
        {
            { "排山倒海", new[] { "boss_paishandaohai_1pai", "boss_paishandaohai_2shan",
                                  "boss_paishandaohai_3dao", "boss_paishandaohai_4hai" } },
            { "翻江倒海", new[] { "boss_fanjiangdaohai_1fan", "boss_fanjiangdaohai_2jiang",
                                  "boss_paishandaohai_3dao", "boss_paishandaohai_4hai" } },
            { "雷霆万钧", new[] { "boss_leitingwanjun_1lei", "boss_leitingwanjun_2ting",
                                  "boss_leitingwanjun_3wan", "boss_leitingwanjun_4jun" } },
        };

        /// <summary>该怪(该阶段)的资产前缀;没有对应形象返回 null —— 调用方回落到字牌格。</summary>
        public static string PrefixFor(EnemyDef def, int phaseIndex = 0)
        {
            if (def == null) return null;
            if (def.Phases.Count > 0)
            {
                if (!BossStages.TryGetValue(def.Id, out var stages)) return null;
                return stages[Mathf.Clamp(phaseIndex, 0, stages.Length - 1)];
            }
            return MinionSlugs.TryGetValue(def.Id, out var slug) ? "enemy_" + slug : null;
        }

        private static readonly Dictionary<string, Sprite> Cache = new();

        /// <summary>取一层的 Sprite;资产不存在返回 null。
        /// 走 Texture2D + Sprite.Create 而不是 Resources.Load&lt;Sprite&gt; ——
        /// 后者依赖 PNG 的导入设置(textureType 必须是 Sprite),没写 .meta 时会取不到。</summary>
        public static Sprite Layer(string prefix, string layer)
        {
            if (string.IsNullOrEmpty(prefix)) return null;
            string key = prefix + "_" + layer;
            if (Cache.TryGetValue(key, out var cached)) return cached;

            var texture = Resources.Load<Texture2D>(key);
            var sprite = texture == null
                ? null
                : Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f), 100f);
            Cache[key] = sprite;
            return sprite;
        }

        /// <summary>这只怪有没有形象资产(至少有主体层)。</summary>
        public static bool Has(EnemyDef def, int phaseIndex = 0) =>
            Layer(PrefixFor(def, phaseIndex), "body") != null;
    }
}
