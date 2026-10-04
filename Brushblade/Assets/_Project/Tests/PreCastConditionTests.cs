using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>R3(spec v7 §10):条件按出字前的状态判定。同一张字里先挂的状态,
    /// 不会让后面的「目标带 X 时翻倍」生效。</summary>
    public class PreCastConditionTests
    {
        private static BattleEngine Battle(CharDef def) =>
            RebalanceFixture.Battle(RebalanceFixture.Graph(def), new[] { def.Id, def.Id },
                RebalanceFixture.Mob());

        private static StatusEffect Burn() => new StatusEffect
        {
            Kind = StatusKind.Burn, Polarity = StatusPolarity.Debuff, Magnitude = 1, TurnsLeft = -1,
        };

        [Test]
        public void StatusAppliedEarlierInSameCast_DoesNotSatisfyCondition()
        {
            var def = new CharDef("序", Element.Heart, effects: new[]
            {
                new EffectDef(EffectKind.Slow, 1),
                new EffectDef(EffectKind.DamageSingle, 100, doubleVs: DamageCondition.Controlled),
            });
            var b = Battle(def);
            b.Cast("序", 0);
            Assert.That(b.Enemies[0].Hp, Is.EqualTo(100000 - 100), "出字前没被控,不翻倍");
        }

        [Test]
        public void StatusPresentBeforeCast_SatisfiesCondition()
        {
            var def = new CharDef("序", Element.Heart, effects: new[]
            {
                new EffectDef(EffectKind.DamageSingle, 100, doubleVs: DamageCondition.Burning),
            });
            var b = Battle(def);
            b.Enemies[0].Statuses.Apply(Burn());
            b.Cast("序", 0);
            Assert.That(b.Enemies[0].Hp, Is.EqualTo(100000 - 200), "出字前带灼,翻倍");
        }

        [Test]
        public void ConditionLostDuringCast_StillCountsFromPreCastState()
        {
            // 第一个效果引爆清掉灼,第二个效果仍按出字前「带灼」翻倍
            var def = new CharDef("序", Element.Heart, effects: new[]
            {
                new EffectDef(EffectKind.Detonate, 0),
                new EffectDef(EffectKind.DamageSingle, 100, doubleVs: DamageCondition.Burning),
            });
            var b = Battle(def);
            b.Enemies[0].Statuses.Apply(Burn());
            int before = b.Enemies[0].Hp;
            b.Cast("序", 0);
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.Burn), Is.False, "引爆清层");
            int detonated = before - b.Enemies[0].Hp - 200;
            Assert.That(detonated, Is.GreaterThan(0), "引爆本身造成了伤害,剩下的 200 是翻倍后的本体");
        }
    }
}
