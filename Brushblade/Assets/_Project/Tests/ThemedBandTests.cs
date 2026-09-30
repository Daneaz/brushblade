using System.Collections.Generic;
using System.IO;
using System.Linq;
using Brushblade.Core;
using Brushblade.Data;
using NUnit.Framework;

namespace Brushblade.CoreTests
{
    /// <summary>十层一主题(2026-09-30 用户拍板):1~50 层按相生序 木→火→土→金→水 分五段,
    /// 每段杂兵以本段属性为主、第 5 层精英与第 10 层主题 Boss 紧扣本段属性;51 层起「词渊」全池混合。
    /// 钉的是实船 enemies.json —— 改池子时这里先红。</summary>
    public sealed class ThemedBandTests
    {
        private static string ConfigDir()
        {
            var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Brushblade")))
                dir = dir.Parent;
            Assert.That(dir, Is.Not.Null, "找不到仓库根目录");
            return Path.Combine(dir.FullName, "Brushblade", "Assets", "StreamingAssets", "config");
        }

        private static EndlessConfig RealEndless() =>
            ConfigLoader.LoadCampaign(File.ReadAllText(Path.Combine(ConfigDir(), "enemies.json")),
                ConfigLoader.LoadGraph(File.ReadAllText(Path.Combine(ConfigDir(), "chars.json")))).Endless;

        private static readonly (string Name, int From, Element Element)[] Themes =
        {
            ("字林", 1, Element.Wood), ("朱砂", 11, Element.Fire), ("文山", 21, Element.Earth),
            ("金石", 31, Element.Metal), ("墨海", 41, Element.Water),
        };

        [Test]
        public void Bands_AreTenFloorsEach_InShengOrder_ThenEndlessMix()
        {
            var bands = RealEndless().Bands;
            Assert.That(bands.Count, Is.EqualTo(Themes.Length + 1));
            for (int i = 0; i < Themes.Length; i++)
            {
                Assert.That(bands[i].Name, Is.EqualTo(Themes[i].Name));
                Assert.That(bands[i].FromDepth, Is.EqualTo(Themes[i].From));
                Assert.That(bands[i].Element, Is.EqualTo(Themes[i].Element), $"{Themes[i].Name} 的段属性");
                Assert.That(bands[i].Flavor, Is.Not.Null.And.Not.Empty, $"{Themes[i].Name} 进段标题卡的风味文案");
                Assert.That(bands[i].MilestoneInk, Is.GreaterThan(0), $"{Themes[i].Name} 首破有奖励(弹窗才有内容)");
            }
            Assert.That(bands[^1].Name, Is.EqualTo("词渊"));
            Assert.That(bands[^1].FromDepth, Is.EqualTo(51));
        }

        /// <summary>「怪物占比以本段属性为主」:本段 10 层的普通层、每层 200 个种子,
        /// 出场杂兵里本段属性至少六成。</summary>
        [Test]
        public void ThemedBand_Minions_MostlyThemeElement()
        {
            var endless = RealEndless();
            foreach (var theme in Themes)
            {
                int themed = 0, total = 0;
                for (int depth = theme.From; depth < theme.From + 10; depth++)
                {
                    if (endless.IsBossDepth(depth)) continue;
                    for (int seed = 0; seed < 200; seed++)
                        foreach (var enemy in EndlessGenerator.BuildFloor(endless, depth, new GameRandom(seed)))
                        {
                            total++;
                            if (enemy.Element == theme.Element) themed++;
                        }
                }
                Assert.That(themed / (double)total, Is.GreaterThanOrEqualTo(0.6),
                    $"{theme.Name}:本段属性占比 {themed}/{total}");
            }
        }

        /// <summary>精英与主题 Boss 的阶段属性以本段属性为主(四阶段里至少两阶段)。</summary>
        [Test]
        public void ThemedBand_Bosses_MatchTheme()
        {
            var endless = RealEndless();
            foreach (var theme in Themes)
                foreach (int depth in new[] { theme.From + 4, theme.From + 9 })
                    for (int seed = 0; seed < 20; seed++)
                    {
                        var boss = EndlessGenerator.BuildFloor(endless, depth, new GameRandom(seed))[0];
                        Assert.That(boss.Phases.Count, Is.GreaterThan(0), $"{theme.Name} 第 {depth} 层首位是 Boss");
                        int matching = boss.Phases.Count(p => p.Element == theme.Element);
                        Assert.That(matching, Is.GreaterThanOrEqualTo(2),
                            $"{theme.Name} 第 {depth} 层 Boss「{boss.Id}」只有 {matching} 阶段是本段属性");
                    }
        }

        [Test]
        public void ThemedBand_EliteAndThemeBoss_Differ()
        {
            var endless = RealEndless();
            foreach (var theme in Themes)
            {
                var elite = EndlessGenerator.BuildFloor(endless, theme.From + 4, new GameRandom(1))[0].Id;
                var final = EndlessGenerator.BuildFloor(endless, theme.From + 9, new GameRandom(1))[0].Id;
                Assert.That(elite, Is.Not.EqualTo(final), $"{theme.Name}:第 5 层精英与第 10 层主题 Boss 不是同一只");
            }
        }

        [Test]
        public void EndlessMix_ContainsEveryMinionAndBoss()
        {
            var endless = RealEndless();
            var mix = endless.Bands[^1];
            var everyMinion = new HashSet<string>(endless.Bands.SelectMany(b => b.EnemyPool).Select(e => e.Id));
            Assert.That(everyMinion.SetEquals(mix.EnemyPool.Select(e => e.Id)), Is.True, "词渊收齐全部杂兵");
            var everyIdiom = endless.Bands.SelectMany(b => b.IdiomBossPool.Concat(b.EliteIdiomBossPool))
                .Select(i => i.Chars).Distinct();
            foreach (var idiom in everyIdiom)
                Assert.That(mix.IdiomBossPool.Any(i => i.Chars == idiom), Is.True, $"词渊收齐成语 Boss:{idiom}");
        }
    }
}
