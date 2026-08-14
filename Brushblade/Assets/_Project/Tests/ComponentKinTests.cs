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

        /// <summary>徽标取组内代表字;自己就是代表字时取组内下一个 ——
        /// 火 显示 ≈灬、灬 显示 ≈火(与设计板一致);金系四个里 钅/戈/刂 都显示 ≈金,金 显示 ≈钅。</summary>
        [Test]
        public void KinBadge_ShowsRepresentative_OrNextWhenSelfIsRepresentative()
        {
            Assert.That(ComponentKin.KinBadge("灬"), Is.EqualTo("火"));
            Assert.That(ComponentKin.KinBadge("火"), Is.EqualTo("灬"));
            Assert.That(ComponentKin.KinBadge("氵"), Is.EqualTo("水"));
            Assert.That(ComponentKin.KinBadge("刂"), Is.EqualTo("金"));
            Assert.That(ComponentKin.KinBadge("金"), Is.EqualTo("钅"));
        }

        [Test]
        public void KinBadge_ReturnsNull_ForPartsOutsideTheList()
        {
            Assert.That(ComponentKin.KinBadge("禾"), Is.Null);
        }

        /// <summary>位形表(spec §1.6):能独立成字的一律「整」,火 例外取「左」(跟随设计板,
        /// 与 灬 的「底」形成对照)。</summary>
        [Test]
        public void PositionOf_MatchesTheDesignBoard()
        {
            Assert.That(ComponentKin.PositionOf("氵"), Is.EqualTo(ComponentPosition.Left));
            Assert.That(ComponentKin.PositionOf("灬"), Is.EqualTo(ComponentPosition.Bottom));
            Assert.That(ComponentKin.PositionOf("火"), Is.EqualTo(ComponentPosition.Left));
            Assert.That(ComponentKin.PositionOf("土"), Is.EqualTo(ComponentPosition.Whole));
            Assert.That(ComponentKin.PositionOf("艹"), Is.EqualTo(ComponentPosition.Top));
            Assert.That(ComponentKin.PositionOf("戈"), Is.EqualTo(ComponentPosition.Right));
            Assert.That(ComponentKin.PositionOf("禾"), Is.EqualTo(ComponentPosition.None));
        }
    }
}
