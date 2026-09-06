using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using Brushblade.Data;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>养成规则(第 19 章首版基准)与存档序列化。纯测试,无 UnityEngine。</summary>
    public class MetaTests
    {
        private static CampaignConfig TwoChapters() => new()
        {
            DropTable = new[] { "木" },
            Chapters = new[]
            {
                new ChapterDef { Name = "蒙学", Stages = new[] { new StageDef(), new StageDef() }, RewardPool = new string[0] },
                new ChapterDef { Name = "字林", Stages = new[] { new StageDef() }, RewardPool = new string[0] },
            },
        };

        [Test]
        public void StartingLibrary_LeavesOneSlotForDrop() // 起手 = 容量则第一回合必弹决议窗
        {
            var meta = new MetaState();
            Assert.That(MetaRules.StartingLibrarySize, Is.LessThan(MetaRules.LibraryCapacityFor(meta)));
        }

        // ---- 角色等级曲线(升到 n+1 需 100+50×(n−1)) ----

        [TestCase(0, 1)]
        [TestCase(99, 1)]
        [TestCase(100, 2)]   // L1→2 需 100
        [TestCase(249, 2)]   // L2→3 需 150(累计 250)
        [TestCase(250, 3)]
        [TestCase(450, 4)]   // L3→4 需 200(累计 450)
        public void CharacterLevel_Curve(int xp, int expected)
        {
            Assert.That(MetaRules.CharacterLevel(xp), Is.EqualTo(expected));
        }

        // ---- 本级经验进度(主界面经验条;与等级曲线同源) ----

        [TestCase(0, 1, 0, 100)]      // 1 级起点:0/100
        [TestCase(99, 1, 99, 100)]
        [TestCase(100, 2, 0, 150)]    // 刚升 2 级:本级归零,下一级要 150
        [TestCase(249, 2, 149, 150)]
        [TestCase(250, 3, 0, 200)]
        [TestCase(300, 3, 50, 200)]
        public void LevelProgress_MatchesCurve(int xp, int level, int into, int need)
        {
            Assert.That(MetaRules.LevelProgress(xp, out int gotInto, out int gotNeed), Is.EqualTo(level));
            Assert.That(gotInto, Is.EqualTo(into), "本级已得经验");
            Assert.That(gotNeed, Is.EqualTo(need), "升下一级所需经验");
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(99)]
        [TestCase(100)]
        [TestCase(4321)]
        [TestCase(999999)]
        public void LevelProgress_AgreesWithCharacterLevel(int xp) // 两个入口不得各算各的
        {
            Assert.That(MetaRules.LevelProgress(xp, out int into, out int need),
                Is.EqualTo(MetaRules.CharacterLevel(xp)));
            Assert.That(into, Is.LessThan(need), "本级已得永远够不着下一级门槛");
            Assert.That(into, Is.GreaterThanOrEqualTo(0));
        }

        [TestCase(1, 500)]
        [TestCase(6, 600)]
        [TestCase(26, 1000)]
        [TestCase(40, 1000)] // 上限 1000
        public void MaxHp_GrowsWithLevel_Capped(int level, int hp)
        {
            Assert.That(MetaRules.MaxHpFor(level), Is.EqualTo(hp));
        }

        // ---- 关卡解锁与通关结算 ----

        [Test]
        public void FirstStage_AlwaysUnlocked_OthersLocked()
        {
            var meta = new MetaState();
            Assert.That(MetaRules.IsStageUnlocked(meta, TwoChapters(), 0, 0), Is.True);
            Assert.That(MetaRules.IsStageUnlocked(meta, TwoChapters(), 0, 1), Is.False);
            Assert.That(MetaRules.IsStageUnlocked(meta, TwoChapters(), 1, 0), Is.False);
        }

        [Test]
        public void ClearingStage_UnlocksNext_ChapterNeedsFullClear()
        {
            var meta = new MetaState();
            MetaRules.ApplyStageCleared(meta, 0, 0);
            Assert.That(MetaRules.IsStageUnlocked(meta, TwoChapters(), 0, 1), Is.True);
            Assert.That(MetaRules.IsStageUnlocked(meta, TwoChapters(), 1, 0), Is.False);
            MetaRules.ApplyStageCleared(meta, 0, 1); // 第 1 章全通
            Assert.That(MetaRules.IsStageUnlocked(meta, TwoChapters(), 1, 0), Is.True);
        }

        [Test]
        public void FirstClear_50Xp_RepeatClear_10Xp()
        {
            var meta = new MetaState();
            Assert.That(MetaRules.ApplyStageCleared(meta, 0, 0), Is.True);  // 首通
            Assert.That(meta.CharacterXp, Is.EqualTo(50));
            Assert.That(MetaRules.ApplyStageCleared(meta, 0, 0), Is.False); // 重复
            Assert.That(meta.CharacterXp, Is.EqualTo(60));
        }

        // ---- 卡等级与集卡升级(19.3.3 白卡基准) ----

        [Test]
        public void CardLevel_DefaultsToOne()
        {
            Assert.That(MetaRules.CardLevel(new MetaState(), "焚"), Is.EqualTo(1));
        }

        [Test]
        public void UpgradeCard_ConsumesCopiesAndInk()
        {
            var meta = new MetaState { Ink = 100 };
            MetaRules.AddCardCopies(meta, "焚", 3);
            Assert.That(MetaRules.TryUpgradeCard(meta, "焚"), Is.True); // 需 2 卡 + 20 墨锭
            Assert.That(MetaRules.CardLevel(meta, "焚"), Is.EqualTo(2));
            Assert.That(meta.CardCopies["焚"], Is.EqualTo(1));
            Assert.That(meta.Ink, Is.EqualTo(80));
        }

        [Test]
        public void CanUpgradeCard_ChecksWithoutMutating()
        {
            var meta = new MetaState { Ink = 100 };
            MetaRules.AddCardCopies(meta, "焚", 3);
            Assert.That(MetaRules.CanUpgradeCard(meta, "焚"), Is.True);
            Assert.That(meta.Ink, Is.EqualTo(100)); // 只判定不消耗
            Assert.That(meta.CardCopies["焚"], Is.EqualTo(3));

            Assert.That(MetaRules.CanUpgradeCard(new MetaState { Ink = 1000 }, "焚"), Is.False); // 无重复卡
            var poor = new MetaState { Ink = 5 };
            MetaRules.AddCardCopies(poor, "焚", 10);
            Assert.That(MetaRules.CanUpgradeCard(poor, "焚"), Is.False); // 墨锭不足
            var maxed = new MetaState { Ink = 99999 };
            maxed.CardLevels["焚"] = MetaRules.MaxCardLevel;
            MetaRules.AddCardCopies(maxed, "焚", 999);
            Assert.That(MetaRules.CanUpgradeCard(maxed, "焚"), Is.False); // 满级
        }

        [Test]
        public void UpgradeCard_InsufficientCopies_Fails()
        {
            var meta = new MetaState { Ink = 1000 };
            MetaRules.AddCardCopies(meta, "焚", 1);
            Assert.That(MetaRules.TryUpgradeCard(meta, "焚"), Is.False);
            Assert.That(MetaRules.CardLevel(meta, "焚"), Is.EqualTo(1));
            Assert.That(meta.Ink, Is.EqualTo(1000)); // 不动状态
        }

        [Test]
        public void UpgradeCard_InsufficientInk_Fails()
        {
            var meta = new MetaState { Ink = 5 };
            MetaRules.AddCardCopies(meta, "焚", 10);
            Assert.That(MetaRules.TryUpgradeCard(meta, "焚"), Is.False);
            Assert.That(meta.CardCopies["焚"], Is.EqualTo(10));
        }

        [Test]
        public void UpgradeCard_AtMaxLevel_Fails()
        {
            var meta = new MetaState { Ink = 999999 };
            meta.CardLevels["焚"] = MetaRules.MaxCardLevel;
            MetaRules.AddCardCopies(meta, "焚", 9999);
            Assert.That(MetaRules.TryUpgradeCard(meta, "焚"), Is.False);
        }

        [TestCase(10, 1, 10)]
        [TestCase(18, 3, 22)]   // 18 × 1.2 = 21.6 → 22(向上取整)
        [TestCase(18, 10, 35)]  // 18 × 1.9 = 34.2 → 35
        [TestCase(6, 2, 7)]     // 低数值字升 1 级即 +1 可感(2026-07-19:floor 吞增幅的修正)
        [TestCase(3, 2, 4)]
        public void ScaleByCardLevel_TenPercentPerLevel_Ceiled(int baseValue, int level, int expected)
        {
            Assert.That(MetaRules.ScaleByCardLevel(baseValue, level), Is.EqualTo(expected));
        }

        [TestCase(1, 100)]   // 1 级 = 基准,伤害与引入攻击力之前逐字节相同
        [TestCase(2, 102)]
        [TestCase(11, 120)]
        [TestCase(25, 148)]
        [TestCase(26, 150)]  // 与 MaxHpFor 同在 26 级触顶
        [TestCase(40, 150)]  // 封顶后不再涨
        public void AttackFor_GrowsTwoPerLevel_CapsAt150(int level, int expected)
        {
            Assert.That(MetaRules.AttackFor(level), Is.EqualTo(expected));
        }

        [Test]
        public void AttackFor_CapsAtSameLevelAsMaxHp()
        {
            // 两条角色属性曲线刻意同形同封顶级(19.2.1),口径一致才好记也好平衡。
            // 分开写死会在改了一条忘了另一条时静默漂移,这条守住它们的耦合。
            Assert.That(MetaRules.AttackFor(26), Is.EqualTo(150));
            Assert.That(MetaRules.MaxHpFor(26), Is.EqualTo(1000));
            Assert.That(MetaRules.AttackFor(25), Is.LessThan(150));
            Assert.That(MetaRules.MaxHpFor(25), Is.LessThan(1000));
        }

        // ---- 防御轴的两条角色属性曲线(E-b4 T4,2026-08-12)----

        [TestCase(1, 0)]    // 起点 0:护甲是土系字给的,不是白送的 —— 1 级行为与引入 DEF 之前逐字节相同
        [TestCase(2, 0)]    // 整数除表达 k = 1/2:每两级 +1
        [TestCase(3, 1)]
        [TestCase(11, 5)]
        [TestCase(25, 12)]  // (25−1)/2 = 12,恰好触顶
        [TestCase(26, 12)]
        public void DefenseFor_GrowsHalfPerLevel_CapsAt12(int level, int expected)
        {
            Assert.That(MetaRules.DefenseFor(level), Is.EqualTo(expected));
        }

        [TestCase(1, 0)]    // 起点 0,同 DefenseFor
        [TestCase(2, 1)]    // k = 1:闪避是概率轴,满级 25% 与 DEF 12 对 R_in=60 的 −20% 同量级
        [TestCase(11, 10)]
        [TestCase(25, 24)]
        [TestCase(26, 25)]  // 与 MaxHpFor / AttackFor 同在 26 级触顶
        public void DodgeFor_GrowsOnePerLevel_CapsAt25(int level, int expected)
        {
            Assert.That(MetaRules.DodgeFor(level), Is.EqualTo(expected));
        }

        [Test]
        public void DefenseAndDodge_StayCapped_FarAboveCapLevel()
        {
            // 封顶必须用**远超封顶级**的等级来证:E-b1 的评审教训是 TestCase(26, 150) 那种
            // 「自然公式值恰好等于封顶值」的用例,把 Math.Min 整个删掉它照样绿,零判别力。
            // 40 级的自然值是 DEF 19 / 闪避 39,与封顶值差得远,删掉封顶这条必红。
            Assert.That(MetaRules.DefenseFor(40), Is.EqualTo(12));
            Assert.That(MetaRules.DodgeFor(40), Is.EqualTo(25));
            Assert.That(MetaRules.DefenseFor(200), Is.EqualTo(12));
            Assert.That(MetaRules.DodgeFor(200), Is.EqualTo(25));
        }

        [Test]
        public void DefenseAndDodge_AreZeroAtLevelOne()
        {
            // T4 的恒等性硬线:1 级角色的战斗行为与引入这两条曲线之前逐字节相同 ——
            // 闪避 0 让 AttackHits 走 hitRate ≥ 100 的短路(一次随机都不摇),
            // 护甲 0 让 max(0, x − 0) == x。任何一个起点不是 0 都会让黄金轨迹整体发散。
            Assert.That(MetaRules.DefenseFor(1), Is.EqualTo(0));
            Assert.That(MetaRules.DodgeFor(1), Is.EqualTo(0));
        }

        // ---- 卡等级进战斗:等级系数先作用于基础值,再走生克 ----

        [Test]
        public void Battle_UsesCardLevels_ForEffectValues()
        {
            var graph = new RecipeGraph(new[]
            {
                new CharDef("木", Element.Wood),
                new CharDef("火", Element.Fire),
                new CharDef("林", Element.Wood, new[] { "木", "木" }),
                new CharDef("焚", Element.Fire, new[] { "林", "火" }, rarity: CardRarity.Purple,
                    effects: new[] { new EffectDef(EffectKind.DamageAll, 18) }),
            });
            var engine = new BattleEngine(graph, new BattleConfig(),
                new[] { "焚" }, System.Array.Empty<string>(),
                new[] { new EnemyDef("怔", Element.Heart, 200, 3) }, seed: 1,
                cardLevels: new System.Collections.Generic.Dictionary<string, int> { ["焚"] = 3 });
            engine.Cast("焚");
            // 基础 18 → 3 级 ×1.2 = 21.6 → 向上取整 22(相生 ×3 已取消,不再乘 3)
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(200 - 22));
        }

        // ---- 收集(19.3.4) ----

        [Test]
        public void AcquireCard_FirstTimeOwns_RepeatBecomesCopies()
        {
            var meta = new MetaState();
            MetaRules.AcquireCard(meta, "炎");
            Assert.That(meta.OwnedCards, Does.Contain("炎"));
            Assert.That(meta.CardCopies.ContainsKey("炎"), Is.False.Or.EqualTo(false)); // 首张不是重复卡
            MetaRules.AcquireCard(meta, "炎");
            MetaRules.AcquireCard(meta, "炎");
            Assert.That(meta.CardCopies["炎"], Is.EqualTo(2));
            Assert.That(meta.OwnedCards, Is.EqualTo(new[] { "炎" })); // 不重复入收集
        }

        // ---- Task 2(2026-09-06):起手字库改为卡池加权抽,以下三条旧测试测的规则
        // (出阵表按等级取前 6 / 出阵不足不补 / 空出阵即空字库)已被废止,随新规则一并替换 ----

        /// <summary>抽卡测试用图:五系各两张(白 + 蓝),外加一张紫档心系字。
        /// 每个字都给配方(部件 + 部件),否则会被当成部件滤掉。</summary>
        private static RecipeGraph PoolGraph() => new(new[]
        {
            // 部件(无配方,component)
            new CharDef("丶", null, new string[0], isComponent: true),
            new CharDef("丿", null, new string[0], isComponent: true),
            // 金
            new CharDef("金白", Element.Metal, new[] { "丶", "丿" }, rarity: CardRarity.White),
            new CharDef("金蓝", Element.Metal, new[] { "丶", "丿" }, rarity: CardRarity.Blue),
            // 木
            new CharDef("木白", Element.Wood, new[] { "丶", "丿" }, rarity: CardRarity.White),
            new CharDef("木蓝", Element.Wood, new[] { "丶", "丿" }, rarity: CardRarity.Blue),
            // 水
            new CharDef("水白", Element.Water, new[] { "丶", "丿" }, rarity: CardRarity.White),
            new CharDef("水蓝", Element.Water, new[] { "丶", "丿" }, rarity: CardRarity.Blue),
            // 火
            new CharDef("火白", Element.Fire, new[] { "丶", "丿" }, rarity: CardRarity.White),
            new CharDef("火蓝", Element.Fire, new[] { "丶", "丿" }, rarity: CardRarity.Blue),
            // 土
            new CharDef("土白", Element.Earth, new[] { "丶", "丿" }, rarity: CardRarity.White),
            new CharDef("土蓝", Element.Earth, new[] { "丶", "丿" }, rarity: CardRarity.Blue),
            // 心系紫档:不参与前 5 张,只能经第 6 张进场
            new CharDef("心紫", Element.Heart, new[] { "丶", "丿" }, rarity: CardRarity.Purple),
        });

        private static MetaState PoolMeta(params string[] owned)
        {
            var meta = new MetaState();
            foreach (var id in owned) meta.OwnedCards.Add(id);
            return meta;
        }

        private static readonly string[] FullPool =
        {
            "金白", "金蓝", "木白", "木蓝", "水白", "水蓝",
            "火白", "火蓝", "土白", "土蓝", "心紫",
        };

        [Test]
        public void StartingLibrary_CoversEveryElement_InFirstFive()
        {
            var graph = PoolGraph();
            var meta = PoolMeta(FullPool);
            // 多摇几个种子:五行覆盖是硬保证,不能只在某个幸运种子上成立
            for (int seed = 1; seed <= 30; seed++)
            {
                var library = MetaRules.StartingLibrary(meta, graph, new GameRandom(seed));
                Assert.That(library.Count, Is.EqualTo(6), $"seed {seed}: 五行各一 + 最高档一张");
                var elements = new List<Element>();
                for (int i = 0; i < 5; i++) elements.Add(graph.Get(library[i]).Element.Value);
                foreach (var e in new[] { Element.Metal, Element.Wood, Element.Water, Element.Fire, Element.Earth })
                    Assert.That(elements.Contains(e), Is.True, $"seed {seed}: 前 5 张缺 {e}");
            }
        }

        [Test]
        public void StartingLibrary_SixthCard_IsFromTopRarityInPool()
        {
            var graph = PoolGraph();
            var meta = PoolMeta(FullPool);   // 池里最高档 = 心紫(Purple)
            for (int seed = 1; seed <= 30; seed++)
            {
                var library = MetaRules.StartingLibrary(meta, graph, new GameRandom(seed));
                Assert.That(graph.Get(library[5]).Rarity, Is.EqualTo(CardRarity.Purple),
                    $"seed {seed}: 第 6 张必须来自卡池里实际存在的最高档");
            }
        }

        [Test]
        public void StartingLibrary_SixthCard_FallsBackToLowerTier_WhenPoolTopsOutLower()
        {
            var graph = PoolGraph();
            // 池里最高只到蓝(不含心紫)
            var meta = PoolMeta("金白", "金蓝", "木白", "木蓝", "水白", "水蓝",
                                "火白", "火蓝", "土白", "土蓝");
            for (int seed = 1; seed <= 20; seed++)
            {
                var library = MetaRules.StartingLibrary(meta, graph, new GameRandom(seed));
                Assert.That(graph.Get(library[5]).Rarity, Is.EqualTo(CardRarity.Blue),
                    $"seed {seed}: 最高档只到蓝就从蓝里抽");
            }
        }

        [Test]
        public void StartingLibrary_AllowsDuplicate_WhenSixthCollidesWithFirstFive()
        {
            var graph = PoolGraph();
            // 金系只有一张蓝,且蓝是全池最高档 —— 前 5 张的金位与第 6 张必然都是「金蓝」
            var meta = PoolMeta("金蓝", "木白", "水白", "火白", "土白");
            var library = MetaRules.StartingLibrary(meta, graph, new GameRandom(7));
            Assert.That(library.Count, Is.EqualTo(6), "撞了也照收,不去重");
            int golds = 0;
            foreach (var id in library) if (id == "金蓝") golds++;
            Assert.That(golds, Is.EqualTo(2), "同一张字拿两份(2026-09-06 拍板:不去重)");
        }

        [Test]
        public void StartingLibrary_SkipsElementsWithNoOwnedCards()
        {
            var graph = PoolGraph();
            var meta = PoolMeta("金白", "木白");   // 只有金木两系
            var library = MetaRules.StartingLibrary(meta, graph, new GameRandom(3));
            Assert.That(library.Count, Is.EqualTo(3), "两系各一 + 最高档一张;缺的系直接跳过,不补齐");
        }

        [Test]
        public void StartingLibrary_EmptyPool_IsEmpty()
        {
            var library = MetaRules.StartingLibrary(new MetaState(), PoolGraph(), new GameRandom(1));
            Assert.That(library, Is.Empty);
        }

        [Test]
        public void StartingLibrary_SameSeed_SameResult()
        {
            var graph = PoolGraph();
            var meta = PoolMeta(FullPool);
            var a = MetaRules.StartingLibrary(meta, graph, new GameRandom(42));
            var b = MetaRules.StartingLibrary(meta, graph, new GameRandom(42));
            Assert.That(a, Is.EqualTo(b), "同种子同结果 —— 断点续爬要靠这条");
        }

        [Test]
        public void StartingLibrary_HeartCards_NeverEnterTheFirstFive()
        {
            var graph = PoolGraph();
            var meta = PoolMeta(FullPool);
            for (int seed = 1; seed <= 30; seed++)
            {
                var library = MetaRules.StartingLibrary(meta, graph, new GameRandom(seed));
                for (int i = 0; i < 5; i++)
                    Assert.That(library[i], Is.Not.EqualTo("心紫"),
                        $"seed {seed}: 心系不参与五行那 5 格");
            }
        }

        [Test]
        public void StartingLibrary_ExcludesComponents()
        {
            var graph = PoolGraph();
            var meta = PoolMeta("金白", "丶", "丿");   // 部件混进 OwnedCards
            var library = MetaRules.StartingLibrary(meta, graph, new GameRandom(5));
            foreach (var id in library)
                Assert.That(graph.Get(id).IsComponent, Is.False, "部件与中间产物字不是抽卡候选");
        }

        [Test]
        public void StartingLibrary_WeightsFavorCommonRarities()
        {
            var graph = PoolGraph();
            // 金系一白一蓝:白 150‰ vs 蓝 300‰ —— 蓝该明显更多
            var meta = PoolMeta("金白", "金蓝");
            int blue = 0;
            for (int seed = 1; seed <= 400; seed++)
            {
                var library = MetaRules.StartingLibrary(meta, graph, new GameRandom(seed));
                if (library[0] == "金蓝") blue++;
            }
            // 理论 300/(150+300) ≈ 66.7%;给足余量,只守「明显偏向蓝」
            Assert.That(blue, Is.GreaterThan(220), $"400 次里蓝只出了 {blue} 次,权重没生效");
            Assert.That(blue, Is.LessThan(360), $"400 次里蓝出了 {blue} 次,白档像是被整档滤掉了");
        }

        [Test]
        public void StartingLibrary_BowenPerkAppendsExtraDraws()
        {
            var graph = PoolGraph();
            var meta = PoolMeta(FullPool);
            int baseline = MetaRules.StartingLibrary(meta, graph, new GameRandom(9)).Count;
            Assert.That(baseline, Is.EqualTo(6));
            meta.PerkLevels["bowen"] = 1;
            Assert.That(MetaRules.StartingLibrary(meta, graph, new GameRandom(9)).Count,
                Is.EqualTo(7), "博闻每级追加一张自由加权抽");
        }

        [Test]
        public void RunEngine_ForwardsCardLevels_ToBattles()
        {
            var graph = new RecipeGraph(new[]
            {
                new CharDef("火", Element.Fire,
                    effects: new[] { new EffectDef(EffectKind.DamageSingle, 10) }),
            });
            var run = new RunEngine(graph,
                new RunConfig
                {
                    Encounters = new[] { new[] { new EnemyDef("怔", Element.Heart, 100, 1) } },
                    RewardPool = new[] { "火" },
                },
                new BattleConfig(), new string[0], new[] { "火" }, seed: 1,
                cardLevels: new System.Collections.Generic.Dictionary<string, int> { ["火"] = 6 });
            run.Battle.Cast("火", 0);
            // 10 × (1 + 0.5) = 15
            Assert.That(run.Battle.Enemies[0].Hp, Is.EqualTo(85));
        }

        // ---- 存档序列化 ----

        [Test]
        public void Save_RoundTrips()
        {
            var meta = new MetaState { CharacterXp = 160, Ink = 42 };
            meta.CardLevels["焚"] = 3;
            MetaRules.AddCardCopies(meta, "灯", 7);
            MetaRules.ApplyStageCleared(meta, 0, 0);

            var restored = SaveSerializer.FromJson(SaveSerializer.ToJson(meta));
            Assert.That(restored.CharacterXp, Is.EqualTo(210)); // 160 + 首通 50
            Assert.That(restored.Ink, Is.EqualTo(42));
            Assert.That(restored.CardLevels["焚"], Is.EqualTo(3));
            Assert.That(restored.CardCopies["灯"], Is.EqualTo(7));
            Assert.That(MetaRules.IsStageUnlocked(restored, TwoChapters(), 0, 1), Is.True);
        }

        [Test]
        public void Save_RoundTrips_PerkLevels()
        {
            var meta = new MetaState();
            meta.PerkLevels["yangyuan"] = 3;
            meta.PerkLevels["yiqi"] = 1;
            var restored = SaveSerializer.FromJson(SaveSerializer.ToJson(meta));
            Assert.That(PerkRules.PerkLevel(restored, "yangyuan"), Is.EqualTo(3));
            Assert.That(PerkRules.PerkLevel(restored, "yiqi"), Is.EqualTo(1));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("not json{{")]
        public void Save_CorruptOrMissing_ReturnsFreshState(string json)
        {
            var meta = SaveSerializer.FromJson(json);
            Assert.That(meta, Is.Not.Null);
            Assert.That(meta.CharacterXp, Is.EqualTo(0));
            Assert.That(MetaRules.CardLevel(meta, "焚"), Is.EqualTo(1));
        }

        // ---- 字表裁剪后的存档清洗(旧存档引用已下架字不得崩溃) ----

        [Test]
        public void PruneUnknownCards_RemovesRetiredIdsEverywhere()
        {
            var graph = new RecipeGraph(new[]
            {
                new CharDef("火", Element.Fire),
                new CharDef("炎", Element.Fire, new[] { "火", "火" }),
            });
            var meta = new MetaState();
            meta.OwnedCards.AddRange(new[] { "炎", "灯" });
            meta.CardLevels["灯"] = 3;
            meta.CardCopies["灯"] = 5;
            meta.CardLevels["炎"] = 2;
            meta.Shop.CardSlots.AddRange(new[] { "灯", "炎" });
            meta.Shop.CardSold.AddRange(new[] { false, true });
            meta.Chests.Add(new ChestState { CardPool = { "灯" } });
            meta.Chests.Add(new ChestState { CardPool = { "灯", "炎" } });
            meta.EndlessV2 = new EndlessSaveState
            {
                Library = { "炎", "灯", "火" },
                Pool = { "火", "丁" },
            };

            MetaRules.PruneUnknownCards(meta, graph);

            Assert.That(meta.OwnedCards, Is.EqualTo(new[] { "炎" }));
            Assert.That(meta.CardLevels.ContainsKey("灯"), Is.False);
            Assert.That(meta.CardLevels["炎"], Is.EqualTo(2));
            Assert.That(meta.CardCopies.ContainsKey("灯"), Is.False);
            Assert.That(meta.Shop.DayStamp, Is.EqualTo(-1)); // 货架含下架字 → 整架作废重摆
            Assert.That(meta.Chests.Count, Is.EqualTo(1));   // 奖池清空的箱子一并移除
            Assert.That(meta.Chests[0].CardPool, Is.EqualTo(new[] { "炎" }));
            Assert.That(meta.EndlessV2.Library, Is.EqualTo(new[] { "炎", "火" }));
            Assert.That(meta.EndlessV2.Pool, Is.EqualTo(new[] { "火" }));
        }

        [Test]
        public void PruneUnknownCards_CleanState_Untouched()
        {
            var graph = new RecipeGraph(new[] { new CharDef("火", Element.Fire) });
            var meta = new MetaState();
            meta.OwnedCards.Add("火");
            meta.Shop.DayStamp = 7;
            meta.Shop.CardSlots.Add("火");
            meta.Shop.CardSold.Add(false);

            MetaRules.PruneUnknownCards(meta, graph);

            Assert.That(meta.OwnedCards, Is.EqualTo(new[] { "火" }));
            Assert.That(meta.Shop.DayStamp, Is.EqualTo(7));
        }

        [Test]
        public void SpeedFor_StartsAtBaselineAndGrowsSlowly()
        {
            // 速度是最强属性(同时翻倍输出与资源产出,spec 口径 5),成长必须压得很慢
            Assert.That(MetaRules.SpeedFor(1), Is.EqualTo(100), "1 级 = 基准 = 与敌人同速");
            Assert.That(MetaRules.SpeedFor(26), Is.EqualTo(125), "封顶级 +25%");
            Assert.That(MetaRules.SpeedFor(99), Is.EqualTo(125), "封顶后不再涨");
        }

        [Test]
        public void StartingCollection_EveryCharExistsAndIsCraftable()
        {
            // 幽灵字守卫(2026-09-05):字表里没有的字放在这里不会报错,只会静默不生效。
            // 「有配方」是 2026-08-05 拍板的口径 —— 拆了要回得来。
            var graph = CharTableTests.RealGraph();
            foreach (var id in MetaRules.StartingCollection)
            {
                Assert.That(graph.TryGet(id, out var def), Is.True, $"起始收藏的「{id}」不在字表里");
                Assert.That(def.IsLeaf, Is.False, $"起始收藏的「{id}」没有配方,拆了回不来");
                Assert.That(def.IsComponent, Is.False, $"起始收藏的「{id}」是部件,不该进收藏");
            }
        }

        // TODO(2026-09-06):教程演示字的起手保证随出阵一起没了。
        // 此前靠「默认出阵必含 Tutorial.DemoChar」保证首局起手拆得动它;起手改成随机抽之后
        // 这个保证不存在,教程第一步可能无字可拆。用户将另行修改新游戏的起手解锁字卡与教程本身。
        // 见 docs/superpowers/specs/2026-09-06-移除出阵-卡池抽卡-design.md 第五节。

        [Test]
        public void RarityWeights_AreMonotonicallyDecreasing_AndSumToThousand()
        {
            Assert.That(MetaRules.RarityWeights.Length, Is.EqualTo(7), "七档稀有度各一个权重");
            int sum = 0;
            foreach (var w in MetaRules.RarityWeights) sum += w;
            Assert.That(sum, Is.EqualTo(1000), "千分比,合计 1000");
            // 「稀有度越高概率越低」只从绿档往上单调 —— 白档刻意压在绿之下(2026-09-06 拍板)
            for (int i = 1; i + 1 < MetaRules.RarityWeights.Length; i++)
                Assert.That(MetaRules.RarityWeights[i], Is.GreaterThan(MetaRules.RarityWeights[i + 1]),
                    $"第 {i} 档权重必须高于第 {i + 1} 档");
            Assert.That(MetaRules.RarityWeights[(int)CardRarity.White - 1],
                Is.LessThan(MetaRules.RarityWeights[(int)CardRarity.Green - 1]), "白档压在绿之下");
        }

        [Test]
        public void RarityOrder_IsAscendingAndCoversEveryRarity()
        {
            Assert.That(MetaRules.RarityOrder.Length, Is.EqualTo(7));
            for (int i = 0; i + 1 < MetaRules.RarityOrder.Length; i++)
                Assert.That((int)MetaRules.RarityOrder[i], Is.LessThan((int)MetaRules.RarityOrder[i + 1]),
                    "必须按枚举数值升序 —— 遍历顺序是同种子同结果的前提");
            Assert.That(MetaRules.RarityOrder[0], Is.EqualTo(CardRarity.White));
            Assert.That(MetaRules.RarityOrder[6], Is.EqualTo(CardRarity.Red));
        }

        [Test]
        public void BuildBattleConfig_UnlockedChars_IsTheWholeCollection()
        {
            var meta = new MetaState();
            meta.OwnedCards.Add("灯");
            meta.OwnedCards.Add("炎");
            var config = MetaRules.BuildBattleConfig(meta, new[] { "灯" });
            Assert.That(config.UnlockedChars, Is.EqualTo(meta.OwnedCards),
                "出阵废止后可合成集 = 整个已解锁卡池(2026-09-06)");
        }
    }
}
