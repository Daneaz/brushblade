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
        DropChoice, // 回合掉字遇满库:停下让玩家替换或跳过(2026-08-04)
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
        public int DropsPerTurn { get; set; } = 1; // 回合掉字数(2026-08-04:由「掉 2 部件」改为「掉 1 字」)
        public int BossPhaseJitterPercent { get; set; } = 8; // Boss 换阶阈值浮动幅度(±总血%,2026-07-19)
        // 阶段内第 N 个敌方回合进入蓄力,下回合释放(计数每阶段重开,见 EnemyState.ApplyPhaseStats)。
        // 2 = 普攻、蓄力、释放 —— 阶段撑满 3 个敌方回合才吃得到大招(2026-07-29)
        public int BossChargeEvery { get; set; } = 2;
        /// <summary>历史遗留字段(2026-08-04):回合掉落已改为从 <see cref="UnlockedChars"/> 掉字
        /// (见 StartTurn),此字段不再有任何读取方,只剩调用方仍在赋值。留着未删是因为跨
        /// Core/Data/Presentation/配置校验四处引用,清理超出掉落改造本次范围。</summary>
        public IReadOnlyList<string> DropTable { get; set; } = Array.Empty<string>();

        /// <summary>可合成的字集合 = 玩家的出阵列表(2026-07-20 拍板:没编入出阵就合不出来,
        /// 与战利品同源);null = 不限(工装与旧调用)。</summary>
        public IReadOnlyCollection<string> UnlockedChars { get; set; }

        /// <summary>同配置、只换血量上限的副本(局内上限奇遇用,2026-08-04)。
        /// 浅拷贝:调用方拿到独立实例,改它不会波及传进来的那份。</summary>
        public BattleConfig WithPlayerMaxHp(int playerMaxHp)
        {
            var copy = (BattleConfig)MemberwiseClone();
            copy.PlayerMaxHp = playerMaxHp;
            return copy;
        }
    }

    /// <summary>结算事件(供表现层做打击感,13.3;架构:表现监听 Core 事件,不反向驱动)。</summary>
    public enum BattleEventKind
    {
        Damage,      // 我方对敌伤害(TargetIndex = 敌人下标)
        Burn,        // 施加灼烧层数
        Shield,      // 获得护盾(TargetIndex = −1 玩家)
        BurnTick,    // 回合末灼烧结算伤害
        BleedTick,   // 回合末流血结算伤害(无属性,不走生克;2026-08-04)
        EnemyDied,   // 敌人被消灭
        EnemyAttack, // 敌方对玩家伤害(Amount = 总伤,含被护盾吸收部分;TargetIndex = 攻击者敌人下标,驱动冲刺动效)
        EnemySplit,  // 叠字怪分裂(TargetIndex = 原体下标)
        BossPhase,   // 成语 Boss 进入新阶段(Amount = 新阶段下标)
        Heal,        // 治疗自身(Amount = 实际回复量,2026-07-19)
        Summon,      // 召唤前排单位(Amount = 血量;SecondIndex = 被顶替的槽位,新增则 −1)
        SummonHit,   // 召唤物替玩家承伤(Amount = 伤害;TargetIndex = 攻击者敌人下标,驱动冲刺动效)
        SummonAttack,     // 召唤物反击敌人(TargetIndex = 敌人下标;仅驱动动效,伤害走 Damage)
        EnemyTurnBegan, // 阶段分隔:此后为敌方行动(2026-07-27)。表现层据此切「召唤反击段 / 敌方段」——
                        // 靠事件种类猜边界会被受击加攻之类的伴随事件带偏,已出过两次动画错乱
        EnemyBuff,   // 加攻(标点小妖给同伴 / 焦痕受击自燃;TargetIndex = 被加成的敌人)
        EnemyRevealed, // 通假字现形/生僻字被读懂(TargetIndex = 该敌人)
        BossCharging,   // Boss 进入蓄力回合(Amount = 即将释放的 BossSkill;驱动预警 UI)
        BossSkillCast,  // Boss 释放技能(Amount = BossSkill);随后是各目标的受击事件
        ShieldBroken,   // 护盾被倾覆清空(TargetIndex = −1,Amount = 清掉的总量)
        Regrow,      // 缺笔妖自补全(TargetIndex = 该敌人,Amount = 实际回血,SecondIndex = 补全进度 1~3)。
                     // 原先是**静默**结算的:模型瞬时回血、表现层只在末次重绘看到结果,
                     // 于是玩家看到的是「召唤物砸上去不掉血」「还没打就满血」(2026-07-29 实测)
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
        private const int SummonCap = 6; // 场上存活召唤物上限(2026-08-03:4 → 6)
        private const int EnemyCap = 6;  // 场上敌人上限(2026-08-03),分裂怪据此守闸
        private const int ScorchGain = 2; // 焦痕受击存活的加攻量
        private const int ArmorBreakPercent = 25; // 破甲的承伤加成(不叠层,恒定)
        private const int PierceBonusPercent = 15; // 穿甲的保底加成(对有无减免的目标一律生效)
        private const int ActionMeterThreshold = 100; // 计量器满值:攒够即行动一次
        private const int MaxActionsPerTurn = 2;      // 单回合行动次数封顶(口径 4)

        private ForgeState _forge;
        private readonly IReadOnlyDictionary<string, int> _cardLevels; // 局外卡等级(19.3.2;null = 全 1 级)
        private int _burnPerStack = 2;      // 灼烧每层结算伤害(10.2;炽 +1,可叠加)
        private int _shieldNormal;          // 普通护盾:关间/段间都延续,整场爬塔通吃(2026-07-26)
        private int _shieldPersist;         // 豁免桶护盾(堡):吸伤时垫在普通桶之后
        private int _apPenaltyNextTurn; // 倾覆造成的下回合 AP 扣减(spec 4.3),消费后清零

        // 回合掉字遇满库时挂起的那个字;Phase == DropChoice 期间非 null
        private string _pendingDrop;

        /// <summary>玩家侧状态容器(HoT / 减伤,2026-08-04 统一迁入状态容器)。减伤 SourceId = 字
        /// ID,同字覆盖 = 只刷新不叠加;TurnsLeft = -1 段内持久,跨战斗携带见 RunEngine._carriedStatuses。</summary>
        private readonly StatusBag _playerStatuses = new();

        /// <summary>SourceId 自增序号(2026-08-04):HoT(技能机制详表「滋」)与 AttackBuff
        /// (标点小妖加攻、焦痕受击自燃,Task 5 后接入)都允许同字/同源叠加,靠每次施放给一个
        /// 独一无二的 SourceId 绕开 Apply() 的同源覆盖。要进快照——续爬后计数器归零会与快照里
        /// 恢复的条目撞号,撞上就被意外覆盖。</summary>
        private int _statusSerial;

        /// <summary>所有减伤来源连乘后的承伤系数(1.0 = 无减伤;乘法叠加,天然趋近但不达 0)。</summary>
        public float DamageReductionMultiplier
        {
            get
            {
                float multiplier = 1f;
                foreach (var s in _playerStatuses.All)
                    if (s.Kind == StatusKind.DamageReduction)
                        multiplier *= 1f - s.Magnitude / 100f;
                return multiplier;
            }
        }

        /// <summary>套减伤系数(2026-08-03):普攻与 Boss 大招同口径——「受伤 −X%」不分攻击类型。</summary>
        private int ReducedDamage(int rawDamage) => (int)Math.Floor(rawDamage * DamageReductionMultiplier);

        public BattleEngine(RecipeGraph graph, BattleConfig config,
            IReadOnlyList<string> startingLibrary, IReadOnlyList<string> startingPool,
            IReadOnlyList<EnemyDef> enemies, int seed, int? startingHp = null,
            IReadOnlyDictionary<string, int> cardLevels = null,
            int startingNormalShield = 0, int startingPersistShield = 0,
            IReadOnlyList<SummonSnapshot> startingSummons = null,
            IReadOnlyList<StatusEffect> startingStatuses = null)
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
            // 召唤物跨战斗保留(2026-08-03):与普通盾同口径,上一层活下来的原样入场(残血不回满)。
            // 这里不再钳制 SummonCap:来源已受上限约束——召唤侧出字时已卡死 SummonCap(Cast/
            // SummonReplaceCountOf),存档文件那条路径也只写受约束过的携带态,不存在真实超员输入。
            if (startingSummons != null)
                foreach (var summon in startingSummons)
                    _summons.Add(SummonState.Restore(summon));
            // 减伤跨战斗保留(2026-08-04):与普通盾同口径,段内持久,到段末才清。
            if (startingStatuses != null)
                _playerStatuses.CopyFrom(startingStatuses);
            Phase = BattlePhase.PlayerTurn;
            StartTurn();
        }

        /// <summary>断点存档专用构造:不发牌、不开回合,状态全部由 <see cref="Restore"/> 灌进来。</summary>
        private BattleEngine(RecipeGraph graph, BattleConfig config,
            IReadOnlyDictionary<string, int> cardLevels, GameRandom random)
        {
            _graph = graph;
            _config = config;
            _cardLevels = cardLevels;
            _random = random;
            _forge = new ForgeState(new List<string>(), new List<string>());
        }

        /// <summary>战斗内断点存档(2026-07-27):摊平全部可变状态。
        /// 配置侧(字表/敌表定义/卡等级)不进快照,复原时由外层照原样传回。</summary>
        public BattleSnapshot Capture()
        {
            var snapshot = new BattleSnapshot
            {
                PlayerHp = PlayerHp,
                Ap = Ap,
                Turn = Turn,
                Phase = Phase,
                ShieldNormal = _shieldNormal,
                ShieldPersist = _shieldPersist,
                BurnPerStack = _burnPerStack,
                RandomState = _random.State,
                Library = new List<string>(_forge.Library),
                Pool = new List<string>(_forge.Pool),
                PendingDrop = _pendingDrop,
                StatusSerial = _statusSerial,
            };
            foreach (var enemy in _enemies) snapshot.Enemies.Add(enemy.Capture());
            foreach (var summon in _summons) snapshot.Summons.Add(summon.Capture());
            foreach (var s in _playerStatuses.All) snapshot.PlayerStatuses.Add(s.Clone());
            return snapshot;
        }

        /// <summary>从断点存档复原。enemyDefs:id → 定义(分裂出的克隆与本体共用一个 Def,
        /// 所以按 id 查而不是按遭遇下标取)。</summary>
        public static BattleEngine Restore(BattleSnapshot snapshot, RecipeGraph graph, BattleConfig config,
            IReadOnlyDictionary<string, int> cardLevels, IReadOnlyDictionary<string, EnemyDef> enemyDefs)
        {
            var engine = new BattleEngine(graph, config, cardLevels, GameRandom.FromState(snapshot.RandomState))
            {
                PlayerHp = snapshot.PlayerHp,
                Ap = snapshot.Ap,
                Turn = snapshot.Turn,
                Phase = snapshot.Phase,
                _shieldNormal = snapshot.ShieldNormal,
                _shieldPersist = snapshot.ShieldPersist,
                _burnPerStack = snapshot.BurnPerStack,
                _pendingDrop = snapshot.PendingDrop,
                _statusSerial = snapshot.StatusSerial,
            };
            engine._forge = new ForgeState(new List<string>(snapshot.Library), new List<string>(snapshot.Pool));
            foreach (var enemy in snapshot.Enemies)
            {
                if (!enemyDefs.TryGetValue(enemy.DefId, out var def))
                    throw new InvalidOperationException($"存档里的字怪「{enemy.DefId}」不在本层遭遇定义中");
                engine._enemies.Add(EnemyState.Restore(enemy, def));
            }
            foreach (var summon in snapshot.Summons)
                engine._summons.Add(SummonState.Restore(summon));
            engine._playerStatuses.CopyFrom(snapshot.PlayerStatuses ?? new List<StatusEffect>());
            return engine;
        }

        public BattlePhase Phase { get; private set; }
        public int Turn { get; private set; }
        public int Ap { get; private set; }
        public int ApPerTurn => _config.ApPerTurn;   // 每回合 AP 上限(UI 满格数 / 提示文案用;一气技能会抬高)
        public int PlayerHp { get; private set; }
        public int MaxHp => _config.PlayerMaxHp;     // 本场生效的血量上限(局内奇遇可抬高,2026-08-04)

        /// <summary>待决议的掉落字(满库时挂起);无待决议时为 null。</summary>
        public string PendingDrop => _pendingDrop;
        public int PlayerShield => _shieldNormal + _shieldPersist;
        public int ShieldNormal => _shieldNormal;
        public int ShieldPersist => _shieldPersist;

        /// <summary>玩家侧状态容器(HoT / 减伤),供战斗结束时取回跨战斗延续(2026-08-04)。</summary>
        public StatusBag PlayerStatuses => _playerStatuses;
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

        /// <summary>拆(免 AP,2026-08-03 拍板)。</summary>
        public BattleError Dismantle(string charId)
        {
            if (Phase != BattlePhase.PlayerTurn) return BattleError.BattleOver;

            var result = ForgeEngine.TryDismantle(charId, _graph, _forge, _config.PoolCapacity, _config.LibraryCapacity);
            if (!result.Success)
            {
                LastForgeError = result.Error;
                return BattleError.ForgeFailed;
            }
            _forge = result.State;
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
        /// replaceSummon:前排满员时顶掉最前的召唤物入场(UI 弹窗确认后才置位),否则满员直接拒出。
        /// attackMode:把字拖到敌人身上出手(2026-07-26),水/土 改走 AttackEffects。</summary>
        public BattleError Cast(string charId, int targetIndex = -1, bool replaceSummon = false,
            bool attackMode = false)
        {
            if (Phase != BattlePhase.PlayerTurn) return BattleError.BattleOver;
            if (!_graph.TryGet(charId, out var def)) return BattleError.NotCastable;

            bool fromLibrary = _forge.Library.Contains(charId);
            bool fromPool = !fromLibrary && def.IsLeaf && _forge.Pool.Contains(charId);
            if (!fromLibrary && !fromPool) return BattleError.NotCastable;
            if (Ap < def.ApCost) return BattleError.NotEnoughAp;

            // 单体效果需要有效的存活目标;未指定且场上仅一个存活敌人时自动锁定(3.8.3 单敌免选)
            if (NeedsTarget(def, attackMode) &&
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

            // 前排放不下就强阻断(2026-07-25):在扣 AP/消耗字之前拒出,交 UI 弹「是否替换?」。
            // 不只看满员——3/4 时召 2 只同样溢出,也得先问过玩家
            if (!replaceSummon && SummonReplaceCountOf(def, attackMode) > 0) return BattleError.SummonCapFull;

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

            ApplyEffects(def, targetIndex, replaceSummon, attackMode);
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
        /// StartTurn 会 +Turn/刷 AP,并可能因回合掉字撞满库而把 Phase 从 PlayerTurn 改成
        /// DropChoice(2026-08-04)——复活后不一定直接落在 PlayerTurn,调用方需按 Phase 分支处理。
        /// StartTurn 无对玩家的 DoT,故复活瞬间不会被二次归零。
        /// 补给(字)由 RunEngine 复活流程经 GrantLibraryChar 注入(部件补给已随掉落改造删除)。</summary>
        public void Revive()
        {
            if (Phase != BattlePhase.Lost) return;
            PlayerHp = _config.PlayerMaxHp;
            Phase = BattlePhase.PlayerTurn;
            StartTurn();
        }

        /// <summary>掉落决议:用待决议字换掉字库第 <paramref name="replaceIndex"/> 张
        /// (被换的字永久移除,与战利品 PickRewardReplacing 同口径)。</summary>
        public BattleError ResolveDrop(int replaceIndex)
        {
            if (Phase != BattlePhase.DropChoice) return BattleError.BattleOver;
            if (replaceIndex < 0 || replaceIndex >= _forge.Library.Count)
                return BattleError.NotCastable;

            var library = new List<string>(_forge.Library);
            library[replaceIndex] = _pendingDrop;
            _forge = new ForgeState(library, _forge.Pool);
            _pendingDrop = null;
            Phase = BattlePhase.PlayerTurn;
            return BattleError.None;
        }

        /// <summary>掉落决议:弃掉这次掉落,字库不变。</summary>
        public BattleError SkipDrop()
        {
            if (Phase != BattlePhase.DropChoice) return BattleError.BattleOver;
            _pendingDrop = null;
            Phase = BattlePhase.PlayerTurn;
            return BattleError.None;
        }

        /// <summary>复活补给:把一个字加入当前战斗字库;满库返回 false 不入(守容量上限)。</summary>
        public bool GrantLibraryChar(string charId)
        {
            if (_forge.Library.Count >= _config.LibraryCapacity) return false;
            var library = new List<string>(_forge.Library) { charId };
            _forge = new ForgeState(library, _forge.Pool);
            return true;
        }

        /// <summary>满库时的补给去处:换掉字库第 <paramref name="index"/> 张(被换的字永久移除)。
        /// 与 ResolveDrop 同口径,但字由调用方给 —— 那个绑定的是回合掉字挂起的 _pendingDrop。</summary>
        public bool ReplaceLibraryChar(int index, string charId)
        {
            if (index < 0 || index >= _forge.Library.Count) return false;
            var library = new List<string>(_forge.Library);
            library[index] = charId;
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

        /// <summary>该字的实际出字效果:攻击模式下优先用 AttackEffects(水/土 的第二用法),
        /// 没有第二用法就照常;都没有效果的用兜底一击。</summary>
        private static IReadOnlyList<EffectDef> EffectsOf(CharDef def, bool attackMode = false)
        {
            if (attackMode && def.AttackEffects.Count > 0) return def.AttackEffects;
            return def.Effects.Count > 0 ? def.Effects : FallbackEffects;
        }

        /// <summary>此刻出这张字会顶掉最前的几只(0 = 空位够,直接进场);UI 弹窗文案与阻断判定共用。</summary>
        public int SummonReplaceCountOf(CharDef def, bool attackMode = false)
        {
            int count = SummonCountOf(def, attackMode);
            return count <= 0 ? 0 : Math.Max(0, AliveSummons() + count - SummonCap);
        }

        /// <summary>这张字一次会召出几只(多条召唤效果累加,封顶到前排上限)。
        /// 满员替换时即「从最前一只起顶掉几只」,供 UI 文案用。</summary>
        public int SummonCountOf(CharDef def, bool attackMode = false)
        {
            int count = 0;
            foreach (var effect in EffectsOf(def, attackMode))
                if (effect.Kind == EffectKind.Summon) count += effect.SummonCount;
            return Math.Min(count, SummonCap);
        }

        /// <summary>该字的效果是否需要指定单体目标(供 UI 进入选目标模式;攻击模式看第二用法)。</summary>
        public static bool NeedsTarget(CharDef def, bool attackMode = false)
        {
            foreach (var effect in EffectsOf(def, attackMode))
                if (effect.Kind == EffectKind.DamageSingle || effect.Kind == EffectKind.BurnSingle
                    || effect.Kind == EffectKind.Bleed || effect.Kind == EffectKind.Freeze
                    || effect.Kind == EffectKind.Slow || effect.Kind == EffectKind.ArmorBreak)
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
                if (!enemy.Alive) continue;
                var burn = enemy.Statuses.Find(StatusKind.Burn);
                if (burn == null || burn.Magnitude <= 0) continue;
                // 灼烧属火(2026-08-03):只结算克制,不结算相生 —— 层数是平值,
                // 相生已在施加时由 WuxingResolver 体现过
                int tick = (int)Math.Floor(burn.Magnitude * _burnPerStack
                    * WuxingResolver.KeMultiplier(Element.Fire, enemy.Element));
                enemy.Hp = Math.Max(0, enemy.Hp - tick);
                burn.Magnitude -= 1;
                if (burn.Magnitude <= 0) enemy.Statuses.Remove(StatusKind.Burn);
                _events.Add(new BattleEvent(BattleEventKind.BurnTick, i, tick));
                if (!enemy.Alive)
                    ResolveDefeat(i);
                else
                    CheckBossPhase(i);
            }
            CheckWin();
            if (Phase != BattlePhase.PlayerTurn) return;

            // 流血(2026-08-03):无属性,不乘任何生克系数
            // 回合数递减挪到 EndTurn 末尾统一处理(2026-08-04,见下方"状态回合递减"),
            // 这里只读不写,避免本回合刚施加的流血被立刻多减一次。
            for (int i = 0; i < _enemies.Count; i++)
            {
                var enemy = _enemies[i];
                if (!enemy.Alive) continue;
                var bleedStatus = enemy.Statuses.Find(StatusKind.Bleed);
                if (bleedStatus == null || bleedStatus.TurnsLeft <= 0) continue;
                int bleed = bleedStatus.Magnitude;
                enemy.Hp = Math.Max(0, enemy.Hp - bleed);
                _events.Add(new BattleEvent(BattleEventKind.BleedTick, i, bleed));
                if (!enemy.Alive)
                    ResolveDefeat(i);
                else
                    CheckBossPhase(i);
            }
            CheckWin();
            if (Phase != BattlePhase.PlayerTurn) return;

            // 持续治疗(2026-08-04):回合数递减挪到 EndTurn 末尾统一处理(与 Bleed 同理,见下方
            // "状态回合递减"),这里只结算不写 TurnsLeft,避免本回合刚施加的 HoT 被立刻多减一次。
            for (int i = _playerStatuses.All.Count - 1; i >= 0; i--)
            {
                var hot = _playerStatuses.All[i];
                if (hot.Kind != StatusKind.HealOverTime) continue;
                if (hot.TargetAll) HealPlayerAndSummons(hot.Magnitude);
                else
                {
                    int healed = Math.Min(_config.PlayerMaxHp - PlayerHp, hot.Magnitude);
                    PlayerHp += healed;
                    _events.Add(new BattleEvent(BattleEventKind.Heal, -1, healed));
                }
            }

            // 召唤物反击(木系,2026-07-19):前排树各打首个存活敌人,走生克。
            // 2026-08-04:与敌人同走行动计量器 —— 减速将来也能作用于我方召唤物。
            for (int s = 0; s < _summons.Count; s++)
            {
                var summon = _summons[s];
                if (!summon.Alive) continue;
                summon.ActionMeter += summon.Speed;
                int acts = Math.Min(summon.ActionMeter / ActionMeterThreshold, MaxActionsPerTurn);
                summon.ActionMeter -= acts * ActionMeterThreshold;
                if (summon.ActionMeter >= ActionMeterThreshold) summon.ActionMeter = 0;
                for (int act = 0; act < acts; act++)
                {
                    if (!summon.Alive) break;          // 反伤可能在两次出手之间打死它
                    int target = -1;
                    for (int i = 0; i < _enemies.Count; i++)
                        if (_enemies[i].Alive) { target = i; break; }
                    if (target < 0) break;
                    _events.Add(new BattleEvent(BattleEventKind.SummonAttack, target, summon.Attack, s)); // 发起者下标 s
                    DamageEnemy(target, summon.Attack, Array.Empty<Element>(), summon.Element);
                }
            }
            CheckWin();
            if (Phase != BattlePhase.PlayerTurn) return;

            _events.Add(new BattleEvent(BattleEventKind.EnemyTurnBegan, -1, 0)); // 召唤段到此为止,以下是敌方行动

            // 行动计量器(2026-08-04):每回合累积有效速度,每满 100 行动一次。旧的半速开关
            // (SlowTurns/SlowActs 交替)是 Speed 50 的特例。冻结期间不累积——保持「节拍原地
            // 暂停」语义,与下方"状态回合递减"里冻结中 SpeedModifier 也暂停递减的处理相呼应。
            var actionCount = new int[_enemies.Count];
            for (int i = 0; i < _enemies.Count; i++)
            {
                var enemy = _enemies[i];
                if (!enemy.Alive || enemy.Statuses.Has(StatusKind.Freeze)) continue;
                int effective = Math.Max(0,
                    enemy.Speed + enemy.Statuses.TotalMagnitude(StatusKind.SpeedModifier));
                enemy.ActionMeter += effective;
                actionCount[i] = Math.Min(enemy.ActionMeter / ActionMeterThreshold, MaxActionsPerTurn);
                enemy.ActionMeter -= actionCount[i] * ActionMeterThreshold;
                if (enemy.ActionMeter >= ActionMeterThreshold) enemy.ActionMeter = 0; // 封顶后不留余额
            }

            // 敌方辅助先行动:标点小妖给其他存活字怪加攻,与站位无关(8.3)。
            // 加成本场累计、回合末不回滚;场上只剩自己时改为亲自出手(2026-07-22)
            for (int i = 0; i < _enemies.Count; i++)
            {
                var enemy = _enemies[i];
                if (!enemy.Alive || enemy.Def.Ability != EnemyAbility.Buff) continue;
                if (actionCount[i] == 0) continue; // 冻结或计量器不足:连辅助加攻都不出手
                if (!HasOtherAliveEnemy(enemy)) continue; // 无人可加 → 交给下面的行动循环
                for (int j = 0; j < _enemies.Count; j++)
                {
                    var other = _enemies[j];
                    if (!other.Alive || other == enemy) continue;
                    // 加成本场累计、回合末不回滚(既有语义)。SourceId 必须每次唯一——用回合数做
                    // 后缀不够:场上若有两只同字标点小妖同回合各给同一目标加一次,回合数后缀会撞车
                    // 变成互相覆盖而非累加(与 Task 4 的 HoT SourceId 教训同型)。
                    other.Statuses.Apply(new StatusEffect
                    {
                        Kind = StatusKind.AttackBuff, Polarity = StatusPolarity.Buff,
                        Magnitude = enemy.Attack, TurnsLeft = -1,
                        SourceId = $"{enemy.Def.Id}#{_statusSerial++}",
                    });
                    _events.Add(new BattleEvent(BattleEventKind.EnemyBuff, j, enemy.Attack));
                }
            }

            // 敌人行动:护盾先吸收(普通桶先扣,豁免桶垫后);行动后结算自身能力。
            // 按 actionCount[i] 循环——Speed 200 这类会在同一回合行动多次;每次行动前重新
            // 检查 Alive,反伤可能在两次行动之间打死它。
            for (int i = 0; i < _enemies.Count; i++)
            {
                var enemy = _enemies[i];
                if (!enemy.Alive) continue;
                if (actionCount[i] == 0) continue; // 冻结或计量器不足:本回合不行动
                if (enemy.Def.Ability == EnemyAbility.Buff && HasOtherAliveEnemy(enemy))
                    continue; // 已用加攻代替出手;独自在场时照常攻击

                for (int act = 0; act < actionCount[i]; act++)
                {
                    if (!enemy.Alive) break; // 反伤可能在两次行动之间打死它

                    if (enemy.IsBoss && ResolveBossTurn(i, enemy))
                        continue; // 已蓄力或已放大招,本回合不走普攻

                    int damage = ReducedDamage(enemy.Attack); // 先减伤(百分比),再护盾吸收(定量)
                    int tankIdx = FirstAliveSummonIndex(); // 召唤物顶前排:整次攻击由首个存活召唤物承受(不溢出)
                    if (tankIdx >= 0)
                    {
                        // 召唤物带属性:敌人打召唤走五行(金克木 ×1.5、木反克土 ×0.5)
                        DamageSummon(i, tankIdx, damage, enemy.Element);
                    }
                    else
                    {
                        DamagePlayerDirect(i, damage);
                    }

                    // 通假字:首次行动后现形(8.3)
                    if (enemy.Def.Ability == EnemyAbility.Disguise && enemy.ApparentElement != enemy.Element)
                    {
                        enemy.ApparentElement = enemy.Element;
                        _events.Add(new BattleEvent(BattleEventKind.EnemyRevealed, i, 0));
                    }
                }
            }
            // 状态回合递减(2026-08-04):统一挪到本回合全部结算之后,避免"刚施加就少一回合"
            // (Bleed_ExpiresAfterThreeTurns 守着这条)。
            // ⚠️ 必须排在 PlayerHp<=0 早退**之前**(2026-08-05,全分支评审 Important 3):早退会
            // 直接 return,若递减挪到早退后面,玩家阵亡的那个 EndTurn 就整个跳过递减 —— 广告复活
            // 满血续战后,所有状态(流血/冻结/减速/HoT)都会多续一回合
            // (Revive_DoesNotGrantExtraStatusTurn 守着这条)。递减不依赖缺笔妖补全循环,排在
            // 补全循环之前不影响其结果。
            // 冻结中:Freeze 自身与流血照常倒计时(冻结只免自身行动,不免流血/解冻倒计时);
            // 只有 SpeedModifier 暂停递减(节拍原地暂停,呼应上面 ActionMeter 累积也暂停)。
            // 2026-08-05(全分支评审 Important 4):原先这里是"只列 Freeze/Bleed 两种"的白名单,
            // 给 Dispel/Cleanse/Immunity/Silence 这批以后会给敌人挂有限时长状态的机制埋雷——
            // 挂上去的新状态会被白名单漏掉,冻结中永不到期且不报错。改成黑名单:除 SpeedModifier
            // 外统统照常递减,新加状态默认安全。
            for (int i = 0; i < _enemies.Count; i++)
            {
                var enemy = _enemies[i];
                if (!enemy.Alive) continue;
                enemy.Statuses.TickTurns(
                    enemy.Statuses.Has(StatusKind.Freeze) ? StatusKind.SpeedModifier : (StatusKind?)null);
            }
            // 玩家侧没有冻结概念,整袋统一递减即可(HoT 到期移除;减伤 TurnsLeft = -1 段内持久,不受影响)。
            _playerStatuses.TickTurns();

            if (PlayerHp <= 0)
            {
                Phase = BattlePhase.Lost;
                return;
            }

            // 缺笔妖:每回合自补全,第 3 次补全完成(8.3)。
            // **排在全部攻击之后**独立一趟(2026-07-30 试玩):原先它跟在缺笔妖自己那一记攻击
            // 后头,场上还有别的怪时就成了「打到一半血突然回了」—— 玩家的心理节拍是
            // 「我打、召唤物打、敌人打，一回合收尾时它才补」,补全得压到最后。
            for (int i = 0; i < _enemies.Count; i++)
            {
                var enemy = _enemies[i];
                // 本回合已被灼烧/召唤物打死的不许回血 —— 死了还补就成了打不死的怪
                if (!enemy.Alive) continue;
                if (enemy.Def.Ability != EnemyAbility.Regrow || enemy.RegrowProgress >= 3) continue;

                int before = enemy.Hp;
                enemy.RegrowProgress += 1;
                enemy.BaseAttack += 2; // 补全成长(形态变化,非增益):不可驱散
                // 上限取 enemy.MaxHp(当前阶段上限)而非 Def.MaxHp:缺笔妖眼下不分阶段,
                // 两者相等,但语义上该跟随阶段 —— 免得日后给它加阶段时回血直接越过阶段上限
                enemy.Hp = Math.Min(enemy.MaxHp, enemy.Hp + 3);
                if (enemy.RegrowProgress == 3)
                {
                    // ×2 只翻基础值,不翻别人给的增益(如标点小妖的 AttackBuff)——形态变化
                    // 不放大外部增益(2026-08-05 裁定;RegrowFinalDouble_DoesNotAmplifyExternalBuff 守着)。
                    enemy.BaseAttack *= 2;
                    enemy.Hp = enemy.MaxHp;
                }
                _events.Add(new BattleEvent(BattleEventKind.Regrow, i,
                    enemy.Hp - before, enemy.RegrowProgress));
            }

            StartTurn();
        }

        private void StartTurn()
        {
            Turn += 1;
            Ap = Math.Max(1, _config.ApPerTurn - _apPenaltyNextTurn); // 下限 1:不出现完全不能动的回合
            _apPenaltyNextTurn = 0;

            // 回合掉字(2026-08-04):从出战牌组掉 N 个字入库,满库则停下让玩家决议。
            // 部件不再掉落 —— 五行部件只能靠拆字获得(拆免 AP 是这条的对冲)。
            if (_config.UnlockedChars != null && _config.UnlockedChars.Count > 0)
            {
                var deck = new List<string>(_config.UnlockedChars);
                for (int i = 0; i < _config.DropsPerTurn; i++)
                {
                    string pick = deck[_random.Next(deck.Count)];
                    if (_forge.Library.Count >= _config.LibraryCapacity)
                    {
                        _pendingDrop = pick;
                        Phase = BattlePhase.DropChoice;
                        return; // 决议完才继续;剩余份额本回合作废
                    }
                    var library = new List<string>(_forge.Library) { pick };
                    _forge = new ForgeState(library, _forge.Pool);
                }
            }
        }

        private void ApplyEffects(CharDef def, int targetIndex, bool replaceSummon = false, bool attackMode = false)
        {
            var recipeElements = _graph.RecipeElements(def.Id);
            var attacker = def.Element ?? Element.Heart; // 中性字视作心(全 1.0x)
            int cardLevel = _cardLevels != null && _cardLevels.TryGetValue(def.Id, out var level) ? level : 1;
            int replaceCursor = 0; // 替换从最前一只起,逐只后移:一次召多只不会顶掉刚进场的自己

            foreach (var effect in EffectsOf(def, attackMode))
            {
                int value = MetaRules.ScaleByCardLevel(effect.Value, cardLevel); // 19.3.2:等级先作用于基础值
                switch (effect.Kind)
                {
                    case EffectKind.DamageSingle:
                        DamageEnemy(targetIndex, BaseValue(effect, value, _enemies[targetIndex]), recipeElements, attacker, effect.IgnoreArmor);
                        break;
                    case EffectKind.DamageAll:
                        int aoeCount = _enemies.Count; // 分裂产生的新怪不吃同一发 AOE
                        for (int i = 0; i < aoeCount; i++)
                            if (_enemies[i].Alive)
                                DamageEnemy(i, BaseValue(effect, value, _enemies[i]), recipeElements, attacker, effect.IgnoreArmor);
                        break;
                    case EffectKind.BurnSingle:
                        if (_enemies[targetIndex].Alive)
                        {
                            ApplyBurn(targetIndex, value);
                            _events.Add(new BattleEvent(BattleEventKind.Burn, targetIndex, value));
                        }
                        break;
                    case EffectKind.Bleed:
                        if (_enemies[targetIndex].Alive)
                        {
                            _enemies[targetIndex].Statuses.Apply(new StatusEffect
                            {
                                Kind = StatusKind.Bleed, Polarity = StatusPolarity.Debuff,
                                Magnitude = value, TurnsLeft = 3,   // 固定 3 回合
                            });
                        }
                        break;
                    case EffectKind.Freeze:
                        if (_enemies[targetIndex].Alive)
                            _enemies[targetIndex].Statuses.Apply(new StatusEffect
                            {
                                // Magnitude 不赋值(2026-08-05 M1):全代码库没有任何地方读它,
                                // 赋了反而是语义为空的垃圾值,TotalMagnitude(Freeze) 会返回它。
                                Kind = StatusKind.Freeze, Polarity = StatusPolarity.Debuff,
                                TurnsLeft = value,
                            });
                        break;
                    case EffectKind.Slow:
                        if (_enemies[targetIndex].Alive)
                        {
                            _enemies[targetIndex].Statuses.Apply(new StatusEffect
                            {
                                Kind = StatusKind.SpeedModifier, Polarity = StatusPolarity.Debuff,
                                Magnitude = -50, TurnsLeft = value, SourceId = def.Id,
                            });
                        }
                        break;
                    case EffectKind.ArmorBreak:
                        _enemies[targetIndex].Statuses.Apply(new StatusEffect
                        {
                            Kind = StatusKind.ArmorBreak,
                            Polarity = StatusPolarity.Debuff,
                            Magnitude = ArmorBreakPercent,
                            TurnsLeft = value,
                            SourceId = null,   // 按 Kind 去重 → 不叠层,重复施加只刷新
                        });
                        break;
                    case EffectKind.DamageReduction:
                        _playerStatuses.Apply(new StatusEffect  // 同字覆盖 = 刷新,不叠加(SourceId 去重)
                        {
                            Kind = StatusKind.DamageReduction, Polarity = StatusPolarity.Buff,
                            Magnitude = value, TurnsLeft = -1, SourceId = def.Id, // 段内持久
                        });
                        break;
                    case EffectKind.BurnAll:
                        for (int i = 0; i < _enemies.Count; i++)
                            if (_enemies[i].Alive)
                            {
                                ApplyBurn(i, value);
                                _events.Add(new BattleEvent(BattleEventKind.Burn, i, value));
                            }
                        break;
                    case EffectKind.Shield:
                        int shield = WuxingResolver.ResolveEffect(value, recipeElements, attacker);
                        if (effect.PersistOnce) _shieldPersist += shield;
                        else _shieldNormal += shield;
                        _events.Add(new BattleEvent(BattleEventKind.Shield, -1, shield));
                        break;
                    case EffectKind.BurnPotency:
                        _burnPerStack += value;
                        break;
                    case EffectKind.HealSelf: // 水系主治疗(2026-07-19 拍板);走生克(相生组合可增益)
                        int heal = WuxingResolver.ResolveEffect(value, recipeElements, attacker);
                        int healed = Math.Min(_config.PlayerMaxHp - PlayerHp, heal);
                        PlayerHp += healed;
                        _events.Add(new BattleEvent(BattleEventKind.Heal, -1, healed));
                        break;
                    case EffectKind.HealAll:
                        HealPlayerAndSummons(WuxingResolver.ResolveEffect(value, recipeElements, attacker));
                        break;
                    case EffectKind.HealOverTime:
                        // 可叠(2026-08-04,技能机制详表「滋」):SourceId 用自增序号而非字 ID,
                        // 让 Apply() 永远走新增分支——同字连放两次得到两条独立倒计时,与老代码
                        // 无条件 List.Add 的口径一致。不能用回合数做后缀:一回合 3 AP,同一回合
                        // 内完全可能连放两次,会被回合数误判成同一来源又变回刷新。
                        _playerStatuses.Apply(new StatusEffect
                        {
                            Kind = StatusKind.HealOverTime, Polarity = StatusPolarity.Buff,
                            Magnitude = WuxingResolver.ResolveEffect(value, recipeElements, attacker),
                            TurnsLeft = effect.Turns, TargetAll = effect.TargetAll,
                            SourceId = $"{def.Id}#{_statusSerial++}",
                        });
                        break;
                    case EffectKind.Summon: // 木系主召唤(2026-07-19 拍板):前排抗伤+回合末反击
                        for (int n = 0; n < effect.SummonCount; n++)
                        {
                            // 被动数值不吃卡等级(2026-08-05):只有血/攻/盾这些"资源"随等级涨,
                            // 反伤/灼烧层/减攻百分比这些"节奏"保持不变,免得档位失控
                            var newborn = new SummonState(effect.SummonChar, attacker, value,
                                MetaRules.ScaleByCardLevel(effect.SummonAttack, cardLevel),
                                effect.Passive);
                            if (AliveSummons() < SummonCap)
                            {
                                _summons.Add(newborn);
                                _events.Add(new BattleEvent(BattleEventKind.Summon, -1, value));
                                continue;
                            }
                            if (!replaceSummon) break; // 溢出已在 Cast 拒出,走不到这;留作越界兜底
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

        /// <summary>叠加灼烧层数(TurnsLeft = -1:段内持久,靠结算段自减 Magnitude,不受 TickTurns 影响)。</summary>
        private void ApplyBurn(int enemyIndex, int value)
        {
            var enemy = _enemies[enemyIndex];
            int newBurn = (enemy.Statuses.Find(StatusKind.Burn)?.Magnitude ?? 0) + value;
            enemy.Statuses.Apply(new StatusEffect
            {
                Kind = StatusKind.Burn, Polarity = StatusPolarity.Debuff,
                Magnitude = newBurn, TurnsLeft = -1,
            });
        }

        /// <summary>群体治疗:玩家 + 全部存活召唤物,各回 amount(玩家不超上限)。</summary>
        private void HealPlayerAndSummons(int amount)
        {
            int healed = Math.Min(_config.PlayerMaxHp - PlayerHp, amount);
            PlayerHp += healed;
            _events.Add(new BattleEvent(BattleEventKind.Heal, -1, healed));
            foreach (var summon in _summons)
            {
                if (!summon.Alive) continue;
                summon.Hp = Math.Min(summon.MaxHp, summon.Hp + amount);
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
            return effect.DoubleVsBurning && target.Statuses.Has(StatusKind.Burn) ? scaledValue * 2 : scaledValue;
        }

        private void DamageEnemy(int enemyIndex, int baseValue,
            IReadOnlyCollection<Element> recipeElements, Element attacker, bool ignoreArmor = false)
        {
            var enemy = _enemies[enemyIndex];
            int damage = WuxingResolver.ResolveEffect(baseValue, recipeElements, attacker, enemy.Element);
            float taken = enemy.DamageTaken;
            // 减免(<1)遭属性克制失效:被克(×1.5)直接按克制结算,不再乘减免。
            // 判断用 < 1 而非 != 1 —— 破甲会把承伤升到 1 以上,那属于加成,不该被这条连坐
            if (taken < 1f && WuxingResolver.KeMultiplier(attacker, enemy.Element) >= 1.5f)
                taken = 1f;
            // 穿甲:同样只忽略减免(<1)
            if (taken < 1f && ignoreArmor) taken = 1f;
            // 穿甲的保底加成:无条件生效
            if (ignoreArmor) taken += PierceBonusPercent / 100f;
            // 破甲加成:始终生效(不受克制影响),不叠层故只加一次。
            // 读 Magnitude 而非常量——施加处写什么百分比,这里就吃什么(将来加「重破甲」只改施加处)。
            var armorBreak = enemy.Statuses.Find(StatusKind.ArmorBreak);
            if (armorBreak != null) taken += armorBreak.Magnitude / 100f;
            if (taken != 1f)
                damage = (int)Math.Floor(damage * taken);
            enemy.Hp = Math.Max(0, enemy.Hp - damage);
            _events.Add(new BattleEvent(BattleEventKind.Damage, enemyIndex, damage));

            enemy.HitsTaken += 1;

            // 死亡先结算:EnemyDied 必须紧跟致死伤害,表现层据此判定「这记是否击杀」
            // (击杀不白闪、让位给置灰)。中间插任何事件都会打断判定 → 白闪抢色 + 置灰错拍
            if (!enemy.Alive)
            {
                ResolveDefeat(enemyIndex);
                return;
            }

            // 生僻字:受击两次后被"读懂"(8.3);打死了就无所谓读不读得懂
            if (enemy.Def.Ability == EnemyAbility.Obscure && enemy.ApparentElement == null && enemy.HitsTaken >= 2)
            {
                enemy.ApparentElement = enemy.Element;
                _events.Add(new BattleEvent(BattleEventKind.EnemyRevealed, enemyIndex, 0));
            }
            CheckBossPhase(enemyIndex);

            // 焦痕:受击存活即自燃加攻(越磨越烫,宜速杀)
            if (enemy.Def.Ability == EnemyAbility.Scorch)
            {
                // 一回合内可能连续多次命中同一目标(玩家多张牌接力打同一敌人),SourceId 必须
                // 每次唯一,否则同回合第二次自燃会覆盖第一次而非叠加(Task 4 的 HoT 教训同型)。
                enemy.Statuses.Apply(new StatusEffect
                {
                    Kind = StatusKind.AttackBuff, Polarity = StatusPolarity.Buff,
                    Magnitude = ScorchGain, TurnsLeft = -1,
                    SourceId = $"{enemy.Def.Id}#{_statusSerial++}",
                });
                _events.Add(new BattleEvent(BattleEventKind.EnemyBuff, enemyIndex, ScorchGain));
            }

            // 叠字怪:首次受击存活 → 分裂成两个半血(8.3;场上 <EnemyCap 时)
            if (enemy.Def.Ability == EnemyAbility.Split && !enemy.HasSplit && _enemies.Count < EnemyCap)
            {
                int half = (enemy.Hp + 1) / 2;
                enemy.Hp = half;
                enemy.HasSplit = true;
                var clone = new EnemyState(enemy.Def)
                {
                    Hp = half,
                    BaseAttack = enemy.Attack, // 一次性快照,不是活的引用——分裂出的怪不继承驱散来源
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

        /// <summary>对玩家造成伤害:护盾先吸收(普通桶先扣,豁免桶垫后)。
        /// 大招走这条 = 不经召唤物顶前排(spec 3.3 总则)。</summary>
        private void DamagePlayerDirect(int enemyIndex, int damage)
        {
            int fromNormal = Math.Min(_shieldNormal, damage);
            _shieldNormal -= fromNormal;
            int fromPersist = Math.Min(_shieldPersist, damage - fromNormal);
            _shieldPersist -= fromPersist;
            int absorbed = fromNormal + fromPersist;
            PlayerHp = Math.Max(0, PlayerHp - (damage - absorbed));
            _events.Add(new BattleEvent(BattleEventKind.EnemyAttack, enemyIndex, damage, -1, absorbed));
        }

        /// <summary>对召唤物造成伤害:走五行(与普攻打召唤同规则)。</summary>
        private void DamageSummon(int enemyIndex, int summonIndex, int damage, Element attacker)
        {
            var summon = _summons[summonIndex];
            int taken = WuxingResolver.ResolveEffect(damage, Array.Empty<Element>(), attacker, summon.Element);
            summon.Hp = Math.Max(0, summon.Hp - taken);
            _events.Add(new BattleEvent(BattleEventKind.SummonHit, enemyIndex, taken, summonIndex));
        }

        /// <summary>Boss 回合三态(spec 2026-07-28):释放 / 蓄力 / 交回普攻。
        /// 返回 true = 本回合已处理,调用方跳过普通攻击。</summary>
        private bool ResolveBossTurn(int index, EnemyState enemy)
        {
            if (enemy.IsCharging)
            {
                enemy.IsCharging = false;
                enemy.ChargeCounter = 0;
                CastBossSkill(index, enemy, enemy.ChargingSkill);
                return true;
            }

            enemy.ChargeCounter += 1;

            var skill = enemy.Def.Phases[enemy.PhaseIndex].Skill;
            if (skill == BossSkill.None || skill == BossSkill.Bulwark)
                return false; // 坚壁/无技能阶段没大招可放,但照常攒数:
                              // 冻结的话,最耗回合的坚壁段(承伤 0.5)会把节奏整个吃掉
            if (enemy.ChargeCounter < _config.BossChargeEvery)
                return false;

            enemy.IsCharging = true;
            enemy.ChargingSkill = skill; // 锁定:预告什么就放什么,期间换阶也不改写
            _events.Add(new BattleEvent(BattleEventKind.BossCharging, index, (int)skill));
            return true; // 蓄力回合不出手
        }

        /// <summary>释放当前阶段字的技能。先发 BossSkillCast 再发各目标受击事件,
        /// 表现层据此把大招动效与后续伤害分开播。
        /// 玩家份伤害统一 Attack×2(2026-07-29 修正,Devour 空放除外):三个敌方回合一轮里
        /// 1 普攻 + 1 蓄力不出手 + 1 释放,若玩家份只按 Attack 结算,总投放只有 2×Attack,
        /// 反而低于没有技能的纯普攻 Boss(3×Attack)——技能变成了减伤。抬到 ×2 后释放回合
        /// 单独顶两个普攻的量,三回合投放追平无技能 Boss(2026-07-30 修正:原注释误记成
        /// 「四个敌方回合里 2 普攻…」,方向刚好相反,实际节拍是 3 回合一轮,见 Finding 1)。</summary>
        private void CastBossSkill(int index, EnemyState enemy, BossSkill skill)
        {
            _events.Add(new BattleEvent(BattleEventKind.BossSkillCast, index, (int)skill));

            switch (skill)
            {
                case BossSkill.Deluge: // 淹没:玩家挨双倍,召唤物各挨一下(不翻倍,仍是分摊主力);减伤同口径吃(2026-08-03)
                    DamagePlayerDirect(index, ReducedDamage(enemy.Attack * 2));
                    for (int s = 0; s < _summons.Count; s++)
                        if (_summons[s].Alive)
                            DamageSummon(index, s, ReducedDamage(enemy.Attack), enemy.Element);
                    break;

                case BossSkill.Pierce: // 贯穿:一击穿过前排,同时打中后面的玩家(本就是 ×2);减伤同口径吃(2026-08-03)
                {
                    int front = FirstAliveSummonIndex();
                    if (front >= 0)
                        DamageSummon(index, front, ReducedDamage(enemy.Attack), enemy.Element);
                    DamagePlayerDirect(index, ReducedDamage(enemy.Attack * 2));
                    break;
                }

                case BossSkill.Topple: // 倾覆:先按常规吸伤(玩家挨双倍),再把剩余护盾整个掀掉;减伤同口径吃(2026-08-03)
                {
                    DamagePlayerDirect(index, ReducedDamage(enemy.Attack * 2));
                    int broken = _shieldNormal + _shieldPersist;
                    if (broken > 0)
                    {
                        _shieldNormal = 0;
                        _shieldPersist = 0;
                        _events.Add(new BattleEvent(BattleEventKind.ShieldBroken, -1, broken));
                    }
                    _apPenaltyNextTurn = 1;
                    break;
                }

                case BossSkill.Devour: // 吞噬:无视血量必杀最前一只(不回血);没得吞就普攻(设计明确不 ×2,唯一例外)
                {
                    int front = FirstAliveSummonIndex();
                    if (front >= 0)
                    {
                        var victim = _summons[front];
                        int lost = victim.Hp;
                        victim.Hp = 0;
                        _events.Add(new BattleEvent(BattleEventKind.SummonHit, index, lost, front));
                    }
                    else
                    {
                        // 没得吞退化成普攻:走减伤同口径(2026-08-03);秒杀分支本身无数值可减,不动
                        DamagePlayerDirect(index, ReducedDamage(enemy.Attack));
                    }
                    break;
                }
            }
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
