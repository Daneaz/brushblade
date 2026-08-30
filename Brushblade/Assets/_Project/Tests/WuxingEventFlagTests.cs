using System;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.CoreTests
{
    /// <summary>相克标记(2026-08-30):把「这一记吃到了相克 ×1.5」写进伤害事件本身,供表现层表达。
    ///
    /// 为什么非做不可:数值上相克 ×1.5 与暴击 ×1.5 长得**一模一样**,而暴击有「暴」字 + 放大档
    /// 专门表达,相克什么都没有 —— 玩家读不出自己打对了属性没有,而这是本作的核心机制。
    /// 相克还顺带无视全部护甲(wuxing-reference.md「相克即破甲」),收益比 1.5 还大,更该看得见。
    ///
    /// 口径与 <see cref="BattleEvent.Crit"/> 完全同构:**不新增 BattleEventKind**,相克是那一记
    /// 伤害的属性,就长在那条事件上 —— 单独发一条事件会逼表现层做事件配对,而这套代码库
    /// 已经在配对判据上栽过两次。
    ///
    /// 只标**相克**不标相生:相生 ×3 由字卡配方静态决定(规格 §相生「他生我」),同一张牌打谁都一样,
    /// 属于牌面信息而非这一记的属性;相克则取决于打的是谁,只有结算当下才知道。
    ///
    /// 测试字给 <see cref="Element.Wood"/> / <see cref="Element.Fire"/> 且**不给配方**:
    /// 没有配方就不会触发相生 ×3(理由同 CritStatTests 用心系),断言里看到的数字就是相克本身。</summary>
    public sealed class WuxingEventFlagTests
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
        public void WoodHitsEarth_MarksKe()
        {
            // 木克土:伤害吃 ×1.5,事件带标记
            var engine = Battle(new[] { Mob(Element.Earth) }, "甲");
            engine.Cast("甲", 0);
            Assert.That(FirstOf(engine, BattleEventKind.Damage).Ke, Is.True, "木克土");
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(500 - 30), "20 × 1.5");
        }

        [Test]
        public void WoodHitsFire_DoesNotMarkKe()
        {
            // 木生火是相生方向,且「我生他」不算 —— 倍率 1.0,不该标相克
            var engine = Battle(new[] { Mob(Element.Fire) }, "甲");
            engine.Cast("甲", 0);
            Assert.That(FirstOf(engine, BattleEventKind.Damage).Ke, Is.False, "木对火 1.0x");
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(500 - 20));
        }

        [Test]
        public void EarthHitsWood_DoesNotMarkKe()
        {
            // 反向:被克方打克制者是 0.5x,更不该标成相克(标了就成了「我占便宜」的反向误导)
            var engine = Battle(new[] { Mob(Element.Wood) }, "丙");
            engine.Cast("丙", 0);
            Assert.That(FirstOf(engine, BattleEventKind.Damage).Ke, Is.False, "土被木克,0.5x");
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(500 - 10), "20 × 0.5");
        }

        [Test]
        public void HeartHitsAnything_NeverMarksKe()
        {
            // 心系中立:与所有属性 1.0x,永不相克
            foreach (var element in new[] { Element.Wood, Element.Fire, Element.Earth,
                Element.Metal, Element.Water, Element.Heart })
            {
                var engine = Battle(new[] { Mob(element) }, "乙");
                engine.Cast("乙", 0);
                Assert.That(FirstOf(engine, BattleEventKind.Damage).Ke, Is.False,
                    $"心对{element}中立");
            }
        }

        [Test]
        public void DamageAll_MarksKePerTarget()
        {
            // 群攻:同一记里各目标各判各的 —— 标记长在每条事件上,不是整批一个值
            var engine = Battle(new[] { Mob(Element.Earth), Mob(Element.Fire) }, "己");
            engine.Cast("己", 0);
            var marks = new System.Collections.Generic.List<bool>();
            foreach (var e in engine.LastEvents)
                if (e.Kind == BattleEventKind.Damage) marks.Add(e.Ke);
            Assert.That(marks.Count, Is.EqualTo(2), "两只怪各一条");
            Assert.That(marks[0], Is.True, "木克土");
            Assert.That(marks[1], Is.False, "木对火 1.0x");
        }

        // ---- 灼烧 / 引爆:同样过 KeMultiplier(Fire, 守方),同样要标 ----

        [Test]
        public void BurnTickOnMetal_MarksKe()
        {
            // 火克金:灼烧 tick 的算式里本来就乘了 KeMultiplier,标记跟着同一个判据走
            var engine = Battle(new[] { Mob(Element.Metal) }, "丁");
            engine.Cast("丁", 0);
            engine.EndTurn();
            Assert.That(FirstOf(engine, BattleEventKind.BurnTick).Ke, Is.True, "火克金");
        }

        [Test]
        public void BurnTickOnWater_DoesNotMarkKe()
        {
            var engine = Battle(new[] { Mob(Element.Water) }, "丁");
            engine.Cast("丁", 0);
            engine.EndTurn();
            Assert.That(FirstOf(engine, BattleEventKind.BurnTick).Ke, Is.False, "火被水克");
        }

        [Test]
        public void DetonateOnMetal_MarksKe()
        {
            var engine = Battle(new[] { Mob(Element.Metal) }, "丁", "戊");
            engine.Cast("丁", 0);
            engine.Cast("戊", 0);
            Assert.That(FirstOf(engine, BattleEventKind.Detonate).Ke, Is.True, "火克金");
        }

        // ---- 恒等性 ----

        [Test]
        public void KeDefaultsToFalse_AndCarriesNoDamage()
        {
            // 新字段是纯标记:构造缺省 false,不参与任何数值 —— 引入它之后伤害逐字节不变
            Assert.That(new BattleEvent(BattleEventKind.Damage, 0, 20).Ke, Is.False);
            var engine = Battle(new[] { Mob(Element.Heart) }, "乙");
            engine.Cast("乙", 0);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(500 - 20));
        }

        [Test]
        public void NonDamageEvents_NeverMarkKe()
        {
            // 与 Crit 同构:只有伤害类事件带这个属性,其余恒 false
            var engine = Battle(new[] { Mob(Element.Metal) }, "丁");
            engine.Cast("丁", 0);
            foreach (var e in engine.LastEvents)
                if (e.Kind != BattleEventKind.Damage && e.Kind != BattleEventKind.BurnTick
                    && e.Kind != BattleEventKind.Detonate)
                    Assert.That(e.Ke, Is.False, $"{e.Kind} 不该带相克标记");
        }
    }
}
