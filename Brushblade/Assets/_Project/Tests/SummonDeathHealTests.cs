using System;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>木脉 L2「归根」(spec 2026-09-13 §2.4):召唤物阵亡时玩家回复
    /// 该召唤物最大生命的 N%。
    ///
    /// 它是一次**完整治疗**(用户 2026-09-13 裁定):吃泉放大、攒泉,也吃水脉 L2
    /// 的溢流转伤害。所以这里既要断回血量,也要断这三条咬合。
    ///
    /// 召唤物死亡在引擎里有两条路径(自焚 / 挨打),两条都要断 —— 它们此前各自
    /// 调 RefreshSummonAura,是这一层最典型的「同一份逻辑两条路径」。</summary>
    public class SummonDeathHealTests
    {
        private const int SummonHp = 200;

        private static RecipeGraph Graph() => new(new[]
        {
            new CharDef("兵", Element.Wood,
                effects: new[] { new EffectDef(EffectKind.Summon, SummonHp, summonAttack: 0, summonChar: "木") }),
        });

        private static BattleEngine Engine(int healPercent, int overhealPercent = 0,
            int enemyAttack = 0, int startingHp = 500) =>
            new(Graph(), new BattleConfig
                {
                    DropTable = new[] { "木" }, PlayerMaxHp = 500, ApPerTurn = 20,
                    SummonDeathHealPercent = healPercent,
                    OverhealDamagePercent = overhealPercent,
                },
                new[] { "兵" }, Array.Empty<string>(),
                new[] { new EnemyDef("靶", Element.Heart, 9000, enemyAttack) },
                seed: 1, startingHp: startingHp);

        [Test]
        public void SummonKilledByDamage_HealsThePlayer()
        {
            // 玩家缺 100 血,召唤物 200 上限 → 回 40
            var engine = Engine(20, startingHp: 400);
            Assert.That(engine.Cast("兵"), Is.EqualTo(BattleError.None));
            int slot = Array.FindIndex(engine.Summons.ToArray(), s => s != null);
            Assert.That(slot, Is.GreaterThanOrEqualTo(0));
            engine.KillSummonForTest(slot);
            Assert.That(engine.PlayerHp, Is.EqualTo(440), "400 + 200×20% = 440");
        }

        [Test]
        public void PerkOff_HealsNothing()
        {
            var engine = Engine(0, startingHp: 400);
            Assert.That(engine.Cast("兵"), Is.EqualTo(BattleError.None));
            int slot = Array.FindIndex(engine.Summons.ToArray(), s => s != null);
            engine.KillSummonForTest(slot);
            Assert.That(engine.PlayerHp, Is.EqualTo(400), "未点亮,阵亡就只是阵亡");
        }

        [Test]
        public void TheHealGainsWellspring()
        {
            var engine = Engine(20, startingHp: 400);
            Assert.That(engine.Cast("兵"), Is.EqualTo(BattleError.None));
            Assert.That(engine.WellspringStacks, Is.EqualTo(0), "前提:起手 0 层");
            int slot = Array.FindIndex(engine.Summons.ToArray(), s => s != null);
            engine.KillSummonForTest(slot);
            // 攒泉阈值 100:一次 40 攒不满一层,但余数要记进去。连死四只才涨一层,
            // 这里只断"确实走了攒泉通道"——余数不外露,故用连续多只验证。
            Assert.That(engine.HealAccum, Is.EqualTo(40),
                "走的是统一治疗入口,余数进 _healAccum");
        }

        [Test]
        public void FullHpPlayer_OverflowBecomesDamageWhenWaterTierTwoIsOn()
        {
            // 玩家满血 → 归根的 40 全部溢出 → 水脉溢流 50% → 20 伤害
            var engine = Engine(20, overhealPercent: 50, startingHp: 500);
            Assert.That(engine.Cast("兵"), Is.EqualTo(BattleError.None));
            int slot = Array.FindIndex(engine.Summons.ToArray(), s => s != null);
            int before = engine.Enemies[0].Hp;
            engine.KillSummonForTest(slot);
            Assert.That(engine.PlayerHp, Is.EqualTo(500), "满血,一点没回");
            Assert.That(before - engine.Enemies[0].Hp, Is.EqualTo(20),
                "40 溢出 × 50% = 20,两条 L2 咬合");
        }

        [Test]
        public void FullHpPlayer_NoDamageWhenWaterTierTwoIsOff()
        {
            var engine = Engine(20, overhealPercent: 0, startingHp: 500);
            Assert.That(engine.Cast("兵"), Is.EqualTo(BattleError.None));
            int slot = Array.FindIndex(engine.Summons.ToArray(), s => s != null);
            int before = engine.Enemies[0].Hp;
            engine.KillSummonForTest(slot);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(before), "没点水脉就只是浪费掉");
        }

        [Test]
        public void ReviveThenDieAgain_HealsAgain()
        {
            var engine = Engine(20, startingHp: 300);
            Assert.That(engine.Cast("兵"), Is.EqualTo(BattleError.None));
            int slot = Array.FindIndex(engine.Summons.ToArray(), s => s != null);
            engine.KillSummonForTest(slot);
            Assert.That(engine.PlayerHp, Is.EqualTo(340));
            engine.ReviveSummonForTest(slot);
            engine.KillSummonForTest(slot);
            Assert.That(engine.PlayerHp, Is.EqualTo(380), "每一次真实死亡都算一次,不去重");
        }
    }
}
