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
        private const int RewardOptionCount = 5; // 战利品字候选数(普通战斗 5 选 1,2026-07-19 拍板)
        private const int RewardPicks = 1;       // 字/部件各自的可取数(Boss 层奖励走宝箱,不经此)

        /// <summary>部件奖励固定候选:五行基础部件(5 选 1)。</summary>
        public static readonly IReadOnlyList<string> ComponentRewardChoices =
            new[] { "金", "木", "水", "火", "土" };

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

        /// <summary>奖励阶段的字候选(已取走的即时移除)。</summary>
        public IReadOnlyList<string> RewardOptions => _rewardOptions;

        /// <summary>奖励阶段的部件候选(固定五行,已取走的即时移除)。</summary>
        public IReadOnlyList<string> ComponentOptions => _componentOptions;

        private readonly List<string> _componentOptions = new();

        public int CharPicksLeft { get; private set; }
        public int ComponentPicksLeft { get; private set; }

        /// <summary>战斗间携带的字库(Reward/Event/RunWon 阶段有效;段末快照的数据源)。</summary>
        public IReadOnlyList<string> CarriedLibrary => _carriedLibrary;

        /// <summary>战斗间携带的部件池(同上)。</summary>
        public IReadOnlyList<string> CarriedPool => _carriedPool;

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
            int charChoiceIndex = -1)
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
            if (gainChar != null && _carriedLibrary.Count >= _battleConfig.LibraryCapacity)
                return false; // 字库已满,收不下(3.8.1「选择不要」;先验后扣,部件不受损)

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
                _carriedLibrary.Add(gainChar);
            foreach (var component in option.GainComponents)
                if (_carriedPool.Count < _battleConfig.PoolCapacity)
                    _carriedPool.Add(component); // 池满则不入(同 3.8.2「池满则不掉」)
            for (int i = 0; i < option.RandomComponents; i++)
                if (_carriedPool.Count < _battleConfig.PoolCapacity)
                    _carriedPool.Add(ComponentRewardChoices[_random.Next(ComponentRewardChoices.Count)]);
            bool inkWon = option.InkChancePercent <= 0 || _random.Next(100) < option.InkChancePercent;
            EarnedInk += (inkWon ? option.Ink : 0) - option.InkCost;
            if (option.HpDelta > 0)
                _carriedHp = Math.Min(_battleConfig.PlayerMaxHp, _carriedHp + option.HpDelta);
            else if (option.HpDelta < 0)
                _carriedHp = Math.Max(1, _carriedHp + option.HpDelta);

            CurrentEvent = null;
            BeginNextBattle();
            return true;
        }

        /// <summary>局内广告扩容:字库 +2,一局一次,跨场有效(2026-07-06 拍板)。
        /// 无尽塔 = 整次登塔一次:跨段由外层快照恢复,塔结算随快照清除。</summary>
        public bool TryExpandLibrary()
        {
            if (LibraryExpanded) return false;
            _battleConfig.LibraryCapacity += 2;
            LibraryExpanded = true;
            return true;
        }

        /// <summary>局内广告扩容:部件池 +2,一局一次(同上)。</summary>
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

            // 段末(Boss 层)同样发战利品(2026-07-20 拍板),取完才结算 → 见 ProceedAfterReward
            // 捕获携带状态:出过的字已消耗不回归(v0.7),池与 HP 延续
            _carriedLibrary = new List<string>(Battle.Library);
            _carriedPool = new List<string>(Battle.Pool);
            _carriedHp = Battle.PlayerHp;

            RollRewardOptions();
            _componentOptions.Clear();
            _componentOptions.AddRange(ComponentRewardChoices);
            CharPicksLeft = RewardPicks;
            ComponentPicksLeft = RewardPicks;
            Phase = RunPhase.Reward;
        }

        /// <summary>取一张字奖励(下标)。额度用尽或字库已满返回 false——
        /// 满库时用替换(PickRewardReplacing)或跳过(3.8.1)。双排额度取尽自动开拔。</summary>
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

        /// <summary>取一个部件奖励(下标):入携带池。额度用尽或池满返回 false——
        /// 满池时用替换(PickRewardComponentReplacing)或跳过。</summary>
        public bool PickRewardComponent(int index)
        {
            if (Phase != RunPhase.Reward || ComponentPicksLeft == 0) return false;
            if (_carriedPool.Count >= _battleConfig.PoolCapacity)
                return false;
            _carriedPool.Add(_componentOptions[index]);
            _componentOptions.RemoveAt(index);
            ComponentPicksLeft -= 1;
            MaybeFinishRewards();
            return true;
        }

        /// <summary>满池替换:奖励部件换掉池中一个(被换的永久移除,2026-07-20)。</summary>
        public bool PickRewardComponentReplacing(int index, int replaceIndex)
        {
            if (Phase != RunPhase.Reward || ComponentPicksLeft == 0) return false;
            if (replaceIndex < 0 || replaceIndex >= _carriedPool.Count) return false;
            _carriedPool[replaceIndex] = _componentOptions[index];
            _componentOptions.RemoveAt(index);
            ComponentPicksLeft -= 1;
            MaybeFinishRewards();
            return true;
        }

        /// <summary>双排额度取尽(或候选枯竭)自动开拔;中途可 SkipReward 提前走。</summary>
        private void MaybeFinishRewards()
        {
            bool charsDone = CharPicksLeft == 0 || _rewardOptions.Count == 0;
            bool componentsDone = ComponentPicksLeft == 0 || _componentOptions.Count == 0;
            if (charsDone && componentsDone)
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
        /// 白(部件)与橙红不参与。索引 = rarity − 1。</summary>
        private static readonly int[] RewardRarityWeights = { 0, 80, 15, 5, 0, 0 };

        /// <summary>固定遍历顺序:保证同种子同结果(不依赖字典插入顺序)。</summary>
        private static readonly CardRarity[] RarityOrder =
        {
            CardRarity.White, CardRarity.Green, CardRarity.Blue,
            CardRarity.Purple, CardRarity.Orange, CardRarity.Red,
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
