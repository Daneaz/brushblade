using Brushblade.Core;
using Brushblade.Data;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>管线输出的 traits 键名/枚举写法与 ConfigLoader 一致(spec v7 §11.9)。
    /// JSON 片段照 tools/pipeline/extract_traits.py 的输出格式手写:改了任一边都要同步改这里。</summary>
    public class TraitPipelineRoundTripTests
    {
        [Test]
        public void PipelineShapedTraits_LoadIntoCharDef()
        {
            var g = ConfigLoader.LoadGraph(@"{""chars"":[{""id"":""炎"",""rarity"":""Gold"",""element"":""Fire"",
                ""effects"":[{""kind"":""DamageSingle"",""value"":168}],
                ""attackEffects"":[{""kind"":""DamageSingle"",""value"":168}],
                ""traits"":[
                  {""slot"":""Lv1"",""name"":""炎灼"",""effects"":[{""kind"":""BurnSingle"",""value"":3}]},
                  {""slot"":""Lv3"",""replaces"":""Lv1"",""name"":""炎灼强化"",""effects"":[{""kind"":""BurnSingle"",""value"":4}]},
                  {""slot"":""Lv4"",""form"":""Passive"",""name"":""双焰"",""effects"":[{""kind"":""BurnSingle"",""value"":2}]},
                  {""slot"":""Lv5"",""face"":""Attack"",""name"":""火上浇油"",""effects"":[{""kind"":""DamageSingle"",""value"":50}]}
                ]}]}");
            var t = g.Get("炎").Traits;
            Assert.That(t.Count, Is.EqualTo(4));
            Assert.That(t[1].Replaces, Is.EqualTo(TraitSlot.Lv1));
            Assert.That(t[2].Form, Is.EqualTo(TraitForm.Passive));
            Assert.That(t[3].Face, Is.EqualTo(TraitFace.Attack));
            Assert.That(t[2].Trigger, Is.EqualTo(TraitTrigger.Cast), "trigger 缺省 Cast");
        }

        [Test]
        public void PipelineShapedTrigger_LoadsAsOnKill()
        {
            var g = ConfigLoader.LoadGraph(@"{""chars"":[{""id"":""炎"",""rarity"":""Gold"",""element"":""Fire"",
                ""effects"":[{""kind"":""DamageSingle"",""value"":168}],
                ""attackEffects"":[{""kind"":""DamageSingle"",""value"":168}],
                ""traits"":[
                  {""slot"":""Lv4"",""form"":""Passive"",""trigger"":""OnKill"",""name"":""乘胜"",""effects"":[{""kind"":""Shield"",""value"":2}]}
                ]}]}");
            Assert.That(g.Get("炎").Traits[0].Trigger, Is.EqualTo(TraitTrigger.OnKill));
        }

        [Test]
        public void PipelineShapedTrigger_LoadsAsOnExecute()
        {
            // D2-金 J3:extract_traits 的「被动·斩杀」→ trigger OnExecute(铁则)
            var g = ConfigLoader.LoadGraph(@"{""chars"":[{""id"":""铡"",""rarity"":""Purple"",""element"":""Metal"",
                ""effects"":[{""kind"":""Block"",""value"":1}],
                ""attackEffects"":[{""kind"":""DamageSingle"",""value"":100}],
                ""traits"":[
                  {""slot"":""Lv4"",""form"":""Passive"",""trigger"":""OnExecute"",""name"":""铁则"",""effects"":[{""kind"":""Morale"",""value"":2}]}
                ]}]}");
            Assert.That(g.Get("铡").Traits[0].Trigger, Is.EqualTo(TraitTrigger.OnExecute));
        }

        [TestCase(@"{""slot"":""Lv4"",""form"":""Passive"",""trigger"":""Weird"",""name"":""x"",""effects"":[]}")]
        [TestCase(@"{""slot"":""Lv4"",""trigger"":""OnCrit"",""name"":""x"",""effects"":[]}")]
        public void BadTrigger_Throws(string trait)
        {
            Assert.Throws<ConfigException>(() => ConfigLoader.LoadGraph(
                @"{""chars"":[{""id"":""炎"",""element"":""Fire"",""effects"":[{""kind"":""Shield"",""value"":5}],
                ""attackEffects"":[{""kind"":""DamageSingle"",""value"":5}],""traits"":[" + trait + "]}]}"));
        }
    }
}
