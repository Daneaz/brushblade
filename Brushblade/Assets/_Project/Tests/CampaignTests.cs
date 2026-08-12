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
        public void BuildRunConfig_ScalesEnemyStats() // F2 逐章加难
        {
            var run = Campaign().BuildRunConfig(1, 0);
            var enemy = run.Encounters[0][0];
            Assert.That(enemy.MaxHp, Is.EqualTo(18));  // 12 × 1.5
            Assert.That(enemy.Attack, Is.EqualTo(6));  // 4 × 1.5
        }

        /// <summary>缩放不许再向上取整(2026-08-12,E-b4/T1)。
        ///
        /// 旧口径是 <c>(int)Math.Ceiling(value × scale)</c>,它给低基础值的怪补了一份
        /// 「取整红利」,且 <c>ceil(10a·s) ≠ 10·ceil(a·s)</c> —— 全表量级 ×10 之后这份红利
        /// 必须消失,否则同一只怪在新旧量级下的相对强度对不上。
        ///
        /// 三条基准值全部选在「旧 ceil 与新口径可分辨」的点上,这条才有判别力:
        /// 换回 Ceiling 的话三条都会红。</summary>
        [Test]
        public void Scale_DoesNotRoundUp_NoCeilingBonus()
        {
            // 20 × 1.1 = 22 恰好是整数;旧 ceil 会读成 23(0.1f 不精确,乘积落在 22.000001)
            Assert.That(CampaignConfig.Scale(Enemy(20, 20), 1.1f).MaxHp, Is.EqualTo(22));
            // 140 × 1.1 = 154;旧 ceil 给 155
            Assert.That(CampaignConfig.Scale(Enemy(140, 30), 1.1f).MaxHp, Is.EqualTo(154));
            // 深度 20 的 scale = 2.9f;140 × 2.9f 实际算出 406.00001,旧 ceil 给 407
            Assert.That(CampaignConfig.Scale(Enemy(140, 30), 2.9f).MaxHp, Is.EqualTo(406));
        }

        /// <summary>缩放与「全表量级 ×10」可交换:<c>Scale(10a, s) == 10 × a × s</c>。
        /// 基础值全是 10 的倍数、scale 全是 0.1 的整数倍,乘积恒为整数,取整只夹 float 噪声。
        /// 遍历真实 enemies.json 的基础值 × 无尽 30 层,任何一处向上/向下偏一格都会红。</summary>
        [Test]
        public void Scale_IsExactOnTenfoldBases_AcrossDepths()
        {
            int[] bases = { 20, 30, 40, 50, 60, 70, 80, 100, 110, 120, 140, 150,
                            160, 180, 210, 220, 230, 240 };
            for (int depth = 1; depth <= 30; depth++)
            {
                float scale = 1f + 0.1f * (depth - 1);
                foreach (int b in bases)
                {
                    int expected = b / 10 * (9 + depth); // 精确值:b × (9+depth)/10
                    Assert.That(CampaignConfig.Scale(Enemy(b, b), scale).MaxHp, Is.EqualTo(expected),
                        $"基础 {b} 在深度 {depth} 上的缩放应精确无取整");
                }
            }
        }

        private static EnemyDef Enemy(int hp, int attack) =>
            new("桩", Element.Heart, hp, attack);

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
        public void LoadCampaign_ParsesMinionDamageTaken() // 小怪级承伤减免解析(墨渍)
        {
            var json = ValidJson.Replace(
                @"{ ""id"": ""错字鬼"", ""element"": ""Wood"", ""maxHp"": 12, ""attack"": 4 }",
                @"{ ""id"": ""错字鬼"", ""element"": ""Wood"", ""maxHp"": 12, ""attack"": 4, ""damageTaken"": 0.7 }");
            var campaign = ConfigLoader.LoadCampaign(json, MiniGraph());
            var enemy = campaign.Chapters[0].Stages[0].Encounters[0][0];
            Assert.That(enemy.DamageTaken, Is.EqualTo(0.7f).Within(1e-6));
        }

        [Test]
        public void Scale_PreservesDamageTaken() // 缩放不得丢承伤系数(端游无尽全走 Scale)
        {
            var scaled = CampaignConfig.Scale(
                new EnemyDef("墨渍", Element.Water, 14, 3, damageTaken: 0.7f), 2f);
            Assert.That(scaled.DamageTaken, Is.EqualTo(0.7f).Within(1e-6));
            Assert.That(scaled.MaxHp, Is.EqualTo(28)); // 14×2 缩放照常
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
