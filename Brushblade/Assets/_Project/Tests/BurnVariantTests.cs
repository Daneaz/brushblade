using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>火系 DOT 三分化(2026-08-09,子项目 E-a):不灭 / 立即结算 / 引爆。
    /// 规格见 docs/superpowers/specs/2026-08-09-火系DOT三分化-design.md。</summary>
    public class BurnVariantTests
    {
        private static RecipeGraph Graph() => new(new[]
        {
            new CharDef("木", Element.Wood),
            // 燃:纯灼烧 4 层(与真实字表的 燃 逐字同配置,便于对照)
            new CharDef("燃", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.BurnSingle, 4) }),
            // 炽:灼烧系数 +1(与真实字表的 炽 同配置)
            new CharDef("炽", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.BurnPotency, 1) }),
        });

        private static BattleEngine Engine(string[] library, EnemyDef[] enemies,
            BattleConfig config = null, int seed = 1) =>
            new(Graph(), config ?? new BattleConfig { DropTable = new[] { "木" }, PlayerMaxHp = 200 },
                library, Array.Empty<string>(), enemies, seed);

        private static EnemyDef Dummy(int hp = 300, int attack = 0) =>
            new("靶", Element.Heart, hp, attack);

        // ---- 灼烧结算的基线(重构守卫)----

        [Test]
        public void Burn_TicksThenDecaysOneStack()
        {
            var engine = Engine(new[] { "燃" }, new[] { Dummy() });
            engine.Cast("燃", 0);
            int before = engine.Enemies[0].Hp;

            engine.EndTurn();
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(before - 8), "4 层 × 系数 2 = 8");
            Assert.That(engine.Enemies[0].Statuses.TotalMagnitude(StatusKind.Burn), Is.EqualTo(3),
                "结算后减一层");

            engine.EndTurn();
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(before - 8 - 6), "3 层 × 2 = 6");
            Assert.That(engine.Enemies[0].Statuses.TotalMagnitude(StatusKind.Burn), Is.EqualTo(2));
        }

        [Test]
        public void Burn_EmitsOneBurnTickEventPerEnemy()
        {
            var engine = Engine(new[] { "燃" }, new[] { Dummy() });
            engine.Cast("燃", 0);
            engine.EndTurn();
            Assert.That(engine.LastEvents.Count(e => e.Kind == BattleEventKind.BurnTick),
                Is.EqualTo(1));
        }

        [Test]
        public void Burn_RespectsKeMultiplier_NotShengMultiplier()
        {
            // 火克金 ×1.5:4 层 × 2 × 1.5 = 12。用金属性靶子才测得出克制,
            // 心属性对全属性都是 1.0x(子项目 D 的教训:同属性对同属性也是 1.0,同样测不出来)
            var engine = Engine(new[] { "燃" }, new[] { new EnemyDef("锈", Element.Metal, 300, 0) });
            engine.Cast("燃", 0);
            int before = engine.Enemies[0].Hp;
            engine.EndTurn();
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(before - 12), "4 × 2 × 1.5(火克金)");
        }

        [Test]
        public void Burn_LastStackRemovesTheStatus()
        {
            var engine = Engine(new[] { "燃" }, new[] { Dummy() });
            engine.Cast("燃", 0);
            for (int i = 0; i < 4; i++) engine.EndTurn();
            Assert.That(engine.Enemies[0].Statuses.Has(StatusKind.Burn), Is.False,
                "烧完 4 层后状态条目被移除");
        }

        [Test]
        public void Burn_KillingTheLastEnemy_WinsTheBattle()
        {
            var engine = Engine(new[] { "燃" }, new[] { Dummy(hp: 6) });
            engine.Cast("燃", 0);
            engine.EndTurn();
            Assert.That(engine.Enemies[0].Alive, Is.False);
            Assert.That(engine.Phase, Is.EqualTo(BattlePhase.Won));
        }
    }
}
