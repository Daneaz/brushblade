using System;
using System.Collections.Generic;
using System.Linq;

namespace Brushblade.Core
{
    public enum RunPhase
    {
        InBattle,
        Reward,   // 战斗胜利,三选一奖励(9.5)
        Event,    // 奇遇:短情境 + 选择(9.6)
        EventOverflow, // 奇遇给的部件超上限:逐个替换/跳过(2026-07-24)
        Reviving, // 看广告复活:补给注入当前战斗(2026-07-24)
        RunWon,
        RunLost,
    }

    /// <summary>一段连战的配置(阶段 1 验证格式:3~5 场,17.2)。</summary>
    public sealed class RunConfig
    {
        /// <summary>每场遭遇的敌人列表。</summary>
        public IReadOnlyList<IReadOnlyList<EnemyDef>> Encounters { get; set; }

        /// <summary>战后字奖励池(抽 5 选 1)。</summary>
        public IReadOnlyList<string> RewardPool { get; set; }

        /// <summary>奇遇事件池(9.6);空则无奇遇。</summary>
        public IReadOnlyList<EventDef> EventPool { get; set; } = System.Array.Empty<EventDef>();

        /// <summary>两场战斗之间触发奇遇的概率(百分比,0~100)。</summary>
        public int EventChancePercent { get; set; }
    }

    /// <summary>连战状态机:战斗 → 奖励 → 下一战。
    /// 跨战斗规则:HP 保留(第 9 章)、部件池保留(3.8.2)、出字即消耗不回归(3.8.1 v0.7 拍板)。</summary>
    public sealed class RunEngine
    {
        private const int RewardOptionCount = 5; // 战利品字候选数(普通战斗 5 选 2,2026-08-04 起)
        private const int RewardPicks = 2;       // 普通战斗 5 选 2(2026-08-04;Boss 层奖励走宝箱,不经此)

        /// <summary>部件奖励固定候选:五行基础部件(奇遇 randomComponents 用;
        /// 2026-08-04 起战利品与复活补给都不再走这份候选,五行部件改为只能靠拆字获得)。</summary>
        public static readonly IReadOnlyList<string> ComponentRewardChoices =
            new[] { "金", "木", "水", "火", "土" };

        private readonly RecipeGraph _graph;
        private readonly RunConfig _runConfig;
        private readonly BattleConfig _battleConfig;
        private readonly GameRandom _random;
        private readonly IReadOnlyDictionary<string, int> _cardLevels;
        private readonly List<string> _rewardOptions = new();
        private readonly List<string> _defeatedEnemyIds = new();

        // 战斗之间的携带状态(奖励与奇遇的作用对象)
        private List<string> _carriedLibrary;
        private List<string> _carriedPool;
        private int _carriedHp;
        // 局内血量上限加成(2026-08-04):奇遇按当前有效上限的百分比复利累加。
        // 局外 Meta.MaxHpFor 硬顶 100,而怪物 scale 无上限 —— 这是关内把上限顶上去的唯一手段。
        private int _maxHpBonus;
        private int _carriedNormalShield;
        private int _carriedPersistShield;
        private List<SummonSnapshot> _carriedSummons = new(); // 召唤物延续(2026-08-03):只带活的,残血原样
        private List<StatusEffect> _carriedStatuses = new(); // 减伤延续(2026-08-04):段内持久,到段末才清;
                                                               // 只承载 DamageReduction,HoT 不跨战斗
        private readonly int _perFloorNormalShield; // 金汤:每关开战补的护盾(叠加上关剩余)

