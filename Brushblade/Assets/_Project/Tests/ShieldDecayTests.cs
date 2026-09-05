using System;
using NUnit.Framework;
using Brushblade.Core;

namespace Brushblade.Core.Tests
{
    /// <summary>护盾在每场战斗结束时衰减 50%(2026-09-05,平衡重做 P0 任务 3)。
    ///
    /// 为什么要这条:护盾此前 `_shieldNormal += shield` 只加不减、整场爬塔通吃,
    /// 是**第二条血条**而不是临时保护。玩家只要有一个回合「每回合获得 > 每回合承伤」,
    /// 那份盾就永久留在身上 —— 战斗不会输也打不死怪,即「中层刮痧」。
    /// 压单张数值只能把转正点往后挪几层(已砍过两次),挪不掉这个性质。
    ///
    /// ⚠ 衰减挂在**战斗结束**而不是回合末(2026-09-05 用户裁定)。这解决的是**跨场累积**;
    /// 单场内的净增长它碰不到,那是刻意保留的残留(设计稿 §1.4)。
    ///
    /// 攒层余数(ShieldAccum / HealAccum)**不减半** —— 它们不是护盾,是厚/泉的进度条。</summary>
    public sealed class ShieldDecayTests
    {
        private static RunSnapshot WinAndCapture(int normal, int persist,
            int shieldAccum = 0, int healAccum = 0)
        {
            var run = RebalanceFixture.Run(normalShield: normal, persistShield: persist);
            if (shieldAccum > 0) run.Battle.GainHeftForTest(shieldAccum);
            if (healAccum > 0) run.Battle.GainWellspringForTest(healAccum);
            run.Battle.Cast("甲", 0);
            Assert.That(run.Battle.Phase, Is.EqualTo(BattlePhase.Won), "夹具前提:必须一发秒杀");
            run.AdvanceAfterBattle();
            return run.Capture();
        }

        [Test]
        public void CarriedShield_HalvesAfterBattle()
        {
            var snap = WinAndCapture(normal: 301, persist: 100);
            Assert.That(snap.CarriedNormalShield, Is.EqualTo(150), "301 / 2 = 150(整数除,向下取整)");
            Assert.That(snap.CarriedPersistShield, Is.EqualTo(50), "两个桶各自减半");
        }

        [Test]
        public void ResourceAccumulators_DoNotHalve()
        {
            var snap = WinAndCapture(normal: 0, persist: 0, shieldAccum: 77, healAccum: 33);
            Assert.That(snap.CarriedShieldAccum, Is.EqualTo(77), "余数是厚的进度条,不是护盾");
            Assert.That(snap.CarriedHealAccum, Is.EqualTo(33));
        }

        [Test]
        public void CarriedShield_OfOne_DecaysToZero()
        {
            var snap = WinAndCapture(normal: 1, persist: 0);
            Assert.That(snap.CarriedNormalShield, Is.EqualTo(0), "1 / 2 = 0,不留残渣");
        }
    }
}
