using System;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>连战状态机:17.2 阶段 1 格式;跨战斗规则见第 9 章 / 3.8.1 / 3.8.2。</summary>
    public class RunEngineTests
    {
        private static RecipeGraph Graph() => new(new[]
        {
            new CharDef("木", Element.Wood),
            new CharDef("火", Element.Fire,
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 4) }),
            new CharDef("林", Element.Wood, new[] { "木", "木" }),
            new CharDef("焚", Element.Fire, new[] { "林", "火" }, apCost: 2,
                effects: new[] { new EffectDef(EffectKind.DamageAll, 18), new EffectDef(EffectKind.BurnAll, 1) }),
            new CharDef("灯", Element.Fire, new[] { "火", "丁" },
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 6), new EffectDef(EffectKind.BurnSingle, 1) }),
            new CharDef("丁", null),
        });

        private static EnemyDef Weak(int hp = 4) => new("枯", Element.Wood, hp, 2);
        private static EnemyDef Strong() => new("讹影", Element.Heart, 100, 60);

        private static RunConfig TwoBattles() => new()
        {
            Encounters = new[] { new[] { Weak() }, new[] { Weak() } },
            RewardPool = new[] { "灯", "焚", "林" },
        };

        private static RunEngine Run(RunConfig config = null, int seed = 7) =>
            new(Graph(), config ?? TwoBattles(), new BattleConfig { DropTable = new[] { "木" } },
                startingLibrary: new[] { "焚" }, startingPool: Array.Empty<string>(), seed: seed);

        private static void WinCurrentBattle(RunEngine run) // 焚 AOE 一发清弱怪
        {
            var error = run.Battle.Cast("焚");
            Assert.That(error, Is.EqualTo(BattleError.None));
            Assert.That(run.Battle.Phase, Is.EqualTo(BattlePhase.Won));
        }

        [Test]
        public void Run_StartsInFirstBattle()
        {
            var run = Run();
            Assert.That(run.Phase, Is.EqualTo(RunPhase.InBattle));
            Assert.That(run.BattleIndex, Is.EqualTo(0));
            Assert.That(run.Battle.Enemies.Single().Def.Id, Is.EqualTo("枯"));
        }

        [Test]
        public void Won_Advance_EntersReward_WithThreeOptionsFromPool()
        {
            var run = Run();
            WinCurrentBattle(run);
            run.AdvanceAfterBattle();
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Reward));
            Assert.That(run.RewardOptions.Count, Is.EqualTo(3));
            Assert.That(run.RewardOptions, Is.SubsetOf(new[] { "灯", "焚", "林" }));
        }

        [Test]
        public void PickReward_AddsChar_CarriesStateIntoNextBattle()
        {
            var run = Run();
            WinCurrentBattle(run); // 焚进入"已使用"
            int hpAfterBattle = run.Battle.PlayerHp;
            run.AdvanceAfterBattle();

            int lampIndex = -1;
            for (int i = 0; i < run.RewardOptions.Count; i++)
                if (run.RewardOptions[i] == "灯") lampIndex = i;
            if (lampIndex < 0) lampIndex = 0;
            var picked = run.RewardOptions[lampIndex];

            run.PickReward(lampIndex);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.InBattle));
            Assert.That(run.BattleIndex, Is.EqualTo(1));
            Assert.That(run.Battle.Library, Does.Contain("焚"));    // 出过的字回归字库(3.8.1)
            Assert.That(run.Battle.Library, Does.Contain(picked));  // 奖励入库
            Assert.That(run.Battle.PlayerHp, Is.EqualTo(hpAfterBattle)); // HP 跨战斗保留
            Assert.That(run.Battle.Pool, Does.Contain("木"));       // 部件池保留(3.8.2)+ 新回合掉落
        }

        [Test]
        public void SkipReward_StartsNextBattle_WithoutNewChar()
        {
            var run = Run();
            WinCurrentBattle(run);
            run.AdvanceAfterBattle();
            run.SkipReward();
            Assert.That(run.Phase, Is.EqualTo(RunPhase.InBattle));
            Assert.That(run.Battle.Library, Is.EquivalentTo(new[] { "焚" }));
        }

        [Test]
        public void WinLastBattle_RunWon_NoRewardPhase()
        {
            var run = Run();
            WinCurrentBattle(run);
            run.AdvanceAfterBattle();
            run.SkipReward();
            WinCurrentBattle(run); // 第二战即最后一战
            run.AdvanceAfterBattle();
            Assert.That(run.Phase, Is.EqualTo(RunPhase.RunWon));
        }

        [Test]
        public void LostBattle_RunLost()
        {
            var run = Run(new RunConfig
            {
                Encounters = new[] { new[] { Strong() } }, // 攻 60,一回合打死
                RewardPool = new[] { "灯" },
            });
            run.Battle.EndTurn();
            Assert.That(run.Battle.Phase, Is.EqualTo(BattlePhase.Lost));
            run.AdvanceAfterBattle();
            Assert.That(run.Phase, Is.EqualTo(RunPhase.RunLost));
        }

        [Test]
        public void RewardOptions_DeterministicBySeed()
        {
            var a = Run(seed: 99);
            var b = Run(seed: 99);
            WinCurrentBattle(a); a.AdvanceAfterBattle();
            WinCurrentBattle(b); b.AdvanceAfterBattle();
            Assert.That(a.RewardOptions, Is.EqualTo(b.RewardOptions));
        }

        [Test]
        public void BattleEngine_StartsWithCarriedHp() // startingHp 参数
        {
            var engine = new BattleEngine(Graph(), new BattleConfig(),
                Array.Empty<string>(), Array.Empty<string>(),
                new[] { Weak() }, seed: 1, startingHp: 33);
            Assert.That(engine.PlayerHp, Is.EqualTo(33));
        }
    }
}
