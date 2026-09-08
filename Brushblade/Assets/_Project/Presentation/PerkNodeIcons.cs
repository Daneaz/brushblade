using Brushblade.Core;

namespace Brushblade.Presentation
{
    /// <summary>节点 → 图标 key(spec 2026-09-08 §7)。15 枚撑起 43 个节点。
    ///
    /// **五行 L1/L2/L3 三层跨五系共用同一枚** —— 图形表达「效果类型」,颜色表达「哪一系」。
    /// 这正是 build_icons.py 开头写的约定:「图形一律白色,底色由 C# 侧上色 —— 一张图跨底色复用」。
    /// 五个系的 L1 长得一样但五种颜色,玩家一眼看出「这是五个系各自的供给节点」。
    ///
    /// 跨树节点用它**所放大的那个效果**的图标(外圈虚线环由 PerkView 画,不进图标管线)——
    /// 图标本身就说明了它放大什么。
    ///
    /// 单独一个文件:眼下唯一的调用点是 PerkView(画布上的节点面),详情面板用的是
    /// 「枝主色浅底 + 代表字水印」那一套、不取图标。留在自己的文件里而不是并进 PerkView,
    /// 是因为它是一张**表**(43 个节点 → 15 枚图标),与那边的画法/手势/布局不是一类东西;
    /// 哪天详情面板要用同一枚图标,直接调 <see cref="KeyFor"/> 即可,不必先做一次搬家。</summary>
    public static class PerkNodeIcons
    {
        public static string KeyFor(PerkNodeDef def)
        {
            if (def.Tree == PerkTree.Cross)
                return def.Branch switch
                {
                    "xvigor" => "attack",
                    "xedge" => "crit",
                    "xdraw" => "perk_draw",
                    _ => "perk_amplify",
                };

            if (def.Tree == PerkTree.Wuxing)
                return def.Depth switch
                {
                    1 => "perk_draw",       // 该系起手格抽取次数 +1
                    2 => "perk_loot",       // 战利品保底 1 张该系
                    3 => "perk_amplify",    // 该系字效果值 +15%
                    _ => def.Branch switch  // L4:各系专属天花板,各不相同
                    {
                        "metal" => "morale",
                        "wood" => "speed",
                        "water" => "perk_wellspring",
                        "fire" => "burn",
                        "earth" => "perk_heft",
                        _ => "perk_amplify",
                    },
                };

            // 被动树与机制树:一枝一枚,层间共用
            return def.Branch switch
            {
                "vigor" => "perk_hp",
                "power" => "attack",
                "edge" => "crit",
                "guard" => "defense",
                "lore" => "perk_library",
                "wide" => "perk_hand",
                "insight" => "perk_draw",
                "qi" => "perk_ap",
                _ => "perk_amplify",
            };
        }
    }
}
