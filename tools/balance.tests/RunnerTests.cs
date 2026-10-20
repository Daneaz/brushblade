using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Brushblade.Balance.Bot;
using Brushblade.Core;
using Brushblade.Data;
using Newtonsoft.Json;
using NUnit.Framework;

namespace Brushblade.Balance.Tests
{
    /// <summary>真实层段配置(enemies.json)+ 手搭连战的夹具。</summary>
    internal static class RunFixture
    {
        private static CampaignConfig _campaign;

        public static CampaignConfig Campaign
        {
            get
            {
                if (_campaign != null) return _campaign;
                var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
                while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Brushblade", "Assets")))
                    dir = dir.Parent;
                if (dir == null) throw new InvalidOperationException("找不到仓库根");
                string path = Path.Combine(dir.FullName, "Brushblade", "Assets", "StreamingAssets", "config", "enemies.json");
                _campaign = ConfigLoader.LoadCampaign(File.ReadAllText(path), Fixture.Graph);
                return _campaign;
            }
        }

        /// <summary>画像同款角色 10 级属性(与 Program 的 Profile 一致)。</summary>
        public static BattleConfig NewBattleConfig() => new BattleConfig
        {
            DropTable = Campaign.DropTable,
            PlayerMaxHp = MetaRules.MaxHpFor(10), PlayerAttack = MetaRules.AttackFor(10),
            PlayerDefense = MetaRules.DefenseFor(10), PlayerDodge = MetaRules.DodgeFor(10),
        };

        /// <summary>前排一只木灵(林召出的「木」),血量给足,不会在夹具里被打掉。</summary>
        public static SummonSnapshot WoodSpirit(int slot = 0) => new SummonSnapshot
        {
            Slot = slot, Char = "木", SourceChar = "林", Element = Element.Wood, Hp = 500, MaxHp = 500,
            Attack = 10, Speed = 100, Statuses = new List<StatusEffect>(),
        };

        /// <summary>厚 <paramref name="stacks"/> 层(层数类,TurnsLeft = −1,跨战斗携带)。</summary>
        public static StatusEffect Heft(int stacks) => new StatusEffect
        {
            Kind = StatusKind.Heft, Magnitude = stacks, Polarity = StatusPolarity.Buff, TurnsLeft = -1, SourceId = "test",
        };

        public static readonly EnemyDef Weak = new EnemyDef("弱", Element.Heart, 1, 0);

