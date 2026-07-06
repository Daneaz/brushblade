using System;
using System.Collections.Generic;
using System.Linq;

namespace Brushblade.Core
{
    public enum BattlePhase
    {
        PlayerTurn,
        Won,
        Lost,
    }

    public enum BattleError
    {
        None,
        BattleOver,
        NotEnoughAp,
        NotCastable,   // 字不在字库(且不是池中可直出的部件)
        InvalidTarget,
        ForgeFailed,   // 拆/合被拆合引擎拒绝(细节见 LastForgeError)
    }

    /// <summary>战斗规则参数(基准值来自第 10 章 10.1)。</summary>
    public sealed class BattleConfig
    {
        public int PlayerMaxHp { get; set; } = 50;
        public int ApPerTurn { get; set; } = 3;
        public int LibraryCapacity { get; set; } = 6;  // 2026-07-06 拍板;局内广告可 +2
        public int PoolCapacity { get; set; } = 10;    // 同上
        public int DropsPerTurn { get; set; } = 2;
        /// <summary>回合开始掉落的部件抽取池(属性权重 = 表内重复度;待设计项)。</summary>
        public IReadOnlyList<string> DropTable { get; set; } = Array.Empty<string>();
    }

    /// <summary>结算事件(供表现层做打击感,13.3;架构:表现监听 Core 事件,不反向驱动)。</summary>
    public enum BattleEventKind
    {
        Damage,      // 我方对敌伤害(TargetIndex = 敌人下标)
        Burn,        // 施加灼烧层数
        Shield,      // 获得护盾(TargetIndex = −1 玩家)
        BurnTick,    // 回合末灼烧结算伤害
        EnemyDied,   // 敌人被消灭
        EnemyAttack, // 敌方对玩家伤害(Amount = 总伤,含被护盾吸收部分)
        EnemySplit,  // 叠字怪分裂(TargetIndex = 原体下标)
        BossPhase,   // 成语 Boss 进入新阶段(Amount = 新阶段下标)
    }

    public readonly struct BattleEvent
    {
        public BattleEventKind Kind { get; }
        public int TargetIndex { get; }  // 敌人下标;玩家侧为 −1
        public int Amount { get; }

        public BattleEvent(BattleEventKind kind, int targetIndex, int amount)
        {
            Kind = kind;
            TargetIndex = targetIndex;
            Amount = amount;
        }
    }

    /// <summary>战斗状态机(第 3 章 3.5 回合流程 / 3.7 结算顺序)。</summary>
    public sealed class BattleEngine
    {
        private readonly RecipeGraph _graph;
        private readonly BattleConfig _config;
        private readonly GameRandom _random;
        private readonly List<string> _usedChars = new();
        private readonly List<EnemyState> _enemies = new();

        private ForgeState _forge;
        private readonly IReadOnlyDictionary<string, int> _cardLevels; // 局外卡等级(19.3.2;null = 全 1 级)
        private int _burnPerStack = 2;      // 灼烧每层结算伤害(10.2;炽 +1,可叠加)
        private int _shieldNormal;          // 回合末全清的护盾
        private int _shieldPersist;         // 豁免一次全清的护盾(堡)

        public BattleEngine(RecipeGraph graph, BattleConfig config,
            IReadOnlyList<string> startingLibrary, IReadOnlyList<string> startingPool,
            IReadOnlyList<EnemyDef> enemies, int seed, int? startingHp = null,
            IReadOnlyDictionary<string, int> cardLevels = null)
        {
            _graph = graph;
            _config = config;
            _cardLevels = cardLevels;
            _random = new GameRandom(seed);
            _forge = new ForgeState(new List<string>(startingLibrary), new List<string>(startingPool));
            foreach (var def in enemies)
                _enemies.Add(new EnemyState(def));

            PlayerHp = startingHp ?? config.PlayerMaxHp;
            Phase = BattlePhase.PlayerTurn;
            StartTurn();
        }

