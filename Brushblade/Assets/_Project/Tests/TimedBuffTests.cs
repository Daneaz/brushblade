using System;
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

        /// <summary>一张纯增益字 "增" 的战斗。敌人血量给大值 —— 增益字不打伤害,
        /// 但 EndTurn() 会让敌人反击,靶子不能被打死(死了 Phase 变 Won,EndTurn 就不再推进)。</summary>
        private static BattleEngine BuffBattle(EffectKind kind, int value, int turns)
        {
            var graph = RebalanceFixture.Graph(
                RebalanceFixture.Char("增", new EffectDef(kind, value, turns: turns)));
            return RebalanceFixture.Battle(graph, new[] { "增", "增", "增", "增" },
                RebalanceFixture.Mob());
        }
    }
}
