using System;
using System.Linq;
using NUnit.Framework;
using Brushblade.Core;

namespace Brushblade.Core.Tests
{
    /// <summary>势(土)/水势(水)的累积与被动增幅(2026-09-02,水土双方向 Task 2)。
    ///
    /// 测试字一律用 <see cref="Element.Heart"/> 且不给配方(同 CritStatTests 的既有惯例):
    /// 心对全属性生克都是 1.0x,没有配方就不会触发相生 ×3 —— 断言里看到的数字
    /// 就是势/水势本身,不掺生克。
    ///
    /// 夹具:PlayerMaxHp = 500、PlayerAttack = 100(基准,保证恒等)。</summary>
    public sealed class MomentumTests
    {
        private static RecipeGraph Graph() => new(new[]
        {
            new CharDef("甲", Element.Heart, effects: new[] { new EffectDef(EffectKind.DamageSingle, 20) }),
        });

        private static EnemyDef Dummy(int hp = 500) => new("怔", Element.Heart, hp, 0);

        private static BattleEngine NewBattle(int maxHp) =>
            new(Graph(), new BattleConfig { PlayerMaxHp = maxHp, PlayerAttack = 100 },
                Array.Empty<string>(), Array.Empty<string>(), new[] { Dummy() }, seed: 1);

        private static BattleEngine NewBattleWithChar(string charId, int maxHp) =>
            new(CharTableTests.RealGraph(), new BattleConfig { PlayerMaxHp = maxHp, PlayerAttack = 100 },
                new[] { charId }, Array.Empty<string>(), new[] { Dummy() }, seed: 1);

        /// <summary>真实字表(CastCharForTest("崩") 要读到真字)外加一个 "_test" 占位字
        /// (CastEffectForTest 用它包一条孤立 EffectDef —— ApplyEffects 会拿 def.Id 去图谱里
        /// 查 RecipeElements,不注册这张字会 KeyNotFoundException)。</summary>
        private static RecipeGraph GraphWithTestChar()
        {
            var chars = new System.Collections.Generic.List<CharDef>(CharTableTests.RealGraph().All)
            {
                new CharDef("_test", Element.Heart, effects: Array.Empty<EffectDef>()),
            };
            return new RecipeGraph(chars);
        }

        /// <summary>两只敌人的战斗夹具(2026-09-02,引爆):全体效果要断言「不止打了一个」,
        /// 单敌的 NewBattle 断不出这条。</summary>
        private static BattleEngine NewBattleWithTwoEnemies(int maxHp) =>
            new(GraphWithTestChar(), new BattleConfig { PlayerMaxHp = maxHp, PlayerAttack = 100 },
                Array.Empty<string>(), Array.Empty<string>(),
                new[] { Dummy(maxHp), Dummy(maxHp) }, seed: 1);

        /// <summary>存档 → 读档,照 SnapshotRoundTripTests 的 Reload() 写法(2026-09-02)。</summary>
        private static BattleEngine NewBattleFromSnapshot(BattleSnapshot snapshot, int maxHp)
        {
            var def = Dummy();
            var defs = new System.Collections.Generic.Dictionary<string, EnemyDef> { [def.Id] = def };
            return BattleEngine.Restore(snapshot, Graph(),
                new BattleConfig { PlayerMaxHp = maxHp, PlayerAttack = 100 }, null, defs);
        }

        /// <summary>打赢一场并 AdvanceAfterBattle,携带态里应含势/水势(2026-09-02)。</summary>
        private static RunEngine NewRunAfterWinningWithMomentum()
        {
            var def = Dummy(hp: 20); // 一发 20 伤秒杀,不受命中/生克干扰
            var config = new RunConfig
            {
                Encounters = new[] { new[] { def } },
                RewardPool = new[] { "甲" },
            };
            var run = new RunEngine(Graph(), config,
                new BattleConfig { PlayerMaxHp = 500, PlayerAttack = 100 },
                new[] { "甲" }, Array.Empty<string>(), seed: 1);
            run.Battle.GainMomentumForTest(80);    // 阈值 50 → 1 层 + 余 30
            run.Battle.GainWaterPowerForTest(80);
            run.Battle.Cast("甲", 0);
            Assert.That(run.Battle.Phase, Is.EqualTo(BattlePhase.Won), "夹具前提:必须一发秒杀");
            run.AdvanceAfterBattle();
            return run;
        }

