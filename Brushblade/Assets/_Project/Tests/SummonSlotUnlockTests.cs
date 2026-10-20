using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>召唤槽位按层解锁(2026-08-27 用户拍板;2026-09-30 改为每档一格;
    /// 2026-10-18 战场改 3×2,8 格 → 6 格)。解锁的是**位置**,不是「前 N 格」——
    /// 2026-10-20 改为前排、后排交替开,每排仍居中往外(中 → 左 → 右):
    /// 前中 → 后中 → 前左 → 后左 → 前右 → 后右。
    ///
    /// <code>
    ///   槽位   0    1    2        3    4    5
    ///   位置  前左 前中 前右      后左 后中 后右
    ///   层数   13    1   25       19    7   31
    /// </code>
    ///
    /// 2026-09-30 用户拍板:召唤物不超过本层敌人总数(Boss 按占格算 2 只)。档位线
    /// 1/7/13/19/25/31 正是普通层敌人数 1+min(5,(d−1)/6) 的跳变层,见
    /// <see cref="SummonSlots_NeverExceedTheFloorsEnemyCount"/>。
    ///
    /// 累计开放格数 1…6,开放集合**不是连续前缀** —— 落位、顶替、携带回位
    /// 一律要按集合判,不能拿「下标 &lt; 开放数」当判据。
    ///
    /// 硬上限(数组长度)恒 6。<see cref="BattleConfig.UnlockedSummonSlots"/> 缺省全开,
    /// 既有测试夹具与章节关卡路径因此一行不用改。</summary>
    public class SummonSlotUnlockTests
    {
        // ---- 曲线本身 ----

        [TestCase(1, 1)]
        [TestCase(6, 1)]
        [TestCase(7, 2)]
        [TestCase(12, 2)]
        [TestCase(13, 3)]
        [TestCase(19, 4)]
        [TestCase(25, 5)]
        [TestCase(30, 5)]
        [TestCase(31, 6)]
        [TestCase(200, 6)]
        public void SummonSlotsFor_FollowsTheUnlockBands(int depth, int slots)
        {
            Assert.That(MetaRules.SummonSlotsFor(depth), Is.EqualTo(slots));
        }

        [Test]
        public void SummonSlotsFor_ClampsNonPositiveDepthToTheFirstBand()
        {
            // 层号理应从 1 起;0 / 负数是调用方失误,给最低档比给满槽安全
            Assert.That(MetaRules.SummonSlotsFor(0), Is.EqualTo(1));
            Assert.That(MetaRules.SummonSlotsFor(-5), Is.EqualTo(1));
        }

        [Test]
        public void SummonSlots_NeverExceedTheFloorsEnemyCount()
        {
            // 2026-09-30 用户拍板:召唤物不超过本层敌人总数。纯静态表,所以要对**每一层**成立,
            // 包括敌人最少的 Boss 层 —— Boss 按占格算 2 只(= 敌方总格位 − 随从上限)。
            // 分裂在战中加敌不算:表只对开场阵容负责。
            var config = new EndlessConfig();
            int bossWeight = Targeting.RowCapacity * 2 - EndlessGenerator.EscortCap;
            Assert.That(bossWeight, Is.EqualTo(2), "夹具前提:Boss 占 1 列 × 2 排");
            for (int depth = 1; depth <= 200; depth++)
            {
                int enemies = config.IsBossDepth(depth)
                    ? bossWeight + EndlessGenerator.EscortCountFor(config, depth)
                    : EndlessGenerator.MinionCountFor(depth);
                Assert.That(MetaRules.SummonSlotsFor(depth), Is.LessThanOrEqualTo(enemies),
                    $"第 {depth} 层:召唤格多于敌人数 {enemies}");
            }
        }

        [Test]
        public void SummonSlots_TrackTheNormalFloorEnemyCountExactly()
        {
            // 反方向:不许比敌人数「少」得太多 —— 普通层两者逐层相等,档位表才没有白白压格
            for (int depth = 1; depth <= 200; depth++)
                Assert.That(MetaRules.SummonSlotsFor(depth),
                    Is.EqualTo(EndlessGenerator.MinionCountFor(depth)), $"第 {depth} 层");
        }

        [Test]
        public void Grid_IsThreeByTwo_OnBothSides()
        {
            // 2026-10-18 战场改 3×2:敌方每排 3 列、召唤槽 6(前 3 + 后 3)、Boss 1 列 × 2 排、
            // 随从上限 = 6 − Boss 占的 2 格 = 4
            Assert.That(Targeting.RowCapacity, Is.EqualTo(3));
            Assert.That(BattleEngine.MaxSummonSlots, Is.EqualTo(6));
            Assert.That(BattleEngine.AllSummonSlotsMask, Is.EqualTo(0b111111));
            Assert.That(Targeting.BossColumnSpan, Is.EqualTo(1));
            Assert.That(EndlessGenerator.EscortCap, Is.EqualTo(4));
            Assert.That(EndlessGenerator.MinionCountFor(30), Is.EqualTo(5));
            Assert.That(EndlessGenerator.MinionCountFor(31), Is.EqualTo(6));
            Assert.That(EndlessGenerator.MinionCountFor(500), Is.EqualTo(6));
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
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 999, shape: TargetArea.All) }),
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
            Assert.That(engine.FrontRow, Is.EqualTo(3));
        }

        [TestCase(1)]
        [TestCase(11)]
        [TestCase(16)]
        [TestCase(30)]
        public void FrontRow_IsFixedGeometry_NotAffectedByUnlocks(int depth)
        {
            // 「前排 = 槽 [0,3)」是固定几何,不随解锁伸缩:未解锁的槽恒为 null,
            // 排位规则扫到的仍是同一段区间,结果逐位相同。表现层也靠这条把未解锁的格子
            // 画在它将来该在的那一排。
            Assert.That(AtDepth(depth, "兵").FrontRow, Is.EqualTo(3));
        }

        [Test]
        public void OpeningDepth_FillsTheMiddleFrontSlot()
        {
            // 开放集合不是连续前缀:第 1 层开的是槽 1(前中),槽 0 是锁着的。
            // 「找最小空槽」若按下标从 0 起扫,第一只就会落进锁着的槽 0。
            var engine = AtDepth(1, "兵");
            Assert.That(engine.Cast("兵"), Is.EqualTo(BattleError.None));
            Assert.That(engine.Summons[0], Is.Null, "前左第 7 层才开");
            Assert.That(engine.Summons[1], Is.Not.Null);
        }

        [Test]
        public void DepthSeven_FillsFrontMiddleAndBackMiddle()
        {
            // 2026-10-20:前后排交替开,第 7 层的第二格是后中,不是前左
            var engine = AtDepth(7, "兵", "兵");
            Assert.That(engine.Cast("兵"), Is.EqualTo(BattleError.None));
            Assert.That(engine.Cast("兵"), Is.EqualTo(BattleError.None));
            Assert.That(engine.Summons[1], Is.Not.Null);
            Assert.That(engine.Summons[4], Is.Not.Null);
            Assert.That(engine.Summons[0], Is.Null, "前左第 13 层才开");
            Assert.That(engine.Summons[3], Is.Null, "后左第 19 层才开");
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
            // 落位环只能踩开着的格。19 层开放 {0,1,3,4},从 1 起顺延 3 只 = 1 → 3 → 4
            var engine = AtDepth(19, "兵");
            Assert.That(engine.PlanSummonSlots(1, 3), Is.EqualTo(new[] { 1, 3, 4 }));
            Assert.That(engine.PlanSummonSlots(4, 3), Is.EqualTo(new[] { 4, 0, 1 }), "环回到最小的开放格");
            Assert.That(engine.PlanSummonSlots(2, 2), Is.EqualTo(new[] { 3, 4 }),
                "起始格本身锁着时,从它往后找第一个开着的");
        }

        // ---- 每一格是第几层解锁 ----

        [TestCase(0, 13)]   // 前左
        [TestCase(1, 1)]    // 前中 —— 开局就有
        [TestCase(2, 25)]   // 前右
        [TestCase(3, 19)]   // 后左
        [TestCase(4, 7)]    // 后中
        [TestCase(5, 31)]   // 后右
        public void UnlockDepthForSlot_MatchesTheBands(int slot, int depth)
        {
            Assert.That(MetaRules.UnlockDepthForSlot(slot), Is.EqualTo(depth));
        }

        [Test]
        public void UnlockedSlots_AlternateFrontAndBack_MiddleOutwards()
        {
            Assert.That(Open(1), Is.EquivalentTo(new[] { 1 }), "开局:前排中间一格");
            Assert.That(Open(6), Is.EquivalentTo(new[] { 1 }), "第 6 层还没到线");
            Assert.That(Open(7), Is.EquivalentTo(new[] { 1, 4 }), "7 层开后中");
            Assert.That(Open(13), Is.EquivalentTo(new[] { 0, 1, 4 }), "13 层补前左");
            Assert.That(Open(19), Is.EquivalentTo(new[] { 0, 1, 3, 4 }), "19 层补后左");
            Assert.That(Open(25), Is.EquivalentTo(new[] { 0, 1, 2, 3, 4 }), "25 层补前右,前排齐");
            Assert.That(Open(31), Is.EquivalentTo(new[] { 0, 1, 2, 3, 4, 5 }), "31 层补后右");
        }

        [Test]
        public void UnlockOrder_IsFrontBackFrontBackFrontBack()
        {
            // 2026-10-20 用户拍板:第 1~6 个解锁的格依次落在 前/后/前/后/前/后
            int[] bands = { 1, 7, 13, 19, 25, 31 };
            var previous = new HashSet<int>();
            for (int i = 0; i < bands.Length; i++)
            {
                var current = new HashSet<int>(Open(bands[i]));
                current.ExceptWith(previous);
                Assert.That(current.Count, Is.EqualTo(1), $"第 {bands[i]} 层应正好新开一格");
                int slot = current.Single();
                bool front = slot < 3;
                Assert.That(front, Is.EqualTo(i % 2 == 0), $"第 {i + 1} 个解锁的格(槽 {slot})排别不对");
                previous.UnionWith(current);
            }
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
            var engine = AtDepth(7, "兵", "兵", "兵");
            Assert.That(engine.Cast("兵"), Is.EqualTo(BattleError.None));
            Assert.That(engine.Cast("兵"), Is.EqualTo(BattleError.None));
            Assert.That(engine.Cast("兵"), Is.EqualTo(BattleError.SummonCapFull),
                "两格已满,第三只要顶替确认");
        }

        [Test]
        public void TwoSlots_MultiSummonCharIsCappedToTheOpenSlots()
        {
            // 桂 那类召 3 只的字在 2 槽时只召得下 2 只 —— 封顶而不是溢出崩溃
            var engine = AtDepth(7, "群");
            Assert.That(engine.SummonCountOf(Graph().Get("群")), Is.EqualTo(2));
        }

        [Test]
        public void DepthTwentyFive_OpensFiveSlots_RightBackStillLocked()
        {
            var engine = AtDepth(25, "兵");
            Assert.That(engine.SummonCapacity, Is.EqualTo(5));
            Assert.That(engine.IsSlotOpen(2), Is.True);   // 前右 25 层开
            Assert.That(engine.IsSlotOpen(5), Is.False);  // 后右 31 层才开
            Assert.That(engine.IsSlotOpen(6), Is.False);  // 越界:硬上限 6 格
        }

        // ---- 层号接线 ----

        [Test]
        public void RunEngine_UsesTheCurrentFloorDepthNotTheSegmentStart()
        {
            // 段从第 12 层起:第 1 场是 12 层(2 槽),第 2 场是 13 层(3 槽)。
            // 拿段起始层当依据的话,整段都停在 2 槽,跨过解锁线也不涨。
            var floor = new[] { new EnemyDef("靶", Element.Heart, 1, 0) };
            var runConfig = new RunConfig
            {
                FromDepth = 12,
                Encounters = new List<IReadOnlyList<EnemyDef>> { floor, floor },
                RewardPool = new[] { "兵" },
            };
            var run = new RunEngine(Graph(), runConfig,
                new BattleConfig { DropTable = new[] { "木" }, PlayerMaxHp = 500, ApPerTurn = 9 },
                startingLibrary: new[] { "扫", "扫" }, startingPool: Array.Empty<string>(), seed: 7);

            Assert.That(run.Battle.SummonCapacity, Is.EqualTo(2), "第 12 层");

            Assert.That(run.Battle.Cast("扫"), Is.EqualTo(BattleError.None));
            Assert.That(run.Battle.Phase, Is.EqualTo(BattlePhase.Won));
            run.AdvanceAfterBattle();
            run.SkipReward();

            Assert.That(run.Battle.SummonCapacity, Is.EqualTo(3), "第 13 层跨过解锁线");
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
