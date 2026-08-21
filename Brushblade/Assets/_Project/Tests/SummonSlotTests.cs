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

        /// <summary>建一个有「甲」(召 1 只「A」)与「戊」的引擎——「戊」带**两条独立的**
        /// EffectKind.Summon 效果(各召 1 只,分别显示「P」「Q」),用来钉住「未指定槽位 +
        /// 顶替」时的游标必须跨这两条效果持续推进,不能各自从 0 起算(2026-08-20 review 抓出
        /// 的收窄作用域回归:游标一旦声明进 case 块内,第二条效果会重新顶掉第一条效果刚放
        /// 进去的那只)。</summary>
        private static BattleEngine MultiEffectSummonEngine(string[] library) => new(
            new RecipeGraph(new[]
            {
                new CharDef("甲", Element.Wood, effects: new[]
                    { new EffectDef(EffectKind.Summon, 10, summonCount: 1, summonAttack: 0, summonChar: "A") }),
                new CharDef("戊", Element.Wood, effects: new[]
                {
                    new EffectDef(EffectKind.Summon, 10, summonCount: 1, summonAttack: 0, summonChar: "P"),
                    new EffectDef(EffectKind.Summon, 10, summonCount: 1, summonAttack: 0, summonChar: "Q"),
                }),
            }),
            new BattleConfig { PlayerMaxHp = 50, ApPerTurn = 9, LibraryCapacity = 9 },
            library, new string[0],
            new[] { new EnemyDef("怔", Element.Heart, 100, 0) }, seed: 1);

        [Test]
        public void Cast_MultiEffectSummon_ReplaceMode_AdvancesAcrossEffects()
        {
            var engine = MultiEffectSummonEngine(new[] { "甲", "甲", "甲", "甲", "甲", "甲", "戊" });
            for (int i = 0; i < 6; i++) engine.Cast("甲"); // 六槽全存活占满

            Assert.That(engine.Cast("戊", replaceSummon: true), Is.EqualTo(BattleError.None));
            Assert.That(engine.AliveSummonCount, Is.EqualTo(6), "顶替不增员");
            Assert.That(engine.Summons[0].Char, Is.EqualTo("P"), "第一条 Summon 效果顶掉最前的槽 0");
            Assert.That(engine.Summons[1].Char, Is.EqualTo("Q"),
                "第二条 Summon 效果接着顶槽 1,不是把游标重置回槽 0 顶掉刚放进去的 P");
            Assert.That(engine.Summons[2].Char, Is.EqualTo("A"), "其余不动");
        }

        [Test]
        public void Cast_MultiEffectSummon_WithSlots_SpreadsAcrossEffects()
        {
            // 落位游标的另一半(2026-08-20 review I-2):指定槽位时的下标也必须跨 effect 累加。
            // 用内层的 n 的话两条效果都取 summonSlots[0],第二条撞上刚放进去的活体 →
            // occupiedByAlive && !replaceSummon → break,第二只静默蒸发(AP 已扣、字已消耗)。
            var engine = MultiEffectSummonEngine(new[] { "戊" });
            Assert.That(engine.Cast("戊", summonSlots: new[] { 1, 4 }), Is.EqualTo(BattleError.None));
            Assert.That(engine.AliveSummonCount, Is.EqualTo(2), "两条效果各落一只,一只都不许蒸发");
            Assert.That(engine.Summons[1], Is.Not.Null, "第一条 Summon 效果落 summonSlots[0] = 槽 1");
            Assert.That(engine.Summons[1].Char, Is.EqualTo("P"));
            Assert.That(engine.Summons[4], Is.Not.Null, "第二条接着取 summonSlots[1] = 槽 4,不是重回槽 1");
            Assert.That(engine.Summons[4].Char, Is.EqualTo("Q"));
            Assert.That(engine.Summons[0], Is.Null, "没点的槽一律不动");
        }
    }
}
