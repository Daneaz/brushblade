using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>ATB 时序归属(2026-08-15):每单位自结算的 DOT / 状态递减 / 立即结算。
    /// 规格见 docs/superpowers/specs/2026-08-15-ATB回合制改造-design.md §4.3。</summary>
    public class AtbTimingTests
    {
        private static RecipeGraph Graph() => new(new[]
        {
            new CharDef("木", Element.Wood),
        });

        private static BattleEngine Engine(EnemyDef[] enemies, BattleConfig config = null) =>
            new(Graph(), config ?? new BattleConfig { PlayerMaxHp = 999 },
                Array.Empty<string>(), Array.Empty<string>(), enemies, seed: 1);

        private static EnemyDef Dummy(string id = "靶", int hp = 999, int attack = 0, int speed = 0) =>
            new(id, Element.Heart, hp, attack, speed: speed);

        [Test]
        public void PlayerSpeed_DefaultsToBaseline()
        {
            var engine = Engine(new[] { Dummy() });

            Assert.That(engine.EffectivePlayerSpeed, Is.EqualTo(100));
        }

        [Test]
        public void PlayerSpeed_ReadsConfigAndSpeedModifier()
        {
            var engine = Engine(new[] { Dummy() },
                new BattleConfig { PlayerMaxHp = 999, PlayerSpeed = 150 });

            Assert.That(engine.EffectivePlayerSpeed, Is.EqualTo(150));
        }

        [Test]
        public void PlayerSpeed_IsClampedLikeEveryoneElse()
        {
            var engine = Engine(new[] { Dummy() },
                new BattleConfig { PlayerMaxHp = 999, PlayerSpeed = 9999 });

            Assert.That(engine.EffectivePlayerSpeed, Is.EqualTo(TurnScheduler.MaxSpeed));
        }

        [Test]
        public void PlayerActionMeter_NeverGoesNegative()
        {
            // 玩家计量器与场上所有单位同口径从 0 起步,不需要任何先手/负债/懒消费之类的特例
            // (2026-08-15 第五次审查订正:前四轮试过的这些记账手法全是在给反向的 tie-break
            // 打补丁——玩家排最先会让它每次推进都抢在敌人前面收回行动权。把 BuildSlots 的
            // 优先级方向调成「玩家排最后」之后,恒非负这条不变式自然成立,不必再靠任何机制
            // 保证。这条测试仍然保留,守住"永不为负"这个不变式)。
            var engine = Engine(new[] { Dummy() });

            Assert.That(engine.PlayerActionMeter, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void EndTurn_IsAWrapperOverAdvanceOnce()
        {
            // 同速基准局:一次 EndTurn 应当恰好走完「召唤物 → 敌人 → 回到玩家」
            var engine = Engine(new[] { Dummy(attack: 10) });

            engine.EndTurn();

            Assert.That(engine.Phase, Is.EqualTo(BattlePhase.PlayerTurn));
            Assert.That(engine.Turn, Is.EqualTo(2), "回到玩家 = 新一拍开始");
        }

        [Test]
        public void Forecast_StartsWithTheEnemyWhenPlayerHasYielded()
        {
            var engine = Engine(new[] { Dummy(attack: 10) });

            engine.YieldTurn();
            var forecast = engine.Forecast(3);

            Assert.That(forecast[0].Kind, Is.EqualTo(ActorKind.Enemy));
        }

        [Test]
        public void AdvanceOnce_ReturnsFalseWhenPlayersTurnComesUp()
        {
            var engine = Engine(new[] { Dummy(attack: 10) });

            engine.YieldTurn();
            bool more = engine.AdvanceOnce();   // 敌人这一拍
            Assert.That(more, Is.True);
            Assert.That(engine.LastActor.Kind, Is.EqualTo(ActorKind.Enemy));

            more = engine.AdvanceOnce();        // 轮到玩家 → 停
            Assert.That(more, Is.False);
            Assert.That(engine.Phase, Is.EqualTo(BattlePhase.PlayerTurn));
        }

        [Test]
        public void FastPlayer_GetsTwoTurnsPerEnemyTurn()
        {
            var engine = Engine(new[] { Dummy(attack: 10) },
                new BattleConfig { PlayerMaxHp = 999, PlayerSpeed = 200 });

            engine.YieldTurn();
            var forecast = engine.Forecast(6);

            Assert.That(forecast.Count(a => a.Kind == ActorKind.Player), Is.EqualTo(4));
            Assert.That(forecast.Count(a => a.Kind == ActorKind.Enemy), Is.EqualTo(2));
        }

        [Test]
        public void EnemyBurn_SettlesBeforeThatEnemyActs_NotAtPlayerTurnEnd()
        {
            // 灼烧从「玩家回合末全场统一烧」改为「它自己动之前烧」。
            // 观察点:玩家让出后、敌人那一拍执行前,敌人血量不该已经掉。
            var engine = Engine(new[] { Dummy(hp: 100, attack: 10) });
            engine.Enemies[0].Statuses.Apply(new StatusEffect
            {
                Kind = StatusKind.Burn, Polarity = StatusPolarity.Debuff,
                Magnitude = 2, TurnsLeft = -1, SourceId = "测",
            });
            int before = engine.Enemies[0].Hp;

            engine.YieldTurn();
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(before), "让出行动权时还不该烧");

            engine.AdvanceOnce();
            Assert.That(engine.Enemies[0].Hp, Is.LessThan(before), "轮到它自己那拍才烧");
        }

        [Test]
        public void EnemyStatus_TicksAfterThatEnemyActs()
        {
            var engine = Engine(new[] { Dummy(attack: 10) });
            engine.Enemies[0].Statuses.Apply(new StatusEffect
            {
                Kind = StatusKind.Bleed, Polarity = StatusPolarity.Debuff,
                Magnitude = 5, TurnsLeft = 3, SourceId = "测",
            });

            engine.YieldTurn();
            engine.AdvanceOnce();   // 敌人这一拍:结算流血 + 行动 + 自身递减

            Assert.That(engine.Enemies[0].Statuses.Find(StatusKind.Bleed).TurnsLeft, Is.EqualTo(2));
        }

        [Test]
        public void SlowedEnemy_TicksItsOwnDotSlower()
        {
            // 口径 1 的直接后果:被减速的敌人中的毒也跌得慢。
            var engine = Engine(new[] { Dummy(hp: 999, attack: 0), Dummy("快", 999, 0) });
            engine.Enemies[0].Statuses.Apply(new StatusEffect
            {
                Kind = StatusKind.SpeedModifier, Polarity = StatusPolarity.Debuff,
                Magnitude = -50, TurnsLeft = -1, SourceId = "缓",
            });
            foreach (var e in engine.Enemies)
                e.Statuses.Apply(new StatusEffect
                {
                    Kind = StatusKind.Bleed, Polarity = StatusPolarity.Debuff,
                    Magnitude = 10, TurnsLeft = 99, SourceId = "血",
                });

            for (int i = 0; i < 4; i++) engine.EndTurn();

            Assert.That(engine.Enemies[0].Hp, Is.GreaterThan(engine.Enemies[1].Hp),
                "半速的怪流血次数少一半,应该更健康");
        }

        [Test]
        public void SummonAura_HealsOnItsOwnTurn_NotAtPlayerTurnEnd()
        {
            // 光环治疗(桃)从「全体召唤物集体先治疗」改为「该召唤物自己那拍先治疗再出手」。
            // 扣血走真实路径(挨敌人一记),不为测试新增生产 API —— 2026-08-11 用户裁定过这条。
            var graph = new RecipeGraph(new[]
            {
                new CharDef("木", Element.Wood),
                new CharDef("桃", Element.Wood, effects: new[]
                {
                    new EffectDef(EffectKind.Summon, 20, summonCount: 1, summonAttack: 2,
                        summonChar: "木", passive: new SummonPassive { HealAlly = 5 }),
                }),
            });
            var engine = new BattleEngine(graph,
                new BattleConfig { PlayerMaxHp = 999, UnlockedChars = new[] { "桃" } },
                new[] { "桃" }, Array.Empty<string>(),
                new[] { new EnemyDef("凶", Element.Heart, 999, 30) }, seed: 1);
            engine.EndTurn();          // 挨一记,掉血
            engine.Cast("桃");         // 召唤带光环的树
            int hurt = engine.PlayerHp;

            engine.YieldTurn();
            Assert.That(engine.PlayerHp, Is.EqualTo(hurt), "让出行动权时还不该治疗");

            while (engine.AdvanceOnce() && engine.LastActor.Kind != ActorKind.Summon) { }
            Assert.That(engine.PlayerHp, Is.GreaterThan(hurt), "召唤物自己那拍才治疗");
        }

        [Test]
        public void BossCharge_CountsItsOwnActions_NotGlobalTurns()
        {
            // spec §4.5:BossChargeEvery 从「阶段内第 N 个敌方回合」改为「该 Boss 自己的行动次数」。
            // 同速下等价 —— 但被减速的 Boss 攒大招也该变慢,这条锁住它。
            // Boss 的构造照 BossSkillTests 里既有的工厂写(带 Phases 的 EnemyDef)。
            var normal = BossEngine(speedModifier: 0);
            var slowed = BossEngine(speedModifier: -50);

            for (int i = 0; i < 4; i++) { normal.EndTurn(); slowed.EndTurn(); }

            int normalCasts = CountBossSkillEvents(normal);
            int slowedCasts = CountBossSkillEvents(slowed);
            Assert.That(slowedCasts, Is.LessThan(normalCasts), "半速的 Boss 攒大招也该慢一半");
        }

        // 单阶段 Boss(照 BossSkillTests.SkillBoss 的工厂抄):BossChargeEvery 单独配成 3,
        // 让「同速正常 Boss 4 个自身行动」恰好在第 4 次行动落在「释放」——LastEvents 才能在
        // 4 次 EndTurn 后逮到 BossSkillCast;半速 Boss 4 回合只轮到 2 次自身行动,连蓄力都摸不到,
        // 两边的差就是本测试要锁住的东西。
        private static EnemyDef ChargeTestBoss() => new("试炼", Element.Heart, 999, 5,
            phases: new[] { new BossPhaseDef("甲", Element.Heart, 999, 5, skill: BossSkill.Deluge) });

        private static BattleEngine BossEngine(int speedModifier)
        {
            var engine = new BattleEngine(Graph(),
                new BattleConfig { PlayerMaxHp = 999, BossPhaseJitterPercent = 0, BossChargeEvery = 3 },
                Array.Empty<string>(), Array.Empty<string>(),
                new[] { ChargeTestBoss() }, seed: 1);
            if (speedModifier != 0)
                engine.Enemies[0].Statuses.Apply(new StatusEffect
                {
                    Kind = StatusKind.SpeedModifier, Polarity = StatusPolarity.Debuff,
                    Magnitude = speedModifier, TurnsLeft = -1, SourceId = "缓",
                });
            return engine;
        }

        private static int CountBossSkillEvents(BattleEngine engine)
        {
            int count = 0;
            foreach (var e in engine.LastEvents)
                if (e.Kind == BattleEventKind.BossSkillCast) count++;
            return count;
        }

        [Test]
        public void PlayerBurn_SettlesAtPlayerTurnStart_NotWhenYielding()
        {
            var engine = Engine(new[] { Dummy(attack: 0) });
            engine.EndTurn();   // 走到一个干净的玩家拍
            engine.PlayerStatuses.Apply(new StatusEffect
            {
                Kind = StatusKind.Burn, Polarity = StatusPolarity.Debuff,
                Magnitude = 2, TurnsLeft = -1, SourceId = "灯",
            });
            int before = engine.PlayerHp;

            engine.YieldTurn();
            Assert.That(engine.PlayerHp, Is.EqualTo(before), "让出行动权那一刻不烧");

            while (engine.AdvanceOnce()) { }   // 推到下一个玩家拍
            Assert.That(engine.PlayerHp, Is.LessThan(before), "玩家自己那拍开头才烧");
        }

        [Test]
        public void PlayerTurn_AlsoStartsWithActorActed()
        {
            // 2026-08-16 复核补:AdvanceOnce 的玩家分支曾经不发 ActorActed(brief 的示例代码
            // 只放在非玩家分支),导致召唤物/敌人的批次都有段首标记、唯独玩家那批没有——下一个
            // 任务的驱动协程要靠这条标记判断"这批事件属于谁",会漏判玩家这一批。挂个灼烧让
            // BeginPlayerTurn() 必定产生事件(BurnTick),锁住"玩家批次同样以 ActorActed(Player)
            // 开头"这条对称性。
            var engine = Engine(new[] { Dummy(attack: 0) });
            engine.EndTurn();   // 走到一个干净的玩家拍(与 PlayerBurn_SettlesAtPlayerTurnStart_NotWhenYielding 同款写法)
            engine.PlayerStatuses.Apply(new StatusEffect
            {
                Kind = StatusKind.Burn, Polarity = StatusPolarity.Debuff,
                Magnitude = 2, TurnsLeft = -1, SourceId = "灯",
            });

            engine.YieldTurn();
            while (engine.AdvanceOnce()) { }   // 推到下一个玩家拍(BeginPlayerTurn 结算灼烧)

            Assert.That(engine.LastEvents[0].Kind, Is.EqualTo(BattleEventKind.ActorActed));
            Assert.That(engine.LastEvents[0].Amount, Is.EqualTo((int)ActorKind.Player));
            Assert.That(engine.LastEvents[0].TargetIndex, Is.EqualTo(-1));
        }

        // 2026-08-16 全分支终审 Important 1:玩家侧状态回合递减曾经错放在 YieldTurn(拍尾),
        // 相对玩家自己的结算(灼烧/HoT)变成「先递减后结算」,与 ActEnemyTurn(结算在前、
        // 递减在后)方向相反,静默改动了三处数值。修复后递减挪到下一次 BeginPlayerTurn 尾部
        // (紧跟 SettlePlayerHots 之后、StartTurn 之前)——原名 …TicksWhenPlayerYields 已经
        // 名不副实,改名并把断言换成新的挂钩点。
        [Test]
        public void PlayerStatus_TicksAtNextBeginPlayerTurn_NotWhenYielding()
        {
            var engine = Engine(new[] { Dummy(attack: 0) });
            engine.PlayerStatuses.Apply(new StatusEffect
            {
                Kind = StatusKind.HealOverTime, Polarity = StatusPolarity.Buff,
                Magnitude = 5, TurnsLeft = 3, SourceId = "滋",
            });

            engine.YieldTurn();
            Assert.That(engine.PlayerStatuses.Find(StatusKind.HealOverTime).TurnsLeft, Is.EqualTo(3),
                "让出行动权那一刻不该递减");

            while (engine.AdvanceOnce()) { }   // 推到下一个玩家拍(BeginPlayerTurn 结算之后才递减)

            Assert.That(engine.PlayerStatuses.Find(StatusKind.HealOverTime).TurnsLeft, Is.EqualTo(2));
        }

        // 2026-08-16 全分支终审 Important 1:给沐(HealOverTime 20/turns 3)补一条有判别力的
        // 回归测试——旧的 PlayerStatus_TicksWhenPlayerYields 只看第 1 次回复量和到期时刻两个
        // 观察点,这两点在「先递减后结算」的错误模型下读数与正确模型恰好相同,从未变红过
        // (沐的实际回复次数被静默从 3 次改成了 2 次)。这里直接断言总回复次数。
        [Test]
        public void PlayerHot_HealsExactlyThreeTimes()
        {
            // 攻 25(> HoT 的 20):每回合先挨打腾出headroom,heal 才不会被
            // Math.Min(MaxHp - PlayerHp, Magnitude) 封顶裁掉,回复量能稳定按 20 结算。
            var engine = Engine(new[] { Dummy(attack: 25) });
            engine.PlayerStatuses.Apply(new StatusEffect
            {
                Kind = StatusKind.HealOverTime, Polarity = StatusPolarity.Buff,
                Magnitude = 20, TurnsLeft = 3, SourceId = "沐",
            });
            int start = engine.PlayerHp;

            for (int i = 0; i < 3; i++) engine.EndTurn();
            Assert.That(engine.PlayerHp, Is.EqualTo(start - 3 * 25 + 3 * 20), "3 个回合各回复一次,共回复 3 次");

            engine.EndTurn();
            Assert.That(engine.PlayerHp, Is.EqualTo(start - 4 * 25 + 3 * 20), "第 4 回合 HoT 已到期,不再回复");
        }

        [Test]
        public void FastPlayer_GetsApAndDropTwiceAsOften()
        {
            // 口径 5:一次行动 = 一份完整回合(3 AP + 1 掉字)
            var engine = Engine(new[] { Dummy(attack: 0) },
                new BattleConfig { PlayerMaxHp = 999, PlayerSpeed = 200 });
            int turnBefore = engine.Turn;

            engine.EndTurn();
            engine.EndTurn();

            Assert.That(engine.Turn, Is.EqualTo(turnBefore + 2));
            Assert.That(engine.Ap, Is.EqualTo(engine.ApPerTurn), "每拍都回满 AP");
        }

        [Test]
        public void Frozen_KeepsItsSlotButSkipsTheAction()
        {
            var engine = Engine(new[] { Dummy(hp: 999, attack: 50) });
            engine.Enemies[0].Statuses.Apply(new StatusEffect
            {
                Kind = StatusKind.Freeze, Polarity = StatusPolarity.Debuff,
                Magnitude = 1, TurnsLeft = 2, SourceId = "冻",
            });
            int hp = engine.PlayerHp;

            engine.EndTurn();

            Assert.That(engine.PlayerHp, Is.EqualTo(hp), "冻结中不出手");
            Assert.That(engine.Enemies[0].Statuses.Find(StatusKind.Freeze).TurnsLeft, Is.EqualTo(1),
                "轮到它就 −1,不靠别人的回合数");
        }

        [Test]
        public void Frozen_ThawsAndResumesActing()
        {
            var engine = Engine(new[] { Dummy(hp: 999, attack: 50) });
            engine.Enemies[0].Statuses.Apply(new StatusEffect
            {
                Kind = StatusKind.Freeze, Polarity = StatusPolarity.Debuff,
                Magnitude = 1, TurnsLeft = 2, SourceId = "冻",
            });

            engine.EndTurn();
            engine.EndTurn();
            int hp = engine.PlayerHp;
            engine.EndTurn();

            Assert.That(engine.Enemies[0].Statuses.Has(StatusKind.Freeze), Is.False, "两拍后解冻");
            Assert.That(engine.PlayerHp, Is.LessThan(hp), "解冻后照常打人");
        }

        [Test]
        public void Frozen_DoesNotDeadlockTheScheduler()
        {
            // 冻结单位照常上行动条(口径 6):它若被排除出调度,自身状态就永远不递减
            var engine = Engine(new[] { Dummy(hp: 999, attack: 0) });
            engine.Enemies[0].Statuses.Apply(new StatusEffect
            {
                Kind = StatusKind.Freeze, Polarity = StatusPolarity.Debuff,
                Magnitude = 1, TurnsLeft = 99, SourceId = "冻",
            });

            var forecast = engine.Forecast(6);

            Assert.That(forecast.Any(a => a.Kind == ActorKind.Enemy), Is.True,
                "冻结单位仍要出现在预测里(会被跳过,但占位)");
        }

        [Test]
        public void PlayerDeath_EndsBattleImmediately_RemainingEnemiesDoNotAct()
        {
            // 三只怪,第一只就能打死玩家:后两只不该再动(UI 也不该再读条)
            var engine = Engine(new[]
            {
                new EnemyDef("甲", Element.Heart, 999, 500),
                new EnemyDef("乙", Element.Heart, 999, 500),
                new EnemyDef("丙", Element.Heart, 999, 500),
            }, new BattleConfig { PlayerMaxHp = 100 });

            engine.EndTurn();

            Assert.That(engine.Phase, Is.EqualTo(BattlePhase.Lost));
            Assert.That(engine.LastEvents.Count(e => e.Kind == BattleEventKind.EnemyAttack),
                Is.EqualTo(1), "第一记就该收口");
        }

        [Test]
        public void LastEnemyDeath_WinsImmediately()
        {
            var engine = Engine(new[] { Dummy(hp: 1, attack: 0) });
            engine.Enemies[0].Statuses.Apply(new StatusEffect
            {
                Kind = StatusKind.Bleed, Polarity = StatusPolarity.Debuff,
                Magnitude = 99, TurnsLeft = 3, SourceId = "血",
            });

            engine.EndTurn();

            Assert.That(engine.Phase, Is.EqualTo(BattlePhase.Won), "流血打死最后一只就当场赢");
        }

        [Test]
        public void Revive_ContinuesTheTimeline_RemainingEnemiesStillAct()
        {
            // spec §4.3.1:复活 = 满血站起来,时间轴原地继续。
            // 旧行为是 StartTurn() 开新一拍,等于白捡「剩下的怪本回合不再出手」。
            var engine = Engine(new[]
            {
                new EnemyDef("甲", Element.Heart, 999, 150),
                new EnemyDef("乙", Element.Heart, 999, 150),
            }, new BattleConfig { PlayerMaxHp = 100 });

            engine.EndTurn();
            Assert.That(engine.Phase, Is.EqualTo(BattlePhase.Lost));

            engine.Revive();
            Assert.That(engine.PlayerHp, Is.EqualTo(100));

            while (engine.AdvanceOnce()) { }
            Assert.That(engine.PlayerHp, Is.LessThan(100), "没行动的怪照常打");
        }

        [Test]
        public void Disguise_RevealsWhenTakingDamage()
        {
            var graph = new RecipeGraph(new[]
            {
                new CharDef("木", Element.Wood,
                    effects: new[] { new EffectDef(EffectKind.DamageSingle, 10) }),
            });
            var engine = new BattleEngine(graph,
                new BattleConfig { PlayerMaxHp = 999, UnlockedChars = new[] { "木" } },
                new[] { "木" }, Array.Empty<string>(),
                new[] { new EnemyDef("通", Element.Wood, 999, 0, EnemyAbility.Disguise) }, seed: 1);
            var disguised = engine.Enemies[0].ApparentElement;

            engine.Cast("木", 0);

            Assert.That(engine.Enemies[0].ApparentElement, Is.EqualTo(engine.Enemies[0].Element),
                "挨打就现形");
        }

        [Test]
        public void Disguise_StillRevealsAfterActing()
        {
            // 旧口径(8.3 / 2026-08-08)保持不变:它出手就现形,打空也算
            var engine = Engine(new[] { new EnemyDef("通", Element.Wood, 999, 10, EnemyAbility.Disguise) });

            engine.EndTurn();

            Assert.That(engine.Enemies[0].ApparentElement, Is.EqualTo(engine.Enemies[0].Element));
        }

        [Test]
        public void PlayerActionMeter_SurvivesRoundTrip()
        {
            var dummy = Dummy(attack: 0);
            var engine = Engine(new[] { dummy },
                new BattleConfig { PlayerMaxHp = 999, PlayerSpeed = 150 });
            engine.EndTurn();   // 速度 150:行动后计量器留 50 的余额

            var snapshot = engine.Capture();
            var defs = new Dictionary<string, EnemyDef> { [dummy.Id] = dummy };
            var restored = BattleEngine.Restore(snapshot, Graph(),
                new BattleConfig { PlayerMaxHp = 999, PlayerSpeed = 150 }, null, defs);

            Assert.That(restored.PlayerActionMeter, Is.EqualTo(engine.PlayerActionMeter));
        }

        [Test]
        public void RestoredBattle_ContinuesTheSameRhythm()
        {
            var dummy1 = Dummy(attack: 0);
            var dummy2 = Dummy("乙", 999, 0);
            var engine = Engine(new[] { dummy1, dummy2 },
                new BattleConfig { PlayerMaxHp = 999, PlayerSpeed = 150 });
            engine.EndTurn();

            var defs = new Dictionary<string, EnemyDef> { [dummy1.Id] = dummy1, [dummy2.Id] = dummy2 };
            var restored = BattleEngine.Restore(engine.Capture(), Graph(),
                new BattleConfig { PlayerMaxHp = 999, PlayerSpeed = 150 }, null, defs);

            Assert.That(restored.Forecast(6), Is.EqualTo(engine.Forecast(6)));
        }

        // ===== LastAdvanceTicks(2026-08-17,每单位行动条)=====

        [Test]
        public void LastAdvanceTicks_RecordsSchedulerTicks()
        {
            // 同速基准局,构造完的状态是「玩家 0,敌人 100」——开场那一拍全场攒到 100,
            // 玩家(priority 0)赢了并列、扣掉自己那 100,敌人的 100 留在条上(2026-08-17)。
            // 所以战斗开始后第一次 AdvanceOnce 走的是 FirstFull 分支:不推进时间,ticks 记 0。
            // 断言 0 不是"没写值"——LastAdvanceTicks 此刻的旧值是开场那一拍的 1,
            // 引擎漏了写回就会读到 1,这条正是那个漏写的哨兵。
            var engine = Engine(new[] { Dummy() });
            engine.YieldTurn();

            engine.AdvanceOnce();

            Assert.That(engine.LastAdvanceTicks, Is.EqualTo(0));
        }

        [Test]
        public void LastAdvanceTicks_RecordsOpeningAdvance()
        {
            // 2026-08-17:构造函数现在就会跑开场推进,所以「战斗刚开始 LastAdvanceTicks 为 0」
            // 这个前提已经不成立。改为断言它记下了开场那一拍 —— 同速开局恰好 1 拍。
            var engine = Engine(new[] { Dummy() });

            Assert.That(engine.LastAdvanceTicks, Is.EqualTo(1));
        }

        [Test]
        public void LastAdvanceTicks_UpdatesBetweenConsecutiveAdvances()
        {
            // 引擎侧的接线要每拍都更新,不能只写第一次(调度器侧的多拍用例盖不到这一层)。
            // 2026-08-17:两个值互换了 —— 开场那一拍已把敌人顶到满格,所以第一次推进是
            // FirstFull 分支(0 拍),敌人吃掉那一格后全场归零,第二次才需要真推 1 拍。
            var engine = Engine(new[] { Dummy() });
            engine.YieldTurn();

            engine.AdvanceOnce();
            int first = engine.LastAdvanceTicks;
            engine.AdvanceOnce();

            Assert.That(first, Is.EqualTo(0),
                "敌人在开场那一拍已被顶到满格,它这一格不需要推进时间(FirstFull 分支)");
            Assert.That(engine.LastAdvanceTicks, Is.EqualTo(1), "全场归零后,再攒满要整整一拍");
        }

        [Test]
        public void LastAdvanceTicks_ReflectsSlowUnitMultipleTicks()
        {
            // 速度 25(= MinSpeed)要四拍才攒满 —— 表现层据此把条动画拉长到四倍。
            //
            // 2026-08-17:速度改成在 EnemyDef 里配好(Task 1 打开的通道)。原先「先构造、
            // 后改 Enemies[0].Speed」现在已经晚了 —— 构造函数会跑开场推进,敌人会以默认
            // 速度 100 参与那一拍,四拍这条要验的路径根本走不到。
            //
            // 玩家速度也要压到同一档:若只压敌人、玩家仍是默认 100,玩家一拍就攒满,
            // ticks 恒为 1。两边都慢才会出现「四拍才有人攒满」。
            //
            // 要推两次 AdvanceOnce:开场那一拍(4 拍)结束时敌人已满格,第一次推进是它吃掉
            // 那一格(FirstFull 分支,0 拍),第二次才是全场从 0 重攒的四拍。四拍同时攒满时
            // 按新 tie-break 玩家(0)赢敌人(3),所以这一格的行动者是玩家 —— 本条要验的是
            // **跨拍数**,不是谁赢(谁赢由 TurnSchedulerTests 的 TieBreak_* 守)。
            var engine = Engine(new[] { Dummy(speed: TurnScheduler.MinSpeed) },
                new BattleConfig { PlayerMaxHp = 999, PlayerSpeed = TurnScheduler.MinSpeed });
            engine.YieldTurn();

            engine.AdvanceOnce();
            engine.AdvanceOnce();

            Assert.That(engine.LastActor.Kind, Is.EqualTo(ActorKind.Player));
            Assert.That(engine.LastAdvanceTicks, Is.EqualTo(4));
        }

        // ===== EnemyDef.Speed 配置通道(2026-08-17,spec §5.8)=====

        [Test]
        public void EnemyDefSpeed_FlowsIntoEnemyState()
        {
            var fast = new EnemyDef("疾", Element.Heart, 999, 0, speed: 200);
            var engine = Engine(new[] { fast });

            Assert.That(engine.Enemies[0].Speed, Is.EqualTo(200));
        }

        [Test]
        public void EnemyDefSpeed_Unset_FallsBackToBaseline()
        {
            // 不配 speed 的怪仍是基准 100 —— 全部既有字表都走这条路径,数值一个不能变
            var engine = Engine(new[] { Dummy() });

            Assert.That(engine.Enemies[0].Speed, Is.EqualTo(100));
        }
    }
}
