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
        public void PlayerActionMeter_NeverGoesNegative()
        {
            // 玩家计量器与场上所有单位同口径从 0 起步,不需要任何先手/负债/懒消费之类的特例
            // (2026-08-15 第五次审查订正:前四轮试过的这些记账手法全是在给反向的 tie-break
            // 打补丁——玩家排最先会让它每次推进都抢在敌人前面收回行动权。把 BuildSlots 的
            // 优先级方向调成「玩家排最后」之后,恒非负这条不变式自然成立,不必再靠任何机制
            // 保证。这条测试仍然保留,守住"永不为负"这个不变式)。
            var engine = Engine(new[] { Dummy() });

            Assert.That(engine.PlayerActionMeter, Is.GreaterThanOrEqualTo(0));
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
