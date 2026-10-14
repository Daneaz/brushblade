using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Brushblade.Core;

namespace Brushblade.Balance.Bot
{
    /// <summary>一个画像 × 卡等级的爬塔配置(各种子共享,只读;估值缓存线程安全)。</summary>
    public sealed class ClimbSetup
    {
        public RecipeGraph Graph;
        public CampaignConfig Campaign;
        /// <summary>已解锁卡池 = 战后奖励池 = 回合掉字源 = 合成目标锁(同旧工装口径)。</summary>
        public IReadOnlyList<string> OwnedCards;
        public IReadOnlyDictionary<string, int> CardLevels;
        /// <summary>每段新建一份战斗配置(角色属性)。RunEngine 会就地改它(扩容),所以不能共享。</summary>
        public Func<BattleConfig> NewBattleConfig;
        /// <summary>剪枝档位(K / 叶子上限)。驱动每段复制一份再挂弃字规则,不改这份。</summary>
        public SearchOptions Search;
        public CardAppraiser Appraiser;
        public int DepthCap = 300;
        /// <summary>单场玩家回合数上限,超出按僵局判死(卒于当前层),同旧工装。</summary>
        public int StallTurns = 60;
        /// <summary>跨段携带护盾 / 木灵 / 状态(生产口径,缺省 true)。false 只供诊断(<c>--no-board-carry</c>):
        /// 拆分「搜索更聪明」与「携带对齐生产」各自的涨幅,读数不代表游戏。</summary>
        public bool CarryBoard = true;
    }

    /// <summary>新一段 <see cref="RunEngine"/> 的构造参数 = 上一段的携带态(spec §4.3)。
    /// 口径照 <c>GameRoot.OnSegmentEnded</c> 写快照 → <c>StartSegment</c> 读快照传构造参数那一对。</summary>
    public sealed class SegmentStart
    {
        public List<string> Library;
        public List<string> Pool;
        /// <summary>null = 满血开段(第一段)。</summary>
        public int? Hp;
        public int MaxHpBonus;
        public bool LibraryExpanded;
        public bool PoolExpanded;
        public bool Revived;
        public List<OpeningEffect> Openings = new List<OpeningEffect>();
        public int NormalShield;
        public List<SummonSnapshot> Summons = new List<SummonSnapshot>();
        public List<StatusEffect> Statuses = new List<StatusEffect>();
    }

    /// <summary>一局爬塔的结果与读数。</summary>
    public sealed class ClimbResult
    {
        public int Seed;
        public int DeathDepth;
        public bool Stalled;
        public BotStats Stats = new BotStats();
        /// <summary>每张字(含池里打出的部件)被打出的次数。</summary>
        public Dictionary<string, int> CastsByChar = new Dictionary<string, int>(StringComparer.Ordinal);
        /// <summary>以攻面(attackMode = true)出字的次数(同旧工装「攻面出字」)。</summary>
        public int AttackFaceCasts;
        /// <summary>出召唤面的次数(含嫁接 / 替换)。</summary>
        public int SummonCasts;
        /// <summary>两面字的出字:攻面 / 五行面。</summary>
        public int DualAttackCasts;
        public int DualFeatureCasts;
        public long CounterDealt;
        public long CounterClipped;
    }

    /// <summary>一局爬塔的驱动(spec §1:取代旧工装的 PlayTurn / ResolveDropChoice / PickBestReward)。
    /// 种子方案与旧工装相同(塔种子 = seed×7919+17,段种子 = 塔种子×17+起始层),同一画像新旧两版遇到的层段编成一致。
    /// 回合搜索的 turnSeed 由(种子, 层, 回合)合成,不读本体 RNG(spec §2.4)。</summary>
    public static class BotRunner
    {
        public static ulong TurnSeed(int seed, int depth, int turn)
        {
            unchecked
            {
                ulong x = Mix((ulong)(uint)seed ^ 0x0F1A_7000_0000_0000UL);
                x = Mix(x ^ (ulong)(uint)depth);
                return Mix(x ^ ((ulong)(uint)turn << 20));
            }
        }

