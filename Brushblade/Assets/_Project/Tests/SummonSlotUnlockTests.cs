using System;
using System.Collections.Generic;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>召唤槽位按层解锁(2026-08-27 用户拍板)。解锁的是**位置**,不是「前 N 格」——
    /// 从每排中间两格往两侧开:
    ///
    /// <code>
    ///   槽位   0    1    2    3        4    5    6    7
    ///   位置  前1  前2  前3  前4      后1  后2  后3  后4
    ///   层数   16    1    1   30       16   11   11   30
    /// </code>
    ///
    /// 累计开放格数仍是 2 / 4 / 6 / 8,但开放集合**不是连续前缀** —— 落位、顶替、携带回位
    /// 一律要按集合判,不能拿「下标 &lt; 开放数」当判据。
    ///
    /// 硬上限(数组长度)恒 8。<see cref="BattleConfig.UnlockedSummonSlots"/> 缺省全开,
    /// 既有测试夹具与章节关卡路径因此一行不用改。</summary>
    public class SummonSlotUnlockTests
    {
        // ---- 曲线本身 ----

        [TestCase(1, 2)]
        [TestCase(10, 2)]
        [TestCase(11, 4)]
        [TestCase(15, 4)]
        [TestCase(16, 6)]
        [TestCase(29, 6)]
        [TestCase(30, 8)]
        [TestCase(200, 8)]
        public void SummonSlotsFor_FollowsTheUnlockBands(int depth, int slots)
        {
            Assert.That(MetaRules.SummonSlotsFor(depth), Is.EqualTo(slots));
        }

        [Test]
        public void SummonSlotsFor_ClampsNonPositiveDepthToTheFirstBand()
        {
            // 层号理应从 1 起;0 / 负数是调用方失误,给最低档比给 8 槽安全
            Assert.That(MetaRules.SummonSlotsFor(0), Is.EqualTo(2));
            Assert.That(MetaRules.SummonSlotsFor(-5), Is.EqualTo(2));
        }

        [Test]
        public void SummonSlotsFor_NeverExceedsTheHardCap()
        {
            for (int depth = 1; depth <= 500; depth++)
                Assert.That(MetaRules.SummonSlotsFor(depth),
                    Is.InRange(1, BattleEngine.MaxSummonSlots), $"第 {depth} 层");
        }

        // ---- 引擎按配置给格 ----

        private static RecipeGraph Graph() => new(new[]
        {
            new CharDef("木", Element.Wood),
            // 兵:1 只;召 3 只的 桂 用它的等价配置「群」
            new CharDef("兵", Element.Wood,
                effects: new[] { new EffectDef(EffectKind.Summon, 100, summonCount: 1, summonAttack: 3, summonChar: "木") }),
            new CharDef("群", Element.Wood,
                effects: new[] { new EffectDef(EffectKind.Summon, 100, summonCount: 3, summonAttack: 3, summonChar: "木") }),
            // 扫:一发清场,用来在层间推进(与 RunEngineTests 的 焚 同一个用途)
            new CharDef("扫", Element.Metal,
                effects: new[] { new EffectDef(EffectKind.DamageAll, 999) }),
        });

        /// <summary>按**层**建引擎:槽位开放集合由解锁表现算,与生产侧同一条路径。
        /// 直接传掩码会让测试自己复述一遍档位表,那正是两处会分叉的地方。</summary>
        private static BattleEngine AtDepth(int depth, params string[] library) =>
            new(Graph(), new BattleConfig
                {
                    DropTable = new[] { "木" }, PlayerMaxHp = 500,
                    UnlockedSummonSlots = MetaRules.UnlockedSlotMask(depth), ApPerTurn = 9,
                },
                library, Array.Empty<string>(),
                new[] { new EnemyDef("靶", Element.Heart, 3000, 0) }, seed: 1);

        [Test]
        public void DefaultConfig_OpensEverySlot()
        {
            // 缺省满开是硬线:1200+ 条既有测试的夹具都不传 UnlockedSummonSlots
            var engine = new BattleEngine(Graph(), new BattleConfig { DropTable = new[] { "木" } },
                Array.Empty<string>(), Array.Empty<string>(),
                new[] { new EnemyDef("靶", Element.Heart, 3000, 0) }, seed: 1);
            Assert.That(engine.SummonCapacity, Is.EqualTo(BattleEngine.MaxSummonSlots));
            Assert.That(engine.FrontRow, Is.EqualTo(4));
        }

        [TestCase(1)]
        [TestCase(11)]
        [TestCase(16)]
        [TestCase(30)]
        public void FrontRow_IsFixedGeometry_NotAffectedByUnlocks(int depth)
        {
            // 「前排 = 槽 [0,4)」是固定几何,不随解锁伸缩:未解锁的槽恒为 null,
            // 排位规则扫到的仍是同一段区间,结果逐位相同。表现层也靠这条把未解锁的格子
            // 画在它将来该在的那一排。
            Assert.That(AtDepth(depth, "兵").FrontRow, Is.EqualTo(4));
        }

        [Test]
        public void OpeningDepth_FillsTheTwoMiddleFrontSlots()
        {
            // 开放集合不是连续前缀:开局开的是槽 1、2(前排中间两格),槽 0 是锁着的。
            // 「找最小空槽」若按下标从 0 起扫,第一只就会落进锁着的槽 0。
            var engine = AtDepth(1, "兵", "兵");
            Assert.That(engine.Cast("兵"), Is.EqualTo(BattleError.None));
            Assert.That(engine.Cast("兵"), Is.EqualTo(BattleError.None));
            Assert.That(engine.Summons[0], Is.Null, "前 1 号位第 16 层才开");
            Assert.That(engine.Summons[1], Is.Not.Null);
            Assert.That(engine.Summons[2], Is.Not.Null);
            Assert.That(engine.Summons[3], Is.Null, "前 4 号位第 30 层才开");
        }

        [Test]
        public void CarriedSummon_InALockedSlot_FallsBackToAnOpenOne()
        {
            // 携带态记的是上一场的槽位。层数只增不减所以正常玩不会遇上,
            // 但存档被改过/档位表调整过时,回位到锁着的格会让这只召唤物彻底不出手
            var carried = new[]
            {
                new SummonSnapshot { Slot = 3, Char = "木", Element = Element.Wood,
                    Hp = 10, MaxHp = 10, Attack = 3, Speed = 100 },
            };
            var engine = new BattleEngine(Graph(), new BattleConfig
                {
                    DropTable = new[] { "木" }, PlayerMaxHp = 500,
                    UnlockedSummonSlots = MetaRules.UnlockedSlotMask(1),
                },
                Array.Empty<string>(), Array.Empty<string>(),
                new[] { new EnemyDef("靶", Element.Heart, 3000, 0) }, seed: 1,
                startingSummons: carried);

            Assert.That(engine.Summons[3], Is.Null, "槽 3 锁着,不许落进去");
            Assert.That(engine.AliveSummonCount, Is.EqualTo(1), "回落到一个开着的格,不能凭空消失");
        }

        [Test]
        public void PlanSummonSlots_WalksOnlyOpenSlots()
        {
            // 落位环只能踩开着的格。11 层开放 {1,2,5,6},从 2 起顺延 3 只 = 2 → 5 → 6
            var engine = AtDepth(11, "兵");
            Assert.That(engine.PlanSummonSlots(2, 3), Is.EqualTo(new[] { 2, 5, 6 }));
            Assert.That(engine.PlanSummonSlots(6, 3), Is.EqualTo(new[] { 6, 1, 2 }), "环回到最小的开放格");
            Assert.That(engine.PlanSummonSlots(0, 2), Is.EqualTo(new[] { 1, 2 }),
                "起始格本身锁着时,从它往后找第一个开着的");
        }

        // ---- 每一格是第几层解锁 ----

        [TestCase(0, 16)]   // 前 1
        [TestCase(1, 1)]    // 前 2 —— 开局就有
        [TestCase(2, 1)]    // 前 3 —— 开局就有
        [TestCase(3, 30)]   // 前 4
        [TestCase(4, 16)]   // 后 1
        [TestCase(5, 11)]   // 后 2
        [TestCase(6, 11)]   // 后 3
        [TestCase(7, 30)]   // 后 4
        public void UnlockDepthForSlot_MatchesTheBands(int slot, int depth)
        {
            Assert.That(MetaRules.UnlockDepthForSlot(slot), Is.EqualTo(depth));
        }

        [Test]
        public void UnlockedSlots_OpenFromTheMiddleOutwards()
        {
            Assert.That(Open(1), Is.EquivalentTo(new[] { 1, 2 }), "开局:前排中间两格");
            Assert.That(Open(10), Is.EquivalentTo(new[] { 1, 2 }), "第 10 层还没到线");
            Assert.That(Open(11), Is.EquivalentTo(new[] { 1, 2, 5, 6 }), "11 层补后排中间两格");
            Assert.That(Open(16), Is.EquivalentTo(new[] { 0, 1, 2, 4, 5, 6 }), "16 层补前后排 1 号");
            Assert.That(Open(30), Is.EquivalentTo(new[] { 0, 1, 2, 3, 4, 5, 6, 7 }), "30 层补 4 号");
        }

        [Test]
        public void UnlockedSlots_NeverShrinkWithDepth()
        {
            // 单调性:深一层不该收走已经开过的格。爬塔只前进,收回槽位没有任何触发路径,
            // 但档位表写错(比如把某格的层数填小)会静默造出一个「越深越少」的洞
            var previous = new HashSet<int>(Open(1));
            for (int depth = 2; depth <= 60; depth++)
            {
                var current = new HashSet<int>(Open(depth));
                Assert.That(current.IsSupersetOf(previous), Is.True, $"第 {depth} 层收回了格子");
                previous = current;
            }
        }

        /// <summary>该层开放的槽位下标。</summary>
        private static List<int> Open(int depth)
        {
            var open = new List<int>();
            int mask = MetaRules.UnlockedSlotMask(depth);
            for (int slot = 0; slot < BattleEngine.MaxSummonSlots; slot++)
                if ((mask & (1 << slot)) != 0) open.Add(slot);
            return open;
        }

        [Test]
        public void UnlockDepthForSlot_AgreesWithSummonSlotsFor()
        {
            // 两个方向必须互为反函数 —— 一边改档位另一边没跟上,UI 会提示错误的解锁层数
            for (int slot = 0; slot < BattleEngine.MaxSummonSlots; slot++)
            {
                int depth = MetaRules.UnlockDepthForSlot(slot);
                // 断在 bool 上而不是用 Contains.Item / Does.Not.Contain(2026-08-27):
                // Unity 自带的 NUnit 把 `Does.Not.Contain(int)` 解析成**字符串子串**约束,
                // 编译期就是 CS1503(工装的 NUnit 3.14 有集合重载,所以工装绿、编辑器红)。
                Assert.That(Open(depth).Contains(slot), Is.True,
                    $"槽 {slot} 声称第 {depth} 层解锁,那一层却没开它");
                if (depth > 1)
                    Assert.That(Open(depth - 1).Contains(slot), Is.False,
                        $"槽 {slot} 在第 {depth - 1} 层就已经开了,解锁层报晚了");
            }
        }

        [Test]
        public void LastBand_OpensEverySlot()
        {
            // 最后一档必须正好铺满硬上限:少了就有永远解锁不了的格子,多了就是配置写错
            Assert.That(MetaRules.SummonSlotsFor(int.MaxValue),
                Is.EqualTo(BattleEngine.MaxSummonSlots));
        }

        [Test]
        public void TwoSlots_ThirdSummonNeedsReplacement()
        {
            var engine = AtDepth(1, "兵", "兵", "兵");
            Assert.That(engine.Cast("兵"), Is.EqualTo(BattleError.None));
            Assert.That(engine.Cast("兵"), Is.EqualTo(BattleError.None));
            Assert.That(engine.Cast("兵"), Is.EqualTo(BattleError.SummonCapFull),
                "两格已满,第三只要顶替确认");
        }

        [Test]
        public void TwoSlots_MultiSummonCharIsCappedToTheOpenSlots()
        {
            // 桂 那类召 3 只的字在 2 槽时只召得下 2 只 —— 封顶而不是溢出崩溃
            var engine = AtDepth(1, "群");
            Assert.That(engine.SummonCountOf(Graph().Get("群")), Is.EqualTo(2));
        }

        [Test]
        public void DepthSixteen_OpensSixSlots_ThreePerRow()
        {
            var engine = AtDepth(16, "兵");
            Assert.That(engine.SummonCapacity, Is.EqualTo(6));
            Assert.That(engine.IsSlotOpen(0), Is.True);   // 前 1
            Assert.That(engine.IsSlotOpen(3), Is.False);  // 前 4 还锁着
            Assert.That(engine.IsSlotOpen(4), Is.True);   // 后 1
            Assert.That(engine.IsSlotOpen(7), Is.False);  // 后 4 还锁着
        }

        // ---- 层号接线 ----

        [Test]
        public void RunEngine_UsesTheCurrentFloorDepthNotTheSegmentStart()
        {
            // 段从第 10 层起:第 1 场是 10 层(2 槽),第 2 场是 11 层(4 槽)。
            // 拿段起始层当依据的话,整段都停在 2 槽,跨过解锁线也不涨。
            var floor = new[] { new EnemyDef("靶", Element.Heart, 1, 0) };
            var runConfig = new RunConfig
            {
                FromDepth = 10,
                Encounters = new List<IReadOnlyList<EnemyDef>> { floor, floor },
                RewardPool = new[] { "兵" },
            };
            var run = new RunEngine(Graph(), runConfig,
                new BattleConfig { DropTable = new[] { "木" }, PlayerMaxHp = 500, ApPerTurn = 9 },
                startingLibrary: new[] { "扫", "扫" }, startingPool: Array.Empty<string>(), seed: 7);

            Assert.That(run.Battle.SummonCapacity, Is.EqualTo(2), "第 10 层");

            Assert.That(run.Battle.Cast("扫"), Is.EqualTo(BattleError.None));
            Assert.That(run.Battle.Phase, Is.EqualTo(BattlePhase.Won));
            run.AdvanceAfterBattle();
            run.SkipReward();

            Assert.That(run.Battle.SummonCapacity, Is.EqualTo(4), "第 11 层跨过解锁线");
        }

        [Test]
        public void RunConfig_DefaultsToDepthOne()
        {
            // 不传 FromDepth 的调用方(章节关卡、测试夹具)落在第 1 层这一档,
            // 而不是落进「0 层」那种没意义的档
            Assert.That(new RunConfig().FromDepth, Is.EqualTo(1));
        }

    }
}
