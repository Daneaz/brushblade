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

        /// <summary>一张纯增益字 "增" 的战斗。敌人血量给大值 —— 增益字不打伤害,
        /// 但 EndTurn() 会让敌人反击,靶子不能被打死(死了 Phase 变 Won,EndTurn 就不再推进)。</summary>
        private static BattleEngine BuffBattle(EffectKind kind, int value, int turns)
        {
            var graph = RebalanceFixture.Graph(
                RebalanceFixture.Char("增", new EffectDef(kind, value, turns: turns)));
            return RebalanceFixture.Battle(graph, new[] { "增", "增", "增", "增" },
                RebalanceFixture.Mob());
        }

        /// <summary>回合数吃卡等级(2026-09-05,平衡重做 P0 任务 8)。
        ///
        /// 现有 ScaleByCardLevel 是「基础值 × (1 + 0.1 × (等级 − 1)),向上取整」——
        /// 那条给数值用没问题,给回合数用就太快了:2 回合的字在 6 级就变 3 回合、
        /// 11 级变 4 回合。回合数是**节奏**,每 5 级 +1 才合适。</summary>
        [TestCase(2, 1, 2)]
        [TestCase(2, 4, 2)]
        [TestCase(2, 5, 3)]
        [TestCase(2, 10, 4)]
        [TestCase(3, 1, 3)]
        [TestCase(3, 5, 4)]
        public void ScaleTurnsByCardLevel_AddsOnePerFiveLevels(int baseTurns, int level, int expected)
        {
            Assert.That(MetaRules.ScaleTurnsByCardLevel(baseTurns, level), Is.EqualTo(expected));
        }

        /// <summary>1 级恒等 —— 与 ScaleByCardLevel 同一条硬线。</summary>
        [Test]
        public void ScaleTurnsByCardLevel_LevelOne_IsIdentity()
        {
            Assert.That(MetaRules.ScaleTurnsByCardLevel(7, 1), Is.EqualTo(7));
            Assert.That(MetaRules.ScaleTurnsByCardLevel(0, 1), Is.EqualTo(0), "0 回合(=本场持久)不许被抬成 1");
        }

        /// <summary>卡等级缩放接进 BattleEngine 真实链路的回归(2026-09-05,任务 8)——
        /// 只测 MetaRules.ScaleTurnsByCardLevel 本身盖不住 BattleEngine 的 Empower/CritBuff
        /// case 有没有真的把它接上;这条走 Cast() / EndTurn() 才能抓住「函数写对了但没接线」。
        ///
        /// 5 级:ScaleTurnsByCardLevel(2, 5) = 3,基础 2 回合的 Empower 应撑到第 3 个回合末才到期。</summary>
        [Test]
        public void Empower_WithTurns_ScalesByCardLevel_ThroughRealCast()
        {
            var graph = RebalanceFixture.Graph(
                RebalanceFixture.Char("增", new EffectDef(EffectKind.Empower, 30, turns: 2)));
            var cardLevels = new Dictionary<string, int> { ["增"] = 5 };
            var battle = new BattleEngine(graph,
                new BattleConfig { PlayerMaxHp = RebalanceFixture.BaseMaxHp, PlayerAttack = 100 },
                new[] { "增", "增", "增", "增" }, Array.Empty<string>(),
                new[] { RebalanceFixture.Mob() }, seed: 1, cardLevels: cardLevels);

            battle.Cast("增");
            // Magnitude 也吃卡等级(ApplyEffects 的 ScaleByCardLevel,与回合数缩放是两条各自
            // 独立的系数):5 级 → ceil(30 × 1.4) = 42,基准 100 + 42 = 142。
            Assert.That(battle.EffectiveAttack, Is.EqualTo(142), "基准 100 + ScaleByCardLevel(30, 5)=42");

            battle.EndTurn();
            battle.EndTurn();
            Assert.That(battle.EffectiveAttack, Is.EqualTo(142),
                "5 级把 2 回合缩放成 3 回合 —— 第 2 个回合末不该到期");

            battle.EndTurn();
            Assert.That(battle.EffectiveAttack, Is.EqualTo(100), "第 3 个回合末到期");
        }
    }
}
