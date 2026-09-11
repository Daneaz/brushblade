using System;
using System.Linq;
using NUnit.Framework;
using Brushblade.Core;

namespace Brushblade.Core.Tests
{
    /// <summary>厚(土)/泉(水)的累积与被动增幅(2026-09-02,水土双方向 Task 2)。
    ///
    /// 测试字一律用 <see cref="Element.Heart"/> 且不给配方(同 CritStatTests 的既有惯例):
    /// 心对全属性生克都是 1.0x,没有配方就不会触发相生 ×3 —— 断言里看到的数字
    /// 就是厚/泉本身,不掺生克。
    ///
    /// 夹具:PlayerMaxHp = 500、PlayerAttack = 100(基准,保证恒等)。</summary>
    public sealed class HeftTests
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

        /// <summary>冰 治疗测试専用夹具:敌人带真实攻击力,先挨一记把血打低,给治疗留出空间
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
            new CharDef("崩测", Element.Heart, effects: new[] { new EffectDef(EffectKind.SpendHeft, 60) }),
            new CharDef("发测", Element.Heart, effects: new[] { new EffectDef(EffectKind.SpendWellspring, 80) }),
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

        /// <summary>打赢一场并 AdvanceAfterBattle,携带态里应含厚/泉(2026-09-02)。</summary>
        private static RunEngine NewRunAfterWinningWithHeft()
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
            // 2026-09-05 阈值 /10 → /5:阈值 50 → 100,80 已不够攒出一层(旧口径下是 1 层 + 余 30),
            // 改用 100 保证仍然实打实攒出 1 层——本测试断言的是 Kind 存在与否,不是层数,
            // 但用一个确实产出层数的输入更贴合「携带态里应含厚/泉」这句设计意图。
            // **2026-09-11(阈值 /5 → /7)**:阈值 100 → 71,100 仍能攒出 1 层(余 29),
            // 断言不会变红;改成 71 只是让「1 层 + 余 0」这句注释继续为真。
            run.Battle.GainHeftForTest(71);    // 阈值 71 → 1 层 + 余 0
            run.Battle.GainWellspringForTest(71);
            run.Battle.Cast("甲", 0);
            Assert.That(run.Battle.Phase, Is.EqualTo(BattlePhase.Won), "夹具前提:必须一发秒杀");
            run.AdvanceAfterBattle();
            return run;
        }

        [Test]
        public void Shield_AtThreshold_GainsOneHeftStack()
        {
            // MaxHp 500 → 阈值 100(2026-09-05 阈值 /10 → /5,原阈值 50)。加 100 盾 = 1 层。
            // **2026-09-11(阈值 /5 → /7)**:500/7 = 71。这条守的是「整除时余数归零」,
            // 所以要换一个**能被 71 整除**的量(71),不是把余数断言改成 29。
            var battle = NewBattle(maxHp: 500);
            battle.GainHeftForTest(71);
            Assert.That(battle.HeftStacks, Is.EqualTo(1));
            Assert.That(battle.ShieldAccum, Is.EqualTo(0), "整除时余数归零");
        }

        [Test]
        public void Shield_BelowThreshold_KeepsRemainderAndNoStack()
        {
            var battle = NewBattle(maxHp: 500);
            battle.GainHeftForTest(30);
            Assert.That(battle.HeftStacks, Is.EqualTo(0));
            Assert.That(battle.ShieldAccum, Is.EqualTo(30), "不足一层的量要留着,下次接着攒");
        }

        [Test]
        public void Shield_AccumulatesAcrossCalls()
        {
            // 2026-09-05 阈值 /10 → /5:阈值 50 → 100,原先两次各 30(合计 60 越过旧阈值 50)
            // 已不够跨过新阈值,改成两次各 60(合计 120 = 1 层 + 余 20)。
            // **2026-09-11(阈值 /5 → /7)**:阈值 100 → 71。这条守的是「单次都跨不过、
            // 合起来才跨过」,60 < 71 仍然满足,输入不用动;只有余数跟着阈值走:
            // 120 − 71 = 49。
            var battle = NewBattle(maxHp: 500);
            battle.GainHeftForTest(60);
            battle.GainHeftForTest(60);   // 合计 120 = 1 层 + 余 49
            Assert.That(battle.HeftStacks, Is.EqualTo(1));
            Assert.That(battle.ShieldAccum, Is.EqualTo(49));
        }

        [Test]
        public void Heft_CapsAtTenStacks_AndStopsAccumulatingRemainder()
        {
            // 2026-09-05 阈值 /10 → /5:阈值 50 → 100,12 层份额从 50×12 改成 100×12。
            // **2026-09-11(阈值 /5 → /7)**:阈值 100 → 71,12 层份额改成 71×12
            // (100×12 仍会撞上限、断言不会红,改是为了让注释里的「12 层」继续为真)。
            var battle = NewBattle(maxHp: 500);
            battle.GainHeftForTest(71 * 12);   // 够 12 层
            Assert.That(battle.HeftStacks, Is.EqualTo(10), "上限 10 层");
            Assert.That(battle.ShieldAccum, Is.EqualTo(0),
                "满层后余数也不再攒 —— 否则掉层时会瞬间跳回满层");
        }

        [Test]
        public void Heft_AddsFivePercentDamagePerStack()
        {
            // 2026-09-05 阈值 /10 → /5:满 10 层份额从 50×10 改成 100×10;
            // HeftPercentPerStack 本身未变(仍是 5%),满层结果依旧 150。
            // **2026-09-11(阈值 /5 → /7)**:满 10 层份额改成 71×10,同上——只为注释为真。
            var battle = NewBattle(maxHp: 500);   // PlayerAttack = 100 基准
            int baseline = battle.EffectiveAttack;
            Assert.That(baseline, Is.EqualTo(100));
            battle.GainHeftForTest(71 * 10);  // 满 10 层
            Assert.That(battle.HeftStacks, Is.EqualTo(10));
            Assert.That(battle.EffectiveAttack, Is.EqualTo(150), "10 层 = +50%,与战意同顶");
        }

        [Test]
        public void Heal_UsesNominalValue_SoOverhealStillGainsWellspring()
        {
            // 这条是整套改动的核心诉求:满血时治疗一分不亏。
            // 2026-09-05 阈值 /10 → /5:阈值 50 → 100,100 名义治疗从「2 层」变成「1 层整除」。
            // **2026-09-11(阈值 /5 → /7)**:阈值 100 → 71,名义治疗改 71 保持「1 层整除」。
            var battle = NewBattle(maxHp: 500);   // 满血
            Assert.That(battle.PlayerHp, Is.EqualTo(500));
            battle.GainWellspringForTest(71);     // 名义治疗 71,实际回血 0
            Assert.That(battle.PlayerHp, Is.EqualTo(500), "满血不会超上限");
            Assert.That(battle.WellspringStacks, Is.EqualTo(1), "溢出的治疗照样攒泉");
        }

        [Test]
        public void Wellspring_CapsAtTenStacks()
        {
            // 2026-09-05 阈值 /10 → /5:阈值 50 → 100,12 层份额从 50×12 改成 100×12。
            // **2026-09-11(阈值 /5 → /7)**:12 层份额改成 71×12,同 Heft 那条——只为注释为真。
            var battle = NewBattle(maxHp: 500);
            battle.GainWellspringForTest(71 * 12);
            Assert.That(battle.WellspringStacks, Is.EqualTo(10));
        }

        // ---- 接入点:真实字表的施法路径(2026-09-02)----
        // ⚠ 沝 已随 Task 10(2026-09-02 水系双方向重配)从 160 改成 340;
        // 圭 已随 Task 11(土系双方向重配)从 200 改成 340,与 沝 同为金档满值。
        // 2026-09-05:沝 随字表调整移出,水系样本换成 冰 —— 冰 同为金档、双方向、
        // HealSelf 满值同样是 340,下面涉及 沝 的断言/算式原样成立,只改字。

        [Test]
        public void Cast_ShieldChar_GainsHeft()
        {
            // 圭 = 护盾 170(卡 1 级,2026-09-04 盾量砍半,旧值 340)。
            // 2026-09-05 阈值 /10 → /5:阈值 50 → 100,170 / 100 = 1 层 + 余 70(原为 3 层 + 余 20)。
            // ⚠ 厚的产出与盾量同比缩:砍盾等于把土系「堆盾涨厚」的循环速度也砍了一半。
            // ⚠ 2026-09-11(档位统一 T3):金档护盾锚点 300 → 209,圭 的盾 119 → 83,
            // 而阈值仍是 maxHp/5 = 100 —— **单发不再跨过一层**,只留 83 点余数。
            // 这不是接线坏了,是「金档砍 31%」撞上「阈值按 maxHp 定」的直接后果:
            // 土系「堆盾涨厚」现在要两张牌才起一层。断言改钉余数,层数钉 0 ——
            // 若哪天调阈值或调护盾值让它重新跨线,这条会以 XFAIL 的方式提醒重新标定。
            // **2026-09-11(阈值 /5 → /7)**:那一天就是今天。用户裁定「3 次圭该叠 3 层厚」,
            // 即金档单发就该有 1 层;阈值 100 → 71(5/7 = 0.714 ≈ 金档锚点同批的 ×0.695,
            // 是按锚点同幅度回调,不是拍的),83 ≥ 71 → 单发 1 层 + 余 12。方向反过来钉:
            // 层数钉 1,并把「3 次圭 = 3 层」这条裁定直接写成断言。
            // ⚠ 攒层本来就跨施放累计(GainStacks 的 accum 是字段不是局部量,余数不丢),
            // 本次改的是**单发够不够一层**的门槛,不是累计与否。
            var battle = NewBattleWithChar("圭", maxHp: 500);
            battle.Cast("圭", -1);
            Assert.That(battle.HeftStacks, Is.EqualTo(1), "圭 的盾 83 ≥ 阈值 71,单发就是 1 层");
            Assert.That(battle.ShieldAccum, Is.EqualTo(12), "83 − 71 = 12,余数留着接着攒");
            // 用户裁定:3 次圭 = 3 层厚。出字即耗字,同一张字没法连出第二次,
            // 用测试钩子补上后两发的同等盾量(83 × 3 = 249 = 71×3 + 36)。
            battle.GainHeftForTest(83);
            battle.GainHeftForTest(83);
            Assert.That(battle.HeftStacks, Is.EqualTo(3), "3 次圭该叠 3 层厚(用户裁定)");
            Assert.That(battle.ShieldAccum, Is.EqualTo(36), "249 − 71×3 = 36");
        }

        [Test]
        public void Cast_HealChar_GainsWellspring()
        {
            // 2026-09-07 字表重做 P2:冰 按 spec §1.4 公式重新标定,治疗 340 → 146
            // (金档治疗锚点240 × HEAL_F × (1 − (冻结1+对控制)×K金),见
            // DualDirectionTests.WaterCharValues_MatchRarityAnchors 的推导)。
            // 阈值仍是 maxHp/5 = 100:146 / 100 = 1 层 + 余 46。
            //
            // ⚠ 观测点留给 T5:旧值 340 能攒 3 层,新值 146 只攒 1 层——泉层数掉了不止一半,
            // 是「治疗值下调」与「阈值 2026-09-05 已经从 /10 改到 /5」两次调整叠加的结果,
            // 不是本次改动本身引入的新账。1 层是否太慢、要不要在 T5 仿真里再调阈值或
            // 治疗值,留给仿真读数判断,这里只钉当前配置下的真实产出。
            // ⚠ 2026-09-11(档位统一 T3):金档治疗锚点 240 → 162,冰 的治疗 146 → 99,
            // 同 Cast_ShieldChar_GainsHeft —— **单发跨不过阈值 100**,只留 99 点余数(差 1)。
            // **2026-09-11(阈值 /5 → /7)**:阈值 100 → 71,99 ≥ 71 → 单发 1 层 + 余 28。
            // 与 Cast_ShieldChar_GainsHeft 同批同因(见那条的账),方向一并反过来钉:
            // 层数钉 1,并把「三发」的累计结果一起钉住(99 × 3 = 297 = 71×4 + 13 → 4 层)。
            var battle = NewBattleWithChar("冰", maxHp: 500);
            battle.Cast("冰", 0);
            Assert.That(battle.WellspringStacks, Is.EqualTo(1), "冰 的治疗 99 ≥ 阈值 71,单发就是 1 层");
            Assert.That(battle.HealAccum, Is.EqualTo(28), "99 − 71 = 28");
            // 三发的累计(出字即耗字,用测试钩子补后两发的同等治疗量)
            battle.GainWellspringForTest(99);
            battle.GainWellspringForTest(99);
            Assert.That(battle.WellspringStacks, Is.EqualTo(4), "297 / 71 = 4 层(治疗值比盾量高,攒得比厚快)");
            Assert.That(battle.HealAccum, Is.EqualTo(13), "297 − 71×4 = 13");
        }

        // ---- 护盾/治疗接上角色攻击成长(2026-09-02,Task 5)----

        [Test]
        public void Shield_ScalesWithCharacterAttack()
        {
            // 2026-09-07 字表重做 P2:圭 护盾按 spec §1.4 公式重新标定,170 → 119(见
            // DualDirectionTests.EarthCharValues_MatchRarityAnchors 的推导)。ATK 150(26 级)
            // 相对基准 100 的比例不变:119 × 150 / 100 = 178.5,向下取整 178。
            // 2026-09-11(T3):金档护盾锚点 300 → 209,圭 119 → 83;83 × 150 / 100 = 124.5 → 124。
            var battle = NewBattleWithChar("圭", maxHp: 500, playerAttack: 150);
            battle.Cast("圭", -1);
            Assert.That(battle.PlayerShield, Is.EqualTo(124));
        }

        [Test]
        public void Shield_AtBaselineAttack_IsIdentical()
        {
            // 恒等性硬线:ATK = 100 时一分不差。
            // 2026-09-07 字表重做 P2:圭 护盾按公式重新标定,170 → 119。
            // 2026-09-11(T3):119 → 83。
            var battle = NewBattleWithChar("圭", maxHp: 500, playerAttack: 100);
            battle.Cast("圭", -1);
            Assert.That(battle.PlayerShield, Is.EqualTo(83));
        }

        [Test]
        public void Shield_IgnoresHeftAndMorale_NoFeedbackLoop()
        {
            // 这条是正反馈环的哨兵,删了会悄悄退化回去:
            // 若护盾读 EffectiveAttack,就成了 堆盾 → 涨厚 → 厚放大护盾 → 涨更多厚。
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
            // 2026-09-05 阈值 /10 → /5:满 10 层厚份额从 50×10 改成 100×10,保持「满 10 层」不变。
            // **2026-09-11(阈值 /5 → /7)**:份额改成 71×10,保持「满 10 层」不变。
            battle.GainHeftForTest(71 * 10);            // 满 10 层厚
            Assert.That(battle.EffectiveAttack, Is.GreaterThan(100), "伤害侧确实被放大了");

            battle.Cast("圭", -1);
            // 圭 加盾前已有的厚带来的盾不算:这里断言的是这一次施放的增量
            // (2026-09-07 字表重做 P2:圭 护盾按公式重新标定,170 → 119)
            Assert.That(battle.PlayerShield, Is.EqualTo(83),
                "护盾只认 config.PlayerAttack,不吃厚也不吃战意");
        }

        [Test]
        public void Heal_IsAmplifiedByWellspring()
        {
            // 泉每层 +5% 治疗(spec §3.1,2026-09-05 由 +10% 改)。满 10 层 = +50%。
            // 用真实攻击的敌人打掉一部分血,给治疗留出空间(优先用既有公开路径,不加测试钩子)。
            //
            // 2026-09-02 双方向重配(Task 10):沝 治疗从 160 改成 340,放大后 > 原本
            // maxHp 500 的封顶空间(会被满血上限吞掉,测不出真实放大量)。maxHp 改成 2000,
            // 阈值(MaxHp/N)随之变成 400 —— 满 10 层要用 GainWellspringForTest(400 * 10)。
            // 2026-09-05 阈值 /10 → /5:阈值从 200(2000/10)改成 400(2000/5),
            // 满 10 层的份额从 200×10 改成 400×10。
            // 2026-09-05:沝 随字表调整移出,换成 冰(HealSelf 满值同样是 340),算式不变。
            var battle = NewBattleWithCharTakingDamage("冰", maxHp: 2000, playerAttack: 100, enemyAttack: 1000);
            battle.EndTurn();   // 敌人打一记,EffectiveDodge 默认 0,必中:2000 - 1000 = 1000
            Assert.That(battle.PlayerHp, Is.EqualTo(1000), "夹具前提:留出治疗空间");
            // **2026-09-11(阈值 /5 → /7)**:maxHp 2000 的阈值从 400 变成 285(2000/7),
            // 满 10 层的份额随之改成 285×10。放大量本身不变(泉仍是 5%/层)。
            battle.GainWellspringForTest(285 * 10);   // 阈值 285(maxHp/7),满 10 层
            Assert.That(battle.WellspringStacks, Is.EqualTo(10), "夹具前提:满层");
            int before = battle.PlayerHp;
            battle.Cast("冰", 0);
            // 2026-09-07 字表重做 P2:冰 治疗 340 → 146(公式重新标定,见
            // DualDirectionTests.WaterCharValues_MatchRarityAnchors)→ ×(100+50)/100 = 219
            // 2026-09-11(T3):金档治疗锚点 240 → 162,冰 146 → 99;99 × 150 / 100 = 148.5 → 148。
            Assert.That(battle.PlayerHp - before, Is.EqualTo(148));
        }

        [Test]
        public void Wellspring_AccumulatesFromUnamplifiedBase_NoFeedbackLoop()
        {
            // 攒泉用的是**未经泉放大**的基数。否则:治疗 → 攒泉 →
            // 泉放大治疗 → 攒更多泉,又是一个正反馈环(与 §3.5 那个同型)。
            var battleA = NewBattleWithChar("冰", maxHp: 500, playerAttack: 100);
            battleA.Cast("冰", 0);
            // 2026-09-05 阈值 /10 → /5:阈值 50 → 100,340 / 100 = 3 层 + 余 40(原为 6 层)。
            // **2026-09-11(阈值 /5 → /7)**:冰 治疗已是 99,99 / 71 = 1 层 + 余 28。
            int stacksFromZero = battleA.WellspringStacks;

            var battleB = NewBattleWithChar("冰", maxHp: 500, playerAttack: 100);
            // 3 层:3 + 1(stacksFromZero)= 4,不会撞上 10 层上限,不会把差值悄悄钳平,
            // 测得出「已有泉不该让这一发攒得更多」这条不变量。
            // 2026-09-05 阈值 /10 → /5:阈值 50 → 100,先攒 3 层的份额从 50×3 改成 100×3。
            // **2026-09-11(阈值 /5 → /7)**:份额改成 71×3(整除,余数 0,让「先有 3 层」为真)。
            battleB.GainWellspringForTest(71 * 3);           // 先有 3 层
            int before = battleB.WellspringStacks;
            battleB.Cast("冰", 0);
            Assert.That(battleB.WellspringStacks - before, Is.EqualTo(stacksFromZero),
                "已有泉不该让这一发治疗攒得更多 —— 攒的基数与泉层数无关");
        }

        [Test]
        public void Heal_AmplifiesUsingStacksBeforeGain_NotAfter()
        {
            // 上一条(Wellspring_AccumulatesFromUnamplifiedBase_NoFeedbackLoop)只断言层数差,
            // 吃不出「先 GainWellspring 再算 amplified(用攒后的层数)」这种顺序反转 —— 两种顺序
            // 下 GainWellspring 收到的都是同一个 healBase,最终层数一样,层数差自然一样。
            // 这条改断言**单次施放的实际回血量**,并且用非零非满(5 层,MaxResourceStacks=10)
            // 的泉 —— 满层时 GainWellspring 直接空转 return,顺序对结果毫无影响,测不出反转。
            //
            // 2026-09-07 字表重做 P2:冰 治疗 340 → 146(公式重新标定,见
            // DualDirectionTests.WaterCharValues_MatchRarityAnchors)。
            // ⚠ 2026-09-11(档位统一 T3):冰 治疗 146 → 99,而阈值是 maxHp/5 = 100 ——
            // **单发已经跨不过一层**,原来那个「3 层整除、余数 0」的夹具会让两种顺序
            // 算出同一个数(层数都停在 3),这条测试当场退化成永远绿。
            // 修法是给夹具留一个**快要跨线的余数**:先攒 3 层 + 余 90(390 = 100×3 + 90),
            //   正确顺序:amplified = AmplifyByWellspring(99) 用旧层数 3、泉 5%/层 →
            //             99 × 115 / 100 = 113(floor)
            //   反转顺序:先 GainWellspring(99) 余数 90+99=189 跨线,层数变 4,
            //             再用新层数 4 算 amplified → 99 × 120 / 100 = 118(floor)
            // 113 ≠ 118,反转时这条断言必须变红。
            //
            // **2026-09-11(阈值 /5 → /7)**:阈值 100 → 71,上面那个「3 层 + 余 90」的夹具
            // 直接失效(390 / 71 = 5 层)。重挑:71×3 = 213 → 层数 3、余数 0。
            // 余数不再需要预留 —— 冰 的一发治疗 99 本身就 ≥ 71,单发必跨线,这正是
            // 本次阈值回调的意思。两种顺序仍给出**不同**的数:
            //   正确顺序:AmplifyByWellspring(99) 用旧层数 3 → 99 × 115 / 100 = 113(floor)
            //   反转顺序:先 GainWellspring(99) → 0 + 99 跨线,层数 4、余 28,
            //             再放大 → 99 × 120 / 100 = 118(floor)
            // 113 ≠ 118 —— 下面「反转顺序对照」那三行用同样的钩子把 118 实算了一遍,
            // 夹具没有退化成「两种顺序同值」的永远绿。
            var battle = NewBattleWithCharTakingDamage("冰", maxHp: 500, playerAttack: 100, enemyAttack: 450);
            battle.EndTurn();   // 敌人打一记,必中:500 - 450 = 50,留够 118 的回血空间不封顶
            Assert.That(battle.PlayerHp, Is.EqualTo(50), "夹具前提:留出的回血空间要盖过两种顺序的差值");
            battle.GainWellspringForTest(71 * 3);        // 先有 3 层(非零非满),余数 0
            Assert.That(battle.WellspringStacks, Is.EqualTo(3), "夹具前提:层数刚好 3");
            Assert.That(battle.HealAccum, Is.EqualTo(0), "夹具前提:余数 0,这一发 99 自己就跨得过 71");

            int before = battle.PlayerHp;
            battle.Cast("冰", 0);
            Assert.That(battle.PlayerHp - before, Is.EqualTo(113),
                "放大值必须用施放前(旧)的层数算,不能用 GainWellspring 攒完之后的新层数");
            Assert.That(battle.WellspringStacks, Is.EqualTo(4),
                "夹具有效性:这一发确实跨过了一层,两种顺序才会读到不同的层数");

            // 反转顺序对照:同样的起点,先攒(层数 3 → 4)再放大,得 118 ≠ 113。
            // 这三行是**防夹具退化的哨兵** —— 若哪天数值再变到两种顺序同值,它会先红。
            var reversed = NewBattleWithChar("冰", maxHp: 500, playerAttack: 100);
            reversed.GainWellspringForTest(71 * 3);
            reversed.GainWellspringForTest(99);          // 先攒:0 + 99 跨线 → 4 层
            Assert.That(reversed.AmplifyByWellspringForTest(99), Is.EqualTo(118),
                "反转顺序会算出 118;与上面的 113 不同,夹具区分得开两种顺序");
        }

        // ---- 快照往返(2026-09-02,Task 3)----

        [Test]
        public void Snapshot_RoundTrip_PreservesStacksAndRemainder()
        {
            // 2026-09-05 阈值 /10 → /5:阈值 50 → 100。
            // 130 / 100 = 1 层 + 余 30(原为 2 层 + 余 30,余数巧合相同:130 − 2×50 = 130 − 1×100 = 30)。
            // 70 / 100 = 0 层 + 余 70(原为 1 层 + 余 20)。
            // **2026-09-11(阈值 /5 → /7)**:阈值 100 → 71。这条守的是「有层数时余数也要
            // 原样带过存档」,所以保住「1 层 + 余 30」这个形状,输入从 130 改成 101
            // (= 71 + 30);70 仍 < 71,零层那一半的构造不用动。
            var battle = NewBattle(maxHp: 500);
            battle.GainHeftForTest(101);      // 1 层 + 余 30
            battle.GainWellspringForTest(70);     // 0 层 + 余 70(70 < 71)
            var snapshot = battle.Capture();
            var restored = NewBattleFromSnapshot(snapshot, maxHp: 500);

            Assert.That(restored.HeftStacks, Is.EqualTo(1));
            Assert.That(restored.ShieldAccum, Is.EqualTo(30), "余数漏存是静默的:续爬会丢半层");
            Assert.That(restored.WellspringStacks, Is.EqualTo(0));
            Assert.That(restored.HealAccum, Is.EqualTo(70));
        }

        [Test]
        public void CarriedStatuses_IncludeHeftAndWellspring()
        {
            // 护盾本来就整场爬塔延续(_shieldNormal),厚必须跟它同步,
            // 否则每场重新攒,而护盾还留着 —— 两者会一直对不上。
            var run = NewRunAfterWinningWithHeft();
            Assert.That(run.CarriedStatuses.Count(s => s.Kind == StatusKind.Heft), Is.EqualTo(1));
            Assert.That(run.CarriedStatuses.Count(s => s.Kind == StatusKind.Wellspring), Is.EqualTo(1));
        }

        // ---- 引爆:SpendHeft / SpendWellspring(2026-09-02,Task 4)----

        [Test]
        public void SpendHeft_DealsStacksTimesValueToAll_AndClearsStacks()
        {
            // 2026-09-05 阈值 /10 → /5:阈值 50 → 100,4 层份额从 50×4 改成 100×4
            // (本测试不断言具体层数,只断言有伤害与引爆后清零,原输入其实仍会通过,
            // 这里改是为了让注释里的「4 层」继续为真)。
            // **2026-09-11(阈值 /5 → /7)**:4 层份额改成 71×4,同理只为注释为真。
            var battle = NewSpendBattle("崩测", maxHp: 500);
            battle.GainHeftForTest(71 * 4);    // 4 层
            int hp0 = battle.Enemies[0].Hp, hp1 = battle.Enemies[1].Hp;

            battle.Cast("崩测", -1);

            // 4 层 × 60 = 240,过 ScaleByAttack(基准 100 → ×1)与相克
            Assert.That(hp0 - battle.Enemies[0].Hp, Is.GreaterThan(0));
            Assert.That(hp1 - battle.Enemies[1].Hp, Is.GreaterThan(0), "是全体效果");
            Assert.That(battle.HeftStacks, Is.EqualTo(0), "引爆清空全部层数");
        }

        [Test]
        public void SpendHeft_AtZeroStacks_IsNoOp_ButSiblingEffectsStillFire()
        {
            // 崩 = 攻击面(全体伤害 + 厚积薄发),2026-09-02 双方向重配后挂在 AttackEffects 上,
            // 需要 attackMode: true 才能打到(支持面/effects 现在是纯护盾)。
            // 0 层时 AOE 那一半仍该打出来 —— 「0 层就整张字拒出」会把 AOE 一起吞掉。
            var battle = NewBattleWithChar("崩", maxHp: 500);
            Assert.That(battle.HeftStacks, Is.EqualTo(0));
            int before = battle.Enemies[0].Hp;

            battle.Cast("崩", 0, attackMode: true);

            Assert.That(battle.Enemies[0].Hp, Is.LessThan(before), "AOE 那一半照常生效");
            Assert.That(battle.HeftStacks, Is.EqualTo(0));
        }

        [Test]
        public void SpendWellspring_DealsStacksTimesValueToAll_AndClearsStacks()
        {
            // 2026-09-05 阈值 /10 → /5:阈值 50 → 100,5 层份额从 50×5 改成 100×5
            // (本测试不断言具体层数,只断言引爆后清零,原输入其实仍会通过,
            // 这里改是为了让注释里的「5 层」继续为真)。
            // **2026-09-11(阈值 /5 → /7)**:5 层份额改成 71×5,同理只为注释为真。
            var battle = NewSpendBattle("发测", maxHp: 500);
            battle.GainWellspringForTest(71 * 5);    // 5 层
            battle.Cast("发测", -1);
            Assert.That(battle.WellspringStacks, Is.EqualTo(0));
        }

        [Test]
        public void SpendHeft_DoesNotNeedTarget()
        {
            // 全体效果,与全体驱散(淡)、全体引爆(炸)同处理
            var def = new CharDef("测", Element.Earth,
                effects: new[] { new EffectDef(EffectKind.SpendHeft, 60) });
            Assert.That(BattleEngine.NeedsTarget(def), Is.False);
        }

        // ---- 攒层阈值 MaxHp/10 → MaxHp/5(2026-09-05,平衡重做 P0 任务 1)----
        // ---- 再 → MaxHp/7(2026-09-11,档位差距与升级替代)----

        /// <summary>攒层阈值 = MaxHp/7(2026-09-11;2026-09-05 曾为 MaxHp/5,更早是 MaxHp/10)。
        ///
        /// MaxHp 500 → 阈值 71。给 141 点护盾应当只攒 1 层、余数 70;
        /// 按 /5 的旧阈值(100)余数会是 41、按 /6(83)会是 58 —— 余数那条断言就是
        /// 「阈值确实是七分之一」的判别式(层数三者都是 1,单看层数分不出来)。
        ///
        /// **2026-09-11(阈值 /5 → /7)**:改名去掉 Fifth。阈值是照**旧锚点**标定的,
        /// 而「档位差距与升级替代」那批把金档锚点整列 ×0.695,金系三张双方向字的
        /// 防御面(圭 83 / 冰 99 / 杜 80)全跌到旧阈值 100 以下,变成「单发 0 层」。
        /// 用户裁定「3 次圭该叠 3 层厚」即金档单发就该有 1 层;取 /7 不取 /6 是因为
        /// 最低的 杜 80 够不着 /6 的 83,而 5/7 = 0.714 ≈ 锚点的 ×0.695,同幅度回调。
        ///
        /// 141 这个输入与下面的 ResourceThreshold_ScalesWithMaxHp 共用:同一个数在
        /// MaxHp 500 下攒 1 层、在 MaxHp 1000 下攒 0 层,两条合起来钉住「跟着 MaxHp 走」。</summary>
        [Test]
        public void ResourceThreshold_IsOneSeventhOfMaxHp()
        {
            var battle = NewBattle(500);
            battle.GainHeftForTest(141);
            Assert.That(battle.HeftStacks, Is.EqualTo(1), "141 / (500/7 = 71) = 1 层(差 1 点不到 2 层)");
            Assert.That(battle.ShieldAccum, Is.EqualTo(70), "余数 141 − 71;按 /5 会是 41、按 /6 会是 58");
        }

        /// <summary>阈值随 MaxHp 走,不是写死的常量。MaxHp 1000 → 阈值 142。
        ///
        /// **2026-09-11(阈值 /5 → /7)**:构造从「199 对 1000/5 = 200 差 1 点」改成
        /// 「141 对 1000/7 = 142 差 1 点」,守的不变量原样:刚好差一点就是不给层数。</summary>
        [Test]
        public void ResourceThreshold_ScalesWithMaxHp()
        {
            var battle = NewBattle(1000);
            battle.GainWellspringForTest(141);
            Assert.That(battle.WellspringStacks, Is.EqualTo(0),
                "141 < 1000/7 = 142,差 1 点攒不满一层 —— 同一个 141 在 MaxHp 500 下是 1 层");
            Assert.That(battle.HealAccum, Is.EqualTo(141));
        }

        // ---- 泉倍率 10%/层 → 5%/层(2026-09-05,平衡重做 P0 任务 2)----

        /// <summary>泉每层 +5% 治疗(2026-09-05,原 +10%)——与厚的 +5% 攻击对齐。
        ///
        /// 引擎注释原先声称两者「同顶」(泉 10×10 = 厚 5×10 = +50%),但那忽略了
        /// **泉充得比厚快一倍**(治疗量普遍高于护盾量)。两条都拉到 5% 才真对齐。
        ///
        /// MaxHp 500 → 阈值 100。攒 400 治疗 = 4 层 → 下一次治疗 ×1.20。
        ///
        /// **2026-09-11(阈值 /5 → /7)**:阈值 100 → 71,400 会攒出 5 层。这条守的是
        /// 「每层 +5%」这个系数,层数只是载体 —— 输入改成 71×4 = 284 把层数钉回 4,
        /// 断言 120 原样不动(改成 5 层 / 125 会把系数与层数两件事搅在一起)。</summary>
        [Test]
        public void Wellspring_AmplifiesFivePercentPerStack()
        {
            var battle = NewBattle(500);
            battle.GainWellspringForTest(71 * 4);
            Assert.That(battle.WellspringStacks, Is.EqualTo(4));
            Assert.That(battle.AmplifyByWellspringForTest(100), Is.EqualTo(120),
                "4 层 × 5% = +20% → 100 → 120(旧口径是 +40% → 140)");
        }

        /// <summary>0 层时恒等 —— 这条是恒等性硬线,别让系数改动破了它。</summary>
        [Test]
        public void Wellspring_ZeroStacks_IsIdentity()
        {
            var battle = NewBattle(500);
            Assert.That(battle.AmplifyByWellspringForTest(137), Is.EqualTo(137));
        }
    }
}
