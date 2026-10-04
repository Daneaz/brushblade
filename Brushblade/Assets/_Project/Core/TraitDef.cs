using System;
using System.Collections.Generic;

namespace Brushblade.Core
{
    /// <summary>特性槽位(spec v7 §1):枚举值即解锁等级。Lv2/7/9/10 只涨数值,没有槽位。</summary>
    public enum TraitSlot
    {
        Lv1 = 1,
        Lv3 = 3,
        Lv4 = 4,
        Lv5 = 5,
        Lv6 = 6,
        Lv8 = 8,
    }

    /// <summary>特性作用于哪一面。Both = 两面通用(Lv1/Lv3/Lv4)。</summary>
    public enum TraitFace
    {
        Both,
        Attack,
        Feature,
    }

    /// <summary>Active = 出字时附带结算;Passive = 修饰本字或附着在产出上(Plan D 执行)。</summary>
    public enum TraitForm
    {
        Active,
        Passive,
    }

    /// <summary>特性何时结算(spec v7 §2.3)。Cast = 出字时(主动特性与「修饰本字」的被动);
    /// OnCrit / OnKill = 本字这次出字暴击 / 击杀时入队,出字末尾兑现(附录 M23)。</summary>
    public enum TraitTrigger
    {
        Cast,
        OnCrit,
        OnKill,
    }

    /// <summary>一条字卡特性(spec v7 §1 / §11.1)。名称是游戏数据(随字表),不进字符串表。</summary>
    public sealed class TraitDef
    {
        public TraitSlot Slot { get; }
        public int UnlockLevel => (int)Slot;
        public TraitFace Face { get; }
        public TraitForm Form { get; }
        public TraitTrigger Trigger { get; }
        /// <summary>解锁后替换掉的低阶槽位(如 Lv3 强化替换 Lv1);null = 不替换。</summary>
        public TraitSlot? Replaces { get; }
        public string Name { get; }
        public IReadOnlyList<EffectDef> Effects { get; }

        public TraitDef(TraitSlot slot, TraitFace face, TraitForm form, TraitSlot? replaces,
            string name, IReadOnlyList<EffectDef> effects,
            TraitTrigger trigger = TraitTrigger.Cast)
        {
            Slot = slot;
            Face = face;
            Form = form;
            Trigger = trigger;
            Replaces = replaces;
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Effects = effects ?? Array.Empty<EffectDef>();
        }

        public bool AppliesTo(CardFace face) =>
            Face == TraitFace.Both
            || (Face == TraitFace.Attack && face == CardFace.Attack)
            || (Face == TraitFace.Feature && face == CardFace.Feature);
    }
}
