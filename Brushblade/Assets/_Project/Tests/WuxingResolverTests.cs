using System;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>用例直接取自 docs/design/wuxing-reference.md 的规格例——规格即测试。</summary>
    public class WuxingResolverTests
    {
        // ---- 相克:木克土,土克水,水克火,火克金,金克木 ----

        [TestCase(Element.Wood, Element.Earth)]
        [TestCase(Element.Earth, Element.Water)]
        [TestCase(Element.Water, Element.Fire)]
        [TestCase(Element.Fire, Element.Metal)]
        [TestCase(Element.Metal, Element.Wood)]
        public void Ke_AttackerCountersDefender_1_5x(Element attacker, Element defender)
        {
            Assert.That(WuxingResolver.KeMultiplier(attacker, defender), Is.EqualTo(1.5f));
        }

        [TestCase(Element.Earth, Element.Wood)]
        [TestCase(Element.Metal, Element.Fire)]
        public void Ke_AttackerCounteredByDefender_0_5x(Element attacker, Element defender)
        {
            Assert.That(WuxingResolver.KeMultiplier(attacker, defender), Is.EqualTo(0.5f));
        }

        [TestCase(Element.Fire, Element.Fire)]   // 同属性
        [TestCase(Element.Wood, Element.Fire)]   // 相生非相克
        [TestCase(Element.Heart, Element.Metal)] // 心中立(攻)
        [TestCase(Element.Metal, Element.Heart)] // 心中立(守)
        public void Ke_Unrelated_1_0x(Element attacker, Element defender)
        {
            Assert.That(WuxingResolver.KeMultiplier(attacker, defender), Is.EqualTo(1.0f));
        }

        // ---- 相生:木生火,火生土,土生金,金生水,水生木 ----

        [Test]
        public void Sheng_WaterWood_Triples() // 淋(氵+林)
        {
            Assert.That(WuxingResolver.ShengMultiplier(new[] { Element.Water, Element.Wood }), Is.EqualTo(3));
        }

        [Test]
        public void Sheng_DuplicatesDeduped() // 焚(木木火)去重后仍含木生火
        {
            Assert.That(WuxingResolver.ShengMultiplier(new[] { Element.Wood, Element.Wood, Element.Fire }), Is.EqualTo(3));
        }

        [Test]
        public void Sheng_MetalEarth_OrderInRecipeIrrelevant() // 壁(辟金+土):土生金
        {
            Assert.That(WuxingResolver.ShengMultiplier(new[] { Element.Metal, Element.Earth }), Is.EqualTo(3));
        }

        [Test]
        public void Sheng_MultiplePairs_NoStacking() // 木生火 + 火生土 → 仍 ×3
        {
            Assert.That(WuxingResolver.ShengMultiplier(new[] { Element.Wood, Element.Fire, Element.Earth }), Is.EqualTo(3));
        }

        [Test]
        public void Sheng_NoPair_1x()
        {
            Assert.That(WuxingResolver.ShengMultiplier(new[] { Element.Fire }), Is.EqualTo(1));
            Assert.That(WuxingResolver.ShengMultiplier(new[] { Element.Wood, Element.Metal }), Is.EqualTo(1));
            Assert.That(WuxingResolver.ShengMultiplier(Array.Empty<Element>()), Is.EqualTo(1));
        }

        [Test]
        public void Sheng_HeartNeverForms_Pair() // 心不参与生克
        {
            Assert.That(WuxingResolver.ShengMultiplier(new[] { Element.Heart, Element.Fire }), Is.EqualTo(1));
        }

        // ---- 效果结算:规格例 ----

        [Test]
        public void Resolve_Fen_Base18_VsMetal_81() // 焚 vs 金怪:floor(18×3×1.5)=81
        {
            var result = WuxingResolver.ResolveEffect(
                18, new[] { Element.Wood, Element.Fire }, Element.Fire, Element.Metal);
            Assert.That(result, Is.EqualTo(81));
        }

        [Test]
        public void Resolve_Fen_Base18_VsNeutralTarget_54() // 焚:18×3
        {
            var result = WuxingResolver.ResolveEffect(
                18, new[] { Element.Wood, Element.Fire }, Element.Fire, Element.Heart);
            Assert.That(result, Is.EqualTo(54));
        }

        [Test]
        public void Resolve_Lin_Base8_24() // 淋:8×3
        {
            var result = WuxingResolver.ResolveEffect(
                8, new[] { Element.Water, Element.Wood }, Element.Water, Element.Heart);
            Assert.That(result, Is.EqualTo(24));
        }

        [Test]
        public void Resolve_Bi_Shield8_NoTarget_24() // 壁:护盾 8×3(无对抗目标)
        {
            var result = WuxingResolver.ResolveEffect(8, new[] { Element.Metal, Element.Earth });
            Assert.That(result, Is.EqualTo(24));
        }

        [Test]
        public void Resolve_FloorsAfterMultiplication() // floor(7×0.5)=3
        {
            var result = WuxingResolver.ResolveEffect(
                7, Array.Empty<Element>(), Element.Metal, Element.Fire);
            Assert.That(result, Is.EqualTo(3));
        }
    }
}
