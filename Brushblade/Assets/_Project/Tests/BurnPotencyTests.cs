using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>灼的火力(spec v7 §4,D1 Task 8):每层伤害 × 给该目标上过灼的火字中最高的等级系数。
    /// 全 1 级 = 系数 100 = 跳过乘除,与旧结算逐位相同。</summary>
    public class BurnPotencyTests
    {
        private static CharDef Burner(string id, int stacks) =>
            RebalanceFixture.Char(id, new EffectDef(EffectKind.BurnSingle, stacks));

        private static readonly CharDef Settle = RebalanceFixture.Char("熯", new EffectDef(EffectKind.BurnSettleNow, 0));
        private static readonly CharDef Boom = RebalanceFixture.Char("煸", new EffectDef(EffectKind.Detonate, 0));

        private static RecipeGraph Graph() =>
            RebalanceFixture.Graph(Burner("燃", 1), Burner("燃二", 3), Burner("灸", 1), Settle, Boom);

        private static BattleConfig Config(int playerAttack = 100) =>
            new BattleConfig { PlayerMaxHp = RebalanceFixture.BaseMaxHp, PlayerAttack = playerAttack };

        private static BattleEngine Battle(Dictionary<string, int> levels, int playerAttack = 100, params string[] library) =>
            new BattleEngine(Graph(), Config(playerAttack), library, Array.Empty<string>(),
                new[] { RebalanceFixture.Mob() }, seed: 1, cardLevels: levels);

        private static int Lost(BattleEngine b, Action act)
        {
            int hp = b.Enemies[0].Hp;
            act();
            return hp - b.Enemies[0].Hp;
        }

        [Test]
        public void Lv8Burn_OneStack_SettlesAtCeilOf20Times142()
        {
            var b = Battle(new Dictionary<string, int> { ["燃"] = 8 }, 100, "燃", "熯");
            b.Cast("燃", 0);
            Assert.That(b.Enemies[0].Statuses.Find(StatusKind.Burn).Potency, Is.EqualTo(142));
            Assert.That(Lost(b, () => b.Cast("熯", 0)), Is.EqualTo(29), "ceil(20 × 1.42) = 29");
        }

        [Test]
        public void Lv8Burn_StillMultipliesByAttackPercent()
        {
            var b = Battle(new Dictionary<string, int> { ["燃"] = 8 }, 200, "燃", "熯");
            b.Cast("燃", 0);
            Assert.That(Lost(b, () => b.Cast("熯", 0)), Is.EqualTo(58), "ceil(20 × 1.42) × 200%");
        }

        [Test]
        public void LowerLevelBurn_DoesNotLowerPotency_HigherRaisesIt()
        {
            var b = Battle(new Dictionary<string, int> { ["燃"] = 8, ["灸"] = 1, ["燃二"] = 10 }, 100, "燃", "灸", "燃二", "熯");
            b.Cast("燃", 0);
            b.Cast("灸", 0);
            var burn = b.Enemies[0].Statuses.Find(StatusKind.Burn);
            Assert.That(burn.Magnitude, Is.EqualTo(2));
            Assert.That(burn.Potency, Is.EqualTo(142), "低级字再上灼不降火力");
            b.Cast("燃二", 0);
            Assert.That(b.Enemies[0].Statuses.Find(StatusKind.Burn).Potency, Is.EqualTo(154), "更高级的抬高");
        }

        [Test]
        public void Lv1Burn_SettlesExactlyAsBefore()
        {
            var b = Battle(null, 100, "燃", "熯");
            b.Cast("燃", 0);
            Assert.That(b.Enemies[0].Statuses.Find(StatusKind.Burn).Potency, Is.EqualTo(100));
            Assert.That(Lost(b, () => b.Cast("熯", 0)), Is.EqualTo(20));
        }

        [Test]
        public void Detonate_AlsoUsesPotency()
        {
            var b = Battle(new Dictionary<string, int> { ["燃二"] = 8 }, 100, "燃二", "煸");
            b.Cast("燃二", 0);
            // 3 层:3×4/2 = 6 个层·回合,每层 ceil(20 × 1.42) = 29
            Assert.That(Lost(b, () => b.Cast("煸", 0)), Is.EqualTo(6 * 29));
        }

        [Test]
        public void EndTurnTick_UsesPotency()
        {
            var b = Battle(new Dictionary<string, int> { ["燃二"] = 8 }, 100, "燃二");
            b.Cast("燃二", 0);
            Assert.That(Lost(b, () => b.EndTurn()), Is.EqualTo(3 * 29));
        }

        [Test]
        public void Potency_SurvivesRealSaveFile()
        {
            var enemy = RebalanceFixture.Mob(attack: 0);
            var graph = Graph();
            var battleConfig = Config();
            var runConfig = new RunConfig
            {
                Encounters = new[] { new[] { enemy }, new[] { enemy } },
                RewardPool = new[] { "燃" },
            };
            var levels = new Dictionary<string, int> { ["燃"] = 8 };
            var run = new RunEngine(graph, runConfig, battleConfig, new[] { "燃", "燃" }, Array.Empty<string>(), 3,
                startingInk: 50, perFloorNormalShield: 2, cardLevels: levels);
            run.Battle.Cast("燃", 0);

            var meta = new MetaState
            {
                EndlessV2 = new EndlessSaveState
                {
                    Depth = 3, Seed = 999,
                    InProgress = new InProgressRun { FromDepth = 1, FirstTowerSegment = true, Run = run.Capture() },
                },
            };
            var reloaded = Data.SaveSerializer.FromJson(Data.SaveSerializer.ToJson(meta));
            var restored = RunEngine.Restore(reloaded.EndlessV2.InProgress.Run, graph, runConfig, battleConfig, levels,
                startingInk: 50, perFloorNormalShield: 2);
            Assert.That(restored.Battle.Enemies[0].Statuses.Find(StatusKind.Burn).Potency, Is.EqualTo(142));
        }
    }
}
