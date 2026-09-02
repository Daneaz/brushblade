using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Brushblade.Core;
using Brushblade.Data;
using NUnit.Framework;

namespace Brushblade.CoreTests
{
    /// <summary>逐怪出场深度闸(2026-09-02)。层段(band)整段共用一个 enemyPool,
    /// 表达不了段内深度差 —— 「低阶护甲怪 1-5 层不出、6 层起可出」这种需求只能靠给
    /// EnemyDef 加一个 MinDepth,编成时按当前深度过滤候选池(Endless.WithinDepth)。</summary>
    public sealed class ArmoredEnemyTests
    {
        // ---- 真实配置读取(照 DefenseValuesTests 抄) ----

        private static string ConfigDir()
        {
            var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Brushblade")))
                dir = dir.Parent;
            Assert.That(dir, Is.Not.Null, "找不到仓库根目录");
            return Path.Combine(dir.FullName, "Brushblade", "Assets", "StreamingAssets", "config");
        }

        private static RecipeGraph RealGraph() =>
            ConfigLoader.LoadGraph(File.ReadAllText(Path.Combine(ConfigDir(), "chars.json")));

        private static EndlessConfig LoadRealEndlessConfig() =>
            ConfigLoader.LoadCampaign(File.ReadAllText(Path.Combine(ConfigDir(), "enemies.json")), RealGraph())
                .Endless;

        private static CampaignConfig LoadRealCampaign() =>
            ConfigLoader.LoadCampaign(File.ReadAllText(Path.Combine(ConfigDir(), "enemies.json")), RealGraph());

        /// <summary>enemies.json 里出现过的全部敌人(去重;照 DefenseValuesTests.AllEnemies 抄)——
        /// 以 endless.bands 的 enemyPool/bossPool 为准,因为无尽层段是 v0.7 的唯一核心玩法。</summary>
        private static List<EnemyDef> AllEnemies()
        {
            var campaign = LoadRealCampaign();
            var seen = new Dictionary<string, EnemyDef>();
            foreach (var band in campaign.Endless?.Bands ?? (IReadOnlyList<BandDef>)Array.Empty<BandDef>())
            {
                foreach (var enemy in band.EnemyPool) seen[enemy.Id] = enemy;
                foreach (var boss in band.BossPool) seen[boss.Id] = boss;
            }
            return seen.Values.ToList();
        }

        [Test]
        public void ArmoredEnemies_CoverAllFiveElementsAtBothTiers()
        {
            var armored = AllEnemies().Where(e => e.Defense > 0).ToList();
            foreach (var element in new[]
                     {
                         Element.Wood, Element.Fire, Element.Earth, Element.Metal, Element.Water,
                     })
            {
                var low = armored.Where(e => e.Element == element && e.Defense < 30).ToList();
                var high = armored.Where(e => e.Element == element && e.Defense >= 30).ToList();
                Assert.That(low.Count, Is.GreaterThanOrEqualTo(1), $"{element} 缺低阶护甲怪");
                Assert.That(high.Count, Is.GreaterThanOrEqualTo(1), $"{element} 缺高阶护甲怪");
            }
        }

        [Test]
        public void LowTierArmoredEnemies_HaveMinDepthSix()
        {
            var all = AllEnemies();
            foreach (var id in new[] { "枯笔", "火漆", "砚台", "铜钤", "墨渍" })
            {
                var enemy = all.First(e => e.Id == id);
                Assert.That(enemy.MinDepth, Is.GreaterThanOrEqualTo(6),
                    $"{id} 是低阶护甲怪,不该在 1-5 层出现");
            }
        }

