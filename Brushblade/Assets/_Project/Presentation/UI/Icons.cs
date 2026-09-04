using System.Collections.Generic;
using UnityEngine;

namespace Brushblade.Presentation
{
    /// <summary>状态图标(2026-08-17,每单位行动条 spec §5)。战斗界面的状态类 chip
    /// 用它替掉文字,把宽度让给新增的行动条。
    ///
    /// **双轨设计**:PNG 取不到时回落成汉字徽章,信息一点不丢。这既是资产缺失的兜底
    /// (<see cref="WuxingChart"/> 的注释警告过 Resources.Load 取不到会静默变空),
    /// 也是「先接结构、美术后补」的过渡态 —— 换 PNG 不需要动一行 C#。
    ///
    /// ⚠ 兜底字必须进字体子集。subset_fonts.py 扫 .cs 的字符串字面量,写在这里就自动收进去,
    /// 但**改完必须重跑一次** `python3 tools/fonts/subset_fonts.py`,否则上线渲染成空框。</summary>
    public static class Icons
    {
        /// <summary>图标在 chip 里的边长。</summary>
        public const float Size = 18f;

        /// <summary>图标与其后数值之间的间距。</summary>
        public const float Gap = 3f;

        /// <summary>key → 兜底汉字。与 tools/icons/build_icons.py 的 ICONS 一一对应,
        /// 两边任一多一个少一个都是上线空白 —— test_icons.py 守着这条。</summary>
        private static readonly Dictionary<string, string> Glyphs = new()
        {
            // 敌方 7
            { "burn", "炎" },
            { "burn_nodecay", "灭" },
            { "freeze", "冰" },
            { "slow", "缓" },
            { "blind", "盲" },
            { "silence", "默" },
            { "curse", "咒" },
            // 玩家 10
            { "seal", "封" },
            { "immunity", "免" },
            { "reflect", "弹" },
            { "attack", "攻" },
            { "morale", "意" },
            { "crit", "暴" },
            { "pierce", "锐" },
            { "defense", "甲" },
            { "dodge", "闪" },
            { "speed", "速" },
            // 护盾(2026-08-26):玩家与召唤物的盾条都用它。与 defense(护甲点数,实心盾)
            // 是两码事 —— 那个是常驻减伤,这个是会被打空的一层临时血
            { "shield", "盾" },
            // 主界面底部导航 4(2026-08-28):PNG 缺失时页签**不画图标**(名字本来就在旁边,
            // 补一个字反而挤),这几条兜底字只为守住「两张表一一对应」那条测试。
            { "nav_deck", "牌" },
            { "nav_bestiary", "册" },
            { "nav_perks", "术" },
            { "nav_shop", "市" },
            // 战斗稿补齐的 11 枚(2026-08-30)
            { "armorbreak", "破" },
            { "bleed", "血" },
            { "scorch", "烫" },
            { "sear", "燎" },
            { "split", "裂" },
            { "obscure", "隐" },
            { "thorns", "刺" },
            { "ranged", "远" },
            { "melee", "近" },
            { "focus", "盯" },
            { "sweep", "扫" },
            { "skewer", "贯" },
            // 第 30 枚(2026-08-30):持续治疗
            { "heal", "愈" },
        };

        private static readonly Dictionary<string, Sprite> Cache = new();

        /// <summary>取图标 Sprite;资产不存在返回 null(调用方转用 <see cref="Fallback"/>)。
        /// 走 Texture2D + Sprite.Create 而不是 Resources.Load&lt;Sprite&gt; —— 后者依赖 PNG 的
        /// textureType 导入设置,没写对 .meta 时会取不到(同 MobAssets / CardFrames)。</summary>
        public static Sprite Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (Cache.TryGetValue(key, out var cached)) return cached;

            var texture = Resources.Load<Texture2D>("icon_" + key);
            var sprite = texture == null
                ? null
                : Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f), 100f);
            Cache[key] = sprite;
            return sprite;
        }

        /// <summary>该 key 的兜底汉字。未知 key 返回「?」而不是空串 ——
        /// 空串会画出一个看不见的 chip,「?」至少让人知道这里漏配了。</summary>
        public static string Fallback(string key) =>
            key != null && Glyphs.TryGetValue(key, out var glyph) ? glyph : "?";
    }
}
