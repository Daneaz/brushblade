using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Balance.Bot;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Balance.Tests
{
    /// <summary>增益面蓄能分(spec 2026-10-16 §6 / Plan F A1):燃意、厚 / 泉 / 战意按「预计兑现伤害」记分。</summary>
    public class EvaluatorStoredValueTests
    {
        private const ulong TurnSeed = 0x5EEDF1UL;
        private static readonly EnemyDef Dummy = new EnemyDef("怔", Element.Heart, 10000, 0);

        private sealed class Scene
        {
            public readonly BattleConfig Config;
            public BattleEngine Battle;

            public Scene(string[] library, EnemyDef[] enemies = null, SummonSnapshot[] summons = null, int? ap = null)
            {
                Config = new BattleConfig
                {
                    PlayerMaxHp = 5000, ApPerTurn = 3, LibraryCapacity = 6, PoolCapacity = 10,
                    BossPhaseJitterPercent = 0, DropsPerTurn = 0,
                };
                Battle = new BattleEngine(Fixture.Graph, Config, library, Array.Empty<string>(),
                    enemies ?? new[] { Dummy }, seed: 11, startingSummons: summons);
                Assert.That(Battle.Phase, Is.EqualTo(BattlePhase.PlayerTurn), "夹具:开场应轮到玩家");
                if (ap.HasValue) Battle = With(x => x.Ap = ap.Value);
            }

            public BattleEngine With(Action<BattleSnapshot> edit)
            {
                var snap = Battle.Capture();
                edit(snap);
                return BattleEngine.Restore(snap, Fixture.Graph, Config, null, Evaluator.EnemyDefsOf(Battle));
            }

            public List<BotAction> Best() =>
                Search.BestTurn(Battle, Fixture.Graph, Config, null, SearchOptions.Fast, TurnSeed);
        }

        private static double Terms(BattleEngine b) => Evaluator.Terms(b, Fixture.Graph, null);

        private static StatusEffect Kindle(int percent, int turns) => new StatusEffect
        {
            Kind = StatusKind.Kindle, Polarity = StatusPolarity.Buff, Magnitude = percent, TurnsLeft = turns, Potency = 100,
        };

        private static StatusEffect Stack(StatusKind kind, int n) =>
            new StatusEffect { Kind = kind, Polarity = StatusPolarity.Buff, Magnitude = n, TurnsLeft = 99 };

        private static string Show(IEnumerable<BotAction> plan) => string.Join(" | ", plan);

        // ── 燃意 ─────────────────────────────────────────────

        [Test]
        public void PlayerKindle_ScoresAboveNone_AndMoreTurnsScoreHigher()
        {
            var s = new Scene(new[] { "炎", "炎", "炎" });
            double none = Terms(s.Battle);
            double t1 = Terms(s.With(x => x.PlayerStatuses.Add(Kindle(20, 1))));
            double t2 = Terms(s.With(x => x.PlayerStatuses.Add(Kindle(20, 2))));
            double t3 = Terms(s.With(x => x.PlayerStatuses.Add(Kindle(20, 3))));
            Assert.That(t2, Is.GreaterThan(none), "挂燃意(剩 2 回合)> 未挂");
            Assert.That(t2, Is.GreaterThan(t1), "剩余回合越多分越高");
            Assert.That(t3, Is.GreaterThan(t2), "剩余回合越多分越高");
        }

        [Test]
        public void SummonKindle_ScoresAboveNone_AndMoreTurnsScoreHigher()
        {
            SummonSnapshot Wood(StatusEffect k)
            {
                var w = Fixture.WoodSummon(0);
                if (k != null) w.Statuses.Add(k);
                return w;
            }
            double none = Terms(new Scene(new[] { "炎" }, summons: new[] { Wood(null) }).Battle);
            double t1 = Terms(new Scene(new[] { "炎" }, summons: new[] { Wood(Kindle(20, 1)) }).Battle);
            double t2 = Terms(new Scene(new[] { "炎" }, summons: new[] { Wood(Kindle(20, 2)) }).Battle);
            Assert.That(t2, Is.GreaterThan(none), "木灵挂燃意 > 未挂");
            Assert.That(t2, Is.GreaterThan(t1), "剩余回合越多分越高");
        }

        [Test]
        public void FastSearch_KeepsKindleFace_WhenNoKillThisTurn()
        {
            // 敌人 10000 血打不死;AP 2 够「热的燃意面 + 一张攻击面」,字库留着后续回合的攻击字
            var s = new Scene(new[] { "热", "炎", "炎", "炎", "炎", "炎" }, ap: 2);
            var plan = s.Best();
            bool kindle = plan.Any(a => a.Kind == ActionKind.Cast && !a.AttackMode
                                        && Fixture.Graph.TryGet(a.CharId, out var d)
                                        && d.Effects.Any(e => e.Kind == EffectKind.Kindle));
            Assert.That(kindle, Is.True, "最优序列含燃意面:" + Show(plan));
        }

        [Test]
        public void FastSearch_StillTakesTheKill_WhenLethalThisTurn()
        {
            // 两发炎攻击面(195 × 2)打得死 300 血的敌人:击杀优先于蓄能
            var frail = new EnemyDef("弱", Element.Heart, 300, 0);
            var s = new Scene(new[] { "热", "炎", "炎", "炎", "炎", "炎" }, enemies: new[] { frail }, ap: 2);
            var plan = s.Best();
            var copy = s.With(_ => { });
            foreach (var a in plan) if (copy.Phase == BattlePhase.PlayerTurn) a.ApplyTo(copy);
            Assert.That(copy.Phase, Is.EqualTo(BattlePhase.Won), "选了击杀序列:" + Show(plan));
        }

        // ── 厚 / 泉 / 战意 ─────────────────────────────────────

        [TestCase(StatusKind.Heft, "崩")]
        [TestCase(StatusKind.Wellspring, "溃")]
        [TestCase(StatusKind.Morale, "炎")]
        public void Stacks_ScoreIsMonotonicInStacks(StatusKind kind, string libraryChar)
        {
            var s = new Scene(new[] { "炎", libraryChar });
            double prev = Terms(s.Battle);
            for (int n = 1; n <= 5; n++)
            {
                int k = n;
                double cur = Terms(s.With(x => x.PlayerStatuses.Add(Stack(kind, k))));
                Assert.That(cur, Is.GreaterThan(prev), $"{kind} {n} 层 > {n - 1} 层");
                prev = cur;
            }
        }

        /// <summary>直伤 / 释放值与 offense 同口径:套卡等级缩放(MetaRules.ScaleEffectValue,Lv5 = ×1.24)。</summary>
        /// 用 炎 + 战意:炎 Lv1 / Lv5 折叠后直伤条目不变,比值只剩等级系数(崩 Lv5 会折入特性,比值被混淆)。
        [TestCase(StatusKind.Morale, "炎")]
        public void StoredValue_ScalesWithCardLevel(StatusKind kind, string ch)
        {
            var s = new Scene(new[] { ch });
            var b = s.With(x => x.PlayerStatuses.Add(Stack(kind, 3)));
            var lv5 = new Dictionary<string, int> { [ch] = 5 };
            double at1 = Evaluator.StoredValue(b, Fixture.Graph, null);
            double at5 = Evaluator.StoredValue(b, Fixture.Graph, lv5);
            Assert.That(at1, Is.GreaterThan(0));
            Assert.That(at5 / at1, Is.EqualTo(MetaRules.ScaleEffectValue(EffectKind.DamageSingle, 10000, 5) / 10000.0).Within(0.01));
        }

        /// <summary>崩 / 溃 攻击面同为全体 45 + 释放 13,预计输出一致,只差「有没有对应的释放字」。</summary>
        [TestCase(StatusKind.Heft, "崩", "溃")]
        [TestCase(StatusKind.Wellspring, "溃", "崩")]
        public void Stacks_WithoutReleaseChar_ScoreLessPerStack(StatusKind kind, string releaser, string other)
        {
            double PerStack(string ch)
            {
                var s = new Scene(new[] { ch });
                return Terms(s.With(x => x.PlayerStatuses.Add(Stack(kind, 4)))) - Terms(s.Battle);
            }
            double with = PerStack(releaser), without = PerStack(other);
            Assert.That(without, Is.GreaterThan(0), "无释放字仍有折扣价值");
            Assert.That(without, Is.LessThan(with), "无释放字每层分低于有释放字");
        }
    }
}
