using System;
using System.Collections.Generic;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>spec A §4.2:每张字的 Block 自带反击 %;BlockMod 的 counter 只能抬高、不能拉低本字自带值(D10)。</summary>
    public class BlockOwnCounterTests
    {
        private static CharDef Guard(int own, int mod = 0) => new("试", Element.Heart,
            effects: new[] { new EffectDef(EffectKind.Block, 2, counterPercent: own) },
            attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 100) },
            traits: mod == 0 ? null : new[] { new TraitDef(TraitSlot.Lv5, TraitFace.Feature, TraitForm.Active, null, "改",
                new[] { new EffectDef(EffectKind.BlockMod, 0, counterPercent: mod) }) });

        private static BattleEngine Battle(CharDef def, int level) =>
            new(RebalanceFixture.Graph(def), new BattleConfig { PlayerMaxHp = RebalanceFixture.BaseMaxHp, PlayerAttack = 100 },
                new[] { "试", "试", "试" }, Array.Empty<string>(), new[] { RebalanceFixture.Mob() }, seed: 1,
                cardLevels: new Dictionary<string, int> { ["试"] = level });

        private static int Counter(CharDef def, int level)
        {
            var b = Battle(def, level);
            b.Cast("试", -1);
            return b.PlayerStatuses.Find(StatusKind.Block).CounterDamage;
        }

        [Test]
        public void OwnCounterPercent_UsedWithoutBlockMod()
        {
            Assert.That(Counter(Guard(45), 1), Is.EqualTo(45));
        }

        [Test]
        public void BlockMod_NeverLowersTheCardsOwnPercent()
        {
            Assert.That(Counter(Guard(70, 50), 5), Is.EqualTo(MetaRules.ScaleByCardLevel(100, 5) * 70 / 100));
        }

        [Test]
        public void BlockMod_StillRaisesALowerOwnPercent()
        {
            Assert.That(Counter(Guard(40, 50), 5), Is.EqualTo(MetaRules.ScaleByCardLevel(100, 5) * 50 / 100));
        }
    }
}
