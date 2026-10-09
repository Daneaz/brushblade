using System;
using System.Collections.Generic;
using System.Linq;

namespace Brushblade.Core
{
    /// <summary>特性反应队列(Plan A R8,D1 Task 1)。
    ///
    /// 纪律:监听器在 <see cref="IBattleHookListener.OnHook"/> 里**只入队、不结算**;
    /// <c>ApplyEffects</c> 不可重入(进门守卫抛异常);队列只在「一个原子动作的末尾」
    /// (安全点)排空 —— 出字、敌人/召唤物/玩家那一拍的边界、拆合。因此存档(<c>Capture</c>)
    /// 时队列必然为空,不进快照。
    ///
    /// 生产代码目前没有监听器:队列永远为空,排空是空转,trace 逐字节恒等。</summary>
    public sealed partial class BattleEngine
    {
        /// <summary>一条待结算的特性反应。只在安全点排空。</summary>
        internal readonly struct Reaction
        {
            public readonly string SourceCharId;
            public readonly Element Element;
            public readonly IReadOnlyList<EffectDef> Effects;
            public readonly int TargetIndex;   // 敌人下标;-1 = 不选敌
            public readonly int Depth;         // 入队时的 TriggerDepth + 1

            public Reaction(string sourceCharId, Element element, IReadOnlyList<EffectDef> effects, int targetIndex, int depth)
            {
                SourceCharId = sourceCharId;
                Element = element;
                Effects = effects;
                TargetIndex = targetIndex;
                Depth = depth;
            }
        }

        private readonly Queue<Reaction> _reactions = new Queue<Reaction>();

        /// <summary>正在 ApplyEffects 里。进门置 true、finally 置 false;已为 true 时再进门直接抛。</summary>
        private bool _inApplyEffects;

        /// <summary>单次排空的上限(死循环保险):反应链正常不会超过个位数。</summary>
        internal const int MaxReactionsPerDrain = 64;

        /// <summary>正在出字的字 ID(<see cref="Cast"/> 的 ApplyEffects 期间非 null),
        /// 给 EnemyHit / EnemyKilled 的 <see cref="HookArgs.CastCharId"/>。排空反应时为 null。</summary>
        private string _castingCharId;

        /// <summary>特性随机流的种子偏移(spec D5)。只给特性里的随机选取用;没有特性命中时一次也不摇。</summary>
        internal const int TraitSeedSalt = 0x5EED7A17;

        internal GameRandom _traitRandom;

        private readonly Dictionary<string, int> _traitUsesThisTurn = new();
        private readonly Dictionary<string, int> _traitUsesThisBattle = new();

        /// <summary>本回合已成功出字数(部件出手也计);StartTurn 清零。「本回合第一张」用。</summary>
        public int CastsThisTurn { get; private set; }

        internal static string TraitKey(string charId, TraitSlot slot, TraitFace face) => $"{charId}/{(int)slot}/{face}";

        /// <summary>次数阀:perTurn / perBattle 为 0 = 不限。两项都未满才算用掉并计数;失败不扣任何一项。</summary>
        internal bool TryUseTrait(string key, int perTurn, int perBattle)
        {
            _traitUsesThisTurn.TryGetValue(key, out int turnUsed);
            _traitUsesThisBattle.TryGetValue(key, out int battleUsed);
            if (perTurn > 0 && turnUsed >= perTurn) return false;
            if (perBattle > 0 && battleUsed >= perBattle) return false;
            _traitUsesThisTurn[key] = turnUsed + 1;
            _traitUsesThisBattle[key] = battleUsed + 1;
            return true;
        }

        // ---- D1 Task 9:出字内触发(附录 M23)与附着载体(附录 M9 / D9) ----

        /// <summary>一次 ApplyEffects 的出字瞬时量(D2-0 E13 收拢)。ApplyEffects 进门 new 一个、
        /// 出门恢复外层;不进快照(生命周期跨不出一张字,更跨不出存档边界,spec §3.4)。
        /// 出字之外 <see cref="_cast"/> 是一个空上下文(名单为空、快照为 null)。</summary>
        private sealed class CastContext
        {
            /// <summary>砺刃:本次出字的额外暴击率(进门按字的元素设置)。</summary>
            public int CritBonus;