        public RunEngine(RecipeGraph graph, RunConfig runConfig, BattleConfig battleConfig,
            IReadOnlyList<string> startingLibrary, IReadOnlyList<string> startingPool, int seed,
            IReadOnlyDictionary<string, int> cardLevels = null, int startingInk = 0,
            int? startingHp = null, int startingNormalShield = 0, int startingPersistShield = 0,
            int perFloorNormalShield = 0, IReadOnlyList<SummonSnapshot> startingSummons = null,
            IReadOnlyList<StatusEffect> startingStatuses = null)
        {
            _startingInk = startingInk;
            _graph = graph;
            _runConfig = runConfig;
            _battleConfig = battleConfig;
            _cardLevels = cardLevels;
            _random = new GameRandom(seed);
            Phase = RunPhase.InBattle;
            BattleIndex = 0;
            _carriedNormalShield = startingNormalShield;
            _carriedPersistShield = startingPersistShield;
            _perFloorNormalShield = perFloorNormalShield;
            if (startingSummons != null) _carriedSummons = new List<SummonSnapshot>(startingSummons);
            if (startingStatuses != null)
                _carriedStatuses = startingStatuses.Select(s => s.Clone()).ToList();
            // 携带态一开始就等于开打时的状态,而不是 null:第一场打完前挂起也有东西可存,
            // 且省掉一处 null 陷阱(AdvanceAfterBattle 会照常整体覆盖)
            _carriedLibrary = new List<string>(startingLibrary);
            _carriedPool = new List<string>(startingPool);
            _carriedHp = startingHp ?? battleConfig.PlayerMaxHp;
            Battle = NewBattle(startingLibrary, startingPool, startingHp); // 断点续爬恢复血量(20.6)
        }

        /// <summary>断点存档专用构造:不开第一场,状态由 <see cref="Restore"/> 灌入。</summary>
        private RunEngine(RecipeGraph graph, RunConfig runConfig, BattleConfig battleConfig,
            IReadOnlyDictionary<string, int> cardLevels, int startingInk, int perFloorNormalShield,
            GameRandom random)
        {
            _graph = graph;
            _runConfig = runConfig;
            _battleConfig = battleConfig;
            _cardLevels = cardLevels;
            _startingInk = startingInk;
            _perFloorNormalShield = perFloorNormalShield;
            _random = random;
        }

        /// <summary>战斗内断点存档(2026-07-27):连当前战斗一起摊平。</summary>
        public RunSnapshot Capture()
        {
            var snapshot = new RunSnapshot
            {
                Phase = Phase,
                BattleIndex = BattleIndex,
                ClearedBattleIndex = ClearedBattleIndex,
                RandomState = _random.State,
                CarriedLibrary = new List<string>(_carriedLibrary),
                CarriedPool = new List<string>(_carriedPool),
                CarriedHp = _carriedHp,
                MaxHpBonus = _maxHpBonus,
                CarriedNormalShield = _carriedNormalShield,
                CarriedPersistShield = _carriedPersistShield,
                CarriedSummons = new List<SummonSnapshot>(_carriedSummons),
                CarriedStatuses = _carriedStatuses.Select(s => s.Clone()).ToList(),
                CharPicksLeft = CharPicksLeft,
                RewardOptions = new List<string>(_rewardOptions),
                ComponentOptions = new List<string>(_componentOptions),
                CurrentEventId = CurrentEvent?.Id,
                EarnedInk = EarnedInk,
                LibraryExpanded = LibraryExpanded,
                PoolExpanded = PoolExpanded,
                Revived = Revived,
                ReviveCharPicksLeft = ReviveCharPicksLeft,
                ReviveRoundsLeft = ReviveRoundsLeft,
                DefeatedEnemyIds = new List<string>(_defeatedEnemyIds),
                Battle = Battle?.Capture(),
            };
            return snapshot;
        }

