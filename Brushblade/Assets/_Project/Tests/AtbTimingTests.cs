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
        public void PlayerActionMeter_StartsInDebt()
        {
            // 玩家开局白拿第一拍(战斗一开始 Phase 就是 PlayerTurn,没经过调度器),
            // 那一拍的 100 记成预支 —— 否则玩家行动后与所有人同时归零,并列时靠优先级
            // 永远先手,其余单位会被饿死。2026-08-15 CTB 改造裁定。
            var engine = Engine(new[] { Dummy() });

            Assert.That(engine.PlayerActionMeter, Is.EqualTo(-TurnScheduler.Threshold));
        }

        [Test]
        public void EndTurn_IsAWrapperOverAdvanceOnce()
        {
            // 同速基准局:一次 EndTurn 应当恰好走完「召唤物 → 敌人 → 回到玩家」
            var engine = Engine(new[] { Dummy(attack: 10) });

            engine.EndTurn();

            Assert.That(engine.Phase, Is.EqualTo(BattlePhase.PlayerTurn));
            Assert.That(engine.Turn, Is.EqualTo(2), "回到玩家 = 新一拍开始");
        }

        [Test]
        public void Forecast_StartsWithTheEnemyWhenPlayerHasYielded()
        {
            var engine = Engine(new[] { Dummy(attack: 10) });

            engine.YieldTurn();
            var forecast = engine.Forecast(3);

            Assert.That(forecast[0].Kind, Is.EqualTo(ActorKind.Enemy));
        }

        [Test]
        public void AdvanceOnce_ReturnsFalseWhenPlayersTurnComesUp()
        {
            var engine = Engine(new[] { Dummy(attack: 10) });

            engine.YieldTurn();
            bool more = engine.AdvanceOnce();   // 敌人这一拍
            Assert.That(more, Is.True);
            Assert.That(engine.LastActor.Kind, Is.EqualTo(ActorKind.Enemy));

            more = engine.AdvanceOnce();        // 轮到玩家 → 停
            Assert.That(more, Is.False);
            Assert.That(engine.Phase, Is.EqualTo(BattlePhase.PlayerTurn));
        }

        [Test]
        public void FastPlayer_GetsTwoTurnsPerEnemyTurn()
        {
            var engine = Engine(new[] { Dummy(attack: 10) },
                new BattleConfig { PlayerMaxHp = 999, PlayerSpeed = 200 });

            engine.YieldTurn();
            var forecast = engine.Forecast(6);

            Assert.That(forecast.Count(a => a.Kind == ActorKind.Player), Is.EqualTo(4));
            Assert.That(forecast.Count(a => a.Kind == ActorKind.Enemy), Is.EqualTo(2));
        }
    }
}
