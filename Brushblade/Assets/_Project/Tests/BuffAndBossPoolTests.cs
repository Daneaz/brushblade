using System;
using System.Linq;
using Brushblade.Core;
using Brushblade.Data;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>标点小妖加 buff(8.3)与 Boss 池随机(8.5.3)。</summary>
    public class BuffAndBossPoolTests
    {
        private static RecipeGraph Graph() => new(new[]
        {
            new CharDef("火", Element.Fire,
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 4) }),
        });

        private static EnemyDef Buffer() => new("标点小妖", Element.Heart, 8, 1, EnemyAbility.Buff);
        private static EnemyDef Ghost() => new("错字鬼", Element.Wood, 12, 4);

        private static BattleEngine Engine(params EnemyDef[] enemies) =>
            new(Graph(), new BattleConfig(), Array.Empty<string>(), new[] { "火" }, enemies, seed: 1);

        // ---- 标点小妖:加 buff ----

        [Test]
        public void Buffer_DoesNotAttackPlayer_BuffsOthers()
        {
            var engine = Engine(Buffer(), Ghost(), Ghost());
            engine.EndTurn();

            Assert.That(engine.PlayerHp, Is.EqualTo(50 - 5 - 5)); // 辅助先行动:buff 当回合生效
            Assert.That(engine.Enemies[1].Attack, Is.EqualTo(5)); // 4 + 1
            Assert.That(engine.Enemies[2].Attack, Is.EqualTo(5));
            Assert.That(engine.Enemies[0].Attack, Is.EqualTo(1)); // 自己不加
            Assert.That(engine.LastEvents.Count(e => e.Kind == BattleEventKind.EnemyBuff), Is.EqualTo(2));
        }

        [Test]
        public void Buffer_Alone_DoesNothing()
        {
            var engine = Engine(Buffer());
            engine.EndTurn();
            Assert.That(engine.PlayerHp, Is.EqualTo(50));
            Assert.That(engine.LastEvents.Any(e => e.Kind == BattleEventKind.EnemyAttack), Is.False);
        }

        [Test]
        public void Buffer_DoesNotBuffDead()
        {
            var engine = Engine(Buffer(), new EnemyDef("枯", Element.Wood, 4, 3), Ghost());
            engine.Cast("火", 1); // 杀掉 4 血的
            engine.EndTurn();
            Assert.That(engine.Enemies[2].Attack, Is.EqualTo(5)); // 活的被加
            Assert.That(engine.Enemies[1].Attack, Is.EqualTo(3)); // 尸体不加
        }

        // ---- Boss 池随机 ----

        private static CampaignConfig WithBossPool()
        {
            var bosses = new[]
            {
                new EnemyDef("排山倒海", Element.Water, 12, 6),
                new EnemyDef("翻江倒海", Element.Water, 14, 7),
                new EnemyDef("雷霆万钧", Element.Metal, 10, 8),
            };
            return new CampaignConfig
            {
                DropTable = Array.Empty<string>(),
                Chapters = new[]
                {
                    new ChapterDef
                    {
                        Name = "蒙学",
                        BossPool = bosses,
                        Stages = new[]
                        {
                            new StageDef
                            {
                                Encounters = new[]
                                {
                                    new[] { Ghost() },
                                    new[] { CampaignConfig.BossPlaceholder },
                                },
                                Boss = true,
                            },
                        },
                        RewardPool = Array.Empty<string>(),
                    },
                },
            };
        }

        [Test]
        public void BossPlaceholder_ResolvedFromPool_DeterministicBySeed()
        {
            var campaign = WithBossPool();
            var a = campaign.BuildRunConfig(0, 0, new GameRandom(5)).Encounters[1][0];
            var b = campaign.BuildRunConfig(0, 0, new GameRandom(5)).Encounters[1][0];
            Assert.That(a.Id, Is.EqualTo(b.Id)); // 同种子同 Boss
            Assert.That(new[] { "排山倒海", "翻江倒海", "雷霆万钧" }, Does.Contain(a.Id));
        }

        [Test]
        public void BossPlaceholder_NullRandom_PicksFirst()
        {
            var boss = WithBossPool().BuildRunConfig(0, 0).Encounters[1][0];
            Assert.That(boss.Id, Is.EqualTo("排山倒海"));
        }

        [Test]
        public void LoadCampaign_ParsesBossPoolAndPlaceholder()
        {
            var graph = ConfigLoader.LoadGraph(@"{ ""chars"": [ { ""id"": ""灯"" } ] }");
            var campaign = ConfigLoader.LoadCampaign(@"{
                ""enemies"": [
                    { ""id"": ""错字鬼"", ""element"": ""Wood"", ""maxHp"": 12, ""attack"": 4 },
                    { ""id"": ""排山倒海"", ""element"": ""Water"", ""maxHp"": 12, ""attack"": 6 }
                ],
                ""dropTable"": [],
                ""chapters"": [ { ""name"": ""蒙学"", ""bossPool"": [ ""排山倒海"" ],
                    ""stages"": [ { ""encounters"": [ [ ""错字鬼"" ], [ ""$Boss"" ] ], ""boss"": true } ],
                    ""rewardPool"": [] } ]
            }", graph);
            var chapter = campaign.Chapters[0];
            Assert.That(chapter.BossPool.Single().Id, Is.EqualTo("排山倒海"));
            Assert.That(chapter.Stages[0].Encounters[1][0], Is.SameAs(CampaignConfig.BossPlaceholder));
        }

        [Test]
        public void LoadCampaign_PlaceholderWithoutPool_Throws()
        {
            var graph = ConfigLoader.LoadGraph(@"{ ""chars"": [ { ""id"": ""灯"" } ] }");
            Assert.Throws<ConfigException>(() => ConfigLoader.LoadCampaign(@"{
                ""enemies"": [],
                ""dropTable"": [],
                ""chapters"": [ { ""name"": ""x"",
                    ""stages"": [ { ""encounters"": [ [ ""$Boss"" ] ] } ], ""rewardPool"": [] } ]
            }", graph));
        }
    }
}
