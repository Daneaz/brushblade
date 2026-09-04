using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>五系部件等价清单(spec 2026-08-15 §1.1)。清单是**显式**的 ——
    /// 不许从 CharDef.Element 推导:禾 的 element 也是 Wood,推导会让 木 顶掉 利=禾+刂 里的 禾。</summary>
    public class ComponentKinTests
    {
        [Test]
        public void TryGetGroup_ReturnsWholeGroup_InDeclarationOrder()
        {
            Assert.That(ComponentKin.TryGetGroup("氵", out var group), Is.True);
            Assert.That(group, Is.EqualTo(new[] { "水", "氵", "冫" }));
        }

        [Test]
        public void MetalGroup_NoLongerHasDaoOrGe()
        {
            // 2026-09-05:戈/刀 随 战/劈/沏 移出字表而消失,清单必须同步 ——
            // 留着会让 RealConfig_AllMembersAreLeavesInTheRealCharTable 报「不存在」
            Assert.That(ComponentKin.TryGetGroup("刀", out _), Is.False);
            Assert.That(ComponentKin.TryGetGroup("戈", out _), Is.False);
            Assert.That(ComponentKin.TryGetGroup("刂", out var group), Is.True);
            Assert.That(group.Contains("金"), Is.True);
            Assert.That(group.Contains("钅"), Is.True);
            Assert.That(group.Count, Is.EqualTo(3));
        }

        [Test]
        public void WoodGroup_IncludesZhu()
        {
            // 2026-09-05:箭 = 竹 + 前 带 竹 进字表,竹 与 木/艹 在配方匹配上等价
            Assert.That(ComponentKin.AreKin("木", "竹"), Is.True);
            Assert.That(ComponentKin.AreKin("艹", "竹"), Is.True);
            Assert.That(ComponentKin.AreKin("竹", "禾"), Is.False, "禾 仍然不是木系等价部件");
        }

        /// <summary>清单外的部件不参与:禾 是形声部件,element 虽为 Wood 也不该与 木 等价。</summary>
        [Test]
        public void TryGetGroup_RejectsPartsOutsideTheList()
        {
            Assert.That(ComponentKin.TryGetGroup("禾", out _), Is.False);
            Assert.That(ComponentKin.TryGetGroup("丁", out _), Is.False);
        }

        [Test]
        public void AreKin_TrueWithinGroup_FalseAcross()
        {
            Assert.That(ComponentKin.AreKin("水", "冫"), Is.True);
            Assert.That(ComponentKin.AreKin("水", "水"), Is.True);
            Assert.That(ComponentKin.AreKin("水", "木"), Is.False);
            Assert.That(ComponentKin.AreKin("木", "禾"), Is.False);
        }

        /// <summary>守卫(分支级审查):「部件等价」与「宝箱前置」互不干扰,唯一支点是
        /// ComponentKin 的全部成员在真实字表里都没有配方(IsLeaf)。哪天设计给
        /// 山/石/戈 之类配了配方,前置判定会开始要求玩家"拥有部件",合成也会开始把
        /// 成品字当部件吃掉——两处都无声,所以钉在这里。
        /// 真实配置加载复用 CharTableTests.RealGraph()(同程序集、同走 Data.ConfigLoader,
        /// 不直接引 Newtonsoft)。</summary>
        [Test]
        public void RealConfig_AllMembersAreLeavesInTheRealCharTable()
        {
            var graph = CharTableTests.RealGraph();
            foreach (var part in ComponentKin.AllParts)
            {
                Assert.That(graph.TryGet(part, out var def), Is.True, $"{part} 在真实字表里不存在");
                Assert.That(def.IsLeaf, Is.True, $"{part} 已经有配方了,五系等价与宝箱前置的互不干扰假设被打破");
                Assert.That(def.IsComponent, Is.True, $"{part} 必须是部件 —— 五系等价只在部件之间成立");
            }
        }
    }
}