        /// <summary>从断点存档复原。runConfig 须由外层用同一颗 Seed 与层深重建
        /// (层段生成是纯函数,重建结果一致),本方法只负责把可变状态灌回去。</summary>
        public static RunEngine Restore(RunSnapshot snapshot, RecipeGraph graph, RunConfig runConfig,
            BattleConfig battleConfig, IReadOnlyDictionary<string, int> cardLevels,
            int startingInk = 0, int perFloorNormalShield = 0)
        {
            var run = new RunEngine(graph, runConfig, battleConfig, cardLevels, startingInk,
                perFloorNormalShield, GameRandom.FromState(snapshot.RandomState))
            {
                Phase = snapshot.Phase,
                BattleIndex = snapshot.BattleIndex,
                ClearedBattleIndex = snapshot.ClearedBattleIndex,
                _carriedLibrary = new List<string>(snapshot.CarriedLibrary),
                _carriedPool = new List<string>(snapshot.CarriedPool),
                _carriedHp = snapshot.CarriedHp,
                _maxHpBonus = snapshot.MaxHpBonus,
                _carriedNormalShield = snapshot.CarriedNormalShield,
                _carriedPersistShield = snapshot.CarriedPersistShield,
                _carriedSummons = new List<SummonSnapshot>(snapshot.CarriedSummons),
                _carriedStatuses = snapshot.CarriedStatuses.Select(s => s.Clone()).ToList(),
                CharPicksLeft = snapshot.CharPicksLeft,
                EarnedInk = snapshot.EarnedInk,
                LibraryExpanded = snapshot.LibraryExpanded,
                PoolExpanded = snapshot.PoolExpanded,
                Revived = snapshot.Revived,
                ReviveCharPicksLeft = snapshot.ReviveCharPicksLeft,
                ReviveRoundsLeft = snapshot.ReviveRoundsLeft,
            };
            run._rewardOptions.AddRange(snapshot.RewardOptions);
            run._componentOptions.AddRange(snapshot.ComponentOptions);
            run._defeatedEnemyIds.AddRange(snapshot.DefeatedEnemyIds);
            // 扩容是构造 BattleConfig 时算进容量的,复原后要补回上限(容量本身不入快照)
            if (snapshot.LibraryExpanded) battleConfig.LibraryCapacity += ExpandBonus;
            if (snapshot.PoolExpanded) battleConfig.PoolCapacity += ExpandBonus;
            foreach (var candidate in runConfig.EventPool)
                if (candidate.Id == snapshot.CurrentEventId) run.CurrentEvent = candidate;
            if (snapshot.Battle != null)
                // 走 BattleConfigForRun():局内上限加成已从快照灌回,续爬的战斗要用抬高后的上限
                run.Battle = BattleEngine.Restore(snapshot.Battle, graph, run.BattleConfigForRun(),
                    cardLevels, EnemyDefsOf(runConfig));
            return run;
        }

        /// <summary>本段所有遭遇里出现过的字怪定义(id → def),供战斗复原按 id 找回。</summary>
        private static IReadOnlyDictionary<string, EnemyDef> EnemyDefsOf(RunConfig runConfig)
        {
            var map = new Dictionary<string, EnemyDef>();
            foreach (var encounter in runConfig.Encounters)
                foreach (var def in encounter)
                    map[def.Id] = def;
            return map;
        }

        public RunPhase Phase { get; private set; }
        public int BattleIndex { get; private set; }

        /// <summary>最近打赢的那一层(段内下标;−1 = 一层未清)。层记账的基准 —— 战利品取完后
        /// BattleIndex 就跳到下一层了,拿它算「刚打完第几层」会多记一层,断点快照会跳过 Boss 层
        /// (2026-07-27 修)。走奇遇的层不跳,所以那个 bug 时有时无。</summary>
        public int ClearedBattleIndex { get; private set; } = -1;
        public BattleEngine Battle { get; private set; }

        /// <summary>本段打赢过的敌人 id(图鉴解锁源;外层写进 MetaState)。</summary>
        public IReadOnlyList<string> DefeatedEnemyIds => _defeatedEnemyIds;

        /// <summary>局内 UI 显示等级化数值用(19.3.2);未记录则 1 级。</summary>
        public int CardLevel(string cardId) =>
            _cardLevels != null && _cardLevels.TryGetValue(cardId, out var level) ? level : 1;

        /// <summary>奖励阶段的字候选(已取走的即时移除)。</summary>
        public IReadOnlyList<string> RewardOptions => _rewardOptions;

        /// <summary>部件候选(固定五行,已取走的即时移除);2026-08-04 起战利品与复活补给都
        /// 不再填充它,当前恒为空——字段保留(不是本次改造的范围)。</summary>
        public IReadOnlyList<string> ComponentOptions => _componentOptions;

        private readonly List<string> _componentOptions = new();

        public int CharPicksLeft { get; private set; }

        /// <summary>战斗间携带的字库(Reward/Event/RunWon 阶段有效;段末快照的数据源)。</summary>
        public IReadOnlyList<string> CarriedLibrary => _carriedLibrary;

