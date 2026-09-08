using System;
using System.Collections.Generic;
using Brushblade.Core;
using UnityEngine;

namespace Brushblade.Presentation
{
    /// <summary>技能树环形星域的**纯布局计算**(spec 2026-09-08 §2)。不碰 GameObject ——
    /// 单独一个文件是为了这份坐标推导能被独立读懂和验算,不用在 500 行的建 UI 代码里找。
    ///
    /// 坐标系:画布左上为原点,y 向下(与 uGUI 的 anchoredPosition 相反,取用处要翻 y)。
    /// 角度:屏幕角,0° = 正右,顺时针为正,270° = 正上。
    ///
    /// ⚠ **三棵树的第一圈不共用一个半径**(层数少的树起点更远),最外层才共用 475。
    /// 这条是从一次失败里得来的:初稿让所有枝的 L1 共用半径 52,那一圈要挤 13 个节点,
    /// 枝间距只剩 32px(节点直径 20),机制树压到 16px —— 直接重叠。</summary>
    public static class PerkLayout
    {
        public const float CanvasSize = 1220f;
        public const float Center = CanvasSize / 2f;

        public const float HubRadius = 44f;
        public const float RootRadius = 100f;
        public const float OuterRadius = 475f;

        /// <summary>跨树节点的半径。要比 <see cref="OuterRadius"/> 多出一整个节点直径还有余,
        /// 三个跨树节点才**读得出是另起的一组**而不是某棵树多长了一层。
        /// 545 − 475 = 70,直径 52 → 径向余 18;实测与最近邻的中心距 100~106(余 48~54)。
        ///
        /// ⚠ 画布 1120 → 1220 就是被这条撑大的:顶上那对在 545 半径上,
        /// 1120 的画布里节点上沿会落到 −6(出界)。</summary>
        public const float CrossRadius = 545f;

        /// <summary>节点直径。spec §2.3:最紧处枝距 88px,直径 52 留 36px 余量,
        /// 节点里放得下两个字的名字。</summary>
        public const float NodeDiameter = 52f;

        private readonly struct Sector
        {
            public readonly float FromDeg, ToDeg, FirstRadius;
            public Sector(float from, float to, float firstRadius)
            {
                FromDeg = from; ToDeg = to; FirstRadius = firstRadius;
            }
            public float MidDeg => (FromDeg + ToDeg) / 2f;
        }

        private static Sector SectorOf(PerkTree tree) => tree switch
        {
            PerkTree.Wuxing => new Sector(180f, 360f, 190f),
            PerkTree.Passive => new Sector(0f, 108f, 200f),
            PerkTree.Mechanic => new Sector(108f, 180f, 280f),
            _ => new Sector(0f, 360f, CrossRadius),   // Cross 不走扇区,见 Place
        };

        /// <summary>某树的枝列表,顺序取自 <see cref="PerkRules.Nodes"/> 里各枝首次出现的顺序 ——
        /// 不在这里另写一份枝名列表,表变了(加枝/调顺序)这里自动跟着变。</summary>
        private static List<string> BranchesOf(PerkTree tree)
        {
            var list = new List<string>();
            foreach (var def in PerkRules.Nodes)
                if (def.Tree == tree && !list.Contains(def.Branch))
                    list.Add(def.Branch);
            return list;
        }

        private static int MaxDepthOf(PerkTree tree)
        {
            int max = 0;
            foreach (var def in PerkRules.Nodes)
                if (def.Tree == tree) max = Math.Max(max, def.Depth);
            return max;
        }

        /// <summary>跨树节点的角度。**不再摆在两棵树的扇区交界上**(2026-09-08 用户裁定)。
        ///
        /// 初版让每个跨树节点落在它所连两棵树的交界角,再从两棵树的**树根**(半径 100,
        /// 紧挨中心)各拉一条虚线过去 —— 那两条线要横穿整个扇区,把沿途每个节点都串一遍,
        /// 画面被切得稀碎。现在改成:跨树节点**贴着它取材最多的那棵树的外沿另起一组,
        /// 一条连线都不画**(<c>PerkView.BuildConnectors</c> 对 Cross 直接跳过)。
        ///
        /// 分组依据是「它数的是谁」:
        ///   相济(每个五行 L3/L4)与博采(每个点到 L3 的五行系)都数五行 → 并排摆在五行正上方
        ///     (五行占 180°~360° 的上半,正上是 270°),对称错开 ±8°,中心距 152。
        ///   融会数的是机制节点 → 摆在机制扇区(108°~180°,左下)的中线 144° 外沿。
        ///
        /// 前置关系不靠连线表达,靠详情面板的「跨树前置」两栏 —— 它本来就是谓词
        /// (「五行任一 L3」),连到某个具体节点反而是撒谎(spec §3.1)。</summary>
        private static float CrossAngle(string branch) => branch switch
        {
            "xvigor" => 262f,   // 相济:五行正上方偏左
            "xdraw" => 278f,    // 博采:五行正上方偏右
            "xedge" => 144f,    // 融会:机制扇区中线
            _ => 270f,
        };

        public static Vector2 Place(PerkNodeDef def)
        {
            if (def.Tree == PerkTree.Cross)
                return Polar(CrossAngle(def.Branch), CrossRadius);

            var sector = SectorOf(def.Tree);
            var branches = BranchesOf(def.Tree);
            int i = branches.IndexOf(def.Branch);
            int n = branches.Count;

            // 枝在扇区内**均分**:第 i 枝落在 (i + 0.5)/n 处,两端各留半格 ——
            // 直接按 i/(n-1) 铺满会让首尾两枝贴到相邻扇区的边界上。
            float angle = sector.FromDeg + (sector.ToDeg - sector.FromDeg) * (i + 0.5f) / n;

            int maxDepth = MaxDepthOf(def.Tree);
            float radius = maxDepth <= 1
                ? sector.FirstRadius
                : sector.FirstRadius
                  + (OuterRadius - sector.FirstRadius) * (def.Depth - 1) / (maxDepth - 1);

            return Polar(angle, radius);
        }

        /// <summary>三个树根标记(纯分区标记,不是可解锁节点 —— spec §2.4)。</summary>
        public static Vector2 RootPlace(PerkTree tree) =>
            Polar(SectorOf(tree).MidDeg, RootRadius);

        public static Vector2 HubPlace() => new Vector2(Center, Center);

        /// <summary>某树扇区的中心方向 + 最外层半径,给跳转锚点用(把视口平移到那儿)。</summary>
        public static Vector2 JumpTarget(PerkTree tree) => tree == PerkTree.Cross
            ? new Vector2(Center, Center)   // 三个跨树节点散在三边,跳到中心才看得全
            : Polar(SectorOf(tree).MidDeg, (SectorOf(tree).FirstRadius + OuterRadius) / 2f);

        private static Vector2 Polar(float degrees, float radius)
        {
            float rad = degrees * Mathf.Deg2Rad;
            return new Vector2(Center + radius * Mathf.Cos(rad),
                               Center + radius * Mathf.Sin(rad));
        }
    }
}
