using System;
using System.Collections.Generic;
using Brushblade.Core;
using Brushblade.Data;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>敌人开场站位(2026-08-30):每排恒定 4 格,列号居中往外,跨列的按宽度打包。</summary>
    public class EnemySlotTests
    {
        private static RecipeGraph Graph() => new(new[]
        {
            new CharDef("木", Element.Wood),
        });

        private static EnemyDef Mob(string id, EnemyRow row = EnemyRow.Front, int span = 1,
            int rowSpan = 1) =>
            new(id, Element.Heart, 100, 10, row: row, columnSpan: span, rowSpan: rowSpan);

        /// <summary>跨两排的 Boss:中间 2 列 × 两排(2026-09-05 用户拍板的占位)。</summary>
        private static EnemyDef CrossRowBoss(string id = "霸") =>
            Mob(id, span: Targeting.BossColumnSpan, rowSpan: Targeting.RowSpanBoth);

        private static BattleEngine Engine(params EnemyDef[] enemies) =>
            new(Graph(), new BattleConfig(), Array.Empty<string>(), new[] { "木" }, enemies, seed: 1);

        // ---- 跨排 Boss(2026-09-05 用户拍板:占中间 2×2,不再占满前排一整排) ----

        /// <summary>Boss 落在中间两列、同时占两排。列 1..2 是 4 列的正中两格
        /// (ColumnOrder 居中往外的头两个),所以「居中」这件事不需要给 Boss 开特例 ——
        /// 既有的列序天然把它放在中间。</summary>
        [Test]
        public void CrossRowBoss_TakesTheTwoCenterColumnsOfBothRows()
        {
            var engine = Engine(CrossRowBoss());
            var boss = engine.Enemies[0];
            Assert.That(boss.Column, Is.EqualTo(1));
            Assert.That(boss.ColumnEnd, Is.EqualTo(3), "占列 1..2");
            Assert.That(boss.RowSpan, Is.EqualTo(2));
            Assert.That(boss.Occupies(EnemyRow.Front), Is.True);
            Assert.That(boss.Occupies(EnemyRow.Back), Is.True);
        }

        /// <summary>随从被挤到两侧列 —— Boss 的中间两列在**两排**都被占掉了。
        /// 4 只随从正好填满左右各一列 × 两排(EscortCap 那个 4 就是这么来的)。</summary>
        [Test]
        public void CrossRowBoss_EscortsFillTheFlankColumns()
        {
            var engine = Engine(CrossRowBoss(),
                Mob("卒1"), Mob("卒2"), Mob("卒3", EnemyRow.Back), Mob("卒4", EnemyRow.Back));
            for (int i = 1; i < engine.Enemies.Count; i++)
            {
                int column = engine.Enemies[i].Column;
                Assert.That(column == 0 || column == 3, Is.True,
                    $"随从 {engine.Enemies[i].Def.Id} 落在列 {column},该在两侧");
            }
        }

        /// <summary>Boss 占着前排,所以近战一开场就打得到它 ——
        /// 不必先清掉那几只随从(它占的两列里前排那一半就是它自己)。</summary>
        [Test]
        public void CrossRowBoss_IsReachableByMelee_EvenWithEscortsAlive()
        {
            var engine = Engine(CrossRowBoss(), Mob("卒", EnemyRow.Back));
            Assert.That(Targeting.CanPlayerHit(engine.Enemies, 0, ignoresRow: false), Is.True,
                "Boss 占前排,近战够得着");
        }

        /// <summary>反过来:Boss 活着就等于「前排还有人」,后排的随从因此打不到。
        /// 这一条是上一条的另一半 —— 前排清空的判定也要认得跨排 Boss。</summary>
        [Test]
        public void CrossRowBoss_BlocksBackRowEscorts()
        {
            var engine = Engine(CrossRowBoss(), Mob("卒", EnemyRow.Back));
            Assert.That(Targeting.CanPlayerHit(engine.Enemies, 1, ignoresRow: false), Is.False,
                "Boss 还站着,后排的随从够不到");
        }

        /// <summary>横扫(同排全打)对跨排 Boss:**打哪一排都扫到它,而且打两次**
        /// (用户 2026-09-05 拍板「横扫、溅射这些 boss 都打两次」)——
        /// 它在被扫的那一排上占着 2 列,横扫沿列方向扫过去就是两格。</summary>
        [Test]
        public void CrossRowBoss_TakesTwoHitsFromSweepInEitherRow()
        {
            var engine = Engine(CrossRowBoss(), Mob("前卒"), Mob("后卒", EnemyRow.Back));
            Assert.That(CountHits(Targeting.ExpandTargets(engine.Enemies, 1, TargetShape.Sweep, 0), 0),
                Is.EqualTo(2), "扫前排:Boss 占那一排的 2 列");
            Assert.That(CountHits(Targeting.ExpandTargets(engine.Enemies, 2, TargetShape.Sweep, 0), 0),
                Is.EqualTo(2), "扫后排:同理");
        }

        /// <summary>贯穿(同列前后全打)对跨排 Boss 同样是两次 —— 它在那一列上占着前后两排,
        /// 这正是「贯穿打满两只」那条口径落在一个实体上的样子。</summary>
        [Test]
        public void CrossRowBoss_TakesTwoHitsFromSkewer()
        {
            var engine = Engine(CrossRowBoss());
            Assert.That(CountHits(Targeting.ExpandTargets(engine.Enemies, 0, TargetShape.Skewer, 0), 0),
                Is.EqualTo(2));
        }

        /// <summary>普通怪不受影响:形状对它照旧一次。这一条防的是「给所有人都记了两次」。</summary>
        [Test]
        public void NormalEnemy_StillTakesOneHitFromSweep()
        {
            var engine = Engine(Mob("甲"), Mob("乙"));
            var targets = Targeting.ExpandTargets(engine.Enemies, 0, TargetShape.Sweep, 0);
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
            var targets = Targeting.ExpandTargets(engine.Enemies, -1, TargetShape.Volley, 4);
            int bossHits = 0;
            foreach (int index in targets) if (index == 0) bossHits++;
            Assert.That(bossHits, Is.EqualTo(2), "4 发均分给 2 只,Boss 不该因为跨排被多打");
        }

        [Test]
        public void ColumnOrder_IsCenterOutward()
        {
            Assert.That(Targeting.ColumnOrder, Is.EqualTo(new[] { 1, 2, 0, 3 }),
                "4 列没有正中,取中间偏左先");
        }

        [Test]
        public void AssignSlots_SingleEnemy_TakesTheCenterColumn()
        {
            var engine = Engine(Mob("甲"));
            Assert.That(engine.Enemies[0].Column, Is.EqualTo(1),
                "单怪落中间偏左,不再靠最左 —— 这一条取代了旧的 RowCells 折叠特例");
        }

        [Test]
        public void AssignSlots_TwoEnemies_AreAdjacentAtCenter()
        {
            var engine = Engine(Mob("甲"), Mob("乙"));
            Assert.That(engine.Enemies[0].Column, Is.EqualTo(1));
            Assert.That(engine.Enemies[1].Column, Is.EqualTo(2));
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
            // 前排:占 2 列的 + 两只占 1 列的 = 宽 4,正好铺满;第四只放不下,改判后排
            var engine = Engine(Mob("甲", span: 2), Mob("乙"), Mob("丙"), Mob("丁"));
            int frontWidth = 0;
            foreach (var e in engine.Enemies)
                if (e.Row == EnemyRow.Front) frontWidth += e.ColumnSpan;
            Assert.That(frontWidth, Is.LessThanOrEqualTo(Targeting.RowCapacity),
                "一排的总宽不许超过列数");
            Assert.That(engine.Enemies[3].Row, Is.EqualTo(EnemyRow.Back));
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
