using System;
using System.Linq;
using Brushblade.Core;
using Brushblade.Data;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>奇遇按等级加权抽取 + 每段保底(2026-10-21)。</summary>
    public class EventTierTests
    {
        private static RecipeGraph Graph() => new(new[]
        {
            new CharDef("木", Element.Wood),
            new CharDef("火", Element.Fire),
            new CharDef("林", Element.Wood, new[] { "木", "木" }),
            new CharDef("焚", Element.Fire, new[] { "林", "火" }, rarity: CardRarity.Purple,
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 18, shape: TargetArea.All) }),
        });

        private static EventDef Ev(string id, int tier, int minDepth = 0) => new()
        {
            Id = id, Text = "…", Tier = tier, MinDepth = minDepth,
            Options = new[] { new EventOption { Label = "离开" } },
        };

        private static RunEngine Make(int floors, int chance, bool pity, int seed, params EventDef[] pool)
        {
            var encounters = Enumerable.Range(0, floors)
                .Select(_ => (System.Collections.Generic.IReadOnlyList<EnemyDef>)
                    new[] { new EnemyDef("枯", Element.Wood, 4, 2) }).ToArray();
            var config = new RunConfig
            {
                Encounters = encounters,
                RewardPool = new[] { "焚" },
                EventPool = pool,
                EventChancePercent = chance,
                EventPity = pity,
            };
            return new RunEngine(Graph(), config, new BattleConfig(),
                Enumerable.Repeat("焚", 6).ToArray(), Array.Empty<string>(), seed);
        }

        private static void Win(RunEngine run)
        {
            Assert.That(run.Battle.Cast("焚"), Is.EqualTo(BattleError.None));
            run.AdvanceAfterBattle();
        }

        [Test]
        public void WeightOf_Table()
        {
            Assert.That(new[] { 1, 2, 3, 4, 0 }.Select(EventRules.WeightOf), Is.EqualTo(new[] { 4, 3, 2, 1, 1 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => EventRules.WeightOf(5));
            Assert.Throws<ArgumentOutOfRangeException>(() => EventRules.WeightOf(-1));
        }

        [Test]
        public void Tier0_PickMatchesUniform()
        {
            var pool = new[] { Ev("甲", 0), Ev("乙", 0), Ev("丙", 0) };
            for (int seed = 0; seed < 50; seed++)
            {
                var rng = new GameRandom(seed);
                rng.Next(int.MaxValue); // 开第一场战斗取的战斗种子
                rng.Next(100);
                int expected = rng.Next(3);
                var run = Make(2, 100, false, seed, pool);
                Win(run);
                Assert.That(run.Phase, Is.EqualTo(RunPhase.Event), $"seed {seed}");
                Assert.That(run.CurrentEvent.Id, Is.EqualTo(pool[expected].Id), $"seed {seed}");
            }
        }

        [Test]
        public void Weighted_FrequencyMatches()
        {
            int tier1 = 0;
            const int n = 2000;
            for (int seed = 0; seed < n; seed++)
            {
                var run = Make(2, 100, false, seed, Ev("一", 1), Ev("四", 4));
                Win(run);
                if (run.CurrentEvent.Id == "一") tier1++;
            }
            Assert.That(tier1 / (double)n, Is.EqualTo(0.8).Within(0.03));
        }

        [Test]
        public void Pity_ForcedOnLastRegularFloor()
        {
            var run = Make(5, 0, true, 3, Ev("甲", 1));
            for (int i = 0; i < 3; i++)
            {
                Win(run);
                Assert.That(run.Phase, Is.EqualTo(RunPhase.Reward), $"第 {i + 1} 层");
                Assert.That(run.EventSeenThisSegment, Is.False);
                run.SkipReward();
            }
            Win(run);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Event), "第 4 层(最后一个非 Boss 层)保底");
            Assert.That(run.EventSeenThisSegment, Is.True);
        }

        [Test]
        public void Pity_NotForcedAfterEventSeen()
        {
            var run = Make(5, 100, true, 5, Ev("甲", 1));
            Win(run);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Event));
            Assert.That(run.ChooseEventOption(0), Is.True);
            run.SkipReward();
            var snapshot = run.Capture();

            var config = new RunConfig
            {
                Encounters = Enumerable.Range(0, 5)
                    .Select(_ => (System.Collections.Generic.IReadOnlyList<EnemyDef>)
                        new[] { new EnemyDef("枯", Element.Wood, 4, 2) }).ToArray(),
                RewardPool = new[] { "焚" },
                EventPool = new[] { Ev("甲", 1) },
                EventChancePercent = 0,
                EventPity = true,
            };
            var restored = RunEngine.Restore(snapshot, Graph(), config, new BattleConfig(), null);
            for (int floor = 2; floor <= 4; floor++)
            {
                Win(restored);
                Assert.That(restored.Phase, Is.EqualTo(RunPhase.Reward), $"第 {floor} 层已出过奇遇,不再保底");
                restored.SkipReward();
            }
        }

        [Test]
        public void Pity_SurvivesSnapshotRoundTrip()
        {
            var run = Make(5, 100, true, 5, Ev("甲", 1));
            Win(run);
            var snapshot = run.Capture();
            Assert.That(snapshot.EventSeenThisSegment, Is.True);
            var config = new RunConfig
            {
                Encounters = Enumerable.Range(0, 5)
                    .Select(_ => (System.Collections.Generic.IReadOnlyList<EnemyDef>)
                        new[] { new EnemyDef("枯", Element.Wood, 4, 2) }).ToArray(),
                RewardPool = new[] { "焚" },
                EventPool = new[] { Ev("甲", 1) },
                EventChancePercent = 100,
                EventPity = true,
            };
            var restored = RunEngine.Restore(snapshot, Graph(), config, new BattleConfig(), null);
            Assert.That(restored.EventSeenThisSegment, Is.True);
        }

        [Test]
        public void Pity_NoEligibleEvent_GoesToReward()
        {
            var run = Make(5, 0, true, 3, Ev("深", 1, minDepth: 99));
            for (int i = 0; i < 3; i++) { Win(run); run.SkipReward(); }
            Win(run);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Reward));
        }

        [Test]
        public void Pity_OffByDefault_ZeroChanceNeverTriggers()
        {
            var run = Make(2, 0, false, 3, Ev("甲", 1));
            Win(run);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Reward));
        }

        private const string Skeleton = @"{{
            ""enemies"": [], ""dropTable"": [],
            ""events"": [ {{ ""id"": ""x"", ""text"": ""t"" {0},
                ""options"": [ {{ ""label"": ""a"" }} ] }} ],
            ""chapters"": [ {{ ""name"": ""y"", ""stages"": [ {{ ""encounters"": [] }} ], ""rewardPool"": [] }} ]
        }}";

        [Test]
        public void Loader_RejectsMissingTier()
        {
            var graph = ConfigLoader.LoadGraph(@"{ ""chars"": [ { ""id"": ""灯"" } ] }");
            Assert.Throws<ConfigException>(() => ConfigLoader.LoadCampaign(string.Format(Skeleton, ""), graph));
            Assert.Throws<ConfigException>(() => ConfigLoader.LoadCampaign(string.Format(Skeleton, @", ""tier"": 5"), graph));
            var ok = ConfigLoader.LoadCampaign(string.Format(Skeleton, @", ""tier"": 3"), graph);
            Assert.That(ok.Events.Single().Tier, Is.EqualTo(3));
        }
    }
}
