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
        public void Floor1_SingleEnemy_NeverSupport() // 辅助型不单独成场(2026-07-19)
        {
            for (int seed = 0; seed < 30; seed++)
            {
                var floor = EndlessGenerator.BuildFloor(Config(), 1, new GameRandom(seed));
                Assert.That(floor.Count, Is.EqualTo(1));
                Assert.That(floor[0].Id, Is.EqualTo("错字鬼")); // 池中唯一非辅助
            }
        }

        [Test]
        public void EnemyCount_GrowsWithDepth_CapsAtEight()
        {
            // 2026-08-27:每 6 层多一只(此前每 4 层),上限 6 → 8
            Assert.That(EndlessGenerator.BuildFloor(Config(), 9, new GameRandom(7)).Count, Is.EqualTo(2));
            Assert.That(EndlessGenerator.BuildFloor(Config(), 13, new GameRandom(7)).Count, Is.EqualTo(3));
            Assert.That(EndlessGenerator.BuildFloor(Config(), 99, new GameRandom(7)).Count, Is.EqualTo(8));
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
                Assert.That(enemy.MaxHp, Is.EqualTo(24).Or.EqualTo(16).Or.EqualTo(44));
        }

        [Test]
        public void BossFloor_SingleScaledBoss()
        {
            var floor = EndlessGenerator.BuildFloor(Config(), 5, new GameRandom(7));
            Assert.That(floor.Count, Is.EqualTo(1));
            Assert.That(floor[0].Id, Is.EqualTo("排山倒海"));
            Assert.That(floor[0].MaxHp, Is.EqualTo(12)); // 第 5 层 Boss scale 1.0(滞后缩放)
        }

        /// <summary>Boss 带小怪(2026-09-05 用户拍板):第 20 层起,每个 Boss 层 +1 只,
        /// 直到把 Boss 之外的格位填满。20 层之前的 Boss(5/10/15)照旧单独出场。
        /// **首项恒为 Boss** —— 落位与表现层都靠这一条(与 BuildFloor 非 Boss 分支的
        /// 「首位强制前排」同一种「第 0 项有特殊约定」的写法)。</summary>
        [TestCase(5, 0)]
        [TestCase(10, 0)]
        [TestCase(15, 0)]
        [TestCase(20, 1)]
        [TestCase(25, 2)]
        [TestCase(30, 3)]
        [TestCase(35, 4)]
        [TestCase(40, 4)]   // 带满之后恒为满,不再长
        [TestCase(95, 4)]
        public void BossFloor_EscortsGrowFromDepth20(int depth, int escorts)
        {
            var floor = EndlessGenerator.BuildFloor(Config(), depth, new GameRandom(7));
            Assert.That(floor[0].Id, Is.EqualTo("排山倒海"), "首项恒为 Boss");
            Assert.That(floor.Count, Is.EqualTo(1 + escorts));
            for (int i = 1; i < floor.Count; i++)
                Assert.That(floor[i].Phases.Count, Is.EqualTo(0), "随从只能是杂兵,不能又是个 Boss");
        }

        /// <summary>随从与 Boss 吃**同一个** scale —— 也就是 Boss 层的滞后缩放。
        ///
        /// 这比同深度的杂兵层略低(20 层:滞后 2.5 vs 杂兵 2.9),是刻意的:滞后缩放的口径是
        /// 「这一层整体该多难」,Boss 层整层按同一个数算才自洽 —— 让随从按杂兵公式走会
        /// 把 Boss 层的总难度顶上去,而滞后那一档本来就是为「四阶段 Boss ≈ 两倍深度的杂兵」
        /// 校准出来的。真要给随从另一档缩放,得在 EndlessConfig 上另开口径,不是在这里改一个乘数。</summary>
        [Test]
        public void BossEscorts_TakeTheSameFloorScale()
        {
            var floor = EndlessGenerator.BuildFloor(Config(), 20, new GameRandom(7));
            Assert.That(floor.Count, Is.EqualTo(2));
            // 第 20 层 Boss 层 scale = (1 + 0.10 × (20 − 5)) × 1 = 2.5;池里三只的血 12/8/22
            Assert.That(floor[1].MaxHp, Is.EqualTo(30).Or.EqualTo(20).Or.EqualTo(55));
        }

        /// <summary>随从也走「辅助不单独成场 / 带甲每场最多 1 只」那两道既有闸 ——
        /// 它们是杂兵组场的规则,不因为旁边站了个 Boss 就失效。</summary>
        [Test]
        public void BossEscorts_RespectSupportAndArmorGates()
        {
            for (int seed = 0; seed < 30; seed++)
            {
                var floor = EndlessGenerator.BuildFloor(Config(), 95, new GameRandom(seed));
                var escorts = floor.Skip(1).ToList();
                Assert.That(escorts.Count(e => e.Ability == EnemyAbility.Buff),
                    Is.LessThanOrEqualTo(1), $"seed {seed}:辅助超过 1 只");
                Assert.That(escorts.Count(e => e.Defense > 0),
                    Is.LessThanOrEqualTo(1), $"seed {seed}:带甲超过 1 只");
            }
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

        [Test]
        public void EveryFloor_HasAtLeastOneNonSupport() // 全辅助场零威胁,禁止出现
        {
            foreach (int depth in new[] { 1, 2, 3, 4, 6, 7, 8, 9, 12, 99 })
                for (int seed = 0; seed < 20; seed++)
                {
                    var floor = EndlessGenerator.BuildFloor(Config(), depth, new GameRandom(seed));
                    Assert.That(floor.Any(e => e.Ability != EnemyAbility.Buff), Is.True,
                        $"depth={depth} seed={seed} 全是辅助型");
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
        public void ChestTier_GrowsWithDepth() // 区间内只出低档或高一档
        {
            var random = new GameRandom(1);
            for (int i = 0; i < 200; i++)
            {
                Assert.That(EndlessRules.ChestTierFor(1, random),
                    Is.EqualTo(ChestTier.Paper).Or.EqualTo(ChestTier.Bamboo));
                Assert.That(EndlessRules.ChestTierFor(12, random),
                    Is.EqualTo(ChestTier.Celadon).Or.EqualTo(ChestTier.Rosewood));
                Assert.That(EndlessRules.ChestTierFor(40, random),
                    Is.EqualTo(ChestTier.Gilded).Or.EqualTo(ChestTier.Vermilion));
                Assert.That(EndlessRules.ChestTierFor(60, random),
                    Is.EqualTo(ChestTier.Vermilion).Or.EqualTo(ChestTier.Crimson));
                Assert.That(EndlessRules.ChestTierFor(80, random), Is.EqualTo(ChestTier.Crimson),
                    "70 层往上已到顶,没有更高一档可掷");
            }
        }

        [Test]
        public void ChestTier_HigherTierIsTenPercent() // 90:10(2026-07-20 拍板)
        {
            var random = new GameRandom(7);
            int high = 0;
            for (int i = 0; i < 2000; i++)
                if (EndlessRules.ChestTierFor(60, random) == ChestTier.Crimson)
                    high++;
            Assert.That(high, Is.InRange(150, 250)); // 期望 200/2000
        }

        [Test]
        public void Xp_TenPerFloor_FiftyOnBoss()
        {
            var config = Config();
            Assert.That(EndlessRules.XpFor(config, 3), Is.EqualTo(10));
            Assert.That(EndlessRules.XpFor(config, 5), Is.EqualTo(50));
        }

        [Test]
        public void SettleChestDepth_ZeroBoss_NoChest_NonzeroUsesTopBoss()
        {
            Assert.That(EndlessRules.SettleChestDepth(0), Is.EqualTo(0));   // 一个 Boss 都没破 → 不发
            Assert.That(EndlessRules.SettleChestDepth(15), Is.EqualTo(15)); // 按本次最高 Boss 层
            // 死亡不降档:函数不接受 died,故档位与结束方式无关(阵亡照发)
        }

        [Test]
        public void FloorInk_TwoPerFloor_FivePerBoss_DoublesEveryTenFloors()
        {
            var config = Config();
            // 1~10 层:普通 2 / Boss 5
            Assert.That(EndlessRules.FloorInk(config, 1), Is.EqualTo(2));
            Assert.That(EndlessRules.FloorInk(config, 5), Is.EqualTo(5));   // Boss
            Assert.That(EndlessRules.FloorInk(config, 9), Is.EqualTo(2));
            Assert.That(EndlessRules.FloorInk(config, 10), Is.EqualTo(5));  // Boss,仍是第一档
            // 11~20 层:翻倍
            Assert.That(EndlessRules.FloorInk(config, 11), Is.EqualTo(4));
            Assert.That(EndlessRules.FloorInk(config, 15), Is.EqualTo(10)); // Boss
            Assert.That(EndlessRules.FloorInk(config, 20), Is.EqualTo(10)); // Boss,仍是第二档
            // 21~30 层:再翻倍
            Assert.That(EndlessRules.FloorInk(config, 21), Is.EqualTo(8));
            Assert.That(EndlessRules.FloorInk(config, 25), Is.EqualTo(20)); // Boss
        }

        // ---- 成语 Boss 生成(20.7) ----

        private static IdiomBossDef Idiom() => new()
        {
            Chars = "刀山火海",
            Elements = new[] { Element.Metal, Element.Earth, Element.Fire, Element.Water },
        };

        [Test]
        public void IdiomBoss_FourPhases_FromTemplate()
        {
            var boss = EndlessGenerator.BuildIdiomBoss(Idiom());
            Assert.That(boss.Id, Is.EqualTo("刀山火海"));
            Assert.That(boss.Phases.Count, Is.EqualTo(4));
            Assert.That(boss.Phases[0].Char, Is.EqualTo("刀"));
            Assert.That(boss.Phases[0].Element, Is.EqualTo(Element.Metal));
            Assert.That(boss.Phases[1].Defense, Is.EqualTo(60));      // 第二字坚壁:护甲 60(与 enemies.json 的山同值)
            Assert.That(boss.Phases[3].Attack, Is.EqualTo(100));       // 末字狂攻
            Assert.That(boss.Phases[3].Element, Is.EqualTo(Element.Water));
        }

        [Test]
        public void BossFloor_DrawsFromIdiomPoolToo()
        {
            var config = Config();
            ((BandDef)config.Bands[0]).IdiomBossPool = new[] { Idiom() };
            bool sawIdiom = false, sawFixed = false;
            for (int seed = 0; seed < 40; seed++)
            {
                var floor = EndlessGenerator.BuildFloor(config, 5, new GameRandom(seed));
                if (floor[0].Id == "刀山火海") sawIdiom = true;
                if (floor[0].Id == "排山倒海") sawFixed = true;
            }
            Assert.That(sawIdiom, Is.True);
            Assert.That(sawFixed, Is.True);
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

        // 半额结算已于 2026-08-30 取消(用户拍板):`EndlessRules.SettleInk` 随之删除,
        // 塔内墨锭改为**赚到即入账**,塔结算时账上已经一分不少 —— 撤退与阵亡拿到的一样多。
        // 原先守它的 Retreat_SettlesFullInk / Death_SettlesHalfInk 两条一并删除;
        // 「即时入账」的新账目由 RunEngine 侧的 FloorInk_GoesIntoTheSameLedgerAsEvents 守。

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
                EndlessV2 = new EndlessSaveState
                {
                    Depth = 13, PlayerHp = 21, EarnedInk = 85, Seed = 42,
                    Library = new List<string> { "焚", "灯" },
                    Pool = new List<string> { "木", "火" },
                    LibraryExpanded = true,
                    TopBossDepth = 10,
                    BestDepthBeforeRun = 9,
                },
            };
            meta.BandMilestones.Add("词渊");

            var restored = Data.SaveSerializer.FromJson(Data.SaveSerializer.ToJson(meta));

            Assert.That(restored.BestDepth, Is.EqualTo(17));
            Assert.That(restored.BandMilestones, Is.EqualTo(new[] { "词渊" }));
            Assert.That(restored.EndlessV2.Depth, Is.EqualTo(13));
            Assert.That(restored.EndlessV2.Library, Is.EqualTo(new[] { "焚", "灯" }));
            Assert.That(restored.EndlessV2.LibraryExpanded, Is.True);
            Assert.That(restored.EndlessV2.TopBossDepth, Is.EqualTo(10)); // 结算宝箱档位据此(2026-07-22)
            // 登塔前的历史最高必须跟着快照落盘(2026-09-02):结算页「新纪录 43 → 45」的左边那个数
            // 靠它。段末告捷会当场刷掉 meta.BestDepth,只留在内存里的话,挂起重进就再也取不回来
            Assert.That(restored.EndlessV2.BestDepthBeforeRun, Is.EqualTo(9));
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

        [Test]
        public void BuildFloor_EnemyCountCapsAtEight()
        {
            Assert.That(EndlessGenerator.BuildFloor(Config(), 99, new GameRandom(7)).Count,
                Is.EqualTo(8), "深层敌人数上限应为 8");
            for (int depth = 1; depth <= 120; depth++)
                Assert.That(EndlessGenerator.BuildFloor(Config(), depth, new GameRandom(7)).Count,
                    Is.LessThanOrEqualTo(8), $"第 {depth} 层敌人数超上限");
        }

        [Test]
        public void BuildFloor_ReachesFullHouseAtDepth43()
        {
            // 放缓节奏(2026-08-27):1 + min(7, (depth−1)/6) —— 43 层才满员,42 层还是 7 只。
            // 这条钉住的是**节奏**而不只是上限:把除数改回 4 会让满员深度回到 29,测试要红。
            Assert.That(EndlessGenerator.BuildFloor(Config(), 42, new GameRandom(7)).Count, Is.EqualTo(7));
            Assert.That(EndlessGenerator.BuildFloor(Config(), 43, new GameRandom(7)).Count, Is.EqualTo(8));
        }
    }
}
