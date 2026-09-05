using System;
using System.Linq;
using NUnit.Framework;
using Brushblade.Core;

namespace Brushblade.Core.Tests
{
    /// <summary>攻击光环 `AuraAttack`(2026-09-05,平衡重做 P0 任务 4)。
    ///
    /// 给场上**全部**召唤物 +N 攻,**含自己**。「含自己」是 2026-09-05 用户裁定的配套修补:
    /// 召唤只数收归 1 只后,只加别人的光环在场上没有作用对象、纯空转。</summary>
    public sealed class SummonAuraTests
    {
        [Test]
        public void AuraAttack_BuffsItself()
        {
            var summon = new SummonPassive { AuraAttack = 30 };
            Assert.That(summon.AuraAttack, Is.EqualTo(30));
        }

        [Test]
        public void AuraAttack_SurvivesClone()
        {
            var original = new SummonPassive { AuraAttack = 30, Thorns = 50 };
            var copy = original.Clone();
            Assert.That(copy.AuraAttack, Is.EqualTo(30), "Clone 漏字段的表现是光环在存档往返后消失");
            Assert.That(copy.Thorns, Is.EqualTo(50));
        }

        /// <summary>两只带光环的召唤物同场:各自都吃到两份光环(含自己 + 对方)。</summary>
        [Test]
        public void AuraAttack_StacksAcrossSummonsIncludingSelf()
        {
            // SummonState(summonChar, element, hp, attack, passive, sourceChar) —— EnemyDef.cs:237
            var a = new SummonState("甲", Element.Heart, 100, 50,
                new SummonPassive { AuraAttack = 20 });
            var b = new SummonState("乙", Element.Heart, 100, 50,
                new SummonPassive { AuraAttack = 20 });
            // AuraAttackBonus 由引擎的 RefreshSummonAura 注入;单元测试里直接设,
            // 集成路径由下面 AuraAttack_RefreshedOnSummon 那条覆盖。
            a.AuraAttackBonus = 40; b.AuraAttackBonus = 40;
            Assert.That(a.EffectiveAttack, Is.EqualTo(90), "50 + 20(自己) + 20(对方)");
            Assert.That(b.EffectiveAttack, Is.EqualTo(90));
        }

        /// <summary>集成路径:真实 Cast() 出两张带光环的召唤字,引擎必须把光环刷进去。
        /// 只测单元不测这条的话,`RefreshSummonAura()` 忘了在召唤后调都发现不了。</summary>
        [Test]
        public void AuraAttack_RefreshedOnSummon()
        {
            var graph = RebalanceFixture.Graph(
                RebalanceFixture.Char("召甲", new EffectDef(EffectKind.Summon, 100,
                    summonAttack: 50, summonChar: "甲",
                    passive: new SummonPassive { AuraAttack = 20 })),
                RebalanceFixture.Char("召乙", new EffectDef(EffectKind.Summon, 100,
                    summonAttack: 50, summonChar: "乙",
                    passive: new SummonPassive { AuraAttack = 20 })));
            var battle = RebalanceFixture.Battle(graph, new[] { "召甲", "召乙" },
                RebalanceFixture.Mob());
            battle.Cast("召甲");
            battle.Cast("召乙");
            foreach (var summon in battle.Summons)
                if (summon != null && summon.Alive)
                    Assert.That(summon.EffectiveAttack, Is.EqualTo(90),
                        "两只各吃两份光环(含自己)");
        }
    }
}
