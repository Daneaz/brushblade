using System.Linq;
using Brushblade.Core;
using Brushblade.Data;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>章节结构(19.1):BuildRunConfig 装配 + LoadCampaign 解析校验。纯测试,无 UnityEngine。</summary>
    public class CampaignTests
    {
        private static EnemyDef Ghost() => new("错字鬼", Element.Wood, 12, 4);

        private static CampaignConfig Campaign() => new()
        {
            DropTable = new[] { "木", "火" },
            Chapters = new[]
            {
                new ChapterDef
                {
                    Name = "蒙学", EnemyScale = 1f,
                    Stages = new[]
                    {
                        new StageDef { Encounters = new[] { new[] { Ghost() } } },
                        new StageDef { Encounters = new[] { new[] { Ghost(), Ghost() } }, Boss = true },
                    },
                    RewardPool = new[] { "灯" },
                },
                new ChapterDef
                {
                    Name = "字林", EnemyScale = 1.5f,
                    Stages = new[] { new StageDef { Encounters = new[] { new[] { Ghost() } } } },
                    RewardPool = new[] { "炎" },
                },
            },
        };

        [Test]
        public void BuildRunConfig_UsesStageEncounters_AndChapterRewardPool()
        {
            var run = Campaign().BuildRunConfig(0, 1);
            Assert.That(run.Encounters.Count, Is.EqualTo(1));
            Assert.That(run.Encounters[0].Count, Is.EqualTo(2));
            Assert.That(run.RewardPool, Is.EqualTo(new[] { "灯" }));
        }

        [Test]
        public void BuildRunConfig_ScalesEnemyStats_CeilRounded() // F2 逐章加难
        {
            var run = Campaign().BuildRunConfig(1, 0);
            var enemy = run.Encounters[0][0];
            Assert.That(enemy.MaxHp, Is.EqualTo(18));  // 12 × 1.5
            Assert.That(enemy.Attack, Is.EqualTo(6));  // 4 × 1.5
        }

        [Test]
        public void BuildRunConfig_DoesNotMutateBaseDefs()
        {
            var campaign = Campaign();
            campaign.BuildRunConfig(1, 0);
            var unscaled = campaign.BuildRunConfig(0, 0).Encounters[0][0];
            Assert.That(unscaled.MaxHp, Is.EqualTo(12));
        }

        // ---- LoadCampaign(嵌入 JSON,纯解析) ----

        private static RecipeGraph MiniGraph() => ConfigLoader.LoadGraph(
            @"{ ""chars"": [ { ""id"": ""灯"", ""element"": ""Fire"" }, { ""id"": ""木"", ""element"": ""Wood"" } ] }");

        private const string ValidJson = @"{
            ""enemies"": [ { ""id"": ""错字鬼"", ""element"": ""Wood"", ""maxHp"": 12, ""attack"": 4 } ],
            ""dropTable"": [ ""木"" ],
            ""chapters"": [
                { ""name"": ""蒙学"", ""enemyScale"": 1.0,
                  ""stages"": [
                    { ""encounters"": [ [ ""错字鬼"" ], [ ""错字鬼"", ""错字鬼"" ] ] },
                    { ""encounters"": [ [ ""错字鬼"" ] ], ""boss"": true }
                  ],
                  ""rewardPool"": [ ""灯"" ] }
            ]
        }";

        [Test]
        public void LoadCampaign_ParsesChaptersStagesDropTable()
        {
            var campaign = ConfigLoader.LoadCampaign(ValidJson, MiniGraph());
            Assert.That(campaign.DropTable, Is.EqualTo(new[] { "木" }));
            Assert.That(campaign.Chapters.Count, Is.EqualTo(1));
            Assert.That(campaign.Chapters[0].Name, Is.EqualTo("蒙学"));
            Assert.That(campaign.Chapters[0].Stages.Count, Is.EqualTo(2));
            Assert.That(campaign.Chapters[0].Stages[1].Boss, Is.True);
            Assert.That(campaign.Chapters[0].Stages[0].Encounters[1].Count, Is.EqualTo(2));
        }

        [Test]
        public void LoadCampaign_UnknownEnemyInStage_Throws()
        {
            var json = ValidJson.Replace(@"[ ""错字鬼"", ""错字鬼"" ]", @"[ ""不存在"" ]");
            var ex = Assert.Throws<ConfigException>(() => ConfigLoader.LoadCampaign(json, MiniGraph()));
            Assert.That(ex.Message, Does.Contain("不存在"));
        }

        [Test]
        public void LoadCampaign_DropTableRefNotInGraph_Throws()
        {
            var json = ValidJson.Replace(@"""dropTable"": [ ""木"" ]", @"""dropTable"": [ ""龘"" ]");
            Assert.Throws<ConfigException>(() => ConfigLoader.LoadCampaign(json, MiniGraph()));
        }

        [Test]
        public void LoadCampaign_RewardRefNotInGraph_Throws()
        {
            var json = ValidJson.Replace(@"""rewardPool"": [ ""灯"" ]", @"""rewardPool"": [ ""龘"" ]");
            Assert.Throws<ConfigException>(() => ConfigLoader.LoadCampaign(json, MiniGraph()));
        }

        [Test]
        public void LoadCampaign_EmptyChapters_Throws()
        {
            Assert.Throws<ConfigException>(() => ConfigLoader.LoadCampaign(
                @"{ ""enemies"": [], ""dropTable"": [], ""chapters"": [] }", MiniGraph()));
        }
    }
}
