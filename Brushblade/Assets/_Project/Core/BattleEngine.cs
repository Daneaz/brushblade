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
        SummonCapFull, // 前排召唤已满(2026-07-25 强阻断):不吃 AP、不消耗字,由 UI 确认后带 replaceSummon 重出
    }

    /// <summary>战斗规则参数(基准值来自第 10 章 10.1)。</summary>
    public sealed class BattleConfig
    {
        public int PlayerMaxHp { get; set; } = 50;
        public int ApPerTurn { get; set; } = 3;
        public int LibraryCapacity { get; set; } = 6;  // 2026-07-06 拍板;局内广告可 +2
        public int PoolCapacity { get; set; } = 10;    // 同上
        public int DropsPerTurn { get; set; } = 2; // 3→2(2026-07-19 二次拍板)
        public int BossPhaseJitterPercent { get; set; } = 8; // Boss 换阶阈值浮动幅度(±总血%,2026-07-19)
        /// <summary>回合开始掉落的部件抽取池(属性权重 = 表内重复度;待设计项)。</summary>
        public IReadOnlyList<string> DropTable { get; set; } = Array.Empty<string>();

        /// <summary>可合成的字集合 = 玩家的出阵列表(2026-07-20 拍板:没编入出阵就合不出来,
        /// 与战利品同源);null = 不限(工装与旧调用)。</summary>
        public IReadOnlyCollection<string> UnlockedChars { get; set; }
    }

    /// <summary>结算事件(供表现层做打击感,13.3;架构:表现监听 Core 事件,不反向驱动)。</summary>
    public enum BattleEventKind
    {
        Damage,      // 我方对敌伤害(TargetIndex = 敌人下标)
        Burn,        // 施加灼烧层数
        Shield,      // 获得护盾(TargetIndex = −1 玩家)
        BurnTick,    // 回合末灼烧结算伤害
        EnemyDied,   // 敌人被消灭
        EnemyAttack, // 敌方对玩家伤害(Amount = 总伤,含被护盾吸收部分;TargetIndex = 攻击者敌人下标,驱动冲刺动效)
        EnemySplit,  // 叠字怪分裂(TargetIndex = 原体下标)
        BossPhase,   // 成语 Boss 进入新阶段(Amount = 新阶段下标)
        Heal,        // 治疗自身(Amount = 实际回复量,2026-07-19)
        Summon,      // 召唤前排单位(Amount = 血量;SecondIndex = 被顶替的槽位,新增则 −1)
        SummonHit,   // 召唤物替玩家承伤(Amount = 伤害;TargetIndex = 攻击者敌人下标,驱动冲刺动效)
        SummonAttack,     // 召唤物反击敌人(TargetIndex = 敌人下标;仅驱动动效,伤害走 Damage)
        SummonCapReached, // 召唤已达上限,本次被拦(仅提示,无实体)
        EnemyBuff,   // 被标点小妖加攻(TargetIndex = 被加成的敌人)
        EnemyRevealed, // 通假字现形/生僻字被读懂(TargetIndex = 该敌人)
    }

    public readonly struct BattleEvent
    {
        public BattleEventKind Kind { get; }
        public int TargetIndex { get; }  // 敌人下标;玩家侧为 −1
        public int Amount { get; }
        public int SecondIndex { get; }  // 关联召唤物下标(SummonAttack=发起者 / SummonHit=承伤者 / Summon=被顶替槽位;其余 −1)
        public int Absorbed { get; }     // EnemyAttack:Amount 中被护盾吃掉的部分(其余 = 实际掉血);别的事件 0

        public BattleEvent(BattleEventKind kind, int targetIndex, int amount, int secondIndex = -1, int absorbed = 0)
        {
            Kind = kind;
            TargetIndex = targetIndex;
            Amount = amount;
            SecondIndex = secondIndex;
            Absorbed = absorbed;
        }
    }

    /// <summary>战斗状态机(第 3 章 3.5 回合流程 / 3.7 结算顺序)。</summary>
    public sealed class BattleEngine
    {
        private readonly RecipeGraph _graph;
        private readonly BattleConfig _config;
        private readonly GameRandom _random;
        private readonly List<EnemyState> _enemies = new();
        private readonly List<SummonState> _summons = new();
        private const int SummonCap = 4; // 场上存活召唤物上限(2026-07-19)
        private const int ScorchGain = 2; // 焦痕受击存活的加攻量

        private ForgeState _forge;
        private readonly IReadOnlyDictionary<string, int> _cardLevels; // 局外卡等级(19.3.2;null = 全 1 级)
        private int _burnPerStack = 2;      // 灼烧每层结算伤害(10.2;炽 +1,可叠加)
        private int _shieldNormal;          // 段内持久的普通护盾(段末清,B 改动)
        private int _shieldPersist;         // 跨段保留的护盾(堡,B 改动)

        public BattleEngine(RecipeGraph graph, BattleConfig config,
            IReadOnlyList<string> startingLibrary, IReadOnlyList<string> startingPool,
            IReadOnlyList<EnemyDef> enemies, int seed, int? startingHp = null,
            IReadOnlyDictionary<string, int> cardLevels = null,
            int startingNormalShield = 0, int startingPersistShield = 0)
        {
            _graph = graph;
            _config = config;
            _cardLevels = cardLevels;
            _random = new GameRandom(seed);
            _forge = new ForgeState(new List<string>(startingLibrary), new List<string>(startingPool));
            foreach (var def in enemies)
                _enemies.Add(new EnemyState(def, config.BossPhaseJitterPercent, _random));

            PlayerHp = startingHp ?? config.PlayerMaxHp;
            _shieldNormal = startingNormalShield;
            _shieldPersist = startingPersistShield;
            Phase = BattlePhase.PlayerTurn;
            StartTurn();
        }

        public BattlePhase Phase { get; private set; }
        public int Turn { get; private set; }
        public int Ap { get; private set; }
        public int ApPerTurn => _config.ApPerTurn;   // 每回合 AP 上限(UI 满格数 / 提示文案用;一气技能会抬高)
        public int PlayerHp { get; private set; }
        public int PlayerShield => _shieldNormal + _shieldPersist;
        public int ShieldNormal => _shieldNormal;
        public int ShieldPersist => _shieldPersist;
        public IReadOnlyList<string> Library => _forge.Library;
        public IReadOnlyList<string> Pool => _forge.Pool;
        public int LibraryCapacity => _config.LibraryCapacity;
        public int PoolCapacity => _config.PoolCapacity;

        /// <summary>可合成字集(= 出阵列表);null = 不限。表现层的拆合台提示按此过滤。</summary>
        public IReadOnlyCollection<string> UnlockedChars => _config.UnlockedChars;
        public IReadOnlyList<EnemyState> Enemies => _enemies;
        public IReadOnlyList<SummonState> Summons => _summons;
        public int SummonCapacity => SummonCap;
        public int AliveSummonCount => AliveSummons();
        public ForgeError LastForgeError { get; private set; }

        private readonly List<BattleEvent> _events = new();

        /// <summary>最近一次动作(Cast/EndTurn)产生的结算事件,动作开始时清空。</summary>
        public IReadOnlyList<BattleEvent> LastEvents => _events;

        /// <summary>拆(1 AP)。</summary>
        public BattleError Dismantle(string charId)
        {
            if (Phase != BattlePhase.PlayerTurn) return BattleError.BattleOver;
            if (Ap < 1) return BattleError.NotEnoughAp;

            var result = ForgeEngine.TryDismantle(charId, _graph, _forge, _config.PoolCapacity, _config.LibraryCapacity);
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

            var result = ForgeEngine.TryCompose(charId, _graph, _forge, _config.LibraryCapacity,
                _config.UnlockedChars);
            if (!result.Success)
            {
                LastForgeError = result.Error;
                return BattleError.ForgeFailed;
            }
            _forge = result.State;
            Ap -= 1;
            return BattleError.None;
        }

        /// <summary>出字(ApCost):字库中的字,或池中可直出的部件(4.5 第二层,防卡手地板)。
        /// replaceSummon:前排满员时顶掉最前的召唤物入场(UI 弹窗确认后才置位),否则满员直接拒出。</summary>
        public BattleError Cast(string charId, int targetIndex = -1, bool replaceSummon = false)
        {
            if (Phase != BattlePhase.PlayerTurn) return BattleError.BattleOver;
            if (!_graph.TryGet(charId, out var def)) return BattleError.NotCastable;

            bool fromLibrary = _forge.Library.Contains(charId);
            bool fromPool = !fromLibrary && def.IsLeaf && _forge.Pool.Contains(charId);
            if (!fromLibrary && !fromPool) return BattleError.NotCastable;
            if (Ap < def.ApCost) return BattleError.NotEnoughAp;

            // 单体效果需要有效的存活目标;未指定且场上仅一个存活敌人时自动锁定(3.8.3 单敌免选)
            if (NeedsTarget(def) &&
                (targetIndex < 0 || targetIndex >= _enemies.Count || !_enemies[targetIndex].Alive))
            {
                int soleAlive = -1;
                for (int i = 0; i < _enemies.Count; i++)
                {
                    if (!_enemies[i].Alive) continue;
                    if (soleAlive >= 0) { soleAlive = -1; break; } // 多于一个存活
                    soleAlive = i;
                }
                if (soleAlive < 0)
                    return BattleError.InvalidTarget;
                targetIndex = soleAlive;
            }

            // 前排满员强阻断(2026-07-25):在扣 AP/消耗字之前拒出,交 UI 弹「已满,是否替换?」
            if (!replaceSummon && IsSummonBlocked(def)) return BattleError.SummonCapFull;

            _events.Clear();
            Ap -= def.ApCost;

            // 出字即消耗(3.8.1 v0.7 拍板,无回归):字从库移除,部件从池中消耗
            if (fromLibrary)
            {
                var library = new List<string>(_forge.Library);
                library.Remove(charId);
                _forge = new ForgeState(library, _forge.Pool);
            }
            else
            {
                var pool = new List<string>(_forge.Pool);
                pool.Remove(charId);
                _forge = new ForgeState(_forge.Library, pool);
            }

            ApplyEffects(def, targetIndex, replaceSummon);
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

        /// <summary>广告复活(2026-07-24):败北态满血续战。HP 回满 → 回到玩家回合(刷 AP)。
        /// StartTurn 只 +Turn/刷 AP/部件掉落,无对玩家的 DoT,故复活瞬间不会被二次归零。
        /// 补给(字/部件)由 RunEngine 复活流程经 GrantLibraryChar/GrantPoolComponent 注入。</summary>
        public void Revive()
        {
            if (Phase != BattlePhase.Lost) return;
            PlayerHp = _config.PlayerMaxHp;
            Phase = BattlePhase.PlayerTurn;
            StartTurn();
        }

        /// <summary>复活补给:把一个字加入当前战斗字库;满库返回 false 不入(守容量上限)。</summary>
        public bool GrantLibraryChar(string charId)
        {
            if (_forge.Library.Count >= _config.LibraryCapacity) return false;
            var library = new List<string>(_forge.Library) { charId };
            _forge = new ForgeState(library, _forge.Pool);
            return true;
        }

        /// <summary>复活补给:把一个部件加入当前战斗部件池;满池返回 false 不入(守容量上限)。</summary>
        public bool GrantPoolComponent(string componentId)
        {
            if (_forge.Pool.Count >= _config.PoolCapacity) return false;
            var pool = new List<string>(_forge.Pool) { componentId };
            _forge = new ForgeState(_forge.Library, pool);
            return true;
        }

        /// <summary>兜底一击(4.5 第二层防卡手地板):无效果的部件/字出手时的弱效果,永不 brick。</summary>
        private static readonly EffectDef[] FallbackEffects = { new(EffectKind.DamageSingle, 3) };

        /// <summary>该字的实际出字效果:无效果者用兜底一击。</summary>
        private static IReadOnlyList<EffectDef> EffectsOf(CharDef def) =>
            def.Effects.Count > 0 ? def.Effects : FallbackEffects;

        /// <summary>该字会召唤,且前排已满 —— 出字须先确认替换。</summary>
        private bool IsSummonBlocked(CharDef def)
        {
            if (AliveSummons() < SummonCap) return false;
            foreach (var effect in EffectsOf(def))
                if (effect.Kind == EffectKind.Summon) return true;
            return false;
        }

        /// <summary>这张字一次会召出几只(多条召唤效果累加,封顶到前排上限)。
        /// 满员替换时即「从最前一只起顶掉几只」,供 UI 文案用。</summary>
        public int SummonCountOf(CharDef def)
        {
            int count = 0;
            foreach (var effect in EffectsOf(def))
                if (effect.Kind == EffectKind.Summon) count += effect.SummonCount;
            return Math.Min(count, SummonCap);
        }

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
                else
                    CheckBossPhase(i);
            }
            CheckWin();
            if (Phase != BattlePhase.PlayerTurn) return;

            // 召唤物反击(木系,2026-07-19):前排树各打首个存活敌人,走生克
            for (int s = 0; s < _summons.Count; s++)
            {
                var summon = _summons[s];
                if (!summon.Alive) continue;
                int target = -1;
                for (int i = 0; i < _enemies.Count; i++)
                    if (_enemies[i].Alive) { target = i; break; }
                if (target < 0) break;
                _events.Add(new BattleEvent(BattleEventKind.SummonAttack, target, summon.Attack, s)); // 发起者下标 s
                DamageEnemy(target, summon.Attack, Array.Empty<Element>(), summon.Element);
            }
            CheckWin();
            if (Phase != BattlePhase.PlayerTurn) return;

            // 敌方辅助先行动:标点小妖给其他存活字怪加攻,与站位无关(8.3)。
            // 加成本场累计、回合末不回滚;场上只剩自己时改为亲自出手(2026-07-22)
            foreach (var enemy in _enemies)
            {
                if (!enemy.Alive || enemy.Def.Ability != EnemyAbility.Buff) continue;
                if (!HasOtherAliveEnemy(enemy)) continue; // 无人可加 → 交给下面的行动循环
                for (int j = 0; j < _enemies.Count; j++)
                {
                    var other = _enemies[j];
                    if (!other.Alive || other == enemy) continue;
                    other.Attack += enemy.Attack;
                    _events.Add(new BattleEvent(BattleEventKind.EnemyBuff, j, enemy.Attack));
                }
            }

            // 敌人行动:护盾先吸收(普通桶先扣,豁免桶垫后);行动后结算自身能力
            for (int i = 0; i < _enemies.Count; i++)
            {
                var enemy = _enemies[i];
                if (!enemy.Alive) continue;
                if (enemy.Def.Ability == EnemyAbility.Buff && HasOtherAliveEnemy(enemy))
                    continue; // 已用加攻代替出手;独自在场时照常攻击

                int damage = enemy.Attack;
                int tankIdx = FirstAliveSummonIndex(); // 召唤物顶前排:整次攻击由首个存活召唤物承受(不溢出)
                if (tankIdx >= 0)
                {
                    var tank = _summons[tankIdx];
                    // 召唤物带属性:敌人打召唤走五行(金克木 ×1.5、木反克土 ×0.5)
                    int taken = WuxingResolver.ResolveEffect(damage, Array.Empty<Element>(), enemy.Element, tank.Element);
                    tank.Hp = Math.Max(0, tank.Hp - taken);
                    _events.Add(new BattleEvent(BattleEventKind.SummonHit, i, taken, tankIdx)); // 承伤者下标
                }
                else
                {
                    int fromNormal = Math.Min(_shieldNormal, damage);
                    _shieldNormal -= fromNormal;
                    int fromPersist = Math.Min(_shieldPersist, damage - fromNormal);
                    _shieldPersist -= fromPersist;
                    int absorbed = fromNormal + fromPersist;
                    PlayerHp = Math.Max(0, PlayerHp - (damage - absorbed));
                    _events.Add(new BattleEvent(BattleEventKind.EnemyAttack, i, damage, -1, absorbed));
                }

                // 通假字:首次行动后现形(8.3)
                if (enemy.Def.Ability == EnemyAbility.Disguise && enemy.ApparentElement != enemy.Element)
                {
                    enemy.ApparentElement = enemy.Element;
                    _events.Add(new BattleEvent(BattleEventKind.EnemyRevealed, i, 0));
                }

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

        private void ApplyEffects(CharDef def, int targetIndex, bool replaceSummon = false)
        {
            var recipeElements = _graph.RecipeElements(def.Id);
            var attacker = def.Element ?? Element.Heart; // 中性字视作心(全 1.0x)
            int cardLevel = _cardLevels != null && _cardLevels.TryGetValue(def.Id, out var level) ? level : 1;
            int replaceCursor = 0; // 替换从最前一只起,逐只后移:一次召多只不会顶掉刚进场的自己

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
                    case EffectKind.HealSelf: // 水系主治疗(2026-07-19 拍板);走生克(相生组合可增益)
                        int heal = WuxingResolver.ResolveEffect(value, recipeElements);
                        int healed = Math.Min(_config.PlayerMaxHp - PlayerHp, heal);
                        PlayerHp += healed;
                        _events.Add(new BattleEvent(BattleEventKind.Heal, -1, healed));
                        break;
                    case EffectKind.Summon: // 木系主召唤(2026-07-19 拍板):前排抗伤+回合末反击
                        for (int n = 0; n < effect.SummonCount; n++)
                        {
                            var newborn = new SummonState(effect.SummonChar, attacker, value,
                                MetaRules.ScaleByCardLevel(effect.SummonAttack, cardLevel));
                            if (AliveSummons() < SummonCap)
                            {
                                _summons.Add(newborn);
                                _events.Add(new BattleEvent(BattleEventKind.Summon, -1, value));
                                continue;
                            }
                            if (!replaceSummon) // 未确认替换:满员只提示(Cast 已在满员时拒出,此处仅拦「部分溢出」)
                            {
                                _events.Add(new BattleEvent(BattleEventKind.SummonCapReached, -1, 0));
                                break;
                            }
                            int slot = NextAliveSummonIndex(replaceCursor);
                            if (slot < 0) break;
                            replaceCursor = slot + 1;
                            _summons[slot] = newborn; // 原地顶替:下标稳定,表现层血条引用不错位
                            _events.Add(new BattleEvent(BattleEventKind.Summon, -1, value, slot));
                        }
                        break;
                }
            }
        }

        /// <summary>场上除 self 外还有存活敌人吗(辅助型据此决定加攻还是出手)。</summary>
        private bool HasOtherAliveEnemy(EnemyState self)
        {
            foreach (var enemy in _enemies)
                if (enemy != self && enemy.Alive) return true;
            return false;
        }

        private int AliveSummons()
        {
            int alive = 0;
            foreach (var summon in _summons)
                if (summon.Alive) alive++;
            return alive;
        }

        private int FirstAliveSummonIndex() => NextAliveSummonIndex(0);

        private int NextAliveSummonIndex(int from)
        {
            for (int s = from; s < _summons.Count; s++)
                if (_summons[s].Alive) return s;
            return -1;
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
            // 承伤减免(坚壁/「山」类)遇属性克制失效:被克(×1.5)直接按克制结算,不再乘减免
            if (enemy.DamageTaken != 1f && WuxingResolver.KeMultiplier(attacker, enemy.Element) < 1.5f)
                damage = (int)Math.Floor(damage * enemy.DamageTaken);
            enemy.Hp = Math.Max(0, enemy.Hp - damage);
            _events.Add(new BattleEvent(BattleEventKind.Damage, enemyIndex, damage));

            // 生僻字:受击两次后被"读懂"(8.3)
            enemy.HitsTaken += 1;
            if (enemy.Def.Ability == EnemyAbility.Obscure && enemy.ApparentElement == null && enemy.HitsTaken >= 2)
            {
                enemy.ApparentElement = enemy.Element;
                _events.Add(new BattleEvent(BattleEventKind.EnemyRevealed, enemyIndex, 0));
            }

            if (!enemy.Alive)
            {
                ResolveDefeat(enemyIndex);
                return;
            }
            CheckBossPhase(enemyIndex);

            // 焦痕:受击存活即自燃加攻(越磨越烫,宜速杀)
            if (enemy.Def.Ability == EnemyAbility.Scorch)
            {
                enemy.Attack += ScorchGain;
                _events.Add(new BattleEvent(BattleEventKind.EnemyBuff, enemyIndex, ScorchGain));
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

        private void ResolveDefeat(int enemyIndex)
        {
            _events.Add(new BattleEvent(BattleEventKind.EnemyDied, enemyIndex, 0));
        }

        /// <summary>Boss 血池换阶(8.5 v0.7):跨过阈值即切阶段(一击可连跨多阶),血量连续不重置。</summary>
        private void CheckBossPhase(int enemyIndex)
        {
            var enemy = _enemies[enemyIndex];
            if (!enemy.IsBoss) return;
            while (enemy.PhaseIndex < enemy.Def.Phases.Count - 1 && enemy.Hp <= enemy.PhaseBounds[enemy.PhaseIndex])
            {
                enemy.ApplyPhaseStats(enemy.PhaseIndex + 1);
                _events.Add(new BattleEvent(BattleEventKind.BossPhase, enemyIndex, enemy.PhaseIndex));
            }
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
