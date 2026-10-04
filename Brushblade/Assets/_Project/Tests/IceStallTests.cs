using System;
using System.Collections.Generic;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>R1b(spec v7 §10):Boss 被冻结改为冰滞 —— 行动条后退半格,下次行动前受伤 +15%,
    /// 行动开始时解除并得霜抗 N。</summary>
    public class IceStallTests
    {
        private static readonly CharDef Freeze2 = new("冻", Element.Water,
            effects: new[] { new EffectDef(EffectKind.Freeze, 2) });
        private static readonly CharDef Hit = new("击", Element.Heart,
            effects: new[] { new EffectDef(EffectKind.DamageSingle, 100) });
        private static readonly CharDef Ember = new("燃", Element.Heart,
            effects: new[] { new EffectDef(EffectKind.BurnSingle, 1) });

        private static BattleEngine Battle() =>
            RebalanceFixture.Battle(RebalanceFixture.Graph(Freeze2, Hit, Ember),
                new[] { "冻", "击", "击", "冻", "燃", "燃" },
                RebalanceFixture.Boss(attack: 1));

        [Test]
        public void FreezeOnBoss_BecomesIceStall_NotFreeze()
        {
            var b = Battle();
            int meter = b.Enemies[0].ActionMeter;
            b.Cast("冻", 0);
            var bag = b.Enemies[0].Statuses;
            Assert.That(bag.Has(StatusKind.Freeze), Is.False);
            Assert.That(bag.Find(StatusKind.IceStall).Magnitude, Is.EqualTo(2));
            Assert.That(b.Enemies[0].ActionMeter, Is.EqualTo(meter - TurnScheduler.Threshold / 2));
        }

        [Test]
        public void IceStall_Adds15PercentDamageTaken_UntilNextAction()
        {
            var plain = Battle();
            plain.Cast("击", 0);
            int normal = 100000 - plain.Enemies[0].Hp;

            var b = Battle();
            b.Cast("冻", 0);
            b.Cast("击", 0);
            Assert.That(100000 - b.Enemies[0].Hp, Is.EqualTo(normal * 115 / 100));
        }

        [Test]
        public void BossAction_ClearsIceStall_AndGrantsFrostResist()
        {
            var b = Battle();
            b.Cast("冻", 0);
            b.EndTurn();
            var bag = b.Enemies[0].Statuses;
            Assert.That(bag.Has(StatusKind.IceStall), Is.False);
            Assert.That(bag.Find(StatusKind.FrostResist).TurnsLeft, Is.EqualTo(2), "N+1 挂上,本拍末尾减 1");
            b.Cast("冻", 0);
            Assert.That(bag.Has(StatusKind.IceStall), Is.False, "霜抗中不能再冰滞");
        }

        [Test]
        public void IceStall_RejectsSecondFreeze_NoSecondPush()
        {
            var b = Battle();
            b.Cast("冻", 0);
            int meter = b.Enemies[0].ActionMeter;
            b.Cast("冻", 0);
            Assert.That(b.Enemies[0].ActionMeter, Is.EqualTo(meter), "冰滞中再冻:不再后退行动条");
            Assert.That(b.Enemies[0].Statuses.Find(StatusKind.IceStall).Magnitude, Is.EqualTo(2));
        }

        [Test]
        public void NegativeMeter_DoesNotBreakScheduler()
        {
            var b = Battle();
            b.Enemies[0].ActionMeter = 0;     // 行动条在 0 时被推成 -5000
            b.Cast("冻", 0);
            Assert.That(b.Enemies[0].ActionMeter, Is.LessThan(0));
            Assert.That(b.Forecast(3).Count, Is.EqualTo(3), "预测照常给出");
            b.EndTurn();
            Assert.That(b.Phase, Is.Not.EqualTo(BattlePhase.Lost));
        }

        [Test]
        public void BurnTick_DoesNotTakeIceStallVulnerability()
        {
            // 灼烧不走 DamageEnemy,本来就不吃易伤;冰滞也在灼烧结算之前解除。
            // 两路都守:带冰滞挨灼烧,掉血等于不带冰滞。
            var plain = Battle();
            plain.Cast("燃", 0);
            int meter = plain.Enemies[0].ActionMeter;
            plain.EndTurn();
            int burnOnly = 100000 - plain.Enemies[0].Hp;
            Assert.That(burnOnly, Is.GreaterThan(0), "对照组确实被灼烧");

            var b = Battle();
            b.Cast("燃", 0);
            b.Cast("冻", 0);
            b.Enemies[0].ActionMeter = meter;   // 与对照同一行动时机,只比较灼烧那一拍
            b.EndTurn();
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.IceStall), Is.False, "这一拍已行动,冰滞解除");
            Assert.That(100000 - b.Enemies[0].Hp, Is.EqualTo(burnOnly));
        }

        [Test]
        public void IceStallAndNegativeMeter_SurviveSnapshotRoundTrip()
        {
            var b = Battle();
            b.Enemies[0].ActionMeter = 0;
            b.Cast("冻", 0);
            int meter = b.Enemies[0].ActionMeter;
            Assert.That(meter, Is.LessThan(0));
            var restored = BattleEngine.Restore(b.Capture(),
                RebalanceFixture.Graph(Freeze2, Hit, Ember),
                new BattleConfig { PlayerMaxHp = RebalanceFixture.BaseMaxHp, PlayerAttack = 100 }, null,
                new Dictionary<string, EnemyDef> { ["钧"] = RebalanceFixture.Boss(attack: 1) });
            Assert.That(restored.Enemies[0].ActionMeter, Is.EqualTo(meter));
            var stall = restored.Enemies[0].Statuses.Find(StatusKind.IceStall);
            Assert.That(stall, Is.Not.Null);
            Assert.That(stall.Magnitude, Is.EqualTo(2));
        }
    }
}