        /// <summary>战斗间携带的部件池(同上)。</summary>
        public IReadOnlyList<string> CarriedPool => _carriedPool;

        public int CarriedNormalShield => _carriedNormalShield;
        public int CarriedPersistShield => _carriedPersistShield;

        /// <summary>战斗间携带的减伤来源(段内持久,到段末才清)。</summary>
        public IReadOnlyList<StatusEffect> CarriedStatuses => _carriedStatuses;

        /// <summary>战斗间携带的召唤物(只含存活者;整次登塔延续,见 20.2)。</summary>
        public IReadOnlyList<SummonSnapshot> CarriedSummons => _carriedSummons;

        public bool LibraryExpanded { get; private set; }
        public bool PoolExpanded { get; private set; }

        /// <summary>当前奇遇(Phase == Event 时非空)。</summary>
        public EventDef CurrentEvent { get; private set; }

        /// <summary>奇遇累积的墨锭净变化(可为负 = 字摊消费;run 结束由外层入账)。</summary>
        public int EarnedInk { get; private set; }

        private readonly int _startingInk;

        /// <summary>当前可支配墨锭(入场余额 + 关内净变化),字摊消费的预算。</summary>
        public int AvailableInk => _startingInk + EarnedInk;

        /// <summary>奇遇选择:应用后果并进入下一战(治疗不超上限,损伤至少留 1,9.6)。
        /// 消费付不起时返回 false,停留在事件中。部件抵价(ComponentCost)须由玩家指定
        /// 不要的部件下标(数量吻合、无重复、不越界,2026-07-19)。
        /// 任选字(GainCharChoices)须给 charChoiceIndex。全部先验后扣:拒绝不动任何状态。</summary>
        public bool ChooseEventOption(int index, IReadOnlyList<int> discardPoolIndices = null,
            int charChoiceIndex = -1, int replaceLibraryIndex = -1)
        {
            if (Phase != RunPhase.Event) return false;
            var option = CurrentEvent.Options[index];
            if (option.InkCost > AvailableInk)
                return false; // 买不起,换个选项

            string gainChar = option.GainChar;
            if (option.GainCharChoices.Count > 0)
            {
                if (charChoiceIndex < 0 || charChoiceIndex >= option.GainCharChoices.Count)
                    return false; // 任选字须指定选哪一个
                gainChar = option.GainCharChoices[charChoiceIndex];
            }
            if (gainChar != null && _battleConfig.UnlockedChars != null
                && !_battleConfig.UnlockedChars.Contains(gainChar))
                return false; // 不在出阵列表(2026-07-20:字摊与战利品/合成同源,没编入就换不到)
            // 字库满:须指定换掉哪一张(2026-07-22,与战利品 PickRewardReplacing 同一口径);
            // 未指定则拒绝,由表现层转入「换掉哪一个」子步。先验后扣,部件不受损。
            bool replacing = gainChar != null && _carriedLibrary.Count >= _battleConfig.LibraryCapacity;
            if (replacing && (replaceLibraryIndex < 0 || replaceLibraryIndex >= _carriedLibrary.Count))
                return false;

            if (option.ComponentCost > 0)
            {
                if (option.ComponentCost > _carriedPool.Count)
                    return false; // 池总量不够,换不起
                if (discardPoolIndices == null || discardPoolIndices.Count != option.ComponentCost)
                    return false; // 须选够指定数量
                var picks = new List<int>(discardPoolIndices);
                picks.Sort();
                for (int i = 0; i < picks.Count; i++)
                {
                    if (picks[i] < 0 || picks[i] >= _carriedPool.Count) return false; // 越界
                    if (i > 0 && picks[i] == picks[i - 1]) return false;              // 重复
                }
                for (int i = picks.Count - 1; i >= 0; i--) // 从后往前删,下标不漂移
                    _carriedPool.RemoveAt(picks[i]);
            }

            if (gainChar != null)
            {
                if (replacing) _carriedLibrary[replaceLibraryIndex] = gainChar; // 被换的字永久移除
                else _carriedLibrary.Add(gainChar);
            }
            // 入池部件:确定项(原序)+ 随机项(只掷一次,防重掷抖动/破种子)。
            // 空位先填,填不下的进溢出队列交玩家决议(替换/跳过,2026-07-24)——不再静默丢。
            var incoming = new List<string>(option.GainComponents);
            for (int i = 0; i < option.RandomComponents; i++)
                incoming.Add(ComponentRewardChoices[_random.Next(ComponentRewardChoices.Count)]);
            foreach (var component in incoming)
                if (_carriedPool.Count < _battleConfig.PoolCapacity)
                    _carriedPool.Add(component);
                else
                    _pendingOverflow.Add(component);
            bool inkWon = option.InkChancePercent <= 0 || _random.Next(100) < option.InkChancePercent;
            EarnedInk += (inkWon ? option.Ink : 0) - option.InkCost;
            // 局内上限:先结算,治疗才吃得到新上限。掷空不是「无事发生」而是反向扣同样百分比。
            if (option.MaxHpPercent != 0)
            {
                bool gained = option.MaxHpChancePercent <= 0
                    || _random.Next(100) < option.MaxHpChancePercent;
                int delta = EffectiveMaxHp * (gained ? option.MaxHpPercent : -option.MaxHpPercent) / 100;
                _maxHpBonus += delta;
                if (delta > 0) _carriedHp += delta;                    // 拿到的是血也是容器
                _carriedHp = Math.Clamp(_carriedHp, 1, EffectiveMaxHp); // 扣上限时把当前血收回来
            }
            if (option.HpDelta > 0)
                _carriedHp = Math.Min(EffectiveMaxHp, _carriedHp + option.HpDelta);
            else if (option.HpDelta < 0)
                _carriedHp = Math.Max(1, _carriedHp + option.HpDelta);

            CurrentEvent = null;
            if (_pendingOverflow.Count > 0)
            {
                Phase = RunPhase.EventOverflow; // 部件超上限:停下让玩家逐个替换/跳过
                return true;
            }
            BeginNextBattle();
            return true;
        }

