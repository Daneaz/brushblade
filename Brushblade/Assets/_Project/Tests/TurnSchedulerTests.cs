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
        // 优先级:玩家 0 / 召唤物 1 / Buff 敌 2 / 其余敌 3(与 BattleEngine 的填法一致)
        private static SchedulerSlot Player(int speed, int meter = 0) =>
            new(ActorRef.Player, speed, meter, 0);

        private static SchedulerSlot Enemy(int index, int speed, int meter = 0) =>
            new(new ActorRef(ActorKind.Enemy, index), speed, meter, 3);

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
            // 我 100 / 敌 60:五个 tick 内恰好 5 次对 3 次(spec §4.1 的口径例)
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
            // 已经满格的不该再推进时间:否则同一 tick 内的第二个行动者会白拿一次累积
            var slots = new List<SchedulerSlot> { Player(100, meter: 120), Enemy(0, 100, meter: 110) };

            var step = TurnScheduler.Advance(slots);

            Assert.That(step.Actor.Kind, Is.EqualTo(ActorKind.Player));
            Assert.That(step.Meters[0], Is.EqualTo(20), "行动者扣 100");
            Assert.That(step.Meters[1], Is.EqualTo(110), "其他人原地不动");
        }
    }
}
