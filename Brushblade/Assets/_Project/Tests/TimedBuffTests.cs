using System;
using System.Collections.Generic;
using NUnit.Framework;
using Brushblade.Core;

namespace Brushblade.Core.Tests
{
    /// <summary>限时增益(2026-09-05,平衡重做 P0 任务 7)。
    ///
    /// Empower / CritBuff 此前一律本场持久(TurnsLeft = -1)。P2 的 利(增攻 +30 × 2 回合)
    /// 与 锋(暴击 +20% × 3 回合)需要限时版,且回合数吃卡等级(任务 8)。
    ///
    /// **Turns <= 0 仍是本场持久** —— 既有字表全部没填 turns,这条兜住它们的行为不变。</summary>
    public sealed class TimedBuffTests
    {
        [Test]
        public void Empower_WithTurns_ExpiresAfterThatManyTurnEnds()
        {
            var battle = BuffBattle(EffectKind.Empower, value: 30, turns: 2);
            battle.Cast("增");
            Assert.That(battle.EffectiveAttack, Is.EqualTo(130), "基准 100 + 30");

            battle.EndTurn();
            Assert.That(battle.EffectiveAttack, Is.EqualTo(130), "第 1 个回合末还在");

            battle.EndTurn();
            Assert.That(battle.EffectiveAttack, Is.EqualTo(100), "第 2 个回合末到期,回到基准");
        }

        [Test]
        public void Empower_WithoutTurns_StaysAllBattle()
        {
            var battle = BuffBattle(EffectKind.Empower, value: 50, turns: 0);
            battle.Cast("增");
            battle.EndTurn();
            battle.EndTurn();
            battle.EndTurn();
            Assert.That(battle.EffectiveAttack, Is.EqualTo(150), "turns 缺省 → 本场持久,既有行为不变");
        }

        [Test]
        public void CritBuff_WithTurns_Expires()
        {
            var battle = BuffBattle(EffectKind.CritBuff, value: 20, turns: 3);
            battle.Cast("增");
            Assert.That(battle.EffectiveCrit, Is.EqualTo(20));
            battle.EndTurn(); battle.EndTurn(); battle.EndTurn();
            Assert.That(battle.EffectiveCrit, Is.EqualTo(0), "3 个回合末后到期");
        }

        /// <summary>一张纯增益字「增」的战斗。敌人血量给大值 —— 增益字不打伤害,
        /// 但 EndTurn() 会让敌人反击,靶子不能被打死(死了 Phase 变 Won,EndTurn 就不再推进)。</summary>
        private static BattleEngine BuffBattle(EffectKind kind, int value, int turns)
        {
            var graph = RebalanceFixture.Graph(
                RebalanceFixture.Char("增", new EffectDef(kind, value, turns: turns)));
            return RebalanceFixture.Battle(graph, new[] { "增", "增", "增", "增" },
                RebalanceFixture.Mob());
        }

        /// <summary>卡等级缩放接进 BattleEngine 真实链路的回归(2026-09-05,任务 8)——
        /// 2026-10-04(spec v7 §1)起回合数不吃卡等级:5 级基础 2 回合的 Empower 仍在第 2 个回合末到期,
        /// 攻击值照旧吃等级。走 Cast() / EndTurn() 真实链路。</summary>
        [Test]
        public void Empower_WithTurns_TurnsDoNotScaleByCardLevel_ThroughRealCast()
        {
            var graph = RebalanceFixture.Graph(
                RebalanceFixture.Char("增", new EffectDef(EffectKind.Empower, 30, turns: 2)));
            var cardLevels = new Dictionary<string, int> { ["增"] = 5 };
            var battle = new BattleEngine(graph,
                new BattleConfig { PlayerMaxHp = RebalanceFixture.BaseMaxHp, PlayerAttack = 100 },
                new[] { "增", "增", "增", "增" }, Array.Empty<string>(),
                new[] { RebalanceFixture.Mob() }, seed: 1, cardLevels: cardLevels);

            battle.Cast("增");
            // Magnitude 仍吃卡等级(Empower 是数值类):5 级 → 30 × 124 / 100 = 37.2 → 38,基准 100 + 38 = 138。
            Assert.That(battle.EffectiveAttack, Is.EqualTo(138), "基准 100 + ScaleByCardLevel(30, 5)=38");

            battle.EndTurn();
            Assert.That(battle.EffectiveAttack, Is.EqualTo(138), "第 1 个回合末未到期");

            battle.EndTurn();
            Assert.That(battle.EffectiveAttack, Is.EqualTo(100), "回合数不吃等级:第 2 个回合末到期");
        }
    }
}
