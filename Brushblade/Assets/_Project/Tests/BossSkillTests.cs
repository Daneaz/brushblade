using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>Boss 技能系统(蓄力预警制):spec 见
    /// docs/superpowers/specs/2026-07-28-boss-skills-design.md</summary>
    public class BossSkillTests
    {
        [Test]
        public void PhaseDef_CarriesSkill_DefaultsToNone()
        {
            var withSkill = new BossPhaseDef("海", Element.Water, 16, 10, skill: BossSkill.Deluge);
            var without = new BossPhaseDef("干", Element.Wood, 12, 6);

            Assert.That(withSkill.Skill, Is.EqualTo(BossSkill.Deluge));
            Assert.That(without.Skill, Is.EqualTo(BossSkill.None));
        }

        [Test]
        public void Scale_PreservesSkill()
        {
            var boss = new EnemyDef("试炼", Element.Water, 12, 6, phases: new[]
            {
                new BossPhaseDef("排", Element.Metal, 12, 6, skill: BossSkill.Topple),
                new BossPhaseDef("海", Element.Water, 16, 10, skill: BossSkill.Deluge),
            });

            var scaled = CampaignConfig.Scale(boss, 2f);

            Assert.That(scaled.Phases[0].Skill, Is.EqualTo(BossSkill.Topple));
            Assert.That(scaled.Phases[1].Skill, Is.EqualTo(BossSkill.Deluge));
            Assert.That(scaled.Phases[1].MaxHp, Is.EqualTo(32)); // 数值照常缩放
        }
    }
}
