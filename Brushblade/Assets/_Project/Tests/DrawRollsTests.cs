using System.Collections.Generic;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>「抽 N 次取最高档」(spec §3.1)。
    ///
    /// ⚠ 断言只写在**确定性**上(rolls=1 逐位等价、取的确实是最高档、随机流消耗量),
    /// 不断概率 —— spec 里那张分布表是给人校准用的,写成断言必然 flaky。</summary>
    public class DrawRollsTests
    {
        private static RecipeGraph Graph() => CharTableTests.RealGraph();

        /// <summary>rolls = 1 时与 DrawWeighted **逐位等价**:同种子、同候选、同结果,
        /// 且消耗的随机数一样多。这是「未点任何节点时逐字节恒等」的凭据。</summary>
        [Test]
        public void OneRoll_IsBitwiseIdenticalToDrawWeighted()
        {
            var graph = Graph();
            var candidates = new List<string>(MetaRules.StartingCollection);
            for (int seed = 0; seed < 20; seed++)
            {
                var a = new GameRandom(seed);
                var b = new GameRandom(seed);
                Assert.That(MetaRules.DrawBest(candidates, graph, a, 1),
                    Is.EqualTo(MetaRules.DrawWeighted(candidates, graph, b)),
                    $"seed {seed}");
                Assert.That(a.Next(1000), Is.EqualTo(b.Next(1000)),
                    $"seed {seed}:随机流消耗量必须一致");
            }
        }

        /// <summary>rolls ≥ 2 时,结果的稀有度不低于同一段随机流下 rolls = 1 的结果。
        /// 「取最高」的定义就是这个 —— 抽多次只会更好或持平。</summary>
        [Test]
        public void MoreRolls_NeverYieldALowerRarity()
        {
            var graph = Graph();
            var candidates = new List<string>(MetaRules.StartingCollection);
            for (int seed = 0; seed < 40; seed++)
            {
                var one = MetaRules.DrawBest(candidates, graph, new GameRandom(seed), 1);
                var two = MetaRules.DrawBest(candidates, graph, new GameRandom(seed), 2);
                Assert.That((int)graph.Get(two).Rarity,
                    Is.GreaterThanOrEqualTo((int)graph.Get(one).Rarity), $"seed {seed}");
            }
        }

        [Test]
        public void EmptyCandidates_ReturnNullAtAnyRollCount()
        {
            var graph = Graph();
            var empty = new List<string>();
            Assert.That(MetaRules.DrawBest(empty, graph, new GameRandom(1), 1), Is.Null);
            Assert.That(MetaRules.DrawBest(empty, graph, new GameRandom(1), 3), Is.Null);
        }

        /// <summary>rolls ≤ 0 是调用方失误 —— 给最低档(1 次),不返回 null。</summary>
        [Test]
        public void NonPositiveRolls_FallBackToASingleDraw()
        {
            var graph = Graph();
            var candidates = new List<string>(MetaRules.StartingCollection);
            Assert.That(MetaRules.DrawBest(candidates, graph, new GameRandom(7), 0), Is.Not.Null);
        }

        // ---- 接进起手 ----

        /// <summary>没点任何节点时,起手序列与引入抽取次数之前**逐字节相同**。</summary>
        [Test]
        public void StartingLibrary_IsUnchangedOnAnEmptySave()
        {
            var graph = Graph();
            var meta = new MetaState();
            meta.OwnedCards.AddRange(MetaRules.StartingCollection);
            // 同一种子下,DrawBest(rolls=1) 与旧的 DrawWeighted 必须产出同一串字。
            // 逐张比对而不是只比数量 —— 只比 Count 的话,换了实现却选错字也不会红。
            var viaBest = MetaRules.StartingLibrary(meta, graph, new GameRandom(12345));
            var viaWeighted = new List<string>();
            var rng = new GameRandom(12345);
            var candidates = MetaRules.PlayableCards(meta, graph);
            foreach (var element in new[] { Element.Metal, Element.Wood, Element.Water,
                                            Element.Fire, Element.Earth })
            {
                var ofElement = new List<string>();
                foreach (var id in candidates)
                    if (graph.Get(id).Element == element) ofElement.Add(id);
                var pick = MetaRules.DrawWeighted(ofElement, graph, rng);
                if (pick != null) viaWeighted.Add(pick);
            }
            Assert.That(viaBest.Count, Is.EqualTo(MetaRules.StartingLibrarySize),
                "起手张数不该因为引入抽取次数而变");
            for (int i = 0; i < 5; i++)
                Assert.That(viaBest[i], Is.EqualTo(viaWeighted[i]),
                    $"下标 {i}:空存档下必须与旧实现选出同一张字");
        }

        /// <summary>金脉 L1 只抬高**金**那一格的抽取次数,其余四系仍是 1 次。
        ///
        /// ⚠ 这里刻意**不**断言「其余四格选出同一张字」——那在既有架构下不可能成立:
        /// 五格共用同一根顺序推进的 GameRandom,金格多摇一次会把后面几格的流位置整体挪位。
        /// 但那几格仍然是「该系按 RarityWeights 抽一张」,**分布一字未变**,变的只是同一分布下
        /// 取到的样本 —— 那是共享随机流的正常性质,不是串系。真正守「没点技能就什么都没变」的
        /// 是 <see cref="StartingLibrary_IsUnchangedOnAnEmptySave"/>。</summary>
        [Test]
        public void MetalTierOne_GivesOnlyTheMetalSlotAnExtraRoll()
        {
            var meta = new MetaState();
            meta.UnlockedPerks.Add("metal_1");
            int global = PerkRules.Bonus(meta, PerkEffect.DrawRolls);
            Assert.That(global, Is.EqualTo(0), "金脉 L1 不是全局加成");

            Assert.That(1 + global + PerkRules.ElementBonus(
                meta, PerkEffect.ElementDrawRolls, Element.Metal), Is.EqualTo(2), "金");
            foreach (var element in new[] { Element.Wood, Element.Water,
                                            Element.Fire, Element.Earth })
                Assert.That(1 + global + PerkRules.ElementBonus(
                    meta, PerkEffect.ElementDrawRolls, element), Is.EqualTo(1),
                    $"{element} 不该被金脉影响");
        }

        [Test]
        public void Insight_AffectsEverySlot()
        {
            var meta = new MetaState();
            meta.UnlockedPerks.Add("insight_1");
            Assert.That(PerkRules.Bonus(meta, PerkEffect.DrawRolls), Is.EqualTo(1));
        }

        /// <summary>专精 + 全局叠加 = 抽 3 次(1 基础 + 1 五行 L1 + 1 慧眼)。</summary>
        [Test]
        public void ElementAndGlobalRolls_Stack()
        {
            var meta = new MetaState();
            meta.UnlockedPerks.Add("metal_1");
            meta.UnlockedPerks.Add("insight_1");
            int rolls = 1 + PerkRules.Bonus(meta, PerkEffect.DrawRolls)
                + PerkRules.ElementBonus(meta, PerkEffect.ElementDrawRolls, Element.Metal);
            Assert.That(rolls, Is.EqualTo(3));
        }
    }
}
