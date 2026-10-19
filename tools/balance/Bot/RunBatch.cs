using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Brushblade.Balance.Bot
{
    /// <summary>一批局的调度(Plan F · A2 §1):<paramref name="expired"/> 为真后不再开新局,已开的跑完。</summary>
    public static class RunBatch
    {
        /// <summary>跑 <paramref name="count"/> 个局(下标 0..count−1),返回每局是否跑完。
        /// 每局开跑前检查一次 <paramref name="expired"/>;结果由 <paramref name="job"/> 自己按下标写回,与调度无关。</summary>
        public static bool[] Run(int count, Action<int> job, Func<bool> expired, bool parallel = true)
        {
            var done = new bool[count];
            void One(int i)
            {
                if (expired()) return;
                job(i);
                done[i] = true;
            }
            if (parallel)
                Parallel.ForEach(Partitioner.Create(Enumerable.Range(0, count).ToList(), loadBalance: true), One);
            else
                for (int i = 0; i < count; i++) One(i);
            return done;
        }
    }

    /// <summary>超时截断后的统计口径:只算跑完的种子。</summary>
    public static class Truncation
    {
        /// <summary>按种子下标顺序留下跑完的局(未跑的槽位是 null)。</summary>
        public static ClimbResult[] Completed(IReadOnlyList<ClimbResult> slots) => slots.Where(r => r != null).ToArray();

        /// <summary>表头前的截断说明;每行都跑满时返回 null(不打印,输出与不截断相同)。</summary>
        public static string Note(IEnumerable<(string row, int done, int total)> rows, double maxMinutes)
        {
            var list = rows.ToList();
            if (list.All(r => r.done == r.total)) return null;
            return $"⚠ 超时截断(--max-minutes {maxMinutes:0.##}):每行只统计跑完的种子 —— "
                   + string.Join(" · ", list.Select(r => $"{r.row} {r.done}/{r.total}"));
        }
    }

    /// <summary>进度输出节流(走 stderr):每跨过 5% 报一次,或距上次报告满 30 秒。</summary>
    public static class ProgressGate
    {
        public const double HeartbeatSeconds = 30;

        public static bool ShouldReport(int done, int total, double elapsed, int lastDone, double lastElapsed)
        {
            if (total <= 0) return false;
            if (done * 20L / total > lastDone * 20L / total) return true;
            return elapsed - lastElapsed >= HeartbeatSeconds;
        }

        public static string Line(int done, int total, double elapsed)
        {
            string eta = done == 0 ? "—" : $"{elapsed / done * (total - done):F0}s";
            return $"进度 {done}/{total} · 已用 {elapsed:F0}s · 预计剩余 {eta}";
        }
    }
}
