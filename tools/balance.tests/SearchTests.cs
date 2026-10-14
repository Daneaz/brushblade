using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Brushblade.Balance.Bot;
using Brushblade.Core;
using Newtonsoft.Json;
using NUnit.Framework;

namespace Brushblade.Balance.Tests
{
    /// <summary>整回合搜索与执行重搜(spec §2.2–§2.4)。真实字表 + 手搭局面。</summary>
    public class SearchTests
    {
        private const ulong TurnSeed = 0x5EEDF1UL;

        /// <summary>手搭一场战斗,带自定义敌人 / 木灵格掩码 / 暴击率。</summary>
        private sealed class Scene
        {
            public readonly BattleConfig Config;
            public readonly IReadOnlyDictionary<string, int> CardLevels;
            public BattleEngine Battle;

            public Scene(string[] library, EnemyDef[] enemies, string[] pool = null, int? ap = null,
                SummonSnapshot[] summons = null, int slotMask = BattleEngine.AllSummonSlotsMask, int crit = 0,
                int cardLevel = 1, int playerHp = 5000, int libraryCapacity = 6)
            {
                CardLevels = cardLevel == 1 ? null : Fixture.Graph.All.ToDictionary(d => d.Id, _ => cardLevel);
                Config = new BattleConfig
                {
                    PlayerMaxHp = playerHp, ApPerTurn = 3, LibraryCapacity = libraryCapacity, PoolCapacity = 10,
                    BossPhaseJitterPercent = 0, DropsPerTurn = 0, UnlockedSummonSlots = slotMask,
                    PlayerCritChance = crit,
                };
                Battle = new BattleEngine(Fixture.Graph, Config, library, pool ?? Array.Empty<string>(), enemies,
                    seed: 7, cardLevels: CardLevels, startingSummons: summons);
                Assert.That(Battle.Phase, Is.EqualTo(BattlePhase.PlayerTurn), "夹具:开场应轮到玩家");
                if (ap.HasValue) Battle = With(s => s.Ap = ap.Value);
            }

            public BattleEngine With(Action<BattleSnapshot> edit)
            {
                var snap = Battle.Capture();
                edit(snap);
                return BattleEngine.Restore(snap, Fixture.Graph, Config, CardLevels, Evaluator.EnemyDefsOf(Battle));
            }

            public List<BotAction> Best(SearchOptions opt = null, SearchStats stats = null, BattleEngine b = null) =>
                Search.BestTurn(b ?? Battle, Fixture.Graph, Config, CardLevels, opt ?? SearchOptions.Fast, TurnSeed, stats);
        }

        private static string Json(BattleEngine b) => JsonConvert.SerializeObject(b.Capture());

        private static string Show(IEnumerable<BotAction> plan) => string.Join(" | ", plan);

        private static int IndexOf(List<BotAction> plan, Func<BotAction, bool> pred)
        {
            for (int i = 0; i < plan.Count; i++) if (pred(plan[i])) return i;
            return -1;
        }

        private static SummonSnapshot Flower(int slot) => new SummonSnapshot
        {
            Slot = slot, Char = "花", SourceChar = "花", Element = Element.Wood,
            Hp = 72, MaxHp = 72, Attack = 18, Speed = 100, Statuses = new List<StatusEffect>(),
        };

        // ── 选对序列 ────────────────────────────────────────────

        [Test]
        public void ArmorBreakBeforeDamage_IsChosen()
        {
            // 玩家血量放大到让护盾的生存分可忽略:比的只是先破甲还是先打
            var armored = new EnemyDef("甲", Element.Heart, 3000, 0, defense: 60);
            var s = new Scene(new[] { "炎", "圭", "炎" }, new[] { armored }, playerHp: 1_000_000);
            var plan = s.Best();
            int brk = IndexOf(plan, a => a.Kind == ActionKind.Cast && a.CharId == "圭" && a.AttackMode);
            int hit = IndexOf(plan, a => a.Kind == ActionKind.Cast && a.CharId == "炎" && a.AttackMode);
            Assert.That(brk, Is.GreaterThanOrEqualTo(0), "出了圭的破甲面:" + Show(plan));
            Assert.That(hit, Is.GreaterThan(brk), "炎在破甲之后:" + Show(plan));
        }

        [Test]
        public void ComposeThenCast_IsChosen()
        {
            var s = new Scene(new[] { "碉" }, new[] { new EnemyDef("怔", Element.Heart, 3000, 0) }, pool: new[] { "火", "火" });
            // 两火直接出只是两下部件兜底;合成炎(或拆碉凑更好的字)再出才划算。合哪个由打分定,断「合了再出它」
            var plan = s.Best();
            int compose = IndexOf(plan, a => a.Kind == ActionKind.Compose);
            Assert.That(compose, Is.GreaterThanOrEqualTo(0), "有合字:" + Show(plan));
            string made = plan[compose].CharId;
            int cast = IndexOf(plan, a => a.Kind == ActionKind.Cast && a.CharId == made);
            Assert.That(cast, Is.GreaterThan(compose), $"合后出 {made}:" + Show(plan));
        }

