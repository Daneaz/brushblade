using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>跨树节点(spec 2026-09-08 §3):相济、博采两个缩放器(融会 2026-10-18 删除),
    /// 加上占了融会位置的斩获终极技两级(spec 2026-10-18「斩获终极技」§1)。
    ///
    /// 与 PerkScalingTests 的分工:那边测**求值公式**(自造 def),这边测**表里配的数值**
    /// 与**前置谓词**。出问题时一眼分得清是公式错了还是数配错了。</summary>
    public class CrossNodeTests
    {
        /// <summary>等级拉满、墨锭管够 —— 本文件测的是**前置与缩放**,
        /// 等级/墨锭那两道闸由 PerkTests 守着,这里不该被它们干扰。
        /// CharacterXp = 100000 是既有测试统一的「全等级开放」写法(PerkTests 里到处是它),
        /// 照抄那一份,别自己算等级换算。</summary>
        private static MetaState MetaWith(params string[] perks)
        {
            var meta = new MetaState { CharacterXp = 100000, Ink = 100000 };
            foreach (var p in perks) meta.UnlockedPerks.Add(p);
            return meta;
        }

        // ---- 表的形状 ----

        [Test]
        public void ScalingCrossNodes_AreInTheTable()
        {
            foreach (var id in new[] { "cross_vigor", "cross_draw" })
            {
                var def = PerkRules.Get(id);
                Assert.That(def.Tree, Is.EqualTo(PerkTree.Cross), $"{id} 该属于 Cross");
                Assert.That(def.Depth, Is.EqualTo(1), $"{id} 的 Depth 恒为 1");
                Assert.That(def.Element.HasValue, Is.False, $"{id} 不绑元素");
                Assert.That(def.Prereq, Is.Not.Null, $"{id} 必须带显式前置");
                Assert.That(def.Prereq.Count, Is.EqualTo(2), $"{id} 要两侧各一个前置");
                Assert.That(def.Scaling, Is.Not.EqualTo(PerkScaling.None), $"{id} 是缩放节点");
            }
        }

        [Test]
        public void CrossNodes_CarryTheSpecValues()
        {
            var vigor = PerkRules.Get("cross_vigor");
            Assert.That(vigor.Effect, Is.EqualTo(PerkEffect.AttackPercent));
            Assert.That(vigor.BaseValue, Is.EqualTo(8));
            Assert.That(vigor.Value, Is.EqualTo(3));
            Assert.That(vigor.Scaling, Is.EqualTo(PerkScaling.PerDeepWuxingNode));
            Assert.That(vigor.UnlockLevel, Is.EqualTo(16));
            Assert.That(vigor.InkCost, Is.EqualTo(1200));

            var draw = PerkRules.Get("cross_draw");
            Assert.That(draw.Effect, Is.EqualTo(PerkEffect.DrawRolls));
            Assert.That(draw.BaseValue, Is.EqualTo(1));
            Assert.That(draw.Value, Is.EqualTo(1));
            Assert.That(draw.Scaling, Is.EqualTo(PerkScaling.PerDeepElement));
            Assert.That(draw.UnlockLevel, Is.EqualTo(10));
            Assert.That(draw.InkCost, Is.EqualTo(900));
        }

        // ---- 前置:两侧都要 ----

        [Test]
        public void Xvigor_NeedsBothSides()
        {
            var def = PerkRules.Get("cross_vigor");

            var onlyWuxing = MetaWith("metal_1_s1", "metal_2_s1", "metal_3");
            Assert.That(PerkRules.PrereqMet(onlyWuxing, def), Is.False, "缺被动侧");

            var onlyPassive = MetaWith("power_1_s1", "power_1_s2");
            Assert.That(PerkRules.PrereqMet(onlyPassive, def), Is.False, "缺五行侧");

            var both = MetaWith("metal_1_s1", "metal_2_s1", "metal_3", "power_1_s1", "power_1_s2");
            Assert.That(PerkRules.PrereqMet(both, def), Is.True, "两侧齐了");
        }

        /// <summary>被动侧 = 被动树累计点亮 2 段(2026-10-20 被动枝合为三段节点,原「任一枝到第 2 层」)。</summary>
        [Test]
        public void Xvigor_PassiveSideNeedsTwoStages()
        {
            var def = PerkRules.Get("cross_vigor");
            Assert.That(PerkRules.PrereqMet(MetaWith("metal_1_s1", "metal_2_s1", "metal_3", "power_1_s1"), def),
                Is.False, "只点 1 段");
        }

        [Test]
        public void Xvigor_PrereqIsAPredicateNotASpecificBranch()
        {
            var def = PerkRules.Get("cross_vigor");
            var viaMetal = MetaWith("metal_1_s1", "metal_2_s1", "metal_3", "power_1_s1", "power_1_s2");
            var viaFire = MetaWith("fire_1_s1", "fire_2_s1", "fire_3", "guard_1_s1", "guard_1_s2");
            Assert.That(PerkRules.PrereqMet(viaMetal, def), Is.True);
            Assert.That(PerkRules.PrereqMet(viaFire, def), Is.True,
                "「五行任一 L3」不是「金脉 L3」;被动侧同理");
        }

        [Test]
        public void Xdraw_NeedsMechanicL1AndWuxingL2()
        {
            var def = PerkRules.Get("cross_draw");
            Assert.That(PerkRules.PrereqMet(MetaWith("qi_1", "water_1_s1"), def), Is.False,
                "五行只到 L1 不够");
            Assert.That(PerkRules.PrereqMet(MetaWith("water_1_s1", "water_2_s1"), def), Is.False,
                "缺机制侧");
            Assert.That(PerkRules.PrereqMet(MetaWith("qi_1", "water_1_s1", "water_2_s1"), def), Is.True);
        }

        [Test]
        public void PrereqMet_IsIndifferentToUnlockOrder()
        {
            var def = PerkRules.Get("cross_vigor");
            var forward = MetaWith("metal_1_s1", "metal_2_s1", "metal_3", "power_1_s1", "power_1_s2");
            var backward = MetaWith("power_1_s2", "power_1_s1", "metal_3", "metal_2_s1", "metal_1_s1");
            Assert.That(PerkRules.PrereqMet(backward, def),
                Is.EqualTo(PerkRules.PrereqMet(forward, def)));
        }

        // ---- 保底值真的在起作用 ----

        [Test]
        public void Xdraw_PaysTheBaseValueWhenNoElementIsDeep()
        {
            // 前置只要五行 L2,缩放却数 L3 —— 这是唯一真会落在基础档的节点(spec §3.4)
            var meta = MetaWith("qi_1", "water_1_s1", "water_2_s1", "cross_draw");
            Assert.That(PerkRules.ScaleCountOf(meta, PerkRules.Get("cross_draw")), Is.EqualTo(0));
            Assert.That(PerkRules.Bonus(meta, PerkEffect.DrawRolls), Is.EqualTo(1),
                "缩放计数为 0 时给保底 1,不是 0");
        }

        [Test]
        public void BaseValue_IsAddedOnceNotPerCount()
        {
            var meta = MetaWith(
                "metal_1_s1", "metal_2_s1", "metal_3", "metal_4",   // 深层 ×2
                "power_1_s1", "power_1_s2",
                "cross_vigor");
            Assert.That(PerkRules.ScaleCountOf(meta, PerkRules.Get("cross_vigor")), Is.EqualTo(2));
            // 力 L1+L2 不叠加取最高档 = 10(2026-10-02);相济 = 8 + 3×2 = 14
            Assert.That(PerkRules.Bonus(meta, PerkEffect.AttackPercent), Is.EqualTo(24),
                "8 + 3×2 = 14,不是 (8+3)×2 = 22");
        }

        // ---- 与既有守卫的衔接 ----

        [Test]
        public void CrossNodes_UnlockByLevelTwentyFour()
        {
            foreach (var id in new[] { "cross_vigor", "cross_draw", "ult_killdraw_s1", "ult_killdraw_s2" })
                Assert.That(PerkRules.Get(id).UnlockLevel, Is.LessThanOrEqualTo(24),
                    $"{id} 的门槛不该超过全开等级");
        }

        /// <summary>烟雾测试:三个跨树节点在两侧前置都齐了时,真的点得亮。
        ///
        /// ⚠ 这条守不住「Prereq 被误改回 null」的回归 —— 它只断言 True 分支,而那个回归下
        /// 这里依然是 True。跨树节点 Depth 恒为 1、Stage 恒为 1,Prereq 一旦变 null,
        /// <c>PrereqMet</c> 的隐式分支会因 <c>Depth &lt;= 1</c> 直接返回 true —— 后果是前置被整个绕过、
        /// 节点随时可点(过度放行),而不是永远点不亮。真正兜住这个回归的是断了 False 分支的
        /// <see cref="Xvigor_NeedsBothSides"/>、<see cref="Xdraw_NeedsMechanicL1AndWuxingL2"/>、
        /// <see cref="KillDrawUlt_NeedsEveryMechanicNode"/> 那几条 —— 改表时别把它们删了只留这条当哨兵。</summary>
        [Test]
        public void CrossNodes_AreReachableAtAll()
        {
            var meta = MetaWith(
                "metal_1_s1", "metal_2_s1", "metal_3",
                "power_1_s1", "power_1_s2",
                "lore_1",
                "water_1_s1", "water_2_s1");
            Assert.That(PerkRules.CanUnlock(meta, "cross_vigor"), Is.True);
            Assert.That(PerkRules.CanUnlock(meta, "cross_draw"), Is.True);
        }

        // ---- 斩获终极技(spec 2026-10-18 终极技 §1) ----

        private static string[] AllMechanicIds()
        {
            var ids = new System.Collections.Generic.List<string>();
            foreach (var def in PerkRules.Nodes) if (def.Tree == PerkTree.Mechanic) ids.Add(def.Id);
            return ids.ToArray();
        }

        [Test]
        public void Cross_EdgeIsGone()
        {
            foreach (var def in PerkRules.Nodes)
            {
                Assert.That(def.Id, Is.Not.EqualTo("cross_edge"), "融会已删除");
                Assert.That(def.Id, Is.Not.EqualTo("wide_3"), "斩获已移出广纳枝");
            }
        }

        [Test]
        public void KillDrawUlt_CarriesTheSpecValues()
        {
            var u1 = PerkRules.Get("ult_killdraw_s1");
            var u2 = PerkRules.Get("ult_killdraw_s2");
            foreach (var u in new[] { u1, u2 })
            {
                Assert.That(u.Tree, Is.EqualTo(PerkTree.Cross), $"{u.Id}:节点样式走跨树那套");
                Assert.That(u.Effect, Is.EqualTo(PerkEffect.KillDraw));
                Assert.That(u.Scaling, Is.EqualTo(PerkScaling.None));
                Assert.That(u.Element.HasValue, Is.False);
            }
            Assert.That((u1.Value, PerkRules.KillDrawChancePercent(u1)), Is.EqualTo((1, 50)));
            Assert.That((u2.Value, PerkRules.KillDrawChancePercent(u2)), Is.EqualTo((2, 70)));
            // 一级 Lv18 / 900(用户 10-18 拍板改门槛,墨价仍取融会原值);二级在其上按被动树同比(L1→L2 门槛 +6、墨价 ×2)
            Assert.That((u1.UnlockLevel, u1.InkCost), Is.EqualTo((18, 900)));
            Assert.That((u2.UnlockLevel, u2.InkCost), Is.EqualTo((24, 1800)));
        }

        [Test]
        public void KillDrawUlt_NeedsEveryMechanicNode()
        {
            var def = PerkRules.Get("ult_killdraw_s1");
            var all = AllMechanicIds();
            Assert.That(all.Length, Is.EqualTo(8), "机制树 4 枝 × 2 层");
            Assert.That(PerkRules.PrereqMet(MetaWith(all), def), Is.True, "机制树全部点亮即可");
            foreach (var missing in all)
            {
                var ids = System.Array.FindAll(all, id => id != missing);
                Assert.That(PerkRules.PrereqMet(MetaWith(ids), def), Is.False, $"缺 {missing} 就该锁");
            }
        }

        [Test]
        public void KillDrawUltTwo_NeedsTierOne()
        {
            var def = PerkRules.Get("ult_killdraw_s2");
            var all = AllMechanicIds();
            Assert.That(PerkRules.PrereqMet(MetaWith(all), def), Is.False, "机制树点满但没点一级");
            var withOne = new System.Collections.Generic.List<string>(all) { "ult_killdraw_s1" };
            Assert.That(PerkRules.PrereqMet(MetaWith(withOne.ToArray()), def), Is.True);
            Assert.That(PerkRules.CanUnlock(MetaWith(withOne.ToArray()), "ult_killdraw_s2"), Is.True);
        }

        [Test]
        public void KillDrawTier_IsTheDeepestOwnedNotTheSum()
        {
            Assert.That(PerkRules.KillDrawTier(MetaWith()), Is.Null);
            Assert.That(PerkRules.KillDrawTier(MetaWith("ult_killdraw_s1")).Id, Is.EqualTo("ult_killdraw_s1"));
            Assert.That(PerkRules.KillDrawTier(MetaWith("ult_killdraw_s2", "ult_killdraw_s1")).Id, Is.EqualTo("ult_killdraw_s2"));
        }
    }
}
