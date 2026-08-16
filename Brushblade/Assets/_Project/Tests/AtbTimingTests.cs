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

        private static EnemyDef Dummy(string id = "靶", int hp = 999, int attack = 0) =>
            new(id, Element.Heart, hp, attack);

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
        public void PlayerStatus_TicksWhenPlayerYields()
        {
            var engine = Engine(new[] { Dummy(attack: 0) });
            engine.PlayerStatuses.Apply(new StatusEffect
            {
                Kind = StatusKind.HealOverTime, Polarity = StatusPolarity.Buff,
                Magnitude = 5, TurnsLeft = 3, SourceId = "滋",
            });

            engine.YieldTurn();

            Assert.That(engine.PlayerStatuses.Find(StatusKind.HealOverTime).TurnsLeft, Is.EqualTo(2));
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
    }
}
