using System.Collections.Generic;
using System.IO;
using System.Linq;
using Brushblade.Core;
using Brushblade.Data;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>奇遇名册对账(2026-10-21):读真实 enemies.json,钉 20 个奇遇的等级、层数门槛、
    /// 选项 3 统一为「离开」(三个负面奇遇例外)与触发概率 30%。</summary>
    public class EventRosterTests
    {
        private static CampaignConfig Real()
        {
            // 锚点只能是 TestContext.CurrentContext.TestDirectory
            var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Brushblade")))
                dir = dir.Parent;
            Assert.That(dir, Is.Not.Null, "找不到仓库根目录");
            var configDir = Path.Combine(dir.FullName, "Brushblade", "Assets", "StreamingAssets", "config");
            var graph = ConfigLoader.LoadGraph(File.ReadAllText(Path.Combine(configDir, "chars.json")));
            return ConfigLoader.LoadCampaign(File.ReadAllText(Path.Combine(configDir, "enemies.json")), graph);
        }

        private static readonly Dictionary<string, int> ExpectedTier = new()
        {
            ["篝火"] = 1, ["宝库"] = 1, ["换气"] = 1, ["废稿堆"] = 1, ["赌书"] = 1, ["淬骨"] = 1, ["当铺"] = 1, ["拦路虎"] = 1,
            ["测字先生"] = 2, ["墨池"] = 2, ["书院"] = 2, ["残碑"] = 2, ["养气"] = 2, ["瘴林"] = 2,
            ["字摊"] = 3, ["药铺"] = 3, ["查禁"] = 3,
            ["老字号"] = 4, ["古战场"] = 4, ["炼字炉"] = 4,
        };

        private static readonly string[] Negative = { "拦路虎", "瘴林", "查禁" };
        private static readonly string[] DeepOnly = { "老字号", "古战场", "炼字炉" };

        [Test]
        public void Roster_TwentyUniqueIds()
        {
            var events = Real().Events;
            Assert.That(events.Count, Is.EqualTo(20));
            Assert.That(events.Select(e => e.Id).Distinct().Count(), Is.EqualTo(20), "id 唯一(续爬按 id 恢复)");
        }

        [Test]
        public void Roster_TiersMatchSpec()
        {
            var byId = Real().Events.ToDictionary(e => e.Id);
            foreach (var kv in ExpectedTier)
            {
                Assert.That(byId.ContainsKey(kv.Key), Is.True, $"缺奇遇 {kv.Key}");
                Assert.That(byId[kv.Key].Tier, Is.EqualTo(kv.Value), kv.Key);
            }
            Assert.That(byId.Count, Is.EqualTo(ExpectedTier.Count));
        }

        [Test]
        public void Roster_DepthGates()
        {
            foreach (var e in Real().Events)
            {
                Assert.That(e.MinDepth, Is.EqualTo(DeepOnly.Contains(e.Id) ? 25 : 0), e.Id);
                Assert.That(e.MaxDepth, Is.EqualTo(0), e.Id);
            }
        }

        private static bool HasNoEffect(EventOption o) =>
            o.HpDelta == 0 && o.Ink == 0 && o.InkCost == 0 && o.ComponentCost == 0 && o.LibraryCost == 0
            && o.NextBattleSeal == 0 && o.MaxHpPercent == 0 && o.RandomComponents == 0
            && o.GainChar == null && o.GainComponents.Count == 0 && o.GainCharChoices.Count == 0;

        [Test]
        public void Roster_ThirdOptionIsLeave()
        {
            foreach (var e in Real().Events.Where(e => !Negative.Contains(e.Id)))
            {
                Assert.That(e.Options.Count, Is.EqualTo(3), e.Id);
                Assert.That(HasNoEffect(e.Options[2]), Is.True, $"{e.Id} 选项 3 应为无效果的离开");
            }
        }

        [Test]
        public void Roster_NegativeEventsAlwaysHaveAnOption()
        {
            var byId = Real().Events.ToDictionary(e => e.Id);
            foreach (var id in Negative)
            {
                Assert.That(byId[id].Options.Count, Is.EqualTo(3), id);
                Assert.That(byId[id].Options[2].HpDelta, Is.LessThan(0), $"{id} 选项 3 是扣血兜底,不吃资源门槛");
            }
        }

        [Test]
        public void Roster_EventChanceIs30()
        {
            Assert.That(Real().EventChancePercent, Is.EqualTo(30));
        }

        [Test]
        public void Roster_NewCostsAreWired()
        {
            var byId = Real().Events.ToDictionary(e => e.Id);
            Assert.That(byId["药铺"].Options[0].NextBattleSeal, Is.EqualTo(1));
            Assert.That(byId["药铺"].Options[1].NextBattleSeal, Is.EqualTo(1));
            Assert.That(byId["瘴林"].Options[1].NextBattleSeal, Is.EqualTo(1));
            Assert.That(byId["拦路虎"].Options[1].LibraryCost, Is.EqualTo(1));
            Assert.That(byId["查禁"].Options[0].LibraryCost, Is.EqualTo(1));
        }
    }
}
