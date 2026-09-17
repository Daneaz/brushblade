using System;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>土脉 L2「反震」(spec 2026-09-13 §2.5):被护盾吸掉的伤害按 N% 反弹。
    ///
    /// **基数是「护盾实际吸掉的量」,不是「打过来的总伤害」** —— 这正是它不能并进
    /// 「镜」那根 60% 总量钳的理由(镜按总伤害折返)。两者同时生效时各算各的。</summary>
    public class ShieldReflectTests
    {
        private static RecipeGraph Graph() => new(new[]
        {
            new CharDef("壁", Element.Earth,
                effects: new[] { new EffectDef(EffectKind.Shield, 1000) }),
        });

        private static BattleEngine Engine(int reflectPercent, int startingShield,
            int enemyAttack, int enemyDefense = 0) =>
            new(Graph(), new BattleConfig
                {
                    DropTable = new[] { "土" }, PlayerMaxHp = 500, ApPerTurn = 20,
                    ShieldReflectPercent = reflectPercent,
                },
                Array.Empty<string>(), Array.Empty<string>(),
                new[] { new EnemyDef("靶", Element.Heart, 9000, enemyAttack, defense: enemyDefense) },
                seed: 1, startingNormalShield: startingShield);

        [Test]
        public void ShieldAbsorbedDamage_BouncesBack()
        {
            var engine = Engine(20, startingShield: 1000, enemyAttack: 100);
            int before = engine.Enemies[0].Hp;
            engine.EndTurn();   // 敌方出手,100 全被护盾吸掉
            Assert.That(engine.PlayerHp, Is.EqualTo(500), "前提:护盾全吸,一点没掉血");
            Assert.That(before - engine.Enemies[0].Hp, Is.EqualTo(20), "吸掉 100 × 20% = 20");
        }

        [Test]
        public void ShieldReflectDamageEvent_IsTaggedWithItsSource()
        {
            var engine = Engine(20, startingShield: 1000, enemyAttack: 100);
            engine.EndTurn();
            var hits = engine.LastEvents.Where(e => e.Kind == BattleEventKind.Damage).ToList();
            Assert.That(hits.Count, Is.EqualTo(1));
            Assert.That(hits[0].Source, Is.EqualTo(DamageSource.ShieldReflect));
            Assert.That(hits[0].Amount, Is.EqualTo(20));
        }

        [Test]
        public void PerkOff_BouncesNothing()
        {
            var engine = Engine(0, startingShield: 1000, enemyAttack: 100);
            int before = engine.Enemies[0].Hp;
            engine.EndTurn();
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(before));
        }

        [Test]
        public void NoShield_BouncesNothing()
        {
            var engine = Engine(20, startingShield: 0, enemyAttack: 100);
            int before = engine.Enemies[0].Hp;
            engine.EndTurn();
            Assert.That(engine.PlayerHp, Is.LessThan(500), "前提:没盾,实打实挨了");
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(before), "没吸到就没得反");
        }

        [Test]
        public void PartialAbsorb_OnlyTheAbsorbedPartCounts()
        {
            // 盾只有 30,打过来 100 → 吸 30、掉血 70 → 反 30×20% = 6
            var engine = Engine(20, startingShield: 30, enemyAttack: 100);
            int before = engine.Enemies[0].Hp;
            engine.EndTurn();
            Assert.That(before - engine.Enemies[0].Hp, Is.EqualTo(6),
                "基数是吸掉的 30,不是打过来的 100");
        }

        [Test]
        public void BounceIgnoresEnemyDefense()
        {
            var engine = Engine(20, startingShield: 1000, enemyAttack: 100, enemyDefense: 15);
            int before = engine.Enemies[0].Hp;
            engine.EndTurn();
            Assert.That(before - engine.Enemies[0].Hp, Is.EqualTo(20),
                "折返不是挥击,不吃护甲(与「镜」同口径)");
        }

        [Test]
        public void CombinesWithMirror_EachAxisCountsSeparately()
        {
            // 「镜」(StatusKind.Reflect)按打过来的总伤害折返,受 60% 总量钳约束;
            // 「反震」按护盾吸掉的量折返,不进那根钳。这里刻意让两者的名义百分比
            // 相加(50% + 20% = 70%)超过 60% 的钳 —— 若实现误把两者并成一根轴,
            // 会被錯误地钳到 enemyAttack×60% = 60;各算各的话应为
            // 100×50%(镜) + 100×20%(反震) = 70。
            var engine = Engine(20, startingShield: 1000, enemyAttack: 100);
            engine.PlayerStatuses.Apply(new StatusEffect
            {
                Kind = StatusKind.Reflect, Polarity = StatusPolarity.Buff,
                Magnitude = 50, TurnsLeft = 3, SourceId = "镜",
            });
            int before = engine.Enemies[0].Hp;
            engine.EndTurn();   // 100 全被护盾吸掉,一点没掉血
            Assert.That(engine.PlayerHp, Is.EqualTo(500), "前提:护盾全吸");
            Assert.That(before - engine.Enemies[0].Hp, Is.EqualTo(70),
                "镜 50 + 反震 20 = 70,没有被 60% 总量钳并轴砍掉");
        }

        // ---- allowReflect: false 时不触发反震(spec §2.5)----

        private static RecipeGraph BarbGraph() => new(new[]
        {
            new CharDef("刺", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 100) }),
        });

        [Test]
        public void BarbRecoil_DoesNotTriggerShieldReflect()
        {
            // 铁画(EnemyAbility.Barb)受击存活即反噬:DamagePlayerDirect(enemyIndex, recoil,
            // allowReflect: false)。那不是敌人的挥击,是玩家自己撞上去的——反震要跟着
            // allowReflect 一起被 gate 掉,否则「玩家打铁画 → 铁画反噬 → 玩家的盾吸收
            // 反噬伤害 → 反震把这份反噬伤害再弹回铁画身上」会凭空多出一份从未发生过的挥击。
            var engine = new BattleEngine(BarbGraph(), new BattleConfig
                {
                    DropTable = new[] { "土" }, PlayerMaxHp = 500, ApPerTurn = 20,
                    ShieldReflectPercent = 20,
                },
                new[] { "刺" }, Array.Empty<string>(),
                new[] { new EnemyDef("铁画", Element.Heart, 9000, 0, EnemyAbility.Barb) },
                seed: 1, startingNormalShield: 1000);

            int before = engine.Enemies[0].Hp;
            Assert.That(engine.Cast("刺", 0), Is.EqualTo(BattleError.None));
            Assert.That(engine.Enemies[0].Alive, Is.True, "前提:没打死,铁画才会反噬");
            // 100 伤 → 铁画反噬 30 → 全被 1000 点护盾吸收。若反震未被 allowReflect gate 掉,
            // 这里会多扣 30×20% = 6 点血,变成 106 而不是 100。
            Assert.That(before - engine.Enemies[0].Hp, Is.EqualTo(100),
                "只掉这一记本身的伤害,铁画的反噬(allowReflect:false)不该经反震再弹回一次");
        }
    }
}
