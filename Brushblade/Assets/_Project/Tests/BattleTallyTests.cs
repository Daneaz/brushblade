using System;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    public class BattleTallyTests
    {
        private static RecipeGraph Graph() => new(new[]
        {
            new CharDef("木", Element.Wood),
            new CharDef("火", Element.Fire),
            new CharDef("林", Element.Wood, new[] { "木", "木" }),
            new CharDef("燃", Element.Fire, new[] { "火", "火" },
                effects: new[] { new EffectDef(EffectKind.DamageAll, 10) }),
        });

        private static BattleEngine Engine(BattleTally tally, string[] library, string[] pool = null) =>
            new(Graph(), new BattleConfig { Tally = tally }, library, pool ?? Array.Empty<string>(),
                new[] { new EnemyDef("锈", Element.Metal, 500, 1) }, seed: 1);

        [Test]
        public void Cast_Success_CountsPlayAndMaxHit()
        {
            var tally = new BattleTally();
            var engine = Engine(tally, new[] { "燃" });
            Assert.That(engine.Cast("燃"), Is.EqualTo(BattleError.None));
            Assert.That(tally.Plays["燃"], Is.EqualTo(1));
            Assert.That(tally.MaxHit, Is.GreaterThan(0));
        }

        [Test]
        public void Cast_Failure_CountsNothing()
        {
            var tally = new BattleTally();
            var engine = Engine(tally, new[] { "燃" });
            Assert.That(engine.Cast("林"), Is.Not.EqualTo(BattleError.None)); // 不在字库
            Assert.That(tally.Plays.Count, Is.EqualTo(0));
        }

        [Test]
        public void Compose_And_Dismantle_CountOnSuccess()
        {
            var tally = new BattleTally();
            var engine = Engine(tally, new[] { "林" });
            Assert.That(engine.Dismantle("林"), Is.EqualTo(BattleError.None));
            Assert.That(tally.Dismantles, Is.EqualTo(1));
            Assert.That(engine.Compose("林"), Is.EqualTo(BattleError.None));
            Assert.That(tally.Composes, Is.EqualTo(1));
            Assert.That(engine.Compose("燃"), Is.Not.EqualTo(BattleError.None)); // 缺部件
            Assert.That(tally.Composes, Is.EqualTo(1));
        }

        [Test]
        public void NoTally_DoesNotThrow()
        {
            var engine = Engine(null, new[] { "燃" });
            Assert.That(engine.Cast("燃"), Is.EqualTo(BattleError.None));
        }
    }
}
