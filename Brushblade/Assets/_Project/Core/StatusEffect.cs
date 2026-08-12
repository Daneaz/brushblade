using System.Collections.Generic;

namespace Brushblade.Core
{
    /// <summary>状态种类(2026-08-04)。护盾不在此列——它是资源不是状态,
    /// 有独立吸伤顺序与跨段规则,驱散/净化本就不该碰它。
    ///
    /// ⚠ **新值一律追加在末尾**(2026-08-12,E-b2):StatusEffect 随
    /// EndlessSaveState.CarriedStatuses / BattleSnapshot.PlayerStatuses 进 JSON,
    /// 而 SaveSerializer 没有注册 StringEnumConverter —— Newtonsoft 默认把枚举
    /// 序列化成**整数**。在中间插值会让所有旧存档里的状态整体错位(减伤变成破甲之类),
    /// 而且是静默的:单元测试全都建新对象,没有一条读旧 JSON 字节。
    /// 序号由 CritStatTests.StatusKindOrdinals_AreLockedForSaveCompatibility 锁住。</summary>
    public enum StatusKind
    {
        Burn,             // 灼烧层数
        Bleed,            // 流血:每回合固定伤害
        Freeze,           // 冻结:跳过行动
        SpeedModifier,    // 速度增减(点数,可正可负)
        HealOverTime,     // 持续治疗
        ObsoleteDamageReduction, // ⚠ 废弃占位,序号 5,**不得删除、不得复用**(2026-08-12,E-b4 T3)。
                          // 原为「减伤百分比」的乘法层,已随点数护甲上线删除,载体换成末尾的
                          // DefenseBuff(18)。不就地改名复用是刻意的:单位从**百分点**变成**点数**
                          // 而序号不变,正是静默存档损坏的那一类 —— 旧存档里的「5, 20」会被读成
                          // 「护甲 +20 点」而不是「减伤 20%」。删掉它则 6 以后全部前移,更糟。
        AttackBuff,       // 攻击加成:**百分点,敌我一致**(2026-08-12 统一;敌人 = BaseAttack 的百分比,
                          // 玩家 = 以 AttackBaseline 100 为基准的百分点)。量级 ×10 时它是比值,永不跟着乘
        ArmorBreak,       // 破甲:目标护甲 −Magnitude **点**,本场持久(TurnsLeft = -1)、**可叠加**
                          // (2026-08-05 曾是「承伤 +25%,不叠层」的乘法代偿;2026-08-12 E-b4 T3 复原。
                          //  名字与序号 7 一动不动 —— 语义变了但载体是同一条,存档安全)
        Curse,            // 诅咒:攻击 −Magnitude%,不叠层只刷新(2026-08-05)
        Seal,             // 封字:玩家下回合 AP −Magnitude(2026-08-06,Boss 倾覆)
        Immunity,         // 免疫:完全挡下 Magnitude 次伤害(2026-08-06)
        Blind,            // 致盲:该敌人攻击的命中率 −Magnitude%(2026-08-07)
        Silence,          // 沉默:该敌人的主动机制全部哑火(2026-08-07)
        Reflect,          // 反弹:把打到玩家的伤害按 Magnitude% 照回攻击者(2026-08-07)
        BurnNoDecay,      // 不灭:该敌人身上的灼烧层数不再每回合衰减(2026-08-09,炑)
        Morale,           // 战意:Magnitude = **层数**(不是攻击加成值),每层 +10 攻击,上限 5 层(2026-08-12)
        ApBoost,          // 玩家每回合 AP 上限加成(2026-08-12,利)
        CritBuff,         // 玩家暴击率加成(百分点,2026-08-12,锋)
        DefenseBuff,      // 玩家护甲加成(**点数**,2026-08-12,E-b4 T2):进 EffectiveDefense 的加数
        PierceBuff,       // 玩家穿透加成(点数,2026-08-12,E-b4 T2):进 EffectiveDefense 的减数,本场持续
    }

    public enum StatusPolarity { Buff, Debuff }

