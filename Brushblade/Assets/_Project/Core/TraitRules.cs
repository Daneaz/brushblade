using System.Collections.Generic;
using System.Linq;

namespace Brushblade.Core
{
    /// <summary>特性解锁规则(spec v7 §1):等级达到槽位值即解锁;被同一作用面上更高槽位 Replaces 的不再生效;
    /// 结果按槽位升序。</summary>
    public static class TraitRules
    {
        public static IReadOnlyList<TraitDef> Unlocked(CharDef def, int cardLevel)
        {
            var unlocked = def.Traits.Where(t => t.UnlockLevel <= cardLevel).ToList();
            var replaced = new HashSet<(TraitSlot, TraitFace)>(
                unlocked.Where(t => t.Replaces.HasValue).Select(t => (t.Replaces.Value, t.Face)));
            return unlocked.Where(t => !replaced.Contains((t.Slot, t.Face))).OrderBy(t => (int)t.Slot).ToList();
        }

        public static IReadOnlyList<TraitDef> ActiveTraits(CharDef def, CardFace face, int cardLevel) =>
            Unlocked(def, cardLevel).Where(t => t.Form == TraitForm.Active && t.AppliesTo(face)).ToList();

        /// <summary>修饰器:出字前折叠进本体,不进结算循环(D1 Task 3)。</summary>
        public static bool IsModifier(EffectKind kind) =>
            kind == EffectKind.Amplify || kind == EffectKind.Reshape;

        /// <summary>本次出字实际结算的效果表(D1 Task 3):
        /// ① 本体在前,已解锁、面匹配的**主动**特性按槽位追加在后(spec v7 R3);
        /// ② 修饰器(本体里、主动特性里、出字时机的被动特性里)按出现顺序收集,逐条按类型分派折叠到 ① 上;
        /// ③ 修饰器本身不进结果。
        ///
        /// 纯函数:不改 <paramref name="body"/> 与特性里的任何 EffectDef(它们是字表共享对象),
        /// 被修饰的效果换成 <see cref="EffectDef.With"/> 产出的副本。没有修饰器时结果与
        /// 「本体 + 主动特性」逐项同一对象 —— 恒等。</summary>
        public static List<EffectDef> Fold(IReadOnlyList<EffectDef> body, CharDef def, CardFace face, int cardLevel)
        {
            var effects = new List<EffectDef>(body.Count);
            var modifiers = new List<EffectDef>();
            foreach (var e in body)
                (IsModifier(e.Kind) ? modifiers : effects).Add(e);
            foreach (var t in Unlocked(def, cardLevel))
            {
                if (!t.AppliesTo(face) || t.Trigger != TraitTrigger.Cast) continue;
                foreach (var e in t.Effects)
                {
                    if (IsModifier(e.Kind)) modifiers.Add(e);
                    // 被动特性的非修饰效果仍不在出字时执行(附着类留给后续任务)
                    else if (t.Form == TraitForm.Active) effects.Add(e);
                }
            }
            foreach (var m in modifiers)
            {
                switch (m.Kind)
                {
                    case EffectKind.Amplify: ApplyAmplify(effects, m); break;
                    case EffectKind.Reshape: ApplyReshape(effects, m); break;
                }
            }
            return effects;
        }

        /// <summary>Amplify:本面每条 scope 匹配的效果挂一项 (百分点, 条件)。不在这里求值 ——
        /// 目标相关的条件要按每一击的目标判定(BattleEngine.AmpPercent)。</summary>
        private static void ApplyAmplify(List<EffectDef> effects, EffectDef amp)
        {
            for (int i = 0; i < effects.Count; i++)
            {
                var e = effects[i];
                if (!InScope(amp.Scope, e.Kind)) continue;
                var terms = new List<(int Percent, DamageCondition If)>(e.AmpTerms) { (amp.Value, amp.OnlyIf) };
                effects[i] = e.With(ampTerms: terms);
            }
        }

        /// <summary>Reshape:只改**第一条** DamageSingle;Reshape 上非缺省的字段覆盖原值。没有就空转。</summary>
        private static void ApplyReshape(List<EffectDef> effects, EffectDef r)
        {
            int at = effects.FindIndex(e => e.Kind == EffectKind.DamageSingle);
            if (at < 0) return;
            effects[at] = effects[at].With(
                shape: r.Shape != TargetArea.Single ? r.Shape : (TargetArea?)null,
                // 改成全体时百分比一并重置:没写 shapePercent 就是全额 100,不沿用原效果(如横扫 50)的溅射比例
                shapePercent: r.ShapePercent != 100 || r.Shape == TargetArea.All ? r.ShapePercent : (int?)null,
                shots: r.Shots != 0 ? r.Shots : (int?)null,
                hitCount: r.HitCount != 1 ? r.HitCount : (int?)null,
                hitPercent: r.HitPercent != 100 ? r.HitPercent : (int?)null,
                forceCrit: r.ForceCrit ? true : (bool?)null,
                armorIgnorePercent: r.ArmorIgnorePercent > 0 ? r.ArmorIgnorePercent : (int?)null,
                shieldStrikePercent: r.ShieldStrikePercent > 0 ? r.ShieldStrikePercent : (int?)null,
                armorStrikePercent: r.ArmorStrikePercent > 0 ? r.ArmorStrikePercent : (int?)null);
        }

        public static bool InScope(AmpScope scope, EffectKind kind)
        {
            bool damage = kind == EffectKind.DamageSingle;
            bool heal = kind == EffectKind.HealSelf || kind == EffectKind.HealAll || kind == EffectKind.HealOverTime;
            bool shield = kind == EffectKind.Shield || kind == EffectKind.ShieldAll;
            bool counter = kind == EffectKind.Block;
            return scope switch
            {
                AmpScope.Damage => damage,
                AmpScope.Heal => heal,
                AmpScope.Shield => shield,
                AmpScope.Counter => counter,
                AmpScope.Seed => false,   // 种的 EffectKind 尚未落地(M6),落地时在这里接上
                AmpScope.All => damage || heal || shield || counter,
                _ => false,
            };
        }
    }
}
