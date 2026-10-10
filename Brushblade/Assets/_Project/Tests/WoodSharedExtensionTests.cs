using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>Plan D2-木 Task 1:共用小扩展 E37–E50(附录 §3)。
    ///
    /// 夹具口径同 EarthSharedExtensionTests:Element.Heart(生克 1.0×)、PlayerAttack = 100(攻击力缩放恒等)、
    /// 暴击率缺省 0(不摇号)。卡等级系数:Lv1 = 100、Lv4 = 118、Lv5 = 124、Lv6 = 130、Lv8 = 142。</summary>
    public class WoodSharedExtensionTests
    {
        private static TraitDef Trait(TraitSlot slot, TraitFace face, TraitForm form, params EffectDef[] effects) =>
            new(slot, face, form, null, "木" + (int)slot + face, effects);

        private static EffectDef Body(SummonPassive passive = null, int count = 1, int hp = 300, int attack = 20) =>
            new(EffectKind.Summon, hp, summonCount: count, summonAttack: attack, summonChar: "木", passive: passive);

        /// <summary>本命追加的写法:`Summon 1`(0 血/攻 0) + 本命字段。</summary>
        private static EffectDef Append(SummonPassive passive) => new(EffectKind.Summon, 0, summonAttack: 0, passive: passive);

        private static CharDef WoodChar(string id, IReadOnlyList<EffectDef> feature, IReadOnlyList<EffectDef> attack = null,
            params TraitDef[] traits) =>
            new(id, Element.Wood, effects: feature,
                attackEffects: attack ?? new[] { new EffectDef(EffectKind.DamageSingle, 100) }, traits: traits);

        private static EffectDef FirstOf(IEnumerable<EffectDef> effects, EffectKind kind) => effects.First(e => e.Kind == kind);

        private static BattleConfig Config => new() { PlayerMaxHp = RebalanceFixture.BaseMaxHp, PlayerAttack = 100 };

        private static SummonSnapshot Wood(int slot, string source = "林", string ch = "木", int hp = 100, int attack = 10) => new()
        {
            Slot = slot, Char = ch, SourceChar = source, Element = Element.Wood, Hp = hp, MaxHp = hp, Attack = attack, Speed = 100,
        };

        private static BattleEngine Battle(CharDef def, int level = 1, IReadOnlyList<SummonSnapshot> summons = null,
            params EnemyDef[] enemies) =>
            new(RebalanceFixture.Graph(def), Config, new[] { def.Id, def.Id, def.Id }, Array.Empty<string>(),
                enemies.Length > 0 ? enemies : new[] { RebalanceFixture.Mob() }, seed: 1,
                cardLevels: new Dictionary<string, int> { [def.Id] = level }, startingSummons: summons);

        private static int DamageOn(BattleEngine b, int enemy) =>
            b.LastEvents.Where(e => e.Kind == BattleEventKind.Damage && e.TargetIndex == enemy).Sum(e => e.Amount);

        // ---------------- E37:本命追加 ----------------

        [Test]
        public void E37_SummonZeroInNonReplacingTrait_MergesIntoBodySummonPassive()
        {
            var def = WoodChar("桂", new[] { Body(new SummonPassive { HealAlly = 3 }) }, null,
                Trait(TraitSlot.Lv4, TraitFace.Feature, TraitForm.Passive, Append(new SummonPassive { Thorns = 25, Taunt = true })));
            var lv1 = TraitRules.CastEffects(def, CardFace.Feature, 1);
            var lv4 = TraitRules.CastEffects(def, CardFace.Feature, 4);
            Assert.That(lv1, Has.Count.EqualTo(1));
            Assert.That(lv4, Has.Count.EqualTo(1), "不追加一条 0 血召唤");
            var summon = lv4[0];
            Assert.That(summon.Value, Is.EqualTo(300), "血 / 攻 / 只数不动");
            Assert.That(summon.SummonAttack, Is.EqualTo(20));
            Assert.That(summon.Passive.HealAlly, Is.EqualTo(3), "本体本命保留");
            Assert.That(summon.Passive.Thorns, Is.EqualTo(25), "追加的本命字段并入");
            Assert.That(summon.Passive.Taunt, Is.True);
            Assert.That(def.Effects[0].Passive.Thorns, Is.EqualTo(0), "纯函数:不改字表对象");
        }

        [Test]
        public void E37_NoBodySummonOnFace_IsNoOp()
        {
            // 攻面没有 Summon(本命追加写在两面):攻面空转,不追加 0 血召唤
            var def = WoodChar("桂", new[] { Body() }, null,
                Trait(TraitSlot.Lv4, TraitFace.Both, TraitForm.Passive, Append(new SummonPassive { Thorns = 25 })));
            var attack = TraitRules.CastEffects(def, CardFace.Attack, 4);
            Assert.That(attack.Select(e => e.Kind), Is.EqualTo(new[] { EffectKind.DamageSingle }));
            Assert.That(attack[0], Is.SameAs(def.AttackEffects[0]), "攻面逐项同一对象");
            Assert.That(TraitRules.CastEffects(def, CardFace.Feature, 4)[0].Passive.Thorns, Is.EqualTo(25));
        }

        [Test]
        public void E37_StacksWithLv3Enhancement()
        {
            var enhance = new TraitDef(TraitSlot.Lv3, TraitFace.Feature, TraitForm.Passive, TraitSlot.Lv1, "强化",
                new[] { Append(new SummonPassive { HealAllyTimes = 2 }) });
            var def = WoodChar("桂", new[] { Body(new SummonPassive { HealAlly = 3 }) }, null,
                enhance, Trait(TraitSlot.Lv6, TraitFace.Feature, TraitForm.Passive, Append(new SummonPassive { Dodge = 10 })));
            var p = TraitRules.CastEffects(def, CardFace.Feature, 6).Single().Passive;
            Assert.That((p.HealAlly, p.HealAllyTimes, p.Dodge), Is.EqualTo((3, 2, 10)));
        }

        [Test]
        public void E37_GraftCarriesAppendedPassive()
        {
            var def = WoodChar("桂", new[] { Body(new SummonPassive { HealAlly = 3 }) }, null,
                Trait(TraitSlot.Lv4, TraitFace.Feature, TraitForm.Passive, Append(new SummonPassive { Thorns = 25 })));
            var b = Battle(def, level: 4, summons: new[] { Wood(0) });
            Assert.That(b.Cast("桂", -1, allySlot: 0), Is.EqualTo(BattleError.None));
            Assert.That(b.LastEvents.Any(e => e.Kind == BattleEventKind.Graft), Is.True);
            Assert.That(b.Summons[0].Passive.Thorns, Is.EqualTo(25), "嫁接把追加的本命一并带走(Q3)");
            Assert.That(b.Summons[0].Passive.HealAlly, Is.EqualTo(3));
        }

        // ---------------- E39:ScaleBasis.SummonCount ----------------

        private static CharDef Crowd() => WoodChar("林", new[] { Body() },
            new[] { new EffectDef(EffectKind.DamageSingle, 100) },
            Trait(TraitSlot.Lv5, TraitFace.Attack, TraitForm.Active,
                new EffectDef(EffectKind.Amplify, 10, scope: AmpScope.Damage, scaleBy: ScaleBasis.SummonCount)));

        private static void Burn(BattleEngine b, int enemy) => b.Enemies[enemy].Statuses.Apply(new StatusEffect
            { Kind = StatusKind.Burn, Polarity = StatusPolarity.Debuff, Magnitude = 3, Potency = 100, TurnsLeft = -1, SourceId = "x" });

        [Test]
        public void E39_SummonCount_ScalesByAliveSummonsBeforeCast()
        {
            var b = Battle(Crowd(), level: 5, summons: new[] { Wood(0), Wood(1), Wood(2, ch: "苗") },
                RebalanceFixture.Mob(), RebalanceFixture.Mob());
            Assert.That(b.Cast("林", 0, attackMode: true), Is.EqualTo(BattleError.None));
            int baseDamage = MetaRules.ScaleByCardLevel(100, 5);
            Assert.That(DamageOn(b, 0), Is.EqualTo((baseDamage * 130 + 99) / 100), "3 只存活木灵(含幼苗)= +30%");
        }

        [Test]
        public void E39_SummonCount_IsNotBurningEnemyCount()
        {
            // 反向:0 只木灵、2 名带灼敌人 —— 漏写分支会静默落到「带灼敌人数」得 +20%
            var b = Battle(Crowd(), level: 5, summons: null, RebalanceFixture.Mob(), RebalanceFixture.Mob());
            Burn(b, 0);
            Burn(b, 1);
            Assert.That(b.Cast("林", 0, attackMode: true), Is.EqualTo(BattleError.None));
            Assert.That(DamageOn(b, 0), Is.EqualTo(MetaRules.ScaleByCardLevel(100, 5)), "没有木灵 = 不加成");

            // 反过来:1 只木灵、2 名带灼敌人 → +10%,不是 +20%
            var c = Battle(Crowd(), level: 5, summons: new[] { Wood(0) }, RebalanceFixture.Mob(), RebalanceFixture.Mob());
            Burn(c, 0);
            Burn(c, 1);
            c.Cast("林", 0, attackMode: true);
            int baseDamage = MetaRules.ScaleByCardLevel(100, 5);
            Assert.That(DamageOn(c, 0), Is.EqualTo((baseDamage * 110 + 99) / 100));
        }

        // ---------------- E40 / E45 / E48:Fold ----------------

        [Test]
        public void E40_ReshapeStrikeFields_AddUpOnFirstDamage()
        {
            var def = WoodChar("森", new[] { Body() }, null,
                Trait(TraitSlot.Lv5, TraitFace.Attack, TraitForm.Active, new EffectDef(EffectKind.Reshape, 0, summonAtkStrikePercent: 20,
                    hostAtkStrikePercent: 100, hostArmorStrikePercent: 50)),
                Trait(TraitSlot.Lv8, TraitFace.Attack, TraitForm.Active, new EffectDef(EffectKind.Reshape, 0, summonAtkStrikePercent: 30,
                    hostAtkStrikePercent: 200, hostArmorStrikePercent: 400)));
            var lv1 = FirstOf(TraitRules.CastEffects(def, CardFace.Attack, 1), EffectKind.DamageSingle);
            var lv8 = FirstOf(TraitRules.CastEffects(def, CardFace.Attack, 8), EffectKind.DamageSingle);
            Assert.That((lv1.SummonAtkStrikePercent, lv1.HostAtkStrikePercent, lv1.HostArmorStrikePercent), Is.EqualTo((0, 0, 0)));
            Assert.That((lv8.SummonAtkStrikePercent, lv8.HostAtkStrikePercent, lv8.HostArmorStrikePercent), Is.EqualTo((50, 300, 450)));
        }

        [Test]
        public void E45_AugmentCountOfCharm_WritesIntoCharmValue()
        {
            var def = new CharDef("花", Element.Wood, effects: new[] { Body() },
                attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 100), new EffectDef(EffectKind.Charm, 0, turns: 2) },
                traits: new[] { Trait(TraitSlot.Lv8, TraitFace.Attack, TraitForm.Active,
                    new EffectDef(EffectKind.Augment, 100, augmentKind: EffectKind.Charm, augmentField: AugmentField.Count)) });
            Assert.That(FirstOf(TraitRules.CastEffects(def, CardFace.Attack, 5), EffectKind.Charm).Value, Is.EqualTo(0));
            Assert.That(FirstOf(TraitRules.CastEffects(def, CardFace.Attack, 8), EffectKind.Charm).Value, Is.EqualTo(100));
        }

        [Test]
        public void E48_AugmentCountOfSummon_AndReshapeSummonPercent()
        {
            var def = WoodChar("林", new[] { Body() }, null,
                Trait(TraitSlot.Lv8, TraitFace.Feature, TraitForm.Active,
                    new EffectDef(EffectKind.Augment, 1, augmentKind: EffectKind.Summon, augmentField: AugmentField.Count),
                    new EffectDef(EffectKind.Reshape, 0, summonPercent: 60)));
            var lv5 = TraitRules.CastEffects(def, CardFace.Feature, 5).Single();
            Assert.That((lv5.SummonCount, lv5.SummonPercent), Is.EqualTo((1, 100)));
            Assert.That(lv5, Is.SameAs(def.Effects[0]));
            var lv8 = TraitRules.CastEffects(def, CardFace.Feature, 8).Single();
            Assert.That((lv8.SummonCount, lv8.SummonPercent, lv8.Value, lv8.SummonAttack), Is.EqualTo((2, 60, 300, 20)));
        }
    }
}
