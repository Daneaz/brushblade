using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>主动特性结算(spec v6 §1 / R3):已解锁且作用面匹配的才生效;Lv3 可替换 Lv1;
    /// 顺序为 本体 → 特性按槽位。</summary>
    public class TraitActiveTests
    {
        private static CharDef Char() => new("试", Element.Heart,
            effects: new[] { new EffectDef(EffectKind.Shield, 50) },                 // 特色面:护盾
            attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 100) },   // 攻击面:单体
            traits: new[]
            {
                new TraitDef(TraitSlot.Lv1, TraitFace.Attack, TraitForm.Active, null, "灼一",
                    new[] { new EffectDef(EffectKind.BurnSingle, 1) }),
                new TraitDef(TraitSlot.Lv3, TraitFace.Attack, TraitForm.Active, TraitSlot.Lv1, "灼二",
                    new[] { new EffectDef(EffectKind.BurnSingle, 2) }),
                new TraitDef(TraitSlot.Lv5, TraitFace.Feature, TraitForm.Active, null, "加盾",
                    new[] { new EffectDef(EffectKind.Shield, 30) }),
                new TraitDef(TraitSlot.Lv6, TraitFace.Attack, TraitForm.Passive, null, "被动",
                    new[] { new EffectDef(EffectKind.DamageSingle, 999) }),
                new TraitDef(TraitSlot.Lv8, TraitFace.Attack, TraitForm.Active, null, "追击",
                    new[] { new EffectDef(EffectKind.DamageSingle, 50) }),
            });

        private static BattleEngine Battle(int level)
        {
            var def = Char();
            return new BattleEngine(RebalanceFixture.Graph(def),
                new BattleConfig { PlayerMaxHp = RebalanceFixture.BaseMaxHp, PlayerAttack = 100 },
                new[] { "试", "试" }, Array.Empty<string>(), new[] { RebalanceFixture.Mob() }, seed: 1,
                cardLevels: new Dictionary<string, int> { ["试"] = level });
        }

        private static int Burn(BattleEngine b) => b.Enemies[0].Statuses.TotalMagnitude(StatusKind.Burn);

        [Test]
        public void Level1_AttackFace_BodyThenLv1()
        {
            var b = Battle(1);
            b.Cast("试", 0, attackMode: true);
            Assert.That(b.Enemies[0].Hp, Is.EqualTo(100000 - 100));
            Assert.That(Burn(b), Is.EqualTo(1));
            int damageAt = b.LastEvents.ToList().FindIndex(e => e.Kind == BattleEventKind.Damage);
            int burnAt = b.LastEvents.ToList().FindIndex(e => e.Kind == BattleEventKind.Burn);
            Assert.That(damageAt, Is.LessThan(burnAt), "R3:本体伤害先于特性");
        }

        [Test]
        public void Level3_ReplacesLv1()
        {
            var b = Battle(3);
            b.Cast("试", 0, attackMode: true);
            Assert.That(Burn(b), Is.EqualTo(2), "只有 Lv3 的 2 层(灼层数不吃等级,spec v7 §1),Lv1 被替换");
        }

        [Test]
        public void FaceMismatch_TraitDoesNotApply()
        {
            var b = Battle(8);
            b.Cast("试", -1);   // 特色面:护盾
            Assert.That(Burn(b), Is.EqualTo(0), "攻击面特性不在特色面生效");
        }

        [Test]
        public void Level8_AttackFace_AddsLv8_PassiveIgnored()
        {
            var b = Battle(8);
            b.Cast("试", 0, attackMode: true);
            int expected = MetaRules.ScaleByCardLevel(100, 8) + MetaRules.ScaleByCardLevel(50, 8);
            Assert.That(b.Enemies[0].Hp, Is.EqualTo(100000 - expected), "被动特性(999)在 Plan A 不执行");
        }

        [Test]
        public void Level5_FeatureFace_AddsLv5Shield()
        {
            var b4 = Battle(4); b4.Cast("试", -1);
            var b5 = Battle(5); b5.Cast("试", -1);
            int shields4 = b4.LastEvents.Count(e => e.Kind == BattleEventKind.Shield);
            int shields5 = b5.LastEvents.Count(e => e.Kind == BattleEventKind.Shield);
            Assert.That(shields5, Is.EqualTo(shields4 + 1), "Lv5 解锁后多一条护盾结算;Lv4 时该特性未解锁");
        }

        [Test]
        public void TraitRules_Unlocked_RespectsLevelAndReplacement()
        {
            var slots = TraitRules.Unlocked(Char(), 3).Select(t => t.Slot).ToList();
            // D1 Task 4:被 Lv3 替换的 Lv1 仍返回(UI 要显示关键词);效果不参与出字(见 Level3_ReplacesLv1)
            Assert.That(slots.Contains(TraitSlot.Lv1), Is.True);
            Assert.That(slots.Contains(TraitSlot.Lv3), Is.True);
            Assert.That(slots.Contains(TraitSlot.Lv5), Is.False);
        }

        [Test]
        public void NoTraits_IsIdentity()
        {
            var plain = new CharDef("素", Element.Heart, attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 100) },
                effects: new[] { new EffectDef(EffectKind.Shield, 50) });
            Assert.That(plain.Traits.Count, Is.EqualTo(0));
            Assert.That(TraitRules.ActiveTraits(plain, CardFace.Attack, 10).Count, Is.EqualTo(0));
        }
    }
}