        [Test]
        public void EveryFloor_HasAtMostOneArmoredEnemy()
        {
            // WithoutArmor 闸(既有)在补齐 10 只后仍须成立 ——
            // 点数护甲对 AOE 有 N 倍惩罚,带甲成群会把 AOE 流派打废。
            var config = LoadRealEndlessConfig();
            for (int depth = 1; depth <= 60; depth++)
            {
                var segment = EndlessGenerator.BuildSegment(config, depth, seed: depth * 7919);
                foreach (var floor in segment.Encounters)
                    Assert.That(floor.Count(e => e.Defense > 0), Is.LessThanOrEqualTo(1),
                        $"第 {depth} 层出现了多只带甲怪");
            }
        }

        [Test]
        public void MinDepth_FiltersEnemiesOutOfEarlyFloors()
        {
            var config = LoadRealEndlessConfig();
            // 1-5 层不出任何带甲怪。2026-09-02 起 enemies.json 里 5 只低阶护甲怪
            // (枯笔/火漆/砚台/铜钤/墨渍)都配了 minDepth: 6,这条在真实配置上
            // 已经是深度闸真正生效的判据,不再是空操作。
            for (int depth = 1; depth <= 5; depth++)
            {
                // Boss 层直接 return,不走深度闸(见 Endless.BuildFloor 的 IsBossDepth 分支)。
                // Boss 的护甲挂在 BossPhaseDef.Defense 上,EnemyDef.Defense 本身仍是 0 ——
                // 断言在 Boss 层碰巧也成立,但那是巧合不是深度闸的功劳,显式跳过不测。
                if (config.IsBossDepth(depth)) continue;

                var floor = EndlessGenerator.BuildFloor(config, depth, new GameRandom(12345 + depth));
                foreach (var enemy in floor)
                    Assert.That(enemy.Defense, Is.EqualTo(0),
                        $"第 {depth} 层不该有带甲怪:{enemy.Id}");
            }
        }

        [Test]
        public void MinDepth_IsPreservedThroughScale()
        {
            // Scale 会重建 EnemyDef —— 漏传 MinDepth 不会报错,只会静默失效
            var def = new EnemyDef("测", Element.Earth, 100, 10, minDepth: 6);
            var scaled = CampaignConfig.Scale(def, 2.0f);
            Assert.That(scaled.MinDepth, Is.EqualTo(6));
        }

        [Test]
        public void MinDepth_GateActuallyExcludesEnemyBeforeItsDepth()
        {
            // 自造一个 2 怪的池子:一只随时可出,一只 6 层才解锁且带甲。
            // BossEvery 拉到 100 让 1-5 层全部避开 Boss 分支,专测深度闸本身。
            var open = new EnemyDef("拓", Element.Earth, 50, 5);
            var gated = new EnemyDef("甲", Element.Earth, 50, 5, defense: 5, minDepth: 6);
            var config = new EndlessConfig
            {
                BossEvery = 100,
                Bands = new[]
                {
                    new BandDef { Name = "测试段", FromDepth = 1,
                        EnemyPool = new[] { open, gated }, BossPool = new[] { open } },
                },
            };

            for (int depth = 1; depth <= 5; depth++)
                for (int seed = 0; seed < 20; seed++)
                {
                    var floor = EndlessGenerator.BuildFloor(config, depth, new GameRandom(seed));
                    Assert.That(floor.Any(e => e.Id == gated.Id), Is.False,
                        $"第 {depth} 层不该出「{gated.Id}」(minDepth=6):seed {seed}");
                }
        }

        [Test]
        public void FirstTowerSegment_StillUsesTheThreeTutorialEnemies()
        {
            // 首塔前 3 层取 Bands[0].EnemyPool 的下标 0/1/2。
            // 新怪必须追加在池尾,否则会挤掉引导用的那三只。
            var config = LoadRealEndlessConfig();
            var segment = EndlessGenerator.BuildFirstTowerSegment(config, seed: 1);
            Assert.That(segment.Encounters[0][0].Id, Is.EqualTo("错字鬼"));
            Assert.That(segment.Encounters[0][0].Defense, Is.EqualTo(0));
        }
    }
}
