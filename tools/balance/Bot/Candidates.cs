using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;

namespace Brushblade.Balance.Bot
{
    public enum ActionKind { Cast, Dismantle, Compose, EndTurn }

    /// <summary>机器人的一步动作(spec §2.1)。字段语义与 <see cref="BattleEngine.Cast"/> 的参数一一对应。</summary>
    public sealed class BotAction
    {
        public ActionKind Kind;
        public string CharId;
        public int LibraryIndex = -1;
        public bool AttackMode;
        public int TargetIndex = -1;
        public int AllySlot = Targeting.PlayerTarget;
        public IReadOnlyList<int> SummonSlots;
        public SummonDropMode DropMode;
        public bool ReplaceSummon;
        /// <summary>合字需腾位时先弃的字(复合动作:弃 + 合)。null = 不弃。</summary>
        public string DiscardForCompose;
        /// <summary>复合合字原子预检用的字表(<see cref="BattleEngine"/> 不公开它)。<see cref="Candidates.Generate"/>
        /// 给复合动作填好;手搭复合动作而不填会在 <see cref="ApplyTo"/> 抛异常(响亮失败,不静默退化成非原子)。</summary>
        public RecipeGraph Graph;

        /// <summary>在给定引擎上执行。
        /// 复合合字(<see cref="DiscardForCompose"/> 非 null)是**原子**的:先用 <see cref="ForgeEngine.TryCompose"/>
        /// 对「弃后的字库 / 池」预检(相位、AP、可合成集、原料、容量与 <see cref="BattleEngine.Compose"/> 同判据),
        /// 预检不过则不弃、不合、不动任何状态,直接返回失败(<see cref="BattleError.ForgeFailed"/> 等);
        /// 预检通过才真弃再真合。</summary>
        public BattleError ApplyTo(BattleEngine battle)
        {
            switch (Kind)
            {
                case ActionKind.Cast:
                    return battle.Cast(CharId, TargetIndex, ReplaceSummon, AttackMode, LibraryIndex,
                        SummonSlots, AllySlot, DropMode);
                case ActionKind.Dismantle:
                    return battle.Dismantle(CharId);
                case ActionKind.Compose:
                    if (DiscardForCompose != null)
                    {
                        var precheck = PrecheckCompound(battle);
                        if (precheck != BattleError.None) return precheck;
                        var discard = battle.Discard(DiscardForCompose);
                        if (discard != BattleError.None) return discard;
                    }
                    return battle.Compose(CharId);
                case ActionKind.EndTurn:
                    if (battle.Phase != BattlePhase.PlayerTurn) return BattleError.BattleOver;
                    battle.EndTurn();
                    return BattleError.None;
                default:
                    throw new ArgumentOutOfRangeException(nameof(Kind), Kind, null);
            }
        }

        /// <summary>复合合字的无副作用预检:模拟 Discard 的取舍(字库优先、再池),再按 Compose 的判据试合。</summary>
        private BattleError PrecheckCompound(BattleEngine battle)
        {
            if (battle.Phase != BattlePhase.PlayerTurn) return BattleError.BattleOver;
            if (battle.Ap < 1) return BattleError.NotEnoughAp;
            var library = new List<string>(battle.Library);
            var pool = new List<string>(battle.Pool);
            if (Graph == null) throw new InvalidOperationException("复合合字缺 Graph,无法原子预检");
            if (!library.Remove(DiscardForCompose) && !pool.Remove(DiscardForCompose)) return BattleError.NotCastable;
            var result = ForgeEngine.TryCompose(CharId, Graph, new ForgeState(library, pool), battle.LibraryCapacity,
                battle.ComposableChars, battle.PoolCapacity);
            return result.Success ? BattleError.None : BattleError.ForgeFailed;
        }

        public override string ToString() => Kind switch
        {
            ActionKind.Cast => $"Cast {CharId}{(AttackMode ? "攻" : "生")} t={TargetIndex} a={AllySlot} {DropMode}"
                + (SummonSlots != null ? $" s=[{string.Join(",", SummonSlots)}]" : "") + (ReplaceSummon ? " R" : ""),
            ActionKind.Compose => DiscardForCompose != null ? $"Compose {CharId} (弃 {DiscardForCompose})" : $"Compose {CharId}",
            ActionKind.Dismantle => $"Dismantle {CharId}",
            _ => "EndTurn",
        };
    }