        [Test]
        public void BackRowFlower_ReplacedByForest()
        {
            // 只开前排 0 与后排 4 两格,都站着木灵(无空位);远程敌人全场随机打,花一碰就死
            var front = Fixture.WoodSummon(0, 1000);
            var shooter = new EnemyDef("射", Element.Heart, 100000, 150, range: AttackRange.Ranged);
            var s = new Scene(new[] { "林" }, Enumerable.Repeat(shooter, 4).ToArray(), ap: 1,
                summons: new[] { front, Flower(4) }, slotMask: (1 << 0) | (1 << 4));
            Assert.That(s.Battle.CanReplaceAt("林", false, 4), Is.True, "前提:林可替换后排的花");
            var plan = s.Best(SearchOptions.Slow);
            Assert.That(plan[0].Kind, Is.EqualTo(ActionKind.Cast), Show(plan));
            Assert.That(plan[0].CharId, Is.EqualTo("林"), Show(plan));
            Assert.That(plan[0].DropMode, Is.EqualTo(SummonDropMode.Replace), Show(plan));
            Assert.That(plan[0].AllySlot, Is.EqualTo(4), Show(plan));
        }

        // ── 剪枝 / 上限 / 转置 ──────────────────────────────────

        private static Scene Midgame(int cardLevel = 5) =>
            new Scene(new[] { "炎", "圭", "林", "冰", "剑", "花" },
                new[]
                {
                    new EnemyDef("甲", Element.Metal, 2400, 180, defense: 30),
                    new EnemyDef("射", Element.Water, 1800, 140, range: AttackRange.Ranged),
                    new EnemyDef("怔", Element.Heart, 2000, 120),
                },
                pool: new[] { "火", "木", "土" }, summons: new[] { Fixture.WoodSummon(0, 800) }, cardLevel: cardLevel);

        [Test]
        public void LeafCap_StopsExpansion_StillReturnsBestFound()
        {
            var s = Midgame();
            var stats = new SearchStats();
            var plan = s.Best(new SearchOptions { K = 12, LeafCap = 5 }, stats);
            Assert.That(stats.Leaves, Is.LessThanOrEqualTo(5));
            Assert.That(stats.Leaves, Is.GreaterThan(0));
            Assert.That(plan.Count, Is.GreaterThan(0));

            var full = new SearchStats();
            s.Best(new SearchOptions { K = 12, LeafCap = 4096 }, full);
            Assert.That(full.Leaves, Is.GreaterThan(5), "前提:不设上限时叶子不止 5 个");
        }

        [Test]
        public void KLimitsBranching()
        {
            var s = Midgame();
            var k1 = new SearchStats();
            var k4 = new SearchStats();
            s.Best(new SearchOptions { K = 1, LeafCap = 4096 }, k1);
            s.Best(new SearchOptions { K = 4, LeafCap = 4096 }, k4);
            Assert.That(k1.Nodes, Is.LessThan(k4.Nodes));
            Assert.That(k4.BestScore, Is.GreaterThanOrEqualTo(k1.BestScore - 1e-9), "K 大不会更差(K=1 的路径包含在 K=4 里)");
        }

        [Test]
        public void Transposition_SameStateExpandedOnce()
        {
            // 拆炎(免 AP)与出圭的先后两条路到同一局面
            var s = new Scene(new[] { "炎", "圭" }, new[] { new EnemyDef("怔", Element.Heart, 3000, 0) }, ap: 2);
            var on = new SearchStats();
            var off = new SearchStats();
            var a = s.Best(new SearchOptions { K = 64, LeafCap = 100000 }, on);
            var b = s.Best(new SearchOptions { K = 64, LeafCap = 100000, Transposition = false }, off);
            Assert.That(on.TranspositionHits, Is.GreaterThan(0), "有转置命中");
            Assert.That(on.Nodes, Is.LessThan(off.Nodes), "命中的局面不再展开");
            Assert.That(on.Nodes, Is.EqualTo(on.UniqueStates), "每个局面恰好展开一次");
            Assert.That(on.BestScore, Is.EqualTo(off.BestScore).Within(1e-9), "转置不改最优分");
        }

        [Test]
        public void RejectedCandidates_ArePrunedOnTheCopy()
        {
            var s = Midgame();
            var stats = new SearchStats();
            var plan = s.Best(SearchOptions.Fast, stats);
            // 计划在本体副本上逐步可执行,不会被拒
            var copy = Evaluator.CloneWithSeed(s.Battle, Fixture.Graph, s.Config, s.CardLevels, null);
            foreach (var a in plan)
                Assert.That(a.ApplyTo(copy), Is.EqualTo(BattleError.None), "计划内每一步都受理:" + a);
            Assert.That(plan.Last().Kind, Is.EqualTo(ActionKind.EndTurn), "回合没分胜负则以结束回合收尾");
        }

        // ── 副本隔离 / 防偷看 / 确定性 ──────────────────────────

