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
        /// <summary>目标选择器(D2-火 修复轮 1):`Weaken … pick All battles N` 这类开局效果靠它落到全体。
        /// 进快照 / 存档;老存档缺字段 = Primary(现状)。</summary>
        public EffectPick Pick { get; set; }
        /// <summary>伤害形状(同上):`DamageSingle … shape All battles N`。老存档缺字段 = Single。</summary>
        public TargetArea Shape { get; set; }
        /// <summary>形状百分比(同上)。老存档缺字段 = 100(EffectDef 的缺省)。</summary>
        public int ShapePercent { get; set; } = 100;

        public OpeningEffect Clone() => (OpeningEffect)MemberwiseClone();
        public EffectDef ToEffect() => new EffectDef(Kind, Value, turns: Turns, targetAll: TargetAll,
            shape: Shape, shapePercent: ShapePercent, pick: Pick);

        /// <summary>出字时登记(EffectDef.OpeningBattles &gt; 0)与 ConfigLoader 加载校验共用的同一份转换 ——
        /// 校验判的就是运行时会登记、开局时会执行的那个效果。记**未缩放**的 Value(开局结算时按来源字等级缩放)。
        /// OnlyIf / AmpTerms 不随登记保留,ConfigLoader 拒绝带它们的开局效果。</summary>
        public static OpeningEffect Of(EffectDef e, string sourceCharId, Element element) => new OpeningEffect
        {
            SourceCharId = sourceCharId, Element = element, Kind = e.Kind, Value = e.Value,
            Turns = e.Turns, TargetAll = e.TargetAll, BattlesLeft = e.OpeningBattles,
            Pick = e.Pick, Shape = e.Shape, ShapePercent = e.ShapePercent,
        };
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
