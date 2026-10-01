
using System;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>火脉 L4「燎原」(spec 2026-10-02 §2.4)。</summary>
    public class BurnAdjacentSpreadTests
    {
        private static RecipeGraph Graph(int stacks) => new(new[]
        {
            new CharDef("燃", Element.Fire, effects: new[] { new EffectDef(EffectKind.BurnSingle, stacks) }),
        });

        // 前排三只:ColumnOrder {1,2,0} → 依次落在列 1、2、0;后排一只落在列 1
        private static BattleEngine Engine(int stacks, bool spread, int hp = 90000) =>
            new(Graph(stacks), new BattleConfig
                {
                    DropTable = new[] { "火" }, PlayerMaxHp = 5000, ApPerTurn = 20,
                    BurnSpreadAdjacent = spread,
                },
                new[] { "燃" }, Array.Empty<string>(),
                new[]
                {
                    new EnemyDef("甲", Element.Heart, hp, 0),
                    new EnemyDef("乙", Element.Heart, 90000, 0),
                    new EnemyDef("丙", Element.Heart, 90000, 0),
                    new EnemyDef("丁", Element.Heart, 90000, 0, row: EnemyRow.Back),
                }, seed: 1);

        private static int Burn(BattleEngine e, int i) =>
            e.Enemies[i].Statuses.Find(StatusKind.Burn)?.Magnitude ?? 0;

        [Test]
        public void AdjacentEnemies_AreLeftRightAndUpDown()
        {
            var e = Engine(2, spread: true);
            int center = Enumerable.Range(0, 4).First(i =>
                e.Enemies[i].Row == EnemyRow.Front && e.Enemies[i].Column == 1);
            var adj = Targeting.AdjacentEnemies(e.Enemies, center);
            // 同排列 0、列 2 + 后排列 1
            Assert.That(adj.Count, Is.EqualTo(3));
            foreach (int i in adj) Assert.That(i, Is.Not.EqualTo(center));
        }

        // 敌人按下标顺序行动,中心(下标 0)先结算:2 → 1 仍有层 → 邻居各 +1;
        // 随后每个邻居在自己那一拍把刚收到的 1 层结算掉(1 → 0,不再扩散)。
        [Test]
        public void TwoStacks_SettleToOne_Spreads()
        {
            var e = Engine(2, spread: true);
            int center = Enumerable.Range(0, 4).First(i =>
                e.Enemies[i].Row == EnemyRow.Front && e.Enemies[i].Column == 1);
            Assert.That(center, Is.EqualTo(0), "中心先行动,下面的时序断言以此为前提");
            e.Cast("燃", center);
            e.EndTurn();     // 各敌人行动前结算灼烧
            Assert.That(Burn(e, center), Is.EqualTo(1), "2 − 1 = 1");
            var adj = Targeting.AdjacentEnemies(e.Enemies, center);
            Assert.That(adj.Count, Is.EqualTo(3));
            foreach (int n in adj)
            {
                Assert.That(e.LastEvents.Count(x => x.Kind == BattleEventKind.Burn && x.TargetIndex == n),
                    Is.EqualTo(1), $"邻居 {n} 恰好收到一次扩散");
                Assert.That(e.LastEvents.Count(x => x.Kind == BattleEventKind.BurnTick && x.TargetIndex == n),
                    Is.EqualTo(1), $"邻居 {n} 在自己那一拍结算掉这 1 层");
                Assert.That(Burn(e, n), Is.EqualTo(0), $"邻居 {n} 1 → 0,不再扩散");
            }
        }

        [Test]
        public void SpreadApplication_IsNotATrigger_NoSameBeatChain()
        {
            var e = Engine(3, spread: true);
            e.Cast("燃", 0);
            e.EndTurn();
            Assert.That(Burn(e, 0), Is.EqualTo(2), "3 − 1 = 2");
            Assert.That(e.LastEvents.Count(x => x.Kind == BattleEventKind.Burn), Is.EqualTo(3),
                "只有中心那一次结算触发扩散,邻居结算 1 → 0 不再扩散");
        }

        [Test]
        public void OneStack_SettlesToZero_DoesNotSpread()
        {
            var e = Engine(1, spread: true);
            e.Cast("燃", 0);
            e.EndTurn();
            for (int i = 1; i < 4; i++) Assert.That(Burn(e, i), Is.EqualTo(0), $"敌人 {i}");
        }

        [Test]
        public void PerkOff_NoSpread()
        {
            var e = Engine(3, spread: false);
            e.Cast("燃", 0);
            e.EndTurn();
            for (int i = 1; i < 4; i++) Assert.That(Burn(e, i), Is.EqualTo(0));
        }

        [Test]
        public void KilledByBurn_DoesNotSpread()
        {
            var e = Engine(3, spread: true, hp: 10);   // 第一跳就烧死
            e.Cast("燃", 0);
            e.EndTurn();
            Assert.That(e.Enemies[0].Alive, Is.False);
            for (int i = 1; i < 4; i++) Assert.That(Burn(e, i), Is.EqualTo(0));
        }
    }
}
