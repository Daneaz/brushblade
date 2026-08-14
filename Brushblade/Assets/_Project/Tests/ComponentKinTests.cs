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
        public void TryGetGroup_MetalHasFourMembers()
        {
            Assert.That(ComponentKin.TryGetGroup("刂", out var group), Is.True);
            Assert.That(group, Is.EqualTo(new[] { "金", "钅", "戈", "刂" }));
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
    }
}
