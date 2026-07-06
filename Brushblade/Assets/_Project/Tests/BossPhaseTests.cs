using System;
using System.Linq;
using Brushblade.Core;
using Brushblade.Data;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>成语 Boss 四阶段(8.5):四个字 = 四个阶段,字面即机制。</summary>
    public class BossPhaseTests
    {
        private static RecipeGraph Graph() => new(new[]
        {
            new CharDef("火", Element.Fire,
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 10) }),
            new CharDef("燃", Element.Fire,
                effects: new[] { new EffectDef(EffectKind.BurnAll, 3) }),
        });

        private static EnemyDef PaiShanDaoHai() => new("排山倒海", Element.Water, 12, 6,
            phases: new[]
            {
                new BossPhaseDef("排", Element.Metal, 12, 6),
                new BossPhaseDef("山", Element.Earth, 15, 4, damageTaken: 0.5f),
                new BossPhaseDef("倒", Element.Wood, 12, 8),
                new BossPhaseDef("海", Element.Water, 16, 10),
            });

        private static BattleEngine Engine() =>
            new(Graph(), new BattleConfig { DropTable = new[] { "火" } }, new[] { "燃" },
                new[] { "火", "火", "火", "火", "火", "火" },
                new[] { PaiShanDaoHai() }, seed: 1);

        [Test]
        public void Boss_StartsInFirstPhase()
        {
            var boss = Engine().Enemies[0];
            Assert.That(boss.IsBoss, Is.True);
            Assert.That(boss.PhaseIndex, Is.EqualTo(0));
            Assert.That(boss.Element, Is.EqualTo(Element.Metal)); // 「排」金
            Assert.That(boss.Hp, Is.EqualTo(12));
            Assert.That(boss.Attack, Is.EqualTo(6));
        }

        [Test]
        public void PhaseKill_AdvancesToNextPhase_NotDeath()
        {
            var engine = Engine();
            engine.Cast("火", 0); // 火 vs 金(排)×1.5 = 15 ≥ 12 → 进「山」

            var boss = engine.Enemies[0];
            Assert.That(boss.Alive, Is.True);
            Assert.That(engine.Phase, Is.EqualTo(BattlePhase.PlayerTurn));
            Assert.That(boss.PhaseIndex, Is.EqualTo(1));
            Assert.That(boss.Element, Is.EqualTo(Element.Earth)); // 「山」土
            Assert.That(boss.Hp, Is.EqualTo(15));                 // 溢出伤害不带入
            Assert.That(boss.Attack, Is.EqualTo(4));
            Assert.That(engine.LastEvents.Any(e => e.Kind == BattleEventKind.BossPhase && e.Amount == 1), Is.True);
        }

        [Test]
        public void ShanPhase_HalvesDamageTaken()
        {
            var engine = Engine();
            engine.Cast("火", 0);  // 破「排」进「山」
            engine.EndTurn();
            engine.Cast("火", 0);  // 火 vs 土:1.0 → 10 × 0.5 = 5
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(15 - 5));
        }

        [Test]
        public void PhaseChange_ClearsBurn()
        {
            var engine = Engine();
            engine.Cast("燃");     // 挂 3 层灼烧
            Assert.That(engine.Enemies[0].Burn, Is.EqualTo(3));
            engine.Cast("火", 0);  // 破「排」→ 换阶段,新字新体
            Assert.That(engine.Enemies[0].Burn, Is.EqualTo(0));
        }

        [Test]
        public void FinalPhaseKill_WinsBattle()
        {
            var engine = Engine();
            for (int phase = 0; phase < 4; phase++)
            {
                // 每阶段用足够的火部件打穿(每回合最多 3 次出手)
                while (engine.Phase == BattlePhase.PlayerTurn && engine.Enemies[0].PhaseIndex == phase && engine.Enemies[0].Alive)
                {
                    if (engine.Cast("火", 0) != BattleError.None)
                        engine.EndTurn(); // AP/部件不足则过回合等掉落
                }
                if (engine.Phase != BattlePhase.PlayerTurn) break;
            }
            Assert.That(engine.Phase, Is.EqualTo(BattlePhase.Won));
        }

        [Test]
        public void LoadCampaign_ParsesPhases()
        {
            var graph = ConfigLoader.LoadGraph(@"{ ""chars"": [ { ""id"": ""灯"" } ] }");
            var campaign = ConfigLoader.LoadCampaign(@"{
                ""enemies"": [
                    { ""id"": ""排山倒海"", ""element"": ""Water"", ""maxHp"": 12, ""attack"": 6, ""phases"": [
                        { ""char"": ""排"", ""element"": ""Metal"", ""maxHp"": 12, ""attack"": 6 },
                        { ""char"": ""山"", ""element"": ""Earth"", ""maxHp"": 15, ""attack"": 4, ""damageTaken"": 0.5 }
                    ] }
                ],
                ""dropTable"": [],
                ""chapters"": [ { ""name"": ""蒙学"",
                    ""stages"": [ { ""encounters"": [ [ ""排山倒海"" ] ], ""boss"": true } ], ""rewardPool"": [] } ]
            }", graph);
            var boss = campaign.Chapters[0].Stages[0].Encounters[0][0];
            Assert.That(boss.Phases.Count, Is.EqualTo(2));
            Assert.That(boss.Phases[0].Char, Is.EqualTo("排"));
            Assert.That(boss.Phases[1].DamageTaken, Is.EqualTo(0.5f));
        }

        [Test]
        public void ChapterScale_ScalesPhases()
        {
            var campaign = new CampaignConfig
            {
                DropTable = Array.Empty<string>(),
                Chapters = new[]
                {
                    new ChapterDef
                    {
                        Name = "字林", EnemyScale = 1.5f,
                        Stages = new[] { new StageDef { Encounters = new[] { new[] { PaiShanDaoHai() } } } },
                        RewardPool = Array.Empty<string>(),
                    },
                },
            };
            var boss = campaign.BuildRunConfig(0, 0).Encounters[0][0];
            Assert.That(boss.Phases[0].MaxHp, Is.EqualTo(18));  // 12×1.5
            Assert.That(boss.Phases[3].Attack, Is.EqualTo(15)); // 10×1.5
            Assert.That(boss.Phases[1].DamageTaken, Is.EqualTo(0.5f)); // 承伤系数不缩放
        }
    }
}
