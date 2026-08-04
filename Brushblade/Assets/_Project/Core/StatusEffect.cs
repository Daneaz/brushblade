using System.Collections.Generic;

namespace Brushblade.Core
{
    /// <summary>状态种类(2026-08-04)。护盾不在此列——它是资源不是状态,
    /// 有独立吸伤顺序与跨段规则,驱散/净化本就不该碰它。</summary>
    public enum StatusKind
    {
        Burn,             // 灼烧层数
        Bleed,            // 流血:每回合固定伤害
        Freeze,           // 冻结:跳过行动
        SpeedModifier,    // 速度增减(点数,可正可负)
        HealOverTime,     // 持续治疗
        DamageReduction,  // 减伤百分比
        AttackBuff,       // 攻击加成
    }

    public enum StatusPolarity { Buff, Debuff }

    /// <summary>一条状态。Magnitude 按 Kind 解读:Burn=层数、Bleed/HealOverTime=每回合量、
    /// DamageReduction=百分比、SpeedModifier=速度点数、AttackBuff=攻击加成。</summary>
    public sealed class StatusEffect
    {
        public StatusKind Kind { get; set; }
        public StatusPolarity Polarity { get; set; }
        public int Magnitude { get; set; }
        public int TurnsLeft { get; set; }   // -1 = 段内持久,不随回合递减
        public string SourceId { get; set; } // 字 ID:同字去重、驱散追溯
        public bool TargetAll { get; set; }  // 仅 HealOverTime 用

        public StatusEffect Clone() => new()
        {
            Kind = Kind, Polarity = Polarity, Magnitude = Magnitude,
            TurnsLeft = TurnsLeft, SourceId = SourceId, TargetAll = TargetAll,
        };
    }

    /// <summary>单位身上的状态集合。封装「同字不叠只刷新」与按极性批量清除,
    /// 供 P1~P3 的 Dispel/Cleanse 直接使用。</summary>
    public sealed class StatusBag
    {
        private readonly List<StatusEffect> _list = new();

        public IReadOnlyList<StatusEffect> All => _list;

        public bool Has(StatusKind kind) => Find(kind) != null;

        public StatusEffect Find(StatusKind kind)
        {
            foreach (var e in _list)
                if (e.Kind == kind) return e;
            return null;
        }

        /// <summary>该种类的量值合计(减伤多源叠加、速度多条修正求和都靠它)。</summary>
        public int TotalMagnitude(StatusKind kind)
        {
            int sum = 0;
            foreach (var e in _list)
                if (e.Kind == kind) sum += e.Magnitude;
            return sum;
        }

        /// <summary>施加一条。同 Kind 且同 SourceId 视为同一来源,覆盖刷新而非叠加
        /// (口径来自 P0:同字减伤不叠加,重复施放只刷新)。SourceId 为 null 时按 Kind 去重。</summary>
        public void Apply(StatusEffect effect)
        {
            for (int i = 0; i < _list.Count; i++)
            {
                if (_list[i].Kind != effect.Kind) continue;
                if (_list[i].SourceId != effect.SourceId) continue;
                _list[i] = effect;
                return;
            }
            _list.Add(effect);
        }

        public void Remove(StatusKind kind) => _list.RemoveAll(e => e.Kind == kind);

        /// <summary>按极性批量清除,返回移除条数(驱散/净化用)。</summary>
        public int RemoveAll(StatusPolarity polarity)
        {
            int before = _list.Count;
            _list.RemoveAll(e => e.Polarity == polarity);
            return before - _list.Count;
        }

        public void Clear() => _list.Clear();

        /// <summary>回合数递减,归零即移除;TurnsLeft &lt; 0 表示段内持久,不受影响。</summary>
        public void TickTurns()
        {
            for (int i = _list.Count - 1; i >= 0; i--)
            {
                if (_list[i].TurnsLeft < 0) continue;
                _list[i].TurnsLeft -= 1;
                if (_list[i].TurnsLeft <= 0) _list.RemoveAt(i);
            }
        }

        /// <summary>深拷贝(快照恢复用):条目是引用对象,浅拷会让两个单位共享同一条状态。</summary>
        public void CopyFrom(IEnumerable<StatusEffect> source)
        {
            _list.Clear();
            foreach (var e in source) _list.Add(e.Clone());
        }
    }
}
