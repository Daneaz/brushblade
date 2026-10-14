using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Brushblade.Balance.Bot;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Balance.Tests
{
    /// <summary>局外字估值(spec §4.1)与它的两处用途(§4.2:战利品 / 掉字换入、合字腾位弃最弱)。</summary>
    public class AppraiserTests
    {
        private static CardAppraiser NewAppraiser() =>
            new CardAppraiser(Fixture.Graph, RunFixture.Campaign.Endless, RunFixture.NewBattleConfig);

        private static readonly string[] Sample = { "炎", "林", "冰", "圭", "花", "焚", "剑", "淼" };

        [Test]
        public void SameInput_SameValue_AcrossInstances()
        {
            var a = NewAppraiser();
            var b = NewAppraiser();
            foreach (var id in Sample)
                foreach (int depth in new[] { 1, 23 })
                    Assert.That(b.Value(id, 5, depth), Is.EqualTo(a.Value(id, 5, depth)), $"{id}@{depth}");
            Assert.That(a.Value("炎", 5, 1), Is.GreaterThan(0), "攻击字在参考局面里打得出伤害");
        }

        [Test]
        public void Cached_PerCharLevelBand()
        {
            var a = NewAppraiser();
            a.Value("炎", 5, 1);
            a.Value("炎", 5, 1);
            a.Value("炎", 5, 7);   // 同层段(字林 1–10)
            Assert.That(a.Computations, Is.EqualTo(1), "字 × 等级 × 层段 只算一次");
            a.Value("炎", 1, 1);
            a.Value("炎", 5, 11);  // 朱砂
            Assert.That(a.Computations, Is.EqualTo(3));
        }

        [Test]
        public void HigherLevel_NotWorse_ForPlainAttackChar()
        {
            var a = NewAppraiser();
            Assert.That(a.Value("炎", 8, 1), Is.GreaterThanOrEqualTo(a.Value("炎", 1, 1)), "卡等级放大数值");
        }

        [Test]
        public void ConcurrentAccess_SameValues_OrderIndependent()
        {
            var keys = (from id in Sample from lv in new[] { 1, 5 } from d in new[] { 1, 15, 33 } select (id, lv, d)).ToList();
            var serial = NewAppraiser();
            var expected = keys.ToDictionary(k => k, k => serial.Value(k.id, k.lv, k.d));

            var shared = NewAppraiser();
            var shuffled = keys.Concat(keys).OrderBy(k => (k.id.GetHashCode() * 31 + k.lv * 7 + k.d) & 0xFFFF).ToList();
            var got = new System.Collections.Concurrent.ConcurrentBag<((string, int, int) key, double v)>();
            Parallel.ForEach(Enumerable.Reverse(shuffled), new ParallelOptions { MaxDegreeOfParallelism = 8 },
                k => got.Add((k, shared.Value(k.id, k.lv, k.d))));

            foreach (var (key, v) in got)
                Assert.That(v, Is.EqualTo(expected[key]), $"{key}");
            Assert.That(shared.Computations, Is.EqualTo(keys.Count), "并发下每个键也只算一次");
        }

        [Test]
        public void ComponentOrUnknown_ValuesZero()
        {
            // 非字(部件)/ 不在字表里:0 分,不抛
            var a = NewAppraiser();
            Assert.That(a.Value("火", 5, 1), Is.EqualTo(0));
            Assert.That(a.Value("不存在的字", 5, 1), Is.EqualTo(0));
        }

        // ── 用途:换入判定 ─────────────────────────────

        private static Func<string, double> Values(params (string id, double v)[] table)
        {
            var d = table.ToDictionary(t => t.id, t => t.v);
            return id => d.TryGetValue(id, out var v) ? v : 0;
        }

        [Test]
        public void DropChoice_ReplacesWeakest_OnlyWhenBetter()
        {
            var value = Values(("炎", 10), ("林", 3), ("冰", 7), ("焚", 5));
            var t = new TestBattle(new[] { "炎", "林", "冰" }, libraryCapacity: 3);

            var better = WithDrop(t, "焚");
            BotRunner.ResolveDrop(better, value);
            Assert.That(better.Library.OrderBy(s => s, StringComparer.Ordinal).ToList(),
                Is.EqualTo(new[] { "焚", "冰", "炎" }.OrderBy(s => s, StringComparer.Ordinal).ToList()), "焚(5) 换掉最弱的林(3)");

            var worse = WithDrop(t, "林");
            var lowValue = Values(("炎", 10), ("林", 3), ("冰", 7));
            var before = worse.Library.ToList();
            BotRunner.ResolveDrop(worse, id => id == "林" ? 1 : lowValue(id));
            Assert.That(worse.Library.ToList(), Is.EqualTo(before), "掉的字不如最弱一张:跳过");
            Assert.That(worse.Phase, Is.EqualTo(BattlePhase.PlayerTurn));
        }

        private static BattleEngine WithDrop(TestBattle t, string drop)
        {
            var snap = t.Battle.Capture();
            snap.Phase = BattlePhase.DropChoice;
            snap.PendingDrop = drop;
            var b = BattleEngine.Restore(snap, Fixture.Graph, t.Config, t.CardLevels, t.EnemyDefs);
            Assert.That(b.Phase, Is.EqualTo(BattlePhase.DropChoice), "夹具:挂起掉字");
            return b;
        }

        [Test]
        public void Rewards_PickByValue_ReplaceWeakestWhenFull()
        {
            // 不满库:5 选 2 取估值最高的两张
            var run = RunFixture.WonRunInReward(new[] { "炎" }, rewardPool: new[] { "林", "冰", "焚", "剑", "花" }, capacity: 6);
            var options = run.RewardOptions.ToList();
            Assert.That(options.Count, Is.GreaterThanOrEqualTo(2), "夹具:有候选");
            var rank = options.Distinct().Select((id, i) => (id, v: (double)(i * 10 + 5))).ToArray();
            var value = Values(rank.Append(("炎", 1)).ToArray());
            var expectTop2 = options.OrderByDescending(id => value(id)).Take(2).ToList();
            BotRunner.PickRewards(run, value);
            foreach (var id in expectTop2)
                Assert.That(run.CarriedLibrary.Contains(id), Is.True, $"取了 {id}");
            Assert.That(run.Phase, Is.Not.EqualTo(RunPhase.Reward), "取完开拔");

            // 满库:只在候选强于库中最弱时替换
            var full = RunFixture.WonRunInReward(new[] { "炎", "冰" }, rewardPool: new[] { "林" }, capacity: 2);
            BotRunner.PickRewards(full, Values(("炎", 10), ("冰", 2), ("林", 5)));
            Assert.That(full.CarriedLibrary.OrderBy(s => s, StringComparer.Ordinal).ToList(),
                Is.EqualTo(new[] { "林", "炎" }.OrderBy(s => s, StringComparer.Ordinal).ToList()), "林(5) 换掉冰(2)");

            var keep = RunFixture.WonRunInReward(new[] { "炎", "冰" }, rewardPool: new[] { "林" }, capacity: 2);
            BotRunner.PickRewards(keep, Values(("炎", 10), ("冰", 8), ("林", 5)));
            Assert.That(keep.CarriedLibrary.OrderBy(s => s, StringComparer.Ordinal).ToList(),
                Is.EqualTo(new[] { "冰", "炎" }.OrderBy(s => s, StringComparer.Ordinal).ToList()), "不占优不换");
        }

        [Test]
        public void WeakestIndex_FirstOnTies()
        {
            var value = Values(("炎", 3), ("林", 1), ("冰", 1));
            Assert.That(BotRunner.WeakestIndex(new[] { "炎", "林", "冰" }, value), Is.EqualTo(1));
            Assert.That(BotRunner.WeakestIndex(Array.Empty<string>(), value), Is.EqualTo(-1));
        }

        [Test]
        public void CompoundCompose_OnlyDiscardsDesignatedWeakest()
        {
            // 林 = 木 + 木,字库满(容量 2):不限弃谁时候选两条;挂上 DiscardPick 后搜索只会弃焚
            var t = new TestBattle(new[] { "炎", "焚" }, pool: new[] { "木", "木" }, enemies: 1, libraryCapacity: 2);
            var opt = SearchOptions.Fast;
            opt.DiscardPick = lib => "焚";
            var plan = Search.BestTurn(t.Battle, Fixture.Graph, t.Config, t.CardLevels, opt, 0xABCUL);
            Assert.That(plan.Where(a => a.DiscardForCompose != null).All(a => a.DiscardForCompose == "焚"), Is.True,
                string.Join(" | ", plan));

            opt.DiscardPick = lib => "炎";
            var stats = new SearchStats();
            Search.BestTurn(t.Battle, Fixture.Graph, t.Config, t.CardLevels, opt, 0xABCUL, stats);
            var free = new SearchStats();
            Search.BestTurn(t.Battle, Fixture.Graph, t.Config, t.CardLevels, SearchOptions.Fast, 0xABCUL, free);
            Assert.That(stats.Generated, Is.LessThan(free.Generated), "限定弃字后少试执行了另一条复合合字");
        }
    }
}
