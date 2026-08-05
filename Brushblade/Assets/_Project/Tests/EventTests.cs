using System;
using System.Linq;
using Brushblade.Core;
using Brushblade.Data;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>奇遇节点(9.6):战斗之间的短情境选择,后果作用于关内携带状态。</summary>
    public class EventTests
    {
        private static RecipeGraph Graph() => new(new[]
        {
            new CharDef("木", Element.Wood),
            new CharDef("火", Element.Fire),
            new CharDef("林", Element.Wood, new[] { "木", "木" }),
            new CharDef("炎", Element.Fire, new[] { "火", "火" },
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 12) }),
            new CharDef("焚", Element.Fire, new[] { "林", "火" }, rarity: CardRarity.Purple,
                effects: new[] { new EffectDef(EffectKind.DamageAll, 18) }),
        });

        private static EventDef Fortune() => new()
        {
            Id = "测字先生",
            Text = "先生请你抽一字卜算。",
            Options = new[]
            {
                new EventOption { Label = "求字", GainChar = "炎" },
                new EventOption { Label = "求财", Ink = 40 },
                new EventOption { Label = "问身", HpDelta = 15 },
                new EventOption { Label = "试炼", HpDelta = -99, GainComponents = new[] { "木", "火" } },
            },
        };

        private static RunConfig Config(int chance) => new()
        {
            Encounters = new[]
            {
                new[] { new EnemyDef("枯", Element.Wood, 4, 2) },
                new[] { new EnemyDef("枯", Element.Wood, 4, 2) },
            },
            RewardPool = new[] { "炎" },
            EventPool = new[] { Fortune() },
            EventChancePercent = chance,
        };

        private static RunEngine Run(int chance = 100, int seed = 7) =>
            new(Graph(), Config(chance), new BattleConfig(),
                new[] { "焚" }, Array.Empty<string>(), seed);

        private static void WinAndSkipReward(RunEngine run)
        {
            Assert.That(run.Battle.Cast("焚"), Is.EqualTo(BattleError.None));
            run.AdvanceAfterBattle();
            run.SkipReward();
        }

        [Test]
        public void Event_TriggersBetweenBattles_At100Percent()
        {
            var run = Run(chance: 100);
            WinAndSkipReward(run);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Event));
            Assert.That(run.CurrentEvent.Id, Is.EqualTo("测字先生"));
            Assert.That(run.BattleIndex, Is.EqualTo(0)); // 尚未进下一战
        }

        [Test]
        public void Event_NeverTriggers_AtZeroPercent()
        {
            var run = Run(chance: 0);
            WinAndSkipReward(run);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.InBattle));
            Assert.That(run.BattleIndex, Is.EqualTo(1));
        }

        [Test]
        public void ChooseOption_GainChar_EntersNextBattleLibrary()
        {
            var run = Run();
            WinAndSkipReward(run);
            run.ChooseEventOption(0); // 求字:得炎
            Assert.That(run.Phase, Is.EqualTo(RunPhase.InBattle));
            Assert.That(run.Battle.Library, Does.Contain("炎"));
            Assert.That(run.Battle.Library, Does.Not.Contain("焚")); // 出字即消耗(v0.7)
        }

        [Test]
        public void ChooseOption_Ink_Accumulates()
        {
            var run = Run();
            WinAndSkipReward(run);
            run.ChooseEventOption(1); // 求财:+40
            Assert.That(run.EarnedInk, Is.EqualTo(40));
        }

        [Test]
        public void ChooseOption_Heal_CapsAtMaxHp()
        {
            var run = Run(); // 未受伤,50 满
            WinAndSkipReward(run);
            run.ChooseEventOption(2); // +15
            Assert.That(run.Battle.PlayerHp, Is.EqualTo(50));
        }

        [Test]
        public void ChooseOption_Damage_LeavesAtLeastOneHp_AndGrantsComponents()
        {
            var run = Run();
            WinAndSkipReward(run);
            run.ChooseEventOption(3); // −99 + 部件
            Assert.That(run.Battle.PlayerHp, Is.EqualTo(1)); // 奇遇不打死人
            Assert.That(run.Battle.Pool, Does.Contain("木").And.Contain("火"));
        }

        [Test]
        public void Event_NotAfterLastBattle()
        {
            var run = Run();
            WinAndSkipReward(run);
            run.ChooseEventOption(0); // 求字:得炎(焚已消耗,末战用炎)
            Assert.That(run.Battle.Cast("炎"), Is.EqualTo(BattleError.None)); // 赢最后一战
            run.AdvanceAfterBattle();
            run.SkipReward();                                    // 段末战利品(2026-07-20)
            Assert.That(run.Phase, Is.EqualTo(RunPhase.RunWon)); // 通关结算,不再触发奇遇
        }

        [Test]
        public void Event_DeterministicBySeed()
        {
            var a = Run(seed: 42);
            var b = Run(seed: 42);
            WinAndSkipReward(a);
            WinAndSkipReward(b);
            Assert.That(a.Phase, Is.EqualTo(b.Phase));
        }

        // ---- 墨锭消费(字摊类,9.3.2) ----

        private static RunConfig ShopConfig() => new()
        {
            Encounters = new[]
            {
                new[] { new EnemyDef("枯", Element.Wood, 4, 2) },
                new[] { new EnemyDef("枯", Element.Wood, 4, 2) },
            },
            RewardPool = new[] { "炎" },
            EventPool = new[]
            {
                new EventDef
                {
                    Id = "字摊",
                    Text = "小摊主人吆喝:好字便宜卖!",
                    Options = new[]
                    {
                        new EventOption { Label = "购「炎」", InkCost = 40, GainChar = "炎" },
                        new EventOption { Label = "离开" },
                    },
                },
            },
            EventChancePercent = 100,
        };

        private static RunEngine ShopRun(int startingInk) =>
            new(Graph(), ShopConfig(), new BattleConfig(),
                new[] { "焚" }, Array.Empty<string>(), seed: 7, startingInk: startingInk);

        [Test]
        public void InkCost_Spends_FromBudget()
        {
            var run = ShopRun(startingInk: 100);
            WinAndSkipReward(run);
            Assert.That(run.AvailableInk, Is.EqualTo(100));
            Assert.That(run.ChooseEventOption(0), Is.True); // 购炎 −40
            Assert.That(run.AvailableInk, Is.EqualTo(60));
            Assert.That(run.EarnedInk, Is.EqualTo(-40));    // run 结束入账为负
            Assert.That(run.Battle.Library, Does.Contain("炎"));
        }

        [Test]
        public void InkCost_Insufficient_RejectedStaysInEvent()
        {
            var run = ShopRun(startingInk: 10);
            WinAndSkipReward(run);
            Assert.That(run.ChooseEventOption(0), Is.False); // 买不起
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Event)); // 留在事件里换别的选
            Assert.That(run.ChooseEventOption(1), Is.True);  // 离开
            Assert.That(run.Phase, Is.EqualTo(RunPhase.InBattle));
        }

        [Test]
        public void InkIncome_RaisesBudget_ForLaterSpending()
        {
            var run = Run(); // 测字先生:求财 +40
            WinAndSkipReward(run);
            run.ChooseEventOption(1);
            Assert.That(run.AvailableInk, Is.EqualTo(40)); // startingInk 缺省 0 + 40
        }

        // ---- 部件抵价(字摊以物易物,2026-07-19:墨锭买一次性品废止) ----

        private static RunConfig BarterConfig() => new()
        {
            Encounters = new[]
            {
                new[] { new EnemyDef("枯", Element.Wood, 4, 2) },
                new[] { new EnemyDef("枯", Element.Wood, 4, 2) },
            },
            RewardPool = new[] { "炎" },
            EventPool = new[]
            {
                new EventDef
                {
                    Id = "字摊",
                    Text = "摊主捻须:以物易物,童叟无欺。",
                    Options = new[]
                    {
                        new EventOption { Label = "两部件换「炎」", ComponentCost = 2, GainChar = "炎" },
                        new EventOption { Label = "只看不买" },
                    },
                },
            },
            EventChancePercent = 100,
        };

        private static RunEngine BarterRun(string[] pool) =>
            new(Graph(), BarterConfig(), new BattleConfig { DropTable = Array.Empty<string>() },
                new[] { "焚" }, pool, seed: 7);

        [Test]
        public void ComponentCost_PlayerPicksComponents_ToDiscard()
        {
            var run = BarterRun(new[] { "木", "火", "木" });
            WinAndSkipReward(run);
            // 玩家自选不要的部件(下标 1、2 = 火与第二个木),而非自动扣最早的
            Assert.That(run.ChooseEventOption(0, new[] { 1, 2 }), Is.True);
            Assert.That(run.Battle.Library, Does.Contain("炎"));
            Assert.That(run.Battle.Pool, Is.EquivalentTo(new[] { "木" })); // 首位的木保留
        }

        [Test]
        public void ComponentCost_WrongPickCountOrDuplicates_Rejected()
        {
            var run = BarterRun(new[] { "木", "木", "火" });
            WinAndSkipReward(run);
            Assert.That(run.ChooseEventOption(0), Is.False);                 // 未选部件
            Assert.That(run.ChooseEventOption(0, new[] { 0 }), Is.False);    // 数量不够
            Assert.That(run.ChooseEventOption(0, new[] { 1, 1 }), Is.False); // 重复下标
            Assert.That(run.ChooseEventOption(0, new[] { 0, 9 }), Is.False); // 越界
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Event));
            Assert.That(run.CarriedPool.Count, Is.EqualTo(3)); // 失败不动池
        }

        [Test]
        public void ComponentCost_Insufficient_RejectedStaysInEvent()
        {
            var run = BarterRun(new[] { "木" });
            WinAndSkipReward(run);
            Assert.That(run.ChooseEventOption(0, new[] { 0 }), Is.False); // 池总量不够,换不起
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Event));
            Assert.That(run.ChooseEventOption(1), Is.True);
        }

        // ---- 新后果(2026-07-19 拍板):随机部件 / 任选字 / 概率赌注 ----

        private static RunConfig OneOptionConfig(params EventOption[] options) => new()
        {
            Encounters = new[]
            {
                new[] { new EnemyDef("枯", Element.Wood, 4, 2) },
                new[] { new EnemyDef("枯", Element.Wood, 4, 2) },
            },
            RewardPool = new[] { "炎" },
            EventPool = new[] { new EventDef { Id = "e", Text = "t", Options = options } },
            EventChancePercent = 100,
        };

        [Test]
        public void RandomComponents_DrawsFromDeckComponents_IntoPool()
        {
            var run = new RunEngine(Graph(),
                OneOptionConfig(new EventOption { Label = "求墨", RandomComponents = 2 }),
                new BattleConfig { DropTable = Array.Empty<string>() },
                new[] { "焚" }, Array.Empty<string>(), seed: 7);
            WinAndSkipReward(run);
            Assert.That(run.ChooseEventOption(0), Is.True);
            Assert.That(run.Battle.Pool.Count, Is.EqualTo(2));
            // 候选 = 出阵表(RewardPool=[炎])所需部件 = [火]
            var allowed = MetaRules.DeckComponents(new[] { "炎" }, Graph()).ToList();
            Assert.That(run.Battle.Pool, Has.All.Matches<string>(c => allowed.Contains(c)));
        }

        [Test]
        public void RandomComponents_PoolFull_EntersOverflow()
        {
            var run = new RunEngine(Graph(),
                OneOptionConfig(new EventOption { Label = "求墨", RandomComponents = 3 }),
                new BattleConfig { PoolCapacity = 2, DropTable = Array.Empty<string>() },
                new[] { "焚" }, new[] { "木" }, seed: 7);
            WinAndSkipReward(run);
            Assert.That(run.ChooseEventOption(0), Is.True);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.EventOverflow)); // 满池不再静默丢
            Assert.That(run.CarriedPool.Count, Is.EqualTo(2));          // 空位先填满
            Assert.That(run.PendingOverflow.Count, Is.EqualTo(2));      // 余下待玩家决议
            var allowed = MetaRules.DeckComponents(new[] { "炎" }, Graph()).ToList();
            Assert.That(run.PendingOverflow, Has.All.Matches<string>(c => allowed.Contains(c)));
        }

        [Test]
        public void GainCharChoices_PlayerPicksOne()
        {
            var run = new RunEngine(Graph(),
                OneOptionConfig(new EventOption
                {
                    Label = "换字",
                    ComponentCost = 2,
                    GainCharChoices = new[] { "林", "炎" },
                }),
                new BattleConfig { DropTable = Array.Empty<string>() },
                new[] { "焚" }, new[] { "木", "火" }, seed: 7);
            WinAndSkipReward(run);
            Assert.That(run.ChooseEventOption(0, new[] { 0, 1 }), Is.False);                     // 未指定选哪个字
            Assert.That(run.ChooseEventOption(0, new[] { 0, 1 }, charChoiceIndex: 9), Is.False); // 越界
            Assert.That(run.CarriedPool.Count, Is.EqualTo(2)); // 失败不动池
            Assert.That(run.ChooseEventOption(0, new[] { 0, 1 }, charChoiceIndex: 1), Is.True);
            Assert.That(run.Battle.Library, Does.Contain("炎"));
            Assert.That(run.Battle.Library, Does.Not.Contain("林"));
            Assert.That(run.Battle.Pool, Is.Empty); // 两部件已抵价
        }

        [Test]
        public void GainChar_LibraryFull_RejectedWithoutConsumingComponents()
        {
            // 容量 1,焚出手后剩炎占满;换字应整体拒绝,部件不受损(修正:先验后扣)
            var run = new RunEngine(Graph(),
                OneOptionConfig(
                    new EventOption { Label = "换林", ComponentCost = 2, GainChar = "林" },
                    new EventOption { Label = "离开" }),
                new BattleConfig { LibraryCapacity = 1, DropTable = Array.Empty<string>() },
                new[] { "焚", "炎" }, new[] { "木", "火" }, seed: 7);
            WinAndSkipReward(run);
            Assert.That(run.ChooseEventOption(0, new[] { 0, 1 }), Is.False);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Event));
            Assert.That(run.CarriedPool.Count, Is.EqualTo(2)); // 部件完好
        }

        /// <summary>满库时指定替换目标即可成交(2026-07-22,复用战利品的满库替换)。</summary>
        private static RunEngine FullLibraryBarterRun() =>
            new(Graph(),
                OneOptionConfig(
                    new EventOption { Label = "换林", ComponentCost = 2, GainChar = "林" },
                    new EventOption { Label = "离开" }),
                new BattleConfig { LibraryCapacity = 1, DropTable = Array.Empty<string>() },
                new[] { "焚", "炎" }, new[] { "木", "火" }, seed: 7);

        [Test]
        public void GainChar_LibraryFull_ReplaceIndexGiven_Succeeds()
        {
            var run = FullLibraryBarterRun();
            WinAndSkipReward(run); // 焚出手后字库只剩炎(容量 1,已满)
            Assert.That(run.CarriedLibrary, Is.EqualTo(new[] { "炎" }));

            Assert.That(run.ChooseEventOption(0, new[] { 0, 1 }, replaceLibraryIndex: 0), Is.True);
            Assert.That(run.Battle.Library, Is.EqualTo(new[] { "林" })); // 炎被换掉
            Assert.That(run.Battle.Pool, Is.Empty);                     // 两部件已抵价
        }

        [Test]
        public void GainChar_LibraryFull_ReplaceIndexOutOfRange_RejectedIntact()
        {
            var run = FullLibraryBarterRun();
            WinAndSkipReward(run);
            Assert.That(run.ChooseEventOption(0, new[] { 0, 1 }, replaceLibraryIndex: 5), Is.False);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Event));
            Assert.That(run.CarriedPool.Count, Is.EqualTo(2)); // 先验后扣:部件完好
            Assert.That(run.CarriedLibrary, Is.EqualTo(new[] { "炎" }));
        }

        [Test]
        public void GainChar_LibraryNotFull_ReplaceIndexIgnored() // 不满时照常入库,不顶替
        {
            var run = new RunEngine(Graph(),
                OneOptionConfig(
                    new EventOption { Label = "换林", ComponentCost = 2, GainChar = "林" },
                    new EventOption { Label = "离开" }),
                new BattleConfig { LibraryCapacity = 5, DropTable = Array.Empty<string>() },
                new[] { "焚", "炎" }, new[] { "木", "火" }, seed: 7);
            WinAndSkipReward(run);
            Assert.That(run.ChooseEventOption(0, new[] { 0, 1 }, replaceLibraryIndex: 0), Is.True);
            Assert.That(run.Battle.Library, Does.Contain("炎").And.Contain("林")); // 炎还在
        }

        [Test]
        public void InkGamble_Chance100_AlwaysPays()
        {
            var run = new RunEngine(Graph(),
                OneOptionConfig(new EventOption
                { Label = "对赌", InkCost = 30, Ink = 100, InkChancePercent = 100 }),
                new BattleConfig { DropTable = Array.Empty<string>() },
                new[] { "焚" }, Array.Empty<string>(), seed: 7, startingInk: 30);
            WinAndSkipReward(run);
            Assert.That(run.ChooseEventOption(0), Is.True);
            Assert.That(run.EarnedInk, Is.EqualTo(70)); // −30 + 100
        }

        [Test]
        public void InkGamble_BothOutcomesOccur_AcrossSeeds()
        {
            bool won = false, lost = false;
            for (int seed = 0; seed < 40 && !(won && lost); seed++)
            {
                var run = new RunEngine(Graph(),
                    OneOptionConfig(new EventOption
                    { Label = "对赌", InkCost = 30, Ink = 100, InkChancePercent = 50 }),
                    new BattleConfig { DropTable = Array.Empty<string>() },
                    new[] { "焚" }, Array.Empty<string>(), seed, startingInk: 30);
                WinAndSkipReward(run);
                Assert.That(run.ChooseEventOption(0), Is.True);
                if (run.EarnedInk == 70) won = true;
                else if (run.EarnedInk == -30) lost = true;
                else Assert.Fail($"意外的 EarnedInk:{run.EarnedInk}");
            }
            Assert.That(won && lost, Is.True, "40 个种子应同时出现赢与输");
        }

        // ---- 部件超上限:可替换 / 跳过(2026-07-24) ----

        private static RunEngine OverflowRun(int startingPoolCount, int gain, int cap = 12) =>
            new(Graph(),
                OverflowConfig(gain),
                new BattleConfig { PoolCapacity = cap, DropTable = Array.Empty<string>() },
                new[] { "焚" }, Enumerable.Repeat("木", startingPoolCount).ToArray(), seed: 7);

        private static RunConfig OverflowConfig(int gainCount) => new()
        {
            Encounters = new[]
            {
                new[] { new EnemyDef("枯", Element.Wood, 4, 2) },
                new[] { new EnemyDef("枯", Element.Wood, 4, 2) },
            },
            RewardPool = new[] { "炎" },
            EventPool = new[]
            {
                new EventDef
                {
                    Id = "废稿堆",
                    Text = "翻找旧稿。",
                    Options = new[]
                    {
                        new EventOption { Label = "翻找", GainComponents = Enumerable.Repeat("火", gainCount).ToArray() },
                    },
                },
            },
            EventChancePercent = 100,
        };

        [Test]
        public void Overflow_FillsEmptyThenPendsRemainder()
        {
            var run = OverflowRun(startingPoolCount: 10, gain: 3); // 10 + 前 2 装满 12,第 3 溢出
            WinAndSkipReward(run);
            Assert.That(run.ChooseEventOption(0), Is.True);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.EventOverflow));
            Assert.That(run.CarriedPool.Count, Is.EqualTo(12));
            Assert.That(run.CarriedPool.Count(c => c == "火"), Is.EqualTo(2));
            Assert.That(run.PendingOverflow, Is.EqualTo(new[] { "火" }));
        }

        [Test]
        public void Overflow_Replace_SwapsChosenPoolItem_ThenAdvances()
        {
            var run = OverflowRun(startingPoolCount: 10, gain: 3);
            WinAndSkipReward(run);
            run.ChooseEventOption(0);
            Assert.That(run.ResolveOverflowReplace(0), Is.True); // 换掉首位的木
            Assert.That(run.Phase, Is.EqualTo(RunPhase.InBattle));
            Assert.That(run.Battle.Pool.Count, Is.EqualTo(12));
            Assert.That(run.Battle.Pool.Count(c => c == "火"), Is.EqualTo(3)); // 溢出项换进来
        }

        [Test]
        public void Overflow_Skip_DropsIt_ThenAdvances()
        {
            var run = OverflowRun(startingPoolCount: 10, gain: 3);
            WinAndSkipReward(run);
            run.ChooseEventOption(0);
            run.ResolveOverflowSkip();
            Assert.That(run.Phase, Is.EqualTo(RunPhase.InBattle));
            Assert.That(run.Battle.Pool.Count, Is.EqualTo(12));
            Assert.That(run.Battle.Pool.Count(c => c == "火"), Is.EqualTo(2)); // 第 3 个被丢
        }

        [Test]
        public void Overflow_MultipleItems_ResolvedOneByOne()
        {
            var run = OverflowRun(startingPoolCount: 11, gain: 3); // 1 装下,2 溢出
            WinAndSkipReward(run);
            run.ChooseEventOption(0);
            Assert.That(run.PendingOverflow.Count, Is.EqualTo(2));
            Assert.That(run.ResolveOverflowReplace(0), Is.True);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.EventOverflow)); // 还剩一个待决
            Assert.That(run.PendingOverflow.Count, Is.EqualTo(1));
            run.ResolveOverflowSkip();
            Assert.That(run.Phase, Is.EqualTo(RunPhase.InBattle));
            Assert.That(run.Battle.Pool.Count, Is.EqualTo(12));
        }

        [Test]
        public void Overflow_Replace_OutOfRange_Rejected_StaysPending()
        {
            var run = OverflowRun(startingPoolCount: 10, gain: 3);
            WinAndSkipReward(run);
            run.ChooseEventOption(0);
            Assert.That(run.ResolveOverflowReplace(-1), Is.False);
            Assert.That(run.ResolveOverflowReplace(99), Is.False);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.EventOverflow)); // 仍在决议
            // 决议完成后再调则无效(已离开溢出阶段)
            run.ResolveOverflowSkip();
            Assert.That(run.ResolveOverflowReplace(0), Is.False);
        }

        [Test]
        public void Overflow_NoOverflow_TakesNormalPath() // 不满则直接入池、进下一战(回归)
        {
            var run = OverflowRun(startingPoolCount: 5, gain: 3);
            WinAndSkipReward(run);
            Assert.That(run.ChooseEventOption(0), Is.True);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.InBattle));
            Assert.That(run.Battle.Pool.Count, Is.EqualTo(8));
            Assert.That(run.PendingOverflow, Is.Empty);
        }

        // ---- 配置解析 ----

        [Test]
        public void LoadCampaign_ParsesEvents()
        {
            var graph = ConfigLoader.LoadGraph(
                @"{ ""chars"": [ { ""id"": ""灯"" }, { ""id"": ""火"" } ] }");
            var campaign = ConfigLoader.LoadCampaign(@"{
                ""enemies"": [ { ""id"": ""错字鬼"", ""element"": ""Wood"", ""maxHp"": 12, ""attack"": 4 } ],
                ""dropTable"": [],
                ""eventChance"": 40,
                ""events"": [
                    { ""id"": ""测字先生"", ""text"": ""先生请你抽一字。"",
                      ""options"": [
                        { ""label"": ""求字"", ""gainChar"": ""灯"" },
                        { ""label"": ""求财"", ""ink"": 40, ""hpDelta"": -3, ""gainComponents"": [ ""火"" ], ""inkCost"": 15, ""componentCost"": 2 }
                      ] }
                ],
                ""chapters"": [ { ""name"": ""蒙学"",
                    ""stages"": [ { ""encounters"": [ [ ""错字鬼"" ] ] } ], ""rewardPool"": [] } ]
            }", graph);
            Assert.That(campaign.EventChancePercent, Is.EqualTo(40));
            var evt = campaign.Events.Single();
            Assert.That(evt.Options.Count, Is.EqualTo(2));
            Assert.That(evt.Options[0].GainChar, Is.EqualTo("灯"));
            Assert.That(evt.Options[1].Ink, Is.EqualTo(40));
            Assert.That(evt.Options[1].InkCost, Is.EqualTo(15));
            Assert.That(evt.Options[1].ComponentCost, Is.EqualTo(2));

            // BuildRunConfig 透传事件池
            var runConfig = campaign.BuildRunConfig(0, 0);
            Assert.That(runConfig.EventPool.Count, Is.EqualTo(1));
            Assert.That(runConfig.EventChancePercent, Is.EqualTo(40));
        }

        [Test]
        public void LoadCampaign_ParsesNewConsequences()
        {
            var graph = ConfigLoader.LoadGraph(
                @"{ ""chars"": [ { ""id"": ""灯"" }, { ""id"": ""火"" } ] }");
            var campaign = ConfigLoader.LoadCampaign(@"{
                ""enemies"": [], ""dropTable"": [],
                ""events"": [
                    { ""id"": ""x"", ""text"": ""t"",
                      ""options"": [
                        { ""label"": ""求墨"", ""randomComponents"": 2 },
                        { ""label"": ""换字"", ""componentCost"": 2, ""gainCharChoices"": [ ""灯"", ""火"" ] },
                        { ""label"": ""对赌"", ""inkCost"": 30, ""ink"": 100, ""inkChancePercent"": 50 }
                      ] }
                ],
                ""chapters"": [ { ""name"": ""y"",
                    ""stages"": [ { ""encounters"": [] } ], ""rewardPool"": [] } ]
            }", graph);
            var options = campaign.Events.Single().Options;
            Assert.That(options[0].RandomComponents, Is.EqualTo(2));
            Assert.That(options[1].GainCharChoices, Is.EqualTo(new[] { "灯", "火" }));
            Assert.That(options[2].InkChancePercent, Is.EqualTo(50));
        }

        [Test]
        public void LoadCampaign_EventGainCharChoiceNotInGraph_Throws()
        {
            var graph = ConfigLoader.LoadGraph(@"{ ""chars"": [ { ""id"": ""灯"" } ] }");
            Assert.Throws<ConfigException>(() => ConfigLoader.LoadCampaign(@"{
                ""enemies"": [], ""dropTable"": [],
                ""events"": [ { ""id"": ""x"", ""text"": ""t"",
                    ""options"": [ { ""label"": ""a"", ""gainCharChoices"": [ ""灯"", ""龘"" ] } ] } ],
                ""chapters"": [ { ""name"": ""y"",
                    ""stages"": [ { ""encounters"": [] } ], ""rewardPool"": [] } ]
            }", graph));
        }

        [Test]
        public void LoadCampaign_EventGainCharNotInGraph_Throws()
        {
            var graph = ConfigLoader.LoadGraph(@"{ ""chars"": [ { ""id"": ""灯"" } ] }");
            Assert.Throws<ConfigException>(() => ConfigLoader.LoadCampaign(@"{
                ""enemies"": [], ""dropTable"": [],
                ""events"": [ { ""id"": ""x"", ""text"": ""t"",
                    ""options"": [ { ""label"": ""a"", ""gainChar"": ""龘"" } ] } ],
                ""chapters"": [ { ""name"": ""y"",
                    ""stages"": [ { ""encounters"": [] } ], ""rewardPool"": [] } ]
            }", graph));
        }

        // ---- 字摊口径:换来的字也须在出阵列表(2026-07-20;与战利品/合成同源) ----

        private static RunEngine StallRun(params string[] unlocked) =>
            new(Graph(), StallConfig(), new BattleConfig
            {
                DropTable = Array.Empty<string>(),
                UnlockedChars = unlocked.Length > 0 ? unlocked : null,
            }, new[] { "焚" }, new[] { "木", "火" }, seed: 7);

        private static RunConfig StallConfig() => new()
        {
            Encounters = new[]
            {
                new[] { new EnemyDef("枯", Element.Wood, 4, 2) },
                new[] { new EnemyDef("枯", Element.Wood, 4, 2) },
            },
            RewardPool = new[] { "炎" },
            EventPool = new[]
            {
                new EventDef
                {
                    Id = "字摊",
                    Text = "以物易物。",
                    Options = new[]
                    {
                        new EventOption
                        {
                            Label = "两部件换字(任选)", ComponentCost = 2,
                            GainCharChoices = new[] { "炎", "林" },
                        },
                        new EventOption { Label = "只看不换" },
                    },
                },
            },
            EventChancePercent = 100,
        };

        [Test]
        public void Stall_RejectsCharOutsideDeck_AndKeepsComponents()
        {
            var run = StallRun("林"); // 出阵只有林,炎换不到
            WinAndSkipReward(run);
            Assert.That(run.ChooseEventOption(0, new[] { 0, 1 }, charChoiceIndex: 0), Is.False);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Event));       // 停留在事件
            Assert.That(run.CarriedPool, Is.EquivalentTo(new[] { "木", "火" })); // 先验后扣:部件不损
        }

        [Test]
        public void Stall_AllowsCharInsideDeck()
        {
            var run = StallRun("炎", "林");
            WinAndSkipReward(run);
            Assert.That(run.ChooseEventOption(0, new[] { 0, 1 }, charChoiceIndex: 0), Is.True);
            Assert.That(run.Battle.Library, Does.Contain("炎"));
        }

        [Test]
        public void FixedGift_AlsoDeckGated() // 守卫是全局的:单字奇遇(测字先生等)同受出阵列表约束
        {
            var run = new RunEngine(Graph(), Config(100),
                new BattleConfig { UnlockedChars = new[] { "林" } },
                new[] { "焚" }, Array.Empty<string>(), seed: 7);
            WinAndSkipReward(run);
            Assert.That(run.ChooseEventOption(0), Is.False); // 「求字」得炎,炎不在出阵列表
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Event));
        }

        [Test]
        public void Stall_UnlockedCharsNull_KeepsUnrestricted() // 工装与旧调用不受影响
        {
            var run = StallRun();
            WinAndSkipReward(run);
            Assert.That(run.ChooseEventOption(0, new[] { 0, 1 }, charChoiceIndex: 0), Is.True);
        }

        // ---- 局内血量上限(2026-08-04):怪物 scale 无上限,靠奇遇把上限顶上去 ----

        /// <summary>上限奇遇:option 0 直接 +30%,option 1 是 80% 掷正/20% 反噬,option 2 拿 1 部件换。</summary>
        private static RunEngine MaxHpRun(int seed = 7, params string[] pool) => new(
            Graph(), MaxHpConfig(), new BattleConfig { PlayerMaxHp = 100 },
            new[] { "焚", "焚" }, pool, seed); // 出字即消耗,连打两场要两张

        private static RunConfig MaxHpConfig() => new RunConfig
            {
                Encounters = new[]
                {
                    new[] { new EnemyDef("枯", Element.Wood, 4, 2) },
                    new[] { new EnemyDef("枯", Element.Wood, 4, 2) },
                    new[] { new EnemyDef("枯", Element.Wood, 4, 2) }, // 三场:末场不走奇遇,复利要连吃两次
                },
                RewardPool = new[] { "炎" },
                EventChancePercent = 100,
                EventPool = new[]
                {
                    new EventDef
                    {
                        Id = "养气",
                        Text = "老道说,气可养。",
                        Options = new[]
                        {
                            new EventOption { Label = "静养", MaxHpPercent = 30 },
                            new EventOption { Label = "猛练", MaxHpPercent = 30, MaxHpChancePercent = 80 },
                            new EventOption { Label = "以物易气", MaxHpPercent = 30, ComponentCost = 1 },
                        },
                    },
                },
            };

        [Test]
        public void MaxHpPercent_RaisesCapAndHealsSameAmount()
        {
            var run = MaxHpRun();
            run.Battle.EndTurn();                        // 先挨一下,腾出回血空间
            int hpBefore = run.Battle.PlayerHp;
            WinAndSkipReward(run);
            Assert.That(run.ChooseEventOption(0), Is.True);

            Assert.That(run.Battle.MaxHp, Is.EqualTo(130));            // 100 → 130
            Assert.That(run.Battle.PlayerHp, Is.EqualTo(hpBefore + 30)); // 同步等量回血
        }

        [Test]
        public void MaxHpPercent_Compounds() // 复利:第二次踩在已提升的上限上
        {
            var run = MaxHpRun();
            WinAndSkipReward(run);
            run.ChooseEventOption(0);        // 100 → 130
            run.Battle.Cast("焚");
            run.AdvanceAfterBattle();
            run.SkipReward();
            Assert.That(run.ChooseEventOption(0), Is.True);

            Assert.That(run.Battle.MaxHp, Is.EqualTo(169)); // 130 + floor(130×0.3)=39
        }

        [Test]
        public void MaxHpChance_Backfire_LowersCapAndClampsHp() // 20% 那一档:反向扣同样百分比
        {
            // 掷不中的种子:MaxHpChancePercent=80,需要 _random.Next(100) >= 80
            var run = MaxHpRun(seed: BackfireSeed());
            WinAndSkipReward(run);
            Assert.That(run.ChooseEventOption(1), Is.True);

            Assert.That(run.Battle.MaxHp, Is.EqualTo(70));          // 100 − 30
            Assert.That(run.Battle.PlayerHp, Is.LessThanOrEqualTo(70)); // 当前血钳到新上限
        }

        [Test]
        public void MaxHpPercent_ComponentCost_RequiresPick() // 以物易气:不给部件不成交
        {
            var run = MaxHpRun(seed: 7, pool: new[] { "木" });
            WinAndSkipReward(run);
            Assert.That(run.ChooseEventOption(2), Is.False);                  // 没指定弃哪个
            Assert.That(run.ChooseEventOption(2, new[] { 0 }), Is.True);
            Assert.That(run.Battle.MaxHp, Is.EqualTo(130));
            Assert.That(run.Battle.Pool, Does.Not.Contain("木"));
        }

        [Test]
        public void MaxHpBonus_SurvivesSnapshotRoundTrip() // 局内加成丢了 = 续爬后上限悄悄回退
        {
            var run = MaxHpRun();
            WinAndSkipReward(run);
            run.ChooseEventOption(0);

            var restored = RunEngine.Restore(run.Capture(), Graph(), MaxHpConfig(),
                new BattleConfig { PlayerMaxHp = 100 }, null, 0, 0);

            Assert.That(restored.Battle.MaxHp, Is.EqualTo(130));
        }

        /// <summary>找一个让 80% 判定落空的种子 —— 断言反噬分支不能靠运气。</summary>
        private static int BackfireSeed()
        {
            for (int seed = 1; seed < 500; seed++)
            {
                var probe = MaxHpRun(seed);
                WinAndSkipReward(probe);
                probe.ChooseEventOption(1);
                if (probe.Battle.MaxHp < 100) return seed;
            }
            throw new InvalidOperationException("500 个种子里没找到掷空的,概率模型有问题");
        }
    }
}
