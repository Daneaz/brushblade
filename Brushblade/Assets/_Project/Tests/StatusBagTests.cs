using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>统一状态容器(2026-08-04):P1~P3 的驱散/净化都要靠它枚举与分类。</summary>
    public class StatusBagTests
    {
        private static StatusEffect Burn(int stacks) => new()
        {
            Kind = StatusKind.Burn, Polarity = StatusPolarity.Debuff, Magnitude = stacks, TurnsLeft = -1,
        };

        private static StatusEffect Reduction(string sourceId, int percent) => new()
        {
            Kind = StatusKind.DefenseBuff, Polarity = StatusPolarity.Buff,
            Magnitude = percent, TurnsLeft = -1, SourceId = sourceId,
        };

        [Test]
        public void Apply_SameKindAndSource_RefreshesInsteadOfStacking()
        {
            var bag = new StatusBag();
            bag.Apply(Reduction("铠", 20));
            bag.Apply(Reduction("铠", 30));   // 同字重复施放,不同量值

            Assert.That(bag.All.Count, Is.EqualTo(1));
            Assert.That(bag.TotalMagnitude(StatusKind.DefenseBuff), Is.EqualTo(30), "后来者覆盖前者");
        }

        [Test]
        public void Apply_SameKindDifferentSource_BothKept()
        {
            var bag = new StatusBag();
            bag.Apply(Reduction("铠", 20));
            bag.Apply(Reduction("崟", 15));

            Assert.That(bag.All.Count, Is.EqualTo(2));
            Assert.That(bag.TotalMagnitude(StatusKind.DefenseBuff), Is.EqualTo(35));
        }

        [Test]
        public void TickTurns_ExpiresAtZero_KeepsPersistent()
        {
            var bag = new StatusBag();
            bag.Apply(new StatusEffect
            {
                Kind = StatusKind.Bleed, Polarity = StatusPolarity.Debuff, Magnitude = 3, TurnsLeft = 1,
            });
            bag.Apply(Reduction("铠", 20)); // TurnsLeft = -1,段内持久

            bag.TickTurns();

            Assert.That(bag.Has(StatusKind.Bleed), Is.False);          // 到期移除
            Assert.That(bag.Has(StatusKind.DefenseBuff), Is.True); // -1 不递减
        }

        [Test]
        public void RemoveAll_ByPolarity_OnlyTouchesThatSide()
        {
            var bag = new StatusBag();
            bag.Apply(Burn(3));
            bag.Apply(Reduction("铠", 20));

            Assert.That(bag.RemoveAll(StatusPolarity.Debuff), Is.EqualTo(1)); // 返回移除条数
            Assert.That(bag.Has(StatusKind.Burn), Is.False);
            Assert.That(bag.Has(StatusKind.DefenseBuff), Is.True);
        }

        [Test]
        public void Find_ReturnsNullWhenAbsent()
        {
            var bag = new StatusBag();
            Assert.That(bag.Find(StatusKind.Freeze), Is.Null);
            Assert.That(bag.TotalMagnitude(StatusKind.Freeze), Is.EqualTo(0));
        }

        [Test]
        public void CopyFrom_DeepCopies_SourceMutationDoesNotLeak()
        {
            var source = new StatusBag();
            source.Apply(Burn(3));

            var target = new StatusBag();
            target.CopyFrom(source.All);

            source.Find(StatusKind.Burn).Magnitude = 99;   // 改源

            Assert.That(target.TotalMagnitude(StatusKind.Burn), Is.EqualTo(3), "深拷贝:改源不该影响目标");
        }
    }
}
