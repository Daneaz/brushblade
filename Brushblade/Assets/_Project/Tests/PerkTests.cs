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

        [Test]
        public void Upgrade_RejectedBelowUnlockLevel() // 角色等级不足→拒(仅 0→1 校验)
        {
            var meta = new MetaState { Ink = 9999, CharacterXp = 0 }; // 1 级
            Assert.That(PerkRules.TryUpgradePerk(meta, "yiqi"), Is.False); // 一气需 6 级
            Assert.That(PerkRules.PerkLevel(meta, "yiqi"), Is.EqualTo(0));
            Assert.That(meta.Ink, Is.EqualTo(9999)); // 拒绝不扣墨锭
        }

        [Test]
        public void Upgrade_RejectedWithoutInk()
        {
            var meta = new MetaState { Ink = 100, CharacterXp = 100 }; // 2 级,养元解锁 200
            Assert.That(PerkRules.TryUpgradePerk(meta, "yangyuan"), Is.False);
            Assert.That(PerkRules.PerkLevel(meta, "yangyuan"), Is.EqualTo(0));
        }

        [Test]
        public void Upgrade_SucceedsAndDeductsInk()
        {
            var meta = new MetaState { Ink = 300, CharacterXp = 100 }; // 2 级
            Assert.That(PerkRules.TryUpgradePerk(meta, "yangyuan"), Is.True); // 解锁到 1 级,扣 200
            Assert.That(PerkRules.PerkLevel(meta, "yangyuan"), Is.EqualTo(1));
            Assert.That(meta.Ink, Is.EqualTo(100));
        }

        [Test]
        public void Upgrade_RejectedAtMaxLevel()
        {
            var meta = new MetaState { Ink = 99999, CharacterXp = 100 };
            meta.PerkLevels["yiqi"] = 2; // 一气已满(上限 2)
            Assert.That(PerkRules.TryUpgradePerk(meta, "yiqi"), Is.False);
            Assert.That(meta.Ink, Is.EqualTo(99999));
        }
    }
}
