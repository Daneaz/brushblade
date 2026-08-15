using System;
using System.Collections.Generic;

namespace Brushblade.Core
{
    /// <summary>行动者类别(2026-08-15,ATB 改造)。</summary>
    public enum ActorKind { Player, Summon, Enemy }

    /// <summary>一个行动者的引用。Player 的 Index 恒为 −1(与 BattleEvent.TargetIndex 的
    /// 「−1 = 玩家」同口径)。</summary>
    public readonly struct ActorRef : IEquatable<ActorRef>
    {
        public ActorKind Kind { get; }
        public int Index { get; }

        public ActorRef(ActorKind kind, int index)
        {
            Kind = kind;
            Index = index;
        }

        public static ActorRef Player => new(ActorKind.Player, -1);

        public bool Equals(ActorRef other) => Kind == other.Kind && Index == other.Index;
        public override bool Equals(object obj) => obj is ActorRef other && Equals(other);
        public override int GetHashCode() => ((int)Kind * 397) ^ Index;
        public override string ToString() => Kind == ActorKind.Player ? "Player" : $"{Kind}[{Index}]";
    }

    /// <summary>调度器的输入槽:一个参战单位的速度与计量器快照。
    /// <paramref name="Priority"/> 是并列时的排序主键(小者先),由 BattleEngine 按
    /// 「玩家 0 / 召唤物 1 / Buff 敌 2 / 其余敌 3」填 —— 调度器不认识 EnemyAbility。</summary>
    public readonly struct SchedulerSlot
    {
        public ActorRef Actor { get; }
        public int Speed { get; }
        public int Meter { get; }
        public int Priority { get; }

        public SchedulerSlot(ActorRef actor, int speed, int meter, int priority)
        {
            Actor = actor;
            Speed = speed;
            Meter = meter;
            Priority = priority;
        }
    }

    /// <summary>一次推进的结果:谁行动,以及推进后全场的计量器(与入参 slots 同序,
    /// 行动者的那一格已经扣掉 Threshold)。</summary>
    public readonly struct SchedulerStep
    {
        public ActorRef Actor { get; }
        public IReadOnlyList<int> Meters { get; }

        public SchedulerStep(ActorRef actor, IReadOnlyList<int> meters)
        {
            Actor = actor;
            Meters = meters;
        }
    }

    /// <summary>行动调度器(2026-08-15,ATB 改造)。**无状态**:计量器存在各单位身上
    /// (EnemyState.ActionMeter / SummonState.ActionMeter / BattleEngine 的玩家计量器),
    /// 本类只做纯计算 —— 这也是 Forecast 不可能误改真实状态的结构性保证。
    ///
    /// 算法(spec §4.1):
    ///   1. 若已有单位 Meter >= Threshold,按 (Priority, 下标) 取第一个,扣 Threshold,返回
    ///   2. 否则推进 ticks = min ceil((Threshold − Meter) / Speed),全场累积,回到 1
    ///
    /// ⚠ 全程整数。不许引入 float/double 中间值 —— Unity Mono 与 .NET 8 的中间精度不同,
    /// 配上取整会整级翻车(2026-08-15 护甲缩放刚踩过)。</summary>
    public static class TurnScheduler
    {
        /// <summary>计量器满值:攒够即行动一次。</summary>
        public const int Threshold = 100;

        /// <summary>有效速度下限。**不是保守取值,是防死锁**:速度 0 的单位永远攒不满 →
        /// 永远轮不到 → 它自己那拍的状态递减永远不跑 → 减速永远解不了(spec 口径 8)。
        /// 25 = 基准的 1/4,四拍动一次。</summary>
        public const int MinSpeed = 25;

        /// <summary>有效速度上限:防养成与叠加失控。CTB 下没有「单回合行动次数封顶」这回事,
        /// 上限全靠这条(旧的 MaxActionsPerTurn = 2 随本次改造删除)。</summary>
        public const int MaxSpeed = 400;

        public static int ClampSpeed(int raw) => Math.Clamp(raw, MinSpeed, MaxSpeed);

        public static SchedulerStep Advance(IReadOnlyList<SchedulerSlot> slots)
        {
            if (slots == null || slots.Count == 0)
                throw new InvalidOperationException("调度器至少需要一个参战单位");

            var meters = new int[slots.Count];
            for (int i = 0; i < slots.Count; i++) meters[i] = slots[i].Meter;

            int winner = FirstFull(slots, meters);
            if (winner < 0)
            {
                int ticks = TicksUntilAnyFull(slots, meters);
                for (int i = 0; i < slots.Count; i++)
                    meters[i] += ClampSpeed(slots[i].Speed) * ticks;
                winner = FirstFull(slots, meters);
            }

            meters[winner] -= Threshold;
            return new SchedulerStep(slots[winner].Actor, meters);
        }

        /// <summary>满格者里 (Priority, 下标) 最小的那个;没有则 −1。</summary>
        private static int FirstFull(IReadOnlyList<SchedulerSlot> slots, int[] meters)
        {
            int best = -1;
            for (int i = 0; i < slots.Count; i++)
            {
                if (meters[i] < Threshold) continue;
                if (best < 0 || slots[i].Priority < slots[best].Priority) best = i;
            }
            return best;
        }

        /// <summary>推进多少 tick 才有人攒满。至少 1 —— 返回 0 会让 Advance 死循环。</summary>
        private static int TicksUntilAnyFull(IReadOnlyList<SchedulerSlot> slots, int[] meters)
        {
            int best = int.MaxValue;
            for (int i = 0; i < slots.Count; i++)
            {
                int need = Threshold - meters[i];
                int speed = ClampSpeed(slots[i].Speed);
                int ticks = (need + speed - 1) / speed; // ceil,整数除
                if (ticks < best) best = ticks;
            }
            return Math.Max(1, best);
        }

        /// <summary>向前预测 count 个行动者,**不改动入参**——在本地拷贝上推演。
        ///
        /// ⚠ 这是「若场面不变」的预测:怪死了、召唤物上场、减速生效都会让实际序列偏离。
        /// 表现层每次重绘都要重新调用,不许缓存。</summary>
        public static IReadOnlyList<ActorRef> Forecast(IReadOnlyList<SchedulerSlot> slots, int count)
        {
            var result = new List<ActorRef>();
            if (slots == null || slots.Count == 0 || count <= 0) return result;

            var working = new SchedulerSlot[slots.Count];
            for (int i = 0; i < slots.Count; i++) working[i] = slots[i];

            for (int n = 0; n < count; n++)
            {
                var step = Advance(working);
                result.Add(step.Actor);
                for (int i = 0; i < working.Length; i++)
                    working[i] = new SchedulerSlot(working[i].Actor, working[i].Speed,
                        step.Meters[i], working[i].Priority);
            }
            return result;
        }

    }
}
