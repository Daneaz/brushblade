using System.Collections.Generic;
using Brushblade.Core;
using Brushblade.Data;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>Boss 技能系统(蓄力预警制):spec 见
    /// docs/superpowers/specs/2026-07-28-boss-skills-design.md</summary>
    public class BossSkillTests
    {
        // 心属性 Boss:对木召唤物 KeMultiplier = 1.0,五行不干扰技能数值断言。
        // 两阶段各 100 血 → 总血 200、阈值 100(jitter=0),玩家打不动就不会换阶。
        private static EnemyDef SkillBoss(BossSkill skill) => new("试炼", Element.Heart, 100, 5,
            phases: new[]
            {
                new BossPhaseDef("甲", Element.Heart, 100, 5, skill: skill),
                new BossPhaseDef("乙", Element.Heart, 100, 5),
            });

        // 首阶段仅 15 血:总血 115、阈值 100(115−15),两发「火」即可推过 —— 专供换阶取消测试。
        // 次阶段技能为 None:换阶后下个敌方回合必是普攻,便于断言"大招没放出来"。
        private static EnemyDef ThinFirstPhaseBoss() => new("薄甲", Element.Heart, 15, 5,
            phases: new[]
            {
                new BossPhaseDef("甲", Element.Heart, 15, 5, skill: BossSkill.Deluge),
                new BossPhaseDef("乙", Element.Heart, 100, 5),
            });

        private static RecipeGraph Graph() => new(new[]
        {
            new CharDef("火", Element.Fire,
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 10) }),
            new CharDef("林", Element.Wood,
                effects: new[] { new EffectDef(EffectKind.Summon, 6, summonCount: 2, summonAttack: 2, summonChar: "木") }),
            new CharDef("盾", Element.Earth,
                effects: new[] { new EffectDef(EffectKind.Shield, 20) }),
            // 护甲 2 点(测试本地值,真实字表是 12):这里的 Boss 攻击力只有 5,
            // 拿 12 会把普攻直接归零,断言看不出「大招也吃护甲」这件事
            new CharDef("铠", Element.Metal,
                effects: new[] { new EffectDef(EffectKind.DefenseBuff, 2) }),
        });

        private static BattleEngine Engine(BossSkill skill) =>
            new(Graph(), new BattleConfig { BossPhaseJitterPercent = 0 },
                new string[0], new[] { "火", "林", "盾", "火", "林", "盾" },
                new[] { SkillBoss(skill) }, seed: 1);

        /// <summary>推进 n 个敌方回合。</summary>
        private static void EndTurns(BattleEngine engine, int n)
        {
            for (int i = 0; i < n; i++) engine.EndTurn();
        }

        [Test]
        public void ChargeCycle_NormalAttack_ThenSilentChargeTurn_ThenCast()
        {
            var engine = Engine(BossSkill.Deluge);
            int full = engine.PlayerHp;

            engine.EndTurn(); // 敌方回合 1:普攻(ChargeCounter 0→1,未达 BossChargeEvery=2)
            Assert.That(engine.PlayerHp, Is.EqualTo(full - 5));
            Assert.That(engine.Enemies[0].ChargeCounter, Is.EqualTo(1));
            Assert.That(engine.Enemies[0].IsCharging, Is.False);

            engine.EndTurn(); // 敌方回合 2:计数达标,进入蓄力,本回合不出手
            Assert.That(engine.PlayerHp, Is.EqualTo(full - 5), "蓄力回合 Boss 不出手");
            Assert.That(engine.Enemies[0].IsCharging, Is.True);
            Assert.That(engine.Enemies[0].ChargingSkill, Is.EqualTo(BossSkill.Deluge), "蓄力时锁定技能");

            engine.EndTurn(); // 敌方回合 3:释放淹没(玩家份 Attack×2)
            Assert.That(engine.PlayerHp, Is.EqualTo(full - 15));
            Assert.That(engine.Enemies[0].IsCharging, Is.False);
            Assert.That(engine.Enemies[0].ChargeCounter, Is.EqualTo(0), "释放后计数归零");
        }

        [Test]
        public void Deluge_HitsPlayerAndEverySummon()
        {
            // 蓄力前才召唤:否则前两回合的普攻会先把最前一只磨死,淹没就打不到两只了
            var engine = Engine(BossSkill.Deluge);
            engine.EndTurn();   // 敌方回合 1:普攻(此时场上无召唤物,伤害落在玩家身上)
            engine.Cast("林");   // 2 只 6 血木召唤
            Assert.That(engine.Summons.Count, Is.EqualTo(2));
            int full = engine.PlayerHp;

            engine.EndTurn(); // 敌方回合 2:蓄力,不出手
            Assert.That(engine.PlayerHp, Is.EqualTo(full), "蓄力回合 Boss 不出手");

            engine.EndTurn(); // 敌方回合 3:释放淹没

            // 玩家份 Attack×2(F3 修正):蓄力回合空转不出手,靠释放回合顶两个普攻的量,
            // 3 回合循环投放 1+0+2=3×Attack,与无技能 Boss 的 3×Attack 持平。
            Assert.That(engine.PlayerHp, Is.EqualTo(full - 10), "大招不被召唤物拦截,玩家份 Attack×2");
            foreach (var summon in engine.Summons)
                Assert.That(summon.Hp, Is.EqualTo(1)); // 6 血挨 5(心对木 ×1.0,召唤物份不翻倍)
        }

        [Test]
        public void ChargeCounter_ResetsAfterCast()
        {
            var engine = Engine(BossSkill.Deluge);
            EndTurns(engine, 3); // 普攻 + 蓄力 + 释放

            Assert.That(engine.Enemies[0].IsCharging, Is.False);
            Assert.That(engine.Enemies[0].ChargeCounter, Is.EqualTo(0));
        }

        [Test]
        public void PhaseDef_CarriesSkill_DefaultsToNone()
        {
            var withSkill = new BossPhaseDef("海", Element.Water, 16, 10, skill: BossSkill.Deluge);
            var without = new BossPhaseDef("干", Element.Wood, 12, 6);

            Assert.That(withSkill.Skill, Is.EqualTo(BossSkill.Deluge));
            Assert.That(without.Skill, Is.EqualTo(BossSkill.None));
        }

        [Test]
        public void Scale_PreservesSkill()
        {
            var boss = new EnemyDef("试炼", Element.Water, 12, 6, phases: new[]
            {
                new BossPhaseDef("排", Element.Metal, 12, 6, skill: BossSkill.Topple),
                new BossPhaseDef("海", Element.Water, 16, 10, skill: BossSkill.Deluge),
            });

            var scaled = CampaignConfig.Scale(boss, 2f);

            Assert.That(scaled.Phases[0].Skill, Is.EqualTo(BossSkill.Topple));
            Assert.That(scaled.Phases[1].Skill, Is.EqualTo(BossSkill.Deluge));
            Assert.That(scaled.Phases[1].MaxHp, Is.EqualTo(32)); // 数值照常缩放
        }

        [Test]
        public void BulwarkPhase_NeverCasts_ButKeepsCounting()
        {
            // 坚壁没有大招可放,所以永不进入蓄力;但照常攒数(2026-07-29)——
            // 冻结的话,最耗回合的坚壁段(承伤 0.5)会把整场的蓄力节奏吃掉。
            var engine = Engine(BossSkill.Bulwark);
            int full = engine.PlayerHp;

            EndTurns(engine, 4);

            Assert.That(engine.Enemies[0].IsCharging, Is.False, "坚壁阶段永不蓄力");
            Assert.That(engine.Enemies[0].ChargeCounter, Is.EqualTo(4), "但计数照常累加");
            Assert.That(engine.PlayerHp, Is.EqualTo(full - 20), "四回合各普攻一次,一回合不落");
        }

        [Test]
        public void NoSkillPhase_NeverCasts_ButKeepsCounting()
        {
            var engine = Engine(BossSkill.None);
            int full = engine.PlayerHp;

            EndTurns(engine, 4);

            Assert.That(engine.Enemies[0].IsCharging, Is.False);
            Assert.That(engine.Enemies[0].ChargeCounter, Is.EqualTo(4));
            Assert.That(engine.PlayerHp, Is.EqualTo(full - 20));
        }

        [Test]
        public void PhaseCross_DuringCharge_StillCastsLockedSkill()
        {
            // 2026-07-29:「抢血取消大招」的支点机制已拆除(它依赖玩家算准带 ±8% 浮动的阈值,
            // 实际做不到,只是个偏向"打断"的随机器,导致技能整场放不出来)。
            // 现在换阶不打断蓄力,且释放的是蓄力时锁定的技能——哪怕当前阶段的字已经换了。
            var engine = new BattleEngine(Graph(),
                new BattleConfig { BossPhaseJitterPercent = 0 },
                new string[0], new[] { "火", "林", "盾", "火", "林", "盾" },
                new[] { ThinFirstPhaseBoss() }, seed: 1);
            var boss = engine.Enemies[0];
            Assert.That(boss.Hp, Is.EqualTo(115)); // 15 + 100,阈值 100

            engine.Cast("火", 0); // 火 vs 心 ×1.0 = 10 → 105,仍在首阶段
            Assert.That(boss.PhaseIndex, Is.EqualTo(0));

            EndTurns(engine, 2); // 敌方两回合:普攻、蓄力
            Assert.That(boss.IsCharging, Is.True);
            Assert.That(boss.ChargingSkill, Is.EqualTo(BossSkill.Deluge), "锁定首阶段「甲」的淹没");

            engine.Cast("火", 0); // 105 → 95 ≤ 100 → 换阶到次阶段「乙」(技能为 None)

            Assert.That(boss.PhaseIndex, Is.EqualTo(1));
            Assert.That(boss.IsCharging, Is.True, "换阶不打断蓄力");
            Assert.That(boss.ChargingSkill, Is.EqualTo(BossSkill.Deluge), "锁定的技能不被改写");

            int full = engine.PlayerHp;
            engine.EndTurn(); // 释放:放的是锁定的淹没,不是当前阶段的 None
            Assert.That(engine.PlayerHp, Is.EqualTo(full - 10), "预告什么就放什么(玩家份 Attack×2)");
        }

        // 两阶段都配 Deluge、首阶段极薄(15 血),用于验证换阶不打断计数接力。
        // 与 ThinFirstPhaseBoss 的区别只在次阶段也有技能——上者次阶段留 None,专测「释放锁定的技能」。
        private static EnemyDef ThinFirstPhaseBossBothSkilled() => new("薄甲乙", Element.Heart, 15, 5,
            phases: new[]
            {
                new BossPhaseDef("甲", Element.Heart, 15, 5, skill: BossSkill.Deluge),
                new BossPhaseDef("乙", Element.Heart, 100, 5, skill: BossSkill.Deluge),
            });

        [Test]
        public void PhaseCross_WhileCounting_PreservesChargeCounter()
        {
            // 换阶完全不影响蓄力计数(2026-07-29)。这是技能能否放出来的命门:
            // 排山倒海式的薄阶段 Boss(18/23/18/24 血)一两回合就能打穿一个阶段,
            // 只要换阶清零,ChargeCounter 就永远攒不满——实测阶段血量抬到 4 倍、
            // DPS30 依然一次大招都放不出。
            var engine = new BattleEngine(Graph(),
                new BattleConfig { BossPhaseJitterPercent = 0 },
                new string[0], new[] { "火", "林", "盾", "火", "林", "盾" },
                new[] { ThinFirstPhaseBossBothSkilled() }, seed: 1);
            var boss = engine.Enemies[0];
            Assert.That(boss.Hp, Is.EqualTo(115)); // 15 + 100,阈值 100(115 − 15)

            engine.EndTurn(); // 敌方回合 1:普攻,ChargeCounter 0 → 1
            Assert.That(boss.ChargeCounter, Is.EqualTo(1));
            Assert.That(boss.IsCharging, Is.False);
            Assert.That(boss.PhaseIndex, Is.EqualTo(0));

            engine.Cast("火", 0); // 115 → 105,仍在首阶段
            engine.Cast("火", 0); // 105 → 95 ≤ 100 → 换阶

            Assert.That(boss.PhaseIndex, Is.EqualTo(1), "跨过阈值,换阶");
            Assert.That(boss.ChargeCounter, Is.EqualTo(1), "换阶不清零,计数接力");

            engine.EndTurn(); // 敌方回合 2:ChargeCounter 1 → 2 ≥ ChargeEvery,进入蓄力
            Assert.That(boss.IsCharging, Is.True);

            int full = engine.PlayerHp;
            engine.EndTurn(); // 敌方回合 3:释放
            Assert.That(engine.PlayerHp, Is.EqualTo(full - 10), "接力攒够后正常释放,玩家份 Attack×2");
            Assert.That(boss.IsCharging, Is.False);
            Assert.That(boss.ChargeCounter, Is.EqualTo(0));
        }

        // ---- 减伤同口径吃大招(2026-08-03):不是只挡普攻 ----

        [Test]
        public void Deluge_AppliesPlayerDefense() // 大招也吃护甲,不是只挡普攻
        {
            var engine = new BattleEngine(Graph(), new BattleConfig { BossPhaseJitterPercent = 0 },
                new[] { "铠" }, new[] { "火", "林", "盾", "火", "林", "盾" },
                new[] { SkillBoss(BossSkill.Deluge) }, seed: 1);
            engine.Cast("铠"); // 护甲 +2
            int full = engine.PlayerHp;

            engine.EndTurn(); // 敌方回合 1:普攻 5 → 5 − 2 = 3
            Assert.That(full - engine.PlayerHp, Is.EqualTo(3), "普攻基线未被破坏");

            engine.EndTurn(); // 敌方回合 2:蓄力,不出手
            engine.EndTurn(); // 敌方回合 3:释放淹没,玩家份 Attack×2=10 → 10 − 2 = 8

            Assert.That(full - engine.PlayerHp, Is.EqualTo(3 + 8),
                "大招也吃护甲:玩家份 10 减 2 点,不是全额 10");
        }

        /// <summary>召唤物**不借用玩家的护甲**(spec §4.2:召唤物没有 DEF)。
        /// ⚠ 语义变化(2026-08-12,E-b4 T3):旧的乘法减伤是「玩家受伤 −X%」,套在 Boss 大招的
        /// **整条伤害**上,连打进召唤物的那一下也一起打折;点数护甲是「玩家这层皮多厚」,
        /// 挡不到召唤物身上。这条测试从此守的是**负向**口径 —— 贯穿打进召唤物那一下是全额。</summary>
        [Test]
        public void Pierce_SummonHit_DoesNotUsePlayerDefense()
        {
            var engine = new BattleEngine(Graph(), new BattleConfig { BossPhaseJitterPercent = 0 },
                new[] { "铠", "林" }, new[] { "火", "盾", "火", "盾" },
                new[] { SkillBoss(BossSkill.Pierce) }, seed: 1);
            engine.Cast("铠");   // 护甲 +2(只保护玩家)
            engine.EndTurn();    // 敌方回合 1:普攻(此时无召唤物,落在玩家身上)
            engine.Cast("林");   // 2 只 6 血木召唤

            EndTurns(engine, 2); // 蓄力 + 释放贯穿

            Assert.That(engine.Summons[0].Hp, Is.EqualTo(1), "召唤物挨全额 5,不吃玩家的 2 点护甲:6−5=1");
        }

        [Test]
        public void Pierce_HitsFrontSummonAndPlayerDouble()
        {
            var engine = Engine(BossSkill.Pierce);
            engine.EndTurn();   // 先走掉普攻回合,免得把最前一只磨死
            engine.Cast("林");   // 2 只 6 血
            int full = engine.PlayerHp;

            EndTurns(engine, 2); // 蓄力 + 释放

            Assert.That(engine.PlayerHp, Is.EqualTo(full - 10), "玩家挨双倍且不被拦截");
            Assert.That(engine.Summons[0].Hp, Is.EqualTo(1), "最前一只被穿:6 − 5");
            Assert.That(engine.Summons[1].Hp, Is.EqualTo(6), "只穿一条线,第二只不受伤");
        }

        [Test]
        public void Pierce_WithoutSummons_StillHitsPlayerDouble()
        {
            var engine = Engine(BossSkill.Pierce);
            int full = engine.PlayerHp;

            EndTurns(engine, 4);

            Assert.That(engine.PlayerHp, Is.EqualTo(full - 20)); // 普攻 5+5 + 贯穿 10
        }

        [Test]
        public void Topple_ClearsAllShieldAndCutsNextTurnAp()
        {
            var engine = Engine(BossSkill.Topple);
            engine.Cast("盾"); // 土系护盾 20
            Assert.That(engine.PlayerShield, Is.EqualTo(20));
            int full = engine.PlayerHp;

            EndTurns(engine, 3); // 普攻(吃 5 点盾,盾 20→15)+ 蓄力 + 倾覆(伤害 Attack×2=10)

            // 结算顺序探针:倾覆先吸伤再清盾——伤害 10(Attack×2)应被剩余 15 点盾吸收,HP 不掉。
            // 若实现被写反(先清盾再结算伤害),盾会先归零,这 10 点伤害直接打进 HP,PlayerHp 会少 10。
            // 光看 PlayerShield 归零不足以区分两种顺序(两种顺序下盾都会清零),这条断言专门锁顺序。
            Assert.That(engine.PlayerHp, Is.EqualTo(full), "倾覆伤害应被吸盾挡下,验证先吸伤再清盾的结算顺序");
            Assert.That(engine.PlayerShield, Is.EqualTo(0), "剩余护盾被清空");
            Assert.That(engine.Ap, Is.EqualTo(2), "下回合 AP 由 3 降为 2");
        }

        // 2026-08-16 全分支终审 Important 1 修复后:玩家侧状态递减(TickPlayerStatuses)从
        // YieldTurn(拍尾)挪回 BeginPlayerTurn 尾部(结算之后、StartTurn 之前)。倾覆挂 Seal
        // 发生在敌方段(第 3 次 EndTurn 内),该次 EndTurn 走到下一次 BeginPlayerTurn 时就已经
        // 把 TurnsLeft 减到 1(仍非零 → 这一拍仍受罚,Ap 仍是 2);第 4 次 EndTurn 再走到
        // BeginPlayerTurn 时减到 0、移除,AP 当场回满——恰好只罚满一个玩家回合,恢复成
        // 本条测试名字本来要守的语义(曾短暂被误改成要多续一轮才解除,已修正)。
        [Test]
        public void ToppleApPenalty_LastsOneTurnOnly()
        {
            var engine = Engine(BossSkill.Topple);
            EndTurns(engine, 3); // 蓄力 + 普攻 + 倾覆:StartTurn 已扣 1 点 AP
            Assert.That(engine.Ap, Is.EqualTo(2));

            engine.EndTurn(); // 第 4 回合:Seal 已被这次 BeginPlayerTurn 减到 0 移除,惩罚解除
            Assert.That(engine.Ap, Is.EqualTo(3), "惩罚只吃一个玩家回合就解除");
        }

        [Test]
        public void ToppleApPenalty_NeverDropsBelowOne()
        {
            var engine = new BattleEngine(Graph(),
                new BattleConfig { BossPhaseJitterPercent = 0, ApPerTurn = 1 },
                new string[0], new[] { "火", "林", "盾", "火", "林", "盾" },
                new[] { SkillBoss(BossSkill.Topple) }, seed: 1);

            EndTurns(engine, 4);

            Assert.That(engine.Ap, Is.EqualTo(1), "AP 下限为 1,玩家至少能做一件事");
        }

        [Test]
        public void Topple_WithFullSummonField_StillHitsPlayer()
        {
            // spec 第 8 节漏测项:全程零召唤物的 Topple_ClearsAllShieldAndCutsNextTurnAp 从未
            // 断言过"绕前排路由"(spec 3.3 总则)。这里前排召满 4 只,验证倾覆的玩家份依旧
            // 直接命中、不被召唤物拦截,召唤物也毫发无伤(倾覆不打召唤物)。
            var engine = Engine(BossSkill.Topple);
            engine.EndTurn();   // 敌方普攻回合(此时场上无召唤物,伤害落在玩家身上)
            engine.Cast("林");
            engine.Cast("林");
            Assert.That(engine.AliveSummonCount, Is.EqualTo(4), "前排满员");
            int full = engine.PlayerHp;

            EndTurns(engine, 2); // 蓄力 + 倾覆释放

            Assert.That(engine.PlayerHp, Is.EqualTo(full - 10), "倾覆玩家份不被满场召唤物拦截");
            foreach (var summon in engine.Summons)
                Assert.That(summon.Hp, Is.EqualTo(6), "倾覆不打召唤物,满血不掉");
        }

        [Test]
        public void Devour_KillsFrontSummon_AndDoesNotHealBoss()
        {
            var engine = Engine(BossSkill.Devour);
            engine.EndTurn();   // 先走掉普攻回合,免得把最前一只磨死
            engine.Cast("林");   // 2 只 6 血,均满血
            int full = engine.PlayerHp;
            int bossHpBefore = engine.Enemies[0].Hp;

            EndTurns(engine, 2); // 蓄力 + 吞噬

            Assert.That(engine.Summons[0].Alive, Is.False, "最前一只被吞:满血 6 也照删");
            Assert.That(engine.Summons[1].Hp, Is.EqualTo(6), "第二只不受影响");
            Assert.That(engine.PlayerHp, Is.EqualTo(full), "吞噬不打玩家");
            // 召唤物每回合反击 2×2=4,Boss 只会掉血,绝不因吞噬回血
            Assert.That(engine.Enemies[0].Hp, Is.LessThan(bossHpBefore), "不回血");
        }

        [Test]
        public void Devour_WithoutSummons_PlainAttackNotDoubled()
        {
            var engine = Engine(BossSkill.Devour);
            int full = engine.PlayerHp;

            EndTurns(engine, 4);

            Assert.That(engine.PlayerHp, Is.EqualTo(full - 15), "普攻 5+5 + 吞噬空放的普攻 5(不翻倍)");
        }

        [Test]
        public void BossSkillCast_PrecedesTargetHitEvents()
        {
            // spec 6.4:BossSkillCast 必须先于各目标受击事件发出——表现层靠这个显式顺序把大招
            // 动效与后续伤害分开播;注释里记着靠事件种类猜边界已出过两次动画错乱,不重蹈。
            var engine = Engine(BossSkill.Deluge);
            engine.EndTurn();   // 敌方普攻回合,场上尚无召唤物
            engine.Cast("林");   // 2 只木召唤,充当淹没的额外受击目标
            engine.EndTurn();   // 蓄力回合(不出手,本回合无相关事件)

            engine.EndTurn(); // 释放淹没:应产出 BossSkillCast + 玩家/召唤物的受击事件

            int castIndex = -1;
            for (int i = 0; i < engine.LastEvents.Count; i++)
                if (engine.LastEvents[i].Kind == BattleEventKind.BossSkillCast) { castIndex = i; break; }
            Assert.That(castIndex, Is.GreaterThanOrEqualTo(0), "本回合应释放技能,产出 BossSkillCast");

            // 只看 EnemyAttack/SummonHit——这两种才是 CastBossSkill 自己产出的"目标受击"事件。
            // 不看 Damage:同一回合更早的召唤反击段(EndTurn 第 2 段)会先给 Boss 记一条 Damage,
            // 那是玩家召唤物打 Boss,跟本次技能释放无关,混进来会把顺序断言带偏。
            bool sawHitAfterCast = false;
            for (int i = 0; i < engine.LastEvents.Count; i++)
            {
                var kind = engine.LastEvents[i].Kind;
                bool isHit = kind == BattleEventKind.EnemyAttack || kind == BattleEventKind.SummonHit;
                if (!isHit) continue;
                Assert.That(i, Is.GreaterThan(castIndex), "受击事件必须晚于 BossSkillCast");
                sawHitAfterCast = true;
            }
            Assert.That(sawHitAfterCast, Is.True, "本用例应产生至少一条受击事件(玩家份 + 召唤物份)");
        }

        // ---- 断点续爬存档(spec 2026-07-28 6.1):蓄力状态不能被读档白嫖取消 ----

        private static IReadOnlyDictionary<string, EnemyDef> Defs(params EnemyDef[] defs)
        {
            var map = new Dictionary<string, EnemyDef>();
            foreach (var d in defs) map[d.Id] = d;
            return map;
        }

        [Test]
        public void Snapshot_RoundTrips_ChargeState()
        {
            var engine = Engine(BossSkill.Deluge);
            EndTurns(engine, 2); // 普攻 + 蓄力,停在蓄力中
            Assert.That(engine.Enemies[0].IsCharging, Is.True);

            var restored = BattleEngine.Restore(engine.Capture(), Graph(),
                new BattleConfig { BossPhaseJitterPercent = 0 },
                null, Defs(SkillBoss(BossSkill.Deluge)));

            Assert.That(restored.Enemies[0].IsCharging, Is.True, "读档不能白嫖取消大招");
            Assert.That(restored.Enemies[0].ChargeCounter, Is.EqualTo(2));
            Assert.That(restored.Enemies[0].ChargingSkill, Is.EqualTo(BossSkill.Deluge), "锁定的技能也要存下来");

            int full = restored.PlayerHp;
            restored.EndTurn();
            Assert.That(restored.PlayerHp, Is.EqualTo(full - 10), "续爬后照常释放,玩家份 Attack×2(F3 修正)");
        }

        [Test]
        public void Snapshot_RoundTrips_ReducedAp()
        {
            var engine = Engine(BossSkill.Topple);
            EndTurns(engine, 3); // 倾覆已生效,当前回合 AP = 2
            Assert.That(engine.Ap, Is.EqualTo(2));

            var restored = BattleEngine.Restore(engine.Capture(), Graph(),
                new BattleConfig { BossPhaseJitterPercent = 0 },
                null, Defs(SkillBoss(BossSkill.Topple)));

            Assert.That(restored.Ap, Is.EqualTo(2), "被削过的 AP 走既有 BattleSnapshot.Ap 存取");
        }

        // ---- 字 → 技能表(spec 2026-07-28):BuildIdiomBoss 逐字取技能 ----

        [Test]
        public void BuildIdiomBoss_UsesPerCharSkills()
        {
            var idiom = new IdiomBossDef
            {
                Chars = "刀山火海",
                Elements = new[] { Element.Metal, Element.Earth, Element.Fire, Element.Water },
                Skills = new[] { BossSkill.Pierce, BossSkill.Bulwark, BossSkill.Devour, BossSkill.Deluge },
            };

            var boss = EndlessGenerator.BuildIdiomBoss(idiom);

            Assert.That(boss.Phases[0].Skill, Is.EqualTo(BossSkill.Pierce));
            Assert.That(boss.Phases[1].Skill, Is.EqualTo(BossSkill.Bulwark));
            Assert.That(boss.Phases[3].Skill, Is.EqualTo(BossSkill.Deluge));
        }

        [Test]
        public void BuildIdiomBoss_WithoutSkills_FallsBackToNone()
        {
            var idiom = new IdiomBossDef
            {
                Chars = "刀山火海",
                Elements = new[] { Element.Metal, Element.Earth, Element.Fire, Element.Water },
            };

            var boss = EndlessGenerator.BuildIdiomBoss(idiom);

            foreach (var phase in boss.Phases)
                Assert.That(phase.Skill, Is.EqualTo(BossSkill.None));
        }

        /// <summary>最小合法战役 JSON:一只三阶段 Boss + 字表。
        /// 「排」「山」走字表,「槑」故意不在表里 → 应 fallback 到 None。</summary>
        private static string CampaignJson(string phaseSkillField = "") => @"
{
  ""enemies"": [
    { ""id"": ""试炼"", ""element"": ""Water"", ""maxHp"": 12, ""attack"": 6,
      ""phases"": [
        { ""char"": ""排"", ""element"": ""Metal"", ""maxHp"": 12, ""attack"": 6" + phaseSkillField + @" },
        { ""char"": ""山"", ""element"": ""Earth"", ""maxHp"": 15, ""attack"": 4, ""defense"": 60 },
        { ""char"": ""槑"", ""element"": ""Wood"", ""maxHp"": 12, ""attack"": 8 }
      ] }
  ],
  ""dropTable"": [""火""],
  ""bossSkills"": { ""排"": ""Topple"", ""山"": ""Bulwark"" },
  ""chapters"": [
    { ""name"": ""测试章"", ""bossPool"": [""试炼""],
      ""stages"": [ { ""encounters"": [[""试炼""]], ""boss"": true } ] }
  ]
}";

        [Test]
        public void LoadCampaign_ResolvesBossSkillsFromCharTable()
        {
            var config = ConfigLoader.LoadCampaign(CampaignJson(), Graph());
            var boss = config.Chapters[0].BossPool[0];

            Assert.That(boss.Phases[0].Skill, Is.EqualTo(BossSkill.Topple));  // 「排」查表
            Assert.That(boss.Phases[1].Skill, Is.EqualTo(BossSkill.Bulwark)); // 「山」查表
        }

        [Test]
        public void LoadCampaign_UnknownCharInPhase_FallsBackToNone()
        {
            var config = ConfigLoader.LoadCampaign(CampaignJson(), Graph());
            var boss = config.Chapters[0].BossPool[0];

            Assert.That(boss.Phases[2].Skill, Is.EqualTo(BossSkill.None), "「槑」不在表里,不报错只留白");
        }

        [Test]
        public void LoadCampaign_ExplicitPhaseSkill_WinsOverTable()
        {
            // 「排」在字表里是 Topple,phase.skill 显式写 Deluge 应当覆盖它
            var config = ConfigLoader.LoadCampaign(CampaignJson(@", ""skill"": ""Deluge"""), Graph());
            var boss = config.Chapters[0].BossPool[0];

            Assert.That(boss.Phases[0].Skill, Is.EqualTo(BossSkill.Deluge));
        }

        [Test]
        public void LoadCampaign_UnknownSkillName_Throws()
        {
            Assert.Throws<ConfigException>(() =>
                ConfigLoader.LoadCampaign(CampaignJson(@", ""skill"": ""不存在的技能"""), Graph()));
        }
    }
}
