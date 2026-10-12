using System;
using System.Collections;
using System.Collections.Generic;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>嵌套迭代器展平驱动(Plan E4 Task 2,Juice.SafeRun 的纯 C# 内核)。
    /// 每一步 MoveNext 对应 Unity 的一帧:断言「每步吐出的值」就是断言节拍。</summary>
    public class RoutineRunnerTests
    {
        /// <summary>代替 Unity 的 YieldInstruction(WaitForSeconds 等):不是 IEnumerator,原样交还。</summary>
        private sealed class FakeInstruction { }

        /// <summary>代替 CustomYieldInstruction:是 IEnumerator,但谓词说不展平,也原样交还。</summary>
        private sealed class FakeCustom : IEnumerator
        {
            public object Current => null;
            public bool MoveNext() => false;
            public void Reset() { }
        }

        private static readonly Func<object, bool> NotCustom = o => o is IEnumerator && !(o is FakeCustom);

        /// <summary>把驱动器逐步跑完,记下每步吐出的值(一步 = 一帧)。</summary>
        private static List<object> Drain(IEnumerator routine, int maxSteps = 100)
        {
            var steps = new List<object>();
            while (routine.MoveNext())
            {
                steps.Add(routine.Current);
                if (steps.Count > maxSteps) Assert.Fail("驱动器没有结束");
            }
            return steps;
        }

        private static IEnumerator Leaf(List<string> log, string name, int frames)
        {
            log.Add(name + ">");
            for (int i = 0; i < frames; i++) yield return null;
            log.Add(name + "<");
        }

        private static IEnumerator Mid(List<string> log)
        {
            log.Add("mid>");
            yield return Leaf(log, "leaf", 2);
            log.Add("mid-after");
            yield return Leaf(log, "empty", 0);   // 立刻结束的子程:父层同一步接着跑
            log.Add("mid<");
        }

        private static IEnumerator Outer(List<string> log)
        {
            log.Add("outer>");
            yield return Mid(log);
            log.Add("outer<");
        }

        [Test]
        public void ThreeLevels_FlattenedSameFramesAsNestedCoroutines()
        {
            var log = new List<string>();
            int done = 0;
            var steps = Drain(RoutineRunner.Guard(Outer(log), null, _ => Assert.Fail("不该报错"), () => done++));

            // 只有叶子的两次 yield null 交给外面:进出子程不额外占帧(与 Unity 嵌套协程同:子程当步开跑,结束当步回父层)
            Assert.That(steps.Count, Is.EqualTo(2));
            Assert.That(steps[0], Is.Null);
            Assert.That(steps[1], Is.Null);
            Assert.That(string.Join(",", log),
                Is.EqualTo("outer>,mid>,leaf>,leaf<,mid-after,empty>,empty<,mid<,outer<"));
            Assert.That(done, Is.EqualTo(1));
        }

        [Test]
        public void FirstStep_RunsUntilFirstLeafYield()
        {
            var log = new List<string>();
            var run = RoutineRunner.Guard(Outer(log), null, null, null);
            Assert.That(run.MoveNext(), Is.True);
            Assert.That(string.Join(",", log), Is.EqualTo("outer>,mid>,leaf>"));
        }

        [Test]
        public void NormalEnd_OnDoneOnlyOnLastStep_AndOnlyOnce()
        {
            var log = new List<string>();
            int done = 0;
            var run = RoutineRunner.Guard(Outer(log), null, null, () => done++);
            Assert.That(run.MoveNext(), Is.True);
            Assert.That(done, Is.EqualTo(0));
            Assert.That(run.MoveNext(), Is.True);
            Assert.That(done, Is.EqualTo(0), "动画没播完不许回调");
            Assert.That(run.MoveNext(), Is.False);
            Assert.That(done, Is.EqualTo(1));
            Assert.That(run.MoveNext(), Is.False);
            Assert.That(done, Is.EqualTo(1), "结束后再推也不再回调");
        }

        private static IEnumerator Thrower(List<string> log)
        {
            log.Add("thrower>");
            yield return null;
            throw new InvalidOperationException("boom");
        }

        private static IEnumerator MidThrow(List<string> log)
        {
            yield return Thrower(log);
            log.Add("mid-after");   // 不该跑到
        }

        private static IEnumerator OuterThrow(List<string> log)
        {
            yield return MidThrow(log);
            log.Add("outer-after"); // 不该跑到
        }

        [Test]
        public void InnerThrows_ErrorReported_DoneOnce_ParentsAbandoned()
        {
            var log = new List<string>();
            var errors = new List<Exception>();
            int done = 0;
            var steps = Drain(RoutineRunner.Guard(OuterThrow(log), null, errors.Add, () => done++));

            Assert.That(steps.Count, Is.EqualTo(1));
            Assert.That(errors.Count, Is.EqualTo(1));
            Assert.That(errors[0], Is.InstanceOf<InvalidOperationException>());
            Assert.That(done, Is.EqualTo(1));
            Assert.That(string.Join(",", log), Is.EqualTo("thrower>"));
        }

        private static IEnumerator ThrowAtOnce()
        {
            throw new InvalidOperationException("first step");
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
        }

        [Test]
        public void ThrowOnFirstStep_DoneCalledInsideFirstMoveNext()
        {
            int done = 0, errors = 0;
            var run = RoutineRunner.Guard(ThrowAtOnce(), null, _ => errors++, () => done++);
            Assert.That(run.MoveNext(), Is.False);
            Assert.That(errors, Is.EqualTo(1));
            Assert.That(done, Is.EqualTo(1));
        }

        private static IEnumerator YieldsInstructions(FakeInstruction wait, FakeCustom custom)
        {
            yield return wait;
            yield return custom;
            yield return null;
        }

        [Test]
        public void NonNestedValues_PassThroughUntouched()
        {
            var wait = new FakeInstruction();
            var custom = new FakeCustom();
            int done = 0;
            var steps = Drain(RoutineRunner.Guard(YieldsInstructions(wait, custom), NotCustom, null, () => done++));

            Assert.That(steps.Count, Is.EqualTo(3));
            Assert.That(ReferenceEquals(steps[0], wait), Is.True, "YieldInstruction 原样交还");
            Assert.That(ReferenceEquals(steps[1], custom), Is.True, "谓词排除的 IEnumerator(CustomYieldInstruction)原样交还");
            Assert.That(steps[2], Is.Null);
            Assert.That(done, Is.EqualTo(1));
        }

        [Test]
        public void NullRoot_DoneOnce()
        {
            int done = 0;
            var steps = Drain(RoutineRunner.Guard(null, null, null, () => done++));
            Assert.That(steps.Count, Is.EqualTo(0));
            Assert.That(done, Is.EqualTo(1));
        }
    }
}
