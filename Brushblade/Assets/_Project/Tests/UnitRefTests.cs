using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    public class UnitRefTests
    {
        [Test]
        public void Factories_AndEquality()
        {
            Assert.That(UnitRef.Player.Side, Is.EqualTo(UnitSide.Player));
            Assert.That(UnitRef.Summon(3), Is.EqualTo(new UnitRef(UnitSide.Summon, 3)));
            Assert.That(UnitRef.Enemy(1) == UnitRef.Enemy(1), Is.True);
            Assert.That(UnitRef.Enemy(1) == UnitRef.Summon(1), Is.False);
            Assert.That(default(UnitRef).Side, Is.EqualTo(UnitSide.None), "default 必须是 None,不能误当玩家/0 号敌人");
            Assert.That(UnitRef.None.Index, Is.EqualTo(-1));
        }

        [Test]
        public void Default_EqualsNone()
        {
            Assert.That(default(UnitRef), Is.EqualTo(UnitRef.None));
            Assert.That(default(UnitRef) == UnitRef.None, Is.True);
            Assert.That(default(UnitRef).GetHashCode(), Is.EqualTo(UnitRef.None.GetHashCode()));
            Assert.That(default(UnitRef) == UnitRef.Player, Is.False);
            Assert.That(default(UnitRef) == UnitRef.Enemy(0), Is.False, "None 不能与 0 号敌人相等");
            Assert.That(UnitRef.Enemy(0) == UnitRef.Enemy(1), Is.False, "Enemy/Summon 仍比较下标");
        }
    }
}
