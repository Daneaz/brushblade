using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>Plan E4 Task 3:TriggerFx 的纯决策规则 —— 出字时飘哪几条主动特性名(B-Qa1)。
    /// 状态挂上时的状态名签(B-Qa2)已改由 Core 的 StatusApplied 事件驱动(spec 2026-10-18,见 StatusAppliedEventTests)。</summary>
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
    }
}
