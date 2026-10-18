using System.Collections.Generic;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>词条气泡的连带词条:冻结的说明里写了「Boss 改为冰滞」,气泡里有冻结就要能看到冰滞(2026-10-18 体验反馈)。</summary>
    public class GlossaryCompanionTests
    {
        [Test]
        public void Freeze_BringsIceStall_RightAfterIt()
        {
            var terms = GlossaryRules.WithCompanions(new List<GlossaryTerm> { GlossaryTerm.Freeze, GlossaryTerm.Slow });
            Assert.That(terms, Is.EqualTo(new[] { GlossaryTerm.Freeze, GlossaryTerm.IceStall, GlossaryTerm.Slow }));
        }

        [Test]
        public void IceStallAlreadyPresent_NotDuplicated()
        {
            var terms = GlossaryRules.WithCompanions(new List<GlossaryTerm> { GlossaryTerm.Mark, GlossaryTerm.Freeze, GlossaryTerm.IceStall });
            Assert.That(terms, Is.EqualTo(new[] { GlossaryTerm.Mark, GlossaryTerm.Freeze, GlossaryTerm.IceStall }));
        }

        [Test]
        public void NoFreeze_Unchanged()
        {
            var terms = GlossaryRules.WithCompanions(new List<GlossaryTerm> { GlossaryTerm.Burn, GlossaryTerm.IceStall });
            Assert.That(terms, Is.EqualTo(new[] { GlossaryTerm.Burn, GlossaryTerm.IceStall }));
        }
    }
}
