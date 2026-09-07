using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>用例直接取自 docs/design/wuxing-reference.md 的规格例——规格即测试。
    /// 相生 ×3 已于 2026-09-02 取消,本文件只覆盖相克。</summary>
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

        // ---- 效果结算 ----

        [Test]
        public void ResolveEffect_NoLongerAppliesSheng()
        {
            // 相生 x3 已取消(2026-09-02 用户拍板):全表只有 4 张字吃得到,
            // 是条空转规则。基础值直接写成实战值,配置值 = 实战值。
            // 木生火,但这里不该再有任何倍率
            Assert.That(WuxingResolver.ResolveEffect(100, Element.Fire, Element.Heart),
                Is.EqualTo(100));
        }

        [Test]
        public void ResolveEffect_StillAppliesKe()
        {
            // 相克保留:火克金 x1.5,火被水克 x0.5
            Assert.That(WuxingResolver.ResolveEffect(100, Element.Fire, Element.Metal),
                Is.EqualTo(150));
            Assert.That(WuxingResolver.ResolveEffect(100, Element.Fire, Element.Water),
                Is.EqualTo(50));
        }

        [Test]
        public void ResolveEffect_FloorsAfterMultiplication() // floor(7×0.5)=3
        {
            var result = WuxingResolver.ResolveEffect(7, Element.Metal, Element.Fire);
            Assert.That(result, Is.EqualTo(3));
        }

        [Test]
        public void ResolveEffect_NoTarget_IsIdentity() // 无对抗目标版本:相生取消后是恒等函数
        {
            Assert.That(WuxingResolver.ResolveEffect(27), Is.EqualTo(27));
        }

        [Test]
        public void ShengRemoval_PreservesCombatValuesOfTheFourAffectedChars()
        {
            // 取消相生前这几张字靠 x3 达到的实战值,取消后由基础值直接表达。
            // 沏 是例外:它在水系重配范围内,由 Task 10 的 DualDirectionTests 覆盖。
            //
            // 2026-09-07 字表重做 P2:这三张字的实战值随全表重新标定又变了(不再是当年
            // x3 折算的产物 —— 焚/蒸 是 spec §6 的新预算落地值,刲 更是整个换了形状:
            // 从单段 450 变成 154 × 2 段(斩杀 + 偷袭 + 分 2 段,design 表 §6)。这条测试
            // 守的不变量没变,还是同一条:CharDef.Effects 存的是玩家看到的实战值,不是又一次
            // 经过任何相生倍率折算的基础值(相生本身已删,ResolveEffect 也不再吃它,
            // 见 ResolveEffect_NoLongerAppliesSheng)——只是把「原 40/45/150 x3」这个
            // 已经不成立的历史推导,换成直接钉当前配置值。
            var graph = CharTableTests.RealGraph();
            Assert.That(graph.Get("焚").Effects.First(e => e.Kind == EffectKind.DamageAll).Value,
                Is.EqualTo(108));
            Assert.That(graph.Get("蒸").Effects.First(e => e.Kind == EffectKind.DamageSingle).Value,
                Is.EqualTo(120));
            Assert.That(graph.Get("刲").Effects.First(e => e.Kind == EffectKind.DamageSingle).Value,
                Is.EqualTo(154), "刲 改分 2 段 + 偷袭 + 斩杀后,单段值从 450 降到 154");
        }

        // ---- 克/被克 的查表入口(2026-09-03,卡组页详情印「克 X ×1.5 / 被 Y 克 ×0.5」) ----

        [Test]
        public void Victim_FollowsTheKeRing()
        {
            Assert.That(WuxingResolver.Victim(Element.Wood), Is.EqualTo(Element.Earth));
            Assert.That(WuxingResolver.Victim(Element.Earth), Is.EqualTo(Element.Water));
            Assert.That(WuxingResolver.Victim(Element.Water), Is.EqualTo(Element.Fire));
            Assert.That(WuxingResolver.Victim(Element.Fire), Is.EqualTo(Element.Metal));
            Assert.That(WuxingResolver.Victim(Element.Metal), Is.EqualTo(Element.Wood));
        }

        [Test]
        public void Counter_IsTheInverseOfVictim()
        {
            foreach (Element attacker in new[] { Element.Wood, Element.Earth, Element.Water,
                Element.Fire, Element.Metal })
            {
                var victim = WuxingResolver.Victim(attacker);
                Assert.That(WuxingResolver.Counter(victim.Value), Is.EqualTo(attacker));
            }
        }

        [Test]
        public void Heart_IsOutsideTheRingOnBothDirections()
        {
            Assert.That(WuxingResolver.Victim(Element.Heart), Is.Null);
            Assert.That(WuxingResolver.Counter(Element.Heart), Is.Null);
        }

    }
}