        public BattlePhase Phase { get; private set; }
        public int Turn { get; private set; }
        public int Ap { get; private set; }
        public int PlayerHp { get; private set; }
        public int PlayerShield => _shieldNormal + _shieldPersist;
        public IReadOnlyList<string> Library => _forge.Library;
        public IReadOnlyList<string> Pool => _forge.Pool;
        public int LibraryCapacity => _config.LibraryCapacity;
        public int PoolCapacity => _config.PoolCapacity;
        public IReadOnlyList<string> UsedChars => _usedChars;
        public IReadOnlyList<EnemyState> Enemies => _enemies;
        public ForgeError LastForgeError { get; private set; }

        private readonly List<BattleEvent> _events = new();

        /// <summary>最近一次动作(Cast/EndTurn)产生的结算事件,动作开始时清空。</summary>
        public IReadOnlyList<BattleEvent> LastEvents => _events;

        /// <summary>拆(1 AP)。</summary>
        public BattleError Dismantle(string charId)
        {
            if (Phase != BattlePhase.PlayerTurn) return BattleError.BattleOver;
            if (Ap < 1) return BattleError.NotEnoughAp;

            var result = ForgeEngine.TryDismantle(charId, _graph, _forge, _config.PoolCapacity);
            if (!result.Success)
            {
                LastForgeError = result.Error;
                return BattleError.ForgeFailed;
            }
            _forge = result.State;
            Ap -= 1;
            return BattleError.None;
        }

        /// <summary>合(1 AP)。</summary>
        public BattleError Compose(string charId)
        {
            if (Phase != BattlePhase.PlayerTurn) return BattleError.BattleOver;
            if (Ap < 1) return BattleError.NotEnoughAp;

            var result = ForgeEngine.TryCompose(charId, _graph, _forge, _config.LibraryCapacity);
            if (!result.Success)
            {
                LastForgeError = result.Error;
                return BattleError.ForgeFailed;
            }
            _forge = result.State;
            Ap -= 1;
            return BattleError.None;
        }

        /// <summary>出字(ApCost):字库中的字,或池中可直出的部件(4.5 第二层,防卡手地板)。</summary>
        public BattleError Cast(string charId, int targetIndex = -1)
        {
            if (Phase != BattlePhase.PlayerTurn) return BattleError.BattleOver;
            if (!_graph.TryGet(charId, out var def)) return BattleError.NotCastable;

            bool fromLibrary = _forge.Library.Contains(charId);
            bool fromPool = !fromLibrary && def.IsLeaf && _forge.Pool.Contains(charId);
            if (!fromLibrary && !fromPool) return BattleError.NotCastable;
            if (Ap < def.ApCost) return BattleError.NotEnoughAp;

            // 单体效果需要有效的存活目标
            if (NeedsTarget(def) &&
                (targetIndex < 0 || targetIndex >= _enemies.Count || !_enemies[targetIndex].Alive))
                return BattleError.InvalidTarget;

            _events.Clear();
            Ap -= def.ApCost;

            // 出字后移出可用区:字进"已使用",部件从池中消耗(3.8.1)
            if (fromLibrary)
            {
                var library = new List<string>(_forge.Library);
                library.Remove(charId);
                _forge = new ForgeState(library, _forge.Pool);
                _usedChars.Add(charId);
            }
            else
            {
                var pool = new List<string>(_forge.Pool);
                pool.Remove(charId);
                _forge = new ForgeState(_forge.Library, pool);
            }

            ApplyEffects(def, targetIndex);
            CheckWin();
            return BattleError.None;
        }

        /// <summary>丢弃(3.8.2 防卡手):从字库或部件池移除,免 AP;字库丢弃本关不回归。</summary>
        public BattleError Discard(string charId)
        {
            if (Phase != BattlePhase.PlayerTurn) return BattleError.BattleOver;

            if (_forge.Library.Contains(charId))
            {
                var library = new List<string>(_forge.Library);
                library.Remove(charId);
                _forge = new ForgeState(library, _forge.Pool);
                return BattleError.None;
            }
            if (_forge.Pool.Contains(charId))
            {
                var pool = new List<string>(_forge.Pool);
                pool.Remove(charId);
                _forge = new ForgeState(_forge.Library, pool);
                return BattleError.None;
            }
            return BattleError.NotCastable;
        }

        /// <summary>兜底一击(4.5 第二层防卡手地板):无效果的部件/字出手时的弱效果,永不 brick。</summary>
        private static readonly EffectDef[] FallbackEffects = { new(EffectKind.DamageSingle, 3) };

