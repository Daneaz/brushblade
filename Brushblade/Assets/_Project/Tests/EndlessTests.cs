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
        public void Scale_BossFloorGetsBonus()
        {
            var config = Config();
            // 第 5 层:1.4 × 1.25 = 1.75
            Assert.That(config.ScaleFor(5), Is.EqualTo(1.75f).Within(0.001f));
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
            Assert.That(floor[0].MaxHp, Is.EqualTo(21)); // ceil(12 × 1.75)
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

        // ---- 结算与里程碑 ----

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
