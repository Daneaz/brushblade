using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>独立效果执行与跨场开局效果(spec v6 §5.1 / §5.2 / §11.5)。</summary>
    public class OpeningEffectTests
    {
        private sealed class DepthRecorder : IBattleHookListener
        {
            public readonly List<HookArgs> Log = new();
            public void OnHook(BattleEngine battle, in HookArgs args) => Log.Add(args);
        }

        [Test]
        public void Detached_AppliesEffects_AtDepthOne()
        {
            var b = RebalanceFixture.Battle(RebalanceFixture.Graph(), new[] { "甲" }, RebalanceFixture.Mob(), RebalanceFixture.Mob());
            var rec = new DepthRecorder();
            b.AddHookListener(rec);
            b.ApplyDetachedEffects("甲", Element.Fire, new[] { new EffectDef(EffectKind.BurnAll, 2) });
            Assert.That(b.Enemies.All(e => e.Statuses.TotalMagnitude(StatusKind.Burn) == 2), Is.True);
            Assert.That(rec.Log.Where(a => a.Kind == HookKind.StatusApplied).All(a => a.Depth == 1), Is.True, "R4:触发结算内深度为 1");
            Assert.That(b.TriggerDepth, Is.EqualTo(0), "退出后归零");
        }

        [Test]
        public void Detached_MetalDamage_DoesNotTriggerMoraleRelease()
        {
            var config = new BattleConfig { PlayerMaxHp = 500, PlayerAttack = 100, MoraleReleasePercent = 300 };
            var b = new BattleEngine(RebalanceFixture.Graph(), config, new[] { "甲" }, Array.Empty<string>(),
                new[] { RebalanceFixture.Mob() }, seed: 1,
                startingStatuses: new[] { new StatusEffect { Kind = StatusKind.Morale, Polarity = StatusPolarity.Buff,
                    Magnitude = config.MoraleCap, TurnsLeft = -1 } });
            b.ApplyDetachedEffects("甲", Element.Metal, new[] { new EffectDef(EffectKind.DamageAll, 100) });
            // 战意本身给攻击加成(满层 ×1.5 左右),所以伤害 > 100;断金 +300% 会让它 ≥ 400
            Assert.That(100000 - b.Enemies[0].Hp, Is.LessThan(400), "独立效果不吃断金 +300%");
            Assert.That(b.PlayerStatuses.TotalMagnitude(StatusKind.Morale), Is.EqualTo(config.MoraleCap), "战意不被清空");
        }

        [Test]
        public void RegisterOpening_TargetedKind_Throws()
        {
            var b = RebalanceFixture.Battle(RebalanceFixture.Graph(), new[] { "甲" }, RebalanceFixture.Mob());
            Assert.Throws<ArgumentException>(() => b.RegisterOpening(new OpeningEffect
                { SourceCharId = "甲", Element = Element.Fire, Kind = EffectKind.BurnSingle, Value = 2, BattlesLeft = 1 }));
        }

        [Test]
        public void Merge_SameKind_KeepsStrongest()
        {
            var a = new OpeningEffect { SourceCharId = "炎", Kind = EffectKind.BurnAll, Value = 2, BattlesLeft = 1 };
            var b = new OpeningEffect { SourceCharId = "焱", Kind = EffectKind.BurnAll, Value = 3, BattlesLeft = 5 };
            var c = new OpeningEffect { SourceCharId = "鍂", Kind = EffectKind.Morale, Value = 2, BattlesLeft = 1 };
            var merged = OpeningRules.Merge(new[] { a }, new[] { b, c });
            Assert.That(merged.Count, Is.EqualTo(2));
            Assert.That(merged.Single(m => m.Kind == EffectKind.BurnAll).Value, Is.EqualTo(3));
            Assert.That(merged.Single(m => m.Kind == EffectKind.BurnAll).SourceCharId, Is.EqualTo("焱"));
        }

        [Test]
        public void Merge_TieOnValue_KeepsLongerDuration()
        {
            var a = new OpeningEffect { SourceCharId = "炎", Kind = EffectKind.BurnAll, Value = 2, BattlesLeft = 1 };
            var b = new OpeningEffect { SourceCharId = "焱", Kind = EffectKind.BurnAll, Value = 2, BattlesLeft = 5 };
            Assert.That(OpeningRules.Merge(new[] { a }, new[] { b }).Single().BattlesLeft, Is.EqualTo(5));
        }

        [Test]
        public void Run_OpeningRegisteredInBattle1_AppliesInBattle2Only()
        {
            var config = new RunConfig
            {
                Encounters = new[] { new[] { RebalanceFixture.Mob(hp: 20) }, new[] { RebalanceFixture.Mob(hp: 20) },
                    new[] { RebalanceFixture.Mob(hp: 20) } },
                RewardPool = new[] { "甲" },
            };
            var run = new RunEngine(RebalanceFixture.Graph(), config,
                new BattleConfig { PlayerMaxHp = 500, PlayerAttack = 100 },
                new[] { "甲", "甲", "甲" }, Array.Empty<string>(), seed: 1); // 出过的字已消耗,多备几张好在后两场继续出

            run.Battle.RegisterOpening(new OpeningEffect
                { SourceCharId = "甲", Element = Element.Metal, Kind = EffectKind.Morale, Value = 2, BattlesLeft = 1 });
            run.Battle.Cast("甲", 0);
            run.AdvanceAfterBattle();
            while (run.Phase == RunPhase.Reward) run.SkipReward();
            Assert.That(run.Battle.PlayerStatuses.TotalMagnitude(StatusKind.Morale), Is.EqualTo(2), "第 2 场开局生效");

            var restored = RunEngine.Restore(run.Capture(), RebalanceFixture.Graph(), config,
                new BattleConfig { PlayerMaxHp = 500, PlayerAttack = 100 }, null);
            restored.Battle.Cast("甲", 0);
            restored.AdvanceAfterBattle();
            while (restored.Phase == RunPhase.Reward) restored.SkipReward();
            Assert.That(restored.Battle.PlayerStatuses.TotalMagnitude(StatusKind.Morale), Is.EqualTo(0), "1 场后到期(读档后依然正确递减)");
        }

        [Test]
        public void RegisterOpening_AllyKind_Shield_AppliesToPlayerInNextBattle()
        {
            var config = new RunConfig
            {
                Encounters = new[] { new[] { RebalanceFixture.Mob(hp: 20) }, new[] { RebalanceFixture.Mob(hp: 20) } },
                RewardPool = new[] { "甲" },
            };
            var run = new RunEngine(RebalanceFixture.Graph(), config,
                new BattleConfig { PlayerMaxHp = 500, PlayerAttack = 100 },
                new[] { "甲", "甲", "甲" }, Array.Empty<string>(), seed: 1);
            run.Battle.RegisterOpening(new OpeningEffect
                { SourceCharId = "甲", Element = Element.Metal, Kind = EffectKind.Shield, Value = 50, BattlesLeft = 1 });
            run.Battle.Cast("甲", 0);
            run.AdvanceAfterBattle();
            while (run.Phase == RunPhase.Reward) run.SkipReward();
            Assert.That(run.Battle.PlayerShield, Is.GreaterThan(0), "友方类开局效果作用于玩家");
        }

        [Test]
        public void Run_Openings_SurviveRunSnapshot_AndKeepCounting()
        {
            var config = new RunConfig
            {
                Encounters = new[] { new[] { RebalanceFixture.Mob(hp: 20) }, new[] { RebalanceFixture.Mob(hp: 20) },
                    new[] { RebalanceFixture.Mob(hp: 20) }, new[] { RebalanceFixture.Mob(hp: 20) } },
                RewardPool = new[] { "甲" },
            };
            var bc = new BattleConfig { PlayerMaxHp = 500, PlayerAttack = 100 };
            var run = new RunEngine(RebalanceFixture.Graph(), config, bc,
                new[] { "甲", "甲", "甲", "甲", "甲" }, Array.Empty<string>(), seed: 1);
            run.Battle.RegisterOpening(new OpeningEffect
                { SourceCharId = "甲", Element = Element.Metal, Kind = EffectKind.Morale, Value = 2, BattlesLeft = 2 });
            run.Battle.Cast("甲", 0);
            run.AdvanceAfterBattle();
            while (run.Phase == RunPhase.Reward) run.SkipReward();
            Assert.That(run.Battle.PlayerStatuses.TotalMagnitude(StatusKind.Morale), Is.EqualTo(2), "第 2 场");

            var restored = RunEngine.Restore(run.Capture(), RebalanceFixture.Graph(), config, bc, null);
            restored.Battle.Cast("甲", 0);
            restored.AdvanceAfterBattle();
            while (restored.Phase == RunPhase.Reward) restored.SkipReward();
            Assert.That(restored.Battle.PlayerStatuses.TotalMagnitude(StatusKind.Morale), Is.EqualTo(2), "读档后第 3 场仍生效");

            restored.Battle.Cast("甲", 0);
            restored.AdvanceAfterBattle();
            while (restored.Phase == RunPhase.Reward) restored.SkipReward();
            Assert.That(restored.Battle.PlayerStatuses.TotalMagnitude(StatusKind.Morale), Is.EqualTo(0), "第 4 场到期");
        }

        [Test]
        public void BattleSnapshot_PendingOpenings_RoundTrip()
        {
            var graph = RebalanceFixture.Graph();
            var b = RebalanceFixture.Battle(graph, new[] { "甲" }, RebalanceFixture.Mob());
            b.RegisterOpening(new OpeningEffect
                { SourceCharId = "甲", Element = Element.Metal, Kind = EffectKind.Morale, Value = 3, BattlesLeft = 2 });
            var restored = BattleEngine.Restore(b.Capture(), graph,
                new BattleConfig { PlayerMaxHp = RebalanceFixture.BaseMaxHp, PlayerAttack = 100 }, null,
                new Dictionary<string, EnemyDef> { ["怔"] = RebalanceFixture.Mob() });
            Assert.That(restored.PendingOpenings.Count, Is.EqualTo(1));
            var o = restored.PendingOpenings[0];
            Assert.That(o.SourceCharId, Is.EqualTo("甲"));
            Assert.That(o.Kind, Is.EqualTo(EffectKind.Morale));
            Assert.That(o.Value, Is.EqualTo(3));
            Assert.That(o.BattlesLeft, Is.EqualTo(2));
        }
    }
}
