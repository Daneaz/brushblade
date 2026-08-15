using System.Collections.Generic;

namespace Brushblade.Core
{
    /// <summary>部件在字中所处的位置(设计板称"位形")。同源变体靠它区分形象。</summary>
    public enum ComponentPosition
    {
        None,   // 不在五系清单里
        Left,
        Right,
        Top,
        Bottom,
        Whole,  // 能独立成字的形态
    }

    /// <summary>五系部件等价清单(spec 2026-08-15 §1.1):同系部件在**配方匹配**上可互相替代。
    ///
    /// ⚠ 清单是**显式**的,不许改成从 <see cref="CharDef.Element"/> 推导 —— `禾` 的 element
    /// 同样是 Wood,推导会让 `木` 顶掉 `利 = 禾+刂` 里的 `禾`,把形声部件当五行部件用。
    /// 守卫测试:ComponentKinTests.TryGetGroup_RejectsPartsOutsideTheList。
    ///
    /// 每组首位是代表字(UI 徽标用);组内顺序即等价匹配的取用顺序。
    /// 本表只管匹配,**不改变拆字产出** —— 变体各自并存,见 spec §1.2。</summary>
    public static class ComponentKin
    {
        private static readonly string[][] Groups =
        {
            new[] { "水", "氵", "冫" },
            new[] { "木", "艹" },
            new[] { "金", "钅", "戈", "刂" },
            new[] { "土", "山", "石" },
            new[] { "火", "灬" },
        };

        /// <summary>五系清单里的全部 14 个成员(展开 <see cref="Groups"/>,守卫测试用:
        /// ComponentKinTests.RealConfig_AllMembersAreLeavesInTheRealCharTable ——
        /// 「部件等价」与「宝箱前置」互不干扰的唯一支点是这 14 个字在真实字表里全都没有配方。</summary>
        public static IReadOnlyList<string> AllParts
        {
            get
            {
                var all = new List<string>();
                foreach (var group in Groups)
                    all.AddRange(group);
                return all;
            }
        }

        public static bool TryGetGroup(string part, out IReadOnlyList<string> group)
        {
            foreach (var candidate in Groups)
            {
                foreach (var member in candidate)
                {
                    if (member != part) continue;
                    group = candidate;
                    return true;
                }
            }
            group = null;
            return false;
        }

        /// <summary>两个部件是否同组(同一个部件与自身也算)。</summary>
        public static bool AreKin(string a, string b)
        {
            if (a == b) return true;
            if (!TryGetGroup(a, out var group)) return false;
            foreach (var member in group)
                if (member == b) return true;
            return false;
        }

        /// <summary>位形表(spec §1.6,表现层数据,设计师可调)。
        /// 能独立成字的一律 Whole;火 例外取 Left —— 跟随设计板,与 灬 的 Bottom 形成对照。</summary>
        private static readonly Dictionary<string, ComponentPosition> Positions = new()
        {
            ["水"] = ComponentPosition.Whole,  ["氵"] = ComponentPosition.Left,
            ["冫"] = ComponentPosition.Left,   ["木"] = ComponentPosition.Whole,
            ["艹"] = ComponentPosition.Top,    ["金"] = ComponentPosition.Whole,
            ["钅"] = ComponentPosition.Left,   ["戈"] = ComponentPosition.Right,
            ["刂"] = ComponentPosition.Right,  ["土"] = ComponentPosition.Whole,
            ["山"] = ComponentPosition.Whole,  ["石"] = ComponentPosition.Whole,
            ["火"] = ComponentPosition.Left,   ["灬"] = ComponentPosition.Bottom,
        };

        public static ComponentPosition PositionOf(string part) =>
            Positions.TryGetValue(part, out var position) ? position : ComponentPosition.None;

        /// <summary>同源徽标文字(UI 右上角 ≈X):取组内代表字;自己就是代表字时取组内下一个。
        /// 清单外返回 null(不画徽标)。</summary>
        public static string KinBadge(string part)
        {
            if (!TryGetGroup(part, out var group)) return null;
            return group[0] == part ? group[1] : group[0];
        }
    }
}