        /// <summary>一场只有 1 血字怪的连战:出炎打赢 → AdvanceAfterBattle → 停在战利品页。</summary>
        public static RunEngine WonRunInReward(string[] library, string[] rewardPool, int capacity,
            int battles = 1, int? startingHp = null, int maxHpBonus = 0,
            bool libraryExpanded = false, IReadOnlyList<OpeningEffect> openings = null,
            int startingNormalShield = 0, IReadOnlyList<SummonSnapshot> startingSummons = null,
            IReadOnlyList<StatusEffect> startingStatuses = null)
        {
            var rc = new RunConfig
            {
                Encounters = Enumerable.Range(0, battles).Select(_ => (IReadOnlyList<EnemyDef>)new[] { Weak }).ToList(),
                RewardPool = rewardPool,
            };
            var cfg = new BattleConfig
            {
                PlayerMaxHp = 1000, PlayerAttack = MetaRules.AttackFor(10), LibraryCapacity = capacity, DropsPerTurn = 0,
            };
            var run = new RunEngine(Fixture.Graph, rc, cfg, library.Append("炎").ToList(), Array.Empty<string>(), seed: 3,
                startingHp: startingHp, maxHpBonus: maxHpBonus, libraryExpanded: libraryExpanded, startingOpenings: openings,
                startingNormalShield: startingNormalShield, startingSummons: startingSummons, startingStatuses: startingStatuses);
            // 夹具多塞一张炎当武器,打出去就消耗掉(出字即消耗),库回到 library
            Assert.That(run.Battle.Cast("炎", 0, attackMode: true), Is.EqualTo(BattleError.None), "夹具:出炎");
            Assert.That(run.Battle.Phase, Is.EqualTo(BattlePhase.Won),
                $"夹具:一击打赢(敌血 {run.Battle.Enemies[0].Hp},事件 {string.Join(",", run.Battle.LastEvents.Select(e => e.GetType().Name))})");
            run.AdvanceAfterBattle();
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Reward), "夹具:进战利品");
            return run;
        }
    }

    /// <summary>爬塔驱动:跨段携带对齐生产(spec §4.3)、种子并发确定性(§6)、回合种子不读本体 RNG(§2.4)。</summary>
    public class RunnerTests
    {
        private static ClimbSetup Setup(int depthCap, SearchOptions opt = null)
        {
            var owned = Fixture.Graph.All
                .Where(d => d.Effects.Count > 0 && !d.IsComponent && d.Element is { } el && (el == Element.Fire || el == Element.Wood))
                .Select(d => d.Id).ToList();
            var levels = owned.ToDictionary(id => id, _ => 5);
            return new ClimbSetup
            {
                Graph = Fixture.Graph, Campaign = RunFixture.Campaign, OwnedCards = owned, CardLevels = levels,
                NewBattleConfig = RunFixture.NewBattleConfig,
                Search = opt ?? new SearchOptions { K = 2, LeafCap = 12 },
                Appraiser = new CardAppraiser(Fixture.Graph, RunFixture.Campaign.Endless, RunFixture.NewBattleConfig),
                DepthCap = depthCap,
            };
        }

        private static (IReadOnlyList<string>, IReadOnlyList<string>) Start(int seed) =>
            seed % 2 == 0
                ? (new[] { "炎", "林", "焚", "花", "炎", "林" }, new[] { "火", "木" })
                : (new[] { "林", "炎", "花", "焚", "林", "炎" }, new[] { "木" });

        // ── 跨段携带(GameRoot.WriteCarriedSnapshot / StartSegment 口径)─────────

        [Test]
        public void CarryFrom_ReadsCarriedState_NotBattle()
        {
            var openings = new List<OpeningEffect>
            {
                new OpeningEffect { SourceCharId = "炎", Element = Element.Fire, Kind = EffectKind.DamageSingle, Value = 7, BattlesLeft = 3 },
            };
            var run = RunFixture.WonRunInReward(new[] { "林" }, rewardPool: new[] { "冰" }, capacity: 6,
                startingHp: 300, maxHpBonus: 120, libraryExpanded: true, openings: openings,
                startingNormalShield: 80, startingSummons: new[] { RunFixture.WoodSpirit() },
                startingStatuses: new[] { RunFixture.Heft(3) });
            BotRunner.PickRewards(run, id => 1);   // 战利品并入携带态(读 Battle.Library 会把它丢掉)
            Assert.That(run.Phase, Is.EqualTo(RunPhase.RunWon), "夹具:段末");

            var carry = BotRunner.CarryFrom(run);
            Assert.That(carry.Hp, Is.EqualTo(run.CarriedHp), "血量取 CarriedHp");
            Assert.That(carry.MaxHpBonus, Is.EqualTo(120));
            Assert.That(carry.LibraryExpanded, Is.True);
            Assert.That(carry.PoolExpanded, Is.False);
            Assert.That(carry.Library, Is.EqualTo(run.CarriedLibrary.ToList()));
            Assert.That(carry.Library.Contains("冰"), Is.True, "战利品在携带字库里");
            Assert.That(carry.Pool, Is.EqualTo(run.CarriedPool.ToList()));
            Assert.That(JsonConvert.SerializeObject(carry.Openings), Is.EqualTo(JsonConvert.SerializeObject(run.CarriedOpenings)));
            Assert.That(run.CarriedNormalShield, Is.GreaterThan(0), "前提:段末有护盾可带");
            Assert.That(run.CarriedSummons.Count, Is.EqualTo(1), "前提:段末有一只活木灵");
            Assert.That(run.CarriedStatuses.Any(st => st.Kind == StatusKind.Heft && st.Magnitude > 0), Is.True, "前提:段末身上有厚");
            Assert.That(carry.NormalShield, Is.EqualTo(run.CarriedNormalShield), "护盾按生产口径(段末 ShieldCarryPercent 保留)");
            Assert.That(carry.Summons.Count, Is.EqualTo(1));
            Assert.That(carry.Summons[0].Char, Is.EqualTo("木"));
            Assert.That(JsonConvert.SerializeObject(carry.Summons), Is.EqualTo(JsonConvert.SerializeObject(run.CarriedSummons)));
            Assert.That(JsonConvert.SerializeObject(carry.Statuses), Is.EqualTo(JsonConvert.SerializeObject(run.CarriedStatuses)));
            Assert.That(carry.Revived, Is.EqualTo(run.Revived));
        }

        [Test]
        public void StartSegment_PassesCarryIntoRunEngine()
        {
            var setup = Setup(depthCap: 10);
            var carry = new SegmentStart
            {
                Library = new List<string> { "炎", "林" }, Pool = new List<string> { "木" },
                Hp = 345, MaxHpBonus = 77, LibraryExpanded = true, PoolExpanded = true,
                NormalShield = 60,
                Summons = new List<SummonSnapshot> { RunFixture.WoodSpirit(slot: 0) },
                Statuses = new List<StatusEffect> { RunFixture.Heft(2) },
                Openings = new List<OpeningEffect>
                {
                    new OpeningEffect { SourceCharId = "炎", Element = Element.Fire, Kind = EffectKind.DamageSingle, Value = 7, BattlesLeft = 2 },
                },
            };
            var run = BotRunner.StartSegment(setup, carry, fromDepth: 6, towerSeed: 99, out var segmentConfig);
            var baseCfg = RunFixture.NewBattleConfig();
            Assert.That(run.MaxHpBonus, Is.EqualTo(77));
            Assert.That(run.EffectiveMaxHp, Is.EqualTo(baseCfg.PlayerMaxHp + 77));
            Assert.That(run.LibraryExpanded && run.PoolExpanded, Is.True);
            Assert.That(run.Battle.LibraryCapacity, Is.GreaterThan(baseCfg.LibraryCapacity), "扩容在开第一场前生效");
            Assert.That(run.CurrentDepth, Is.EqualTo(6));
            Assert.That(run.Battle.Turn, Is.GreaterThanOrEqualTo(1));
            Assert.That(run.CarriedHp, Is.EqualTo(345), "起始血量 = 携带血量");
            Assert.That(run.Battle.PlayerShield, Is.GreaterThanOrEqualTo(60), "携带护盾进了第一场");
            Assert.That(run.Battle.Summons.Count(x => x != null && x.Alive && x.Char == "木"), Is.EqualTo(1), "携带木灵进了第一场");
            Assert.That(run.Battle.PlayerStatuses.TotalMagnitude(StatusKind.Heft), Is.GreaterThanOrEqualTo(2), "携带厚进了第一场");

            // 搜索副本用的配置与本体这场一致(上限加成、扩容、按层解锁的召唤槽)
            var cfg = BotRunner.BattleConfigFor(run, segmentConfig);
            var copy = BattleEngine.Restore(run.Battle.Capture(), Fixture.Graph, cfg, setup.CardLevels, Evaluator.EnemyDefsOf(run.Battle));
            Assert.That(copy.MaxHp, Is.EqualTo(run.Battle.MaxHp));
            Assert.That(copy.LibraryCapacity, Is.EqualTo(run.Battle.LibraryCapacity));
            Assert.That(copy.PoolCapacity, Is.EqualTo(run.Battle.PoolCapacity));
            Assert.That(JsonConvert.SerializeObject(copy.Capture()), Is.EqualTo(JsonConvert.SerializeObject(run.Battle.Capture())));
        }

        // ── 回合种子 ────────────────────────────

        [Test]
        public void TurnSeed_DependsOnSeedDepthTurn()
        {
            ulong a = BotRunner.TurnSeed(1, 2, 3);
            Assert.That(BotRunner.TurnSeed(1, 2, 3), Is.EqualTo(a));
            Assert.That(BotRunner.TurnSeed(2, 2, 3), Is.Not.EqualTo(a));
            Assert.That(BotRunner.TurnSeed(1, 3, 3), Is.Not.EqualTo(a));
            Assert.That(BotRunner.TurnSeed(1, 2, 4), Is.Not.EqualTo(a));
            Assert.That(BotRunner.TurnSeed(1, 3, 2), Is.Not.EqualTo(BotRunner.TurnSeed(1, 2, 3)), "层与回合不可互换");
        }

        // ── 爬塔与并发 ────────────────────────────

        [Test]
        public void Climb_ProducesDepthAndStats()
        {
            var setup = Setup(depthCap: 3);
            var (lib, pool) = Start(0);
            var r = BotRunner.Climb(setup, 0, lib, pool);
            Assert.That(r.DeathDepth, Is.InRange(1, 3));
            Assert.That(r.Stats.Turns, Is.GreaterThan(0));
            Assert.That(r.Stats.BodyRejected, Is.EqualTo(0), "副本与本体判据不分叉");
            Assert.That(r.CastsByChar.Values.Sum(), Is.GreaterThan(0), "出过字");
        }

        [Test]
        public void SameSeed_SerialAndParallel_IdenticalDepths()
        {
            var seeds = Enumerable.Range(0, 6).ToList();
            var serial = BotRunner.ClimbMany(Setup(depthCap: 3), seeds, Start, parallel: false);
            var parallel = BotRunner.ClimbMany(Setup(depthCap: 3), seeds, Start, parallel: true);
            Assert.That(parallel.Select(r => r.Seed).ToList(), Is.EqualTo(seeds), "结果按种子顺序排列");
            Assert.That(parallel.Select(r => r.DeathDepth).ToList(), Is.EqualTo(serial.Select(r => r.DeathDepth).ToList()));
            Assert.That(parallel.Select(r => r.Stats.Steps).ToList(), Is.EqualTo(serial.Select(r => r.Stats.Steps).ToList()));
            Assert.That(parallel.Select(r => r.Stats.Replans).ToList(), Is.EqualTo(serial.Select(r => r.Stats.Replans).ToList()));
            Assert.That(parallel.Select(r => JsonConvert.SerializeObject(r.CastsByChar.OrderBy(kv => kv.Key, StringComparer.Ordinal))).ToList(),
                Is.EqualTo(serial.Select(r => JsonConvert.SerializeObject(r.CastsByChar.OrderBy(kv => kv.Key, StringComparer.Ordinal))).ToList()));
        }

        /// <summary>跨段版:深度上限放到三段,确定性要覆盖 CarryFrom → StartSegment 这条路径(携带的木灵 / 护盾 / 状态)。</summary>
        [Test]
        public void SameSeed_SerialAndParallel_IdenticalAcrossSegments()
        {
            int bossEvery = RunFixture.Campaign.Endless.BossEvery;
            var seeds = Enumerable.Range(0, 4).ToList();
            var serial = BotRunner.ClimbMany(Setup(depthCap: bossEvery * 3), seeds, Start, parallel: false);
            var parallel = BotRunner.ClimbMany(Setup(depthCap: bossEvery * 3), seeds, Start, parallel: true);
            Assert.That(serial.Count(r => r.DeathDepth > bossEvery), Is.GreaterThan(0),
                $"前提:至少一个种子跨段(卒层 {string.Join(",", serial.Select(r => r.DeathDepth))},BossEvery {bossEvery})");
            Assert.That(parallel.Select(r => r.DeathDepth).ToList(), Is.EqualTo(serial.Select(r => r.DeathDepth).ToList()));
            Assert.That(parallel.Select(r => r.Stats.Steps).ToList(), Is.EqualTo(serial.Select(r => r.Stats.Steps).ToList()));
            Assert.That(parallel.Select(r => r.Stats.Replans).ToList(), Is.EqualTo(serial.Select(r => r.Stats.Replans).ToList()));
            Assert.That(parallel.Select(r => JsonConvert.SerializeObject(r.CastsByChar.OrderBy(kv => kv.Key, StringComparer.Ordinal))).ToList(),
                Is.EqualTo(serial.Select(r => JsonConvert.SerializeObject(r.CastsByChar.OrderBy(kv => kv.Key, StringComparer.Ordinal))).ToList()));
        }
    }
}
