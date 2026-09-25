using System;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>字库补给(2026-09-23 用户拍板):持有字跌破 3 张时,看一次广告补一轮 5 选 2,
    /// 整次登塔一次。
    ///
    /// 它与复活补给**共用** <see cref="RunPhase.Reviving"/> 和整套选字 API
    /// (ReviveCharPicksLeft / PickReviveChar / SkipReviveReward)—— 选字 UI 一行不用重写。
    /// 共享状态的代价是「两条路径读同一个字段」,所以这里专门钉两件事:
    /// ① <see cref="RunEngine.CurrentSupply"/> 能把两种来源分开(文案靠它分叉);
    /// ② 字库补给是**一轮**(复活是两轮),轮次用尽要回到 InBattle。</summary>
    public class RestockSupplyTests
    {
        /// <summary>六张纯伤害字:够 RollRewardOptions 抽满 5 个候选(去重后仍有余)。</summary>
        private static RecipeGraph Graph() => new(new[]
        {
            new CharDef("甲", Element.Heart, effects: new[] { new EffectDef(EffectKind.DamageSingle, 30) }),
            new CharDef("乙", Element.Heart, effects: new[] { new EffectDef(EffectKind.DamageSingle, 3) }),
            new CharDef("丙", Element.Heart, effects: new[] { new EffectDef(EffectKind.DamageSingle, 3) }),
            new CharDef("丁", Element.Heart, effects: new[] { new EffectDef(EffectKind.DamageSingle, 3) }),
            new CharDef("戊", Element.Heart, effects: new[] { new EffectDef(EffectKind.DamageSingle, 3) }),
            new CharDef("己", Element.Heart, effects: new[] { new EffectDef(EffectKind.DamageSingle, 3) }),
            new CharDef("庚", Element.Heart, effects: new[] { new EffectDef(EffectKind.DamageSingle, 3) }),
        });

        private static RunConfig Config() => new()
        {
            Encounters = new[] { new[] { new EnemyDef("靶", Element.Heart, 500, 0) } },
            RewardPool = new[] { "乙", "丙", "丁", "戊", "己", "庚" },
        };

        /// <summary>起手 <paramref name="libraryCount"/> 张字;敌人攻 0,战斗不会自行结束。</summary>
        private static RunEngine Run(int libraryCount)
        {
            var library = new string[libraryCount];
            for (int i = 0; i < libraryCount; i++) library[i] = "甲";
            return new RunEngine(Graph(), Config(), new BattleConfig { DropTable = new[] { "甲" } },
                startingLibrary: library, startingPool: Array.Empty<string>(), seed: 7);
        }

        [Test]
        public void OnlyOffered_WhenLibraryDropsBelowThree()
        {
            Assert.That(Run(2).RestockAvailable, Is.True, "2 张:可领");
            Assert.That(Run(3).RestockAvailable, Is.False, "3 张:正好卡在线上,不给");
            Assert.That(Run(5).RestockAvailable, Is.False, "5 张:不给");
        }

        [Test]
        public void OncePerTowerClimb()
        {
            var run = Run(1);
            Assert.That(run.TryRestock(), Is.True);
            run.SkipReviveReward();                       // 挑完/跳过,回到战斗
            Assert.That(run.RestockAvailable, Is.False, "同一次登塔不该再给第二次");
            Assert.That(run.TryRestock(), Is.False);
        }

        [Test]
        public void MarkRestocked_BlocksASecondClaimAfterResume()
        {
            var run = Run(1);
            run.MarkRestocked();                          // 模拟从快照恢复
            Assert.That(run.RestockAvailable, Is.False);
            Assert.That(run.TryRestock(), Is.False);
        }

        [Test]
        public void EntersSupplyPhase_TaggedAsRestock()
        {
            var run = Run(1);
            Assert.That(run.TryRestock(), Is.True);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Reviving), "复用复活补给的选字阶段");
            Assert.That(run.CurrentSupply, Is.EqualTo(SupplyKind.Restock),
                "来源要标明 —— 文案靠它分叉,不能靠猜战斗阶段");
        }

        [Test]
        public void OffersFiveOptionsAndTwoPicks()
        {
            var run = Run(1);
            run.TryRestock();
            Assert.That(run.RewardOptions.Count, Is.EqualTo(5), "候选 5 个");
            Assert.That(run.ReviveCharPicksLeft, Is.EqualTo(2), "选 2 次");
        }

        [Test]
        public void PickedChar_LandsInTheCurrentBattleLibrary()
        {
            var run = Run(1);
            run.TryRestock();
            string picked = run.RewardOptions[0];
            Assert.That(run.PickReviveChar(0), Is.True);
            Assert.That(run.Battle.Library.Contains(picked), Is.True, "字要落进当前战斗的字库");
        }

        [Test]
        public void OneRoundOnly_ThenBackToBattle()
        {
            // 复活补给是**两轮**(败北补偿),字库补给只有一轮 —— 共用同一套轮次字段,
            // 这条就是两者唯一的数值分歧,漏掉的话字库补给会白送四个字
            var run = Run(1);
            run.TryRestock();
            run.PickReviveChar(0);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Reviving), "还剩一次选字");
            run.PickReviveChar(0);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.InBattle), "两次选完即回战斗,不再开第二轮");
            Assert.That(run.Battle.Library.Count, Is.EqualTo(3), "起手 1 张 + 补给 2 张");
        }

        [Test]
        public void ReviveStaysTaggedAsRevive()
        {
            // 反向守卫:共用阶段之后,复活那条路径不能被带偏
            var run = Run(1);
            run.TryRestock();
            run.SkipReviveReward();
            Assert.That(run.CurrentSupply, Is.EqualTo(SupplyKind.Restock));

            var other = Run(1);
            Assert.That(other.CurrentSupply, Is.EqualTo(SupplyKind.Revive),
                "缺省值是复活 —— 复活路径此前不设这个字段,缺省错了它就永远读成补给");
        }
    }
}
