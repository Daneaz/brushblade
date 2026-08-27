using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>召唤落位表。**玩家点的那一格永远算数**(2026-08-27 用户拍板):
    /// 第一只必落 startSlot —— 那格站着人就顶替它(UI 因此弹一次替换确认),
    /// 而不是悄悄挪到旁边的空位。第二只起才走「跳过活人、先填空槽与尸体槽、
    /// 凑不满才回头顶替」的环绕顺延。
    ///
    /// 三代语义,别混:
    ///   ① 最早是「环上取 N 个连续位」,不管占没占 —— 顺延会平白顶掉后面站着的人。
    ///   ② 2026-08-23 改成「一律跳过活人」,修掉了 ① 的误顶,但连**玩家亲手点的那一格**
    ///      也一起跳了:点在有人的格上,召唤物落到隔壁,玩家的指定失效且毫无提示。
    ///   ③ 现在是「首只听点击、余数跳活人」—— ① 的误顶与 ② 的指定失效都不在了。
    ///
    /// startSlot 本身**未解锁**时退回纯环绕扫描(Core 自守,不指望 UI 已经拦过)。</summary>
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
            // ApPerTurn 给足:预置八格要连出八张,默认 3 AP 不够(与落位规则无关的夹具需要)
            new(Graph(), new BattleConfig { PlayerMaxHp = 100, ApPerTurn = 12 },
                System.Array.Empty<string>(),
                new[] { "素", "素", "素", "素", "素", "素", "素", "素", "素" },
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
            Assert.That(engine.PlanSummonSlots(6, 3), Is.EqualTo(new[] { 6, 7, 0 }));
        }

        [Test]
        public void SingleSummon_OnOccupiedSlot_HonorsThatSlot()
        {
            // 2026-08-27 语义③ 的核心:只召 1 只时,玩家点哪格就落哪格 —— 那格站着人就顶替它,
            // 不再悄悄挪到隔壁的空位(语义② 会返回 { 3 },玩家的指定就此失效且毫无提示)
            var engine = Engine();
            Occupy(engine, 2);
            Assert.That(engine.PlanSummonSlots(2, 1), Is.EqualTo(new[] { 2 }));
        }

        [Test]
        public void SingleSummon_OnOccupiedSlot_PromptsReplace()
        {
            // 与上一条配对:落位表指向活人格 → SummonReplaceCountOf 报 1 → UI 弹替换确认。
            // 「提示替换而不是自动跳位」这个诉求,靠的就是这条链
            var engine = Engine();
            Occupy(engine, 2);
            var plan = engine.PlanSummonSlots(2, 1);
            Assert.That(engine.SummonReplaceCountOf(Graph().Get("素"), false, plan),
                Is.EqualTo(1));
        }

        [Test]
        public void MultiSummon_StartSlotOccupied_ReplacesThereThenSkipsAlive()
        {
            // 多只召唤同样听第一下点击:首只顶替选定格,**其余**才跳过活人去找空位。
            // 槽 2、3 都有人、4 起是空的 → 首只顶 2,第二只跳过 3 落 4
            var engine = Engine();
            Occupy(engine, 2, 3);
            Assert.That(engine.PlanSummonSlots(2, 2), Is.EqualTo(new[] { 2, 4 }));
        }

        [Test]
        public void StartSlotLocked_FallsBackToRingScan()
        {
            // startSlot 未解锁时不能硬塞进落位表(那会把召唤物放进锁着的格)。
            // 只开槽 1、2(与开局同型),点锁着的槽 0 → 退回环绕扫描,从槽 1 起
            var engine = new BattleEngine(Graph(),
                new BattleConfig { PlayerMaxHp = 100, ApPerTurn = 12,
                    UnlockedSummonSlots = (1 << 1) | (1 << 2) },
                System.Array.Empty<string>(), new[] { "素", "素" },
                new[] { new EnemyDef("靶", Element.Heart, 200, 0) }, seed: 1);
            var plan = engine.PlanSummonSlots(0, 1);
            Assert.That(plan, Is.EqualTo(new[] { 1 }));
            Assert.That(plan.Contains(0), Is.False, "锁着的格不能进落位表");
        }

        [Test]
        public void SkipsOccupiedSlotsAlongTheWay()
        {
            var engine = Engine();
            Occupy(engine, 3, 4);
            Assert.That(engine.PlanSummonSlots(2, 3), Is.EqualTo(new[] { 2, 5, 6 }),
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
            // 只剩 1 个空位(槽 7)却要召 3 只,且选定格 1 站着人:
            // 首只照旧听点击落 1(顶替),**其余** 2 只先吃空位 7、再回头顶替 —— 顶替同样从
            // 选定格之后顺延,于是第三只落 2。
            // (语义② 下 plan[0] 是那个唯一的空位 7;现在 7 退到第二位。)
            var engine = Engine();
            Occupy(engine, 0, 1, 2, 3, 4, 5, 6);
            var plan = engine.PlanSummonSlots(1, 3);
            Assert.That(plan.Count, Is.EqualTo(3));
            Assert.That(plan[0], Is.EqualTo(1), "首只永远是玩家点的那一格");
            Assert.That(plan[1], Is.EqualTo(7), "余数里空位优先");
            Assert.That(plan, Is.EquivalentTo(new[] { 1, 7, 2 }));
        }

        [Test]
        public void AllSlotsFull_PlanIsAllReplacements()
        {
            var engine = Engine();
            Occupy(engine, 0, 1, 2, 3, 4, 5, 6, 7);
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
            int cap = BattleEngine.MaxSummonSlots;
            for (int start = 0; start < cap; start++)
                for (int count = 1; count <= cap; count++)
                {
                    var plan = engine.PlanSummonSlots(start, count);
                    Assert.That(plan.Count, Is.EqualTo(count), $"start={start} count={count}");
                    Assert.That(new HashSet<int>(plan).Count, Is.EqualTo(count),
                        $"start={start} count={count} 出现重复下标");
                    foreach (int s in plan) Assert.That(s, Is.InRange(0, cap - 1));
                }
        }

        [Test]
        public void SingleSummon_OnOccupiedSlot_CastIsBlockedUntilConfirmed()
        {
            // 端到端把提示链走完:落位表指向活人 → Cast 返回 SummonCapFull(强阻断,**不吃 AP、
            // 不消耗字**)→ UI 弹替换确认 → 带 replaceSummon 重出才真落位。
            // 只断 SummonReplaceCountOf 守不住这条:那只是弹窗**文案**的取数,真正决定
            // 「弹不弹」的是 Cast 的返回值(BattleEngine:748 那句)。
            var engine = Engine();
            Occupy(engine, 2);
            string before = engine.Summons[2].Char;
            int apBefore = engine.Ap;

            var plan = engine.PlanSummonSlots(2, 1);
            Assert.That(engine.Cast("素", summonSlots: plan),
                Is.EqualTo(BattleError.SummonCapFull), "点在活人格上要被强阻断,交给 UI 确认");
            Assert.That(engine.Ap, Is.EqualTo(apBefore), "强阻断不该扣 AP");
            Assert.That(engine.Summons[2].Char, Is.EqualTo(before), "确认之前不许改动那一格");

            Assert.That(engine.Cast("素", replaceSummon: true, summonSlots: plan),
                Is.EqualTo(BattleError.None), "确认后带 replaceSummon 重出才落位");
            Assert.That(engine.Summons[2], Is.Not.Null);
            Assert.That(engine.Ap, Is.LessThan(apBefore));
        }

        [Test]
        public void EmptyStartSlot_MeansNoReplacementPrompt()
        {
            // 端到端对照:点的是**空**格 → 不顶任何人 → SummonReplaceCountOf 为 0 → UI 不弹确认。
            // (语义② 下「点在有人的格上」也是 0;现在那种情形归 SingleSummon_OnOccupiedSlot_PromptsReplace。)
            var engine = Engine();
            Occupy(engine, 2);
            var plan = engine.PlanSummonSlots(3, 1);
            Assert.That(plan, Is.EqualTo(new[] { 3 }));
            Assert.That(engine.SummonReplaceCountOf(Graph().Get("素"), false, plan),
                Is.EqualTo(0));
        }

        [Test]
        public void MultiSummon_EmptyStartSlot_StillSkipsAliveAlongTheWay()
        {
            // 首只听点击这条改动**不该**影响「点在空格上」的老行为:槽 2 空、3 有人、4 空
            // → [2, 4],与语义② 逐位相同
            var engine = Engine();
            Occupy(engine, 3);
            Assert.That(engine.PlanSummonSlots(2, 2), Is.EqualTo(new[] { 2, 4 }));
        }
    }
}
