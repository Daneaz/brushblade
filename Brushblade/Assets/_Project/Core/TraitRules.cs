using System.Collections.Generic;
using System.Linq;

namespace Brushblade.Core
{
    /// <summary>特性解锁规则(spec v6 §1):等级达到槽位值即解锁;被更高槽位 Replaces 的不再生效;
    /// 结果按槽位升序 —— 即 R3 的 Lv1/Lv3 → Lv5/Lv8 顺序。</summary>
    public static class TraitRules
    {
        public static IReadOnlyList<TraitDef> Unlocked(CharDef def, int cardLevel)
        {
            var unlocked = def.Traits.Where(t => t.UnlockLevel <= cardLevel).ToList();
            var replaced = new HashSet<TraitSlot>(unlocked.Where(t => t.Replaces.HasValue).Select(t => t.Replaces.Value));
            return unlocked.Where(t => !replaced.Contains(t.Slot)).OrderBy(t => (int)t.Slot).ToList();
        }

        public static IReadOnlyList<TraitDef> ActiveTraits(CharDef def, CardFace face, int cardLevel) =>
            Unlocked(def, cardLevel).Where(t => t.Form == TraitForm.Active && t.AppliesTo(face)).ToList();
    }
}