        /// <summary>奇遇部件溢出待决议项(队首为当前项;EventOverflow 阶段有效,2026-07-24)。</summary>
        public IReadOnlyList<string> PendingOverflow => _pendingOverflow;

        private readonly List<string> _pendingOverflow = new();

        /// <summary>溢出决议:用队首溢出项换掉池中指定一个(被换的永久移除)。队空则进下一战。</summary>
        public bool ResolveOverflowReplace(int poolIndex)
        {
            if (Phase != RunPhase.EventOverflow) return false;
            if (poolIndex < 0 || poolIndex >= _carriedPool.Count) return false;
            _carriedPool[poolIndex] = _pendingOverflow[0];
            _pendingOverflow.RemoveAt(0);
            FinishOverflowIfDone();
            return true;
        }

        /// <summary>溢出决议:丢弃队首溢出项。队空则进下一战。</summary>
        public void ResolveOverflowSkip()
        {
            if (Phase != RunPhase.EventOverflow) return;
            _pendingOverflow.RemoveAt(0);
            FinishOverflowIfDone();
        }

        private void FinishOverflowIfDone()
        {
            if (_pendingOverflow.Count == 0)
                BeginNextBattle();
        }

        private const int ExpandBonus = 2; // 广告扩容的容量增量(复原时也要按它补回上限)

        /// <summary>局内广告扩容:字库 +2,一局一次,跨场有效(2026-07-06 拍板)。
        /// 无尽塔 = 整次登塔一次:跨段由外层快照恢复,塔结算随快照清除。</summary>
        public bool TryExpandLibrary()
        {
            if (LibraryExpanded) return false;
            _battleConfig.LibraryCapacity += ExpandBonus;
            LibraryExpanded = true;
            return true;
        }

        /// <summary>局内广告扩容:部件池 +2,一局一次(同上)。</summary>
        public bool TryExpandPool()
        {
            if (PoolExpanded) return false;
            _battleConfig.PoolCapacity += ExpandBonus;
            PoolExpanded = true;
            return true;
        }

        // ---- 广告复活(2026-07-24):整次登塔一次,满血续战 + 补给注入当前战斗 ----

        private const int ReviveCharPicks = 2;   // 复活补给:每轮选字次数
        private const int ReviveRounds = 2;      // 复活补给:重抽轮数(2026-08-04:两轮各 5 选 2 = 4 字)

