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

        /// <summary>两场连战的爬塔配置(2026-09-11,累计三段验证用)。
        /// 奇遇池留空 → ProceedAfterBattle 不会岔进奇遇页,打赢后的相位是确定的 Reward。
        /// 两处都要用同一份配置重建(读档那条要把它再传给 RunEngine.Restore),所以做成方法。</summary>
        private static RunConfig TwoBattleConfig() => new()
        {
            Encounters = new[] { new[] { Dummy(hp: 20) }, new[] { Dummy(hp: 20) } },
            RewardPool = new[] { "甲" },
        };

        private static BattleConfig RunBattleConfig() =>
            new() { PlayerMaxHp = 500, PlayerAttack = 100 };

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
            // **2026-09-11(阈值 /7 → 固定 100)**:同理回到 100,「1 层 + 余 0」继续为真。
            run.Battle.GainHeftForTest(100);    // 阈值 100 → 1 层 + 余 0
            run.Battle.GainWellspringForTest(100);
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
            // **2026-09-11(阈值 /7 → 固定 100)**:整除量随之换成 100。数字与 /5 时代同值
            // 纯属巧合(500/5 也是 100)—— 阈值已经与 maxHp 无关了,别把它读成「回到 /5」。
            var battle = NewBattle(maxHp: 500);
            battle.GainHeftForTest(100);
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
            // **2026-09-11(阈值 /7 → 固定 100)**:60 < 100 仍然满足,输入照旧不动;
            // 余数改成 120 − 100 = 20。
            var battle = NewBattle(maxHp: 500);
            battle.GainHeftForTest(60);
            battle.GainHeftForTest(60);   // 合计 120 = 1 层 + 余 20
            Assert.That(battle.HeftStacks, Is.EqualTo(1));
            Assert.That(battle.ShieldAccum, Is.EqualTo(20));
        }

        [Test]
        public void Heft_CapsAtTenStacks_AndStopsAccumulatingRemainder()
        {
            // 2026-09-05 阈值 /10 → /5:阈值 50 → 100,12 层份额从 50×12 改成 100×12。
            // **2026-09-11(阈值 /5 → /7)**:阈值 100 → 71,12 层份额改成 71×12
            // (100×12 仍会撞上限、断言不会红,改是为了让注释里的「12 层」继续为真)。
            // **2026-09-11(阈值 /7 → 固定 100)**:12 层份额改回 100×12,同上只为注释为真。
            var battle = NewBattle(maxHp: 500);
            battle.GainHeftForTest(100 * 12);   // 够 12 层
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
            // **2026-09-11(阈值 /7 → 固定 100)**:份额改回 100×10;系数(5%/层)一直没动。
            var battle = NewBattle(maxHp: 500);   // PlayerAttack = 100 基准
            int baseline = battle.EffectiveAttack;
            Assert.That(baseline, Is.EqualTo(100));
            battle.GainHeftForTest(100 * 10);  // 满 10 层
            Assert.That(battle.HeftStacks, Is.EqualTo(10));
            Assert.That(battle.EffectiveAttack, Is.EqualTo(150), "10 层 = +50%,与战意同顶");
        }

        [Test]
        public void Heal_UsesNominalValue_SoOverhealStillGainsWellspring()
        {
            // 这条是整套改动的核心诉求:满血时治疗一分不亏。
            // 2026-09-05 阈值 /10 → /5:阈值 50 → 100,100 名义治疗从「2 层」变成「1 层整除」。
            // **2026-09-11(阈值 /5 → /7)**:阈值 100 → 71,名义治疗改 71 保持「1 层整除」。
            // **2026-09-11(阈值 /7 → 固定 100)**:名义治疗改回 100,「1 层整除」照旧。
            var battle = NewBattle(maxHp: 500);   // 满血
            Assert.That(battle.PlayerHp, Is.EqualTo(500));
            battle.GainWellspringForTest(100);    // 名义治疗 100,实际回血 0
            Assert.That(battle.PlayerHp, Is.EqualTo(500), "满血不会超上限");
            Assert.That(battle.WellspringStacks, Is.EqualTo(1), "溢出的治疗照样攒泉");
        }

        [Test]
        public void Wellspring_CapsAtTenStacks()
        {
            // 2026-09-05 阈值 /10 → /5:阈值 50 → 100,12 层份额从 50×12 改成 100×12。
            // **2026-09-11(阈值 /5 → /7)**:12 层份额改成 71×12,同 Heft 那条——只为注释为真。
            // **2026-09-11(阈值 /7 → 固定 100)**:12 层份额改回 100×12,同理。
            var battle = NewBattle(maxHp: 500);
            battle.GainWellspringForTest(100 * 12);
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
            // **2026-09-11(阈值 /7 → 固定 100)**:阈值 71 → 100,方向**再反一次**——
            // 圭 的盾 83 < 100,单发回到「0 层 + 余 83」。用户拍固定 100 时接受的正是这条:
            // 金档单发不再保证一层,换来的是阈值不随等级/血量/技能漂移(见 BattleEngine
            // ResourceThresholdValue 的注释)。所以这里把重点从「单发够一层」挪到**累计**:
            // 三发 圭 = 249 = 100×2 + 49 → 2 层 + 余 49,一点都不丢。
            var battle = NewBattleWithChar("圭", maxHp: 500);
            battle.Cast("圭", -1);
            Assert.That(battle.HeftStacks, Is.EqualTo(0), "圭 的盾 83 < 阈值 100,单发攒不满一层");
            Assert.That(battle.ShieldAccum, Is.EqualTo(83), "83 全留成余数,下一发接着攒");
            // 出字即耗字,同一张字没法连出第二次,用测试钩子补上后两发的同等盾量。
            battle.GainHeftForTest(83);
            battle.GainHeftForTest(83);
            Assert.That(battle.HeftStacks, Is.EqualTo(2), "三发 圭 249 = 2 层(单发不足一层≠白打)");
            Assert.That(battle.ShieldAccum, Is.EqualTo(49), "249 − 100×2 = 49,余数继续留着");
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
            // **2026-09-11(阈值 /7 → 固定 100)**:阈值 71 → 100,与 Cast_ShieldChar_GainsHeft
            // 同批同因,方向再反一次 —— 99 < 100,单发回到「0 层 + 余 99」(只差 1 点)。
            // 重点同样落在**累计**上:三发 297 = 100×2 + 97 → 2 层 + 余 97。
            var battle = NewBattleWithChar("冰", maxHp: 500);
            battle.Cast("冰", 0);
            Assert.That(battle.WellspringStacks, Is.EqualTo(0), "冰 的治疗 99 < 阈值 100,单发差 1 点");
            Assert.That(battle.HealAccum, Is.EqualTo(99), "99 全留成余数");
            // 三发的累计(出字即耗字,用测试钩子补后两发的同等治疗量)
            battle.GainWellspringForTest(99);
            battle.GainWellspringForTest(99);
            Assert.That(battle.WellspringStacks, Is.EqualTo(2), "三发 冰 297 = 2 层");
            Assert.That(battle.HealAccum, Is.EqualTo(97), "297 − 100×2 = 97");
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
            // **2026-09-11(阈值 /7 → 固定 100)**:份额改回 100×10,仍是「满 10 层」。
            battle.GainHeftForTest(100 * 10);           // 满 10 层厚
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
            // **2026-09-11(阈值 /7 → 固定 100)**:maxHp 2000 **不再影响阈值**,它现在
            // 只负责「治疗不被满血上限吞掉」这一件事(夹具原意)。份额改成 100×10。
            battle.GainWellspringForTest(100 * 10);   // 阈值固定 100,满 10 层
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
            // **2026-09-11(阈值 /7 → 固定 100)**:99 < 100 → 0 层 + 余 99。
            int stacksFromZero = battleA.WellspringStacks;

            var battleB = NewBattleWithChar("冰", maxHp: 500, playerAttack: 100);
            // 3 层:3 + 0(stacksFromZero)= 3,不会撞上 10 层上限,不会把差值悄悄钳平,
            // 测得出「已有泉不该让这一发攒得更多」这条不变量。
            // 2026-09-05 阈值 /10 → /5:阈值 50 → 100,先攒 3 层的份额从 50×3 改成 100×3。
            // **2026-09-11(阈值 /5 → /7)**:份额改成 71×3(整除,余数 0,让「先有 3 层」为真)。
            // **2026-09-11(阈值 /7 → 固定 100)**:份额改回 100×3(整除,余数 0)。
            // ⚠ 余数必须是 0,否则 battleB 那一发是「余数 + 99」跨的线,与 battleA 的
            // 「0 + 99」不同源,层数差就不再只反映放大与否了。
            battleB.GainWellspringForTest(100 * 3);          // 先有 3 层,余数 0
            Assert.That(battleB.HealAccum, Is.EqualTo(0), "夹具前提:起点余数为 0,与 battleA 同源");
            int before = battleB.WellspringStacks;
            battleB.Cast("冰", 0);
            Assert.That(battleB.WellspringStacks - before, Is.EqualTo(stacksFromZero),
                "已有泉不该让这一发治疗攒得更多 —— 攒的基数与泉层数无关");
            // 判别式:若攒的基数走了放大后的值(3 层 → 99 × 115/100 = 113 ≥ 100),
            // 上面那句会读到 +1 层、这句会读到余数 13。两句一起钉,夹具退化不了。
            Assert.That(battleB.HealAccum, Is.EqualTo(99),
                "攒进去的是未放大的 99,不是 3 层泉放大后的 113");
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
            //
            // **2026-09-11(阈值 /7 → 固定 100)**:99 又跨不过阈值了,「余数 0」的夹具
            // 会让两种顺序同停在 3 层 —— 与 /7 之前那次退化一模一样。于是**把预留余数
            // 请回来**:100×3 + 90 = 390 → 3 层 + 余 90,90 + 99 = 189 必跨线。
            //   正确顺序:AmplifyByWellspring(99) 用旧层数 3 → 99 × 115 / 100 = 113(floor)
            //   反转顺序:先 GainWellspring(99) → 90 + 99 跨线,层数 4、余 89,
            //             再放大 → 99 × 120 / 100 = 118(floor)
            // 113 ≠ 118 照旧。(390 这个数与 /5 时代同值纯属巧合:那时是 maxHp/5 算出来的
            // 100,现在是写死的 100。)
            var battle = NewBattleWithCharTakingDamage("冰", maxHp: 500, playerAttack: 100, enemyAttack: 450);
            battle.EndTurn();   // 敌人打一记,必中:500 - 450 = 50,留够 118 的回血空间不封顶
            Assert.That(battle.PlayerHp, Is.EqualTo(50), "夹具前提:留出的回血空间要盖过两种顺序的差值");
            battle.GainWellspringForTest(100 * 3 + 90);  // 先有 3 层(非零非满)+ 余 90
            Assert.That(battle.WellspringStacks, Is.EqualTo(3), "夹具前提:层数刚好 3");
            Assert.That(battle.HealAccum, Is.EqualTo(90), "夹具前提:余数 90,这一发 99 必跨线");

            int before = battle.PlayerHp;
            battle.Cast("冰", 0);
            Assert.That(battle.PlayerHp - before, Is.EqualTo(113),
                "放大值必须用施放前(旧)的层数算,不能用 GainWellspring 攒完之后的新层数");
            Assert.That(battle.WellspringStacks, Is.EqualTo(4),
                "夹具有效性:这一发确实跨过了一层,两种顺序才会读到不同的层数");

            // 反转顺序对照:同样的起点,先攒(层数 3 → 4)再放大,得 118 ≠ 113。
            // 这三行是**防夹具退化的哨兵** —— 若哪天数值再变到两种顺序同值,它会先红。
            var reversed = NewBattleWithChar("冰", maxHp: 500, playerAttack: 100);
            reversed.GainWellspringForTest(100 * 3 + 90);
            reversed.GainWellspringForTest(99);          // 先攒:90 + 99 跨线 → 4 层
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
            // **2026-09-11(阈值 /7 → 固定 100)**:同一个形状,输入改成 130(= 100 + 30);
            // 70 仍 < 100,零层那一半照旧不动。
            var battle = NewBattle(maxHp: 500);
            battle.GainHeftForTest(130);          // 1 层 + 余 30
            battle.GainWellspringForTest(70);     // 0 层 + 余 70(70 < 100)
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
            // **2026-09-11(阈值 /7 → 固定 100)**:4 层份额改回 100×4,同理。
            var battle = NewSpendBattle("崩测", maxHp: 500);
            battle.GainHeftForTest(100 * 4);   // 4 层
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
            // **2026-09-11(阈值 /7 → 固定 100)**:5 层份额改回 100×5,同理。
            var battle = NewSpendBattle("发测", maxHp: 500);
            battle.GainWellspringForTest(100 * 5);   // 5 层
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
        // ---- 再 → **固定 100**(2026-09-11 用户拍板,阈值不再挂在 MaxHp 上)----

        /// <summary>攒层阈值 = **固定 100**(2026-09-11 用户拍板)。
        ///
        /// **2026-09-11(阈值 /7 → 固定 100)**:这条原名 ResourceThreshold_IsOneSeventhOfMaxHp,
        /// 守的是「阈值确实是 MaxHp 的七分之一」—— **那个前提整个作废了**,不是改个除数的事。
        /// 沿革 /10 → /5 → /7 三次都挂在 PlayerMaxHp 上,而那是结构性错配:被阈值 gate 的
        /// 盾/治疗走 ScaleByBaseAttack(值 × PlayerAttack / 100),MaxHpFor 26 级 ×2.0、
        /// AttackFor 26 级 ×1.5,阈值涨得比它卡的东西快一倍;养元技能 vigor 再累计
        /// +600 MaxHp 且只加血不加攻。固定值一次斩断这条耦合。
        ///
        /// 这条改钉常量本身:给 141 点护盾 = 1 层 + 余 41。余数是判别式 ——
        /// 按 /7(71)余数会是 70、按 /10(50)会直接攒出 2 层(层数单看分不出 /5 与固定 100,
        /// 因为 500/5 恰好也是 100,所以下面那条「不随 MaxHp 变」才是真正的哨兵)。</summary>
        [Test]
        public void ResourceThreshold_IsFixedHundred()
        {
            Assert.That(BattleEngine.ResourceThresholdValue, Is.EqualTo(100),
                "阈值是写死的公开常量,不再由任何量推导");
            var battle = NewBattle(500);
            battle.GainHeftForTest(141);
            Assert.That(battle.HeftStacks, Is.EqualTo(1), "141 / 100 = 1 层");
            Assert.That(battle.ShieldAccum, Is.EqualTo(41), "余数 141 − 100;按 /7 会是 70");
        }

        /// <summary>阈值**不随 MaxHp 变** —— 这是 2026-09-11 这次改动的核心保证。
        ///
        /// **2026-09-11(阈值 /7 → 固定 100)**:这条原名 ResourceThreshold_ScalesWithMaxHp,
        /// 断言的正好是反过来的一句。方向反转不是把旧断言取反了事,它是本次改动**唯一的
        /// 回归哨兵**:哪天有人把阈值改回 MaxHp/N(不论 N 取几),只有这条会红 ——
        /// 上面那条钉常量的会被「常量还在、只是没人读它」的改法绕过去。
        ///
        /// 所以断的不是某个具体数字,而是**同一个输入量在两种 MaxHp 下读出同一组层数与余数**。
        /// 141 在 500 与 2000 下都该是 1 层 + 余 41;按任何 MaxHp/N 的旧口径,
        /// 两者的阈值差 4 倍,这两组读数不可能相同(/7:71 → 1 层余 70,285 → 0 层余 141)。
        /// 层数与余数两句都断,是因为层数会被 10 层上限钳平成同值,余数才是细判别式。</summary>
        [Test]
        public void ResourceThreshold_DoesNotScaleWithMaxHp()
        {
            var small = NewBattle(500);
            var large = NewBattle(2000);
            small.GainWellspringForTest(141);
            large.GainWellspringForTest(141);

            Assert.That(large.WellspringStacks, Is.EqualTo(small.WellspringStacks),
                "同一个输入量,层数不该随 MaxHp 变");
            Assert.That(large.HealAccum, Is.EqualTo(small.HealAccum),
                "余数同理 —— 层数会被上限钳平,余数是更细的判别式");
            Assert.That(small.WellspringStacks, Is.EqualTo(1), "夹具有效性:141 确实跨过了一层");
            Assert.That(small.HealAccum, Is.EqualTo(41));
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
        /// 断言 120 原样不动(改成 5 层 / 125 会把系数与层数两件事搅在一起)。
        ///
        /// **2026-09-11(阈值 /7 → 固定 100)**:同理,输入改成 100×4 = 400 把层数钉回 4,
        /// 断言 120 仍然原样不动。注释里那句「MaxHp 500 → 阈值 100」现在应读作
        /// 「阈值恒为 100」—— MaxHp 已经不参与了。</summary>
        [Test]
        public void Wellspring_AmplifiesFivePercentPerStack()
        {
            var battle = NewBattle(500);
            battle.GainWellspringForTest(100 * 4);
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
