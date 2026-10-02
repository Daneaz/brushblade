using System;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>涌泉/积土(spec 2026-10-02 §2.4):威力 +150%,返还 ⌊n × 50%⌋ 层。</summary>
    public class SpendRefundTests
    {
        private static RecipeGraph Graph() => new(new[]
        {
            new CharDef("崩", Element.Earth, effects: new[] { new EffectDef(EffectKind.SpendHeft, 10) }),
            new CharDef("涌", Element.Water, effects: new[] { new EffectDef(EffectKind.SpendWellspring, 10) }),
        });

        private static BattleEngine Engine(StatusKind kind, int stacks, int heftPct, int wellPct, string card) =>
            new(Graph(), new BattleConfig
                {
                    DropTable = new[] { "土" }, PlayerMaxHp = 5000, ApPerTurn = 20,
                    HeftSpendPercent = heftPct, WellspringSpendPercent = wellPct,
                },
                new[] { card }, Array.Empty<string>(),
                new[] { new EnemyDef("靶", Element.Heart, 900000, 0) }, seed: 1,
                startingStatuses: stacks > 0 ? new[] { new StatusEffect
                {
                    Kind = kind, Polarity = StatusPolarity.Buff, Magnitude = stacks, TurnsLeft = -1,
                } } : null);

        private static (int dmg, int left) Fire(StatusKind kind, int stacks, int heftPct, int wellPct, string card)
        {
            var e = Engine(kind, stacks, heftPct, wellPct, card);
            int before = e.Enemies[0].Hp;
            Assert.That(e.Cast(card), Is.EqualTo(BattleError.None));
            return (before - e.Enemies[0].Hp, e.PlayerStatuses.TotalMagnitude(kind));
        }

        [TestCase(10, 5)]
        [TestCase(7, 3)]
        [TestCase(1, 0)]
        public void Heft_RefundsHalfRoundedDown(int stacks, int expectedLeft)
        {
            var (_, left) = Fire(StatusKind.Heft, stacks, 150, 0, "崩");
            Assert.That(left, Is.EqualTo(expectedLeft));
        }

        [Test]
        public void Heft_PowerIsTwoAndAHalfTimes()
        {
            var (boosted, _) = Fire(StatusKind.Heft, 10, 150, 0, "崩");
            var (plain, plainLeft) = Fire(StatusKind.Heft, 10, 0, 0, "崩");
            Assert.That(boosted, Is.EqualTo(plain * 250 / 100).Within(1));
            Assert.That(plainLeft, Is.EqualTo(0), "没点积土不返还");
        }

        [Test]
        public void Wellspring_RefundsHalfAndBoosts()
        {
            var (boosted, left) = Fire(StatusKind.Wellspring, 10, 0, 150, "涌");
            var (plain, _) = Fire(StatusKind.Wellspring, 10, 0, 0, "涌");
            Assert.That(left, Is.EqualTo(5));
            Assert.That(boosted, Is.EqualTo(plain * 250 / 100).Within(1));
        }

        [Test]
        public void HeftPerk_DoesNotLeakIntoWellspring()
        {
            var (_, left) = Fire(StatusKind.Wellspring, 10, 150, 0, "涌");
            Assert.That(left, Is.EqualTo(0), "积土只管厚");
        }

        [Test]
        public void ZeroStacks_StaysIdle()
        {
            var (dmg, left) = Fire(StatusKind.Heft, 0, 150, 0, "崩");
            Assert.That(dmg, Is.EqualTo(0));
            Assert.That(left, Is.EqualTo(0));
        }
    }
}
