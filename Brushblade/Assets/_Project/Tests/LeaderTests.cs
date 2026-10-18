using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Brushblade.Core;
using Brushblade.Data;
using NUnit.Framework;

namespace Brushblade.CoreTests
{
    /// <summary>双字头领 + 敌人档位(spec 2026-10-18 §3、§5 的 Core 部分)。
    ///
    /// 头领是 enemies.json 段落 <c>leaderPool</c> 里的独立 EnemyDef,占 1 列 × 2 排;
    /// 普通层敌人数 ≥ 2(7 层起,2026-10-18 收尾 spec §3)时 7–24 层每层 1/3 出一只,25 层起必出,
    /// 出时遭遇 = 1 头领 + (N − 2) 小怪(7–12 层即头领单挑)。
    /// 档位(<see cref="EnemyTier"/>)由生成处写入:小怪 / 随从 Minion、头领 Leader、
    /// 精英层 Boss Elite、主题 Boss 层 Boss。</summary>
    public sealed class LeaderTests
    {
        private static string ConfigDir()
        {
            // 锚点只能用 TestDirectory(AppContext.BaseDirectory 在 Unity Test Runner 下指向编辑器安装目录)
            var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Brushblade")))
                dir = dir.Parent;
            Assert.That(dir, Is.Not.Null, "找不到仓库根目录");
            return Path.Combine(dir.FullName, "Brushblade", "Assets", "StreamingAssets", "config");
        }

        private static EndlessConfig RealEndless()
        {
            var graph = ConfigLoader.LoadGraph(File.ReadAllText(Path.Combine(ConfigDir(), "chars.json")));
            var campaign = ConfigLoader.LoadCampaign(
                File.ReadAllText(Path.Combine(ConfigDir(), "enemies.json")), graph);
            return campaign.Endless;
        }

        private static IReadOnlyList<EnemyDef> Floor(EndlessConfig config, int depth, int seed) =>
            EndlessGenerator.BuildFloor(config, depth, EndlessGenerator.FloorRandom(seed, depth));

        // ---- 档位字段 ----

        [Test]
        public void EnemyDef_DefaultsToMinion()
        {
            Assert.That(new EnemyDef("枯", Element.Wood, 10, 1).Tier, Is.EqualTo(EnemyTier.Minion));
        }

        [Test]
        public void Scale_CarriesTier_AndCanOverwriteIt()
        {
            // Scale 是 EnemyDef 的重建点,漏带就静默回落成 Minion
            var leader = new EnemyDef("朽木", Element.Wood, 100, 10, tier: EnemyTier.Leader);
            Assert.That(CampaignConfig.Scale(leader, 2f).Tier, Is.EqualTo(EnemyTier.Leader));
            Assert.That(CampaignConfig.Scale(leader, 2f, EnemyTier.Elite).Tier, Is.EqualTo(EnemyTier.Elite));
        }

        // ---- 配置 ----

        [TestCase("字林", "朽木", "荆棘")]
        [TestCase("朱砂", "烽燧", "余烬")]
        [TestCase("文山", "碑碣", "磐石")]
        [TestCase("金石", "钟鼎", "刀笔")]
        [TestCase("墨海", "漩涡", "浊浪")]
        [TestCase("词渊", "漩涡", "浊浪")]   // 词渊暂无名单,沿用墨海两只(spec §3)
        public void RealConfig_EveryBandHasItsTwoLeaders(string bandName, string a, string b)
        {
            var band = RealEndless().Bands.Single(x => x.Name == bandName);
            Assert.That(band.LeaderPool.Select(e => e.Id).ToArray(), Is.EqualTo(new[] { a, b }));
            foreach (var leader in band.LeaderPool)
            {
                Assert.That(leader.ColumnSpan, Is.EqualTo(1), leader.Id);
                Assert.That(leader.RowSpan, Is.EqualTo(Targeting.RowSpanBoth), leader.Id + " 占上下两格");
                Assert.That(leader.Phases.Count, Is.Zero, leader.Id + " 不是成语 Boss");
                if (band.Element.HasValue)
                    Assert.That(leader.Element, Is.EqualTo(band.Element.Value), leader.Id + " 属性随段");
            }
        }