        /// <summary>本次登塔是否已用过复活(一次性;进快照,断点续爬恢复,GameRoot 处理)。</summary>
        public bool Revived { get; private set; }

        /// <summary>复活补给本轮剩余选字次数 / 剩余重抽轮数(Reviving 阶段有效)。</summary>
        public int ReviveCharPicksLeft { get; private set; }
        public int ReviveRoundsLeft { get; private set; }

        /// <summary>可复活:当前战斗已败北且本次登塔未用过复活。</summary>
        public bool ReviveAvailable => Battle.Phase == BattlePhase.Lost && !Revived;

        /// <summary>看广告复活:满血续战(接着打这一场),并进入 Reviving 补给阶段。</summary>
        public bool TryRevive()
        {
            if (!ReviveAvailable) return false;
            Revived = true;
            Battle.Revive(); // HP 回满 + 回到玩家回合
            RollRewardOptions();
            ReviveCharPicksLeft = ReviveCharPicks;
            ReviveRoundsLeft = ReviveRounds;
            Phase = RunPhase.Reviving;
            return true;
        }

        /// <summary>复活补给取一字:直接注入当前战斗字库(非携带快照)。满库或额度尽返回 false。</summary>
        public bool PickReviveChar(int index)
        {
            if (Phase != RunPhase.Reviving || ReviveCharPicksLeft == 0) return false;
            if (index < 0 || index >= _rewardOptions.Count) return false;
            if (!Battle.GrantLibraryChar(_rewardOptions[index])) return false; // 满库不入
            _rewardOptions.RemoveAt(index);
            ReviveCharPicksLeft -= 1;
            MaybeFinishRevive();
            return true;
        }

        /// <summary>复活补给满库替换(2026-08-04):换掉战斗字库第 <paramref name="replaceIndex"/> 张。
        /// 此前满库只能眼看着额度作废 —— 看了广告却一无所得,故与战利品 PickRewardReplacing 拉齐。</summary>
        public bool PickReviveCharReplacing(int index, int replaceIndex)
        {
            if (Phase != RunPhase.Reviving || ReviveCharPicksLeft == 0) return false;
            if (index < 0 || index >= _rewardOptions.Count) return false;
            if (!Battle.ReplaceLibraryChar(replaceIndex, _rewardOptions[index])) return false;
            _rewardOptions.RemoveAt(index);
            ReviveCharPicksLeft -= 1;
            MaybeFinishRevive();
            return true;
        }

        /// <summary>放弃剩余复活补给,直接接着打。</summary>
        public void SkipReviveReward()
        {
            if (Phase != RunPhase.Reviving) return;
            Phase = RunPhase.InBattle;
        }

        /// <summary>断点续爬恢复:标记本次登塔已复活过(防重进本层二次复活)。</summary>
        public void MarkRevived() => Revived = true;

        private void MaybeFinishRevive()
        {
            if (ReviveCharPicksLeft > 0 && _rewardOptions.Count > 0) return; // 本轮还能取

            ReviveRoundsLeft -= 1;
            if (ReviveRoundsLeft > 0)
            {
                RollRewardOptions();                  // 下一轮:候选重新抽满
                ReviveCharPicksLeft = ReviveCharPicks;
                return;
            }
            Phase = RunPhase.InBattle; // 轮次用尽,接着打这一场
        }