            /// <summary>金脉 L2「锋芒」的每张字一次闸门:RollCrit 首次摇到暴击时兑现并置 true。
            /// 不限制的话一张群攻字暴击 5 个目标就能顶满战意上限。</summary>
            public bool CritMoraleGranted;

            /// <summary>R3(spec v7 §10):本次出字**之前**每个敌人满足哪些 <see cref="DamageCondition"/>,
            /// 按敌人下标存位掩码。只在 ApplyEffects 的同步调用栈内非 null。</summary>
            public int[] PreCastConditions;

            /// <summary>出字前每名敌人的灼层数(死者 0;D2-火 N4 计数缩放)。与 PreCastConditions 同生命周期、同「外层优先」。</summary>
            public int[] PreCastBurnStacks;

            /// <summary>选择器的两张「本次出字」名单(D1 Task 5):命中过的敌人(按命中顺序去重)/ 真正被冻住的敌人。</summary>
            public List<int> HitTargets = new List<int>();
            public List<int> FrozenTargets = new List<int>();

            /// <summary>本次出字召出的召唤物槽位(按落位顺序;幼苗、SummonedThisCast 选择器用,D1 Task 7)。</summary>
            public List<int> SummonedSlots = new List<int>();

            /// <summary>本次出字 Morale 效果累计溢出的层数(聚金,D2-金 J7;补满与锋芒不计)。</summary>
            public int MoraleOverflow;

            /// <summary>本次出字内的**实际**治疗量(溢出不算;治疗转盾用)。</summary>
            public int HealTotal;

            /// <summary>本次出字(顶层 Cast,TriggerDepth == 0)已解锁、面匹配的暴击时 / 击杀时特性;其余时候为 null。</summary>
            public IReadOnlyList<TraitDef> OnCrit;
            public IReadOnlyList<TraitDef> OnKill;
            /// <summary>斩杀时(D2-金 J3,铁则):同上,只认顶层出字;ResolveDefeat 在 source == Execute 时入队。</summary>
            public IReadOnlyList<TraitDef> OnExecute;
            public CharDef TraitDef;

            /// <summary>本次出字的 BurnSingle / BurnAll 落到过的敌人(烟熏「带本字灼」的判据)。</summary>
            public List<int> BurnedTargets = new List<int>();

            /// <summary>本次出字给玩家实际入账的护盾(反震的挂载条件)。</summary>
            public int ShieldGranted;

            /// <summary>本次出字的面(攻击面 = true)。入队反应时按它取本面本体,解析 bodyPercent(D2-火 E5)。</summary>
            public bool AttackMode;

            /// <summary>本次出字内每条特性已入队几次(D2-火 N13,TraitDef.MaxPerCast);没有 limit 的特性不记。</summary>
            public Dictionary<TraitDef, int> Enqueued;
        }

        /// <summary>当前出字的瞬时量;见 <see cref="CastContext"/>。</summary>
        private CastContext _cast = new CastContext();

        private static IReadOnlyList<TraitDef> NullIfEmpty(IReadOnlyList<TraitDef> traits) => traits.Count == 0 ? null : traits;

        /// <summary>出字内触发入队(R4:只有顶层出字的伤害 / 击杀会走到这里 —— 排空期间 _cast.OnCrit / _cast.OnKill 为 null)。
        /// 每条特性一条反应,目标 = 被暴击 / 被击杀的那名敌人。</summary>
        private void EnqueueCastTraits(IReadOnlyList<TraitDef> traits, int enemyIndex)
        {
            if (traits == null || TriggerDepth > 0) return;
            var element = _cast.TraitDef.Element ?? Element.Heart;
            // 反应里的 CharDef 是合成的,读不到本体:bodyPercent 在入队前按本面本体解析;特性来源键同 Fold(G11)
            var body = EffectsOf(_cast.TraitDef, _cast.AttackMode);
            foreach (var t in traits)
            {
                // 出字内次数上限(N13,连爆「最多 2 次」):计数随 CastContext,下一张字重新起算
                if (t.MaxPerCast > 0)
                {
                    _cast.Enqueued ??= new Dictionary<TraitDef, int>();
                    _cast.Enqueued.TryGetValue(t, out int used);
                    if (used >= t.MaxPerCast) continue;
                    _cast.Enqueued[t] = used + 1;
                }
                Enqueue(new Reaction(_cast.TraitDef.Id, element,
                    t.Effects.Select(e => TraitRules.ForCast(e, t, _cast.TraitDef, body)).ToList(),
                    enemyIndex, TriggerDepth + 1));
            }
        }