        [Test]
        public void RealConfig_LeadersNeverEnterTheMinionPools()
        {
            // 头领只从 leaderPool 出;混进 enemyPool 会被当 1 格的小怪抽,占格账就错了
            foreach (var band in RealEndless().Bands)
                foreach (var leader in band.LeaderPool)
                    Assert.That(band.EnemyPool.Any(e => e.Id == leader.Id), Is.False, $"{band.Name}:{leader.Id}");
        }

        /// <summary>头领必须弱于本段 Boss(spec §3,用户硬要求「比当前层的 Boss 弱」):
        /// 头领在它**可出场**的每个普通层 d(第 <see cref="FirstLeaderDepth"/> 层起,到本段末)按普通层
        /// <c>ScaleFor(d)</c> 缩放;Boss 按**本段内头领可出场之后的第一个 Boss 层**的滞后缩放
        /// (<c>ScaleFor</c> 对 Boss 层 = 1 + k×(层 − BossEvery),比同层杂兵低一截)—— 其余段即本段首个 Boss 层
        /// (15/25…),字林(头领 7 层起)取第 10 层 Boss(收尾 spec §3:第 5 层精英在头领出场之前,不算「本段 Boss」)。
        /// 生命 ≤ 本段**每一只** Boss 各阶段生命合计 × 50%,攻击 ≤ 该 Boss 各阶段攻击最大值。
        /// 成语 Boss 取 <see cref="EndlessGenerator.BuildIdiomBoss"/> 的阶段值。
        ///
        /// 末段(词渊)不封口,按其他段同样的十层算段末:之后每十层头领与 Boss 的缩放比只会更小。</summary>
        [Test]
        public void RealConfig_LeadersAreWeakerThanTheBandsFirstBossFloor()
        {
            var config = RealEndless();
            var bands = config.Bands;
            var failures = new List<string>();
            for (int b = 0; b < bands.Count; b++)
            {
                var band = bands[b];
                var bosses = band.BossPool.Concat(band.EliteBossPool)
                    .Concat(band.IdiomBossPool.Select(EndlessGenerator.BuildIdiomBoss))
                    .Concat(band.EliteIdiomBossPool.Select(EndlessGenerator.BuildIdiomBoss))
                    .ToList();
                Assert.That(bosses, Is.Not.Empty, band.Name);
                int last = b + 1 < bands.Count ? bands[b + 1].FromDepth - 1 : band.FromDepth + 9;
                int first = Math.Max(FirstLeaderDepth, band.FromDepth);
                Assert.That(first, Is.LessThanOrEqualTo(last), band.Name + " 段内有头领可出场的层");
                int bossDepth = first;
                while (!config.IsBossDepth(bossDepth)) bossDepth++;
                float bossScale = config.ScaleFor(bossDepth);
                for (int depth = first; depth <= last; depth++)
                {
                    if (config.IsBossDepth(depth)) continue;
                    foreach (var leaderDef in band.LeaderPool)
                    {
                        var leader = CampaignConfig.Scale(leaderDef, config.ScaleFor(depth));
                        foreach (var bossDef in bosses)
                        {
                            var boss = CampaignConfig.Scale(bossDef, bossScale);
                            int hpSum = boss.Phases.Sum(p => p.MaxHp);
                            int atkMax = boss.Phases.Max(p => p.Attack);
                            if (leader.MaxHp * 2 > hpSum)
                                failures.Add($"{band.Name} {depth} 层:{leader.Id} 生命 {leader.MaxHp} > {boss.Id}@{bossDepth} 合计 {hpSum} 的一半");
                            if (leader.Attack > atkMax)
                                failures.Add($"{band.Name} {depth} 层:{leader.Id} 攻击 {leader.Attack} > {boss.Id}@{bossDepth} 最大阶段攻击 {atkMax}");
                        }
                    }
                }
            }
            Assert.That(failures, Is.Empty, string.Join("\n", failures.Take(20)));
        }

        /// <summary>头领最早可出场的层:敌人数第一次 ≥ <see cref="EndlessGenerator.LeaderMinEnemies"/>。</summary>
        private static int FirstLeaderDepth
        {
            get
            {
                int d = 1;
                while (EndlessGenerator.MinionCountFor(d) < EndlessGenerator.LeaderMinEnemies) d++;
                return d;
            }
        }

