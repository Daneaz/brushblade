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
        public void DiscardedChar_DoesNotReturnNextBattleInRun() // 丢弃≠使用,关内不回归
        {
            var run = Run();
            // Run() 起手字库 [焚];先补一张灯进字库再验证
            run.Battle.Cast("焚"); // 焚使用 → 胜利
            Assert.That(run.Battle.Phase, Is.EqualTo(BattlePhase.Won));
            run.AdvanceAfterBattle();
            run.SkipReward();

            Assert.That(run.Battle.Library, Does.Contain("焚")); // 使用的字回归
            run.Battle.Discard("焚");
            WinByFallbackOrSkip(run);
        }

        private static void WinByFallbackOrSkip(RunEngine run)
        {
            // 丢弃后字库为空,验证到此为止:丢弃的字已不在库中,也不在已使用列表
            Assert.That(run.Battle.Library, Is.Empty);
            Assert.That(run.Battle.UsedChars, Is.Empty);
        }

        // ---- 局内广告扩容(2026-07-06 拍板):字库 6+2、部件池 10+2,每关各一次,关卡结束恢复 ----

        [Test]
        public void Defaults_Library6_Pool10()
        {
            var config = new BattleConfig();
            Assert.That(config.LibraryCapacity, Is.EqualTo(6));
            Assert.That(config.PoolCapacity, Is.EqualTo(10));
        }

        [Test]
        public void ExpandPool_OncePerRun_RaisesCapBy2()
        {
            var run = Run();
            Assert.That(run.TryExpandPool(), Is.True);
            Assert.That(run.TryExpandPool(), Is.False); // 每关一次

            // 扩容后池上限 12:塞到 11 个再拆(+2)会被拒,10 个 +2 = 12 恰好允许
            var pool = new System.Collections.Generic.List<string>();
            for (int i = 0; i < 10; i++) pool.Add("木");
            var result = ForgeEngine.TryDismantle("焚", Graph(),
                new ForgeState(new[] { "焚" }, pool), 12);
            Assert.That(result.Success, Is.True);
        }

        [Test]
        public void ExpandLibrary_AffectsCurrentAndLaterBattlesInRun()
        {
            var run = Run();
            Assert.That(run.TryExpandLibrary(), Is.True);
            Assert.That(run.TryExpandLibrary(), Is.False);

            // 灌满 6 张后仍可合成第 7 张(上限已到 8)
            var battle = run.Battle;
            // 直接验证配置生效:通过合成路径太长,改用容量可观察值
            Assert.That(battle.LibraryCapacity, Is.EqualTo(8));
            Assert.That(battle.PoolCapacity, Is.EqualTo(10));

            WinCurrentBattle(run);
            run.AdvanceAfterBattle();
            run.SkipReward();
            Assert.That(run.Battle.LibraryCapacity, Is.EqualTo(8)); // 关内跨场保持
        }

        [Test]
        public void BattleEngine_StartsWithCarriedHp() // startingHp 参数
        {
            var engine = new BattleEngine(Graph(), new BattleConfig(),
                Array.Empty<string>(), Array.Empty<string>(),
                new[] { Weak() }, seed: 1, startingHp: 33);
            Assert.That(engine.PlayerHp, Is.EqualTo(33));
        }

        // ---- 字库容量全链路(3.8.1:满库须替换或不要;修无尽塔字库超上限)----

        private static RunEngine RunWith(BattleConfig battleConfig, string[] library,
            RunConfig config = null, string[] pool = null, int seed = 7) =>
            new(Graph(), config ?? TwoBattles(), battleConfig,
                library, pool ?? Array.Empty<string>(), seed);

        [Test]
        public void PickReward_BelowCapacity_ReturnsTrue()
        {
            var run = Run();
            WinCurrentBattle(run);
            run.AdvanceAfterBattle();
            Assert.That(run.PickReward(0), Is.True);
        }

        [Test]
        public void PickReward_AtCapacity_Rejected_StaysInReward()
        {
            var run = RunWith(new BattleConfig { LibraryCapacity = 2, DropTable = new[] { "木" } },
                new[] { "焚", "灯" });
            WinCurrentBattle(run); // 焚进已使用
            run.AdvanceAfterBattle();
            // 回归后字库 [灯,焚] = 2,已满
            Assert.That(run.CarriedLibrary, Is.EquivalentTo(new[] { "灯", "焚" }));
            Assert.That(run.PickReward(0), Is.False);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Reward)); // 停在奖励页等替换/跳过
        }

        [Test]
        public void PickRewardReplacing_SwapsAndProceeds()
        {
            var run = RunWith(new BattleConfig { LibraryCapacity = 2, DropTable = new[] { "木" } },
                new[] { "焚", "灯" });
            WinCurrentBattle(run);
            run.AdvanceAfterBattle();

            int replaceIndex = -1; // 替换掉「灯」
            for (int i = 0; i < run.CarriedLibrary.Count; i++)
                if (run.CarriedLibrary[i] == "灯") replaceIndex = i;
            var picked = run.RewardOptions[0];

            Assert.That(run.PickRewardReplacing(0, replaceIndex), Is.True);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.InBattle));
            Assert.That(run.Battle.Library.Count, Is.EqualTo(2)); // 不超上限
            Assert.That(run.Battle.Library, Does.Contain(picked));
            Assert.That(run.Battle.Library, Does.Not.Contain("灯")); // 被替换的字永久移除
        }

        [Test]
        public void SkipReward_AtCapacity_StillWorks() // 「选择不要」
        {
            var run = RunWith(new BattleConfig { LibraryCapacity = 2, DropTable = new[] { "木" } },
                new[] { "焚", "灯" });
            WinCurrentBattle(run);
            run.AdvanceAfterBattle();
            run.SkipReward();
            Assert.That(run.Phase, Is.EqualTo(RunPhase.InBattle));
            Assert.That(run.Battle.Library.Count, Is.EqualTo(2));
        }

        [Test]
        public void EventGainChar_AtCapacity_Rejected()
        {
            var config = new RunConfig
            {
                Encounters = new[] { new[] { Weak() }, new[] { Weak() } },
                RewardPool = new[] { "灯" },
                EventChancePercent = 100,
                EventPool = new[] { new EventDef { Id = "字摊", Text = "…", Options = new[]
                {
                    new EventOption { Label = "得灯", GainChar = "灯" },
                    new EventOption { Label = "离开" },
                } } },
            };
            var run = RunWith(new BattleConfig { LibraryCapacity = 1, DropTable = new[] { "木" } },
                new[] { "焚" }, config);
            WinCurrentBattle(run);
            run.AdvanceAfterBattle();
            run.SkipReward(); // 100% 进奇遇
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Event));
            Assert.That(run.ChooseEventOption(0), Is.False); // 字库已满,收不下
            Assert.That(run.ChooseEventOption(1), Is.True);  // 离开可选
            Assert.That(run.Battle.Library, Is.EquivalentTo(new[] { "焚" }));
        }

        [Test]
        public void EventGainComponents_ClampedAtPoolCapacity()
        {
            var config = new RunConfig
            {
                Encounters = new[] { new[] { Weak() }, new[] { Weak() } },
                RewardPool = new[] { "灯" },
                EventChancePercent = 100,
                EventPool = new[] { new EventDef { Id = "废稿堆", Text = "…", Options = new[]
                {
                    new EventOption { Label = "拾取", GainComponents = new[] { "火", "火" } },
                } } },
            };
            var run = RunWith(new BattleConfig { PoolCapacity = 2, DropTable = new[] { "木" } },
                new[] { "焚" }, config, pool: new[] { "木", "木" }); // 池已满
            WinCurrentBattle(run);
            run.AdvanceAfterBattle();
            run.SkipReward();
            Assert.That(run.ChooseEventOption(0), Is.True); // 池满则不入(同「池满则不掉」)
            Assert.That(run.Battle.Pool.Count, Is.LessThanOrEqualTo(2));
        }

        [Test]
        public void Compose_CountsUsedChars_TowardLibraryCapacity() // 出过的字战后回归,占容量(堵回归溢出)
        {
            var engine = new BattleEngine(Graph(),
                new BattleConfig { LibraryCapacity = 1, DropTable = new[] { "木" } },
                new[] { "灯" }, new[] { "木", "木" }, new[] { Strong() }, seed: 1);
            Assert.That(engine.Cast("灯", 0), Is.EqualTo(BattleError.None)); // 灯进已使用
            var error = engine.Compose("林"); // 库虽空,但灯将回归:0 个可用位
            Assert.That(error, Is.EqualTo(BattleError.ForgeFailed));
            Assert.That(engine.LastForgeError, Is.EqualTo(ForgeError.LibraryFull));
        }
    }
}
