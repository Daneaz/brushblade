using System;
using System.Collections.Generic;
using System.Linq;

namespace Brushblade.Core
{
    /// <summary>一次出字的预估(Plan E4 预估签,附录 §A 3.4)。口径见 plan「已定口径 §A」:
    /// 暴击按不暴算(必暴照算)、生克按 ApparentElement、伤害取 Damage 事件 Amount 合计(过生克 / 护甲 / 易伤,
    /// 未扣敌盾,不封顶剩余血)、护盾 / 治疗取实际入账、反应链不算。</summary>
    public sealed class CastEstimate
    {
        /// <summary>影子出字的受理结果;非 None = 这处落了会被拒(签不出)。</summary>
        public BattleError Error;
        /// <summary>打全体(签开头写「全体」);同 <see cref="BattleEngine.HitsAllEnemiesNow"/>。</summary>
        public bool AllEnemies;
        /// <summary>被这一下打到(伤害或灼)的每只敌人,按下标升序。</summary>
        public IReadOnlyList<TargetHit> Enemies = Array.Empty<TargetHit>();
        /// <summary>落在 allySlot 那个友方(−1 = 玩家)身上的护盾实际入账合计;盾满 = 0。</summary>
        public int ShieldGranted;
        /// <summary>落在 allySlot 那个友方身上的实际回复合计;满血 = 0。</summary>
        public int HealAmount;
        /// <summary>玩家战意这一下净涨的层数(出字前后之差,≥ 0;断金清空时为 0)。</summary>
        public int MoraleGain;
        /// <summary>这次实际进结算的效果表:已扣本场用尽的限次特性、已并入部件印记(与 ApplyEffects 同一个函数)。</summary>
        public IReadOnlyList<EffectDef> Effects = Array.Empty<EffectDef>();
        /// <summary>本面已解锁、MaxPerBattle &gt; 0、本场已用尽的特性(含 OnKill 的折桂),按槽位升序。</summary>
        public IReadOnlyList<TraitDef> SpentTraits = Array.Empty<TraitDef>();
    }

    /// <summary>预估里一只敌人身上的结果。</summary>
    public readonly struct TargetHit
    {
        public int Index { get; }
        /// <summary>该下标所有 Damage 事件 Amount 之和。</summary>
        public int Damage { get; }
        /// <summary>段数:该下标 Damage 事件条数,跨排 Boss 的同一挥(SameSwing)不另计。</summary>
        public int Hits { get; }
        /// <summary>该下标 Burn 事件 Amount 之和(实际入账层数)。</summary>
        public int Burn { get; }
        /// <summary>吃到相克 ×1.5 / 被克 ×0.5(按 ApparentElement 判)。</summary>
        public bool Ke { get; }
        public bool Countered { get; }
        /// <summary>被斩杀(直杀 / 致命)。</summary>
        public bool Executed { get; }
        /// <summary>这一下打死。</summary>
        public bool Killed { get; }

        public TargetHit(int index, int damage, int hits, int burn, bool ke, bool countered, bool executed, bool killed)
        {
            Index = index;
            Damage = damage;
            Hits = hits;
            Burn = burn;
            Ke = ke;
            Countered = countered;
            Executed = executed;
            Killed = killed;
        }
    }

    public sealed partial class BattleEngine
    {
        /// <summary>影子引擎标记(Plan E4)。只有 <see cref="EstimateCast"/> 造出的影子为 true;本体恒 false,
        /// 读它的三处(RollCrit / DrainReactions / DamageEnemy 生克取元素)在 false 时与改前逐字节相同。</summary>
        private bool _estimate;

        private readonly List<OpeningEffect> _appliedOpenings = new List<OpeningEffect>();

        /// <summary>本场构造时实际结算过的开局效果(克隆,按传入顺序)。不进快照:断点续爬恢复后为空(D-Q4 不补播)。
        /// 非空时 <see cref="OpeningSteps"/>[0] 就是开局效果那一拍。</summary>
        public IReadOnlyList<OpeningEffect> AppliedOpenings => _appliedOpenings;

        /// <summary>这一发实际进结算的效果表(ApplyEffects 与预估共用):本场用尽的限次特性整条不进 Fold(D2-土 E35),
        /// 再并入部件印记(E10)。<paramref name="battleLimited"/> 为空时走静态的 CastEffectsOf。</summary>
        internal List<EffectDef> EffectsForCast(CharDef def, bool attackMode, int cardLevel,
            IReadOnlyList<TraitDef> battleLimited, IReadOnlyList<EffectDef> partExtra)
        {
            var castEffects = battleLimited.Count == 0
                ? CastEffectsOf(def, attackMode, cardLevel)
                : TraitRules.Fold(EffectsOf(def, attackMode), def, FaceOf(def, attackMode), cardLevel,
                    skip: t => t.MaxPerBattle > 0 && BattleUsesOf(def, t) >= t.MaxPerBattle);
            if (partExtra != null) castEffects = TraitRules.FoldExtra(castEffects, partExtra);
            return castEffects;
        }

