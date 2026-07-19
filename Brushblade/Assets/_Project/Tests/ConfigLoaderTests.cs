using System.IO;
using System.Linq;
using Brushblade.Core;
using Brushblade.Data;
using NUnit.Framework;
using UnityEngine;

namespace Brushblade.Core.Tests
{
    /// <summary>配置加载:JSON → RecipeGraph,非法数据 fail fast(architecture §3)。</summary>
    public class ConfigLoaderTests
    {
        [Test]
        public void LoadGraph_ParsesCharWithAllFields()
        {
            var graph = ConfigLoader.LoadGraph(@"{
                ""chars"": [
                    { ""id"": ""火"", ""element"": ""Fire"",
                      ""effects"": [ { ""kind"": ""DamageSingle"", ""value"": 4 } ] },
                    { ""id"": ""林"", ""element"": ""Wood"", ""recipe"": [ ""木"", ""木"" ] },
                    { ""id"": ""木"", ""element"": ""Wood"" }
                ]
            }");
            var fire = graph.Get("火");
            Assert.That(fire.Element, Is.EqualTo(Element.Fire));
            Assert.That(fire.IsLeaf, Is.True);
            Assert.That(fire.ApCost, Is.EqualTo(1)); // 缺省 1
            Assert.That(fire.Effects.Single().Kind, Is.EqualTo(EffectKind.DamageSingle));
            Assert.That(fire.Effects.Single().Value, Is.EqualTo(4));
            Assert.That(graph.Get("林").Recipe, Is.EqualTo(new[] { "木", "木" }));
        }

        [Test]
        public void LoadGraph_MissingElement_IsNeutral()
        {
            var graph = ConfigLoader.LoadGraph(@"{ ""chars"": [ { ""id"": ""丁"" } ] }");
            Assert.That(graph.Get("丁").Element, Is.Null);
        }

        [Test]
        public void LoadGraph_UnknownElement_Throws()
        {
            var ex = Assert.Throws<ConfigException>(() => ConfigLoader.LoadGraph(
                @"{ ""chars"": [ { ""id"": ""謎"", ""element"": ""Void"" } ] }"));
            Assert.That(ex.Message, Does.Contain("謎"));
        }

        [Test]
        public void LoadGraph_ParsesEffectFlags() // 灼/堡的条件标志
        {
            var graph = ConfigLoader.LoadGraph(@"{
                ""chars"": [
                    { ""id"": ""灼"", ""element"": ""Fire"",
                      ""effects"": [ { ""kind"": ""DamageSingle"", ""value"": 8, ""doubleVsBurning"": true } ] },
                    { ""id"": ""堡"", ""element"": ""Earth"",
                      ""effects"": [ { ""kind"": ""Shield"", ""value"": 10, ""persistOnce"": true } ] }
                ]
            }");
            Assert.That(graph.Get("灼").Effects.Single().DoubleVsBurning, Is.True);
            Assert.That(graph.Get("灼").Effects.Single().PersistOnce, Is.False);
            Assert.That(graph.Get("堡").Effects.Single().PersistOnce, Is.True);
        }

        [Test]
        public void LoadGraph_UnknownEffectKind_Throws()
        {
            Assert.Throws<ConfigException>(() => ConfigLoader.LoadGraph(
                @"{ ""chars"": [ { ""id"": ""火"", ""effects"": [ { ""kind"": ""Explode"", ""value"": 1 } ] } ] }"));
        }

        [Test]
        public void LoadGraph_RecipeReferencesUndefinedChar_Throws() // fail fast 二次校验
        {
            var ex = Assert.Throws<ConfigException>(() => ConfigLoader.LoadGraph(
                @"{ ""chars"": [ { ""id"": ""林"", ""recipe"": [ ""木"", ""木"" ] } ] }"));
            Assert.That(ex.Message, Does.Contain("木"));
        }

        [Test]
        public void LoadGraph_DuplicateId_Throws()
        {
            Assert.Throws<ConfigException>(() => ConfigLoader.LoadGraph(
                @"{ ""chars"": [ { ""id"": ""火"" }, { ""id"": ""火"" } ] }"));
        }

        [Test]
        public void LoadGraph_MalformedJson_Throws()
        {
            Assert.Throws<ConfigException>(() => ConfigLoader.LoadGraph("not json"));
        }

        // ---- 战役配置实船守卫(嵌入式解析测试见 CampaignTests,纯 C# 可在工装跑) ----

