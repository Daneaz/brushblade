using Brushblade.Core;
using Brushblade.Data;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>traits 字段解析与校验(spec v6 §11.1)。</summary>
    public class TraitConfigTests
    {
        private const string Body = @"""effects"":[{""kind"":""Shield"",""value"":5}],
            ""attackEffects"":[{""kind"":""DamageSingle"",""value"":5}]";

        [Test]
        public void Parses_AllFields()
        {
            var g = ConfigLoader.LoadGraph(@"{""chars"":[{""id"":""甲"",""element"":""Fire""," + Body + @",
                ""traits"":[
                  {""slot"":""Lv1"",""face"":""Attack"",""name"":""灼"",""effects"":[{""kind"":""BurnSingle"",""value"":1}]},
                  {""slot"":""Lv3"",""face"":""Attack"",""replaces"":""Lv1"",""name"":""灼+"",""effects"":[{""kind"":""BurnSingle"",""value"":2}]},
                  {""slot"":""Lv4"",""form"":""Passive"",""name"":""稳"",""effects"":[{""kind"":""Shield"",""value"":1}]}
                ]}]}");
            var t = g.Get("甲").Traits;
            Assert.That(t.Count, Is.EqualTo(3));
            Assert.That(t[0].Slot, Is.EqualTo(TraitSlot.Lv1));
            Assert.That(t[0].Face, Is.EqualTo(TraitFace.Attack));
            Assert.That(t[0].Form, Is.EqualTo(TraitForm.Active));
            Assert.That(t[1].Replaces, Is.EqualTo(TraitSlot.Lv1));
            Assert.That(t[2].Face, Is.EqualTo(TraitFace.Both), "face 缺省 Both");
            Assert.That(t[2].Form, Is.EqualTo(TraitForm.Passive));
            Assert.That(t[0].Name, Is.EqualTo("灼"));
        }

        [TestCase(@"{""slot"":""Lv2"",""name"":""x"",""effects"":[]}")]
        [TestCase(@"{""slot"":""Lv1"",""face"":""Up"",""name"":""x"",""effects"":[]}")]
        [TestCase(@"{""slot"":""Lv1"",""form"":""Weird"",""name"":""x"",""effects"":[]}")]
        [TestCase(@"{""slot"":""Lv3"",""replaces"":""Lv1"",""name"":""x"",""effects"":[]}")]
        public void Invalid_Throws(string trait)
        {
            var ex = Assert.Throws<ConfigException>(() => ConfigLoader.LoadGraph(
                @"{""chars"":[{""id"":""甲"",""element"":""Fire""," + Body + @",""traits"":[" + trait + "]}]}"));
            Assert.That(ex.Message, Does.Contain("甲"));
        }

        [Test]
        public void DuplicateSlot_Throws()
        {
            Assert.Throws<ConfigException>(() => ConfigLoader.LoadGraph(
                @"{""chars"":[{""id"":""甲"",""element"":""Fire""," + Body + @",""traits"":[
                  {""slot"":""Lv1"",""name"":""a"",""effects"":[]},{""slot"":""Lv1"",""name"":""b"",""effects"":[]}]}]}"));
        }

        [Test]
        public void ActiveTraitNeedsTarget_ButFaceBodyDoesNot_Throws()
        {
            // 特色面本体是护盾(不选敌方目标),特性却是单体灼烧 → 出手时没有目标 → 拒绝
            var ex = Assert.Throws<ConfigException>(() => ConfigLoader.LoadGraph(
                @"{""chars"":[{""id"":""甲"",""element"":""Fire""," + Body + @",""traits"":[
                  {""slot"":""Lv5"",""face"":""Feature"",""name"":""错"",""effects"":[{""kind"":""BurnSingle"",""value"":1}]}]}]}"));
            Assert.That(ex.Message, Does.Contain("错"));
        }

        [Test]
        public void SingleFaceLv3_MayReplace_BothFaceLv1()
        {
            // D1 Task 13:Lv1 是「两面」一行(只放关键词名),Lv3 按面各一行并替换它
            var g = ConfigLoader.LoadGraph(@"{""chars"":[{""id"":""甲"",""element"":""Fire""," + Body + @",""traits"":[
                  {""slot"":""Lv1"",""name"":""灼"",""effects"":[]},
                  {""slot"":""Lv3"",""face"":""Attack"",""replaces"":""Lv1"",""name"":""灼·强化"",""effects"":[{""kind"":""BurnSingle"",""value"":2}]}]}]}");
            Assert.That(g.Get("甲").Traits[1].Replaces, Is.EqualTo(TraitSlot.Lv1));
        }

        [Test]
        public void SingleFaceLv3_CannotReplace_OtherSingleFaceLv1()
        {
            Assert.Throws<ConfigException>(() => ConfigLoader.LoadGraph(
                @"{""chars"":[{""id"":""甲"",""element"":""Fire""," + Body + @",""traits"":[
                  {""slot"":""Lv1"",""face"":""Feature"",""name"":""灼"",""effects"":[]},
                  {""slot"":""Lv3"",""face"":""Attack"",""replaces"":""Lv1"",""name"":""灼·强化"",""effects"":[{""kind"":""BurnSingle"",""value"":2}]}]}]}"));
        }

        [Test]
        public void NoTraitsField_IsEmpty()
        {
            var g = ConfigLoader.LoadGraph(@"{""chars"":[{""id"":""甲"",""element"":""Fire""," + Body + "}]}");
            Assert.That(g.Get("甲").Traits.Count, Is.EqualTo(0));
        }
    }
}
