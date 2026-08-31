using System;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.CoreTests
{
    /// <summary>被克标记(2026-08-31):这一记吃了 **0.5x**,打软了。
    ///
    /// <see cref="BattleEvent.Ke"/> 只标了占便宜的那一头(1.5x)。吃亏的那一头此前一点表达都没有 ——
    /// 玩家打出去伤害莫名其妙只有一半,读不出是自己属性挑错了,还会误以为是敌人有护甲。
    /// 生克是双向的规则,表现也该是双向的。
    ///
    /// 与 Ke **互斥且同源**:两者都由 KeMultiplier(攻, 守) 这一个数决定 —— >1 是 Ke,&lt;1 是
    /// Countered,==1 两者皆假。不可能同时为真,所以留成两个 bool 而不是升格成枚举:
    /// 改动面小,且非法组合在构造点根本造不出来(见 BattleEngine 里三处赋值都读同一个倍率)。</summary>
    public sealed class CounteredEventFlagTests
    {
        private static RecipeGraph Graph() => new(new[]
        {
            new CharDef("甲", Element.Wood, effects: new[] { new EffectDef(EffectKind.DamageSingle, 20) }),
            new CharDef("乙", Element.Heart, effects: new[] { new EffectDef(EffectKind.DamageSingle, 20) }),
            new CharDef("丙", Element.Earth, effects: new[] { new EffectDef(EffectKind.DamageSingle, 20) }),
            new CharDef("丁", Element.Fire, effects: new[] { new EffectDef(EffectKind.BurnSingle, 3) }),
            new CharDef("戊", Element.Heart, effects: new[] { new EffectDef(EffectKind.Detonate, 0) }),
            new CharDef("己", Element.Wood, effects: new[] { new EffectDef(EffectKind.DamageAll, 10) }),
        });

        private static EnemyDef Mob(Element element, int hp = 500) => new("怔", element, hp, 0);

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

        // ---- 直接伤害 ----

        [Test]
        public void EarthHitsWood_MarksCountered()
        {
            // 土被木克:20 → 10,事件标 Countered
            var engine = Battle(new[] { Mob(Element.Wood) }, "丙");
            engine.Cast("丙", 0);
            var hit = FirstOf(engine, BattleEventKind.Damage);
            Assert.That(hit.Countered, Is.True, "土打木是 0.5x");
            Assert.That(hit.Ke, Is.False, "占便宜的那一头不是我");
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(500 - 10));
        }

        [Test]
        public void WoodHitsEarth_MarksKeNotCountered()
        {
            // 反方向:木克土,标 Ke 不标 Countered
            var engine = Battle(new[] { Mob(Element.Earth) }, "甲");
            engine.Cast("甲", 0);
            var hit = FirstOf(engine, BattleEventKind.Damage);
            Assert.That(hit.Ke, Is.True);
            Assert.That(hit.Countered, Is.False);
        }

        [Test]
        public void NeutralMatchup_MarksNeither()
        {
            // 木对火 1.0x:两个标记都不该亮
            var engine = Battle(new[] { Mob(Element.Fire) }, "甲");
            engine.Cast("甲", 0);
            var hit = FirstOf(engine, BattleEventKind.Damage);
            Assert.That(hit.Ke, Is.False);
            Assert.That(hit.Countered, Is.False);
        }

        [Test]
        public void HeartHitsAnything_NeverCountered()
        {
            // 心系中立:与所有属性 1.0x,两头都不沾
            foreach (var element in new[] { Element.Wood, Element.Fire, Element.Earth,
                Element.Metal, Element.Water, Element.Heart })
            {
                var engine = Battle(new[] { Mob(element) }, "乙");
                engine.Cast("乙", 0);
                Assert.That(FirstOf(engine, BattleEventKind.Damage).Countered, Is.False,
                    $"心对{element}中立");
            }
        }

        [Test]
        public void DamageAll_MarksCounteredPerTarget()
        {
            // 群攻:各目标各判各的 —— 木打土是占便宜,木打金是吃亏,同一记里两种都有
            var engine = Battle(new[] { Mob(Element.Earth), Mob(Element.Metal) }, "己");
            engine.Cast("己", 0);
            var marks = new System.Collections.Generic.List<(bool ke, bool countered)>();
            foreach (var e in engine.LastEvents)
                if (e.Kind == BattleEventKind.Damage) marks.Add((e.Ke, e.Countered));
            Assert.That(marks.Count, Is.EqualTo(2));
            Assert.That(marks[0].ke, Is.True, "木克土");
            Assert.That(marks[0].countered, Is.False);
            Assert.That(marks[1].ke, Is.False);
            Assert.That(marks[1].countered, Is.True, "金克木,木打金吃亏");
        }

        // ---- 灼烧 / 引爆 ----

        [Test]
        public void BurnTickOnWater_MarksCountered()
        {
            // 水克火:灼烧 tick 打在水系身上是 0.5x
            var engine = Battle(new[] { Mob(Element.Water) }, "丁");
            engine.Cast("丁", 0);
            engine.EndTurn();
            var tick = FirstOf(engine, BattleEventKind.BurnTick);
            Assert.That(tick.Countered, Is.True, "火被水克");
            Assert.That(tick.Ke, Is.False);
        }

        [Test]
        public void DetonateOnWater_MarksCountered()
        {
            var engine = Battle(new[] { Mob(Element.Water) }, "丁", "戊");
            engine.Cast("丁", 0);
            engine.Cast("戊", 0);
            Assert.That(FirstOf(engine, BattleEventKind.Detonate).Countered, Is.True);
        }

        // ---- 互斥与缺省 ----

        [Test]
        public void KeAndCountered_AreNeverBothTrue()
        {
            // 两个标记同源于一个倍率,不可能同时成立。全属性组合扫一遍钉死这条
            foreach (var attacker in new[] { "甲", "丙", "乙" })
                foreach (var element in new[] { Element.Wood, Element.Fire, Element.Earth,
                    Element.Metal, Element.Water, Element.Heart })
                {
                    var engine = Battle(new[] { Mob(element) }, attacker);
                    engine.Cast(attacker, 0);
                    var hit = FirstOf(engine, BattleEventKind.Damage);
                    Assert.That(hit.Ke && hit.Countered, Is.False,
                        $"{attacker} 打 {element}:两个标记同时为真");
                }
        }

        [Test]
        public void CounteredDefaultsToFalse_AndCarriesNoDamage()
        {
            Assert.That(new BattleEvent(BattleEventKind.Damage, 0, 20).Countered, Is.False);
            var engine = Battle(new[] { Mob(Element.Heart) }, "乙");
            engine.Cast("乙", 0);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(500 - 20), "纯标记,不参与数值");
        }

        [Test]
        public void NonDamageEvents_NeverMarkCountered()
        {
            var engine = Battle(new[] { Mob(Element.Water) }, "丁");
            engine.Cast("丁", 0);
            foreach (var e in engine.LastEvents)
                if (e.Kind != BattleEventKind.Damage && e.Kind != BattleEventKind.BurnTick
                    && e.Kind != BattleEventKind.Detonate)
                    Assert.That(e.Countered, Is.False, $"{e.Kind} 不该带被克标记");
        }
    }
}
