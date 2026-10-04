using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>spec v7 §5.2 第 2 律:灼 ≤ 10 层,护盾 ≤ 最大生命。事件报实际入账量。</summary>
    public class CombatCapsEnforcementTests
    {
        [Test]
        public void Burn_IsCappedAtTen_EventReportsActualGain()
        {
            var def = new CharDef("燚", Element.Fire, effects: new[] { new EffectDef(EffectKind.BurnSingle, 7) });
            var b = RebalanceFixture.Battle(RebalanceFixture.Graph(def), new[] { "燚", "燚" }, RebalanceFixture.Mob());
            b.Cast("燚", 0);
            b.Cast("燚", 0);
            Assert.That(b.Enemies[0].Statuses.TotalMagnitude(StatusKind.Burn), Is.EqualTo(CombatCaps.BurnStacks));
            var burn = b.LastEvents.Last(e => e.Kind == BattleEventKind.Burn);
            Assert.That(burn.Amount, Is.EqualTo(3), "第二次只加得进 3 层");
        }

        [Test]
        public void Burn_AlreadyAtCap_EmitsNoBurnEvent()
        {
            var def = new CharDef("燚", Element.Fire, effects: new[] { new EffectDef(EffectKind.BurnSingle, 10) });
            var b = RebalanceFixture.Battle(RebalanceFixture.Graph(def), new[] { "燚", "燚" }, RebalanceFixture.Mob());
            b.Cast("燚", 0);
            int before = b.LastEvents.Count(e => e.Kind == BattleEventKind.Burn);
            Assert.That(before, Is.EqualTo(1));
            b.Cast("燚", 0);
            Assert.That(b.Enemies[0].Statuses.TotalMagnitude(StatusKind.Burn), Is.EqualTo(10));
            Assert.That(b.LastEvents.Any(e => e.Kind == BattleEventKind.Burn), Is.False, "满层再加,0 层不发事件");
        }

        [Test]
        public void PlayerShield_IsCappedAtMaxHp_EventReportsActualGain()
        {
            var def = new CharDef("垒", Element.Earth, effects: new[] { new EffectDef(EffectKind.Shield, 400) });
            var b = RebalanceFixture.Battle(RebalanceFixture.Graph(def), new[] { "垒", "垒" }, RebalanceFixture.Mob());
            b.Cast("垒", -1);
            int first = b.PlayerShield;
            b.Cast("垒", -1);
            Assert.That(b.PlayerShield, Is.EqualTo(RebalanceFixture.BaseMaxHp));
            var shield = b.LastEvents.Last(e => e.Kind == BattleEventKind.Shield);
            Assert.That(shield.Amount, Is.EqualTo(RebalanceFixture.BaseMaxHp - first));
        }

        [Test]
        public void SummonShield_IsCappedAtSummonMaxHp()
        {
            var summonDef = new CharDef("兵", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.Summon, 100, summonCount: 1, summonAttack: 3, summonChar: "甲") });
            var wall = new CharDef("垒", Element.Earth, effects: new[] { new EffectDef(EffectKind.Shield, 400) });
            var b = RebalanceFixture.Battle(RebalanceFixture.Graph(summonDef, wall),
                new[] { "兵", "垒", "垒" }, RebalanceFixture.Mob());
            b.Cast("兵");
            int maxHp = b.Summons[0].MaxHp;
            b.Cast("垒", allySlot: 0);
            b.Cast("垒", allySlot: 0);
            Assert.That(b.Summons[0].Shield, Is.EqualTo(maxHp));
            var shield = b.LastEvents.Last(e => e.Kind == BattleEventKind.Shield);
            Assert.That(shield.Amount, Is.LessThan(400));
        }
    }
}
