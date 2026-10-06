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

        /// <summary>本次出字(顶层 Cast,TriggerDepth == 0)已解锁、面匹配的暴击时 / 击杀时特性;其余时候为 null。
        /// ApplyEffects 进门设、出门恢复外层(与 _castHitTargets 同一套保存 / 恢复)。</summary>
        private IReadOnlyList<TraitDef> _castOnCrit;
        private IReadOnlyList<TraitDef> _castOnKill;
        private CharDef _castTraitDef;

        /// <summary>本次出字的 BurnSingle / BurnAll 落到过的敌人(烟熏「带本字灼」的判据)。</summary>
        private List<int> _castBurnedTargets = new List<int>();

        /// <summary>本次出字给玩家实际入账的护盾(反震的挂载条件)。</summary>
        private int _castShieldGranted;

        private static IReadOnlyList<TraitDef> NullIfEmpty(IReadOnlyList<TraitDef> traits) => traits.Count == 0 ? null : traits;

        /// <summary>出字内触发入队(R4:只有顶层出字的伤害 / 击杀会走到这里 —— 排空期间 _castOnCrit / _castOnKill 为 null)。
        /// 每条特性一条反应,目标 = 被暴击 / 被击杀的那名敌人。</summary>
        private void EnqueueCastTraits(IReadOnlyList<TraitDef> traits, int enemyIndex)
        {
            if (traits == null || TriggerDepth > 0) return;
            var element = _castTraitDef.Element ?? Element.Heart;
            foreach (var t in traits)
                Enqueue(new Reaction(_castTraitDef.Id, element, t.Effects, enemyIndex, TriggerDepth + 1));
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
