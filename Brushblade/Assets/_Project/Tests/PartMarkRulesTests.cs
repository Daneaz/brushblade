using System;
using System.Collections.Generic;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>Plan E3 Task 1:部件池「印」角标(PartsAndGraft 稿「角标个数 = 剩余次数」)。
    /// 同字部件按池中出现顺序,前 charges 张画;charges 大于池中同字张数时截断。</summary>
    public class PartMarkRulesTests
    {
        private static Func<string, int> Charges(Dictionary<string, int> map) =>
            id => map.TryGetValue(id, out int n) ? n : 0;

        [Test]
        public void Badges_FirstChargesOfSameChar()
        {
            var pool = new[] { "火", "山", "火", "火" };
            Assert.That(PartMarkRules.Badges(pool, Charges(new() { ["火"] = 2 })),
                Is.EqualTo(new[] { true, false, true, false }));
        }

        [Test]
        public void Badges_ChargeUsed_OnlyFirstLeft()
        {
            var pool = new[] { "火", "火", "火" };
            Assert.That(PartMarkRules.Badges(pool, Charges(new() { ["火"] = 1 })),
                Is.EqualTo(new[] { true, false, false }));
        }

        [Test]
        public void Badges_ChargesExceedPool_Truncated()
        {
            var pool = new[] { "山", "火" };
            Assert.That(PartMarkRules.Badges(pool, Charges(new() { ["火"] = 2, ["山"] = 1 })),
                Is.EqualTo(new[] { true, true }));
        }

        [Test]
        public void Badges_NoMarks_AllFalse_EmptyPool_Empty()
        {
            Assert.That(PartMarkRules.Badges(new[] { "火", "山" }, _ => 0), Is.EqualTo(new[] { false, false }));
            Assert.That(PartMarkRules.Badges(Array.Empty<string>(), _ => 3), Is.Empty);
        }

        [Test]
        public void Badges_QueriesEachCharOnce()
        {
            int calls = 0;
            PartMarkRules.Badges(new[] { "火", "火", "山", "火" }, id => { calls++; return 1; });
            Assert.That(calls, Is.EqualTo(2), "同字只查一次(读口每次遍历印记表)");
        }
    }
}
