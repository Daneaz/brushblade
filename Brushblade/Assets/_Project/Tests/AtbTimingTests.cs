using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>ATB 时序归属(2026-08-15):每单位自结算的 DOT / 状态递减 / 立即结算。
    /// 规格见 docs/superpowers/specs/2026-08-15-ATB回合制改造-design.md §4.3。</summary>
    public class AtbTimingTests
    {
        private static RecipeGraph Graph() => new(new[]
        {
            new CharDef("木", Element.Wood),
        });

        private static BattleEngine Engine(EnemyDef[] enemies, BattleConfig config = null) =>
            new(Graph(), config ?? new BattleConfig { PlayerMaxHp = 999 },
                Array.Empty<string>(), Array.Empty<string>(), enemies, seed: 1);

        private static EnemyDef Dummy(string id = "靶", int hp = 999, int attack = 0) =>
            new(id, Element.Heart, hp, attack);

        [Test]
        public void PlayerSpeed_DefaultsToBaseline()
        {
            var engine = Engine(new[] { Dummy() });

            Assert.That(engine.EffectivePlayerSpeed, Is.EqualTo(100));
        }

        [Test]
        public void PlayerSpeed_ReadsConfigAndSpeedModifier()
        {
            var engine = Engine(new[] { Dummy() },
                new BattleConfig { PlayerMaxHp = 999, PlayerSpeed = 150 });

            Assert.That(engine.EffectivePlayerSpeed, Is.EqualTo(150));
        }

        [Test]
        public void PlayerSpeed_IsClampedLikeEveryoneElse()
        {
            var engine = Engine(new[] { Dummy() },
                new BattleConfig { PlayerMaxHp = 999, PlayerSpeed = 9999 });

            Assert.That(engine.EffectivePlayerSpeed, Is.EqualTo(TurnScheduler.MaxSpeed));
        }

        [Test]
        public void PlayerActionMeter_StartsEmpty()
        {
            var engine = Engine(new[] { Dummy() });

            Assert.That(engine.PlayerActionMeter, Is.EqualTo(0));
        }
    }
}
