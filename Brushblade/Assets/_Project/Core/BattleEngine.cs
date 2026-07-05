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

            // 3.7 结算顺序第 1 条:灼烧(X 层 → X×系数 伤害,然后 −1 层;系数基础 2,炽可加,10.2)
            foreach (var enemy in _enemies)
            {
                if (!enemy.Alive || enemy.Burn <= 0) continue;
                enemy.Hp = Math.Max(0, enemy.Hp - enemy.Burn * _burnPerStack);
                enemy.Burn -= 1;
            }
            CheckWin();
            if (Phase != BattlePhase.PlayerTurn) return;

            // 敌人行动:护盾先吸收(普通桶先扣,豁免桶垫后)
            foreach (var enemy in _enemies)
            {
                if (!enemy.Alive) continue;
                int damage = enemy.Def.Attack;
                int fromNormal = Math.Min(_shieldNormal, damage);
                _shieldNormal -= fromNormal;
                int fromPersist = Math.Min(_shieldPersist, damage - fromNormal);
                _shieldPersist -= fromPersist;
                PlayerHp = Math.Max(0, PlayerHp - (damage - fromNormal - fromPersist));
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
                        DamageEnemy(_enemies[targetIndex], BaseValue(effect, value, _enemies[targetIndex]), recipeElements, attacker);
                        break;
                    case EffectKind.DamageAll:
                        foreach (var enemy in _enemies)
                            if (enemy.Alive)
                                DamageEnemy(enemy, BaseValue(effect, value, enemy), recipeElements, attacker);
                        break;
                    case EffectKind.BurnSingle:
                        if (_enemies[targetIndex].Alive)
                            _enemies[targetIndex].Burn += value;
                        break;
                    case EffectKind.BurnAll:
                        foreach (var enemy in _enemies)
                            if (enemy.Alive)
                                enemy.Burn += value;
                        break;
                    case EffectKind.Shield:
                        int shield = WuxingResolver.ResolveEffect(value, recipeElements);
                        if (effect.PersistOnce) _shieldPersist += shield;
                        else _shieldNormal += shield;
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

        private void DamageEnemy(EnemyState enemy, int baseValue,
            IReadOnlyCollection<Element> recipeElements, Element attacker)
        {
            int damage = WuxingResolver.ResolveEffect(baseValue, recipeElements, attacker, enemy.Def.Element);
            enemy.Hp = Math.Max(0, enemy.Hp - damage);
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
