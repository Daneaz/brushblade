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

        // ---- 相生「他生我」:原料里含生本字属性的那个,才 ×3(2026-08-04 收紧) ----

        [Test]
        public void Sheng_MotherInRecipe_Triples() // 焚(火系,林+火 = 木+火):木生火
        {
            Assert.That(WuxingResolver.ShengMultiplier(
                new[] { Element.Wood, Element.Fire }, Element.Fire), Is.EqualTo(3));
        }

        [Test]
        public void Sheng_SelfGeneratesOther_DoesNotCount() // 淋(水系,氵+林 = 水+木):水生木是「我生他」
        {
            Assert.That(WuxingResolver.ShengMultiplier(
                new[] { Element.Water, Element.Wood }, Element.Water), Is.EqualTo(1));
        }

        [Test]
        public void Sheng_MotherAloneSuffices() // 蒸(火系,艹+烝 = 木):原料只有母属性也算
        {
            Assert.That(WuxingResolver.ShengMultiplier(
                new[] { Element.Wood }, Element.Fire), Is.EqualTo(3));
        }

        [Test]
        public void Sheng_DuplicatesDeduped() // 焚(木木火)去重后仍含木生火
        {
            Assert.That(WuxingResolver.ShengMultiplier(
                new[] { Element.Wood, Element.Wood, Element.Fire }, Element.Fire), Is.EqualTo(3));
        }

        [Test]
        public void Sheng_EarthMetal_OrderInRecipeIrrelevant() // 刲(金系,圭+刂 = 土+金):土生金
        {
            Assert.That(WuxingResolver.ShengMultiplier(
                new[] { Element.Metal, Element.Earth }, Element.Metal), Is.EqualTo(3));
        }

        [Test]
        public void Sheng_MultipleMothers_NoStacking() // 母属性出现多次 → 仍 ×3
        {
            Assert.That(WuxingResolver.ShengMultiplier(
                new[] { Element.Wood, Element.Wood, Element.Fire }, Element.Fire), Is.EqualTo(3));
        }

        [Test]
        public void Sheng_NoMother_1x()
        {
            // 灶(火系,火+土):火生土是「我生他」
            Assert.That(WuxingResolver.ShengMultiplier(
                new[] { Element.Fire, Element.Earth }, Element.Fire), Is.EqualTo(1));
            // 崟(土系,山+金 = 土+金):土生金是「我生他」
            Assert.That(WuxingResolver.ShengMultiplier(
                new[] { Element.Earth, Element.Metal }, Element.Earth), Is.EqualTo(1));
            Assert.That(WuxingResolver.ShengMultiplier(
                Array.Empty<Element>(), Element.Fire), Is.EqualTo(1));
        }

        [Test]
        public void Sheng_HeartNeverForms_Pair() // 心不参与生克:既不被生,也不生人
        {
            Assert.That(WuxingResolver.ShengMultiplier(
                new[] { Element.Wood, Element.Fire }, Element.Heart), Is.EqualTo(1));
            Assert.That(WuxingResolver.ShengMultiplier(
                new[] { Element.Heart }, Element.Fire), Is.EqualTo(1));
        }

        // ---- 效果结算:规格例 ----

        [Test]
        public void Resolve_Fen_Base7_VsMetal_31() // 焚 vs 金怪:floor(7×3×1.5)=31
        {
            var result = WuxingResolver.ResolveEffect(
                7, new[] { Element.Wood, Element.Fire }, Element.Fire, Element.Metal);
            Assert.That(result, Is.EqualTo(31));
        }

        [Test]
        public void Resolve_Fen_Base7_VsNeutralTarget_21() // 焚:7×3
        {
            var result = WuxingResolver.ResolveEffect(
                7, new[] { Element.Wood, Element.Fire }, Element.Fire, Element.Heart);
            Assert.That(result, Is.EqualTo(21));
        }

        [Test]
        public void Resolve_Kui_Base15_45() // 刲(金系,圭+刂 = 土+金):土生金,15×3
        {
            var result = WuxingResolver.ResolveEffect(
                15, new[] { Element.Earth, Element.Metal }, Element.Metal, Element.Heart);
            Assert.That(result, Is.EqualTo(45));
        }

        [Test]
        public void Resolve_Lin_SelfGeneratesOther_NoTriple() // 淋(水系,水+木):我生他,群疗不翻倍
        {
            var result = WuxingResolver.ResolveEffect(
                27, new[] { Element.Water, Element.Wood }, Element.Water);
            Assert.That(result, Is.EqualTo(27));
        }

        [Test]
        public void Resolve_Shield_NoTarget_TriplesOnMother() // 护盾无对抗目标:金系配方含土 → 8×3
        {
            var result = WuxingResolver.ResolveEffect(
                8, new[] { Element.Metal, Element.Earth }, Element.Metal);
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
