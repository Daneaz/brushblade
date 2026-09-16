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
            // —— 恒非负这条不变式是结构性的:计量器只在「满格(≥100)」时才扣 100,扣完必然
            // ≥ 0,没有任何一处会把它写成负数。这条测试守的就是这个不变式。
            //
            // ⚠ 现行方向是**玩家排最先**(玩家 0 / 召唤 1 / Buff 敌 2 / 其余敌 3,2026-08-17)。
            // 这里原先写着「把优先级方向调成『玩家排最后』之后,恒非负自然成立」—— 那个因果是
            // 错的:2026-08-15 那四轮记账手法(创建时先手 / 玩家记负债 / 懒消费一次 Advance)
            // 要抵消的是**构造函数给玩家的免费先手**,不是玩家优先本身。免费先手已随
            // 2026-08-17 的改造删除,玩家优先因此不再需要任何补偿,不变式照样成立。
            // 完整推演与实测数字见 BattleEngine.BuildSlots 的注释。
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

        // ==================== 加速 / 急速(2026-09-16,水,EffectKind.Haste) ====================
        //
        // SpeedModifier 此前只出现负值(减速)。这是它第一次取正——按接线复查清单挨个核对过
        // (BattleEngine.ConditionMet「对控制」判据、Targeting.PickEnemyTargetForSummon 的
        // preferUnslowed 早已是 `< 0`/`>= 0`),下面 Haste_DoesNotCountAsSlowed_ForControlledCondition
        // 单独钉住第一条。复查还额外抓到一个真实缺陷:BuildSlots() 给召唤物排调度顺序时只读
        // Speed 裸值、不加 Statuses 里的 SpeedModifier(与敌人分支不对称)——此前无害,因为
        // 从没有效果把 SpeedModifier 挂到召唤物自己的状态袋上,Haste 是第一个真正踩上去的,
        // 已在 BattleEngine.BuildSlots 里补上一行,SummonInfo.BuildFigures 的详情面板同一口径补齐。

        /// <summary>加速/急速换算基数用测试图谱:兵(底速 100,攻 5——非零攻击是为了
        /// Haste_OnSummon_SpeedsUpActualTurnOrder_ThroughScheduling 能靠 SummonAttack
        /// 事件数数出手次数,前四条只看 Speed/Statuses 的测试不受这个改动影响)、
        /// 捷(底速 150,与桤/森/藻/林同型)、速(加速 50%,2 回合)、疾(急速 100%,1 回合)。
        /// 拆成 HasteGraph()/HasteEngine() 两层是为了快照往返测试能拿到**同一份**图谱
        /// 传给 BattleEngine.Restore(graph, …)。</summary>
        private static RecipeGraph HasteGraph() => new(new[]
        {
            new CharDef("木", Element.Wood),
            new CharDef("兵", Element.Wood, effects: new[]
            {
                new EffectDef(EffectKind.Summon, 999, summonCount: 1, summonAttack: 5, summonChar: "木"),
            }),
            new CharDef("捷", Element.Wood, effects: new[]
            {
                new EffectDef(EffectKind.Summon, 999, summonCount: 1, summonAttack: 0, summonChar: "木",
                    passive: new SummonPassive { Speed = 150 }),
            }),
            new CharDef("速", Element.Water, effects: new[] { new EffectDef(EffectKind.Haste, 50, turns: 2) }),
            new CharDef("疾", Element.Water, effects: new[] { new EffectDef(EffectKind.Haste, 100, turns: 5) }),
        });

        private static EnemyDef HasteTarget() => new("靶", Element.Heart, 999999, 0);

        private static BattleEngine HasteEngine() => new(HasteGraph(),
            new BattleConfig { PlayerMaxHp = 999, ApPerTurn = 9 },
            new[] { "兵", "兵", "捷", "速", "速", "速", "疾" }, Array.Empty<string>(),
            new[] { HasteTarget() }, seed: 1);

        [Test]
        public void Haste_AddsPercentageOfTargetBaseSpeed_AsPositiveSpeedModifier()
        {
            // 加速 50% 挂在 100 底速的召唤物上 → +50 点;挂在 150 底速的上 → +75 点——
            // 换算基数是目标自己的基础速度,不是固定数(brief「换算基数」一节)。
            var engine = HasteEngine();
            engine.Cast("兵");              // slot 0,底速 100
            engine.Cast("捷");              // slot 1,底速 150
            engine.Cast("速", allySlot: 0); // 加速 50%:100 底速 → +50
            engine.Cast("速", allySlot: 1); // 加速 50%:150 底速 → +75

            Assert.That(engine.Summons[0].Statuses.TotalMagnitude(StatusKind.SpeedModifier), Is.EqualTo(50));
            Assert.That(engine.Summons[1].Statuses.TotalMagnitude(StatusKind.SpeedModifier), Is.EqualTo(75));
            Assert.That(engine.Summons[0].Speed + engine.Summons[0].Statuses.TotalMagnitude(StatusKind.SpeedModifier),
                Is.EqualTo(150), "100 底速召唤物的有效速度");
            Assert.That(engine.Summons[1].Speed + engine.Summons[1].Statuses.TotalMagnitude(StatusKind.SpeedModifier),
                Is.EqualTo(225), "150 底速召唤物的有效速度");
        }

        [Test]
        public void Haste_HundredPercent_IsRapid()
        {
            // 急速 100%:100 底速 → 200
            var engine = HasteEngine();
            engine.Cast("兵");              // slot 0,底速 100
            engine.Cast("疾", allySlot: 0); // 急速 100%:100 底速 → +100

            Assert.That(engine.Summons[0].Statuses.TotalMagnitude(StatusKind.SpeedModifier), Is.EqualTo(100));
            Assert.That(engine.Summons[0].Speed + engine.Summons[0].Statuses.TotalMagnitude(StatusKind.SpeedModifier),
                Is.EqualTo(200));
        }

        [Test]
        public void Haste_DoesNotCountAsSlowed_ForControlledCondition()
        {
            // 「对控制」的双倍判据只认负的 SpeedModifier —— 这里直接在敌人身上模拟一次正值
            // (真实规则里 Haste 只会挂在我方身上,这里只测 ConditionMet 这个共享判据本身没有
            // 「挂着 SpeedModifier 就是被控制了」这类误判)。
            var graph = new RecipeGraph(new[]
            {
                new CharDef("斩", Element.Heart,
                    effects: new[] { new EffectDef(EffectKind.DamageSingle, 100, doubleVs: DamageCondition.Controlled) }),
            });
            var engine = new BattleEngine(graph, new BattleConfig { PlayerMaxHp = 999, PlayerAttack = 100 },
                new[] { "斩" }, Array.Empty<string>(),
                new[] { new EnemyDef("靶", Element.Heart, 999, 0) }, seed: 1);
            engine.Enemies[0].Statuses.Apply(new StatusEffect
            {
                Kind = StatusKind.SpeedModifier, Polarity = StatusPolarity.Buff,
                Magnitude = 50, TurnsLeft = -1, SourceId = "测",
            });

            int hpBefore = engine.Enemies[0].Hp;
            engine.Cast("斩", 0);
            int damage = hpBefore - engine.Enemies[0].Hp;

            Assert.That(damage, Is.EqualTo(100),
                "挂着正的 SpeedModifier(加速)不该让敌人吃到「对控制」双倍伤害(200)");
        }

        [Test]
        public void Haste_ExpiresAfterTurns()
        {
            // 落在玩家身上测到期:玩家侧状态递减挂在 BeginPlayerTurn,一次 EndTurn() 正好一拍,
            // 与 TimedBuffTests 的 Empower/CritBuff 到期测试同一口径——不挑召唤物,是为了避开
            // ATB 下召唤物自身速度可能让它一次 EndTurn() 内出手不止一次的时序复杂度。
            var engine = HasteEngine();
            Assert.That(engine.EffectivePlayerSpeed, Is.EqualTo(100), "基准");

            engine.Cast("速"); // 默认 allySlot = 玩家;加速 50%,2 回合:100 → 150
            Assert.That(engine.EffectivePlayerSpeed, Is.EqualTo(150));

            engine.EndTurn();
            Assert.That(engine.EffectivePlayerSpeed, Is.EqualTo(150), "第 1 个回合末还在");

            engine.EndTurn();
            Assert.That(engine.EffectivePlayerSpeed, Is.EqualTo(100), "第 2 个回合末到期,速度回落到基准");
        }

        [Test]
        public void Haste_OnSummon_SpeedsUpActualTurnOrder_ThroughScheduling()
        {
            // I-1(评审复审):前四条 Haste 测试全部在断言里手算「Speed + TotalMagnitude」,
            // 相当于把 BuildSlots() 里那一行公式在测试里重写了一遍,没有一条真的经过
            // BuildSlots()/TurnScheduler —— 把 BuildSlots() 里那两行加法删掉退回裸 Speed,
            // 前四条与另外 1799 条测试仍会全绿,而那正是"加速挂上了、调度顺序纹丝不动"这个
            // 静默空转本身。这一条改成从**可观测行为**(谁真的出手更多次)反推调度顺序。
            var engine = HasteEngine();
            engine.Cast("兵");              // slot 0,底速 100,攻 5
            engine.Cast("兵");              // slot 1,底速 100,攻 5(同底速对照组,不吃急速)
            engine.Cast("疾", allySlot: 0); // 只给 slot 0 挂急速 100%:100 → 200,是 slot 1 的两倍

            var events = new List<BattleEvent>();
            for (int i = 0; i < 8; i++)
            {
                engine.EndTurn();
                events.AddRange(engine.LastEvents);
            }

            int hastedActs = events.Count(e => e.Kind == BattleEventKind.SummonAttack && e.SecondIndex == 0);
            int normalActs = events.Count(e => e.Kind == BattleEventKind.SummonAttack && e.SecondIndex == 1);
            Assert.That(hastedActs, Is.GreaterThan(normalActs),
                "急速把 slot 0 的有效速度顶到 slot 1 的两倍,它应当出手更多次 —— " +
                "把 BuildSlots() 里 SpeedModifier 那一行加法删掉退回裸 Speed,这条必须变红");
        }

        [Test]
        public void Haste_StacksInsteadOfRefreshing()
        {
            // N-3(评审复审):规格第 4 条「SourceId 铸序号使其可叠」有实现无断言 —— 补上,
            // 与 BuffCharTests.DefenseBuff_StacksInsteadOfRefreshing(癸,同款 SourceId 铸序号)
            // 同一口径:同字连出两次应叠加,不是刷新回同一个值。
            var engine = HasteEngine();
            engine.Cast("速"); // 玩家:加速 50%,+50
            engine.Cast("速"); // 再来一张同字:应叠成 +100,不是刷新回 +50

            Assert.That(engine.PlayerStatuses.TotalMagnitude(StatusKind.SpeedModifier), Is.EqualTo(100),
                "同字连出两次应叠加(50+50),SourceId 若退化成裸字 ID 会被 Apply() 判成同源刷新,停在 50");
        }

        [Test]
        public void Haste_StackedMagnitude_SurvivesSnapshotRoundTrip()
        {
            // N-3(评审复审)后半句:把 _statusSerial 快照往返那条链带上——不仅叠加后的
            // 总量要在 Capture()/Restore() 之后保住,续爬后再出一张同字也必须继续走「新增」
            // 分支,而不是撞上快照里没存对的旧序号、被 Apply() 误判成同源刷新。
            var engine = HasteEngine();
            engine.Cast("速");
            engine.Cast("速");
            Assert.That(engine.PlayerStatuses.TotalMagnitude(StatusKind.SpeedModifier), Is.EqualTo(100));

            var defs = new Dictionary<string, EnemyDef> { ["靶"] = HasteTarget() };
            var restored = BattleEngine.Restore(engine.Capture(), HasteGraph(),
                new BattleConfig { PlayerMaxHp = 999, ApPerTurn = 9 }, null, defs);

            Assert.That(restored.PlayerStatuses.TotalMagnitude(StatusKind.SpeedModifier), Is.EqualTo(100),
                "叠加后的总量要在快照往返后保住");

            restored.Cast("速"); // 续爬后再出一张同字
            Assert.That(restored.PlayerStatuses.TotalMagnitude(StatusKind.SpeedModifier), Is.EqualTo(150),
                "_statusSerial 若没有正确写进快照,这一张会撞上旧序号被判成同源刷新," +
                "总量会停在 100 而不是 150");
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
            // 这个前提已经不成立。改为断言它记下了开场那一段 —— 速度 100 从 0 攒满
            // 恰好 Threshold / 100 拍(2026-09-03 刻度细化后是 100 拍,此前是 1 拍)。
            var engine = Engine(new[] { Dummy() });

            Assert.That(engine.LastAdvanceTicks, Is.EqualTo(TurnScheduler.Threshold / 100));
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
            Assert.That(engine.LastAdvanceTicks, Is.EqualTo(TurnScheduler.Threshold / 100),
                "全场归零后,再攒满要速度 100 的整整一次行动的时间");
        }

        [Test]
        public void LastAdvanceTicks_ReflectsSlowUnitMultipleTicks()
        {
            // 速度 25(= MinSpeed)要四倍时间才攒满 —— 表现层据此把条动画拉长到四倍。
            //
            // 2026-08-17:速度改成在 EnemyDef 里配好(Task 1 打开的通道)。原先「先构造、
            // 后改 Enemies[0].Speed」现在已经晚了 —— 构造函数会跑开场推进,敌人会以默认
            // 速度 100 参与那一拍,四拍这条要验的路径根本走不到。
            //
            // 玩家速度也要压到同一档:若只压敌人、玩家仍是默认 100,玩家一拍就攒满,
            // ticks 恒为 1。两边都慢才会出现「四拍才有人攒满」。
            //
            // 要推两次 AdvanceOnce:开场那一段结束时敌人已满格,第一次推进是它吃掉
            // 那一格(FirstFull 分支,0 拍),第二次才是全场从 0 重攒的那一段。同时攒满时
            // 按新 tie-break 玩家(0)赢敌人(3),所以这一格的行动者是玩家 —— 本条要验的是
            // **跨拍数**,不是谁赢(谁赢由 TurnSchedulerTests 的 TieBreak_* 守)。
            var engine = Engine(new[] { Dummy(speed: TurnScheduler.MinSpeed) },
                new BattleConfig { PlayerMaxHp = 999, PlayerSpeed = TurnScheduler.MinSpeed });
            engine.YieldTurn();

            engine.AdvanceOnce();
            engine.AdvanceOnce();

            Assert.That(engine.LastActor.Kind, Is.EqualTo(ActorKind.Player));
            Assert.That(engine.LastAdvanceTicks,
                Is.EqualTo(TurnScheduler.Threshold / TurnScheduler.MinSpeed));
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

        // ===== 开场走调度(2026-08-17,spec 口径 2 / 7)=====

        [Test]
        public void Opening_SameSpeed_PlayerActsFirst()
        {
            // 同速开局:全场从 0 攒,同拍满格,玩家 priority 最小 → 玩家先动。
            // 构造完玩家计量器已被扣过 100(它真的行动了),而敌人停在满格等下一拍。
            var engine = Engine(new[] { Dummy() });

            Assert.That(engine.LastActor, Is.EqualTo(ActorRef.Player));
            Assert.That(engine.PlayerActionMeter, Is.EqualTo(0), "玩家攒满后消费了那一拍");
            Assert.That(engine.Enemies[0].ActionMeter, Is.EqualTo(TurnScheduler.Threshold));
        }

        [Test]
        public void Opening_EnemyFullInOneTickWhilePlayerIsNot_EnemyActsFirst()
        {
            // 口径 7:tie-break 只管**真正的同拍满格**。玩家 25 要 400 拍、敌人 400 只要 25 拍,
            // 敌人自己先满,根本走不到 tie-break。没有这条,后人把 priority 当成绝对顺序
            // 也不会变红。
            var engine = Engine(new[] { new EnemyDef("疾", Element.Heart, 999, 0, speed: 400) },
                new BattleConfig { PlayerMaxHp = 999, PlayerSpeed = TurnScheduler.MinSpeed });

            Assert.That(engine.OpeningSteps[0].Actor.Kind, Is.EqualTo(ActorKind.Enemy));
        }

        [Test]
        public void Opening_FasterEnemy_ActsFirst()
        {
            // 2026-09-03 改判(Threshold 100 → 10000,时间精度):**更快的就是先动**。
            //
            // 这条此前叫 Opening_FasterEnemyButPlayerFullInOneTick_PlayerStillActsFirst,
            // 断言的是「敌人快 4 倍,玩家仍先动」—— 那不是设计,是粗粒度的副产物:
            // 旧刻度下一拍给的量 = 速度,速度 ≥ 100 的一拍必满,于是 400 和 100 被抹成同拍,
            // 再由 tie-break 判给玩家,快出来的那 300 点憋在条上看不见,之后突然连动两次
            // (用户 2026-09-03 报的「速度条与行动不匹配」)。细化刻度后 400 只要 25 拍、
            // 100 要 100 拍,先满的先动,条上也看得见 —— tie-break 回到它本来的位置:
            // 只裁真正的并列(见上一条与 TurnSchedulerTests.TieBreak_*)。
            var engine = Engine(new[] { new EnemyDef("疾", Element.Heart, 999, 0, speed: 400) });

            Assert.That(engine.OpeningSteps[0].Actor.Kind, Is.EqualTo(ActorKind.Enemy));
        }

        [Test]
        public void Opening_SlowedPlayer_EnemyActsFirst()
        {
            // 与上面两条互补:敌人只是基准 100,单靠玩家慢(25 要 4 拍)就够让敌人抢到第一拍。
            // 这条走 PlayerSpeed 配置通道,证明「敌人先动」不需要敌人被配高速。
            var engine = Engine(new[] { Dummy() },
                new BattleConfig { PlayerMaxHp = 999, PlayerSpeed = TurnScheduler.MinSpeed });

            Assert.That(engine.OpeningSteps[0].Actor.Kind, Is.EqualTo(ActorKind.Enemy));
        }

        [Test]
        public void Opening_RecordsReplayDataForEveryStep()
        {
            // spec §5.7:构造函数把开场推进跑完了,表现层要靠 OpeningSteps 把过程演出来。
            // 每条都必须带齐回放所需的四样:谁动、跨几拍、推进后计量器、该拍事件。
            var engine = Engine(new[] { Dummy() });

            Assert.That(engine.OpeningSteps, Is.Not.Empty);
            var first = engine.OpeningSteps[0];
            Assert.That(first.Ticks, Is.GreaterThan(0), "开场必然跨了至少一拍");
            Assert.That(first.EnemyMeters.Count, Is.EqualTo(engine.Enemies.Count));
            Assert.That(first.Events, Is.Not.Empty, "每批至少带 ActorActed 段首标记");
        }

        [Test]
        public void Opening_RecordsEnemyHpBeforeOpening()
        {
            // 开场推进在构造函数里就跑完了,表现层拿到引擎时敌人血量已经是**开场后**的值。
            // 回放那几拍时血条的起点必须是开场**前**的血,否则 OnImpact 会把它从终值再往下
            // 推一段 —— 携带满格召唤物开局时就是「怪还活着、血条却空了」(用户 2026-09-15 报)。
            // Core 不记这一笔,表现层无从复原。
            var carried = new[]
            {
                new SummonSnapshot { Slot = 0, Char = "木", Element = Element.Wood,
                    Hp = 10, MaxHp = 10, Attack = 30, Speed = 100,
                    ActionMeter = TurnScheduler.Threshold },
            };
            var engine = new BattleEngine(Graph(), new BattleConfig { PlayerMaxHp = 999 },
                Array.Empty<string>(), Array.Empty<string>(),
                new[] { Dummy() }, seed: 1, startingSummons: carried);

            Assert.That(engine.Enemies[0].Hp, Is.LessThan(999), "满格召唤物开场先打了一记");
            Assert.That(engine.OpeningPreEnemyHp.Count, Is.EqualTo(engine.Enemies.Count));
            Assert.That(engine.OpeningPreEnemyHp[0], Is.EqualTo(999), "开场前是满血");
        }

        [Test]
        public void Opening_SnapshotRestore_HasNoOpeningSteps()
        {
            // 口径 8:断点续爬恢复的是战斗中途,没有「开场」可回放 —— 表现层据此跳过回放
            var dummy = Dummy();
            var engine = Engine(new[] { dummy });
            var defs = new Dictionary<string, EnemyDef> { [dummy.Id] = dummy };
            var restored = BattleEngine.Restore(engine.Capture(), Graph(),
                new BattleConfig { PlayerMaxHp = 999 }, null, defs);

            Assert.That(restored.OpeningSteps, Is.Empty);
        }

        [Test]
        public void Summon_EntersFieldAtFullMeter()
        {
            // 口径 3:召唤术的价值在「立刻有个肉盾并反击」。这条守住那个头寸不被再删一次
            // ——它 2026-08-15 被删过,理由在当时成立(召唤物排最先),方向调回来后失效。
            // 用真实召唤路径(Cast 一个召唤字)而不是直接构造 SummonState。
            // 图/召唤字照 SummonAura_HealsOnItsOwnTurn_NotAtPlayerTurnEnd 的写法抄(桃 召 木)。
            var graph = new RecipeGraph(new[]
            {
                new CharDef("木", Element.Wood),
                new CharDef("桃", Element.Wood, effects: new[]
                {
                    new EffectDef(EffectKind.Summon, 20, summonCount: 1, summonAttack: 2, summonChar: "木"),
                }),
            });
            var engine = new BattleEngine(graph,
                new BattleConfig { PlayerMaxHp = 999, UnlockedChars = new[] { "桃" } },
                new[] { "桃" }, Array.Empty<string>(),
                new[] { new EnemyDef("凶", Element.Heart, 999, 30) }, seed: 1);

            engine.Cast("桃");

            Assert.That(engine.Summons[0].ActionMeter, Is.EqualTo(TurnScheduler.Threshold));
        }
    }
}
