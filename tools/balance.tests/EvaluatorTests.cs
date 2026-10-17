using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Balance.Bot;
using Brushblade.Core;
using Newtonsoft.Json;
using NUnit.Framework;

namespace Brushblade.Balance.Tests
{
    public class EvaluatorTests
    {
        private const int PlayerMax = 5000;
        private const ulong Leaf = 0xF1F1UL;

        private static readonly EnemyDef Dummy = new EnemyDef("怔", Element.Heart, 10000, 0);
        private static readonly EnemyDef Brute = new EnemyDef("凶", Element.Heart, 10000, 1200);

        private static BattleConfig Config() => new BattleConfig
        {
            PlayerMaxHp = PlayerMax, ApPerTurn = 3, LibraryCapacity = 6, PoolCapacity = 10,
            BossPhaseJitterPercent = 0, DropsPerTurn = 0,
        };

        private sealed class Scene
        {
            public BattleConfig Config;
            public BattleEngine Battle;

            public Scene(EnemyDef[] enemies, SummonSnapshot[] summons = null, string[] library = null)
            {
                Config = Config();
                Battle = new BattleEngine(Fixture.Graph, Config, library ?? new[] { "炎" }, Array.Empty<string>(),
                    enemies, seed: 11, startingSummons: summons);
                Assert.That(Battle.Phase, Is.EqualTo(BattlePhase.PlayerTurn), "夹具:开场应轮到玩家");
            }

            /// <summary>改快照再复原,返回新引擎(本场景的 Battle 不变)。</summary>
            public BattleEngine With(Action<BattleSnapshot> edit)
            {
                var snap = Battle.Capture();
                edit(snap);
                return BattleEngine.Restore(snap, Fixture.Graph, Config, null, Evaluator.EnemyDefsOf(Battle));
            }

            public double Full(BattleEngine b, ulong leaf = Leaf) => Evaluator.Full(b, Fixture.Graph, Config, null, leaf);
        }

        private static string Json(BattleEngine b) => JsonConvert.SerializeObject(b.Capture());

        // ── §3.1 终局 ─────────────────────────────────────────────

        [Test]
        public void Won_IsMillionMinusTurns()
        {
            var s = new Scene(new[] { Dummy });
            var won = s.With(x => { x.Phase = BattlePhase.Won; x.Turn = 7; foreach (var e in x.Enemies) e.Hp = 0; });
            Assert.That(Evaluator.Quick(won, Fixture.Graph, null), Is.EqualTo(1_000_000 - 7));
            var fasterWin = s.With(x => { x.Phase = BattlePhase.Won; x.Turn = 3; foreach (var e in x.Enemies) e.Hp = 0; });
            Assert.That(Evaluator.Quick(fasterWin, Fixture.Graph, null), Is.GreaterThan(Evaluator.Quick(won, Fixture.Graph, null)), "越快赢分越高");
        }

        [Test]
        public void Lost_IsMinusMillion()
        {
            var s = new Scene(new[] { Dummy });
            var lost = s.With(x => { x.Phase = BattlePhase.Lost; x.PlayerHp = 0; });
            Assert.That(Evaluator.Quick(lost, Fixture.Graph, null), Is.EqualTo(-1_000_000));
        }

        [Test]
        public void Full_EnemyTurnKillsPlayer_IsMinusMillion()
        {
            var s = new Scene(new[] { Brute });
            var dying = s.With(x => x.PlayerHp = 1);
            Assert.That(s.Full(dying), Is.EqualTo(-1_000_000));
        }

        // ── §3.1 进攻 / 生存 / 蓄能 ─────────────────────────────────

        [Test]
        public void Offense_DamageRatioTimes100_PlusFiftyPerKill()
        {
            var s = new Scene(new[] { Dummy, Dummy });
            double baseline = Evaluator.Quick(s.Battle, Fixture.Graph, null);
            var hurt = s.With(x => x.Enemies[0].Hp = 7500); // 损失 25%
            Assert.That(Evaluator.Quick(hurt, Fixture.Graph, null) - baseline, Is.EqualTo(25.0).Within(1e-9));
            var killed = s.With(x => x.Enemies[1].Hp = 0);   // 损失 100% + 打倒 50
            Assert.That(Evaluator.Quick(killed, Fixture.Graph, null) - baseline, Is.EqualTo(150.0).Within(1e-9));
        }

        [Test]
        public void Survival_WeightSwitchesAtHalfHp()
        {
            var s = new Scene(new[] { Dummy });
            var above = s.With(x => x.PlayerHp = PlayerMax / 2 + 1); // > 50% → W = 80
            var at = s.With(x => x.PlayerHp = PlayerMax / 2);         // ≤ 50% → W = 200
            double offense = 0; // 敌人满血、无蓄能
            Assert.That(Evaluator.Quick(above, Fixture.Graph, null), Is.EqualTo(offense + 2501.0 / PlayerMax * 80).Within(1e-9));
            Assert.That(Evaluator.Quick(at, Fixture.Graph, null), Is.EqualTo(offense + 2500.0 / PlayerMax * 200).Within(1e-9));
        }

        [Test]
        public void Survival_ShieldCountsButDoesNotMoveTheHpLine()
        {
            var s = new Scene(new[] { Dummy });
            // 血 2500(= 50%)+ 盾 2000:血线判据不含盾 → 仍按 W = 200
            var b = s.With(x => { x.PlayerHp = 2500; x.ShieldNormal = 2000; });
            Assert.That(Evaluator.Quick(b, Fixture.Graph, null), Is.EqualTo(4500.0 / PlayerMax * 200).Within(1e-9));
        }

