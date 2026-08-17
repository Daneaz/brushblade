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
            new CharDef("焚", Element.Fire, new[] { "林", "火" }, rarity: CardRarity.Purple,
                effects: new[] { new EffectDef(EffectKind.DamageAll, 18), new EffectDef(EffectKind.BurnAll, 1) }),
            new CharDef("灯", Element.Fire, new[] { "火", "丁" },
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 6), new EffectDef(EffectKind.BurnSingle, 1) }),
            new CharDef("丁", null),
            new CharDef("铠", Element.Metal,
                effects: new[] { new EffectDef(EffectKind.DefenseBuff, 12) }),
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
        public void Reward_NormalBattle_PicksTwoChars()
        {
            var run = Run();
            WinCurrentBattle(run);
            run.AdvanceAfterBattle();

            Assert.That(run.Phase, Is.EqualTo(RunPhase.Reward));
            Assert.That(run.CharPicksLeft, Is.EqualTo(2));
            // 简报断言候选数=5(RewardOptionCount 上限),但默认 Run() 走 TwoBattles() 的奖池,
            // 全图仅 3 个非叶子字(灯/焚/林),候选枯竭即停——与上面
            // Won_Advance_EntersReward_WithThreeOptionsFromPool 同一口径,这里改断言实际值 3。
            Assert.That(run.RewardOptions.Count, Is.EqualTo(3));
        }

        // ---- 层记账基准(2026-07-27):外层靠它算「刚打完第几层」并推进断点快照 ----

        [Test]
        public void ClearedBattleIndex_StaysOnFinishedFloor_AfterNextBattleBegins()
        {
            var run = Run();
            WinCurrentBattle(run);
            run.AdvanceAfterBattle();
            Assert.That(run.ClearedBattleIndex, Is.EqualTo(0)); // 刚打完第 1 场

            run.SkipReward();                                  // 取完战利品 → 直接开下一战
            Assert.That(run.BattleIndex, Is.EqualTo(1));       // 已在第 2 场
            Assert.That(run.ClearedBattleIndex, Is.EqualTo(0)); // 但记账基准仍是刚打完的第 1 场
        }

        [Test]
        public void ClearedBattleIndex_Advances_OnlyAfterNextWin()
        {
            // 出字即消耗,备一张焚才打得过第二场
            var run = new RunEngine(Graph(), TwoBattles(), new BattleConfig { DropTable = new[] { "木" } },
                startingLibrary: new[] { "焚", "焚" }, startingPool: Array.Empty<string>(), seed: 7);
            WinCurrentBattle(run);
            run.AdvanceAfterBattle();
            run.SkipReward();
            WinCurrentBattle(run);
            run.AdvanceAfterBattle();
            Assert.That(run.ClearedBattleIndex, Is.EqualTo(1)); // 打完第 2 场才推进
        }

        [Test]
        public void ClearedBattleIndex_StartsBeforeFirstFloor() // 一层未清时不能指向第 0 层
        {
            Assert.That(Run().ClearedBattleIndex, Is.EqualTo(-1));
        }

        [Test]
        public void PickReward_AddsChar_CastCharConsumed()
        {
            // 部件池起手给「木」(2026-08-04 复审修正):掉字改造后回合不再自动掉部件入池,
            // 空池→空池的断言验证不了「部件池保留」,必须真的放东西进去才有得测
            var run = new RunEngine(Graph(), TwoBattles(), new BattleConfig { DropTable = new[] { "木" } },
                startingLibrary: new[] { "焚" }, startingPool: new[] { "木" }, seed: 7);
            WinCurrentBattle(run); // 焚出手即消耗(v0.7 拍板,无回归);焚走库不碰池,池里的木不受影响
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
            Assert.That(run.Battle.Pool, Does.Contain("木")); // 部件池保留(3.8.2):跨战斗延续,不被清空
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
            Assert.That(run.ReviveRoundsLeft, Is.EqualTo(2));
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
        }

        [Test]
        public void ReviveReward_PicksExhausted_ResumesBattle()
        {
            var run = LostRun();
            run.TryRevive();
            run.PickReviveChar(0);
            run.PickReviveChar(0);                 // 第一轮 2 次用尽 → 自动开第二轮
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Reviving));
            Assert.That(run.ReviveRoundsLeft, Is.EqualTo(1));

            run.PickReviveChar(0);
            run.PickReviveChar(0);                 // 第二轮 2 次用尽 → 轮次耗尽,接着打
            Assert.That(run.Phase, Is.EqualTo(RunPhase.InBattle));
            Assert.That(run.Battle.Phase, Is.EqualTo(BattlePhase.PlayerTurn));
        }

        // ---- 满库复活走替换(2026-08-04):此前满库时补给静默归零,广告白看 ----

        /// <summary>字库塞满的败北局:复活补给无处可放,只能走替换。</summary>
        private static RunEngine FullLibraryLostRun()
        {
            var run = new RunEngine(Graph(),
                new RunConfig
                {
                    Encounters = new[] { new[] { Strong() } },
                    RewardPool = new[] { "灯", "焚", "林" },
                },
                new BattleConfig { LibraryCapacity = 2 },
                startingLibrary: new[] { "焚", "焚" }, // 2/2 满
                startingPool: Array.Empty<string>(), seed: 7);
            run.Battle.EndTurn();
            Assert.That(run.Battle.Phase, Is.EqualTo(BattlePhase.Lost));
            run.TryRevive();
            return run;
        }

        [Test]
        public void PickReviveChar_FullLibrary_RejectedSoUiCanOfferReplace()
        {
            var run = FullLibraryLostRun();
            Assert.That(run.PickReviveChar(0), Is.False);   // 直接取放不下
            Assert.That(run.ReviveCharPicksLeft, Is.EqualTo(2)); // 额度不能被吞
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Reviving));
        }

        [Test]
        public void PickReviveCharReplacing_SwapsLibrarySlot()
        {
            var run = FullLibraryLostRun();
            string incoming = run.RewardOptions[0];

            Assert.That(run.PickReviveCharReplacing(0, 0), Is.True);

            Assert.That(run.Battle.Library[0], Is.EqualTo(incoming)); // 换进指定槽位
            Assert.That(run.Battle.Library.Count, Is.EqualTo(2));     // 容量没被撑破
            Assert.That(run.ReviveCharPicksLeft, Is.EqualTo(1));
            Assert.That(run.RewardOptions, Does.Not.Contain(incoming)); // 候选取走
        }

        [Test]
        public void PickReviveCharReplacing_OutOfRange_Rejected()
        {
            var run = FullLibraryLostRun();
            Assert.That(run.PickReviveCharReplacing(0, 9), Is.False);
            Assert.That(run.ReviveCharPicksLeft, Is.EqualTo(2)); // 额度不受损
        }

        [Test]
        public void SkipReviveReward_ResumesBattleImmediately()
        {
            var run = LostRun();
            run.TryRevive();
            run.SkipReviveReward();
            Assert.That(run.Phase, Is.EqualTo(RunPhase.InBattle));
        }

        // 2026-08-16 CTB 改造:原本"一次 EndTurn 就会再被打死"依赖旧的判负时序 bug——旧实现里
        // 判负推迟到下一次 BeginPlayerTurn 才补判,期间会多跑一次 AdvanceOnce()(玩家已摸满、
        // 不需要推进),顺带把玩家的计量器消费掉。新实现里 DamagePlayerDirect 扣血的那一刻
        // 立刻标记 Phase=Lost、循环当场停止——那次"额外消费玩家计量器"的动作不会再发生,
        // 玩家阵亡瞬间自己的计量器仍是"摸满未消费"状态。Revive() 不重置调度器(spec §4.3.1
        // 明确要求),这个"摸满未消费"的计量器原样带进复活后的第一次 EndTurn()——YieldTurn
        // 之后第一次 AdvanceOnce 一看玩家已摸满,不需要任何推进就直接判给玩家,于是又跑了
        // 一次 BeginPlayerTurn()(给一轮新 AP),敌人的计量器纹丝未动,这一次 EndTurn 内
        // 根本没机会出手。这不是 bug——玩家没死时也是同样节奏(敌人打完一拳 meter 归 0、
        // 玩家满格→下一次 AdvanceOnce 轮到玩家);已经 controller 复核裁定为正确行为(见
        // task-12-red-list.md),不采纳"在 Revive() 里钳位玩家计量器"的建议(那会重新引入
        // 特例,且会让玩家白白多等本已攒满的一拍)。断言从"1 次 EndTurn"改成"再多打 1 次
        // EndTurn"才会被 Strong 打死——多出的这一次是玩家的"复活赠送拍",敌人紧随其后的
        // 那一拍才是真正的出手;测试真正要守的"只能复活一次"结论不变。
        [Test]
        public void Revive_OnlyOncePerRun()
        {
            var run = LostRun();
            run.TryRevive();
            run.SkipReviveReward();

            run.Battle.EndTurn(); // 复活赠送的这一拍:玩家计量器摸满未消费,又轮到玩家一次,敌人还没出手
            Assert.That(run.Battle.Phase, Is.EqualTo(BattlePhase.PlayerTurn));

            run.Battle.EndTurn(); // 这一拍敌人才真正出手,又被 Strong 打死
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
        public void Revive_TwoRounds_YieldsFourChars()
        {
            var run = LostRun();
            Assert.That(run.TryRevive(), Is.True);
            Assert.That(run.ReviveCharPicksLeft, Is.EqualTo(2));
            Assert.That(run.ReviveRoundsLeft, Is.EqualTo(2));

            run.PickReviveChar(0);
            run.PickReviveChar(0);

            // 第一轮取尽 → 自动开第二轮,候选重新抽满。简报断言候选数=5(RewardOptionCount 上限),
            // 但 LostRun() 走的奖池(灯/焚/林)全图仅 3 个非叶子字,候选枯竭即停——与
            // Reward_NormalBattle_PicksTwoChars 同一口径,这里改断言实际值 3。
            Assert.That(run.ReviveRoundsLeft, Is.EqualTo(1));
            Assert.That(run.ReviveCharPicksLeft, Is.EqualTo(2));
            Assert.That(run.RewardOptions.Count, Is.EqualTo(3));
        }

        [Test]
        public void ReviveRounds_SurviveRoundTrip() // 轮次丢了 = 续爬后第二轮凭空消失
        {
            var run = LostRun();
            run.TryRevive();
            run.PickReviveChar(0);

            var restored = RunEngine.Restore(run.Capture(), Graph(),
                new RunConfig
                {
                    Encounters = new[] { new[] { Strong() } },
                    RewardPool = new[] { "灯", "焚", "林" },
                },
                new BattleConfig { DropTable = new[] { "木" } }, null, 0, 0);

            Assert.That(restored.ReviveRoundsLeft, Is.EqualTo(2));
            Assert.That(restored.ReviveCharPicksLeft, Is.EqualTo(1));
        }

        // 2026-08-16 CTB 改造:原名/原注释("跨任务问题,Task 1 遗留")依据的是 Battle.Revive()
        // 内部调用 StartTurn() 这一旧实现——spec §4.3.1 已把这句调用整个删除("复活=满血站起来,
        // 时间轴原地继续,不重置调度器、不跳过剩余行动者、不补任何递减"),复活因此不再触发
        // 任何回合起始副作用(回合掉字包含在内),"复活撞库进 DropChoice"这条路径不复存在
        // (同一因果链见 Revive_FullLibrary_DoesNotTriggerDropChoice)。这条测试原本要守护的
        // 更深一层不变量——RunPhase 与 Battle.Phase 是两条独立状态机,表现层不能凭 RunPhase
        // 推断 Battle 侧状态——依然成立,只是不再能用"复活撞库"这个具体场景来构造双非常态
        // 叠加了。改名 + 改断言:验证复活后 Battle.Phase 干净地回到 PlayerTurn,库容维持
        // 死亡前的状态,不被复活动作本身触发掉字判定。
        /// <summary>Revive 不再触发 StartTurn,不会把 Battle 打进 DropChoice(spec §4.3.1)。</summary>
        [Test]
        public void Revive_WithFullLibrary_ReturnsBattleDirectlyToPlayerTurn()
        {
            var config = new RunConfig
            {
                Encounters = new[] { new[] { Strong() } },
                RewardPool = new[] { "灯", "焚", "林" },
            };
            var battleConfig = new BattleConfig { DropTable = new[] { "木" }, UnlockedChars = new[] { "木" } };
            var almostFullLibrary = new[] { "焚", "木", "木", "木", "木" };
            var run = new RunEngine(Graph(), config, battleConfig,
                startingLibrary: almostFullLibrary, startingPool: Array.Empty<string>(), seed: 7);
            Assert.That(run.Battle.Phase, Is.EqualTo(BattlePhase.PlayerTurn)); // 第 1 回合掉字没撞满库
            Assert.That(run.Battle.Library.Count, Is.EqualTo(6));

            run.Battle.EndTurn();
            Assert.That(run.Battle.Phase, Is.EqualTo(BattlePhase.Lost));

            Assert.That(run.TryRevive(), Is.True);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Reviving));
            Assert.That(run.Battle.Phase, Is.EqualTo(BattlePhase.PlayerTurn), "Revive 不再触发 StartTurn,不会撞进 DropChoice");
            Assert.That(run.Battle.Library.Count, Is.EqualTo(6), "库容维持复活前的满员状态,不受复活动作影响");

            run.SkipReviveReward();
            Assert.That(run.Phase, Is.EqualTo(RunPhase.InBattle));
            Assert.That(run.Battle.Phase, Is.EqualTo(BattlePhase.PlayerTurn)); // Battle 侧不受 RunPhase 切换影响
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
        public void Defaults_Library6_Pool10_Drops1()
        {
            var config = new BattleConfig();
            Assert.That(config.LibraryCapacity, Is.EqualTo(6));
            Assert.That(config.PoolCapacity, Is.EqualTo(10));
            Assert.That(config.DropsPerTurn, Is.EqualTo(1)); // 掉部件→掉字(2026-08-04:2→1)
        }

        // ---- 战利品字排 5 选 2(2026-08-04 拍板:部件那一路整个删掉,五行部件改为只能靠拆字获得);
        //      Boss 层同样发战利品(2026-07-20) ----

        /// <summary>池含 3 个字 + 3 个部件:部件不作为字奖励(2026-07-20),故字排只出 3 个。</summary>
        private static RunConfig SixCharPool() => new()
        {
            Encounters = new[] { new[] { Weak() }, new[] { Weak() } },
            RewardPool = new[] { "灯", "焚", "林", "木", "火", "丁" },
        };

        [Test]
        public void Reward_RollsCharsFromPool_SkipsLeaves()
        {
            var run = Run(SixCharPool());
            WinCurrentBattle(run);
            run.AdvanceAfterBattle();
            Assert.That(run.RewardOptions, Is.EquivalentTo(new[] { "灯", "焚", "林" })); // 部件不入字排
            Assert.That(run.CharPicksLeft, Is.EqualTo(2));
        }

        [Test]
        public void PickBothChars_AutoProceeds()
        {
            var run = Run(SixCharPool());
            WinCurrentBattle(run);
            run.AdvanceAfterBattle();

            var first = run.RewardOptions[0];
            Assert.That(run.PickReward(0), Is.True);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Reward)); // 还差一次额度,尚未开拔

            var second = run.RewardOptions[0];
            Assert.That(run.PickReward(0), Is.True);
            Assert.That(run.PickReward(0), Is.False); // 字额度用完(5 选 2)
            Assert.That(run.Phase, Is.EqualTo(RunPhase.InBattle)); // 双次取满自动开拔
            Assert.That(run.Battle.Library, Does.Contain(first));
            Assert.That(run.Battle.Library, Does.Contain(second));
        }

        // ---- 字奖励按稀有度加权(2026-07-20 拍板:绿 80% / 蓝 15% / 紫 5%) ----

        /// <summary>稀有度测试图谱:每档 3 个字,配方都是 木+木(合法即可,战斗不用)。</summary>
        private static RecipeGraph RarityGraph()
        {
            var defs = new System.Collections.Generic.List<CharDef>
            {
                new("木", Element.Wood),
                new("火", Element.Fire, effects: new[] { new EffectDef(EffectKind.DamageSingle, 4) }),
                new("焚", Element.Fire, new[] { "木", "火" }, rarity: CardRarity.Purple,
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
                EndlessV2 = new EndlessSaveState
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
            if (restored.EndlessV2.LibraryExpanded) resumed.TryExpandLibrary();
            if (restored.EndlessV2.PoolExpanded) resumed.TryExpandPool();

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

        [Test]
        public void DefenseBuff_CarriesToNextBattle() // 段内持久:护甲增益跨层保留(与护盾同口径)
        {
            var run = new RunEngine(Graph(), TwoBattles(),
                new BattleConfig { DropTable = new[] { "木" } },
                startingLibrary: new[] { "焚", "铠" }, startingPool: Array.Empty<string>(), seed: 7);
            run.Battle.Cast("铠");
            Assert.That(run.Battle.EffectivePlayerDefense, Is.EqualTo(12));
            WinCurrentBattle(run);            // 焚一发清场(不 EndTurn,护甲增益不变)
            run.AdvanceAfterBattle();
            Assert.That(run.CarriedStatuses.First(s => s.SourceId == "铠").Magnitude, Is.EqualTo(12));
            run.SkipReward();                 // 进入第二关
            Assert.That(run.Phase, Is.EqualTo(RunPhase.InBattle));
            Assert.That(run.Battle.EffectivePlayerDefense, Is.EqualTo(12), "护甲增益跨场保留");
        }

        // ---- 召唤物跨战斗保留(2026-08-03 拍板):与普通盾同口径全程延续,直到死亡 ----

        /// <summary>召唤专用字表:「林」召 2 只 1 血木偶(好打死),「森」召 4 只(打满上限),
        /// 「焚」AOE 清场。不动上面的 Graph(),免得牵动既有用例。</summary>
        private static RecipeGraph SummonGraph() => new(new[]
        {
            new CharDef("木", Element.Wood),
            new CharDef("火", Element.Fire),
            new CharDef("林", Element.Wood, new[] { "木", "木" },
                effects: new[] { new EffectDef(EffectKind.Summon, 1, summonCount: 2, summonAttack: 2, summonChar: "木") }),
            new CharDef("森", Element.Wood, new[] { "林", "木" },
                effects: new[] { new EffectDef(EffectKind.Summon, 6, summonCount: 4, summonAttack: 2, summonChar: "木") }),
            new CharDef("焚", Element.Fire, new[] { "林", "火" }, rarity: CardRarity.Purple,
                effects: new[] { new EffectDef(EffectKind.DamageAll, 18) }),
        });

        /// <summary>两层同一只敌人的召唤物专用跑图;<paramref name="secondEnemy"/> 可单独指定
        /// 第二层的敌人(2026-08-17:携带的满格召唤物会在开场把弱敌直接秒了,验「满编阻断」
        /// 需要一只挨得住开场那几下的)。</summary>
        private static RunEngine SummonRun(EnemyDef enemy, string[] library, EnemyDef secondEnemy = null) =>
            new(SummonGraph(),
                new RunConfig
                {
                    Encounters = new[] { new[] { enemy }, new[] { secondEnemy ?? enemy } },
                    RewardPool = new[] { "焚" },
                },
                new BattleConfig { DropTable = new[] { "木" } },
                startingLibrary: library, startingPool: Array.Empty<string>(), seed: 7);

        [Test]
        public void Summons_SurviveIntoNextBattle_WithCurrentHp()
        {
            var run = SummonRun(Weak(), new[] { "森", "焚" });
            Assert.That(run.Battle.Cast("森"), Is.EqualTo(BattleError.None)); // 1 AP:4 只 6 血木偶
            Assert.That(run.Battle.Cast("焚"), Is.EqualTo(BattleError.None)); // 2 AP:AOE 火克木秒清
            Assert.That(run.Battle.Phase, Is.EqualTo(BattlePhase.Won));

            run.AdvanceAfterBattle();                                        // 抓取携带态就在这一步
            Assert.That(run.CarriedSummons.Count, Is.EqualTo(4));
            run.SkipReward();                                                // 开下一层

            Assert.That(run.Battle.Summons.Count, Is.EqualTo(4));
            Assert.That(run.Battle.AliveSummonCount, Is.EqualTo(4));
            var carried = run.Battle.Summons[0];
            Assert.That(carried.Char, Is.EqualTo("木"));
            Assert.That(carried.Element, Is.EqualTo(Element.Wood));
            Assert.That(carried.Hp, Is.EqualTo(6));
            Assert.That(carried.MaxHp, Is.EqualTo(6));
            Assert.That(carried.Attack, Is.EqualTo(2));
        }

        [Test]
        public void DeadSummons_AreNotCarried_AndSlotsRepack()
        {
            // 敌人 20 血:够挨一轮召唤物反击(2 只×2)还活着,好让敌方回合打死首只木偶
            var run = SummonRun(new EnemyDef("枯", Element.Wood, 20, 2), new[] { "林", "焚" });
            Assert.That(run.Battle.Cast("林"), Is.EqualTo(BattleError.None)); // 2 只 1 血木偶
            Assert.That(run.Battle.AliveSummonCount, Is.EqualTo(2));

            run.Battle.EndTurn();       // 回合末反击 → 敌方回合整次攻击由首只承受(2 伤 > 1 血)
            Assert.That(run.Battle.AliveSummonCount, Is.EqualTo(1));

            Assert.That(run.Battle.Cast("焚"), Is.EqualTo(BattleError.None));
            Assert.That(run.Battle.Phase, Is.EqualTo(BattlePhase.Won));

            run.AdvanceAfterBattle();
            run.SkipReward();

            Assert.That(run.Battle.Summons.Count, Is.EqualTo(1), "死尸不带走,槽位从 0 号重排");
            Assert.That(run.Battle.Summons[0].Alive, Is.True);
            Assert.That(run.Battle.Summons[0].Hp, Is.EqualTo(1), "残血原样带走,不回满");
        }

        [Test]
        public void CarriedSummons_CountTowardCap()
        {
            // 第二层用 40 血的怪:携带的 6 只满格召唤物在开场推进里各打一下(2 伤 × 6 = 12),
            // 它得挨得住,否则战斗在构造函数里就结束了、Cast 一律回 BattleOver
            // (那条「开局即胜」的行为由 CarriedFullMeterSummons_WinNextBattleOnOpening 守,2026-08-17)。
            var run = SummonRun(Weak(), new[] { "森", "林", "焚", "林" },
                secondEnemy: new EnemyDef("枯", Element.Wood, 40, 2));
            Assert.That(run.Battle.Cast("森"), Is.EqualTo(BattleError.None)); // 4 只
            Assert.That(run.Battle.Cast("林"), Is.EqualTo(BattleError.None)); // +2 只 = 6 只满编
            Assert.That(run.Battle.Cast("焚"), Is.EqualTo(BattleError.None)); // AOE 秒敌人
            Assert.That(run.Battle.Phase, Is.EqualTo(BattlePhase.Won));      // 确认战斗真的赢了

            run.AdvanceAfterBattle();
            Assert.That(run.CarriedSummons.Count, Is.EqualTo(6), "应当真的执行了携带逻辑");
            run.SkipReward();

            // 新战斗中,检验带过来的 6 只召唤物确实占据了全部容量
            Assert.That(run.Battle.Phase, Is.EqualTo(BattlePhase.PlayerTurn), "这一层的怪挨得住开场");
            Assert.That(run.Battle.AliveSummonCount, Is.EqualTo(6));
            Assert.That(run.Battle.AliveSummonCount, Is.EqualTo(run.Battle.SummonCapacity));
            // 满编强阻断照旧生效:带过来的算进存活数
            Assert.That(run.Battle.Cast("林"), Is.EqualTo(BattleError.SummonCapFull));
            // 确认替换后仍不超上限
            Assert.That(run.Battle.Cast("林", replaceSummon: true), Is.EqualTo(BattleError.None));
            Assert.That(run.Battle.AliveSummonCount, Is.EqualTo(run.Battle.SummonCapacity));
        }

        [Test]
        public void CarriedSummons_StartNextBattleWithEmptyMeter()
        {
            // 行动计量器**每场战斗独立**(2026-08-18 用户报 bug):上一场攒的行动力不跨战斗。
            //
            // 这条曾经是反的:2026-08-17 把「携带满格召唤物进弱敌层会在构造函数里就打完」
            // 当成正确的 CTB 语义(攒满了就该动)、记进 spec §5.7 当「方案固有副作用」,
            // 还专门写了哨兵守它。那个判断是错的 —— 从玩法看,上一场攒的行动力让召唤物
            // 在下一场白送一次攻击,而玩家对此毫无操作空间。
            //
            // ⚠ 归零只在**跨战斗携带**这条路径上做(RunEngine.CaptureAliveSummons)。
            // 断点续爬(BattleEngine.Capture → BattleSnapshot.Summons)走的是同一个
            // SummonState.Capture(),那条**必须原样保留**计量器,否则读档会把节奏重置 ——
            // 由 ActionMeter_SurvivesRoundTrip_RhythmContinues 守着。改错层会同时破坏它。
            var run = SummonRun(Weak(), new[] { "森", "焚" });
            Assert.That(run.Battle.Cast("森"), Is.EqualTo(BattleError.None)); // 4 只召唤物
            Assert.That(run.Battle.Summons[0].ActionMeter, Is.EqualTo(TurnScheduler.Threshold),
                "本场内确实是满格的 —— 上场即满格那条口径不受本次影响");
            Assert.That(run.Battle.Cast("焚"), Is.EqualTo(BattleError.None)); // AOE 秒敌人
            run.AdvanceAfterBattle();

            // 携带态里就该已经归零,而不是等进了新战斗再清
            Assert.That(run.CarriedSummons.Count, Is.EqualTo(4));
            foreach (var carried in run.CarriedSummons)
                Assert.That(carried.ActionMeter, Is.EqualTo(0), "携带态不带行动力");

            run.SkipReward();  // 开下一层

            Assert.That(run.Battle.Phase, Is.EqualTo(BattlePhase.PlayerTurn),
                "召唤物计量器归零 → 开场没人抢先,弱敌活着等玩家出手");
            // 「没带行动力过来」的可观察证据是**开场只有玩家那一拍**,而不是「召唤物 meter 为 0」——
            // 构造函数的开场推进会把全场从 0 推到满格(行动者扣 100),所以构造**之后**召唤物
            // 必然读到 100。那 100 是这一场推出来的,不是上一场带来的;真正被修掉的是
            // 「它带着满格进场、于是抢在玩家之前先动」。
            Assert.That(run.Battle.OpeningSteps.Count, Is.EqualTo(1),
                "开场只推进一次,行动者是玩家");
            Assert.That(run.Battle.OpeningSteps[0].Actor, Is.EqualTo(ActorRef.Player),
                "同速下玩家 priority 最小 —— 召唤物若带着满格进场就会抢在它前面");
        }
    }
}
