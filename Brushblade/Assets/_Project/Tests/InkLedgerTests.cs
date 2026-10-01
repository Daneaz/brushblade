using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    public class InkLedgerTests
    {
        [Test]
        public void Gain_AddsInkAndEarned()
        {
            var meta = new MetaState { Ink = 10 };
            MetaRules.GainInk(meta, 5);
            Assert.That(meta.Ink, Is.EqualTo(15));
            Assert.That(meta.Stats.InkEarned, Is.EqualTo(5));
            Assert.That(meta.Stats.InkSpent, Is.EqualTo(0));
        }

        [Test]
        public void Spend_SubtractsInkAndCountsSpent()
        {
            var meta = new MetaState { Ink = 10 };
            MetaRules.SpendInk(meta, 4);
            Assert.That(meta.Ink, Is.EqualTo(6));
            Assert.That(meta.Stats.InkSpent, Is.EqualTo(4));
        }

        [Test]
        public void Delta_RoutesBySign_ZeroIsNoop()
        {
            var meta = new MetaState { Ink = 10 };
            MetaRules.ApplyInkDelta(meta, 3);
            MetaRules.ApplyInkDelta(meta, -2);
            MetaRules.ApplyInkDelta(meta, 0);
            Assert.That(meta.Ink, Is.EqualTo(11));
            Assert.That(meta.Stats.InkEarned, Is.EqualTo(3));
            Assert.That(meta.Stats.InkSpent, Is.EqualTo(2));
        }

        [Test]
        public void NonPositiveAmounts_AreNoops()
        {
            var meta = new MetaState { Ink = 10 };
            MetaRules.GainInk(meta, 0);
            MetaRules.SpendInk(meta, -5);
            Assert.That(meta.Ink, Is.EqualTo(10));
            Assert.That(meta.Stats.InkEarned + meta.Stats.InkSpent, Is.EqualTo(0));
        }

        [Test]
        public void TryUpgradeCard_CountsSpent()
        {
            var meta = new MetaState { Ink = 100000 };
            meta.CardCopies["x"] = 999;
            Assert.That(MetaRules.TryUpgradeCard(meta, "x"), Is.True);
            Assert.That(meta.Stats.InkSpent, Is.EqualTo(MetaRules.InkRequired(1, CardRarity.White)));
        }
    }
}
