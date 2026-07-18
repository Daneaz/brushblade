using Brushblade.Core;
using Brushblade.Data;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>无尽层段配置解析(20.3):enemies.json endless 段 → EndlessConfig。</summary>
    public class EndlessConfigTests
    {
        private static RecipeGraph Graph() => ConfigLoader.LoadGraph(@"{
            ""chars"": [ { ""id"": ""木"" }, { ""id"": ""灼"", ""element"": ""Fire"" } ] }");

        private const string BaseJson = @"{
            ""enemies"": [
                { ""id"": ""错字鬼"", ""element"": ""Wood"", ""maxHp"": 12, ""attack"": 4 },
                { ""id"": ""排山倒海"", ""element"": ""Water"", ""maxHp"": 12, ""attack"": 6 }
            ],
            ""dropTable"": [ ""木"" ],
            ""chapters"": [ { ""name"": ""字林"", ""enemyScale"": 1.0,
                ""stages"": [ { ""encounters"": [ [ ""错字鬼"" ] ] } ] } ],
            ""endless"": {
                ""bossEvery"": 5,
                ""scalePerDepth"": 0.12,
                ""bands"": [
                    { ""name"": ""字林"", ""fromDepth"": 1, ""enemyPool"": [ ""错字鬼"" ],
                      ""bossPool"": [ ""排山倒海"" ], ""rewardPool"": [ ""灼"" ], ""milestoneInk"": 0 },
                    { ""name"": ""词渊"", ""fromDepth"": 11, ""enemyPool"": [ ""错字鬼"" ],
                      ""bossPool"": [ ""排山倒海"" ], ""rewardPool"": [ ""灼"" ], ""milestoneInk"": 200 }
                ]
            } }";

        [Test]
        public void ParsesEndlessSection()
        {
            var campaign = ConfigLoader.LoadCampaign(BaseJson, Graph());
            var endless = campaign.Endless;
            Assert.That(endless, Is.Not.Null);
            Assert.That(endless.ScalePerDepth, Is.EqualTo(0.12f).Within(0.001f));
            Assert.That(endless.Bands.Count, Is.EqualTo(2));
            Assert.That(endless.Bands[1].Name, Is.EqualTo("词渊"));
            Assert.That(endless.Bands[1].FromDepth, Is.EqualTo(11));
            Assert.That(endless.Bands[1].MilestoneInk, Is.EqualTo(200));
            Assert.That(endless.Bands[0].EnemyPool[0].Attack, Is.EqualTo(4)); // 解析为 EnemyDef
            Assert.That(endless.Bands[0].BossPool[0].Id, Is.EqualTo("排山倒海"));
        }

        [Test]
        public void MissingEndlessSection_LeavesNull()
        {
            var json = BaseJson.Substring(0, BaseJson.IndexOf(@"""endless""")).TrimEnd().TrimEnd(',') + "}";
            var campaign = ConfigLoader.LoadCampaign(json, Graph());
            Assert.That(campaign.Endless, Is.Null);
        }

        [Test]
        public void ParsesIdiomBosses()
        {
            var json = BaseJson.Replace(@"""bossPool"": [ ""排山倒海"" ], ""rewardPool""",
                @"""bossPool"": [ ""排山倒海"" ], ""idiomBosses"": [ { ""chars"": ""刀山火海"", ""elements"": [ ""Metal"", ""Earth"", ""Fire"", ""Water"" ] } ], ""rewardPool""");
            var campaign = ConfigLoader.LoadCampaign(json, Graph());
            var idioms = campaign.Endless.Bands[0].IdiomBossPool;
            Assert.That(idioms.Count, Is.EqualTo(1));
            Assert.That(idioms[0].Chars, Is.EqualTo("刀山火海"));
            Assert.That(idioms[0].Elements[2], Is.EqualTo(Element.Fire));
        }

        [Test]
        public void IdiomBoss_WrongLength_Throws()
        {
            var json = BaseJson.Replace(@"""bossPool"": [ ""排山倒海"" ], ""rewardPool""",
                @"""bossPool"": [ ""排山倒海"" ], ""idiomBosses"": [ { ""chars"": ""刀山火"", ""elements"": [ ""Metal"", ""Earth"", ""Fire"" ] } ], ""rewardPool""");
            Assert.Throws<ConfigException>(() => ConfigLoader.LoadCampaign(json, Graph()));
        }

        [Test]
        public void Band_UnknownEnemy_Throws()
        {
            var json = BaseJson.Replace(@"""enemyPool"": [ ""错字鬼"" ]", @"""enemyPool"": [ ""不存在"" ]");
            Assert.Throws<ConfigException>(() => ConfigLoader.LoadCampaign(json, Graph()));
        }

        [Test]
        public void Band_UnknownRewardChar_Throws()
        {
            var json = BaseJson.Replace(@"""rewardPool"": [ ""灼"" ]", @"""rewardPool"": [ ""謎"" ]");
            Assert.Throws<ConfigException>(() => ConfigLoader.LoadCampaign(json, Graph()));
        }
    }
}
