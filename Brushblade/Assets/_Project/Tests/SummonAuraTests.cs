using System;
using System.Collections.Generic;
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

        /// <summary>2026-09-05 复审 Critical 1:复活(EffectKind.Revive)把一只召唤物从死亡拉回
        /// 存活集合,是与 Summon 效果同构的「新占一个存活位」路径,却在初版实现里漏了
        /// RefreshSummonAura()。断言两头都要对——复活的这只自己要吃到含自己的光环,
        /// 场上原有的那只也要把复活者的份额重新算进总量。</summary>
        [Test]
        public void Revive_RefreshesAuraOnBothSides()
        {
            var graph = RebalanceFixture.Graph(
                RebalanceFixture.Char("召甲", new EffectDef(EffectKind.Summon, 30,
                    summonAttack: 50, summonChar: "甲",
                    passive: new SummonPassive { AuraAttack = 20 })),
                RebalanceFixture.Char("召乙", new EffectDef(EffectKind.Summon, 100,
                    summonAttack: 50, summonChar: "乙",
                    passive: new SummonPassive { AuraAttack = 20 })),
                RebalanceFixture.Char("救", new EffectDef(EffectKind.Revive, 1)));
            var battle = RebalanceFixture.Battle(graph, new[] { "召甲", "召乙", "救" },
                RebalanceFixture.Mob(attack: 999));

            battle.Cast("召甲"); // 30 血,slot 0(最前)
            battle.Cast("召乙"); // 100 血,slot 1
            battle.EndTurn();    // 敌人 999 攻一击秒杀最前一只(召甲,30 血)

            Assert.That(battle.Summons[0].Alive, Is.False, "召甲被打死");
            Assert.That(battle.Summons[1].EffectiveAttack, Is.EqualTo(70),
                "召甲死后光环只剩召乙自己那份 —— 这一步走 DamageSummon,任务 4 初版就已接对");

            battle.Cast("救"); // 复活召甲,半血回归

            Assert.That(battle.Summons[0].Alive, Is.True, "召甲应已复活");
            Assert.That(battle.Summons[0].EffectiveAttack, Is.EqualTo(90),
                "复活的召甲自己也要吃到含自己的光环 —— 这是 Revive 分支漏调 RefreshSummonAura 时会红的断言");
            Assert.That(battle.Summons[1].EffectiveAttack, Is.EqualTo(90),
                "召乙也要重新算上复活的召甲那一份");
        }

        /// <summary>2026-09-05 复审 Critical 2:Boss 技能「吞噬」(BossSkill.Devour)直接把
        /// 召唤物血量置零,完全绕开 DamageSummon —— 是一条独立的死亡路径,初版实现没接
        /// RefreshSummonAura()。</summary>
        [Test]
        public void Devour_RefreshesAuraOnKill()
        {
            var boss = new EnemyDef("噬", Element.Heart, 500, 1,
                phases: new[] { new BossPhaseDef("噬", Element.Heart, 500, 1, skill: BossSkill.Devour) });
            var graph = RebalanceFixture.Graph(
                RebalanceFixture.Char("召甲", new EffectDef(EffectKind.Summon, 50,
                    summonAttack: 50, summonChar: "甲",
                    passive: new SummonPassive { AuraAttack = 20 })),
                RebalanceFixture.Char("召乙", new EffectDef(EffectKind.Summon, 50,
                    summonAttack: 50, summonChar: "乙",
                    passive: new SummonPassive { AuraAttack = 20 })));
            var battle = RebalanceFixture.Battle(graph, new[] { "召甲", "召乙" }, boss);

            battle.EndTurn();     // Boss 普攻回合(召唤物尚未上场,不受影响);ChargeCounter → 1
            battle.Cast("召甲");   // slot 0(最前)
            battle.Cast("召乙");   // slot 1
            Assert.That(battle.Summons[1].EffectiveAttack, Is.EqualTo(90),
                "召唤之后先确认两份光环都吃到了(前置断言,验证测试搭建本身没搭错)");

            battle.EndTurn();     // ChargeCounter → 2,蓄力回合,不出手
            battle.EndTurn();     // 释放吞噬:无视血量必杀最前一只(召甲)

            Assert.That(battle.Summons[0].Alive, Is.False, "召甲被吞噬秒杀");
            Assert.That(battle.Summons[1].EffectiveAttack, Is.EqualTo(70),
                "召甲死后光环只剩召乙自己那份 —— 吞噬绕开 DamageSummon,漏接 RefreshSummonAura 时这条会红");
        }

        /// <summary>2026-09-05 复审 Important 3a:跨战斗携带(<c>BattleEngine</c> 构造函数的
        /// <c>startingSummons</c> 参数)只调用 <c>PlaceCarried</c> 落位,不会自己重算光环——
        /// <c>AuraAttackBonus</c> 缺省 0,携带进新战斗的召唤物开局那几拍会静默吃不到光环,
        /// 直到本场再触发一次召唤/死亡才被动纠正。用 <c>Summons[s].Capture(s)</c> 造快照,
        /// 与 SummonPassiveTests.Snapshot_RoundTrip_KeepsSpeedShieldAndPassive 同一条既有路数,
        /// 不必真的走一遍 JSON 序列化。</summary>
        [Test]
        public void AuraAttack_RefreshedOnStartingSummons()
        {
            var graph = RebalanceFixture.Graph(
                RebalanceFixture.Char("召甲", new EffectDef(EffectKind.Summon, 100,
                    summonAttack: 50, summonChar: "甲",
                    passive: new SummonPassive { AuraAttack = 20 })),
                RebalanceFixture.Char("召乙", new EffectDef(EffectKind.Summon, 100,
                    summonAttack: 50, summonChar: "乙",
                    passive: new SummonPassive { AuraAttack = 20 })));
            var first = RebalanceFixture.Battle(graph, new[] { "召甲", "召乙" }, RebalanceFixture.Mob());
            first.Cast("召甲");
            first.Cast("召乙");

            var carried = new List<SummonSnapshot>();
            for (int s = 0; s < first.Summons.Count; s++)
                if (first.Summons[s] != null) carried.Add(first.Summons[s].Capture(s));

            var second = new BattleEngine(graph,
                new BattleConfig { PlayerMaxHp = RebalanceFixture.BaseMaxHp, PlayerAttack = 100 },
                new[] { "召甲", "召乙" }, Array.Empty<string>(),
                new[] { RebalanceFixture.Mob() }, seed: 1, startingSummons: carried);

            foreach (var summon in second.Summons)
                if (summon != null && summon.Alive)
                    Assert.That(summon.EffectiveAttack, Is.EqualTo(90),
                        "跨战斗携带落位后,PlaceCarried 不会自己补光环,构造函数必须显式刷新一次");
        }

        /// <summary>2026-09-05 复审 Important 3b:断点续爬读档(<c>BattleEngine.Restore</c>)
        /// 与上面 startingSummons 是同一条理由的另一条代码路径——两处都调 PlaceCarried,
        /// 但走的是两个不同的方法体,漏一个不会让另一个跟着红。</summary>
        [Test]
        public void AuraAttack_RefreshedOnRestore()
        {
            var graph = RebalanceFixture.Graph(
                RebalanceFixture.Char("召甲", new EffectDef(EffectKind.Summon, 100,
                    summonAttack: 50, summonChar: "甲",
                    passive: new SummonPassive { AuraAttack = 20 })),
                RebalanceFixture.Char("召乙", new EffectDef(EffectKind.Summon, 100,
                    summonAttack: 50, summonChar: "乙",
                    passive: new SummonPassive { AuraAttack = 20 })));
            var mob = RebalanceFixture.Mob();
            var origin = RebalanceFixture.Battle(graph, new[] { "召甲", "召乙" }, mob);
            origin.Cast("召甲");
            origin.Cast("召乙");

            var defs = new Dictionary<string, EnemyDef> { [mob.Id] = mob };
            var restored = BattleEngine.Restore(origin.Capture(), graph,
                new BattleConfig { PlayerMaxHp = RebalanceFixture.BaseMaxHp, PlayerAttack = 100 },
                null, defs);

            foreach (var summon in restored.Summons)
                if (summon != null && summon.Alive)
                    Assert.That(summon.EffectiveAttack, Is.EqualTo(90),
                        "读档复原后,PlaceCarried 不会自己补光环,Restore 必须显式刷新一次");
        }

        /// <summary>召唤物攻击吃玩家的战意 + 厚(2026-09-05,推翻 2026-08-28 的「战意玩家专属」)。
        ///
        /// 战意 1 层 = +10%、5 层 = +50%;厚 1 层 = +5%、10 层 = +50%。满 buff = ×2。
        /// 归零后回到基础值 —— 乘区是现读的,不是出手时冻结的。</summary>
        [Test]
        public void SummonAttack_ReadsMoraleAndHeft()
        {
            var summon = new SummonState("甲", Element.Heart, 100, 100);
            summon.PlayerAttackPercent = 100;
            Assert.That(summon.EffectiveAttack, Is.EqualTo(100), "无 buff 时恒等 —— 恒等性硬线");

            summon.PlayerAttackPercent = 110;   // 战意 1 层
            Assert.That(summon.EffectiveAttack, Is.EqualTo(110));

            summon.PlayerAttackPercent = 150;   // 战意 5 层
            Assert.That(summon.EffectiveAttack, Is.EqualTo(150));

            summon.PlayerAttackPercent = 200;   // 战意 5 层 + 厚 10 层
            Assert.That(summon.EffectiveAttack, Is.EqualTo(200));

            summon.PlayerAttackPercent = 100;   // 战意衰减归零
            Assert.That(summon.EffectiveAttack, Is.EqualTo(100), "归零回到基础值,乘区是现读的");
        }

        /// <summary>缺省 100:老存档里的召唤物没有这个字段,反序列化得 0 会把攻击归零。</summary>
        [Test]
        public void SummonAttack_DefaultPercent_IsHundred()
        {
            var summon = new SummonState("甲", Element.Heart, 100, 77);
            Assert.That(summon.EffectiveAttack, Is.EqualTo(77), "没显式设 percent 也不能打成 0");
        }

        /// <summary>加点(AttackBuff / 光环)先加,百分比后乘 —— 与玩家侧
        /// EffectiveAttack 的「先加后乘」同序。反过来会让加点吃不到战意的放大。</summary>
        [Test]
        public void SummonAttack_FlatBeforePercent()
        {
            var summon = new SummonState("甲", Element.Heart, 100, 100);
            summon.AuraAttackBonus = 50;
            summon.PlayerAttackPercent = 150;
            Assert.That(summon.EffectiveAttack, Is.EqualTo(225), "(100 + 50) × 1.5,不是 100×1.5 + 50");
        }
    }
}
