using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>配方转盘(spec 2026-10-19 §2):5 层起 Boss / 精英层战后,4 低档 + 1 高档,高档 10%。</summary>
    public class RecipeWheelTests
    {
        // 部件:五行偏旁 + 两个声旁
        private static CharDef Part(string id, Element? e = null) => new(id, e);

        private static CharDef Char(string id, CardRarity rarity, params string[] recipe) =>
            new(id, Element.Fire, recipe, rarity: rarity,
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 6, shape: TargetArea.All) });

        /// <summary>收集 = 灯(火 + 丁)→ 可合成闭包 = 灯 火 丁。
        /// 蓝档 6 个未收集:蓝甲/蓝乙/蓝丙/蓝丁 只用 火 / 丁 / 灬(灬 与 火 同系)→ 可凑齐;蓝戊/蓝己 要 禾 → 凑不齐。</summary>
        private static List<CharDef> Defs(int purple = 2, int blue = 6, int green = 2)
        {
            var defs = new List<CharDef>
            {
                Part("火", Element.Fire), Part("灬", Element.Fire), Part("丁"), Part("禾"),
                Char("灯", CardRarity.Blue, "火", "丁"),
            };
            string[] blueNames = { "蓝甲", "蓝乙", "蓝丙", "蓝丁", "蓝戊", "蓝己" };
            for (int i = 0; i < blue; i++)
                defs.Add(i < 4 ? Char(blueNames[i], CardRarity.Blue, i % 2 == 0 ? "火" : "灬", "丁", "丁")
                               : Char(blueNames[i], CardRarity.Blue, "禾", "火"));
            for (int i = 0; i < purple; i++) defs.Add(Char("紫" + i, CardRarity.Purple, "火", "火", "丁"));
            for (int i = 0; i < green; i++) defs.Add(Char("绿" + i, CardRarity.Green, "火", "丁", "丁", "丁"));
            defs.Add(Char("金0", CardRarity.Gold, "火", "火"));
            return defs;
        }

        private static RecipeGraph Graph(int purple = 2, int blue = 6, int green = 2) => new(Defs(purple, blue, green));

        private static readonly string[] Owned = { "灯" };

        private static RecipeWheelResult Spin(RecipeGraph graph, int seed, int depth = 15,
            IReadOnlyCollection<string> owned = null, IReadOnlyCollection<string> recipes = null) =>
            RecipeWheel.Spin(depth, graph, owned ?? Owned, recipes ?? Array.Empty<string>(), new GameRandom(seed));

        // ---- 时机 ----

        [Test]
        public void AppliesAt_OnlyBossDepthsFromFive()
        {
            var config = new EndlessConfig(); // BossEvery = 5
            var hits = Enumerable.Range(1, 45).Where(d => RecipeWheel.AppliesAt(config, d)).ToArray();
            Assert.That(hits, Is.EqualTo(new[] { 5, 10, 15, 20, 25, 30, 35, 40, 45 }), "用户 10-20 改 5 层起;普通层不出;精英与主题 Boss 都出");
        }

        [Test]
        public void BuildSegment_FillsWheelDepthsAndSeed()
        {
            var config = new EndlessConfig
            {
                Bands = new[]
                {
                    new BandDef { Name = "字林", FromDepth = 1,
                        EnemyPool = new[] { new EnemyDef("错字鬼", Element.Wood, 12, 4) },
                        BossPool = new[] { new EnemyDef("排山倒海", Element.Water, 12, 6) } },
                },
            };
            Assert.That(EndlessGenerator.BuildSegment(config, 6, 42).RecipeWheelDepths.ToArray(), Is.EqualTo(new[] { 10 }), "5 层起(用户 10-20):第 10 层 Boss 也出");
            var segment = EndlessGenerator.BuildSegment(config, 11, 42);
            Assert.That(segment.RecipeWheelDepths.ToArray(), Is.EqualTo(new[] { 15 }));
            Assert.That(segment.RecipeWheelSeed, Is.EqualTo(42));
            Assert.That(EndlessGenerator.BuildSegment(config, 18, 42).RecipeWheelDepths.ToArray(), Is.EqualTo(new[] { 20 }),
                "段中续爬重建同一段,判定一致");
        }

        // ---- 分档表 ----

        [TestCase(5, CardRarity.Blue, CardRarity.Purple)]
        [TestCase(11, CardRarity.Blue, CardRarity.Purple)]
        [TestCase(20, CardRarity.Blue, CardRarity.Purple)]
        [TestCase(21, CardRarity.Purple, CardRarity.Gold)]
        [TestCase(30, CardRarity.Purple, CardRarity.Gold)]
        [TestCase(31, CardRarity.Gold, CardRarity.Orange)]
        [TestCase(40, CardRarity.Gold, CardRarity.Orange)]
        [TestCase(41, CardRarity.Orange, CardRarity.Red)]
        [TestCase(120, CardRarity.Orange, CardRarity.Red)]
        public void TiersFor_Table(int depth, CardRarity low, CardRarity high)
        {
            var (l, h) = RecipeWheel.TiersFor(depth);
            Assert.That(l, Is.EqualTo(low));
            Assert.That(h, Is.EqualTo(high));
        }

        // ---- 五格内容 ----

        [Test]
        public void Spin_FourLowPlusOneHigh_AllUncollectedDistinctNonComponent()
        {
            var graph = Graph();
            for (int seed = 0; seed < 50; seed++)
            {
                var wheel = Spin(graph, seed, recipes: new[] { "紫1" });
                Assert.That(wheel.Slots.Count, Is.EqualTo(5));
                Assert.That(wheel.HighIndex, Is.EqualTo(4));
                Assert.That(wheel.Slots.Distinct().Count(), Is.EqualTo(5));
                for (int i = 0; i < 4; i++)
                    Assert.That(graph.Get(wheel.Slots[i]).Rarity, Is.EqualTo(CardRarity.Blue));
                Assert.That(wheel.Slots[4], Is.EqualTo("紫0"), "紫1 已在配方集,不再出");
                Assert.That(wheel.Slots.Contains("灯"), Is.False, "已收集的不出");
                Assert.That(wheel.Slots.All(id => !graph.Get(id).IsComponent), Is.True);
                Assert.That(wheel.Landed, Is.InRange(0, 4));
                Assert.That(wheel.Target, Is.EqualTo(wheel.Slots[wheel.Landed]));
            }
        }

        [Test]
        public void Spin_PrefersCandidatesWhosePartsAreObtainable()
        {
            var graph = Graph();
            for (int seed = 0; seed < 50; seed++)
            {
                var low = Spin(graph, seed).Slots.Take(4).OrderBy(s => s, StringComparer.Ordinal).ToArray();
                Assert.That(low, Is.EqualTo(new[] { "蓝丁", "蓝丙", "蓝乙", "蓝甲" }.OrderBy(s => s, StringComparer.Ordinal).ToArray()),
                    "四个可凑齐的(灬 与 火 同系)排在要 禾 的前面");
            }
        }

        // 用户 10-21:未收集的字不足时,从**同档**(已收集 / 已在配方集的字)随机补足,维持 5 格;不再往低一档补。
        [Test]
        public void Spin_LowTierShort_FillsFromSameTierCollected_KeepsFive()
        {
            var graph = Graph(blue: 4);   // 蓝:灯 + 蓝甲..蓝丁
            var owned = new[] { "灯", "蓝丙", "蓝丁" };
            for (int seed = 0; seed < 30; seed++)
            {
                var wheel = Spin(graph, seed, owned: owned);
                Assert.That(wheel.Slots.Count, Is.EqualTo(5));
                var lows = wheel.Slots.Take(4).ToArray();
                Assert.That(lows.All(id => graph.Get(id).Rarity == CardRarity.Blue), Is.True, "只用同档补,不掉到绿");
                Assert.That(lows.Contains("蓝甲") && lows.Contains("蓝乙"), Is.True, "未收集的优先");
                Assert.That(lows.Distinct().Count(), Is.EqualTo(4));
                Assert.That(wheel.HighIndex, Is.EqualTo(4));
                Assert.That(wheel.CollectedSlots.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
                    Is.EqualTo(lows.Where(id => owned.Contains(id)).OrderBy(x => x, StringComparer.Ordinal).ToArray()),
                    "补进来的已收集字记在 CollectedSlots");
            }
        }

        [Test]
        public void Spin_HighTierAllCollected_FillsFromSameTier()
        {
            var graph = Graph();   // 紫0 / 紫1
            var wheel = Spin(graph, 1, owned: new[] { "灯", "紫0", "紫1" });
            Assert.That(wheel.Slots.Count, Is.EqualTo(5));
            Assert.That(graph.Get(wheel.Slots[wheel.HighIndex]).Rarity, Is.EqualTo(CardRarity.Purple), "高档格仍是紫");
        }

        [Test]
        public void Spin_FillAlsoUsesRecipesAlreadyInRun()
        {
            var graph = Graph(blue: 4);
            var wheel = Spin(graph, 3, owned: new[] { "灯", "蓝丙" }, recipes: new[] { "蓝丁" });
            var lows = wheel.Slots.Take(4).ToList();
            Assert.That(lows.Contains("蓝甲") && lows.Contains("蓝乙"), Is.True, "未收集且不在配方集的优先");
            var fill = lows.Where(id => id != "蓝甲" && id != "蓝乙").ToList();
            Assert.That(fill.Count, Is.EqualTo(2));
            Assert.That(fill.All(id => id == "灯" || id == "蓝丙" || id == "蓝丁"), Is.True, "补位取同档已收集 / 已在配方集的字");
            Assert.That(fill.Distinct().Count(), Is.EqualTo(2));
        }

        [Test]
        public void Spin_TierHasNoCharsAtAll_DoesNotDropToLowerTier()
        {
            var graph = Graph(purple: 0, blue: 0);   // 21–30 层:低档紫(无此档字)、高档金
            var wheel = Spin(graph, 1, depth: 25);
            Assert.That(wheel.Slots, Is.EqualTo(new[] { "金0" }), "该档整档无字时不往低档补");
        }

        [Test]
        public void Spin_NoCandidates_ReturnsNull()
        {
            var graph = Graph(purple: 0, blue: 0, green: 0);
            Assert.That(Spin(graph, 1, depth: 45), Is.Null, "橙 / 红两档整档无字(含已收集)才跳过");
        }

        [Test]
        public void Spin_IsDeterministicPerSeed()
        {
            var graph = Graph();
            var a = Spin(graph, 9);
            var b = Spin(graph, 9);
            Assert.That(b.Slots, Is.EqualTo(a.Slots));
            Assert.That(b.Landed, Is.EqualTo(a.Landed));
        }

        [Test]
        public void Spin_LandingDistribution_HighTenPercent()
        {
            var graph = Graph();
            var counts = new int[5];
            const int n = 10000;
            var random = new GameRandom(12345);
            for (int i = 0; i < n; i++)
                counts[RecipeWheel.Spin(15, graph, Owned, Array.Empty<string>(), random).Landed]++;
            Assert.That(counts[4] / (double)n, Is.EqualTo(0.10).Within(0.01), "高档格 10%");
            for (int i = 0; i < 4; i++)
                Assert.That(counts[i] / (double)n, Is.EqualTo(0.225).Within(0.015), "低档各 22.5%");
        }

        [Test]
        public void StreamFor_IsNotTheFloorStream()
        {
            Assert.That(RecipeWheel.StreamFor(42, 15).State,
                Is.Not.EqualTo(EndlessGenerator.FloorRandom(42, 15).State));
            Assert.That(RecipeWheel.StreamFor(42, 15).State, Is.EqualTo(RecipeWheel.StreamFor(42, 15).State));
        }

        // ---- RunEngine 接线 ----

        private static RunConfig BossFloor(int depth, bool wheel) => new()
        {
            Encounters = new[] { new[] { new EnemyDef("枯", Element.Wood, 4, 2) } },
            RewardPool = new[] { "灯" },
            FromDepth = depth,
            RecipeWheelDepths = wheel ? new[] { depth } : Array.Empty<int>(),
            RecipeWheelSeed = 42,
        };

        private static RunEngine Run(RunConfig config, IReadOnlyCollection<string> unlocked = null) =>
            new(Graph(), config, new BattleConfig { UnlockedChars = unlocked ?? Owned },
                new[] { "灯" }, Array.Empty<string>(), seed: 7);

        private static void Win(RunEngine run)
        {
            Assert.That(run.Battle.Cast("灯", -1), Is.EqualTo(BattleError.None));
            Assert.That(run.Battle.Phase, Is.EqualTo(BattlePhase.Won));
            run.AdvanceAfterBattle();
        }

        [Test]
        public void Run_WheelDepth_SpinsBeforeReward_AndRecordsRecipeAndUnfamiliar()
        {
            var run = Run(BossFloor(15, wheel: true));
            Assert.That(run.LastRecipeWheel, Is.Null);
            Win(run);
            var wheel = run.LastRecipeWheel;
            Assert.That(wheel, Is.Not.Null);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Reward), "转盘在战后结算同一步完成,随后照常选字");
            Assert.That(run.RunRecipes, Is.EqualTo(new[] { wheel.Target }));
            Assert.That(run.UnfamiliarChars, Is.EqualTo(new[] { wheel.Target }));
            var expected = RecipeWheel.Spin(15, Graph(), Owned, Array.Empty<string>(), RecipeWheel.StreamFor(42, 15));
            Assert.That(wheel.Slots, Is.EqualTo(expected.Slots), "候选与落点走 (塔种子, 层) 的独立流");
            Assert.That(wheel.Landed, Is.EqualTo(expected.Landed));
        }

        [Test]
        public void Run_WheelLandsOnCollectedFill_NoRecipeNoUnfamiliar() // 用户 10-21:同档补位可能落在已收集的字上
        {
            var all = Graph().All.Where(d => !d.IsComponent && !d.IsLeaf).Select(d => d.Id).ToArray();
            var run = Run(BossFloor(15, wheel: true), unlocked: all);
            Win(run);
            Assert.That(run.LastRecipeWheel, Is.Not.Null, "全收集也照转(同档补足 5 格)");
            Assert.That(run.LastRecipeWheel.Slots.Count, Is.EqualTo(5));
            Assert.That(run.RunRecipes, Is.Empty, "已收集的字本来就能合,不记配方");
            Assert.That(run.LastRecipeWheel.TargetCollected, Is.True, "结果卡据此改写「已收集」");
            Assert.That(run.UnfamiliarChars, Is.Empty, "已收集的字永不生疏");
        }

        [Test]
        public void Run_WheelGrantsLandedChar_Uncollected_IsUnfamiliar() // 用户 10-21:抽中即得一张,未收集的生疏
        {
            var run = Run(BossFloor(15, wheel: true));
            Win(run);
            var target = run.LastRecipeWheel.Target;
            Assert.That(run.CarriedLibrary, Is.EqualTo(new[] { "灯" }), "点「继续」之前不入库");
            Assert.That(run.WheelGrantPending, Is.True);
            Assert.That(run.ClaimWheelGrant(), Is.True);
            Assert.That(run.CarriedLibrary, Is.EqualTo(new[] { "灯", target }), "落中的字进字库");
            Assert.That(run.WheelGrantPending, Is.False);
            Assert.That(run.ClaimWheelGrant(), Is.False, "只收一次");
            Assert.That(run.IsUnfamiliar(target), Is.True);
        }

        [Test]
        public void Run_WheelGrantsLandedChar_Collected_NotUnfamiliar() // 用户 10-21:已收集的也给,但不生疏
        {
            var all = Graph().All.Where(d => !d.IsComponent && !d.IsLeaf).Select(d => d.Id).ToArray();
            var run = Run(BossFloor(15, wheel: true), unlocked: all);
            int before = run.CarriedLibrary.Count;
            Win(run);
            Assert.That(run.ClaimWheelGrant(), Is.True);
            var target = run.LastRecipeWheel.Target;
            Assert.That(run.CarriedLibrary.Count, Is.EqualTo(before + 1));
            Assert.That(run.CarriedLibrary[before], Is.EqualTo(target));
            Assert.That(run.IsUnfamiliar(target), Is.False);
        }

        private static void WinAny(RunEngine run) // 满库夹具:开局掉字遇满库会先停在 DropChoice
        {
            if (run.Battle.Phase == BattlePhase.DropChoice) run.Battle.SkipDrop();
            Assert.That(run.Battle.Cast("灯", -1), Is.EqualTo(BattleError.None));
            Assert.That(run.Battle.Phase, Is.EqualTo(BattlePhase.Won));
            run.AdvanceAfterBattle();
        }

        // 多放一张:出手的那张「灯」打完这场会离库,战后正好满库
        private static RunEngine FullLibraryRun() =>
            new(Graph(), BossFloor(15, wheel: true), new BattleConfig { UnlockedChars = Owned },
                Enumerable.Repeat("灯", new BattleConfig().LibraryCapacity + 1).ToArray(), Array.Empty<string>(), seed: 7);

        [Test]
        public void Run_WheelGrant_FullLibrary_WaitsForReplace()
        {
            var run = FullLibraryRun();
            WinAny(run);
            var target = run.LastRecipeWheel.Target;
            Assert.That(run.WheelGrantPending, Is.True, $"库满:等玩家换一张或放弃");
            Assert.That(run.ClaimWheelGrant(), Is.False, "库满收不下,仍挂起");
            Assert.That(run.WheelGrantPending, Is.True);
            Assert.That(run.CarriedLibrary.All(c => c == "灯"), Is.True, "库满不硬塞");
            Assert.That(run.ResolveWheelGrant(99), Is.False, "越界");
            Assert.That(run.ResolveWheelGrant(0), Is.True);
            Assert.That(run.CarriedLibrary[0], Is.EqualTo(target));
            Assert.That(run.WheelGrantPending, Is.False);
            Assert.That(run.ResolveWheelGrant(0), Is.False, "只换一次");
        }

        [Test]
        public void Run_WheelGrant_FullLibrary_SkipKeepsLibrary_AndSnapshotKeepsPending()
        {
            var run = FullLibraryRun();
            WinAny(run);
            var restored = RunEngine.Restore(run.Capture(), Graph(), BossFloor(15, wheel: true),
                new BattleConfig { UnlockedChars = Owned }, null);
            Assert.That(restored.WheelGrantPending, Is.True, "挂起再进仍要决议");
            restored.SkipWheelGrant();
            Assert.That(restored.WheelGrantPending, Is.False);
            Assert.That(restored.CarriedLibrary.All(c => c == "灯"), Is.True);
        }

        [Test]
        public void Run_NonWheelDepth_NoSpin()
        {
            var run = Run(BossFloor(10, wheel: false));
            Win(run);
            Assert.That(run.LastRecipeWheel, Is.Null);
            Assert.That(run.RunRecipes, Is.Empty);
        }

        [Test]
        public void Run_Wheel_DoesNotShiftMainRandom()
        {
            var with = Run(BossFloor(15, wheel: true));
            var without = Run(BossFloor(15, wheel: false));
            Win(with);
            Win(without);
            Assert.That(with.LastRecipeWheel, Is.Not.Null);
            Assert.That(with.Capture().RandomState, Is.EqualTo(without.Capture().RandomState));
            Assert.That(with.RewardOptions, Is.EqualTo(without.RewardOptions));
        }

        [Test]
        public void Run_UnlimitedPool_SkipsWheel() // UnlockedChars = null(工装):无「未收集」可言
        {
            var run = new RunEngine(Graph(), BossFloor(15, wheel: true), new BattleConfig(),
                new[] { "灯" }, Array.Empty<string>(), seed: 7);
            Win(run);
            Assert.That(run.LastRecipeWheel, Is.Null);
        }

        [Test]
        public void Run_SnapshotAfterWheel_RestoresResultAndSets() // Review Focus 4:停在选字页挂起
        {
            var run = Run(BossFloor(15, wheel: true));
            Win(run);
            var snapshot = run.Capture();
            var restored = RunEngine.Restore(snapshot, Graph(), BossFloor(15, wheel: true),
                new BattleConfig { UnlockedChars = Owned }, null);
            Assert.That(restored.Phase, Is.EqualTo(RunPhase.Reward));
            Assert.That(restored.LastRecipeWheel.Slots, Is.EqualTo(run.LastRecipeWheel.Slots));
            Assert.That(restored.LastRecipeWheel.Landed, Is.EqualTo(run.LastRecipeWheel.Landed));
            Assert.That(restored.RunRecipes, Is.EqualTo(run.RunRecipes));
            restored.AdvanceAfterBattle(); // 已不在战斗阶段:不会再转一次
            Assert.That(restored.RunRecipes.Count, Is.EqualTo(1));
        }

        [Test]
        public void Save_RoundTrip_KeepsWheelAndRecipeSets() // 真实存档路径(Newtonsoft 走 Data.SaveSerializer)
        {
            var run = Run(BossFloor(15, wheel: true));
            Win(run);
            var meta = new MetaState
            {
                EndlessV2 = new EndlessSaveState
                {
                    RunRecipes = new List<string>(run.RunRecipes),
                    UnfamiliarChars = new List<string>(run.UnfamiliarChars),
                    InProgress = new InProgressRun { FromDepth = 15, Run = run.Capture() },
                },
            };
            var reloaded = Data.SaveSerializer.FromJson(Data.SaveSerializer.ToJson(meta));
            Assert.That(reloaded.EndlessV2.RunRecipes, Is.EqualTo(run.RunRecipes));
            Assert.That(reloaded.EndlessV2.UnfamiliarChars, Is.EqualTo(run.UnfamiliarChars));
            var restored = RunEngine.Restore(reloaded.EndlessV2.InProgress.Run, Graph(), BossFloor(15, wheel: true),
                new BattleConfig { UnlockedChars = Owned }, null);
            Assert.That(restored.LastRecipeWheel.Slots, Is.EqualTo(run.LastRecipeWheel.Slots));
            Assert.That(restored.LastRecipeWheel.HighIndex, Is.EqualTo(run.LastRecipeWheel.HighIndex));
            Assert.That(restored.LastRecipeWheel.Target, Is.EqualTo(run.LastRecipeWheel.Target));
            Assert.That(restored.UnfamiliarChars, Is.EqualTo(run.UnfamiliarChars));
        }

        [Test]
        public void PruneUnknownCards_DropsRemovedCharsFromRecipeSets()
        {
            var meta = new MetaState
            {
                EndlessV2 = new EndlessSaveState
                {
                    RunRecipes = new List<string> { "蓝甲", "幽灵字" },
                    UnfamiliarChars = new List<string> { "幽灵字" },
                },
            };
            MetaRules.PruneUnknownCards(meta, Graph());
            Assert.That(meta.EndlessV2.RunRecipes, Is.EqualTo(new[] { "蓝甲" }));
            Assert.That(meta.EndlessV2.UnfamiliarChars, Is.Empty);
        }

        [Test]
        public void Run_WheelExcludesRecipesCarriedFromEarlierSegments()
        {
            var graph = Graph();
            var first = RecipeWheel.Spin(15, graph, Owned, Array.Empty<string>(), RecipeWheel.StreamFor(42, 15));
            var run = new RunEngine(graph, BossFloor(15, wheel: true), new BattleConfig { UnlockedChars = Owned },
                new[] { "灯" }, Array.Empty<string>(), seed: 7, runRecipes: new[] { first.Target });
            Win(run);
            Assert.That(run.LastRecipeWheel.Slots.Contains(first.Target), Is.False);
            Assert.That(run.RunRecipes.Count, Is.EqualTo(2));
        }
        [Test]
        public void Run_CardLevel_UnfamiliarIsLvZero_OuterLevelKept() // 表现层显示口径:与 BattleEngine.CardLevelOf 同一条
        {
            var graph = Graph();
            var levels = new Dictionary<string, int> { ["灯"] = 4, ["蓝甲"] = 5, ["蓝乙"] = 5, ["蓝丙"] = 5, ["蓝丁"] = 5,
                ["蓝戊"] = 5, ["蓝己"] = 5, ["紫0"] = 5, ["紫1"] = 5 };
            var run = new RunEngine(graph, BossFloor(15, wheel: true), new BattleConfig { UnlockedChars = Owned },
                new[] { "灯" }, Array.Empty<string>(), seed: 7, cardLevels: levels);
            Win(run);
            string target = run.LastRecipeWheel.Target;
            Assert.That(run.IsUnfamiliar(target), Is.True);
            Assert.That(run.CardLevel(target), Is.EqualTo(MetaRules.UnfamiliarCardLevel));
            Assert.That(run.OuterCardLevel(target), Is.EqualTo(5), "「局外 LvN」读局外等级");
            Assert.That(run.IsUnfamiliar("灯"), Is.False);
            Assert.That(run.CardLevel("灯"), Is.EqualTo(4));
            Assert.That(run.OuterCardLevel("灯"), Is.EqualTo(4));
        }

        [Test]
        public void Run_IsUnfamiliar_CollectedCharNeverUnfamiliar() // 与 BattleConfigForRun 的剔除同口径
        {
            var run = new RunEngine(Graph(), BossFloor(15, wheel: false), new BattleConfig { UnlockedChars = Owned },
                new[] { "灯" }, Array.Empty<string>(), seed: 7, unfamiliarChars: new[] { "灯", "蓝甲" });
            Assert.That(run.IsUnfamiliar("灯"), Is.False);
            Assert.That(run.IsUnfamiliar("蓝甲"), Is.True);
            Assert.That(run.Battle.IsUnfamiliar("蓝甲"), Is.True);
            Assert.That(run.CardLevel("灯"), Is.EqualTo(1));
        }
    }
}