        [Test]
        public void Survival_LiveSummonsAtHalfWeight_DeadOnesIgnored()
        {
            var alive = Fixture.WoodSummon(0, hp: 600);
            alive.Shield = 200;
            var s = new Scene(new[] { Dummy }, summons: new[] { alive, Fixture.WoodSummon(1, hp: 0) });
            double player = (double)s.Battle.PlayerHp / PlayerMax * 80; // 开场满血 → W = 80
            Assert.That(Evaluator.Quick(s.Battle, Fixture.Graph, null), Is.EqualTo(player + 800.0 / 1000 * 40).Within(1e-9));
        }

        // 厚 / 泉 / 战意 的蓄能分见 EvaluatorStoredValueTests(2026-10-17 起按预计兑现伤害记分,不再每层 3 分)

        // ── §2.4 副本隔离与防偷看 ───────────────────────────────────

        /// <summary>两只会打人的敌人 + 两只血量上限不同的前排木灵:近战择目标在两只木灵间随机(择敌流),
        /// 打中哪只记分不同(一只被打死、一只只掉一截),所以敌人回合的分数对 RNG 敏感。</summary>
        private static Scene RandomSensitive()
        {
            var frail = Fixture.WoodSummon(0, 1000);
            var sturdy = Fixture.WoodSummon(1, 6000);
            sturdy.MaxHp = 6000;
            return new Scene(new[] { Brute, Brute }, summons: new[] { frail, sturdy });
        }

        private static readonly uint[] RngFlips = { 0x5A5A5A5Au, 0xA5A5A5A5u, 0x3C3C3C3Cu, 0x12345678u, 0x0F0F0F0Fu, 0xDEADBEEFu };

        [Test]
        public void Full_DoesNotTouchTheBattle()
        {
            var s = RandomSensitive();
            string before = Json(s.Battle);
            s.Full(s.Battle);
            Assert.That(Json(s.Battle), Is.EqualTo(before));
        }

        [Test]
        public void Full_SameLeafSeed_SameScore()
        {
            var s = RandomSensitive();
            Assert.That(s.Full(s.Battle), Is.EqualTo(s.Full(s.Battle)));
        }

        [Test]
        public void Full_IgnoresTheBattlesOwnRng()
        {
            var s = RandomSensitive();
            double expected = s.Full(s.Battle);
            var plainScores = new HashSet<double>();
            {
                var plain = Evaluator.CloneWithSeed(s.Battle, Fixture.Graph, s.Config, null, null);
                plain.EndTurn();
                plainScores.Add(Evaluator.Quick(plain, Fixture.Graph, null));
            }
            foreach (uint flip in RngFlips)
            {
                var other = s.With(x => { x.RandomState ^= flip; x.TargetRandomState ^= flip; x.TraitRandomState ^= flip; });
                // 对照:照抄本体随机流的副本跑敌人回合,分数随本体 RNG 变
                var plain = Evaluator.CloneWithSeed(other, Fixture.Graph, s.Config, null, null);
                plain.EndTurn();
                plainScores.Add(Evaluator.Quick(plain, Fixture.Graph, null));

                Assert.That(s.Full(other), Is.EqualTo(expected), $"换本体 RNG(^{flip:X8}),Full 不变");
            }
            Assert.That(plainScores.Count, Is.GreaterThan(1), "前提:这个局面的敌人回合分数确实随 RNG 变(否则本测试是空转)");
        }

        [Test]
        public void CloneWithSeed_ReplacesAllThreeStreams_NullKeepsThem()
        {
            var s = RandomSensitive();
            var orig = s.Battle.Capture();
            var seeded = Evaluator.CloneWithSeed(s.Battle, Fixture.Graph, s.Config, null, 42).Capture();
            Assert.That(seeded.RandomState, Is.Not.EqualTo(orig.RandomState));
            Assert.That(seeded.TargetRandomState, Is.Not.EqualTo(orig.TargetRandomState));
            Assert.That(seeded.TraitRandomState, Is.Not.EqualTo(orig.TraitRandomState));
            Assert.That(seeded.TraitRandomState, Is.Not.EqualTo(0u), "0 会被 Restore 当成旧快照");

            var plain = Evaluator.CloneWithSeed(s.Battle, Fixture.Graph, s.Config, null, null);
            Assert.That(Json(plain), Is.EqualTo(Json(s.Battle)), "seed 为空 = 原样复制");
        }

        // ── 护盾的价值来自敌人回合 ──────────────────────────────────

        [Test]
        public void Full_ShieldBeatsEqualDamage_WhenEnemyHitsHardAndPlayerIsLow()
        {
            var s = new Scene(new[] { Brute });
            const int amount = 3000;
            var shielded = s.With(x => { x.PlayerHp = 800; x.ShieldNormal = amount; });
            var struck = s.With(x => { x.PlayerHp = 800; x.Enemies[0].Hp -= amount; });

            Assert.That(s.Full(struck), Is.EqualTo(-1_000_000), "前提:不出盾,敌人回合打死残血玩家");
            Assert.That(s.Full(shielded), Is.GreaterThan(s.Full(struck)), "出盾的 Full 分更高");
        }
    }
}