        /// <summary>战斗分出胜负后由视图调用:胜 → 奖励/通关,负 → 结算 run。</summary>
        public void AdvanceAfterBattle()
        {
            if (Phase != RunPhase.InBattle) return;

            if (Battle.Phase == BattlePhase.Lost)
            {
                // 此时 _carriedSummons 仍是上一层的阵容,没清空:安全,因为 TryRevive 复活的是
                // 同一个 BattleEngine(不走 NewBattle),OnSegmentEnded(won:false) 在写盘前就
                // 从这里早退,随后塔结算把 _meta.Endless 置 null。若将来复活改成本层重开,
                // 死掉的召唤物会集体诈尸,那时才需要在这里清空。
                Phase = RunPhase.RunLost;
                return;
            }
            if (Battle.Phase != BattlePhase.Won) return;

            ClearedBattleIndex = BattleIndex; // 这一层已清:记账基准就地钉住,后续开下一战也不动

            // 图鉴解锁(2026-07-22):打赢才记,外层负责同步进 MetaState
            foreach (var enemy in Battle.Enemies)
                if (!_defeatedEnemyIds.Contains(enemy.Def.Id))
                    _defeatedEnemyIds.Add(enemy.Def.Id);

            // 段末(Boss 层)同样发战利品(2026-07-20 拍板),取完才结算 → 见 ProceedAfterReward
            // 捕获携带状态:出过的字已消耗不回归(v0.7),池与 HP 延续
            _carriedLibrary = new List<string>(Battle.Library);
            _carriedPool = new List<string>(Battle.Pool);
            _carriedHp = Battle.PlayerHp;
            _carriedNormalShield = Battle.ShieldNormal;
            _carriedPersistShield = Battle.ShieldPersist;
            _carriedSummons = CaptureAliveSummons();
            // 只取减伤:HoT 是本场限定,不随携带态跨战斗(2026-08-04)
            _carriedStatuses = Battle.PlayerStatuses.All
                .Where(s => s.Kind == StatusKind.DamageReduction)
                .Select(s => s.Clone())
                .ToList();

            RollRewardOptions();
            CharPicksLeft = RewardPicks;
            Phase = RunPhase.Reward;
        }

        /// <summary>取一张字奖励(下标)。额度用尽或字库已满返回 false——
        /// 满库时用替换(PickRewardReplacing)或跳过(3.8.1)。字额度取尽自动开拔。</summary>
        public bool PickReward(int index)
        {
            if (Phase != RunPhase.Reward || CharPicksLeft == 0) return false;
            if (_carriedLibrary.Count >= _battleConfig.LibraryCapacity)
                return false;
            _carriedLibrary.Add(_rewardOptions[index]);
            _rewardOptions.RemoveAt(index);
            CharPicksLeft -= 1;
            MaybeFinishRewards();
            return true;
        }

        /// <summary>满库替换:奖励字换掉字库中一张(被换的字永久移除,3.8.1)。</summary>
        public bool PickRewardReplacing(int index, int replaceIndex)
        {
            if (Phase != RunPhase.Reward || CharPicksLeft == 0) return false;
            if (replaceIndex < 0 || replaceIndex >= _carriedLibrary.Count) return false;
            _carriedLibrary[replaceIndex] = _rewardOptions[index];
            _rewardOptions.RemoveAt(index);
            CharPicksLeft -= 1;
            MaybeFinishRewards();
            return true;
        }

        /// <summary>字额度取尽(或候选枯竭)自动开拔;中途可 SkipReward 提前走。</summary>
        private void MaybeFinishRewards()
        {
            if (CharPicksLeft == 0 || _rewardOptions.Count == 0)
                ProceedAfterReward();
        }

        /// <summary>提前开拔:放弃剩余战利品额度,进入奇遇或下一战。</summary>
        public void SkipReward()
        {
            if (Phase != RunPhase.Reward) return;
            ProceedAfterReward();
        }

        /// <summary>奖励结算后:段末直接通关(不再走奇遇),否则按概率触发奇遇(9.6)或下一战。</summary>
        private void ProceedAfterReward()
        {
            if (BattleIndex >= _runConfig.Encounters.Count - 1)
            {
                Phase = RunPhase.RunWon; // Boss 层战利品取完 → 交给外层结算并进安全层
                return;
            }
            if (_runConfig.EventPool.Count > 0 && _random.Next(100) < _runConfig.EventChancePercent)
            {
                CurrentEvent = _runConfig.EventPool[_random.Next(_runConfig.EventPool.Count)];
                Phase = RunPhase.Event;
                return;
            }
            BeginNextBattle();
        }

        /// <summary>字奖励的稀有度权重(2026-07-20 拍板):绿 80% / 蓝 15% / 紫 5%;
        /// 白(部件)与金橙红不参与。索引 = rarity − 1。</summary>
        private static readonly int[] RewardRarityWeights = { 0, 80, 15, 5, 0, 0, 0 };