        [Test]
        public void FirstLeaderDepth_IsSeven()
        {
            Assert.That(FirstLeaderDepth, Is.EqualTo(7), "收尾 spec §3:敌人数 ≥ 2 起(7 层)出头领");
        }

        // ---- 生成 ----

        [Test]
        public void Generator_LeaderAppearsOnlyFromDepth7_AlwaysFrom25_NeverOnBossFloors()
        {
            var config = RealEndless();
            for (int depth = 1; depth <= 80; depth++)
            {
                int withLeader = 0;
                const int seeds = 300;
                for (int seed = 0; seed < seeds; seed++)
                {
                    var floor = Floor(config, depth, seed);
                    int leaders = floor.Count(e => e.Tier == EnemyTier.Leader);
                    Assert.That(leaders, Is.LessThanOrEqualTo(1), $"第 {depth} 层一层最多一只头领");
                    if (leaders == 1) withLeader++;
                }
                if (config.IsBossDepth(depth) || depth < 7)
                    Assert.That(withLeader, Is.Zero, $"第 {depth} 层不该有头领");
                else if (depth >= 25)
                    Assert.That(withLeader, Is.EqualTo(seeds), $"第 {depth} 层必出头领");
                else
                    Assert.That(withLeader, Is.InRange(seeds / 4, seeds * 5 / 12),
                        $"第 {depth} 层头领出现 {withLeader}/{seeds},应约 1/3");
            }
        }

        [Test]
        public void Generator_LeaderFloor_IsOneLeaderPlusMinionsFillingTheSameCells()
        {
            var config = RealEndless();
            for (int depth = 7; depth <= 80; depth++)
            {
                if (config.IsBossDepth(depth)) continue;
                for (int seed = 0; seed < 50; seed++)
                {
                    var floor = Floor(config, depth, seed);
                    if (!floor.Any(e => e.Tier == EnemyTier.Leader)) continue;
                    Assert.That(floor[0].Tier, Is.EqualTo(EnemyTier.Leader), "头领恒为首项(与 Boss 同口径,落位先占中列)");
                    Assert.That(floor.Count, Is.EqualTo(1 + EndlessGenerator.MinionCountFor(depth) - 2), $"第 {depth} 层");
                    int cells = floor.Sum(e => e.ColumnSpan * e.RowSpan);
                    Assert.That(cells, Is.EqualTo(EndlessGenerator.MinionCountFor(depth)), $"第 {depth} 层占格");
                    Assert.That(floor.Skip(1).All(e => e.Tier == EnemyTier.Minion), Is.True);
                    Assert.That(floor.Count(e => e.Defense > 0), Is.LessThanOrEqualTo(1), "带甲每场最多 1 只,头领也算");
                }
            }
        }

        [Test]
        public void Generator_LeaderIsScaledLikeANormalFloor()
        {
            var config = RealEndless();
            var floor = Floor(config, 31, 1);
            var leader = floor[0];
            var def = config.BandFor(31).LeaderPool.Single(e => e.Id == leader.Id);
            Assert.That(leader.MaxHp, Is.EqualTo(CampaignConfig.Scale(def, config.ScaleFor(31)).MaxHp));
            Assert.That(leader.RowSpan, Is.EqualTo(Targeting.RowSpanBoth), "缩放后仍占两排");
        }

        [Test]
        public void Generator_WritesTiers_MinionEliteBoss()
        {
            var config = RealEndless();
            for (int seed = 0; seed < 20; seed++)
            {
                foreach (var e in Floor(config, 6, seed))   // 7 层起可能出头领
                    Assert.That(e.Tier, Is.EqualTo(EnemyTier.Minion), "普通层小怪");
                var elite = Floor(config, 15, seed);
                Assert.That(elite[0].Tier, Is.EqualTo(EnemyTier.Elite), "第 15 层是精英层");
                Assert.That(elite.Skip(1).All(e => e.Tier == EnemyTier.Minion), Is.True, "随从是小怪");
                Assert.That(Floor(config, 20, seed)[0].Tier, Is.EqualTo(EnemyTier.Boss), "第 20 层是主题 Boss 层");
                Assert.That(Floor(config, 55, seed)[0].Tier, Is.EqualTo(EnemyTier.Elite),
                    "词渊没配精英池,第 55 层仍是精英层(档位按层定)");
            }
        }

