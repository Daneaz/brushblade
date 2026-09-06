using System;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>木脉 L4(spec §3.4.1):由**木系字**召出的召唤物速度 +40。
    ///
    /// 判据是**打出的那张字**的元素(CharDef.Element,BattleEngine.ApplyEffects 里叫 attacker),
    /// 不是 SummonState.Element(召唤物自己的元素)—— 与五行 L3 同一判据。土系(碉)也有召唤字,
    /// 这条测试专门守「加成不外溢到别的元素」这一侧,配置侧的三条守卫见 PerkInjectionTests。</summary>
    public class WoodSummonSpeedTests
    {
        private static RecipeGraph Graph() => new(new[]
        {
            new CharDef("兵", Element.Wood,
                effects: new[] { new EffectDef(EffectKind.Summon, 100, summonAttack: 3, summonChar: "木") }),
            new CharDef("碉", Element.Earth,
                effects: new[] { new EffectDef(EffectKind.Summon, 100, summonAttack: 3, summonChar: "木") }),
        });

        private static BattleEngine Engine(string cardId, int woodSummonSpeedBonus) =>
            new(Graph(), new BattleConfig
                {
                    DropTable = new[] { "木" }, PlayerMaxHp = 500,
                    WoodSummonSpeedBonus = woodSummonSpeedBonus,
                },
                new[] { cardId }, Array.Empty<string>(),
                new[] { new EnemyDef("靶", Element.Heart, 3000, 0) }, seed: 1);

        [Test]
        public void WoodCard_SummonSpeedGetsTheBonus()
        {
            var engine = Engine("兵", woodSummonSpeedBonus: 40);
            Assert.That(engine.Cast("兵"), Is.EqualTo(BattleError.None));
            var summon = engine.Summons.FirstOrDefault(s => s != null);
            Assert.That(summon, Is.Not.Null);
            Assert.That(summon.Speed, Is.EqualTo(140), "缺省 100 + 木脉 40");
        }

        [Test]
        public void EarthCard_SummonSpeedDoesNotGetTheBonus()
        {
            var engine = Engine("碉", woodSummonSpeedBonus: 40);
            Assert.That(engine.Cast("碉"), Is.EqualTo(BattleError.None));
            var summon = engine.Summons.FirstOrDefault(s => s != null);
            Assert.That(summon, Is.Not.Null);
            Assert.That(summon.Speed, Is.EqualTo(100), "土系字召出的不该吃木脉加成");
        }

        [Test]
        public void ZeroBonus_IsIdenticalToBeforeTheBranchExisted()
        {
            var engine = Engine("兵", woodSummonSpeedBonus: 0);
            Assert.That(engine.Cast("兵"), Is.EqualTo(BattleError.None));
            var summon = engine.Summons.FirstOrDefault(s => s != null);
            Assert.That(summon, Is.Not.Null);
            Assert.That(summon.Speed, Is.EqualTo(100));
        }
    }
}
