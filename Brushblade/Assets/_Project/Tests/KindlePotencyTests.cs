using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>燃意加灼的火力(spec C G3 顾虑 1,controller 裁定改法 A):施加燃意时,本字的 Amplify scope Burn / All
    /// 按 AmpBurnPotency 同口径折进燃意记下的火力(StatusEffect.Potency);之后每次命中加灼直接用它。
    /// 起因:灿 Lv6 灿烂(燃面 `Amplify 50 scope Burn`)在燃面改燃意后 Lv6–7 无灼可放大,整条空转。</summary>
    public class KindlePotencyTests
    {
        private const int Level = 6;

        private static CharDef Ember(params EffectDef[] featureAmps) => new("燃", Element.Fire,
            effects: new[] { new EffectDef(EffectKind.Kindle, 20, turns: 3) },
            attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 100) },
            traits: featureAmps.Length == 0 ? null : new[]
            {
                new TraitDef(TraitSlot.Lv6, TraitFace.Feature, TraitForm.Passive, null, "测", featureAmps),
            });

        private static SummonSnapshot Sprite() => new()
        {
            Slot = 0, Char = "木", Element = Element.Wood, Hp = 100000, MaxHp = 100000, Attack = 50, Speed = 100,
        };

        private static BattleEngine Battle(CharDef def, IReadOnlyList<SummonSnapshot> summons = null) =>
            new(RebalanceFixture.Graph(new[] { def }),
                new BattleConfig { PlayerMaxHp = RebalanceFixture.BaseMaxHp, PlayerAttack = 100, ApPerTurn = 20 },
                new[] { def.Id, def.Id, def.Id }, Array.Empty<string>(), new[] { RebalanceFixture.Mob() }, seed: 1,
                cardLevels: new Dictionary<string, int> { [def.Id] = Level }, startingSummons: summons);

        /// <summary>与 BattleEngine.Amplified 同口径(向上取整)。</summary>
        private static int Amp(int potency, int percent) => (potency * (100 + percent) + 99) / 100;

        private static int Base => MetaRules.CardLevelPercent(Level);

        /// <summary>燃意挂玩家 → 攻击面一击 → 敌人身上那层灼的火力。</summary>
        private static int HitBurnPotency(CharDef def)
        {
            var b = Battle(def);
            Assert.That(b.Cast(def.Id, -1, attackMode: false, allySlot: Targeting.PlayerTarget), Is.EqualTo(BattleError.None));
            Assert.That(b.Cast(def.Id, 0, attackMode: true), Is.EqualTo(BattleError.None));
            var burn = b.Enemies[0].Statuses.Find(StatusKind.Burn);
            Assert.That(burn, Is.Not.Null, "前提:燃意命中加了灼");
            return burn.Potency;
        }

        [Test]
        public void NoAmplify_PotencyIsCardLevelPercent_Unchanged()
        {
            Assert.That(HitBurnPotency(Ember()), Is.EqualTo(Base), "没有 Amplify:火力逐位不变");
        }

        [Test]
        public void ScopeBurn_RaisesKindleBurnPotency_ByHalf()
        {
            int plain = HitBurnPotency(Ember());
            int amped = HitBurnPotency(Ember(new EffectDef(EffectKind.Amplify, 50, scope: AmpScope.Burn)));
            Assert.That(amped, Is.EqualTo(Amp(plain, 50)), "灿烂口径:× 1.5");
        }

        [Test]
        public void ScopeAll_AlsoRaisesKindleBurnPotency()
        {
            int amped = HitBurnPotency(Ember(new EffectDef(EffectKind.Amplify, 30, scope: AmpScope.All)));
            Assert.That(amped, Is.EqualTo(Amp(Base, 30)), "精进 / 先声一类 scope All 同样放大燃意火力");
        }

        [Test]
        public void OnSummon_PotencyFollowsTheCastingChar_NotTheHolder()
        {
            var def = Ember(new EffectDef(EffectKind.Amplify, 50, scope: AmpScope.Burn));
            var b = Battle(def, new[] { Sprite() });
            Assert.That(b.Cast(def.Id, -1, attackMode: false, allySlot: 0), Is.EqualTo(BattleError.None));
            Assert.That(b.Summons[0].Statuses.Find(StatusKind.Kindle).Potency, Is.EqualTo(Amp(Base, 50)));
            // 先垫 5 层火力 100 的灼:敌方回合开头的 tick 不会把木灵加的那层烧没;火力合并取大,读到的就是木灵那层的
            b.Enemies[0].Statuses.Apply(new StatusEffect
                { Kind = StatusKind.Burn, Polarity = StatusPolarity.Debuff, Magnitude = 5, TurnsLeft = -1, Potency = 100 });
            b.EndTurn();
            Assert.That(b.LastEvents.Any(e => e.Kind == BattleEventKind.Burn && e.TargetIndex == 0), Is.True, "前提:木灵命中加了灼");
            var burn = b.Enemies[0].Statuses.Find(StatusKind.Burn);
            Assert.That(burn.Potency, Is.EqualTo(Amp(Base, 50)), "木灵命中加灼的火力 = 施加那张字(Lv6 + 灿烂)的火力");
        }

        [Test]
        public void RealData_Can_Lv6_KindlePotencyRaisedByHalf()
        {
            var graph = CharTableTests.RealGraph();
            var b = new BattleEngine(graph,
                new BattleConfig { PlayerMaxHp = RebalanceFixture.BaseMaxHp, PlayerAttack = 100, ApPerTurn = 20, BossPhaseJitterPercent = 0 },
                new[] { "灿", "灿", "灿" }, Array.Empty<string>(), new[] { RebalanceFixture.Mob() }, seed: 1,
                cardLevels: new Dictionary<string, int> { ["灿"] = 6 });
            Assert.That(b.Cast("灿", -1, attackMode: false, allySlot: Targeting.PlayerTarget), Is.EqualTo(BattleError.None));
            Assert.That(b.PlayerStatuses.Find(StatusKind.Kindle).Potency, Is.EqualTo(Amp(MetaRules.CardLevelPercent(6), 50)),
                "灿 Lv6 灿烂(燃面 Amplify 50 scope Burn)放大燃意火力");
        }
    }
}
