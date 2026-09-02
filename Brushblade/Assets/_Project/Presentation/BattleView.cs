using System.Collections.Generic;
using System.Linq;
using System.Text;
using Brushblade.Core;
using Brushblade.Data;
using UnityEngine;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>连战界面:战斗 → 结算 → 三选一奖励 → 下一战。每次操作后整体重绘(原型期够用)。
    /// 战斗内交互:点字库字 → 出字/拆;点部件 → 直出;可合成列表一键合;单体效果进入选目标模式。</summary>
    public sealed class BattleView : MonoBehaviour
    {
        // ===== 稿上的骨架尺寸(docs/design/ui/scenes/Battle.dc.html)=====
        // 都是**逻辑单位**,由稿子的 pt 换算而来:CanvasScaler 1600×900 按高匹配,
        // iPhone 16 Pro Max 932×430pt → 实际画布 1950×900,1pt = 2.093 逻辑单位。
        // SafeAreaFitter(GameRoot.NewView)在真机上会让出刘海;编辑器 16:9 下它是空操作,
        // 所以 Frame 自己按 SafeArea.MissingInset() 补齐差额 —— 0/1 边界落在稿上的 .safe 框,
        // 不是靠根节点白得的(见 BuildSkeleton 里 Frame 那一层)。
        // 改这些数就是改版面 —— 改完要同步改稿,别让两边漂开(scenes/README.md)。
        private const float RailW = 142f;      // 稿 68pt:左窄栏(相克环图 + 配字表)
        private const float BenchW = 276f;     // 稿 132pt:右栏拆合台
        private const float ArenaGap = 13f;    // 稿 6pt:三栏之间
        private const float TopBarH = 46f;     // 稿 22pt:顶栏
        private const float PlayerBarH = 84f;  // 稿 40pt:玩家条
        private const float RowGap = 17f;      // 稿 8pt:同排格与格之间
        private const float MidGap = 6f;       // 稿 3pt:中区各行之间
        private const float FieldGap = 4f;     // 稿 2pt:战场四排之间
        private const float DividerH = 2f;     // 稿 1pt:敌我分隔线
        private const float RailGap = 10f;     // 稿 .rail gap 5pt:左栏内部各元素间距
        private const float BenchGap = 10f;    // 稿 .bench gap 5pt:拆合台内部间距
        private const float BenchPad = 15f;    // 稿 .bench padding 7pt:拆合台内边距
        // 字库带的高度。稿上 .hand 是被字牌(56pt)撑出来的,但这里它是**叠放层**的槽
        // (见 _centerRow),两个消费方谁也撑不出另一个要的高度,只能给死一个数。
        // 非战斗阶段那一路最高的一件是奇遇选项钮 72,这个数也装得下。
        private const float HandBandH = 117f;  // 稿 56pt
        // 战场网格的实际内容宽:一排 4 格 × 293 + 3 个间距 × 17(稿 4×140 + 3×8 = 584pt)。
        // 中区本身是 1260(稿 602pt),两侧各富余 18 —— 玩家条 / 字库带 / 部件池原先都铺满
        // 1260,比上方的战场网格两边各宽出一圈,竖着看边缘不齐(2026-09-01 用户拍板收窄对齐)。
        private const float FieldContentWidth = EnemyCellWidth * 4 + RowGap * 3;
        private RecipeGraph _graph;
        private RunEngine _run;
        // 执笔人详情弹窗(PlayerInfo.Sheet)要用:局外等级/技能不在 BattleEngine 上,得从这里
        // 复原基准值(2026-09-01,单位详情轮二 Task 5)。必须是 GameRoot._meta 那一个实例——
        // 另建一份或重新 MetaStore.Load() 都会让这里的小字数值悄悄偏离 BuildBattleConfig
        // 当初写进战斗配置的那份基准,而且没有任何测试拦得住。
        private MetaState _meta;
        private System.Action<bool> _onRunEnded;
        private Juice _juice;
        private readonly System.Collections.Generic.List<RectTransform> _enemyRects = new();
        // 分层形象(下标→MobView);没有形象资产的怪该位为 null,回落圆形字头像
        private readonly System.Collections.Generic.List<MobView> _enemyMobs = new();
        // 整格点击层(2026-08-22):拖字打人悬停预览要就地改颜色,不能重绘 —— 得存着引用。
        private readonly System.Collections.Generic.List<Image> _enemyHitAreas = new();
        // 拖拽悬停预览态(2026-08-22):当前悬停的主目标下标(−1 = 未悬停任何敌人),
        // 与被形状溅到、临时改了色的格子(连同各自改色前的原值,松手/挪走时原样恢复)。
        private int _hoverPreviewPrimary = -1;
        private readonly System.Collections.Generic.List<(int index, Color original)> _hoverPreviewCells = new();
        // 召唤物本体/血条按 _summons 下标索引(事件带 SecondIndex 定位承伤/发起者;死后仍在动画期可见)
        private readonly System.Collections.Generic.Dictionary<int, RectTransform> _summonRectByCore = new();
        // 槽位 → 整格矩形(2026-08-21,拖拽落位命中判定用)。与 _summonRectByCore 的区别有二:
        // 它记的是**整格**而不是字块(手指落点粗),而且**空槽也记** —— 拖召唤字最常见的落点
        // 恰恰是空格,只记有人的格等于把主要用法排除在外。
        private readonly System.Collections.Generic.Dictionary<int, RectTransform> _summonCellByCore = new();
        private readonly System.Collections.Generic.Dictionary<int, (RectTransform fill, UnityEngine.UI.Text label)> _summonBarByCore = new();
        // 召唤物盾条(2026-08-26):与 _summonBarByCore 同款,按**核心槽位**索引
        private readonly System.Collections.Generic.Dictionary<int, (RectTransform fill, UnityEngine.UI.Text label)> _summonShieldBarByCore = new();
        private readonly System.Collections.Generic.Dictionary<int, int> _summonAnimHp = new(); // 出手前血(下标→值);SummonHit 触达按承伤者下标逐记降
        // 出手前盾(2026-08-26,下标→值)。与 _animShield 之于玩家同构:召唤格有了常驻盾条,
        // 一段里连挨两记时不逐记推的话,条会在第一记就跳到整段的终值
        private readonly System.Collections.Generic.Dictionary<int, int> _summonAnimShield = new();
        private readonly System.Collections.Generic.HashSet<int> _dyingEnemies = new(); // 死亡动画进行中的怪:重绘时维持着色,置灰交给死亡节拍(2026-07-25)
        private int _animsInFlight; // 在播的打击动画数;>0 = 锁输入 + 血条画在出手前值(2026-07-25),归零才放行重绘
        private bool Animating => _animsInFlight > 0;

        // 出手前血量:动画期间血条画在此值,每记命中钳向终值(触达才掉血,2026-07-25)。
        private int _animPlayerHp;
        private int _animShield;    // 出手前护盾:与血条同理,敌方一记打来时按吸收量逐记降(2026-07-25)
        private readonly System.Collections.Generic.List<int> _animEnemyHp = new();
        // 血条 fill/label 引用:命中回调据此就地推进,不整屏重绘(重绘会毁掉进行中动画的锚点)
        private (RectTransform fill, UnityEngine.UI.Text label) _playerHpBar;
        private (RectTransform fill, UnityEngine.UI.Text label) _playerShieldBar;
        private readonly System.Collections.Generic.List<(RectTransform fill, UnityEngine.UI.Text label)> _enemyHpBars = new();

        // 行动条(2026-08-17):每个参战单位一条,读各自的 ActionMeter。与血条同款持有
        // fill/label 引用 —— 动画期间不重绘,靠这些引用就地推进(见 SetActionBar)。
        private (RectTransform fill, UnityEngine.UI.Text label) _playerActionBar;
        private readonly System.Collections.Generic.List<(RectTransform fill, UnityEngine.UI.Text label)> _enemyActionBars = new();
        private readonly System.Collections.Generic.Dictionary<int, (RectTransform fill, UnityEngine.UI.Text label)> _summonActionBarByCore = new();

        private BattleEngine Battle => _run.Battle;

        // 交互状态
        private string _selectedChar;   // 当前选中的字/部件
        private int _selectedIndex = -1; // 选中的字库卡位(同字多张时区分是哪张,2026-08-17);部件池选中为 −1
        private bool _targeting;        // 等待点击敌人
        // 治疗选目标(2026-08-22):单体治疗字出字前先点一个友方(玩家或某只存活召唤物)。
        // 与 _targeting 同构,只是点击面对象从敌人换成我方——选中的仍是 _selectedChar,
        // 落点走 Cast 的 allySlot 参数(Targeting.PlayerTarget = 玩家)。
        private bool _allyTargeting;
        // 方向选择态(2026-09-02):水/土 双方向字选中后先问「攻」还是「护」,
        // 选完再进各自的目标态。与 _targeting / _allyTargeting 同构,
        // 只是这一态还没决定要打谁,只决定走哪套效果。
        private bool _directionPicking;
        // 进目标选择态时记住选的是哪个方向(2026-09-02):点敌人/点友方那两条回调
        // 隔了一次用户交互才回来,不带着这个值会退回 attackMode: false。
        private bool _pendingAttackMode;
        // 敌人 + 友方两段选目标(2026-08-26):圭/垚/垒 是「护盾 + 单体伤害」,沐/沝/澡 是
        // 「治疗 + 单体伤害」—— 两个目标都要选。先点敌人,下标暂存在这里,再进 _allyTargeting。
        // 改前 OnEnemyClicked 选完敌人直接 BeginCast,友方那一段根本进不去,治疗/护盾**永远落玩家**。
        // −1 = 这一张不需要敌人目标(纯友方字),BeginCast 照旧传 −1。
        private int _pendingAllyEnemyTarget = -1;
        // 玩家血条区(2026-08-27):拖治疗/加盾字时,松手落在这块上 = 施给玩家自己。
        // 召唤物那边有 _summonCellByCore,玩家没有槽位,所以单记一份。
        private RectTransform _playerAllyRect;
        // 召唤落位(2026-08-20):出召唤字先点位子,攒够只数才真正 Cast。
        // 槽位攒在这里、没调 Cast 之前引擎一无所知 —— 连选途中取消整张字天然回滚。
        private bool _slotPicking;      // 等待点击召唤位
        private string _pendingSummonChar;   // 待落位的字
        private int _pendingSummonTarget = -1;
        private bool _pendingSummonAttackMode;
        private int _pendingSummonLibraryIndex = -1;
        private int _pendingSummonCount;     // 这张字召几只 = 要点几个位子
        private GameObject _modal;      // 当前模态弹窗(同屏仅一个)
        // 单位详情弹窗开着时(_modal 是 UnitSheet 建的那个),Refresh 靠它重新取一份 UnitDetail
        // 整体重建——稿上写着「数值随战斗实时刷新,不暂停」,事件驱动而不是每帧。
        // 返回 null = 那个单位没了(比如召唤物被打死),Refresh 顺手关掉详情而不是抱着空数据崩。
        private System.Func<UnitDetail> _unitSheetSource;
        // 与 UI/UnitSheet.cs 内部 private 的 SheetName 保持一致的字面量——那边不让改、也没有
        // 公开出来,这里只能复述字符串,用来判断当前 _modal 是不是详情弹窗、还是被别的模态
        // (奇遇替换弹窗等)顶掉了。顶掉的情形下没必要也不应该把详情弹窗重新弹到别的模态上面。
        private const string UnitSheetGameObjectName = "UnitSheet";
        private GameObject _rewardModal;// 战利品弹窗:与 _modal 分层,避免提示覆盖选择流程
        private string _message = Strings.T("battle.hint.initial");

        private string _title;          // 关卡标题(顶栏,可选)
        // 局内奇遇能抬高上限(2026-08-04),故以引擎当场值为准;Init 透传的那份只作 Battle 未就绪时的兜底
        private int _playerMaxHp = 50;
        private int PlayerMaxHp => Battle?.MaxHp ?? _playerMaxHp;

        // 容器
        // 四排(2026-08-20):敌方后排 / 敌方前排 / 我方前排 / 我方后排,各 3 格。
        // 排序自上而下,两侧的**前排相邻**、夹着中间那条分隔线 —— 纵深才读得出来。
        private Transform _enemyBackRow;
        private Transform _enemyFrontRow;
        private Transform _summonFrontRow;
        private Transform _summonBackRow;
        private Transform _topLeft, _topRight, _bottomRow;
        private Transform _statusRow;    // 教程提示/奇遇文案(结束回合钮 2026-07-21 已移出)
        private Transform _endTurnRow;   // 结束回合钮:2026-08-20 起在右侧拆合台竖栏的底部
        private Transform _libraryRow;
        private Transform _poolRow;
        private Transform _suggestRow;   // 拆合台:选中详情或空态两行说明(稿 .picked/.empty)
        private Transform _craftRow;     // 拆合台:可合成列表,常驻+可滚动(稿 .craft),2026-08-31 从 _suggestRow 拆出
        private Transform _hintColumn;   // 差字面板(屏幕左侧竖排,平铺列表,2026-08-31 起不再是五行三级目录)
        private Transform _actionRow;
        // 非战斗阶段的宽操作区(2026-08-20):结算 / 奇遇 / 部件超限 / 跑图结束用它。
        // 这些界面此前借的是拆合台的 _actionRow,而拆合台是右侧那条窄竖栏 —— 奇遇的
        // 260 宽选项钮塞不进去,所以给它们留一条横贯中区的带。
        // 它与 _libraryRow **共占同一个槽**(两个铺满的叠放层,见 BuildSkeleton 的 Band),
        // 两者从不在同一阶段绘制(见 Refresh 的 switch)。
        private Transform _centerRow;
        private GameObject _rowDivider;  // 敌我前排之间的墨线:只在战斗阶段现身(2026-08-20)
        private Text _messageLabel;
        private bool _resolvingHint;    // 本次重绘落在动画锁里:底部提示行画「结算中……」而非播报

        private Tutorial _tutorial;      // 新手引导(11.2);null = 不引导
        private GameObject _coachOverlay;         // 当前引导弹层(2026-08-31 改稿,同 _modal 的生命周期套路)
        private TutorialStep _coachShownStep = (TutorialStep)(-1); // 上次为哪一步弹出过;换步才重新弹
        private bool _coachDismissed;             // 当前这步的弹层是否已被玩家点「下一步」关掉去真操作
        private bool _tutorialSkipped;            // 「跳过引导」——此后 DrawTutorialHint 不再自动弹层

        private System.Action _onNewFloor;      // 连战推进到新一场时回调(无尽断点快照,20.6)
        private System.Action _onFloorCleared;  // 战利品取完时回调:本层记账落盘(2026-07-20)
        private System.Action _onExit;       // 中途退出(无尽=挂起);null 时退化为认输
        private System.Action _onProgress;   // 每次玩家行动后落盘(2026-07-27 断点续存)
        private System.Action _onAbandon;    // 弃塔:阵亡待遇半额结算(2026-07-19,与挂起并列选项)
        private System.Action _onExpanded;   // 广告扩容后回调(即时落盘,防挂起丢失)
        private int _lastBattleIndex;
        // 这一场的开场回放播过没有(2026-08-17)。用 BattleIndex 而不是 bool:
        // 换一场战斗就自然重置,不必找地方清标记。−1 = 还没播过任何一场。
        private int _openingPlayedForBattle = -1;
        private RunPhase _lastPhase;    // 上一帧的阶段:用于检测「战利品阶段结束」这一次转换
        private int _pendingRewardIndex = -1;   // 满库替换:已选中待替换入库的奖励下标(3.8.1)
        private int _previewRewardIndex = -1;   // 字奖励预览:首点看简述,再点确认(新手友好)

        public void Init(RecipeGraph graph, RunEngine run, MetaState meta, System.Action<bool> onRunEnded,
            Tutorial tutorial = null, string title = null, int playerMaxHp = 50,
            System.Action onNewFloor = null, System.Action onExit = null, System.Action onProgress = null,
            System.Action onExpanded = null, System.Action onAbandon = null,
            System.Action onFloorCleared = null)
        {
            _graph = graph;
            _run = run;
            _meta = meta;
            _onRunEnded = onRunEnded;
            _onNewFloor = onNewFloor;
            _onFloorCleared = onFloorCleared;
            _onExit = onExit;
            _onProgress = onProgress;
            _onAbandon = onAbandon;
            _onExpanded = onExpanded;
            _lastBattleIndex = run.BattleIndex;
            _lastPhase = run.Phase;
            _tutorial = tutorial;
            _title = title ?? "";
            _playerMaxHp = playerMaxHp;
            BuildSkeleton();
            _juice = gameObject.AddComponent<Juice>();
            _juice.Init((RectTransform)transform);
            Refresh();
        }

        private RectTransform EnemyAnchor(int i) => i >= 0 && i < _enemyRects.Count ? _enemyRects[i] : null;
        private RectTransform SummonAnchor(int coreIndex) => _summonRectByCore.TryGetValue(coreIndex, out var r) ? r : null;
        // 召唤反击飞字用:发起者是谁,飞它自己的字(2026-08-17);别叫 SummonInfo,和 UI 的同名类撞
        private SummonState SummonAt(int i) => i >= 0 && i < Battle.Summons.Count ? Battle.Summons[i] : null;

        /// <summary>第 i 只怪**看起来**是什么属性(2026-08-30):敌方那记攻击的飘字/屏闪按它上色。
        ///
        /// 用 ApparentElement 而不是 Element 是刻意的 —— 伪装怪(通假字)、生僻字没现形之前它是 null,
        /// 飘字就回落中性色。玩家本来就还不知道那是什么属性,提前用真属性上色等于泄题。</summary>
        private Element? EnemyElement(int i) =>
            i >= 0 && i < Battle.Enemies.Count ? Battle.Enemies[i].ApparentElement : null;

        /// <summary>本次结算里死亡的怪(下标取自 LastEvents 的 EnemyDied)。</summary>
        private System.Collections.Generic.List<int> DeathsThisAction()
        {
            var deaths = new System.Collections.Generic.List<int>();
            foreach (var e in Battle.LastEvents)
                if (e.Kind == BattleEventKind.EnemyDied) deaths.Add(e.TargetIndex);
            return deaths;
        }

        /// <summary>播放打击感:登记死亡怪(重绘保持着色)+ 计数在播动画(锁输入、血条画在出手前值)。
        /// 须在 Refresh 之前 BeginAnim,Play 回调 OnAnimDone 归零才放行。</summary>
        private void PlayAnimated(System.Collections.Generic.IReadOnlyList<BattleEvent> events,
            System.Collections.Generic.List<int> deaths)
        {
            _juice.Play(events, EnemyAnchor, SummonAnchor, () => OnAnimDone(deaths), OnImpact, SummonAt,
                EnemyElement);
        }

        /// <summary>一段打击动画开演:计数 +1(锁输入、血条改画出手前值),须在 Refresh 前调用。</summary>
        private void BeginAnim() => _animsInFlight++;

        /// <summary>动画落幕:计数 -1,死亡怪转正式置灰;全部落幕才重绘(放行输入/结算 UI/胜负标语)。</summary>
        private void OnAnimDone(System.Collections.Generic.List<int> deaths)
        {
            _animsInFlight = System.Math.Max(0, _animsInFlight - 1);
            _dyingEnemies.ExceptWith(deaths); // 动画已把它们置灰,后续重绘照常画灰
            if (!Animating) Refresh();         // 期间不重绘:锁输入,血条 fill/label 引用不被毁
        }

        /// <summary>出手前记下玩家/敌人血量:动画期间血条画在此值,每记命中钳向终值(触达才掉血)。</summary>
        private void SnapshotPreHp()
        {
            _animPlayerHp = Battle.PlayerHp;
            _animShield = Battle.PlayerShield;
            _summonAnimHp.Clear();
            _summonAnimShield.Clear();
            for (int i = 0; i < Battle.Summons.Count; i++)
                if (Battle.Summons[i] != null && Battle.Summons[i].Alive)
                {
                    _summonAnimHp[i] = Battle.Summons[i].Hp; // 出手前存活者(下标→血);本回合被打死的仍画得出,旧尸不画
                    _summonAnimShield[i] = Battle.Summons[i].Shield;
                }
            _animEnemyHp.Clear();
            foreach (var e in Battle.Enemies) _animEnemyHp.Add(e.Hp);
        }

        /// <summary>一记命中触达:把对应血条从出手前值推向终值,钳到终值(治护盾双扣/回弹)。</summary>
        private void OnImpact(BattleEvent e)
        {
            switch (e.Kind)
            {
                case BattleEventKind.Damage:
                case BattleEventKind.BurnTick:
                case BattleEventKind.BleedTick:
                case BattleEventKind.Detonate: // 引爆(2026-08-09,灱):Amount 是实打的伤害,同口径推血条——
                                                // 不接这条,血条会一动不动到本段动画收尾才突然跳,是这段注释说的老毛病
                    // TargetIndex < 0 = 玩家自己在烧(2026-08-06,灯花的灼身):走玩家血条,
                    // 与 EnemyAttack 同款推进。PushEnemyHp 对 −1 会直接返回 false,
                    // 不加这条分支血条就一动不动、只有最终重绘才突然掉下去
                    if (e.TargetIndex < 0)
                    {
                        if (_playerHpBar.fill == null) break;
                        int burnBefore = _animPlayerHp;
                        _animPlayerHp = System.Math.Max(Battle.PlayerHp, _animPlayerHp - e.Amount);
                        SetHpBar(_playerHpBar, _animPlayerHp, PlayerMaxHp);
                        ChipDamage(_playerHpBar, burnBefore, _animPlayerHp, PlayerMaxHp);
                        break;
                    }
                    // 挨这一记的形象抖起来:主体抖、墨丝甩尾、眼睛瞪大(MobView 三层各自不同步)
                    if (e.TargetIndex < _enemyMobs.Count && _enemyMobs[e.TargetIndex] != null)
                        _enemyMobs[e.TargetIndex].PlayHit();
                    // Amount 分账与玩家侧 EnemyAttack 同口径:Absorbed 走盾条,余量才掉血
                    PushEnemyHp(e.TargetIndex, -(e.Amount - e.Absorbed));
                    break;
                case BattleEventKind.ImmunityBlocked: // 完全挡下:血条护盾条都不动,表达交给 Juice 的飘字
                    break;
                // 打空:血条护盾条都不动,表达交给 Juice 的飘字。防御性占位——目前 Juice 的
                // Missed 分支没调 onImpact,这条走不到,但显式空 case 比漏判更稳(与 ImmunityBlocked 同构)
                case BattleEventKind.Missed:
                    break;
                case BattleEventKind.EnemyAttack: // Amount 分账:Absorbed 走护盾条,余量才掉血,各自钳到终值
                    _animShield = System.Math.Max(Battle.PlayerShield, _animShield - e.Absorbed);
                    SetShieldBar(_animShield);
                    if (_playerHpBar.fill == null) break;
                    int hitBefore = _animPlayerHp;
                    _animPlayerHp = System.Math.Max(Battle.PlayerHp, _animPlayerHp - (e.Amount - e.Absorbed));
                    SetHpBar(_playerHpBar, _animPlayerHp, PlayerMaxHp);
                    ChipDamage(_playerHpBar, hitBefore, _animPlayerHp, PlayerMaxHp);
                    break;
                case BattleEventKind.Shield: // 筑盾触达才涨,与掉盾同一条推进(不整屏重绘)
                    // TargetIndex ≥0 = 盾加在召唤物身上(2026-08-26),推那一格的盾条;−1 才是玩家
                    if (e.TargetIndex >= 0)
                    {
                        int shieldSlot = e.TargetIndex;
                        if (shieldSlot >= Battle.Summons.Count || Battle.Summons[shieldSlot] == null
                            || !_summonAnimShield.ContainsKey(shieldSlot)
                            || !_summonShieldBarByCore.TryGetValue(shieldSlot, out var ssb)
                            || ssb.fill == null) break;
                        _summonAnimShield[shieldSlot] = System.Math.Min(
                            Battle.Summons[shieldSlot].Shield, _summonAnimShield[shieldSlot] + e.Amount);
                        SetShieldBarOn(ssb, _summonAnimShield[shieldSlot]);
                        _juice.BarPulse(ssb.fill, Theme.Jade, Element.Earth); // 土:盾条起势
                        break;
                    }
                    _animShield = System.Math.Min(Battle.PlayerShield, _animShield + e.Amount);
                    SetShieldBar(_animShield);
                    _juice.BarPulse(_playerShieldBar.fill, Theme.Jade, Element.Earth); // 土:盾条起势
                    break;
                case BattleEventKind.Heal: // 水系治疗:与群攻同一记里触达,血条即时上推(此前只在末次重绘才涨)
                    // SecondIndex ≥0 = 治疗落在召唤物身上,推那只召唤物的血条(镜像 SummonHit,只是方向向上)
                    if (e.SecondIndex >= 0)
                    {
                        int hsi = e.SecondIndex;
                        if (hsi >= Battle.Summons.Count || Battle.Summons[hsi] == null
                            || !_summonAnimHp.ContainsKey(hsi)
                            || !_summonBarByCore.TryGetValue(hsi, out var hbar) || hbar.fill == null) break;
                        _summonAnimHp[hsi] = System.Math.Min(Battle.Summons[hsi].Hp, _summonAnimHp[hsi] + e.Amount);
                        SetHpBar(hbar, _summonAnimHp[hsi], Battle.Summons[hsi].MaxHp);
                        _juice.BarPulse(hbar.fill, Theme.SplitBlue, Element.Water); // 水:血条起势
                        break;
                    }
                    if (_playerHpBar.fill == null) break;
                    _animPlayerHp = System.Math.Min(Battle.PlayerHp, _animPlayerHp + e.Amount);
                    SetHpBar(_playerHpBar, _animPlayerHp, PlayerMaxHp);
                    _juice.BarPulse(_playerHpBar.fill, Theme.SplitBlue, Element.Water); // 水:血条起势
                    break;
                case BattleEventKind.EnemySplit: // 分裂:原体当场减半(Amount = 减半后的血),动画血量直接按过去
                    SetEnemyHp(e.TargetIndex, e.Amount);
                    break;
                case BattleEventKind.Regrow: // 缺笔妖补全:同一条血条往**上**推
                    if (!PushEnemyHp(e.TargetIndex, e.Amount)) break;
                    _juice.BarPulse(_enemyHpBars[e.TargetIndex].fill, Theme.Jade);
                    if (e.TargetIndex < _enemyMobs.Count && _enemyMobs[e.TargetIndex] != null)
                        _enemyMobs[e.TargetIndex].SetStateAmount(Mathf.Clamp01(e.SecondIndex / 3f)); // 状态层跟着补全长
                    break;
                case BattleEventKind.ShieldBroken: // 倾覆技能把剩余护盾整个掀掉:直接推到 0,不等最终重绘才归零
                    _animShield = 0;
                    SetShieldBar(_animShield);
                    break;
                case BattleEventKind.SummonHit: // 敌人打召唤:按承伤者下标(SecondIndex)血条逐记降,钳到其终值(死了钳到 0)
                    int si = e.SecondIndex;
                    if (si < 0 || si >= Battle.Summons.Count || Battle.Summons[si] == null
                        || !_summonAnimHp.ContainsKey(si)
                        || !_summonBarByCore.TryGetValue(si, out var sbar) || sbar.fill == null) break;
                    // Amount 分账与玩家侧 EnemyAttack 同口径:Absorbed 走盾条,余量才掉血
                    if (e.Absorbed > 0 && _summonAnimShield.ContainsKey(si)
                        && _summonShieldBarByCore.TryGetValue(si, out var hitShield))
                    {
                        _summonAnimShield[si] = System.Math.Max(
                            Battle.Summons[si].Shield, _summonAnimShield[si] - e.Absorbed);
                        SetShieldBarOn(hitShield, _summonAnimShield[si]);
                    }
                    int summonBefore = _summonAnimHp[si];
                    _summonAnimHp[si] = System.Math.Max(Battle.Summons[si].Hp,
                        _summonAnimHp[si] - (e.Amount - e.Absorbed));
                    SetHpBar(sbar, _summonAnimHp[si], Battle.Summons[si].MaxHp);
                    ChipDamage(sbar, summonBefore, _summonAnimHp[si], Battle.Summons[si].MaxHp);
                    break;
                case BattleEventKind.SummonBurnTick: // 召唤物自身灼烧(2026-08-26):TargetIndex 就是槽位
                    int bsi = e.TargetIndex;
                    if (bsi < 0 || bsi >= Battle.Summons.Count || Battle.Summons[bsi] == null
                        || !_summonAnimHp.ContainsKey(bsi)
                        || !_summonBarByCore.TryGetValue(bsi, out var bbar) || bbar.fill == null) break;
                    int burntBefore = _summonAnimHp[bsi];
                    _summonAnimHp[bsi] = System.Math.Max(Battle.Summons[bsi].Hp, _summonAnimHp[bsi] - e.Amount);
                    SetHpBar(bbar, _summonAnimHp[bsi], Battle.Summons[bsi].MaxHp);
                    ChipDamage(bbar, burntBefore, _summonAnimHp[bsi], Battle.Summons[bsi].MaxHp);
                    break;
            }
        }

        /// <summary>把某只怪的动画血量推进 delta(负 = 挨打,正 = 回血),只钳 [0, 当前上限]。
        /// **不能钳到 enemy.Hp** —— 那是整段结算跑完的终值。同一段里若既挨打又回血
        /// (缺笔妖:召唤物打完,它在回合收尾补),终值就成了一个被抬高的地板:
        /// 最后几记伤害整个被吃掉,血条提前回满;补全把血拉满时更离谱 —— 挨一记打血条反而往上跳
        /// (12 血挨 4 记 ×2 再补 2:逐记本该 10/8/6/4,实际演成 10/8/6/**6**;
        /// 补全回满那回合则是 6 挨一记直接演成 **12**。2026-07-30 实测)。
        /// 事件金额是名义值(会溢出目标剩余血),所以钳 0 与上限即可,溢出部分自然吃掉;
        /// 与模型的任何漂移都由动画落幕后的 Refresh 兜底。返回是否真的推动了(供调用方接后续表现)。</summary>
        private bool PushEnemyHp(int index, int delta) =>
            index >= 0 && index < _animEnemyHp.Count && SetEnemyHp(index, _animEnemyHp[index] + delta);

        /// <summary>直接把动画血量按到某个值。分裂用得着:原体在模型里当场减半却**不发伤害事件**,
        /// 纯累加跟不上,血条会一直停在减半前(去掉终值钳位后这条才浮出来)。</summary>
        private bool SetEnemyHp(int index, int hp)
        {
            if (index < 0 || index >= _enemyHpBars.Count
                || index >= _animEnemyHp.Count || index >= Battle.Enemies.Count) return false;
            if (_enemyHpBars[index].fill == null) return false;
            var enemy = Battle.Enemies[index];
            int before = _animEnemyHp[index];
            _animEnemyHp[index] = Mathf.Clamp(hp, 0, enemy.MaxHp);
            SetHpBar(_enemyHpBars[index], _animEnemyHp[index], enemy.MaxHp);
            ChipDamage(_enemyHpBars[index], before, _animEnemyHp[index], enemy.MaxHp);
            return true;
        }

        /// <summary>掉血残影:条上那一截浅色尾巴(2026-08-30)。只在**掉**的方向留,回血/补全不留 ——
        /// 涨的那一头已经有 BarPulse 的辉光在表达了。血条本身是瞬时按到新值的,
        /// 没有这截尾巴,掉 3 点和掉 30 点在画面上都只是长度变了一下。</summary>
        private void ChipDamage((RectTransform fill, UnityEngine.UI.Text label) bar, int from, int to, int maxHp)
        {
            if (bar.fill == null || maxHp <= 0 || to >= from) return;
            _juice.ChipDamage(bar.fill, from / (float)maxHp, to / (float)maxHp);
        }

        /// <summary>血条 + 血值叠加其上(带深色描边保对比度);返回 fill/label 供命中回调就地推进。</summary>
        private (RectTransform fill, UnityEngine.UI.Text label) HpBar(Transform parent, int hp, int maxHp, Vector2 size)
        {
            var bar = Ui.Bar(parent, hp / (float)maxHp, Theme.Cinnabar, size);
            var fill = (RectTransform)bar.transform.Find("Fill");
            var label = Ui.ThemedLabel(bar.transform, $"{hp}/{maxHp}", Mathf.Clamp((int)(size.y * 0.7f), 10, 13),
                Color.white, Theme.TitleFont);
            Ui.Stretch(label.rectTransform);
            var outline = label.gameObject.AddComponent<Outline>(); // 深色描边:浅底/满色底都读得清
            outline.effectColor = Theme.Ink;
            outline.effectDistance = new Vector2(1.2f, 1.2f);
            return (fill, label);
        }

        /// <summary>行动条(2026-08-17):meter / Threshold 的进度 + 百分比叠字。
        /// 2026-08-31 改口径:.foe/.ally/.me 三种单位的行动条稿上**同色**藏青
        /// (Theme.InkSoft = #3D4E69,一字不差)——此前这里错写成赭金,与护盾条撞色
        /// (根因见 DrawEnemies 里敌人行动条那处的修复记录,虽然敌人早已改直调 Ui.Bar,
        /// 不再经这个 helper,但颜色错误的根源就是这里)。
        /// >80% 转稿的 .soon 态:**我方是绿**(Theme.DoneGreen ≈ 稿 #2E7D46,差值 (5,9,0)
        /// 可忽略不计,不为此新增 Theme 常量)——敌方的朱砂 soon 态在敌人自己直调 Ui.Bar
        /// 那条裸条上,与这个共享 helper 是两码事,这里不能照抄那份朱砂。
        /// 与 <see cref="HpBar"/> 同款返回 fill/label,供动画期间就地推进。</summary>
        private (RectTransform fill, UnityEngine.UI.Text label) ActionBar(
            Transform parent, int meter, Vector2 size, int fontSize)
        {
            float frac = Mathf.Clamp01(meter / (float)TurnScheduler.Threshold);
            bool soon = frac > 0.8f;
            var bar = Ui.Bar(parent, frac, soon ? Theme.DoneGreen : Theme.InkSoft, size);
            var fill = (RectTransform)bar.transform.Find("Fill");
            var label = Ui.ThemedLabel(bar.transform, $"{Mathf.RoundToInt(frac * 100)}%",
                fontSize, Color.white, Theme.TitleFont);
            Ui.Stretch(label.rectTransform);
            var outline = label.gameObject.AddComponent<Outline>(); // 与血条同款描边,保对比度
            outline.effectColor = Theme.Ink;
            outline.effectDistance = new Vector2(1.2f, 1.2f);
            return (fill, label);
        }

        /// <summary>速度 100 的单位从 0% 攒到 100% 所需毫秒(2026-08-17)。
        /// 一次推进的条动画时长 = LastAdvanceTicks × 本值,所以速度 200 的走一半时间 ——
        /// 「与速度挂钩」就落在这一条上。
        ///
        /// ⚠ 这直接决定战斗节奏:每个行动者出手前都要等这么久,一轮五个单位就是 2.5 秒。
        /// 嫌拖就改这一个数。</summary>
        private const float ActionBarBaseMs = 500f;

        /// <summary>全场计量器快照(玩家 −1,召唤物与敌人按下标)。推进前拍一次、推进后拍一次,
        /// 条在两者之间插值 —— 这是把 Core 的离散跳跃反演成匀速流动的全部手法。</summary>
        private (int player, int[] summons, int[] enemies) MeterSnapshot()
        {
            var summons = new int[Battle.Summons.Count];
            for (int i = 0; i < summons.Length; i++) summons[i] = Battle.Summons[i]?.ActionMeter ?? 0;
            var enemies = new int[Battle.Enemies.Count];
            for (int i = 0; i < enemies.Length; i++) enemies[i] = Battle.Enemies[i].ActionMeter;
            return (Battle.PlayerActionMeter, summons, enemies);
        }

        /// <summary>把全场的条从 pre 匀速推到 post。**行动者例外**:它涨到 100% 就停住,
        /// 不在这里回落 —— 让玩家看清「是它满了才动」,回落交给动作播完之后
        /// (<see cref="DropActingBar"/>)。
        ///
        /// ⚠ 用 unscaledDeltaTime:Juice 会改 Time.timeScale 做打击顿帧,
        /// 条跟着一起卡是错的(它表达的是时间流逝本身)。</summary>
        private System.Collections.IEnumerator FillActionBars(
            (int player, int[] summons, int[] enemies) pre,
            (int player, int[] summons, int[] enemies) post,
            ActorRef actor, int ticks)
        {
            if (ticks <= 0) yield break;   // 已有人满格,无需推进

            float duration = ticks * ActionBarBaseMs / 1000f;
            // 行动者的目标是满格(它的 post 已经扣过 Threshold,直接插到 post 会看到条倒退)。
            // ⚠ 任何让行动者以非满格状态出手的机制(打断、抢拍)都要改这里。
            float actingTarget = TurnScheduler.Threshold;

            void Apply(float k)
            {
                SetActionBar(_playerActionBar, actor.Kind == ActorKind.Player
                    ? Mathf.Lerp(pre.player, actingTarget, k)
                    : Mathf.Lerp(pre.player, post.player, k));
                for (int i = 0; i < pre.summons.Length && i < post.summons.Length; i++)
                    if (_summonActionBarByCore.TryGetValue(i, out var bar))
                        SetActionBar(bar, actor.Kind == ActorKind.Summon && actor.Index == i
                            ? Mathf.Lerp(pre.summons[i], actingTarget, k)
                            : Mathf.Lerp(pre.summons[i], post.summons[i], k));
                for (int i = 0; i < pre.enemies.Length && i < post.enemies.Length
                        && i < _enemyActionBars.Count; i++)
                    SetActionBar(_enemyActionBars[i], actor.Kind == ActorKind.Enemy && actor.Index == i
                        ? Mathf.Lerp(pre.enemies[i], actingTarget, k)
                        : Mathf.Lerp(pre.enemies[i], post.enemies[i], k));
            }

            for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
            {
                Apply(Mathf.Clamp01(t / duration));
                yield return null;
            }
            // ⚠ 循环条件是 t < duration,最后一帧的 k 恒 < 1(60fps/0.5s 时约 0.967)——
            // 不补这一次,行动者会停在「97%」而不是满格,「是它满了才动」这个观感就没了。
            Apply(1f);
        }

        /// <summary>行动者的条回落到余额(动作播完才调)。余额不一定是 0 ——
        /// 攒过头的部分带到下一拍,这是 CTB 的既有语义。</summary>
        private void DropActingBar(ActorRef actor, (int player, int[] summons, int[] enemies) post)
        {
            switch (actor.Kind)
            {
                case ActorKind.Player:
                    SetActionBar(_playerActionBar, post.player);
                    break;
                case ActorKind.Summon:
                    if (actor.Index < post.summons.Length
                        && _summonActionBarByCore.TryGetValue(actor.Index, out var summonBar))
                        SetActionBar(summonBar, post.summons[actor.Index]);
                    break;
                case ActorKind.Enemy:
                    if (actor.Index < post.enemies.Length && actor.Index < _enemyActionBars.Count)
                        SetActionBar(_enemyActionBars[actor.Index], post.enemies[actor.Index]);
                    break;
            }
        }

        /// <summary>行动条就地推进(条未画出时静默跳过)。meter 取 float 是因为动画要在
        /// 整数拍之间插值 —— Core 侧全程整数,浮点只活在表现层(spec 全局约束)。</summary>
        private static void SetActionBar((RectTransform fill, UnityEngine.UI.Text label) bar, float meter)
        {
            float frac = Mathf.Clamp01(meter / TurnScheduler.Threshold);
            if (bar.fill != null)
                Ui.Anchor(bar.fill, Vector2.zero, new Vector2(frac, 1), Vector2.zero, Vector2.zero);
            if (bar.label != null) bar.label.text = $"{Mathf.RoundToInt(frac * 100)}%";
        }

        // 护盾条满格基准值(护盾本身无上限概念,取一个常见量级当刻度)。
        // 2026-08-26:30 → 200。30 是 2026-08-12 全表数值 ×10 之前定的,现在最小的一张
        // 垒 就有 50,任何一次加盾都直接顶满,条永远是满的、等于没有条。
        // 200 之下:垒 50 = 1/4、圭 200 = 满、㙓 450 夹到满。
        private const float ShieldBarFull = 200f;

        /// <summary>玩家护盾条就地推进(2026-08-26 起条恒在,不会再有「没画出来」的情形)。</summary>
        private void SetShieldBar(int shield) => SetShieldBarOn(_playerShieldBar, shield);

        private static void SetShieldBarOn((RectTransform fill, UnityEngine.UI.Text label) bar, int shield)
        {
            if (bar.fill != null)
                Ui.Anchor(bar.fill, Vector2.zero, new Vector2(Mathf.Clamp01(shield / ShieldBarFull), 1),
                    Vector2.zero, Vector2.zero);
            if (bar.label != null) bar.label.text = shield.ToString();
        }

        /// <summary>召唤物身上挂着几条增益(2026-08-28)。按**条数**数而不是按 Magnitude 求和:
        /// 那几条的单位互不相同(护甲是点数、暴击是百分点、免疫是次数),加在一起没有意义。
        /// 走 Polarity 而不是列举 StatusKind —— 将来再让哪条增益能挂给召唤物,这里不用改。</summary>
        private static int CountBuffs(SummonState summon)
        {
            int count = 0;
            var all = summon.Statuses.All;
            for (int i = 0; i < all.Count; i++)
                if (all[i].Polarity == StatusPolarity.Buff && all[i].Magnitude > 0) count++;
            return count;
        }

        private static void SetHpBar((RectTransform fill, UnityEngine.UI.Text label) bar, int hp, int maxHp)
        {
            if (bar.fill != null)
                Ui.Anchor(bar.fill, Vector2.zero, new Vector2(Mathf.Clamp01(hp / (float)maxHp), 1), Vector2.zero, Vector2.zero);
            if (bar.label != null) bar.label.text = $"{hp}/{maxHp}";
        }

        /// <summary>版面骨架:顶栏 + 三栏(左窄栏 / 中区 / 拆合台),照稿的 flex 直译成布局组。
        ///
        /// 三条纵向规则全在布局组的开关上,别用锚点去凑:
        ///   ① 稿 .erow { flex: none } —— 战场四排各自锁死高度(见 <see cref="MakeFieldRow"/>);
        ///   ② 稿 .divider { margin: auto 0 } —— field 的富余全堆到分隔线上下,两侧各一半;
        ///   ③ 稿 .mid { flex: 1 } —— 中区吃掉左右两栏之外的全部横向。</summary>
        private void BuildSkeleton()
        {
            var root = (RectTransform)transform;
            Ui.Stretch(root);

            // 空白处点击 = 取消选中(2026-07-21):全屏透明层,最先建 → 在最底层,
            // 内容层带 Image 的元素会先拦下射线,只有真正的空白落到这里
            var backdrop = Ui.Panel(transform, "Backdrop");
            Ui.Stretch((RectTransform)backdrop.transform);
            var backdropImage = backdrop.AddComponent<Image>();
            backdropImage.color = new Color(0, 0, 0, 0);
            var backdropButton = backdrop.AddComponent<Button>();
            backdropButton.transition = Selectable.Transition.None;
            backdropButton.targetGraphic = backdropImage;
            backdropButton.onClick.AddListener(() =>
            {
                if (_selectedChar != null || _targeting || _slotPicking || _allyTargeting) CancelSelection();
            });

            // 版面主干挂在这一层,**不挂根节点**:弹窗、过关横幅、飘字、上面那块 Backdrop
            // 全都挂根节点铺满整屏,根节点一旦有布局组就会把它们一件件排成行。
            //
            // 这一层同时是稿 .safe 的落点(与 MapView 的 content 对应):左右各内缩 59pt、
            // 底部内缩 21pt。SideInset/BottomInset 已由 SafeAreaFitter 在真机上让出大半,
            // MissingInset() 只补差额 —— 编辑器/无刘海机上差额就是全部,真机上接近 0。
            var frame = Ui.VStack(transform, "Frame", 0);
            var (padSide, padBottom) = SafeArea.MissingInset();
            Ui.Anchor((RectTransform)frame.transform, Vector2.zero, Vector2.one,
                new Vector2(padSide, padBottom), new Vector2(-padSide, 0));
            var frameLayout = frame.GetComponent<VerticalLayoutGroup>();
            frameLayout.childForceExpandWidth = true;   // 顶栏与三栏都通栏
            frameLayout.childAlignment = TextAnchor.UpperCenter;

            // 顶栏两段:关卡名·层数·场次(左) | 墨锭·回合·退出(右)。
            // 2026-08-21 曾是三段(中段放战斗提示);2026-08-27 用户拍板把提示挪到屏幕最底部,
            // 中段腾空 —— 左右两段的锚点没动,顶栏该显示什么一样不少。
            var topBar = Ui.Panel(frame.transform, "TopBar");
            Ui.Sized(topBar, height: TopBarH);
            _topLeft = Ui.Row(topBar.transform, "Left", 10).transform;
            Ui.Anchor((RectTransform)_topLeft, new Vector2(0, 0), new Vector2(0.26f, 1), Vector2.zero, Vector2.zero);
            // 提示行:屏幕**最底部**通栏(2026-08-27 用户拍板)。与 _statusRow 同一条底边
            // ——那一排眼下只剩教程指引,而教程将来要整体迁走。「结算中……」已经并进本行
            // (见 Refresh 末尾),所以这两者不会叠字。
            // 挂在 transform 下而不是 _statusRow 里:_statusRow 每次 Refresh 都被 Ui.Clear 清空,
            // 而这个 label 是常驻对象,靠 Refresh 末尾改 text 更新。
            var messageGo = Ui.Panel(transform, "Message");
            Ui.Anchor((RectTransform)messageGo.transform, new Vector2(0.02f, 0.010f), new Vector2(0.98f, 0.053f), Vector2.zero, Vector2.zero);
            _messageLabel = Ui.ThemedLabel(messageGo.transform, "", 19, Theme.TextDim);
            Ui.Stretch(_messageLabel.rectTransform);
            _topRight = Ui.Row(topBar.transform, "Right", 14).transform;
            Ui.Anchor((RectTransform)_topRight, new Vector2(0.70f, 0), new Vector2(1, 1), Vector2.zero, Vector2.zero);

            // 稿 .arena:左窄栏定宽、中区吃余量、右栏定宽,三栏都铺满顶栏以下的整条带。
            var arena = Ui.Row(frame.transform, "Arena", ArenaGap);
            var arenaLayout = arena.GetComponent<HorizontalLayoutGroup>();
            arenaLayout.childForceExpandHeight = true;
            arenaLayout.childAlignment = TextAnchor.UpperLeft;
            Ui.Sized(arena, flexHeight: 1f);

            // ---- 左窄栏(稿 .rail) ----
            var rail = Ui.VStack(arena.transform, "Rail", RailGap);
            rail.GetComponent<VerticalLayoutGroup>().childForceExpandWidth = true;
            // minWidth 与 preferredWidth 同值 = 稿的 flex: none。只写 preferredWidth 不够:
            // 中区内容一旦宽过余量,布局组会把**所有**子物体按 min…preferred 等比压回去,
            // 左右两栏跟着一起缩 —— 那就不是三栏定宽了。右栏同理。
            Ui.Sized(rail, width: RailW).minWidth = RailW;

            // 五行速查常驻(2026-07-22;2026-07-29 改为直接摆环图,不再点开弹窗)。
            // 相生环图 2026-08-31 拍板整个撤掉:战斗中要实时查的是「我这张字克不克它」,
            // 相生 ×3 由配方静态决定,属牌面信息(长按详情弹窗已经显示)。稿上左栏也只有
            // 相克这一张。WuxingChart.Mount 的 sheng:true 分支保留不删——CollectionView /
            // CharInfo 之类的图鉴页面仍可能要用它,删的只是这一个调用点。
            WuxingChart.Mount(rail.transform, sheng: false);

            // 差字面板(稿 .missing):平铺列表,吃掉左栏剩下的高度,内容顶对齐(2026-08-31
            // 从五行三级目录改稿——原来的 MiddleCenter 是给「大多数时候只有标题+几个桶按钮」
            // 的收起态凑数的,现在常驻显示若干行「字 缺 N」,顶对齐才是列表该有的样子)。
            var hintGo = Ui.VStack(rail.transform, "HintPanel", 4);
            hintGo.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.UpperCenter;
            Ui.Sized(hintGo, flexHeight: 1f);
            _hintColumn = hintGo.transform;

            // ---- 中区(稿 .mid) ----
            var mid = Ui.VStack(arena.transform, "Mid", MidGap);
            var midLayout = mid.GetComponent<VerticalLayoutGroup>();
            midLayout.childForceExpandWidth = true;   // 每一行都铺满中区宽度
            midLayout.childAlignment = TextAnchor.UpperCenter;
            Ui.Sized(mid, flexWidth: 1f);

            // 中区被左右两栏夹到 1260 逻辑单位(稿 602pt),而玩家条 / 字库带 / 部件池
            // 三行进一步收窄到 FieldContentWidth = 1223 并居中(见该常量的说明)。
            // 手牌行满员时**装不下**:12 张牌 96 + 13 个间距 8 + 计数标题 96 + 广告位 88
            // = 1440,溢出约 217(18%)。HorizontalLayoutGroup 会等比压窄每格(既有行为,
            // 与旧版面同),压扁而不是溢出 —— 压后牌约 82 单位,仍比旧版的 68 大两成。
            // 别为这一成八改牌宽:稿上的 46×56pt 是量出来的,改它会让整屏比例跟稿漂开。
            // (2026-09-01 收窄对齐前是溢出 10%、压后 87;标题从竖排单字改横排定宽
            //  也吃掉了一部分,两笔加起来就是从 87 到 82。)
            //
            // 收窄居中槽:Mid 的 childForceExpandWidth 会把直接子物体一律撑满 1260,所以
            // 「收窄再居中」只能靠一层通栏的槽 —— 槽照旧铺满 1260,真正那一件建在槽里、
            // 自己按 FieldContentWidth 定宽,由槽的 MiddleCenter 居中。槽不设 LayoutElement:
            // 高度跟着里面那件的 preferredHeight 走,部件池那种「高度由内容撑」的行才不会
            // 被钉死成某个数。
            GameObject NarrowSlot(string name) => Ui.Row(mid.transform, name + "Slot");

            // 战场四排(稿 .field):敌方贴顶、我方贴底,富余的纵向全部堆在分隔线两侧。
            var field = Ui.VStack(mid.transform, "Field", FieldGap);
            var fieldLayout = field.GetComponent<VerticalLayoutGroup>();
            fieldLayout.childAlignment = TextAnchor.UpperCenter;
            // 真正实现「四排不互相挤」的是这一条:布局组的 childForceExpand 会把每个子物体的
            // flexible 强抬到 1,那样四排会各分一份富余,分隔线两侧的 Spacer 也就白设了。
            fieldLayout.childForceExpandHeight = false;
            // Field 是 Mid 里**唯一** flexibleHeight = 1 的行 —— 换成布局组之后,任何一行
            // 内容变高,扣的都是 Field 的份额,不再是各区各自写死的比例。两个已知会跳的场景:
            //   ① 教程提示出现时,_statusRow 多塞一个 26 号 Label(约高 31),战场就少 31;
            //   ② 部件池空↔非空(高度 0 ↔ 约 56)会让分隔线、敌我间距跟着跳一下。
            // 换布局组之前这两件事都不可能发生(每一区都是写死的 y 比例)——不是本次改动的
            // 错误,是切换布局方式的必然结果,只是过去没人把这笔账记下来。
            //
            // 当前纵向预算口径(供改行高前先算一遍):Field 的高 = Mid 减去 PlayerBar(84)/
            // Band(117)/ Pool / Status,后两者由内容撑。四排内容目前合计约 524,余量薄,
            // 改任何一行的高度都要重新过一遍这笔账,别凭感觉调。
            Ui.Sized(field, flexHeight: 1f);

            // 四排(2026-08-20):敌方后排 / 敌方前排 / 我方前排 / 我方后排。
            // 排序自上而下,两侧的**前排相邻**、夹着中间那条分隔线 —— 纵深才读得出来。
            _enemyBackRow = MakeFieldRow(field.transform, "EnemiesBack");
            _enemyFrontRow = MakeFieldRow(field.transform, "EnemiesFront");

            // 稿:.divider { margin: auto 0 } —— field 剩下的纵向全部堆到这一道的上下,
            // 两侧各留一半。用两个 flexibleHeight = 1 的空 Spacer 夹住分隔线来表达。
            // 富余越多,敌我离得越开。
            Ui.Sized(Ui.Panel(field.transform, "SpacerTop"), flexHeight: 1f);
            // 敌我前排之间的分隔线:两侧「前排」贴着它,越远离它的排越靠后。
            // 线只占 74% 宽(稿 .divider 定义了两次,86% 那份被 74%/.3 那份用同特异度、
            // 排在后面的规则覆盖,浏览器实际渲染的是 74%/.3 —— 稿已去重,详见 scenes/README.md
            // 或 Battle.dc.html 的 .divider 规则),所以外面套一个通栏的槽,线在槽里按比例居中 ——
            // 槽是布局组排的那一件,线是槽里锚出来的,SetActive 仍然切在线本身上。
            var dividerSlot = Ui.Panel(field.transform, "DividerSlot");
            Ui.Sized(dividerSlot, height: DividerH, flexWidth: 1f);
            _rowDivider = Ui.Panel(dividerSlot.transform, "RowDivider");
            var dividerImage = _rowDivider.AddComponent<Image>();
            dividerImage.color = new Color(Theme.InkSoft.r, Theme.InkSoft.g, Theme.InkSoft.b, 0.3f);
            // raycastTarget = false —— 它只是一条线,不能拦掉空白点击(那是取消选中用的)
            dividerImage.raycastTarget = false;
            Ui.Anchor((RectTransform)_rowDivider.transform,
                new Vector2(0.13f, 0f), new Vector2(0.87f, 1f), Vector2.zero, Vector2.zero);
            Ui.Sized(Ui.Panel(field.transform, "SpacerBottom"), flexHeight: 1f);

            _summonFrontRow = MakeFieldRow(field.transform, "SummonsFront");
            _summonBackRow = MakeFieldRow(field.transform, "SummonsBack");

            // 玩家条(稿 .me):定高,不跟着 field 伸缩
            var bottomGo = Ui.Row(NarrowSlot("PlayerStats").transform, "PlayerStats");
            Ui.Sized(bottomGo, width: FieldContentWidth, height: PlayerBarH);
            _bottomRow = bottomGo.transform;
            // 执笔人详情入口(2026-09-01,单位详情轮二 Task 5):挂在 _bottomRow 自己身上,
            // 只挂一次——DrawPlayerStats 每次 Refresh 只 Ui.Clear 它的子物件,不动它本身
            // (与 Ui.Clear 只删子物件、不碰父物件同一条,见方法说明)。图透明只为接点击,
            // 视觉不变;选目标态下 AttachAllyTargetPicker 会把选中覆盖层挂成它的子物件,
            // 子物件天然盖在父物件的 Graphic 之上,详见 OnPlayerClicked 的方法注释。
            var bottomImage = bottomGo.AddComponent<Image>();
            bottomImage.color = Color.clear;
            var bottomButton = bottomGo.AddComponent<Button>();
            bottomButton.transition = Selectable.Transition.None;
            bottomButton.targetGraphic = bottomImage;
            bottomButton.onClick.AddListener(OnPlayerClicked);

            // 字库行与非战斗阶段的宽操作区**共占同一条带、按阶段只画其一**:字库只在
            // 战斗回合内/战利品/复活补给三个阶段画,而这条带的四个消费方(结算/奇遇/
            // 部件超限/跑图结束)与那三个阶段互斥。做成上下两行会让这条带占双倍高度,
            // 把战场四排挤扁 —— 所以是两个铺满同一个槽的叠放层,各自 Ui.Clear / 绘制。
            var band = Ui.Panel(NarrowSlot("Band").transform, "Band");
            Ui.Sized(band, width: FieldContentWidth, height: HandBandH);

            // 字库行左对齐(2026-09-01 用户拍板):稿 .hand 是 justify-content:center,
            // 但计数标题在行首 —— 整组居中会让标题的 x 随牌数左右漂,与下面同样带标题的
            // 部件池行对不齐,「竖着看」正是别扭在这里。改左对齐后两行的标题共用一个 x。
            var libraryGo = Ui.Row(band.transform, "Library");
            libraryGo.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            Ui.Stretch((RectTransform)libraryGo.transform);
            _libraryRow = libraryGo.transform;

            var centerGo = Ui.Row(band.transform, "Center");
            Ui.Stretch((RectTransform)centerGo.transform);
            _centerRow = centerGo.transform;

            // 部件池与底部提示行:高度由内容撑(Mid 的 childForceExpandHeight 已关,
            // 它们不会去分 field 的富余)
            var poolGo = Ui.Row(NarrowSlot("Pool").transform, "Pool");
            Ui.Sized(poolGo, width: FieldContentWidth);
            poolGo.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            _poolRow = poolGo.transform;
            _statusRow = Ui.Row(mid.transform, "Status").transform;

            // ---- 右栏:拆合台(稿 .bench) ----
            // 薄宣纸卡(半透,融层段染色);2026-08-20 从底部横卡改为右侧竖栏。
            var workbenchCard = Ui.CardPanel(arena.transform, "Workbench", Theme.PaperCard, 20);
            Ui.Sized(workbenchCard.gameObject, width: BenchW).minWidth = BenchW;
            var workbenchStack = Ui.VStack(workbenchCard.transform, "Stack", BenchGap);
            Ui.Stretch((RectTransform)workbenchStack.transform);
            var workbenchLayout = workbenchStack.GetComponent<VerticalLayoutGroup>();
            workbenchLayout.childForceExpandWidth = true;
            // 稿 .bench { padding: 7px } —— 内容原先直接贴着卡片边,这里补上内边距。
            workbenchLayout.padding = new RectOffset((int)BenchPad, (int)BenchPad, (int)BenchPad, (int)BenchPad);
            Ui.ThemedLabel(workbenchStack.transform, Strings.T("battle.label.workbench_title"), 13, Theme.TextDim, Theme.TitleFont);
            // 选中详情或空态两行说明(稿 .picked / .empty):高度由内容撑。2026-08-31 接稿
            // 拆分:这里不再兼管可合成列表——可合成列表挪到下面的 CraftScroll,常驻显示、
            // 不再受选中态影响(旧实现选中了字就看不到可合成列表,是两者挤在同一个槽的将就)。
            // childForceExpandWidth 显式开:里面的选中详情行(牌面+信息列)要铺满栏宽,
            // 效果文字才有正确的换行宽度可依据。
            var suggestGo = Ui.VStack(workbenchStack.transform, "Picked", 6);
            suggestGo.GetComponent<VerticalLayoutGroup>().childForceExpandWidth = true;
            _suggestRow = suggestGo.transform;
            // 动作行**横排**(2026-08-21 用户拍板):出 / 拆 / 弃 三个单字钮一行排完。
            //   3 × 76 + 间距 8×2 = 244 ≤ BenchW ✓
            _actionRow = Ui.Row(workbenchStack.transform, "Actions", 8).transform;

            // 可合成标题 + 可滚动列表(稿 .craft { flex: 1; overflow-y: auto }):吃掉栏里的
            // 余量,把结束回合钮压在栏底。2026-08-31 从「挤在选中详情同一个槽、不会滚动」
            // 改成独立常驻区 + 真滚动——手里字一多(≥8 条可合成)旧的不滚动实现会把
            // 「结束回合」钮顶出卡片下缘,点不到。只列可合成的;缺料的字归左栏「配字表」
            // (稿的口径:拆合台是动手的地方,不是清单)。
            Ui.ThemedLabel(workbenchStack.transform, Strings.T("battle.label.craft_title"), 13, Theme.TextDim, Theme.TitleFont);
            var craftScroll = Ui.ScrollList(workbenchStack.transform, "CraftScroll", CraftRowSpacing, out var craftContent);
            Ui.Sized(craftScroll, flexHeight: 1f);
            _craftRow = craftContent;

            // 结束回合钮:与拆合台同栏、压在栏底(稿 .bench 的最后一件),仍是右手拇指位。
            _endTurnRow = Ui.Row(workbenchStack.transform, "EndTurn").transform;
        }

        /// <summary>战场的一排。稿:<c>.erow { flex: none }</c> —— 四排各自锁死高度,
        /// 宁可 field 留白也不互相挤。稿上写明了理由:field 空间一紧,默认的 flex-shrink
        /// 会把后排那一行压扁,看上去就成了「后排叠在前排上」。</summary>
        private static Transform MakeFieldRow(Transform field, string name)
        {
            var go = Ui.Row(field, name, RowGap);
            Ui.Sized(go, flexHeight: 0f);
            return go.transform;
        }

        // ---- 渲染 ----

        // 字牌位置登记(charId→当前 RectTransform):过渡动效的起终点;每次重绘重新登记
        private readonly System.Collections.Generic.Dictionary<string, RectTransform> _tileRects = new();
        // 字库卡位→牌面(2026-08-17):同字多张时 _tileRects 按 charId 只留最后一张,飞字起点改按位取
        private readonly System.Collections.Generic.List<RectTransform> _libraryTileRects = new();

        // ---- 新到手的牌:持续高亮(2026-08-30) ----

        /// <summary>刚到手的字/部件 → 光晕到期时刻(unscaledTime)。拆出的部件、合出的字、
        /// 选中的战利品、奇遇拿到的东西都登记在这里,`DrawLibrary` / `DrawPool` 每次重绘照着
        /// 把光晕重新套上。
        ///
        /// 记「到期时刻」而不是「剩余时长」:重绘频繁(点一下界面就是一次),存时长的话每次
        /// 重绘都会把倒计时重置,牌会一直亮着。
        ///
        /// ⚠ 用**字**做键而不是卡位:卡位在拆合/战利品插入后会整体位移,记下的下标下一帧就
        /// 指向别人了。代价是同字多张时会一起亮 —— 那也是能自圆其说的读法(「这个字刚变多」),
        /// 比亮错一张强。</summary>
        private readonly System.Collections.Generic.Dictionary<string, float> _freshGlyphs = new();

        /// <summary>高亮时长:够看清是哪张牌变了,又不至于拖进下一次操作。</summary>
        private const float FreshGlowSeconds = 2.4f;

        /// <summary>登记一张刚到手的牌。到期时刻统一往后推,连着拿到两张同名字时以后一次为准。</summary>
        private void MarkFresh(string glyph)
        {
            if (!string.IsNullOrEmpty(glyph))
                _freshGlyphs[glyph] = Time.unscaledTime + FreshGlowSeconds;
        }

        private void MarkFresh(System.Collections.Generic.IEnumerable<string> glyphs)
        {
            if (glyphs == null) return;
            foreach (var glyph in glyphs) MarkFresh(glyph);
        }

        /// <summary>重绘时给还在高亮期内的牌套上光晕;顺手清掉过期条目,免得这张表越攒越长。</summary>
        private void ApplyFreshGlow(string glyph, RectTransform tile, Color color)
        {
            if (!_freshGlyphs.TryGetValue(glyph, out float until)) return;
            if (Time.unscaledTime >= until) { _freshGlyphs.Remove(glyph); return; }
            _juice.Glow(tile, color, until);
        }

        /// <summary>手里有什么的一张快照(携带字库 + 携带池,各按多重集计数)。
        /// 奇遇结算前拍一张,结算后与新状态比对,多出来的就是这次拿到的。</summary>
        private (System.Collections.Generic.Dictionary<string, int> lib,
                 System.Collections.Generic.Dictionary<string, int> pool) SnapshotHoldings()
            => (Tally(_run.CarriedLibrary), Tally(_run.CarriedPool));

        private static System.Collections.Generic.Dictionary<string, int> Tally(
            System.Collections.Generic.IReadOnlyList<string> items)
        {
            var tally = new System.Collections.Generic.Dictionary<string, int>();
            for (int i = 0; i < items.Count; i++)
                tally[items[i]] = tally.TryGetValue(items[i], out int n) ? n + 1 : 1;
            return tally;
        }

        /// <summary>与快照比对,把多出来的字/部件登记成「刚到手」。
        ///
        /// 用 diff 而不是逐条读 `EventOption` 的字段:随机部件(`RandomComponents`)事前根本
        /// 不知道会掷出哪几个,任选字还要看玩家点了哪一项 —— 而 Core 日后再添获得途径时,
        /// 这里也不用跟着改。</summary>
        private void MarkFreshSince((System.Collections.Generic.Dictionary<string, int> lib,
                                     System.Collections.Generic.Dictionary<string, int> pool) before)
        {
            MarkFreshDelta(before.lib, _run.CarriedLibrary);
            MarkFreshDelta(before.pool, _run.CarriedPool);
        }

        private void MarkFreshDelta(System.Collections.Generic.Dictionary<string, int> before,
            System.Collections.Generic.IReadOnlyList<string> after)
        {
            foreach (var pair in Tally(after))
                if (!before.TryGetValue(pair.Key, out int had) || pair.Value > had)
                    MarkFresh(pair.Key);
        }

        /// <summary>把刚到手的字从来源位置飞进字库里它的新牌位,落位弹跳。
        ///
        /// **调用时机必须在触发重绘之后** —— `_tileRects` 里登记的得是新牌位。
        /// 找不到就安静早退:战利品选到最后一张时额度归零,Core 会当场把相位推走,
        /// 那一帧的字库画的已经是下一战的 `Battle.Library`,新字还没进去(它在携带库里)。
        /// 那种情况下光晕仍然有效 —— 下一战开打重绘时它就亮在字库里。</summary>
        private void FlyIntoLibrary(string glyph, bool hasFrom, Vector3 fromPos)
        {
            if (!hasFrom) return;
            if (!_tileRects.TryGetValue(glyph, out var target) || target == null) return;
            _juice.FlyGlyph(glyph, Theme.ElementColor(_graph.Get(glyph).Element), fromPos, target.position,
                () => _juice.PopTile(target));
        }

        private bool TryGetTilePos(string charId, out Vector3 pos)
        {
            if (_tileRects.TryGetValue(charId, out var rect) && rect != null)
            {
                pos = rect.position;
                return true;
            }
            pos = default;
            return false;
        }

        /// <summary>飞字起点:出的是字库第 <paramref name="libraryIndex"/> 张就用那张牌面,
        /// 否则(部件直出/下标缺失)退回按 charId 查。</summary>
        private bool TryGetCastFromPos(string charId, int libraryIndex, out Vector3 pos)
        {
            if (libraryIndex >= 0 && libraryIndex < _libraryTileRects.Count
                && _libraryTileRects[libraryIndex] != null)
            {
                pos = _libraryTileRects[libraryIndex].position;
                return true;
            }
            return TryGetTilePos(charId, out pos);
        }

        private void Refresh()
        {
            // 战利品阶段结束(不论选完、跳过还是引擎自动开拔)→ 本层记账落盘,挂起不丢收益
            if (_lastPhase == RunPhase.Reward && _run.Phase != RunPhase.Reward)
                _onFloorCleared?.Invoke();

            // 复活补给阶段结束的一次性检测(2026-08-16 全分支终审 Important 2):TryRevive() 只把
            // PlayerHp 回满、Battle.Phase 置回 PlayerTurn,并不会补跑 BeginPlayerTurn——时间轴上
            // 满格未动的敌人本该先补完它们那一拍(玩家是在上一拍被打死的,计量器停在死亡时余额、
            // 几乎恒为 0,补不满就轮不到它;并列时的优先级已在 2026-08-17 反转成
            // 玩家 0 / 召唤物 1 / Buff 敌 2 / 其余敌 3,玩家排**最先**),否则玩家会
            // 直接拿到一个 AP 还停在死亡时余额(几乎恒为 0)、Turn 没 +1、也没掉字的"幽灵回合"。
            // 三条退出路径都要覆盖,故分两半检测,缺一个都会漏:
            // (a) 補给挑完/主动跳过——SkipReviveReward()/PickReviveChar(Replacing) 的
            //     MaybeFinishRevive() 在调用方那边(供给弹窗的按钮回调)已经把 Phase 改成
            //     InBattle,才调这次 Refresh()——本次入口时 _run.Phase 已经不是 Reviving 了,
            //     只有上一次 Refresh 收尾时存的 _lastPhase 还留着 Reviving,靠它才追得到;
            // (b) 奖励池为空:TryRevive() 刚把 Phase 设成 Reviving,本次 Refresh 入口时还是
            //     Reviving,但下面几行的收尾检查会在**本次调用内**就把它翻回 InBattle——
            //     _lastPhase 那时记的还是战斗结算前的旧值,追不到这种"进也是这次、出也是
            //     这次"的瞬间转换,得在收尾检查跑完之后再看一眼当前 _run.Phase 才抓得到。
            bool enteredReviving = _lastPhase == RunPhase.Reviving || _run.Phase == RunPhase.Reviving;
            _lastPhase = _run.Phase;

            // 复活补给额度取尽或候选枯竭 → 收尾。
            // 满库**不再**收尾(2026-08-04):看了广告却因满库一无所得是白看,现在转入替换子步,
            // 与战利品 PickRewardReplacing 同口径。
            if (_run.Phase == RunPhase.Reviving
                && !(_run.ReviveCharPicksLeft > 0 && _run.RewardOptions.Count > 0))
                _run.SkipReviveReward();

            if (enteredReviving && _run.Phase == RunPhase.InBattle && !Animating)
            {
                // 复活流程刚结束(三条出口任一条),接着跑被打断的循环:满格未动的敌人先补完
                // 那一拍(不是因为敌人优先——反转后玩家的优先级最小,而是玩家的计量器停在
                // 死亡时余额),调度器随后自然轮到玩家(BeginPlayerTurn 发 AP、Turn+1、掉字)。
                // 不补 YieldTurn()——玩家是在上一拍被打死的,那一拍早就让过了,回头再补一次
                // 只会像 Revive() 生产代码修之前那样多跑一次玩家侧状态递减。
                // 这是一次性转换(_lastPhase 已在上面改成 InBattle),AdvanceRoutine 内部会
                // 反复自己调 Refresh() 把后续帧画出来,本帧不再往下走完整的 switch 绘制。
                BeginAnim();
                StartCoroutine(AdvanceRoutine());
                return;
            }

            // 开场回放(2026-08-17):进战斗后先把开场那几拍演出来,再放开输入。
            // 与 enteredReviving 同型:BeginAnim + 起协程 + return(本帧不走完整 switch,
            // OpeningRoutine 内部会自己 Refresh)。靠 _openingPlayedForBattle 守一次性,
            // 不必让 Core 记「播过没有」。断点续爬恢复的战斗 OpeningSteps 为空,自然跳过。
            // ⚠ 位置须在 enteredReviving 之后:复活续跑优先级更高,而且复活时开场早已播过。
            if (_run.Phase == RunPhase.InBattle && !Animating
                && _openingPlayedForBattle != _run.BattleIndex
                && Battle.OpeningSteps.Count > 0)
            {
                _openingPlayedForBattle = _run.BattleIndex;
                BeginAnim();
                StartCoroutine(OpeningRoutine());
                return;
            }

            if (_run.Phase == RunPhase.InBattle && _run.BattleIndex != _lastBattleIndex)
            {
                _lastBattleIndex = _run.BattleIndex;
                _onNewFloor?.Invoke(); // 新一场开打:携带态已就位,供外层快照
            }
            _tileRects.Clear();
            _resolvingHint = false; // 本次重绘是否处在动画锁里,末尾决定底部那行画什么
            Ui.Clear(_topLeft);
            Ui.Clear(_topRight);
            Ui.Clear(_enemyBackRow);
            Ui.Clear(_enemyFrontRow);
            Ui.Clear(_suggestRow);
            Ui.Clear(_craftRow);
            Ui.Clear(_actionRow);
            Ui.Clear(_centerRow);
            Ui.Clear(_hintColumn);
            Ui.Clear(_statusRow);
            Ui.Clear(_endTurnRow);
            Ui.Clear(_libraryRow);
            Ui.Clear(_poolRow);
            Ui.Clear(_bottomRow);
            Ui.Clear(_summonFrontRow);
            Ui.Clear(_summonBackRow);
            if (_run.Phase != RunPhase.Reward && _run.Phase != RunPhase.Reviving && _rewardModal != null)
                Destroy(_rewardModal); // 离开战利品/复活阶段:弹窗不能留在战斗界面上

            // 分隔线现在挂在 Frame/Arena/Mid/Field/DividerSlot/RowDivider 这条路径下
            // (骨架换布局组之后不再是 transform 的直接子节点了),但不参与上面的 Ui.Clear
            // 这条结论没变(它不该每帧重建),所以要在这里显式收起:战利品/复活/奇遇/
            // 部件超限/跑图结束这些阶段四排全空,
            // 留着它就是一条孤零零横在标题下方的墨线(2026-08-20 修回)。
            _rowDivider.SetActive(_run.Phase == RunPhase.InBattle);

            switch (_run.Phase)
            {
                case RunPhase.InBattle when Battle.Phase == BattlePhase.PlayerTurn
                    || Battle.Phase == BattlePhase.DropChoice:
                    DrawEnemies();
                    DrawTopBar();
                    DrawSummons();
                    DrawPlayerStats();
                    // ⚠ 这个 break 跳过了下面**全部**操作区,字库也在内 —— 动画锁期间
                    // _libraryTileRects 恒为空,而本方法开头的 Ui.Clear(_libraryRow) 已经把旧牌
                    // 销毁了。任何「等 Refresh 把字牌画出来再拿它的 RectTransform」的代码
                    // (飞牌起终点之类)都必须排在动画落幕之后,见 DealRoutine 的文档
                    // (2026-08-27:抽卡动画就是栽在这里,整段静默空跑)。
                    if (Animating) // 召唤/敌方行动中:锁出字,只留退出口(DrawTopBar 已画),待动画完成放行
                    {
                        _resolvingHint = true; // 「结算中……」由 Refresh 末尾画进底部提示行
                        break;
                    }
                    DrawLibrary();
                    DrawPool();
                    DrawSuggest();
                    DrawActions();
                    DrawCraftList();
                    DrawEndTurn();
                    // 回合掉字遇满库(2026-08-04):弹窗盖住操作区即可,Cast/EndTurn 已被 Core 拒绝
                    if (Battle.Phase == BattlePhase.DropChoice) DrawDropChoiceStep();
                    break;
                case RunPhase.InBattle: // 本场已分胜负,等待结算
                    DrawEnemies();
                    DrawTopBar();
                    DrawSummons();
                    DrawPlayerStats();
                    if (!Animating) DrawBattleSettle(); // 动画未落幕先不出结算/标语(第4项)
                    break;
                case RunPhase.Reward:
                    DrawTopBar();
                    DrawPlayerStats();
                    DrawLibrary(); // 携带字库:满库替换的操作对象
                    DrawPool();    // 携带部件池:随战利品页一并展示当前持有
                    DrawReward();
                    break;
                case RunPhase.Reviving:
                    DrawTopBar();
                    DrawPlayerStats();
                    DrawLibrary(); // 复活补给注入当前战斗字库,即时可见
                    DrawPool();
                    DrawReviveCharStep(); // 走到这里时已由 Refresh 顶部的收尾检查保证还有字可选
                    break;
                case RunPhase.Event:
                    DrawTopBar();    // 2026-08-27 用户拍板:奇遇不该把等级/墨锭那一栏整条吞掉
                    DrawPlayerStats();
                    DrawEvent();
                    break;
                case RunPhase.EventOverflow:
                    DrawEventOverflowStep();
                    break;
                default:
                    DrawRunEnd();
                    break;
            }
            DrawTutorialHint();
            // 单位详情弹窗开着时数值跟着 Refresh 走(2026-09-01,单位详情轮二 Task 5;稿上
            // 「数值随战斗实时刷新,不暂停」,事件驱动而非每帧)——重新拿一份 UnitDetail 整体
            // 重建。用 _modal 的 GameObject 名字判断而不是另记一个「当前是不是详情弹窗」的
            // 布尔:期间若被别的模态(奇遇替换弹窗等)顶掉,_modal 已经指向别的物体,名字
            // 自然对不上,不会把详情弹窗重新弹到别的模态上面;玩家自己点 ×/知道了/遮罩关掉后
            // _modal 变 Unity 假 null,同样不会再重建。
            // ⚠ 隐式依赖(2026-09-01 review 补记):这一块**不检查** Animating,而
            // AdvanceRoutine() 在 BeginAnim 的锁区间内、每个行动者动作播完都会调一次
            // Refresh(),也就是一次结算里详情理论上会被反复重建。今天打不通纯粹是巧合:
            // 详情弹窗的遮罩是铺满 transform 的全屏拦截层(UnitSheet.Show 里的 overlay),
            // 且下面紧跟着那句 SetAsLastSibling 每次 Refresh 后都把它置顶,详情开着时玩家
            // 点不到「结束回合」也出不了字,根本进不去会触发 AdvanceRoutine 的结算流程。
            // 这条不是设计出来的保证,是两处互不知情的代码碰巧对上了——将来谁把某个模态
            // 改成半屏或半透遮罩,这里就会在结算过程中悄悄开始闪,而且没有任何测试拦得住
            // (Presentation 层无自动化测试)。
            if (_modal != null && _modal.name == UnitSheetGameObjectName && _unitSheetSource != null)
            {
                var detail = _unitSheetSource();
                if (detail != null) _modal = UnitSheet.Show(transform, detail);
                else { Object.Destroy(_modal); _modal = null; _unitSheetSource = null; } // 单位没了(召唤物阵亡等):跟着关掉详情
            }
            // 长按 preview 置顶:重绘后 preview 须盖在战斗 UI 之上
            if (_modal != null) _modal.transform.SetAsLastSibling();
            // 底部提示行现在是全屏唯一的提示位,动画期间让给「结算中……」——播报要等整轮推进
            // 完才有完整内容(蓄力/释放/护盾被掀空按批累加),动画落幕后 OnAnimDone 那次重绘
            // 自然会把它显示出来。两者不能叠在同一行。
            _messageLabel.text = _resolvingHint ? Strings.T("battle.phase.resolving") : _message;
            SaveProgressIfChanged();
        }

        private string _savedFingerprint; // 上次落盘时的进度指纹

        /// <summary>每次玩家行动后落盘(2026-07-27)。挂在 Refresh 末尾而不是逐个动作入口埋点:
        /// 状态一变必然重绘,这样拆/合/出/丢/结束回合/取战利品/奇遇/复活一个都漏不掉。
        /// 靠指纹过滤纯 UI 重绘(选中字牌、看详情),免得点一下就写一次盘。</summary>
        private void SaveProgressIfChanged()
        {
            if (_onProgress == null) return;
            string fingerprint = ProgressFingerprint();
            if (fingerprint == _savedFingerprint) return;
            _savedFingerprint = fingerprint;
            _onProgress();
        }

        /// <summary>进度指纹:任何一次真实行动都会改变其中至少一项。</summary>
        private string ProgressFingerprint()
        {
            var battle = Battle;
            var sb = new StringBuilder();
            sb.Append(_run.Phase).Append('|').Append(_run.BattleIndex).Append('|')
              .Append(_run.CharPicksLeft).Append('|')
              .Append(_run.EarnedInk).Append('|')
              .Append(battle.Phase).Append('|').Append(battle.Turn).Append('|').Append(battle.Ap).Append('|')
              .Append(battle.PlayerHp).Append('|').Append(battle.PlayerShield).Append('|')
              .Append(string.Join(",", battle.Library)).Append('|')
              .Append(string.Join(",", battle.Pool));
            foreach (var enemy in battle.Enemies)
                sb.Append('|').Append(enemy.Hp).Append(',').Append(enemy.Statuses.TotalMagnitude(StatusKind.Burn)).Append(',').Append(enemy.Attack);
            foreach (var summon in battle.Summons)
                sb.Append('|').Append(summon?.Hp ?? -1);
            return sb.ToString();
        }

        private const int TutorialStepTotal = 4; // 与 Tutorial 私有 Script 数组的固定四步同口径(稿 .seal 也写死「共 4 步」)

        /// <summary>新手引导:稿上的四步故事弹层(2026-08-31 改稿,原先是屏底一行「◆ 提示」)。
        ///
        /// 弹层只在 <see cref="Tutorial.Step"/> **换成新的一步**时自动弹出;玩家点「下一步」
        /// 只是关掉弹层去真的操作(拆/合/出/领奖)——真正的步骤推进是那些操作各自调用的
        /// <see cref="Tutorial.Notify"/>,不是这颗按钮。下一次 Refresh 发现 Step 变了会自动
        /// 重新弹出下一步。与 <c>_modal</c> 同一套生命周期:每次 Refresh 先销毁上一份,
        /// 该显示才重新画一份——不然每次 Refresh 都新建一份会一直叠加。</summary>
        private void DrawTutorialHint()
        {
            if (_coachOverlay != null) { Object.Destroy(_coachOverlay); _coachOverlay = null; }
            if (_tutorial == null || _tutorial.Done || _tutorialSkipped) return;
            // 奇遇页不弹教程(2026-08-30 改序的连带,合并 main 时带过来的):先奇遇后选字之后,
            // 首层打赢有四成概率(enemies.json 的 eventChance = 40)先弹奇遇 —— 而教程此刻等的是
            // 「选一张字」,弹在奇遇页上就成了指着 A 说 B。教程是**动作**驱动的,这一步不会因为
            // 少弹一次就丢:走完奇遇进选字,它照样在那儿等。
            // ⚠ 这条守卫在 main 上拦的是屏底一行提示,换成整块模态的引导弹层之后**更该拦** ——
            // 一行字只是碍眼,一块模态会把奇遇的选项整个盖住。
            if (_run.Phase == RunPhase.Event || _run.Phase == RunPhase.EventOverflow) return;
            var step = _tutorial.Step;
            if (step != _coachShownStep)
            {
                _coachShownStep = step;
                _coachDismissed = false;
            }
            if (_coachDismissed) return;
            var (tale, doIt, then) = TutorialText(step);
            _coachOverlay = CoachOverlay.Show(transform, StepNumber(step), TutorialStepTotal, tale, doIt, then,
                onNext: () => { _coachDismissed = true; Refresh(); },
                onSkip: () => { _tutorialSkipped = true; Refresh(); });
        }

        private static int StepNumber(TutorialStep step) => step switch
        {
            TutorialStep.DismantleDemo => 1,
            TutorialStep.RecomposeDemo => 2,
            TutorialStep.CastDemo => 3,
            TutorialStep.PickReward => 4,
            _ => TutorialStepTotal,
        };

        private static (string tale, string doIt, string then) TutorialText(TutorialStep step) => step switch
        {
            TutorialStep.DismantleDemo => (
                Strings.T("battle.coach.dismantle_demo.tale"),
                Strings.T("battle.coach.dismantle_demo.doit"),
                Strings.T("battle.coach.dismantle_demo.then")),
            TutorialStep.RecomposeDemo => (
                Strings.T("battle.coach.recompose_demo.tale"),
                Strings.T("battle.coach.recompose_demo.doit"),
                Strings.T("battle.coach.recompose_demo.then")),
            TutorialStep.CastDemo => (
                Strings.T("battle.coach.cast_demo.tale"),
                Strings.T("battle.coach.cast_demo.doit"),
                Strings.T("battle.coach.cast_demo.then")),
            TutorialStep.PickReward => (
                Strings.T("battle.coach.pick_reward.tale"),
                Strings.T("battle.coach.pick_reward.doit"),
                Strings.T("battle.coach.pick_reward.then")),
            _ => ("", "", ""),
        };

        private const float CoachBtnSize = 46f;   // 稿 .coachbtn { width/height: 20pt }
        private const float CoachBtnTapH = 92f;   // 稿 .tap::after { height: 44pt }——触控目标 ≥44pt

        /// <summary>顶栏(稿 .bar):关卡名 · 层数 · 弹性空 · 墨锭 chip · 回合 · ? 引导钮 · ✕ 退出钮。
        ///
        /// 关卡名沿用既有的「{title} · 战斗 N」组合文案(不拆开重做——项目没有稿上「文山」
        /// 那种独立短名,_title 本来就是「「字林」第 1~30 层」这种整段描述,已经比稿的
        /// 单一地名承载更多信息);新增的是第二段「第 N 层」,取自 <see cref="RunEngine.CurrentDepth"/>,
        /// 对应稿 .seg 里「层」的那一半——「场」那一半已经在老文案的「战斗 N」里给过了,
        /// 不重复拼一次。</summary>
        private void DrawTopBar()
        {
            Ui.ThemedLabel(_topLeft, string.IsNullOrEmpty(_title)
                    ? Strings.T("battle.label.battle_index", ("index", _run.BattleIndex + 1))
                    : Strings.T("battle.label.battle_index_titled", ("title", _title), ("index", _run.BattleIndex + 1)),
                20, Theme.TextMain, Theme.TitleFont, TextAnchor.MiddleLeft);
            Ui.ThemedLabel(_topLeft, Strings.T("battle.label.depth", ("depth", _run.CurrentDepth)),
                13, Theme.TextDim, align: TextAnchor.MiddleLeft);
            // 塔内预算与账户是同一本账了(2026-08-30 半额取消):层清算与字摊收支都记在
            // run.EarnedInk 上、随赚随结进账户,所以这里也走 InkCounter,与外层五个顶栏
            // 共用同一套增减飘字 —— 打完一层当场就能看见 +N,而不是等回到地图才补一个总数。
            // 差额只会在「刚挣到、还没走到下一个存档点」的那一小段里存在,飘字因此比账户更早,
            // 正是想要的时序;每条离塔路径都先 CommitEventInk,所以切回外层时两边必然相等。
            Ui.InkCounter(_topRight, _run.AvailableInk, 18);
            Ui.ThemedLabel(_topRight, Strings.T("battle.label.turn", ("turn", Battle.Turn)), 18, Theme.TextDim);
            DrawCoachButton(_topRight);
            bool suspend = _onExit != null; // 无尽:退出可挂起/弃塔(2026-07-19);否则=认输
            Ui.PillButton(_topRight, Strings.T("battle.btn.exit"), () => // 统一弹窗确认(2026-07-19 拍板)
            {
                if (suspend)
                    ShowModal(Strings.T("battle.dialog.suspend_tower.title"),
                        Strings.T("battle.dialog.suspend_tower.body"),
                        (Strings.T("battle.btn.suspend"), _onExit, Theme.Cinnabar, Color.white),
                        (Strings.T("battle.btn.abandon"), () => _onAbandon?.Invoke(), Theme.InkSoft, Color.white),
                        (Strings.T("battle.btn.continue_fight"), null, Theme.LockedBg, Theme.TextMain));
                else
                    ShowModal(Strings.T("battle.dialog.exit_confirm.title"), Strings.T("battle.dialog.exit_confirm.body"),
                        (Strings.T("battle.btn.confirm_exit"), () => _onRunEnded(false), Theme.Cinnabar, Color.white),
                        (Strings.T("battle.btn.continue_fight"), null, Theme.LockedBg, Theme.TextMain));
            }, Theme.ExitPink, Color.white, 15, new Vector2(90, 38));
        }

        /// <summary>「?」引导钮(稿 .coachbtn):重新打开当前这步的引导弹层(2026-08-31 接上
        /// <see cref="DrawTutorialHint"/>,取代此前占位的提示弹窗)。
        /// 清 <see cref="_coachDismissed"/> 让下一次 Refresh 重新画;顺带清掉
        /// <see cref="_tutorialSkipped"/>——「跳过引导」关的是自动弹出,不该连带把这颗
        /// 手动求助的入口也堵死,不然玩家一旦手滑点了跳过,「?」就成了摆设。教程已经跑完
        /// (<c>_tutorial.Done</c>)或压根没有引导(<c>_tutorial == null</c>)时没有「当前步」
        /// 可重开,点了不做事。
        ///
        /// 触控目标 ≥44pt(稿 .tap::after):TopBarH 只有 46 个逻辑单位(22pt),天生装不下
        /// 92(44pt)高的命中区——稿的 CSS 版本靠 <c>position:absolute</c> 让 <c>.tap::after</c>
        /// 溢出 <c>.bar</c> 的边界来解决,这里用同样的思路:视觉圆钮(<see cref="CoachBtnSize"/>)
        /// 正常挂在 <paramref name="parent"/> 这个 Row 里参与布局(46 高,不撑爆 46 高的顶栏);
        /// 命中区是视觉钮的**子节点**,不挂 LayoutElement、不受 Row 约束,单纯靠 Anchor 在
        /// 视觉钮基础上下各多撑 (<see cref="CoachBtnTapH"/> − <see cref="CoachBtnSize"/>) / 2——
        /// 视觉钮所在的 Panel 没有 Mask/RectMask2D,子节点超出父矩形不会被裁掉,这才是关键。
        /// ⚠ 命中区会略微探进 TopBar 上方的安全区留白与下方 Arena 顶端(拆合台卡片的圆角
        /// 标题区)——两处本就没有其它可点元素,与稿的溢出取舍一致。</summary>
        private void DrawCoachButton(Transform parent)
        {
            var visual = Ui.Panel(parent, "Coach");
            var visualElement = visual.AddComponent<LayoutElement>();
            visualElement.preferredWidth = CoachBtnSize;
            visualElement.preferredHeight = CoachBtnSize;
            var visualImage = visual.AddComponent<Image>();
            visualImage.sprite = Theme.Rounded((int)(CoachBtnSize / 2f));
            visualImage.type = Image.Type.Sliced;
            visualImage.color = Theme.GoldSoft; // 稿 rgba(201,169,74,.22) 的近似不透明版
            visualImage.raycastTarget = false;  // 点击交给下面的 Hit 子节点,视觉层别抢
            var label = Ui.ThemedLabel(visual.transform, "?", 22, Theme.GoldDeep, Theme.TitleFont);
            Ui.Stretch(label.rectTransform);

            var hit = Ui.Panel(visual.transform, "Hit");
            var hitImage = hit.AddComponent<Image>();
            hitImage.color = new Color(0, 0, 0, 0); // 透明,只用来接收点击、撑出 44pt 高的命中区
            Ui.Anchor((RectTransform)hit.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-CoachBtnSize / 2f, -CoachBtnTapH / 2f), new Vector2(CoachBtnSize / 2f, CoachBtnTapH / 2f));
            var button = hit.AddComponent<Button>();
            button.targetGraphic = hitImage;
            button.onClick.AddListener(() =>
            {
                if (_tutorial == null || _tutorial.Done) return; // 没有「当前步」可重开
                _tutorialSkipped = false;
                _coachDismissed = false;
                Refresh();
            });
        }

        // 玩家条(稿 .me)。信息列宽度不是编译期常量——_bottomRow 铺满 Mid 区,实际宽度
        // 由运行时布局决定,不像召唤/敌人格有固定格宽,所以这里让 info 及其内部三条都
        // flexibleWidth = 1 顶满剩余空间,而不是像 DrawSummons/DrawEnemies 那样现场
        // 算一个固定 infoWidth。
        private const float PlayerBlkSize = 71f;         // 稿 .me .blk { 34×34 }
        private const float PlayerInfoSpacing = 4f;      // 稿 .me .info { gap: 2pt }
        private const float PlayerHeaderHeight = 18f;    // 容得下 14 号「执笔人」
        private const float PlayerHpBarHeight = 17f;     // 稿 .me .hpb { height: 8pt }
        private const float PlayerShieldBarHeight = 10f; // 稿 .me .shb { height: 5pt }
        private const float PlayerActionBarHeight = 6f;  // 稿 .me .atb { height: 3pt },与敌人同口径
        private const float PlayerSttWidth = 251f;       // 稿 .stt { width: 120pt }
        private const float PlayerApGap = 10f;           // 稿 .ap { gap: 5pt }
        private const float PlayerApPipGap = 6f;         // 稿 .ap .pips { gap: 3pt }
        private const float PlayerApPipWidth = 17f;      // 稿 .ap .pips i { width: 8pt }
        private const float PlayerApPipHeight = 42f;     // 稿 .ap .pips i { height: 20pt }

        /// <summary>玩家条(2026-08-31 改稿):与召唤/敌人格同构的一条——立绘块 + 信息列 +
        /// 状态栏 + AP,DOM 顺序取自稿 Battle.dc.html(blk→info→stt→ap;简报草稿把
        /// 3、4 段顺序写反了,以稿为准,2026-08-31 用户确认)。
        ///
        /// 血/盾条不再用共享的 HpBar/ShieldBar helper(那两个会把数值叠成条上文字)——
        /// 稿上 .me/.ally 的 .hpb/.shb 都是裸条,数字挪到头行文字里,与 .foe「条上叠字、
        /// 没有头行数字」是两种不同的读法,不能顺手复用敌人那一套。行动条例外:调度方
        /// 明确要求继续走共享 ActionBar helper(只改颜色不改结构),所以行动条仍带百分比
        /// 叠字,与血/盾条不对称,是刻意的,不是漏改。
        ///
        /// ⚠ 这不是「比照敌人格先例」——敌人格的 <see cref="HpBar"/> 现在仍在用(见
        /// <c>DrawEnemies</c>),没有被绕开;唯一被绕开、只留颜色/soon 态改动的公共 helper
        /// 是 <see cref="ActionBar"/>。别把这条注释理解成「敌人格血条也是绕开 helper 的」。</summary>
        private void DrawPlayerStats()
        {
            // 立绘块(稿 .me .blk):墨底白字。没有稿上那枚金色等级角标——项目没有
            // 「玩家等级」这个数据源(无尽层段爬塔,非角色养成等级制,见第 20 章 GDD),
            // 伪造一个数字会比空着更误导人,留给以后真的接入等级概念时再补。
            //
            // Blk/Info 外壳换成 Ui.UnitBlock(2026-08-31 收口,与敌人格/召唤格同一套写法)。
            // 水平间距直接读 _bottomRow 自己那条 Ui.Row 的 spacing——Blk/Info 原本就是
            // 它的直接子物体,间距一直来自这里、从没另立过常量,现在只是把这个既有值
            // 接着用,数值没变。infoWidth 传 -1f:玩家信息列不像敌人/召唤格有算得出的
            // 定宽,靠 flexWidth 吃剩余空间,下面会把 Ui.UnitBlock 内部按 -1f 建出的
            // LayoutElement 改成 flexWidth:1。
            float blkInfoGap = _bottomRow.GetComponent<HorizontalLayoutGroup>().spacing;
            Ui.UnitBlock(_bottomRow, "Unit", PlayerBlkSize, -1f, blkInfoGap,
                out var portrait, out var info);
            var blkImage = portrait.gameObject.AddComponent<Image>();
            blkImage.sprite = Theme.Rounded(15);
            blkImage.type = Image.Type.Sliced;
            blkImage.color = Theme.Ink;
            var faceLabel = Ui.ThemedLabel(portrait, Strings.T("battle.label.player_face"),
                Mathf.RoundToInt(PlayerBlkSize * 0.56f), Color.white, Theme.TitleFont);
            Ui.Stretch(faceLabel.rectTransform);

            // 把 Ui.Bar/ActionBar 建出来的裸条,从「固定宽」改判成「跟着 info 撑满」——
            // 这几条的真实宽度要等 _bottomRow 布局完才知道,建的时候先随便给个 0。
            void StretchWidth(GameObject go)
            {
                var el = go.GetComponent<LayoutElement>();
                el.preferredWidth = -1f;
                el.flexibleWidth = 1f;
            }

            // 分隔线(稿 .stt/.ap 都有 border-left: 1px solid #E4DDCE),把四段在视觉上隔开。
            // 用 Theme.PanelBorder(#DED7C9,与稿差值可忽略)——它本来就是「面板描边(稿上
            // 统一 1pt)」的既有 token,不为这条线新增颜色常量。高度取 PlayerBlkSize:
            // 稿上 .me 靠 align-items:center 让行高等于最高的子项(立绘块),.stt/.ap 的
            // align-self:stretch 只是撑到这个高度,不是另有一个独立的行高来源。
            void Divider()
            {
                var div = Ui.Panel(_bottomRow, "Divider");
                var divElement = div.AddComponent<LayoutElement>();
                divElement.preferredWidth = 2f;
                divElement.preferredHeight = PlayerBlkSize;
                var divImage = div.AddComponent<Image>();
                divImage.color = Theme.PanelBorder;
                // raycastTarget = false —— 它只是一条线,不能拦掉空白点击(那是取消选中用的)。
                // 上一个任务漏了这行,与 RowDivider(BuildSkeleton)同一个坑,见那边的注释。
                divImage.raycastTarget = false;
            }

            // 信息列(稿 .me .info { flex: 1 }):吃掉 blk/stt/ap 之外的全部剩余宽度。
            // info 由上面的 Ui.UnitBlock 建出(VStack + LayoutElement),这里只按玩家条
            // 自己的口径接着配:spacing 用 PlayerInfoSpacing(UnitBlock 建的时候还不知道
            // 这个值),宽度从 UnitBlock 给的占位 -1f 改判成 flexWidth:1。
            var infoLayout = info.GetComponent<VerticalLayoutGroup>();
            infoLayout.childAlignment = TextAnchor.UpperLeft;
            infoLayout.spacing = PlayerInfoSpacing;
            var infoElement = info.GetComponent<LayoutElement>();
            infoElement.preferredWidth = -1f;
            infoElement.flexibleWidth = 1f;

            // 头行:执笔人(左)+ 血/上限 盾 N(右),与 MapView.StatCell 同一套「同一块
            // 满宽面板叠两条 Stretch 文字、靠 TextAnchor 分左右」的做法。
            int shownHp = Animating ? _animPlayerHp : Battle.PlayerHp;
            int shownShield = Animating ? _animShield : Battle.PlayerShield;
            var header = Ui.Panel(info.transform, "Header");
            Ui.Sized(header, height: PlayerHeaderHeight, flexWidth: 1f);
            var whoLabel = Ui.ThemedLabel(header.transform, Strings.T("battle.label.player_name"),
                14, Theme.TextMain, Theme.TitleFont, TextAnchor.MiddleLeft);
            Ui.Stretch(whoLabel.rectTransform);
            var hpLabel = Ui.ThemedLabel(header.transform,
                Strings.T("battle.label.player_hp_shield",
                    ("hp", shownHp), ("hpMax", PlayerMaxHp), ("shield", shownShield)),
                11, Theme.TextDim, null, TextAnchor.MiddleRight);
            Ui.Stretch(hpLabel.rectTransform);

            // 血条(裸条,2026-08-31 起不再叠字——数字已经在头行读到)
            var hpBarGo = Ui.Bar(info.transform, PlayerMaxHp > 0 ? shownHp / (float)PlayerMaxHp : 0f,
                Theme.Cinnabar, new Vector2(0f, PlayerHpBarHeight));
            StretchWidth(hpBarGo);
            _playerHpBar = ((RectTransform)hpBarGo.transform.Find("Fill"), null);

            // 护盾条(裸条,常驻——0 时是空条,不再「有盾才画」跳一下整块布局)
            var shieldBarGo = Ui.Bar(info.transform, Mathf.Clamp01(shownShield / ShieldBarFull),
                Theme.Gold, new Vector2(0f, PlayerShieldBarHeight));
            StretchWidth(shieldBarGo);
            _playerShieldBar = ((RectTransform)shieldBarGo.transform.Find("Fill"), null);

            // 行动条(2026-08-17 加入,2026-08-31 改色):仍走共享 ActionBar helper,
            // 带百分比叠字——调度方明确要求这里只改颜色不改结构,见方法注释。
            _playerActionBar = ActionBar(info.transform, Battle.PlayerActionMeter,
                new Vector2(0f, PlayerActionBarHeight), 8);
            StretchWidth(_playerActionBar.fill.parent.gameObject);

            Divider(); // info | stt 分隔线

            // 状态栏(稿 .stt):定宽 120pt,超出收 +N —— 与敌人 chip 行同一套 Ui.ChipFlow,
            // 不再是旧版「都为 0 就不建、按需创建的无限宽单行」;内容/顺序/颜色/图标
            // 一个没变,只是从「有则建」改成「恒定占位、内容为空时是一条空槽」——与
            // blk/info/ap 同为结构性四段之一,不能忽有忽无。
            var statusChips = new List<Ui.ChipSpec>();
            int seal = Battle.PlayerStatuses.TotalMagnitude(StatusKind.Seal);
            if (seal > 0) statusChips.Add(new($"−{seal}AP", Theme.InkSoft, Color.white, "seal"));
            int playerBurn = Battle.PlayerStatuses.TotalMagnitude(StatusKind.Burn);
            if (playerBurn > 0) statusChips.Add(new($"{playerBurn}", Theme.Cinnabar, Color.white, "burn"));
            int immunity = Battle.PlayerStatuses.TotalMagnitude(StatusKind.Immunity);
            if (immunity > 0) statusChips.Add(new($"{immunity}", Theme.Jade, Color.white, "immunity"));
            int reflect = Battle.PlayerStatuses.TotalMagnitude(StatusKind.Reflect);
            if (reflect > 0) statusChips.Add(new($"{reflect}%", Theme.Jade, Color.white, "reflect"));
            // 攻击增益 / 战意(2026-08-12,剡 / 战 / 戮):两者都只改 EffectiveAttack,
            // 而战斗界面不显示攻击力 —— 不出这一格的话这三个字打出去毫无反馈。
            // ApBoost(利)不出格:AP 格子数直接读 Battle.ApPerTurn,多一格就是它的反馈。
            int attackBuff = Battle.PlayerStatuses.TotalMagnitude(StatusKind.AttackBuff);
            if (attackBuff > 0) statusChips.Add(new($"+{attackBuff}", Theme.Gold, Color.white, "attack"));
            int morale = Battle.PlayerStatuses.TotalMagnitude(StatusKind.Morale);
            if (morale > 0) statusChips.Add(new($"{morale}", Theme.Gold, Color.white, "morale"));
            // 暴击率(2026-08-12,锋):读 EffectiveCrit(已钳到 100)而不是状态总量 ——
            // 叠 6 张锋时玩家该看到的是 100 不是 120
            if (Battle.EffectiveCrit > 0)
                statusChips.Add(new($"{Battle.EffectiveCrit}%", Theme.Gold, Color.white, "crit"));
            // 穿透(2026-08-12,锐):读状态总量而不是某次结算的有效值 —— 穿透打谁减多少要看
            // 那只怪的甲,玩家该看到的是自己攒了多少
            int pierceBuff = Battle.PlayerStatuses.TotalMagnitude(StatusKind.PierceBuff);
            if (pierceBuff > 0) statusChips.Add(new($"{pierceBuff}", Theme.Gold, Color.white, "pierce"));
            // 护甲 / 闪避 / 速度(2026-08-17 改口径):只在**有增益**时出,不再常驻——
            // 基础值仍能在养成界面看到,局内只报「我从字上攒到了什么」(与穿透同口径)。
            // speed 取 != 0 而非 > 0:被减速是坏消息,恰恰更该让玩家看见。
            int defenseBuff = Battle.PlayerStatuses.TotalMagnitude(StatusKind.DefenseBuff);
            if (defenseBuff > 0) statusChips.Add(new($"+{defenseBuff}", Theme.Jade, Color.white, "defense"));
            int dodgeBuff = Battle.PlayerStatuses.TotalMagnitude(StatusKind.DodgeBuff);
            if (dodgeBuff > 0) statusChips.Add(new($"+{dodgeBuff}%", Theme.Jade, Color.white, "dodge"));
            int speedMod = Battle.PlayerStatuses.TotalMagnitude(StatusKind.SpeedModifier);
            if (speedMod != 0)
                statusChips.Add(new(speedMod > 0 ? $"+{speedMod}" : $"−{-speedMod}",
                    speedMod > 0 ? Theme.Jade : Theme.InkSoft, Color.white, "speed"));
            // 势 / 水势(2026-09-02,终审修复):无图标资产,文字里自带字头区分——同「seal」
            // 那行 AP 后缀同一处理,字头走字符串表。层数为 0 时不占格,与其余增益类 chip 同口径。
            int momentum = Battle.MomentumStacks;
            if (momentum > 0)
                statusChips.Add(new($"{Strings.T("status.momentum.chip")}{momentum}", Theme.Gold, Color.white, null));
            int waterPower = Battle.WaterPowerStacks;
            if (waterPower > 0)
                statusChips.Add(new($"{Strings.T("status.waterpower.chip")}{waterPower}", Theme.Jade, Color.white, null));
            var stt = Ui.ChipFlow(_bottomRow, "Status", statusChips, PlayerSttWidth - 4f, 12, 2,
                ChipPadX, ChipPadY, ChipSpacing, ChipLineSpacing);
            stt.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.UpperLeft;
            Ui.Sized(stt, width: PlayerSttWidth);

            Divider(); // stt | ap 分隔线

            // AP 竖笔画格(稿 .ap):满的是墨、空的是白格,像还没蘸墨的笔画——原先三颗
            // 8px 小圆点分量还不如旁边的状态 chip。
            //
            // 2026-09-01 用户拍板去掉稿上的「3/3」数字(.ap .n):几根笔画格本来就是
            // 「满了几根 / 一共几根」,数字是同一件事说第二遍。连带没了的是稿上唯一的
            // dry 表现(.ap.dry .n 转深朱砂)——AP 见底时**每一根笔画格都是空的**,
            // 这本身就是最一眼的 dry 反馈,不必再给标签或格子补一套变色规则。
            // (稿另有 .ap.dry .pips i.on 一条,在当前 AP 语义下永远不触发:dry 恰好
            //  意味着 Ap == 0,不会有任何一枚 pip 处于 on 态。落地一直没实现它。)
            var apRow = Ui.Row(_bottomRow, "Ap", PlayerApGap);
            Ui.ThemedLabel(apRow.transform, "AP", 12, Theme.TextDim);
            var pips = Ui.Row(apRow.transform, "Pips", PlayerApPipGap);
            for (int i = 0; i < Battle.ApPerTurn; i++)
            {
                var pip = Ui.Panel(pips.transform, $"Pip{i}");
                var image = pip.AddComponent<Image>();
                image.sprite = Theme.Rounded(4);
                image.type = Image.Type.Sliced;
                image.color = i < Battle.Ap ? Theme.Ink : Theme.PaperDim;
                var element = pip.AddComponent<LayoutElement>();
                element.preferredWidth = PlayerApPipWidth;
                element.preferredHeight = PlayerApPipHeight;
            }

            // 治疗选目标态(2026-08-22):玩家整条底栏点亮为「治玩家」的点击面——覆盖对象从
            // 原来的 hpStack(单个 VStack)换成 _bottomRow(整条横排的容器),结构改横排
            // 之后玩家不再有单一的「血条那一块」,整条 .me 才是对应稿上的落点。
            // 判据仍走 Battle.CanHealSlot(Targeting.PlayerTarget),不是恒真的假设——
            // 万一以后这条规则改了,表现层不必跟着改。
            // 拖拽落点判定要用它(2026-08-27):玩家没有「槽位」,这一整条就是他的落点。
            _playerAllyRect = (RectTransform)_bottomRow;
            if (_allyTargeting && Battle.CanHealSlot(Targeting.PlayerTarget))
                AttachAllyTargetPicker(_bottomRow, Targeting.PlayerTarget);
        }

        // 召唤格尺寸(2026-08-31 横排改造,与敌人格同构)。之前挤在 34/28 的小方块里,
        // 横排后立绘反而放大到 48/36(稿写明了这条收益)。尺寸 = 稿 pt × 2.093,与
        // 敌人格(EnemyCellWidth 一带)同一套换算。
        private const float SummonCellWidth = 289f;         // 稿 .ally.front/.rear { width: 138pt }
        private const float SummonCellHeightFront = 113f;   // 稿 .slotfree.front { height: 54pt }
        private const float SummonCellHeightBack = 88f;     // 稿 .slotfree.rear { height: 42pt }
        private const float SummonPortraitFront = 100f;     // 稿 .ally.front .blk { 48×48 }
        private const float SummonPortraitBack = 75f;       // 稿 .ally.rear .blk { 36×36 }
        private const float SummonBlkInfoGap = 10f;         // 稿 .ally { gap: 5pt }
        private const float SummonInfoSpacing = 4f;         // 稿 .ally .info { gap: 2pt }
        private const float SummonHeaderHeight = 16f;       // 容得下 12 号「攻 N」
        private const float SummonHpBarHeight = 13f;        // 稿 .ally .hpb { height: 6pt }
        // 盾条 / 行动条与敌人格同一口径(稿 .shb/.atb 两种单位都是 height:3pt),
        // 复用 EnemyShieldBarHeight/EnemyActionBarHeight(见敌人格常量),不重复定义。
        // DrawSummons 本体不再用它(格子内部改横排,不再是竖排 VStack),但锁格/空槽
        // (DrawLockedSummonSlot/DrawEmptySummonSlot)仍各自套一层单子物体的 VStack,
        // 留着这个常量给那两处用,省得再定义一份。
        private const float SummonStackSpacing = 2f;

        /// <summary>我方召唤物(木系):替玩家承伤并反击。2026-08-20 起分前后两排、各 3 格,
        /// 下标即槽位(<c>0..FrontRow-1</c> 前排,其余后排),**空槽也画**虚框占位 ——
        /// 召唤/阵亡时布局不跳动,玩家也能一眼看出还剩几个位子。
        ///
        /// 2026-08-31 格内改横排(与 <see cref="DrawEnemies"/> 同构):立绘在左、信息列在右
        /// (头行 攻N+血/上限、chip 行、血/盾/行动条自上而下)。血/盾条同玩家条一样不再叠字
        /// (数字已在头行),行动条例外仍走共享 ActionBar helper(结构不变、只改颜色)。</summary>
        private void DrawSummons()
        {
            _summonRectByCore.Clear();
            _summonCellByCore.Clear();
            _summonBarByCore.Clear();
            _summonShieldBarByCore.Clear();
            _summonActionBarByCore.Clear();
            // 八格**常态化显示**(2026-08-27 用户拍板):未解锁的也画出来,印上「N 关解锁」——
            // 玩家因此一眼看得出还有几格没开、什么时候开,而不是打到第 10 层突然多出两格。
            // 循环到硬上限而不是 SummonCapacity;后者只决定这一格是实格还是锁着的格。
            for (int i = 0; i < Battle.Summons.Count; i++)
            {
                // 下标即槽位:[0, FrontRow) 前排,其余后排。**用 Battle.FrontRow 而不是写死 4** ——
                // 槽位几何是 Core 的事,表现层跟着它走。它是固定的,不随解锁伸缩,
                // 所以锁着的格子也画在它将来该在的那一排
                bool front = i < Battle.FrontRow;
                // 判据是**集合**不是「下标 < 开放数」(2026-08-27):解锁按位置来,
                // 开局开的是槽 1、2,槽 0 锁着 —— 按下标比大小会把锁格画反
                if (!Battle.IsSlotOpen(i)) { DrawLockedSummonSlot(i, front); continue; }
                var summon = Battle.Summons[i];
                // 动画期间:本回合被打死的召唤物照常画出(玩家看得到它挨打);平时只画存活的(=我方回合开始清理死尸)
                bool visible = summon != null
                    && (summon.Alive || (Animating && _summonAnimHp.ContainsKey(i)));
                if (!visible) { DrawEmptySummonSlot(i, front, summon); continue; }

                var cell = Ui.Panel(front ? _summonFrontRow : _summonBackRow, $"Summon{i}");
                var cellElement = cell.AddComponent<LayoutElement>();
                cellElement.preferredWidth = SummonCellWidth;
                cellElement.preferredHeight = front ? SummonCellHeightFront : SummonCellHeightBack;
                _summonCellByCore[i] = (RectTransform)cell.transform;

                float portraitSize = front ? SummonPortraitFront : SummonPortraitBack;
                float infoWidth = SummonCellWidth - portraitSize - SummonBlkInfoGap;
                int summonIndex = i; // 闭包捕获:直接用 i 会全都指向循环终值

                // 立绘在左、信息列在右(2026-08-31 收口,与敌人格/玩家条同一套 Ui.UnitBlock)
                Ui.UnitBlock(cell.transform, "Block", portraitSize, infoWidth, SummonBlkInfoGap,
                    out var portraitMount, out var info);
                info.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.UpperLeft;

                // 保持着色挨打:HP 掉到 0 + 我方回合开始消失来表达阵亡,不在动画里就变灰(免飘字/掉血还没到就先灰)
                var glyph = Ui.RoundButton(portraitMount, summon.Char, () => OnSummonClicked(summonIndex),
                    Theme.ElementSoft(summon.Element), Theme.ElementSoftFg(summon.Element),
                    Mathf.RoundToInt(portraitSize * 0.46f), new Vector2(portraitSize, portraitSize), 12);
                Ui.Stretch((RectTransform)glyph.transform); // 挂载点已按 portraitSize 定好尺寸,铺满即可
                _summonRectByCore[i] = (RectTransform)glyph.transform;

                // 护盾角标(稿 .ally .sh,与敌人格同一判据):叠在立绘左下角,Shield > 0 才画
                if (summon.Shield > 0)
                {
                    var badge = Ui.Chip(glyph.transform, summon.Shield.ToString(), Theme.Gold, Theme.GoldText,
                        ShieldBadgeFontSize, ShieldBadgePadX, ShieldBadgePadY, "shield");
                    var badgeElement = badge.GetComponent<LayoutElement>();
                    Ui.Anchor((RectTransform)badge.transform, Vector2.zero, Vector2.zero,
                        new Vector2(ShieldBadgeMargin, ShieldBadgeMargin),
                        new Vector2(ShieldBadgeMargin + badgeElement.preferredWidth,
                            ShieldBadgeMargin + badgeElement.preferredHeight));
                }

                // info 已由上面的 Ui.UnitBlock 建好,这里只补 spacing——UnitBlock 建的时候
                // 还不知道这个值(4f 与 VStack 默认值巧合相同,显式写出不依赖这个巧合)。
                info.GetComponent<VerticalLayoutGroup>().spacing = SummonInfoSpacing;

                // 头行:攻 N(左)+ 血/上限(右),稿 .ally .hd —— 与玩家条头行同一套
                // 「同一块满宽面板叠两条 Stretch 文字」做法,取代旧版顶行的攻/字块/状态三段横排。
                int shownHp = Animating && _summonAnimHp.TryGetValue(i, out var pre) ? pre : summon.Hp;
                var header = Ui.Panel(info.transform, "Header");
                Ui.Sized(header, width: infoWidth, height: SummonHeaderHeight);
                var atkLabel = Ui.ThemedLabel(header.transform,
                    Strings.T("battle.label.summon_attack", ("attack", summon.Attack)), 12, Theme.TextMain,
                    null, TextAnchor.MiddleLeft);
                Ui.Stretch(atkLabel.rectTransform);
                var hpLabel = Ui.ThemedLabel(header.transform, $"{shownHp}/{summon.MaxHp}", 11, Theme.TextDim,
                    null, TextAnchor.MiddleRight);
                Ui.Stretch(hpLabel.rectTransform);

                // chip 行(稿 .cps):被动 + 灼烧 + 增益条数,从旧顶行右翼搬进信息列 ——
                // 内容/判据完全不变(SummonPassiveTag/Burn/CountBuffs),只是从竖排小 chip
                // 摞改成横排 Ui.ChipFlow(与敌人 chip 行同一套截断逻辑,虽这三项几乎不会溢出)。
                var chipSpecs = new List<Ui.ChipSpec>();
                string passiveTag = SummonPassiveTag(summon.Passive);
                if (passiveTag.Length > 0) chipSpecs.Add(new(passiveTag, Theme.Cinnabar, Color.white));
                int burn = summon.Statuses.TotalMagnitude(StatusKind.Burn);
                if (burn > 0) chipSpecs.Add(new($"{burn}", Theme.Cinnabar, Color.white, "burn"));
                int buffs = CountBuffs(summon);
                if (buffs > 0)
                    chipSpecs.Add(new(Strings.T("summon.buff_count", ("count", buffs)), Theme.Jade, Color.white));
                Ui.ChipFlow(info.transform, "Chips", chipSpecs, infoWidth - 4f, ChipFontSize, ChipMaxLines,
                    ChipPadX, ChipPadY, ChipSpacing, ChipLineSpacing);

                // 三条(裸条,数字已在头行读到,与玩家条同一取舍):血、盾、行动条自上而下。
                // 血条颜色刻意与玩家/敌人不同(稿 .ally .hpb > span { background: #2E7D46 } 是绿,
                // .me/.foe 都是红):稿用色区分敌我——召唤物是友军,红色会被误读成类敌方单位。
                var hpBarGo = Ui.Bar(info.transform, summon.MaxHp > 0 ? shownHp / (float)summon.MaxHp : 0f,
                    Theme.DoneGreen, new Vector2(infoWidth, SummonHpBarHeight));
                _summonBarByCore[i] = ((RectTransform)hpBarGo.transform.Find("Fill"), null);

                // 盾条(2026-08-26)接在血条下面,**常驻** —— 0 时是一条空条,不再有无盾时
                // 整格塌一行、加盾时又顶回来的跳动。动画期间画出手前值(与血条同理),
                // Shield / SummonHit 触达才推
                int shownShield = Animating && _summonAnimShield.TryGetValue(i, out var preShield)
                    ? preShield : summon.Shield;
                var shieldBarGo = Ui.Bar(info.transform, Mathf.Clamp01(shownShield / ShieldBarFull), Theme.Gold,
                    new Vector2(infoWidth, EnemyShieldBarHeight));
                _summonShieldBarByCore[i] = ((RectTransform)shieldBarGo.transform.Find("Fill"), null);

                // 行动条:仍走共享 ActionBar helper(带百分比叠字),与玩家条同一取舍,
                // 只是颜色/soon 态已经在 helper 里统一改过(见 ActionBar 方法注释)。
                _summonActionBarByCore[i] = ActionBar(info.transform, summon.ActionMeter,
                    new Vector2(infoWidth, EnemyActionBarHeight), 8);

                if (_slotPicking) AttachSlotPicker(cell.transform, summonIndex);
                // 友方选目标态(2026-08-22 治疗;2026-08-26 起护盾同走这条):判据走
                // Battle.CanHealSlot,不是「反正画出来的都是存活的所以恒真」——这里就是与
                // Cast 内部同一条判据的落地,规则改了这里自动跟着改,不必表现层另猜一遍。
                else if (_allyTargeting && Battle.CanHealSlot(summonIndex))
                    AttachAllyTargetPicker(cell.transform, summonIndex);
            }
        }

        /// <summary>未解锁的槽位(2026-08-27 用户拍板「常态化显示」):画一块压暗的占位 +
        /// 「N 关解锁」。
        ///
        /// 与空槽(<see cref="DrawEmptySummonSlot"/>)刻意长得不一样:空槽是「现在没人、可以落」,
        /// 锁格是「这一层根本还没这格」—— 两者都点不出东西,但玩家要分得清是自己没召还是没开。
        /// 不挂 <see cref="AttachSlotPicker"/>:落位只在开着的格里选(那边也有同一条守卫)。
        ///
        /// 解锁层数走 <see cref="MetaRules.UnlockDepthForSlot"/> —— 与决定「这一层开几格」的
        /// 是同一张档位表,表现层不自己数第二遍。</summary>
        private void DrawLockedSummonSlot(int slot, bool front)
        {
            var cell = Ui.VStack(front ? _summonFrontRow : _summonBackRow, $"SummonLocked{slot}",
                SummonStackSpacing);
            var cellElement = cell.AddComponent<LayoutElement>();
            cellElement.preferredWidth = SummonCellWidth;
            cellElement.preferredHeight = front ? SummonCellHeightFront : SummonCellHeightBack;
            _summonCellByCore[slot] = (RectTransform)cell.transform;

            float glyphSize = front ? SummonPortraitFront : SummonPortraitBack;
            var plate = Ui.Panel(cell.transform, "Lock");
            var image = plate.AddComponent<Image>();
            image.sprite = Theme.Rounded(12);
            image.type = Image.Type.Sliced;
            image.color = new Color(Theme.InkSoft.r, Theme.InkSoft.g, Theme.InkSoft.b, 0.08f);
            image.raycastTarget = false;
            var plateElement = plate.AddComponent<LayoutElement>();
            plateElement.preferredWidth = SummonCellWidth;
            plateElement.preferredHeight = glyphSize;

            // 锁图标 + 层数分两行:一行放不下「[封] 30 关解锁」而不挤(格宽 289,字号 11)
            var lockGlyph = Ui.ThemedLabel(plate.transform, Icons.Fallback("seal"),
                Mathf.RoundToInt(glyphSize * 0.34f), Theme.LockGray, Theme.TitleFont);
            Ui.Anchor(lockGlyph.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(-glyphSize / 2f, -glyphSize * 0.58f), new Vector2(glyphSize / 2f, -2f));
            var hint = Ui.ThemedLabel(plate.transform,
                Strings.T("battle.summon.slot_locked", ("depth", MetaRules.UnlockDepthForSlot(slot))),
                11, Theme.LockGray);
            Ui.Anchor(hint.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(4f, 2f), new Vector2(-4f, glyphSize * 0.36f));
        }

        /// <summary>空槽(2026-08-20;2026-08-21 两次调整后的口径):**平时什么也不画**,
        /// 只靠 LayoutElement 把格子的位置占住,布局因此照样不跳动;
        /// **选位/拖放时**才画出字块位置的占位块,让玩家看得出召唤物会落在哪儿、多大。
        ///
        /// 尸体槽平时也走这里(引擎从不移除阵亡召唤物,<c>Alive == false</c> 的条目一直占着槽),
        /// 选位时画的是实色底 + 原字:点它的后果与空槽一样(直接落位、不弹确认),
        /// 看到的东西却不该一样。</summary>
        private void DrawEmptySummonSlot(int slot, bool front, SummonState corpse = null)
        {
            var cell = Ui.Panel(front ? _summonFrontRow : _summonBackRow, $"SummonEmpty{slot}");
            var cellElement = cell.AddComponent<LayoutElement>();
            cellElement.preferredWidth = SummonCellWidth;
            cellElement.preferredHeight = front ? SummonCellHeightFront : SummonCellHeightBack;
            _summonCellByCore[slot] = (RectTransform)cell.transform;
            // 占位块**只在选位态出现**(2026-08-21 用户两次拍板的合并结果):
            // 平时六格常驻在屏幕上是纯噪音,所以先去掉了;但选位/拖放的那一刻它恰恰有用 ——
            // 翠玉高亮铺的是整格,而这块淡墨圆角块画在**字块的确切位置**上,
            // 玩家因此看得出召唤物落下来会长在哪儿、多大。
            //
            // 平时什么都不画也不会让布局跳动 —— 撑住格子的是上面那个 LayoutElement,不是这块图。
            if (_slotPicking)
            {
                float glyphSize = front ? SummonPortraitFront : SummonPortraitBack;
                bool showCorpse = corpse != null;
                var ghost = Ui.Panel(cell.transform, showCorpse ? "Corpse" : "Ghost");
                var image = ghost.AddComponent<Image>();
                image.sprite = Theme.Rounded(12);
                image.type = Image.Type.Sliced;
                // 尸体槽用实色 LockedBg 压住,空槽用淡墨 —— 点它们的后果一样(直接落位、
                // 不弹确认),但玩家得看得出这一格是空的还是躺着一具尸体。
                image.color = showCorpse
                    ? Theme.LockedBg
                    : new Color(Theme.InkSoft.r, Theme.InkSoft.g, Theme.InkSoft.b, 0.12f);
                image.raycastTarget = false; // 不吃点击:让点击落到 AttachSlotPicker 那一层上
                // 与实格的字块对齐:实格是 VStack 从顶排下来,字块贴格顶
                Ui.Anchor((RectTransform)ghost.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(-glyphSize / 2f, -glyphSize), new Vector2(glyphSize / 2f, 0f));
                if (showCorpse)
                {
                    var corpseGlyph = Ui.ThemedLabel(ghost.transform, corpse.Char,
                        Mathf.RoundToInt(glyphSize * 0.46f), Theme.LockGray, Theme.TitleFont);
                    Ui.Stretch(corpseGlyph.rectTransform);
                }
                AttachSlotPicker(cell.transform, slot);
            }
        }

        /// <summary>选位子态的整格点击层(2026-08-20):盖满一格吃下点击。
        /// 整格而不是只有字块 —— 移动端手指落点粗,180×98 的格比 56 的字块好点得多。
        /// 拖拽落位共用同一套高亮:起拖即点亮,松手落在哪一格由 <c>SummonSlotAt</c> 判。
        ///
        /// 2026-08-21:去掉「已选/未选」两态与「第 N 只」角标 —— 改成只选一次、多只顺延之后,
        /// 不再存在「选到一半」的中间态,六格永远同色可选。去重也不再靠这里把关:
        /// 顺延在环上取连续 N 个位(N ≤ 6),下标天然互不重复。</summary>
        private void AttachSlotPicker(Transform cell, int slot)
        {
            var overlay = Ui.Panel(cell, "SlotPick");
            // 实格是 VStack:不忽略布局的话这一层会被当成第五行排进去,把整格挤变形
            overlay.AddComponent<LayoutElement>().ignoreLayout = true;
            var image = overlay.AddComponent<Image>();
            image.sprite = Theme.Rounded(12);
            image.type = Image.Type.Sliced;
            image.color = new Color(Theme.Jade.r, Theme.Jade.g, Theme.Jade.b, 0.14f);
            Ui.Stretch((RectTransform)overlay.transform);
            var button = overlay.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = image;
            button.onClick.AddListener(() => OnSlotPicked(slot));
        }

        /// <summary>治疗选目标态的整格点击层(2026-08-22),与 <see cref="AttachSlotPicker"/> 同款做法:
        /// 盖满一格吃下点击,而不是只挂在字块/血条这类小面积上——移动端手指落点粗。
        /// <paramref name="cell"/> 在召唤物是格子的 VStack,在玩家是 <c>Hp</c> 那个 VStack,
        /// 两处都要 <c>ignoreLayout</c>,否则这层覆盖会被当成一行排进布局,把整格挤变形。</summary>
        private void AttachAllyTargetPicker(Transform cell, int slot)
        {
            var overlay = Ui.Panel(cell, "AllyPick");
            overlay.AddComponent<LayoutElement>().ignoreLayout = true;
            var image = overlay.AddComponent<Image>();
            image.sprite = Theme.Rounded(12);
            image.type = Image.Type.Sliced;
            image.color = new Color(Theme.Jade.r, Theme.Jade.g, Theme.Jade.b, 0.14f);
            Ui.Stretch((RectTransform)overlay.transform);
            var button = overlay.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = image;
            button.onClick.AddListener(() => OnAllyTargetPicked(slot));
        }

        /// <summary>进入友方选目标态(2026-08-27 抽出,四个入口共用:点「出字」、拖到敌人身上
        /// 松手后的第二段、拖纯友方字、点敌人转第二段)。
        /// enemyTarget = 第一段已经选过的敌人下标;纯友方字传 −1。
        ///
        /// <paramref name="attackMode"/> 在这里设 <see cref="_pendingAttackMode"/>(2026-09-02
        /// 终审修复):此前靠每个调用方自己在调用前手动赋值,拖放路径漏了这一步,松手后
        /// <see cref="OnAllyTargetPicked"/> 读到默认值 false,把攻击面(如 澡/杜/壁)当成了
        /// 护面出。改成参数化,新调用方不会再有第二次漏掉的机会。</summary>
        private void EnterAllyTargeting(CharDef def, int enemyTarget, bool attackMode)
        {
            _targeting = false;
            _allyTargeting = true;
            _pendingAllyEnemyTarget = enemyTarget;
            _pendingAttackMode = attackMode;
            _message = Strings.T("battle.hint.cast_pick_ally_target", ("charId", def.Id));
        }

        /// <summary>该屏幕坐标落在哪个**友方**落点上(2026-08-27,拖治疗/加盾用)。
        /// 命中返回 true 并给出 slot(<see cref="Targeting.PlayerTarget"/> = 玩家本人)。
        ///
        /// 判据走 <see cref="BattleEngine.CanHealSlot"/> —— 与覆盖层、与引擎 Cast 内部同一条,
        /// 尸体槽因此接不住这一拖(与点击那条路径同口径)。
        /// 返回值用 out 而不是「−1 表示没命中」:−1 是玩家本人这个合法落点。</summary>
        private bool TryGetAllySlotAt(Vector2 screenPos, out int slot)
        {
            foreach (var pair in _summonCellByCore)
                if (pair.Value != null && Battle.CanHealSlot(pair.Key)
                    && RectTransformUtility.RectangleContainsScreenPoint(pair.Value, screenPos, null))
                {
                    slot = pair.Key;
                    return true;
                }
            if (_playerAllyRect != null && Battle.CanHealSlot(Targeting.PlayerTarget)
                && RectTransformUtility.RectangleContainsScreenPoint(_playerAllyRect, screenPos, null))
            {
                slot = Targeting.PlayerTarget;
                return true;
            }
            slot = Targeting.PlayerTarget;
            return false;
        }

        /// <summary>治疗目标选定(2026-08-22):slot == Targeting.PlayerTarget 治玩家,
        /// 否则治该槽位的召唤物。落点仍要过一遍 Battle.CanHealSlot 兜底——覆盖层理应只在
        /// 合法目标上出现,这里再挡一次是防守式编程,不是第二套判据。</summary>
        private void OnAllyTargetPicked(int slot)
        {
            if (!_allyTargeting || _selectedChar == null) return;
            if (!Battle.CanHealSlot(slot)) return;
            string charId = _selectedChar;
            int libraryIndex = _selectedIndex;
            int enemyTarget = _pendingAllyEnemyTarget; // 第一段选过的敌人;纯友方字为 −1
            _pendingAllyEnemyTarget = -1;
            BeginCast(charId, enemyTarget, attackMode: _pendingAttackMode, libraryIndex: libraryIndex, allySlot: slot);
        }

        /// <summary>点召唤物 = 看详情(2026-08-15),与点敌人(<see cref="OnEnemyClicked"/>)对称;
        /// 治疗选目标态下(2026-08-22)则改为选中该召唤物为治疗目标。与
        /// <see cref="OnEnemyClicked"/> 在 <c>_targeting</c> 时的写法同一套纪律:
        /// 不可治的槽位平时压根不会走到这(<see cref="AttachAllyTargetPicker"/> 的覆盖层
        /// 只盖在 <c>CanHealSlot</c> 为真的格子上,玩家点不到这枚按钮),这里再判一遍纯属兜底。</summary>
        private void OnSummonClicked(int index)
        {
            if (index < 0 || index >= Battle.Summons.Count) return;
            var summon = Battle.Summons[index];
            if (_allyTargeting)
            {
                if (!Battle.CanHealSlot(index)) return;
                OnAllyTargetPicked(index);
                return;
            }
            if (summon == null || !summon.Alive) return;
            // 2026-09-01 改走 UnitSheet + SummonInfo.Sheet(单位详情轮二 Task 5),替掉原来
            // 拼一段文本塞进通用 Ui.Modal 的写法。刷新时召唤物可能已经阵亡(槽位变 null 或
            // Alive 变 false)——那种情形下不再调 SummonInfo.Sheet(它不吃 null),让 Refresh
            // 那边的重建逻辑顺手关掉详情。
            if (_modal != null) Object.Destroy(_modal);
            _unitSheetSource = () =>
            {
                var s = Battle.Summons[index];
                return s != null && s.Alive ? SummonInfo.Sheet(s) : null;
            };
            _modal = UnitSheet.Show(transform, _unitSheetSource());
        }

        /// <summary>点玩家条 = 看详情(2026-09-01,单位详情轮二 Task 5)——执笔人此前没有任何
        /// 点击入口。与 <see cref="OnEnemyClicked"/> 同一套纪律:选目标态优先,治疗选目标态下
        /// (<c>_allyTargeting</c>)改成把玩家选为治疗目标;够不到时(<c>!CanHealSlot</c>)直接
        /// 忽略,不落到下面的看详情分支——同 <see cref="OnEnemyClicked"/> 的注释,落下去会让
        /// 玩家以为自己点歪了。绑在 <c>_bottomRow</c> 自己身上的按钮见 <c>BuildSkeleton</c>;
        /// <see cref="AttachAllyTargetPicker"/> 选目标态下会在它身上再叠一层子物件覆盖层,
        /// 子物件的 Graphic 天然盖住父物件自己的 Graphic,点击先命中那层,不需要额外互斥判断。
        /// 另外一层不会撞见的情形是拖字牌打怪(<see cref="DragToAttack"/>)松手落在玩家条上——
        /// uGUI 的 Button 点击要求「按下目标 == 抬起目标」,而拖放的按下发生在字牌卡片自己身上,
        /// 不在 <c>_bottomRow</c> 上,松手时哪怕正好压在这颗按钮上也命中不了 click,天然不会
        /// 误开详情。</summary>
        private void OnPlayerClicked()
        {
            if (_allyTargeting)
            {
                if (!Battle.CanHealSlot(Targeting.PlayerTarget)) return;
                OnAllyTargetPicked(Targeting.PlayerTarget);
                return;
            }
            if (_modal != null) Object.Destroy(_modal);
            _unitSheetSource = () => PlayerInfo.Sheet(Battle, _meta);
            _modal = UnitSheet.Show(transform, _unitSheetSource());
        }

        /// <summary>召唤物被动的一行提示,让玩家看得出这只树跟别的树不一样。
        /// 一只召唤物只有一种被动(数据侧如此),所以取第一个非零项即可。
        /// 禁用 emoji —— 字体子集补不出来,上线渲染成空框。</summary>
        private static string SummonPassiveTag(SummonPassive passive)
        {
            if (passive == null) return "";
            if (passive.OnHitBurn > 0)
                return passive.OnHitBurnAll
                    ? Strings.T("battle.summon.burn_all", ("n", passive.OnHitBurn))
                    : Strings.T("battle.summon.burn_attach", ("n", passive.OnHitBurn));
            if (passive.Thorns > 0) return Strings.T("battle.summon.thorns", ("n", passive.Thorns));
            if (passive.HealAlly > 0) return Strings.T("battle.summon.heal", ("n", passive.HealAlly));
            if (passive.OnHitCurse > 0) return Strings.T("battle.summon.curse", ("n", passive.OnHitCurse));
            if (passive.Dodge > 0) return Strings.T("battle.summon.dodge", ("n", passive.Dodge));
            if (passive.Speed > 100) return Strings.T("battle.summon.haste");
            return "";
        }

        // 敌人格尺寸(2026-08-30 横排复原,用户拍板)。竖排(2026-08-21~2026-08-30)期间
        // 中区 602px 宽,竖排却只占用形象那一份直径,横向本就是这块屏最富余的资源。
        // 稿(Battle.dc.html .foe/.foeslot)写明了理由:横排后格高由立绘单独决定
        // (信息列 36 < 立绘 60),纵向反而比竖排省 24px/排,省下的全给敌我之间的留白。
        // 尺寸 = 稿 pt × 2.093(逻辑单位换算,与骨架其余常量同一套换算)。
        private const float EnemyCellWidth = 293f;        // 稿 140pt
        private const float EnemyCellHeightFront = 138f;  // 稿 66pt
        private const float EnemyCellHeightBack = 109f;   // 稿 52pt
        private const float EnemyPortraitFront = 126f;    // 稿 60pt
        private const float EnemyPortraitBack = 96f;      // 稿 46pt

        // 立绘与信息列的间距(稿 .foe { gap: 6px })。信息列自身的行距(稿 .info { gap: 2px }),
        // 头行里名字与属性徽章的间距(稿 .hd { gap: 4px })——同一套 ×2.093 换算。
        private const float EnemyBlkInfoGap = 13f;   // 6pt
        private const float EnemyInfoSpacing = 4f;   // 2pt
        private const float EnemyHeaderSpacing = 8f; // 4pt

        // 三条状态条的高度(稿:血条 7pt、护盾条/行动条各 3pt)。宽度不设常量——
        // 前排、后排、Boss 跨列的信息列宽各不相同,在每只敌人的绘制现场按实际格宽算。
        private const float EnemyHpBarHeight = 15f;
        private const float EnemyShieldBarHeight = 6f;
        private const float EnemyActionBarHeight = 6f;

        // 元素徽章(稿 .els):头行里跟名字并排的小胶囊,字号压到比状态 chip 还小——
        // 它只需要装得下最长的单字元素名(火/水/木/金/土/问号),不必跟 chip 抢可读性预算。
        private const int ElementBadgeFontSize = 10;
        private const int ElementBadgePadX = 6;
        private const int ElementBadgePadY = 4;

        // 护盾角标(稿 .sh):叠在立绘左下角,与护盾条同一判据——Shield > 0 才画。
        // 敌人 Shield 眼下恒为 0(2026-08-30 拍板,来源是将来的加盾辅助怪),真机看不到属预期,
        // 别因为试玩看不到就以为没接上,也别为了让它显形去给哪只怪配盾。
        private const int ShieldBadgeFontSize = 10;
        private const int ShieldBadgePadX = 4;
        private const int ShieldBadgePadY = 3;
        private const float ShieldBadgeMargin = 4f;

        // 空格位虚线框(稿 .foeslot { border: 1px dashed #C6BCA8; opacity: .45 })——
        // uGUI 没有虚线,用 Ui.OutlinedPanel 的实线 + 45% 透明近似。这是「每排恒定 4 格」
        // 在屏上真正成立的那一半:打死一只怪之后,其余的不该跳位,空位也要看得见。
        private const int SlotFrameRadius = 12;
        private const float SlotFrameThickness = 2f;

        /// <summary>护盾条的填充比例(稿 Battle.dc.html 的 shieldPct):按各自血量归一,
        /// 盾达到自身血量的 1/4 时满格,与稿一致。玩家/召唤物那两条盾条走的是另一套
        /// 绝对刻度(<see cref="ShieldBarFull"/>)——两套刻度并存是 2026-08-26 的既有决策,
        /// 不是本轮引入,敌人这条沿用的仍是本函数的归一算法。</summary>
        private static float ShieldFraction(int shield, int maxHp) =>
            maxHp <= 0 ? 0f : Mathf.Min(1f, shield * 4f / maxHp);

        // 敌人格 chip 行(2026-08-11 换行改造)。比默认 chip 紧一档(字号 12→11、
        // 内边距 18/12→12/8、间距 5→4):实测「火 攻12 灼烧6 不灭」从 2 行降回 1 行,
        // 「水 攻15 承伤 灼烧9 不灭 致盲−50% 沉默」从 3 行降到 2 行,
        // 且两行只多要 17px 而不是 27px —— 这是 12px 预算能成立的前提。
        // 上限 2 行:3 行要再吃 22px,超出的按列表顺序从尾部丢,末尾补「+N」。
        // 2026-08-30:横排后可用宽度不再是整格宽,是信息列宽(前/后排、Boss 跨列各不相同),
        // 在绘制现场算,不设常量。
        private const int ChipFontSize = 11;
        private const int ChipPadX = 12;
        private const int ChipPadY = 8;
        private const float ChipSpacing = 4f;
        private const float ChipLineSpacing = 3f;
        private const int ChipMaxLines = 2;

        // 拖字打人悬停预览(2026-08-22):主目标复用「选目标态整格微亮」的既有强度(0.07f,
        // 见 DrawEnemies 的 hitArea.color),被形状溅到的用更淡一档,与主目标拉开区分。
        private const float HoverPreviewPrimaryAlpha = 0.07f;
        private const float HoverPreviewSplashAlpha = 0.035f;

        /// <summary>敌方两排(2026-08-20):后排在上、前排在下(贴着中间的分隔线),
        /// 站位读 <see cref="EnemyState.Row"/> —— 那是**实例状态**,开场按每排上限 4 分配、
        /// 溢出会改判,和 <c>EnemyDef.Row</c> 那个偏好不是一回事。
        ///
        /// ⚠ 下标对齐:<c>_enemyRects</c> / <c>_enemyMobs</c> / <c>_enemyHpBars</c> /
        /// <c>_enemyActionBars</c> 四个列表全都按**敌人下标**索引(事件的 TargetIndex 直接拿去取),
        /// 所以下面仍是一层按 i 升序的循环、每轮四个列表各 Add 一次 —— 这四个列表的顺序
        /// 不能变。不能改成「先画前排再画后排」那种按排遍历,列表顺序会与 Battle.Enemies
        /// 错开,打谁就抖谁那套全部指错人。
        ///
        /// 2026-08-22 固定格位:每排预先建好 <see cref="Targeting.RowCapacity"/> 个
        /// 固定格位(Transform 意义上的**子物体顺序**按列 0..2,与上面 i 升序的四个列表顺序
        /// 无关),敌人按 <c>(Row, Column)</c> 落进对应格位,空格位只留一个带
        /// <see cref="LayoutElement"/>(仅 preferredWidth)的透明占位撑宽度,不画任何可见元素。
        /// 这是为了让 <see cref="Ui.Row"/> 的 <c>HorizontalLayoutGroup</c>(子物体整体
        /// TextAnchor.MiddleCenter,见 Ui.cs)把两排都摆满同样多格再居中 —— 此前前排 2 只、
        /// 后排 3 只时各自居中,列对不上。副产品:敌人死后不再因为「按存活数重排」而整体
        /// 跳位,尸体格位原地不动。</summary>
        private void DrawEnemies()
        {
            _enemyRects.Clear();
            _enemyMobs.Clear();
            _enemyHpBars.Clear();
            _enemyActionBars.Clear();
            _enemyHitAreas.Clear();
            // 悬停预览引用的都是这次要被清掉的旧格子:整屏重绘期间不可能还在拖拽中
            // (拖拽中间只有 RedrawSummonRows 那条不动敌人区的路径),但保险起见清空防悬空引用。
            _hoverPreviewPrimary = -1;
            _hoverPreviewCells.Clear();

            var frontCells = new GameObject[Targeting.RowCapacity];
            var backCells = new GameObject[Targeting.RowCapacity];
            for (int c = 0; c < frontCells.Length; c++)
            {
                frontCells[c] = Ui.Panel(_enemyFrontRow, $"EnemySlotFront{c}");
                Ui.Sized(frontCells[c], width: EnemyCellWidth, height: EnemyCellHeightFront);
            }
            for (int c = 0; c < backCells.Length; c++)
            {
                backCells[c] = Ui.Panel(_enemyBackRow, $"EnemySlotBack{c}");
                Ui.Sized(backCells[c], width: EnemyCellWidth, height: EnemyCellHeightBack);
            }
            // 本次绘制里每排格位是否已被占用(2026-08-22 评审加固)。按**本次绘制**已用掉的
            // 格位算,不读 Transform.childCount —— 预建的空格位本来就在那儿,child 数恒为
            // RowCapacity,读它算不出"谁占了谁没占"。
            var frontUsed = new bool[frontCells.Length];
            var backUsed = new bool[backCells.Length];

            for (int i = 0; i < Battle.Enemies.Count; i++)
            {
                var enemy = Battle.Enemies[i];
                int index = i;
                bool front = enemy.Row == EnemyRow.Front;
                // 死亡动画进行中的怪:重绘时仍保持着色挨打,置灰交给死亡节拍(GreyOut),别在重绘时就变灰
                bool dying = _dyingEnemies.Contains(index);
                bool showAlive = enemy.Alive || dying;
                // 选目标态下够不到的怪置灰且不可点(2026-08-20)。判据**一律走 Battle.CanTarget**,
                // 表现层不自己推排位规则 —— CanTarget 读的是 EnemyState.Row 这个实例状态,
                // 「配置就在后排的怪 / 前排满员被改判到后排的怪 / 叠字分裂出的克隆」三种来源
                // 一次覆盖;照 EnemyDef.Row 那个偏好自己算一套,后两种一定漏。
                bool reachable = !_targeting || _selectedChar == null
                    || Battle.CanTarget(_graph.Get(_selectedChar), index, _pendingAttackMode);

                // 格位守卫(2026-08-22 评审加固,2026-08-30 扩到跨列):Core 的不变式
                // (每排 ≤ RowCapacity、列不重号)理应保证 enemy.Column/ColumnSpan 永远落在
                // [0, RowCapacity) 且同排不重叠,但表现层崩掉的代价是整个战斗界面白屏,
                // 兜底的代价只是一只怪画错位置 —— 不对称,所以兜。
                // 越界或撞列就回落到本排第一个能放下连续 span 列的起点 —— 跨列的怪
                // (Boss)不能只找单列空位,回落到的那一格未必放得下它的 ColumnSpan。
                var cells = front ? frontCells : backCells;
                var used = front ? frontUsed : backUsed;
                int span = enemy.Def.ColumnSpan;
                int col = enemy.Column;
                bool fits = col >= 0 && col + span <= cells.Length;
                for (int c = col; fits && c < col + span; c++)
                    if (used[c]) fits = false;
                if (!fits) col = FindFreeRun(used, span);
                if (col < 0)
                {
                    // 连回落都没有空格位:说明本排存活敌人数已经超过 RowCapacity,违反 Core
                    // 不变式,比越界/撞列更极端。不画这只怪,但仍要给四个下标对齐的列表
                    // (_enemyRects/_enemyMobs/_enemyHpBars/_enemyActionBars)占一位 ——
                    // 不然后续敌人的下标全部错位,TargetIndex 指错人(比不画更糟)。
                    _enemyMobs.Add(null);
                    _enemyHpBars.Add((null, null));
                    _enemyActionBars.Add((null, null));
                    _enemyRects.Add(null);
                    _enemyHitAreas.Add(null);
                    continue;
                }
                for (int c = col; c < col + span; c++) used[c] = true;
                // 跨列的怪(眼下只有 Boss)吞掉的预建空格位要销毁,不是隐藏——留着会把行撑宽
                for (int c = col + 1; c < col + span; c++)
                {
                    if (cells[c] == null) continue;
                    Object.Destroy(cells[c]);
                    cells[c] = null;
                }

                var cell = cells[col];
                var cellElement = cell.GetComponent<LayoutElement>();
                cellElement.preferredHeight = front ? EnemyCellHeightFront : EnemyCellHeightBack;
                // 宽度按 span 算,把被它吞掉的那几条格间距也算进去,否则会比整排窄
                // (span-1) 个 RowGap
                cellElement.preferredWidth = EnemyCellWidth * span + RowGap * (span - 1);
                float portraitSize = front ? EnemyPortraitFront : EnemyPortraitBack;
                float infoWidth = cellElement.preferredWidth - portraitSize - EnemyBlkInfoGap;
                // 立绘在左、信息列在右(稿:横排格高由立绘单独决定,比竖排省 24px/排),
                // 2026-08-31 收口成 Ui.UnitBlock,与召唤格/玩家条同一套写法。
                Ui.UnitBlock(cell.transform, "Block", portraitSize, infoWidth, EnemyBlkInfoGap,
                    out var portraitMount, out var info);
                info.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.UpperLeft;

                // 有形象就用分层字怪(Boss 按当前阶段取图),否则回落圆形字头像
                MobView mob = null;
                GameObject portrait = null;
                string prefix = MobAssets.PrefixFor(enemy.Def, enemy.PhaseIndex);
                if (MobAssets.Layer(prefix, "body") != null)
                {
                    // 死了也照旧画形象,只是置灰 —— 换回字头像会让尸体「跳」一下形。
                    // 挂载点已经是 Ui.UnitBlock 按 portraitSize 定好尺寸的节点,MobView
                    // (它自己只设 sizeDelta,不是 ILayoutElement)直接加在它身上即可,
                    // 不必再另建一个节点——重新挂名字方便层级里辨认是哪只怪。
                    portrait = portraitMount.gameObject;
                    portrait.name = $"Mob{i}";
                    mob = portrait.AddComponent<MobView>();
                    mob.Init(prefix, portraitSize);
                    mob.SetStateAmount(MobAssets.StateAmountFor(enemy)); // L4 绑战斗状态
                    if (!showAlive || !reachable) mob.ApplyTint(Theme.LockedBg);
                }
                if (portrait == null)
                {
                    portrait = Ui.CircleGlyph(portraitMount,
                        EnemyInfo.FaceChar(enemy.Def, enemy.PhaseIndex),
                        showAlive && reachable ? Theme.ElementColor(enemy.ApparentElement) : Theme.LockedBg,
                        // 白字压在 LockedBg 这种浅底上看不见:置灰的一并把字色降到 TextDim
                        showAlive && reachable ? Color.white : Theme.TextDim, portraitSize);
                    Ui.Stretch((RectTransform)portrait.transform); // 挂载点已定好尺寸,铺满即可
                }
                _enemyMobs.Add(mob);
                if (_targeting && enemy.Alive && reachable && mob == null)
                {
                    var outline = portrait.AddComponent<Outline>(); // 圆头像用描边示意可选中
                    outline.effectColor = Theme.Ink;
                    outline.effectDistance = new Vector2(3, 3);
                }
                // 护盾角标(稿 .sh):叠在立绘左下角,Shield > 0 才画(与护盾条同一判据)
                if (enemy.Shield > 0)
                {
                    var badge = Ui.Chip(portrait.transform, enemy.Shield.ToString(), Theme.Gold, Theme.GoldText,
                        ShieldBadgeFontSize, ShieldBadgePadX, ShieldBadgePadY, "shield");
                    var badgeElement = badge.GetComponent<LayoutElement>();
                    Ui.Anchor((RectTransform)badge.transform, Vector2.zero, Vector2.zero,
                        new Vector2(ShieldBadgeMargin, ShieldBadgeMargin),
                        new Vector2(ShieldBadgeMargin + badgeElement.preferredWidth,
                            ShieldBadgeMargin + badgeElement.preferredHeight));
                }

                // 点击区盖满整格:形象各层不吃 raycast(见 MobView),没有它整格点不动
                var hitArea = cell.AddComponent<Image>();
                hitArea.color = _targeting && enemy.Alive && reachable
                    ? new Color(Theme.Ink.r, Theme.Ink.g, Theme.Ink.b, 0.07f) // 选目标时整格微亮,提示可点
                    : new Color(0, 0, 0, 0);
                _enemyHitAreas.Add(hitArea); // 拖字打人悬停预览要就地改这个颜色,存引用

                // 信息列:名字+属性徽章一行、chip 一行、血条、护盾条、行动条,自上而下(稿 .info)。
                // info 已由上面的 Ui.UnitBlock 建好(VStack + 定宽 LayoutElement),这里只按
                // 敌人格自己的口径补 spacing——与 childAlignment 同理,UnitBlock 建的时候
                // 还不知道这个值(4f 与 VStack 默认值巧合相同,显式写出不依赖这个巧合)。
                info.GetComponent<VerticalLayoutGroup>().spacing = EnemyInfoSpacing;

                // 头行:名字 + 五行属性徽章(稿 .hd/.els)。元素徽章从 chip 行搬到这里——
                // 与名字一起才是「这是谁」,不该跟灼烧/致盲那些战况 chip 混排。
                var header = Ui.Row(info.transform, "Header", EnemyHeaderSpacing);
                Ui.ThemedLabel(header.transform, BossTitle(enemy), 15, Theme.TextMain, Theme.TitleFont);
                // 显示用的元素名走 CharInfo.ElementName(查表)。
                string elementName = enemy.ApparentElement is { } apparent ? CharInfo.ElementName(apparent) : "?";
                Ui.Chip(header.transform, elementName, Theme.ElementColor(enemy.ApparentElement), Color.white,
                    ElementBadgeFontSize, ElementBadgePadX, ElementBadgePadY);

                // chip 行:攻击模式/技能特性/debuff/DoT。列表顺序即优先级:装不下 ChipMaxLines
                // 行时从**尾部**丢弃,末尾补「+N」,所以越靠前的越保得住。
                // 完整信息仍在敌人详情弹窗里。
                var chipSpecs = new List<Ui.ChipSpec>
                {
                    new(Strings.T("battle.label.enemy_attack", ("attack", enemy.Attack)), Theme.PaperDim, Theme.TextMain),
                };
                if (enemy.Defense > 0)
                    chipSpecs.Add(new(Strings.T("enemy.defense_chip", ("defense", enemy.Defense)), Theme.InkSoft, Color.white));
                // 读 ChargingSkill 而不是当前阶段的技能:蓄力期间玩家可能把 Boss 推过阶段,
                // 那时阶段技能已经变了,但预告过的大招不改口(2026-07-29)
                if (enemy.IsCharging && enemy.IsBoss)
                    // 别用 emoji:⚡ 不在 Noto Serif SC 里,子集补不出来,上线渲染成空框
                    // (test_subset_fonts_cover_charset 正是拦这个的)。预警靠朱砂底色已经够显眼
                    chipSpecs.Add(new(Strings.T("battle.label.charging_next_turn",
                            ("skillName", EnemyInfo.BossSkillName(enemy.ChargingSkill))),
                        Theme.Cinnabar, Color.white));
                int burnStacks = enemy.Statuses.TotalMagnitude(StatusKind.Burn);
                if (burnStacks > 0)
                    chipSpecs.Add(new($"{burnStacks}", Theme.Cinnabar, Color.white, "burn"));
                // 不灭(2026-08-09,炑):灼烧层数不衰减,与灼烧同朱砂系
                if (enemy.Statuses.Has(StatusKind.BurnNoDecay))
                    chipSpecs.Add(new("", Theme.Cinnabar, Color.white, "burn_nodecay"));
                // 冻结 / 减速(2026-08-13):此前这两个状态在敌人身上零显示 —— 冻结的怪不出手、
                // 减速的怪隔回合才出手,玩家只能靠数它哪回合打了自己来倒推。
                // 排在致盲之前:这两条直接回答「它下回合会不会打我」,信息价值高于减伤类,
                // 不该在 ChipFlow 装不下时被从尾部丢掉。
                if (enemy.Statuses.Has(StatusKind.Freeze))
                    chipSpecs.Add(new("", Theme.InkSoft, Color.white, "freeze"));
                // 只画负向:正向 SpeedModifier 眼下没有任何来源(唯一施加点是 EffectKind.Slow 的
                // −50),画加速分支就是死代码。数字是**速度点数**不是百分比,故不带 %。
                int speedMod = enemy.Statuses.TotalMagnitude(StatusKind.SpeedModifier);
                if (speedMod < 0)
                    chipSpecs.Add(new($"−{-speedMod}", Theme.InkSoft, Color.white, "slow"));
                int blind = enemy.Statuses.TotalMagnitude(StatusKind.Blind);
                if (blind > 0)
                    chipSpecs.Add(new($"−{blind}%", Theme.InkSoft, Color.white, "blind"));
                if (enemy.Statuses.Has(StatusKind.Silence))
                    chipSpecs.Add(new("", Theme.InkSoft, Color.white, "silence"));
                int curse = enemy.Statuses.TotalMagnitude(StatusKind.Curse);
                if (curse > 0)
                    chipSpecs.Add(new($"−{curse}%", Theme.InkSoft, Color.white, "curse"));
                // 能力 chip 统一走 EnemyInfo(与详情弹窗同一套命名);
                // 机制失效(叠字已分裂/通假已现形/生僻已读懂)时返回空串,不画
                if (enemy.Alive)
                {
                    string abilityChip = EnemyInfo.AbilityChipText(enemy);
                    if (abilityChip.Length > 0)
                        chipSpecs.Add(new(abilityChip,
                            Theme.AbilityChipColor(enemy.Def.Ability), Color.white));
                }
                // 左右各留 2px:贴着列宽排会让最后一个 chip 卡在边界上,浮点抖一下就换行。
                Ui.ChipFlow(info.transform, "Chips", chipSpecs, infoWidth - 4f, ChipFontSize,
                    ChipMaxLines, ChipPadX, ChipPadY, ChipSpacing, ChipLineSpacing);

                // 存活或濒死(死亡动画中)都画血条:动画期间画出手前值,伤害触达才逐记掉血;
                // 濒死者随死亡节拍置灰,真正死透(动画完)才转「已正」。血值上条,带描边保对比度。
                if (showAlive)
                {
                    int barHp = Animating && i < _animEnemyHp.Count ? _animEnemyHp[i] : enemy.Hp;
                    _enemyHpBars.Add(HpBar(info.transform, barHp, enemy.MaxHp, new Vector2(infoWidth, EnemyHpBarHeight)));
                    // 护盾条(稿 .shb):盾 = 1/4 血时满格,盾为 0 时整条不画——与角标同一判据。
                    // 填充色用 Theme.Gold(稿 #C9A94A,同一族,不为这条 6px 高的细条新增色值)。
                    // 上一版这里误判「稿上 .shb/.atb 背景色相同」而临时改成翡翠避让——其实两者
                    // 在稿上本就不同色,真正撞色的是下面那条行动条(抄错成了金色),已改正,
                    // 两条各自照稿对色后不再冲突。
                    // 敌人 Shield 眼下恒为 0,真机看不到属预期(见常量注释)。
                    if (enemy.Shield > 0)
                        Ui.Bar(info.transform, ShieldFraction(enemy.Shield, enemy.MaxHp), Theme.Gold,
                            new Vector2(infoWidth, EnemyShieldBarHeight));
                    // 行动条紧跟护盾条(2026-08-17,用户拍板放血条下方)。稿 .atb 无文字覆盖——
                    // 高度只有 6,塞百分比数字必糊,故直接调 Ui.Bar 出裸条而不是共享的 ActionBar
                    // helper(那个固定带文字,是给玩家/召唤物的更高条用的)。fill 仍存进
                    // _enemyActionBars 供动画期间就地推进,SetActionBar 对 label == null
                    // 本就判空跳过,不受影响。
                    // 底色跟稿走:.foe/.ally/.me 三种单位的行动条稿上**同色** #3D4E69,
                    // 正是 Theme.InkSoft(一字不差,Theme 里早有这个 token,不是新增)。
                    // >80% 时稿转 .soon 态——敌方转朱砂 #C53637 = Theme.Cinnabar;
                    // 我方/玩家的 soon 态是绿色 #2E7D46,不是同一个色,下个任务给我方条
                    // 补这一态时别照抄这里的朱砂。
                    float actionFrac = Mathf.Clamp01(enemy.ActionMeter / (float)TurnScheduler.Threshold);
                    bool actionSoon = actionFrac > 0.8f;
                    var actionBarGo = Ui.Bar(info.transform, actionFrac,
                        actionSoon ? Theme.Cinnabar : Theme.InkSoft, new Vector2(infoWidth, EnemyActionBarHeight));
                    _enemyActionBars.Add(((RectTransform)actionBarGo.transform.Find("Fill"), null));
                }
                else
                {
                    // 「已正」= 那个错字被改正了(2026-08-23 用户确认语义):字怪死亡时代替血条。
                    // 是主题双关而非机制描述 —— key 名 corpse_settled 说的是机制那一面,别照 key 名去译。
                    Ui.ThemedLabel(info.transform, Strings.T("battle.label.corpse_settled"), 14, Theme.LockGray);
                    _enemyHpBars.Add((null, null));
                    _enemyActionBars.Add((null, null));   // 下标与 _enemyHpBars 严格同步
                }

                var button = cell.AddComponent<Button>();
                button.targetGraphic = hitArea;
                button.onClick.AddListener(() => OnEnemyClicked(index));
                button.interactable = enemy.Alive && reachable; // 够不到:连详情都不弹,免得像点歪了
                _enemyRects.Add((RectTransform)portrait.transform);
            }

            // 空格位画成看得见的虚线框(稿 .foeslot):没有它,「每排恒定 4 格」在屏上就是
            // 一句空话——打死一只怪之后玩家看不出旁边还留着位子。
            DrawEmptyEnemySlots(frontCells, frontUsed);
            DrawEmptyEnemySlots(backCells, backUsed);
        }

        /// <summary>把本排没被占用的格位画成虚线框(稿 .foeslot);已被跨列怪吞掉、
        /// Destroy 置 null 的格位直接跳过——那是 Boss 吃掉的位置,压根不该再占地方。
        /// uGUI 没有虚线,用 <see cref="Ui.OutlinedPanel"/> 的实线 + 45% 透明近似。</summary>
        private static void DrawEmptyEnemySlots(GameObject[] cells, bool[] used)
        {
            for (int c = 0; c < cells.Length; c++)
            {
                if (cells[c] == null || used[c]) continue;
                var frame = Ui.OutlinedPanel(cells[c].transform, "Slot", Color.clear,
                    new Color(Theme.PaperDim.r, Theme.PaperDim.g, Theme.PaperDim.b, 0.45f),
                    SlotFrameRadius, SlotFrameThickness);
                Ui.Stretch((RectTransform)frame.transform);
            }
        }

        /// <summary>本排第一个能放下连续 <paramref name="span"/> 列的起点;放不下返回 −1。
        /// span = 1 时等价于 <c>System.Array.IndexOf(used, false)</c>——跨列的怪(Boss)
        /// 不能只找单列空位,回落到的那一格未必放得下它的 ColumnSpan。</summary>
        private static int FindFreeRun(bool[] used, int span)
        {
            for (int start = 0; start + span <= used.Length; start++)
            {
                bool ok = true;
                for (int c = start; c < start + span; c++)
                    if (used[c]) { ok = false; break; }
                if (ok) return start;
            }
            return -1;
        }

        private const float HandTileW = 96f;    // 稿 .tile { width: 46pt }
        private const float HandTileH = 117f;   // 稿 .tile { height: 56pt }
        private const float HandAdSlotW = 88f;  // 稿 .adslot { width: 42pt }
        private const float HandAdSlotH = 117f; // 稿 .adslot { height: 56pt },与手牌牌面同高

        private const float CountCaptionW = 96f;  // 「部件池 12/12」14 号横排约 88,留 8 的余量

        /// <summary>字库行 / 部件池行行首的计数标题(稿 .handlbl、.pool .lbl)。
        ///
        /// 2026-09-01 用户拍板改版。稿上这两处是 <c>writing-mode: vertical-rl</c>,落地时
        /// 曾按「逐字拆开纵向摞」近似(旧的 VerticalLabel),但「部件池 12/12」有 8 个字符,
        /// 堆起来约 128 单位 —— 比整条字库带(117)还高,一个字一个字往下读也确实别扭。
        /// 现在改回横排一行、**定宽** CountCaptionW:定宽不是为了标题自己好看,是为了让
        /// 两行的第一张牌落在同一个 x 上(标题自然宽度「字库 5/9」与「部件池 12/12」差着
        /// 一大截,不定宽就对不齐)。配合两行都改左对齐,标题与牌各自成一条竖线。</summary>
        private static void CountCaption(Transform parent, string text, int fontSize, Color color)
        {
            var label = Ui.ThemedLabel(parent, text, fontSize, color, null, TextAnchor.MiddleLeft);
            Ui.Sized(label.gameObject, width: CountCaptionW);
        }

        /// <summary>手牌行末尾的广告扩容位(稿 .adslot):常驻显示,用过后转灰而不是消失——
        /// 旧实现里 Ui.AdBadge 只在未扩容时画,扩过容之后这个位置直接空着,看不出「已经
        /// 扩过容了」这条反馈;稿上明确是「已用过时转灰」的持续态,不是用完就撤。
        /// 没有做稿上的虚线边框(dashed)——Unity UI 没有现成的虚线描边,与 Task 6 空敌人格
        /// 同一个坑同一个取舍:实线圆角描边 + 素色近似,不为这一处引入资源或自定义 Shader。</summary>
        private void DrawHandAdSlot()
        {
            bool used = _run.LibraryExpanded;
            var outer = Ui.OutlinedPanel(_libraryRow, "HandAdSlot",
                used ? Theme.LockedBg : Theme.AdGreenBg, used ? Theme.PanelBorder : Theme.AdGreen,
                10, 1.5f, out var face);
            Ui.Sized(outer.gameObject, width: HandAdSlotW, height: HandAdSlotH);
            var stack = Ui.VStack(face.transform, "Stack", 4);
            Ui.Stretch((RectTransform)stack.transform);
            Ui.ThemedLabel(stack.transform,
                used ? Strings.T("battle.btn.hand_ad_used") : Strings.T("battle.btn.hand_ad_slot"),
                11, used ? Theme.LockGray : Theme.AdGreenText);
            if (used) return;
            var button = outer.gameObject.AddComponent<Button>();
            button.targetGraphic = outer;
            button.onClick.AddListener(() => // 原型:点击即生效,SDK 后接
            {
                _run.TryExpandLibrary();
                _onExpanded?.Invoke();
                _message = Strings.T("battle.label.library_cap_up");
                Refresh();
            });
        }

        private void DrawLibrary()
        {
            _libraryTileRects.Clear();
            // 奖励页显示携带字库(出过的字已回归)——这才是下一战的真实字库,也是替换的操作对象
            bool rewardPhase = _run.Phase == RunPhase.Reward;
            var library = rewardPhase ? _run.CarriedLibrary : Battle.Library;
            CountCaption(_libraryRow, Strings.T("battle.label.library_count",
                ("count", library.Count), ("capacity", Battle.LibraryCapacity)), 14, Theme.TextDim);
            if (library.Count == 0)
                Ui.ThemedLabel(_libraryRow, Strings.T("battle.label.library_empty"), 16, Theme.TextDim);
            for (int i = 0; i < library.Count; i++)
            {
                int index = i;
                string charId = library[i];
                var def = _graph.Get(charId);
                // 同字多张按卡位区分选中(2026-08-17):只亮玩家点的那张,不连坐
                bool selected = _selectedChar == charId && _selectedIndex == index && !_targeting && !_allyTargeting;
                System.Action tap = () =>
                {
                    if (rewardPhase) OnRewardLibraryClicked(charId);
                    else OnLibraryCharClicked(charId, index);
                };
                // 2026-08-31 接稿:68×85 → 96×117(稿 46×56pt,比改前大约四成)。满员 12 张
                // 时装不下是预期的(见 BuildSkeleton 里 Mid 那段账),HorizontalLayoutGroup
                // 会等比压窄每格,不会溢出到左右两栏底下——别为这个再改牌宽。
                var tile = Ui.GlyphTile(_libraryRow, def, selected, tap,
                    new Vector2(HandTileW, HandTileH));
                // AP 不够就去饱和压暗、属性动效停(《字牌形象关键词包》§4.4):
                // 「用不了」要在点下去之前就看得出来,不能等弹窗告诉你
                if (!rewardPhase)
                    tile.GetComponent<CardFrameView>()?.SetPlayable(def.ApCost <= Battle.Ap);
                HoldToPreview.Attach(tile.gameObject, () => ShowCharPreview(charId));
                if (!rewardPhase) AttachDragToAttack(tile.gameObject, def, index);
                _tileRects[charId] = (RectTransform)tile.transform;
                _libraryTileRects.Add((RectTransform)tile.transform); // 卡位→牌面,飞字起点按位取
                ApplyFreshGlow(charId, (RectTransform)tile.transform, Theme.ElementColor(def.Element));
            }
            DrawHandAdSlot();
        }

        /// <summary>拖字打人(2026-07-26):拖到敌人身上松手 = 攻击那个敌人。
        /// 水/土 因此在双击的治疗/加盾之外多一个攻击用法;其余字拖放 = 出字并顺手选中目标。
        ///
        /// 2026-08-21 拖字召唤:拖的若是召唤字,**起拖那一刻就点亮 6 个槽位**,松手落在哪一格
        /// 就安置在哪一格。此前只有「松手落在敌人身上」才会进落位态,落在槽位上反而算取消 ——
        /// 而召唤字根本不携带目标信息(全 15 张都是纯召唤,没有 attackEffects),
        /// 「拖到敌人身上才召得出」是把唯一无意义的落点定成了唯一有效的落点。</summary>
        private void AttachDragToAttack(GameObject tile, CharDef def, int libraryIndex = -1)
        {
            // 召唤只数与 AP 都按 attackMode 口径先算一遍:两条都过才点亮槽位。
            // AP 不够时故意**不**点亮 —— 让它走松手时的常规路径,由引擎当场报「AP 不够」,
            // 与点「出字」被拒同口径(见 BeginCast 里那条同因的守卫)。
            bool summons = Battle.SummonCountOf(def, attackMode: true) > 0;
            // 治疗 / 加盾拖拽(2026-08-27 用户拍板,参考拖拽召唤):
            //   · **纯友方字**(㵘/淼/㙓/壁 —— 友方效果 + 群体伤害,不用选敌人):起拖即点亮
            //     友方落点,拖到自己或某只召唤物身上松手即施放。
            //   · **既要敌人又要友方**的(沝/澡/沐/垚/圭/垒):照旧拖到敌人身上,松手后进第二段
            //     点友方 —— 与点「出字」那条路径同一个状态机,不写第二套。
            bool allyOnly = BattleEngine.NeedsAllyTarget(def, attackMode: true)
                && !BattleEngine.NeedsTarget(def, attackMode: true);

            // 字影是**纯文字**压在场景上,得用过了 WCAG 的 GlyphColor 而不是 UI 色块那套
            // ElementColor(金 #B3A382 对宣纸底只有 2.48,拖起来是一团糊的)
            DragToAttack.Attach(tile, def.Id, Theme.GlyphColor(def.Element),
                () => _run.Phase == RunPhase.InBattle && Battle.Phase == BattlePhase.PlayerTurn && !Animating,
                screenPos =>
                {
                    // 松手前先清悬停预览,不管接下来落进哪个分支(2026-08-22)——
                    // 三条分支(落位/取消/出字)都必须清干净,免得留下改过色的格子。
                    ClearHoverPreview();
                    _hoverPreviewPrimary = -1;
                    if (_slotPicking)
                    {
                        int slot = SummonSlotAt(screenPos);
                        // 落在槽位上才算数(2026-08-21 用户拍板):多只召唤从这一格起顺延。
                        if (slot >= 0) { OnSlotPicked(slot); return; }
                        // 落在敌人身上或空白处 = 取消。召唤字不携带目标信息,拖到敌人身上
                        // 本就没有意义,不给它兜底语义;AP 与字库一滴未动,重拖即可。
                        CancelSelection();
                        return;
                    }
                    if (_allyTargeting)
                    {
                        // 纯友方字:落在自己或某只召唤物身上才算数,与召唤落位同一条纪律
                        if (TryGetAllySlotAt(screenPos, out int allySlot))
                        {
                            BeginCast(def.Id, -1, attackMode: true, libraryIndex: libraryIndex,
                                allySlot: allySlot);
                            return;
                        }
                        CancelSelection();
                        return;
                    }
                    int target = EnemyIndexAt(screenPos);
                    if (target < 0) { CancelSelection(); return; } // 没落在敌人身上:当作取消,不出字
                    // 还要选友方就进第二段(2026-08-27),别在这里就出字 —— 与 OnEnemyClicked
                    // 同一条免选口径:场上没有存活召唤物时引擎自动锁玩家,不弹没得选的选择
                    if (BattleEngine.NeedsAllyTarget(def, attackMode: true) && Battle.AliveSummonCount > 0)
                    {
                        _selectedChar = def.Id;
                        _selectedIndex = libraryIndex;
                        EnterAllyTargeting(def, enemyTarget: target, attackMode: true);
                        Refresh();
                        return;
                    }
                    BeginCast(def.Id, target, attackMode: true, libraryIndex: libraryIndex);
                },
                onBeginDrag: !summons && !allyOnly ? null : () =>
                {
                    if (Battle.Ap < def.ApCost) return;
                    if (allyOnly)
                    {
                        // 纯友方字:点亮玩家血条区与可施的召唤格
                        _selectedChar = def.Id;
                        _selectedIndex = libraryIndex;
                        EnterAllyTargeting(def, enemyTarget: -1, attackMode: true);
                        RedrawAllyTargets();
                        return;
                    }
                    EnterSlotPicking(def.Id, -1, attackMode: true, libraryIndex,
                        Battle.SummonCountOf(def, attackMode: true));
                    RedrawSummonRows(); // 只重画召唤两排:全量 Refresh 会销毁正被拖的这张字牌
                },
                onDragMove: screenPos => OnDragHover(screenPos, def));
        }

        /// <summary>拖字打人途中,悬停到某只敌人上方时预览这一发会打到的全部格子(2026-08-22)。
        /// 判据一律走 <see cref="Targeting.ExpandTargets"/>,形状/连发数也一律走
        /// <see cref="BattleEngine.AttackShapeOf"/>(2026-08-22 评审 Finding 2 后从表现层自己
        /// 挑效果列表改成调 Core 的公开 accessor——原先的表现层版本漏了「两个效果列表都空则用
        /// FallbackEffects」那一支)。攻击模式恒传 <c>attackMode: true</c>,与 onDrop 那边传给
        /// <see cref="BeginCast"/> 的值一致(拖字打人这条路径本就是 attackMode)。
        ///
        /// 连发(Volley)没有主目标(<see cref="BattleEngine.NeedsTarget"/> 对它就返回 false),
        /// 这里选择仍然预览它固定会打到的那几格(<c>primaryIndex: -1</c> 求出的表与悬停在
        /// 哪只敌人上无关)——只要指针落在任意一只敌人身上(与松手判定同一条门槛),就整体
        /// 亮出连发会覆盖的格子,不特别标「主目标」(它本来就没有主目标概念)。
        ///
        /// ⚠ 每帧都会调用:只改已存在的 <see cref="_enemyHitAreas"/> 颜色,不重绘任何 GameObject
        /// ——DragToAttack.cs 顶部有整段警告解释为什么(销毁正被拖的对象会掐断 OnEndDrag)。
        /// 悬停格没变时直接 return,不做无用功。</summary>
        private void OnDragHover(Vector2 screenPos, CharDef def)
        {
            // 召唤字走落位预览(起拖已点亮 6 槽),不叠加打人预览
            int target = _slotPicking ? -1 : EnemyIndexAt(screenPos);
            if (target == _hoverPreviewPrimary) return; // 悬停格没变,别做无用功
            _hoverPreviewPrimary = target;
            ClearHoverPreview();

            if (target < 0 || !Battle.CanTarget(def, target, attackMode: true)) return;

            var (shape, shots) = BattleEngine.AttackShapeOf(def, attackMode: true);
            var hits = shape == TargetShape.Volley
                ? Targeting.ExpandTargets(Battle.Enemies, -1, shape, shots) // 连发无主目标,与悬停格无关
                : Targeting.ExpandTargets(Battle.Enemies, target, shape, shots);
            for (int n = 0; n < hits.Count; n++)
            {
                int i = hits[n];
                if (i < 0 || i >= _enemyHitAreas.Count || _enemyHitAreas[i] == null) continue;
                bool primary = shape != TargetShape.Volley && n == 0; // 首项即主目标,Volley 除外
                _hoverPreviewCells.Add((i, _enemyHitAreas[i].color)); // 先存原色,清预览时原样还原
                _enemyHitAreas[i].color = new Color(Theme.Ink.r, Theme.Ink.g, Theme.Ink.b,
                    primary ? HoverPreviewPrimaryAlpha : HoverPreviewSplashAlpha);
            }
        }

        /// <summary>把悬停预览改过色的格子原样还原(2026-08-22)。松手/取消/悬停格变化时都要调。</summary>
        private void ClearHoverPreview()
        {
            foreach (var (i, original) in _hoverPreviewCells)
                if (i < _enemyHitAreas.Count && _enemyHitAreas[i] != null)
                    _enemyHitAreas[i].color = original;
            _hoverPreviewCells.Clear();
        }

        /// <summary>该屏幕坐标落在第几个召唤槽上;都没命中返回 −1。
        /// 判定用整格(<see cref="_summonCellByCore"/>)而非字块 —— 与 EnemyIndexAt 同一条理由:
        /// 手指落点粗,只认字块会经常擦边落空。空槽也在表里,那正是拖召唤最常见的落点。
        ///
        /// **锁着的格不算命中**(2026-08-27):它们同样登记在 _summonCellByCore 里(重绘时
        /// 整表重建,少登记一格会让别处拿不到它的 RectTransform),但松手落在上面等于没落 ——
        /// 返回它的槽号会让召唤物落进一个本层还不存在的位子。</summary>
        private int SummonSlotAt(Vector2 screenPos)
        {
            foreach (var pair in _summonCellByCore)
                if (pair.Value != null && Battle.IsSlotOpen(pair.Key)
                    && RectTransformUtility.RectangleContainsScreenPoint(pair.Value, screenPos, null))
                    return pair.Key;
            return -1;
        }

        /// <summary>该屏幕坐标落在哪个存活敌人格上;都没命中返回 −1。
        /// 判定用整格 = 点击区(<see cref="_enemyHitAreas"/>,与格子上那个 <c>Button</c> 盖的
        /// 是同一块)而非立绘本身:手指落点粗,立绘窄的话会经常擦边落空。
        ///
        /// ⚠ 2026-08-31 前这里读的是 <c>_enemyRects[i].parent</c>——套 <see cref="Ui.UnitBlock"/>
        /// 之前立绘的父级就是 cell(整格),套完之后立绘的父级变成 UnitBlock 里新建的挂载点
        /// (CircleGlyph 分支只有 126×126/96×96,MobView 分支碰巧因为 shell 被 Stretch 铺满
        /// 还对,但那是巧合不是保证)——没有形象、回落圆头像的怪(灯花/墨溅/悬针/败笔)拖字
        /// 落点区会跟着缩水。改读 _enemyHitAreas 就不会再受立绘父级是谁影响,与 <c>_enemyRects</c>
        /// 是同一套下标对齐(DrawEnemies 里两个列表在同一批 continue/正常分支里成对 Add,
        /// 包括越界兜底的 col &lt; 0 分支)。</summary>
        private int EnemyIndexAt(Vector2 screenPos)
        {
            for (int i = 0; i < _enemyRects.Count && i < Battle.Enemies.Count; i++)
            {
                if (!Battle.Enemies[i].Alive || _enemyRects[i] == null) continue;
                var hitArea = i < _enemyHitAreas.Count && _enemyHitAreas[i] != null
                    ? _enemyHitAreas[i].rectTransform
                    : _enemyRects[i];
                if (RectTransformUtility.RectangleContainsScreenPoint(hitArea, screenPos, null))
                    return i;
            }
            return -1;
        }

        /// <summary>消息条简述(2026-07-21):只给等级化效果;拼音/释义/配方走长按 preview。
        /// 2026-08-21:AP 从这里撤掉 —— 一律 1,印出来是零信息量(与字牌费用带同因)。</summary>
        private string Brief(string charId)
        {
            var def = _graph.Get(charId);
            return $"「{charId}」{CharInfo.EffectsText(def, _run.CardLevel(charId))}";
        }

        /// <summary>长按看详情:preview 只读,不动选中态。</summary>
        private void ShowCharPreview(string charId)
        {
            if (_modal != null) Object.Destroy(_modal);
            _modal = CharPreview.Show(transform, _graph.Get(charId), _graph, _run.CardLevel(charId));
        }

        /// <summary>奖励页点字库:看简述(替换已改在战利品弹窗内完成,2026-07-20)。</summary>
        private void OnRewardLibraryClicked(string charId)
        {
            _message = Brief(charId);
            Refresh();
        }

        private const float PartTileW = 67f;          // 稿 .part { width: 32pt }
        private const float PartTileH = 80f;          // 稿 .part { height: 38pt }
        private const int PartGlyphFontSize = 30;     // 稿 .part .pg { font-size: 17pt } 附近
        private const float PoolAdSlotW = 67f;        // 稿 .adpart { width: 32pt },与部件卡同宽
        private const float PoolAdSlotH = 80f;        // 稿 .adpart { height: 38pt },与部件卡同高

        /// <summary>部件卡(稿 .part):一个大字形。同源变体不写在卡面上,由
        /// <see cref="PlaceKinBadge"/> 贴到四角(2026-09-01 用户拍板还原四角设计)。
        /// 仍不复用 <see cref="Ui.RoundButton"/>:那个是圆钮口径(定圆角/定字号),
        /// 这里要的是稿上 67×80 的竖版卡,单独手搭。</summary>
        private static Button PartTile(Transform parent, string glyph,
            System.Action onClick, Color bg, Color fg)
        {
            var go = new GameObject($"Part_{glyph}", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.sprite = Theme.Rounded(12);
            image.type = Image.Type.Sliced;
            image.color = bg;
            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            if (onClick != null) button.onClick.AddListener(() => onClick());
            var element = go.AddComponent<LayoutElement>();
            element.preferredWidth = PartTileW;
            element.preferredHeight = PartTileH;

            var label = Ui.ThemedLabel(go.transform, glyph, PartGlyphFontSize, fg, Theme.TitleFont);
            Ui.Stretch(label.rectTransform);
            return button;
        }

        /// <summary>把一个同源徽标贴到部件卡的某个角(2026-08-15 首版;2026-08-31 接稿时曾被
        /// 卡面上一行「≈氵冫」取代,2026-09-01 用户拍板还原四角设计)。
        /// corner:0=右上、1=右下、2=左下、3=左上,从右上起顺时针填。
        ///
        /// 四个角全部可用。同组最大是金系 5 个(金钅戈刂刀),除自己外 4 个 —— 刚好占满
        /// 四角,再加成员就得换设计。
        ///
        /// 尺寸 24×14(font 10 / pad 4):窄边距是刻意的(spec §1.6b「小胶囊」),默认
        /// padX=18/padY=12 单个就占掉大半卡宽,四个角一起画会把字形埋掉。传进
        /// <see cref="Ui.ChipWidth"/>/<see cref="Ui.ChipHeight"/> 的 pad 必须与传给
        /// <see cref="Ui.Chip"/> 的一致,否则算出来的尺寸不对、锚点跟着错位。
        ///
        /// 徽标是 Text/Image,raycastTarget 默认开着,但点击会冒泡到卡片本身那个 Button
        /// (与卡面字形同理),所以不必逐个关掉。</summary>
        private static void PlaceKinBadge(Transform tile, string kinPart, Element? element, int corner)
        {
            const int font = 10;
            const int padX = 4;
            const int padY = 4;
            string text = $"≈{kinPart}";
            float w = Ui.ChipWidth(text, font, padX);
            float h = Ui.ChipHeight(font, padY);
            var badge = Ui.Chip(tile, text, Theme.ElementColor(element), Color.white, font, padX, padY);
            var (anchor, offsetMin, offsetMax) = corner switch
            {
                0 => (new Vector2(1, 1), new Vector2(-w - 2, -h - 2), new Vector2(-2, -2)),
                1 => (new Vector2(1, 0), new Vector2(-w - 2, 2), new Vector2(-2, h + 2)),
                2 => (new Vector2(0, 0), new Vector2(2, 2), new Vector2(w + 2, h + 2)),
                _ => (new Vector2(0, 1), new Vector2(2, -h - 2), new Vector2(w + 2, -2)),
            };
            Ui.Anchor((RectTransform)badge.transform, anchor, anchor, offsetMin, offsetMax);
        }

        /// <summary>部件池行末尾的广告扩容位(稿 .adpart):常驻显示,用过后转灰而不是消失,
        /// 与 <see cref="DrawHandAdSlot"/> 同一个理由、同一套取舍(实线近似虚线边框)。</summary>
        private void DrawPoolAdSlot()
        {
            bool used = _run.PoolExpanded;
            var outer = Ui.OutlinedPanel(_poolRow, "PoolAdSlot",
                used ? Theme.LockedBg : Theme.AdGreenBg, used ? Theme.PanelBorder : Theme.AdGreen,
                10, 1.5f, out var face);
            Ui.Sized(outer.gameObject, width: PoolAdSlotW, height: PoolAdSlotH);
            var stack = Ui.VStack(face.transform, "Stack", 2);
            Ui.Stretch((RectTransform)stack.transform);
            Ui.ThemedLabel(stack.transform,
                used ? Strings.T("battle.btn.pool_ad_used") : Strings.T("battle.btn.pool_ad_slot"),
                11, used ? Theme.LockGray : Theme.AdGreenText);
            if (used) return;
            var button = outer.gameObject.AddComponent<Button>();
            button.targetGraphic = outer;
            button.onClick.AddListener(() => // 原型:点击即生效,SDK 后接
            {
                _run.TryExpandPool();
                _onExpanded?.Invoke();
                _message = Strings.T("battle.label.pool_cap_up");
                Refresh();
            });
        }

        private void DrawPool()
        {
            // 奖励页显示携带池(部件不再随战利品入池,这里只展示当前持有,2026-08-04)
            bool rewardPhase = _run.Phase == RunPhase.Reward;
            var poolChars = rewardPhase ? _run.CarriedPool : Battle.Pool;
            CountCaption(_poolRow, Strings.T("battle.label.pool_count",
                ("count", poolChars.Count), ("capacity", Battle.PoolCapacity)), 14, Theme.TextDim);
            foreach (var id in poolChars)
            {
                string charId = id;
                var def = _graph.Get(charId);
                bool selected = _selectedChar == charId && !_targeting && !_allyTargeting;
                System.Action tap = () =>
                {
                    if (rewardPhase) { _message = Brief(charId); Refresh(); }
                    else OnPoolCharClicked(charId);
                };
                // 2026-08-31 接稿:56×56 的 RoundButton → 67×80 的 PartTile(稿 32×38pt)。
                var tile = PartTile(_poolRow, charId, tap,
                    selected ? Theme.ElementColor(def.Element) : Theme.ElementSoft(def.Element),
                    selected ? Color.white : Theme.ElementSoftFg(def.Element));
                // 同源徽标(2026-08-15,部件五系通用;2026-09-01 用户拍板从卡面一行文字
                // 还原回四角):同组**其他全部**成员各占一个角。判据用 TryGetGroup 而不是
                // 「只标变体、代表字不标」——代表字(水/金/木/火/土)自己也要标出它能顶谁,
                // 这是 2026-08-15 的裁定,与下面拆合台的转位提示同口径。
                if (ComponentKin.TryGetGroup(charId, out var kinGroup))
                {
                    int corner = 0;
                    foreach (var kinPart in kinGroup)
                    {
                        if (kinPart == charId) continue; // 自己不标在自己身上
                        PlaceKinBadge(tile.transform, kinPart, def.Element, corner++);
                    }
                }
                HoldToPreview.Attach(tile.gameObject, () => ShowCharPreview(charId));
                if (!rewardPhase) AttachDragToAttack(tile.gameObject, def); // 水/土 直出的攻击用法在这一排
                _tileRects[charId] = (RectTransform)tile.transform; // 同名部件取最后一个,动效近似即可
                ApplyFreshGlow(charId, (RectTransform)tile.transform, Theme.ElementColor(def.Element));
            }
            // 稿 .poolnote「下回合掉 1 个」没有照抄:2026-08-04 起部件已经不再随回合掉落
            // (BattleEngine.cs:1784「部件不再掉落——五行部件只能靠拆字获得」),掉的是字、
            // 落进字库,不是部件、落进这个池。稿这句是规则改动前的旧文案,原样搬过来会让
            // 玩家看着一句不成立的承诺,故略去,不新造一句话去描述不存在的机制。
            DrawPoolAdSlot();
        }

        /// <summary>拆合台选中详情或空态两行说明(稿 .picked / .empty)。可合成列表已经拆去
        /// <see cref="DrawCraftList"/>——这两段稿上是并列常驻的两块,不是「选中了就看不到
        /// 可合成列表」的互斥关系(旧实现挤在同一个槽位,是过渡期的将就)。</summary>
        private void DrawSuggest()
        {
            if (_selectedChar != null || _targeting) return; // 选中态详情由 DrawActions 画
            Ui.ThemedLabel(_suggestRow, Strings.T("battle.hint.workbench_empty"), 14, Theme.TextDim);
        }

        private const float CraftRowSpacing = 8f;  // 稿 .craft { gap: 4pt }
        private const float CraftRowHeight = 50f;  // 触控:与出/拆/弃三钮同一档(52)接近

        /// <summary>可合成列表(稿 .craft):只列部件池认同源变体后能凑齐配方的字,一行一条
        /// 「部件 → 字」,点击即合。常驻显示,不受选中态影响,内容多时靠 <see cref="Ui.ScrollList"/>
        /// 滚动——2026-08-31 从「挤在 _suggestRow、选中就消失、不会滚」改过来。
        ///
        /// 缺料的字不在这里:稿的口径是「拆合台是动手的地方,不是清单」,归左栏
        /// <see cref="DrawNearMissHints"/> 的配字表。</summary>
        private void DrawCraftList()
        {
            // 只提示已收集的字:合不出来的不该出现在拆合台(2026-07-19)
            var suggest = ForgeEngine.Suggest(_graph, Battle.Pool, Battle.Library, Battle.UnlockedChars);
            DrawNearMissHints(suggest.NearMisses); // 左侧差字面板:与拆合台选中态无关,一直画
            if (suggest.Composable.Count == 0)
            {
                Ui.ThemedLabel(_craftRow, Strings.T("battle.hint.craft_empty"), 13, Theme.TextDim);
                return;
            }
            foreach (var id in suggest.Composable)
            {
                string charId = id;
                var def = _graph.Get(charId);
                // 稿 .cr:绿底一行,「部件+部件 → 字」,点击即合。旧版这里是逐个部件画圆钮
                // 再拼 "+"/"="——2026-08-31 接稿简化成一行文字,复杂的等价匹配/转位提示
                // 已经挪到部件池卡面自己身上常驻显示(见 DrawPool 的 KinHint)。
                var row = Ui.CardPanel(_craftRow, $"Craft_{charId}", Theme.AdGreenBg, 10);
                Ui.Sized(row.gameObject, height: CraftRowHeight);
                var button = row.gameObject.AddComponent<Button>();
                button.targetGraphic = row;
                button.onClick.AddListener(() => OnCompose(charId));
                var inner = Ui.Row(row.transform, "Inner", 6);
                Ui.Stretch((RectTransform)inner.transform);
                Ui.ThemedLabel(inner.transform, string.Join("+", def.Recipe), 15, Theme.TextDim, Theme.TitleFont);
                Ui.ThemedLabel(inner.transform, "→", 13, Theme.TextDim);
                Ui.ThemedLabel(inner.transform, charId, 22, Theme.GlyphColor(def.Element), Theme.TitleFont);
            }
        }

        private const int HintGlyphFontSize = 20;    // 稿 .mrow .g,衬线大字
        private const int HintMissingFontSize = 13;  // 稿 .mrow .need
        private const float HintRowGap = 6f;            // 稿 .mrow { gap: 3pt }
        private const int HintMissingMaxRows = 12;      // 稿 .missing { overflow: hidden } 的静默截断额度

        /// <summary>差字面板(屏幕左窄栏,稿 .missing):平铺列表,一行一条「字 缺 N」,按五行
        /// 着色,纯展示不可点。
        ///
        /// 2026-08-31 从五行三级目录(一级选桶、二级选字、三级看差什么)拍板改成这个平铺
        /// 列表——左栏只有 142 宽(稿 68pt),塞不下三级目录(第 5 个任务的 review 实测过:
        /// 二级目录每行 4 个 38 宽的钮 + 间距 = 164,已经超出栏宽会被压变形);而这块面板的
        /// 作用是「扫一眼还缺什么」,不是浏览器,不需要能点开细节——长按字库/部件里的字
        /// 本就能看完整配方(CharPreview)。</summary>
        private void DrawNearMissHints(System.Collections.Generic.IReadOnlyList<NearMiss> nearMisses)
        {
            if (nearMisses.Count == 0) return;
            Ui.ThemedLabel(_hintColumn, Strings.T("battle.hint.recipe_panel_title"), 16, Theme.TextDim, Theme.TitleFont);
            int shown = System.Math.Min(nearMisses.Count, HintMissingMaxRows);
            for (int i = 0; i < shown; i++)
            {
                var miss = nearMisses[i];
                var def = _graph.Get(miss.CharId);
                var row = Ui.Row(_hintColumn, $"Hint_{miss.CharId}_{i}", HintRowGap);
                Ui.ThemedLabel(row.transform, miss.CharId, HintGlyphFontSize, Theme.GlyphColor(def.Element), Theme.TitleFont);
                Ui.ThemedLabel(row.transform, Strings.T("battle.hint.missing_short", ("missing", miss.MissingIngredient)),
                    HintMissingFontSize, Theme.CinnabarDark);
            }
        }

        // 拆合台栏内可用宽:BenchW 减两侧 BenchPad。栏里任何一件「按栏宽定宽」的东西
        // 都该拿这个数,别再各自写死一个近似值。
        private const float BenchInnerW = BenchW - BenchPad * 2;   // 276 − 15×2 = 246

        /// <summary>拆合台里的整句提示(选目标态 / 落位态那几条)。
        ///
        /// 这些句子是**整句**,不是标签:「「治」点击治疗目标(玩家或召唤物)|点空白取消」
        /// 16 号下约 400 宽,而栏内只有 246 —— 单行渲染会直接甩出卡片外糊到中区上
        /// (2026-09-01 用户报的溢出)。这里定宽 + 开 Wrap 让它换行。
        ///
        /// 行高走 <see cref="Ui.WrappedTextHeight"/>(纯函数,不靠 Text.preferredHeight ——
        /// 理由见那个方法的注释)。</summary>
        private static Text BenchHint(Transform parent, string text, int fontSize, Color color)
        {
            var label = Ui.ThemedLabel(parent, text, fontSize, color, align: TextAnchor.UpperLeft);
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            Ui.Sized(label.gameObject, width: BenchInnerW,
                height: Ui.WrappedTextHeight(text, fontSize, BenchInnerW));
            return label;
        }

        private const float PickedRowGap = 6f;          // 稿 .picked { gap: 3pt }
        private const float PickedTileW = 71f;          // 稿 .picked .pt { width: 34pt }
        private const float PickedTileH = 90f;          // 稿 .picked .pt { height: 43pt }
        private const int PickedGlyphFontSize = 42;     // 稿 .picked .pt { font-size: 20pt }

        private void DrawActions()
        {
            // 选位置态:动作行只画一句提示(2026-08-20 review M-2)。这一段必须在判空**之前**——
            // 拖召唤字进来的那条路径 _selectedChar 是 null,早退的话这里什么也不画,
            // 玩家看不出自己正处在「等你选位置」的状态。
            // 不画「出/拆/弃」:再点一次「出」会走 OnCastPressed → BeginCast,把落位态悄悄重置。
            // 「取消」按钮 2026-08-21 随整排一起移除 —— 点空白即取消(Backdrop)。
            if (_slotPicking)
            {
                BenchHint(_actionRow, Strings.T("battle.hint.slot_picking_dragging", ("charId", _pendingSummonChar)), 16, Theme.TextMain);
                return;
            }
            if (_selectedChar == null) return;
            var def = _graph.Get(_selectedChar);

            // 选中详情(稿 .picked):牌面 + 名/效果两行。2026-08-31 接稿改成这个更简的格式——
            // 旧版这里第一行是「选中字 → 拆解部件」的分解预览,那一条没有真的丢:配方拆解
            // 长按字牌的详情弹窗本就有(CharInfo.Detail 含配方行)。
            // 转位提示那一条 2026-09-01 用户拍板还原,见下面 KinVariants 那段。
            var picked = Ui.Row(_suggestRow, "PickedRow", PickedRowGap).transform;
            var pickedOuter = Ui.OutlinedPanel(picked, "Tile", Color.white, Theme.RarityColor(def.Rarity), 8, 2f, out var pickedFace);
            // minWidth 与 preferredWidth 同值(2026-09-01 修「牌面有时大有时小」):
            // HorizontalLayoutGroup 一旦发现子物体的 preferred 之和超过行宽,会把**所有**
            // 子物体从 preferred 往 min 等比压回去。而右边信息列的 preferredWidth 来自
            // Text —— Text 的 preferredWidth 报的是**不换行时的整句宽度**,效果说明一长
            // 就是好几百,于是每次都超预算、牌面跟着被压窄:效果短的字牌面正常,效果长的
            // 字牌面明显小一圈。钉死 minWidth 让牌面不参与这场压缩,再给信息列一个算得出
            // 的定宽(见下),整行就再也不超预算了。
            Ui.Sized(pickedOuter.gameObject, width: PickedTileW, height: PickedTileH).minWidth = PickedTileW;
            var pickedGlyph = Ui.ThemedLabel(pickedFace.transform, def.Id, PickedGlyphFontSize,
                Theme.GlyphColor(def.Element), Theme.TitleFont);
            Ui.Stretch(pickedGlyph.rectTransform);
            var pickedInfo = Ui.VStack(picked, "Info", 2);
            var pickedInfoLayout = pickedInfo.GetComponent<VerticalLayoutGroup>();
            pickedInfoLayout.childAlignment = TextAnchor.UpperLeft;
            // 显式开 childForceExpandWidth:效果说明要按这一列的实际宽度换行,不开的话
            // Text 拿不到宽度、算不出该在哪里断行(下面 effectLabel 的 Wrap 依赖这个)。
            pickedInfoLayout.childForceExpandWidth = true;
            // 定宽而不是 flexWidth:1(2026-09-01,同上)。flexWidth 只在**有富余**时才分配,
            // 决定不了「超预算时谁先缩」;这里的病根恰恰是 Text 把 preferredWidth 报成了
            // 不换行的整句宽度,行永远处在超预算态,flex 从来轮不上。直接把剩余宽度算给它,
            // preferredWidth 就等于实际宽度,effectLabel 的 Wrap 也拿到了正确的换行依据。
            Ui.Sized(pickedInfo, width: BenchInnerW - PickedTileW - PickedRowGap);
            int cardLevel = _run.CardLevel(_selectedChar);
            string elementName = def.Element is { } elem ? CharInfo.ElementName(elem) : Strings.T("char.element.neutral");
            Ui.ThemedLabel(pickedInfo.transform, Strings.T("battle.label.picked_meta",
                ("rarity", CharInfo.RarityName(def.Rarity)), ("element", elementName), ("level", cardLevel)),
                12, Theme.TextDim, align: TextAnchor.UpperLeft);
            var effectLabel = Ui.ThemedLabel(pickedInfo.transform, CharInfo.EffectsText(def, cardLevel),
                13, Theme.TextMain, align: TextAnchor.UpperLeft);
            // 效果说明可能比两个字的部件名长得多,稿 .pv 是能换行的正文——这里是本文件
            // 唯一一处要求 Text 真的换行(其余标签/按钮标题按既有惯例任其单行溢出,
            // 见 BattleView.cs 里 EventLabelWidthTests 那条注释),窄栏放不下的长效果描述
            // 硬要单行会甩出卡片外糊到中区上,比换行更难看。
            effectLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
            effectLabel.verticalOverflow = VerticalWrapMode.Overflow;

            // 转位提示(2026-08-15 用户裁定,2026-09-01 用户拍板还原):选中五系部件时,把
            // **同组全部**可互换的成员列出来 —— 选 氵 显示「⇄ 水 冫」,选 刂 显示「⇄ 金 钅 戈」。
            //
            // 这与部件卡四角的 ≈X 徽标是**两条不同的口径**,别互相「对齐」:徽标要在一张
            // 67×80 的卡上用最小面积回答「这张能顶谁」,这里是选中后的详情,空间够、给全量,
            // 还配一句说明这是什么机制。判据同为 TryGetGroup —— 代表字(水/金…)自己也要
            // 出这一条,2026-08-15 用户点名要补的正是它。
            //
            // 纯说明不是操作:等价匹配在 ForgeEngine.TryCompose 里自动生效,不花 AP
            // (spec §1.6c)。所以这些字钮 onClick 传 null。
            //
            // 只对独体字(IsLeaf)出:合成字选中时这一栏该讲的是它的配方,不是部件互换。
            if (def.IsLeaf)
            {
                if (ComponentKin.TryGetGroup(_selectedChar, out var kinGroup))
                {
                    // 拆合台内宽 246(BenchW 276 − 两侧 BenchPad 15)。金系除自己外 4 个是上限:
                    // 「⇄」20 + 4×38 + 5 个间距×6 = 202,放得下。
                    var kinRow = Ui.Row(_suggestRow, "KinVariants", 6).transform;
                    Ui.ThemedLabel(kinRow, "⇄", 16, Theme.TextDim);
                    foreach (var kin in kinGroup)
                    {
                        if (kin == _selectedChar) continue; // 自己不列进「可换成」
                        Ui.RoundButton(kinRow, kin, null,
                            Theme.ElementColor(_graph.Get(kin).Element), Color.white, 16, new Vector2(38, 38), 8);
                    }
                    Ui.ThemedLabel(_suggestRow, Strings.T("battle.hint.kin_variant_label"), 13, Theme.TextDim);
                }
                else
                {
                    Ui.ThemedLabel(_suggestRow, Strings.T("battle.hint.leaf_char"), 13, Theme.TextDim);
                }
            }

            // 第二行(动作)
            if (_targeting)
            {
                BenchHint(_actionRow, Strings.T("battle.hint.targeting_enemy", ("charId", _selectedChar)), 16, Theme.TextMain);
                return;
            }
            // 治疗选目标态(2026-08-22):同 _targeting 一样只画一句提示——再点一次「出」
            // 会走 OnCastPressed → BeginCast,把这个待选态悄悄重置。
            if (_allyTargeting)
            {
                BenchHint(_actionRow, Strings.T("battle.hint.targeting_ally", ("charId", _selectedChar)), 16, Theme.TextMain);
                return;
            }
            // 方向选择(2026-09-02):水/土 双方向字选中后画「攻」「护」两个钮,
            // 点完各自进对应的目标态。与下面那排动作钮同尺寸,免得手指点位跳。
            if (_directionPicking)
            {
                Ui.RoundButton(_actionRow, Strings.T("battle.btn.direction_attack"),
                    () => CastInDirection(def, attackMode: true),
                    Theme.Cinnabar, Color.white, 17, new Vector2(76, 52));
                Ui.RoundButton(_actionRow, Strings.T("battle.btn.direction_support"),
                    () => CastInDirection(def, attackMode: false),
                    Theme.ElementColor(def.Element), Color.white, 17, new Vector2(76, 52));
                return;
            }
            // 2026-08-21 用户拍板:动作名一律收成单字 —— 「出字 / 直出 / 兜底一击」三种情形
            // 统一叫「出」,「丢弃」叫「弃」。竖栏里按钮排一行,长标签会把整行挤换行;
            // 而三种「出」的差别(库里出 / 部件直出 / 无效果字的兜底一击)属于结算细节,
            // 玩家在按钮上分不分得清都不影响他要点的那一下。
            // 动作按钮 ≥50 高(2026-07-19 iOS 反馈:手指可点性)
            Ui.RoundButton(_actionRow, Strings.T("battle.btn.cast"), () => OnCastPressed(def), Theme.Cinnabar, Color.white, 17, new Vector2(76, 52));
            // 2026-09-01 二级拆解:去掉「必须在字库里」的前提 —— 部件池里带配方的部件
            // (烝 = 丞 + 灬)同样该给拆按钮,ForgeEngine.TryDismantle 认两种来源。
            if (!def.IsLeaf)
                Ui.RoundButton(_actionRow, Strings.T("battle.btn.dismantle"), () => OnDismantle(def.Id), Theme.SplitBlue, Color.white, 17, new Vector2(76, 52));
            Ui.RoundButton(_actionRow, Strings.T("battle.btn.discard"), () => OnDiscard(def.Id), Theme.ExitPink, Color.white, 17, new Vector2(76, 52));
            // 「取消」整排移除(2026-08-21 用户拍板):点屏幕空白处本来就取消选中
            // (BuildSkeleton 最先建的 Backdrop 全屏透明层),按钮是同一功能的第二个入口。
        }

        private void DrawEndTurn()
        {
            Ui.PillButton(_endTurnRow, Strings.T("battle.btn.end_turn"), ConfirmEndTurn, Theme.Cinnabar, Color.white, 21, new Vector2(190, 52));
        }

        /// <summary>回合掉字遇满库(2026-08-04):停下让玩家选替换哪一张,或跳过这次掉落。
        /// 结构照搬 DrawEventReplaceStep —— 同一个「满库换哪张」的心智模型。</summary>
        private void DrawDropChoiceStep()
        {
            string incoming = Battle.PendingDrop;

            if (_modal != null) Object.Destroy(_modal);
            _modal = Ui.ModalShell(transform, Strings.T("battle.drop.replace_title", ("charId", incoming)),
                new Vector2(360, 240), dismissable: false, out var stack);
            Ui.ThemedLabel(stack, Strings.T("battle.common.replace_warning"), 15, Theme.TextDim);

            Transform row = null;
            for (int i = 0; i < Battle.Library.Count; i++)
            {
                if (i % 4 == 0) row = Ui.Row(stack, $"Row{i / 4}", 8).transform;
                int replaceIndex = i;
                var def = _graph.Get(Battle.Library[i]);
                Ui.GlyphTile(row, def, false, () =>
                {
                    string dropped = Battle.Library[replaceIndex];
                    if (Battle.ResolveDrop(replaceIndex) == BattleError.None)
                    {
                        _message = Strings.T("battle.common.replaced_msg", ("incoming", incoming), ("dropped", dropped));
                        if (_modal != null) Object.Destroy(_modal);
                    }
                    Refresh();
                }, new Vector2(74, 96));
            }

            Ui.PillButton(stack, Strings.T("battle.btn.drop_skip"), () =>
            {
                Battle.SkipDrop();
                if (_modal != null) Object.Destroy(_modal);
                _message = Strings.T("battle.drop.skip_msg", ("charId", incoming));
                Refresh();
            }, Theme.LockedBg, Theme.TextMain, 16, new Vector2(150, 46));
        }

        /// <summary>还有 AP 时先确认,避免误触把这回合的 AP 作废(2026-07-21)。
        /// AP 耗尽的自动结束与「AP 不够」弹窗里的快捷钮直连 OnEndTurn,不重复确认。</summary>
        private void ConfirmEndTurn()
        {
            if (Battle.Ap <= 0)
            {
                OnEndTurn();
                return;
            }
            ShowModal(Strings.T("battle.dialog.ap_left.title"),
                Strings.T("battle.dialog.ap_left.body", ("ap", Battle.Ap)),
                (Strings.T("battle.btn.end_turn"), OnEndTurn, Theme.Cinnabar, Color.white),
                (Strings.T("common.reconsider"), null, Theme.LockedBg, Theme.TextMain));
        }

        private void DrawBattleSettle()
        {
            if (Battle.Phase == BattlePhase.Won)
            {
                ShowVictoryBanner(); // 过关提示走屏幕中央横幅,自动推进(2026-07-21)
                return;
            }
            Ui.ThemedLabel(_centerRow, Strings.T("battle.phase.defeat_ellipsis"), 36, Theme.TextMain, Theme.TitleFont);
            // 无尽塔:整次登塔一次广告复活——满血续战 + 补给,让空手也有再战之力(2026-07-24)
            if (_onExit != null && _run.ReviveAvailable)
                Ui.AdBadge(_centerRow, Strings.T("battle.btn.ad_revive"), () =>
                {
                    _previewRewardIndex = -1;
                    _run.TryRevive();
                    _onExpanded?.Invoke(); // 即时落盘:防「刚看完广告就挂起」白看
                    _message = Strings.T("battle.revive.full_hp_msg");
                    Refresh();
                }, new Vector2(160, 60));
            Ui.PillButton(_centerRow, Strings.T("battle.btn.settle"), AdvanceAfterSettle,
                Theme.Jade, Color.white, 26, new Vector2(150, 70));
        }

        private bool _bannerRunning; // 横幅协程已起:Refresh 会反复走到这里,防重复

        /// <summary>过关提示(2026-07-21):屏幕中央大字,停留后淡出并自动进战利品。
        /// Boss 层加大加朱砂色、多停留一会儿。</summary>
        private void ShowVictoryBanner()
        {
            if (_bannerRunning) return;
            _bannerRunning = true;

            bool boss = false;
            foreach (var enemy in Battle.Enemies)
                if (enemy.IsBoss) { boss = true; break; }

            // 墨色横带压暗底下的血条/字牌:大字与背景分层,不再糊在一起(2026-07-21)
            var banner = Ui.Panel(transform, "VictoryBanner");
            Ui.Anchor((RectTransform)banner.transform,
                new Vector2(0, boss ? 0.42f : 0.45f), new Vector2(1, boss ? 0.62f : 0.59f),
                Vector2.zero, Vector2.zero);
            var scrim = banner.AddComponent<Image>();
            scrim.color = new Color(Theme.Ink.r, Theme.Ink.g, Theme.Ink.b, boss ? 0.88f : 0.8f);
            var group = banner.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false; // 只是提示,不拦点击
            var label = Ui.ThemedLabel(banner.transform, boss ? Strings.T("battle.phase.boss_broken_banner") : Strings.T("battle.phase.floor_cleared_banner"),
                boss ? 72 : 44, boss ? Theme.Gold : Theme.CardWhite, Theme.TitleFont);
            Ui.Stretch(label.rectTransform);
            StartCoroutine(VictoryBannerRoutine(banner, group, boss ? 1.8f : 1.2f));
        }

        private System.Collections.IEnumerator VictoryBannerRoutine(GameObject banner, CanvasGroup group, float hold)
        {
            for (float t = 0; t < hold; t += Time.unscaledDeltaTime)
                yield return null;
            const float fade = 0.3f;
            for (float t = 0; t < fade; t += Time.unscaledDeltaTime)
            {
                group.alpha = 1f - t / fade;
                yield return null;
            }
            Destroy(banner);
            AdvanceAfterSettle();
        }

        private void AdvanceAfterSettle()
        {
            _bannerRunning = false;
            _run.AdvanceAfterBattle();
            _pendingRewardIndex = -1;
            _previewRewardIndex = -1;
            _message = "";
            Refresh();
        }

        /// <summary>战利品弹窗:字库满时就地转入「换掉哪一个」子步;额度用尽(2026-08-04 起
        /// 5 选 2)由 Core 侧 MaybeFinishRewards 自动开拔——走到这里时必然还有字可选。</summary>
        private void DrawReward()
        {
            if (_rewardModal != null) Destroy(_rewardModal);

            // 替换子步的前提会被广告扩容推翻(2026-08-18):+2 徽章就画在弹窗背后的字库行上
            // (case RunPhase.Reward 同时走 DrawLibrary),玩家正是为了不丢字才去看的广告。
            // 这个下标是粘滞 UI 状态,只在替换成功/「算了不换」时才清 —— 不在这里按当前容量
            // 复核,腾出空位后弹窗依旧扣着「字库已满」,玩家看着 7/9 却被要求换字,
            // 广告等于白看。空位一出现就退回选字步,直接收下。
            if (_pendingRewardIndex >= 0 && _run.CarriedLibrary.Count < Battle.LibraryCapacity)
                _pendingRewardIndex = -1;

            if (_pendingRewardIndex >= 0)
                DrawRewardReplaceStep();
            else
                DrawRewardCharStep();
        }

        private void DrawRewardCharStep()
        {
            _rewardModal = Ui.ModalShell(transform, Strings.T("battle.reward.pick_title", ("left", _run.CharPicksLeft)),
                new Vector2(340, 165), dismissable: false, out var content);
            var preview = _previewRewardIndex >= 0
                ? Brief(_run.RewardOptions[_previewRewardIndex]) + Strings.T("battle.reward.tap_again_suffix")
                : Strings.T("battle.reward.pick_hint", ("count", _run.CarriedLibrary.Count), ("capacity", Battle.LibraryCapacity));
            Ui.ThemedLabel(content, preview, 16, Theme.TextDim);

            var row = Ui.Row(content, "Options", 10);
            for (int i = 0; i < _run.RewardOptions.Count; i++)
            {
                int index = i;
                var id = _run.RewardOptions[i];
                var def = _graph.Get(id);
                // 先声明后赋值:tap 要在闭包里读这张牌的位置(飞字起点),而 C# 不许 lambda
                // 引用它后面才声明的局部变量
                GameObject tile = null;
                System.Action tap = () =>
                {
                    if (_previewRewardIndex != index)
                    {
                        _previewRewardIndex = index; // 首点预览效果,再点确认
                        Refresh();
                        return;
                    }
                    _previewRewardIndex = -1;
                    // 起点须在重绘销毁弹窗牌之前捕获(与 OnCompose 同一条约束)
                    bool hasFrom = tile != null;
                    Vector3 fromPos = hasFrom ? tile.transform.position : default;
                    if (_run.PickReward(index))
                    {
                        _tutorial?.Notify(TutorialAction.PickReward);
                        _message = Strings.T("battle.reward.added_msg", ("charId", id));
                        MarkFresh(id);     // 记在重绘之前:光晕是重绘时照表套上的
                        CancelSelection(); // 额度归零 → 下次 Refresh 由 Core 侧自动开拔
                        // 选中的字从弹窗飞进字库并落位弹跳(2026-08-30):此前只有一行文字
                        // 说「已收入」,牌是凭空出现在字库里的 —— 玩家得自己去队尾找它。
                        // 光晕接着亮 2.4s,与拆合、奇遇同一套读法。
                        FlyIntoLibrary(id, hasFrom, fromPos);
                        return;
                    }
                    // PickReward 有三种拒收原因(阶段不符/额度尽/满库),此前一律当成「字库已满」
                    // 转入替换子步 —— 提示因此会说谎:非满库时也弹「换掉哪一个」。
                    // 按真实条件分流,并把状态打进日志,便于定位(2026-08-18 诊断中)。
                    bool libraryFull = _run.CarriedLibrary.Count >= Battle.LibraryCapacity;
                    Debug.Log($"[战利品诊断] Phase={_run.Phase} 额度={_run.CharPicksLeft} " +
                              $"携带字数={_run.CarriedLibrary.Count} 显示容量={Battle.LibraryCapacity} " +
                              $"已扩容={_run.LibraryExpanded} 判定满库={libraryFull}");
                    if (libraryFull)
                    {
                        _pendingRewardIndex = index; // 真满库(3.8.1):转入替换子步
                    }
                    else
                    {
                        // 不是满库却被拒:把真实原因摆到台面上,而不是诬赖字库
                        // ⚠ {phase} 直接传的是 RunPhase 枚举,ToString() 会把英文成员名(如
                        // Reward)拼进中文消息——搬字符串表前就有的既有小问题,原样带过来。
                        _message = Strings.T("battle.reward.rejected_detail",
                            ("charId", id), ("count", _run.CarriedLibrary.Count),
                            ("capacity", Battle.LibraryCapacity), ("left", _run.CharPicksLeft), ("phase", _run.Phase));
                    }
                    Refresh();
                };
                var button = Ui.GlyphTile(row.transform, def,
                    index == _previewRewardIndex, tap);
                tile = button.gameObject;
                HoldToPreview.Attach(button.gameObject, () => ShowCharPreview(id));
            }

            DrawRewardAdBadge(content);

            Ui.RoundButton(content, Strings.T("battle.btn.reward_skip"), () =>
            {
                _previewRewardIndex = -1;
                _run.SkipReward();
                _tutorial?.Notify(TutorialAction.PickReward); // 跳过也算完成节拍,引导不卡死
                _message = Strings.T("battle.reward.skip_msg");
                CancelSelection();
            }, Theme.LockedBg, Theme.TextMain, 17, new Vector2(190, 46));
        }

        private void DrawRewardReplaceStep()
        {
            var incoming = _run.RewardOptions[_pendingRewardIndex];
            _rewardModal = Ui.ModalShell(transform,
                Strings.T("battle.reward.replace_title", ("charId", incoming)),
                new Vector2(360, 165), dismissable: false, out var content);
            Ui.ThemedLabel(content,
                Strings.T("battle.reward.replace_hint", ("count", _run.CarriedLibrary.Count), ("capacity", Battle.LibraryCapacity)),
                16, Theme.TextDim);

            var row = Ui.Row(content, "Library", 8);
            for (int i = 0; i < _run.CarriedLibrary.Count; i++)
            {
                int replaceIndex = i;
                var def = _graph.Get(_run.CarriedLibrary[i]);
                Ui.GlyphTile(row.transform, def, false, () =>
                {
                    string dropped = _run.CarriedLibrary[replaceIndex];
                    if (_run.PickRewardReplacing(_pendingRewardIndex, replaceIndex))
                    {
                        _pendingRewardIndex = -1;
                        _tutorial?.Notify(TutorialAction.PickReward);
                        _message = Strings.T("battle.reward.replaced_in_msg", ("incoming", incoming), ("dropped", dropped));
                        MarkFresh(incoming); // 换进来的那张也高亮:满库替换时更要看清换进了什么
                        CancelSelection();
                    }
                }, new Vector2(74, 96));
            }

            DrawRewardAdBadge(content);

            Ui.RoundButton(content, Strings.T("battle.btn.replace_cancel"), () =>
            {
                _pendingRewardIndex = -1;
                Refresh();
            }, Theme.LockedBg, Theme.TextMain, 17, new Vector2(150, 46));
        }

        /// <summary>战利品弹窗内的广告扩容入口(2026-08-18)。
        /// **必须画在弹窗内容里**:Ui.ModalShell 铺的是全屏 Image 遮罩(还挂着吞点击的 Button),
        /// DrawLibrary 画在弹窗背后的那枚 +2 徽章被整个盖住,满库时玩家根本够不着 ——
        /// 「不想丢字就看广告」这条路在最需要它的时刻是断的,只能被迫替换或弃字。
        /// 扩容后 DrawReward() 的容量复核会把替换子步退回选字步,直接收下。</summary>
        private void DrawRewardAdBadge(Transform content)
        {
            if (_run.LibraryExpanded) return;
            Ui.AdBadge(content, Strings.T("battle.btn.ad_expand_library"), () =>
            {
                _run.TryExpandLibrary();
                _onExpanded?.Invoke(); // 即时落盘,与字库行那枚徽章同口径
                _message = Strings.T("battle.label.library_cap_up");
                Refresh();
            }, new Vector2(190, 44));
        }

        // ---- 复活补给(2026-07-24):以战利品展示方式给字,直接注入当前战斗字库。
        // 2026-08-04:部件补给随 Core 一并删除——五行部件今后只能靠拆字获得;
        // 满库转入替换子步(看了广告不该因满库一无所得),额度尽/候选枯竭才由收尾检查 SkipReviveReward ----

        private int _pendingReviveIndex = -1; // 满库待替换:已选中的候选字下标(-1 = 未进替换子步)

        private void DrawReviveCharStep()
        {
            // 同 DrawReward:复活补给页也画着字库行与 +2 徽章,扩容后满库前提不再成立
            if (_pendingReviveIndex >= 0 && Battle.Library.Count < Battle.LibraryCapacity)
                _pendingReviveIndex = -1;
            if (_pendingReviveIndex >= 0) { DrawReviveReplaceStep(); return; }
            if (_rewardModal != null) Destroy(_rewardModal);

            _rewardModal = Ui.ModalShell(transform, Strings.T("battle.revive.pick_title", ("left", _run.ReviveCharPicksLeft)),
                new Vector2(340, 165), dismissable: false, out var content);
            Ui.ThemedLabel(content, _previewRewardIndex >= 0
                ? Brief(_run.RewardOptions[_previewRewardIndex]) + Strings.T("battle.reward.tap_again_suffix")
                : Strings.T("battle.reward.pick_hint", ("count", Battle.Library.Count), ("capacity", Battle.LibraryCapacity)), 16, Theme.TextDim);

            var row = Ui.Row(content, "Options", 10);
            for (int i = 0; i < _run.RewardOptions.Count; i++)
            {
                int index = i;
                var id = _run.RewardOptions[i];
                var def = _graph.Get(id);
                System.Action tap = () =>
                {
                    if (_previewRewardIndex != index) { _previewRewardIndex = index; Refresh(); return; }
                    _previewRewardIndex = -1;
                    if (Battle.Library.Count >= Battle.LibraryCapacity)
                        _pendingReviveIndex = index;             // 满库:转入「换掉哪一张」
                    else if (_run.PickReviveChar(index))
                        _message = Strings.T("battle.reward.added_msg", ("charId", id));
                    Refresh();
                };
                var tile = Ui.GlyphTile(row.transform, def, index == _previewRewardIndex, tap);
                HoldToPreview.Attach(tile.gameObject, () => ShowCharPreview(id));
            }

            Ui.RoundButton(content, Strings.T("battle.btn.revive_skip"), () =>
            {
                _previewRewardIndex = -1;
                _run.SkipReviveReward();
                _message = Strings.T("battle.revive.skip_msg");
                CancelSelection();
            }, Theme.LockedBg, Theme.TextMain, 17, new Vector2(190, 46));
        }

        /// <summary>复活补给满库替换(2026-08-04):结构同 DrawDropChoiceStep,
        /// 区别是换进来的字来自补给候选而非回合掉落。</summary>
        private void DrawReviveReplaceStep()
        {
            string incoming = _run.RewardOptions[_pendingReviveIndex];

            if (_rewardModal != null) Destroy(_rewardModal);
            _rewardModal = Ui.ModalShell(transform, Strings.T("battle.revive.replace_title", ("charId", incoming)),
                new Vector2(360, 240), dismissable: false, out var stack);
            Ui.ThemedLabel(stack, Strings.T("battle.common.replace_warning"), 15, Theme.TextDim);

            Transform row = null;
            for (int i = 0; i < Battle.Library.Count; i++)
            {
                if (i % 4 == 0) row = Ui.Row(stack, $"Row{i / 4}", 8).transform;
                int replaceIndex = i;
                var def = _graph.Get(Battle.Library[i]);
                Ui.GlyphTile(row, def, false, () =>
                {
                    string dropped = Battle.Library[replaceIndex];
                    if (_run.PickReviveCharReplacing(_pendingReviveIndex, replaceIndex))
                        _message = Strings.T("battle.common.replaced_msg", ("incoming", incoming), ("dropped", dropped));
                    _pendingReviveIndex = -1;
                    Refresh();
                }, new Vector2(74, 96));
            }

            Ui.PillButton(stack, Strings.T("battle.btn.revive_replace_cancel"), () =>
            {
                _pendingReviveIndex = -1; // 退回候选列表,额度未动
                Refresh();
            }, Theme.LockedBg, Theme.TextMain, 16, new Vector2(150, 46));
        }

        /// <summary>选中某个奇遇选项时画进底部提示行的那句话(2026-08-27 用户拍板)。
        ///
        /// 效果说明**不在选项钮上** —— 钮宽 260 / 字号 22 只装得下 23 个半宽,而
        /// 「入炉淬骨(八成 上限 +30%,两成 反噬 −30%)」有 39 个,溢出到钮外被邻钮盖掉。
        /// 于是 label 只留名称,说明改由本方法送进屏幕最底部那条通栏提示行,不吃钮的宽度限。
        ///
        /// 尾部缀「再点一次」:奇遇是**不可逆**决策,首点只选中不结算(见 _previewEventOption),
        /// 玩家读完这句话再点第二下才真的执行 —— 那句提示就是告诉他还有第二下。
        /// 无效果的「离开」类选项显式画成「无」,不留空白 —— 空白读起来像没加载出来。</summary>
        private static string EventOptionHint(EventOption option) =>
            Strings.T("battle.event.option_detail",
                ("label", option.Label),
                ("detail", string.IsNullOrEmpty(option.Detail)
                    ? Strings.T("battle.event.option_detail_none")
                    : option.Detail))
            + Strings.T("battle.event.tap_again_suffix");

        private int _previewEventOption = -1; // 奇遇选项:首点看效果说明,再点同一个才结算(2026-08-27)
        // 预览态是为**哪一层**的奇遇留的。_previewEventOption 是实例字段,而奇遇一层一个 ——
        // 不记这个,挂起续爬或下一层的奇遇撞上同一个下标时,首点就会跳过说明直接结算,
        // 而那是一次不可逆操作。用 BattleIndex 而不是 evt.Id:奇遇池是有放回抽的,
        // 同一个 Id 会在不同层再出现。
        private int _previewEventFloor = -1;
        private int _pendingEventOption = -1; // 部件抵价/任选字:待成交的选项下标
        private int _pendingCharChoice = -1;  // 任选字:已选中的字下标(-1 = 未选)
        private readonly System.Collections.Generic.List<int> _eventPicks = new(); // 已点选的池下标

        private void DrawEvent() // 奇遇(9.6):短情境 + 选择;部件抵价/任选字由玩家点选(2026-07-19)
        {
            var evt = _run.CurrentEvent;
            if (_previewEventFloor != _run.BattleIndex || _previewEventOption >= evt.Options.Count)
            {
                _previewEventOption = -1;   // 换了一层(或选项数变少):预览态不跨奇遇沿用
                _previewEventFloor = _run.BattleIndex;
            }
            // evt.Id 是奇遇事件配置数据里的 id/展示名,不是本文件的硬编码文案——这里只登记
            // 「奇遇 · X」这层胶字模板本体。
            Ui.ThemedLabel(_enemyFrontRow, Strings.T("battle.event.title", ("eventName", evt.Id)), 30, Theme.TextMain, Theme.TitleFont);
            // 情境文案画在战场那一排,**不是** _statusRow(2026-08-20 修回):_statusRow 在屏幕
            // 最底边,把文案放那儿会变成 选项钮 → 部件池 → 文案,玩家得先看见三个按钮、
            // 再把视线甩到屏幕底边才读得到自己在选什么。这一排(0.431–0.543)在奇遇阶段本来
            // 就是空的(四排只在战斗阶段画),且**高于**选项钮所在的 _centerRow(0.125–0.220),
            // 阅读顺序因此是 文案 → 选项。2026-08-21 标题也搬到了左下,但这条理由与标题无关。
            // 正文直接用 evt.Text —— 它是事件数据,不套胶字模板(2026-08-27:此前还在正文尾部
            // 缀「(墨锭 N)」,而顶栏本来就有墨锭那一格,重复一遍反倒抢正文的注意力)。
            // 效果说明**不在这里**:选中某个选项时才画进底部提示行,见选项钮的 onClick。
            Ui.ThemedLabel(_summonFrontRow, evt.Text, 18, Theme.TextDim);

            if (_pendingEventOption >= 0)
            {
                var pending = evt.Options[_pendingEventOption];
                if (_eventReplacing) // 字与部件都备齐,只差「换掉哪一张」(2026-07-22)
                {
                    DrawEventReplaceStep(pending);
                    return;
                }
                bool needCharChoice = pending.GainCharChoices.Count > 0 && _pendingCharChoice < 0;
                Ui.ThemedLabel(_centerRow, needCharChoice
                        ? Strings.T("battle.event.pick_char_prompt", ("optionLabel", pending.Label))
                        : Strings.T("battle.event.pick_components_prompt",
                            ("optionLabel", pending.Label), ("cost", pending.ComponentCost), ("picked", _eventPicks.Count)),
                    20, Theme.TextMain, Theme.TitleFont);
                Ui.RoundButton(_centerRow, Strings.T("battle.btn.cancel"), () =>
                {
                    ResetEventSelection();
                    _message = "";
                    Refresh();
                }, Theme.LockedBg, Theme.TextMain, 16, new Vector2(84, 48));
                if (needCharChoice)
                    DrawEventCharChoices(pending);
                else
                    DrawEventPoolPicker(pending);
                return;
            }

            // ⚠ 钮上只画 option.Label 这个**名称**,效果说明由 EventOptionDetails 列在正文下方
            // (2026-08-27 用户拍板)。标签是 horizontalOverflow = Overflow —— 超宽**不会换行也不会
            // 省略号**,而是溢出到钮外被邻钮的底图盖掉,玩家看到的就是「描述展示不全」
            // (旧口径把效果写进 label,「入炉淬骨(八成 上限 +30%,两成 反噬 −30%)」39 个半宽 = 429px,
            // 钮宽 260 只装得下 23 个,后半截整个不见)。
            // 钮宽 260 / 字号 22 的容量是 23 个半宽;EventLabelWidthTests 钉住这条,新增选项时
            // 别再把效果塞回 label —— 那是同一个坑。
            for (int i = 0; i < evt.Options.Count; i++)
            {
                int index = i;
                var option = evt.Options[i];
                bool affordable = option.InkCost <= _run.AvailableInk
                    && option.ComponentCost <= _run.CarriedPool.Count
                    && AnyGainable(option); // 给的字都不在出阵列表 → 整个选项置灰(2026-07-20)
                var button = Ui.RoundButton(_centerRow, option.Label, () =>
                {
                    // 首点只选中(2026-08-27 用户拍板):效果说明画进底部提示行,钮转高亮,
                    // **不结算**。奇遇是不可逆决策,而钮上只有名称 —— 不给这一步,玩家就是在
                    // 盲点。再点同一个才往下走;点另一个则换成那一个的说明。
                    // 与战利品页的 _previewRewardIndex 同一套惯例(battle.reward.tap_again_suffix)。
                    if (_previewEventOption != index)
                    {
                        _previewEventOption = index;
                        _previewEventFloor = _run.BattleIndex;
                        _message = EventOptionHint(option);
                        Refresh();
                        return;
                    }
                    _previewEventOption = -1; // 确认了,这一格的预览态就地清掉
                    if (option.ComponentCost > 0 || option.GainCharChoices.Count > 0)
                    {
                        _pendingEventOption = index; // 进入选件/选字模式
                        _pendingCharChoice = -1;
                        _eventPicks.Clear();
                        _message = option.GainCharChoices.Count > 0
                            ? Strings.T("battle.event.pick_char_only")
                            : Strings.T("battle.event.barter_prompt", ("cost", option.ComponentCost));
                        Refresh();
                        return;
                    }
                    int inkBefore = _run.AvailableInk;
                    var beforeHoldings = SnapshotHoldings(); // 结算前拍一张,结算后 diff 出拿到了什么
                    if (_run.ChooseEventOption(index))
                    {
                        _message = option.InkChancePercent > 0 // 赌注:按墨锭变化播报输赢
                            ? (_run.AvailableInk > inkBefore
                                ? Strings.T("battle.event.gamble_win", ("ink", option.Ink))
                                : Strings.T("battle.event.gamble_lose"))
                            : $"{evt.Id}:{option.Label}";
                        MarkFreshSince(beforeHoldings); // 拿到的字/部件高亮,与战利品同一套读法
                        CancelSelection();
                        return;
                    }
                    // 固定赠字的奇遇满库同样转替换子步(2026-07-22),与字摊一致;
                    // 买不起是另一回事(Core 先查 InkCost),那种情况不进替换
                    if (option.InkCost <= _run.AvailableInk && option.GainChar != null
                        && _run.CarriedLibrary.Count >= Battle.LibraryCapacity)
                    {
                        _pendingEventOption = index;
                        _pendingCharChoice = -1;
                        _eventPicks.Clear();
                        _eventReplacing = true;
                        _message = Strings.T("battle.event.replace_needed_msg", ("charId", option.GainChar));
                        Refresh();
                        return;
                    }
                    CancelSelection();
                    ShowAlert(Strings.T("battle.dialog.event_unaffordable.title"), option.InkCost > _run.AvailableInk
                        ? Strings.T("battle.dialog.event_unaffordable.body_ink",
                            ("label", option.Label), ("cost", option.InkCost), ("available", _run.AvailableInk))
                        : Strings.T("battle.dialog.event_unaffordable.body_failed", ("label", option.Label)));
                }, !affordable ? Theme.LockedBg : index == _previewEventOption ? Theme.Cinnabar : Theme.InkSoft,
                    affordable ? Color.white : Theme.TextDim, 22, new Vector2(260, 72));
                button.interactable = affordable;
            }
        }

        private bool _eventReplacing; // 字摊交易已备齐但字库满:等玩家选换掉哪一张

        /// <summary>字摊满库替换(2026-07-22):走模态弹窗、字牌每行 4 个换行铺开(此前塞在
        /// 拆合台一行里挤成一团)。字与部件都已选定,只补一个替换目标即成交;取消则部件不少。</summary>
        private void DrawEventReplaceStep(EventOption option)
        {
            string incoming = _pendingCharChoice >= 0
                ? option.GainCharChoices[_pendingCharChoice] : option.GainChar;

            if (_modal != null) Object.Destroy(_modal);
            _modal = Ui.ModalShell(transform, Strings.T("battle.event.replace_title", ("charId", incoming)),
                new Vector2(360, 240), dismissable: false, out var stack);
            Ui.ThemedLabel(stack, Strings.T("battle.common.replace_warning"), 15, Theme.TextDim);

            Transform row = null;
            for (int i = 0; i < _run.CarriedLibrary.Count; i++)
            {
                if (i % 4 == 0) row = Ui.Row(stack, $"Row{i / 4}", 8).transform;
                int replaceIndex = i;
                var def = _graph.Get(_run.CarriedLibrary[i]);
                Ui.GlyphTile(row, def, false, () =>
                {
                    string dropped = _run.CarriedLibrary[replaceIndex];
                    var picks = _eventPicks.Count > 0 ? _eventPicks.ToArray() : null;
                    var beforeHoldings = SnapshotHoldings();
                    if (_run.ChooseEventOption(_pendingEventOption, picks, _pendingCharChoice, replaceIndex))
                    {
                        _message = Strings.T("battle.event.trade_replaced_msg", ("incoming", incoming), ("dropped", dropped));
                        MarkFreshSince(beforeHoldings);
                        if (_modal != null) Object.Destroy(_modal);
                        ResetEventSelection();
                        CancelSelection();
                        return;
                    }
                    Refresh();
                }, new Vector2(74, 96));
            }

            Ui.PillButton(stack, Strings.T("battle.btn.replace_cancel"), () =>
            {
                if (_modal != null) Object.Destroy(_modal);
                ResetEventSelection();
                _message = Strings.T("battle.event.trade_cancel_msg");
                Refresh();
            }, Theme.LockedBg, Theme.TextMain, 16, new Vector2(150, 46));
        }

        private void ResetEventSelection()
        {
            _previewEventOption = -1; // 从选件/选字子步退回选项列表:预览态也要归零,
                                      // 不然玩家再点同一个钮会跳过说明直接结算
            _pendingEventOption = -1;
            _pendingCharChoice = -1;
            _eventReplacing = false;
            _eventPicks.Clear();
        }

        /// <summary>选项是否还有能入手的字:不给字的选项(纯墨锭/血量)恒为 true。</summary>
        private bool AnyGainable(EventOption option)
        {
            if (option.GainCharChoices.Count > 0)
            {
                foreach (var id in option.GainCharChoices)
                    if (CanGain(id)) return true;
                return false;
            }
            return option.GainChar == null || CanGain(option.GainChar);
        }

        /// <summary>此字能否入手:出阵列表之外的字换到也不能合、口径与战利品一致(RunEngine 会拒)。</summary>
        private bool CanGain(string charId)
        {
            var unlocked = Battle.UnlockedChars;
            if (unlocked == null) return true;
            foreach (var id in unlocked)
                if (id == charId) return true;
            return false;
        }

        /// <summary>任选字:候选平铺(元素色字牌),点选即定;无部件成本则当场成交。</summary>
        private void DrawEventCharChoices(EventOption option)
        {
            for (int i = 0; i < option.GainCharChoices.Count; i++)
            {
                int choice = i;
                string charId = option.GainCharChoices[i];
                var def = _graph.Get(charId);
                if (!CanGain(charId)) // 不在出阵列表:换到也白换,直接置灰(2026-07-20)
                {
                    var locked = Ui.RoundButton(_poolRow, charId, null,
                        Theme.LockedBg, Theme.TextDim, 26, new Vector2(64, 64), 12);
                    locked.interactable = false;
                    continue;
                }
                Ui.RoundButton(_poolRow, charId, () =>
                {
                    _pendingCharChoice = choice;
                    if (option.ComponentCost > 0)
                    {
                        _message = Strings.T("battle.event.pick_components_for_char", ("charId", charId), ("cost", option.ComponentCost));
                        Refresh();
                        return;
                    }
                    var beforeHoldings = SnapshotHoldings();
                    if (_run.ChooseEventOption(_pendingEventOption, null, choice))
                    {
                        _message = Strings.T("battle.event.trade_got_msg", ("charId", charId));
                        MarkFreshSince(beforeHoldings);
                        ResetEventSelection();
                        CancelSelection();
                        return;
                    }
                    if (_run.CarriedLibrary.Count >= Battle.LibraryCapacity)
                    {
                        _eventReplacing = true; // 满库不再是死路:转入「换掉哪一张」
                        _message = Strings.T("battle.event.replace_needed_msg", ("charId", charId));
                        Refresh();
                        return;
                    }
                    ResetEventSelection();
                    CancelSelection();
                    ShowAlert(Strings.T("battle.dialog.event_char_unaffordable.title"),
                        Strings.T("battle.dialog.event_char_unaffordable.body", ("charId", charId)));
                }, Theme.ElementSoft(def.Element), Theme.ElementSoftFg(def.Element),
                    26, new Vector2(64, 64), 12);
            }
        }

        /// <summary>抵价选件:携带池平铺,点选高亮,凑够数自动成交。</summary>
        private void DrawEventPoolPicker(EventOption option)
        {
            Ui.ThemedLabel(_poolRow, Strings.T("battle.event.pool_title"), 16, Theme.TextDim, Theme.TitleFont);
            for (int i = 0; i < _run.CarriedPool.Count; i++)
            {
                int index = i;
                string charId = _run.CarriedPool[i];
                var def = _graph.Get(charId);
                bool picked = _eventPicks.Contains(index);
                Ui.RoundButton(_poolRow, charId, () =>
                {
                    if (picked) _eventPicks.Remove(index);
                    else _eventPicks.Add(index);
                    if (_eventPicks.Count == option.ComponentCost)
                    {
                        var beforeHoldings = SnapshotHoldings();
                        if (_run.ChooseEventOption(_pendingEventOption, _eventPicks.ToArray(), _pendingCharChoice))
                        {
                            MarkFreshSince(beforeHoldings);
                            string gained = _pendingCharChoice >= 0
                                ? option.GainCharChoices[_pendingCharChoice] : option.GainChar;
                            _message = gained != null
                                ? Strings.T("battle.event.trade_got_msg", ("charId", gained))
                                : Strings.T("battle.event.trade_success_label_msg", ("optionLabel", option.Label));
                            ResetEventSelection();
                            CancelSelection();
                            return;
                        }
                        if (_run.CarriedLibrary.Count >= Battle.LibraryCapacity)
                            _eventReplacing = true; // 满库转入「换掉哪一张」(先验后扣,部件未损)
                        Refresh();
                        return;
                    }
                    Refresh();
                }, picked ? Theme.ElementColor(def.Element) : Theme.ElementSoft(def.Element),
                    picked ? Color.white : Theme.ElementSoftFg(def.Element), 22, new Vector2(56, 56), 12);
            }
        }

        /// <summary>部件超上限(2026-07-24):逐个决议——用当前溢出部件换掉池中一个,或跳过不要。</summary>
        private void DrawEventOverflowStep()
        {
            var overflow = _run.PendingOverflow;
            if (overflow.Count == 0) return; // 决议完成的过渡帧
            string incoming = overflow[0];
            Ui.ThemedLabel(_enemyFrontRow, Strings.T("battle.event.overflow_title"), 30, Theme.TextMain, Theme.TitleFont);
            // 警示文案画在战场那一排,**不是** _statusRow(2026-08-20 修回,同 DrawEvent 那一处):
            // _statusRow 在屏幕最底边,放那儿玩家很可能在没读到「永久失去」之前就点掉了一次
            // 不可逆操作。这一排在部件超限阶段本来就是空的,且高于选项所在的 _centerRow。
            Ui.ThemedLabel(_summonFrontRow,
                Strings.T("battle.event.overflow_prompt", ("charId", incoming), ("remaining", overflow.Count)),
                18, Theme.TextDim);

            Ui.PillButton(_centerRow, Strings.T("battle.btn.overflow_skip", ("incoming", incoming)), () =>
            {
                _run.ResolveOverflowSkip();
                _message = Strings.T("battle.event.overflow_skip_msg", ("charId", incoming));
                Refresh();
            }, Theme.LockedBg, Theme.TextMain, 18, new Vector2(160, 56));

            Ui.ThemedLabel(_poolRow, Strings.T("battle.event.overflow_pool_title"), 16, Theme.TextDim, Theme.TitleFont);
            for (int i = 0; i < _run.CarriedPool.Count; i++)
            {
                int index = i;
                var def = _graph.Get(_run.CarriedPool[i]);
                Ui.RoundButton(_poolRow, def.Id, () =>
                {
                    string dropped = _run.CarriedPool[index];
                    _run.ResolveOverflowReplace(index);
                    _message = Strings.T("battle.event.overflow_replaced_msg", ("incoming", incoming), ("dropped", dropped));
                    Refresh();
                }, Theme.ElementSoft(def.Element), Theme.ElementSoftFg(def.Element),
                    22, new Vector2(56, 56), 12);
            }
        }

        private void DrawRunEnd()
        {
            bool won = _run.Phase == RunPhase.RunWon;
            bool tower = _onExit != null; // 无尽:胜=Boss 层告捷进安全层,负=塔结算
            Ui.ThemedLabel(_centerRow, won
                    ? (tower ? Strings.T("battle.phase.run_won_tower_banner") : Strings.T("battle.phase.run_won_stage_banner"))
                    : Strings.T("battle.phase.defeat_banner"),
                40, Theme.TextMain, Theme.TitleFont);
            Ui.PillButton(_centerRow, won && tower ? Strings.T("battle.btn.to_safe_floor") : tower ? Strings.T("battle.btn.settle") : Strings.T("common.back_to_map"),
                () => _onRunEnded(won), Theme.Jade, Color.white, 26, new Vector2(190, 70));
            _message = won
                ? (tower ? Strings.T("battle.phase.run_won_tower_msg") : Strings.T("battle.phase.run_won_stage_msg"))
                : (tower ? Strings.T("battle.phase.run_lost_tower_msg") : Strings.T("battle.phase.run_lost_stage_msg"));
        }

        // ---- 交互 ----

        private void OnLibraryCharClicked(string charId, int index)
        {
            // 方向按钮已经画出来时(2026-09-02 审阅 Important),再点字牌本体不该绕过它们
            // 静默出字——玩家全程靠「选中→再点即出」这个手势操作,方向按钮刚出现时很容易
            // 沿用同一手势再点一次牌本体,若不挡在这里会跳过「攻」「护」两个可见按钮,
            // 静默按 CastInDirection 的默认分支(护)出字。
            if (_selectedChar == charId && _selectedIndex == index
                && !_targeting && !_allyTargeting && !_directionPicking)
            {
                OnCastPressed(_graph.Get(charId)); // 再点一次选中字 = 直接出字
                return;
            }
            _selectedChar = charId;
            _selectedIndex = index;
            _targeting = false;
            _allyTargeting = false;
            _pendingAllyEnemyTarget = -1;
            _directionPicking = false; // 改点了另一张字:上一张的方向选择作废(2026-09-02 终审修复)
            _pendingAttackMode = false;
            ResetSlotPicking(); // 改主意点了别的字:上一张的落位作废
            _message = Brief(charId) + Strings.T("battle.hint.suffix_tap_again_cast");
            Refresh();
        }

        private void OnPoolCharClicked(string charId)
        {
            // 方向按钮已经画出来时(2026-09-02 审阅 Important),再点部件本体不该绕过它们
            // 静默出字,理由同 OnLibraryCharClicked。
            if (_selectedChar == charId && _selectedIndex < 0
                && !_targeting && !_allyTargeting && !_directionPicking)
            {
                OnCastPressed(_graph.Get(charId)); // 再点一次选中部件 = 直出
                return;
            }
            _selectedChar = charId;
            _selectedIndex = -1;
            _targeting = false;
            _allyTargeting = false;
            _pendingAllyEnemyTarget = -1;
            _directionPicking = false; // 改点了另一张字:上一张的方向选择作废(2026-09-02 终审修复)
            _pendingAttackMode = false;
            ResetSlotPicking();
            _message = Brief(charId) + Strings.T("battle.hint.suffix_direct_cast");
            Refresh();
        }

        private void OnCastPressed(CharDef def)
        {
            // 双方向字(2026-09-02):有 AttackEffects 就先问方向。
            // AttackEffects 为空的字(火/金/木全部)一路直下,行为与改造前完全一致。
            if (def.AttackEffects.Count > 0 && !_directionPicking)
            {
                _directionPicking = true;
                _message = Strings.T("battle.hint.pick_direction", ("charId", def.Id));
                Refresh();
                return;
            }
            _directionPicking = false;
            CastInDirection(def, attackMode: false);
        }

        /// <summary>选定方向后的出字流程(2026-09-02)。<paramref name="attackMode"/>
        /// true = 攻击面(走 AttackEffects),false = 治疗/加盾面(走 Effects)。
        ///
        /// 这就是原来 OnCastPressed 的整个函数体,只是把写死的 attackMode: false
        /// 变成参数 —— 免选判据、友方目标判据、最终 BeginCast 三处全部跟着这个参数走,
        /// 否则会出现「选了攻击方向,却按治疗面判断要不要选目标」这种错位。
        ///
        /// ⚠ 良性冗余(2026-09-02 审阅提醒):下面进 _targeting/_allyTargeting 的两个分支
        /// 没有顺手把 _directionPicking 复位回 false,它会残留为 true。这不是 bug ——
        /// DrawActions 里 _targeting/_allyTargeting 的判断排在 _directionPicking 之前,
        /// 不会画出按钮重叠;CancelSelection() 最终也会把它连同其余交互态一起清干净。
        /// 留着不清是因为没有必要,不是漏改。</summary>
        private void CastInDirection(CharDef def, bool attackMode)
        {
            // 免选的判据是**合法目标**而不是存活敌人(2026-08-20):前排只剩一只时,
            // 出一张够不到后排的字本就没得选,还弹一次选目标纯属让玩家白点一下。
            // 与 Core 的 Cast 同口径 —— 那边合法目标恰好一个时会自动锁定。
            if (BattleEngine.NeedsTarget(def, attackMode) && LegalTargetCount(def, attackMode) > 1)
            {
                _targeting = true;
                _pendingAttackMode = attackMode;
                _message = Strings.T("battle.hint.cast_pick_enemy_target", ("charId", def.Id));
                Refresh();
                return;
            }
            // 友方目标(2026-08-22):场上有存活召唤物才进选目标态——没有的话引擎会自动锁玩家
            // (Cast 里 AliveSummons() == 0 那条免选口径),UI 弹一次没得选的选择纯属白点一下,
            // 与上面「单敌免选」同一条纪律。
            if (BattleEngine.NeedsAllyTarget(def, attackMode) && Battle.AliveSummonCount > 0)
            {
                EnterAllyTargeting(def, enemyTarget: -1, attackMode: attackMode);
                Refresh();
                return;
            }
            BeginCast(def.Id, -1, attackMode: attackMode, libraryIndex: _selectedIndex);
        }

        /// <summary>这张字现在有几只敌人点得动(2026-08-20)。判据走 <c>Battle.CanTarget</c>,
        /// 与置灰、与引擎的自动锁定三处同源。</summary>
        private int LegalTargetCount(CharDef def, bool attackMode)
        {
            int count = 0;
            for (int i = 0; i < Battle.Enemies.Count; i++)
                if (Battle.CanTarget(def, i, attackMode)) count++;
            return count;
        }

        // ---- 召唤落位(2026-08-20) ----

        /// <summary>出字总入口:召唤字先进选位子态,其余照旧直接结算。
        /// 目标已经选过了(若需要),这里只补落位。
        /// allySlot:治疗目标(2026-08-22),默认 Targeting.PlayerTarget(玩家)。</summary>
        private void BeginCast(string charId, int target, bool attackMode, int libraryIndex,
            int allySlot = Targeting.PlayerTarget)
        {
            var def = _graph.Get(charId);
            // AP 不够就别进选位置态(2026-08-20 review I-1):否则玩家会认真点完两格,
            // 凑齐后才被 Cast 的 NotEnoughAp 拒掉 —— 改动前是点「出字」当场就被拒,
            // 而林/桂/森 都是 2+ AP,这条很容易撞上。交给引擎当场报错,与改动前同口径。
            if (Battle.Ap < def.ApCost)
            {
                ExecuteCast(charId, target, attackMode: attackMode, libraryIndex: libraryIndex, allySlot: allySlot);
                return;
            }
            int summonCount = Battle.SummonCountOf(def, attackMode);
            if (summonCount <= 0)
            {
                ExecuteCast(charId, target, attackMode: attackMode, libraryIndex: libraryIndex, allySlot: allySlot);
                return;
            }
            EnterSlotPicking(charId, target, attackMode, libraryIndex, summonCount);
            Refresh();
        }

        /// <summary>只置位、不重绘 —— 拖拽路径需要在**不动字牌**的前提下点亮槽位:
        /// 全量 Refresh 会销毁正被拖的那张字牌,连带把 uGUI 的拖拽事件流掐断
        /// (OnEndDrag 再也不会来,字影留在屏幕上、状态卡死)。</summary>
        private void EnterSlotPicking(string charId, int target, bool attackMode,
            int libraryIndex, int summonCount)
        {
            _slotPicking = true;
            // 三个选目标态在这里必须互斥清干净:进选位态时,另外两态挂出去的高亮都得撤。
            // _targeting 的高亮画在敌人格上,后续全量 Refresh 会带走,漏清问题不大;
            // 但 _allyTargeting 的高亮(AttachAllyTargetPicker)画在玩家血条区(_bottomRow)——
            // 拖字召唤走的是 RedrawSummonRows 那条轻量重画路径,只重画召唤两排,够不着
            // _bottomRow。不在这里清掉的话,「治我」覆盖层会残留在玩家血条上且仍可点,
            // 直到下一次真正的全量 Refresh 才消失(2026-08-22 评审 Finding 追出的缺口)。
            _targeting = false;
            _allyTargeting = false;
            _pendingAllyEnemyTarget = -1;
            _pendingSummonChar = charId;
            _pendingSummonTarget = target;
            _pendingSummonAttackMode = attackMode;
            _pendingSummonLibraryIndex = libraryIndex;
            _pendingSummonCount = summonCount;
            _message = SlotPickMessage();
        }

        /// <summary>只重画我方两排(拖拽中点亮/熄灭槽位用)。字牌行不动,拖拽事件流不断。</summary>
        private void RedrawSummonRows()
        {
            Ui.Clear(_summonFrontRow);
            Ui.Clear(_summonBackRow);
            DrawSummons();
            _messageLabel.text = _message;
        }

        /// <summary>拖治疗/加盾字途中重画友方落点(2026-08-27):召唤两排 **+ 玩家血条区**。
        /// 比 <see cref="RedrawSummonRows"/> 多出 _bottomRow 那一块 —— 玩家本人是最常见的
        /// 施放目标,不重画它就只有召唤格会亮,玩家看不出还能拖到自己身上。
        ///
        /// 仍然不走全量 <see cref="Refresh"/>:那会 Ui.Clear(_libraryRow) 销毁正被拖的这张字牌。
        /// _bottomRow 与 _libraryRow 是两个互不包含的 section,清前者不碰后者。</summary>
        private void RedrawAllyTargets()
        {
            Ui.Clear(_summonFrontRow);
            Ui.Clear(_summonBackRow);
            DrawSummons();
            Ui.Clear(_bottomRow);
            DrawPlayerStats();
            _messageLabel.text = _message;
        }

        private string SlotPickMessage() => _pendingSummonCount > 1
            ? Strings.T("battle.hint.slot_picking_multi", ("charId", _pendingSummonChar), ("count", _pendingSummonCount))
            : Strings.T("battle.hint.slot_picking_single", ("charId", _pendingSummonChar));

        /// <summary>选定一个召唤位,当场结算(2026-08-21 用户拍板:**只选一次**)。
        ///
        /// 多只召唤(林/森/桂 各 2,四叠字 4)从选定那格起**顺延**占后面的槽,
        /// 走到 5 号绕回 0 号。此前是「连点 N 次、每只各选一格」——玩家要记住自己点到第几只,
        /// 而多出来的那点摆位自由并不值这份记账负担。
        ///
        /// 落位表由引擎的 <see cref="BattleEngine.PlanSummonSlots"/> 算,表现层不自己推这套规则:
        /// 召唤物落在哪是引擎语义,而且「不重复、长度恰好」那两条不变式也由引擎那边一并保证
        /// (破坏任一条会让第二只写进同一个槽或被静默吞掉,而 AP 已经扣了)。
        ///
        /// 2026-08-27 用户拍板:**第一只必落玩家点的那一格** —— 那格站着人就顶替它,于是
        /// ExecuteCast 会拿到 SummonCapFull 并弹一次替换确认。「跳到下一个空位」只服务于
        /// 一次召多只的第二只起(2026-08-23 那版把玩家对第一只的指定也一起跳了,点在有人的
        /// 格上召唤物落到隔壁,指定失效且毫无提示)。这一段本身两版都不用改 —— 规则全在引擎里。</summary>
        private void OnSlotPicked(int slot)
        {
            if (!_slotPicking || !Battle.IsSlotOpen(slot)) return;
            var slots = Battle.PlanSummonSlots(slot, _pendingSummonCount);

            string charId = _pendingSummonChar;
            int target = _pendingSummonTarget;
            bool attackMode = _pendingSummonAttackMode;
            int libraryIndex = _pendingSummonLibraryIndex;
            ResetSlotPicking();
            ExecuteCast(charId, target, attackMode: attackMode, libraryIndex: libraryIndex, summonSlots: slots);
        }

        private void ResetSlotPicking()
        {
            _slotPicking = false;
            _pendingSummonChar = null;
            _pendingSummonTarget = -1;
            _pendingSummonAttackMode = false;
            _pendingSummonLibraryIndex = -1;
            _pendingSummonCount = 0;
        }

        /// <summary>位子的人话名字:下标 0..5 玩家看不懂,说「前排第 2 位」才认得出是哪一格。</summary>
        private string SlotName(int slot) => slot < Battle.FrontRow
            ? Strings.T("battle.dialog.slot_name_front", ("n", slot + 1))
            : Strings.T("battle.dialog.slot_name_back", ("n", slot - Battle.FrontRow + 1));

        private void OnEnemyClicked(int index)
        {
            if (_targeting && _selectedChar != null)
            {
                // 够不到的怪已经置灰且 interactable = false,走不到这;真走到了也直接忽略 ——
                // 落到下面的「看详情」分支会让玩家以为自己点歪了
                var picked = _graph.Get(_selectedChar);
                if (!Battle.CanTarget(picked, index, _pendingAttackMode)) return;
                // 还要选友方就转第二段,别在这里就出字(2026-08-26)。免选口径与 OnCastPressed
                // 那条同源:场上没有存活召唤物时引擎会自动锁玩家,弹一次没得选的选择纯属白点。
                if (BattleEngine.NeedsAllyTarget(picked, _pendingAttackMode) && Battle.AliveSummonCount > 0)
                {
                    EnterAllyTargeting(picked, enemyTarget: index, attackMode: _pendingAttackMode);
                    Refresh();
                    return;
                }
                BeginCast(_selectedChar, index, attackMode: _pendingAttackMode, libraryIndex: _selectedIndex);
                return;
            }
            // 非选目标态点怪 = 看详情(2026-07-22);此前这里什么也不做。
            // 2026-09-01 改走 UnitSheet + EnemyInfo.Sheet(单位详情轮二 Task 5):原来的
            // EnemyPreview 是按 EnemyDef 画的图鉴式预览,拿不到战斗中的实时状态(当前血量、
            // 身上挂着什么状态)。EnemyPreview 本身不删——BestiaryView.cs 两处怪物图鉴调用点
            // 还在用它。
            if (_modal != null) Object.Destroy(_modal);
            _unitSheetSource = () => EnemyInfo.Sheet(Battle.Enemies[index]);
            _modal = UnitSheet.Show(transform, _unitSheetSource());
        }

        private void ExecuteCast(string charId, int target, bool replaceSummon = false, bool attackMode = false,
            int libraryIndex = -1, IReadOnlyList<int> summonSlots = null, int allySlot = Targeting.PlayerTarget)
        {
            bool hasFrom = TryGetCastFromPos(charId, libraryIndex, out var fromPos); // 起点须在重绘销毁字牌前捕获
            SnapshotPreHp(); // 出手前血量:动画期间血条画在此值,伤害触达才逐记掉血
            var error = Battle.Cast(charId, target, replaceSummon, attackMode, libraryIndex, summonSlots, allySlot);
            if (error == BattleError.SummonCapFull) // 顶替强阻断:AP/字都没动,确认了才重出
            {
                var def = _graph.Get(charId);
                int replaceCount = Battle.SummonReplaceCountOf(def, attackMode, summonSlots);
                ShowModal(Strings.T("battle.dialog.slot_occupied.title"),
                    ReplaceSummonBody(def, attackMode, summonSlots),
                    (Strings.T("battle.btn.confirm_replace_summon", ("count", replaceCount)),
                        () => ExecuteCast(charId, target, replaceSummon: true, attackMode, libraryIndex, summonSlots, allySlot),
                        Theme.Cinnabar, Color.white),
                    (Strings.T("battle.btn.cancel"), null, Theme.LockedBg, Theme.TextMain));
                _message = Strings.T("battle.msg.slot_occupied_pending");
                CancelSelection();
                return;
            }
            if (error == BattleError.None)
                _tutorial?.Notify(TutorialAction.Cast, charId);
            else
                MaybeModalError(error, charId, _graph.Get(charId).ApCost);
            _message = error == BattleError.None ? Strings.T("battle.msg.cast_success", ("charId", charId)) : Describe(error);
            AppendBossPhaseMessage();
            // 蓄力/释放/护盾被掀空事件只产自 EndTurn(见 OnEndTurn 处的 AppendBossSkillMessage),
            // Cast() 自己的 _events 永远不会有这三种——此前这里的调用是死代码(F4,2026-07-29)
            var deaths = error == BattleError.None ? DeathsThisAction() : new System.Collections.Generic.List<int>();
            _dyingEnemies.UnionWith(deaths); // 登记须在 CancelSelection 重绘前:重绘据此保持死怪着色
            if (error == BattleError.None) DropReplacedSummonSnapshots(); // 被顶替的槽位:改画新召唤物,别停在旧血量
            if (error == BattleError.None) BeginAnim(); // 锁输入 + 血条改画出手前值,须在重绘前置位
            CancelSelection();
            if (error == BattleError.None)
            {
                // 飞牌到首个受击敌人,到达才播结算表现;事件快照防连点串场
                var events = new System.Collections.Generic.List<BattleEvent>(Battle.LastEvents);
                var toRect = CastTargetRect(events);
                if (hasFrom && toRect != null)
                    _juice.FlyGlyph(charId, Theme.ElementColor(_graph.Get(charId).Element), fromPos, toRect.position,
                        () => PlayAnimated(events, deaths));
                else
                    PlayAnimated(events, deaths); // 无伤害目标(纯护盾等)或起点缺失:即时表现
                MaybeAutoEndTurn();
            }
        }

        /// <summary>顶替确认的正文:按玩家实际点的位子说话(2026-08-20)。
        /// summonSlots 为 null 是自动落位的老路径(表现层眼下都走选位子,只剩兜底意义),
        /// 那时说不出具体是哪一格,退回旧口径的整体说法。</summary>
        private string ReplaceSummonBody(CharDef def, bool attackMode, IReadOnlyList<int> summonSlots)
        {
            int count = Battle.SummonCountOf(def, attackMode);
            if (summonSlots == null)
                return Strings.T("battle.dialog.slot_occupied.body_generic",
                        ("alive", Battle.AliveSummonCount), ("capacity", Battle.SummonCapacity),
                        ("charId", def.Id), ("count", count))
                    + Strings.T("battle.dialog.slot_occupied.body_generic_suffix",
                        ("n", Battle.SummonReplaceCountOf(def, attackMode)));
            var body = new StringBuilder();
            for (int n = 0; n < count && n < summonSlots.Count; n++)
            {
                int slot = summonSlots[n];
                if (Battle.SlotOccupancy(slot) != SlotState.Alive) continue;
                body.Append(Strings.T("battle.dialog.slot_occupied.body_line",
                    ("slotName", SlotName(slot)), ("charId", Battle.Summons[slot].Char)));
            }
            body.Append(Strings.T("battle.dialog.slot_occupied.body_suffix")); // 不带量词:顶 1 只与顶 2 只共用这一句
            return body.ToString();
        }

        /// <summary>召唤替换(Summon 事件带被顶替槽位):抹掉该槽的出手前血量快照,
        /// 让动画期间就画新召唤物的满血,不残留旧值(旧值配新上限会显示成半血)。</summary>
        private void DropReplacedSummonSnapshots()
        {
            foreach (var e in Battle.LastEvents)
                if (e.Kind == BattleEventKind.Summon && e.SecondIndex >= 0)
                {
                    _summonAnimHp.Remove(e.SecondIndex);
                    _summonAnimShield.Remove(e.SecondIndex);
                }
        }

        /// <summary>出字动效终点:第一个受击/受灼敌人格;没有则 null。</summary>
        private RectTransform CastTargetRect(System.Collections.Generic.IReadOnlyList<BattleEvent> events)
        {
            foreach (var e in events)
                if ((e.Kind == BattleEventKind.Damage || e.Kind == BattleEventKind.Burn)
                    && e.TargetIndex >= 0 && e.TargetIndex < _enemyRects.Count)
                    return _enemyRects[e.TargetIndex];
            return null;
        }

        private float _autoEndDueAt; // AP 耗尽后自动结束回合的时点;每次动作重置,给连续丢弃留手

        /// <summary>AP 耗尽自动结束回合:留短缓冲看清结算;免 AP 丢弃会顺延缓冲。</summary>
        private void MaybeAutoEndTurn()
        {
            if (_run.Phase != RunPhase.InBattle || Battle.Phase != BattlePhase.PlayerTurn || Battle.Ap != 0)
                return;
            _autoEndDueAt = Time.unscaledTime + 0.45f;
            StartCoroutine(AutoEndTurn());
        }

        private System.Collections.IEnumerator AutoEndTurn()
        {
            // 等缓冲到点「且」出牌动画播完:与结算串行,免两段动画重叠(输入锁/血条引用错乱)
            while (Time.unscaledTime < _autoEndDueAt || Animating)
                yield return null;
            if (_run.Phase != RunPhase.InBattle || Battle.Phase != BattlePhase.PlayerTurn || Battle.Ap != 0)
                yield break; // 期间局面已变(胜负已分/新回合)则作罢
            OnEndTurn();
            _messageLabel.text = Strings.T("battle.phase.auto_end_turn_prefix") + _message;
        }

        private void AppendBossPhaseMessage()
        {
            foreach (var e in Battle.LastEvents)
                if (e.Kind == BattleEventKind.BossPhase)
                {
                    var enemy = Battle.Enemies[e.TargetIndex];
                    // 显示用元素名走 CharInfo.ElementName(查表),同 :1439 的敌人属性 chip。
                    _message += Strings.T("battle.msg.boss_phase_change",
                        ("char", enemy.Def.Phases[e.Amount].Char), ("element", CharInfo.ElementName(enemy.Element)));
                }
        }

        private void AppendBossSkillMessage()
        {
            foreach (var e in Battle.LastEvents)
            {
                if (e.Kind == BattleEventKind.BossCharging)
                    _message += Strings.T("battle.msg.boss_charging", ("skillName", EnemyInfo.BossSkillName((BossSkill)e.Amount)));
                else if (e.Kind == BattleEventKind.BossSkillCast)
                    _message += $"  {EnemyInfo.BossSkillName((BossSkill)e.Amount)}!";
                else if (e.Kind == BattleEventKind.ShieldBroken)
                    _message += Strings.T("battle.msg.shield_broken", ("amount", e.Amount));
            }
        }

        private void OnDiscard(string charId)
        {
            var error = Battle.Discard(charId, _selectedIndex); // 同字多张:丢玩家选中的那张
            _message = error == BattleError.None ? Strings.T("battle.msg.discard_success", ("charId", charId)) : Describe(error);
            CancelSelection();
            if (error == BattleError.None)
                MaybeAutoEndTurn(); // 0 AP 时的丢弃:顺延自动结束缓冲,可连续丢
        }

        private void OnDismantle(string charId)
        {
            bool hasFrom = TryGetTilePos(charId, out var fromPos);
            var recipe = _graph.Get(charId).Recipe;
            var error = Battle.Dismantle(charId);
            if (error == BattleError.None)
            {
                _tutorial?.Notify(TutorialAction.Dismantle, charId);
                // 拆出来的两个部件持续高亮(2026-08-30):落位弹跳只有 0.16s,而部件池里
                // 一排同色小方块,眨眼就找不出刚才拆出来的是哪两个 —— 光晕替玩家指着它们。
                // ⚠ 必须记在 CancelSelection() **之前**:光晕是重绘时照表套上去的,
                // 而那一行就是本次重绘的触发点,记晚一步这一轮就白记了
                MarkFresh(recipe);
            }
            else
                MaybeModalError(error, charId, 1);
            _message = error == BattleError.None ? Strings.T("battle.msg.dismantle_success", ("charId", charId)) : Describe(error);
            CancelSelection();
            if (error == BattleError.None)
            {
                if (hasFrom) // 部件从原字牌位置散落到池中新位(重绘后已登记)
                    foreach (var part in recipe)
                    {
                        if (!_tileRects.TryGetValue(part, out var partTile) || partTile == null) continue;
                        var target = partTile;
                        _juice.FlyGlyph(part, Theme.ElementColor(_graph.Get(part).Element), fromPos, target.position,
                            () => _juice.PopTile(target));
                    }
                MaybeAutoEndTurn();
            }
        }

        private void OnCompose(string charId)
        {
            // 起点须在重绘销毁部件牌前捕获
            var partsFrom = new System.Collections.Generic.List<(string glyph, Vector3 pos)>();
            foreach (var part in _graph.Get(charId).Recipe)
                if (TryGetTilePos(part, out var pos))
                    partsFrom.Add((part, pos));
            var error = Battle.Compose(charId);
            if (error == BattleError.None)
            {
                _tutorial?.Notify(TutorialAction.Compose, charId);
                // 合出来的字持续高亮,与拆字同一套:满库 12 张牌里新多出来的那张要能一眼认出。
                // 同样必须记在下面 CancelSelection() 的重绘之前
                MarkFresh(charId);
            }
            else
                MaybeModalError(error, charId, 1);
            _message = error == BattleError.None ? Strings.T("battle.msg.compose_success", ("charId", charId)) : Describe(error);
            CancelSelection();
            if (error == BattleError.None)
            {
                if (_tileRects.TryGetValue(charId, out var resultTile) && resultTile != null && partsFrom.Count > 0)
                {
                    int arrived = 0; // 闭包共享:全部到齐才弹跳
                    foreach (var (glyph, pos) in partsFrom)
                        _juice.FlyGlyph(glyph, Theme.ElementColor(_graph.Get(glyph).Element), pos, resultTile.position,
                            () => { if (++arrived == partsFrom.Count) _juice.PopTile(resultTile); });
                }
                MaybeAutoEndTurn();
            }
        }

        private void OnEndTurn()
        {
            if (Battle.Phase != BattlePhase.PlayerTurn) return;
            BeginAnim();               // 锁输入:整段推进期间不许出字,须在重绘前置位
            CancelSelection();
            Battle.YieldTurn();
            _tutorial?.Notify(TutorialAction.EndTurn);
            StartCoroutine(AdvanceRoutine());
        }

        /// <summary>逐格推进(2026-08-15 ATB 改造):一次 AdvanceOnce 播一个行动者的动画,原先是
        /// 一次 EndTurn 拿到整轮事件、由 Juice 猜边界切三段——现在边界由引擎给,段间停顿挪到这里。
        /// 全程只在 OnEndTurn 里 BeginAnim 过一次,循环内不逐批调用 OnAnimDone(那会提前把
        /// _animsInFlight 归零、误放行输入)——死亡怪下标累积到 allDeaths,循环结束后一次性调用
        /// OnAnimDone 收尾解锁 + 清死亡着色(否则 _dyingEnemies 里较早批次的下标会一直挂着,
        /// 撞上下一场战斗同下标的新敌人,把它显示成灰)。
        /// AppendBossSkillMessage 须按批调用而非等循环结束才读一次:蓄力/释放/护盾被掀空这些事件
        /// 可能出在本轮任意一个行动者的批次里,循环结束后 Battle.LastEvents 只剩最后一批
        /// (通常是轮回玩家那个只带 ActorActed 标记的空批),整轮跑到一半的播报会被静默吞掉。</summary>
        private System.Collections.IEnumerator AdvanceRoutine()
        {
            _message = ""; // 蓄力/释放/护盾被掀空的播报按批累加在这里,回合前缀最后再补上
            var allDeaths = new System.Collections.Generic.List<int>();
            var drawnIndices = new System.Collections.Generic.List<int>(); // 本轮掉字落在哪几个卡位
            while (true)
            {
                SnapshotPreHp();       // 每个行动者出手前的血量:动画逐记扣
                var preMeters = MeterSnapshot();   // 推进前的计量器:条从这里起步
                bool more = Battle.AdvanceOnce();
                var postMeters = MeterSnapshot();  // 推进后:条的终点
                var events = SplitOutDrawn(Battle.LastEvents, drawnIndices);
                var deaths = DeathsThisAction();
                _dyingEnemies.UnionWith(deaths); // 登记须在下面 Refresh 前:重绘据此保持死怪着色
                allDeaths.AddRange(deaths);
                AppendBossSkillMessage();
                // 条先匀速涨到位(行动者停在 100%),再播它的动作 —— 动作期间条冻结。
                // 这一段在 Refresh 之前:条的 fill/label 引用来自**上一次**重绘,还有效。
                yield return FillActionBars(preMeters, postMeters, Battle.LastActor,
                    Battle.LastAdvanceTicks);
                // 每批事件必以 ActorActed 开头(段首标记),所以 events.Count > 0 恒真——
                // 哪怕这一拍除了标记什么都没发生,也会误播整段 _juice.Play() + 停顿(约 0.42s)。
                // 只在还有别的事件时才播。
                if (events.Any(e => e.Kind != BattleEventKind.ActorActed))
                {
                    bool done = false;
                    _juice.Play(events, EnemyAnchor, SummonAnchor, () => done = true, OnImpact, SummonAt,
                        EnemyElement);
                    while (!done) yield return null;
                    yield return _juice.Wait(0.12f); // 行动者之间的停顿(替代已删的 Juice.PhaseGap);走 Juice 的节拍才吃快进
                }
                DropActingBar(Battle.LastActor, postMeters); // 动作播完,行动者的条回落到余额
                Refresh();
                if (!more) break;
                if (Battle.Phase == BattlePhase.Won || Battle.Phase == BattlePhase.Lost) break;
            }
            _message = (Battle.Phase == BattlePhase.PlayerTurn
                ? Strings.T("battle.phase.new_turn_prefix", ("turn", Battle.Turn), ("apPerTurn", Battle.ApPerTurn)) : "") + _message;
            OnAnimDone(allDeaths); // 解锁输入(_animsInFlight 归零)+ 清死亡着色 + 归零后重绘
            // 抽卡动画必须排在 OnAnimDone **之后**(2026-08-27 修):循环内那几次 Refresh 都发生在
            // BeginAnim 的锁里,而 Refresh 的玩家回合分支遇 Animating 会在 DrawLibrary() 之前
            // 就 break —— 动画期间字库一张牌都不画(且 Refresh 开头的 Ui.Clear 已把旧牌销毁)。
            // 放在 OnAnimDone 之前,DealRoutine 拿到的 _libraryTileRects 恒为空,整段静默空跑。
            // 是 OnAnimDone 归零后那次 Refresh 才把新牌建出来的。
            yield return DealRoutine(drawnIndices);
        }

        /// <summary>回放开场那几拍(2026-08-17)。构造函数已经把开场推进跑完了(spec §5.2),
        /// 所以这里**不推进任何状态**,只把 Core 记下的 OpeningSteps 逐拍演出来 ——
        /// 否则玩家会看到「进战斗即已打完」(携带满格召唤物秒掉弱敌,spec §5.7),
        /// 同速开局则看不到条从 0% 涨起来、直接就是自己的回合。
        ///
        /// 循环体与 AdvanceRoutine 一致,差别只在数据来源(回放 vs 现场推进)。
        /// pre 的串法:首拍从全 0 起——这对玩家成立(PlayerActionMeter 恒从 0 起步),
        /// 但对携带满格召唤物**不成立**(它进场就是 Threshold,不是 0)。这处近似没有
        /// 可见后果:该拍必然 Ticks == 0(FirstFull 分支的充要条件),下面在播放前会把
        /// 行动者的条单独按到 Threshold 再播,不依赖这里的 pre 值(2026-08-18 修 I3)。
        /// 之后每拍的 pre 是上一拍的 post,这段是精确的——Core 逐拍记的就是真实计量器。
        ///
        /// ⚠ 血条只能画在开场**结束后**的值上:Core 没记开场前的血量,表现层无从复原。
        /// 于是开场里挨了打却没死的怪,其血条会被 OnImpact 从终值再往下推一段(PushEnemyHp
        /// 刻意不钳终值),由收尾的 Refresh 兜回去。玩家/召唤物侧的 OnImpact 钳的是终值下限,
        /// 不会偏。开场通常只有一拍(玩家自己),这条只在携带满格召唤物时才看得见。
        ///
        /// ⚠ 当前配速下(全部字怪 Speed = 100)本方法还有两条限制没处理,一旦给敌人配速就要
        /// 一起补(详见 Core.EnemyDef.Speed 的文档):没接 AppendBossSkillMessage(Boss 蓄力
        /// 播报读的是 Battle.LastEvents 而非逐拍的 step.Events);开场中途死掉的召唤物在
        /// DrawSummons 里连头像格都不画(SnapshotPreHp 只在开场结束后跑一次)。</summary>
        private System.Collections.IEnumerator OpeningRoutine()
        {
            var steps = Battle.OpeningSteps;
            var pre = (player: 0, summons: new int[Battle.Summons.Count],
                enemies: new int[Battle.Enemies.Count]);
            // 开场里死掉的怪一次收齐(判定口径同 DeathsThisAction:LastEvents 里的 EnemyDied)。
            // 登记进 _dyingEnemies 须在下面那次 Refresh **之前**:重绘据此保持死怪着色,
            // 否则回放一开始它们就是灰的,死亡节拍再置灰一次毫无表现。
            // 收尾一次性交给 OnAnimDone 清掉——留着会撞上下一场同下标的新敌人,把它画成灰。
            var allDeaths = new System.Collections.Generic.List<int>();
            var drawnIndices = new System.Collections.Generic.List<int>(); // 开场那一拍的掉字卡位
            foreach (var step in steps)
                foreach (var e in step.Events)
                    if (e.Kind == BattleEventKind.EnemyDied) allDeaths.Add(e.TargetIndex);
            _dyingEnemies.UnionWith(allDeaths);

            // 先画一次,再动条 —— 条的 fill/label 引用是 switch 里的 Draw* 建出来的,而上面那个
            // 接入点是 return 掉整个 switch 的(首战更是一次都没画过),不补这次重绘
            // FillActionBars 全打在空引用上(SetActionBar 静默跳过),回放什么都看不见。
            // SnapshotPreHp 也必须在这次重绘之前:Animating 期间血条画的是 _anim*Hp,
            // 首战它是默认 0(玩家血条整段回放画成 0/50),第二场起是上一场的陈旧值。
            SnapshotPreHp();
            Refresh();
            // Draw* 建条时读的是开场**结束后**的计量器,按回全 0 只是近似起点——携带满格
            // 召唤物时不准(它当前值就是 Threshold),但该拍会在下面播放前单独按满,见下。
            PaintActionBars(pre);

            foreach (var step in steps)
            {
                var post = (player: step.PlayerMeter,
                    summons: ToArray(step.SummonMeters), enemies: ToArray(step.EnemyMeters));
                // Ticks == 0 是「这一拍推进前已经满格」的实证(TurnScheduler.Advance 的
                // FirstFull 分支的充要条件)——携带满格召唤物开局时,它的第一拍就是这个情形。
                // FillActionBars 遇 ticks <= 0 会直接 yield break、不画任何东西,于是玩家会
                // 看到「空条的召唤物挥了一刀」(spec §5.7 的反例,2026-08-18 修 I3)。这里先把
                // 该行动者的条按到 Threshold,再走正常的播动作 → 回落(DropActingBar)。
                if (step.Ticks <= 0)
                {
                    switch (step.Actor.Kind)
                    {
                        case ActorKind.Player:
                            SetActionBar(_playerActionBar, TurnScheduler.Threshold);
                            break;
                        case ActorKind.Summon:
                            if (_summonActionBarByCore.TryGetValue(step.Actor.Index, out var fullSummonBar))
                                SetActionBar(fullSummonBar, TurnScheduler.Threshold);
                            break;
                        case ActorKind.Enemy:
                            if (step.Actor.Index < _enemyActionBars.Count)
                                SetActionBar(_enemyActionBars[step.Actor.Index], TurnScheduler.Threshold);
                            break;
                    }
                }
                yield return FillActionBars(pre, post, step.Actor, step.Ticks);
                // 与 AdvanceRoutine 同一条守卫:每批必以 ActorActed 开头(段首标记),
                // 只有标记时不该白播一整段动画 + 停顿。末拍是玩家自己那一拍,其 Events 是
                // BeginPlayerTurn/StartTurn 那一批(发牌/AP/玩家灼烧),照常播。
                // 摘 CharDrawn 必须在下面那条守卫**之前**:末拍(玩家自己)常常只有段首标记
                // + 掉字两样,不摘就会白播一整段 Juice 动画 + 0.12s 停顿,而抽卡另有动画。
                var events = SplitOutDrawn(step.Events, drawnIndices);
                if (events.Any(e => e.Kind != BattleEventKind.ActorActed))
                {
                    bool done = false;
                    _juice.Play(events, EnemyAnchor, SummonAnchor, () => done = true, OnImpact,
                        enemyElement: EnemyElement);
                    while (!done) yield return null;
                    yield return _juice.Wait(0.12f);
                }
                DropActingBar(step.Actor, post);
                pre = post;
            }

            Refresh();
            OnAnimDone(allDeaths); // 解锁输入 + 清死亡着色 + 归零后重绘(Battle 已 Won 时才出结算)
            yield return DealRoutine(drawnIndices); // 首回合的发牌同样要飞一遍(顺序同 AdvanceRoutine)
        }

        /// <summary>把 <see cref="BattleEventKind.CharDrawn"/> 从事件批里摘出来,落位下标记进
        /// <paramref name="drawnIndices"/>,其余原序返回交给 Juice。
        ///
        /// 摘掉而不是让 Juice 忽略它:轮回玩家那一拍的事件批常常**只有**段首标记 + 掉字两样,
        /// 不摘就会通过「这批还有别的事件」那条守卫,白播一整段 Juice 动画 + 0.12s 停顿 ——
        /// 而抽卡的表现是 <see cref="DealRoutine"/> 那条独立动画,不在 Juice 里。</summary>
        private static System.Collections.Generic.List<BattleEvent> SplitOutDrawn(
            System.Collections.Generic.IReadOnlyList<BattleEvent> source,
            System.Collections.Generic.List<int> drawnIndices)
        {
            var rest = new System.Collections.Generic.List<BattleEvent>(source.Count);
            foreach (var e in source)
            {
                if (e.Kind == BattleEventKind.CharDrawn) drawnIndices.Add(e.Amount);
                else rest.Add(e);
            }
            return rest;
        }

        /// <summary>回合开始的抽卡动画(2026-08-27 用户拍板):新掉的字从字库行**右侧**逐张滑入
        /// 到自己的卡位,错峰落位并弹跳一下。
        ///
        /// 从右边进而不是从左边:掉字一律 append 到字库末尾,新牌永远在最右 —— 从左端飞过来
        /// 是横穿整行、逆着落点方向跑(2026-08-27 首版就是那样,用户实机看出来的)。
        ///
        /// ⚠ **只能在 <see cref="OnAnimDone"/> 之后调用**(2026-08-27 修:此前放在它之前,动画
        /// 一次都没播过)。Refresh 的玩家回合分支遇 Animating 会在 DrawLibrary() 之前就 break ——
        /// 整个动画锁期间字库一张牌都不画,而 Refresh 开头的 Ui.Clear(_libraryRow) 已经把旧牌
        /// 销毁了。所以锁里调用本协程,_libraryTileRects 恒为空,下面那两条早退会让它静默空跑。
        /// 真正把新牌建出来的是 OnAnimDone 归零后那一次 Refresh。
        ///
        /// 时序上有两个坑,顺序不能动:
        ///   ① **先把新牌 localScale 按 0,再等一帧**。调用方(OnAnimDone)那次 Refresh 已经把
        ///      新牌按最终样子建出来了 —— 不先藏,玩家会先看到牌凭空出现、再看它飞一遍。
        ///      按 0 这一步在那次 Refresh 之后、本协程首次 yield 之前,同一帧内完成,不会渲染。
        ///   ② **position 只能在等过一帧之后读**。Unity 的 UI 布局不是建对象时算的,刚 Refresh
        ///      建出来的 RectTransform 此刻 position 还是父级中心,直接拿去当飞行终点会让所有牌
        ///      都飞到字库行正中央。localScale = 0 不影响 HorizontalLayoutGroup 的排布
        ///      (它只看 LayoutElement.preferredWidth),所以「藏着等一帧」两个目的能同时达到。
        ///      ExecuteCast 那边的飞牌起点用的是相反的招 —— 在重绘**销毁**旧牌之前抢先读,
        ///      两处解决的是同一个「布局晚一帧」的问题。
        ///
        /// 本协程**自己重新持锁**(BeginAnim 是计数器,可重入):调用方已经 OnAnimDone 解锁过了,
        /// 不重锁的话玩家能在等布局那一帧里点字牌出字 —— 那会 Refresh 掉正在飞的 tile。
        /// 两条早退都在 BeginAnim 之前,锁不会失配。
        ///
        /// 卡位越界/为空一律跳过:满库那次 Core 不发 CharDrawn(字进的是 PendingDrop),
        /// 但战斗当拍已 Won 时 Refresh 走的是不画字库的分支,那时 _libraryTileRects 是空的。</summary>
        // 抽卡滑入的三个手感参数(2026-08-27 用户拍板「从右边滑入、再慢一点」)。
        // 时长是出字那记飞牌(0.22s)的两倍:出字是「砸」,抽卡是「滑」,快了就看不清是从哪来的。
        private const float DealFlyDuration = 0.45f;
        // 入口离最右那张牌多远。约两张牌宽(牌 68 + 间距 8 = 76),够看出「从外面进来」,
        // 又不至于伸进右侧拆合台(左缘 1272)的地盘太深。
        private const float DealSlideIn = 152f;
        // 多张之间的错峰。单张时用不到 —— DropsPerTurn 缺省是 1,养成抬高后才会有多张。
        private const float DealStagger = 0.12f;

        private System.Collections.IEnumerator DealRoutine(
            System.Collections.Generic.IReadOnlyList<int> libraryIndices)
        {
            if (libraryIndices.Count == 0) yield break;

            var tiles = new System.Collections.Generic.List<RectTransform>(libraryIndices.Count);
            var glyphs = new System.Collections.Generic.List<string>(libraryIndices.Count);
            foreach (int index in libraryIndices)
            {
                if (index < 0 || index >= _libraryTileRects.Count || index >= Battle.Library.Count) continue;
                var rect = _libraryTileRects[index];
                if (rect == null) continue;
                rect.localScale = Vector3.zero; // 坑 ①:先藏,同一帧内,不会渲染出来
                tiles.Add(rect);
                glyphs.Add(Battle.Library[index]);
            }
            if (tiles.Count == 0) yield break;

            BeginAnim(); // 早退在此之前,锁不会失配;配对的 OnAnimDone 在本协程末尾
            yield return null; // 坑 ②:等布局跑完,下面读到的 position 才是牌的真实落点

            // 入口在**最右那张新牌的右侧**(2026-08-27 用户拍板):掉字一律 append 到字库末尾,
            // 新牌永远落在最右 —— 从左端的计数标签飞过来是横穿整行、逆着落点方向跑(改前就是
            // 那样)。所有新牌共用同一个入口 x,依次滑到自己的位置,像从右边推进来。
            //
            // 从后往前找第一张还活着的(跨帧持有的 RectTransform 一律判空,见 Juice.PopTile)。
            // ⚠ 找不到也**不能**早退:BeginAnim 已经在上面调过了,早退会让锁失配、输入永久锁死。
            // 那种情形交给下面循环里的 `if (rect == null) { pending--; continue; }` —— pending
            // 归零、while 立刻过、末尾的 OnAnimDone 照常配对。
            float entryX = 0f;
            for (int i = tiles.Count - 1; i >= 0; i--)
                if (tiles[i] != null) { entryX = tiles[i].position.x + DealSlideIn; break; }

            int pending = tiles.Count;
            for (int i = 0; i < tiles.Count; i++)
            {
                var rect = tiles[i];
                if (rect == null) { pending--; continue; }
                var def = _graph.Get(glyphs[i]);
                var target = rect.position;
                _juice.FlyGlyph(glyphs[i], Theme.ElementColor(def.Element),
                    new Vector3(entryX, target.y, target.z), target, () =>
                    {
                        if (rect != null)
                        {
                            rect.localScale = Vector3.one; // 滑到才现身
                            _juice.PopTile(rect);
                        }
                        pending--;
                    }, DealFlyDuration, easeOut: true);
                if (i + 1 < tiles.Count)
                    yield return new WaitForSecondsRealtime(DealStagger); // 多张错峰,不糊成一团
            }
            while (pending > 0) yield return null;
            OnAnimDone(NoDeaths); // 与上面那次 BeginAnim 配对:解锁输入 + 归零后重绘(恢复牌的 scale)
        }

        /// <summary>给不牵扯死亡的 <see cref="OnAnimDone"/> 调用用的空表(它只拿来做 ExceptWith)。</summary>
        private static readonly System.Collections.Generic.List<int> NoDeaths = new();

        /// <summary>把全场行动条一次性按到 meters(不插值)。开场回放起手用。</summary>
        private void PaintActionBars((int player, int[] summons, int[] enemies) meters)
        {
            SetActionBar(_playerActionBar, meters.player);
            for (int i = 0; i < meters.summons.Length; i++)
                if (_summonActionBarByCore.TryGetValue(i, out var bar))
                    SetActionBar(bar, meters.summons[i]);
            for (int i = 0; i < meters.enemies.Length && i < _enemyActionBars.Count; i++)
                SetActionBar(_enemyActionBars[i], meters.enemies[i]);
        }

        /// <summary>IReadOnlyList&lt;int&gt; → int[],供 FillActionBars 的元组用。</summary>
        private static int[] ToArray(System.Collections.Generic.IReadOnlyList<int> source)
        {
            var result = new int[source.Count];
            for (int i = 0; i < result.Length; i++) result[i] = source[i];
            return result;
        }

        private void CancelSelection()
        {
            _selectedChar = null;
            _selectedIndex = -1;
            _targeting = false;
            _allyTargeting = false;
            _directionPicking = false;
            _pendingAttackMode = false;
            _pendingAllyEnemyTarget = -1;
            ResetSlotPicking(); // 连选途中取消 = 整张字回滚:没调 Cast,AP 与字库一滴未动
            Refresh();
        }

        /// <summary>成语 Boss 显示当前阶段字:排【山】倒海;普通怪显示名字。</summary>
        private static string BossTitle(EnemyState enemy)
        {
            if (!enemy.IsBoss)
                return enemy.Def.Id;
            var title = new StringBuilder();
            for (int i = 0; i < enemy.Def.Phases.Count; i++)
                title.Append(i == enemy.PhaseIndex ? $"【{enemy.Def.Phases[i].Char}】" : enemy.Def.Phases[i].Char);
            return title.ToString();
        }

        /// <summary>模态弹窗(提示统一弹窗,2026-07-19 拍板);同屏仅一个。</summary>
        private void ShowModal(string title, string body,
            params (string label, System.Action onClick, Color bg, Color fg)[] buttons)
        {
            if (_modal != null) Object.Destroy(_modal);
            _modal = Ui.Modal(transform, title, body, buttons);
        }

        /// <summary>单按钮告知弹窗:资源被拒类(库满/池满/额度用尽/付不起)统一走这里。</summary>
        private void ShowAlert(string title, string body)
        {
            if (_modal != null) Object.Destroy(_modal);
            _modal = Ui.Alert(transform, title, body);
        }

        /// <summary>被拒操作弹窗:AP 不够附「结束回合」快捷钮;拆合失败给原因。
        /// 误点类(点尸体/不可出)保持消息条,不打断。</summary>
        private void MaybeModalError(BattleError error, string charId, int neededAp)
        {
            if (error == BattleError.NotEnoughAp)
                ShowModal(Strings.T("battle.dialog.not_enough_ap.title"),
                    Strings.T("battle.dialog.not_enough_ap.body",
                        ("charId", charId), ("neededAp", neededAp), ("ap", Battle.Ap), ("apPerTurn", Battle.ApPerTurn)),
                    (Strings.T("battle.btn.end_turn"), OnEndTurn, Theme.Cinnabar, Color.white),
                    (Strings.T("common.reconsider"), null, Theme.LockedBg, Theme.TextMain));
            else if (error == BattleError.ForgeFailed)
                ShowModal(Strings.T("battle.common.rejected"), Describe(error),
                    (Strings.T("common.ok"), null, Theme.LockedBg, Theme.TextMain));
        }

        private string Describe(BattleError error) => error switch
        {
            BattleError.NotEnoughAp => Strings.T("battle.error.not_enough_ap"),
            BattleError.NotCastable => Strings.T("battle.error.not_castable"),
            BattleError.InvalidTarget => Strings.T("battle.error.invalid_target"),
            BattleError.BattleOver => Strings.T("battle.error.battle_over"),
            BattleError.ForgeFailed => Battle.LastForgeError switch
            {
                ForgeError.PoolWouldOverflow => Strings.T("battle.error.pool_overflow"),
                ForgeError.MissingIngredients => Strings.T("battle.error.missing_ingredients"),
                ForgeError.LibraryFull => Strings.T("battle.error.library_full"),
                ForgeError.NotUnlocked => Strings.T("battle.error.not_unlocked"),
                ForgeError.NotDismantlable => Strings.T("battle.error.not_dismantlable"),
                _ => Strings.T("battle.common.rejected"),
            },
            _ => "",
        };

    }
}
