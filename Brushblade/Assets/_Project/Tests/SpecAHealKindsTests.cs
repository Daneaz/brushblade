using System;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>spec A §6.1 沐·沐霖(Lifesteal)/ 冰·冰肌(OverhealShield),D8。
    /// 夹具字无配方,IsComponent 缺省为 true 会被当部件直出 —— 显式 isComponent: false。</summary>
    public class SpecAHealKindsTests
    {
        private static BattleEngine Battle(CharDef def, params EnemyDef[] enemies) =>
            new(RebalanceFixture.Graph(def),
                new BattleConfig { PlayerMaxHp = RebalanceFixture.BaseMaxHp, PlayerAttack = 100 },
                new[] { def.Id, def.Id, def.Id }, Array.Empty<string>(),
                enemies.Length > 0 ? enemies : new[] { RebalanceFixture.Mob() }, seed: 1);

        private static CharDef Leech(int percent) => new("霖", Element.Heart,
            effects: new[] { new EffectDef(EffectKind.Cleanse, 0) },
            attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 100), new EffectDef(EffectKind.Lifesteal, percent) },
            isComponent: false);

        private static CharDef Ice(int percent) => new("肌", Element.Heart,
            effects: new[] { new EffectDef(EffectKind.HealSelf, 100), new EffectDef(EffectKind.OverhealShield, percent) },
            isComponent: false);

        [Test]
        public void Lifesteal_HealsPlayer_PercentOfThisCastsDirectDamage()
        {
            var b = Battle(Leech(20));
            b.DamagePlayerForTest(200);
            b.Cast("霖", 0, attackMode: true);
            Assert.That(b.PlayerHp, Is.EqualTo(RebalanceFixture.BaseMaxHp - 200 + 20));
        }

        [Test]
        public void Lifesteal_CountsActualDamage_NotOverkill()
        {
            var b = Battle(Leech(20), RebalanceFixture.Mob(hp: 50), RebalanceFixture.Mob());
            b.DamagePlayerForTest(200);
            b.Cast("霖", 0, attackMode: true);
            Assert.That(b.PlayerHp, Is.EqualTo(RebalanceFixture.BaseMaxHp - 200 + 10), "落地 50 × 20%");
        }

        [Test]
        public void Lifesteal_DoesNotGainWellspring()
        {
            var b = Battle(Leech(100));
            b.DamagePlayerForTest(200);
            b.Cast("霖", 0, attackMode: true);
            Assert.That(b.PlayerHp, Is.EqualTo(RebalanceFixture.BaseMaxHp - 100));
            Assert.That(b.PlayerStatuses.TotalMagnitude(StatusKind.Wellspring), Is.EqualTo(0));
        }

        [Test]
        public void OverhealShield_ConvertsOverflowToShield()
        {
            var b = Battle(Ice(100));
            b.DamagePlayerForTest(30);
            b.Cast("肌", -1);
            Assert.That(b.PlayerHp, Is.EqualTo(RebalanceFixture.BaseMaxHp));
            Assert.That(b.PlayerShield, Is.EqualTo(70));
        }

        [Test]
        public void OverhealShield_Percent_RoundsDown()
        {
            var b = Battle(Ice(50));
            b.DamagePlayerForTest(30);
            b.Cast("肌", -1);
            Assert.That(b.PlayerShield, Is.EqualTo(35));
        }

        [Test]
        public void OverhealShield_NoOverflow_NoShield()
        {
            var b = Battle(Ice(100));
            b.DamagePlayerForTest(200);
            b.Cast("肌", -1);
            Assert.That(b.PlayerShield, Is.EqualTo(0));
        }
    }
}
