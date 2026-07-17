using System;
using System.Collections.Generic;

namespace Brushblade.Core
{
    public enum RunPhase
    {
        InBattle,
        Reward,   // 战斗胜利,三选一奖励(9.5)
        Event,    // 奇遇:短情境 + 选择(9.6)
        RunWon,
        RunLost,
    }

    /// <summary>一段连战的配置(阶段 1 验证格式:3~5 场,17.2)。</summary>
    public sealed class RunConfig
    {
        /// <summary>每场遭遇的敌人列表。</summary>
        public IReadOnlyList<IReadOnlyList<EnemyDef>> Encounters { get; set; }

        /// <summary>战后三选一的奖励字池。</summary>
        public IReadOnlyList<string> RewardPool { get; set; }

        /// <summary>奇遇事件池(9.6);空则无奇遇。</summary>
        public IReadOnlyList<EventDef> EventPool { get; set; } = System.Array.Empty<EventDef>();

        /// <summary>两场战斗之间触发奇遇的概率(百分比,0~100)。</summary>
        public int EventChancePercent { get; set; }
    }

    /// <summary>连战状态机:战斗 → 奖励 → 下一战。
    /// 跨战斗规则:HP 保留(第 9 章)、部件池保留(3.8.2)、出过的字战后回归字库(3.8.1)。</summary>
    public sealed class RunEngine
    {
        private const int RewardChoices = 3;

        private readonly RecipeGraph _graph;
        private readonly RunConfig _runConfig;
        private readonly BattleConfig _battleConfig;
        private readonly GameRandom _random;
        private readonly IReadOnlyDictionary<string, int> _cardLevels;
        private readonly List<string> _rewardOptions = new();

        // 战斗之间的携带状态(奖励与奇遇的作用对象)
        private List<string> _carriedLibrary;
        private List<string> _carriedPool;
        private int _carriedHp;

        public RunEngine(RecipeGraph graph, RunConfig runConfig, BattleConfig battleConfig,
            IReadOnlyList<string> startingLibrary, IReadOnlyList<string> startingPool, int seed,
            IReadOnlyDictionary<string, int> cardLevels = null, int startingInk = 0,
            int? startingHp = null)
        {
            _startingInk = startingInk;
            _graph = graph;
            _runConfig = runConfig;
            _battleConfig = battleConfig;
            _cardLevels = cardLevels;
            _random = new GameRandom(seed);
            Phase = RunPhase.InBattle;
            BattleIndex = 0;
            Battle = NewBattle(startingLibrary, startingPool, startingHp); // 断点续爬恢复血量(20.6)
        }

        public RunPhase Phase { get; private set; }
        public int BattleIndex { get; private set; }
        public BattleEngine Battle { get; private set; }

        /// <summary>奖励阶段的三选一选项(字 id)。</summary>
        public IReadOnlyList<string> RewardOptions => _rewardOptions;

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
        /// 需要消费(InkCost)且余额不足时返回 false,停留在事件中。</summary>
        public bool ChooseEventOption(int index)
        {
            if (Phase != RunPhase.Event) return false;
            var option = CurrentEvent.Options[index];
            if (option.InkCost > AvailableInk)
                return false; // 买不起,换个选项

            if (option.GainChar != null)
                _carriedLibrary.Add(option.GainChar);
            _carriedPool.AddRange(option.GainComponents);
            EarnedInk += option.Ink - option.InkCost;
            if (option.HpDelta > 0)
                _carriedHp = Math.Min(_battleConfig.PlayerMaxHp, _carriedHp + option.HpDelta);
            else if (option.HpDelta < 0)
                _carriedHp = Math.Max(1, _carriedHp + option.HpDelta);

            CurrentEvent = null;
            BeginNextBattle();
            return true;
        }

        /// <summary>局内广告扩容:字库 +2,每关一次,关内跨场有效(2026-07-06 拍板)。
        /// 关卡结束自然恢复:每关的 BattleConfig 由外层新建。</summary>
        public bool TryExpandLibrary()
        {
            if (LibraryExpanded) return false;
            _battleConfig.LibraryCapacity += 2;
            LibraryExpanded = true;
            return true;
        }

        /// <summary>局内广告扩容:部件池 +2,每关一次。</summary>
        public bool TryExpandPool()
        {
            if (PoolExpanded) return false;
            _battleConfig.PoolCapacity += 2;
            PoolExpanded = true;
            return true;
        }

        /// <summary>战斗分出胜负后由视图调用:胜 → 奖励/通关,负 → 结算 run。</summary>
        public void AdvanceAfterBattle()
        {
            if (Phase != RunPhase.InBattle) return;

            if (Battle.Phase == BattlePhase.Lost)
            {
                Phase = RunPhase.RunLost;
                return;
            }
            if (Battle.Phase != BattlePhase.Won) return;

            if (BattleIndex >= _runConfig.Encounters.Count - 1)
            {
                Phase = RunPhase.RunWon;
                return;
            }

            // 捕获携带状态:出过的字回归字库(3.8.1),池与 HP 延续
            _carriedLibrary = new List<string>(Battle.Library);
            _carriedLibrary.AddRange(Battle.UsedChars);
            _carriedPool = new List<string>(Battle.Pool);
            _carriedHp = Battle.PlayerHp;

            RollRewardOptions();
            Phase = RunPhase.Reward;
        }

        /// <summary>选取奖励(下标),进入奇遇或下一战。</summary>
        public void PickReward(int index)
        {
            if (Phase != RunPhase.Reward) return;
            _carriedLibrary.Add(_rewardOptions[index]);
            ProceedAfterReward();
        }

        /// <summary>放弃奖励,进入奇遇或下一战。</summary>
        public void SkipReward()
        {
            if (Phase != RunPhase.Reward) return;
            ProceedAfterReward();
        }

        /// <summary>奖励结算后:按概率触发奇遇(9.6),否则直接下一战。</summary>
        private void ProceedAfterReward()
        {
            if (_runConfig.EventPool.Count > 0 && _random.Next(100) < _runConfig.EventChancePercent)
            {
                CurrentEvent = _runConfig.EventPool[_random.Next(_runConfig.EventPool.Count)];
                Phase = RunPhase.Event;
                return;
            }
            BeginNextBattle();
        }

        private void RollRewardOptions()
        {
            _rewardOptions.Clear();
            var pool = new List<string>(_runConfig.RewardPool);
            for (int i = 0; i < RewardChoices && pool.Count > 0; i++)
            {
                int pick = _random.Next(pool.Count);
                _rewardOptions.Add(pool[pick]);
                pool.RemoveAt(pick);
            }
        }

        private void BeginNextBattle()
        {
            BattleIndex += 1;
            Battle = NewBattle(_carriedLibrary, _carriedPool, _carriedHp);
            Phase = RunPhase.InBattle;
        }

        private BattleEngine NewBattle(IReadOnlyList<string> library, IReadOnlyList<string> pool, int? startingHp)
        {
            return new BattleEngine(_graph, _battleConfig, library, pool,
                _runConfig.Encounters[BattleIndex], _random.Next(int.MaxValue), startingHp, _cardLevels);
        }
    }
}
