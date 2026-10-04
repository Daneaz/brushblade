using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>spec v7 §5.2 / §11.7:硬上限集中在 CombatCaps 一处,BattleConfig 的缺省值引用它。</summary>
    public class CombatCapsTests
    {
        [Test]
        public void Values_MatchSpec()
        {
            Assert.That(CombatCaps.MoraleStacks, Is.EqualTo(5));
            Assert.That(CombatCaps.HeftStacks, Is.EqualTo(10));
            Assert.That(CombatCaps.WellspringStacks, Is.EqualTo(10));
            Assert.That(CombatCaps.ReflectPercent, Is.EqualTo(60));
        }

        [Test]
        public void BattleConfigDefaults_ComeFromCombatCaps()
        {
            var config = new BattleConfig();
            Assert.That(config.MoraleCap, Is.EqualTo(CombatCaps.MoraleStacks));
            Assert.That(config.HeftCap, Is.EqualTo(CombatCaps.HeftStacks));
            Assert.That(config.WellspringCap, Is.EqualTo(CombatCaps.WellspringStacks));
        }
    }
}