    /// <summary>候选动作生成(spec §2.1)。只读 Core 的 public 查询,不碰本体状态。
    /// 每条候选都按 Core 的受理判据逐条构造(<c>CastCore</c> / <c>ForgeEngine</c>),目标是「列出来的都能执行」;
    /// 搜索层仍以副本上实际返回的 <see cref="BattleError"/> 为准,被拒即剪(双保险)。</summary>
    public static class Candidates
    {
        public static List<BotAction> Generate(BattleEngine battle, RecipeGraph graph)
        {
            var result = new List<BotAction>();
            if (battle.Phase != BattlePhase.PlayerTurn) return result;

            AddCasts(battle, graph, result);
            AddForge(battle, graph, result);
            result.Add(new BotAction { Kind = ActionKind.EndTurn });
            return result;
        }

        // ---- 出字 ----

        private static void AddCasts(BattleEngine battle, RecipeGraph graph, List<BotAction> result)
        {
            var library = battle.Library;
            var seen = new HashSet<string>();
            // 字库的字(同字多张只列一次,消耗第一张 —— 结果相同);池中部件:与 CastCore 的 fromPool 同判据
            for (int i = 0; i < library.Count; i++)
                if (seen.Add(library[i])) AddCastsOf(battle, graph, library[i], i, result);
            foreach (var id in battle.Pool)
                if (graph.TryGet(id, out var def) && def.IsComponent && seen.Add(id))
                    AddCastsOf(battle, graph, id, -1, result);
        }

        private static void AddCastsOf(BattleEngine battle, RecipeGraph graph, string id, int libraryIndex,
            List<BotAction> result)
        {
            if (!graph.TryGet(id, out var def) || def.ApCost > battle.Ap) return;
            foreach (bool attackMode in FacesOf(def))
            {
                var face = new FaceInfo(battle, def, attackMode, fromLibrary: libraryIndex >= 0);
                var enemyTargets = EnemyTargets(battle, face);
                if (enemyTargets.Count == 0) continue;
                foreach (var ally in AllyOptions(battle, face))
                    foreach (int target in enemyTargets)
                        result.Add(new BotAction
                        {
                            Kind = ActionKind.Cast, CharId = id, LibraryIndex = libraryIndex, AttackMode = attackMode,
                            TargetIndex = target, AllySlot = ally.AllySlot, SummonSlots = ally.SummonSlots,
                            DropMode = ally.DropMode, ReplaceSummon = ally.ReplaceSummon,
                        });
            }
        }

        /// <summary>要出的 attackMode 列表:两面字两面都出;单面字只出它实际那一面
        /// (只有攻击面的字必须带 attackMode = true,否则落到兜底一击)。</summary>
        private static IEnumerable<bool> FacesOf(CharDef def)
        {
            if (CardFaceRules.HasTwoFaces(def)) { yield return false; yield return true; }
            else yield return def.AttackEffects.Count > 0;
        }

        /// <summary>敌方落点:要选目标 → 每只活敌一条;不选目标(全体 / 无敌对)→ 一条 −1。
        /// 判据 = NeedsTargetNow(与 CastCore 同源,含池中部件的拆字印记)。
        /// 例外:折叠后有连发(DamageSingle + Scatter)的面不需选目标,但玩家指定目标时首发落在该敌人
        /// (Core Targeting.ExpandTargets 的 volleyLeadsWithPrimary)—— −1 之外再为每只活敌各列一条,同局面由转置表去重。</summary>
        private static List<int> EnemyTargets(BattleEngine battle, FaceInfo face)
        {
            bool volley = face.Effects.Any(e => e.Kind == EffectKind.DamageSingle && e.Shape == TargetArea.Scatter);
            if (!face.NeedsTarget && !volley) return new List<int> { -1 };
            var targets = face.NeedsTarget ? new List<int>() : new List<int> { -1 };
            for (int i = 0; i < battle.Enemies.Count; i++)
                if (battle.Enemies[i].Alive) targets.Add(i);
            return targets;
        }

        private readonly struct AllyOption
        {
            public readonly int AllySlot;
            public readonly IReadOnlyList<int> SummonSlots;
            public readonly SummonDropMode DropMode;
            public readonly bool ReplaceSummon;

            public AllyOption(int allySlot, IReadOnlyList<int> summonSlots = null,
                SummonDropMode dropMode = SummonDropMode.Auto, bool replaceSummon = false)
            {
                AllySlot = allySlot; SummonSlots = summonSlots; DropMode = dropMode; ReplaceSummon = replaceSummon;
            }
        }

