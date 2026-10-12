using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>Plan E4 Task 3:TriggerFx 的两条纯决策规则 —— 出字时飘哪几条主动特性名(B-Qa1)、
    /// 状态挂上时飘哪一个状态名(B-Qa2,前后 diff)。Presentation 只负责取集合与画。</summary>
    public class E4TraitCueTests
    {
        private static TraitDef T(TraitSlot slot, TraitFace face, string name, TraitForm form = TraitForm.Active,
            TraitTrigger trigger = TraitTrigger.Cast, int maxPerBattle = 0, params EffectDef[] effects) =>
            new(slot, face, form, null, name,
                effects.Length > 0 ? effects : new[] { new EffectDef(EffectKind.DamageSingle, 10) },
                trigger, maxPerBattle: maxPerBattle);

        private static CharDef Def(params TraitDef[] traits) =>
            new("测", Element.Fire, effects: new[] { new EffectDef(EffectKind.DamageSingle, 10) }, traits: traits);

        // ================= CastAnnounced(B-Qa1) =================

        [Test]
        public void CastAnnounced_OnlyLv5Lv6Lv8_OfThisFace()
        {
            var def = Def(
                T(TraitSlot.Lv1, TraitFace.Both, "一"),
                T(TraitSlot.Lv3, TraitFace.Both, "三"),
                T(TraitSlot.Lv5, TraitFace.Attack, "攻五"),
                T(TraitSlot.Lv5, TraitFace.Feature, "特五"),
                T(TraitSlot.Lv6, TraitFace.Attack, "攻六"),
                T(TraitSlot.Lv8, TraitFace.Attack, "攻八"));
            var names = TraitCueRules.CastAnnounced(def, CardFace.Attack, 8, _ => false).Select(t => t.Name).ToList();
            Assert.That(names, Is.EqualTo(new[] { "攻五", "攻六", "攻八" }));
            names = TraitCueRules.CastAnnounced(def, CardFace.Feature, 8, _ => false).Select(t => t.Name).ToList();
            Assert.That(names, Is.EqualTo(new[] { "特五" }));
        }

        [Test]
        public void CastAnnounced_RespectsLevel_Passive_Trigger()
        {
            var def = Def(
                T(TraitSlot.Lv5, TraitFace.Attack, "主动"),
                T(TraitSlot.Lv6, TraitFace.Attack, "被动", form: TraitForm.Passive),
                T(TraitSlot.Lv8, TraitFace.Attack, "击杀时", trigger: TraitTrigger.OnKill));
            Assert.That(TraitCueRules.CastAnnounced(def, CardFace.Attack, 5, _ => false).Select(t => t.Name),
                Is.EqualTo(new[] { "主动" }), "Lv5 只解锁到 Lv5");
            Assert.That(TraitCueRules.CastAnnounced(def, CardFace.Attack, 8, _ => false).Select(t => t.Name),
                Is.EqualTo(new[] { "主动" }), "被动 / 非出字时触发的不在出字时飘");
        }

        [Test]
        public void CastAnnounced_SkipsSpent_AndOpeningOnly()
        {
            var spent = T(TraitSlot.Lv5, TraitFace.Attack, "限次", maxPerBattle: 1);
            var opening = T(TraitSlot.Lv6, TraitFace.Attack, "跨场",
                effects: new EffectDef(EffectKind.Shield, 50, openingBattles: 3));
            var mixed = T(TraitSlot.Lv8, TraitFace.Attack, "混合",
                effects: new[] { new EffectDef(EffectKind.Shield, 50, openingBattles: 3), new EffectDef(EffectKind.DamageSingle, 5) });
            var def = Def(spent, opening, mixed);
            var names = TraitCueRules.CastAnnounced(def, CardFace.Attack, 8, t => ReferenceEquals(t, spent))
                .Select(t => t.Name).ToList();
            Assert.That(names, Is.EqualTo(new[] { "混合" }), "已用尽与只登记开局效果的不飘;本场也有效果的照飘");
        }

        [Test]
        public void CastAnnounced_NoTraits_Empty()
        {
            Assert.That(TraitCueRules.CastAnnounced(Def(), CardFace.Attack, 8, null), Is.Empty);
        }

        // ================= AnnounceableKinds / NewlyApplied(B-Qa2) =================

        private static StatusEffect S(StatusKind kind, int magnitude = 1) =>
            new StatusEffect { Kind = kind, Polarity = StatusPolarity.Debuff, Magnitude = magnitude, TurnsLeft = 2 };

        private static StatusBag Bag(params StatusEffect[] effects)
        {
            var bag = new StatusBag();
            foreach (var e in effects) bag.Apply(e);
            return bag;
        }

        [Test]
        public void AnnounceableKinds_OnlyControlFamily_SlowOnlyWhenNegative()
        {
            var kinds = TraitCueRules.AnnounceableKinds(Bag(S(StatusKind.Burn, 3), S(StatusKind.Freeze),
                S(StatusKind.SpeedModifier, 2), S(StatusKind.AttackBuff, 20), S(StatusKind.Vulnerable, 20)));
            Assert.That(kinds.Contains(StatusKind.Freeze), Is.True);
            Assert.That(kinds.Contains(StatusKind.Vulnerable), Is.True);
            Assert.That(kinds.Contains(StatusKind.Burn), Is.False, "灼有数字飘字,不飘名");
            Assert.That(kinds.Contains(StatusKind.AttackBuff), Is.False);
            Assert.That(kinds.Contains(StatusKind.SpeedModifier), Is.False, "加速不是减速");
            kinds = TraitCueRules.AnnounceableKinds(Bag(S(StatusKind.SpeedModifier, -2)));
            Assert.That(kinds.Contains(StatusKind.SpeedModifier), Is.True, "减速");
        }

        [Test]
        public void AnnounceableKinds_AllTwelve()
        {
            var all = new[]
            {
                StatusKind.IceStall, StatusKind.Freeze, StatusKind.Silence, StatusKind.Blind, StatusKind.Charm,
                StatusKind.Taunt, StatusKind.BuffBlock, StatusKind.HealBlock, StatusKind.Bind, StatusKind.Vulnerable,
                StatusKind.Doom,
            };
            var kinds = TraitCueRules.AnnounceableKinds(Bag(all.Select(k => S(k, 10)).Append(S(StatusKind.SpeedModifier, -1)).ToArray()));
            Assert.That(kinds.Count, Is.EqualTo(12));
            Assert.That(TraitCueRules.AnnouncedKinds.Count, Is.EqualTo(12));
        }

        [Test]
        public void AnnounceableKinds_NullBag_Empty()
        {
            Assert.That(TraitCueRules.AnnounceableKinds(null).Count, Is.EqualTo(0));
        }

        [Test]
        public void NewlyApplied_FirstNewInPriorityOrder_AtMostOne()
        {
            var before = new HashSet<StatusKind> { StatusKind.Freeze };
            var after = new HashSet<StatusKind> { StatusKind.Freeze, StatusKind.Doom, StatusKind.SpeedModifier };
            Assert.That(TraitCueRules.NewlyApplied(before, after), Is.EqualTo(StatusKind.SpeedModifier),
                "按 冰滞 / 冻结 / 减速 … 致命 的顺序取第一条新出现的");
            Assert.That(TraitCueRules.NewlyApplied(after, after), Is.Null, "没有新出现");
            Assert.That(TraitCueRules.NewlyApplied(after, before), Is.Null, "只消失不飘");
            Assert.That(TraitCueRules.NewlyApplied(null, new HashSet<StatusKind> { StatusKind.IceStall }),
                Is.EqualTo(StatusKind.IceStall), "之前没记录 = 空集");
        }

        [Test]
        public void NewlyApplied_IgnoresNonAnnouncedKinds()
        {
            Assert.That(TraitCueRules.NewlyApplied(new HashSet<StatusKind>(),
                new HashSet<StatusKind> { StatusKind.Burn, StatusKind.Morale }), Is.Null);
        }
    }
}
