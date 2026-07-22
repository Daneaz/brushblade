using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    public class PerkTests
    {
        [Test]
        public void PerkLevel_DefaultsToZero()
        {
            var meta = new MetaState();
            Assert.That(PerkRules.PerkLevel(meta, "yangyuan"), Is.EqualTo(0));
        }

        [Test]
        public void Bonus_EqualsLevelTimesPerLevelValue()
        {
            var meta = new MetaState();
            meta.PerkLevels["yangyuan"] = 3;  // 养元 +10/级
            meta.PerkLevels["yiqi"] = 2;      // 一气 +1/级
            Assert.That(PerkRules.HpBonus(meta), Is.EqualTo(30));
            Assert.That(PerkRules.ApBonus(meta), Is.EqualTo(2));
            Assert.That(PerkRules.ShieldBonus(meta), Is.EqualTo(0));
        }

        [Test]
        public void Yiqi_MaxLevelIsTwo() // 平衡硬线:AP 上限 2
        {
            Assert.That(PerkRules.Get("yiqi").MaxLevel, Is.EqualTo(2));
        }
    }
}