        /// <summary>预估这张字落在 <paramref name="targetIndex"/>(敌人)/ <paramref name="allySlot"/>(友方)会怎样,
        /// 参数语义同 <see cref="Cast"/>。做法:Capture → Restore 出一个影子引擎,在影子上真跑一次出字,读影子的事件
        /// (附录 §A 3.3)。本体一个字节都不动:不摇它的随机流、不写它的字段、不覆盖 LastEvents。
        /// AP 不够照样预估(影子补 AP);顶替召唤按已确认处理。</summary>
        public CastEstimate EstimateCast(string charId, int targetIndex, bool attackMode, int libraryIndex = -1,
            int allySlot = Targeting.PlayerTarget)
        {
            if (Phase != BattlePhase.PlayerTurn) return new CastEstimate { Error = BattleError.BattleOver };
            if (charId == null || !_graph.TryGet(charId, out var def)) return new CastEstimate { Error = BattleError.NotCastable };

            // 效果表与已用特性在本体上只读地求(与影子出字时同态)
            var face = FaceOf(def, attackMode);
            int level = CardLevelOf(def.Id);
            var effects = EffectsForCast(def, attackMode, level, TraitRules.BattleLimited(def, face, level), PoolMarkEffectsOf(def));
            var spent = TraitRules.Unlocked(def, level)
                .Where(t => t.MaxPerBattle > 0 && t.AppliesTo(face) && TraitSpentThisBattle(def, t)).ToList();
            bool all = HitsAllEnemiesNow(charId, attackMode);

            // Restore 按 id 回查字怪定义(分裂克隆与本体共用一个 Def);与断点存档同一个前提:同场同 id 同定义
            var enemyDefs = new Dictionary<string, EnemyDef>();
            foreach (var e in _enemies) enemyDefs[e.Def.Id] = e.Def;
            var shadow = Restore(Capture(), _graph, _config.ForEstimate(), _cardLevels, enemyDefs);
            shadow._estimate = true;
            shadow.Ap = Math.Max(shadow.Ap, def.ApCost);
            var executions = new ExecutionLog();
            shadow.AddHookListener(executions);
            int moraleBefore = shadow._playerStatuses.TotalMagnitude(StatusKind.Morale);

            var error = shadow.CastCore(charId, targetIndex, replaceSummon: true, attackMode, libraryIndex, null, allySlot);
            var result = new CastEstimate { Error = error, AllEnemies = all, Effects = effects, SpentTraits = spent };
            if (error != BattleError.None) return result;

            var events = shadow._events;
            var hits = new List<TargetHit>();
            for (int i = 0; i < shadow._enemies.Count; i++)
            {
                int damage = 0, count = 0, burn = 0;
                bool ke = false, countered = false, killed = false, touched = false;
                foreach (var e in events)
                {
                    if (e.TargetIndex != i) continue;
                    switch (e.Kind)
                    {
                        case BattleEventKind.Damage:
                            touched = true;
                            damage += e.Amount;
                            if (!e.SameSwing) count++;
                            ke |= e.Ke;
                            countered |= e.Countered;
                            break;
                        case BattleEventKind.Burn:
                            touched = true;
                            burn += e.Amount;
                            break;
                        case BattleEventKind.EnemyDied:
                            killed = true;
                            break;
                    }
                }
                if (touched)
                    hits.Add(new TargetHit(i, damage, count, burn, ke, countered, executions.Executed.Contains(i), killed));
            }
            result.Enemies = hits;
            foreach (var e in events)
            {
                if (e.Kind == BattleEventKind.Shield && e.TargetIndex == allySlot) result.ShieldGranted += e.Amount;
                // Heal 的受治者在 SecondIndex(−1 = 玩家,≥ 0 = 木灵槽位),见 HealAlly / HealPlayerAndSummons
                if (e.Kind == BattleEventKind.Heal && e.SecondIndex == allySlot) result.HealAmount += e.Amount;
            }
            result.MoraleGain = Math.Max(0, shadow._playerStatuses.TotalMagnitude(StatusKind.Morale) - moraleBefore);
            return result;
        }

        /// <summary>影子上记斩杀(EnemyKilled 钩子 Source = Execute):直杀与致命都走 ResolveDefeat(Execute)。</summary>
        private sealed class ExecutionLog : IBattleHookListener
        {
            public readonly HashSet<int> Executed = new HashSet<int>();

            public void OnHook(BattleEngine battle, in HookArgs args)
            {
                if (args.Kind == HookKind.EnemyKilled && args.Source == EffectSource.Execute && args.Subject.Index >= 0)
                    Executed.Add(args.Subject.Index);
            }
        }
    }
}
