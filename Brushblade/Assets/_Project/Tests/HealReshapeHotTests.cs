using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>spec A §4.3 + 执行期决定 D14:「涓」面只有 HealOverTime,Reshape shape All(淋·细雨)改作用于它 ——
    /// TargetAll、每回合量 × shapePercent%(向下取整);有 HealSelf 时仍只改 HealSelf(原口径)。</summary>
    public class HealReshapeHotTests
    {
        private static CharDef Rain(params EffectDef[] body) => new("细", Element.Heart, effects: body,
            traits: new[] { new TraitDef(TraitSlot.Lv5, TraitFace.Feature, TraitForm.Active, null, "细雨",
                new[] { new EffectDef(EffectKind.Reshape, 0, shape: TargetArea.All, shapePercent: 50) }) });

        [Test]
        public void ReshapeAll_OnHotOnlyFace_MakesHotTargetAll_AtShapePercent()
        {
            var hot = TraitRules.CastEffects(Rain(new EffectDef(EffectKind.HealOverTime, 114, turns: 5)), CardFace.Feature, 5)
                .Single(e => e.Kind == EffectKind.HealOverTime);
            Assert.That(hot.TargetAll, Is.True);
            Assert.That(hot.Value, Is.EqualTo(57));
            Assert.That(hot.Turns, Is.EqualTo(5));
        }

        [Test]
        public void ReshapeAll_PrefersHealSelf_WhenPresent()
        {
            var folded = TraitRules.CastEffects(Rain(new EffectDef(EffectKind.HealSelf, 80),
                new EffectDef(EffectKind.HealOverTime, 10, turns: 2)), CardFace.Feature, 5);
            Assert.That(folded.Single(e => e.Kind == EffectKind.HealSelf).Shape, Is.EqualTo(TargetArea.All));
            var hot = folded.Single(e => e.Kind == EffectKind.HealOverTime);
            Assert.That(hot.TargetAll, Is.False);
            Assert.That(hot.Value, Is.EqualTo(10));
        }

        [Test]
        public void BeforeLv5_HotUntouched()
        {
            var hot = TraitRules.CastEffects(Rain(new EffectDef(EffectKind.HealOverTime, 114, turns: 5)), CardFace.Feature, 4)
                .Single(e => e.Kind == EffectKind.HealOverTime);
            Assert.That(hot.TargetAll, Is.False);
            Assert.That(hot.Value, Is.EqualTo(114));
        }
    }
}