        [Test]
        public void BestTurn_DoesNotTouchTheBattle()
        {
            var s = Midgame();
            string before = Json(s.Battle);
            s.Best(SearchOptions.Fast);
            Assert.That(Json(s.Battle), Is.EqualTo(before));
        }

        [Test]
        public void BestTurn_Deterministic()
        {
            var s = Midgame();
            Assert.That(Show(s.Best()), Is.EqualTo(Show(s.Best())));
        }

        [Test]
        public void BestTurn_IgnoresTheBattlesOwnRng()
        {
            var s = Midgame();
            string expected = Show(s.Best());
            foreach (uint flip in new[] { 0x5A5A5A5Au, 0x12345678u, 0xDEADBEEFu })
            {
                var other = s.With(x => { x.RandomState ^= flip; x.TargetRandomState ^= flip; x.TraitRandomState ^= flip; });
                Assert.That(Show(s.Best(b: other)), Is.EqualTo(expected), $"换本体 RNG(^{flip:X8}),选出的序列不变");
            }
        }

        // ── 执行与重搜(§2.3)──────────────────────────────────

        /// <summary>A 血 200:炎一下 168 打不死,暴击 ×1.5 = 252 打死。B 很肉。
        /// 暴击率 50%,本体与副本的暴击各摇各的 —— 摇得不一样就偏离预期,须从剩余 AP 重搜。</summary>
        private static Scene CritKill(int crit) => new Scene(new[] { "炎", "炎", "炎" },
            new[] { new EnemyDef("弱", Element.Heart, 200, 0), new EnemyDef("肉", Element.Heart, 100000, 0) }, crit: crit);

        [Test]
        public void PlayTurn_NoRandomness_NoReplan_PlanExecutedAsIs()
        {
            var s = CritKill(0);
            var expected = s.Best();
            var stats = new BotStats();
            var executed = Search.PlayTurn(s.Battle, Fixture.Graph, s.Config, s.CardLevels, SearchOptions.Fast, TurnSeed, stats);
            Assert.That(stats.Replans, Is.EqualTo(0));
            Assert.That(stats.BodyRejected, Is.EqualTo(0));
            Assert.That(Show(executed), Is.EqualTo(Show(expected)));
            Assert.That(s.Battle.Turn, Is.EqualTo(2), "回合已结束");
        }

        [Test]
        public void PlayTurn_Midgame_RestoredCopyMatchesBody_NoSpuriousReplan()
        {
            // 带木灵、特性、部件池的局面:副本经 Capture/Restore 复制,若复原会改写任何字段,每步都会误判偏离
            var s = Midgame();
            var stats = new BotStats();
            var executed = Search.PlayTurn(s.Battle, Fixture.Graph, s.Config, s.CardLevels, SearchOptions.Fast, TurnSeed, stats);
            Assert.That(stats.Replans, Is.EqualTo(0), Show(executed));
            Assert.That(stats.Steps, Is.GreaterThan(0));
            Assert.That(stats.Searches, Is.EqualTo(1));
        }

        [Test]
        public void PlayTurn_CritKillDeviates_ReplansRemainingAp()
        {
            int replanned = 0;
            for (uint flip = 1; flip <= 24; flip++)
            {
                var s = CritKill(50);
                var b = s.With(x => { x.RandomState ^= flip * 0x9E3779B9u; });
                var stats = new BotStats();
                var executed = Search.PlayTurn(b, Fixture.Graph, s.Config, s.CardLevels, SearchOptions.Fast, TurnSeed, stats);
                Assert.That(stats.BodyRejected, Is.EqualTo(0), "重搜后不会落到死掉的目标上被拒:" + Show(executed));
                Assert.That(executed.Count(a => a.Kind == ActionKind.Cast), Is.EqualTo(3), "AP 用满:" + Show(executed));
                Assert.That(executed.Last().Kind, Is.EqualTo(ActionKind.EndTurn));
                if (stats.Replans > 0)
                {
                    replanned++;
                    Assert.That(stats.Searches, Is.EqualTo(1 + stats.Replans));
                }
            }
            Assert.That(replanned, Is.GreaterThan(0), "前提:至少一个种子下暴击结果与副本不同、触发重搜");
        }

        // ── 耗时(报告用,不进常规跑)───────────────────────────

        [Test, Explicit("基准:单回合搜索耗时,写进报告")]
        public void Benchmark_FastSlow()
        {
            var s = Midgame();
            foreach (var (name, opt) in new[] { ("Fast", SearchOptions.Fast), ("Slow", SearchOptions.Slow) })
            {
                s.Best(opt); // 热身
                const int runs = 5;
                var stats = new SearchStats();
                var sw = Stopwatch.StartNew();
                for (int i = 0; i < runs; i++) { stats = new SearchStats(); s.Best(opt, stats); }
                sw.Stop();
                TestContext.Progress.WriteLine(
                    $"{name}: {sw.Elapsed.TotalMilliseconds / runs:F1} ms/turn  nodes={stats.Nodes} leaves={stats.Leaves} " +
                    $"generated={stats.Generated} rejected={stats.Rejected} tt={stats.TranspositionHits} best={stats.BestScore:F1}");
            }
        }
    }
}