    /// <summary>一条状态。Magnitude 按 Kind 解读:Burn=层数、Bleed/HealOverTime=每回合量、
    /// SpeedModifier=速度点数、
    /// AttackBuff=攻击加成的**百分点,敌我同一单位**(2026-08-12 统一:
    ///   <see cref="StatusKind.AttackBuff"/> 的注释是这条的出处;
    ///   敌人侧 EnemyState.Attack 拿它乘 BaseAttack,玩家侧 EffectiveAttack 拿它加在基准 100 上,
    ///   两边都是比值 —— 量级 ×10 只乘数量,比值一律不乘)、
    /// ArmorBreak=**削减的护甲点数**(EffectiveEnemyDefense / EffectivePlayerDefense 的减数)、
    /// Curse=减攻百分比,与 AttackBuff **同一根轴**,EnemyState.Attack 里直接相减(±50% 精确相消)、
    /// Seal=AP 扣减量(StartTurn 读它)、
    /// Blind=命中降低百分比(AttackHits 读它)、
    /// Morale=战意层数(EffectiveAttack 乘 MoralePerStack 才是攻击加成)、
    /// ApBoost=每回合 AP 上限加成(StartTurn 与 ApPerTurn 两侧都读它)、
    /// CritBuff=暴击率加成的百分点(EffectiveCrit 读它)、
    /// DefenseBuff=护甲**点数**(EffectivePlayerDefense 的加数)、
    /// PierceBuff=穿透点数(EffectiveEnemyDefense 的减数)。
    ///
    /// ⚠ 护甲这一层的**变动量一律走状态**(2026-08-12,E-b4 §4.5.3):基础护甲是不可变属性
    /// (BattleConfig.PlayerDefense / EnemyState.Defense),增(DefenseBuff)、减(ArmorBreak)、
    /// 穿(PierceBuff)全部是本类的条目 —— StatusBag 本来就进快照,这是「零新增快照字段」的全部依据。</summary>
    public sealed class StatusEffect
    {
        public StatusKind Kind { get; set; }
        public StatusPolarity Polarity { get; set; }
        public int Magnitude { get; set; }
        // -1 = 战内持久,不随回合递减(2026-08-06 M5 改准确:是否跨战斗延续到下一场是另一件事,
        // 取决于 RunEngine 的携带态白名单——目前只有 DefenseBuff 会被带过去,免疫/玩家灼烧/
        // 封字等其余 TurnsLeft=-1 的状态都在每场战斗结束时丢弃,称「段内」持久并不准确)。
        public int TurnsLeft { get; set; }

        /// <summary>来源标识,两种相反用法并存,加新状态时先想清楚要哪种(2026-08-05 M3):
        /// 1) **去重键**——直接传字 ID(如 "铠"):同字再放视为同一来源,Apply() 覆盖刷新不叠加
        ///    (DefenseBuff 走这条)。
        /// 2) **铸唯一序号使其可叠**——传 "字#序号"(如 "滋#7",序号取自 BattleEngine._statusSerial /
        ///    RunSnapshot.StatusSerial):每次施放序号不同,天然绕开 Apply() 的同源覆盖,叠加而非刷新
        ///    (HealOverTime、AttackBuff、ArmorBreak 走这条)。
        /// 忘记铸序号、误传裸字 ID 会让本该可叠的状态静默退化成刷新——Task 4 的 Critical 就是这么踩的。</summary>
        public string SourceId { get; set; }
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
        /// (口径来自 P0:同字减伤不叠加,重复施放只刷新)。SourceId 为 null 时按 Kind 去重。
        /// 要允许同源可叠(如 HoT/AttackBuff),调用方得给 SourceId 铸唯一序号——见
        /// <see cref="StatusEffect.SourceId"/> 的两种用法说明。</summary>
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

        /// <summary>按极性从头移除至多 count 条,返回实际移除条数(计数式驱散用)。</summary>
        public int RemoveFirst(StatusPolarity polarity, int count)
        {
            int removed = 0;
            for (int i = 0; i < _list.Count && removed < count; )
            {
                if (_list[i].Polarity != polarity) { i++; continue; }
                _list.RemoveAt(i);
                removed++;
            }
            return removed;
        }

        public void Clear() => _list.Clear();

        /// <summary>移除指定的那一条(按引用)。免疫消耗到 0 时用——袋子里可能有多条
        /// 同 Kind 不同来源的免疫,不能用按 Kind 的 Remove 一把全清。</summary>
        public void RemoveEntry(StatusEffect effect) => _list.Remove(effect);

        /// <summary>回合数递减,归零即移除;TurnsLeft &lt; 0 表示段内持久,不受影响。
        /// <paramref name="except"/> 可选豁免一个种类不递减(冻结中 SpeedModifier 暂停用,
        /// 2026-08-05:黑名单式豁免——新加的有限时长状态默认照常递减,不会像原先的白名单
        /// 那样悄悄漏减)。</summary>
        public void TickTurns(StatusKind? except = null)
        {
            for (int i = _list.Count - 1; i >= 0; i--)
            {
                if (_list[i].TurnsLeft < 0) continue;
                if (except.HasValue && _list[i].Kind == except.Value) continue;
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