        /// <summary>友方落点(与 CastCore 的校验顺序对齐):
        /// 召唤面 → 每个开放且不站人的格各一条普通召唤(首只落该格,余数按 PlanSummonSlots;要顶人则带 replaceSummon),
        ///          每只活木灵按 CanGraftAt / CanReplaceAt 各一条嫁接 / 替换;
        /// 选友方的面 → 玩家 + 每只活召唤物;带复活的面 → 另加每具可复活的尸体;
        /// 其余 → 玩家(自身 / 无友方落点)。</summary>
        private static List<AllyOption> AllyOptions(BattleEngine battle, FaceInfo face)
        {
            var def = face.Def;
            bool attackMode = face.AttackMode;
            var options = new List<AllyOption>();
            int summonCount = face.SummonCount;
            if (summonCount > 0)
            {
                // Core 缺省落位(summonSlots = null)的首格 = NextEmptySlot:先空槽、后尸体槽。这一格用缺省落位
                // (不传槽位),其余格各传 PlanSummonSlots —— 两者在多只 / 入场幼苗时落位不同,缺省那条不能被
                // 「从该格起 PlanSummonSlots」替代(暴力 oracle 在 Lv5/8 森、藻上抓到过)。
                int defaultSlot = DefaultSummonSlot(battle);
                for (int slot = 0; slot < BattleEngine.MaxSummonSlots; slot++)
                {
                    if (!battle.IsSlotOpen(slot) || battle.SlotOccupancy(slot) == SlotState.Alive) continue;
                    var plan = slot == defaultSlot ? null : battle.PlanSummonSlots(slot, summonCount);
                    options.Add(new AllyOption(Targeting.PlayerTarget, plan,
                        replaceSummon: face.SummonReplaceCount(plan) > 0));
                }
                for (int slot = 0; slot < BattleEngine.MaxSummonSlots; slot++)
                {
                    if (face.CanGraftAt(slot))
                        options.Add(new AllyOption(slot, dropMode: SummonDropMode.Graft));
                    if (face.CanReplaceAt(slot))
                        options.Add(new AllyOption(slot, dropMode: SummonDropMode.Replace));
                }
                return options;
            }

            options.Add(new AllyOption(Targeting.PlayerTarget));
            // 本体选友方(CastCore 校验口径),或已解锁特性折进来的效果选友方(Core 不校验、但按 allySlot 结算,
            // 如 杜 Lv5+ 攻击面的特性;暴力 oracle 抓到过)
            bool needsAlly = BattleEngine.NeedsAllyTarget(def, attackMode)
                || face.Effects.Any(BattleEngine.EffectNeedsAllyTarget);
            bool revive = BattleEngine.HasRevive(def, attackMode);
            for (int slot = 0; slot < BattleEngine.MaxSummonSlots; slot++)
            {
                var occupancy = battle.SlotOccupancy(slot);
                if (needsAlly && occupancy == SlotState.Alive && battle.CanHealSlot(slot))
                    options.Add(new AllyOption(slot));
                else if (revive && occupancy == SlotState.Corpse && battle.CanReviveSlot(slot))
                    options.Add(new AllyOption(slot));
            }
            return options;
        }

        /// <summary>一张字一面的派生量,一次 <see cref="BattleEngine.CastEffectsNow"/> 算完(性能:Core 每个读口各折叠一遍效果表,
        /// 每次约 4µs,一个面原先要折叠 3 + 8 + 2×活木灵 次)。
        ///
        /// 判据与 Core 的读口逐条同源(Task 1 的暴力 oracle 守着):
        /// - 字库里的字:<c>CastEffectsNow</c> 不并印记(PoolMarkEffectsOf 只认「池中、不在字库」的部件),
        ///   恰等于 <c>SummonCountOf</c> / <c>ReplaceableSummonCountOf</c> / 嫁接判据读的静态 <c>CastEffectsOf</c>,
        ///   于是只数、可顶人只数、嫁接 / 替换都从同一张表派生;
        /// - 池中部件:印记可能并进效果,与静态表不同 → 只数 / 嫁接 / 替换仍调 Core 读口,不复刻。</summary>
        private sealed class FaceInfo
        {
            public readonly CharDef Def;
            public readonly bool AttackMode;
            public readonly IReadOnlyList<EffectDef> Effects;
            public readonly bool NeedsTarget;
            public readonly int SummonCount;
            private readonly BattleEngine _battle;
            private readonly bool _derived;
            private readonly int _replaceable;
            private readonly bool _summonFace;
            private readonly int _capacity;

