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

        /// <summary>金系是最大的一组:5 个成员。这个数字有表现层含义 ——
        /// 部件卡把同组其他成员各标一个角,除自己外 4 个刚好占满四角,再加成员就得换设计。</summary>
        [Test]
        public void TryGetGroup_MetalHasFiveMembers()
        {
            Assert.That(ComponentKin.TryGetGroup("刀", out var group), Is.True);
            Assert.That(group, Is.EqualTo(new[] { "金", "钅", "戈", "刂", "刀" }));
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
