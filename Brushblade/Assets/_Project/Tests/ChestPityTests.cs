using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>宝箱七档对齐卡稀有度 + 金/橙/红计数保底(2026-08-29 拍板)。
    /// 权重表单位是千分比:红在鎏金匣只有 0.1%,百分比装不下。</summary>
    public class ChestPityTests
    {
        private sealed class FakeTime : ITimeSource
        {
            public long NowUnixSeconds { get; set; } = 1_000_000;
        }

        /// <summary>七档稀有度各一个字,配方全是部件 → 前置恒满足,不受叠字门槛干扰。</summary>
        private static RecipeGraph RarityGraph() => new(new[]
        {
            new CharDef("c1", Element.Fire),
            new CharDef("c2", Element.Water),
            new CharDef("r1", Element.Fire, new[] { "c1", "c2" }, rarity: CardRarity.White),
            new CharDef("r2", Element.Fire, new[] { "c1", "c2" }, rarity: CardRarity.Green),
            new CharDef("r3", Element.Fire, new[] { "c1", "c2" }, rarity: CardRarity.Blue),
            new CharDef("r4", Element.Fire, new[] { "c1", "c2" }, rarity: CardRarity.Purple),
            new CharDef("r5", Element.Fire, new[] { "c1", "c2" }, rarity: CardRarity.Gold),
            new CharDef("r6", Element.Fire, new[] { "c1", "c2" }, rarity: CardRarity.Orange),
            new CharDef("r7", Element.Fire, new[] { "c1", "c2" }, rarity: CardRarity.Red),
        });

        private static readonly string[] RarityPool = { "r1", "r2", "r3", "r4", "r5", "r6", "r7" };

        /// <summary>在给定 meta 上掉一只箱、走完计时、开掉。计数保底跨箱累计,故 meta 由调用方持有。</summary>
        private static ChestRewards OpenOn(MetaState meta, ChestTier tier, int seed,
            RecipeGraph graph, string[] pool = null)
        {
            var time = new FakeTime();
            Assert.That(ChestRules.TryAwardChest(meta, tier, pool ?? RarityPool, time), Is.True);
            int index = meta.Chests.Count - 1;
            Assert.That(ChestRules.TryStartOpening(meta, index, time), Is.True);
            time.NowUnixSeconds += ChestRules.DurationSeconds[(int)tier - 1];
            Assert.That(ChestRules.TryOpen(meta, index, time, new GameRandom(seed), out var rewards, graph),
                Is.True);
            return rewards;
        }

        private static int CountOf(ChestRewards rewards, CardRarity rarity, RecipeGraph graph)
        {
            int count = 0;
            foreach (var card in rewards.Cards)
                if (graph.Get(card).Rarity == rarity) count++;
            return count;
        }

        /// <summary>找一个该档自然不出该稀有度的种子,用来把保底和自然掉落分开验。</summary>
        private static int SeedWithout(ChestTier tier, CardRarity rarity, RecipeGraph graph)
        {
            for (int seed = 1; seed <= 500; seed++)
                if (CountOf(OpenOn(new MetaState(), tier, seed, graph), rarity, graph) == 0)
                    return seed;
            Assert.Fail($"500 个种子里找不到「{tier} 不出 {rarity}」的样本");
            return 0;
        }

        // ---- 七档常量表 ----

        /// <summary>七张按档索引的表必须同长:漏改一张,开高档箱就是数组越界。</summary>
        [Test]
        public void TierTables_AllCoverSevenTiers()
        {
            const int tiers = 7;
            Assert.That((int)ChestTier.Crimson, Is.EqualTo(tiers), "赤霄是最高档");
            Assert.That(ChestRules.DurationSeconds.Length, Is.EqualTo(tiers));
            Assert.That(ChestRules.AdReductionSeconds.Length, Is.EqualTo(tiers));
            Assert.That(ChestRules.CardCount.Length, Is.EqualTo(tiers));
            Assert.That(ChestRules.InkReward.Length, Is.EqualTo(tiers));
            Assert.That(ShopRules.ChestPrice.Length, Is.EqualTo(tiers));
            for (int tier = 1; tier <= tiers; tier++)
            {
                Assert.That(ChestRules.TierName((ChestTier)tier), Is.Not.EqualTo("?"),
                    $"第 {tier} 档没有名字");
                Assert.That(ChestRules.CardRarityWeightsFor((ChestTier)tier).Count, Is.EqualTo(7),
                    "权重表每档七列,与 CardRarity 一一对应");
            }
            Assert.That(ChestRules.TierWeightsFor(1).Count, Is.EqualTo(tiers), "商城掉档权重也是七列");
        }

        [Test]
        public void CardRarityWeights_EachTierSumsToOneThousand()
        {
            for (int tier = 1; tier <= 7; tier++)
            {
                int sum = 0;
                foreach (int weight in ChestRules.CardRarityWeightsFor((ChestTier)tier)) sum += weight;
                Assert.That(sum, Is.EqualTo(ChestRules.RarityWeightTotal),
                    $"第 {tier} 档权重合计不是 {ChestRules.RarityWeightTotal}‰");
            }
        }

        /// <summary>用户拍板的九个数(2026-08-29),单位千分比。</summary>
        [TestCase(ChestTier.Celadon, CardRarity.Gold, 10)]
        [TestCase(ChestTier.Rosewood, CardRarity.Gold, 20)]
        [TestCase(ChestTier.Gilded, CardRarity.Gold, 50)]
        [TestCase(ChestTier.Rosewood, CardRarity.Orange, 5)]
        [TestCase(ChestTier.Gilded, CardRarity.Orange, 10)]
        [TestCase(ChestTier.Vermilion, CardRarity.Orange, 20)]
        [TestCase(ChestTier.Gilded, CardRarity.Red, 1)]
        [TestCase(ChestTier.Vermilion, CardRarity.Red, 5)]
        [TestCase(ChestTier.Crimson, CardRarity.Red, 10)]
        public void CardRarityWeights_MatchPinnedNumbers(ChestTier tier, CardRarity rarity, int expected)
        {
            Assert.That(ChestRules.CardRarityWeightsFor(tier)[(int)rarity - 1], Is.EqualTo(expected));
        }

        /// <summary>白档 12 个字曾因权重列写死 0 而永远掉不出来(其中 7 个连商城都没有)。</summary>
        [Test]
        public void Paper_YieldsWhiteCards()
        {
            var graph = RarityGraph();
            int white = 0;
            for (int seed = 1; seed <= 30; seed++)
                white += CountOf(OpenOn(new MetaState(), ChestTier.Paper, seed, graph), CardRarity.White, graph);
            Assert.That(white, Is.GreaterThan(0), "素纸匣白权重 400‰,30 箱 90 张不该一张白都没有");
        }

        // ---- 计数保底 ----

        /// <summary>红保底:第 20 个赤霄箱强制给一张红(同种子下自然是不出红的)。</summary>
        [Test]
        public void Pity_Red_ForcedOnTwentiethCrimsonChest()
        {
            var graph = RarityGraph();
            int seed = SeedWithout(ChestTier.Crimson, CardRarity.Red, graph);

            var meta = new MetaState { RedPity = 19 };
            var rewards = OpenOn(meta, ChestTier.Crimson, seed, graph);

            Assert.That(CountOf(rewards, CardRarity.Red, graph), Is.EqualTo(1), "保底该补上一张红");
            Assert.That(rewards.Cards.Count, Is.EqualTo(ChestRules.CardCount[(int)ChestTier.Crimson - 1]),
                "保底是替掉一张,不是额外加一张");
            Assert.That(meta.RedPity, Is.Zero, "出了红就归零");
        }

        /// <summary>差一箱不触发:同种子、RedPity=18 时仍然没有红。</summary>
        [Test]
        public void Pity_Red_NotForcedBeforeThreshold()
        {
            var graph = RarityGraph();
            int seed = SeedWithout(ChestTier.Crimson, CardRarity.Red, graph);

            var meta = new MetaState { RedPity = 18 };
            Assert.That(CountOf(OpenOn(meta, ChestTier.Crimson, seed, graph), CardRarity.Red, graph), Is.Zero);
            Assert.That(meta.RedPity, Is.EqualTo(19), "没出红,计数继续涨");
        }

        /// <summary>自然掉出红也重置计数(不是只有保底才清)。</summary>
        [Test]
        public void Pity_ResetsOnNaturalDrop()
        {
            var graph = RarityGraph();
            int seed = 0;
            for (int candidate = 1; candidate <= 500 && seed == 0; candidate++)
                if (CountOf(OpenOn(new MetaState(), ChestTier.Crimson, candidate, graph), CardRarity.Red, graph) > 0)
                    seed = candidate;
            Assert.That(seed, Is.Not.Zero, "赤霄匣红 1%×16 张,500 个种子里总该有自然出红的");

            var meta = new MetaState { RedPity = 5 };
            OpenOn(meta, ChestTier.Crimson, seed, graph);
            Assert.That(meta.RedPity, Is.Zero);
        }

        /// <summary>高档箱推进全部低档保底:赤霄箱同时是一次橙保底、一次金保底的进度。</summary>
        [Test]
        public void Pity_HigherTierAdvancesLowerPities()
        {
            var graph = RarityGraph();
            var meta = new MetaState();
            OpenOn(meta, ChestTier.Crimson, SeedWithout(ChestTier.Crimson, CardRarity.Red, graph), graph);
            Assert.That(meta.RedPity, Is.EqualTo(1));
            Assert.That(meta.OrangePity + meta.GoldPity, Is.GreaterThan(0),
                "赤霄箱也该推进橙/金的计数(没出对应稀有度时)");
        }

        /// <summary>低档箱不推进高档保底:紫檀及以下和金/橙/红的计数无关。</summary>
        [Test]
        public void Pity_LowTierDoesNotAdvanceHigherPities()
        {
            var graph = RarityGraph();
            var meta = new MetaState();
            for (int seed = 1; seed <= 5; seed++)
                OpenOn(meta, ChestTier.Rosewood, seed, graph);
            Assert.That(meta.GoldPity, Is.Zero, "金保底只数鎏金及以上");
            Assert.That(meta.OrangePity, Is.Zero, "橙保底只数朱漆及以上");
            Assert.That(meta.RedPity, Is.Zero, "红保底只数赤霄");
        }

        /// <summary>金保底:第 5 个鎏金箱强制给金。</summary>
        [Test]
        public void Pity_Gold_ForcedOnFifthGildedChest()
        {
            var graph = RarityGraph();
            int seed = SeedWithout(ChestTier.Gilded, CardRarity.Gold, graph);
            var meta = new MetaState { GoldPity = 4 };
            Assert.That(CountOf(OpenOn(meta, ChestTier.Gilded, seed, graph), CardRarity.Gold, graph),
                Is.EqualTo(1));
            Assert.That(meta.GoldPity, Is.Zero);
        }

        /// <summary>橙保底:第 10 个朱漆箱强制给橙。</summary>
        [Test]
        public void Pity_Orange_ForcedOnTenthVermilionChest()
        {
            var graph = RarityGraph();
            int seed = SeedWithout(ChestTier.Vermilion, CardRarity.Orange, graph);
            var meta = new MetaState { OrangePity = 9 };
            Assert.That(CountOf(OpenOn(meta, ChestTier.Vermilion, seed, graph), CardRarity.Orange, graph),
                Is.EqualTo(1));
            Assert.That(meta.OrangePity, Is.Zero);
        }

        /// <summary>保底替掉的是结果里最低的一张,不会把更值钱的卡顶掉。</summary>
        [Test]
        public void Pity_ReplacesLowestRarityCard()
        {
            var graph = RarityGraph();
            int seed = SeedWithout(ChestTier.Crimson, CardRarity.Red, graph);
            var before = OpenOn(new MetaState(), ChestTier.Crimson, seed, graph);

            int lowest = 0;
            foreach (var card in before.Cards)
            {
                int rarity = (int)graph.Get(card).Rarity;
                if (lowest == 0 || rarity < lowest) lowest = rarity;
            }

            var after = OpenOn(new MetaState { RedPity = 19 }, ChestTier.Crimson, seed, graph);
            int lowestBefore = 0, lowestAfter = 0;
            foreach (var card in before.Cards) if ((int)graph.Get(card).Rarity == lowest) lowestBefore++;
            foreach (var card in after.Cards) if ((int)graph.Get(card).Rarity == lowest) lowestAfter++;
            Assert.That(lowestAfter, Is.EqualTo(lowestBefore - 1), "被换掉的正是最低稀有度的那一张");
        }

        /// <summary>单箱保底降级时按权重抽,不再均匀抽:池里没有紫,紫檀匣的保底只能在
        /// 金/橙/红里取 —— 按权重(金 20‰ / 橙 5‰ / 红 0‰)红永远不出、金显著多于橙;
        /// 旧的均匀抽会让三者各占三分之一,红也会冒出来。</summary>
        /// <summary>单箱保底表的公开读法(2026-09-04 为宝箱说明弹窗加的入口)。
        /// 与 <see cref="ChestRules.PityRules"/> 一起构成「保底怎么玩」那一屏的唯一数据源 ——
        /// 弹窗不许自己抄一份档位→稀有度的对照,抄一份就会跟着这张表一起过期。</summary>
        [TestCase(ChestTier.Paper, null)]
        [TestCase(ChestTier.Bamboo, null)]
        [TestCase(ChestTier.Celadon, CardRarity.Blue)]
        [TestCase(ChestTier.Rosewood, CardRarity.Purple)]
        [TestCase(ChestTier.Gilded, CardRarity.Purple)]
        [TestCase(ChestTier.Vermilion, CardRarity.Purple)]
        [TestCase(ChestTier.Crimson, CardRarity.Purple)]
        public void GuaranteedRarityFor_MatchesSpec(ChestTier tier, CardRarity? expected)
        {
            Assert.That(ChestRules.GuaranteedRarityFor(tier), Is.EqualTo(expected));
        }

        /// <summary>公开出来的那张表要**真的**是开箱走的那张:光断言常量值,哪天开箱改读
        /// 别的表就静默分叉了(说明弹窗照旧写着已经不生效的保底)。所以这里逐档实开,
        /// 断言每一箱的结果里确实有一张 ≥ 声明的保底档。</summary>
        [Test]
        public void GuaranteedRarityFor_AgreesWithActualOpen()
        {
            var graph = RarityGraph();
            for (int tier = 1; tier <= 7; tier++)
            {
                var chestTier = (ChestTier)tier;
                var floor = ChestRules.GuaranteedRarityFor(chestTier);
                if (floor == null) continue;
                for (int seed = 1; seed <= 40; seed++)
                {
                    var rewards = OpenOn(new MetaState(), chestTier, seed, graph);
                    bool met = false;
                    foreach (var card in rewards.Cards)
                        if (graph.Get(card).Rarity >= floor.Value) { met = true; break; }
                    Assert.That(met, Is.True,
                        $"{ChestRules.TierName(chestTier)} 声明保底 {floor} 却开出一箱没有(seed {seed})");
                }
            }
        }

        [Test]
        public void Guarantee_FallbackDrawsByWeight_NotUniform()
        {
            var graph = RarityGraph();
            var pool = new[] { "r2", "r5", "r6", "r7" }; // 绿 + 金橙红,无紫
            int gold = 0, orange = 0, red = 0;
            for (int seed = 1; seed <= 200; seed++)
            {
                var rewards = OpenOn(new MetaState(), ChestTier.Rosewood, seed, graph, pool);
                gold += CountOf(rewards, CardRarity.Gold, graph);
                orange += CountOf(rewards, CardRarity.Orange, graph);
                red += CountOf(rewards, CardRarity.Red, graph);
            }
            Assert.That(red, Is.Zero, "紫檀匣红权重 0,保底降级也不该把红抽出来");
            Assert.That(gold, Is.GreaterThan(orange), "金 20‰ 该显著多于橙 5‰");
        }
    }
}