            public FaceInfo(BattleEngine battle, CharDef def, bool attackMode, bool fromLibrary)
            {
                _battle = battle;
                Def = def;
                AttackMode = attackMode;
                Effects = battle.CastEffectsNow(def.Id, attackMode);
                bool needsTarget = false;
                foreach (var e in Effects) if (BattleEngine.EffectNeedsTarget(e)) { needsTarget = true; break; }
                NeedsTarget = needsTarget;
                // 「字库里」与 PoolMarkEffectsOf 的判据对齐:同字既在字库又在池里也算字库
                _derived = fromLibrary || battle.Library.Contains(def.Id) || !def.IsComponent;
                if (_derived)
                {
                    _capacity = battle.SummonCapacity;
                    int all = 0, body = 0;
                    foreach (var e in Effects)
                    {
                        if (e.Kind == EffectKind.Summon) { all += e.SummonCount; body += e.SummonCount; }
                        else if (e.Kind == EffectKind.SummonSapling) all += e.SummonCount;
                    }
                    SummonCount = Math.Min(all, _capacity);
                    _replaceable = Math.Min(body, _capacity);
                    // HasSummonFace:五行面、木、本面有 Summon
                    _summonFace = !attackMode && def.Element == Element.Wood && body > 0;
                }
                else SummonCount = battle.SummonCountOf(def, attackMode);
            }

            private bool Alive(int slot) => _battle.SlotOccupancy(slot) == SlotState.Alive;

            /// <summary>同 <see cref="BattleEngine.SummonReplaceCountOf"/>。</summary>
            public int SummonReplaceCount(IReadOnlyList<int> plan)
            {
                if (!_derived) return _battle.SummonReplaceCountOf(Def, AttackMode, plan);
                if (_replaceable <= 0) return 0;
                if (plan == null) return Math.Max(0, _battle.AliveSummonCount + _replaceable - _capacity);
                int replaced = 0;
                for (int n = 0; n < _replaceable && n < plan.Count; n++)
                    if (Alive(plan[n])) replaced++;
                return replaced;
            }

            /// <summary>同 <see cref="BattleEngine.CanGraftAt"/>。</summary>
            public bool CanGraftAt(int slot) =>
                _derived ? _summonFace && Alive(slot) : _battle.CanGraftAt(Def.Id, AttackMode, slot);

            /// <summary>同 <see cref="BattleEngine.CanReplaceAt"/>。</summary>
            public bool CanReplaceAt(int slot) =>
                _derived ? _replaceable > 0 && Alive(slot) : _battle.CanReplaceAt(Def.Id, AttackMode, slot);
        }

        /// <summary>与 BattleEngine.NextEmptySlot(私有)同判据:开放格里先找空槽,再找尸体槽;没有 −1。</summary>
        private static int DefaultSummonSlot(BattleEngine battle)
        {
            for (int s = 0; s < BattleEngine.MaxSummonSlots; s++)
                if (battle.IsSlotOpen(s) && battle.SlotOccupancy(s) == SlotState.Empty) return s;
            for (int s = 0; s < BattleEngine.MaxSummonSlots; s++)
                if (battle.IsSlotOpen(s) && battle.SlotOccupancy(s) == SlotState.Corpse) return s;
            return -1;
        }

        // ---- 拆 / 合 ----

        private static void AddForge(BattleEngine battle, RecipeGraph graph, List<BotAction> result)
        {
            var state = new ForgeState(battle.Library, battle.Pool);

            // 拆(免 AP):字库 / 池里每个带配方的字各一条,容量判据交给 ForgeEngine 本身
            var seen = new HashSet<string>();
            foreach (var id in battle.Library.Concat(battle.Pool))
            {
                if (!seen.Add(id)) continue;
                if (ForgeEngine.TryDismantle(id, graph, state, battle.PoolCapacity, battle.LibraryCapacity).Success)
                    result.Add(new BotAction { Kind = ActionKind.Dismantle, CharId = id });
            }

            // 合(1 AP):目标取 ForgeEngine.Suggest(与引擎同一份可合成集过滤);
            // 字库满则「弃一张 + 合」作复合动作,每个可弃的字各一条(弃谁由搜索按打分定)
            if (battle.Ap < 1) return;
            var composable = battle.ComposableChars;
            foreach (var id in ForgeEngine.Suggest(graph, battle.Pool, battle.Library, composable).Composable)
            {
                var direct = ForgeEngine.TryCompose(id, graph, state, battle.LibraryCapacity, composable, battle.PoolCapacity);
                if (direct.Success)
                {
                    result.Add(new BotAction { Kind = ActionKind.Compose, CharId = id });
                    continue;
                }
                if (direct.Error != ForgeError.LibraryFull) continue;
                foreach (var discard in battle.Library.Distinct())
                {
                    var library = new List<string>(battle.Library);
                    library.Remove(discard);
                    var after = ForgeEngine.TryCompose(id, graph, new ForgeState(library, battle.Pool),
                        battle.LibraryCapacity, composable, battle.PoolCapacity);
                    if (after.Success)
                        result.Add(new BotAction { Kind = ActionKind.Compose, CharId = id, DiscardForCompose = discard, Graph = graph });
                }
            }
        }
    }
}
