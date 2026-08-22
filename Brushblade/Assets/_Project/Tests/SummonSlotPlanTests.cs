using System.Collections.Generic;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>召唤落位表(2026-08-23 用户拍板):玩家选定起始槽后,多只召唤从那格起顺延,
    /// **先填空槽与尸体槽,撞到站着人的位子就跳过**;只有空位真的凑不满时,
    /// 才回头顶替 —— 除非六格全满,否则不该弹顶替确认。
    ///
    /// 改前是「环上取 N 个连续位」,不管占没占,于是选定格恰好有人时必弹一次替换,
    /// 而旁边明明还空着。</summary>
    public class SummonSlotPlanTests
    {
        /// <summary>素:召 1 只 10 血、攻 0 的召唤物(攻 0 = 不反击,敌人血量恒定)。</summary>
        private static RecipeGraph Graph() => new(new[]
        {
            new CharDef("素", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.Summon, 10,
                    summonCount: 1, summonAttack: 0, summonChar: "木") }),
        });

        private static BattleEngine Engine() =>
            // ApPerTurn 给足:预置六格要连出六张,默认 3 AP 不够(与落位规则无关的夹具需要)
            new(Graph(), new BattleConfig { PlayerMaxHp = 100, ApPerTurn = 10 },
                System.Array.Empty<string>(),
                new[] { "素", "素", "素", "素", "素", "素", "素" },
                new[] { new EnemyDef("靶", Element.Heart, 200, 0) }, seed: 1);

        /// <summary>在指定槽位放上存活召唤物。</summary>
        private static void Occupy(BattleEngine engine, params int[] slots)
        {
            foreach (int s in slots)
                Assert.That(engine.Cast("素", summonSlots: new[] { s }),
                    Is.EqualTo(BattleError.None), $"预置槽 {s} 失败");
        }

        [Test]
        public void AllEmpty_TakesConsecutiveSlotsFromStart()
        {
            var engine = Engine();
            Assert.That(engine.PlanSummonSlots(2, 3), Is.EqualTo(new[] { 2, 3, 4 }));
        }

        [Test]
        public void WrapsAroundPastLastSlot()
        {
            var engine = Engine();
            Assert.That(engine.PlanSummonSlots(4, 3), Is.EqualTo(new[] { 4, 5, 0 }));
        }

        [Test]
        public void StartSlotOccupied_SkipsToNextFree()
        {
            // 用户拍板的核心:选定格站着人 → 顺延到下一个空格,不提示替换
            var engine = Engine();
            Occupy(engine, 2);
            Assert.That(engine.PlanSummonSlots(2, 1), Is.EqualTo(new[] { 3 }));
        }

        [Test]
        public void SkipsOccupiedSlotsAlongTheWay()
        {
            var engine = Engine();
            Occupy(engine, 3, 4);
            Assert.That(engine.PlanSummonSlots(2, 3), Is.EqualTo(new[] { 2, 5, 0 }),
                "3、4 站着人就跳过,继续往下找空的");
        }

        [Test]
        public void CorpseSlotCountsAsFree()
        {
            // 尸体占槽但可被直接覆盖(SlotState.Corpse),不该被当成要顶替的
            var engine = Engine();
            Occupy(engine, 2);
            engine.Summons[2].Hp = 0;
            Assert.That(engine.PlanSummonSlots(2, 1), Is.EqualTo(new[] { 2 }));
        }

        [Test]
        public void NotEnoughFreeSlots_FallsBackToOccupiedOnes()
        {
            // 只剩 1 个空位却要召 3 只:先吃那个空位,不够的部分才顶替,
            // 且顶替的也从选定格起顺延
            var engine = Engine();
            Occupy(engine, 0, 1, 2, 3, 4);
            var plan = engine.PlanSummonSlots(1, 3);
            Assert.That(plan.Count, Is.EqualTo(3));
            Assert.That(plan[0], Is.EqualTo(5), "唯一的空位排在最前");
            Assert.That(plan, Is.EquivalentTo(new[] { 5, 1, 2 }));
        }

        [Test]
        public void AllSlotsFull_PlanIsAllReplacements()
        {
            var engine = Engine();
            Occupy(engine, 0, 1, 2, 3, 4, 5);
            var plan = engine.PlanSummonSlots(3, 2);
            Assert.That(plan, Is.EqualTo(new[] { 3, 4 }), "全满才从选定格起顺延顶替");
        }

        [Test]
        public void PlanIsAlwaysExactLengthAndDistinct()
        {
            // ApplyEffects 的落位循环依赖这两条不变式:长度恰好 count、下标互不重复。
            // 破坏任一条,第二只会写进同一个槽、或被静默吞掉,而 AP 已经扣了
            var engine = Engine();
            Occupy(engine, 1, 4);
            for (int start = 0; start < 6; start++)
                for (int count = 1; count <= 6; count++)
                {
                    var plan = engine.PlanSummonSlots(start, count);
                    Assert.That(plan.Count, Is.EqualTo(count), $"start={start} count={count}");
                    Assert.That(new HashSet<int>(plan).Count, Is.EqualTo(count),
                        $"start={start} count={count} 出现重复下标");
                    foreach (int s in plan) Assert.That(s, Is.InRange(0, 5));
                }
        }

        [Test]
        public void FreeSlotsAvailable_MeansNoReplacementPrompt()
        {
            // 端到端:选定格有人但别处还空着 → SummonReplaceCountOf 为 0 → UI 不弹确认
            var engine = Engine();
            Occupy(engine, 2);
            var plan = engine.PlanSummonSlots(2, 1);
            Assert.That(engine.SummonReplaceCountOf(Graph().Get("素"), false, plan),
                Is.EqualTo(0));
        }
    }
}