        /// <summary>该字的实际出字效果:无效果者用兜底一击。</summary>
        private static IReadOnlyList<EffectDef> EffectsOf(CharDef def) =>
            def.Effects.Count > 0 ? def.Effects : FallbackEffects;

        /// <summary>该字的效果是否需要指定单体目标(供 UI 进入选目标模式)。</summary>
        public static bool NeedsTarget(CharDef def)
        {
            foreach (var effect in EffectsOf(def))
                if (effect.Kind == EffectKind.DamageSingle || effect.Kind == EffectKind.BurnSingle)
                    return true;
            return false;
        }

        /// <summary>结束回合:灼烧结算 → 胜负检查 → 敌人行动 → 胜负检查 → 下回合开始(3.5/3.7)。</summary>
        public void EndTurn()
        {
            if (Phase != BattlePhase.PlayerTurn) return;
            _events.Clear();

            // 3.7 结算顺序第 1 条:灼烧(X 层 → X×系数 伤害,然后 −1 层;系数基础 2,炽可加,10.2)
            for (int i = 0; i < _enemies.Count; i++)
            {
                var enemy = _enemies[i];
                if (!enemy.Alive || enemy.Burn <= 0) continue;
                int tick = enemy.Burn * _burnPerStack;
                enemy.Hp = Math.Max(0, enemy.Hp - tick);
                enemy.Burn -= 1;
                _events.Add(new BattleEvent(BattleEventKind.BurnTick, i, tick));
                if (!enemy.Alive)
                    ResolveDefeat(i);
            }
            CheckWin();
            if (Phase != BattlePhase.PlayerTurn) return;

            // 敌人行动:护盾先吸收(普通桶先扣,豁免桶垫后);行动后结算自身能力
            foreach (var enemy in _enemies)
            {
                if (!enemy.Alive) continue;
                int damage = enemy.Attack;
                int fromNormal = Math.Min(_shieldNormal, damage);
                _shieldNormal -= fromNormal;
                int fromPersist = Math.Min(_shieldPersist, damage - fromNormal);
                _shieldPersist -= fromPersist;
                PlayerHp = Math.Max(0, PlayerHp - (damage - fromNormal - fromPersist));
                _events.Add(new BattleEvent(BattleEventKind.EnemyAttack, -1, damage));

                // 缺笔妖:每回合自补全,第 3 次补全完成(8.3)
                if (enemy.Def.Ability == EnemyAbility.Regrow && enemy.RegrowProgress < 3)
                {
                    enemy.RegrowProgress += 1;
                    enemy.Attack += 2;
                    enemy.Hp = Math.Min(enemy.Def.MaxHp, enemy.Hp + 3);
                    if (enemy.RegrowProgress == 3)
                    {
                        enemy.Attack *= 2;
                        enemy.Hp = enemy.Def.MaxHp;
                    }
                }
            }
            if (PlayerHp <= 0)
            {
                Phase = BattlePhase.Lost;
                return;
            }

            // 护盾全清:清算点在敌方行动结束后(10.2);豁免桶挺过本次,降级为普通桶
            _shieldNormal = _shieldPersist;
            _shieldPersist = 0;

            StartTurn();
        }

        private void StartTurn()
        {
            Turn += 1;
            Ap = _config.ApPerTurn;

            // 部件掉落:+N 随机部件,池满则不掉(第 3 章 3.5 / v0.4)
            if (_config.DropTable.Count > 0)
            {
                var pool = new List<string>(_forge.Pool);
                for (int i = 0; i < _config.DropsPerTurn && pool.Count < _config.PoolCapacity; i++)
                    pool.Add(_random.Pick(_config.DropTable));
                _forge = new ForgeState(_forge.Library, pool);
            }
        }