        /// <summary>多个种子各爬一局;<paramref name="parallel"/> 时种子之间 <see cref="Parallel.For"/>。
        /// 每局的状态、RNG 互不共享,结果按 <paramref name="seeds"/> 的顺序排列,与调度无关(spec §6)。</summary>
        public static ClimbResult[] ClimbMany(ClimbSetup s, IReadOnlyList<int> seeds,
            Func<int, (IReadOnlyList<string> library, IReadOnlyList<string> pool)> start, bool parallel)
        {
            var results = new ClimbResult[seeds.Count];
            void One(int i)
            {
                var (library, pool) = start(seeds[i]);
                results[i] = Climb(s, seeds[i], library, pool);
            }
            if (parallel) Parallel.For(0, seeds.Count, One);
            else for (int i = 0; i < seeds.Count; i++) One(i);
            return results;
        }

        /// <summary>一路深入直到阵亡(永不撤退),返回卒层与读数。</summary>
        public static ClimbResult Climb(ClimbSetup s, int seed, IReadOnlyList<string> library, IReadOnlyList<string> pool)
        {
            var result = new ClimbResult { Seed = seed };
            var endless = s.Campaign.Endless;
            int towerSeed = seed * 7919 + 17;
            int fromDepth = 1;
            var carry = new SegmentStart { Library = library.ToList(), Pool = pool.ToList() };

            while (fromDepth <= s.DepthCap)
            {
                var run = StartSegment(s, carry, fromDepth, towerSeed, out var segmentConfig);
                while (run.Phase == RunPhase.InBattle || run.Phase == RunPhase.Reward
                       || run.Phase == RunPhase.Event || run.Phase == RunPhase.EventOverflow)
                {
                    if (run.Phase == RunPhase.Reward) { PickRewards(run, ValueAt(s, run.CurrentDepth)); continue; }
                    if (run.Phase == RunPhase.Event) { ChooseBestEvent(run); continue; }
                    if (run.Phase == RunPhase.EventOverflow) { run.ResolveOverflowSkip(); continue; }

                    if (PlayBattle(s, run, segmentConfig, seed, result))
                    {
                        result.Stalled = true;
                        result.DeathDepth = fromDepth + run.BattleIndex; // 僵局计为卒于当前层
                        return result;
                    }
                    run.AdvanceAfterBattle();
                }

                if (run.Phase != RunPhase.RunWon)
                {
                    result.DeathDepth = fromDepth + run.BattleIndex;
                    return result;
                }
                carry = CarryFrom(run);
                if (!s.CarryBoard)
                {
                    carry.NormalShield = 0;
                    carry.Summons.Clear();
                    carry.Statuses.Clear();
                }
                fromDepth += endless.BossEvery;
            }
            result.DeathDepth = s.DepthCap;
            return result;
        }

        /// <summary>打一场,返回是否僵局(玩家回合数超过上限)。</summary>
        private static bool PlayBattle(ClimbSetup s, RunEngine run, BattleConfig segmentConfig, int seed, ClimbResult result)
        {
            var battle = run.Battle;
            int depth = run.CurrentDepth;
            var config = BattleConfigFor(run, segmentConfig);
            var value = ValueAt(s, depth);
            var opt = new SearchOptions
            {
                K = s.Search.K, LeafCap = s.Search.LeafCap, Transposition = s.Search.Transposition,
                DiscardPick = lib => { int i = WeakestIndex(lib, value); return i < 0 ? null : lib[i]; },
            };
            int turns = 0;
            while (turns <= s.StallTurns)
            {
                if (battle.Phase == BattlePhase.DropChoice) { ResolveDrop(battle, value); continue; }
                if (battle.Phase != BattlePhase.PlayerTurn) break;
                turns++;
                var executed = Search.PlayTurn(battle, s.Graph, config, s.CardLevels, opt,
                    TurnSeed(seed, depth, battle.Turn), result.Stats);
                foreach (var a in executed) Tally(s.Graph, a, result);
            }
            result.CounterDealt += battle.CounterDamageDealt;
            result.CounterClipped += battle.CounterDamageClipped;
            return turns > s.StallTurns;
        }

