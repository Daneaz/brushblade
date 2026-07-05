using System;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>战斗结算事件流(13.3 打击感的数据源)。</summary>
    public class BattleEventTests
    {
        private static RecipeGraph Graph() => new(new[]
        {
            new CharDef("木", Element.Wood),
            new CharDef("火", Element.Fire),
            new CharDef("辟", Element.Metal),
            new CharDef("土", Element.Earth),
            new CharDef("林", Element.Wood, new[] { "木", "木" }),
            new CharDef("燃", Element.Fire, new[] { "火", "火" },
                effects: new[] { new EffectDef(EffectKind.BurnAll, 3) }),
            new CharDef("焚", Element.Fire, new[] { "林", "火" }, apCost: 2,
                effects: new[] { new EffectDef(EffectKind.DamageAll, 18), new EffectDef(EffectKind.BurnAll, 1) }),
            new CharDef("壁", Element.Earth, new[] { "辟", "土" },
                effects: new[] { new EffectDef(EffectKind.Shield, 8) }),
        });

        private static BattleEngine Engine(string[] library, params EnemyDef[] enemies) =>
            new(Graph(), new BattleConfig(), library, Array.Empty<string>(),
                enemies.Length > 0 ? enemies : new[] { new EnemyDef("锈", Element.Metal, 200, 5) }, seed: 1);

        [Test]
        public void Cast_EmitsDamagePerEnemy_AndBurn()
        {
            var engine = Engine(new[] { "焚" },
                new EnemyDef("锈", Element.Metal, 200, 5), new EnemyDef("怔", Element.Heart, 200, 3));
            engine.Cast("焚");

            var damage = engine.LastEvents.Where(e => e.Kind == BattleEventKind.Damage).ToList();
            Assert.That(damage.Count, Is.EqualTo(2));
            Assert.That(damage[0].TargetIndex, Is.EqualTo(0));
            Assert.That(damage[0].Amount, Is.EqualTo(81)); // 焚 vs 金:18×3×1.5
            Assert.That(damage[1].Amount, Is.EqualTo(54)); // vs 心:18×3

            var burn = engine.LastEvents.Where(e => e.Kind == BattleEventKind.Burn).ToList();
            Assert.That(burn.Count, Is.EqualTo(2));
            Assert.That(burn[0].Amount, Is.EqualTo(1));
        }

        [Test]
        public void Cast_Shield_EmitsPlayerSideEvent()
        {
            var engine = Engine(new[] { "壁" });
            engine.Cast("壁");
            var shield = engine.LastEvents.Single(e => e.Kind == BattleEventKind.Shield);
            Assert.That(shield.TargetIndex, Is.EqualTo(-1));
            Assert.That(shield.Amount, Is.EqualTo(24)); // 土生金 ×3
        }

        [Test]
        public void Kill_EmitsEnemyDied()
        {
            var engine = Engine(new[] { "焚" }, new EnemyDef("枯", Element.Wood, 10, 2));
            engine.Cast("焚"); // 54 > 10
            Assert.That(engine.LastEvents.Any(e => e.Kind == BattleEventKind.EnemyDied && e.TargetIndex == 0), Is.True);
        }

        [Test]
        public void EndTurn_EmitsBurnTick_AndEnemyAttack()
        {
            var engine = Engine(new[] { "燃" });
            engine.Cast("燃"); // 3 层灼烧
            engine.EndTurn();

            var tick = engine.LastEvents.Single(e => e.Kind == BattleEventKind.BurnTick);
            Assert.That(tick.TargetIndex, Is.EqualTo(0));
            Assert.That(tick.Amount, Is.EqualTo(6)); // 3×2

            var attack = engine.LastEvents.Single(e => e.Kind == BattleEventKind.EnemyAttack);
            Assert.That(attack.Amount, Is.EqualTo(5));
        }

        [Test]
        public void Events_ClearedAtEachAction()
        {
            var engine = Engine(new[] { "燃", "壁" });
            engine.Cast("燃");
            Assert.That(engine.LastEvents, Is.Not.Empty);
            engine.Cast("壁");
            Assert.That(engine.LastEvents.Any(e => e.Kind == BattleEventKind.Burn), Is.False); // 上一动作的事件已清
            Assert.That(engine.LastEvents.Any(e => e.Kind == BattleEventKind.Shield), Is.True);
        }
    }
}
