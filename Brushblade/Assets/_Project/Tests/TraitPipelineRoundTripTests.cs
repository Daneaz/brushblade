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
        }
    }
}
