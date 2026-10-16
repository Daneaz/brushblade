using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Brushblade.Core;

namespace Brushblade.Balance.Bot
{
    /// <summary>局外字估值(spec §4.1):一张字在「该层段的参考局面」里打出最好的一下,值多少快速分。
    ///
    /// 参考局面 = 该层段起始层的普通战编成(<see cref="EndlessGenerator.BuildSegment"/>,固定种子)+ 满血玩家 + 无木灵,
    /// 字库只有这一张字。对它两面 × 全部合法落点(<see cref="Candidates.Generate"/> 里这张字的出字候选)各在
    /// 副本上真执行一次(副本不暴击),取 <see cref="Evaluator.Terms"/> 的最大增量。
    /// 用 Terms 而不是 Quick:前几层一张群攻就能清场,Quick 会记成 1e6 的「已赢」,把估值排序冲垮。
    ///
    /// 结果按「字 × 卡等级 × 层段」缓存;缓存可跨线程共享,每个键只算一次(<see cref="Lazy{T}"/>),
    /// 计算只读入参与固定种子,所以结果与访问顺序、线程交错无关(spec §6)。</summary>
    public sealed class CardAppraiser
    {
        /// <summary>参考局面的固定种子(层段编成与战斗随机流)。</summary>
        public const int ReferenceSeed = 0x0F1A;

        private readonly RecipeGraph _graph;
        private readonly EndlessConfig _endless;
        private readonly Func<BattleConfig> _newConfig;
        private readonly ConcurrentDictionary<(string id, int level, int band), Lazy<double>> _cache =
            new ConcurrentDictionary<(string, int, int), Lazy<double>>();
        private int _computations;

        /// <param name="newConfig">每次返回一份新的战斗配置(角色属性同画像);估值会改它的掉字 / AP 项。</param>
        public CardAppraiser(RecipeGraph graph, EndlessConfig endless, Func<BattleConfig> newConfig)
        {
            _graph = graph;
            _endless = endless;
            _newConfig = newConfig;
        }

        /// <summary>实际算过的键数(测试用:同键只算一次)。</summary>
        public int Computations => Volatile.Read(ref _computations);

        /// <summary>该字在 <paramref name="depth"/> 所在层段、<paramref name="cardLevel"/> 级时的估值。非字(部件)/ 不在字表为 0。</summary>
        public double Value(string charId, int cardLevel, int depth)
        {
            int band = _endless.BandFor(depth).FromDepth;
            return _cache.GetOrAdd((charId, cardLevel, band),
                key => new Lazy<double>(() => Compute(key.id, key.level, key.band), LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        }

        /// <summary>按画像的卡等级表取等级(缺省 1,同 Core 的 CardLevelOf)。</summary>
        public static int LevelOf(IReadOnlyDictionary<string, int> levels, string id) =>
            levels != null && levels.TryGetValue(id, out int lv) ? lv : 1;

        private double Compute(string id, int level, int bandFrom)
        {
            Interlocked.Increment(ref _computations);
            if (!_graph.TryGet(id, out var def) || def.IsComponent || (def.Effects.Count == 0 && def.AttackEffects.Count == 0))
                return 0;

            var runConfig = EndlessGenerator.BuildSegment(_endless, bandFrom, ReferenceSeed);
            var config = _newConfig();
            config.DropsPerTurn = 0;                                 // 字库只留这一张,不掉字
            config.ApPerTurn = Math.Max(config.ApPerTurn, def.ApCost); // 估的是字本身,不受 AP 限制
            var levels = new Dictionary<string, int>(StringComparer.Ordinal) { [id] = level };
            var run = new RunEngine(_graph, runConfig, config, new[] { id }, Array.Empty<string>(),
                seed: ReferenceSeed, cardLevels: levels);
            var battle = run.Battle;
            if (battle.Phase != BattlePhase.PlayerTurn) return 0;

            var copyConfig = config.WithPlayerMaxHp(run.EffectiveMaxHp);
            copyConfig.UnlockedSummonSlots = MetaRules.UnlockedSlotMask(run.CurrentDepth);
            double baseline = Evaluator.Terms(battle, _graph, levels);
            var snap = battle.Capture();
            var defs = Evaluator.EnemyDefsOf(battle);
            double best = 0;
            foreach (var action in Candidates.Generate(battle, _graph))
            {
                if (action.Kind != ActionKind.Cast || action.CharId != id) continue;
                var copy = BattleEngine.Restore(snap, _graph, copyConfig, levels, defs);
                copy.SuppressCritRolls = true;
                if (action.ApplyTo(copy) != BattleError.None) continue;
                best = Math.Max(best, Evaluator.Terms(copy, _graph, levels) - baseline);
            }
            return best;
        }
    }
}
