using Brushblade.Core;
using Brushblade.Data;

namespace Brushblade.Presentation
{
    /// <summary>技能节点简述:从定义机械生成。临时可编译版(T1,技能树重构)——
    /// 新增的那些 PerkEffect 暂时全部落到一个通用文案,逐条效果文案是 T9 的事。</summary>
    public static class PerkInfo
    {
        /// <summary>一行短语(如「AP +1」)。</summary>
        public static string ShortEffect(PerkNodeDef def) => def.Effect switch
        {
            PerkEffect.MaxHp => Strings.T("perk.info.effect.max_hp", ("value", def.Value)),
            PerkEffect.Ap => Strings.T("perk.info.effect.ap", ("value", def.Value)),
            PerkEffect.LibraryCapacity => Strings.T("perk.info.effect.library", ("value", def.Value)),
            _ => Strings.T("perk.info.effect.generic", ("value", def.Value)),
        };

        /// <summary>详情:节点 id + 效果短语 + 门槛/价格。</summary>
        public static string Detail(PerkNodeDef def) =>
            $"{def.Id} · {ShortEffect(def)} · Lv.{def.UnlockLevel} · {def.InkCost}{Strings.T("perk.info.unit.ink")}";
    }
}
