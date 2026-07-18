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

        [TestCase(1, 50)]
        [TestCase(6, 60)]
        [TestCase(26, 100)]
        [TestCase(40, 100)] // 上限 100
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

        // ---- 卡等级进战斗:等级系数先作用于基础值,再走生克 ----

        [Test]
        public void Battle_UsesCardLevels_ForEffectValues()
        {
            var graph = new RecipeGraph(new[]
            {
                new CharDef("木", Element.Wood),
                new CharDef("火", Element.Fire),
                new CharDef("林", Element.Wood, new[] { "木", "木" }),
                new CharDef("焚", Element.Fire, new[] { "林", "火" }, apCost: 2,
                    effects: new[] { new EffectDef(EffectKind.DamageAll, 18) }),
            });
            var engine = new BattleEngine(graph, new BattleConfig(),
                new[] { "焚" }, System.Array.Empty<string>(),
                new[] { new EnemyDef("怔", Element.Heart, 200, 3) }, seed: 1,
                cardLevels: new System.Collections.Generic.Dictionary<string, int> { ["焚"] = 3 });
            engine.Cast("焚");
            // 基础 18 → 3 级 ×1.2 = 21.6 → 向上取整 22 → 木生火 ×3 = 66
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(200 - 66));
        }

        // ---- 收集与出阵卡组(19.3.4) ----

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

        // 出阵列表(2026-07-19 拍板):≤15 字、每属性≤5、≤3 种属性
        private static RecipeGraph DeckGraph() => new(new[]
        {
            new CharDef("灯", Element.Fire), new CharDef("炎", Element.Fire),
            new CharDef("烧", Element.Fire), new CharDef("燃", Element.Fire),
            new CharDef("灼", Element.Fire), new CharDef("炽", Element.Fire),
            new CharDef("林", Element.Wood), new CharDef("杜", Element.Wood),
            new CharDef("汀", Element.Water), new CharDef("钉", Element.Metal),
            new CharDef("圭", Element.Earth), new CharDef("焚", Element.Fire),
        });

        private static MetaState OwnAll(params string[] cards)
        {
            var meta = new MetaState();
            foreach (var card in cards) MetaRules.AcquireCard(meta, card);
            return meta;
        }

        [Test]
        public void TrySetDeck_ValidatesOwnershipAndDuplicates()
        {
            var graph = DeckGraph();
            var meta = OwnAll("灯", "炎");

            Assert.That(MetaRules.TrySetDeck(meta, new[] { "灯", "炎" }, graph), Is.True);
            Assert.That(meta.Deck, Is.EqualTo(new[] { "灯", "炎" }));

            Assert.That(MetaRules.TrySetDeck(meta, new[] { "焚" }, graph), Is.False);      // 未收集
            Assert.That(MetaRules.TrySetDeck(meta, new[] { "灯", "灯" }, graph), Is.False); // 重复
            Assert.That(meta.Deck, Is.EqualTo(new[] { "灯", "炎" })); // 失败不动状态
        }

        [Test]
        public void TrySetDeck_PerElementLimitFive()
        {
            var graph = DeckGraph();
            var meta = OwnAll("灯", "炎", "烧", "燃", "灼", "炽");
            // 六张火:超「每属性最多 5」
            Assert.That(MetaRules.TrySetDeck(meta,
                new[] { "灯", "炎", "烧", "燃", "灼", "炽" }, graph), Is.False);
            Assert.That(MetaRules.TrySetDeck(meta,
                new[] { "灯", "炎", "烧", "燃", "灼" }, graph), Is.True);
        }

        [Test]
        public void TrySetDeck_ElementKindsLimitThree()
        {
            var graph = DeckGraph();
            var meta = OwnAll("灯", "林", "汀", "钉", "圭");
            // 火木水金四种属性:超「最多 3 种」
            Assert.That(MetaRules.TrySetDeck(meta, new[] { "灯", "林", "汀", "钉" }, graph), Is.False);
            Assert.That(MetaRules.TrySetDeck(meta, new[] { "灯", "林", "汀" }, graph), Is.True);
        }

        [Test]
        public void StartingLibrary_TakesTopSixByLevel_FromRoster()
        {
            var graph = DeckGraph();
            var meta = OwnAll("灯", "炎", "烧", "燃", "灼", "林", "杜", "圭");
            meta.CardLevels["烧"] = 5;
            meta.CardLevels["炎"] = 3;
            meta.CardLevels["燃"] = 1;
            meta.CardLevels["圭"] = 9; // 不在出阵列表,不该带出
            MetaRules.TrySetDeck(meta, new[] { "灯", "炎", "烧", "燃", "灼", "林", "杜" }, graph);

            var library = MetaRules.StartingLibrary(meta);
            Assert.That(library.Count, Is.EqualTo(6)); // 起手 = 字库容量 6
            Assert.That(library, Does.Contain("烧").And.Contain("炎")); // 等级高者优先
            Assert.That(library, Does.Not.Contain("圭")); // 列表外不带出(列表足 6 时)
        }

        [Test]
        public void StartingLibrary_RosterShort_FillsFromOwned()
        {
            var graph = DeckGraph();
            var meta = OwnAll("灯", "烧");
            meta.CardLevels["烧"] = 5;
            MetaRules.TrySetDeck(meta, new[] { "灯" }, graph);
            var library = MetaRules.StartingLibrary(meta);
            Assert.That(library, Is.EquivalentTo(new[] { "灯", "烧" })); // 列表不足按等级补齐
        }

        [Test]
        public void StartingLibrary_EmptyDeck_UsesOwnedCards()
        {
            var meta = new MetaState();
            MetaRules.AcquireCard(meta, "灯");
            Assert.That(MetaRules.StartingLibrary(meta), Is.EqualTo(new[] { "灯" }));
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
    }
}
