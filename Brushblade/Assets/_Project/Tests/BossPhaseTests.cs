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

        // 血池 Boss(2026-07-19 拍板):总血 55 = 12+15+12+16;
        // 无浮动时阈值 43/28/16(hp ≤ 阈值即进下一阶段),血量连续不重置
        private static BattleEngine Engine(int jitter = 0, int seed = 1) =>
            new(Graph(), new BattleConfig { DropTable = new[] { "火" }, BossPhaseJitterPercent = jitter },
                new[] { "燃" }, new[] { "火", "火", "火", "火", "火", "火" },
                new[] { PaiShanDaoHai() }, seed);

        [Test]
        public void Boss_StartsInFirstPhase_WithPooledHp()
        {
            var boss = Engine().Enemies[0];
            Assert.That(boss.IsBoss, Is.True);
            Assert.That(boss.PhaseIndex, Is.EqualTo(0));
            Assert.That(boss.Element, Is.EqualTo(Element.Metal)); // 「排」金
            Assert.That(boss.Hp, Is.EqualTo(55));                 // 一条总血
            Assert.That(boss.MaxHp, Is.EqualTo(55));
            Assert.That(boss.Attack, Is.EqualTo(6));
        }

        [Test]
        public void CrossThreshold_SwitchesPhase_HpContinues()
        {
            var engine = Engine();
            engine.Cast("火", 0); // 火 vs 金(排)×1.5 = 15 → 55-15=40 ≤ 43 → 进「山」

            var boss = engine.Enemies[0];
            Assert.That(boss.Alive, Is.True);
            Assert.That(engine.Phase, Is.EqualTo(BattlePhase.PlayerTurn));
            Assert.That(boss.PhaseIndex, Is.EqualTo(1));
            Assert.That(boss.Element, Is.EqualTo(Element.Earth)); // 「山」土
            Assert.That(boss.Hp, Is.EqualTo(40));                 // 血量连续,不重置
            Assert.That(boss.Attack, Is.EqualTo(4));
            Assert.That(engine.LastEvents.Any(e => e.Kind == BattleEventKind.BossPhase && e.Amount == 1), Is.True);
        }

        [Test]
        public void BigHit_CanCrossMultipleThresholds()
        {
            var graph = new RecipeGraph(new[]
            {
                new CharDef("炮", Element.Fire, effects: new[] { new EffectDef(EffectKind.DamageSingle, 20) }),
            });
            var engine = new BattleEngine(graph,
                new BattleConfig { DropTable = System.Array.Empty<string>(), BossPhaseJitterPercent = 0 },
                new[] { "炮" }, Array.Empty<string>(), new[] { PaiShanDaoHai() }, seed: 1);
            engine.Cast("炮", 0); // 火 vs 金 ×1.5 = 30 → 55-30=25 ≤ 43 且 ≤ 28 → 连跨两阶进「倒」
            var boss = engine.Enemies[0];
            Assert.That(boss.PhaseIndex, Is.EqualTo(2));
            Assert.That(boss.Element, Is.EqualTo(Element.Wood));
            Assert.That(boss.Hp, Is.EqualTo(25));
            Assert.That(engine.LastEvents.Count(e => e.Kind == BattleEventKind.BossPhase), Is.EqualTo(2));
        }

        [Test]
        public void ShanPhase_HalvesDamageTaken()
        {
            var engine = Engine();
            engine.Cast("火", 0);  // 40 血进「山」
            engine.EndTurn();
            engine.Cast("火", 0);  // 火 vs 土:1.0 → 10 × 0.5 = 5
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(40 - 5));
        }

        [Test]
        public void Thresholds_JitteredBySeed_VaryAcrossSeeds()
        {
            // 浮动 30%:同一 Boss 不同种子,单发 15 伤(至 40 血)后阶段应有差异
            var outcomes = new System.Collections.Generic.HashSet<int>();
            for (int seed = 1; seed <= 20; seed++)
            {
                var engine = Engine(jitter: 30, seed: seed);
                engine.Cast("火", 0);
                outcomes.Add(engine.Enemies[0].PhaseIndex);
            }
            Assert.That(outcomes.Count, Is.GreaterThan(1)); // 有的已换阶,有的还没
        }

        [Test]
        public void Thresholds_SameSeed_Deterministic()
        {
            var a = Engine(jitter: 30, seed: 7);
            var b = Engine(jitter: 30, seed: 7);
            a.Cast("火", 0);
            b.Cast("火", 0);
            Assert.That(a.Enemies[0].PhaseIndex, Is.EqualTo(b.Enemies[0].PhaseIndex));
            Assert.That(a.Enemies[0].Hp, Is.EqualTo(b.Enemies[0].Hp));
        }

        [Test]
        public void PhaseChange_ClearsBurn()
        {
            var engine = Engine();
            engine.Cast("燃");     // 挂 3 层灼烧
            Assert.That(engine.Enemies[0].Burn, Is.EqualTo(3));
            engine.Cast("火", 0);  // 跨阈值换阶段,新字新体
            Assert.That(engine.Enemies[0].Burn, Is.EqualTo(0));
        }

        [Test]
        public void FinalPhaseKill_WinsBattle()
        {
            // 掉字改造(2026-08-04):回合不再掉部件续弹药,直接给足「火」把 4 阶段(总血 55)打穿
            var engine = new BattleEngine(Graph(), new BattleConfig { BossPhaseJitterPercent = 0 },
                new[] { "燃" }, Enumerable.Repeat("火", 30).ToArray(), new[] { PaiShanDaoHai() }, seed: 1);
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

        // ---- Freeze 补测(task 9 review finding,2026-08-03):冻结中的 Boss 完全不行动,
        // 含蓄力计数与技能释放 ----

        private static RecipeGraph FreezeGraph() => new(new[]
        {
            new CharDef("火", Element.Fire,
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 10) }),
            new CharDef("冻", Element.Water,
                effects: new[] { new EffectDef(EffectKind.Freeze, 3) }),
        });

        // 单阶段技能 Boss:BossChargeEvery 默认 2 → 正常节奏是 普攻(计数1)→蓄力(计数2)→释放。
        // 冻 3 回合正好盖住整个周期,借此断言冻结把「蓄力计数/进入蓄力/释放大招」全部拦下。
        private static EnemyDef SkillBoss() => new("试炼", Element.Heart, 100, 5,
            phases: new[] { new BossPhaseDef("甲", Element.Heart, 100, 5, skill: BossSkill.Deluge) });

        [Test]
        public void Freeze_PausesBossChargeAndSkillRelease()
        {
            var engine = new BattleEngine(FreezeGraph(),
                new BattleConfig { BossPhaseJitterPercent = 0 },
                new[] { "冻" }, Array.Empty<string>(), new[] { SkillBoss() }, seed: 1);
            engine.Cast("冻", 0);
            int hp0 = engine.PlayerHp;

            engine.EndTurn(); // 冻结回合 1:本应普攻+计数,冻结后不出手
            Assert.That(engine.PlayerHp, Is.EqualTo(hp0));
            Assert.That(engine.Enemies[0].ChargeCounter, Is.EqualTo(0), "冻结中不蓄力计数");
            Assert.That(engine.Enemies[0].IsCharging, Is.False);

            engine.EndTurn(); // 冻结回合 2:本应计数达标进入蓄力,冻结后仍不出手
            Assert.That(engine.PlayerHp, Is.EqualTo(hp0));
            Assert.That(engine.Enemies[0].IsCharging, Is.False, "冻结中不会进入蓄力状态");

            engine.EndTurn(); // 冻结回合 3:本应释放大招,冻结后仍不出手
            Assert.That(engine.PlayerHp, Is.EqualTo(hp0), "冻结覆盖了整个蓄力周期,大招没放出来");
            Assert.That(engine.LastEvents.Any(e => e.Kind == BattleEventKind.BossSkillCast), Is.False);
            Assert.That(engine.LastEvents.Any(e => e.Kind == BattleEventKind.BossCharging), Is.False);

            engine.EndTurn(); // 解冻后第 1 个敌方回合:恢复普攻,重新计数
            Assert.That(engine.PlayerHp, Is.EqualTo(hp0 - 5), "解冻后恢复普攻");
            Assert.That(engine.Enemies[0].ChargeCounter, Is.EqualTo(1));
        }
    }
}
