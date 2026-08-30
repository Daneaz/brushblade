using System;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.CoreTests
{
    /// <summary>伤害事件带上**攻击方属性**(2026-08-30),供表现层按五行着色。
    ///
    /// 为什么表现层自己查不到:一条 Damage 事件只说"第 i 只怪掉了 N 血",打它的是哪张牌、
    /// 哪只召唤物,事件里一个字都没有 —— 而这正是要用来上色的那个属性。
    /// 敌方攻击(EnemyAttack / SummonHit)反过来是查得到的:TargetIndex 就是攻击者的下标,
    /// 表现层顺着它读 Enemies[i].ApparentElement 即可,所以那两类**刻意不加字段**,
    /// Core 的改动面越小越好。
    ///
    /// 与 <see cref="BattleEvent.Crit"/> / <see cref="BattleEvent.Ke"/> 同构:长在伤害事件上的属性,
    /// 不新增 BattleEventKind。缺省 null = 「这条事件没有攻击方概念」(筑盾、现形、分裂……)。</summary>
    public sealed class DamageElementTests
    {
        private static RecipeGraph Graph() => new(new[]
        {
            new CharDef("甲", Element.Wood, effects: new[] { new EffectDef(EffectKind.DamageSingle, 20) }),
            new CharDef("乙", Element.Water, effects: new[] { new EffectDef(EffectKind.DamageSingle, 20) }),
            new CharDef("丙", Element.Heart, effects: new[] { new EffectDef(EffectKind.DamageAll, 10) }),
            new CharDef("丁", Element.Fire, effects: new[] { new EffectDef(EffectKind.BurnSingle, 3) }),
            new CharDef("戊", Element.Heart, effects: new[] { new EffectDef(EffectKind.Detonate, 0) }),
            new CharDef("己", Element.Metal, effects: new[] { new EffectDef(EffectKind.Shield, 7) }),
            new CharDef("庚", Element.Earth, effects: new[] { new EffectDef(EffectKind.Summon, 20,
                summonCount: 1, summonAttack: 6, summonChar: "辛") }),
            // 辛 = 召唤物显示的那个字。它在字表里是水系,但召唤物**不继承这个** —— 见
            // SummonAttackCarriesSummonElement
            new CharDef("辛", Element.Water, effects: new[] { new EffectDef(EffectKind.DamageSingle, 6) }),
        });

        private static EnemyDef Mob(Element element = Element.Heart, int hp = 500, int attack = 0)
            => new("怔", element, hp, attack);

        private static BattleEngine Battle(EnemyDef[] enemies, params string[] library) =>
            new(Graph(), new BattleConfig { PlayerMaxHp = 100 },
                library, Array.Empty<string>(), enemies, seed: 1);

        private static BattleEvent FirstOf(BattleEngine engine, BattleEventKind kind)
        {
            foreach (var e in engine.LastEvents)
                if (e.Kind == kind) return e;
            Assert.Fail($"这一拍没有 {kind} 事件");
            return default;
        }

        // ---- 玩家出字 ----

        [Test]
        public void DamageCarriesCastingCharElement()
        {
            var engine = Battle(new[] { Mob() }, "甲");
            engine.Cast("甲", 0);
            Assert.That(FirstOf(engine, BattleEventKind.Damage).Attacker, Is.EqualTo(Element.Wood));
        }

        [Test]
        public void DifferentCharsCarryTheirOwnElement()
        {
            var engine = Battle(new[] { Mob() }, "乙");
            engine.Cast("乙", 0);
            Assert.That(FirstOf(engine, BattleEventKind.Damage).Attacker, Is.EqualTo(Element.Water));
        }

        [Test]
        public void DamageAll_EveryEventCarriesTheSameCaster()
        {
            // 群攻:三条事件都出自同一张牌,属性也就都一样(与 Ke 不同 —— 那个是各判各的)
            var engine = Battle(new[] { Mob(), Mob(), Mob() }, "丙");
            engine.Cast("丙", 0);
            int seen = 0;
            foreach (var e in engine.LastEvents)
                if (e.Kind == BattleEventKind.Damage)
                {
                    Assert.That(e.Attacker, Is.EqualTo(Element.Heart));
                    seen++;
                }
            Assert.That(seen, Is.EqualTo(3));
        }

        // ---- 灼烧 / 引爆:恒为火 ----

        [Test]
        public void BurnTickIsAlwaysFire()
        {
            // 灼烧的生克判定本来就写死了 KeMultiplier(Fire, …),属性侧口径一致
            var engine = Battle(new[] { Mob() }, "丁");
            engine.Cast("丁", 0);
            engine.EndTurn();
            Assert.That(FirstOf(engine, BattleEventKind.BurnTick).Attacker, Is.EqualTo(Element.Fire));
        }

        [Test]
        public void DetonateIsAlwaysFire()
        {
            // 引爆由心系的「戊」触发,但炸的是灼烧层 —— 属性该是火,不是触发它那张牌的
            var engine = Battle(new[] { Mob() }, "丁", "戊");
            engine.Cast("丁", 0);
            engine.Cast("戊", 0);
            Assert.That(FirstOf(engine, BattleEventKind.Detonate).Attacker, Is.EqualTo(Element.Fire));
        }

        // ---- 召唤物 ----

        [Test]
        public void SummonAttackCarriesSummonElement()
        {
            // 召唤物出手打的那记 Damage 带 summon.Element —— 而召唤物的属性继承的是
            // **召唤它那张牌**(庚,土),不是它显示的那个字(辛,字表里是水)。
            // 这是既有设计:SummonState 的 Element 由 BattleEngine 传 attacker 进去,
            // summonChar 只决定它长什么样。生克也按这个属性算,所以表现层照它上色是对的
            // —— 哪怕牌面上写着「辛」而颜色是土色。
            var engine = Battle(new[] { Mob(hp: 500) }, "庚");
            engine.Cast("庚", 0);
            for (int turn = 0; turn < 6 && engine.Phase == BattlePhase.PlayerTurn; turn++)
            {
                engine.EndTurn();
                foreach (var e in engine.LastEvents)
                    if (e.Kind == BattleEventKind.Damage)
                    {
                        Assert.That(e.Attacker, Is.EqualTo(Element.Earth), "召唤物继承召唤它那张牌的属性");
                        return;
                    }
            }
            Assert.Fail("六个回合里召唤物一次都没出手");
        }

        // ---- 缺省 ----

        [Test]
        public void NonDamageEvents_CarryNoAttacker()
        {
            // 筑盾没有「攻击方」可言 —— 缺省 null,表现层据此回落到自己的语义色
            var engine = Battle(new[] { Mob() }, "己");
            engine.Cast("己", 0);
            Assert.That(FirstOf(engine, BattleEventKind.Shield).Attacker, Is.Null);
        }

        [Test]
        public void AttackerDefaultsToNull_AndCarriesNoDamage()
        {
            // 纯标记:构造缺省 null,不参与任何数值
            Assert.That(new BattleEvent(BattleEventKind.Damage, 0, 20).Attacker, Is.Null);
            var engine = Battle(new[] { Mob() }, "甲");
            engine.Cast("甲", 0);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(500 - 20), "木对心 1.0x,20 就是 20");
        }
    }
}