        private static void Tally(RecipeGraph graph, BotAction a, ClimbResult r)
        {
            if (a.Kind != ActionKind.Cast) return;
            r.CastsByChar.TryGetValue(a.CharId, out int n);
            r.CastsByChar[a.CharId] = n + 1;
            if (a.AttackMode) r.AttackFaceCasts++;
            if (!graph.TryGet(a.CharId, out var def)) return;
            // 取面口径同旧工装:有攻面且选了攻面才读 AttackEffects
            var effects = a.AttackMode && def.AttackEffects.Count > 0 ? def.AttackEffects : def.Effects;
            if (effects.Any(e => e.Kind == EffectKind.Summon)) r.SummonCasts++;
            if (CardFaceRules.HasTwoFaces(def))
            {
                if (a.AttackMode) r.DualAttackCasts++;
                else r.DualFeatureCasts++;
            }
        }

        private static Func<string, double> ValueAt(ClimbSetup s, int depth) =>
            id => s.Appraiser.Value(id, CardAppraiser.LevelOf(s.CardLevels, id), depth);

        // ---- 段与携带(spec §4.3)----

        /// <summary>段末携带态,照 <c>GameRoot.OnSegmentEnded</c> 写进 <c>EndlessSaveState</c> 的那组字段:
        /// 字库 / 池取携带态(含 Boss 层战利品)、血取 <see cref="RunEngine.CarriedHp"/>(含胜利回血)、
        /// 上限加成、两个扩容标志、复活标志、护盾(段末已按 ShieldCarryPercent 折算)、木灵、厚 / 泉、开局效果。</summary>
        public static SegmentStart CarryFrom(RunEngine run) => new SegmentStart
        {
            Library = new List<string>(run.CarriedLibrary),
            Pool = new List<string>(run.CarriedPool),
            Hp = run.CarriedHp,
            MaxHpBonus = run.MaxHpBonus,
            LibraryExpanded = run.LibraryExpanded,
            PoolExpanded = run.PoolExpanded,
            Revived = run.Revived,
            NormalShield = run.CarriedNormalShield,
            Summons = new List<SummonSnapshot>(run.CarriedSummons),
            Statuses = new List<StatusEffect>(run.CarriedStatuses),
            Openings = new List<OpeningEffect>(run.CarriedOpenings),
        };

        /// <summary>开一段,构造参数照 <c>GameRoot.StartSegment</c> 的新起一段分支(非断点续爬)。
        /// <paramref name="segmentConfig"/> 是传给 RunEngine 的那份配置(已被它就地加上扩容),
        /// 搜索副本的配置由 <see cref="BattleConfigFor"/> 从它派生。</summary>
        public static RunEngine StartSegment(ClimbSetup s, SegmentStart carry, int fromDepth, int towerSeed,
            out BattleConfig segmentConfig)
        {
            var endless = s.Campaign.Endless;
            var runConfig = EndlessGenerator.BuildSegment(endless, fromDepth, towerSeed,
                s.Campaign.Events, s.Campaign.EventChancePercent);
            runConfig.RewardPool = s.OwnedCards; // 同 GameRoot:无条件覆盖为已解锁卡池
            segmentConfig = s.NewBattleConfig();
            segmentConfig.UnlockedChars = s.OwnedCards;
            var run = new RunEngine(s.Graph, runConfig, segmentConfig, carry.Library, carry.Pool,
                seed: unchecked(towerSeed * 17 + fromDepth), cardLevels: s.CardLevels,
                startingHp: carry.Hp,
                startingNormalShield: carry.NormalShield,
                startingSummons: carry.Summons,
                startingStatuses: carry.Statuses,
                libraryExpanded: carry.LibraryExpanded,
                poolExpanded: carry.PoolExpanded,
                maxHpBonus: carry.MaxHpBonus,
                startingOpenings: carry.Openings);
            if (carry.Revived) run.MarkRevived();
            return run;
        }

