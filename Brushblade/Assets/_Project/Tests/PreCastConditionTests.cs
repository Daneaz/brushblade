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
            Assert.That(detonated, Is.EqualTo(20), "引爆 = 1 层 × BurnPerStack 20;剩下的 200 是翻倍后的本体");
        }

        [Test]
        public void EnemySplitMidCast_CloneDoesNotSatisfyCondition()
        {
            // 第一个效果打出分裂,第二个效果 All + 灼烧条件:本体出字前带灼翻倍,
            // 分裂出的克隆出字前不存在,条件视为不满足。
            var def = new CharDef("序", Element.Heart, effects: new[]
            {
                new EffectDef(EffectKind.DamageSingle, 40),
                new EffectDef(EffectKind.DamageSingle, 100, doubleVs: DamageCondition.Burning, shape: TargetArea.All),
            });
            var b = Battle2(def);
            b.Enemies[0].Statuses.Apply(Burn());
            b.Cast("序", 0);
            Assert.That(b.Enemies.Count, Is.EqualTo(2), "分裂出了克隆");
            Assert.That(b.Enemies[0].Hp, Is.EqualTo(480 - 200), "本体出字前带灼,翻倍");
            Assert.That(b.Enemies[1].Hp, Is.EqualTo(480 - 100), "克隆出字前不存在,不翻倍");
        }

        private static BattleEngine Battle2(CharDef def) =>
            RebalanceFixture.Battle(RebalanceFixture.Graph(def), new[] { def.Id, def.Id },
                RebalanceFixture.Mob(hp: 1000, ability: EnemyAbility.Split));
    }
}
