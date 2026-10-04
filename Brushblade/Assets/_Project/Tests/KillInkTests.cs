using System;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>战斗内墨锭掉落(2026-09-30 用户拍板「墨锭随关卡深入增长」→ 击杀掉墨锭):
    /// 每杀一只按当前层结一份,赚到即入账(与层墨锭同一本 EarnedInk),Boss 一只顶五只。</summary>
    public class KillInkTests
    {
        private static RecipeGraph Graph() => new(new[]
        {
            new CharDef("木", Element.Wood),
            new CharDef("焚", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 18, shape: TargetArea.All) }),
            new CharDef("凿", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 18) }),
        });

        private static EnemyDef Weak() => new("枯", Element.Wood, 4, 0);

        private static RunEngine Run(bool killInk, int fromDepth = 1, params EnemyDef[] enemies) =>
            new(Graph(), new RunConfig
                {
                    Encounters = new[] { enemies.Length > 0 ? enemies : new[] { Weak(), Weak() }, new[] { Weak() } },
                    RewardPool = new[] { "焚" },
                    FromDepth = fromDepth,
                    KillInkDrops = killInk,
                },
                new BattleConfig { DropTable = new[] { "木" }, ApPerTurn = 10 },
                startingLibrary: new[] { "凿", "凿", "焚" }, startingPool: Array.Empty<string>(), seed: 7);

        [Test]
        public void KillInk_GrowsWithDepth_BossWorthFive()
        {
            Assert.That(EndlessRules.KillInk(1, isBoss: false), Is.EqualTo(1));
            Assert.That(EndlessRules.KillInk(9, isBoss: false), Is.EqualTo(1));
            Assert.That(EndlessRules.KillInk(10, isBoss: false), Is.EqualTo(2));
            Assert.That(EndlessRules.KillInk(50, isBoss: false), Is.EqualTo(6));
            Assert.That(EndlessRules.KillInk(5, isBoss: true), Is.EqualTo(5));
            Assert.That(EndlessRules.KillInk(50, isBoss: true), Is.EqualTo(30));
        }

        [Test]
        public void Sync_CreditsEachKillOnce_AtTheMomentItDies()
        {
            var run = Run(killInk: true, fromDepth: 12);   // 12 层:每只 2
            run.Battle.Cast("凿", 0);
            Assert.That(run.SyncKillInk(), Is.EqualTo(2), "杀一只结一份");
            Assert.That(run.EarnedInk, Is.EqualTo(2), "赚到即入账");
            Assert.That(run.SyncKillInk(), Is.EqualTo(0), "同一只不重复结");
            run.Battle.Cast("凿", 1);
            Assert.That(run.SyncKillInk(), Is.EqualTo(2));
            Assert.That(run.EarnedInk, Is.EqualTo(4));
        }

        [Test]
        public void KillInkFor_MatchesWhatSyncCredits()
        {
            var run = Run(killInk: true, fromDepth: 12);
            Assert.That(run.KillInkFor(0), Is.EqualTo(2), "表现层飘字与入账同一个数");
            Assert.That(run.KillInkFor(99), Is.EqualTo(0), "越界不抛");
            Assert.That(Run(killInk: false).KillInkFor(0), Is.EqualTo(0));
        }

        [Test]
        public void AdvanceAfterBattle_SettlesUnsyncedKills()
        {
            // 表现层没来得及同步(比如最后一击后直接结算)也不丢钱
            var run = Run(killInk: true);
            run.Battle.Cast("焚");
            run.AdvanceAfterBattle();
            Assert.That(run.EarnedInk, Is.EqualTo(2), "两只各 1");
        }

        [Test]
        public void NextBattle_StartsFreshLedger_AtItsOwnDepth()
        {
            var run = Run(killInk: true, fromDepth: 9);    // 第 9 层每只 1,第 10 层每只 2
            run.Battle.Cast("焚");
            run.AdvanceAfterBattle();
            for (int i = 0; i < 5 && run.Phase == RunPhase.Reward; i++) run.SkipReward();
            Assert.That(run.CurrentDepth, Is.EqualTo(10));
            run.Battle.Cast("凿", 0);
            run.SyncKillInk();
            Assert.That(run.EarnedInk, Is.EqualTo(2 + 2), "上一层 2 × 1,这一层 1 × 2");
        }

        [Test]
        public void Disabled_NoKillInk()
        {
            // 章节关卡与测试夹具不开:只有无尽层段(BuildSegment)打开它
            var run = Run(killInk: false);
            run.Battle.Cast("焚");
            Assert.That(run.SyncKillInk(), Is.EqualTo(0));
            run.AdvanceAfterBattle();
            Assert.That(run.EarnedInk, Is.EqualTo(0));
        }

        [Test]
        public void Snapshot_KeepsCreditedKills_NoDoublePayOnResume()
        {
            var run = Run(killInk: true);
            run.Battle.Cast("凿", 0);
            run.SyncKillInk();
            var snapshot = run.Capture();
            var restored = RunEngine.Restore(snapshot, Graph(), new RunConfig
                {
                    Encounters = new[] { new[] { Weak(), Weak() }, new[] { Weak() } },
                    RewardPool = new[] { "焚" }, KillInkDrops = true,
                },
                new BattleConfig { DropTable = new[] { "木" }, ApPerTurn = 10 }, cardLevels: null);
            Assert.That(restored.SyncKillInk(), Is.EqualTo(0), "恢复后那具尸体不再结一次");
            Assert.That(restored.EarnedInk, Is.EqualTo(1));
        }

        [Test]
        public void BuildSegment_TurnsKillInkOn()
        {
            var config = new EndlessConfig
            {
                Bands = new[] { new BandDef { Name = "字林", FromDepth = 1,
                    EnemyPool = new[] { Weak() }, BossPool = new[] { Weak() } } },
            };
            Assert.That(EndlessGenerator.BuildSegment(config, 1, seed: 1).KillInkDrops, Is.True);
        }
    }
}
