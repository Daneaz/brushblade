using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>spec C §5.3:火五行面数值格「燃意 · 回合 · 灼」、「燃意」词条、全体燃意面范围为全体、精进项印燃意。</summary>
    public class SpecCSheetTests
    {
        private static CharDef Ember(EffectPick pick = EffectPick.Primary, params EffectDef[] extra) => new("燃", Element.Fire,
            effects: new[] { new EffectDef(EffectKind.Kindle, 20, turns: 3, pick: pick) }.Concat(extra).ToArray(),
            attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 100) }, rarity: CardRarity.Purple);

        [Test]
        public void FireFeatureSheet_IsKindleTurnsBurn()
        {
            var cells = FaceStats.Sheet(Ember(), CardFace.Feature, 1);
            Assert.That(cells.Select(c => c.Kind), Is.EqualTo(new[] { FaceStatKind.Kindle, FaceStatKind.Duration, FaceStatKind.Burn }));
            Assert.That(cells.Select(c => c.Value), Is.EqualTo(new[] { 20, 3, BattleEngine.KindleBurnPerHit }));
            Assert.That(cells[1].DurationOf, Is.EqualTo(FaceStatKind.Kindle));
        }

        [Test]
        public void KindleCell_ScalesWithCardLevel()
        {
            Assert.That(FaceStats.Scales(FaceStatKind.Kindle), Is.True);
            Assert.That(FaceStats.Sheet(Ember(), CardFace.Feature, 5)[0].Value,
                Is.EqualTo(MetaRules.ScaleEffectValue(EffectKind.Kindle, 20, 5)));
        }

        [Test]
        public void Glossary_KindleIsLastKeyword_AndMapsCells()
        {
            Assert.That(GlossaryRules.Group(GlossaryTerm.Kindle), Is.EqualTo(0));
            Assert.That((int)GlossaryTerm.Kindle, Is.EqualTo((int)GlossaryTerm.SpendWellspring + 1));
            Assert.That(GlossaryRules.TermOf(FaceStatKind.Kindle), Is.EqualTo(GlossaryTerm.Kindle));
            Assert.That(GlossaryRules.ChipOf(GlossaryTerm.Kindle), Is.EqualTo(("burn", ChipTone.Boost)));
        }

        [Test]
        public void AllAlliesKindle_AreaIsAll()
        {
            Assert.That(CardSheetRules.AreaOf(Ember(EffectPick.AllAllies), CardFace.Feature, 1), Is.EqualTo(TargetArea.All));
            Assert.That(CardSheetRules.AreaOf(Ember(), CardFace.Feature, 1), Is.EqualTo(TargetArea.Single));
        }

        [Test]
        public void FeatureAmpItems_IncludeKindle()
        {
            var def = Ember(EffectPick.Primary, new EffectDef(EffectKind.Amplify, 10, scope: AmpScope.All));
            Assert.That(CardSheetRules.FeatureAmpItems(def, 1).Contains(AmpItem.Kindle), Is.True);
        }
    }
}
