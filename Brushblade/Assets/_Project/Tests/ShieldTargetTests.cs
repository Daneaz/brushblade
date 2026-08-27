using System;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>护盾选友方目标(2026-08-26)。土系 5 张护盾字(㙓/垚/圭/垒/壁)从「只能加给
    /// 玩家」改成「加给玩家或某只召唤物」,与单体治疗(spec §8.1)共用同一套
    /// NeedsAllyTarget / CanHealSlot / allySlot 流程,不写第二份。
    ///
    /// 召唤物没有豁免桶(SummonState.Shield 只有一个整数),所以 persistOnce 的那一张(㙓)
    /// 加到召唤物身上时并进同一个 Shield —— 豁免桶本来就是玩家侧「倾覆清盾」的对策,
    /// 召唤物不吃倾覆。</summary>
    public class ShieldTargetTests
    {
        private static RecipeGraph Graph() => new(new[]
        {
            new CharDef("木", Element.Wood),
            // 垒:护盾 50(纯盾,不带伤害;真实字表的 垒 还带 DamageSingle 30,那一支单独测)
            new CharDef("垒", Element.Earth,
                effects: new[] { new EffectDef(EffectKind.Shield, 50) }),
            // 㙓:豁免桶护盾 450
            new CharDef("㙓", Element.Earth,
                effects: new[] { new EffectDef(EffectKind.Shield, 450, persistOnce: true) }),
            // 圭:护盾 + 单体伤害 —— 这张要**先选敌人再选友方**
            new CharDef("圭", Element.Earth,
                effects: new[] { new EffectDef(EffectKind.Shield, 200),
                                 new EffectDef(EffectKind.DamageSingle, 135) }),
            // 兵:普通召唤物,当收盾方
            new CharDef("兵", Element.Wood,
                effects: new[] { new EffectDef(EffectKind.Summon, 100, summonCount: 1, summonAttack: 3, summonChar: "木") }),
        });

        private static BattleEngine Engine(string[] library, EnemyDef[] enemies = null, int seed = 1) =>
            new(Graph(), new BattleConfig { DropTable = new[] { "木" }, PlayerMaxHp = 500 },
                library, Array.Empty<string>(),
                enemies ?? new[] { new EnemyDef("靶", Element.Heart, 3000, 0) }, seed);

        [Test]
        public void NeedsAllyTarget_TrueForShield()
        {
            Assert.That(BattleEngine.NeedsAllyTarget(Graph().Get("垒")), Is.True);
            Assert.That(BattleEngine.NeedsAllyTarget(Graph().Get("㙓")), Is.True);
            Assert.That(BattleEngine.NeedsAllyTarget(Graph().Get("圭")), Is.True);
            Assert.That(BattleEngine.NeedsAllyTarget(Graph().Get("兵")), Is.False, "召唤字不选友方");
        }

        [Test]
        public void Shield_DefaultsToPlayer()
        {
            // 不传 allySlot 时口径与改前逐位相同 —— 上千条既有测试靠这条不变
            var engine = Engine(new[] { "垒" });
            engine.Cast("垒");
            Assert.That(engine.PlayerShield, Is.EqualTo(50));
        }

        [Test]
        public void Shield_GoesToTheChosenSummon()
        {
            var engine = Engine(new[] { "兵", "垒" });
            engine.Cast("兵");
            engine.Cast("垒", allySlot: 0);

            Assert.That(engine.Summons[0].Shield, Is.EqualTo(50));
            Assert.That(engine.PlayerShield, Is.EqualTo(0), "选了召唤物,玩家就一分不得");
        }

        [Test]
        public void Shield_PersistOnceGoesIntoTheSummonsSingleBucket()
        {
            // 召唤物没有豁免桶,㙓 的 450 并进同一个 Shield
            var engine = Engine(new[] { "兵", "㙓" });
            engine.Cast("兵");
            engine.Cast("㙓", allySlot: 0);

            Assert.That(engine.Summons[0].Shield, Is.EqualTo(450));
            Assert.That(engine.ShieldPersist, Is.EqualTo(0));
            Assert.That(engine.ShieldNormal, Is.EqualTo(0));
        }

        [Test]
        public void Shield_EventCarriesTheSlot()
        {
            // 表现层据 TargetIndex 决定动哪条盾条:−1 = 玩家血条区,≥0 = 那一格召唤物
            var engine = Engine(new[] { "兵", "垒" });
            engine.Cast("兵");
            engine.Cast("垒", allySlot: 0);
            var shieldEvent = engine.LastEvents.Single(e => e.Kind == BattleEventKind.Shield);
            Assert.That(shieldEvent.TargetIndex, Is.EqualTo(0));

            var engine2 = Engine(new[] { "垒" });
            engine2.Cast("垒");
            Assert.That(engine2.LastEvents.Single(e => e.Kind == BattleEventKind.Shield).TargetIndex,
                Is.EqualTo(Targeting.PlayerTarget));
        }

        [Test]
        public void Shield_AbsorbsForTheSummonItWasGivenTo()
        {
            // 端到端:盾加在召唤物身上,它挨打时先吃盾
            var engine = Engine(new[] { "兵", "垒" },
                new[] { new EnemyDef("拳", Element.Heart, 3000, 30) });
            engine.Cast("兵");
            engine.Cast("垒", allySlot: 0);
            int hpBefore = engine.Summons[0].Hp;

            engine.EndTurn();

            Assert.That(engine.Summons[0].Hp, Is.EqualTo(hpBefore), "30 伤全被 50 盾吃掉");
            Assert.That(engine.Summons[0].Shield, Is.EqualTo(20));
        }

        [Test]
        public void Shield_RejectsCorpseSlot()
        {
            // 与治疗同一条口径:点着一具占槽的尸体是玩家点错了,不能悄悄改判成加给玩家
            var engine = Engine(new[] { "兵", "垒" });
            engine.Cast("兵");
            engine.Summons[0].Hp = 0;

            Assert.That(engine.Cast("垒", allySlot: 0), Is.EqualTo(BattleError.InvalidTarget));
            Assert.That(engine.PlayerShield, Is.EqualTo(0), "拒出就不该扣字扣 AP,更不该加盾");
        }

        [Test]
        public void Shield_AutoLocksToPlayerWhenNoSummonAlive()
        {
            // 免选口径:场上没有存活召唤物时自动锁玩家,不让 UI 弹一次没得选的选择
            var engine = Engine(new[] { "垒" });
            Assert.That(engine.Cast("垒", allySlot: 3), Is.EqualTo(BattleError.None));
            Assert.That(engine.PlayerShield, Is.EqualTo(50));
        }

        [Test]
        public void Shield_WithDamage_TakesBothTargets()
        {
            // 圭 = 护盾 200 + 单体 135:敌人目标与友方目标互不干扰
            var engine = Engine(new[] { "兵", "圭" },
                new[] { new EnemyDef("靶", Element.Heart, 3000, 0) });
            engine.Cast("兵");
            int enemyHp = engine.Enemies[0].Hp;

            engine.Cast("圭", targetIndex: 0, allySlot: 0);

            Assert.That(engine.Summons[0].Shield, Is.EqualTo(200));
            Assert.That(engine.Enemies[0].Hp, Is.LessThan(enemyHp));
            Assert.That(engine.PlayerShield, Is.EqualTo(0));
        }

        [Test]
        public void RealConfig_AllEarthShieldCharsNeedAllyTarget()
        {
            var graph = RealGraph();
            foreach (string id in new[] { "㙓", "垚", "圭", "垒", "壁" })
                Assert.That(BattleEngine.NeedsAllyTarget(graph.Get(id)), Is.True, id);
        }

        private static RecipeGraph RealGraph() => CharTableTests.RealGraph();
    }
}