        [Test]
        public void Generator_FloorsWithoutLeaders_AreUnchangedInShape()
        {
            // 头领只在 MinionCountFor ≥ 2 时介入;第 1–6 层编成只数不变
            var config = RealEndless();
            for (int depth = 1; depth <= 6; depth++)
            {
                if (config.IsBossDepth(depth)) continue;
                Assert.That(Floor(config, depth, 3).Count, Is.EqualTo(EndlessGenerator.MinionCountFor(depth)));
            }
        }

        [Test]
        public void Generator_Depth7To12_LeaderFloorIsASoloLeader()
        {
            // 7–12 层 MinionCountFor = 2 = 头领占格,出头领时就是单挑(收尾 spec §3)
            var config = RealEndless();
            int solos = 0;
            for (int depth = 7; depth <= 12; depth++)
            {
                if (config.IsBossDepth(depth)) continue;
                for (int seed = 0; seed < 60; seed++)
                {
                    var floor = Floor(config, depth, seed);
                    if (!floor.Any(e => e.Tier == EnemyTier.Leader)) continue;
                    Assert.That(floor.Count, Is.EqualTo(1), $"第 {depth} 层头领单挑");
                    solos++;
                }
            }
            Assert.That(solos, Is.GreaterThan(0), "前提:7–12 层确实摇出过头领");
        }

        [Test]
        public void SoloLeader_DeathWinsWithoutBehead()
        {
            // Review Focus 4:场上只有头领一只 —— 阵亡即胜,斩首不对空集施加、不播报
            var graph = new RecipeGraph(new[]
            {
                new CharDef("凿", Element.Heart,
                    effects: new[] { new EffectDef(EffectKind.DamageSingle, 100) }),
            });
            var leader = new EnemyDef("朽木", Element.Wood, 10, 10, tier: EnemyTier.Leader,
                columnSpan: 1, rowSpan: Targeting.RowSpanBoth);
            var engine = new BattleEngine(graph,
                new BattleConfig { DropTable = new[] { "凿" }, PlayerMaxHp = 500, ApPerTurn = 20 },
                new[] { "凿", "凿" }, Array.Empty<string>(), new[] { leader }, seed: 1);
            Assert.That(engine.Cast("凿", 0), Is.EqualTo(BattleError.None));
            Assert.That(engine.Phase, Is.EqualTo(BattlePhase.Won), "头领阵亡即胜");
            Assert.That(engine.LastEvents.Count(e => e.Kind == BattleEventKind.Behead), Is.Zero, "单挑不斩首");
        }

        // ---- 墨锭 ----

        [Test]
        public void KillInk_LeaderWorthTwo()
        {
            Assert.That(EndlessRules.KillInk(12, isBoss: false, isLeader: true), Is.EqualTo(4));
            Assert.That(EndlessRules.KillInk(12, isBoss: false), Is.EqualTo(2));
        }

        [Test]
        public void RunEngine_CreditsLeaderKillDouble()
        {
            var graph = new RecipeGraph(new[]
            {
                new CharDef("木", Element.Wood),
                new CharDef("凿", Element.Heart,
                    effects: new[] { new EffectDef(EffectKind.DamageSingle, 18) }),
            });
            var leader = new EnemyDef("朽木", Element.Wood, 4, 0, tier: EnemyTier.Leader);
            var run = new RunEngine(graph, new RunConfig
                {
                    Encounters = new[] { new[] { leader, new EnemyDef("枯", Element.Wood, 4, 0) } },
                    RewardPool = new[] { "凿" },
                    FromDepth = 12,
                    KillInkDrops = true,
                },
                new BattleConfig { DropTable = new[] { "木" }, ApPerTurn = 10 },
                startingLibrary: new[] { "凿", "凿" }, startingPool: Array.Empty<string>(), seed: 7);
            Assert.That(run.KillInkFor(0), Is.EqualTo(4), "头领 ×2");
            Assert.That(run.KillInkFor(1), Is.EqualTo(2));
        }
    }
}