        /// <summary>附着:给敌人挂一条隐藏载体(Carrier 存在 Magnitude 里)。同字同特性再挂只刷新。</summary>
        private static void AttachRider(StatusBag bag, string sourceId, string traitKey, StatusKind carrier) =>
            bag.Apply(new StatusEffect
            {
                Kind = StatusKind.TraitRider, Polarity = StatusPolarity.Debuff,
                Magnitude = (int)carrier, TurnsLeft = -1, SourceId = sourceId, TraitKey = traitKey,
            });

        /// <summary>载体 <paramref name="carrier"/> 从这个单位身上移除了:移除挂在它上面的 TraitRider,
        /// 以及与该载体 SourceId + TraitKey 相同的全部附带状态。没有载体时空转(恒等)。</summary>
        private static void DropRiders(StatusBag bag, StatusKind carrier)
        {
            var riders = bag.All.Where(s => s.Kind == StatusKind.TraitRider && s.Magnitude == (int)carrier).ToList();
            foreach (var r in riders)
                foreach (var s in bag.All.Where(s => s.SourceId == r.SourceId && s.TraitKey == r.TraitKey).ToList())
                    bag.RemoveEntry(s);
        }

        /// <summary>两桶护盾都空了:反震失去载体,移除(D9)。</summary>
        private void DropShieldRecoilIfEmpty()
        {
            if (_shieldNormal + _shieldPersist <= 0) _playerStatuses.Remove(StatusKind.ShieldRecoil);
        }

        /// <summary>反震挂载:同类取最强,只留一条(TraitKey 跟随较强的那条)。</summary>
        private void ApplyShieldRecoil(int percent, string sourceId, string traitKey)
        {
            var existing = _playerStatuses.Find(StatusKind.ShieldRecoil);
            if (existing != null && existing.Magnitude >= percent) return;
            _playerStatuses.Remove(StatusKind.ShieldRecoil);
            _playerStatuses.Apply(new StatusEffect
            {
                Kind = StatusKind.ShieldRecoil, Polarity = StatusPolarity.Buff,
                Magnitude = percent, TurnsLeft = -1, SourceId = sourceId, TraitKey = traitKey,
            });
        }

        // ---- D2-0 Task 7:字形特性(拆字 / 成字)与部件印记(spec §11.8,E9 / E10) ----

        /// <summary>一条拆字印记:拆出的 <see cref="PartChar"/> 本回合从池中出手时,并入来源特性
        /// (<see cref="TraitKey"/>)的效果,还剩 <see cref="Remaining"/> 次。</summary>
        internal readonly struct PartMark
        {
            public readonly string PartChar;
            public readonly int Remaining;
            public readonly string TraitKey;
            public readonly string SourceCharId;

            public PartMark(string partChar, int remaining, string traitKey, string sourceCharId)
            {
                PartChar = partChar;
                Remaining = remaining;
                TraitKey = traitKey;
                SourceCharId = sourceCharId;
            }
        }

        /// <summary>本回合的拆字印记(计数表,不改 ForgeState.Pool);StartTurn 清空,进快照。</summary>
        private readonly List<PartMark> _partMarks = new();

