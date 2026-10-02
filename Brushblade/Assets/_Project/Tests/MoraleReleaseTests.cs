using System;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>金脉 L4「断金」(spec 2026-10-02 §2.4)。</summary>
    public class MoraleReleaseTests
    {
        private static RecipeGraph Graph() => new(new[]
        {
            new CharDef("刀", Element.Metal, isComponent: false, effects: new[] { new EffectDef(EffectKind.DamageSingle, 100) }),
            new CharDef("火刀", Element.Fire, isComponent: false, effects: new[] { new EffectDef(EffectKind.DamageSingle, 100) }),
            new CharDef("砺", Element.Metal, isComponent: false, effects: new[] { new EffectDef(EffectKind.CritBuff, 5) }),
            new CharDef("金", Element.Metal, isComponent: true,
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 100) }),
        });

        private static BattleEngine Engine(int release, int morale, string[] library, string[] pool = null) =>
            new(Graph(), new BattleConfig
                {
                    DropTable = new[] { "金" }, PlayerMaxHp = 5000, ApPerTurn = 20,
                    MoraleReleasePercent = release,
                },
                library, pool ?? Array.Empty<string>(),
                new[] { new EnemyDef("靶", Element.Heart, 900000, 0) }, seed: 1,
                startingStatuses: morale > 0 ? new[] { new StatusEffect
                {
                    Kind = StatusKind.Morale, Polarity = StatusPolarity.Buff, Magnitude = morale, TurnsLeft = -1,
                } } : null);

        private static int Morale(BattleEngine e) => e.PlayerStatuses.TotalMagnitude(StatusKind.Morale);

        private static int Hit(BattleEngine e, string card)
        {
            int before = e.Enemies[0].Hp;
            Assert.That(e.Cast(card, 0), Is.EqualTo(BattleError.None));
            return before - e.Enemies[0].Hp;
        }

        [Test]
        public void FullMorale_MetalCard_QuadruplesAndClears()
        {
            int released = Hit(Engine(300, 5, new[] { "刀" }), "刀");
            var plainEngine = Engine(0, 5, new[] { "刀" });
            int plain = Hit(plainEngine, "刀");
            Assert.That(released, Is.EqualTo(plain * 4));
            var e = Engine(300, 5, new[] { "刀" });
            Hit(e, "刀");
            Assert.That(Morale(e), Is.EqualTo(0), "释放后清空");
            Assert.That(Morale(plainEngine), Is.EqualTo(5), "没点断金不清");
        }

        [Test]
        public void BelowFive_NoRelease()
        {
            var e = Engine(300, 4, new[] { "刀" });
            int hit = Hit(e, "刀");
            Assert.That(hit, Is.EqualTo(Hit(Engine(0, 4, new[] { "刀" }), "刀")));
            Assert.That(Morale(e), Is.EqualTo(4));
        }

        [Test]
        public void NonMetalCard_DoesNotRelease()
        {
            var e = Engine(300, 5, new[] { "火刀" });
            Hit(e, "火刀");
            Assert.That(Morale(e), Is.EqualTo(5));
        }

        [Test]
        public void MetalComponent_DoesNotRelease()
        {
            var e = Engine(300, 5, Array.Empty<string>(), pool: new[] { "金" });
            Hit(e, "金");
            Assert.That(Morale(e), Is.EqualTo(5), "部件不算");
        }

        [Test]
        public void MetalBuffWithoutDamage_DoesNotConsume()
        {
            var e = Engine(300, 5, new[] { "砺" });
            e.Cast("砺");
            Assert.That(Morale(e), Is.EqualTo(5));
        }
    }
}