        [Test]
        public void ShippedCampaignJson_LoadsAgainstShippedChars() // 实船双表交叉守卫
        {
            var graph = ConfigLoader.LoadGraph(File.ReadAllText(
                Path.Combine(Application.streamingAssetsPath, "config/chars.json")));
            var campaign = ConfigLoader.LoadCampaign(File.ReadAllText(
                Path.Combine(Application.streamingAssetsPath, "config/enemies.json")), graph);
            Assert.That(campaign.Chapters.Count, Is.EqualTo(3));          // 首发 3 章(17 章 v0.5)
            foreach (var chapter in campaign.Chapters)
            {
                Assert.That(chapter.Stages.Count, Is.EqualTo(5));         // 每章 5 关
                Assert.That(chapter.Stages[4].Boss, Is.True);             // 章末 Boss 关
                Assert.That(chapter.RewardPool, Is.Not.Empty);            // 字池分章投放(F3)
            }
            Assert.That(campaign.DropTable, Is.Not.Empty);
        }

        // ---- 实际配置表:StreamingAssets/config/chars.json 必须永远可加载 ----

        [Test]
        public void ShippedCharsJson_LoadsFiveElementLadders() // 首发字库:5 系 × 2/3/4 叠
        {
            var json = File.ReadAllText(
                Path.Combine(Application.streamingAssetsPath, "config/chars.json"));
            var graph = ConfigLoader.LoadGraph(json);

            // 每系升阶链存在:部件 → 2叠 → 3叠 → 4叠(四金/四木为 PUA 显示代理)
            var ladders = new[]
            {
                new[] { "金", "鍂", "鑫", "" },
                new[] { "木", "林", "森", "" },
                new[] { "水", "沝", "淼", "㵘" },
                new[] { "火", "炎", "焱", "燚" },
                new[] { "土", "圭", "垚", "㙓" },
            };
            // 稀有度阶梯(2026-07-19 拍板):部件白 / 2叠绿 / 3叠蓝 / 4叠紫
            var rarities = new[] { CardRarity.White, CardRarity.Green, CardRarity.Blue, CardRarity.Purple };
            foreach (var ladder in ladders)
                for (int i = 0; i < ladder.Length; i++)
                {
                    Assert.That(graph.TryGet(ladder[i], out var def), Is.True, ladder[i]);
                    Assert.That(def.Rarity, Is.EqualTo(rarities[i]), ladder[i]);
                    if (i == 0) continue;
                    // 链式配方:上一阶 + 部件
                    Assert.That(def.Recipe, Is.EqualTo(new[] { ladder[i - 1], ladder[0] }));
                }

            // 3/4 叠为高阶字:出字 2 AP
            Assert.That(graph.Get("焱").ApCost, Is.EqualTo(2));
            Assert.That(graph.Get("燚").ApCost, Is.EqualTo(2));
        }

        /// <summary>首局教程剧本(拆炎→合炎→出炎)必须能打通首层敌人,否则新手卡死。
        /// 「只能合已收集的字」后首局只有 2 叠可用,这里守住实船数值(步骤机测试见 TutorialTests)。</summary>
        [Test]
        public void ShippedConfig_FirstTowerTutorial_CanClearFloorOne()
        {
            var configDir = Path.Combine(Application.streamingAssetsPath, "config");
            var graph = ConfigLoader.LoadGraph(File.ReadAllText(Path.Combine(configDir, "chars.json")));
            var campaign = ConfigLoader.LoadCampaign(
                File.ReadAllText(Path.Combine(configDir, "enemies.json")), graph);

            var segment = EndlessGenerator.BuildFirstTowerSegment(campaign.Endless, seed: 7);
            var floorOne = segment.Encounters[0];
            Assert.That(floorOne.Count, Is.EqualTo(1)); // 首层单敌

            var battle = new BattleEngine(graph,
                new BattleConfig
                {
                    DropTable = campaign.DropTable,
                    UnlockedChars = new[] { "鍂", "林", "沝", "炎", "圭" }, // 初始出阵列表
                },
                new[] { "炎" }, new[] { "火", "火" }, floorOne, seed: 7);

            Assert.That(battle.Compose("焱"), Is.EqualTo(BattleError.ForgeFailed)); // 不在出阵,合不出
            Assert.That(battle.Dismantle("炎"), Is.EqualTo(BattleError.None));
            Assert.That(battle.Compose("炎"), Is.EqualTo(BattleError.None));
            Assert.That(battle.Cast("炎"), Is.EqualTo(BattleError.None));
            if (battle.Phase == BattlePhase.PlayerTurn)
                battle.EndTurn(); // 直伤不足则灼烧补刀
            Assert.That(battle.Phase, Is.EqualTo(BattlePhase.Won));
        }
    }
}
