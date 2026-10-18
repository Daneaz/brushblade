using System;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>广纳(2026-10-18 改口径):回合开始掉字时若字库 ≤ 1 张,掉字数 +N。</summary>
    public class EmptyLibraryDrawTests
    {
        private static RecipeGraph Graph() => new(new[]
        {
            new CharDef("刀", Element.Metal, effects: new[] { new EffectDef(EffectKind.DamageSingle, 1) }),
        });

        private static BattleEngine Engine(int extra, string[] library, int low = 0) =>
            new(Graph(), new BattleConfig
                {
                    DropTable = new[] { "金" }, PlayerMaxHp = 5000, ApPerTurn = 20, LibraryCapacity = 9,
                    UnlockedChars = new[] { "刀" }, DropsPerTurn = 1, EmptyLibraryExtraDraws = extra,
                    LowLibraryExtraDraws = low,
                },
                library, Array.Empty<string>(),
                new[] { new EnemyDef("靶", Element.Heart, 900000, 0) }, seed: 1);

        [TestCase(0, 1)]
        [TestCase(1, 2)]
        [TestCase(2, 3)]
        public void EmptyLibrary_DrawsExtra(int extra, int expected)
        {
            var e = Engine(extra, new[] { "刀" });
            for (int i = 0; i < 20 && e.Library.Count > 0; i++) e.Cast("刀", 0); // 打空(构造时的开局掉字也算在内)
            Assert.That(e.Library.Count, Is.EqualTo(0));
            e.EndTurn();
            Assert.That(e.Library.Count, Is.EqualTo(expected));
        }

        [TestCase(0, 2)]
        [TestCase(1, 3)]
        [TestCase(2, 4)]
        public void OneCardLeft_DrawsExtra(int extra, int expected)
        {
            var e = Engine(extra, new[] { "刀", "刀" });
            e.Cast("刀", 0);                 // 构造时已掉 1,共 3 张,打两张剩 1
            e.Cast("刀", 0);
            Assert.That(e.Library.Count, Is.EqualTo(1));
            e.EndTurn();
            Assert.That(e.Library.Count, Is.EqualTo(expected), "1 + 正常掉 1 + 广纳");
        }

        [Test]
        public void TwoCardsLeft_DrawsNormally()
        {
            var e = Engine(2, new[] { "刀", "刀" });
            e.Cast("刀", 0);                 // 构造时已掉 1,共 3 张,打一张剩 2
            Assert.That(e.Library.Count, Is.EqualTo(2));
            e.EndTurn();
            Assert.That(e.Library.Count, Is.EqualTo(3), "2 + 正常掉 1,不吃广纳");
        }

        // ---- 集腋(spec 2026-10-18 §7):字库不足 3 张(≤ 2)掉字 +N,与广纳叠加 ----

        /// <summary>广纳 + 集腋各 1(Review Focus 3):字库 0/1 张 +2、2 张 +1、≥3 张 0。
        /// leftAfterCasts = 打完后剩几张;构造时开局已掉 1,故起手给 leftAfterCasts 张就剩 leftAfterCasts + 1,再打 1 张。</summary>
        [TestCase(0, 0 + 1 + 2)]
        [TestCase(1, 1 + 1 + 2)]
        [TestCase(2, 2 + 1 + 1)]
        [TestCase(3, 3 + 1)]
        public void WideAndLow_Stack(int left, int expected)
        {
            var library = new string[Math.Max(left, 1)];
            for (int i = 0; i < library.Length; i++) library[i] = "刀";
            var e = Engine(1, library, low: 1);
            for (int i = 0; i < 20 && e.Library.Count > left; i++) e.Cast("刀", 0);
            Assert.That(e.Library.Count, Is.EqualTo(left), "前提");
            e.EndTurn();
            Assert.That(e.Library.Count, Is.EqualTo(expected));
        }

        [TestCase(2, 2 + 1 + 1)]
        [TestCase(3, 3 + 1)]
        public void LowAlone_TriggersBelowThree(int left, int expected)
        {
            var library = new string[left];
            for (int i = 0; i < library.Length; i++) library[i] = "刀";
            var e = Engine(0, library, low: 1);
            for (int i = 0; i < 20 && e.Library.Count > left; i++) e.Cast("刀", 0);
            Assert.That(e.Library.Count, Is.EqualTo(left), "前提");
            e.EndTurn();
            Assert.That(e.Library.Count, Is.EqualTo(expected), "集腋单点:不足 3 张 +1");
        }
    }
}
