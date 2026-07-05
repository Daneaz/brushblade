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

        // ---- 连战配置(enemies.json → RunConfig) ----

        private static RecipeGraph MiniGraph() => ConfigLoader.LoadGraph(
            @"{ ""chars"": [ { ""id"": ""灯"", ""element"": ""Fire"" } ] }");

        [Test]
        public void LoadRunConfig_ParsesEnemiesEncountersRewards()
        {
            var run = ConfigLoader.LoadRunConfig(@"{
                ""enemies"": [ { ""id"": ""错字鬼"", ""element"": ""Wood"", ""maxHp"": 10, ""attack"": 3 } ],
                ""encounters"": [ [ ""错字鬼"", ""错字鬼"" ] ],
                ""rewardPool"": [ ""灯"" ]
            }", MiniGraph());
            Assert.That(run.Encounters.Count, Is.EqualTo(1));
            Assert.That(run.Encounters[0].Count, Is.EqualTo(2));
            Assert.That(run.Encounters[0][0].Element, Is.EqualTo(Element.Wood));
            Assert.That(run.Encounters[0][0].MaxHp, Is.EqualTo(10));
            Assert.That(run.RewardPool, Is.EqualTo(new[] { "灯" }));
        }

        [Test]
        public void LoadRunConfig_EncounterReferencesUnknownEnemy_Throws()
        {
            var ex = Assert.Throws<ConfigException>(() => ConfigLoader.LoadRunConfig(@"{
                ""enemies"": [],
                ""encounters"": [ [ ""不存在"" ] ],
                ""rewardPool"": []
            }", MiniGraph()));
            Assert.That(ex.Message, Does.Contain("不存在"));
        }

        [Test]
        public void LoadRunConfig_RewardNotInGraph_Throws()
        {
            Assert.Throws<ConfigException>(() => ConfigLoader.LoadRunConfig(@"{
                ""enemies"": [], ""encounters"": [], ""rewardPool"": [ ""龘"" ]
            }", MiniGraph()));
        }

        [Test]
        public void ShippedEnemiesJson_LoadsAgainstShippedChars() // 实船双表交叉守卫
        {
            var graph = ConfigLoader.LoadGraph(File.ReadAllText(
                Path.Combine(Application.streamingAssetsPath, "config/chars.json")));
            var run = ConfigLoader.LoadRunConfig(File.ReadAllText(
                Path.Combine(Application.streamingAssetsPath, "config/enemies.json")), graph);
            Assert.That(run.Encounters.Count, Is.InRange(3, 5)); // 阶段 1 格式:3~5 场(17.2)
            Assert.That(run.RewardPool, Is.Not.Empty);
        }

        // ---- 实际配置表:StreamingAssets/config/chars.json 必须永远可加载 ----

        [Test]
        public void ShippedCharsJson_LoadsAndSupportsFireLoopExample() // 第 3 章 3.9 战例
        {
            var json = File.ReadAllText(
                Path.Combine(Application.streamingAssetsPath, "config/chars.json"));
            var graph = ConfigLoader.LoadGraph(json);

            // 焚 = 林+火,配方属性 {木,火} 构成木生火 → ×3
            Assert.That(graph.Get("焚").ApCost, Is.EqualTo(2));
            Assert.That(WuxingResolver.ShengMultiplier(graph.RecipeElements("焚")), Is.EqualTo(3));

            // 升阶链存在:火 → 炎 → 焱 → 燚
            foreach (var id in new[] { "火", "炎", "焱", "燚" })
                Assert.That(graph.TryGet(id, out _), Is.True, id);
        }
    }
}
