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
    /// 「玩家 0 / 召唤物 1 / Buff 敌 2 / 其余敌 3」填 —— 调度器不认识 EnemyAbility。
    ///
    /// ⚠ **玩家排最先**(2026-08-17,推翻 2026-08-15 的反向口径):同速并列时
    /// 玩家 0 → 召唤物 1 → Buff 敌 2 → 其余敌 3。旧口径让玩家排最后,并警告「方向不要
    /// 再调回去」—— 那个诊断把病根归错了:病根是「玩家优先 + 构造函数给的免费先手」
    /// 这个组合。免费先手已随本次改造删除,玩家优先因此不再需要任何记账补偿。
    /// 完整推演与实测数字见 BattleEngine.BuildSlots 的注释与
    /// docs/superpowers/specs/2026-08-17-开场走调度与同速优先级反转-design.md §二。</summary>
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

        /// <summary>本次跨了多少拍;已有人满格、无需推进时为 0(2026-08-17,每单位行动条)。
        /// 表现层据此定行动条动画时长 —— 时长 = Ticks × BaseMs,只有这样「速度 100 的单位
        /// 从 0% 攒到 100%」才恒等于 BaseMs,条才是严格匀速的(spec §6.2)。
        /// 算出来本来就有(TicksUntilAnyFull),此前算完即丢。</summary>
        public int Ticks { get; }

        public SchedulerStep(ActorRef actor, IReadOnlyList<int> meters, int ticks)
        {
            Actor = actor;
            Meters = meters;
            Ticks = ticks;
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
        /// <summary>计量器满值:攒够即行动一次。
        ///
        /// ⚠ **10000 而不是 100 是时间精度,不是数值口径**(2026-09-03)。一拍给的量恒等于
        /// 速度,所以 Threshold 就是「一次行动要几拍」的刻度:取 100 时速度 150 的单位一拍
        /// 直接涨 150,超出的 50 点条上钳在 100% 完全看不见,攒两拍白送一次行动 ——
        /// 实机表现就是「行动条一动不动,桤 连打两下」(用户 2026-09-03 报的 bug)。
        /// 取 10000 后一拍最多涨 4%(MaxSpeed / Threshold),溢出几乎归零,每次出手前
        /// 条都真的走到 100%,「谁先满谁先动」在画面上重新成立。
        ///
        /// 速度的**语义没有变**:速度仍是「每拍涨多少」,100 仍是基准,速率比仍是速度比,
        /// 变的只是一拍多细(1/100 拍)。表现层的 BattleView.ActionBarBaseMs 是配套的另一半
        /// (500ms/拍 → 5ms/拍),两者必须同时改,否则一次推进的动画会长 100 倍。</summary>
        public const int Threshold = 10000;

        /// <summary>有效速度下限。**不是保守取值,是防死锁**:速度 0 的单位永远攒不满 →
        /// 永远轮不到 → 它自己那拍的状态递减永远不跑 → 减速永远解不了(spec 口径 8)。
        /// 25 = 基准的 1/4,攒一次行动要四倍时间。</summary>
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

            int ticks = 0;
            int winner = FirstFull(slots, meters);
            if (winner < 0)
            {
                ticks = TicksUntilAnyFull(slots, meters);
                for (int i = 0; i < slots.Count; i++)
                    meters[i] += ClampSpeed(slots[i].Speed) * ticks;
                winner = FirstFull(slots, meters);
            }

            meters[winner] -= Threshold;
            return new SchedulerStep(slots[winner].Actor, meters, ticks);
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