        private void ApplyEffects(CharDef def, int targetIndex)
        {
            var recipeElements = _graph.RecipeElements(def.Id);
            var attacker = def.Element ?? Element.Heart; // 中性字视作心(全 1.0x)
            int cardLevel = _cardLevels != null && _cardLevels.TryGetValue(def.Id, out var level) ? level : 1;

            foreach (var effect in EffectsOf(def))
            {
                int value = MetaRules.ScaleByCardLevel(effect.Value, cardLevel); // 19.3.2:等级先作用于基础值
                switch (effect.Kind)
                {
                    case EffectKind.DamageSingle:
                        DamageEnemy(targetIndex, BaseValue(effect, value, _enemies[targetIndex]), recipeElements, attacker);
                        break;
                    case EffectKind.DamageAll:
                        int aoeCount = _enemies.Count; // 分裂产生的新怪不吃同一发 AOE
                        for (int i = 0; i < aoeCount; i++)
                            if (_enemies[i].Alive)
                                DamageEnemy(i, BaseValue(effect, value, _enemies[i]), recipeElements, attacker);
                        break;
                    case EffectKind.BurnSingle:
                        if (_enemies[targetIndex].Alive)
                        {
                            _enemies[targetIndex].Burn += value;
                            _events.Add(new BattleEvent(BattleEventKind.Burn, targetIndex, value));
                        }
                        break;
                    case EffectKind.BurnAll:
                        for (int i = 0; i < _enemies.Count; i++)
                            if (_enemies[i].Alive)
                            {
                                _enemies[i].Burn += value;
                                _events.Add(new BattleEvent(BattleEventKind.Burn, i, value));
                            }
                        break;
                    case EffectKind.Shield:
                        int shield = WuxingResolver.ResolveEffect(value, recipeElements);
                        if (effect.PersistOnce) _shieldPersist += shield;
                        else _shieldNormal += shield;
                        _events.Add(new BattleEvent(BattleEventKind.Shield, -1, shield));
                        break;
                    case EffectKind.BurnPotency:
                        _burnPerStack += value;
                        break;
                }
            }
        }

        /// <summary>条件基础值:灼类效果对带灼烧目标翻倍(10.3.1),再进生克结算。</summary>
        private static int BaseValue(EffectDef effect, int scaledValue, EnemyState target)
        {
            return effect.DoubleVsBurning && target.Burn > 0 ? scaledValue * 2 : scaledValue;
        }

        private void DamageEnemy(int enemyIndex, int baseValue,
            IReadOnlyCollection<Element> recipeElements, Element attacker)
        {
            var enemy = _enemies[enemyIndex];
            int damage = WuxingResolver.ResolveEffect(baseValue, recipeElements, attacker, enemy.Element);
            if (enemy.DamageTaken != 1f)
                damage = (int)Math.Floor(damage * enemy.DamageTaken); // 「山」类承伤减免
            enemy.Hp = Math.Max(0, enemy.Hp - damage);
            _events.Add(new BattleEvent(BattleEventKind.Damage, enemyIndex, damage));
            if (!enemy.Alive)
            {
                ResolveDefeat(enemyIndex);
                return;
            }

            // 叠字怪:首次受击存活 → 分裂成两个半血(8.3;场上 <4 时)
            if (enemy.Def.Ability == EnemyAbility.Split && !enemy.HasSplit && _enemies.Count < 4)
            {
                int half = (enemy.Hp + 1) / 2;
                enemy.Hp = half;
                enemy.HasSplit = true;
                var clone = new EnemyState(enemy.Def)
                {
                    Hp = half,
                    Attack = enemy.Attack,
                    HasSplit = true,
                };
                _enemies.Add(clone);
                _events.Add(new BattleEvent(BattleEventKind.EnemySplit, enemyIndex, half));
            }
        }

        /// <summary>血量归零的结算:成语 Boss 还有下一阶段则换阶段(8.5,溢出伤害不带入),否则死亡。</summary>
        private void ResolveDefeat(int enemyIndex)
        {
            var enemy = _enemies[enemyIndex];
            if (enemy.IsBoss && enemy.PhaseIndex < enemy.Def.Phases.Count - 1)
            {
                enemy.EnterPhase(enemy.PhaseIndex + 1);
                _events.Add(new BattleEvent(BattleEventKind.BossPhase, enemyIndex, enemy.PhaseIndex));
                return;
            }
            _events.Add(new BattleEvent(BattleEventKind.EnemyDied, enemyIndex, 0));
        }

        private void CheckWin()
        {
            foreach (var enemy in _enemies)
                if (enemy.Alive)
                    return;
            Phase = BattlePhase.Won;
        }
    }
}
