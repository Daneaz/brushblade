using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>火系 DOT 三分化(2026-08-09,子项目 E-a):不灭 / 立即结算 / 引爆。
    /// 规格见 docs/superpowers/specs/2026-08-09-火系DOT三分化-design.md。</summary>
    public class BurnVariantTests
    {
        private static RecipeGraph Graph() => new(new[]
        {
            new CharDef("木", Element.Wood),
            // 燃:效果同真实字表(纯灼烧 4 层);属性刻意用 心(真实是 火),
            // 隔离施加时的生克——灼烧只在结算时吃克制,施加这一步不该被生克污染测试
            new CharDef("燃", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.BurnSingle, 4) }),
            // 炽:灼烧系数 +1(与真实字表的 炽 同配置)
            new CharDef("炽", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.BurnPotency, 1) }),
        });

        private static BattleEngine Engine(string[] library, EnemyDef[] enemies,
            BattleConfig config = null, int seed = 1) =>
            new(Graph(), config ?? new BattleConfig { DropTable = new[] { "木" }, PlayerMaxHp = 200 },
                library, Array.Empty<string>(), enemies, seed);

        private static EnemyDef Dummy(int hp = 300, int attack = 0) =>
            new("靶", Element.Heart, hp, attack);

        // ---- 灼烧结算的基线(重构守卫)----

        [Test]
        public void Burn_TicksThenDecaysOneStack()
        {
            var engine = Engine(new[] { "燃" }, new[] { Dummy() });
            engine.Cast("燃", 0);
            int before = engine.Enemies[0].Hp;

            engine.EndTurn();
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(before - 8), "4 层 × 系数 2 = 8");
            Assert.That(engine.Enemies[0].Statuses.TotalMagnitude(StatusKind.Burn), Is.EqualTo(3),
                "结算后减一层");

            engine.EndTurn();
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(before - 8 - 6), "3 层 × 2 = 6");
            Assert.That(engine.Enemies[0].Statuses.TotalMagnitude(StatusKind.Burn), Is.EqualTo(2));
        }

        [Test]
        public void Burn_TicksOnlyTheBurningEnemy_WithCorrectTargetIndex()
        {
            // 评审 Important 3:原名叫「每个敌人一条」,但场上只有 1 个敌人,变异把
            // TargetIndex 写死成 0 都测不出来。这里放两个敌人,只烧 1 号,断言事件
            // 数量与 TargetIndex 都对——写死会让 2 号烧掉的血全飘到 1 号头上。
            var engine = Engine(new[] { "燃" }, new[] { Dummy(), Dummy() });
            engine.Cast("燃", 1);
            engine.EndTurn();

            var ticks = engine.LastEvents.Where(e => e.Kind == BattleEventKind.BurnTick).ToList();
            Assert.That(ticks.Count, Is.EqualTo(1), "只有 1 号敌人带灼烧");
            Assert.That(ticks[0].TargetIndex, Is.EqualTo(1), "事件要带对目标下标,不能写死成 0");
        }

        [Test]
        public void Burn_TicksBothBurningEnemies_EachWithOwnTargetIndex()
        {
            var engine = Engine(new[] { "燃", "燃" }, new[] { Dummy(), Dummy() });
            engine.Cast("燃", 0);
            engine.Cast("燃", 1);
            engine.EndTurn();

            var ticks = engine.LastEvents.Where(e => e.Kind == BattleEventKind.BurnTick)
                .OrderBy(e => e.TargetIndex).ToList();
            Assert.That(ticks.Count, Is.EqualTo(2));
            Assert.That(ticks[0].TargetIndex, Is.EqualTo(0));
            Assert.That(ticks[1].TargetIndex, Is.EqualTo(1));
        }

        [Test]
        public void Burn_RespectsKeMultiplier_NotShengMultiplier()
        {
            // 火克金 ×1.5:4 层 × 2 × 1.5 = 12。用金属性靶子才测得出克制,
            // 心属性对全属性都是 1.0x(子项目 D 的教训:同属性对同属性也是 1.0,同样测不出来)
            var engine = Engine(new[] { "燃" }, new[] { new EnemyDef("锈", Element.Metal, 300, 0) });
            engine.Cast("燃", 0);
            int before = engine.Enemies[0].Hp;
            engine.EndTurn();
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(before - 12), "4 × 2 × 1.5(火克金)");
        }

        [Test]
        public void Burn_UsesCurrentBurnPerStack_AndFloors_NotRounds()
        {
            // 评审 Minor 2:炽 在 fixture 里定义了但基线一条都没 Cast 过它,于是
            // _burnPerStack 写死成 2、Math.Floor 换成 Math.Round 都测不出来。炽 把系数从
            // 基础 2 抬到 3;3 层 × 3 × 1.5(金)= 13.5 —— floor 给 13,Math.Round 的
            // 银行家舍入会把 13.5 舍到偶数 14,两者在这里可分辨。
            var engine = Engine(new[] { "燃", "炽" }, new[] { new EnemyDef("锈", Element.Metal, 300, 0) });
            engine.Cast("燃", 0); // 4 层
            int before = engine.Enemies[0].Hp;

            engine.EndTurn(); // 系数仍是基础 2:4 × 2 × 1.5 = 12,层数减到 3
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(before - 12));

            engine.Cast("炽"); // 系数 2 → 3(全局字段,不需要选目标)
            engine.EndTurn(); // 3 × 3 × 1.5 = 13.5 → floor 13
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(before - 12 - 13),
                "floor(13.5) = 13;若用 Math.Round 会是 14");
        }

        [Test]
        public void Burn_LastStackRemovesTheStatus()
        {
            var engine = Engine(new[] { "燃" }, new[] { Dummy() });
            engine.Cast("燃", 0);
            for (int i = 0; i < 4; i++) engine.EndTurn();
            Assert.That(engine.Enemies[0].Statuses.Has(StatusKind.Burn), Is.False,
                "烧完 4 层后状态条目被移除");
        }

        [Test]
        public void Burn_KillingTheLastEnemy_EmitsEnemyDied_AndWinsTheBattle()
        {
            // 评审 Important 1:Alive/Phase 两条断言都不经过 ResolveDefeat——它只发
            // EnemyDied 事件、不碰 Phase(Phase 由外层 CheckWin() 单独决定)。补事件断言
            // 才堵住「漏调 ResolveDefeat」这个洞:死亡动画/掉落飘字全靠这条事件驱动。
            var engine = Engine(new[] { "燃" }, new[] { Dummy(hp: 6) });
            engine.Cast("燃", 0);
            engine.EndTurn();
            Assert.That(engine.Enemies[0].Alive, Is.False);
            Assert.That(engine.Phase, Is.EqualTo(BattlePhase.Won));
            Assert.That(engine.LastEvents.Count(e => e.Kind == BattleEventKind.EnemyDied
                && e.TargetIndex == 0), Is.EqualTo(1),
                "灼烧致死要发 EnemyDied —— 那是 ResolveDefeat 分支唯一的可观测产物");
        }

        [Test]
        public void Burn_DoesNotTickAnAlreadyDeadEnemy()
        {
            // 评审 Minor 1:守卫「!enemy.Alive return」原来零覆盖。0 号被烧死后,
            // 灼烧状态条目还挂着(层数从 4 减到 3,>0 不会被移除)——若没有这道守卫,
            // 下一回合会对着尸体再结算一次,多发一条 BurnTick、多杀一次。
            var engine = Engine(new[] { "燃" }, new[] { Dummy(hp: 6), Dummy() });
            engine.Cast("燃", 0);
            engine.EndTurn();
            Assert.That(engine.Enemies[0].Alive, Is.False);

            engine.EndTurn();
            Assert.That(engine.LastEvents.Any(e => e.Kind == BattleEventKind.BurnTick
                && e.TargetIndex == 0), Is.False, "死敌人不该再吃灼烧结算");
        }

        [Test]
        public void Burn_CrossingPhaseThreshold_EmitsBossPhase()
        {
            // 评审 Important 2:CheckBossPhase 分支原来零覆盖,而 Task 4(引爆)恰恰最容易
            // 一击连跨多阶。两阶 Boss,总血 13(3+10);灼烧一击 8 点打到 5,
            // ≤ 下一阶预算 10 就该换阶。BossPhaseJitterPercent 归零去掉随机浮动,阈值才可推算。
            var boss = new EnemyDef("靶", Element.Heart, 0, 0, phases: new[]
            {
                new BossPhaseDef("靶一阶", Element.Heart, 3, 0),
                new BossPhaseDef("靶二阶", Element.Heart, 10, 0),
            });
            var config = new BattleConfig
            {
                DropTable = new[] { "木" }, PlayerMaxHp = 200, BossPhaseJitterPercent = 0,
            };
            var engine = Engine(new[] { "燃" }, new[] { boss }, config);

            engine.Cast("燃", 0); // 4 层,心属性无生克:tick = 4 × 2 = 8
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(13), "总血 = 3 + 10");

            engine.EndTurn(); // 13 − 8 = 5,≤ 10(下一阶预算)→ 换阶
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(5));
            Assert.That(engine.Enemies[0].Alive, Is.True);
            Assert.That(engine.LastEvents.Any(e => e.Kind == BattleEventKind.BossPhase), Is.True,
                "灼烧把 Boss 血量打过阶段阈值时要换阶,否则 Boss 停在旧阶段属性上继续挨打");
        }
    }
}
