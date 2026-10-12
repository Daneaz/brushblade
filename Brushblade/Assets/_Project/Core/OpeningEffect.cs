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
        /// <summary>条件门(D2-木 E50,一叶知秋 `if OwnSummonAlive`):回合脉冲载荷带它,触发时按出字前快照判。
        /// 开局效果不带(ConfigLoader 拒绝带 OnlyIf 的开局效果)。老存档缺字段 = None。</summary>
        public DamageCondition OnlyIf { get; set; }

        public OpeningEffect Clone() => (OpeningEffect)MemberwiseClone();
        public EffectDef ToEffect() => new EffectDef(Kind, Value, turns: Turns, targetAll: TargetAll,
            shape: Shape, shapePercent: ShapePercent, pick: Pick, onlyIf: OnlyIf);

        /// <summary>出字时登记(EffectDef.OpeningBattles &gt; 0)与 ConfigLoader 加载校验共用的同一份转换 ——
        /// 校验判的就是运行时会登记、开局时会执行的那个效果。记**未缩放**的 Value(开局结算时按来源字等级缩放)。
        /// AmpTerms 不随登记保留;OnlyIf 随登记保留(D2-木 E50,回合脉冲载荷用),ConfigLoader 仍拒绝带它的开局效果。</summary>
        public static OpeningEffect Of(EffectDef e, string sourceCharId, Element element) => new OpeningEffect
        {
            SourceCharId = sourceCharId, Element = element, Kind = e.Kind, Value = e.Value,
            Turns = e.Turns, TargetAll = e.TargetAll, BattlesLeft = e.OpeningBattles,
            Pick = e.Pick, Shape = e.Shape, ShapePercent = e.ShapePercent, OnlyIf = e.OnlyIf,
        };
    }

    /// <summary>开局卷轴的一行(Plan E4 附录 §D 一.3,D-Q1 方案 A:不出灰显行,没有 Shadowed 字段)。</summary>
    public readonly struct OpeningRow
    {
        /// <summary>这条开局效果(克隆)。</summary>
        public OpeningEffect Effect { get; }
        /// <summary>本场开局时的剩余场数(含本场)。</summary>
        public int LeftBefore { get; }
        /// <summary>本场结束后还剩几场 = max(0, LeftBefore − 1);0 = 本场是最后一场。</summary>
        public int LeftAfter { get; }

        public OpeningRow(OpeningEffect effect, int leftBefore)
        {
            Effect = effect;
            LeftBefore = leftBefore;
            LeftAfter = Math.Max(0, leftBefore - 1);
        }
    }

    /// <summary>同类开局效果只取最强(spec v7 §5.2 第 1 律):按 (Kind, TargetAll) 分组,
    /// Value 大者胜;Value 相同取 BattlesLeft 长者。</summary>
    public static class OpeningRules
    {
        /// <summary>分组键:Merge 的「同类」判据。</summary>
        internal static (EffectKind, bool) GroupKey(OpeningEffect o) => (o.Kind, o.TargetAll);

        public static List<OpeningEffect> Merge(IEnumerable<OpeningEffect> existing, IEnumerable<OpeningEffect> incoming) =>
            existing.Concat(incoming)
                .Where(o => o.BattlesLeft > 0)
                .GroupBy(GroupKey)
                .Select(g => g.OrderByDescending(o => o.Value).ThenByDescending(o => o.BattlesLeft).First().Clone())
                .ToList();

        /// <summary>开局卷轴的行:按输入顺序,每条一行(输入即本场携带的那批,已经 Merge 过,同类只剩一条)。
        /// BattlesLeft ≤ 0 的不出行(与 Merge 同一过滤)。null / 空 = 空表。</summary>
        public static IReadOnlyList<OpeningRow> Display(IReadOnlyList<OpeningEffect> openings)
        {
            if (openings == null || openings.Count == 0) return Array.Empty<OpeningRow>();
            var rows = new List<OpeningRow>(openings.Count);
            foreach (var o in openings)
                if (o.BattlesLeft > 0) rows.Add(new OpeningRow(o.Clone(), o.BattlesLeft));
            return rows;
        }

        /// <summary>本场之后仍在的条数(顶栏 chip 的 N,D-Q2):LeftAfter &gt; 0 的行数。</summary>
        public static int LiveCount(IReadOnlyList<OpeningEffect> openings)
        {
            int n = 0;
            foreach (var row in Display(openings))
                if (row.LeftAfter > 0) n++;
            return n;
        }

        /// <summary>挂这条开局效果的特性:来源字特性里 OpeningBattles &gt; 0、Kind 与 Value(未缩放)都相同的那一条;
        /// 找不到 = null。数据守卫保证每个字的开局效果 (Kind, Value) 唯一。</summary>
        public static TraitDef SourceTrait(CharDef source, OpeningEffect o)
        {
            if (source == null || o == null) return null;
            foreach (var t in source.Traits)
                foreach (var e in t.Effects)
                    if (e.OpeningBattles > 0 && e.Kind == o.Kind && e.Value == o.Value) return t;
            return null;
        }
    }
}
