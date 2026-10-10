using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using Brushblade.Data;
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

        // ================= 提交 ②:结算分支 =================

        private static int Amp(int v, int percent) => (v * (100 + percent) + 99) / 100;

        // ---------------- E38:生面 Amplify 作用域 ----------------

        private static (int Hp, int Attack) SummonedStats(CharDef def, int level, int casts = 1)
        {
            var b = Battle(def, level);
            for (int i = 0; i < casts; i++) Assert.That(b.Cast(def.Id, -1, replaceSummon: true), Is.EqualTo(BattleError.None));
            var s = b.Summons.Last(x => x != null && x.Alive);
            return (s.MaxHp, s.Attack);
        }

        [Test]
        public void E38_FeatureFaceAmplifyAll_ScalesSummonHpAndAttack()
        {
            // 藤·精进:Amplify 12 All(两面),Lv4 生面召唤的血与攻都吃
            var plain = WoodChar("藤", new[] { Body() });
            var refined = WoodChar("藤", new[] { Body() }, null,
                Trait(TraitSlot.Lv4, TraitFace.Both, TraitForm.Passive, new EffectDef(EffectKind.Amplify, 12, scope: AmpScope.All)));
            var (hp, atk) = SummonedStats(plain, 4);
            Assert.That(SummonedStats(refined, 4), Is.EqualTo((Amp(hp, 12), Amp(atk, 12))));
            Assert.That(SummonedStats(refined, 3), Is.EqualTo(SummonedStats(plain, 3)), "Lv3 未解锁 = 原值");
        }

        [Test]
        public void E38_FirstCastCondition_OnlyFirstSummonOfTurnIsAmplified()
        {
            // 花·先声:Amplify 15 All if FirstCastThisTurn
            var plain = WoodChar("花", new[] { Body() });
            var herald = WoodChar("花", new[] { Body() }, null,
                Trait(TraitSlot.Lv4, TraitFace.Both, TraitForm.Passive,
                    new EffectDef(EffectKind.Amplify, 15, scope: AmpScope.All, onlyIf: DamageCondition.FirstCastThisTurn)));
            var (hp, atk) = SummonedStats(plain, 4);
            Assert.That(SummonedStats(herald, 4), Is.EqualTo((Amp(hp, 15), Amp(atk, 15))), "本回合第一张");
            Assert.That(SummonedStats(herald, 4, casts: 2), Is.EqualTo((hp, atk)), "第二张不吃");
        }

        [Test]
        public void E38_HealAndShieldSummons_InScope_SaplingNot()
        {
            Assert.That(TraitRules.InScope(AmpScope.All, EffectKind.Summon), Is.True);
            Assert.That(TraitRules.InScope(AmpScope.All, EffectKind.HealSummons), Is.True);
            Assert.That(TraitRules.InScope(AmpScope.All, EffectKind.ShieldSummons), Is.True);
            Assert.That(TraitRules.InScope(AmpScope.All, EffectKind.SummonSapling), Is.False, "幼苗百分比不吃(Q5)");
            Assert.That(TraitRules.InScope(AmpScope.All, EffectKind.Taunt), Is.False);
            Assert.That(TraitRules.InScope(AmpScope.All, EffectKind.Endure), Is.False);
            Assert.That(TraitRules.InScope(AmpScope.Heal, EffectKind.HealSummons), Is.False, "只扩 All,不动 Heal / Shield 档");
            Assert.That(TraitRules.InScope(AmpScope.Shield, EffectKind.ShieldSummons), Is.False);

            int Shielded(bool amp)
            {
                var traits = amp ? new[] { Trait(TraitSlot.Lv4, TraitFace.Feature, TraitForm.Passive,
                    new EffectDef(EffectKind.Amplify, 20, scope: AmpScope.All)) } : Array.Empty<TraitDef>();
                var def = WoodChar("荫", new[] { new EffectDef(EffectKind.ShieldSummons, 50) }, null, traits);
                var b = Battle(def, 4, new[] { Wood(0) });
                b.Cast("荫");
                return b.Summons[0].Shield;
            }
            Assert.That(Shielded(true), Is.EqualTo(Amp(Shielded(false), 20)));

            int Healed(bool amp)
            {
                var traits = amp ? new[] { Trait(TraitSlot.Lv4, TraitFace.Feature, TraitForm.Passive,
                    new EffectDef(EffectKind.Amplify, 20, scope: AmpScope.All)) } : Array.Empty<TraitDef>();
                var def = WoodChar("疗", new[] { new EffectDef(EffectKind.HealSummons, 30) }, null, traits);
                var w = Wood(0, hp: 1000);
                w.Hp = 100;
                var b = Battle(def, 4, new[] { w });
                b.Cast("疗");
                return b.Summons[0].Hp - 100;
            }
            Assert.That(Healed(true), Is.EqualTo(Amp(Healed(false), 20)));
        }

        // ---------------- E40:按木灵攻击 / 护甲加伤 ----------------

        private static CharDef Striker(EffectDef reshape) => WoodChar("森", new[] { Body() },
            new[] { new EffectDef(EffectKind.DamageSingle, 100) },
            Trait(TraitSlot.Lv5, TraitFace.Attack, TraitForm.Active, reshape));

        [Test]
        public void E40_SummonAtkStrike_AddsSumOfAliveSummonAttack()
        {
            var b = Battle(Striker(new EffectDef(EffectKind.Reshape, 0, summonAtkStrikePercent: 50)), 5,
                new[] { Wood(0, attack: 30), Wood(1, source: "藻", attack: 10), Wood(2, ch: "苗", attack: 20) });
            b.Cast("森", 0, attackMode: true);
            Assert.That(DamageOn(b, 0), Is.EqualTo(MetaRules.ScaleByCardLevel(100, 5) + (30 + 10 + 20) * 50 / 100));
        }

        [Test]
        public void E40_HostStrikes_ReadStrongestOwnSummonOnly()
        {
            var own = Wood(0, source: "森", attack: 40);
            own.Statuses = new List<StatusEffect>
                { new() { Kind = StatusKind.DefenseBuff, Polarity = StatusPolarity.Buff, Magnitude = 12, TurnsLeft = -1, SourceId = "森#armor" } };
            var weak = Wood(1, source: "森", attack: 10);
            var foreign = Wood(2, source: "林", attack: 99);
            var sprout = Wood(3, source: "森", ch: "苗", attack: 90);
            var b = Battle(Striker(new EffectDef(EffectKind.Reshape, 0, hostAtkStrikePercent: 200, hostArmorStrikePercent: 400)), 5,
                new[] { own, weak, foreign, sprout });
            b.Cast("森", 0, attackMode: true);
            Assert.That(DamageOn(b, 0), Is.EqualTo(MetaRules.ScaleByCardLevel(100, 5) + 40 * 200 / 100 + 12 * 400 / 100),
                "本字木灵(非幼苗)取最大:攻 40、护甲 12;别家木灵与幼苗不算");

            var none = Battle(Striker(new EffectDef(EffectKind.Reshape, 0, hostAtkStrikePercent: 200)), 5, new[] { foreign });
            none.Cast("森", 0, attackMode: true);
            Assert.That(DamageOn(none, 0), Is.EqualTo(MetaRules.ScaleByCardLevel(100, 5)), "本字木灵不在场 = 0");
        }

        // ---------------- E41 / E47:幼苗取本体 / 小藻 ----------------

        [Test]
        public void E41_SaplingFromBody_WhenNothingSummonedThisCast()
        {
            var def = WoodChar("林", new[] { Body(hp: 400, attack: 30) },
                new[] { new EffectDef(EffectKind.DamageSingle, 100), new EffectDef(EffectKind.SummonSapling, 20, summonCount: 2, fromBody: true) });
            var b = Battle(def, 5);
            b.Cast("林", 0, attackMode: true);
            var saplings = b.Summons.Where(x => x != null && x.Alive).ToList();
            Assert.That(saplings, Has.Count.EqualTo(2));
            Assert.That(saplings.All(x => x.Char == "苗" && x.Element == Element.Wood && x.SourceChar == "林"), Is.True);
            Assert.That(saplings[0].MaxHp, Is.EqualTo(MetaRules.ScaleByCardLevel(400, 5) * 20 / 100));
            Assert.That(saplings[0].Attack, Is.EqualTo(MetaRules.ScaleByCardLevel(30, 5) * 20 / 100));

            var plain = WoodChar("林", new[] { Body(hp: 400, attack: 30) },
                new[] { new EffectDef(EffectKind.DamageSingle, 100), new EffectDef(EffectKind.SummonSapling, 20, summonCount: 2) });
            var c = Battle(plain, 5);
            c.Cast("林", 0, attackMode: true);
            Assert.That(c.Summons.Any(x => x != null && x.Alive), Is.False, "不写 fromBody:原口径空转");
        }

        [Test]
        public void E47_SproutSapling_MarksParentSlot()
        {
            var def = WoodChar("藻", new[] { Body(hp: 400, attack: 30), new EffectDef(EffectKind.SummonSapling, 30, summonCount: 2, sprout: true) });
            var b = Battle(def, 1, new[] { Wood(0) });
            b.Cast("藻");
            var host = b.Summons.ToList().FindIndex(x => x != null && x.Char == "木" && x.SourceChar == "藻");
            var sprouts = b.Summons.Where(x => x != null && x.Char == "苗").ToList();
            Assert.That(host, Is.GreaterThanOrEqualTo(0));
            Assert.That(sprouts, Has.Count.EqualTo(2));
            Assert.That(sprouts.All(x => x.SproutParentSlot == host), Is.True, "小藻记母体槽位");

            var plain = WoodChar("藻", new[] { Body(hp: 400, attack: 30), new EffectDef(EffectKind.SummonSapling, 30, summonCount: 2) });
            var c = Battle(plain, 1);
            c.Cast("藻");
            Assert.That(c.Summons.Where(x => x != null && x.Char == "苗").All(x => x.SproutParentSlot == -1), Is.True, "缺省不记");
        }

        // ---------------- E42:嫁接算本次召出 ----------------

        [Test]
        public void E42_Graft_CountsAsSummonedThisCast()
        {
            var def = WoodChar("柘", new[] { Body(), new EffectDef(EffectKind.Taunt, 2, pick: EffectPick.SummonedThisCast) });
            var b = Battle(def, 1, new[] { Wood(0) });
            Assert.That(b.Cast("柘", -1, allySlot: 0), Is.EqualTo(BattleError.None));
            Assert.That(b.LastEvents.Any(e => e.Kind == BattleEventKind.Graft), Is.True);
            Assert.That(b.Summons[0].Statuses.Has(StatusKind.Taunt), Is.True, "被嫁接者吃到 pick SummonedThisCast 的嘲讽");
        }

        // ---------------- E43:复活全部 / 按百分比 ----------------

        [Test]
        public void E43_ReviveZero_RevivesAllCorpses_WithPercentHp()
        {
            var def = WoodChar("森", new[] { new EffectDef(EffectKind.Revive, 0, reviveHpPercent: 30) });
            var b = Battle(def, 1, new[] { Wood(0, hp: 101), Wood(1, hp: 200), Wood(2, hp: 50) });
            b.Summons[0].Hp = 0;
            b.Summons[1].Hp = 0;
            Assert.That(b.Cast("森"), Is.EqualTo(BattleError.None));
            Assert.That(b.Summons[0].Hp, Is.EqualTo(31), "⌈101 × 30%⌉");
            Assert.That(b.Summons[1].Hp, Is.EqualTo(60));
            Assert.That(b.Summons[2].Hp, Is.EqualTo(50), "活着的不动");

            var half = WoodChar("森", new[] { new EffectDef(EffectKind.Revive, 1) });
            var c = Battle(half, 1, new[] { Wood(0, hp: 101), Wood(1, hp: 200) });
            c.Summons[0].Hp = 0;
            c.Summons[1].Hp = 0;
            c.Cast("森");
            Assert.That((c.Summons[0].Hp, c.Summons[1].Hp), Is.EqualTo((51, 0)), "缺省:救 1 只、半血");
        }

        // ---------------- E45:魅惑反打加成 ----------------

        [Test]
        public void E45_CharmMagnitude_DoublesCounterAttack()
        {
            int VictimDamage(int charmValue)
            {
                var def = RebalanceFixture.Char("花", new EffectDef(EffectKind.Charm, charmValue, turns: 1));
                var b = Battle(def, 1, null, RebalanceFixture.Mob(attack: 100), RebalanceFixture.Mob(attack: 0));
                b.Cast("花", 0);
                Assert.That(b.Enemies[0].Statuses.Find(StatusKind.Charm).Magnitude, Is.EqualTo(charmValue > 0 ? 100 + charmValue : 1));
                b.EndTurn();
                return 100000 - b.Enemies[1].Hp;
            }
            Assert.That(VictimDamage(0), Is.EqualTo(100), "缺省 Magnitude 1 → ×1");
            Assert.That(VictimDamage(100), Is.EqualTo(200), "移花接木:Magnitude 200 → ×2");
        }

        // ---------------- E46:柘弓 own / byArmor ----------------

        private static StatusEffect EntryArmor(int n, string id) => new()
            { Kind = StatusKind.DefenseBuff, Polarity = StatusPolarity.Buff, Magnitude = n, TurnsLeft = -1, SourceId = id };

        [Test]
        public void E46_SummonStrikeOwnByArmor_StrongestOwnShootsOnce()
        {
            var strong = Wood(0, source: "柘", attack: 5);
            strong.Statuses = new List<StatusEffect> { EntryArmor(20, "柘#armor") };
            var weak = Wood(1, source: "柘", attack: 50);
            weak.Statuses = new List<StatusEffect> { EntryArmor(8, "柘#a2") };
            var foreign = Wood(2, source: "林", attack: 70);
            var def = WoodChar("柘", new[] { Body() },
                new[] { new EffectDef(EffectKind.DamageSingle, 100), new EffectDef(EffectKind.SummonStrike, 200, ownSummons: true, byArmor: true) });
            var b = Battle(def, 5, new[] { strong, weak, foreign });
            b.Cast("柘", 0, attackMode: true);
            var strikes = b.LastEvents.Where(e => e.Kind == BattleEventKind.SummonAttack).ToList();
            Assert.That(strikes.Select(e => e.SecondIndex), Is.EqualTo(new[] { 0 }), "只最强一只本字木灵出手");
            Assert.That(DamageOn(b, 0), Is.EqualTo(MetaRules.ScaleByCardLevel(100, 5) + 20 * 200 / 100), "基数 = 护甲 20 × 200%");

            var all = WoodChar("柘", new[] { Body() },
                new[] { new EffectDef(EffectKind.DamageSingle, 100), new EffectDef(EffectKind.SummonStrike, 200) });
            var c = Battle(all, 5, new[] { Wood(0, attack: 5), Wood(1, attack: 50) });
            c.Cast("柘", 0, attackMode: true);
            Assert.That(c.LastEvents.Count(e => e.Kind == BattleEventKind.SummonAttack), Is.EqualTo(2), "缺省:全部木灵各出手");
        }

        // ---------------- E48:召唤改形 ----------------

        [Test]
        public void E48_SummonPercent_TwoAt60Percent()
        {
            var plain = WoodChar("林", new[] { Body(hp: 500, attack: 40) });
            var stand = WoodChar("林", new[] { Body(hp: 500, attack: 40) }, null,
                Trait(TraitSlot.Lv8, TraitFace.Feature, TraitForm.Active,
                    new EffectDef(EffectKind.Augment, 1, augmentKind: EffectKind.Summon, augmentField: AugmentField.Count),
                    new EffectDef(EffectKind.Reshape, 0, summonPercent: 60)));
            var (hp, atk) = SummonedStats(plain, 8);
            var b = Battle(stand, 8);
            b.Cast("林");
            var alive = b.Summons.Where(x => x != null && x.Alive).ToList();
            Assert.That(alive, Has.Count.EqualTo(2));
            Assert.That(alive.All(x => x.MaxHp == hp * 60 / 100 && x.Attack == atk * 60 / 100), Is.True);
        }

        // ---------------- E50:回合脉冲的条件门 ----------------

        private static CharDef Catalpa() => WoodChar("楸", new[]
        {
            new EffectDef(EffectKind.TurnPulse, 0, turns: 3, perHit: new[]
            {
                new EffectDef(EffectKind.Vulnerable, 30, turns: 1, pick: EffectPick.Random, onlyIf: DamageCondition.OwnSummonAlive),
            }),
        });

        [Test]
        public void E50_TurnPulseVulnerable_OnlyWhileOwnSummonAlive()
        {
            Assert.That(BattleEngine.TurnPulseAllows(EffectKind.Vulnerable), Is.True);
            var with = Battle(Catalpa(), 1, new[] { Wood(0, source: "楸", hp: 5000) });
            with.Cast("楸");
            with.EndTurn();
            Assert.That(with.Enemies[0].Statuses.Has(StatusKind.Vulnerable), Is.True, "楸灵在场:标记");

            var without = Battle(Catalpa(), 1, new[] { Wood(0, source: "林", hp: 5000), Wood(1, source: "楸", ch: "苗", hp: 5000) });
            without.Cast("楸");
            without.EndTurn();
            Assert.That(without.Enemies[0].Statuses.Has(StatusKind.Vulnerable), Is.False, "楸灵不在场(别家木灵 / 楸的幼苗不算):空转");
        }

        [Test]
        public void E50_OpeningEffectOnlyIf_ClonesAndSurvivesRealSaveFile()
        {
            var o = new OpeningEffect { Kind = EffectKind.Vulnerable, OnlyIf = DamageCondition.OwnSummonAlive };
            Assert.That(o.Clone().OnlyIf, Is.EqualTo(DamageCondition.OwnSummonAlive));
            Assert.That(o.ToEffect().OnlyIf, Is.EqualTo(DamageCondition.OwnSummonAlive));
            Assert.That(OpeningEffect.Of(new EffectDef(EffectKind.Weaken, 10), "x", Element.Wood).OnlyIf, Is.EqualTo(DamageCondition.None));

            var def = Catalpa();
            var graph = RebalanceFixture.Graph(def);
            var enemy = RebalanceFixture.Mob(attack: 0);
            var runConfig = new RunConfig { Encounters = new[] { new[] { enemy }, new[] { enemy } }, RewardPool = new[] { "楸" } };
            var levels = new Dictionary<string, int> { ["楸"] = 1 };
            var run = new RunEngine(graph, runConfig, Config, new[] { "楸", "楸" }, Array.Empty<string>(), 3, cardLevels: levels);
            Assert.That(run.Battle.Cast("楸"), Is.EqualTo(BattleError.None));
            var meta = new MetaState
            {
                EndlessV2 = new EndlessSaveState
                {
                    Depth = 3, Seed = 999,
                    InProgress = new InProgressRun { FromDepth = 1, FirstTowerSegment = true, Run = run.Capture() },
                },
            };
            var reloaded = SaveSerializer.FromJson(SaveSerializer.ToJson(meta));
            var restored = RunEngine.Restore(reloaded.EndlessV2.InProgress.Run, graph, runConfig, Config, levels);
            Assert.That(restored.Battle.PlayerStatuses.Find(StatusKind.TurnPulse).OnHit[0].OnlyIf,
                Is.EqualTo(DamageCondition.OwnSummonAlive), "OnlyIf 进存档 JSON 往返");
        }

        [Test]
        public void E50_OwnSummonAlive_IsTargetIndependentSnapshotCondition()
        {
            Assert.That((int)DamageCondition.OwnSummonAlive, Is.EqualTo(18));
            // 生面 Amplify 带 if OwnSummonAlive:本字木灵在场才加成(与目标无关,不选敌的召唤也判得到)
            var def = WoodChar("楸", new[] { Body() }, null,
                Trait(TraitSlot.Lv4, TraitFace.Feature, TraitForm.Passive,
                    new EffectDef(EffectKind.Amplify, 50, scope: AmpScope.All, onlyIf: DamageCondition.OwnSummonAlive)));
            var (hp, _) = SummonedStats(WoodChar("楸", new[] { Body() }), 4);
            var b = Battle(def, 4, new[] { Wood(0, source: "楸") });
            b.Cast("楸");
            Assert.That(b.Summons[1].MaxHp, Is.EqualTo(Amp(hp, 50)), "楸灵在场");
            var c = Battle(def, 4, new[] { Wood(0, source: "林") });
            c.Cast("楸");
            Assert.That(c.Summons[1].MaxHp, Is.EqualTo(hp), "楸灵不在场");
        }

        // ================= 提交 ③:携带与次数、管线 =================

        // ---------------- E44:木灵限时状态不跨场(Ruling 16)----------------

        [Test]
        public void E44_TimedSummonStatuses_AreNotCarried_EntryArmorAndBurnAre()
        {
            StatusEffect Timed(StatusKind kind, int turns, string id) => new()
                { Kind = kind, Polarity = StatusPolarity.Buff, Magnitude = 9, TurnsLeft = turns, SourceId = id };
            var wood = Wood(1, hp: 900);
            wood.Passive = new SummonPassive { Armor = 5 };
            wood.Statuses = new List<StatusEffect>
            {
                new() { Kind = StatusKind.Burn, Polarity = StatusPolarity.Debuff, Magnitude = 9, Potency = 100, TurnsLeft = -1, SourceId = "burn" },
                EntryArmor(7, "塔"),            // SummonDefense 裸 ID
                EntryArmor(5, "桩#armor"),      // 坚木本命
                Timed(StatusKind.AttackBuff, 3, "剡"),
                Timed(StatusKind.DefenseBuff, 4, "铠#7"),
                Timed(StatusKind.Reflect, 5, "壁"),
            };
            var config = new RunConfig
            {
                Encounters = new[] { new[] { RebalanceFixture.Mob(hp: 20) }, new[] { RebalanceFixture.Mob(hp: 20) } },
                RewardPool = new[] { "甲" },
            };
            var run = new RunEngine(RebalanceFixture.Graph(), config, Config, new[] { "甲", "甲" }, Array.Empty<string>(), seed: 1,
                startingSummons: new[] { wood });
            Assert.That(run.Battle.Cast("甲", 0), Is.EqualTo(BattleError.None));
            Assert.That(run.Battle.Phase, Is.EqualTo(BattlePhase.Won));
            run.AdvanceAfterBattle();
            var carried = run.CarriedSummons.Single().Statuses;
            Assert.That(carried.Select(st => st.SourceId).OrderBy(x => x, StringComparer.Ordinal).ToArray(),
                Is.EqualTo(new[] { "burn", "塔", "桩#armor" }), "限时状态剥离;Burn −1 与两种入场护甲照常携带");
        }

        // ---------------- E49:battleLimit 放行击杀时 ----------------

        private static CharDef Laurel() => new("桂", Element.Wood,
            effects: new[] { Body() },
            attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 100, shape: TargetArea.All) },
            traits: new[] { new TraitDef(TraitSlot.Lv5, TraitFace.Attack, TraitForm.Passive, null, "折桂",
                new[] { new EffectDef(EffectKind.Morale, 1) }, trigger: TraitTrigger.OnKill, maxPerBattle: 1) });

        [Test]
        public void E49_OnKillBattleLimit_SecondKillNotEnqueued()
        {
            var b = Battle(Laurel(), 5, null, RebalanceFixture.Mob(hp: 10), RebalanceFixture.Mob(hp: 10), RebalanceFixture.Mob(hp: 100000));
            Assert.That(b.Cast("桂", 0, attackMode: true), Is.EqualTo(BattleError.None));
            Assert.That(b.Enemies.Count(e => !e.Alive), Is.EqualTo(2), "夹具前提:一次出字两次击杀");
            Assert.That(b.PlayerStatuses.TotalMagnitude(StatusKind.Morale), Is.EqualTo(1), "每场 1 次:第二次击杀不再入队");

            var unlimited = new CharDef("桂", Element.Wood, effects: new[] { Body() },
                attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 100, shape: TargetArea.All) },
                traits: new[] { new TraitDef(TraitSlot.Lv5, TraitFace.Attack, TraitForm.Passive, null, "折桂",
                    new[] { new EffectDef(EffectKind.Morale, 1) }, trigger: TraitTrigger.OnKill) });
            var c = Battle(unlimited, 5, null, RebalanceFixture.Mob(hp: 10), RebalanceFixture.Mob(hp: 10), RebalanceFixture.Mob(hp: 100000));
            c.Cast("桂", 0, attackMode: true);
            Assert.That(c.PlayerStatuses.TotalMagnitude(StatusKind.Morale), Is.EqualTo(2), "不限次:两次击杀各入队");
        }

        // ---------------- ConfigLoader 同步 ----------------

        private static RecipeGraph LoadWood(string featureEffects, string attackEffects = @"{""kind"":""DamageSingle"",""value"":10}",
            string traits = "") => ConfigLoader.LoadGraph(
            @"{""chars"":[{""id"":""林"",""element"":""Wood"",""effects"":[" + featureEffects + @"],
              ""attackEffects"":[" + attackEffects + @"],""traits"":[" + traits + @"]}]}");

        private const string SummonJson = @"{""kind"":""Summon"",""value"":300,""count"":1,""attack"":20,""summonChar"":""木""}";

        [Test]
        public void Loader_ReadsWoodSharedFields()
        {
            var g = LoadWood(SummonJson + @",{""kind"":""Reshape"",""value"":0,""summonPercent"":60},"
                + @"{""kind"":""Augment"",""value"":1,""augmentKind"":""Summon"",""augmentField"":""Count""},"
                + @"{""kind"":""Revive"",""value"":0,""reviveHpPercent"":30},"
                + @"{""kind"":""SummonSapling"",""value"":30,""count"":2,""sprout"":true}",
                @"{""kind"":""DamageSingle"",""value"":10},{""kind"":""Reshape"",""value"":0,""summonAtkStrikePercent"":20,""hostAtkStrikePercent"":200,""hostArmorStrikePercent"":400},"
                + @"{""kind"":""SummonStrike"",""value"":200,""ownSummons"":true,""byArmor"":true},"
                + @"{""kind"":""SummonSapling"",""value"":20,""count"":2,""fromBody"":true},"
                + @"{""kind"":""Charm"",""value"":0,""turns"":1},{""kind"":""Augment"",""value"":100,""augmentKind"":""Charm"",""augmentField"":""Count""},"
                + @"{""kind"":""Amplify"",""value"":10,""scope"":""Damage"",""scaleBy"":""SummonCount""}");
            var def = g.Get("林");
            Assert.That(def.Effects[1].SummonPercent, Is.EqualTo(60));
            Assert.That(def.Effects[3].ReviveHpPercent, Is.EqualTo(30));
            Assert.That(def.Effects[4].Sprout, Is.True);
            var r = def.AttackEffects[1];
            Assert.That((r.SummonAtkStrikePercent, r.HostAtkStrikePercent, r.HostArmorStrikePercent), Is.EqualTo((20, 200, 400)));
            Assert.That((def.AttackEffects[2].OwnSummons, def.AttackEffects[2].ByArmor), Is.EqualTo((true, true)));
            Assert.That(def.AttackEffects[3].FromBody, Is.True);
            Assert.That(def.AttackEffects[6].ScaleBy, Is.EqualTo(ScaleBasis.SummonCount));
        }

        [TestCase(@"{""kind"":""HealSelf"",""value"":10,""fromBody"":true}")]                  // fromBody 只给 SummonSapling
        [TestCase(@"{""kind"":""HealSelf"",""value"":10,""sprout"":true}")]                    // sprout 只给 SummonSapling
        [TestCase(@"{""kind"":""SummonStrike"",""value"":10,""byArmor"":true}")]                // byArmor 须配 own(fix:只柘弓用)
        [TestCase(@"{""kind"":""HealSelf"",""value"":10,""ownSummons"":true}")]                // own 只给 SummonStrike
        [TestCase(@"{""kind"":""HealSelf"",""value"":10,""reviveHpPercent"":30}")]             // 只给 Revive
        [TestCase(@"{""kind"":""Revive"",""value"":1,""reviveHpPercent"":101}")]               // 1..100
        [TestCase(@"{""kind"":""HealSelf"",""value"":10,""summonAtkStrikePercent"":10}")]      // 只给 DamageSingle / Reshape
        [TestCase(@"{""kind"":""HealSelf"",""value"":10,""summonPercent"":60}")]               // 只给 Summon / Reshape
        [TestCase(@"{""kind"":""HealSelf"",""value"":10,""scaleBy"":""SummonCount""}")]        // SummonCount 只给 Amplify
        [TestCase(@"{""kind"":""Augment"",""value"":1,""augmentKind"":""Seed"",""augmentField"":""Count""}")]   // Count 只认 Block / Charm / Summon
        public void Loader_RejectsWoodFieldsOnWrongHosts(string effect)
        {
            Assert.Throws<ConfigException>(() => LoadWood(SummonJson + "," + effect));
        }

        [Test]
        public void Loader_TurnPulseAllowsVulnerableWithOwnSummonAlive_RejectsOtherConditions()
        {
            const string ok = @"{""kind"":""TurnPulse"",""value"":0,""turns"":3,""perHit"":[{""kind"":""Vulnerable"",""value"":30,""turns"":1,""pick"":""Random"",""onlyIf"":""OwnSummonAlive""}]}";
            var pulse = LoadWood(SummonJson + "," + ok).Get("林").Effects[1];
            Assert.That(pulse.PerHit[0].OnlyIf, Is.EqualTo(DamageCondition.OwnSummonAlive));
            Assert.Throws<ConfigException>(() => LoadWood(SummonJson + "," + ok.Replace("OwnSummonAlive", "Burning")),
                "其余条件仍拒(水 W7 口径)");
        }

        [Test]
        public void Loader_BattleLimitAllowsOnKill_GlyphSaplingNeedsFromBody()
        {
            string Trait(string trigger, string effects, string extra = "") =>
                @"{""slot"":""Lv5"",""face"":""Attack"",""form"":""Passive"",""trigger"":""" + trigger + @""",""name"":""折桂"",""effects"":[" + effects + "]" + extra + "}";
            var g = LoadWood(SummonJson, traits: Trait("OnKill", @"{""kind"":""Morale"",""value"":1}", @",""maxPerBattle"":1"));
            Assert.That(g.Get("林").Traits[0].MaxPerBattle, Is.EqualTo(1));
            Assert.Throws<ConfigException>(() => LoadWood(SummonJson, traits: Trait("OnCrit", @"{""kind"":""Morale"",""value"":1}", @",""maxPerBattle"":1")),
                "暴击时仍不收 battleLimit");

            string Glyph(string effect) => @"{""slot"":""Lv4"",""form"":""Passive"",""trigger"":""OnDismantle"",""name"":""双木"",""effects"":[" + effect + "]}";
            var glyph = LoadWood(SummonJson, traits: Glyph(@"{""kind"":""SummonSapling"",""value"":20,""count"":2,""fromBody"":true}"));
            Assert.That(glyph.Get("林").Traits[0].Effects[0].FromBody, Is.True, "fromBody 的幼苗可写在字形特性里");
            Assert.Throws<ConfigException>(() => LoadWood(SummonJson, traits: Glyph(@"{""kind"":""SummonSapling"",""value"":20,""count"":2}")));
        }

        [Test]
        public void E41_GlyphSaplingFromBody_PlacesSaplingsOnDismantle()
        {
            // 字形即时类走 ApplyDetachedEffects(合成的 CharDef 没有本体):回图谱取真身的生面 Summon
            var def = WoodChar("林", new[] { Body(hp: 400, attack: 30) }, null,
                new TraitDef(TraitSlot.Lv4, TraitFace.Both, TraitForm.Passive, null, "双木",
                    new[] { new EffectDef(EffectKind.SummonSapling, 20, summonCount: 2, fromBody: true) }, trigger: TraitTrigger.OnDismantle));
            var b = Battle(def, 4);
            var detached = typeof(BattleEngine).GetMethod("ApplyDetachedEffects",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            detached.Invoke(b, new object[] { "林", Element.Wood, def.Traits[0].Effects, -1 });
            var saplings = b.Summons.Where(x => x != null && x.Alive).ToList();
            Assert.That(saplings, Has.Count.EqualTo(2));
            Assert.That(saplings[0].MaxHp, Is.EqualTo(MetaRules.ScaleByCardLevel(400, 4) * 20 / 100));
        }

        // ================= 末尾预留(Ruling 17):只加名、不加行为 =================

        [Test]
        public void Reserve_EnumsAppendedAtTheEnd()
        {
            Assert.That((int)EffectKind.SummonEmpower, Is.EqualTo((int)EffectKind.ApCost + 1));
            Assert.That(new[] { EffectKind.SummonEmpower, EffectKind.SummonThorns, EffectKind.SummonRanged, EffectKind.Bind,
                EffectKind.ThornMark, EffectKind.SummonMerge, EffectKind.SummonAct, EffectKind.GrantChar }.Select(k => (int)k),
                Is.EqualTo(Enumerable.Range((int)EffectKind.ApCost + 1, 8)));
            Assert.That(Enum.GetValues(typeof(EffectKind)).Cast<int>().Max(), Is.EqualTo((int)EffectKind.GrantChar));
            Assert.That(new[] { StatusKind.SummonEmpower, StatusKind.SummonThorns, StatusKind.SummonRanged, StatusKind.Bind,
                StatusKind.ThornMark }.Select(k => (int)k), Is.EqualTo(Enumerable.Range((int)StatusKind.SkillCut + 1, 5)));
            Assert.That(Enum.GetValues(typeof(StatusKind)).Cast<int>().Max(), Is.EqualTo((int)StatusKind.ThornMark));
            Assert.That(((int)EffectPick.OtherSummons, (int)EffectPick.StrongestSummon),
                Is.EqualTo(((int)EffectPick.AllAllies + 1, (int)EffectPick.AllAllies + 2)));
            Assert.That(((int)EffectSource.DeathBlast, (int)EffectSource.ReturnFire),
                Is.EqualTo(((int)EffectSource.ShieldSpend + 1, (int)EffectSource.ShieldSpend + 2)));
            Assert.That((int)ScaleBasis.SummonCount, Is.EqualTo((int)ScaleBasis.Shield + 1));
        }

        private static SummonPassive FullWoodPassive() => new()
        {
            AttackPercent = 20, HealAllyPercent = 30, HealMirror = 100, ThornsBleed = 35, CritAura = 10,
            GroveCut = 5, GroveCutCap = 20, ThicketCut = 25, ThicketMin = 3, Guard = 30, ReturnFire = 200,
            DeathBlast = 300, SproutDeathSlow = 1, DeathPart = true,
        };

        private static string WoodFields(SummonPassive p) =>
            $"{p.AttackPercent}/{p.HealAllyPercent}/{p.HealMirror}/{p.ThornsBleed}/{p.CritAura}/{p.GroveCut}/{p.GroveCutCap}/"
            + $"{p.ThicketCut}/{p.ThicketMin}/{p.Guard}/{p.ReturnFire}/{p.DeathBlast}/{p.SproutDeathSlow}/{p.DeathPart}";

        [Test]
        public void Reserve_NewPassiveFields_CloneAndMerge()
        {
            var full = FullWoodPassive();
            Assert.That(WoodFields(full.Clone()), Is.EqualTo(WoodFields(full)), "Clone 同步全部新字段");
            Assert.That(WoodFields(TraitRules.MergePassive(new SummonPassive { Thorns = 50 }, full)), Is.EqualTo(WoodFields(full)),
                "MergePassive 并入全部新字段");
            Assert.That(TraitRules.MergePassive(full, new SummonPassive()).Guard, Is.EqualTo(30), "缺省不覆盖");
            // 本命追加(E37)经新字段走通:Summon 0 + Guard 30
            var def = WoodChar("柘", new[] { Body(new SummonPassive { Taunt = true }) }, null,
                Trait(TraitSlot.Lv6, TraitFace.Feature, TraitForm.Passive, Append(new SummonPassive { Guard = 30 })));
            var p = TraitRules.CastEffects(def, CardFace.Feature, 6).Single().Passive;
            Assert.That((p.Taunt, p.Guard), Is.EqualTo((true, 30)));
        }

        [Test]
        public void Reserve_NewPassiveFields_SurviveRealSaveFile()
        {
            var w = Wood(0, hp: 500);
            w.Passive = FullWoodPassive();
            var graph = RebalanceFixture.Graph();
            var enemy = RebalanceFixture.Mob(attack: 0);
            var runConfig = new RunConfig { Encounters = new[] { new[] { enemy }, new[] { enemy } }, RewardPool = new[] { "甲" } };
            var run = new RunEngine(graph, runConfig, Config, new[] { "甲", "甲" }, Array.Empty<string>(), 3, startingSummons: new[] { w });
            var meta = new MetaState
            {
                EndlessV2 = new EndlessSaveState
                {
                    Depth = 3, Seed = 999,
                    InProgress = new InProgressRun { FromDepth = 1, FirstTowerSegment = true, Run = run.Capture() },
                },
            };
            var reloaded = SaveSerializer.FromJson(SaveSerializer.ToJson(meta));
            var restored = RunEngine.Restore(reloaded.EndlessV2.InProgress.Run, graph, runConfig, Config, new Dictionary<string, int>());
            Assert.That(WoodFields(restored.Battle.Summons.Single(x => x != null).Passive), Is.EqualTo(WoodFields(FullWoodPassive())), "本命新字段进存档 JSON 往返");
        }

        [Test]
        public void Reserve_OnSummonDeath_IsIdenticalWithNewFieldsAbsent()
        {
            // 单点调用空实现:阵亡流程(钩子 / 归根 / 刷光环)不变 —— 无新字段时与 base 逐位恒等由 WoodSharedIdentityTests 守,
            // 这里只钉「带了新本命字段也还没有行为」(Task 3 接入时会改这条)
            var w = Wood(0, hp: 10);
            w.Passive = FullWoodPassive();
            var b = Battle(RebalanceFixture.Char("乙", new EffectDef(EffectKind.DamageSingle, 1)), 1, new[] { w },
                RebalanceFixture.Mob(attack: 500));
            b.EndTurn();
            Assert.That(b.Summons[0].Alive, Is.False, "夹具前提:木灵阵亡");
            Assert.That(b.Enemies[0].Statuses.All.Any(), Is.False, "预留字段没有行为");
        }
    }
}
