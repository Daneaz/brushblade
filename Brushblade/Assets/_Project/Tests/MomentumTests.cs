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

        private static BattleEngine NewBattleWithChar(string charId, int maxHp, int playerAttack = 100) =>
            new(CharTableTests.RealGraph(),
                new BattleConfig { PlayerMaxHp = maxHp, PlayerAttack = playerAttack },
                new[] { charId }, Array.Empty<string>(), new[] { Dummy() }, seed: 1);

        /// <summary>沝 治疗测试専用夹具:敌人带真实攻击力,先挨一记把血打低,给治疗留出空间
        /// (2026-09-02)。没有 DamagePlayerForTest 钩子:照 DefenseValuesTests
        /// .PlayerHitAfterCasting 同一手法,用已有的公开路径(EndTurn 打一记)而不是
        /// 给引擎加新的可调用面。</summary>
        private static BattleEngine NewBattleWithCharTakingDamage(
            string charId, int maxHp, int playerAttack, int enemyAttack) =>
            new(CharTableTests.RealGraph(),
                new BattleConfig { PlayerMaxHp = maxHp, PlayerAttack = playerAttack },
                new[] { charId }, Array.Empty<string>(),
                new[] { new EnemyDef("靶", Element.Heart, 100000, enemyAttack) }, seed: 1);

        /// <summary>引爆两条效果各自的测试字(2026-09-02,Task 4 review 后改走真实 Cast() —— 见
        /// Cast() 的三条前置校验:Phase/字在图谱且在库/AP 够用,手造字塞进 Library 就都满足,
        /// 不需要绕过 Cast 的测试钩子)。</summary>
        private static RecipeGraph SpendGraph() => new(new[]
        {
            new CharDef("崩测", Element.Heart, effects: new[] { new EffectDef(EffectKind.SpendMomentum, 60) }),
            new CharDef("泻测", Element.Heart, effects: new[] { new EffectDef(EffectKind.SpendWaterPower, 80) }),
        });

        /// <summary>两只敌人的战斗夹具(2026-09-02,引爆):全体效果要断言「不止打了一个」,
        /// 单敌的 NewBattle 断不出这条。字放进 Library 才能走真实 Cast()。</summary>
        private static BattleEngine NewSpendBattle(string charId, int maxHp) =>
            new(SpendGraph(), new BattleConfig { PlayerMaxHp = maxHp, PlayerAttack = 100 },
                new[] { charId }, Array.Empty<string>(),
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
        // ⚠ 沝 已随 Task 10(2026-09-02 水系双方向重配)从 160 改成 340;
        // 圭 已随 Task 11(土系双方向重配)从 200 改成 340,与 沝 同为金档满值。

        [Test]
        public void Cast_ShieldChar_GainsMomentum()
        {
            // 圭 = 护盾 170(卡 1 级,2026-09-04 盾量砍半,旧值 340)。阈值 50 → 3 层 + 余 20。
            // ⚠ 势的产出与盾量同比缩:砍盾等于把土系「堆盾涨势」的循环速度也砍了一半。
            var battle = NewBattleWithChar("圭", maxHp: 500);
            battle.Cast("圭", -1);
            Assert.That(battle.MomentumStacks, Is.EqualTo(3));
        }

        [Test]
        public void Cast_HealChar_GainsWaterPower()
        {
            // 沝 = 治疗 340(卡 1 级,2026-09-02 双方向重配,旧值 160)。阈值 50 → 6 层 + 余 40。
            var battle = NewBattleWithChar("沝", maxHp: 500);
            battle.Cast("沝", 0);
            Assert.That(battle.WaterPowerStacks, Is.EqualTo(6));
            Assert.That(battle.HealAccum, Is.EqualTo(40));
        }

        // ---- 护盾/治疗接上角色攻击成长(2026-09-02,Task 5)----

        [Test]
        public void Shield_ScalesWithCharacterAttack()
        {
            // 圭 = 护盾 170(2026-09-04 盾量砍半,旧值 340)。ATK 150(26 级)→ 255。
            var battle = NewBattleWithChar("圭", maxHp: 500, playerAttack: 150);
            battle.Cast("圭", -1);
            Assert.That(battle.PlayerShield, Is.EqualTo(255));
        }

        [Test]
        public void Shield_AtBaselineAttack_IsIdentical()
        {
            // 恒等性硬线:ATK = 100 时一分不差。圭 170(2026-09-04 盾量砍半,旧值 340)。
            var battle = NewBattleWithChar("圭", maxHp: 500, playerAttack: 100);
            battle.Cast("圭", -1);
            Assert.That(battle.PlayerShield, Is.EqualTo(170));
        }

        [Test]
        public void Shield_IgnoresMomentumAndMorale_NoFeedbackLoop()
        {
            // 这条是正反馈环的哨兵,删了会悄悄退化回去:
            // 若护盾读 EffectiveAttack,就成了 堆盾 → 涨势 → 势放大护盾 → 涨更多势。
            // 满 5 层战意走构造函数的 startingStatuses 直接注入(与 BattleEngineTests
            // .DefenseBuff_InjectedViaConstructor_AppliesImmediately 同一手法,2026-09-02
            // review 后改用既有公开路径,不再新增 internal 测试钩子)——真实字里「战」给的是
            // Empower 不是 Morale,「刺」给 Morale 但出字即耗字、还得跨回合累积并躲开战意
            // 衰减规则,反而更麻烦。
            var battle = new BattleEngine(CharTableTests.RealGraph(),
                new BattleConfig { PlayerMaxHp = 500, PlayerAttack = 100 },
                new[] { "圭" }, Array.Empty<string>(), new[] { Dummy() }, seed: 1,
                startingStatuses: new[] { new StatusEffect
                {
                    Kind = StatusKind.Morale, Magnitude = 5,
                    Polarity = StatusPolarity.Buff, TurnsLeft = -1, SourceId = "test",
                } });
            battle.GainMomentumForTest(50 * 10);            // 满 10 层势 = EffectiveAttack 150
            Assert.That(battle.EffectiveAttack, Is.GreaterThan(100), "伤害侧确实被放大了");

            battle.Cast("圭", -1);
            // 圭 加盾前已有的势带来的盾不算:这里断言的是这一次施放的增量
            // (2026-09-04 盾量砍半:圭 340 → 170)
            Assert.That(battle.PlayerShield, Is.EqualTo(170),
                "护盾只认 config.PlayerAttack,不吃势也不吃战意");
        }

        [Test]
        public void Heal_IsAmplifiedByWaterPower()
        {
            // 水势每层 +10% 治疗(spec §3.1)。满 10 层 = +100%。
            // 用真实攻击的敌人打掉一部分血,给治疗留出空间(优先用既有公开路径,不加测试钩子)。
            //
            // 2026-09-02 双方向重配(Task 10):沝 治疗从 160 改成 340,放大后 680 > 原本
            // maxHp 500 的封顶空间(会被满血上限吞掉,测不出真实放大量)。maxHp 改成 2000,
            // 阈值(MaxHp/10)随之变成 200 —— 满 10 层要用 GainWaterPowerForTest(200 * 10),
            // 放大比例本身(层数 × 10%)未变。
            var battle = NewBattleWithCharTakingDamage("沝", maxHp: 2000, playerAttack: 100, enemyAttack: 1000);
            battle.EndTurn();   // 敌人打一记,EffectiveDodge 默认 0,必中:2000 - 1000 = 1000
            Assert.That(battle.PlayerHp, Is.EqualTo(1000), "夹具前提:留出治疗空间");
            battle.GainWaterPowerForTest(200 * 10);    // 阈值 200(maxHp/10),满 10 层
            Assert.That(battle.WaterPowerStacks, Is.EqualTo(10), "夹具前提:满层");
            int before = battle.PlayerHp;
            battle.Cast("沝", 0);
            // 沝 治疗 340(卡 1 级,2026-09-02 双方向重配)→ ×(100+100)/100 = 680
            Assert.That(battle.PlayerHp - before, Is.EqualTo(680));
        }

        [Test]
        public void WaterPower_AccumulatesFromUnamplifiedBase_NoFeedbackLoop()
        {
            // 攒水势用的是**未经水势放大**的基数。否则:治疗 → 攒水势 →
            // 水势放大治疗 → 攒更多水势,又是一个正反馈环(与 §3.5 那个同型)。
            var battleA = NewBattleWithChar("沝", maxHp: 500, playerAttack: 100);
            battleA.Cast("沝", 0);
            int stacksFromZero = battleA.WaterPowerStacks;   // 2026-09-02 双方向重配:340 / 50 = 6 层 + 余 40

            var battleB = NewBattleWithChar("沝", maxHp: 500, playerAttack: 100);
            // 3 层而非旧版的 5 层:5 + 6(stacksFromZero)会撞上 10 层上限,把差值悄悄钳平,
            // 测不出「已有水势不该让这一发攒得更多」这条不变量。3 + 6 = 9,留有余量。
            battleB.GainWaterPowerForTest(50 * 3);           // 先有 3 层
            int before = battleB.WaterPowerStacks;
            battleB.Cast("沝", 0);
            Assert.That(battleB.WaterPowerStacks - before, Is.EqualTo(stacksFromZero),
                "已有水势不该让这一发治疗攒得更多 —— 攒的基数与水势层数无关");
        }

        [Test]
        public void Heal_AmplifiesUsingStacksBeforeGain_NotAfter()
        {
            // 上一条(WaterPower_AccumulatesFromUnamplifiedBase_NoFeedbackLoop)只断言层数差,
            // 吃不出「先 GainWaterPower 再算 amplified(用攒后的层数)」这种顺序反转 —— 两种顺序
            // 下 GainWaterPower 收到的都是同一个 healBase,最终层数一样,层数差自然一样。
            // 这条改断言**单次施放的实际回血量**,并且用非零非满(5 层,MaxResourceStacks=10)
            // 的水势 —— 满层时 GainWaterPower 直接空转 return,顺序对结果毫无影响,测不出反转。
            //
            // 沝 治前 3 层(阈值 50 × 3,整除,余数 0;2026-09-02 双方向重配 沝 160→340 后,
            // 旧版的 5 层会让 amplified 的两种算法都逼近/撞上 maxHp 500 的封顶,故改用 3 层):
            //   正确顺序:amplified = AmplifyByWaterPower(340) 用旧层数 3 → 340 × 130 / 100 = 442
            //   反转顺序:先 GainWaterPower(340) 层数变 9(340 / 50 = 6 层 + 余 40,3+6=9),
            //             再用新层数 9 算 amplified → 340 × 190 / 100 = 646
            // 442 ≠ 646,反转时这条断言必须变红。
            var battle = NewBattleWithCharTakingDamage("沝", maxHp: 500, playerAttack: 100, enemyAttack: 450);
            battle.EndTurn();   // 敌人打一记,必中:500 - 450 = 50,留够 442 的回血空间不封顶
            Assert.That(battle.PlayerHp, Is.EqualTo(50), "夹具前提:留出的回血空间要盖过两种顺序的差值");
            battle.GainWaterPowerForTest(50 * 3);   // 先有 3 层(非零非满)
            Assert.That(battle.WaterPowerStacks, Is.EqualTo(3), "夹具前提:整除,层数刚好 3");

            int before = battle.PlayerHp;
            battle.Cast("沝", 0);
            Assert.That(battle.PlayerHp - before, Is.EqualTo(442),
                "放大值必须用施放前(旧)的层数算,不能用 GainWaterPower 攒完之后的新层数");
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
            var battle = NewSpendBattle("崩测", maxHp: 500);
            battle.GainMomentumForTest(50 * 4);   // 4 层
            int hp0 = battle.Enemies[0].Hp, hp1 = battle.Enemies[1].Hp;

            battle.Cast("崩测", -1);

            // 4 层 × 60 = 240,过 ScaleByAttack(基准 100 → ×1)与相克
            Assert.That(hp0 - battle.Enemies[0].Hp, Is.GreaterThan(0));
            Assert.That(hp1 - battle.Enemies[1].Hp, Is.GreaterThan(0), "是全体效果");
            Assert.That(battle.MomentumStacks, Is.EqualTo(0), "引爆清空全部层数");
        }

        [Test]
        public void SpendMomentum_AtZeroStacks_IsNoOp_ButSiblingEffectsStillFire()
        {
            // 崩 = 攻击面(全体伤害 + 发势),2026-09-02 双方向重配后挂在 AttackEffects 上,
            // 需要 attackMode: true 才能打到(支持面/effects 现在是纯护盾)。
            // 0 层时 AOE 那一半仍该打出来 —— 「0 层就整张字拒出」会把 AOE 一起吞掉。
            var battle = NewBattleWithChar("崩", maxHp: 500);
            Assert.That(battle.MomentumStacks, Is.EqualTo(0));
            int before = battle.Enemies[0].Hp;

            battle.Cast("崩", 0, attackMode: true);

            Assert.That(battle.Enemies[0].Hp, Is.LessThan(before), "AOE 那一半照常生效");
            Assert.That(battle.MomentumStacks, Is.EqualTo(0));
        }

        [Test]
        public void SpendWaterPower_DealsStacksTimesValueToAll_AndClearsStacks()
        {
            var battle = NewSpendBattle("泻测", maxHp: 500);
            battle.GainWaterPowerForTest(50 * 5);   // 5 层
            battle.Cast("泻测", -1);
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
