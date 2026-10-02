using System;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>五行 L1 专精(spec 2026-10-02 §2.1):只对打出那张字的元素 = 本系生效。</summary>
    public class ElementSpecialtyTests
    {
        private static RecipeGraph Graph() => new(new[]
        {
            new CharDef("盾", Element.Earth, effects: new[] { new EffectDef(EffectKind.Shield, 100) }),
            new CharDef("木盾", Element.Wood, effects: new[] { new EffectDef(EffectKind.Shield, 100) }),
            new CharDef("泉", Element.Water, effects: new[] { new EffectDef(EffectKind.HealSelf, 100) }),
            new CharDef("土泉", Element.Earth, effects: new[] { new EffectDef(EffectKind.HealSelf, 100) }),
            new CharDef("林", Element.Wood, effects: new[] { new EffectDef(EffectKind.Summon, 200,
                summonCount: 1, summonAttack: 0, summonChar: "木") }),
            new CharDef("金召", Element.Metal, effects: new[] { new EffectDef(EffectKind.Summon, 200,
                summonCount: 1, summonAttack: 0, summonChar: "木") }),
            new CharDef("刀", Element.Metal, effects: new[] { new EffectDef(EffectKind.DamageSingle, 10) }),
            new CharDef("火刀", Element.Fire, effects: new[] { new EffectDef(EffectKind.DamageSingle, 10) }),
        });

        private static BattleEngine Engine(BattleConfig cfg, string card, int? hp = null)
        {
            cfg.DropTable ??= new[] { "土" };
            cfg.PlayerMaxHp = 5000;
            cfg.ApPerTurn = 20;
            return new BattleEngine(Graph(), cfg, new[] { card }, Array.Empty<string>(),
                new[] { new EnemyDef("靶", Element.Heart, 90000, 0) }, seed: 1, startingHp: hp);
        }

        [Test]
        public void ShieldPercent_AppliesToEarthOnly()
        {
            var earth = Engine(new BattleConfig { ShieldPercent = 30 }, "盾");
            earth.Cast("盾");
            var wood = Engine(new BattleConfig { ShieldPercent = 30 }, "木盾");
            wood.Cast("木盾");
            var baseline = Engine(new BattleConfig(), "盾");
            baseline.Cast("盾");
            Assert.That(earth.PlayerShield, Is.EqualTo(baseline.PlayerShield * 130 / 100));
            Assert.That(wood.PlayerShield, Is.EqualTo(baseline.PlayerShield), "木系字不吃筑垒");
        }

        [Test]
        public void HealPercent_AppliesToWaterOnly()
        {
            var water = Engine(new BattleConfig { HealPercent = 30 }, "泉", hp: 1000);
            water.Cast("泉");
            var earth = Engine(new BattleConfig { HealPercent = 30 }, "土泉", hp: 1000);
            earth.Cast("土泉");
            var baseline = Engine(new BattleConfig(), "泉", hp: 1000);
            baseline.Cast("泉");
            int baseHeal = baseline.PlayerHp - 1000;
            Assert.That(water.PlayerHp - 1000, Is.EqualTo(baseHeal * 130 / 100));
            Assert.That(earth.PlayerHp - 1000, Is.EqualTo(baseHeal), "土系字不吃甘霖");
        }

        [Test]
        public void SummonHpPercent_AppliesToWoodOnly()
        {
            var wood = Engine(new BattleConfig { SummonHpPercent = 30 }, "林");
            wood.Cast("林");
            var metal = Engine(new BattleConfig { SummonHpPercent = 30 }, "金召");
            metal.Cast("金召");
            var baseline = Engine(new BattleConfig(), "林");
            baseline.Cast("林");
            int baseHp = FirstSummon(baseline).MaxHp;
            Assert.That(FirstSummon(wood).MaxHp, Is.EqualTo(baseHp * 130 / 100));
            Assert.That(FirstSummon(wood).Hp, Is.EqualTo(FirstSummon(wood).MaxHp), "满血入场");
            Assert.That(FirstSummon(metal).MaxHp, Is.EqualTo(baseHp), "金系字召的不吃深根");
        }

        [Test]
        public void MetalCrit_OnlyMetalCardsCrit()
        {
            // 100 必暴、短路不摇随机:金系字必暴,火系字不暴
            var metal = Engine(new BattleConfig { MetalCritChance = 100 }, "刀");
            int before = metal.Enemies[0].Hp;
            metal.Cast("刀", 0);
            var metalHit = before - metal.Enemies[0].Hp;
            var plain = Engine(new BattleConfig(), "刀");
            plain.Cast("刀", 0);
            var plainHit = before - plain.Enemies[0].Hp;
            Assert.That(metalHit, Is.EqualTo(plainHit * BattleConfig.CritMultiplierPercent / 100));

            var fire = Engine(new BattleConfig { MetalCritChance = 100 }, "火刀");
            fire.Cast("火刀", 0);
            var firePlain = Engine(new BattleConfig(), "火刀");
            firePlain.Cast("火刀", 0);
            Assert.That(fire.Enemies[0].Hp, Is.EqualTo(firePlain.Enemies[0].Hp), "火系字不吃砺刃");
        }

        private static SummonState FirstSummon(BattleEngine e)
        {
            foreach (var s in e.Summons) if (s != null) return s;
            Assert.Fail("没召出来");
            return null;
        }
    }
}
