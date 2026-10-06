using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using Brushblade.Data;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>D1 Task 4:Lv3 按 Kind 原位替换本体(附录 M0b)与 Augment 叠加修饰器(M4)。
    /// 断言落在纯函数 TraitRules.CastEffects 的输出上(顺序、数值),再各配一条引擎端到端。</summary>
    public class TraitLv3ReplaceTests
    {
        private static EffectDef Burn(int n) => new(EffectKind.BurnSingle, n);
        private static EffectDef Dmg(int n) => new(EffectKind.DamageSingle, n);

        private static TraitDef Lv1(TraitFace face, string name) =>
            new(TraitSlot.Lv1, face, TraitForm.Active, null, name, Array.Empty<EffectDef>());

        private static TraitDef Lv3(TraitFace face, params EffectDef[] effects) =>
            new(TraitSlot.Lv3, face, TraitForm.Active, TraitSlot.Lv1, "强化", effects);

        private static TraitDef Aug(TraitSlot slot, TraitFace face, EffectDef augment) =>
            new(slot, face, TraitForm.Passive, null, "增", new[] { augment });

        private static EffectDef Augment(int n, EffectKind of, AugmentField field) =>
            new(EffectKind.Augment, n, augmentKind: of, augmentField: field);

        private static BattleEngine Battle(CharDef def, int level) =>
            new(RebalanceFixture.Graph(def),
                new BattleConfig { PlayerMaxHp = RebalanceFixture.BaseMaxHp, PlayerAttack = 100 },
                new[] { def.Id, def.Id }, Array.Empty<string>(), new[] { RebalanceFixture.Mob() }, seed: 1,
                cardLevels: new Dictionary<string, int> { [def.Id] = level });

        // ---------------- Lv3 替换 ----------------

        [Test]
        public void Lv3_ReplacesBodyBurnInPlace_KeepsOrder()
        {
            var def = new CharDef("攻", Element.Heart, effects: new[] { Dmg(100), Burn(1) },
                traits: new[] { Lv1(TraitFace.Both, "灼"), Lv3(TraitFace.Both, Burn(2)) });
            var folded = TraitRules.CastEffects(def, CardFace.Feature, 3);
            Assert.That(folded.Select(e => (e.Kind, e.Value)).ToList(),
                Is.EqualTo(new[] { (EffectKind.DamageSingle, 100), (EffectKind.BurnSingle, 2) }));

            var b = Battle(def, 3);
            b.Cast("攻", 0);
            Assert.That(b.Enemies[0].Statuses.TotalMagnitude(StatusKind.Burn), Is.EqualTo(2));
        }

        [Test]
        public void Lv3_FeatureFace_ReplacesBurnAndWeaken()
        {
            // Weaken 由 Task 5 提供;这里用 Slow(Value = 回合)夹具顶:两条同时被替换
            var def = new CharDef("燃", Element.Heart,
                effects: new[] { Burn(1), new EffectDef(EffectKind.Slow, 2) },
                traits: new[]
                {
                    Lv1(TraitFace.Feature, "灼"),
                    Lv3(TraitFace.Feature, Burn(2), new EffectDef(EffectKind.Slow, 3)),
                });
            var folded = TraitRules.CastEffects(def, CardFace.Feature, 3);
            Assert.That(folded.Select(e => (e.Kind, e.Value)).ToList(),
                Is.EqualTo(new[] { (EffectKind.BurnSingle, 2), (EffectKind.Slow, 3) }));
        }

        [Test]
        public void Lv3_NoSameKind_Appends()
        {
            var def = new CharDef("攻", Element.Heart, effects: new[] { Dmg(100) },
                traits: new[] { Lv1(TraitFace.Both, "灼"), Lv3(TraitFace.Both, Burn(2)) });
            var folded = TraitRules.CastEffects(def, CardFace.Feature, 3);
            Assert.That(folded.Select(e => (e.Kind, e.Value)).ToList(),
                Is.EqualTo(new[] { (EffectKind.DamageSingle, 100), (EffectKind.BurnSingle, 2) }));
        }

        [Test]
        public void Lv3_BelowLevel3_BodyUntouched()
        {
            var body = new[] { Dmg(100), Burn(1) };
            var def = new CharDef("攻", Element.Heart, effects: body,
                traits: new[] { Lv1(TraitFace.Both, "灼"), Lv3(TraitFace.Both, Burn(2)) });
            var folded = TraitRules.CastEffects(def, CardFace.Feature, 2);
            Assert.That(folded.Count, Is.EqualTo(2));
            Assert.That(ReferenceEquals(folded[0], body[0]) && ReferenceEquals(folded[1], body[1]), Is.True);
        }

        [Test]
        public void Lv3_DoesNotMutateSharedBody()
        {
            var def = new CharDef("攻", Element.Heart, effects: new[] { Dmg(100), Burn(1) },
                traits: new[] { Lv3(TraitFace.Both, Burn(2)) });
            TraitRules.CastEffects(def, CardFace.Feature, 3);
            Assert.That(def.Effects[1].Value, Is.EqualTo(1));
        }

        [Test]
        public void Lv3_AttackFaceBody_ReplacesOnlyThatFace()
        {
            var def = new CharDef("双", Element.Heart,
                effects: new[] { Burn(1) }, attackEffects: new[] { Dmg(100), Burn(1) },
                traits: new[] { Lv3(TraitFace.Attack, Burn(2)) });
            Assert.That(TraitRules.CastEffects(def, CardFace.Attack, 3)[1].Value, Is.EqualTo(2));
            Assert.That(TraitRules.CastEffects(def, CardFace.Feature, 3)[0].Value, Is.EqualTo(1));
        }

        /// <summary>终审 Minor 6:真实字表的形状 —— Lv1 恒为两面(只放名字,效果在本体),Lv3 单面替换。
        /// 替换按 Kind 落在那一面的本体上;另一面本体不动;Lv1 仍在 Unlocked 里(UI 显示关键词)。</summary>
        [Test]
        public void BothFaceLv1_SingleFaceLv3_ReplacesOnlyThatFace_EndToEnd()
        {
            var def = new CharDef("双", Element.Heart,
                effects: new[] { Burn(1) }, attackEffects: new[] { Dmg(100), Burn(1) },
                traits: new[] { Lv1(TraitFace.Both, "灼"), Lv3(TraitFace.Attack, Burn(2)) });

            Assert.That(TraitRules.CastEffects(def, CardFace.Attack, 3).Select(e => (e.Kind, e.Value)).ToList(),
                Is.EqualTo(new[] { (EffectKind.DamageSingle, 100), (EffectKind.BurnSingle, 2) }), "攻面:灼原位换成 2");
            Assert.That(TraitRules.CastEffects(def, CardFace.Feature, 3).Select(e => (e.Kind, e.Value)).ToList(),
                Is.EqualTo(new[] { (EffectKind.BurnSingle, 1) }), "另一面本体不动");
            Assert.That(TraitRules.Unlocked(def, 3).Any(t => t.Slot == TraitSlot.Lv1), Is.True);

            var attack = Battle(def, 3);
            attack.Cast("双", 0, attackMode: true);
            Assert.That(attack.Enemies[0].Statuses.TotalMagnitude(StatusKind.Burn), Is.EqualTo(2));
            var feature = Battle(def, 3);
            feature.Cast("双", 0);
            Assert.That(feature.Enemies[0].Statuses.TotalMagnitude(StatusKind.Burn), Is.EqualTo(1));
        }

        [Test]
        public void Unlocked_StillReturnsReplacedLv1_ButNonLv1ReplacementStillHides()
        {
            var def = new CharDef("试", Element.Heart, effects: new[] { Dmg(100) }, traits: new[]
            {
                Lv1(TraitFace.Both, "灼"),
                Lv3(TraitFace.Both, Burn(2)),
                new TraitDef(TraitSlot.Lv5, TraitFace.Both, TraitForm.Active, null, "五", new[] { Burn(1) }),
                new TraitDef(TraitSlot.Lv8, TraitFace.Both, TraitForm.Active, TraitSlot.Lv5, "八", new[] { Burn(5) }),
            });
            var slots = TraitRules.Unlocked(def, 8).Select(t => t.Slot).ToList();
            Assert.That(slots.Contains(TraitSlot.Lv1), Is.True, "被 Lv3 替换的 Lv1 仍返回(UI 要显示关键词)");
            Assert.That(slots.Contains(TraitSlot.Lv5), Is.False, "非 Lv1 的替换语义保持");
            Assert.That(slots.Contains(TraitSlot.Lv8), Is.True);
        }

        // ---------------- Augment ----------------

        [Test]
        public void Augment_BlockPlusOne_MergesWithinCast()
        {
            var def = new CharDef("铠", Element.Metal, effects: new[] { new EffectDef(EffectKind.Block, 1) },
                attackEffects: new[] { Dmg(100) },
                traits: new[] { Aug(TraitSlot.Lv4, TraitFace.Feature, Augment(1, EffectKind.Block, AugmentField.Count)) });
            var folded = TraitRules.CastEffects(def, CardFace.Feature, 4);
            Assert.That(folded.Count, Is.EqualTo(1), "增益器不留在结果里,也没有第二条 Block");
            Assert.That(folded[0].Value, Is.EqualTo(2));
            Assert.That(def.Effects[0].Value, Is.EqualTo(1), "共享本体不被改");

            var b = Battle(def, 4);
            b.Cast("铠", -1);
            Assert.That(b.PlayerStatuses.Find(StatusKind.Block).Magnitude, Is.EqualTo(2));
            Assert.That(b.PlayerStatuses.All.Count(s => s.Kind == StatusKind.Block), Is.EqualTo(1));
        }

        [Test]
        public void Augment_FreezeTurnsPlusOne()
        {
            var def = new CharDef("冻", Element.Heart, effects: new[] { new EffectDef(EffectKind.Freeze, 1) },
                traits: new[] { Aug(TraitSlot.Lv4, TraitFace.Both, Augment(1, EffectKind.Freeze, AugmentField.Turns)) });
            var folded = TraitRules.CastEffects(def, CardFace.Feature, 4);
            Assert.That(folded.Single().Value, Is.EqualTo(2), "Freeze 的回合在 Value 上");
        }

        [Test]
        public void Augment_TurnsField_PicksRightFieldPerKind()
        {
            foreach (var (kind, inValue) in new[]
            {
                (EffectKind.Freeze, true), (EffectKind.Slow, true),
                (EffectKind.DefenseBuff, false), (EffectKind.ArmorBreak, false), (EffectKind.HealOverTime, false),
            })
            {
                var body = new EffectDef(kind, 5, turns: 2);
                var def = new CharDef("试", Element.Heart, effects: new[] { body },
                    traits: new[] { Aug(TraitSlot.Lv4, TraitFace.Both, Augment(2, kind, AugmentField.Turns)) });
                var e = TraitRules.CastEffects(def, CardFace.Feature, 4).Single();
                Assert.That(e.Value, Is.EqualTo(inValue ? 7 : 5), kind.ToString());
                Assert.That(e.Turns, Is.EqualTo(2 + (inValue ? 0 : 2)), kind.ToString());
                Assert.That(TraitRules.TurnsOf(e), Is.EqualTo(inValue ? 7 : 4), kind.ToString());
            }
        }

        [Test]
        public void Augment_ShotsPlusOne_OnFirstDamage()
        {
            var def = new CharDef("连", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 100, shape: TargetArea.Scatter, shots: 3) },
                traits: new[] { Aug(TraitSlot.Lv4, TraitFace.Both, Augment(1, EffectKind.DamageSingle, AugmentField.Shots)) });
            Assert.That(TraitRules.CastEffects(def, CardFace.Feature, 4).Single().Shots, Is.EqualTo(4));
        }

        [Test]
        public void Augment_NoTarget_IsNoOp()
        {
            var body = new EffectDef(EffectKind.Shield, 50);
            var def = new CharDef("盾", Element.Heart, effects: new[] { body },
                traits: new[] { Aug(TraitSlot.Lv4, TraitFace.Both, Augment(1, EffectKind.Block, AugmentField.Count)) });
            var folded = TraitRules.CastEffects(def, CardFace.Feature, 4);
            Assert.That(folded.Count, Is.EqualTo(1));
            Assert.That(ReferenceEquals(folded[0], body), Is.True);
        }

        [Test]
        public void Augment_AppliesToLv3ReplacedEffect()
        {
            // 先替换、后增益:拼装顺序 本体 → Lv3 替换 → 追加 → 折叠
            var def = new CharDef("冻", Element.Heart, effects: new[] { new EffectDef(EffectKind.Freeze, 1) },
                traits: new[]
                {
                    Lv1(TraitFace.Both, "冻"),
                    Lv3(TraitFace.Both, new EffectDef(EffectKind.Freeze, 2)),
                    Aug(TraitSlot.Lv4, TraitFace.Both, Augment(1, EffectKind.Freeze, AugmentField.Turns)),
                });
            Assert.That(TraitRules.CastEffects(def, CardFace.Feature, 4).Single().Value, Is.EqualTo(3));
        }

        [Test]
        public void Augment_TwoInOneTrait_EachApplies()
        {
            // 冰锁写法:同一条特性里两条 Augment(冻结 +1、减速 +1),折叠后各自落到自己的 Kind 上
            var def = new CharDef("锁", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.Freeze, 1), new EffectDef(EffectKind.Slow, 2) },
                traits: new[]
                {
                    new TraitDef(TraitSlot.Lv4, TraitFace.Both, TraitForm.Passive, null, "冰锁", new[]
                    {
                        Augment(1, EffectKind.Freeze, AugmentField.Turns),
                        Augment(1, EffectKind.Slow, AugmentField.Turns),
                    }),
                });
            var folded = TraitRules.CastEffects(def, CardFace.Feature, 4);
            Assert.That(folded.Select(e => (e.Kind, e.Value)).ToList(),
                Is.EqualTo(new[] { (EffectKind.Freeze, 2), (EffectKind.Slow, 3) }));
        }

        [Test]
        public void Augment_IsDiscrete_NotScaledByCardLevel()
        {
            Assert.That(MetaRules.ScalesWithCardLevel(EffectKind.Augment), Is.False);
        }

        [Test]
        public void CastEffects_NoTraits_IsBodyIdentity()
        {
            var body = new[] { Dmg(100), Burn(1) };
            var def = new CharDef("素", Element.Heart, effects: body);
            var folded = TraitRules.CastEffects(def, CardFace.Feature, 9);
            Assert.That(folded.Count, Is.EqualTo(2));
            Assert.That(ReferenceEquals(folded[0], body[0]) && ReferenceEquals(folded[1], body[1]), Is.True);
        }

        // ---------------- ConfigLoader ----------------

        private static string AugmentChar(string augment) =>
            @"{""chars"":[{""id"":""甲"",""element"":""Metal"",""effects"":[{""kind"":""Block"",""value"":1}],
                ""attackEffects"":[{""kind"":""DamageSingle"",""value"":5}],
                ""traits"":[{""slot"":""Lv4"",""form"":""Passive"",""face"":""Feature"",""name"":""砥砺"",""effects"":[" + augment + "]}]}]}";

        [Test]
        public void Loader_ParsesAugment()
        {
            var g = ConfigLoader.LoadGraph(AugmentChar(
                @"{""kind"":""Augment"",""value"":1,""augmentKind"":""Block"",""augmentField"":""Count""}"));
            var e = g.Get("甲").Traits[0].Effects[0];
            Assert.That(e.Kind, Is.EqualTo(EffectKind.Augment));
            Assert.That(e.AugmentKind, Is.EqualTo(EffectKind.Block));
            Assert.That(e.AugmentField, Is.EqualTo(AugmentField.Count));
        }

        [TestCase(@"{""kind"":""Augment"",""value"":1,""augmentField"":""Count""}")]                          // 缺 augmentKind
        [TestCase(@"{""kind"":""Augment"",""value"":1,""augmentKind"":""Block""}")]                           // 缺 augmentField
        [TestCase(@"{""kind"":""Augment"",""value"":1,""augmentKind"":""Block"",""augmentField"":""Turns""}")]   // 组合无效
        [TestCase(@"{""kind"":""Augment"",""value"":1,""augmentKind"":""Shield"",""augmentField"":""Count""}")]  // 组合无效
        [TestCase(@"{""kind"":""Augment"",""value"":0,""augmentKind"":""Block"",""augmentField"":""Count""}")]   // 加量 < 1
        [TestCase(@"{""kind"":""Shield"",""value"":1,""augmentKind"":""Block""}")]                            // 非 Augment 写了字段
        public void Loader_InvalidAugment_Throws(string augment)
        {
            Assert.Throws<ConfigException>(() => ConfigLoader.LoadGraph(AugmentChar(augment)));
        }
    }
}
