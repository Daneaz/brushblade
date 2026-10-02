using System.Collections.Generic;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>缩放节点的求值(spec 2026-09-08 §5)。
    ///
    /// 本文件**不依赖表里真有跨树节点** —— 它自己造 PerkNodeDef 来测求值规则,
    /// 这样 Task 1 的求值逻辑与 Task 2 的入表数值可以各自变红/变绿,
    /// 出问题时一眼分得清是「公式错了」还是「数值配错了」。</summary>
    public class PerkScalingTests
    {
        private static MetaState MetaWith(params string[] perks)
        {
            var meta = new MetaState { CharacterXp = 0 };
            foreach (var p in perks) meta.UnlockedPerks.Add(p);
            return meta;
        }

        // ---- CountOwned:遍历口径 ----

        [Test]
        public void CountOwned_CountsNodesAtOrDeeperThanMinDepth()
        {
            // 金脉四层各点一段 → L3/L4 两个够深(L1/L2 三段式,按段计数)
            var meta = MetaWith("metal_1_s1", "metal_2_s1", "metal_3", "metal_4");
            Assert.That(PerkRules.CountOwned(meta, PerkTree.Wuxing, 3), Is.EqualTo(2),
                "PerDeepWuxingNode 数的是**节点**:一系点满 4 层算 2(L3、L4 各一个)");
            Assert.That(PerkRules.CountOwned(meta, PerkTree.Wuxing, 1), Is.EqualTo(4));
        }

        [Test]
        public void CountOwned_IsIndifferentToInsertionOrder()
        {
            var forward = MetaWith("metal_1_s1", "metal_2_s1", "metal_3");
            var backward = MetaWith("metal_3", "metal_2_s1", "metal_1_s1");
            Assert.That(PerkRules.CountOwned(backward, PerkTree.Wuxing, 3),
                Is.EqualTo(PerkRules.CountOwned(forward, PerkTree.Wuxing, 3)),
                "必须遍历固定顺序的 Nodes,不是 meta.UnlockedPerks");
        }

        [Test]
        public void CountOwned_IgnoresUnknownIds()
        {
            // 改表后的旧档里会留着已删除节点的 id;索引器会抛,整个存档读不出来
            var meta = MetaWith("metal_3", "a_node_that_no_longer_exists");
            Assert.That(PerkRules.CountOwned(meta, PerkTree.Wuxing, 3), Is.EqualTo(1));
        }

        // ---- ScaleCountOf:三种口径 ----

        private static PerkNodeDef Scaler(PerkEffect effect, PerkScaling scaling,
            int baseValue, int perCount) =>
            new PerkNodeDef("t_scaler", PerkTree.Cross, "tscale", null,
                depth: 1, unlockLevel: 1, inkCost: 0, effect: effect,
                value: perCount, baseValue: baseValue, scaling: scaling,
                prereq: new List<PerkRequirement>());

        [Test]
        public void PerDeepWuxingNode_CountsOnlyDepthThreeAndDeeper()
        {
            var meta = MetaWith("metal_1_s1", "metal_2_s1", "wood_1_s1", "wood_2_s1");
            var def = Scaler(PerkEffect.AttackPercent, PerkScaling.PerDeepWuxingNode, 8, 3);
            Assert.That(PerkRules.ScaleCountOf(meta, def), Is.EqualTo(0),
                "L1/L2(三段式)不计入,只数 L3/L4");
        }

        [Test]
        public void PerDeepElement_CountsElementsNotNodes()
        {
            var meta = MetaWith("metal_1_s1", "metal_2_s1", "metal_3", "metal_4");
            var def = Scaler(PerkEffect.DrawRolls, PerkScaling.PerDeepElement, 1, 1);
            Assert.That(PerkRules.ScaleCountOf(meta, def), Is.EqualTo(1),
                "一系点满 4 层只算 1 个系(与 PerDeepWuxingNode 的 2 刻意不同)");
        }

        [Test]
        public void PerDeepElement_ClampsAtTwo()
        {
            var meta = MetaWith(
                "metal_1_s1", "metal_2_s1", "metal_3",
                "wood_1_s1", "wood_2_s1", "wood_3",
                "water_1_s1", "water_2_s1", "water_3",
                "fire_1_s1", "fire_2_s1", "fire_3",
                "earth_1_s1", "earth_2_s1", "earth_3");
            var def = Scaler(PerkEffect.DrawRolls, PerkScaling.PerDeepElement, 1, 1);
            Assert.That(PerkRules.ScaleCountOf(meta, def), Is.EqualTo(2),
                "上限 2 夹在 ScaleCount 里,不是夹在调用点");
        }

        [Test]
        public void PerMechanicNode_CountsEveryMechanicNode()
        {
            var meta = MetaWith("lore_1", "lore_2", "qi_1");
            var def = Scaler(PerkEffect.CritChance, PerkScaling.PerMechanicNode, 6, 3);
            Assert.That(PerkRules.ScaleCountOf(meta, def), Is.EqualTo(3), "机制树只有两层,全算");
        }

        [Test]
        public void ScaleCountOf_IsOneForNonScalingNodes()
        {
            var meta = MetaWith("power_1");
            Assert.That(PerkRules.ScaleCountOf(meta, PerkRules.Get("power_1")), Is.EqualTo(1),
                "非缩放节点恒 1 —— 详情面板拿它算分量时不用分支");
        }

        // ---- 恒等性硬线 ----

        [Test]
        public void ExistingNodes_AreAllUnscaledWithZeroBaseValue()
        {
            foreach (var def in PerkRules.Nodes)
            {
                if (def.Tree == PerkTree.Cross) continue;
                Assert.That(def.Scaling, Is.EqualTo(PerkScaling.None), $"{def.Id} 不该是缩放节点");
                Assert.That(def.BaseValue, Is.EqualTo(0), $"{def.Id} 的 BaseValue 必须是 0");
                Assert.That(def.Prereq, Is.Null, $"{def.Id} 必须走隐式同枝前置");
            }
        }

        [Test]
        public void UnscaledBonus_IsBitIdenticalToPlainSum()
        {
            // 被动树 2026-10-02 起按枝取最高档,这里用机制树守「非缩放 = 原样求和」
            var meta = MetaWith("lore_1", "lore_2");
            Assert.That(PerkRules.Bonus(meta, PerkEffect.LibraryCapacity), Is.EqualTo(2),
                "1 + 1 —— Scaling == None 时走的还是 sum += def.Value");
        }
    }
}
