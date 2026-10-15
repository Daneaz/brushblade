using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>spec C §2:同面多条 Reshape 的 perHit 拼接(低槽在前),不再后者覆盖;形状 / 击数仍覆盖(Q18)。</summary>
    public class PerHitAppendTests
    {
        private static CharDef Twin() => new("鍂", Element.Metal,
            effects: new[] { new EffectDef(EffectKind.Block, 1, counterPercent: 30) },
            attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 100) },
            traits: new[]
            {
                new TraitDef(TraitSlot.Lv5, TraitFace.Attack, TraitForm.Active, null, "金石", new[]
                {
                    new EffectDef(EffectKind.Reshape, 0, hitCount: 2, hitPercent: 60,
                        perHit: new[] { new EffectDef(EffectKind.ArmorBreak, 20, turns: 3) }),
                }),
                new TraitDef(TraitSlot.Lv8, TraitFace.Attack, TraitForm.Active, null, "双锋", new[]
                {
                    new EffectDef(EffectKind.Reshape, 0, hitCount: 2, perHit: new[] { new EffectDef(EffectKind.Morale, 1) }),
                }),
            });

        [Test]
        public void Lv8PerHit_AppendsToLv5PerHit()
        {
            var hit = TraitRules.CastEffects(Twin(), CardFace.Attack, 8).Single(e => e.Kind == EffectKind.DamageSingle);
            Assert.That(hit.PerHit.Select(e => e.Kind), Is.EqualTo(new[] { EffectKind.ArmorBreak, EffectKind.Morale }));
            Assert.That(hit.HitCount, Is.EqualTo(2));
        }

        [Test]
        public void Lv5Only_Unchanged()
        {
            var hit = TraitRules.CastEffects(Twin(), CardFace.Attack, 5).Single(e => e.Kind == EffectKind.DamageSingle);
            Assert.That(hit.PerHit.Select(e => e.Kind), Is.EqualTo(new[] { EffectKind.ArmorBreak }));
            Assert.That(hit.HitPercent, Is.EqualTo(60));
        }
    }
}
