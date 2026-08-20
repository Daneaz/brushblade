using System.Collections.Generic;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>召唤物槽位模型(2026-08-20):_summons 是定长 6 的槽位数组,下标即槽位。</summary>
    [TestFixture]
    public class SummonSlotTests
    {
        /// <summary>建一个只有「梅」(召 1 只,60 血 20 攻)和一只木桩敌人的引擎。</summary>
        private static BattleEngine MakeEngine()
        {
            var graph = new RecipeGraph(new List<CharDef>
            {
                new("木", Element.Wood),
                new("梅", Element.Wood, new[] { "木", "木" },
                    new[] { new EffectDef(EffectKind.Summon, 60, summonCount: 1, summonAttack: 20, summonChar: "梅") }),
            });
            var config = new BattleConfig { PlayerMaxHp = MetaRules.MaxHpFor(1) };
            // 敌人攻 200(一下就能打死 60 血的梅),血 9999(打不死,回合数可控)。
            // 近战默认打前排槽序最小的那只 —— KillFrontSummon 靠这条定向。
            var enemies = new List<EnemyDef> { new("木桩", Element.Earth, 9999, 200) };
            return new BattleEngine(graph, config,
                new[] { "梅", "梅", "梅", "梅", "梅", "梅", "梅" }, new string[0], enemies, seed: 1);
        }

        /// <summary>把 slot 上的召唤物打死,造出一具尸体。
        ///
        /// 不直接写 <c>Summons[slot].Hp = 0</c> —— <c>SummonState.Hp</c> 是 <c>internal set</c>,
        /// Tests 是独立程序集,跨程序集不可见;为测试放开它等于把活体状态的写权交出去。
        /// 走引擎的真实途径(敌人近战必打前排槽序最小的存活者)反而更贴近实际。
        /// 因此**只能用来打死前排里最靠前的那只**。</summary>
        private static void KillFrontSummon(BattleEngine engine, int slot)
        {
            for (int guard = 0; guard < 10
                 && engine.Summons[slot] != null && engine.Summons[slot].Alive; guard++)
                engine.EndTurn();
            Assert.That(engine.Summons[slot], Is.Not.Null);
            Assert.That(engine.Summons[slot].Alive, Is.False, "夹具前提:这只该被打死了");
        }

        [Test]
        public void Summons_IsAlwaysSixSlots_EmptyOnesAreNull()
        {
            var engine = MakeEngine();
            Assert.That(engine.Summons.Count, Is.EqualTo(6), "槽位数组恒长 6");
            for (int i = 0; i < 6; i++)
                Assert.That(engine.Summons[i], Is.Null, $"槽 {i} 开局应为空");
            Assert.That(engine.AliveSummonCount, Is.EqualTo(0));
        }

        [Test]
        public void Cast_FillsLowestEmptySlot()
        {
            var engine = MakeEngine();
            Assert.That(engine.Cast("梅"), Is.EqualTo(BattleError.None));
            Assert.That(engine.Summons[0], Is.Not.Null, "第一只落槽 0");
            Assert.That(engine.Summons[0].Char, Is.EqualTo("梅"));
            Assert.That(engine.Summons[1], Is.Null, "槽 1 仍空");
            Assert.That(engine.AliveSummonCount, Is.EqualTo(1));
        }

        [Test]
        public void CarriedSummons_KeepTheirSlots_AcrossBattles()
        {
            var engine = MakeEngine();
            engine.Cast("梅");                       // 顺序填 → 槽 0
            engine.Cast("梅");                       // 顺序填 → 槽 1
            KillFrontSummon(engine, 0);              // 槽 0 阵亡,槽 1 活着
            var carried = new List<SummonSnapshot>();
            for (int s = 0; s < engine.Summons.Count; s++)
                if (engine.Summons[s] != null && engine.Summons[s].Alive)
                    carried.Add(engine.Summons[s].Capture(s));

            Assert.That(carried.Count, Is.EqualTo(1), "只带走活的");
            Assert.That(carried[0].Slot, Is.EqualTo(1), "带走的那只记着它原来的槽位");

            var graph = new RecipeGraph(new List<CharDef> { new("木", Element.Wood) });
            var config = new BattleConfig { PlayerMaxHp = MetaRules.MaxHpFor(1) };
            var next = new BattleEngine(graph, config, new string[0], new string[0],
                new List<EnemyDef> { new("木桩", Element.Earth, 9999, 0) }, seed: 1,
                startingSummons: carried);

            Assert.That(next.Summons[0], Is.Null, "槽 0 不该被顶上来");
            Assert.That(next.Summons[1], Is.Not.Null, "站位原样保留");
            Assert.That(next.AliveSummonCount, Is.EqualTo(1));
        }

        [Test]
        public void Cast_WithExplicitSlot_LandsThere()
        {
            var engine = MakeEngine();
            Assert.That(engine.Cast("梅", summonSlots: new[] { 4 }), Is.EqualTo(BattleError.None));
            Assert.That(engine.Summons[4], Is.Not.Null, "落在玩家指定的后排槽");
            Assert.That(engine.Summons[0], Is.Null, "不再自动占前排最小槽");
        }

        [Test]
        public void Cast_OntoCorpseSlot_OverwritesWithoutReplaceFlag()
        {
            var engine = MakeEngine();
            engine.Cast("梅", summonSlots: new[] { 0 });
            KillFrontSummon(engine, 0);   // 只能打死前排最靠前的那只,所以这里用槽 0
            // 尸体槽是空位的一种:不需要 replaceSummon 确认
            Assert.That(engine.Cast("梅", summonSlots: new[] { 0 }), Is.EqualTo(BattleError.None));
            Assert.That(engine.Summons[0].Alive, Is.True);
            Assert.That(engine.AliveSummonCount, Is.EqualTo(1));
        }

        [Test]
        public void Cast_OntoLivingSlot_NeedsReplaceConfirmation()
        {
            var engine = MakeEngine();
            engine.Cast("梅", summonSlots: new[] { 2 });
            Assert.That(engine.Cast("梅", summonSlots: new[] { 2 }), Is.EqualTo(BattleError.SummonCapFull),
                "点存活槽 = 顶替,必须先确认");
            Assert.That(engine.AliveSummonCount, Is.EqualTo(1), "被拒的这次不许改动任何状态");
            Assert.That(engine.Cast("梅", replaceSummon: true, summonSlots: new[] { 2 }), Is.EqualTo(BattleError.None));
            Assert.That(engine.AliveSummonCount, Is.EqualTo(1), "顶替不增员");
        }

        [Test]
        public void SlotOccupancy_ReportsEmptyCorpseAlive()
        {
            var engine = MakeEngine();
            Assert.That(engine.SlotOccupancy(0), Is.EqualTo(SlotState.Empty));
            Assert.That(engine.SlotOccupancy(3), Is.EqualTo(SlotState.Empty), "后排空槽同样报 Empty");
            engine.Cast("梅", summonSlots: new[] { 0 });
            Assert.That(engine.SlotOccupancy(0), Is.EqualTo(SlotState.Alive));
            KillFrontSummon(engine, 0);
            Assert.That(engine.SlotOccupancy(0), Is.EqualTo(SlotState.Corpse));
        }
    }
}
