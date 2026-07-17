using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>无尽模式核心(第 20 章):层段/深度缩放/遭遇生成/结算与里程碑。</summary>
    public class EndlessTests
    {
        private static EnemyDef Ghost() => new("错字鬼", Element.Wood, 12, 4);
        private static EnemyDef Imp() => new("标点小妖", Element.Heart, 8, 1, EnemyAbility.Buff);
        private static EnemyDef Boss() => new("排山倒海", Element.Water, 12, 6);
        private static EnemyDef DeepGhost() => new("生僻字", Element.Earth, 22, 2, EnemyAbility.Obscure);

        private static EndlessConfig Config() => new()
        {
            Bands = new[]
            {
                new BandDef { Name = "字林", FromDepth = 1,
                    EnemyPool = new[] { Ghost(), Imp() }, BossPool = new[] { Boss() },
                    RewardPool = new[] { "灼" }, MilestoneInk = 0 },
                new BandDef { Name = "词渊", FromDepth = 11,
                    EnemyPool = new[] { Ghost(), Imp(), DeepGhost() }, BossPool = new[] { Boss() },
                    RewardPool = new[] { "灼", "炽" }, MilestoneInk = 200 },
            },
        };

        // ---- 层段与缩放 ----

        [Test]
        public void BandFor_PicksByDepth()
        {
            var config = Config();
            Assert.That(config.BandFor(1).Name, Is.EqualTo("字林"));
            Assert.That(config.BandFor(10).Name, Is.EqualTo("字林"));
            Assert.That(config.BandFor(11).Name, Is.EqualTo("词渊"));
            Assert.That(config.BandFor(999).Name, Is.EqualTo("词渊"));
        }

        [Test]
        public void BossDepth_EveryFifth()
        {
            var config = Config();
            Assert.That(config.IsBossDepth(5), Is.True);
            Assert.That(config.IsBossDepth(10), Is.True);
            Assert.That(config.IsBossDepth(3), Is.False);
            Assert.That(config.IsBossDepth(11), Is.False);
        }

        [Test]
        public void Scale_LinearByDepth()
        {
            var config = Config();
            Assert.That(config.ScaleFor(1), Is.EqualTo(1f).Within(0.001f));
            Assert.That(config.ScaleFor(11), Is.EqualTo(2f).Within(0.001f));
        }

        [Test]
        public void Scale_BossFloors_LagBehindTrash() // Boss 滞后缩放:仿真校准(2026-07-17)
        {
            var config = Config();
            // Boss@1.0 ≈ 杂兵@2.0 难度(关卡制实测),故 Boss 层 scale = 1 + k×(depth−5)
            Assert.That(config.ScaleFor(5), Is.EqualTo(1f).Within(0.001f));
            Assert.That(config.ScaleFor(10), Is.EqualTo(1.5f).Within(0.001f));
            Assert.That(config.ScaleFor(15), Is.EqualTo(2f).Within(0.001f));
        }

        // ---- 遭遇生成 ----

        [Test]
        public void Floor1_SingleEnemy_FromBandPool()
        {
            var floor = EndlessGenerator.BuildFloor(Config(), 1, new GameRandom(7));
            Assert.That(floor.Count, Is.EqualTo(1));
            Assert.That(new[] { "错字鬼", "标点小妖" }, Does.Contain(floor[0].Id));
        }

        [Test]
        public void EnemyCount_GrowsWithDepth_CapsAtFour()
        {
            Assert.That(EndlessGenerator.BuildFloor(Config(), 9, new GameRandom(7)).Count, Is.EqualTo(3));
            Assert.That(EndlessGenerator.BuildFloor(Config(), 99, new GameRandom(7)).Count, Is.EqualTo(4));
        }

        [Test]
        public void SameSeedSameDepth_SameFloor()
        {
            var a = EndlessGenerator.BuildFloor(Config(), 8, new GameRandom(42));
            var b = EndlessGenerator.BuildFloor(Config(), 8, new GameRandom(42));
            Assert.That(a.Select(e => e.Id), Is.EqualTo(b.Select(e => e.Id)));
        }

        [Test]
        public void Enemies_AreScaledByDepth()
        {
            var floor = EndlessGenerator.BuildFloor(Config(), 11, new GameRandom(7));
            // scale=2.0:错字鬼 24/8,标点 16/2,生僻字 44/4——全部翻倍
            foreach (var enemy in floor)
                Assert.That(enemy.MaxHp, Is.AnyOf(24, 16, 44));
        }

        [Test]
        public void BossFloor_SingleScaledBoss()
        {
            var floor = EndlessGenerator.BuildFloor(Config(), 5, new GameRandom(7));
            Assert.That(floor.Count, Is.EqualTo(1));
            Assert.That(floor[0].Id, Is.EqualTo("排山倒海"));
            Assert.That(floor[0].MaxHp, Is.EqualTo(12)); // 第 5 层 Boss scale 1.0(滞后缩放)
        }

        [Test]
        public void SupportEnemy_AtMostOnePerFloor()
        {
            for (int seed = 0; seed < 30; seed++)
            {
                var floor = EndlessGenerator.BuildFloor(Config(), 99, new GameRandom(seed));
                Assert.That(floor.Count(e => e.Ability == EnemyAbility.Buff), Is.LessThanOrEqualTo(1));
            }
        }

        // ---- 段组装(20.2/20.6 断点续爬) ----

        [Test]
        public void Segment_From1_FiveFloors_LastIsBoss()
        {
            var run = EndlessGenerator.BuildSegment(Config(), fromDepth: 1, seed: 42);
            Assert.That(run.Encounters.Count, Is.EqualTo(5));
            Assert.That(run.Encounters[4].Count, Is.EqualTo(1));
            Assert.That(run.Encounters[4][0].Id, Is.EqualTo("排山倒海"));
        }

        [Test]
        public void Segment_ResumeMidSegment_MatchesOriginalFloors()
        {
            // 断点续爬核心性质:从第 3 层恢复,第 3~5 层编成与整段生成时一致
            var full = EndlessGenerator.BuildSegment(Config(), fromDepth: 1, seed: 42);
            var resumed = EndlessGenerator.BuildSegment(Config(), fromDepth: 3, seed: 42);
            Assert.That(resumed.Encounters.Count, Is.EqualTo(3));
            for (int i = 0; i < 3; i++)
                Assert.That(resumed.Encounters[i].Select(e => e.Id),
                    Is.EqualTo(full.Encounters[i + 2].Select(e => e.Id)));
        }

        [Test]
        public void Segment_RewardPool_FromBand()
        {
            var run = EndlessGenerator.BuildSegment(Config(), fromDepth: 11, seed: 42);
            Assert.That(run.RewardPool, Is.EqualTo(new[] { "灼", "炽" }));
        }

        [Test]
        public void Segment_ScriptedOpening_FirstThreeFloorsFixed() // 20.10 初次登入剧本化
        {
            var run = EndlessGenerator.BuildFirstTowerSegment(Config(), seed: 42);
            Assert.That(run.Encounters[0].Select(e => e.Id), Is.EqualTo(new[] { "错字鬼" }));
            Assert.That(run.Encounters[1].Select(e => e.Id), Is.EqualTo(new[] { "错字鬼", "错字鬼" }));
            Assert.That(run.Encounters[2].Count, Is.EqualTo(2)); // 第 3 层双敌
            Assert.That(run.Encounters[4][0].Id, Is.EqualTo("排山倒海")); // 第 5 层仍是 Boss
        }

        [Test]
        public void RunEngine_StartingHp_AppliedToFirstBattle() // 断点续爬恢复血量(20.6)
        {
            var graph = new RecipeGraph(new[] { new CharDef("灯", Element.Fire) });
            var runConfig = EndlessGenerator.BuildSegment(Config(), fromDepth: 3, seed: 42);
            var engine = new RunEngine(graph, runConfig, new BattleConfig(),
                startingLibrary: new[] { "灯" }, startingPool: new string[0], seed: 1,
                startingHp: 21);
            Assert.That(engine.Battle.PlayerHp, Is.EqualTo(21));
        }

        // ---- 宝箱档位与经验(20.8) ----

        [Test]
        public void ChestTier_GrowsWithDepth()
        {
            var random = new GameRandom(1);
            Assert.That(EndlessRules.ChestTierFor(1, random), Is.AnyOf(ChestTier.Paper, ChestTier.Bamboo));
            Assert.That(EndlessRules.ChestTierFor(12, random), Is.AnyOf(ChestTier.Celadon, ChestTier.Rosewood));
            Assert.That(EndlessRules.ChestTierFor(60, random), Is.AnyOf(ChestTier.Gilded, ChestTier.Crimson));
        }

        [Test]
        public void Xp_TenPerFloor_FiftyOnBoss()
        {
            var config = Config();
            Assert.That(EndlessRules.XpFor(config, 3), Is.EqualTo(10));
            Assert.That(EndlessRules.XpFor(config, 5), Is.EqualTo(50));
        }

        // ---- 结算与里程碑 ----

        [Test]
        public void RankTitle_ByBestDepth() // 书法段位(11.3.2 → 20.3)
        {
            Assert.That(EndlessRules.RankTitle(0), Is.EqualTo("白丁"));
            Assert.That(EndlessRules.RankTitle(9), Is.EqualTo("白丁"));
            Assert.That(EndlessRules.RankTitle(10), Is.EqualTo("学童"));
            Assert.That(EndlessRules.RankTitle(25), Is.EqualTo("秀才"));
            Assert.That(EndlessRules.RankTitle(50), Is.EqualTo("举人"));
            Assert.That(EndlessRules.RankTitle(75), Is.EqualTo("进士"));
            Assert.That(EndlessRules.RankTitle(120), Is.EqualTo("翰林"));
        }

        [Test]
        public void Retreat_SettlesFullInk()
        {
            Assert.That(EndlessRules.SettleInk(120, died: false), Is.EqualTo(120));
        }

        [Test]
        public void Death_SettlesHalfInk()
        {
            Assert.That(EndlessRules.SettleInk(121, died: true), Is.EqualTo(60));
        }

        [Test]
        public void BestDepth_OnlyImproves()
        {
            var meta = new MetaState();
            EndlessRules.UpdateBest(meta, 12);
            EndlessRules.UpdateBest(meta, 8);
            Assert.That(meta.BestDepth, Is.EqualTo(12));
        }

        [Test]
        public void EndlessState_SurvivesSaveRoundTrip() // 断点续爬(20.6)存档回归
        {
            var meta = new MetaState
            {
                BestDepth = 17,
                Endless = new EndlessSaveState
                {
                    Depth = 13, PlayerHp = 21, EarnedInk = 85, Seed = 42,
                    Library = new List<string> { "焚", "灯" },
                    Pool = new List<string> { "木", "火" },
                    LibraryExpanded = true,
                },
            };
            meta.BandMilestones.Add("词渊");

            var restored = Data.SaveSerializer.FromJson(Data.SaveSerializer.ToJson(meta));

            Assert.That(restored.BestDepth, Is.EqualTo(17));
            Assert.That(restored.BandMilestones, Is.EqualTo(new[] { "词渊" }));
            Assert.That(restored.Endless.Depth, Is.EqualTo(13));
            Assert.That(restored.Endless.Library, Is.EqualTo(new[] { "焚", "灯" }));
            Assert.That(restored.Endless.LibraryExpanded, Is.True);
        }

        [Test]
        public void BandMilestone_AwardedOnce()
        {
            var meta = new MetaState();
            var band = Config().Bands[1];
            Assert.That(EndlessRules.TryAwardMilestone(meta, band), Is.True);
            Assert.That(meta.Ink, Is.EqualTo(200));
            Assert.That(EndlessRules.TryAwardMilestone(meta, band), Is.False);
            Assert.That(meta.Ink, Is.EqualTo(200));
        }
    }
}
