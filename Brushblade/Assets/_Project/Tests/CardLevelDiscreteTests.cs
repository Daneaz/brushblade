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

        // 全枚举守卫:新增 EffectKind 必须在下面两个集合里显式归类(离散 / 连续),
        // 否则漏归类会悄悄落进 ScalesWithCardLevel 的 `_ => true` 而被等级放大。
        private static readonly HashSet<EffectKind> Discrete = new()
        {
            EffectKind.Freeze, EffectKind.Slow, EffectKind.BurnSingle, EffectKind.BurnAll,
            EffectKind.Morale, EffectKind.Immunity, EffectKind.Revive, EffectKind.Block,
            EffectKind.Dispel, EffectKind.ApBoost, EffectKind.Charm,
            // 修饰器(D1 Task 3):百分点按池档位定值、不吃卡等级;出字前被 Fold 折叠,不进结算
            EffectKind.Amplify, EffectKind.Reshape,
        };

        private static readonly HashSet<EffectKind> Continuous = new()
        {
            EffectKind.DamageSingle, EffectKind.Shield, EffectKind.ShieldAll, EffectKind.BurnPotency,
            EffectKind.HealSelf, EffectKind.Summon, EffectKind.Bleed, EffectKind.HealAll,
            EffectKind.HealOverTime, EffectKind.DefenseBuff, EffectKind.ArmorBreak, EffectKind.Cleanse,
            EffectKind.Blind, EffectKind.Silence, EffectKind.Reflect, EffectKind.BurnNoDecay,
            EffectKind.BurnSettleNow, EffectKind.Detonate, EffectKind.Empower, EffectKind.CritBuff,
            EffectKind.PierceBuff, EffectKind.SpendHeft, EffectKind.SpendWellspring, EffectKind.Quench,
            EffectKind.Haste, EffectKind.Unseal,
        };

        [Test]
        public void EveryEffectKind_IsClassified_AndDiscreteSetMatchesRule()
        {
            var discreteByRule = new HashSet<EffectKind>();
            foreach (EffectKind kind in Enum.GetValues(typeof(EffectKind)))
            {
                Assert.That(Discrete.Contains(kind) || Continuous.Contains(kind), Is.True,
                    $"EffectKind.{kind} 未归类:新增 kind 必须加进本测试的 Discrete 或 Continuous 集合,并核对 MetaRules.ScalesWithCardLevel");
                if (!MetaRules.ScalesWithCardLevel(kind)) discreteByRule.Add(kind);
            }
            Assert.That(Discrete.Overlaps(Continuous), Is.False, "同一 kind 不能同时算离散与连续");
            Assert.That(discreteByRule.SetEquals(Discrete), Is.True,
                "离散集合必须与 ScalesWithCardLevel 为 false 的集合一致");
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
