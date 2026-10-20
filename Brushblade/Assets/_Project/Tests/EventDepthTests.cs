using System;
using System.IO;
using System.Linq;
using Brushblade.Core;
using Brushblade.Data;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>奇遇按层数出现(2026-10-20):字摊拆成两档 —— 1~19 层出金字版「字摊」,
    /// 20 层起出橙字版「老字号」。门槛按刚打完那一层(CurrentDepth)判,0 = 不设限。</summary>
    public class EventDepthTests
    {
        private static RecipeGraph Graph() => new(new[]
        {
            new CharDef("木", Element.Wood),
            new CharDef("火", Element.Fire),
            new CharDef("林", Element.Wood, new[] { "木", "木" }),
            new CharDef("焚", Element.Fire, new[] { "林", "火" }, rarity: CardRarity.Purple,
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 18, shape: TargetArea.All) }),
        });

        private static EventDef Low() => new()
        {
            Id = "低", Text = "…", MaxDepth = 19,
            Options = new[] { new EventOption { Label = "离开" } },
        };

        private static EventDef High() => new()
        {
            Id = "高", Text = "…", MinDepth = 20,
            Options = new[] { new EventOption { Label = "离开" } },
        };

        /// <summary>从 fromDepth 开一段两层的连战,打赢第一层后停在奇遇或选字。</summary>
        private static RunEngine WinFirstFloor(int fromDepth, int seed, params EventDef[] pool)
        {
            var config = new RunConfig
            {
                Encounters = new[]
                {
                    new[] { new EnemyDef("枯", Element.Wood, 4, 2) },
                    new[] { new EnemyDef("枯", Element.Wood, 4, 2) },
                },
                RewardPool = new[] { "焚" },
                EventPool = pool,
                EventChancePercent = 100,
                FromDepth = fromDepth,
            };
            var run = new RunEngine(Graph(), config, new BattleConfig(),
                new[] { "焚" }, Array.Empty<string>(), seed);
            Assert.That(run.Battle.Cast("焚"), Is.EqualTo(BattleError.None));
            run.AdvanceAfterBattle();
            return run;
        }

        [Test]
        public void MinDepth_BlocksBelowThreshold()
        {
            Assert.That(WinFirstFloor(19, 7, High()).Phase, Is.EqualTo(RunPhase.Reward), "19 层不出 20 层起的奇遇");
            Assert.That(WinFirstFloor(20, 7, High()).Phase, Is.EqualTo(RunPhase.Event), "20 层起照常出");
        }

        [Test]
        public void MaxDepth_BlocksAboveThreshold()
        {
            Assert.That(WinFirstFloor(19, 7, Low()).Phase, Is.EqualTo(RunPhase.Event), "19 层照常出");
            Assert.That(WinFirstFloor(20, 7, Low()).Phase, Is.EqualTo(RunPhase.Reward), "20 层起不再出");
        }

        [Test]
        public void MixedPool_OnlyDrawsEligible()
        {
            for (int seed = 0; seed < 50; seed++)
            {
                Assert.That(WinFirstFloor(19, seed, Low(), High()).CurrentEvent.Id, Is.EqualTo("低"), $"seed {seed}");
                Assert.That(WinFirstFloor(21, seed, Low(), High()).CurrentEvent.Id, Is.EqualTo("高"), $"seed {seed}");
            }
        }

        private static CampaignConfig RealCampaign()
        {
            // 锚点只能是 TestContext.CurrentContext.TestDirectory(AppContext.BaseDirectory 在 Unity 下指向编辑器目录)
            var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Brushblade")))
                dir = dir.Parent;
            Assert.That(dir, Is.Not.Null, "找不到仓库根目录");
            var configDir = Path.Combine(dir.FullName, "Brushblade", "Assets", "StreamingAssets", "config");
            var graph = ConfigLoader.LoadGraph(File.ReadAllText(Path.Combine(configDir, "chars.json")));
            return ConfigLoader.LoadCampaign(File.ReadAllText(Path.Combine(configDir, "enemies.json")), graph);
        }

        [Test]
        public void RealConfig_StallSplitByDepth()
        {
            var events = RealCampaign().Events;
            var low = events.Single(e => e.Id == "字摊");
            var high = events.Single(e => e.Id == "老字号");

            Assert.That((low.MinDepth, low.MaxDepth), Is.EqualTo((0, 19)));
            Assert.That((high.MinDepth, high.MaxDepth), Is.EqualTo((20, 0)));

            // 低层:舍 1 部件换金字任选 / 白讨随机部件 —— 前期拿不到橙字
            Assert.That(low.Options[0].ComponentCost, Is.EqualTo(1));
            Assert.That(low.Options[0].GainCharChoices, Is.EquivalentTo(new[] { "鍂", "林", "冰", "炎", "圭" }));
            Assert.That(low.Options[1].RandomComponents, Is.EqualTo(1));
            Assert.That(low.Options[1].GainCharChoices, Is.Empty);

            // 高层:舍 1 部件换橙字任选 / 白讨金字任选
            Assert.That(high.Options[0].ComponentCost, Is.EqualTo(1));
            Assert.That(high.Options[0].GainCharChoices, Is.EquivalentTo(new[] { "鑫", "森", "淼", "焱", "垚" }));
            Assert.That(high.Options[1].GainCharChoices, Is.EquivalentTo(new[] { "鍂", "林", "冰", "炎", "圭" }));

            Assert.That(events.Select(e => e.Id).Distinct().Count(), Is.EqualTo(events.Count), "奇遇 id 唯一(续爬按 id 恢复)");
        }
    }
}
