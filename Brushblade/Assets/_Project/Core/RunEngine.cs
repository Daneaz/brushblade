using System;
using System.Collections.Generic;

namespace Brushblade.Core
{
    public enum RunPhase
    {
        InBattle,
        Reward,   // 战斗胜利,三选一奖励(9.5)
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

        public RunEngine(RecipeGraph graph, RunConfig runConfig, BattleConfig battleConfig,
            IReadOnlyList<string> startingLibrary, IReadOnlyList<string> startingPool, int seed,
            IReadOnlyDictionary<string, int> cardLevels = null)
        {
            _graph = graph;
            _runConfig = runConfig;
            _battleConfig = battleConfig;
            _cardLevels = cardLevels;
            _random = new GameRandom(seed);
            Phase = RunPhase.InBattle;
            BattleIndex = 0;
            Battle = NewBattle(startingLibrary, startingPool, startingHp: null);
        }

        public RunPhase Phase { get; private set; }
        public int BattleIndex { get; private set; }
        public BattleEngine Battle { get; private set; }

        /// <summary>奖励阶段的三选一选项(字 id)。</summary>
        public IReadOnlyList<string> RewardOptions => _rewardOptions;

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

            RollRewardOptions();
            Phase = RunPhase.Reward;
        }

        /// <summary>选取奖励(下标),进入下一战。</summary>
        public void PickReward(int index)
        {
            if (Phase != RunPhase.Reward) return;
            StartNextBattle(_rewardOptions[index]);
        }

        /// <summary>放弃奖励,直接进入下一战。</summary>
        public void SkipReward()
        {
            if (Phase != RunPhase.Reward) return;
            StartNextBattle(null);
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

        private void StartNextBattle(string rewardChar)
        {
            // 出过的字回归字库(3.8.1)+ 奖励入库;部件池与 HP 跨战斗保留
            var library = new List<string>(Battle.Library);
            library.AddRange(Battle.UsedChars);
            if (rewardChar != null) library.Add(rewardChar);

            BattleIndex += 1;
            Battle = NewBattle(library, Battle.Pool, Battle.PlayerHp);
            Phase = RunPhase.InBattle;
        }

        private BattleEngine NewBattle(IReadOnlyList<string> library, IReadOnlyList<string> pool, int? startingHp)
        {
            return new BattleEngine(_graph, _battleConfig, library, pool,
                _runConfig.Encounters[BattleIndex], _random.Next(int.MaxValue), startingHp, _cardLevels);
        }
    }
}