        /// <summary>拆 / 合成功后、DrainReactions 之前调用。已解锁的字形特性每张字每条每场 1 次(E9):
        /// 即时类脱离出字结算(不选敌,targetIndex −1);印记类挂一条印记(拆出的部件里没有 PartChar 时不挂)。
        /// 没有字形特性时整段空转 —— 不摇随机数、不碰次数阀(恒等)。</summary>
        private bool FireGlyphTraits(string charId, TraitTrigger trigger)
        {
            if (!_graph.TryGet(charId, out var def)) return false;
            var traits = TraitRules.Glyph(def, CardLevelOf(charId), trigger);
            if (traits.Count == 0) return false;
            bool applied = false;
            foreach (var t in traits)
            {
                if (Phase == BattlePhase.Won || Phase == BattlePhase.Lost) break;
                if (t.PartChar != null && !def.Recipe.Contains(t.PartChar)) continue;   // 配方变动的保险
                string key = TraitKey(def.Id, t.Slot, t.Face);
                if (!TryUseTrait(key, perTurn: 0, perBattle: 1)) continue;
                if (t.PartChar != null)
                {
                    _partMarks.Add(new PartMark(t.PartChar, t.PartCount, key, def.Id));
                    continue;
                }
                ApplyDetachedEffects(def.Id, def.Element ?? Element.Heart, t.Effects, targetIndex: -1);
                applied = true;
            }
            return applied;
        }

        /// <summary>拆 / 合收尾,与 Cast 同序:DrainReactions → RefreshSummonAura → CheckWin。
        /// 只在有即时字形特性真结算过时才刷光环 / 判胜(无字形数据时恒等)。</summary>
        private void FinishForgeAction(bool glyphApplied)
        {
            DrainReactions();
            if (!glyphApplied) return;
            RefreshSummonAura();
            CheckWin();
        }

        /// <summary>部件 <paramref name="partId"/> 可用的第一条印记(先来先用)及其效果;没有 = −1 / null。
        /// 来源特性在字表里找不到(数据变动)的印记视为无效。</summary>
        private int FindPartMark(string partId, out IReadOnlyList<EffectDef> effects)
        {
            effects = null;
            for (int i = 0; i < _partMarks.Count; i++)
            {
                var m = _partMarks[i];
                if (m.PartChar != partId || m.Remaining <= 0) continue;
                if (!_graph.TryGet(m.SourceCharId, out var src)) continue;
                var trait = src.Traits.FirstOrDefault(t => TraitKey(src.Id, t.Slot, t.Face) == m.TraitKey);
                if (trait == null) continue;
                effects = trait.Effects;
                return i;
            }
            return -1;
        }

        /// <summary>用掉第 <paramref name="index"/> 条印记一次;用完移除。</summary>
        private void ConsumePartMark(int index)
        {
            var m = _partMarks[index];
            if (m.Remaining <= 1) _partMarks.RemoveAt(index);
            else _partMarks[index] = new PartMark(m.PartChar, m.Remaining - 1, m.TraitKey, m.SourceCharId);
        }

        internal int PendingReactionCount => _reactions.Count;

        /// <summary>入队。战斗已分胜负时丢弃。</summary>
        internal void Enqueue(in Reaction reaction)
        {
            if (Phase == BattlePhase.Won || Phase == BattlePhase.Lost) return;
            _reactions.Enqueue(reaction);
        }

        /// <summary>FIFO 排空;每条走 ApplyDetachedEffects(深度 = reaction.Depth)。
        /// 单次排空超过 <see cref="MaxReactionsPerDrain"/> 抛 InvalidOperationException(死循环保险)。
        /// Phase 为 Won/Lost 时清空队列直接返回。</summary>
        internal void DrainReactions()
        {
            int resolved = 0;
            while (_reactions.Count > 0)
            {
                if (Phase == BattlePhase.Won || Phase == BattlePhase.Lost)
                {
                    _reactions.Clear();
                    return;
                }
                if (resolved >= MaxReactionsPerDrain)
                {
                    _reactions.Clear();
                    throw new InvalidOperationException(
                        $"特性反应单次排空超过 {MaxReactionsPerDrain} 条:反应互相触发成了死循环");
                }
                var r = _reactions.Dequeue();
                resolved++;
                // ApplyDetachedEffects 自己会 +1,这里先摆到 Depth − 1,结算期间深度 = r.Depth
                int outerDepth = TriggerDepth;
                TriggerDepth = r.Depth - 1;
                try { ApplyDetachedEffects(r.SourceCharId, r.Element, r.Effects, r.TargetIndex); }
                finally { TriggerDepth = outerDepth; }
            }
        }
    }
}
