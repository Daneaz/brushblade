using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using Brushblade.Data;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>Plan D2-土 Task 1:共用小扩展 E27–E36(附录 §3)。
    ///
    /// 夹具口径同 TraitModifierTests:Element.Heart(生克 1.0×)、PlayerAttack = 100(攻击力缩放恒等)、
    /// 暴击率缺省 0(不摇号)。卡等级系数:Lv1 = 100、Lv4 = 118、Lv5 = 124、Lv6 = 130、Lv8 = 142。</summary>
    public class EarthSharedExtensionTests
    {
        private static TraitDef Trait(TraitSlot slot, TraitFace face, TraitForm form, params EffectDef[] effects) =>
            new(slot, face, form, null, "土" + (int)slot + face, effects);

        private static EffectDef Reshape(int shieldStrike = 0, int armorStrike = 0, TargetArea shape = TargetArea.Single,
            int shots = 0, bool persist = false, int armorShield = 0, bool armorStrikeEach = false,
            ScaleBasis scaleBy = ScaleBasis.None, int scaleDivisor = 0, int scaleCap = 0) =>
            new(EffectKind.Reshape, 0, persistOnce: persist, shape: shape, shots: shots,
                shieldStrikePercent: shieldStrike, armorStrikePercent: armorStrike, armorShieldPercent: armorShield,
                armorStrikeEach: armorStrikeEach, scaleBy: scaleBy, scaleDivisor: scaleDivisor, scaleCap: scaleCap);

        private static CharDef TwoFace(params TraitDef[] traits) => new("试", Element.Heart,
            effects: new[] { new EffectDef(EffectKind.Shield, 60), new EffectDef(EffectKind.DefenseBuff, 10, turns: 2) },
            attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 100), new EffectDef(EffectKind.ArmorBreak, 5, turns: 2) },
            traits: traits);

        private static EffectDef FirstOf(IEnumerable<EffectDef> effects, EffectKind kind) => effects.First(e => e.Kind == kind);

        // ---------------- E27:strike 百分比同面相加 ----------------

        [Test]
        public void E27_TowerBaseAndSuppressTower_ArmorStrikeAddsTo800()
        {
            // 塔:Lv4 塔基 armorStrike 300 + Lv8 镇塔 armorStrike 500(同在攻面)= 800,不再被后者覆盖成 500
            var def = TwoFace(
                Trait(TraitSlot.Lv4, TraitFace.Attack, TraitForm.Passive, Reshape(armorStrike: 300)),
                Trait(TraitSlot.Lv8, TraitFace.Attack, TraitForm.Active, Reshape(armorStrike: 500),
                    new EffectDef(EffectKind.Weaken, 30, turns: 2)));
            Assert.That(FirstOf(TraitRules.CastEffects(def, CardFace.Attack, 5), EffectKind.DamageSingle).ArmorStrikePercent,
                Is.EqualTo(300), "Lv5 只有塔基");
            Assert.That(FirstOf(TraitRules.CastEffects(def, CardFace.Attack, 8), EffectKind.DamageSingle).ArmorStrikePercent,
                Is.EqualTo(800), "塔基 + 镇塔 = 800");
        }

        [Test]
        public void E27_EarthQuakeNoLongerLowersMountainPress()
        {
            // 㙓:Lv5 山压 shieldStrike 50 + Lv8 地动 shape All shieldStrike 30 → 80(旧口径被地动压成 30)
            var def = TwoFace(
                Trait(TraitSlot.Lv5, TraitFace.Attack, TraitForm.Active, Reshape(shieldStrike: 50)),
                Trait(TraitSlot.Lv8, TraitFace.Attack, TraitForm.Active, Reshape(shieldStrike: 30, shape: TargetArea.All)));
            var lv5 = FirstOf(TraitRules.CastEffects(def, CardFace.Attack, 5), EffectKind.DamageSingle);
            var lv8 = FirstOf(TraitRules.CastEffects(def, CardFace.Attack, 8), EffectKind.DamageSingle);
            Assert.That(lv5.ShieldStrikePercent, Is.EqualTo(50));
            Assert.That(lv8.ShieldStrikePercent, Is.EqualTo(80), "地动不再把山压的 50 压低成 30");
            Assert.That(lv8.Shape, Is.EqualTo(TargetArea.All), "其余字段照旧非缺省覆盖");
        }

        [Test]
        public void E27_RealData_FoldedStrikes_EqualBodyPlusSumOfReshapes()
        {
            // 现数据:每张字、每一面、每一级,折叠后首条伤害的 strike 百分比 = 本体 + 同面已生效 Reshape 之和
            // (逐位恒等另经 git archive 导出改动前代码对拍,见 task-1-report)
            foreach (var def in CharTableTests.RealGraph().All)
                foreach (var face in new[] { CardFace.Feature, CardFace.Attack })
                    for (int level = 1; level <= 10; level++)
                    {
                        if (face == CardFace.Attack && def.AttackEffects.Count == 0) continue;
                        var folded = TraitRules.CastEffects(def, face, level);
                        var dmg = folded.FirstOrDefault(e => e.Kind == EffectKind.DamageSingle);
                        if (dmg == null) continue;
                        var body = BattleEngine.EffectsOf(def, face == CardFace.Attack).FirstOrDefault(e => e.Kind == EffectKind.DamageSingle);
                        var reshapes = BattleEngine.EffectsOf(def, face == CardFace.Attack).Where(e => e.Kind == EffectKind.Reshape)
                            .Concat(TraitRules.Unlocked(def, level).Where(t => t.AppliesTo(face) && t.Trigger == TraitTrigger.Cast)
                                .SelectMany(t => t.Effects).Where(e => e.Kind == EffectKind.Reshape)).ToList();
                        if (body == null) continue;   // 首条伤害来自特性追加:不在本口径内
                        Assert.That(dmg.ShieldStrikePercent, Is.EqualTo(body.ShieldStrikePercent + reshapes.Sum(r => r.ShieldStrikePercent)),
                            $"{def.Id}/{face}/Lv{level} shieldStrike");
                        Assert.That(dmg.ArmorStrikePercent, Is.EqualTo(body.ArmorStrikePercent + reshapes.Sum(r => r.ArmorStrikePercent)),
                            $"{def.Id}/{face}/Lv{level} armorStrike");
                    }
        }

        // ---------------- E29:Reshape persist ----------------

        [Test]
        public void E29_ReshapePersist_OnFeatureFace_MakesFirstShieldPersist()
        {
            var def = new CharDef("试", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.Shield, 60), new EffectDef(EffectKind.Shield, 10) },
                attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 100) },
                traits: new[] { Trait(TraitSlot.Lv8, TraitFace.Feature, TraitForm.Active, Reshape(persist: true)) });
            var lv7 = TraitRules.CastEffects(def, CardFace.Feature, 7);
            var lv8 = TraitRules.CastEffects(def, CardFace.Feature, 8);
            Assert.That(lv7[0].PersistOnce, Is.False);
            Assert.That(lv8[0].PersistOnce, Is.True, "第一条护盾改为留存");
            Assert.That(lv8[1].PersistOnce, Is.False, "只改第一条");
            Assert.That(lv8.Any(e => e.Kind == EffectKind.Reshape), Is.False, "修饰器不进结算表");
        }

        [Test]
        public void E29_ReshapePersist_ShieldAllAlsoCounts()
        {
            var def = new CharDef("试", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.ShieldAll, 60) },
                traits: new[] { Trait(TraitSlot.Lv8, TraitFace.Feature, TraitForm.Active, Reshape(persist: true)) });
            Assert.That(TraitRules.CastEffects(def, CardFace.Feature, 8)[0].PersistOnce, Is.True);
        }

        // ---------------- E30:Reshape armorShield ----------------

        [Test]
        public void E30_ReshapeArmorShield_WritesFirstShield()
        {
            var def = TwoFace(Trait(TraitSlot.Lv4, TraitFace.Feature, TraitForm.Passive, Reshape(armorShield: 300)));
            var folded = TraitRules.CastEffects(def, CardFace.Feature, 4);
            Assert.That(FirstOf(folded, EffectKind.Shield).ArmorShieldPercent, Is.EqualTo(300));
            Assert.That(FirstOf(TraitRules.CastEffects(def, CardFace.Feature, 3), EffectKind.Shield).ArmorShieldPercent, Is.EqualTo(0));
        }

        // ---------------- E31 / E36:伤害字段随 Reshape 落到第一条伤害 ----------------

        [Test]
        public void E31_E36_ReshapeCarriesArmorStrikeEachAndShieldHits()
        {
            var def = TwoFace(Trait(TraitSlot.Lv8, TraitFace.Attack, TraitForm.Active,
                Reshape(armorStrike: 300, armorStrikeEach: true, shape: TargetArea.Scatter, shots: 3,
                    scaleBy: ScaleBasis.Shield, scaleDivisor: 100, scaleCap: 4)));
            var dmg = FirstOf(TraitRules.CastEffects(def, CardFace.Attack, 8), EffectKind.DamageSingle);
            Assert.That(dmg.ArmorStrikeEach, Is.True);
            Assert.That(dmg.ArmorStrikePercent, Is.EqualTo(300));
            Assert.That(dmg.ScaleBy, Is.EqualTo(ScaleBasis.Shield));
            Assert.That(dmg.ScaleDivisor, Is.EqualTo(100));
            Assert.That(dmg.ScaleCap, Is.EqualTo(4));
        }

        // ---------------- E34:AugmentField.Battle 与战后携带 ----------------

        private static EffectDef Guinie() =>
            new(EffectKind.Augment, 0, augmentKind: EffectKind.DefenseBuff, augmentField: AugmentField.Battle);

        [Test]
        public void E34_AugmentBattle_SetsFirstDefenseBuffTurnsToMinusOne_AndTurnsAddKeepsIt()
        {
            var def = TwoFace(
                Trait(TraitSlot.Lv4, TraitFace.Feature, TraitForm.Passive,
                    new EffectDef(EffectKind.Augment, 4, augmentKind: EffectKind.DefenseBuff, augmentField: AugmentField.Turns)),
                Trait(TraitSlot.Lv6, TraitFace.Feature, TraitForm.Passive, Guinie()));
            Assert.That(FirstOf(TraitRules.CastEffects(def, CardFace.Feature, 5), EffectKind.DefenseBuff).Turns, Is.EqualTo(6));
            Assert.That(FirstOf(TraitRules.CastEffects(def, CardFace.Feature, 6), EffectKind.DefenseBuff).Turns, Is.EqualTo(-1),
                "持续到战斗结束");
            var battleFirst = TwoFace(
                Trait(TraitSlot.Lv6, TraitFace.Feature, TraitForm.Passive, Guinie()),
                Trait(TraitSlot.Lv8, TraitFace.Feature, TraitForm.Passive,
                    new EffectDef(EffectKind.Augment, 4, augmentKind: EffectKind.DefenseBuff, augmentField: AugmentField.Turns)));
            Assert.That(FirstOf(TraitRules.CastEffects(battleFirst, CardFace.Feature, 8), EffectKind.DefenseBuff).Turns, Is.EqualTo(-1),
                "−1 之后再加回合不变(WithTurns 不加)");
        }

        [Test]
        public void E34_BattleScopedDefenseBuff_IsNotCarried()
        {
            // 携带白名单删掉 DefenseBuff(E34):本场持续(TurnsLeft −1)的护甲战后不跨场;厚 / 泉照旧
            var run = RebalanceFixture.Run();
            run.Battle.PlayerStatuses.Apply(new StatusEffect
            {
                Kind = StatusKind.DefenseBuff, Polarity = StatusPolarity.Buff, Magnitude = 10, TurnsLeft = -1, SourceId = "臬#0",
            });
            Assert.That(run.Battle.Cast("甲", 0), Is.EqualTo(BattleError.None));
            Assert.That(run.Battle.Phase, Is.EqualTo(BattlePhase.Won));
            run.AdvanceAfterBattle();
            Assert.That(run.CarriedStatuses.Any(s => s.Kind == StatusKind.DefenseBuff), Is.False,
                "本场持续的护甲战后不进 CarriedStatuses");
        }

        // ---------------- ConfigLoader ----------------

        /// <summary>一张两面土字:固面 Shield + DefenseBuff,攻面 DamageSingle。traitFace 指定特性写在哪一面。</summary>
        private static RecipeGraph Load(string traitEffects, string traitFace = "Feature") => ConfigLoader.LoadGraph(
            @"{""chars"":[{""id"":""塔"",""element"":""Earth"",""effects"":[{""kind"":""Shield"",""value"":60},{""kind"":""DefenseBuff"",""value"":10,""turns"":2}],
              ""attackEffects"":[{""kind"":""DamageSingle"",""value"":100}],
              ""traits"":[{""slot"":""Lv8"",""face"":""" + traitFace + @""",""form"":""Passive"",""name"":""修"",""effects"":[" + traitEffects + @"]}]}]}");

        [Test]
        public void Loader_ReadsSharedModifierFields()
        {
            var persist = Load(@"{""kind"":""Reshape"",""value"":0,""persistOnce"":true}").Get("塔").Traits[0].Effects[0];
            Assert.That(persist.PersistOnce, Is.True);
            var armorShield = Load(@"{""kind"":""Reshape"",""value"":0,""armorShieldPercent"":300}").Get("塔").Traits[0].Effects[0];
            Assert.That(armorShield.ArmorShieldPercent, Is.EqualTo(300));
            var each = Load(@"{""kind"":""Reshape"",""value"":0,""shape"":""Scatter"",""shots"":3,""armorStrikePercent"":300,""armorStrikeEach"":true}",
                "Attack").Get("塔").Traits[0].Effects[0];
            Assert.That(each.ArmorStrikeEach, Is.True);
            var hits = Load(@"{""kind"":""Reshape"",""value"":0,""scaleBy"":""Shield"",""scaleDivisor"":100,""scaleCap"":4}",
                "Attack").Get("塔").Traits[0].Effects[0];
            Assert.That(hits.ScaleBy, Is.EqualTo(ScaleBasis.Shield));
            Assert.That(hits.ScaleDivisor, Is.EqualTo(100));
            Assert.That(hits.ScaleCap, Is.EqualTo(4));
            var battle = Load(@"{""kind"":""Augment"",""value"":0,""augmentKind"":""DefenseBuff"",""augmentField"":""Battle""}")
                .Get("塔").Traits[0].Effects[0];
            Assert.That(battle.AugmentField, Is.EqualTo(AugmentField.Battle));
        }

        [TestCase(@"{""kind"":""Reshape"",""value"":0,""persistOnce"":true}", "Attack")]                       // 伤害面上的 persist
        [TestCase(@"{""kind"":""Reshape"",""value"":0,""armorShieldPercent"":300}", "Attack")]                 // 伤害面上的 armorShield
        [TestCase(@"{""kind"":""Shield"",""value"":10,""armorShieldPercent"":300}", "Feature")]                // armorShield 只给 Reshape
        [TestCase(@"{""kind"":""DefenseBuff"",""value"":10,""turns"":2,""persistOnce"":true}", "Feature")]    // persistOnce 宿主不对
        [TestCase(@"{""kind"":""Reshape"",""value"":0,""armorStrikeEach"":true}", "Attack")]                   // 逐发镇压没有镇压
        [TestCase(@"{""kind"":""Reshape"",""value"":0,""scaleBy"":""Shield""}", "Attack")]                     // 缺除数
        [TestCase(@"{""kind"":""Reshape"",""value"":0,""scaleDivisor"":100}", "Attack")]                      // 除数没配 scaleBy Shield
        [TestCase(@"{""kind"":""Amplify"",""value"":10,""scaleBy"":""Shield"",""scaleDivisor"":100}", "Attack")] // Shield 档只给伤害
        [TestCase(@"{""kind"":""Augment"",""value"":0,""augmentKind"":""Shield"",""augmentField"":""Battle""}", "Feature")] // Battle 只配 DefenseBuff
        public void Loader_RejectsBadSharedModifier(string effect, string face)
        {
            Assert.Throws<ConfigException>(() => Load(effect, face));
        }
    }
}
