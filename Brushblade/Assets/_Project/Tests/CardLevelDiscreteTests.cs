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
            // Augment(D1 Task 4):Value 是加几次 / 几回合 / 几跳,离散量;出字前被 Fold 折叠,不进结算
            EffectKind.Augment,
            // D1 Task 7:减伤是固定百分点(与 60% 非护甲减伤封顶直接相关,池词条写死数字)、反击增强是倍率、
            // 保命 Value 不用(一次性);治疗转盾 / 幼苗 / 群刺是对另一个已缩放量取百分比(再缩放就重复吃等级);
            // 加泉 / 加厚是层数
            EffectKind.DamageCut, EffectKind.CounterBoost, EffectKind.Endure, EffectKind.ShieldFromHeal,
            EffectKind.SummonSapling, EffectKind.SummonStrike, EffectKind.AddWellspring, EffectKind.AddHeft,
            // 净化(D1 Task 7 修复,Ruling 10):Value = 清几个减益,条数是离散量
            EffectKind.Cleanse,
            // 反震(D1 Task 9):Value = 反弹吸收量的百分比;吸收量本身已随护盾吃过等级
            EffectKind.ShieldRecoil,
            // 嘲讽(D2-0 Task 2):Value = 回合数(0 = 本场),离散量
            EffectKind.Taunt,
            // D2-火 Task 2:灼层翻倍的百分比作用于离散层数;拉平不用 Value
            EffectKind.BurnScale, EffectKind.BurnEqualize,
            // D2-火 Task 3:灼附着族 —— 上炎的 Value 是层数,其余不用 Value(焚城反应的 Value = 层数)
            EffectKind.HealBlock, EffectKind.BurnGrow, EffectKind.BurnHold, EffectKind.BurnBurst, EffectKind.BurnBacklash,
            // D2-火 Task 4:受击回敬的 Value = 每回合次数上限(回敬的效果触发时按来源字等级另行缩放)
            EffectKind.Retaliate,
            // D2-火 Task 5:追加一击的 Value = 本体百分比(本体伤害结算时另吃等级)、自损 = 百分比、解冻 / 揭示不用 Value
            EffectKind.ExtraStrike, EffectKind.SelfCost, EffectKind.Thaw, EffectKind.Reveal,
            // D2-金 Task 1:格挡修饰器 —— Value 不用,反击百分比 / 次数下限是离散量
            EffectKind.BlockMod,
            // D2-金 Task 3:致命的 Value = 回合数
            EffectKind.Doom,
            // D2-水 Task 2:冰水的 Value = 减速回合、冷却的 Value = 拍数
            EffectKind.ThawSlow, EffectKind.ChargeDelay,
        };

        private static readonly HashSet<EffectKind> Continuous = new()
        {
            EffectKind.DamageSingle, EffectKind.Shield, EffectKind.ShieldAll, EffectKind.BurnPotency,
            EffectKind.HealSelf, EffectKind.Summon, EffectKind.Bleed, EffectKind.HealAll,
            EffectKind.HealOverTime, EffectKind.DefenseBuff, EffectKind.ArmorBreak,
            EffectKind.Blind, EffectKind.Silence, EffectKind.Reflect, EffectKind.BurnNoDecay,
            EffectKind.BurnSettleNow, EffectKind.Detonate, EffectKind.Empower, EffectKind.CritBuff,
            EffectKind.PierceBuff, EffectKind.SpendHeft, EffectKind.SpendWellspring, EffectKind.Quench,
            EffectKind.Haste, EffectKind.Unseal,
            // 减攻(D1 Task 5):Value 是百分点,吃卡等级;Turns 不吃(读 effect.Turns)
            EffectKind.Weaken,
            // 种(D1 Task 6):Value 是每次回复量,吃卡等级;Turns 不吃
            EffectKind.Seed,
            // 标记(D1 Task 6):Value 是百分点,吃卡等级;Turns 不吃
            EffectKind.Vulnerable,
            // D1 Task 7:群疗 / 群盾的 Value 是回复量 / 护盾量(群疗 pct 模式下是百分比,同样随等级 —— 与 Weaken 百分点同口径)
            EffectKind.HealSummons, EffectKind.ShieldSummons,
            // D2-火 Task 4:埋雷的 Value 是伤害量,吃卡等级(出字时再乘攻击力定死)
            EffectKind.Mine,
            // D2-金 Task 4:战意族 —— 聚金 / 富甲 / 金气的 Value 是护盾量 / 护甲点数,连续
            EffectKind.MoraleOverflowShield, EffectKind.MoraleArmor, EffectKind.MoraleShield,
            // D2-水 Task 2:怀山 / 寒彻的 Value 是伤害量,吃卡等级(出字时再乘攻击力定死,同埋雷)
            EffectKind.FrostBite, EffectKind.ThawStrike,
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
