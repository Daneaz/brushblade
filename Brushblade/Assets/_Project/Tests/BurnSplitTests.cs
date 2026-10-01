using System;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>灼烧基数拆两份(2026-10-02):敌人侧吃 BurnPotency/蓄热/添薪,我方侧恒为基础值。</summary>
    public class BurnSplitTests
    {
        private static RecipeGraph Graph() => new(new[]
        {
            new CharDef("燃", Element.Fire, effects: new[] { new EffectDef(EffectKind.BurnSingle, 2) }),
            new CharDef("炽", Element.Fire, effects: new[] { new EffectDef(EffectKind.BurnPotency, 10) }),
            new CharDef("木", Element.Wood),
            new CharDef("垛", Element.Wood,
                effects: new[] { new EffectDef(EffectKind.Summon, 200, summonCount: 1, summonAttack: 3, summonChar: "木") }),
        });

        private static StatusEffect Burn(int stacks) => new()
        {
            Kind = StatusKind.Burn, Polarity = StatusPolarity.Debuff, Magnitude = stacks, TurnsLeft = -1,
        };

        // 开局即进入首个玩家回合,会先结算掉 1 层起始灼烧;给 2 层,留 1 层给下一次结算。
        private static BattleEngine Engine(int enemyBonus, params string[] library) =>
            new(Graph(), new BattleConfig
                {
                    DropTable = new[] { "火" }, PlayerMaxHp = 5000, ApPerTurn = 20,
                    EnemyBurnPerStackBonus = enemyBonus,
                },
                library, Array.Empty<string>(),
                new[] { new EnemyDef("靶", Element.Heart, 90000, 0) }, seed: 1,
                startingStatuses: new[] { Burn(2) });

        [Test]
        public void EnemyBurnBonus_RaisesEnemyTicksOnly()
        {
            var boosted = Engine(10, "燃");
            boosted.Cast("燃", 0);
            var plain = Engine(0, "燃");
            plain.Cast("燃", 0);
            int hpBoosted = boosted.Enemies[0].Hp, hpPlain = plain.Enemies[0].Hp;
            boosted.EndTurn();
            plain.EndTurn();
            int tickBoosted = hpBoosted - boosted.Enemies[0].Hp;
            int tickPlain = hpPlain - plain.Enemies[0].Hp;
            Assert.That(tickPlain, Is.EqualTo(2 * BattleConfig.BaseBurnPerStack), "前提:2 层 × 20");
            Assert.That(tickBoosted, Is.EqualTo(2 * (BattleConfig.BaseBurnPerStack + 10)));
        }

        [Test]
        public void PlayerBurn_IgnoresEnemyBonusAndPotency()
        {
            var engine = Engine(10, "炽");
            engine.Cast("炽");            // 敌人侧每层 +10
            int before = engine.PlayerHp;
            engine.EndTurn();            // 敌人攻击 0,唯一扣血 = 玩家灼烧 1 层
            Assert.That(before - engine.PlayerHp, Is.EqualTo(BattleConfig.BaseBurnPerStack),
                "我方灼烧恒为基础值");
        }

        [Test]
        public void SummonBurn_IgnoresEnemyBonusAndPotency()
        {
            var engine = new BattleEngine(Graph(),
                new BattleConfig
                {
                    DropTable = new[] { "木" }, PlayerMaxHp = 5000, ApPerTurn = 20,
                    EnemyBurnPerStackBonus = 10,
                },
                new[] { "垛", "炽" }, Array.Empty<string>(),
                new[] { new EnemyDef("靶", Element.Heart, 90000, 0) }, seed: 1);
            engine.Cast("垛");
            engine.Cast("炽");
            engine.Summons[0].Statuses.Apply(Burn(1));
            int before = engine.Summons[0].Hp;
            engine.EndTurn();
            Assert.That(before - engine.Summons[0].Hp, Is.EqualTo(BattleConfig.BaseBurnPerStack),
                "召唤物灼烧恒为基础值,不吃 BurnPotency 与添薪");
        }
    }
}
