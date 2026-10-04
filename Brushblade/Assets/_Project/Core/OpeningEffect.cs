using System;
using System.Collections.Generic;
using System.Linq;

namespace Brushblade.Core
{
    /// <summary>跨场开局效果(spec v7 §5.1):之后 BattlesLeft 场,每场开局对全场(不选目标)结算一次。
    /// 用可写属性的 POCO 而不是 EffectDef,是为了让存档序列化稳定(EffectDef 只读、构造参数多)。</summary>
    public sealed class OpeningEffect
    {
        public string SourceCharId { get; set; }
        public Element Element { get; set; }
        public EffectKind Kind { get; set; }
        public int Value { get; set; }
        public int Turns { get; set; }
        public bool TargetAll { get; set; }
        public int BattlesLeft { get; set; }

        public OpeningEffect Clone() => (OpeningEffect)MemberwiseClone();
        public EffectDef ToEffect() => new EffectDef(Kind, Value, turns: Turns, targetAll: TargetAll);
    }

    /// <summary>同类开局效果只取最强(spec v7 §5.2 第 1 律):按 (Kind, TargetAll) 分组,
    /// Value 大者胜;Value 相同取 BattlesLeft 长者。</summary>
    public static class OpeningRules
    {
        public static List<OpeningEffect> Merge(IEnumerable<OpeningEffect> existing, IEnumerable<OpeningEffect> incoming) =>
            existing.Concat(incoming)
                .Where(o => o.BattlesLeft > 0)
                .GroupBy(o => (o.Kind, o.TargetAll))
                .Select(g => g.OrderByDescending(o => o.Value).ThenByDescending(o => o.BattlesLeft).First().Clone())
                .ToList();
    }
}