        /// <summary>本场战斗实际用的配置(同 <c>RunEngine.BattleConfigForRun</c>):段配置 + 本关血量上限 + 按层解锁的召唤槽。
        /// 搜索 / 打分的副本经 <c>Restore</c> 用它,容量、上限才与本体一致。</summary>
        public static BattleConfig BattleConfigFor(RunEngine run, BattleConfig segmentConfig)
        {
            var config = segmentConfig.WithPlayerMaxHp(run.EffectiveMaxHp);
            config.UnlockedSummonSlots = MetaRules.UnlockedSlotMask(run.CurrentDepth);
            return config;
        }

        // ---- 局外选择(spec §4.2)----

        /// <summary>字库里估值最低的一张的下标(同分取靠前的);空库 −1。</summary>
        public static int WeakestIndex(IReadOnlyList<string> library, Func<string, double> value)
        {
            int weakest = -1;
            double weakestValue = double.PositiveInfinity;
            for (int i = 0; i < library.Count; i++)
            {
                double v = value(library[i]);
                if (v < weakestValue) { weakestValue = v; weakest = i; }
            }
            return weakest;
        }

        /// <summary>回合掉字撞满库:掉的字估值高于库中最弱则换入,否则跳过(与旧工装同型,只换估值来源)。</summary>
        public static void ResolveDrop(BattleEngine battle, Func<string, double> value)
        {
            int weakest = WeakestIndex(battle.Library, value);
            if (weakest >= 0 && value(battle.PendingDrop) > value(battle.Library[weakest]))
                battle.ResolveDrop(weakest);
            else
                battle.SkipDrop();
        }

        /// <summary>战利品 5 选 2:取估值最高的;满库时强于库中最弱才替换,否则不再取(与旧工装同型)。</summary>
        public static void PickRewards(RunEngine run, Func<string, double> value)
        {
            while (run.Phase == RunPhase.Reward && run.CharPicksLeft > 0 && run.RewardOptions.Count > 0)
            {
                int best = 0;
                double bestValue = double.NegativeInfinity;
                for (int i = 0; i < run.RewardOptions.Count; i++)
                {
                    double v = value(run.RewardOptions[i]);
                    if (v > bestValue) { bestValue = v; best = i; }
                }
                if (run.PickReward(best)) continue;

                int weakest = WeakestIndex(run.CarriedLibrary, value);
                if (weakest < 0 || bestValue <= value(run.CarriedLibrary[weakest]) || !run.PickRewardReplacing(best, weakest))
                    break;
            }
            if (run.Phase == RunPhase.Reward)
                run.SkipReward();
        }

        /// <summary>奇遇:沿用旧工装的 ChooseBestEvent(spec §4.2)。</summary>
        public static void ChooseBestEvent(RunEngine run)
        {
            var options = run.CurrentEvent.Options;
            var order = Enumerable.Range(0, options.Count).OrderByDescending(i =>
                options[i].Ink + options[i].HpDelta * 2 + (options[i].GainChar != null ? 5 : 0)
                + options[i].GainComponents.Count - options[i].InkCost - options[i].ComponentCost);
            foreach (int i in order)
            {
                var picks = options[i].ComponentCost > 0
                    ? Enumerable.Range(0, options[i].ComponentCost).ToArray() : null; // 抵价取前 N 个
                if (run.ChooseEventOption(i, picks)) return;
            }
            run.ChooseEventOption(0);
        }

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