        /// <summary>固定遍历顺序:保证同种子同结果(不依赖字典插入顺序)。必须按枚举数值升序。</summary>
        private static readonly CardRarity[] RarityOrder =
        {
            CardRarity.White, CardRarity.Green, CardRarity.Blue,
            CardRarity.Purple, CardRarity.Gold, CardRarity.Orange, CardRarity.Red,
        };

        private void RollRewardOptions()
        {
            _rewardOptions.Clear();

            // 候选按稀有度分组:部件不是奖励字(靠每回合掉落),重复项只留一份
            var byRarity = new Dictionary<CardRarity, List<string>>();
            foreach (var id in _runConfig.RewardPool)
            {
                if (!_graph.TryGet(id, out var def) || def.IsLeaf)
                    continue;
                if (!byRarity.TryGetValue(def.Rarity, out var group))
                    byRarity[def.Rarity] = group = new List<string>();
                if (!group.Contains(id))
                    group.Add(id);
            }

            for (int i = 0; i < RewardOptionCount; i++)
            {
                var pick = DrawWeightedReward(byRarity);
                if (pick == null) break; // 候选枯竭
                _rewardOptions.Add(pick);
            }
        }

        /// <summary>按稀有度权重抽一个并从候选中移除;权重全零(池里只有白/橙红)则均匀兜底。</summary>
        private string DrawWeightedReward(Dictionary<CardRarity, List<string>> byRarity)
        {
            int total = 0;
            foreach (var rarity in RarityOrder)
                if (byRarity.TryGetValue(rarity, out var group) && group.Count > 0)
                    total += RewardRarityWeights[(int)rarity - 1];

            if (total <= 0)
            {
                var all = new List<string>();
                foreach (var rarity in RarityOrder)
                    if (byRarity.TryGetValue(rarity, out var group))
                        all.AddRange(group);
                if (all.Count == 0) return null;
                var fallback = all[_random.Next(all.Count)];
                foreach (var group in byRarity.Values) group.Remove(fallback);
                return fallback;
            }

            int roll = _random.Next(total);
            foreach (var rarity in RarityOrder)
            {
                if (!byRarity.TryGetValue(rarity, out var group) || group.Count == 0)
                    continue;
                roll -= RewardRarityWeights[(int)rarity - 1];
                if (roll < 0)
                {
                    int index = _random.Next(group.Count);
                    var pick = group[index];
                    group.RemoveAt(index);
                    return pick;
                }
            }
            return null;
        }

        private void BeginNextBattle()
        {
            BattleIndex += 1;
            _carriedNormalShield += _perFloorNormalShield; // 金汤每关补盾,叠加上关剩余
            Battle = NewBattle(_carriedLibrary, _carriedPool, _carriedHp);
            Phase = RunPhase.InBattle;
        }

        /// <summary>存活召唤物的携带态:死尸丢弃(槽位释放,下一场从 0 号重排),残血原样带走。</summary>
        private List<SummonSnapshot> CaptureAliveSummons()
        {
            var alive = new List<SummonSnapshot>();
            foreach (var summon in Battle.Summons)
                if (summon.Alive) alive.Add(summon.Capture());
            return alive;
        }

        /// <summary>本关生效的血量上限 = 局外基础 + 奇遇累加的局内加成(至少 1)。</summary>
        public int EffectiveMaxHp => Math.Max(1, _battleConfig.PlayerMaxHp + _maxHpBonus);

        /// <summary>把局内上限加成折进配置再交给战斗;无加成时原样返回,不白拷一份。</summary>
        private BattleConfig BattleConfigForRun() =>
            _maxHpBonus == 0 ? _battleConfig : _battleConfig.WithPlayerMaxHp(EffectiveMaxHp);

        private BattleEngine NewBattle(IReadOnlyList<string> library, IReadOnlyList<string> pool, int? startingHp)
        {
            return new BattleEngine(_graph, BattleConfigForRun(), library, pool,
                _runConfig.Encounters[BattleIndex], _random.Next(int.MaxValue), startingHp, _cardLevels,
                _carriedNormalShield, _carriedPersistShield, _carriedSummons, _carriedStatuses);
        }
    }
}
