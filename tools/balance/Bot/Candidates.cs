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

        /// <summary>在给定引擎上执行。复合合字先弃后合;弃字失败直接返回,不再合。</summary>
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
                var enemyTargets = EnemyTargets(battle, id, attackMode);
                if (enemyTargets.Count == 0) continue;
                foreach (var ally in AllyOptions(battle, def, attackMode))
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
        /// 判据 = NeedsTargetNow(与 CastCore 同源,含池中部件的拆字印记)。</summary>
        private static List<int> EnemyTargets(BattleEngine battle, string id, bool attackMode)
        {
            if (!battle.NeedsTargetNow(id, attackMode)) return new List<int> { -1 };
            var targets = new List<int>();
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
        private static List<AllyOption> AllyOptions(BattleEngine battle, CharDef def, bool attackMode)
        {
            var options = new List<AllyOption>();
            int summonCount = battle.SummonCountOf(def, attackMode);
            if (summonCount > 0)
            {
                for (int slot = 0; slot < BattleEngine.MaxSummonSlots; slot++)
                {
                    if (!battle.IsSlotOpen(slot) || battle.SlotOccupancy(slot) == SlotState.Alive) continue;
                    var plan = battle.PlanSummonSlots(slot, summonCount);
                    options.Add(new AllyOption(Targeting.PlayerTarget, plan,
                        replaceSummon: battle.SummonReplaceCountOf(def, attackMode, plan) > 0));
                }
                for (int slot = 0; slot < BattleEngine.MaxSummonSlots; slot++)
                {
                    if (battle.CanGraftAt(def.Id, attackMode, slot))
                        options.Add(new AllyOption(slot, dropMode: SummonDropMode.Graft));
                    if (battle.CanReplaceAt(def.Id, attackMode, slot))
                        options.Add(new AllyOption(slot, dropMode: SummonDropMode.Replace));
                }
                return options;
            }

            options.Add(new AllyOption(Targeting.PlayerTarget));
            bool needsAlly = BattleEngine.NeedsAllyTarget(def, attackMode);
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
                        result.Add(new BotAction { Kind = ActionKind.Compose, CharId = id, DiscardForCompose = discard });
                }
            }
        }
    }
}
