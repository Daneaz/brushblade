using System;
using System.Collections.Generic;

namespace Brushblade.Core
{
    /// <summary>特性槽位(spec v6 §1):枚举值即解锁等级。Lv2/7/9/10 只涨数值,没有槽位。</summary>
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

    /// <summary>一条字卡特性(spec v6 §1 / §11.1)。名称是游戏数据(随字表),不进字符串表。</summary>
    public sealed class TraitDef
    {
        public TraitSlot Slot { get; }
        public int UnlockLevel => (int)Slot;
        public TraitFace Face { get; }
        public TraitForm Form { get; }
        /// <summary>解锁后替换掉的低阶槽位(如 Lv3 强化替换 Lv1);null = 不替换。</summary>
        public TraitSlot? Replaces { get; }
        public string Name { get; }
        public IReadOnlyList<EffectDef> Effects { get; }

        public TraitDef(TraitSlot slot, TraitFace face, TraitForm form, TraitSlot? replaces,
            string name, IReadOnlyList<EffectDef> effects)
        {
            Slot = slot;
            Face = face;
            Form = form;
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
