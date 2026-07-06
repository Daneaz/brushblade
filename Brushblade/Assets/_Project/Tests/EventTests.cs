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
            new CharDef("焚", Element.Fire, new[] { "林", "火" }, apCost: 2,
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
            Assert.That(run.Battle.Library, Does.Contain("焚")); // 原有的字仍在
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
            run.ChooseEventOption(1);
            Assert.That(run.Battle.Cast("焚"), Is.EqualTo(BattleError.None)); // 赢最后一战
            run.AdvanceAfterBattle();
            Assert.That(run.Phase, Is.EqualTo(RunPhase.RunWon)); // 直接通关,无奇遇
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
                        { ""label"": ""求财"", ""ink"": 40, ""hpDelta"": -3, ""gainComponents"": [ ""火"" ] }
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

            // BuildRunConfig 透传事件池
            var runConfig = campaign.BuildRunConfig(0, 0);
            Assert.That(runConfig.EventPool.Count, Is.EqualTo(1));
            Assert.That(runConfig.EventChancePercent, Is.EqualTo(40));
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
    }
}