        [Test]
        public void Shield_AtThreshold_GainsOneMomentumStack()
        {
            // MaxHp 500 → 阈值 50。加 50 盾 = 1 层。
            var battle = NewBattle(maxHp: 500);
            battle.GainMomentumForTest(50);
            Assert.That(battle.MomentumStacks, Is.EqualTo(1));
            Assert.That(battle.ShieldAccum, Is.EqualTo(0), "整除时余数归零");
        }

        [Test]
        public void Shield_BelowThreshold_KeepsRemainderAndNoStack()
        {
            var battle = NewBattle(maxHp: 500);
            battle.GainMomentumForTest(30);
            Assert.That(battle.MomentumStacks, Is.EqualTo(0));
            Assert.That(battle.ShieldAccum, Is.EqualTo(30), "不足一层的量要留着,下次接着攒");
        }

        [Test]
        public void Shield_AccumulatesAcrossCalls()
        {
            var battle = NewBattle(maxHp: 500);
            battle.GainMomentumForTest(30);
            battle.GainMomentumForTest(30);   // 合计 60 = 1 层 + 余 10
            Assert.That(battle.MomentumStacks, Is.EqualTo(1));
            Assert.That(battle.ShieldAccum, Is.EqualTo(10));
        }

        [Test]
        public void Momentum_CapsAtTenStacks_AndStopsAccumulatingRemainder()
        {
            var battle = NewBattle(maxHp: 500);
            battle.GainMomentumForTest(50 * 12);   // 够 12 层
            Assert.That(battle.MomentumStacks, Is.EqualTo(10), "上限 10 层");
            Assert.That(battle.ShieldAccum, Is.EqualTo(0),
                "满层后余数也不再攒 —— 否则掉层时会瞬间跳回满层");
        }

        [Test]
        public void Momentum_AddsFivePercentDamagePerStack()
        {
            var battle = NewBattle(maxHp: 500);   // PlayerAttack = 100 基准
            int baseline = battle.EffectiveAttack;
            Assert.That(baseline, Is.EqualTo(100));
            battle.GainMomentumForTest(50 * 10);  // 满 10 层
            Assert.That(battle.MomentumStacks, Is.EqualTo(10));
            Assert.That(battle.EffectiveAttack, Is.EqualTo(150), "10 层 = +50%,与战意同顶");
        }

        [Test]
        public void Heal_UsesNominalValue_SoOverhealStillGainsWaterPower()
        {
            // 这条是整套改动的核心诉求:满血时治疗一分不亏。
            var battle = NewBattle(maxHp: 500);   // 满血
            Assert.That(battle.PlayerHp, Is.EqualTo(500));
            battle.GainWaterPowerForTest(100);    // 名义治疗 100,实际回血 0
            Assert.That(battle.PlayerHp, Is.EqualTo(500), "满血不会超上限");
            Assert.That(battle.WaterPowerStacks, Is.EqualTo(2), "溢出的治疗照样攒水势");
        }

        [Test]
        public void WaterPower_CapsAtTenStacks()
        {
            var battle = NewBattle(maxHp: 500);
            battle.GainWaterPowerForTest(50 * 12);
            Assert.That(battle.WaterPowerStacks, Is.EqualTo(10));
        }

        // ---- 接入点:真实字表的施法路径(2026-09-02)----
        // ⚠ 下面两条读的是**当前**字表数值(圭 = 护盾 200,沝 = 治疗 160,均卡 1 级)。
        // Task 10/11 重配字表数值后,这两条断言要跟着更新。

        [Test]
        public void Cast_ShieldChar_GainsMomentum()
        {
            // 圭 = 护盾 200(卡 1 级)。MaxHp 500 → 阈值 50 → 4 层。
            var battle = NewBattleWithChar("圭", maxHp: 500);
            battle.Cast("圭", -1);
            Assert.That(battle.MomentumStacks, Is.EqualTo(4));
        }

        [Test]
        public void Cast_HealChar_GainsWaterPower()
        {
            // 沝 = 治疗 160(卡 1 级)。阈值 50 → 3 层 + 余 10。
            var battle = NewBattleWithChar("沝", maxHp: 500);
            battle.Cast("沝", 0);
            Assert.That(battle.WaterPowerStacks, Is.EqualTo(3));
            Assert.That(battle.HealAccum, Is.EqualTo(10));
        }

