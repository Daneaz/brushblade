using System.Collections.Generic;
using System.IO;
using System.Linq;
using Brushblade.Core;
using Brushblade.Data;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>开局装配(2026-08-05 拍板):初始收集 = 五系各白/绿/蓝一张;
    /// 部件的一切来源(初始池、奇遇随机部件)都从**出阵表所需部件**里取,不再是固定金木水火土。</summary>
    public class StartingSetupTests
    {
        private static RecipeGraph RealGraph() => CharTableTests.RealGraph();

        // ---- 初始收集:五系 × 白绿蓝 ----

        [Test]
        public void StartingCollection_IsFifteenCards()
        {
            Assert.That(MetaRules.StartingCollection.Count, Is.EqualTo(15));
            Assert.That(MetaRules.StartingCollection.Distinct().Count(), Is.EqualTo(15), "不得重复");
        }

        [Test]
        public void StartingCollection_EachElementHasWhiteGreenBlue()
        {
            var graph = RealGraph();
            foreach (var element in new[] { Element.Metal, Element.Wood, Element.Water,
                                            Element.Fire, Element.Earth })
            {
                var ofElement = MetaRules.StartingCollection
                    .Select(graph.Get)
                    .Where(d => d.Element == element)
                    .ToList();
                Assert.That(ofElement.Count, Is.EqualTo(3), $"{element} 应恰好 3 张");
                foreach (var rarity in new[] { CardRarity.White, CardRarity.Green, CardRarity.Blue })
                    Assert.That(ofElement.Count(d => d.Rarity == rarity), Is.EqualTo(1),
                        $"{element} 的 {rarity} 档应恰好 1 张");
            }
        }

        [Test]
        public void StartingCollection_AllComposable() // 全部可拆可合,否则拆合玩法断链
        {
            var graph = RealGraph();
            foreach (var id in MetaRules.StartingCollection)
                Assert.That(graph.Get(id).Recipe, Is.Not.Empty, $"{id} 无配方,拆了合不回来");
        }

        // ---- 默认出阵 ----

        [Test]
        public void StartingDeck_FitsStartingLibraryAndIsOwned()
        {
            Assert.That(MetaRules.StartingDeck.Count,
                Is.GreaterThanOrEqualTo(MetaRules.DeckMinimum));
            // 起手字库只装前 StartingLibrarySize 张:默认出阵不超过它,保证默认出阵全部上场
            Assert.That(MetaRules.StartingDeck.Count,
                Is.LessThanOrEqualTo(MetaRules.StartingLibrarySize));
            foreach (var id in MetaRules.StartingDeck)
                Assert.That(MetaRules.StartingCollection, Contains.Item(id));
        }

        [Test]
        public void StartingDeck_ContainsTutorialChar() // 教程要拆它,必须在起手字库里
        {
            Assert.That(MetaRules.StartingDeck, Contains.Item(Tutorial.DemoChar));
        }

        [Test]
        public void StartingDeck_CoversEveryElement()
        {
            var graph = RealGraph();
            var elements = MetaRules.StartingDeck.Select(id => graph.Get(id).Element).ToList();
            foreach (var element in new[] { Element.Metal, Element.Wood, Element.Water,
                                            Element.Fire, Element.Earth })
                Assert.That(elements, Contains.Item(element), $"默认出阵缺 {element}");
        }

        // ---- 部件来源:从出阵表派生 ----

        [Test]
        public void DeckComponents_AreTheLeavesOfDeckRecipes()
        {
            var graph = RealGraph();
            // 2026-08-14:城 随第二批裁定移出字表,换同为土系蓝档护盾的 垒(厽+土)。
            var deck = new[] { "剑", "垒" }; // 佥+刂 / 厽+土
            var components = MetaRules.DeckComponents(deck, graph);
            Assert.That(components, Is.EquivalentTo(new[] { "佥", "刂", "厽", "土" }));
        }

        [Test]
        public void DeckComponents_Deduplicates()
        {
            var graph = RealGraph();
            var deck = new[] { "剁", "剑" }; // 朵+刂 / 佥+刂 —— 刂 共用(割 于 2026-08-14 移出)
            Assert.That(MetaRules.DeckComponents(deck, graph).Count(c => c == "刂"), Is.EqualTo(1));
        }

        [Test]
        public void DeckComponents_SkipsNonLeafIngredients() // 只要部件,低阶字不算
        {
            var graph = RealGraph();
            var components = MetaRules.DeckComponents(new[] { "焱" }, graph); // 火+炎,炎是字
            Assert.That(components, Contains.Item("火"));
            Assert.That(components, Does.Not.Contain("炎"));
        }

        [Test]
        public void DeckComponents_EmptyDeck_IsEmpty()
        {
            Assert.That(MetaRules.DeckComponents(new string[0], RealGraph()), Is.Empty);
        }

        [Test]
        public void DeckComponents_StartingDeck_CoversEveryStartingDeckRecipe()
        {
            var graph = RealGraph();
            var components = MetaRules.DeckComponents(MetaRules.StartingDeck, graph);
            // 默认出阵的每个字都能用池里的部件拼出来 —— 否则随机到的部件是死牌
            foreach (var id in MetaRules.StartingDeck)
                foreach (var part in graph.Get(id).Recipe)
                    Assert.That(components, Contains.Item(part), $"{id} 的原料 {part} 不在派生部件池里");
        }

        // ---- 奇遇随机部件只从出阵表派生的部件里取 ----

        [Test]
        public void EventRandomComponents_ComeFromDeckComponents()
        {
            var graph = RealGraph();
            var deck = new List<string> { "剑", "城" };
            var allowed = MetaRules.DeckComponents(deck, graph).ToList();

            // 多个种子都必须落在派生集合内
            for (int seed = 1; seed <= 30; seed++)
            {
                var run = NewRunWithDeck(graph, deck, seed);
                var pool = run.Battle.Pool;
                Assert.That(pool, Has.All.Matches<string>(c => allowed.Contains(c)),
                    $"seed {seed}: 初始部件池 {string.Join(",", pool)} 越出 {string.Join(",", allowed)}");
            }
        }

        // ---- 实船:新初始字必须打得过首塔首层(否则新手一开局就卡死) ----

        /// <summary>用真实 chars.json + enemies.json 跑首层。ConfigLoaderTests 里那条同名守卫
        /// 引了 UnityEngine.Application 被工装排除,这里用不依赖引擎的路径再守一遍。</summary>
        [Test]
        public void ShippedConfig_StartingDeck_ClearsFirstFloor()
        {
            var graph = RealGraph();
            var campaign = ConfigLoader.LoadCampaign(
                File.ReadAllText(Path.Combine(RepoRoot(),
                    "Brushblade/Assets/StreamingAssets/config/enemies.json")), graph);

            var segment = EndlessGenerator.BuildFirstTowerSegment(campaign.Endless, seed: 7);
            var floorOne = segment.Encounters[0];
            var deck = MetaRules.StartingDeck;
            var demo = Tutorial.DemoChar;

            var battle = new BattleEngine(graph,
                new BattleConfig { DropTable = campaign.DropTable, UnlockedChars = deck },
                new[] { demo },
                MetaRules.RollStartingPool(deck, graph, new GameRandom(7)),
                floorOne, seed: 7);

            Assert.That(battle.Dismantle(demo), Is.EqualTo(BattleError.None), "拆演示字");
            Assert.That(battle.Compose(demo), Is.EqualTo(BattleError.None), "合回演示字");
            Assert.That(battle.Cast(demo), Is.EqualTo(BattleError.None), "打出演示字");
            if (battle.Phase == BattlePhase.PlayerTurn)
                battle.EndTurn();
            Assert.That(battle.Phase, Is.EqualTo(BattlePhase.Won), "首层没能在一回合内清掉");
        }

        /// <summary>层段的 rewardPool 是死配置,2026-08-05 从 enemies.json 清掉:
        /// 战利品只出自出阵表(GameRoot 无条件覆盖为 meta.Deck)。这里守两件事——
        /// 配置里没被填回去,且缺失项解析成**空列表而不是 null**(RollRewardOptions 直接 foreach 它)。</summary>
        [Test]
        public void ShippedConfig_BandRewardPools_AreEmptyNotNull()
        {
            var campaign = ShippedCampaign(out _);
            foreach (var band in campaign.Endless.Bands)
            {
                Assert.That(band.RewardPool, Is.Not.Null, $"{band.Name}: 解析成 null 会让抽奖 NRE");
                Assert.That(band.RewardPool, Is.Empty, $"{band.Name}: 死配置被填回来了");
            }
        }

        [Test]
        public void ShippedConfig_SegmentFromEmptyBandPool_StillBuilds()
        {
            var campaign = ShippedCampaign(out _);
            var segment = EndlessGenerator.BuildSegment(campaign.Endless, fromDepth: 1, seed: 7);
            Assert.That(segment.RewardPool, Is.Not.Null); // GameRoot 覆盖前必须已是空列表
            Assert.That(segment.RewardPool, Is.Empty);
        }

        private static CampaignConfig ShippedCampaign(out RecipeGraph graph)
        {
            graph = RealGraph();
            return ConfigLoader.LoadCampaign(
                File.ReadAllText(Path.Combine(RepoRoot(),
                    "Brushblade/Assets/StreamingAssets/config/enemies.json")), graph);
        }

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Brushblade")))
                dir = dir.Parent;
            Assert.That(dir, Is.Not.Null, "找不到仓库根");
            return dir.FullName;
        }

        /// <summary>建一个出阵表为 deck 的 run;初始部件池按新规则从出阵表部件里随机。</summary>
        private static RunEngine NewRunWithDeck(RecipeGraph graph, IReadOnlyList<string> deck, int seed)
        {
            var runConfig = new RunConfig
            {
                Encounters = new[] { new[] { new EnemyDef("靶", Element.Heart, 10, 0) } },
                RewardPool = deck,
            };
            var battleConfig = new BattleConfig { PlayerMaxHp = 50, UnlockedChars = deck };
            return new RunEngine(graph, runConfig, battleConfig,
                startingLibrary: deck,
                startingPool: MetaRules.RollStartingPool(deck, graph, new GameRandom(seed)),
                seed: seed);
        }
    }
}
