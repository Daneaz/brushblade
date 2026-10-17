using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>spec C §5 燃意:挂我方(玩家 / 木灵 / 我方全体),攻击 +X%,期间每段 DamageSingle / 木灵出手命中给敌人 +1 层灼;
    /// 同一单位取较高 % 与较长回合;Amplify All 放大燃意 %;灼 tick / 反击 / 非伤害效果不加灼。</summary>
    public class KindleTests
    {
        private static CharDef Ember(string id = "燃", int pct = 20, int turns = 2, EffectPick pick = EffectPick.Primary,
            params EffectDef[] extra) => new(id, Element.Fire,
            effects: new[] { new EffectDef(EffectKind.Kindle, pct, turns: turns, pick: pick) }.Concat(extra).ToArray(),
            attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 100) });

        private static SummonSnapshot Sprite(int attack = 50) => new()
        {
            Slot = 0, Char = "木", Element = Element.Wood, Hp = 100000, MaxHp = 100000, Attack = attack, Speed = 100,
        };

        private static BattleEngine Battle(CharDef[] defs, IReadOnlyList<SummonSnapshot> summons = null, params EnemyDef[] enemies) =>
            new(RebalanceFixture.Graph(defs),
                new BattleConfig { PlayerMaxHp = RebalanceFixture.BaseMaxHp, PlayerAttack = 100, ApPerTurn = 20 },
                defs.SelectMany(d => new[] { d.Id, d.Id, d.Id }).ToArray(), Array.Empty<string>(),
                enemies.Length > 0 ? enemies : new[] { RebalanceFixture.Mob() }, seed: 1, startingSummons: summons);

        private static int Burn(BattleEngine b, int i) => b.Enemies[i].Statuses.TotalMagnitude(StatusKind.Burn);

        [Test]
        public void OnPlayer_RaisesAttack_AndEachHitAddsOneBurn()
        {
            var b = Battle(new[] { Ember() });
            Assert.That(b.Cast("燃", -1, attackMode: false, allySlot: Targeting.PlayerTarget), Is.EqualTo(BattleError.None));
            var k = b.PlayerStatuses.Find(StatusKind.Kindle);
            Assert.That((k.Magnitude, k.TurnsLeft), Is.EqualTo((20, 2)));
            Assert.That(b.EffectiveAttack, Is.EqualTo(120));
            Assert.That(b.Cast("燃", 0, attackMode: true), Is.EqualTo(BattleError.None));
            Assert.That(b.Enemies[0].MaxHp - b.Enemies[0].Hp, Is.EqualTo(120), "100 × 120%");
            Assert.That(Burn(b, 0), Is.EqualTo(BattleEngine.KindleBurnPerHit));
            Assert.That(b.LastEvents.Any(e => e.Kind == BattleEventKind.Burn && e.TargetIndex == 0), Is.True);
        }

        [Test]
        public void MultiHit_AddsBurnPerHit()
        {
            var twin = new CharDef("连", Element.Fire, effects: new[] { new EffectDef(EffectKind.Cleanse, 0) },
                attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 10, hitCount: 3) });
            var b = Battle(new[] { Ember(), twin });
            b.Cast("燃", -1, attackMode: false, allySlot: Targeting.PlayerTarget);
            b.Cast("连", 0, attackMode: true);
            Assert.That(Burn(b, 0), Is.EqualTo(3));
        }

        [Test]
        public void OnSummon_RaisesItsAttack_NotPlayers_AndItsHitAddsBurn()
        {
            var b = Battle(new[] { Ember() }, new[] { Sprite() });
            Assert.That(b.Cast("燃", -1, attackMode: false, allySlot: 0), Is.EqualTo(BattleError.None));
            Assert.That(b.Summons[0].Statuses.Has(StatusKind.Kindle), Is.True);
            Assert.That(b.Summons[0].EffectiveAttack, Is.EqualTo(60), "50 × 120%");
            Assert.That(b.EffectiveAttack, Is.EqualTo(100), "玩家不受木灵燃意影响");
            b.EndTurn();
            Assert.That(b.LastEvents.Any(e => e.Kind == BattleEventKind.Burn && e.TargetIndex == 0), Is.True, "木灵命中加灼");
        }

        [Test]
        public void PlayerKindle_DoesNotLeakToSummons()
        {
            var b = Battle(new[] { Ember() }, new[] { Sprite() });
            b.Cast("燃", -1, attackMode: false, allySlot: Targeting.PlayerTarget);
            Assert.That(b.Summons[0].EffectiveAttack, Is.EqualTo(50));
        }

        [Test]
        public void AllAllies_GoesToPlayerAndEverySummon_NoAllyTargetNeeded()
        {
            var all = Ember(pick: EffectPick.AllAllies);
            Assert.That(BattleEngine.NeedsAllyTarget(all, false), Is.False);
            Assert.That(BattleEngine.NeedsTarget(all, false), Is.False);
            var b = Battle(new[] { all }, new[] { Sprite() });
            Assert.That(b.Cast("燃", -1, attackMode: false), Is.EqualTo(BattleError.None));
            Assert.That(b.PlayerStatuses.Has(StatusKind.Kindle), Is.True);
            Assert.That(b.Summons[0].Statuses.Has(StatusKind.Kindle), Is.True);
        }

        [Test]
        public void Reapply_TakesHigherPercent_AndLongerTurns()
        {
            var b = Battle(new[] { Ember("燃", 20, 2), Ember("烬", 15, 3) });
            b.Cast("燃", -1, attackMode: false, allySlot: Targeting.PlayerTarget);
            b.Cast("烬", -1, attackMode: false, allySlot: Targeting.PlayerTarget);
            var all = b.PlayerStatuses.All.Where(s => s.Kind == StatusKind.Kindle).ToList();
            Assert.That(all.Count, Is.EqualTo(1));
            Assert.That((all[0].Magnitude, all[0].TurnsLeft), Is.EqualTo((20, 3)));
        }

        [Test]
        public void AmplifyAll_AmplifiesKindlePercent()
        {
            var b = Battle(new[] { Ember(extra: new EffectDef(EffectKind.Amplify, 10, scope: AmpScope.All)) });
            b.Cast("燃", -1, attackMode: false, allySlot: Targeting.PlayerTarget);
            Assert.That(b.PlayerStatuses.Find(StatusKind.Kindle).Magnitude, Is.EqualTo(22), "20 × 110%");
        }

        [Test]
        public void NonDamageEffect_DoesNotTrigger_Kindle()   // Review Focus 1
        {
            var scorch = new CharDef("灼", Element.Fire, effects: new[] { new EffectDef(EffectKind.Cleanse, 0) },
                attackEffects: new[] { new EffectDef(EffectKind.BurnSingle, 3) });
            var b = Battle(new[] { Ember(), scorch });
            b.Cast("燃", -1, attackMode: false, allySlot: Targeting.PlayerTarget);
            b.Cast("灼", 0, attackMode: true);
            Assert.That(Burn(b, 0), Is.EqualTo(3), "只有 BurnSingle 的 3 层,燃意不加");
        }

        [Test]
        public void BurnTick_DoesNotTrigger_Kindle()   // Review Focus 1
        {
            var scorch = new CharDef("灼", Element.Fire, effects: new[] { new EffectDef(EffectKind.Cleanse, 0) },
                attackEffects: new[] { new EffectDef(EffectKind.BurnSingle, 3) });
            var b = Battle(new[] { Ember("燃", 20, 5), scorch });
            b.Cast("燃", -1, attackMode: false, allySlot: Targeting.PlayerTarget);
            b.Cast("灼", 0, attackMode: true);
            b.EndTurn();
            Assert.That(Burn(b, 0), Is.EqualTo(2), "结算一次 −1 层,不因燃意多加");
        }

        [Test]
        public void BlockCounter_DoesNotTrigger_Kindle()   // Review Focus 1
        {
            var guard = new CharDef("锋", Element.Fire,
                effects: new[] { new EffectDef(EffectKind.Block, 1, counterPercent: 30), new EffectDef(EffectKind.Kindle, 20, turns: 2) },
                attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 100) });
            var b = Battle(new[] { guard }, null, RebalanceFixture.Mob(attack: 300));   // 预算 300×60%(格挡前)= 180 > 反击 30
            Assert.That(b.Cast("锋", -1, attackMode: false, allySlot: Targeting.PlayerTarget), Is.EqualTo(BattleError.None));
            b.EndTurn();
            Assert.That(b.Enemies[0].MaxHp - b.Enemies[0].Hp, Is.GreaterThan(0), "夹具自检:反击打出");
            Assert.That(Burn(b, 0), Is.EqualTo(0));
        }

        [Test]
        public void Expires_AfterTurns()
        {
            var b = Battle(new[] { Ember("燃", 20, 1) });
            b.Cast("燃", -1, attackMode: false, allySlot: Targeting.PlayerTarget);
            b.EndTurn();
            Assert.That(b.PlayerStatuses.Has(StatusKind.Kindle), Is.False);
            Assert.That(b.EffectiveAttack, Is.EqualTo(100));
        }

        [Test]
        public void FeatureFace_LandsOnSelfAndSummons_EvenWithHostileAllPickTrait()
        {
            var smoke = new TraitDef(TraitSlot.Lv6, TraitFace.Feature, TraitForm.Passive, null, "烟熏",
                new[] { new EffectDef(EffectKind.Blind, 15, pick: EffectPick.All, riderOf: StatusKind.Burn) });
            var single = new CharDef("燃", Element.Fire,
                effects: new[] { new EffectDef(EffectKind.Kindle, 20, turns: 2) },
                attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 100) }, traits: new[] { smoke });
            var all = new CharDef("爆", Element.Fire,
                effects: new[] { new EffectDef(EffectKind.Kindle, 20, turns: 2, pick: EffectPick.AllAllies) },
                attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 100, shape: TargetArea.All) }, traits: new[] { smoke });
            foreach (var d in new[] { single, all })
                Assert.That(CardFaceRules.Landing(d, CardFace.Feature, 6), Is.EqualTo(FaceLanding.Self | FaceLanding.Summons), d.Id);
        }

        [Test]
        public void BurnRider_WithPickAll_RidesOnAnyBurningEnemy()
        {
            var scorch = new CharDef("灼", Element.Fire, effects: new[] { new EffectDef(EffectKind.Cleanse, 0) },
                attackEffects: new[] { new EffectDef(EffectKind.BurnSingle, 2) });
            var smoky = Ember(extra: new EffectDef(EffectKind.Blind, 15, pick: EffectPick.All, riderOf: StatusKind.Burn));
            var b = Battle(new[] { smoky, scorch }, null, RebalanceFixture.Mob(), RebalanceFixture.Mob());
            b.Cast("灼", 0, attackMode: true);
            b.Cast("燃", -1, attackMode: false, allySlot: Targeting.PlayerTarget);
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.Blind), Is.True, "带灼的敌人吃附着");
            Assert.That(b.Enemies[1].Statuses.Has(StatusKind.Blind), Is.False, "不带灼的不吃");
        }
            [Test]
        public void AttuneOnly_Face_NeedsAllyTarget()   // 追加裁定:Attune 要落到木灵,必须选友方
        {
            var gild = new CharDef("点", Element.Metal, effects: new[] { new EffectDef(EffectKind.Attune, 0) });
            Assert.That(BattleEngine.EffectNeedsAllyTarget(new EffectDef(EffectKind.Attune, 0)), Is.True);
            Assert.That(BattleEngine.NeedsAllyTarget(gild, false), Is.True);
        }

        [Test]
        public void KindleAllAllies_PlusAttune_Face_NeedsAllyTarget()   // 否则 Attune 拿不到木灵,静默空转
        {
            var face = new CharDef("引", Element.Fire, effects: new[]
            {
                new EffectDef(EffectKind.Kindle, 20, turns: 2, pick: EffectPick.AllAllies), new EffectDef(EffectKind.Attune, 0),
            }, attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 100) });
            Assert.That(BattleEngine.NeedsAllyTarget(face, false), Is.True);
        }
    }
}
