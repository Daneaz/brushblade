using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>带种子 RNG:确定性 + 可存续(断点续爬要能从存档里的 state 接着摇,
    /// 否则挂起再进的随机流会与不中断时分叉)。</summary>
    public class GameRandomTests
    {
        private static List<int> Take(GameRandom random, int count, int bound = 100)
        {
            var result = new List<int>();
            for (int i = 0; i < count; i++) result.Add(random.Next(bound));
            return result;
        }

        [Test]
        public void SameSeed_SameSequence()
        {
            Assert.That(Take(new GameRandom(42), 50), Is.EqualTo(Take(new GameRandom(42), 50)));
        }

        [Test]
        public void DifferentSeeds_DifferentSequences()
        {
            Assert.That(Take(new GameRandom(1), 50), Is.Not.EqualTo(Take(new GameRandom(2), 50)));
        }

        [Test]
        public void ResumeFromState_ContinuesTheSameStream() // 断点续爬的核心保证
        {
            var uninterrupted = new GameRandom(7);
            Take(uninterrupted, 10);                 // 先摇 10 个
            var expected = Take(uninterrupted, 20);  // 接下来的 20 个

            var saved = new GameRandom(7);
            Take(saved, 10);
            var resumed = GameRandom.FromState(saved.State); // 存档 → 读档
            Assert.That(Take(resumed, 20), Is.EqualTo(expected));
        }

        [Test]
        public void Next_StaysInRange()
        {
            var random = new GameRandom(9);
            for (int i = 0; i < 500; i++)
            {
                int value = random.Next(7);
                Assert.That(value, Is.InRange(0, 6));
            }
        }

        [Test]
        public void Next_One_IsAlwaysZero()
        {
            var random = new GameRandom(3);
            for (int i = 0; i < 20; i++) Assert.That(random.Next(1), Is.EqualTo(0));
        }

        [Test]
        public void NextZeroOrOne_DoesNotAdvanceState()
        {
            // maxExclusive <= 1 直接 return 0,不调 NextBits、不碰 _state(GameRandom.cs:49)。
            // 这条性质是 Targeting.PickAllyTarget 单候选短路的第一层保证——候选池只剩
            // PlayerTarget 一个时即便真去调 Next(1),随机流也不会位移;短路本身只是
            // 纵深防御的第二层。这条性质一旦被改掉(比如给 maxExclusive == 1 也走
            // NextBits),短路就从「双保险」降级成「唯一防线」。
            var random = new GameRandom(3);
            uint before = random.State;
            random.Next(0);
            random.Next(1);
            Assert.That(random.State, Is.EqualTo(before), "Next(0)/Next(1) 不该推进内部状态");
        }

        [Test]
        public void Next_CoversWholeRange() // 别退化成常数或只落半区
        {
            var random = new GameRandom(11);
            var seen = new HashSet<int>();
            for (int i = 0; i < 2000; i++) seen.Add(random.Next(10));
            Assert.That(seen.Count, Is.EqualTo(10));
        }

        [Test]
        public void Next_RoughlyUniform() // 2000 次分 10 档,每档应在均值 200 附近
        {
            var random = new GameRandom(2024);
            var counts = new int[10];
            for (int i = 0; i < 2000; i++) counts[random.Next(10)]++;
            Assert.That(counts.Min(), Is.GreaterThan(120));
            Assert.That(counts.Max(), Is.LessThan(300));
        }

        [Test]
        public void Next_LargeBound_Works() // RunEngine 用 Next(int.MaxValue) 派生子种子
        {
            var random = new GameRandom(5);
            var seen = new HashSet<int>();
            for (int i = 0; i < 100; i++)
            {
                int value = random.Next(int.MaxValue);
                Assert.That(value, Is.InRange(0, int.MaxValue - 1));
                seen.Add(value);
            }
            Assert.That(seen.Count, Is.EqualTo(100)); // 不该撞车
        }

        [Test]
        public void Pick_ReturnsMembers_AndVaries()
        {
            var random = new GameRandom(4);
            var items = new[] { "甲", "乙", "丙" };
            var seen = new HashSet<string>();
            for (int i = 0; i < 200; i++)
            {
                var pick = random.Pick(items);
                Assert.That(items, Has.Member(pick));
                seen.Add(pick);
            }
            Assert.That(seen.Count, Is.EqualTo(3));
        }

        [Test]
        public void ZeroSeed_DoesNotDegenerate() // xorshift 的 0 状态会永远吐 0
        {
            var random = new GameRandom(0);
            var seen = new HashSet<int>();
            for (int i = 0; i < 200; i++) seen.Add(random.Next(10));
            Assert.That(seen.Count, Is.GreaterThan(1));
        }
    }
}
