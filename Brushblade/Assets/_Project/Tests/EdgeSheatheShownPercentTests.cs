using System;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>锋刃回鞘显示百分比 = 100 + 断金,Meta 与 BattleEngine 两侧同值。</summary>
    public class EdgeSheatheShownPercentTests
    {
        private static MetaState WithDuanJin()
        {
            var meta = new MetaState { CharacterXp = 100000, Ink = 100000 };
            meta.UnlockedPerks.Add("metal_4");
            return meta;
        }

        [Test]
        public void EdgeSheatheShownPercent_NoPerk_100() =>
            Assert.That(MetaRules.EdgeSheatheShownPercent(new MetaState()), Is.EqualTo(100));

        [Test]
        public void EdgeSheatheShownPercent_WithDuanJin_300() =>
            Assert.That(MetaRules.EdgeSheatheShownPercent(WithDuanJin()), Is.EqualTo(300));

        [Test]
        public void BattleEngine_EdgeSheatheShownPercent_MatchesMeta()
        {
            foreach (var meta in new[] { new MetaState(), WithDuanJin() })
            {
                var cfg = MetaRules.BuildBattleConfig(meta, Array.Empty<string>());
                var engine = new BattleEngine(new RecipeGraph(new[] { new CharDef("金", Element.Metal, isComponent: true) }),
                    cfg, new[] { "金" }, Array.Empty<string>(), new[] { RebalanceFixture.Mob() }, seed: 1);
                Assert.That(engine.EdgeSheatheShownPercent, Is.EqualTo(MetaRules.EdgeSheatheShownPercent(meta)));
            }
        }
    }
}
