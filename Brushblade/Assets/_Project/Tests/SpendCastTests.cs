using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>spec C §6:释放前加层排在 Spend 之前;按本次释放层数的破甲 / 冻结 / 回澜 / 地动 / ≥8 ×2;0 层全不触发。
    /// 夹具:土字攻击面只有 SpendHeft 10 + 被测效果(没有 DamageSingle,不攒厚);攻击 100;靶子心属性、无甲。</summary>
    public class SpendCastTests
    {
        private static CharDef Quake(params EffectDef[] extra) => new("崩", Element.Earth,
            effects: new[] { new EffectDef(EffectKind.Shield, 10) },
            attackEffects: new[] { new EffectDef(EffectKind.SpendHeft, 10) }.Concat(extra).ToArray());

        private static StatusEffect Heft(int n) => new()
            { Kind = StatusKind.Heft, Polarity = StatusPolarity.Buff, Magnitude = n, TurnsLeft = -1 };

        private static BattleEngine Battle(CharDef def, int heft, IReadOnlyList<SummonSnapshot> summons = null) =>
            new(RebalanceFixture.Graph(def),
                new BattleConfig { PlayerMaxHp = RebalanceFixture.BaseMaxHp, PlayerAttack = 100, ApPerTurn = 20 },
                new[] { def.Id, def.Id }, Array.Empty<string>(), new[] { RebalanceFixture.Mob() }, seed: 1,
                startingSummons: summons, startingStatuses: heft > 0 ? new[] { Heft(heft) } : null);

        private static int Lost(BattleEngine b) => b.Enemies[0].MaxHp - b.Enemies[0].Hp;

        private static void Release(BattleEngine b) =>
            Assert.That(b.Cast("崩", -1, attackMode: true), Is.EqualTo(BattleError.None));

        [Test]
        public void Fold_HoistsSameResourceAddBeforeSpend()
        {
            var def = new CharDef("溃", Element.Water, effects: new[] { new EffectDef(EffectKind.HealAll, 10) },
                attackEffects: new[]
                {
                    new EffectDef(EffectKind.SpendWellspring, 10), new EffectDef(EffectKind.AddHeft, 2),
                    new EffectDef(EffectKind.AddWellspring, 3),
                });
            var kinds = TraitRules.CastEffects(def, CardFace.Attack, 1).Select(e => e.Kind).ToList();
            Assert.That(kinds, Is.EqualTo(new[] { EffectKind.AddWellspring, EffectKind.SpendWellspring, EffectKind.AddHeft }),
                "只挪同资源的加层;别的资源原位不动");
        }

        [Test]
        public void PreSpendAdd_CountsInThisRelease()
        {
            var b = Battle(Quake(new EffectDef(EffectKind.AddHeft, 3)), heft: 2);
            Release(b);
            Assert.That(Lost(b), Is.EqualTo(50), "(2 + 3) × 10");
            Assert.That(b.HeftStacks, Is.EqualTo(0), "加的层也被清空");
        }

        [Test]
        public void ArmorBreak_PerSpent()
        {
            var b = Battle(Quake(new EffectDef(EffectKind.ArmorBreak, 2, turns: 2, pick: EffectPick.All, scaleBy: ScaleBasis.Spent)), heft: 3);
            Release(b);
            Assert.That(b.Enemies[0].Statuses.TotalMagnitude(StatusKind.ArmorBreak), Is.EqualTo(6), "每层 2 × 3 层");
        }

        [TestCase(5, false)]
        [TestCase(6, true)]
        public void Freeze_OnlyAtMinSpent(int heft, bool frozen)
        {
            var b = Battle(Quake(new EffectDef(EffectKind.Freeze, 1, pick: EffectPick.All, minSpent: 6)), heft);
            Release(b);
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.Freeze), Is.EqualTo(frozen));
        }

        [TestCase(7, 70)]
        [TestCase(8, 160)]
        public void Doubles_AtEightOrMore(int heft, int lost)
        {
            var b = Battle(Quake(new EffectDef(EffectKind.Amplify, 100, scope: AmpScope.Spend, minSpent: 8)), heft);
            Release(b);
            Assert.That(Lost(b), Is.EqualTo(lost));
        }

        [Test]
        public void AmplifySpend_WithoutGate_AddsPercent()
        {
            var b = Battle(Quake(new EffectDef(EffectKind.Amplify, 30, scope: AmpScope.Spend)), heft: 5);
            Release(b);
            Assert.That(Lost(b), Is.EqualTo(65), "50 × 130%");
        }

        [Test]
        public void SpendHeal_HealsPercentOfReleaseDamage()
        {
            var b = Battle(Quake(new EffectDef(EffectKind.SpendHeal, 30)), heft: 5);
            b.DamagePlayerForTest(500);
            int before = b.PlayerHp;
            Release(b);
            Assert.That(b.PlayerHp - before, Is.EqualTo(15), "50 × 30%");
            Assert.That(b.WellspringStacks, Is.EqualTo(0), "回澜不攒泉");
        }

        [Test]
        public void SpendShield_GivesPlayerAndSummons()
        {
            var sprite = new SummonSnapshot { Slot = 0, Char = "木", Element = Element.Wood, Hp = 1000, MaxHp = 1000, Attack = 0, Speed = 100 };
            var b = Battle(Quake(new EffectDef(EffectKind.SpendShield, 50)), heft: 4, summons: new[] { sprite });
            Release(b);
            Assert.That(b.PlayerShield, Is.EqualTo(20), "每目标 40 × 50%");
            Assert.That(b.Summons[0].Shield, Is.EqualTo(20));
            Assert.That(b.HeftStacks, Is.EqualTo(0), "地动不攒厚");
        }

        [Test]
        public void ZeroStacks_NothingReadsSpent_Fires()   // Review Focus 3
        {
            var b = Battle(Quake(
                new EffectDef(EffectKind.ArmorBreak, 2, turns: 2, pick: EffectPick.All, scaleBy: ScaleBasis.Spent),
                new EffectDef(EffectKind.Freeze, 1, pick: EffectPick.All, minSpent: 1),
                new EffectDef(EffectKind.Slow, 2, pick: EffectPick.All, minSpent: 1),
                new EffectDef(EffectKind.SpendHeal, 30),
                new EffectDef(EffectKind.SpendShield, 50)), heft: 0);
            b.DamagePlayerForTest(500);
            int hp = b.PlayerHp;
            Release(b);
            var st = b.Enemies[0].Statuses;
            Assert.That(st.Has(StatusKind.ArmorBreak) || st.Has(StatusKind.Freeze) || st.Has(StatusKind.SpeedModifier), Is.False);
            Assert.That((b.PlayerHp, b.PlayerShield), Is.EqualTo((hp, 0)));
            Assert.That(b.LastEvents.Any(e => e.Kind == BattleEventKind.Damage || e.Kind == BattleEventKind.Heal
                || e.Kind == BattleEventKind.Shield), Is.False, "不发事件");
        }
    }
}
