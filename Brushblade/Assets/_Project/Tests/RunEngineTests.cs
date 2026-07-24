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
        public void PickReward_AddsChar_CastCharConsumed()
        {
            var run = Run();
            WinCurrentBattle(run); // 焚出手即消耗(v0.7 拍板,无回归)
            int hpAfterBattle = run.Battle.PlayerHp;
            run.AdvanceAfterBattle();

            int lampIndex = -1;
            for (int i = 0; i < run.RewardOptions.Count; i++)
                if (run.RewardOptions[i] == "灯") lampIndex = i;
            if (lampIndex < 0) lampIndex = 0;
            var picked = run.RewardOptions[lampIndex];

            run.PickReward(lampIndex);
            run.SkipReward(); // 字已取,放弃部件额度即开拔(可不取满)
            Assert.That(run.Phase, Is.EqualTo(RunPhase.InBattle));
            Assert.That(run.BattleIndex, Is.EqualTo(1));
            Assert.That(run.Battle.Library, Does.Not.Contain("焚")); // 出字即消耗,不回归
            Assert.That(run.Battle.Library, Does.Contain(picked));   // 奖励入库
            Assert.That(run.Battle.PlayerHp, Is.EqualTo(hpAfterBattle)); // HP 跨战斗保留
            Assert.That(run.Battle.Pool, Does.Contain("木"));        // 部件池保留(3.8.2)+ 新回合掉落
        }

        [Test]
        public void SkipReward_StartsNextBattle_WithoutNewChar()
        {
            var run = Run();
            WinCurrentBattle(run);
            run.AdvanceAfterBattle();
            run.SkipReward();
            Assert.That(run.Phase, Is.EqualTo(RunPhase.InBattle));
            Assert.That(run.Battle.Library, Is.Empty); // 焚已消耗,跳过奖励则空库(兜底出部件仍可战)
        }

        /// <summary>段末(Boss 层)也发战利品(2026-07-20 拍板),取完才结算。</summary>
        [Test]
        public void WinLastBattle_EntersReward_ThenRunWon()
        {
            var run = Run();
            WinCurrentBattle(run);
            run.AdvanceAfterBattle();
            int fenIndex = -1; // 焚已消耗,末战需从奖励再拿一张(池仅 3 字,候选必含焚)
            for (int i = 0; i < run.RewardOptions.Count; i++)
                if (run.RewardOptions[i] == "焚") fenIndex = i;
            run.PickReward(fenIndex);
            run.SkipReward();

            WinCurrentBattle(run); // 第二战即最后一战
            run.AdvanceAfterBattle();
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Reward)); // 先给战利品
            Assert.That(run.RewardOptions, Is.Not.Empty);

            var picked = run.RewardOptions[0];
            Assert.That(run.PickReward(0), Is.True);
            Assert.That(run.CarriedLibrary, Does.Contain(picked)); // 取到的字进携带态,供外层写快照
            run.SkipReward();
            Assert.That(run.Phase, Is.EqualTo(RunPhase.RunWon)); // 取完才结算
        }

        [Test]
        public void LastBattleReward_SkippedEntirely_StillRunWon()
        {
            var run = Run();
            WinCurrentBattle(run);
            run.AdvanceAfterBattle();
            for (int i = 0; i < run.RewardOptions.Count; i++)
                if (run.RewardOptions[i] == "焚") run.PickReward(i); // 末战还得靠焚清场
            run.SkipReward();

            WinCurrentBattle(run);
            run.AdvanceAfterBattle();
            run.SkipReward(); // 段末战利品一件不取,也要能结算
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

        // ---- 广告复活(2026-07-24):续战 + 补给注入当前战斗 ----

        private static RunEngine LostRun(int seed = 7) // 打到败北态(RunPhase 仍 InBattle)
        {
            var run = Run(new RunConfig
            {
                Encounters = new[] { new[] { Strong() } },
                RewardPool = new[] { "灯", "焚", "林" },
            }, seed);
            run.Battle.EndTurn();
            Assert.That(run.Battle.Phase, Is.EqualTo(BattlePhase.Lost));
            return run;
        }

        [Test]
        public void ReviveAvailable_WhenLost_AndNotYetUsed()
        {
            var run = LostRun();
            Assert.That(run.ReviveAvailable, Is.True);
        }

        [Test]
        public void TryRevive_RestoresBattle_EntersRevivingWithPicks()
        {
            var run = LostRun();
            Assert.That(run.TryRevive(), Is.True);
            Assert.That(run.Revived, Is.True);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Reviving));
            Assert.That(run.Battle.Phase, Is.EqualTo(BattlePhase.PlayerTurn)); // 满血续战
            Assert.That(run.Battle.PlayerHp, Is.EqualTo(50));
            Assert.That(run.ReviveCharPicksLeft, Is.EqualTo(2));
            Assert.That(run.ReviveComponentPicksLeft, Is.EqualTo(3));
            Assert.That(run.ReviveAvailable, Is.False); // 一次性
        }

        [Test]
        public void PickReviveReward_WritesIntoLiveBattle_NotCarried()
        {
            var run = LostRun();
            run.TryRevive();
            string charPick = run.RewardOptions[0];
            Assert.That(run.PickReviveChar(0), Is.True);
            Assert.That(run.Battle.Library, Does.Contain(charPick)); // 注入当前战斗
            Assert.That(run.ReviveCharPicksLeft, Is.EqualTo(1));

            string compPick = run.ComponentOptions[0];
            Assert.That(run.PickReviveComponent(0), Is.True);
            Assert.That(run.Battle.Pool, Does.Contain(compPick));
            Assert.That(run.ReviveComponentPicksLeft, Is.EqualTo(2));
        }

        [Test]
        public void ReviveReward_PicksExhausted_ResumesBattle()
        {
            var run = LostRun();
            run.TryRevive();
            run.PickReviveChar(0);
            run.PickReviveChar(0);                 // 2 次字额度用尽
            run.PickReviveComponent(0);
            run.PickReviveComponent(0);
            run.PickReviveComponent(0);            // 3 次部件额度用尽 → 收尾
            Assert.That(run.Phase, Is.EqualTo(RunPhase.InBattle));
            Assert.That(run.Battle.Phase, Is.EqualTo(BattlePhase.PlayerTurn));
        }

        [Test]
        public void SkipReviveReward_ResumesBattleImmediately()
        {
            var run = LostRun();
            run.TryRevive();
            run.SkipReviveReward();
            Assert.That(run.Phase, Is.EqualTo(RunPhase.InBattle));
        }

        [Test]
        public void Revive_OnlyOncePerRun()
        {
            var run = LostRun();
            run.TryRevive();
            run.SkipReviveReward();
            run.Battle.EndTurn(); // 又被 Strong 打死
            Assert.That(run.Battle.Phase, Is.EqualTo(BattlePhase.Lost));
            Assert.That(run.ReviveAvailable, Is.False);
            Assert.That(run.TryRevive(), Is.False);
        }

        [Test]
        public void MarkRevived_BlocksLaterRevive() // 断点续爬恢复:防重进本层二次复活
        {
            var run = LostRun();
            run.MarkRevived();
            Assert.That(run.ReviveAvailable, Is.False);
            Assert.That(run.TryRevive(), Is.False);
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

        // ---- 局内广告扩容(2026-07-06 拍板):字库 6+2、部件池 10+2,一局各一次 ----

        [Test]
        public void Defaults_Library6_Pool10_Drops2()
        {
            var config = new BattleConfig();
            Assert.That(config.LibraryCapacity, Is.EqualTo(6));
            Assert.That(config.PoolCapacity, Is.EqualTo(10));
            Assert.That(config.DropsPerTurn, Is.EqualTo(2)); // 3→2(2026-07-19 二次拍板)
        }

        // ---- 战利品双排 5 选 1(2026-07-19 拍板):字池抽 5 选 1 + 固定五行部件 5 选 1;
        //      Boss 层同样发战利品(2026-07-20) ----

        /// <summary>池含 3 个字 + 3 个部件:部件不作为字奖励(2026-07-20),故字排只出 3 个。</summary>
        private static RunConfig SixCharPool() => new()
        {
            Encounters = new[] { new[] { Weak() }, new[] { Weak() } },
            RewardPool = new[] { "灯", "焚", "林", "木", "火", "丁" },
        };

        private static bool PickComponent(RunEngine run, string id)
        {
            for (int i = 0; i < run.ComponentOptions.Count; i++)
                if (run.ComponentOptions[i] == id) return run.PickRewardComponent(i);
            return false;
        }

        [Test]
        public void Reward_RollsCharsFromPool_AndFiveFixedComponents()
        {
            var run = Run(SixCharPool());
            WinCurrentBattle(run);
            run.AdvanceAfterBattle();
            Assert.That(run.RewardOptions, Is.EquivalentTo(new[] { "灯", "焚", "林" })); // 部件不入字排
            Assert.That(run.ComponentOptions, Is.EquivalentTo(new[] { "金", "木", "水", "火", "土" }));
            Assert.That(run.CharPicksLeft, Is.EqualTo(1));
            Assert.That(run.ComponentPicksLeft, Is.EqualTo(1));
        }

        [Test]
        public void PickOneCharAndOneComponent_AutoProceeds()
        {
            var run = Run(SixCharPool());
            WinCurrentBattle(run);
            run.AdvanceAfterBattle();

            var first = run.RewardOptions[0];
            Assert.That(run.PickReward(0), Is.True);
            Assert.That(run.PickReward(0), Is.False); // 字额度用完(5 选 1)
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Reward)); // 部件额度未用,尚未开拔

            Assert.That(PickComponent(run, "木"), Is.True);
            Assert.That(PickComponent(run, "火"), Is.False); // 部件额度用完
            Assert.That(run.Phase, Is.EqualTo(RunPhase.InBattle)); // 双排取满自动开拔
            Assert.That(run.Battle.Library, Does.Contain(first));
            Assert.That(run.Battle.Pool, Does.Contain("木"));
        }

        // ---- 字奖励按稀有度加权(2026-07-20 拍板:绿 80% / 蓝 15% / 紫 5%) ----

        /// <summary>稀有度测试图谱:每档 3 个字,配方都是 木+木(合法即可,战斗不用)。</summary>
        private static RecipeGraph RarityGraph()
        {
            var defs = new System.Collections.Generic.List<CharDef>
            {
                new("木", Element.Wood),
                new("火", Element.Fire, effects: new[] { new EffectDef(EffectKind.DamageSingle, 4) }),
                new("焚", Element.Fire, new[] { "木", "火" }, apCost: 2,
                    effects: new[] { new EffectDef(EffectKind.DamageAll, 18) }),
            };
            foreach (var (prefix, rarity) in new[]
                     { ("绿", CardRarity.Green), ("蓝", CardRarity.Blue), ("紫", CardRarity.Purple) })
                for (int i = 1; i <= 3; i++)
                    defs.Add(new CharDef($"{prefix}{i}", Element.Wood, new[] { "木", "木" }, rarity: rarity));
            return new RecipeGraph(defs);
        }

        private static readonly string[] RarityPool =
            { "绿1", "绿2", "绿3", "蓝1", "蓝2", "蓝3", "紫1", "紫2", "紫3" };

        private static RunEngine RarityRun(int seed, string[] pool = null) => new(RarityGraph(),
            new RunConfig
            {
                Encounters = new[] { new[] { Weak() }, new[] { Weak() } },
                RewardPool = pool ?? RarityPool,
            },
            new BattleConfig { DropTable = new[] { "木" } },
            startingLibrary: new[] { "焚" }, startingPool: Array.Empty<string>(), seed: seed);

        [Test]
        public void Reward_RarityWeights_GreenDominatesBlueBeatsPurple()
        {
            int green = 0, blue = 0, purple = 0;
            for (int seed = 0; seed < 400; seed++)
            {
                var run = RarityRun(seed);
                WinCurrentBattle(run);
                run.AdvanceAfterBattle();
                switch (run.RewardOptions[0][0]) // 首个抽出的字:每次都是全新加权抽取
                {
                    case '绿': green++; break;
                    case '蓝': blue++; break;
                    case '紫': purple++; break;
                }
            }
            Assert.That(green + blue + purple, Is.EqualTo(400));
            Assert.That(green, Is.GreaterThan(blue));   // 80% vs 15%
            Assert.That(blue, Is.GreaterThan(purple));  // 15% vs 5%
            Assert.That(green, Is.GreaterThan(240));    // 期望 320,留足抽样余量
            Assert.That(purple, Is.LessThan(80));       // 期望 20
        }

        [Test]
        public void Reward_OnlyDrawsFromGivenPool() // 池 = 出阵列表,外面的字不该冒出来
        {
            var run = RarityRun(seed: 3, pool: new[] { "蓝1", "蓝2" });
            WinCurrentBattle(run);
            run.AdvanceAfterBattle();
            Assert.That(run.RewardOptions, Is.EquivalentTo(new[] { "蓝1", "蓝2" })); // 池小于 5 则全给,不重复
        }

        [Test]
        public void Reward_SkipsComponents() // 部件不是奖励字(靠每回合掉落)
        {
            var run = RarityRun(seed: 5, pool: new[] { "木", "火", "绿1" });
            WinCurrentBattle(run);
            run.AdvanceAfterBattle();
            Assert.That(run.RewardOptions, Is.EqualTo(new[] { "绿1" }));
        }

        [Test]
        public void Reward_DeterministicBySeed_WithRarityWeights()
        {
            var a = RarityRun(seed: 42);
            var b = RarityRun(seed: 42);
            WinCurrentBattle(a); a.AdvanceAfterBattle();
            WinCurrentBattle(b); b.AdvanceAfterBattle();
            Assert.That(a.RewardOptions, Is.EqualTo(b.RewardOptions));
        }

        // ---- 池满替换部件(2026-07-20 拍板:字与部件都要能替换,不能只能放弃) ----

        [Test]
        public void PickRewardComponentReplacing_SwapsPoolSlot()
        {
            var run = RunWith(new BattleConfig { PoolCapacity = 2, DropTable = new[] { "木" } },
                new[] { "焚" }, SixCharPool(), pool: new[] { "木", "丁" }); // 池已满
            WinCurrentBattle(run);
            run.AdvanceAfterBattle();

            int fireIndex = run.ComponentOptions.ToList().IndexOf("火");
            Assert.That(run.PickRewardComponent(fireIndex), Is.False);          // 满池,常规取用被拒
            Assert.That(run.PickRewardComponentReplacing(fireIndex, 1), Is.True); // 换掉「丁」
            Assert.That(run.CarriedPool, Is.EquivalentTo(new[] { "木", "火" }));
            Assert.That(run.ComponentPicksLeft, Is.EqualTo(0));
        }

        [Test]
        public void PickRewardComponentReplacing_ValidatesIndexAndQuota()
        {
            var run = RunWith(new BattleConfig { PoolCapacity = 2, DropTable = new[] { "木" } },
                new[] { "焚" }, SixCharPool(), pool: new[] { "木", "丁" });
            WinCurrentBattle(run);
            run.AdvanceAfterBattle();

            Assert.That(run.PickRewardComponentReplacing(0, -1), Is.False); // 越界
            Assert.That(run.PickRewardComponentReplacing(0, 9), Is.False);
            Assert.That(run.CarriedPool, Is.EquivalentTo(new[] { "木", "丁" })); // 失败不动状态

            Assert.That(run.PickRewardComponentReplacing(0, 0), Is.True);
            Assert.That(run.PickRewardComponentReplacing(0, 0), Is.False); // 额度已用尽
        }

        [Test]
        public void PickRewardComponent_PoolFull_Rejected()
        {
            var run = RunWith(new BattleConfig { PoolCapacity = 2, DropTable = new[] { "木" } },
                new[] { "焚" }, SixCharPool(), pool: new[] { "木", "木" }); // 池已满
            WinCurrentBattle(run); // 焚 AOE 取胜,池未动
            run.AdvanceAfterBattle();
            Assert.That(PickComponent(run, "火"), Is.False); // 池满不收
            run.SkipReward();
            Assert.That(run.Battle.Pool.Count, Is.LessThanOrEqualTo(2));
        }

        /// <summary>挂起续爬后广告扩容仍在(2026-07-22 排查):标志过存档、重放要真的把容量抬回来。
        /// 标志 round-trip 与「扩容影响当前战斗」各有测试,这里把两半串成端到端,锁住恢复口径。</summary>
        [Test]
        public void ExpandFlags_SurviveSaveRoundTrip_AndRestoreCapacity()
        {
            var run = Run();
            run.TryExpandLibrary();
            run.TryExpandPool();

            // 模拟 OnExpanded 落盘 + 挂起后重新读档
            var meta = new MetaState
            {
                Endless = new EndlessSaveState
                {
                    Depth = 3, PlayerHp = 30, Seed = 42,
                    LibraryExpanded = run.LibraryExpanded,
                    PoolExpanded = run.PoolExpanded,
                },
            };
            var restored = Data.SaveSerializer.FromJson(Data.SaveSerializer.ToJson(meta));

            // 模拟 StartSegment:新 run(容量回落到基准)后重放扩容
            var resumed = Run();
            Assert.That(resumed.Battle.LibraryCapacity, Is.EqualTo(6)); // 重放前是基准值
            if (restored.Endless.LibraryExpanded) resumed.TryExpandLibrary();
            if (restored.Endless.PoolExpanded) resumed.TryExpandPool();

            Assert.That(resumed.Battle.LibraryCapacity, Is.EqualTo(8));
            Assert.That(resumed.Battle.PoolCapacity, Is.EqualTo(12));
        }

        [Test]
        public void ExpandPool_OncePerRun_RaisesCapBy2()
        {
            var run = Run();
            Assert.That(run.TryExpandPool(), Is.True);
            Assert.That(run.TryExpandPool(), Is.False); // 每关一次

            // 扩容后池上限 12;焚拆解只有部件『火』回池(林回库),11 个 +1 = 12 恰好允许
            var pool = new System.Collections.Generic.List<string>();
            for (int i = 0; i < 11; i++) pool.Add("木");
            var result = ForgeEngine.TryDismantle("焚", Graph(),
                new ForgeState(new[] { "焚" }, pool), 12, 8);
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

        /// <summary>池部件直出取胜:字库原封不动,便于构造满库场景。</summary>
        private static void WinByPoolCast(RunEngine run)
        {
            var error = run.Battle.Cast("火"); // 单敌免选,火胜弱怪
            Assert.That(error, Is.EqualTo(BattleError.None));
            Assert.That(run.Battle.Phase, Is.EqualTo(BattlePhase.Won));
        }

        [Test]
        public void PickReward_AtCapacity_Rejected_StaysInReward()
        {
            var run = RunWith(new BattleConfig { LibraryCapacity = 1, DropTable = new[] { "木" } },
                new[] { "灯" }, pool: new[] { "火" });
            WinByPoolCast(run); // 字库 [灯] 未动 = 满(上限 1)
            run.AdvanceAfterBattle();
            Assert.That(run.CarriedLibrary, Is.EquivalentTo(new[] { "灯" }));
            Assert.That(run.PickReward(0), Is.False);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Reward)); // 停在奖励页等替换/跳过
        }

        [Test]
        public void PickRewardReplacing_SwapsAndProceeds()
        {
            var run = RunWith(new BattleConfig { LibraryCapacity = 1, DropTable = new[] { "木" } },
                new[] { "灯" }, pool: new[] { "火" });
            WinByPoolCast(run);
            run.AdvanceAfterBattle();
            var picked = run.RewardOptions[0];

            Assert.That(run.PickRewardReplacing(0, 0), Is.True); // 换掉「灯」
            run.SkipReward();
            Assert.That(run.Phase, Is.EqualTo(RunPhase.InBattle));
            Assert.That(run.Battle.Library, Is.EquivalentTo(new[] { picked })); // 不超上限
            Assert.That(run.Battle.Library, Does.Not.Contain("灯")); // 被替换的字永久移除
        }

        [Test]
        public void SkipReward_AtCapacity_StillWorks() // 「选择不要」
        {
            var run = RunWith(new BattleConfig { LibraryCapacity = 1, DropTable = new[] { "木" } },
                new[] { "灯" }, pool: new[] { "火" });
            WinByPoolCast(run);
            run.AdvanceAfterBattle();
            run.SkipReward();
            Assert.That(run.Phase, Is.EqualTo(RunPhase.InBattle));
            Assert.That(run.Battle.Library, Is.EquivalentTo(new[] { "灯" }));
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
                new[] { "灯" }, config, pool: new[] { "火" });
            WinByPoolCast(run); // 字库 [灯] 未动 = 满
            run.AdvanceAfterBattle();
            run.SkipReward(); // 100% 进奇遇
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Event));
            Assert.That(run.ChooseEventOption(0), Is.False); // 字库已满,收不下
            Assert.That(run.ChooseEventOption(1), Is.True);  // 离开可选
            Assert.That(run.Battle.Library, Is.EquivalentTo(new[] { "灯" }));
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
        public void Compose_AfterCast_FreesLibrarySlot() // 出字即消耗(v0.7):出手后容量位释放
        {
            var engine = new BattleEngine(Graph(),
                new BattleConfig { LibraryCapacity = 1, DropTable = new[] { "木" } },
                new[] { "灯" }, new[] { "木", "木" }, new[] { Strong() }, seed: 1);
            Assert.That(engine.Cast("灯", 0), Is.EqualTo(BattleError.None)); // 灯消耗,库空
            Assert.That(engine.Compose("林"), Is.EqualTo(BattleError.None)); // 空位可再合
            Assert.That(engine.Library, Is.EquivalentTo(new[] { "林" }));
        }

        [Test]
        public void Shield_CarriesToNextBattle() // 段内持久:护盾跨层保留
        {
            var run = new RunEngine(Graph(), TwoBattles(),
                new BattleConfig { DropTable = new[] { "木" } },
                startingLibrary: new[] { "焚" }, startingPool: Array.Empty<string>(), seed: 7,
                cardLevels: null, startingInk: 0, startingHp: null,
                startingNormalShield: 5);
            Assert.That(run.Battle.PlayerShield, Is.EqualTo(5));
            WinCurrentBattle(run);            // 焚一发清场(不 EndTurn,护盾不变)
            run.AdvanceAfterBattle();
            run.SkipReward();                 // 进入第二场
            Assert.That(run.Phase, Is.EqualTo(RunPhase.InBattle));
            Assert.That(run.Battle.PlayerShield, Is.EqualTo(5)); // 护盾跨场保留
            Assert.That(run.CarriedNormalShield, Is.EqualTo(5));
        }

        [Test]
        public void Shield_PerFloor_AddsEachBattle() // 金汤每关补盾:叠加上关剩余
        {
            var run = new RunEngine(Graph(), TwoBattles(),
                new BattleConfig { DropTable = new[] { "木" } },
                startingLibrary: new[] { "焚" }, startingPool: Array.Empty<string>(), seed: 7,
                cardLevels: null, startingInk: 0, startingHp: null,
                startingNormalShield: 5, startingPersistShield: 0, perFloorNormalShield: 2);
            Assert.That(run.Battle.PlayerShield, Is.EqualTo(5)); // 第 1 关:段首注入,不重复加
            WinCurrentBattle(run);            // 焚一发清场(不 EndTurn,护盾不变)
            run.AdvanceAfterBattle();
            run.SkipReward();                 // 进入第二关
            Assert.That(run.Battle.PlayerShield, Is.EqualTo(7)); // 上关剩 5 + 每关 2
        }
    }
}
