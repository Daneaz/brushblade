using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>行动调度器(2026-08-15,ATB 改造 T1):纯计算,不认识战斗状态。
    /// 规格见 docs/superpowers/specs/2026-08-15-ATB回合制改造-design.md §4.1。</summary>
    public class TurnSchedulerTests
    {
        // 优先级:玩家 0 / 召唤物 1 / Buff 敌 2 / 其余敌 3(与 BattleEngine.BuildSlots 的填法一致)。
        // 玩家排**最先**(2026-08-17 用户拍板,推翻 2026-08-15 的反向口径)。旧口径把玩家排最后
        // 并警告「方向不要再调回去」,理由是「玩家优先会每次推进都抢回行动权」——那个诊断把病根
        // 归错了:病根是「玩家优先 + 构造函数的免费先手」这个组合。免费先手已删,完整推演与实测
        // 数字见 BattleEngine.BuildSlots 的注释。
        private static SchedulerSlot Player(int speed, int meter = 0) =>
            new(ActorRef.Player, speed, meter, 0);

        private static SchedulerSlot Enemy(int index, int speed, int meter = 0) =>
            new(new ActorRef(ActorKind.Enemy, index), speed, meter, 3);

        private static SchedulerSlot Summon(int index, int speed, int meter = 0) =>
            new(new ActorRef(ActorKind.Summon, index), speed, meter, 1);

        private static SchedulerSlot Buffer(int index, int speed, int meter = 0) =>
            new(new ActorRef(ActorKind.Enemy, index), speed, meter, 2);

        /// <summary>连推 n 拍,返回出手序列。每拍把新计量器写回槽位——
        /// 这正是 BattleEngine 接线后要做的事。</summary>
        private static List<ActorRef> Sequence(List<SchedulerSlot> slots, int n)
        {
            var result = new List<ActorRef>();
            for (int i = 0; i < n; i++)
            {
                var step = TurnScheduler.Advance(slots);
                result.Add(step.Actor);
                for (int s = 0; s < slots.Count; s++)
                    slots[s] = new SchedulerSlot(slots[s].Actor, slots[s].Speed,
                        step.Meters[s], slots[s].Priority);
            }
            return result;
        }

        [Test]
        public void SameSpeed_AlternatesInPriorityOrder()
        {
            // 玩家排最先(2026-08-17,推翻 2026-08-15 的反向口径):同速并列时玩家赢,
            // 序列是「玩家 → 敌」交替。免费先手已随构造函数改造删除,所以玩家不会连动两次
            // ——那正是旧警告担心的现象,病根在免费先手而非本排序(见 BuildSlots 注释)。
            var slots = new List<SchedulerSlot> { Player(100), Enemy(0, 100) };

            var seq = Sequence(slots, 4);

            Assert.That(seq[0].Kind, Is.EqualTo(ActorKind.Player));
            Assert.That(seq[1].Kind, Is.EqualTo(ActorKind.Enemy));
            Assert.That(seq[2].Kind, Is.EqualTo(ActorKind.Player));
            Assert.That(seq[3].Kind, Is.EqualTo(ActorKind.Enemy));
        }

        [Test]
        public void SpeedRatio_IsExactOverFiveTicks()
        {
            // 我 100 / 敌 60:前 8 次行动里恰好 5 次对 3 次,比例与速度 100:60 一致(spec §4.1 的口径例)
            var slots = new List<SchedulerSlot> { Player(100), Enemy(0, 60) };

            var seq = Sequence(slots, 8);

            Assert.That(seq.Take(8).Count(a => a.Kind == ActorKind.Player), Is.EqualTo(5));
            Assert.That(seq.Take(8).Count(a => a.Kind == ActorKind.Enemy), Is.EqualTo(3));
        }

        [Test]
        public void DoubleSpeed_ActsTwiceAsOften()
        {
            var slots = new List<SchedulerSlot> { Player(200), Enemy(0, 100) };

            var seq = Sequence(slots, 6);

            Assert.That(seq.Count(a => a.Kind == ActorKind.Player), Is.EqualTo(4));
            Assert.That(seq.Count(a => a.Kind == ActorKind.Enemy), Is.EqualTo(2));
        }

        [Test]
        public void Meter_KeepsRemainderAfterActing()
        {
            // 速度 150:第一拍攒到 150,行动后留 50 —— 余额必须留着,不清零
            var slots = new List<SchedulerSlot> { Player(150) };

            var step = TurnScheduler.Advance(slots);

            Assert.That(step.Actor.Kind, Is.EqualTo(ActorKind.Player));
            Assert.That(step.Meters[0], Is.EqualTo(50));
        }

        [Test]
        public void AlreadyFull_ActsWithoutAdvancingTime()
        {
            // 已经满格的不该再推进时间:否则同一 tick 内的第二个行动者会白拿一次累积。
            // 两者都已满格时按优先级判并列:玩家(0)先于敌人(3)——2026-08-17 反转
            var slots = new List<SchedulerSlot> { Player(100, meter: 120), Enemy(0, 100, meter: 110) };

            var step = TurnScheduler.Advance(slots);

            Assert.That(step.Actor.Kind, Is.EqualTo(ActorKind.Player));
            Assert.That(step.Meters[0], Is.EqualTo(20), "行动者(玩家)扣 100");
            Assert.That(step.Meters[1], Is.EqualTo(110), "其他人(敌人)原地不动");
        }

        [Test]
        public void TieBreak_PlayerThenSummonThenBufferThenOthers()
        {
            // 全部同速同计量器 —— 纯考排序契约。这条锁死后,谁「顺手优化」调度器
            // 都会立刻变红。顺序:玩家 0 → 召唤物 1 → Buff 敌 2 → 其余敌 3(2026-08-17)
            var slots = new List<SchedulerSlot>
            {
                Enemy(0, 100), Buffer(1, 100), Summon(0, 100), Player(100),
            };

            var seq = Sequence(slots, 4);

            Assert.That(seq[0], Is.EqualTo(ActorRef.Player));
            Assert.That(seq[1], Is.EqualTo(new ActorRef(ActorKind.Summon, 0)));
            Assert.That(seq[2], Is.EqualTo(new ActorRef(ActorKind.Enemy, 1)), "Buff 敌先于普通敌");
            Assert.That(seq[3], Is.EqualTo(new ActorRef(ActorKind.Enemy, 0)));
        }

        [Test]
        public void TieBreak_SamePriorityFollowsIndexOrder()
        {
            var slots = new List<SchedulerSlot> { Enemy(0, 100), Enemy(1, 100), Enemy(2, 100) };

            var seq = Sequence(slots, 3);

            Assert.That(seq[0].Index, Is.EqualTo(0));
            Assert.That(seq[1].Index, Is.EqualTo(1));
            Assert.That(seq[2].Index, Is.EqualTo(2));
        }

        [Test]
        public void ClampSpeed_BoundsAreTwentyFiveToFourHundred()
        {
            Assert.That(TurnScheduler.ClampSpeed(0), Is.EqualTo(25));
            Assert.That(TurnScheduler.ClampSpeed(-999), Is.EqualTo(25));
            Assert.That(TurnScheduler.ClampSpeed(100), Is.EqualTo(100));
            Assert.That(TurnScheduler.ClampSpeed(800), Is.EqualTo(400));
        }

        [Test]
        public void ZeroSpeed_StillActsEventually_NoDeadlock()
        {
            // 速度 0 若原样使用,该单位永远攒不满 → 永远轮不到 → 它自己那拍的状态递减
            // 永远不跑 → 减速永远解不了。钳到 25 就自然消解(四拍动一次)。
            var slots = new List<SchedulerSlot> { Player(100), Enemy(0, 0) };

            var seq = Sequence(slots, 10);

            Assert.That(seq.Any(a => a.Kind == ActorKind.Enemy), Is.True, "速度 0 的单位仍须能行动");
        }

        [Test]
        public void ExtremeSpeed_IsCappedAtFourHundred()
        {
            // 800 与 400 表现必须一致:上限之外再堆速度不再有收益
            var capped = new List<SchedulerSlot> { Player(400), Enemy(0, 100) };
            var beyond = new List<SchedulerSlot> { Player(800), Enemy(0, 100) };

            var a = Sequence(capped, 10);
            var b = Sequence(beyond, 10);

            Assert.That(b.Select(x => x.Kind), Is.EqualTo(a.Select(x => x.Kind)));
        }

        [Test]
        public void Forecast_MatchesActualSequence()
        {
            var slots = new List<SchedulerSlot> { Player(100), Enemy(0, 60) };
            var predicted = TurnScheduler.Forecast(slots, 8);

            var actual = Sequence(new List<SchedulerSlot>(slots), 8);

            Assert.That(predicted, Is.EqualTo(actual));
        }

        [Test]
        public void Forecast_DoesNotMutateInput()
        {
            // UI 每帧刷新一次预测;若 Forecast 动了真实计量器,光是看着行动条就能把战斗推着走。
            var slots = new List<SchedulerSlot> { Player(100, meter: 30), Enemy(0, 60, meter: 70) };

            TurnScheduler.Forecast(slots, 20);

            Assert.That(slots[0].Meter, Is.EqualTo(30));
            Assert.That(slots[1].Meter, Is.EqualTo(70));
        }

        [Test]
        public void Forecast_ZeroCountReturnsEmpty()
        {
            var slots = new List<SchedulerSlot> { Player(100) };

            Assert.That(TurnScheduler.Forecast(slots, 0), Is.Empty);
        }

        // ===== Ticks(2026-08-17,每单位行动条):表现层据此定动画时长 =====

        [Test]
        public void Advance_ReportsTicksSpent()
        {
            // 玩家速度 100 从 0 起步,一拍就攒满
            var step = TurnScheduler.Advance(new List<SchedulerSlot> { Player(100), Enemy(0, 50) });

            Assert.That(step.Ticks, Is.EqualTo(1));
        }

        [Test]
        public void Advance_ReportsZeroTicksWhenSomeoneAlreadyFull()
        {
            // 已有人满格 → 不推进,直接消费。时长为 0,表现层跳过条动画
            var step = TurnScheduler.Advance(new List<SchedulerSlot> { Player(100, TurnScheduler.Threshold) });

            Assert.That(step.Ticks, Is.EqualTo(0));
        }

        [Test]
        public void Advance_SlowUnitNeedsMoreTicks()
        {
            // 速度 25(= MinSpeed)四拍才满 —— 这正是「慢的条涨得慢」在时长上的落点
            var step = TurnScheduler.Advance(new List<SchedulerSlot> { Enemy(0, TurnScheduler.MinSpeed) });

            Assert.That(step.Ticks, Is.EqualTo(4));
        }

        [Test]
        public void Advance_TicksMatchMeterGrowth()
        {
            // Ticks 与计量器增量必须自洽:非行动者涨的量 = 速度 × Ticks。
            // 这条守的是「时长与条的行程同一个来源」——两者一旦脱钩,条会在动画结束时跳一下。
            var slots = new List<SchedulerSlot> { Player(100), Enemy(0, 40) };
            var step = TurnScheduler.Advance(slots);

            Assert.That(step.Meters[1], Is.EqualTo(40 * step.Ticks));
        }
    }
}
