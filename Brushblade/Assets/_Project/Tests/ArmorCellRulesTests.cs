using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>头行护甲格显示规则(拍板稿 drafts/削甲图标「新显示规则」)。</summary>
    public class ArmorCellRulesTests
    {
        private static EnemyState Mob(int armor) => new(RebalanceFixture.Mob(armor: armor));
        private static EnemyState Boss(int armor) => new(RebalanceFixture.Boss(armor: armor));

        private static void Shred(EnemyState e, int n) => e.Statuses.Apply(new StatusEffect
        {
            Kind = StatusKind.ArmorBreak, Polarity = StatusPolarity.Debuff, Magnitude = n, TurnsLeft = 3,
        });
        private static void Add(EnemyState e, StatusKind k) => e.Statuses.Apply(new StatusEffect
        {
            Kind = k, Polarity = StatusPolarity.Debuff, Magnitude = 1, TurnsLeft = 3,
        });

        [Test]
        public void Cell_Normal_ShowsDefense()
        {
            var c = ArmorCellRules.ForEnemy(Mob(40));
            Assert.That(c.Visible, Is.True);
            Assert.That(c.Kind, Is.EqualTo(ArmorCellKind.Normal));
            Assert.That(c.Value, Is.EqualTo(40));
            Assert.That(c.ShowValue, Is.True);
        }

        [Test]
        public void Cell_BaseZero_Hidden()
        {
            var a = Mob(0); Shred(a, 10);
            var b = Mob(0); Add(b, StatusKind.Sunder);
            Assert.That(ArmorCellRules.ForEnemy(a).Visible, Is.False);
            Assert.That(ArmorCellRules.ForEnemy(b).Visible, Is.False);
            Assert.That(ArmorCellRules.ForEnemy(Mob(0)).Visible, Is.False);
        }

        [Test]
        public void Cell_Shred_ShowsRemaining()
        {
            var e = Mob(40); Shred(e, 15);
            var c = ArmorCellRules.ForEnemy(e);
            Assert.That(c.Kind, Is.EqualTo(ArmorCellKind.Shred));
            Assert.That(c.Value, Is.EqualTo(25));
            Assert.That(c.ShowValue, Is.True);
            Assert.That(c.Visible, Is.True);
        }

        [Test]
        public void Cell_Shred_ToZero_StillShown()
        {
            var e = Mob(40); Shred(e, 50);
            var c = ArmorCellRules.ForEnemy(e);
            Assert.That(c.Kind, Is.EqualTo(ArmorCellKind.Shred));
            Assert.That(c.Value, Is.EqualTo(0));
            Assert.That(c.Visible, Is.True);
            Assert.That(c.ShowValue, Is.True);
        }

        [Test]
        public void Cell_Sunder_Mob_NoValue()
        {
            var e = Mob(40); Add(e, StatusKind.Sunder);
            var c = ArmorCellRules.ForEnemy(e);
            Assert.That(c.Visible, Is.True);
            Assert.That(c.Kind, Is.EqualTo(ArmorCellKind.Sunder));
            Assert.That(c.ShowValue, Is.False);
        }

        [Test]
        public void Cell_Sunder_Boss_HalfValue()
        {
            var e = Boss(100); Add(e, StatusKind.Sunder);
            var c = ArmorCellRules.ForEnemy(e);
            Assert.That(c.Kind, Is.EqualTo(ArmorCellKind.Sunder));
            Assert.That(c.Value, Is.EqualTo(50));
            Assert.That(c.ShowValue, Is.True);
        }

        [Test]
        public void Cell_SunderAndShred_Boss()
        {
            var e = Boss(100); Add(e, StatusKind.Sunder); Shred(e, 20);
            var c = ArmorCellRules.ForEnemy(e);
            Assert.That(c.Kind, Is.EqualTo(ArmorCellKind.Sunder));
            Assert.That(c.Value, Is.EqualTo(30));
            Assert.That(c.ShowValue, Is.True);
        }

        [Test]
        public void Cell_SilenceOnly_MobHidden_BossHalf()
        {
            var m = Mob(40); Add(m, StatusKind.Silence);
            Assert.That(ArmorCellRules.ForEnemy(m).Visible, Is.False);
            var b = Boss(100); Add(b, StatusKind.Silence);
            var c = ArmorCellRules.ForEnemy(b);
            Assert.That(c.Visible, Is.True);
            Assert.That(c.Kind, Is.EqualTo(ArmorCellKind.Normal));
            Assert.That(c.Value, Is.EqualTo(50));
        }

        [Test]
        public void Cell_SilenceAndShred_ShowsShredWithValue()
        {
            var e = Mob(40); Add(e, StatusKind.Silence); Shred(e, 10);
            var c = ArmorCellRules.ForEnemy(e);
            Assert.That(c.Kind, Is.EqualTo(ArmorCellKind.Shred));
            Assert.That(c.Value, Is.EqualTo(0));
            Assert.That(c.Visible, Is.True);
            Assert.That(c.ShowValue, Is.True);
        }

        [Test]
        public void ForUnit_PlayerStyle_UsesGivenBaseAndShown()
        {
            var bag = new StatusBag();
            bag.Apply(new StatusEffect { Kind = StatusKind.ArmorBreak, Polarity = StatusPolarity.Debuff, Magnitude = 5, TurnsLeft = 2 });
            var c = ArmorCellRules.ForUnit(12, 7, bag, isBoss: false);
            Assert.That(c.Kind, Is.EqualTo(ArmorCellKind.Shred));
            Assert.That(c.Value, Is.EqualTo(7));
            Assert.That(ArmorCellRules.ForUnit(0, 0, bag, false).Visible, Is.False);
        }
    }
}
