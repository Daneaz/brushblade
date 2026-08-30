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

        private static EnemyDef Mob(string id, EnemyRow row = EnemyRow.Front, int span = 1) =>
            new(id, Element.Heart, 100, 10, row: row, columnSpan: span);

        private static BattleEngine Engine(params EnemyDef[] enemies) =>
            new(Graph(), new BattleConfig(), Array.Empty<string>(), new[] { "木" }, enemies, seed: 1);

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
