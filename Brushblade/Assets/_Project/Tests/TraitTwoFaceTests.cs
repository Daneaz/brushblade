using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using Brushblade.Data;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>spec v7:两面平等,同一槽位攻击、五行各一条;Replaces 只在同一作用面内替换。</summary>
    public class TraitTwoFaceTests
    {
        private const string Body = @"""effects"":[{""kind"":""Shield"",""value"":5}],
            ""attackEffects"":[{""kind"":""DamageSingle"",""value"":5}]";

        private static RecipeGraph Load(string traits, string body = Body) =>
            ConfigLoader.LoadGraph(@"{""chars"":[{""id"":""甲"",""element"":""Fire""," + body + @",""traits"":[" + traits + "]}]}");

        [Test]
        public void SameSlot_TwoFaces_BothLoaded()
        {
            var t = Load(@"{""slot"":""Lv5"",""face"":""Attack"",""name"":""攻五"",""effects"":[{""kind"":""DamageSingle"",""value"":1}]},
                           {""slot"":""Lv5"",""face"":""Feature"",""name"":""行五"",""effects"":[{""kind"":""Shield"",""value"":1}]}").Get("甲").Traits;
            Assert.That(t.Count, Is.EqualTo(2));
        }

        [Test]
        public void SameSlotSameFace_Throws()
        {
            Assert.Throws<ConfigException>(() => Load(
                @"{""slot"":""Lv5"",""face"":""Attack"",""name"":""a"",""effects"":[]},{""slot"":""Lv5"",""face"":""Attack"",""name"":""b"",""effects"":[]}"));
        }

        [Test]
        public void Replaces_MustTargetSameFace()
        {
            // Attack 面的 Lv3 声明替换 Lv1,但 Lv1 只有 Feature 面那条 → 同面不存在 → 拒绝
            var ex = Assert.Throws<ConfigException>(() => Load(
                @"{""slot"":""Lv1"",""face"":""Feature"",""name"":""行一"",""effects"":[]},
                  {""slot"":""Lv3"",""face"":""Attack"",""replaces"":""Lv1"",""name"":""攻三"",""effects"":[]}"));
            Assert.That(ex.Message, Does.Contain("攻三"));
        }

        [Test]
        public void Unlocked_ReplacesOnlyWithinSameFace()
        {
            var def = new CharDef("甲", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.Shield, 5) },
                attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 5) },
                traits: new[]
                {
                    new TraitDef(TraitSlot.Lv1, TraitFace.Attack, TraitForm.Active, null, "攻一", Array.Empty<EffectDef>()),
                    new TraitDef(TraitSlot.Lv1, TraitFace.Feature, TraitForm.Active, null, "行一", Array.Empty<EffectDef>()),
                    new TraitDef(TraitSlot.Lv3, TraitFace.Attack, TraitForm.Active, TraitSlot.Lv1, "攻三", Array.Empty<EffectDef>()),
                });
            var names = TraitRules.Unlocked(def, 3).Select(t => t.Name).ToList();
            // D1 Task 4:被 Lv3 替换的 Lv1 仍返回(UI 要显示关键词名),替换只作用于效果拼装,且仍只管同一面
            Assert.That(names.Contains("攻一"), Is.True, "Lv1 被替换仍返回");
            Assert.That(names.Contains("行一"), Is.True, "另一面不受影响");
            Assert.That(names.Contains("攻三"), Is.True);
        }

        [Test]
        public void BothFaces_Lv5_EachAppliesOnItsFace()
        {
            var def = new CharDef("甲", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.Shield, 50) },
                attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 100) },
                traits: new[]
                {
                    new TraitDef(TraitSlot.Lv5, TraitFace.Attack, TraitForm.Active, null, "攻五",
                        new[] { new EffectDef(EffectKind.BurnSingle, 1) }),
                    new TraitDef(TraitSlot.Lv5, TraitFace.Feature, TraitForm.Active, null, "行五",
                        new[] { new EffectDef(EffectKind.Shield, 30) }),
                });
            Assert.That(TraitRules.ActiveTraits(def, CardFace.Attack, 5).Single().Name, Is.EqualTo("攻五"));
            Assert.That(TraitRules.ActiveTraits(def, CardFace.Feature, 5).Single().Name, Is.EqualTo("行五"));
        }

        [Test]
        public void AttackFaceTrait_OnCharWithoutAttackEffects_Throws()
        {
            var ex = Assert.Throws<ConfigException>(() => Load(
                @"{""slot"":""Lv5"",""face"":""Attack"",""name"":""空攻"",""effects"":[{""kind"":""DamageSingle"",""value"":1}]}",
                @"""effects"":[{""kind"":""DamageSingle"",""value"":5}]"));
            Assert.That(ex.Message, Does.Contain("空攻"));
        }

        [Test]
        public void EmptyTraitName_Throws()
        {
            Assert.Throws<ConfigException>(() => Load(@"{""slot"":""Lv4"",""form"":""Passive"",""name"":"""",""effects"":[]}"));
            Assert.Throws<ConfigException>(() => Load(@"{""slot"":""Lv4"",""form"":""Passive"",""effects"":[]}"));
        }

        [Test]
        public void ActiveAllyTrait_OnFaceWithoutAllyTarget_Throws()
        {
            // 攻击面本体是单体伤害(不选友方),特性却要给友方加盾 → 拒绝
            var ex = Assert.Throws<ConfigException>(() => Load(
                @"{""slot"":""Lv5"",""face"":""Attack"",""name"":""错盾"",""effects"":[{""kind"":""Shield"",""value"":1}]}"));
            Assert.That(ex.Message, Does.Contain("错盾"));
        }

        [Test]
        public void ActiveEnemyTrait_OnBothFaces_ChecksEachFace()
        {
            // Both 面的单体灼烧:攻击面选敌人 OK,特色面(护盾)不选敌人 → 拒绝
            Assert.Throws<ConfigException>(() => Load(
                @"{""slot"":""Lv1"",""name"":""两面灼"",""effects"":[{""kind"":""BurnSingle"",""value"":1}]}"));
        }

        [Test]
        public void ActiveEnemyTrait_OnAttackFace_Accepted()
        {
            Assert.DoesNotThrow(() => Load(
                @"{""slot"":""Lv1"",""face"":""Attack"",""name"":""攻灼"",""effects"":[{""kind"":""BurnSingle"",""value"":1}]}"));
        }

        [Test]
        public void BothAndSingleFaceTraitsAtSameSlot_Throws()
        {
            var ex = Assert.Throws<ConfigException>(() => Load(
                @"{""slot"":""Lv5"",""name"":""两面五"",""effects"":[]},
                  {""slot"":""Lv5"",""face"":""Attack"",""name"":""攻五"",""effects"":[{""kind"":""DamageSingle"",""value"":1}]}"));
            Assert.That(ex.Message, Does.Contain("Lv5"));
        }
    }
}