        // ---- 快照往返(2026-09-02,Task 3)----

        [Test]
        public void Snapshot_RoundTrip_PreservesStacksAndRemainder()
        {
            var battle = NewBattle(maxHp: 500);
            battle.GainMomentumForTest(130);      // 2 层 + 余 30
            battle.GainWaterPowerForTest(70);     // 1 层 + 余 20
            var snapshot = battle.Capture();
            var restored = NewBattleFromSnapshot(snapshot, maxHp: 500);

            Assert.That(restored.MomentumStacks, Is.EqualTo(2));
            Assert.That(restored.ShieldAccum, Is.EqualTo(30), "余数漏存是静默的:续爬会丢半层");
            Assert.That(restored.WaterPowerStacks, Is.EqualTo(1));
            Assert.That(restored.HealAccum, Is.EqualTo(20));
        }

        [Test]
        public void CarriedStatuses_IncludeMomentumAndWaterPower()
        {
            // 护盾本来就整场爬塔延续(_shieldNormal),势必须跟它同步,
            // 否则每场重新攒,而护盾还留着 —— 两者会一直对不上。
            var run = NewRunAfterWinningWithMomentum();
            Assert.That(run.CarriedStatuses.Count(s => s.Kind == StatusKind.Momentum), Is.EqualTo(1));
            Assert.That(run.CarriedStatuses.Count(s => s.Kind == StatusKind.WaterPower), Is.EqualTo(1));
        }

        // ---- 引爆:SpendMomentum / SpendWaterPower(2026-09-02,Task 4)----

        [Test]
        public void SpendMomentum_DealsStacksTimesValueToAll_AndClearsStacks()
        {
            var battle = NewBattleWithTwoEnemies(maxHp: 500);
            battle.GainMomentumForTest(50 * 4);   // 4 层
            int hp0 = battle.Enemies[0].Hp, hp1 = battle.Enemies[1].Hp;

            battle.CastEffectForTest(new EffectDef(EffectKind.SpendMomentum, 60));

            // 4 层 × 60 = 240,过 ScaleByAttack(基准 100 → ×1)与相克
            Assert.That(hp0 - battle.Enemies[0].Hp, Is.GreaterThan(0));
            Assert.That(hp1 - battle.Enemies[1].Hp, Is.GreaterThan(0), "是全体效果");
            Assert.That(battle.MomentumStacks, Is.EqualTo(0), "引爆清空全部层数");
        }

        [Test]
        [Ignore("等 Task 11 给崩配发势")]
        public void SpendMomentum_AtZeroStacks_IsNoOp_ButSiblingEffectsStillFire()
        {
            // 崩 = 全体伤害 + 发势。0 层时 AOE 那一半仍该打出来 ——
            // 「0 层就整张字拒出」会把 AOE 一起吞掉。
            var battle = NewBattleWithTwoEnemies(maxHp: 500);
            Assert.That(battle.MomentumStacks, Is.EqualTo(0));
            int before = battle.Enemies[0].Hp;

            battle.CastCharForTest("崩");

            Assert.That(battle.Enemies[0].Hp, Is.LessThan(before), "AOE 那一半照常生效");
            Assert.That(battle.MomentumStacks, Is.EqualTo(0));
        }

        [Test]
        public void SpendWaterPower_DealsStacksTimesValueToAll_AndClearsStacks()
        {
            var battle = NewBattleWithTwoEnemies(maxHp: 500);
            battle.GainWaterPowerForTest(50 * 5);   // 5 层
            battle.CastEffectForTest(new EffectDef(EffectKind.SpendWaterPower, 80));
            Assert.That(battle.WaterPowerStacks, Is.EqualTo(0));
        }

        [Test]
        public void SpendMomentum_DoesNotNeedTarget()
        {
            // 全体效果,与全体驱散(淡)、全体引爆(炸)同处理
            var def = new CharDef("测", Element.Earth,
                effects: new[] { new EffectDef(EffectKind.SpendMomentum, 60) });
            Assert.That(BattleEngine.NeedsTarget(def), Is.False);
        }
    }
}
