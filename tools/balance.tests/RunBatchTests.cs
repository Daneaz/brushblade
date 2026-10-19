using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Brushblade.Balance.Bot;
using NUnit.Framework;

namespace Brushblade.Balance.Tests
{
    /// <summary>慢档跑批工具(Plan F · A2 §1):截断后只统计跑完的种子、进度节流、到时不开新局。</summary>
    public class RunBatchTests
    {
        [Test]
        public void Completed_DropsUnfinishedSeeds_KeepsSeedOrder()
        {
            var slots = new ClimbResult[]
            {
                new ClimbResult { Seed = 0, DeathDepth = 10 }, null,
                new ClimbResult { Seed = 2, DeathDepth = 30 }, null,
            };
            var done = Truncation.Completed(slots);
            Assert.That(done.Select(r => r.Seed).ToArray(), Is.EqualTo(new[] { 0, 2 }));
            Assert.That(done.Average(r => r.DeathDepth), Is.EqualTo(20.0));
        }

        [Test]
        public void Note_NullWhenEveryRowComplete()
        {
            Assert.That(Truncation.Note(new[] { ("单系·木 Lv5", 30, 30), ("单系·金 Lv5", 30, 30) }, 10), Is.Null);
        }

        [Test]
        public void Note_ListsDoneOverTotalPerRow()
        {
            string note = Truncation.Note(new[] { ("单系·木 Lv5", 12, 30), ("单系·金 Lv5", 30, 30) }, 10);
            Assert.That(note, Does.Contain("超时截断"));
            Assert.That(note, Does.Contain("单系·木 Lv5 12/30"));
            Assert.That(note, Does.Contain("单系·金 Lv5 30/30"));
        }

        [Test]
        public void ProgressGate_ReportsOnEachFivePercentStep()
        {
            // 100 局:跨过 5 / 10 … 才报;同一档内不重复报
            Assert.That(ProgressGate.ShouldReport(done: 4, total: 100, elapsed: 1, lastDone: 0, lastElapsed: 0), Is.False);
            Assert.That(ProgressGate.ShouldReport(5, 100, 1, 0, 0), Is.True);
            Assert.That(ProgressGate.ShouldReport(9, 100, 2, 5, 1), Is.False);
            Assert.That(ProgressGate.ShouldReport(10, 100, 2, 5, 1), Is.True);
            Assert.That(ProgressGate.ShouldReport(100, 100, 3, 99, 2), Is.True);
        }

        [Test]
        public void ProgressGate_ReportsAtLeastEvery30Seconds()
        {
            Assert.That(ProgressGate.ShouldReport(6, 1000, 29.9, 5, 0), Is.False);
            Assert.That(ProgressGate.ShouldReport(6, 1000, 30.0, 5, 0), Is.True);
            // 计时触发时局数没变也要报(长局卡住时仍有心跳)
            Assert.That(ProgressGate.ShouldReport(5, 1000, 61, 5, 30), Is.True);
        }

        [Test]
        public void ProgressGate_LineFormat()
        {
            Assert.That(ProgressGate.Line(45, 900, 31.2), Is.EqualTo("进度 45/900 · 已用 31s · 预计剩余 593s"));
            Assert.That(ProgressGate.Line(0, 900, 3), Is.EqualTo("进度 0/900 · 已用 3s · 预计剩余 —"));
        }

        [Test]
        public void Run_NotExpired_RunsEveryJob()
        {
            int ran = 0;
            var done = RunBatch.Run(20, _ => Interlocked.Increment(ref ran), expired: () => false);
            Assert.That(ran, Is.EqualTo(20));
            Assert.That(done.All(d => d), Is.True);
        }

        [Test]
        public void Run_Expired_StartsNoNewJob()
        {
            int ran = 0;
            var done = RunBatch.Run(20, _ => Interlocked.Increment(ref ran), expired: () => true);
            Assert.That(ran, Is.EqualTo(0));
            Assert.That(done.Any(d => d), Is.False);
        }

        [Test]
        public void Run_ExpiresMidway_FinishedJobsStayCounted()
        {
            // 串行可判定:第 5 局跑完后到时,其余不再开
            int ran = 0;
            var done = RunBatch.Run(20, _ => ran++, expired: () => ran >= 5, parallel: false);
            Assert.That(ran, Is.EqualTo(5));
            Assert.That(done.Count(d => d), Is.EqualTo(5));
            Assert.That(done.Take(5).All(d => d), Is.True);
        }
    }
}
