using System.Collections.Generic;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>敌方排位(2026-08-20):Row/Range/Focus 三个正交字段,
    /// 每排上限 3,溢出改判到另一排。</summary>
    [TestFixture]
    public class EnemyRowTests
    {
        private static BattleEngine MakeEngine(params EnemyDef[] enemies)
        {
            var graph = new RecipeGraph(new List<CharDef> { new("木", Element.Wood) });
            var config = new BattleConfig { PlayerMaxHp = MetaRules.MaxHpFor(1) };
            return new BattleEngine(graph, config, new string[0], new string[0],
                new List<EnemyDef>(enemies), seed: 1);
        }

        private static EnemyDef Mob(string id, EnemyRow row = EnemyRow.Front) =>
            new(id, Element.Earth, 100, 0, row: row);

        [Test]
        public void EnemyDef_DefaultsToFrontMeleeDefault()
        {
            var def = new EnemyDef("错字鬼", Element.Wood, 140, 40);
            Assert.That(def.Row, Is.EqualTo(EnemyRow.Front));
            Assert.That(def.Range, Is.EqualTo(AttackRange.Melee));
            Assert.That(def.Focus, Is.EqualTo(AttackFocus.Default));
        }

        [Test]
        public void Rows_HonourPreference_WhenWithinCap()
        {
            var engine = MakeEngine(Mob("a"), Mob("b", EnemyRow.Back), Mob("c"));
            Assert.That(engine.Enemies[0].Row, Is.EqualTo(EnemyRow.Front));
            Assert.That(engine.Enemies[1].Row, Is.EqualTo(EnemyRow.Back));
            Assert.That(engine.Enemies[2].Row, Is.EqualTo(EnemyRow.Front));
        }

        [Test]
        public void Rows_OverflowToTheOtherRow_WhenPreferredIsFull()
        {
            // 四只都想站后排,后排只有 3 格 —— 第四只改判前排
            var engine = MakeEngine(Mob("a", EnemyRow.Back), Mob("b", EnemyRow.Back),
                Mob("c", EnemyRow.Back), Mob("d", EnemyRow.Back));
            int back = 0, front = 0;
            foreach (var e in engine.Enemies)
                if (e.Row == EnemyRow.Back) back++; else front++;
            Assert.That(back, Is.EqualTo(3), "后排不超过 3");
            Assert.That(front, Is.EqualTo(1), "溢出的改判前排");
            Assert.That(engine.Enemies[3].Row, Is.EqualTo(EnemyRow.Front), "改判的是排在后面的那只");
        }

        [Test]
        public void SixEnemies_NeverExceedThreePerRow()
        {
            var engine = MakeEngine(Mob("a"), Mob("b"), Mob("c"), Mob("d"), Mob("e"), Mob("f"));
            int back = 0, front = 0;
            foreach (var e in engine.Enemies)
                if (e.Row == EnemyRow.Back) back++; else front++;
            Assert.That(front, Is.EqualTo(3));
            Assert.That(back, Is.EqualTo(3));
        }

        [Test]
        public void Scale_PreservesRowRangeFocus()
        {
            var def = new EnemyDef("悬针", Element.Metal, 90, 45,
                row: EnemyRow.Back, range: AttackRange.Ranged, focus: AttackFocus.Player);
            var scaled = CampaignConfig.Scale(def, 2.0f);
            Assert.That(scaled.Row, Is.EqualTo(EnemyRow.Back));
            Assert.That(scaled.Range, Is.EqualTo(AttackRange.Ranged));
            Assert.That(scaled.Focus, Is.EqualTo(AttackFocus.Player));
            Assert.That(scaled.MaxHp, Is.GreaterThan(90), "缩放本身照常生效");
        }
    }
}
