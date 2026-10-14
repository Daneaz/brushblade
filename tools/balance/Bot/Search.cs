using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Brushblade.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Brushblade.Balance.Bot
{
    /// <summary>剪枝档位(spec §2.2 第 6 条)。每次取都是新实例,调用方改了不串档。</summary>
    public sealed class SearchOptions
    {
        /// <summary>每层按快速分只留前 K 个子局面继续展开。</summary>
        public int K;
        /// <summary>每回合叶子(完整打分)次数上限;到达即停止展开,按已有叶子取最优。</summary>
        public int LeafCap;
        /// <summary>转置表(同一局面只展开一次)。只为测试对照留开关,生产恒开。</summary>
        public bool Transposition = true;
        /// <summary>复合合字(弃 + 合)只许弃哪一张(spec §4.2:弃估值最低的)。入参是当前字库,返回要弃的字;
        /// null = 不限(每张可弃的字各一条候选,交打分定)。打分不计字库,不限时同分会按候选顺序弃,
        /// 所以爬塔驱动必须给它挂上局外估值。</summary>
        public Func<IReadOnlyList<string>, string> DiscardPick;

        public static SearchOptions Fast => new SearchOptions { K = 4, LeafCap = 256 };
        public static SearchOptions Slow => new SearchOptions { K = 12, LeafCap = 4096 };
    }

    /// <summary>一次 <see cref="Search.BestTurn"/> 的计数。</summary>
    public sealed class SearchStats
    {
        public int Nodes;              // 展开过的局面
        public int UniqueStates;       // 转置表里的不同局面数(开转置时 = Nodes + 终局叶子)
        public int Leaves;             // 完整打分次数
        public int Generated;          // 在副本上试执行的候选数(不含结束回合)
        public int Rejected;           // 其中被 Core 拒的(剪掉)
        public int TranspositionHits;  // 撞上已展开局面而跳过的次数
        public double BestScore = double.NegativeInfinity;

        public void Add(SearchStats o)
        {
            Nodes += o.Nodes; UniqueStates += o.UniqueStates; Leaves += o.Leaves; Generated += o.Generated;
            Rejected += o.Rejected; TranspositionHits += o.TranspositionHits;
        }
    }

    /// <summary>执行层计数(跨回合累加,Task 4 的驱动读它出报告)。</summary>
    public sealed class BotStats
    {
        public int Turns;          // PlayTurn 次数
        public int Searches;       // BestTurn 次数(= Turns + Replans,不计步数上限兜底)
        public int Replans;        // 偏离预期后的重搜
        public int BodyRejected;   // 本体拒了计划里的一步(应恒 0;非 0 说明副本与本体判据分叉)
        public int Steps;          // 本体上执行的动作数(不含结束回合)
        public readonly SearchStats Search = new SearchStats();
        public readonly Dictionary<string, int> ActionCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        internal void Count(BotAction a)
        {
            string kind = a.Kind != ActionKind.Cast ? a.Kind.ToString()
                : a.DropMode != SummonDropMode.Auto ? a.DropMode.ToString()
                : a.AttackMode ? "CastAttack" : "CastFeature";
            ActionCounts.TryGetValue(kind, out int n);
            ActionCounts[kind] = n + 1;
        }
    }

    /// <summary>整回合剪枝搜索(spec §2.2)与执行重搜(§2.3)。
    /// 所有展开都在 <c>Capture → Restore</c> 的副本上;根副本的三条随机流由 turnSeed 派生(§2.4,不取自本体),
    /// 子副本沿用父副本自己的随机流;叶子的敌人回合另用一颗由 turnSeed 派生、与叶子序号无关的固定种子。</summary>
    public static class Search
    {
        /// <summary>执行层的步数兜底:拆字免 AP,重搜理论上有限步收敛,这里防御性封顶。</summary>
        private const int MaxStepsPerTurn = 64;

        private const ulong RootSalt = 0x0F1A_0000_0000_0001UL;
        private const ulong LeafSalt = 0x0F1A_0000_0000_0002UL;

        /// <summary>计划里的一步:动作 + 副本上执行后的局面键(结束回合为 null)。</summary>
        internal sealed class Step
        {
            public BotAction Action;
            public ulong ExpectedKey;
        }

        private sealed class Ctx
        {
            public RecipeGraph Graph;
            public BattleConfig Config;
            public IReadOnlyDictionary<string, int> CardLevels;
            public SearchOptions Opt;
            public ulong LeafSeed;
            public SearchStats Stats;
            public HashSet<ulong> Visited = new HashSet<ulong>();
            public List<Step> Path = new List<Step>();
            public List<Step> Best = new List<Step>();
        }

        /// <summary>从当前局面搜出本回合剩余 AP 的最优动作序列(未分胜负时以 <see cref="ActionKind.EndTurn"/> 收尾)。
        /// 不改本体(副本隔离);结果只取决于本体的对局状态与 <paramref name="turnSeed"/>,与本体随机流无关。</summary>
        public static List<BotAction> BestTurn(BattleEngine b, RecipeGraph g, BattleConfig cfg,
            IReadOnlyDictionary<string, int> cardLevels, SearchOptions opt, ulong turnSeed, SearchStats stats = null)
            => Plan(b, g, cfg, cardLevels, opt, turnSeed, stats ?? new SearchStats()).Select(s => s.Action).ToList();

        internal static List<Step> Plan(BattleEngine b, RecipeGraph g, BattleConfig cfg,
            IReadOnlyDictionary<string, int> cardLevels, SearchOptions opt, ulong turnSeed, SearchStats stats)
        {
            if (b.Phase != BattlePhase.PlayerTurn) return new List<Step>();
            var ctx = new Ctx
            {
                Graph = g, Config = cfg, CardLevels = cardLevels, Opt = opt, Stats = stats,
                LeafSeed = Mix(turnSeed ^ LeafSalt),
            };
            var root = Evaluator.CloneWithSeed(b, g, cfg, cardLevels, Mix(turnSeed ^ RootSalt));
            root.SuppressCritRolls = true; // §2.3:副本暴击按不暴击算(必暴照算)
            Visit(root, StateKey(root), ctx, depth: 0, stopLeafDone: false);
            stats.UniqueStates = ctx.Visited.Count;
            return ctx.Best;
        }

        private static bool CapReached(Ctx c) => c.Stats.Leaves >= c.Opt.LeafCap;

        private static void Visit(BattleEngine node, ulong key, Ctx c, int depth, bool stopLeafDone)
        {
            if (CapReached(c)) return;
            if (c.Opt.Transposition && !c.Visited.Add(key)) { c.Stats.TranspositionHits++; return; }
            c.Stats.Nodes++;

            // 「到此结束回合」本身是一片叶子(根的子节点已在深入前统一打过,见下)
            if (!stopLeafDone) Leaf(node, endTurn: true, c);

            var picked = Pick(node, c);
            // 叶子上限下防 DFS 偏斜:先给每个根子节点打一片「到此结束」叶子,再逐个深入
            if (depth == 0)
                foreach (var ch in picked)
                {
                    if (ch.engine.Phase != BattlePhase.PlayerTurn) continue;
                    c.Path.Add(new Step { Action = ch.action, ExpectedKey = ch.key });
                    Leaf(ch.engine, endTurn: true, c);
                    c.Path.RemoveAt(c.Path.Count - 1);
                }

            foreach (var ch in picked)
            {
                if (CapReached(c)) break;
                c.Path.Add(new Step { Action = ch.action, ExpectedKey = ch.key });
                if (ch.engine.Phase == BattlePhase.PlayerTurn) Visit(ch.engine, ch.key, c, depth + 1, stopLeafDone: depth == 0);
                else if (!c.Opt.Transposition || c.Visited.Add(ch.key))
                    Leaf(ch.engine, endTurn: false, c); // 分出胜负(或挂起掉字):不再接结束回合
                c.Path.RemoveAt(c.Path.Count - 1);
            }
        }

        /// <summary>在副本上试执行全部候选(被 Core 拒的剪掉),按快速分降序取前 K 个未展开过的子局面。
        /// AP 用完时只剩免 AP 的拆字:只留**当场有结算**的(Lv4 字形特性等,LastEvents 非空)——
        /// 没有即时效果的拆在 AP 0 时改变不了本回合(部件出不了、印记回合末作废),展开它只会让叶子数爆炸。</summary>
        private static List<(BotAction action, BattleEngine engine, ulong key)> Pick(BattleEngine node, Ctx c)
        {
            bool apSpent = node.Ap <= 0;
            var children = new List<(BotAction action, BattleEngine engine, double quick, int order)>();
            var snap = node.Capture(); // 每节点只 Capture 一次:Restore 对快照全是深拷贝(列表 / 状态 / 本命逐个 Clone)
            var defs = Evaluator.EnemyDefsOf(node);
            foreach (var action in Candidates.Generate(node, c.Graph))
            {
                if (action.Kind == ActionKind.EndTurn) continue;
                if (apSpent && action.Kind != ActionKind.Dismantle) continue;
                if (action.DiscardForCompose != null && c.Opt.DiscardPick != null
                    && action.DiscardForCompose != c.Opt.DiscardPick(node.Library)) continue;
                c.Stats.Generated++;
                var child = BattleEngine.Restore(snap, c.Graph, c.Config, c.CardLevels, defs);
                child.SuppressCritRolls = true;
                if (action.ApplyTo(child) != BattleError.None) { c.Stats.Rejected++; continue; }
                if (apSpent && child.LastEvents.Count == 0) continue;
                children.Add((action, child, Evaluator.Quick(child), children.Count));
            }

            var picked = new List<(BotAction, BattleEngine, ulong)>();
            var local = new HashSet<ulong>();
            foreach (var ch in children.OrderByDescending(x => x.quick).ThenBy(x => x.order))
            {
                if (picked.Count >= c.Opt.K) break;
                ulong childKey = StateKey(ch.engine);
                if (c.Opt.Transposition && (c.Visited.Contains(childKey) || !local.Add(childKey)))
                {
                    c.Stats.TranspositionHits++;
                    continue;
                }
                picked.Add((ch.action, ch.engine, childKey));
            }
            return picked;
        }

        private static void Leaf(BattleEngine state, bool endTurn, Ctx c)
        {
            if (CapReached(c)) return;
            c.Stats.Leaves++;
            double score = Evaluator.Full(state, c.Graph, c.Config, c.CardLevels, c.LeafSeed);
            if (score <= c.Stats.BestScore) return; // 同分取先到的(快速分排序靠前 / 更短)
            c.Stats.BestScore = score;
            c.Best = new List<Step>(c.Path);
            if (endTurn) c.Best.Add(new Step { Action = new BotAction { Kind = ActionKind.EndTurn } });
        }

        /// <summary>在本体上执行一回合(spec §2.3):搜出序列逐步执行,每步后比对本体与副本预期的局面键,
        /// 不一致(暴击、击杀改变目标、随机落点不同等)就从当前状态对剩余 AP 重搜。
        /// 本体拒了某一步同样重搜(计入 <see cref="BotStats.BodyRejected"/>)。
        /// 返回本体上实际执行(且受理)的动作。结束回合后返回;中途分出胜负或挂起掉字时也返回,交由驱动处理。</summary>
        public static List<BotAction> PlayTurn(BattleEngine b, RecipeGraph g, BattleConfig cfg,
            IReadOnlyDictionary<string, int> cardLevels, SearchOptions opt, ulong turnSeed, BotStats stats)
        {
            var executed = new List<BotAction>();
            if (b.Phase != BattlePhase.PlayerTurn) return executed;
            stats.Turns++;

            var plan = Replan(b, g, cfg, cardLevels, opt, turnSeed, stats);
            int next = 0;
            for (int steps = 0; b.Phase == BattlePhase.PlayerTurn; steps++)
            {
                if (steps >= MaxStepsPerTurn || next >= plan.Count)
                {
                    var end = new BotAction { Kind = ActionKind.EndTurn };
                    end.ApplyTo(b);
                    executed.Add(end);
                    break;
                }

                var step = plan[next++];
                var error = step.Action.ApplyTo(b);
                if (error != BattleError.None)
                {
                    stats.BodyRejected++;
                    stats.Replans++;
                    plan = Replan(b, g, cfg, cardLevels, opt, turnSeed, stats);
                    next = 0;
                    continue;
                }
                executed.Add(step.Action);
                if (step.Action.Kind == ActionKind.EndTurn) break;
                stats.Steps++;
                stats.Count(step.Action);

                if (b.Phase != BattlePhase.PlayerTurn) break;
                if (StateKey(b) != step.ExpectedKey)
                {
                    stats.Replans++;
                    plan = Replan(b, g, cfg, cardLevels, opt, turnSeed, stats);
                    next = 0;
                }
            }
            return executed;
        }

        private static List<Step> Replan(BattleEngine b, RecipeGraph g, BattleConfig cfg,
            IReadOnlyDictionary<string, int> cardLevels, SearchOptions opt, ulong turnSeed, BotStats stats)
        {
            stats.Searches++;
            var s = new SearchStats();
            var plan = Plan(b, g, cfg, cardLevels, opt, turnSeed, s);
            stats.Search.Add(s);
            return plan;
        }

        // ---- 局面键 ----

        /// <summary>转置表 / 偏离判定用的局面键(<see cref="StateHash"/>)。</summary>
        internal static ulong StateKey(BattleEngine b) => StateHash.Of(b);

        /// <summary>键的 JSON 对照(去掉与 <see cref="StateHash.Ignored"/> 相同的字段),只给冲突测试用。</summary>
        internal static string StateKeyJson(BattleEngine b) => JsonConvert.SerializeObject(b.Capture(), KeySettings);

        private static readonly JsonSerializerSettings KeySettings = new JsonSerializerSettings
        {
            ContractResolver = new KeyResolver(),
        };

        private sealed class KeyResolver : DefaultContractResolver
        {
            protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
            {
                var p = base.CreateProperty(member, memberSerialization);
                if (StateHash.Ignored.Contains(member.Name)) p.ShouldSerialize = _ => false;
                return p;
            }
        }

        // ---- 种子派生 ----

        private static ulong Mix(ulong x)
        {
            unchecked
            {
                ulong z = x + 0x9E3779B97F4A7C15UL;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }
    }
}
