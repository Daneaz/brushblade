using System;
using System.Collections.Generic;
using Brushblade.Core;
using Brushblade.Data;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>敌人开场站位(2026-08-30):每排恒定 3 格(2026-10-18:4 → 3),列号居中往外,跨列的按宽度打包。</summary>
    public class EnemySlotTests
    {
        private static RecipeGraph Graph() => new(new[]
        {
            new CharDef("木", Element.Wood),
        });

        private static EnemyDef Mob(string id, EnemyRow row = EnemyRow.Front, int span = 1,
            int rowSpan = 1) =>
            new(id, Element.Heart, 100, 10, row: row, columnSpan: span, rowSpan: rowSpan);

        /// <summary>跨两排的 Boss:中列 1 列 × 两排(2026-10-18 战场改 3×2,原中间 2 列 × 两排)。</summary>
        private static EnemyDef CrossRowBoss(string id = "霸") =>
            Mob(id, span: Targeting.BossColumnSpan, rowSpan: Targeting.RowSpanBoth);

        private static BattleEngine Engine(params EnemyDef[] enemies) =>
            new(Graph(), new BattleConfig(), Array.Empty<string>(), new[] { "木" }, enemies, seed: 1);

        // ---- 跨排 Boss(2026-10-18:占中列竖两格,1 列 × 2 排) ----

        /// <summary>Boss 落在中列、同时占两排。列 1 是 3 列的正中
        /// (ColumnOrder 居中往外的第一个),所以「居中」这件事不需要给 Boss 开特例 ——
        /// 既有的列序天然把它放在中间。</summary>
        [Test]
        public void CrossRowBoss_TakesTheCenterColumnOfBothRows()
        {
            var engine = Engine(CrossRowBoss());
            var boss = engine.Enemies[0];
            Assert.That(boss.Column, Is.EqualTo(1));
            Assert.That(boss.ColumnEnd, Is.EqualTo(2), "只占列 1");
            Assert.That(boss.RowSpan, Is.EqualTo(2));
            Assert.That(boss.Occupies(EnemyRow.Front), Is.True);
            Assert.That(boss.Occupies(EnemyRow.Back), Is.True);
        }

        /// <summary>随从被挤到两侧列 —— Boss 的中列在**两排**都被占掉了。
        /// 4 只随从正好填满左右各一列 × 两排(EscortCap = 6 − 2 那个 4 就是这么来的)。</summary>
        [Test]
        public void CrossRowBoss_EscortsFillTheFlankColumns()
        {
            var engine = Engine(CrossRowBoss(),
                Mob("卒1"), Mob("卒2"), Mob("卒3", EnemyRow.Back), Mob("卒4", EnemyRow.Back));
            for (int i = 1; i < engine.Enemies.Count; i++)
            {
                int column = engine.Enemies[i].Column;
                Assert.That(column == 0 || column == 2, Is.True,
                    $"随从 {engine.Enemies[i].Def.Id} 落在列 {column},该在两侧");
            }
        }

        /// <summary>Boss 占着前排,所以近战召唤物一开场就打得到它,而且只打它 ——
        /// Boss 活着就等于「前排还有人」,后排的随从够不到。
        /// 2026-09-30 取消偷袭后玩家字卡不再有排位限制,这条改钉召唤物近战那一路
        /// (原来钉的是玩家侧的 CanPlayerHit,已删)。</summary>
        [Test]
        public void CrossRowBoss_CountsAsFrontRow_ForMeleeSummons()
        {
            var engine = Engine(CrossRowBoss(), Mob("卒", EnemyRow.Back));
            for (int seed = 1; seed <= 20; seed++)
                Assert.That(Targeting.PickEnemyTargetForSummon(engine.Enemies, ranged: false, new GameRandom(seed)),
                    Is.EqualTo(0), "Boss 占前排:近战召唤物打它,够不到后排随从");
        }

        /// <summary>横扫(同排全打)对跨排 Boss:**打哪一排都扫到它,只结算一次**
        /// (2026-10-18 战场改 3×2:Boss 在每一排只占 1 列,横扫扫过的就是一格;
        /// 原 2×2 时代「横扫、溅射 boss 都打两次」随之作废)。</summary>
        [Test]
        public void CrossRowBoss_TakesOneHitFromSweepInEitherRow()
        {
            var engine = Engine(CrossRowBoss(), Mob("前卒"), Mob("后卒", EnemyRow.Back));
            Assert.That(CountHits(Targeting.ExpandTargets(engine.Enemies, 1, TargetArea.Row, 0), 0),
                Is.EqualTo(1), "扫前排:Boss 占那一排的 1 列");
            Assert.That(CountHits(Targeting.ExpandTargets(engine.Enemies, 2, TargetArea.Row, 0), 0),
                Is.EqualTo(1), "扫后排:同理");
        }

        /// <summary>溅射(同排左右相邻)对跨排 Boss 也只一次 —— 从任一排的邻格溅过去都碰到它。</summary>
        [Test]
        public void CrossRowBoss_TakesOneHitFromSplashInEitherRow()
        {
            var engine = Engine(CrossRowBoss(), Mob("前卒"), Mob("后卒", EnemyRow.Back));
            Assert.That(CountHits(Targeting.ExpandTargets(engine.Enemies, 1, TargetArea.Adjacent, 0), 0),
                Is.EqualTo(1), "前排邻格溅到 Boss");
            Assert.That(CountHits(Targeting.ExpandTargets(engine.Enemies, 2, TargetArea.Adjacent, 0), 0),
                Is.EqualTo(1), "后排邻格溅到 Boss");
        }

        /// <summary>贯穿(同列前后全打)对跨排 Boss:它占满中列两排,贯穿该列**命中一次**
        /// (2026-10-18 Review Focus:同一大怪不被同一记打两次)。</summary>
        [Test]
        public void CrossRowBoss_TakesOneHitFromSkewer()
        {
            var engine = Engine(CrossRowBoss());
            Assert.That(CountHits(Targeting.ExpandTargets(engine.Enemies, 0, TargetArea.Column, 0), 0),
                Is.EqualTo(1));
        }

        /// <summary>贯穿打随从所在的侧列,不会穿到中列的 Boss;打 Boss 也不会穿到侧列随从。</summary>
        [Test]
        public void Skewer_StaysInItsColumn_AroundCrossRowBoss()
        {
            var engine = Engine(CrossRowBoss(), Mob("前卒"), Mob("后卒", EnemyRow.Back));
            // 前卒落列 0、后卒落列 0(中列被 Boss 两排占掉,侧列按 ColumnOrder 先左)
            Assert.That(engine.Enemies[1].Column, Is.EqualTo(0));
            Assert.That(engine.Enemies[2].Column, Is.EqualTo(0));
            var side = Targeting.ExpandTargets(engine.Enemies, 1, TargetArea.Column, 0);
            Assert.That(CountHits(side, 0), Is.EqualTo(0), "侧列贯穿不碰 Boss");
            Assert.That(CountHits(side, 2), Is.EqualTo(1), "同列后卒被贯穿");
            var center = Targeting.ExpandTargets(engine.Enemies, 0, TargetArea.Column, 0);
            Assert.That(center.Count, Is.EqualTo(1), "中列只有 Boss 一只");
        }

        /// <summary>横扫跨排 Boss 落到事件上:一次出手、一条伤害,不带 SameSwing。</summary>
        [Test]
        public void CrossRowBoss_SweepIsOneSwingOneHit()
        {
            var graph = new RecipeGraph(new[]
            {
                new CharDef("木", Element.Wood),
                // 横扫一记:主目标之外同排全扫
                new CharDef("扫", Element.Metal, new[] { "木", "木" },
                    new[] { new EffectDef(EffectKind.DamageSingle, 10, shape: TargetArea.Row) }),
            });
            var engine = new BattleEngine(graph, new BattleConfig { PlayerMaxHp = 500 },
                new[] { "扫" }, System.Array.Empty<string>(),
                new[] { CrossRowBoss("霸") }, seed: 1);

            Assert.That(engine.Cast("扫", 0), Is.EqualTo(BattleError.None));

            var damages = new List<BattleEvent>();
            foreach (var e in engine.LastEvents)
                if (e.Kind == BattleEventKind.Damage && e.TargetIndex == 0) damages.Add(e);
            Assert.That(damages.Count, Is.EqualTo(1), "一份伤害");
            Assert.That(damages[0].SameSwing, Is.False);
        }

        /// <summary>召唤物横扫跨排 Boss:**出手事件一条,伤害一条**(2026-10-18 起 Boss 只占 1 列)。
        ///
        /// 「剑」就是这条路径 —— 它是召唤字,横扫是那只召唤物出的手,走 SummonAttack 而不是
        /// 玩家侧的 Damage。SummonAttack 在表现层触发「召唤物的字飞过去」那段动作 + 一拍等待,
        /// 多一条就会让它扑第二遍(用户:「剑的横扫还是有两次动作」)。</summary>
        [Test]
        public void SummonSweep_OnCrossRowBoss_SwingsOnceHitsOnce()
        {
            var graph = new RecipeGraph(new[]
            {
                new CharDef("木", Element.Wood),
                // 召 1 只带横扫的召唤物(与「剑」同型:Sweep + 50% 溅射)
                new CharDef("剑", Element.Metal, new[] { "木", "木" },
                    new[] { new EffectDef(EffectKind.Summon, 50, summonCount: 1, summonAttack: 20,
                        summonChar: "木", passive: new SummonPassive
                        { Shape = TargetArea.Row, ShapePercent = 50 }) }),
            });
            var engine = new BattleEngine(graph, new BattleConfig { PlayerMaxHp = 500 },
                new[] { "剑" }, System.Array.Empty<string>(),
                new[] { CrossRowBoss("霸") }, seed: 1);

            Assert.That(engine.Cast("剑"), Is.EqualTo(BattleError.None));
            engine.EndTurn();   // 召唤物在回合末出手

            int swings = 0, damages = 0;
            foreach (var e in engine.LastEvents)
            {
                if (e.Kind == BattleEventKind.SummonAttack) swings++;
                if (e.Kind == BattleEventKind.Damage && e.TargetIndex == 0) damages++;
            }
            Assert.That(swings, Is.EqualTo(1), "出手一次 —— 再多就是召唤物扑了两遍");
            Assert.That(damages, Is.EqualTo(1), "受击一次 —— 竖块 Boss 在一排只占一格");
        }

        /// <summary>普通怪不受影响:形状对它照旧一次。这一条防的是「给所有人都记了两次」。</summary>
        [Test]
        public void NormalEnemy_StillTakesOneHitFromSweep()
        {
            var engine = Engine(Mob("甲"), Mob("乙"));
            var targets = Targeting.ExpandTargets(engine.Enemies, 0, TargetArea.Row, 0);
            Assert.That(CountHits(targets, 0), Is.EqualTo(1));
            Assert.That(CountHits(targets, 1), Is.EqualTo(1));
        }

        /// <summary>某个下标在结果表里出现几次。**不用 LINQ 的 Contains/Count** ——
        /// Tests 程序集在 Unity 下拿不到那几个重载(CLAUDE.md 记着这条),
        /// 而这里要数的正好也是次数而不是有无。</summary>
        private static int CountHits(System.Collections.Generic.IReadOnlyList<int> targets, int index)
        {
            int hits = 0;
            foreach (int i in targets) if (i == index) hits++;
            return hits;
        }

        /// <summary>连发的候选表里 Boss **只出现一次**。它按排收集候选(后排优先),
        /// 而跨排 Boss 两排都算 —— 不去重的话同一只怪会被连发多打一发。</summary>
        [Test]
        public void CrossRowBoss_CountsOnceInVolleyPool()
        {
            var engine = Engine(CrossRowBoss(), Mob("卒"));
            // 4 发、场上 2 只:去重后候选是 [Boss, 卒],循环两轮 → 各 2 发
            var targets = Targeting.ExpandTargets(engine.Enemies, -1, TargetArea.Scatter, 4);
            int bossHits = 0;
            foreach (int index in targets) if (index == 0) bossHits++;
            Assert.That(bossHits, Is.EqualTo(2), "4 发均分给 2 只,Boss 不该因为跨排被多打");
        }

        [Test]
        public void ColumnOrder_IsCenterOutward()
        {
            Assert.That(Targeting.ColumnOrder, Is.EqualTo(new[] { 1, 0, 2 }),
                "3 列:正中先,再左、右");
        }

        [Test]
        public void AssignSlots_SingleEnemy_TakesTheCenterColumn()
        {
            var engine = Engine(Mob("甲"));
            Assert.That(engine.Enemies[0].Column, Is.EqualTo(1),
                "单怪落正中,不靠最左 —— 这一条取代了旧的 RowCells 折叠特例");
        }

        [Test]
        public void AssignSlots_TwoEnemies_AreAdjacentAtCenter()
        {
            var engine = Engine(Mob("甲"), Mob("乙"));
            Assert.That(engine.Enemies[0].Column, Is.EqualTo(1));
            Assert.That(engine.Enemies[1].Column, Is.EqualTo(0));
        }

        [Test]
        public void AssignSlots_BossSpansWholeRow_MinionsGoToTheOtherRow()
        {
            var engine = Engine(Mob("霸", span: Targeting.RowCapacity), Mob("卒"));
            Assert.That(engine.Enemies[0].Row, Is.EqualTo(EnemyRow.Front));
            Assert.That(engine.Enemies[0].Column, Is.EqualTo(0), "占满整排,起始列只能是 0");
            Assert.That(engine.Enemies[1].Row, Is.EqualTo(EnemyRow.Back),
                "前排被 Boss 占满,小怪改判到后排");
        }

        [Test]
        public void AssignSlots_PacksByWidthNotByCount()
        {
            // 前排:占 2 列的 + 一只占 1 列的 = 宽 3,正好铺满;第三只放不下,改判后排
            var engine = Engine(Mob("甲", span: 2), Mob("乙"), Mob("丙"));
            int frontWidth = 0;
            foreach (var e in engine.Enemies)
                if (e.Row == EnemyRow.Front) frontWidth += e.ColumnSpan;
            Assert.That(frontWidth, Is.LessThanOrEqualTo(Targeting.RowCapacity),
                "一排的总宽不许超过列数");
            Assert.That(engine.Enemies[2].Row, Is.EqualTo(EnemyRow.Back));
        }

        [Test]
        public void AssignSlots_NoTwoEnemiesOverlapInAColumn()
        {
            var engine = Engine(Mob("甲", span: 2), Mob("乙"), Mob("丙"),
                Mob("丁", EnemyRow.Back), Mob("戊", EnemyRow.Back));
            var seen = new HashSet<(EnemyRow, int)>();
            foreach (var e in engine.Enemies)
                for (int c = e.Column; c < e.ColumnEnd; c++)
                    Assert.That(seen.Add((e.Row, c)), Is.True, $"列 {c} 被占了两次");
        }
    }
}
