using System;
using System.Collections.Generic;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>spec v7 §1:层数、回合、次数、击数不随卡等级涨。判据只有 MetaRules.ScalesWithCardLevel 一份。</summary>
    public class CardLevelDiscreteTests
    {
        [TestCase(EffectKind.Freeze)]
        [TestCase(EffectKind.Slow)]
        [TestCase(EffectKind.BurnSingle)]
        [TestCase(EffectKind.BurnAll)]
        [TestCase(EffectKind.Morale)]
        [TestCase(EffectKind.Immunity)]
        [TestCase(EffectKind.Revive)]
        [TestCase(EffectKind.Block)]
        [TestCase(EffectKind.Dispel)]
        [TestCase(EffectKind.ApBoost)]
        [TestCase(EffectKind.Charm)]
        public void DiscreteKinds_DoNotScale(EffectKind kind)
        {
            Assert.That(MetaRules.ScalesWithCardLevel(kind), Is.False);
            Assert.That(MetaRules.ScaleEffectValue(kind, 2, 10), Is.EqualTo(2));
        }

        [TestCase(EffectKind.DamageSingle)]
        [TestCase(EffectKind.Shield)]
        [TestCase(EffectKind.HealSelf)]
        [TestCase(EffectKind.Bleed)]
        [TestCase(EffectKind.Empower)]
        [TestCase(EffectKind.Blind)]
        public void ContinuousKinds_Scale(EffectKind kind)
        {
            Assert.That(MetaRules.ScalesWithCardLevel(kind), Is.True);
            Assert.That(MetaRules.ScaleEffectValue(kind, 100, 5), Is.EqualTo(MetaRules.ScaleByCardLevel(100, 5)));
        }

        [Test]
        public void Freeze_AtLevel10_StillOneTurn()
        {
            var def = new CharDef("冻", Element.Water, effects: new[] { new EffectDef(EffectKind.Freeze, 1) });
            var b = new BattleEngine(RebalanceFixture.Graph(def),
                new BattleConfig { PlayerMaxHp = RebalanceFixture.BaseMaxHp, PlayerAttack = 100 },
                new[] { "冻", "冻" }, Array.Empty<string>(), new[] { RebalanceFixture.Mob() }, seed: 1,
                cardLevels: new Dictionary<string, int> { ["冻"] = 10 });
            b.Cast("冻", 0);
            Assert.That(b.Enemies[0].Statuses.Find(StatusKind.Freeze).TurnsLeft, Is.EqualTo(1));
        }
    }
}
