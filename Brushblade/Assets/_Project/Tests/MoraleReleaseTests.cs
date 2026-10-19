using System;
using System.Collections.Generic;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>金脉 L4「断金」(spec 2026-10-02 §2.4;2026-10-19 并入锋刃回鞘)。
    /// 新口径(spec 2026-10-19 §2):断金不再单独判定,只在锋刃回鞘触发时(战意满 + 金系字**攻击面**带伤害)
    /// 把本张伤害从 +100% 再加到 +(100 + N)%。旧口径「任何带伤害的金字面、+N%」作废,原 FullMorale_MetalCard_QuadruplesAndClears
    /// (×4、没点断金不清战意)改为下面的 ×5 / 锋刃回鞘本身就清战意;规则本体的覆盖见 EdgeSheatheTests。</summary>
    public class MoraleReleaseTests
    {
        private static IReadOnlyList<EffectDef> Dmg() => new[] { new EffectDef(EffectKind.DamageSingle, 100) };

        private static RecipeGraph Graph() => new(new[]
        {
            new CharDef("刀", Element.Metal, isComponent: false, effects: Dmg(), attackEffects: Dmg()),
            new CharDef("火刀", Element.Fire, isComponent: false, effects: Dmg(), attackEffects: Dmg()),
            new CharDef("砺", Element.Metal, isComponent: false, effects: new[] { new EffectDef(EffectKind.CritBuff, 5) },
                attackEffects: new[] { new EffectDef(EffectKind.CritBuff, 5) }),
            new CharDef("金", Element.Metal, isComponent: true, effects: Dmg(), attackEffects: Dmg()),
        });

        private static BattleEngine Engine(int release, int morale, string[] library, string[] pool = null) =>
            new(Graph(), new BattleConfig
                {
                    DropTable = new[] { "金" }, PlayerMaxHp = 5000, ApPerTurn = 20,
                    MoraleReleasePercent = release,
                },
                library, pool ?? Array.Empty<string>(),
                new[] { new EnemyDef("靶", Element.Heart, 900000, 0) }, seed: 1,
                startingStatuses: morale > 0 ? new[] { new StatusEffect
                {
                    Kind = StatusKind.Morale, Polarity = StatusPolarity.Buff, Magnitude = morale, TurnsLeft = -1,
                } } : null);

        private static int Morale(BattleEngine e) => e.PlayerStatuses.TotalMagnitude(StatusKind.Morale);

        private static int Hit(BattleEngine e, string card, bool attack = true)
        {
            int before = e.Enemies[0].Hp;
            Assert.That(e.Cast(card, 0, attackMode: attack), Is.EqualTo(BattleError.None));
            return before - e.Enemies[0].Hp;
        }

        [Test]
        public void FullMorale_MetalAttackFace_AddsOnTopOfEdgeSheathe()
        {
            // 对照:同战意的火字(心靶无生克,战意攻击加成相同)
            int plain = Hit(Engine(300, 5, new[] { "火刀" }), "火刀");
            Assert.That(Hit(Engine(0, 5, new[] { "刀" }), "刀"), Is.EqualTo(plain * 2), "没点断金:锋刃回鞘 +100%");
            var e = Engine(300, 5, new[] { "刀" });
            Assert.That(Hit(e, "刀"), Is.EqualTo(plain * 5), "断金 +300%:+(100 + 300)%");
            Assert.That(Morale(e), Is.EqualTo(0), "释放后清空");
        }

        [Test]
        public void BelowFive_NoRelease()
        {
            var e = Engine(300, 4, new[] { "刀" });
            int hit = Hit(e, "刀");
            Assert.That(hit, Is.EqualTo(Hit(Engine(0, 4, new[] { "刀" }), "刀")));
            Assert.That(Morale(e), Is.EqualTo(4));
        }

        [Test]
        public void FeatureFace_NoLongerReleases()
        {
            // 新口径:五行面(非攻击面)即使带伤害也不触发
            var e = Engine(300, 5, new[] { "刀" });
            Assert.That(Hit(e, "刀", attack: false), Is.EqualTo(Hit(Engine(300, 5, new[] { "火刀" }), "火刀", attack: false)));
            Assert.That(Morale(e), Is.EqualTo(5));
        }

        [Test]
        public void NonMetalCard_DoesNotRelease()
        {
            var e = Engine(300, 5, new[] { "火刀" });
            Hit(e, "火刀");
            Assert.That(Morale(e), Is.EqualTo(5));
        }

        [Test]
        public void MetalComponent_DoesNotRelease()
        {
            var e = Engine(300, 5, Array.Empty<string>(), pool: new[] { "金" });
            Hit(e, "金");
            Assert.That(Morale(e), Is.EqualTo(5), "部件不算");
        }

        [Test]
        public void MetalBuffWithoutDamage_DoesNotConsume()
        {
            var e = Engine(300, 5, new[] { "砺" });
            e.Cast("砺", attackMode: true);
            Assert.That(Morale(e), Is.EqualTo(5));
        }
    }
}
