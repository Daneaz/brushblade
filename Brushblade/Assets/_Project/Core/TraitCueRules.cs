using System;
using System.Collections.Generic;
using System.Linq;

namespace Brushblade.Core
{
    /// <summary>特性在面印位上印哪一枚(Plan E4 附录 §B a3 / e3):攻 / 五行面 / 两面通用,
    /// 字形特性(拆字 / 成字)改印墨蓝「拆」/「合」—— 它们不是某一面的效果。</summary>
    public enum TraitSealKind
    {
        Attack,
        Feature,
        Both,
        Dismantle,
        Compose,
    }

    /// <summary>特性触发反馈的纯规则(飘字名签、升级预览、字卡详情两面条共用)。</summary>
    public static class TraitCueRules
    {
        /// <summary>判定顺序:拆字时 → Dismantle;成字时 → Compose;否则按 <see cref="TraitDef.Face"/>。</summary>
        public static TraitSealKind SealOf(TraitDef t)
        {
            if (t.Trigger == TraitTrigger.OnDismantle) return TraitSealKind.Dismantle;
            if (t.Trigger == TraitTrigger.OnCompose) return TraitSealKind.Compose;
            return t.Face switch
            {
                TraitFace.Attack => TraitSealKind.Attack,
                TraitFace.Feature => TraitSealKind.Feature,
                _ => TraitSealKind.Both,
            };
        }
            /// <summary>出字时飘名的主动特性(Plan E4 B-Qa1,TriggerFx A):本面已解锁的主动特性里,只取 Lv5 / Lv6 / Lv8、
        /// 出字时触发(OnKill / OnCrit 这类要等条件成立,不在出字那一刻飘)、本场未用尽(<paramref name="spent"/>,
        /// 通常接 <c>BattleEngine.TraitSpentThisBattle</c>,null = 都没用)、且不是「只登记开局效果」的那几条(跨场效果本场不落地)。
        /// Lv1 / Lv3 / Lv4 每张字每次出字都有,飘出来是噪音。按槽位升序。</summary>
        public static IReadOnlyList<TraitDef> CastAnnounced(CharDef def, CardFace face, int cardLevel, Func<TraitDef, bool> spent)
        {
            if (def == null || def.Traits.Count == 0) return Array.Empty<TraitDef>();
            return TraitRules.ActiveTraits(def, face, cardLevel)
                .Where(t => (t.Slot == TraitSlot.Lv5 || t.Slot == TraitSlot.Lv6 || t.Slot == TraitSlot.Lv8)
                    && t.Trigger == TraitTrigger.Cast
                    && !(t.Effects.Count > 0 && t.Effects.All(e => e.OpeningBattles > 0))
                    && !(spent?.Invoke(t) ?? false))
                .ToList();
        }

        /// <summary>状态挂上时飘状态名的范围(B-Qa2,TriggerFx E / F):控制类与新出现的 kind,顺序即同一单位同一动作只飘一条时的取舍优先级
        /// (冰滞 / 冻结 / 减速 / 封禁 / 致盲 / 魅惑 / 嘲讽 / 禁增益 / 干涸 / 被缠 / 标记 / 致命)。
        /// 灼、加攻、护盾、战意这类已有数字飘字的不在内。减速 = SpeedModifier 合计为负。</summary>
        public static readonly IReadOnlyList<StatusKind> AnnouncedKinds = new[]
        {
            StatusKind.IceStall, StatusKind.Freeze, StatusKind.SpeedModifier, StatusKind.Silence, StatusKind.Blind,
            StatusKind.Charm, StatusKind.Taunt, StatusKind.BuffBlock, StatusKind.HealBlock, StatusKind.Bind,
            StatusKind.Vulnerable, StatusKind.Doom,
        };

        /// <summary>单位身上此刻「挂着、在播报范围内」的 kind 集合(动作前后各取一次再 <see cref="NewlyApplied"/>)。
        /// SpeedModifier 只在合计为负(减速)时算;其余按「有就算」(量为 0 的条目不算,同战场 chip 的判据)。</summary>
        public static HashSet<StatusKind> AnnounceableKinds(StatusBag bag)
        {
            var kinds = new HashSet<StatusKind>();
            if (bag == null) return kinds;
            foreach (var kind in AnnouncedKinds)
            {
                bool on = kind == StatusKind.SpeedModifier
                    ? bag.TotalMagnitude(kind) < 0
                    : kind == StatusKind.Vulnerable || kind == StatusKind.Blind
                        ? bag.TotalMagnitude(kind) > 0
                        : bag.Has(kind);
                if (on) kinds.Add(kind);
            }
            return kinds;
        }

        /// <summary>动作前后两份集合里**新出现**的那一个(按 <see cref="AnnouncedKinds"/> 的顺序取第一条;同一单位同一动作最多 1 条)。
        /// before 为 null 当空集;没有新出现的返回 null。</summary>
        public static StatusKind? NewlyApplied(ISet<StatusKind> before, ISet<StatusKind> after)
        {
            if (after == null) return null;
            foreach (var kind in AnnouncedKinds)
                if (after.Contains(kind) && (before == null || !before.Contains(kind))) return kind;
            return null;
        }
    }
}
