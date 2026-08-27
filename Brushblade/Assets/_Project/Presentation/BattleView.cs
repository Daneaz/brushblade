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
        private RecipeGraph _graph;
        private RunEngine _run;
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
        private Transform _suggestRow;
        private Transform _hintColumn;   // 差字面板(屏幕左侧竖排,五行三级目录)
        private Transform _actionRow;
        // 非战斗阶段的宽操作区(2026-08-20):结算 / 奇遇 / 部件超限 / 跑图结束用它。
        // 这些界面此前借的是拆合台的 _actionRow,而拆合台已经搬进 218px 的右侧竖栏
        // —— 奇遇的 260 宽选项钮塞不进去,所以给它们留一条横贯屏幕的带。
        // 它与 _libraryRow 在 y 上有重叠,但两者从不在同一阶段绘制(见 Refresh 的 switch)。
        private Transform _centerRow;
        private GameObject _rowDivider;  // 敌我前排之间的墨线:只在战斗阶段现身(2026-08-20)
        private Text _messageLabel;
        private string _hintBucket;      // 差字目录一级选中的五行桶(金木水火土心/中性;null = 收起)
        private string _hintCharFocus;   // 三级目录二级选中的字(null = 未选)

        private Tutorial _tutorial;      // 新手引导(11.2);null = 不引导

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

        public void Init(RecipeGraph graph, RunEngine run, System.Action<bool> onRunEnded,
            Tutorial tutorial = null, string title = null, int playerMaxHp = 50,
            System.Action onNewFloor = null, System.Action onExit = null, System.Action onProgress = null,
            System.Action onExpanded = null, System.Action onAbandon = null,
            System.Action onFloorCleared = null)
        {
            _graph = graph;
            _run = run;
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
            _juice.Play(events, EnemyAnchor, SummonAnchor, () => OnAnimDone(deaths), OnImpact, SummonAt);
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
                        _animPlayerHp = System.Math.Max(Battle.PlayerHp, _animPlayerHp - e.Amount);
                        SetHpBar(_playerHpBar, _animPlayerHp, PlayerMaxHp);
                        break;
                    }
                    // 挨这一记的形象抖起来:主体抖、墨丝甩尾、眼睛瞪大(MobView 三层各自不同步)
                    if (e.TargetIndex < _enemyMobs.Count && _enemyMobs[e.TargetIndex] != null)
                        _enemyMobs[e.TargetIndex].PlayHit();
                    PushEnemyHp(e.TargetIndex, -e.Amount);
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
                    _animPlayerHp = System.Math.Max(Battle.PlayerHp, _animPlayerHp - (e.Amount - e.Absorbed));
                    SetHpBar(_playerHpBar, _animPlayerHp, PlayerMaxHp);
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
                    _summonAnimHp[si] = System.Math.Max(Battle.Summons[si].Hp,
                        _summonAnimHp[si] - (e.Amount - e.Absorbed));
                    SetHpBar(sbar, _summonAnimHp[si], Battle.Summons[si].MaxHp);
                    break;
                case BattleEventKind.SummonBurnTick: // 召唤物自身灼烧(2026-08-26):TargetIndex 就是槽位
                    int bsi = e.TargetIndex;
                    if (bsi < 0 || bsi >= Battle.Summons.Count || Battle.Summons[bsi] == null
                        || !_summonAnimHp.ContainsKey(bsi)
                        || !_summonBarByCore.TryGetValue(bsi, out var bbar) || bbar.fill == null) break;
                    _summonAnimHp[bsi] = System.Math.Max(Battle.Summons[bsi].Hp, _summonAnimHp[bsi] - e.Amount);
                    SetHpBar(bbar, _summonAnimHp[bsi], Battle.Summons[bsi].MaxHp);
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
            _animEnemyHp[index] = Mathf.Clamp(hp, 0, enemy.MaxHp);
            SetHpBar(_enemyHpBars[index], _animEnemyHp[index], enemy.MaxHp);
            return true;
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
        /// 填充色用赭金 —— 血条是朱砂、护盾条是翡翠,三者必须一眼分得开。
        /// 与 <see cref="HpBar"/> 同款返回 fill/label,供动画期间就地推进。</summary>
        private (RectTransform fill, UnityEngine.UI.Text label) ActionBar(
            Transform parent, int meter, Vector2 size, int fontSize)
        {
            float frac = Mathf.Clamp01(meter / (float)TurnScheduler.Threshold);
            var bar = Ui.Bar(parent, frac, Theme.Gold, size);
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

        /// <summary>护盾条(2026-08-26):玩家与召唤物共用。**常驻** —— 值为 0 时是一条空条,
        /// 不再「有盾才画」:那样一涨一消整块布局跟着跳一下。
        ///
        /// 文字去掉了「护盾」二字,换成盾牌图标 + 纯数字(<see cref="Icons"/> 的 "shield" —— 描边盾,
        /// 与 "defense" 那个实心盾刻意分开:一个是会被打空的临时血,一个是常驻减伤点数)。
        /// 与 <see cref="HpBar"/> / <see cref="ActionBar"/> 同款返回 fill/label,供动画期间就地推进。</summary>
        private (RectTransform fill, UnityEngine.UI.Text label) ShieldBar(Transform parent, int shield, Vector2 size)
        {
            var bar = Ui.Bar(parent, Mathf.Clamp01(shield / ShieldBarFull), Theme.Jade, size);
            var fill = (RectTransform)bar.transform.Find("Fill");

            // 图标压在条的最左端,数字仍居中 —— 与 Ui.Chip 的「图标在左、文字在右」同一套摆法
            float iconSpan = Mathf.Min(Icons.Size, size.y + 4f);
            var sprite = Icons.Get("shield");
            if (sprite != null)
            {
                var iconGo = Ui.Panel(bar.transform, "ShieldIcon");
                var iconImage = iconGo.AddComponent<Image>();
                iconImage.sprite = sprite;
                iconImage.color = Color.white;
                iconImage.preserveAspect = true;
                Ui.Anchor((RectTransform)iconGo.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(2f, -iconSpan / 2f), new Vector2(2f + iconSpan, iconSpan / 2f));
            }
            else
            {
                // 兜底汉字(资产缺失时):占同样的宽,布局与有图时一致
                var glyph = Ui.ThemedLabel(bar.transform, Icons.Fallback("shield"),
                    Mathf.Clamp((int)(size.y * 0.9f), 9, 13), Color.white, Theme.TitleFont);
                Ui.Anchor(glyph.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(2f, -iconSpan / 2f), new Vector2(2f + iconSpan, iconSpan / 2f));
            }

            var label = Ui.ThemedLabel(bar.transform, shield.ToString(),
                Mathf.Clamp((int)(size.y * 0.75f), 9, 13), Color.white, Theme.TitleFont);
            Ui.Stretch(label.rectTransform);
            var outline = label.gameObject.AddComponent<Outline>(); // 与血条同款描边,保对比度
            outline.effectColor = Theme.Ink;
            outline.effectDistance = new Vector2(1.2f, 1.2f);
            return (fill, label);
        }

        private static void SetShieldBarOn((RectTransform fill, UnityEngine.UI.Text label) bar, int shield)
        {
            if (bar.fill != null)
                Ui.Anchor(bar.fill, Vector2.zero, new Vector2(Mathf.Clamp01(shield / ShieldBarFull), 1),
                    Vector2.zero, Vector2.zero);
            if (bar.label != null) bar.label.text = shield.ToString();
        }

        /// <summary>召唤格顶行的右翼(2026-08-26):被动 + 身上挂着的状态,竖着摞小 chip。
        /// 与玩家状态行同一套 <see cref="Ui.Chip"/> + <see cref="Icons"/>,只是内边距压到最小 ——
        /// 这一翼只有 <see cref="SummonSideWidth"/> 宽。
        ///
        /// 被动是常驻标签(朱砂),状态是会消的减益(各自的图标)。两者摞在一起而不是分两处:
        /// 玩家读这一格时问的是「这只现在什么情况」,不是「哪些来自被动」。</summary>
        private void DrawSummonStatusColumn(Transform head, SummonState summon, float glyphSize)
        {
            var column = Ui.VStack(head, "Status", 2);
            var element = column.AddComponent<LayoutElement>();
            element.preferredWidth = SummonSideWidth;
            element.preferredHeight = glyphSize;

            string passiveTag = SummonPassiveTag(summon.Passive);
            if (passiveTag.Length > 0)
                Ui.Chip(column.transform, passiveTag, Theme.Cinnabar, Color.white,
                    SummonChipFontSize, SummonChipPadX, SummonChipPadY);

            int burn = summon.Statuses.TotalMagnitude(StatusKind.Burn);
            if (burn > 0)
                Ui.Chip(column.transform, $"{burn}", Theme.Cinnabar, Color.white,
                    SummonChipFontSize, SummonChipPadX, SummonChipPadY, "burn");
        }

        // 右翼 chip 的字号与内边距。定这么小是被 SummonSideWidth = 58 逼出来的,不是随手填的:
        // 按 Ui.ChipWidth 的口径(text.Length × fontSize + padX),最长的被动标签是
        // 「反伤100%」7 字 → 7 × 8 + 2 = 58,**恰好**贴着 58 不溢出。
        // ⚠ 加更长的被动文案、或把这三个数调大之前,先拿 Ui.ChipWidth 重算一遍 ——
        // 溢出的 chip 会横着压到中间的字块上(Ui.PackChips 明说它不截断,由调用方保证宽度)。
        // 完整文案在点召唤物弹出的详情里(SummonInfo),这一翼只是速读。
        private const int SummonChipFontSize = 8;
        private const int SummonChipPadX = 2;
        private const int SummonChipPadY = 2;

        private static void SetHpBar((RectTransform fill, UnityEngine.UI.Text label) bar, int hp, int maxHp)
        {
            if (bar.fill != null)
                Ui.Anchor(bar.fill, Vector2.zero, new Vector2(Mathf.Clamp01(hp / (float)maxHp), 1), Vector2.zero, Vector2.zero);
            if (bar.label != null) bar.label.text = $"{hp}/{maxHp}";
        }

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

            // 顶栏一行三段(2026-08-21 用户拍板):关卡名·层数·场次(左) | 战斗提示(中) | 墨锭·回合·退出(右)。
            // 提示此前独占下面一条带(0.900–0.945,40.5px);并进顶栏后那 40.5px 归中区,
            // 而标题与墨锭都回到原来的位置没动 —— 高度是白赚的,布局没有任何一样东西被挪走。
            var topBar = Ui.Panel(transform, "TopBar");
            Ui.Anchor((RectTransform)topBar.transform, new Vector2(0.02f, 0.94f), new Vector2(0.98f, 1f), Vector2.zero, Vector2.zero);
            _topLeft = Ui.Row(topBar.transform, "Left", 10).transform;
            Ui.Anchor((RectTransform)_topLeft, new Vector2(0, 0), new Vector2(0.26f, 1), Vector2.zero, Vector2.zero);
            var messageGo = Ui.Panel(topBar.transform, "Message");
            Ui.Anchor((RectTransform)messageGo.transform, new Vector2(0.26f, 0), new Vector2(0.70f, 1), Vector2.zero, Vector2.zero);
            _messageLabel = Ui.ThemedLabel(messageGo.transform, "", 19, Theme.TextDim);
            Ui.Stretch(_messageLabel.rectTransform);
            _topRight = Ui.Row(topBar.transform, "Right", 14).transform;
            Ui.Anchor((RectTransform)_topRight, new Vector2(0.70f, 0), new Vector2(1, 1), Vector2.zero, Vector2.zero);

            // 五行速查常驻(2026-07-22;2026-07-29 改为直接摆环图,不再点开弹窗):
            // 挂在消息行两端(那里通常是空白),向下延展 —— 敌人行居中排布,够不到这两角
            // 纵向 0.158×900 ≈ 142px = 标题 20 + 间距 2 + 环图 120
            var keGo = WuxingChart.Mount(transform, sheng: false);
            Ui.Anchor((RectTransform)keGo.transform,
                new Vector2(0.004f, 0.780f), new Vector2(0.086f, 0.938f), Vector2.zero, Vector2.zero);

            var shengGo = WuxingChart.Mount(transform, sheng: true);
            Ui.Anchor((RectTransform)shengGo.transform,
                new Vector2(0.914f, 0.780f), new Vector2(0.996f, 0.938f), Vector2.zero, Vector2.zero);

            // ================= 纵向预算(2026-08-21 竖排复原) =================
            // 900 基准高(CanvasScaler 1600×900 按高匹配)。中区 = 四排 + 分隔线的全部预算。
            //
            // 2026-08-20 那版是 0.340–0.900 = 504px,只够横排格(形象在左、信息在右)。
            // 本次把敌人格改回竖排(形象在上、条在正下方),格高从 140/119 涨到 171/150,
            // 两排多要 62px。钱是这么来的:
            //   ① 提示行从顶部 0.900–0.945 搬到左下 → 上缘 0.900 抬到 0.938   … +34.2px
            //   ② 字牌去费用带后 84×105 → 68×85,字库区 123 → 91.8            … +31.5px
            // 中区因此 0.305–0.938 = 0.633 → **569.7px**,形象一分没缩(仍 138 / 117)。
            //
            // 逐项加法(区域给了多少 / 内容最坏多少 / 余量),自上而下:
            //   顶部留白 0.930–0.938                        …  7.2px   (与顶栏脱开)
            //   敌方后排 0.760–0.930  = 0.170 → 153.0px  内容 150px  余 3.0px
            //     └ 格高 = 形象 117 + 2 + 条区 31
            //   排间留白 0.753–0.760                        …  6.3px
            //   敌方前排 0.560–0.753  = 0.193 → 173.7px  内容 171px  余 2.7px
            //     └ 格高 = 形象 138 + 2 + 条区 31(血条 16 + 3 + 行动条 12)
            //     └ 名字与 chip **叠在形象上**,不进这笔加法;竖着排要再吃 22 + 41 = 63px/格
            //   分隔带 0.543–0.560                          … 15.3px
            //     └ 留白 6.3 + 分隔线 1.8(0.550–0.552) + 留白 7.2;两侧前排贴着它
            //   我方前排 0.431–0.543  = 0.112 → 100.8px  内容  98px  余 2.8px
            //     └ 字块 56 + 2 + 血条 13 + 2 + 行动条 9 + 2 + 属性行 14(攻/盾/被动同排)
            //   排间留白 0.425–0.431                        …  5.4px
            //   我方后排 0.321–0.425  = 0.104 →  93.6px  内容  88px  余 5.6px
            //     └ 字块 48 + 2 + 血条 12 + 2 + 行动条 8 + 2 + 属性行 14
            //   收尾留白 0.305–0.321                        … 14.4px
            //   ——————————————————————————————————————————————
            //   区域 153.0 + 173.7 + 100.8 + 93.6 = 521.1px
            //   留白  7.2 + 6.3 + 15.3 + 5.4 + 14.4 =  48.6px
            //   合计 569.7px = 0.305–0.938 ✓;四排内容 507px,四区余量合计 14.1px,**闭合**。
            //
            // **改动任何一格的内容高度时请重算上面这串加法**,逐格的加法在
            // EnemyCellHeightFront / SummonCellHeightFront 那两处常量旁。
            _enemyBackRow = MakeSection("EnemiesBack", 0.760f, 0.930f);   // 153.0px
            _enemyFrontRow = MakeSection("EnemiesFront", 0.560f, 0.753f); // 173.7px
            _summonFrontRow = MakeSection("SummonsFront", 0.431f, 0.543f); // 100.8px
            _summonBackRow = MakeSection("SummonsBack", 0.321f, 0.425f);   // 93.6px

            // 敌我前排之间的分隔线:两侧「前排」贴着它,越远离它的排越靠后。
            // raycastTarget = false —— 它只是一条线,不能拦掉空白点击(那是取消选中用的)
            _rowDivider = Ui.Panel(transform, "RowDivider");
            var dividerImage = _rowDivider.AddComponent<Image>();
            dividerImage.color = new Color(Theme.InkSoft.r, Theme.InkSoft.g, Theme.InkSoft.b, 0.35f);
            dividerImage.raycastTarget = false;
            Ui.Anchor((RectTransform)_rowDivider.transform,
                new Vector2(0.16f, 0.550f), new Vector2(0.86f, 0.552f), Vector2.zero, Vector2.zero);

            // 74px(2026-08-13 从 50px 抬高)。2026-08-17:护盾数值并进条上叠字(省 17px)、
            // 但护盾条要从 7 抬到 14 才放得下叠字(还回去 7px),净省 10px;新增行动条吃 12px。
            // 再把状态 chip 内边距收到敌人格同档、血条 20→18、行动条 14→12 省下 8px 之后,
            // 内容最坏 73px(20-2 血条 + 14-2 行动条 + 14 护盾条 + 24-4 状态行 + 9 间距),
            // 区域 73.8px —— 逐项可复算,余 0.8px。**改动内容高度时请重算这串加法。**
            // 2026-08-20:高度一分未动,只是整体下移 0.220。2026-08-21:再下移 0.035(31.5px),
            // 接手字牌缩小让出的那一段;高度仍是 73.8px,一分未动。
            _bottomRow = MakeSection("PlayerStats", 0.223f, 0.305f); // 73.8px

            // 拆合台薄宣纸卡(半透,融层段染色):2026-08-20 从底部横卡改为**右侧竖栏**。
            // 2026-08-21 左移加宽:0.862–0.998(宽 218)→ 0.795–0.985(宽 **304**)。
            // 加宽 86px 是为了让配方条不再被 VerticalLayoutGroup 压扁(旧宽度约 9 条到顶)。
            // 左缘 0.795 = 1272px 由字库行卡死:字牌缩到 68 宽后,满员 12 张的行宽
            // 12×68 + 11×8 = 904 居中 → 最右到 x = 1252,留 20px 不压。
            // 上缘 0.775 让开右上角的相生环图(0.780 起);右缘留 0.015 不贴屏幕边。
            var workbenchCard = Ui.CardPanel(transform, "Workbench", Theme.PaperCard, 20);
            Ui.Anchor((RectTransform)workbenchCard.transform, new Vector2(0.795f, 0.100f), new Vector2(0.985f, 0.775f), Vector2.zero, Vector2.zero);
            var workbenchStack = Ui.VStack(workbenchCard.transform, "Stack", 8);
            Ui.Stretch((RectTransform)workbenchStack.transform);
            Ui.ThemedLabel(workbenchStack.transform, Strings.T("battle.label.workbench_title"), 13, Theme.TextDim, Theme.TitleFont);
            _suggestRow = Ui.VStack(workbenchStack.transform, "Content", 6).transform;
            // 动作行**横排**(2026-08-21 用户拍板):出 / 拆 / 弃 三个单字钮一行排完。
            // 2026-08-20 改竖栏时它被一并改成 VStack(栏宽只有 217.6px,「出字/丢弃」那种
            // 长标签横着放不下);现在标签收成单字、栏宽也加到 304px,横排回来:
            //   3 × 76 + 间距 8×2 = 244px ≤ 304 ✓
            _actionRow = Ui.Row(workbenchStack.transform, "Actions", 8).transform;

            // 差字面板:屏幕最左侧,上下居中,五行三级目录
            var hintGo = Ui.VStack(transform, "HintPanel", 4);
            Ui.Anchor((RectTransform)hintGo.transform, new Vector2(0.002f, 0.16f), new Vector2(0.135f, 0.84f), Vector2.zero, Vector2.zero);
            hintGo.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
            _hintColumn = hintGo.transform;

            // 下面三区在 2026-08-13 整体下移 0.027(24px),给 PlayerStats 让位(见上);
            // 2026-08-20 又整体下移 0.220(198px),接手拆合台让出的底部。
            // 2026-08-21:字库区**第一次减高** —— 字牌去掉费用带后 105 → 85,区高 123 → 91.8,
            // 省下的 31.2px 全部交给中区(见上面那串加法的第 ② 笔)。部件钮区高度仍未动。
            // ⚠ 字库行是唯一**横向也要限界**的一区:它的内容宽随字库上限增长,而左右两侧
            // 都有常驻面板。满员 12 张(基础 7 + 博闻满级 3 + 局内广告 2)时内容宽
            // ≈ 标签 90 + 广告位 130 + 12×68 + 间距 104 = 1140px;若照别的区那样通栏居中,
            // 会铺到 x 230–1370,左压配字表(右缘 216)、右压拆合台(左缘 1272)。
            // 所以这里不用 MakeSection,改成夹在两者之间的 0.135–0.790(1048px):
            // 10 张(基础 7 + 博闻满级 3)= 988px 仍然宽松;满 12 张时 HorizontalLayoutGroup
            // 会把每格等比压到约 92%(牌 68 → 62),**压扁而不是叠上去** —— 两害相权取其轻。
            var libraryGo = Ui.Row(transform, "Library");
            Ui.Anchor((RectTransform)libraryGo.transform,
                new Vector2(0.135f, 0.121f), new Vector2(0.790f, 0.223f), Vector2.zero, Vector2.zero);
            _libraryRow = libraryGo.transform;                    // 91.8px ≥ 85 字牌
            _poolRow = MakeSection("Pool", 0.053f, 0.121f);       // 61px ≥ 56 部件钮(高度不变)
            // 39px:只装单行标签(字号 18~26,26 号行高约 31px)。教程指引 / 「结算中……」。
            // ⚠ 若将来这里要放两行文案,得另找地方要空间,不能再从这里挤。
            _statusRow = MakeSection("Status", 0.010f, 0.053f);

            // 非战斗阶段的宽操作区。上下缘都被同阶段共存的区卡死,别再挪:
            //   下缘 > 0.121 —— 奇遇/部件超限阶段同屏画部件池(0.053–0.121)
            //   上缘 < 0.223 —— 战斗结算阶段同屏画玩家条(0.223–0.305)
            // 与字库行(0.121–0.223)几乎完全重叠是**有意的**:字库只在战斗回合内/战利品/
            // 复活补给三个阶段画,和这条带的四个消费方(结算/奇遇/部件超限/跑图结束)互斥。
            // 2026-08-21:上缘随 PlayerStats 从 0.255 收到 0.220(区高 117 → 85.5px)——
            // 最高的一件是奇遇选项钮 260×72,85.5px 仍装得下,余 13.5px。
            _centerRow = MakeSection("Center", 0.125f, 0.220f);  // 85.5px

            // 结束回合钮:2026-08-20 从屏幕右缘中部移到拆合台竖栏正下方 —— 仍是右手拇指位,
            // 且与拆合台同栏对齐。2026-08-21 随拆合台一起左移,保持同栏。
            var endTurnGo = Ui.Row(transform, "EndTurn");
            Ui.Anchor((RectTransform)endTurnGo.transform,
                new Vector2(0.795f, 0.020f), new Vector2(0.985f, 0.092f), Vector2.zero, Vector2.zero);
            _endTurnRow = endTurnGo.transform;
        }

        private Transform MakeSection(string name, float yMin, float yMax)
        {
            var go = Ui.Row(transform, name);
            Ui.Anchor((RectTransform)go.transform, new Vector2(0, yMin), new Vector2(1, yMax), Vector2.zero, Vector2.zero);
            return go.transform;
        }

        // ---- 渲染 ----

        // 字牌位置登记(charId→当前 RectTransform):过渡动效的起终点;每次重绘重新登记
        private readonly System.Collections.Generic.Dictionary<string, RectTransform> _tileRects = new();
        // 字库卡位→牌面(2026-08-17):同字多张时 _tileRects 按 charId 只留最后一张,飞字起点改按位取
        private readonly System.Collections.Generic.List<RectTransform> _libraryTileRects = new();

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
            Ui.Clear(_topLeft);
            Ui.Clear(_topRight);
            Ui.Clear(_enemyBackRow);
            Ui.Clear(_enemyFrontRow);
            Ui.Clear(_suggestRow);
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

            // 分隔线是 transform 的直接子节点、不参与上面的 Ui.Clear(它不该每帧重建),
            // 所以要在这里显式收起:战利品/复活/奇遇/部件超限/跑图结束这些阶段四排全空,
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
                    if (Animating) // 召唤/敌方行动中:锁出字,只留退出口(DrawTopBar 已画),待动画完成放行
                    {
                        Ui.ThemedLabel(_statusRow, Strings.T("battle.phase.resolving"), 20, Theme.TextDim, Theme.TitleFont);
                        break;
                    }
                    DrawLibrary();
                    DrawPool();
                    DrawSuggest();
                    DrawActions();
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
            // 长按 preview 置顶:重绘后 preview 须盖在战斗 UI 之上
            if (_modal != null) _modal.transform.SetAsLastSibling();
            _messageLabel.text = _message;
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

        /// <summary>引导横幅:一步一句话(11.2.5),金色置于结束回合行(屏幕中部显眼)。</summary>
        private void DrawTutorialHint()
        {
            if (_tutorial == null || _tutorial.Done) return;
            var hint = Ui.Label(_statusRow, "◆ " + TutorialText(_tutorial.Step), 26);
            hint.color = Theme.GoldBorder;
            hint.transform.SetAsFirstSibling();
        }

        private static string TutorialText(TutorialStep step) => step switch
        {
            TutorialStep.DismantleDemo => Strings.T("battle.hint.tutorial.dismantle_demo"),
            TutorialStep.RecomposeDemo => Strings.T("battle.hint.tutorial.recompose_demo"),
            TutorialStep.CastDemo => Strings.T("battle.hint.tutorial.cast_demo"),
            TutorialStep.PickReward => Strings.T("battle.hint.tutorial.pick_reward"),
            _ => "",
        };

        private void DrawTopBar()
        {
            Ui.ThemedLabel(_topLeft, string.IsNullOrEmpty(_title)
                    ? Strings.T("battle.label.battle_index", ("index", _run.BattleIndex + 1))
                    : Strings.T("battle.label.battle_index_titled", ("title", _title), ("index", _run.BattleIndex + 1)),
                20, Theme.TextMain, Theme.TitleFont, TextAnchor.MiddleLeft);
            Ui.IngotLabel(_topRight, _run.AvailableInk.ToString(), 18);
            Ui.ThemedLabel(_topRight, Strings.T("battle.label.turn", ("turn", Battle.Turn)), 18, Theme.TextDim);
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

        private void DrawPlayerStats()
        {
            var hpStack = Ui.VStack(_bottomRow, "Hp", 3);
            // 血值上条(2026-07-25);动画期间画在出手前值,敌人攻击触达才逐记掉血
            _playerHpBar = HpBar(hpStack.transform, Animating ? _animPlayerHp : Battle.PlayerHp,
                PlayerMaxHp, new Vector2(260, 18));
            // 行动条(2026-08-17):放血条与护盾条之间,与敌人/召唤物同口径读 ActionMeter
            _playerActionBar = ActionBar(hpStack.transform, Battle.PlayerActionMeter, new Vector2(260, 12), 9);
            // 护盾条(2026-07-25):动画期间画出手前值,敌方一记触达才按吸收量降,与血条同步可见。
            // 2026-08-17:数值从条下的独立文字行并进条上叠字(与 HpBar 同款),省 17px 给行动条。
            // 2026-08-26:改为**常驻**(用户拍板)——此前「有盾才画」,一涨一消整块底栏跟着跳一下;
            // 文字也从「护盾 N」换成盾牌图标 + 纯数字,与召唤格那条盾条共用 ShieldBar。
            int shownShield = Animating ? _animShield : Battle.PlayerShield;
            _playerShieldBar = ShieldBar(hpStack.transform, shownShield, new Vector2(260, 14));
            // 玩家侧状态一行小图标(2026-08-06 起为 chip,2026-08-17 改图标)。
            // Row 按需创建(2026-08-06 M8):都为 0 时不留一个空 Row 白吃 VStack 的一份间距。
            GameObject statusRow = null;
            int seal = Battle.PlayerStatuses.TotalMagnitude(StatusKind.Seal);
            if (seal > 0)
            {
                statusRow ??= Ui.Row(hpStack.transform, "PlayerStatus", 6);
                Ui.Chip(statusRow.transform, $"−{seal}AP", Theme.InkSoft, Color.white, 12,
                    ChipPadX, ChipPadY, "seal");
            }
            int playerBurn = Battle.PlayerStatuses.TotalMagnitude(StatusKind.Burn);
            if (playerBurn > 0)
            {
                statusRow ??= Ui.Row(hpStack.transform, "PlayerStatus", 6);
                Ui.Chip(statusRow.transform, $"{playerBurn}", Theme.Cinnabar, Color.white, 12,
                    ChipPadX, ChipPadY, "burn");
            }
            int immunity = Battle.PlayerStatuses.TotalMagnitude(StatusKind.Immunity);
            if (immunity > 0)
            {
                statusRow ??= Ui.Row(hpStack.transform, "PlayerStatus", 6);
                Ui.Chip(statusRow.transform, $"{immunity}", Theme.Jade, Color.white, 12,
                    ChipPadX, ChipPadY, "immunity");
            }
            int reflect = Battle.PlayerStatuses.TotalMagnitude(StatusKind.Reflect);
            if (reflect > 0)
            {
                statusRow ??= Ui.Row(hpStack.transform, "PlayerStatus", 6);
                Ui.Chip(statusRow.transform, $"{reflect}%", Theme.Jade, Color.white, 12,
                    ChipPadX, ChipPadY, "reflect");
            }
            // 攻击增益 / 战意(2026-08-12,剡 / 战 / 戮):两者都只改 EffectiveAttack,
            // 而战斗界面不显示攻击力 —— 不出这一格的话这三个字打出去毫无反馈。
            // ApBoost(利)不出格:下方 AP 格子数直接读 Battle.ApPerTurn,多一格就是它的反馈。
            int attackBuff = Battle.PlayerStatuses.TotalMagnitude(StatusKind.AttackBuff);
            if (attackBuff > 0)
            {
                statusRow ??= Ui.Row(hpStack.transform, "PlayerStatus", 6);
                Ui.Chip(statusRow.transform, $"+{attackBuff}", Theme.Gold, Color.white, 12,
                    ChipPadX, ChipPadY, "attack");
            }
            int morale = Battle.PlayerStatuses.TotalMagnitude(StatusKind.Morale);
            if (morale > 0)
            {
                statusRow ??= Ui.Row(hpStack.transform, "PlayerStatus", 6);
                Ui.Chip(statusRow.transform, $"{morale}", Theme.Gold, Color.white, 12,
                    ChipPadX, ChipPadY, "morale");
            }
            // 暴击率(2026-08-12,锋):读 EffectiveCrit(已钳到 100)而不是状态总量 ——
            // 叠 6 张锋时玩家该看到的是 100 不是 120
            if (Battle.EffectiveCrit > 0)
            {
                statusRow ??= Ui.Row(hpStack.transform, "PlayerStatus", 6);
                Ui.Chip(statusRow.transform, $"{Battle.EffectiveCrit}%", Theme.Gold, Color.white, 12,
                    ChipPadX, ChipPadY, "crit");
            }
            // 穿透(2026-08-12,锐):读状态总量而不是某次结算的有效值 —— 穿透打谁减多少要看
            // 那只怪的甲,玩家该看到的是自己攒了多少
            int pierceBuff = Battle.PlayerStatuses.TotalMagnitude(StatusKind.PierceBuff);
            if (pierceBuff > 0)
            {
                statusRow ??= Ui.Row(hpStack.transform, "PlayerStatus", 6);
                Ui.Chip(statusRow.transform, $"{pierceBuff}", Theme.Gold, Color.white, 12,
                    ChipPadX, ChipPadY, "pierce");
            }
            // 护甲 / 闪避 / 速度(2026-08-17 改口径):只在**有增益**时出,不再常驻。
            //
            // ⚠ 这里推翻了 2026-08-13 的取舍。那时读的是 Effective*(基础 + 增益),理由是
            // 「只显示增益会让 0 级增益时整条消失,玩家看不到自己本来就有 4 点甲」。
            // 现在反过来:角色等级给的基础值白占一行版面,而版面正是这次要省的东西
            // (每单位新增一条行动条)。基础值仍能在养成界面看到,局内只报「我从字上攒到了什么」。
            //
            // 改完之后这三条与 穿透 同口径(读状态总量),四者可以一起理解:
            // 局内 chip = 本场攒到的增量,不是角色面板。
            //
            // speed 取 != 0 而非 > 0:被减速是坏消息,恰恰更该让玩家看见 ——
            // 「只在有增益时出」的意图是「基础值不必常驻」,不是「隐藏负面」。
            int defenseBuff = Battle.PlayerStatuses.TotalMagnitude(StatusKind.DefenseBuff);
            if (defenseBuff > 0)
            {
                statusRow ??= Ui.Row(hpStack.transform, "PlayerStatus", 6);
                Ui.Chip(statusRow.transform, $"+{defenseBuff}", Theme.Jade, Color.white, 12,
                    ChipPadX, ChipPadY, "defense");
            }
            int dodgeBuff = Battle.PlayerStatuses.TotalMagnitude(StatusKind.DodgeBuff);
            if (dodgeBuff > 0)
            {
                statusRow ??= Ui.Row(hpStack.transform, "PlayerStatus", 6);
                Ui.Chip(statusRow.transform, $"+{dodgeBuff}%", Theme.Jade, Color.white, 12,
                    ChipPadX, ChipPadY, "dodge");
            }
            int speedMod = Battle.PlayerStatuses.TotalMagnitude(StatusKind.SpeedModifier);
            if (speedMod != 0)
            {
                statusRow ??= Ui.Row(hpStack.transform, "PlayerStatus", 6);
                Ui.Chip(statusRow.transform, speedMod > 0 ? $"+{speedMod}" : $"−{-speedMod}",
                    speedMod > 0 ? Theme.Jade : Theme.InkSoft, Color.white, 12,
                    ChipPadX, ChipPadY, "speed");
            }

            var apStack = Ui.VStack(_bottomRow, "Ap", 4);
            Ui.ThemedLabel(apStack.transform, "AP", 12, Theme.TextDim);
            var pips = Ui.Row(apStack.transform, "Pips", 12);
            for (int i = 0; i < Battle.ApPerTurn; i++)
            {
                var pip = Ui.Panel(pips.transform, $"Pip{i}");
                var image = pip.AddComponent<Image>();
                image.sprite = Theme.Rounded(10);
                image.type = Image.Type.Sliced;
                image.color = i < Battle.Ap ? Theme.Gold : Theme.PaperDim;
                pip.transform.localRotation = Quaternion.Euler(0, 0, 45);
                var element = pip.AddComponent<LayoutElement>();
                element.preferredWidth = 18;
                element.preferredHeight = 18;
            }

            // 治疗选目标态(2026-08-22):玩家血条区整体点亮为「治玩家」的点击面。
            // 必须在 hpStack 的其余子物件都画完之后再挂——覆盖层要盖在最上面才吃得到点击
            // (与 AttachAllyTargetPicker/AttachSlotPicker 同一套「整格覆盖层」做法)。
            // 判据走 Battle.CanHealSlot(Targeting.PlayerTarget),不是恒真的假设——
            // 万一以后这条规则改了,表现层不必跟着改。
            // 拖拽落点判定要用它(2026-08-27):玩家没有「槽位」,这一块血条区就是他的落点
            _playerAllyRect = (RectTransform)hpStack.transform;
            if (_allyTargeting && Battle.CanHealSlot(Targeting.PlayerTarget))
                AttachAllyTargetPicker(hpStack.transform, Targeting.PlayerTarget);
        }

        // 召唤格尺寸(2026-08-20 四排改造)。每排 3 格而不是 6 格,格宽从 ~54 翻到 180。
        //
        // 2026-08-26 重排(用户拍板):原先「攻 / 盾 / 被动」挤在字块下面的一行属性行里,
        // 现在改成 **攻 | 字块 | 状态** 三段横排,属性行的位置让给常驻盾条。
        //   ├ 左:攻击力(定宽 SummonSideWidth)
        //   ├ 中:字块(SummonGlyphFront/Back)
        //   └ 右:状态列(同样定宽 —— 两侧等宽字块才居中,少一边字块就会偏)
        // 横向账(前排):58 + 4 + 56 + 4 + 58 = 180 ✓ 恰好铺满
        private const float SummonCellWidth = 180f;
        private const float SummonGlyphFront = 56f;
        private const float SummonGlyphBack = 48f;    // ≈ 85%
        private const float SummonSideWidth = 58f;    // 顶行左右两翼各占的宽度,必须相等
        private const float SummonHeadSpacing = 4f;
        private const float SummonBarWidthFront = 140f;
        private const float SummonBarWidthBack = 120f;
        private const float SummonShieldBarHeight = 10f;
        // 逐项加法(VStack 间距 2):
        //   前排 顶行 56 + 2 + 血条 13 + 2 + 行动条 9 + 2 + 盾条 10 = 94
        //   后排 顶行 48 + 2 + 血条 12 + 2 + 行动条 8 + 2 + 盾条 10 = 84
        // **改动内容高度时请重算这两串加法**,并对照 BuildSkeleton 里两排 section 的高度
        // (SummonsFront 100.8px / SummonsBack 93.6px)。
        private const float SummonCellHeightFront = 94f;
        private const float SummonCellHeightBack = 84f;
        private const float SummonStackSpacing = 2f;

        /// <summary>我方召唤物(木系):替玩家承伤并反击。2026-08-20 起分前后两排、各 3 格,
        /// 下标即槽位(<c>0..FrontRow-1</c> 前排,其余后排),**空槽也画**虚框占位 ——
        /// 召唤/阵亡时布局不跳动,玩家也能一眼看出还剩几个位子。</summary>
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
                var cell = Ui.VStack(front ? _summonFrontRow : _summonBackRow, $"Summon{i}", SummonStackSpacing);
                var cellElement = cell.AddComponent<LayoutElement>();
                cellElement.preferredWidth = SummonCellWidth;
                cellElement.preferredHeight = front ? SummonCellHeightFront : SummonCellHeightBack;
                _summonCellByCore[i] = (RectTransform)cell.transform;
                float glyphSize = front ? SummonGlyphFront : SummonGlyphBack;
                float barWidth = front ? SummonBarWidthFront : SummonBarWidthBack;
                int summonIndex = i; // 闭包捕获:直接用 i 会全都指向循环终值
                // 顶行三段(2026-08-26):左 攻击力 | 中 字块 | 右 状态列。
                // 两翼**必须等宽**(SummonSideWidth),否则 MiddleCenter 会把字块推偏。
                var head = Ui.Row(cell.transform, "Head", SummonHeadSpacing).transform;
                var headElement = head.gameObject.AddComponent<LayoutElement>();
                headElement.preferredWidth = SummonCellWidth;
                headElement.preferredHeight = glyphSize;

                var attackSide = Ui.Panel(head, "Attack");
                var attackElement = attackSide.AddComponent<LayoutElement>();
                attackElement.preferredWidth = SummonSideWidth;
                attackElement.preferredHeight = glyphSize;
                var attackLabel = Ui.ThemedLabel(attackSide.transform,
                    Strings.T("battle.label.summon_attack", ("attack", summon.Attack)), 12, Theme.TextDim);
                Ui.Stretch(attackLabel.rectTransform);

                // 保持着色挨打:HP 掉到 0 + 我方回合开始消失来表达阵亡,不在动画里就变灰(免飘字/掉血还没到就先灰)
                var glyph = Ui.RoundButton(head, summon.Char, () => OnSummonClicked(summonIndex),
                    Theme.ElementSoft(summon.Element), Theme.ElementSoftFg(summon.Element),
                    Mathf.RoundToInt(glyphSize * 0.46f), new Vector2(glyphSize, glyphSize), 12);
                _summonRectByCore[i] = (RectTransform)glyph.transform;

                DrawSummonStatusColumn(head, summon, glyphSize);

                // 血值上条(2026-07-25,带描边保对比度)。动画期间画出手前值,SummonHit 触达才降
                int shownHp = Animating && _summonAnimHp.TryGetValue(i, out var pre) ? pre : summon.Hp;
                _summonBarByCore[i] = HpBar(cell.transform, shownHp, summon.MaxHp,
                    new Vector2(barWidth, front ? 13 : 12));
                _summonActionBarByCore[i] = ActionBar(cell.transform, summon.ActionMeter,
                    new Vector2(barWidth, front ? 9 : 8), 8);
                // 盾条(2026-08-26)接在行动条下面,**常驻** —— 0 时是一条空条,不再有无盾时
                // 整格塌一行、加盾时又顶回来的跳动。动画期间画出手前值(与血条同理),
                // Shield / SummonHit 触达才推
                int shownShield = Animating && _summonAnimShield.TryGetValue(i, out var preShield)
                    ? preShield : summon.Shield;
                _summonShieldBarByCore[i] = ShieldBar(cell.transform, shownShield,
                    new Vector2(barWidth, SummonShieldBarHeight));
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

            float glyphSize = front ? SummonGlyphFront : SummonGlyphBack;
            var plate = Ui.Panel(cell.transform, "Lock");
            var image = plate.AddComponent<Image>();
            image.sprite = Theme.Rounded(12);
            image.type = Image.Type.Sliced;
            image.color = new Color(Theme.InkSoft.r, Theme.InkSoft.g, Theme.InkSoft.b, 0.08f);
            image.raycastTarget = false;
            var plateElement = plate.AddComponent<LayoutElement>();
            plateElement.preferredWidth = SummonCellWidth;
            plateElement.preferredHeight = glyphSize;

            // 锁图标 + 层数分两行:一行放不下「[封] 30 关解锁」而不挤(格宽 180,字号 11)
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
                float glyphSize = front ? SummonGlyphFront : SummonGlyphBack;
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

        /// <summary>进入友方选目标态(2026-08-27 抽出,三个入口共用:点「出字」、
        /// 拖到敌人身上松手后的第二段、拖纯友方字)。
        /// enemyTarget = 第一段已经选过的敌人下标;纯友方字传 −1。</summary>
        private void EnterAllyTargeting(CharDef def, int enemyTarget)
        {
            _targeting = false;
            _allyTargeting = true;
            _pendingAllyEnemyTarget = enemyTarget;
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
            BeginCast(charId, enemyTarget, attackMode: false, libraryIndex: libraryIndex, allySlot: slot);
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
            if (_modal != null) Object.Destroy(_modal);
            _modal = Ui.Modal(transform, SummonInfo.Title(summon), SummonInfo.Detail(summon),
                new Vector2(320, 200), (Strings.T("common.ok"), null, Theme.LockedBg, Theme.TextMain));
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

        // 敌人格尺寸(2026-07-28 随形象接入放大:圆头像 104 → 形象 150,格 168×208 → 190×220)。
        // 形象底稿四周留了 10% 白,同直径下视觉体积比实心圆头像小,所以要给得更足。
        // 2026-08-11:格高 220 → 232,给 chip 第二行腾 12px(信息区 68 → 80)。
        // 2026-08-17:形象 150 → 138,给每个敌人自己的行动条腾 12px + 间距。
        //
        // 2026-08-21 竖排复原(用户拍板):2026-08-20 那次为了纵向预算把格内改成「形象在左、
        // 信息在右」,实机看下来血条与它对应的怪读不成一体 —— 一排三只时,谁的条是谁的要靠数。
        // 现在改回**形象在上、血条行动条在正下方同一列**。
        //
        // 纵向预算是这么腾出来的(见 BuildSkeleton 的那串加法):
        //   ① 消息提示行(0.900–0.945,40.5px)搬到左下,中区上缘从 0.900 抬到 0.938
        //   ② 字牌去掉费用带后 84×105 → 68×85,字库区 123 → 91.8,中区下缘从 0.340 落到 0.305
        // 两笔合计 +65.7px,中区 504 → 569.7px,**形象因此一分没缩**,仍是 138 / 117。
        //
        // 2026-08-21 二改(实机反馈:名字压在形象上看不清):**名字独占一行**,排在形象正下方,
        // 形象相应缩小把这一行的高度让出来。chip 仍叠在形象底部 —— 它自带底色、又只有一两个,
        // 压在墨色笔画上仍读得出;而名字是长串文字,叠上去必糊。
        //
        // 每格纵向:2 + 形象 + 2 + 名字 19 + 2 + 条区 31 = 形象 + 56。
        // 反推形象:前排 171 − 56 = 115 → 取 114;后排 150 − 56 = 94。
        private const float EnemyPortraitFront = 114f;
        private const float EnemyPortraitBack = 94f;
        private const float EnemyNameHeight = 19f;   // 单行 15 号字 ≈ 19px
        private const float EnemyBarWidth = 170f;    // 血条/行动条:格宽减两侧各 10
        private const float EnemyBarsHeight = 31f;   // 血条 16 + 间距 3 + 行动条 12
        // 前后排同宽:宽度由条与 chip 决定,与形象直径无关。三格一排 3×190 + 16 = 586 居中,
        // 落在配字表右缘(216)与拆合台左缘(1272)之间,两头都不压。
        private const float EnemyCellWidth = 190f;
        // 格高 = 2 + 形象 + 2 + 名字 + 2 + 条区。chip 叠在形象上,不进这笔加法。
        private const float EnemyCellHeightFront =
            2f + EnemyPortraitFront + 2f + EnemyNameHeight + 2f + EnemyBarsHeight; // 170
        private const float EnemyCellHeightBack =
            2f + EnemyPortraitBack + 2f + EnemyNameHeight + 2f + EnemyBarsHeight;  // 150

        // 敌人格 chip 行(2026-08-11 换行改造)。比默认 chip 紧一档(字号 12→11、
        // 内边距 18/12→12/8、间距 5→4):实测「火 攻12 灼烧6 不灭」从 2 行降回 1 行,
        // 「水 攻15 承伤 灼烧9 不灭 致盲−50% 沉默」从 3 行降到 2 行,
        // 且两行只多要 17px 而不是 27px —— 这是 12px 预算能成立的前提。
        // 上限 2 行:3 行要再吃 22px,敌人区没有;超出的按列表顺序从尾部丢,末尾补「+N」。
        // 2026-08-21:chip 改叠在形象上之后,可用宽度从「信息列 200」换成「整格 190」——
        // 它铺满格宽而不是只铺形象(138/117),否则窄 50px 会多换一行、多丢 chip。
        private const int ChipFontSize = 11;
        private const int ChipPadX = 12;
        private const int ChipPadY = 8;
        private const float ChipSpacing = 4f;
        private const float ChipLineSpacing = 3f;
        private const int ChipMaxLines = 2;
        // 左右各留 2px:贴着列宽排会让最后一个 chip 卡在边界上,浮点抖一下就换行。
        // 2026-08-21:基准换回整格宽,前后排共用同一个数(格宽与形象直径无关)。
        private const float ChipAreaWidth = EnemyCellWidth - 4f;

        // 拖字打人悬停预览(2026-08-22):主目标复用「选目标态整格微亮」的既有强度(0.07f,
        // 见 DrawEnemies 的 hitArea.color),被形状溅到的用更淡一档,与主目标拉开区分。
        private const float HoverPreviewPrimaryAlpha = 0.07f;
        private const float HoverPreviewSplashAlpha = 0.035f;

        /// <summary>敌方两排(2026-08-20):后排在上、前排在下(贴着中间的分隔线),
        /// 站位读 <see cref="EnemyState.Row"/> —— 那是**实例状态**,开场按每排上限 3 分配、
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
        /// 跳位,尸体格位原地不动。
        ///
        /// 2026-08-23 例外:某一排**只有一只怪**时该排只建 1 格,由同一个 MiddleCenter
        /// 把它摆正中(实机反馈:单怪遭遇下铺三格会把它顶到最左)。判据在
        /// <see cref="Targeting.RowCells"/> —— 2026-08-26 收窄为「**两排都** ≤1 只」才折叠。</summary>
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

            // 每排画几格:判据在 Targeting.RowCells(与 Column 的几何同一处定义)。
            // 数的是**格位上的怪**而非存活数:尸体照样占格(见下面 showAlive 的处理),
            // 打死一只就让剩下的重新居中会让整排跳位 —— 那正是固定格位要消掉的毛病。
            int frontCount = 0, backCount = 0;
            foreach (var e in Battle.Enemies)
                if (e.Row == EnemyRow.Front) frontCount++; else backCount++;

            var frontCells = new GameObject[Targeting.RowCells(frontCount, backCount)];
            var backCells = new GameObject[Targeting.RowCells(backCount, frontCount)];
            for (int c = 0; c < frontCells.Length; c++)
            {
                frontCells[c] = Ui.Panel(_enemyFrontRow, $"EnemySlotFront{c}");
                frontCells[c].AddComponent<LayoutElement>().preferredWidth = EnemyCellWidth;
            }
            for (int c = 0; c < backCells.Length; c++)
            {
                backCells[c] = Ui.Panel(_enemyBackRow, $"EnemySlotBack{c}");
                backCells[c].AddComponent<LayoutElement>().preferredWidth = EnemyCellWidth;
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
                    || Battle.CanTarget(_graph.Get(_selectedChar), index);

                // 格位守卫(2026-08-22 评审加固):Core 的不变式(每排 ≤ RowCapacity、列不重号)
                // 理应保证 enemy.Column 永远落在 [0, RowCapacity) 且同排不重号,但表现层崩掉
                // 的代价是整个战斗界面白屏,兜底的代价只是一只怪画错位置 —— 不对称,所以兜。
                // 越界或撞列(本排该列已被这次绘制的另一只怪占了)就回落到本排第一个空格位。
                var cells = front ? frontCells : backCells;
                var used = front ? frontUsed : backUsed;
                int col = enemy.Column;
                if (col < 0 || col >= cells.Length || used[col])
                    col = System.Array.IndexOf(used, false);
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
                used[col] = true;
                var cell = cells[col];
                var cellElement = cell.GetComponent<LayoutElement>();
                cellElement.preferredHeight = front ? EnemyCellHeightFront : EnemyCellHeightBack;
                float portraitSize = front ? EnemyPortraitFront : EnemyPortraitBack;

                // 有形象就用分层字怪(Boss 按当前阶段取图),否则回落圆形字头像
                MobView mob = null;
                GameObject portrait = null;
                string prefix = MobAssets.PrefixFor(enemy.Def, enemy.PhaseIndex);
                if (MobAssets.Layer(prefix, "body") != null)
                {
                    // 死了也照旧画形象,只是置灰 —— 换回字头像会让尸体「跳」一下形
                    portrait = new GameObject($"Mob{i}", typeof(RectTransform));
                    portrait.transform.SetParent(cell.transform, false);
                    mob = portrait.AddComponent<MobView>();
                    mob.Init(prefix, portraitSize);
                    mob.SetStateAmount(MobAssets.StateAmountFor(enemy)); // L4 绑战斗状态
                    if (!showAlive || !reachable) mob.ApplyTint(Theme.LockedBg);
                }
                portrait ??= Ui.CircleGlyph(cell.transform,
                    EnemyInfo.FaceChar(enemy.Def, enemy.PhaseIndex),
                    showAlive && reachable ? Theme.ElementColor(enemy.ApparentElement) : Theme.LockedBg,
                    // 白字压在 LockedBg 这种浅底上看不见:置灰的一并把字色降到 TextDim
                    showAlive && reachable ? Color.white : Theme.TextDim, portraitSize);
                _enemyMobs.Add(mob);
                // 形象贴格顶、横向居中(2026-08-21 竖排格):名字与条区都在它正下方,同一列
                Ui.Anchor((RectTransform)portrait.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(-portraitSize / 2f, -2f - portraitSize), new Vector2(portraitSize / 2f, -2f));
                if (_targeting && enemy.Alive && reachable && mob == null)
                {
                    var outline = portrait.AddComponent<Outline>(); // 圆头像用描边示意可选中
                    outline.effectColor = Theme.Ink;
                    outline.effectDistance = new Vector2(3, 3);
                }

                // 点击区盖满整格:形象各层不吃 raycast(见 MobView),没有它整格点不动
                var hitArea = cell.AddComponent<Image>();
                hitArea.color = _targeting && enemy.Alive && reachable
                    ? new Color(Theme.Ink.r, Theme.Ink.g, Theme.Ink.b, 0.07f) // 选目标时整格微亮,提示可点
                    : new Color(0, 0, 0, 0);
                _enemyHitAreas.Add(hitArea); // 拖字打人悬停预览要就地改这个颜色,存引用

                // 名字独占一行,排在形象正下方(2026-08-21 二改)。此前它半透叠在形象顶部,
                // 而形象本身就是水墨字形 —— 长串名字压在笔画上读不出来(实机反馈)。
                var nameGo = Ui.Panel(cell.transform, "Name");
                Ui.Anchor((RectTransform)nameGo.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                    new Vector2(0f, -2f - portraitSize - 2f - EnemyNameHeight),
                    new Vector2(0f, -2f - portraitSize - 2f));
                Ui.Stretch(Ui.ThemedLabel(nameGo.transform, BossTitle(enemy), 15,
                    Theme.TextMain, Theme.TitleFont).rectTransform);

                // chip 仍叠在形象底部:它自带底色、通常只有一两个,压在笔画上仍读得出;
                // 给它独占一行要再吃 41px/格(两行 chip),两个形象就得再缩 20 —— 不值。
                // 铺**整格宽**而不是形象宽:窄 50px 会让 chip 多换一行、多丢内容。
                // chip 的底色 Image 吃射线,但 uGUI 会沿父链找到 cell 上的 Button,点击照常落到「点敌人」。
                var chipHolder = Ui.VStack(cell.transform, "ChipHolder", 0);
                chipHolder.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.LowerCenter;
                Ui.Anchor((RectTransform)chipHolder.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                    new Vector2(0f, -2f - portraitSize), new Vector2(0f, -2f));
                // chip 攒成列表再交给 ChipFlow 分行 —— 它要先看全部文字才能决定在哪断行。
                // 列表顺序即优先级:装不下 ChipMaxLines 行时从**尾部**丢弃,末尾补「+N」,
                // 所以越靠前的越保得住。完整信息仍在敌人详情弹窗里。
                var chipSpecs = new List<Ui.ChipSpec>
                {
                    // 显示用的元素名走 CharInfo.ElementName(查表)—— 与差字面板/桶键那套
                    // BattleView 私有的 ElementKey 是两件事,别弄混(见 ElementKey 定义处的注释)。
                    new(enemy.ApparentElement is { } apparent ? CharInfo.ElementName(apparent) : "?",
                        Theme.ElementColor(enemy.ApparentElement), Color.white),
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
                Ui.ChipFlow(chipHolder.transform, "Chips", chipSpecs, ChipAreaWidth, ChipFontSize,
                    ChipMaxLines, ChipPadX, ChipPadY, ChipSpacing, ChipLineSpacing);

                // 条区:形象正下方、横向居中、贴格底 —— 「血条和形象同一列」就是这一处
                var bars = Ui.VStack(cell.transform, "Bars", 3);
                Ui.Anchor((RectTransform)bars.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                    new Vector2(-EnemyBarWidth / 2f, 0f), new Vector2(EnemyBarWidth / 2f, EnemyBarsHeight));

                // 存活或濒死(死亡动画中)都画血条:动画期间画出手前值,伤害触达才逐记掉血;
                // 濒死者随死亡节拍置灰,真正死透(动画完)才转「已正」。血值上条,带描边保对比度。
                if (showAlive)
                {
                    int barHp = Animating && i < _animEnemyHp.Count ? _animEnemyHp[i] : enemy.Hp;
                    _enemyHpBars.Add(HpBar(bars.transform, barHp, enemy.MaxHp, new Vector2(EnemyBarWidth, 16)));
                    // 行动条紧跟血条(2026-08-17,用户拍板放血条下方)
                    _enemyActionBars.Add(ActionBar(bars.transform, enemy.ActionMeter, new Vector2(EnemyBarWidth, 12), 9));
                }
                else
                {
                    // 「已正」= 那个错字被改正了(2026-08-23 用户确认语义):字怪死亡时代替血条。
                    // 是主题双关而非机制描述 —— key 名 corpse_settled 说的是机制那一面,别照 key 名去译。
                    Ui.ThemedLabel(bars.transform, Strings.T("battle.label.corpse_settled"), 14, Theme.LockGray);
                    _enemyHpBars.Add((null, null));
                    _enemyActionBars.Add((null, null));   // 下标与 _enemyHpBars 严格同步
                }

                var button = cell.AddComponent<Button>();
                button.targetGraphic = hitArea;
                button.onClick.AddListener(() => OnEnemyClicked(index));
                button.interactable = enemy.Alive && reachable; // 够不到:连详情都不弹,免得像点歪了
                _enemyRects.Add((RectTransform)portrait.transform);
            }

        }

        private void DrawLibrary()
        {
            _libraryTileRects.Clear();
            // 奖励页显示携带字库(出过的字已回归)——这才是下一战的真实字库,也是替换的操作对象
            bool rewardPhase = _run.Phase == RunPhase.Reward;
            var library = rewardPhase ? _run.CarriedLibrary : Battle.Library;
            Ui.ThemedLabel(_libraryRow, Strings.T("battle.label.library_count",
                ("count", library.Count), ("capacity", Battle.LibraryCapacity)), 16, Theme.TextDim, Theme.TitleFont);
            if (!_run.LibraryExpanded)
                Ui.AdBadge(_libraryRow, "+2", () => // 原型:点击即生效,SDK 后接
                {
                    _run.TryExpandLibrary();
                    _onExpanded?.Invoke();
                    _message = Strings.T("battle.label.library_cap_up");
                    Refresh();
                }, new Vector2(64, 38));
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
                // 2026-08-21:84×105 → 68×85。费用带撤销后牌面少了 19% 的死高度,
                // 牌整体缩小把这笔省下来的还给纵向预算(字库区 123 → 95)。牌面锁 0.8 竖版比例,
                // 所以宽度跟着走。顺带:满员 12 张时行宽 12×68 + 11×8 = 904(原 1184),
                // 不再压到左侧配字表与右侧拆合台。
                var tile = Ui.GlyphTile(_libraryRow, def, selected, tap,
                    new Vector2(68, 85));
                // AP 不够就去饱和压暗、属性动效停(《字牌形象关键词包》§4.4):
                // 「用不了」要在点下去之前就看得出来,不能等弹窗告诉你
                if (!rewardPhase)
                    tile.GetComponent<CardFrameView>()?.SetPlayable(def.ApCost <= Battle.Ap);
                HoldToPreview.Attach(tile.gameObject, () => ShowCharPreview(charId));
                if (!rewardPhase) AttachDragToAttack(tile.gameObject, def, index);
                _tileRects[charId] = (RectTransform)tile.transform;
                _libraryTileRects.Add((RectTransform)tile.transform); // 卡位→牌面,飞字起点按位取
            }
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

            DragToAttack.Attach(tile, def.Id, Theme.ElementColor(def.Element),
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
                        EnterAllyTargeting(def, enemyTarget: target);
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
                        EnterAllyTargeting(def, enemyTarget: -1);
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
        /// 判定用整格(字符圆的父级)而非字符圆本身:手指落点粗,圆只有 104 宽会经常擦边落空。</summary>
        private int EnemyIndexAt(Vector2 screenPos)
        {
            for (int i = 0; i < _enemyRects.Count && i < Battle.Enemies.Count; i++)
            {
                if (!Battle.Enemies[i].Alive || _enemyRects[i] == null) continue;
                var hitArea = _enemyRects[i].parent as RectTransform ?? _enemyRects[i];
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
            return $"「{charId}」{CharInfo.EffectsText(def, _run.CardLevel(charId), _graph)}";
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

        private void DrawPool()
        {
            // 奖励页显示携带池(部件不再随战利品入池,这里只展示当前持有,2026-08-04)
            bool rewardPhase = _run.Phase == RunPhase.Reward;
            var poolChars = rewardPhase ? _run.CarriedPool : Battle.Pool;
            Ui.ThemedLabel(_poolRow, Strings.T("battle.label.pool_count",
                ("count", poolChars.Count), ("capacity", Battle.PoolCapacity)), 16, Theme.TextDim, Theme.TitleFont);
            if (!_run.PoolExpanded)
                Ui.AdBadge(_poolRow, "+2", () => // 原型:点击即生效,SDK 后接
                {
                    _run.TryExpandPool();
                    _onExpanded?.Invoke();
                    _message = Strings.T("battle.label.pool_cap_up");
                    Refresh();
                }, new Vector2(64, 38));
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
                var tile = Ui.RoundButton(_poolRow, charId, tap,
                    selected ? Theme.ElementColor(def.Element) : Theme.ElementSoft(def.Element),
                    selected ? Color.white : Theme.ElementSoftFg(def.Element),
                    22, new Vector2(56, 56), 12);
                HoldToPreview.Attach(tile.gameObject, () => ShowCharPreview(charId));
                if (!rewardPhase) AttachDragToAttack(tile.gameObject, def); // 水/土 直出的攻击用法在这一排

                // 同源徽标(2026-08-15,部件五系通用):同组**其他全部**成员各占一个角。
                // 设计参考:docs/design/frame/字斗设计板.dc.html 的部件池 row。
                //
                // 全量 + 代表字也标(2026-08-15 用户裁定,与转位提示对齐)。
                // 同组最大是金系 5 个(金钅戈刂刀),除自己外最多 4 个,四角刚好占满 ——
                // 位形指示器已于 2026-08-16 移除,左上角空出来给徽标用。
                // Ui 没有角标原语,用既有的 Chip + Anchor 拼 —— 不为这一处新增公共 API。
                if (ComponentKin.TryGetGroup(charId, out var kinGroup))
                {
                    int corner = 0;
                    foreach (var kinPart in kinGroup)
                    {
                        if (kinPart == charId) continue; // 自己不标在自己身上
                        PlaceKinBadge(tile.transform, kinPart, def.Element, corner++);
                    }
                }

                _tileRects[charId] = (RectTransform)tile.transform; // 同名部件取最后一个,动效近似即可
            }
        }

        /// <summary>把一个同源徽标贴到部件卡的某个角(2026-08-15)。
        /// corner:0=右上、1=右下、2=左下、3=左上,从右上起顺时针填。
        ///
        /// 四个角全部可用(位形指示器已于 2026-08-16 移除)。同组最大是金系 5 个
        /// (金钅戈刂刀),除自己外 4 个 —— 刚好占满四角,再加成员就得换设计。
        ///
        /// 尺寸 24×14:窄边距是刻意的(spec §1.6b「小胶囊」),默认 padX=18/padY=12 在
        /// 56×56 的卡上单个就占 68% 宽,四个角一起画会把字形埋掉。
        /// ChipWidth/ChipHeight 的 pad 必须与 Chip() 传的一致,否则尺寸算错、位置跟着错。</summary>
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

        private void DrawSuggest()
        {
            // 只提示已收集的字:合不出来的不该出现在拆合台(2026-07-19)
            var suggest = ForgeEngine.Suggest(_graph, Battle.Pool, Battle.Library, Battle.UnlockedChars);
            DrawNearMissHints(suggest.NearMisses); // 左侧差字面板:选中与否都显示
            if (_selectedChar != null || _targeting) return; // 选中态:拆合台交给拆字+动作两行
            if (suggest.Composable.Count == 0)
                Ui.ThemedLabel(_suggestRow, Strings.T("battle.hint.suggest_empty"), 15, Theme.TextDim);
            // 一行一组配方,**整组必须排得下**(2026-08-21 用户要求)。
            // 合配方读作「火 + 炎 = 焱」:部件 36×2 + 「+」12 + 「=」14 + 结果 60 + 间距 6×4 = 182px,
            // 而拆合台竖栏内宽 304px(0.795–0.985,CardPanel 只有圆角、无内边距)—— 放得下,余 122px。
            var comboStack = Ui.VStack(_suggestRow, "ComboRows", 4);
            foreach (var id in suggest.Composable)
            {
                string charId = id;
                var def = _graph.Get(charId);
                var combo = Ui.Row(comboStack.transform, $"Combo_{charId}", 6); // 触控:组间距/主按钮加大
                for (int n = 0; n < def.Recipe.Count; n++)
                {
                    if (n > 0) Ui.ThemedLabel(combo.transform, "+", 14, Theme.TextDim);
                    var partDef = _graph.Get(def.Recipe[n]);
                    Ui.RoundButton(combo.transform, def.Recipe[n], null,
                        Theme.ElementColor(partDef.Element), Color.white, 15, new Vector2(36, 36), 8);
                }
                Ui.ThemedLabel(combo.transform, "=", 14, Theme.TextDim);
                // 结果字牌:白底 + 属性色大字,点击即合(2026-07-19 反馈:去「合」字;不加粗,粗体发糊)
                Ui.RoundButton(combo.transform, charId, () => OnCompose(charId),
                    Color.white, Theme.ElementColor(def.Element), 30, new Vector2(60, 54), 12);
            }
        }

        // 五行分桶排序键——与 ElementKey()/ElementByName() 是同一套内部键,只管排序/查表,
        // 显示另走 CharInfo.ElementName(查字符串表,见 DrawNearMissHints 的胶囊标签)。
        // 键必须留原始汉字、不进字符串表:ElementByName 拿它反查 Element,翻译了就再也查不回去(2026-08-23)。
        private static readonly string[] HintBucketOrder = { "金", "木", "水", "火", "土", "心", "中性" };

        /// <summary>差字面板(屏幕左侧竖排):统一五行三级目录——属性→字→差什么,玩家主动查。</summary>
        private void DrawNearMissHints(System.Collections.Generic.IReadOnlyList<NearMiss> nearMisses)
        {
            if (nearMisses.Count == 0) return;

            // 分桶:目标字的五行属性(心/中性单列,避免丢字)
            var buckets = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<NearMiss>>();
            foreach (var miss in nearMisses)
            {
                var element = _graph.Get(miss.CharId).Element;
                // "中性" 同样是分桶用的 key,显示走 CharInfo.ElementName/char.element.neutral(同上)。
                string key = element is { } e ? ElementKey(e) : "中性";
                if (!buckets.TryGetValue(key, out var list))
                    buckets[key] = list = new System.Collections.Generic.List<NearMiss>();
                list.Add(miss);
            }

            // 一级:属性胶囊竖排(带可合数),点选/再点收起
            Ui.ThemedLabel(_hintColumn, Strings.T("battle.hint.recipe_panel_title"), 16, Theme.TextDim, Theme.TitleFont);
            foreach (var key in HintBucketOrder)
            {
                if (!buckets.TryGetValue(key, out var list)) continue;
                bool selected = _hintBucket == key;
                var element = ElementByName(key);
                // 标签走 CharInfo.ElementName(查表);key 只管上面的桶查找/下面的 selected 比对,
                // 不进这个字符串(2026-08-23 补漏:此前直接显示 key,英文包下胶囊仍是「金 3」这种原始汉字)。
                string label = element is { } el ? CharInfo.ElementName(el) : Strings.T("char.element.neutral");
                Ui.RoundButton(_hintColumn, $"{label} {list.Count}", () =>
                {
                    _hintBucket = selected ? null : key;
                    _hintCharFocus = null;
                    Refresh();
                }, selected ? Theme.ElementColor(element) : Theme.ElementSoft(element),
                    selected ? Color.white : Theme.ElementSoftFg(element), 14, new Vector2(100, 36), 8);
            }

            if (_hintBucket == null || !buckets.TryGetValue(_hintBucket, out var bucketChars)) return;

            // 二级:该系可合的字(窄栏每行 4,最多四行,超出计数)
            const int perRow = 4, maxShown = 16;
            Transform row = null;
            NearMiss? focused = null;
            for (int i = 0; i < bucketChars.Count && i < maxShown; i++)
            {
                if (i % perRow == 0) row = Ui.Row(_hintColumn, $"HintChars{i / perRow}", 4).transform;
                var miss = bucketChars[i];
                bool focus = _hintCharFocus == miss.CharId;
                if (focus) focused = miss;
                var def = _graph.Get(miss.CharId);
                Ui.RoundButton(row, miss.CharId, () =>
                {
                    _hintCharFocus = focus ? null : miss.CharId;
                    Refresh();
                }, focus ? Theme.Ink : Theme.CardWhite,
                    focus ? Color.white : Theme.ElementColor(def.Element), 15, new Vector2(38, 36), 8);
            }
            if (bucketChars.Count > maxShown)
                Ui.ThemedLabel(_hintColumn, Strings.T("battle.hint.more_chars", ("count", bucketChars.Count)), 13, Theme.TextDim);

            // 三级:差什么
            if (focused is { } target)
            {
                var def = _graph.Get(target.CharId);
                Ui.ThemedLabel(_hintColumn,
                    Strings.T("battle.hint.missing_ingredient", ("charId", target.CharId),
                        ("recipe", string.Join("+", def.Recipe)), ("missing", target.MissingIngredient)),
                    14, Theme.TextMain);
            }
        }

        // 反向解析:把 ElementKey()/HintBucketOrder 那套内部键解回 Element。同样不进字符串表——
        // 这里认的是 ElementKey() 吐出的原始汉字,不是玩家看到的翻译文本。
        private static Element? ElementByName(string name) => name switch
        {
            "金" => Element.Metal,
            "木" => Element.Wood,
            "水" => Element.Water,
            "火" => Element.Fire,
            "土" => Element.Earth,
            "心" => Element.Heart,
            _ => null,
        };

        private void DrawActions()
        {
            // 选位置态:动作行只画一句提示(2026-08-20 review M-2)。这一段必须在判空**之前**——
            // 拖召唤字进来的那条路径 _selectedChar 是 null,早退的话这里什么也不画,
            // 玩家看不出自己正处在「等你选位置」的状态。
            // 不画「出/拆/弃」:再点一次「出」会走 OnCastPressed → BeginCast,把落位态悄悄重置。
            // 「取消」按钮 2026-08-21 随整排一起移除 —— 点空白即取消(Backdrop)。
            if (_slotPicking)
            {
                Ui.ThemedLabel(_actionRow, Strings.T("battle.hint.slot_picking_dragging", ("charId", _pendingSummonChar)), 16, Theme.TextMain);
                return;
            }
            if (_selectedChar == null) return;
            var def = _graph.Get(_selectedChar);

            // 第一行(拆字):选中字 → 部件拆解,读作「炎 → 火 + 火」。
            // 2026-08-21:整组排在**同一行**(用户要求)。此前「选中字 + 箭头」与「部件」分成两行,
            // 那是 2026-08-20 竖栏只有 217.6px 宽时的将就;竖栏加宽到 304px 后一行装得下:
            //   选中字 52 + 6 + 箭头 16 + 6 + 部件 38 + 4 + 「+」12 + 4 + 部件 38 = 176px,余 128px。
            // 同源变体那支仍可能多到 4 个:52 + 6 + 16 + 6 + 38×4 + 6×3 = 244px,也放得下。
            var head = Ui.Row(_suggestRow, "Selected", 6).transform;
            Ui.RoundButton(head, def.Id, null, Theme.Ink, Color.white, 22, new Vector2(52, 52), 12);
            if (!def.IsLeaf)
            {
                Ui.ThemedLabel(head, "→", 16, Theme.TextDim);
                for (int n = 0; n < def.Recipe.Count; n++)
                {
                    if (n > 0) Ui.ThemedLabel(head, "+", 14, Theme.TextDim);
                    Ui.RoundButton(head, def.Recipe[n], null,
                        Theme.ElementColor(_graph.Get(def.Recipe[n]).Element), Color.white, 16, new Vector2(38, 38), 8);
                }
            }
            else
            {
                // 转位提示(2026-08-15 用户裁定):选中五系部件时,把**同组全部**可互换的成员列出来
                // ——选 氵 显示「氵 ⇄ 水 冫」,选 刂 显示「刂 ⇄ 金 钅 戈」。
                //
                // 这里与右上角 ≈X 徽标是**两条不同的口径**,别互相"对齐":
                // 徽标是单向的(变体 → 代表字,代表字自己不带徽标),它要在一张 56×56 的卡上
                // 用最小的面积回答「这张是什么」;转位提示是选中后的详情,空间够,给全量。
                // 所以判据用 TryGetGroup 而不是 KinBadge —— 后者对代表字返回 null,会把
                // 选中 水 时的提示整条吞掉(用户点名要补的正是这一条)。
                //
                // 纯说明不是操作:等价匹配在 ForgeEngine.TryCompose 里自动生效,不花 AP(spec §1.6c)。
                if (ComponentKin.TryGetGroup(_selectedChar, out var kinGroup))
                {
                    Ui.ThemedLabel(head, "⇄", 16, Theme.TextDim);
                    foreach (var kin in kinGroup)
                    {
                        if (kin == _selectedChar) continue; // 自己不列进"可换成"
                        Ui.RoundButton(head, kin, null,
                            Theme.ElementColor(_graph.Get(kin).Element), Color.white, 16, new Vector2(38, 38), 8);
                    }
                    Ui.ThemedLabel(_suggestRow, Strings.T("battle.hint.kin_variant_label"), 13, Theme.TextDim);
                }
                else
                {
                    Ui.ThemedLabel(_suggestRow, Strings.T("battle.hint.leaf_char"), 14, Theme.TextDim);
                }
            }

            // 第二行(动作)
            if (_targeting)
            {
                Ui.ThemedLabel(_actionRow, Strings.T("battle.hint.targeting_enemy", ("charId", _selectedChar)), 16, Theme.TextMain);
                return;
            }
            // 治疗选目标态(2026-08-22):同 _targeting 一样只画一句提示——再点一次「出」
            // 会走 OnCastPressed → BeginCast,把这个待选态悄悄重置。
            if (_allyTargeting)
            {
                Ui.ThemedLabel(_actionRow, Strings.T("battle.hint.targeting_ally", ("charId", _selectedChar)), 16, Theme.TextMain);
                return;
            }
            bool inLibrary = System.Linq.Enumerable.Contains(Battle.Library, _selectedChar);
            // 2026-08-21 用户拍板:动作名一律收成单字 —— 「出字 / 直出 / 兜底一击」三种情形
            // 统一叫「出」,「丢弃」叫「弃」。竖栏里按钮排一行,长标签会把整行挤换行;
            // 而三种「出」的差别(库里出 / 部件直出 / 无效果字的兜底一击)属于结算细节,
            // 玩家在按钮上分不分得清都不影响他要点的那一下。
            // 动作按钮 ≥50 高(2026-07-19 iOS 反馈:手指可点性)
            Ui.RoundButton(_actionRow, Strings.T("battle.btn.cast"), () => OnCastPressed(def), Theme.Cinnabar, Color.white, 17, new Vector2(76, 52));
            if (inLibrary && !def.IsLeaf)
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
                System.Action tap = () =>
                {
                    if (_previewRewardIndex != index)
                    {
                        _previewRewardIndex = index; // 首点预览效果,再点确认
                        Refresh();
                        return;
                    }
                    _previewRewardIndex = -1;
                    if (_run.PickReward(index))
                    {
                        _tutorial?.Notify(TutorialAction.PickReward);
                        _message = Strings.T("battle.reward.added_msg", ("charId", id));
                        CancelSelection(); // 额度归零 → 下次 Refresh 由 Core 侧自动开拔
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
                var tile = Ui.GlyphTile(row.transform, def,
                    index == _previewRewardIndex, tap);
                HoldToPreview.Attach(tile.gameObject, () => ShowCharPreview(id));
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

        /// <summary>奇遇三个选项的效果明细,一行排开(2026-08-27 用户拍板)。
        ///
        /// 效果说明**不在选项钮上** —— 钮宽 260 / 字号 22 只装得下 23 个半宽,而
        /// 「入炉淬骨(八成 上限 +30%,两成 反噬 −30%)」有 39 个,溢出到钮外被邻钮盖掉。
        /// 于是 label 只留名称,说明搬到通栏的正文行下面,不吃那个宽度限。
        ///
        /// 常驻显示而不是长按/悬停才出:奇遇是**不可逆**决策,「点下去会发生什么」必须在点之前
        /// 就看得见;而长按看详情那套惯例(字牌/敌人)首次遇到的玩家不会知道要长按。
        ///
        /// 分隔用两个全角空格,与 battle.event.body_with_ink 里「(墨锭」前那 4 个半角空格同性质
        /// —— 是排版留白,不是文案,所以不进字符串表。
        /// 无效果的「离开」类选项显式画成「无」,不留空白 —— 空白读起来像没加载出来。</summary>
        private static string EventOptionDetails(EventDef evt)
        {
            var parts = new System.Collections.Generic.List<string>(evt.Options.Count);
            foreach (var option in evt.Options)
                parts.Add(Strings.T("battle.event.option_detail",
                    ("label", option.Label),
                    ("detail", string.IsNullOrEmpty(option.Detail)
                        ? Strings.T("battle.event.option_detail_none")
                        : option.Detail)));
            return string.Join("\u3000\u3000", parts);
        }

        private int _pendingEventOption = -1; // 部件抵价/任选字:待成交的选项下标
        private int _pendingCharChoice = -1;  // 任选字:已选中的字下标(-1 = 未选)
        private readonly System.Collections.Generic.List<int> _eventPicks = new(); // 已点选的池下标

        private void DrawEvent() // 奇遇(9.6):短情境 + 选择;部件抵价/任选字由玩家点选(2026-07-19)
        {
            var evt = _run.CurrentEvent;
            // evt.Id 是奇遇事件配置数据里的 id/展示名,不是本文件的硬编码文案——这里只登记
            // 「奇遇 · X」这层胶字模板本体。
            Ui.ThemedLabel(_enemyFrontRow, Strings.T("battle.event.title", ("eventName", evt.Id)), 30, Theme.TextMain, Theme.TitleFont);
            // 情境文案画在战场那一排,**不是** _statusRow(2026-08-20 修回):_statusRow 在屏幕
            // 最底边,把文案放那儿会变成 选项钮 → 部件池 → 文案,玩家得先看见三个按钮、
            // 再把视线甩到屏幕底边才读得到自己在选什么。这一排(0.431–0.543)在奇遇阶段本来
            // 就是空的(四排只在战斗阶段画),且**高于**选项钮所在的 _centerRow(0.125–0.220),
            // 阅读顺序因此是 文案 → 选项。2026-08-21 标题也搬到了左下,但这条理由与标题无关。
            // evt.Text 同样是事件正文数据,不在本文件文案范围——这里登记「正文 + 墨锭余额」
            // 这层胶字模板,「(墨锭」前 4 个空格是刻意留白,别收窄。
            // 正文之后换行接效果明细(2026-08-27):选项钮上只有名称,「点下去会发生什么」全在这一行。
            // 与正文共用一个 Text 而不是再加一个 label —— _summonFrontRow 是 HorizontalLayoutGroup,
            // 第二个 label 会横向并排而不是换到下一行;"\n" 走 verticalOverflow = Overflow,两行都画得出。
            Ui.ThemedLabel(_summonFrontRow, Strings.T("battle.event.body_with_ink", ("eventText", evt.Text), ("ink", _run.AvailableInk))
                + "\n" + EventOptionDetails(evt), 18, Theme.TextDim);

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
                    if (_run.ChooseEventOption(index))
                    {
                        _message = option.InkChancePercent > 0 // 赌注:按墨锭变化播报输赢
                            ? (_run.AvailableInk > inkBefore
                                ? Strings.T("battle.event.gamble_win", ("ink", option.Ink))
                                : Strings.T("battle.event.gamble_lose"))
                            : $"{evt.Id}:{option.Label}";
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
                }, affordable ? Theme.InkSoft : Theme.LockedBg,
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
                    if (_run.ChooseEventOption(_pendingEventOption, picks, _pendingCharChoice, replaceIndex))
                    {
                        _message = Strings.T("battle.event.trade_replaced_msg", ("incoming", incoming), ("dropped", dropped));
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
                    if (_run.ChooseEventOption(_pendingEventOption, null, choice))
                    {
                        _message = Strings.T("battle.event.trade_got_msg", ("charId", charId));
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
                        if (_run.ChooseEventOption(_pendingEventOption, _eventPicks.ToArray(), _pendingCharChoice))
                        {
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
            if (_selectedChar == charId && _selectedIndex == index && !_targeting && !_allyTargeting)
            {
                OnCastPressed(_graph.Get(charId)); // 再点一次选中字 = 直接出字
                return;
            }
            _selectedChar = charId;
            _selectedIndex = index;
            _targeting = false;
            _allyTargeting = false;
            _pendingAllyEnemyTarget = -1;
            ResetSlotPicking(); // 改主意点了别的字:上一张的落位作废
            _message = Brief(charId) + Strings.T("battle.hint.suffix_tap_again_cast");
            Refresh();
        }

        private void OnPoolCharClicked(string charId)
        {
            if (_selectedChar == charId && _selectedIndex < 0 && !_targeting && !_allyTargeting)
            {
                OnCastPressed(_graph.Get(charId)); // 再点一次选中部件 = 直出
                return;
            }
            _selectedChar = charId;
            _selectedIndex = -1;
            _targeting = false;
            _allyTargeting = false;
            _pendingAllyEnemyTarget = -1;
            ResetSlotPicking();
            _message = Brief(charId) + Strings.T("battle.hint.suffix_direct_cast");
            Refresh();
        }

        private void OnCastPressed(CharDef def)
        {
            // 免选的判据是**合法目标**而不是存活敌人(2026-08-20):前排只剩一只时,
            // 出一张够不到后排的字本就没得选,还弹一次选目标纯属让玩家白点一下。
            // 与 Core 的 Cast 同口径 —— 那边合法目标恰好一个时会自动锁定。
            if (BattleEngine.NeedsTarget(def) && LegalTargetCount(def, attackMode: false) > 1)
            {
                _targeting = true;
                _message = Strings.T("battle.hint.cast_pick_enemy_target", ("charId", def.Id));
                Refresh();
                return;
            }
            // 友方目标(2026-08-22):场上有存活召唤物才进选目标态——没有的话引擎会自动锁玩家
            // (Cast 里 AliveSummons() == 0 那条免选口径),UI 弹一次没得选的选择纯属白点一下,
            // 与上面「单敌免选」同一条纪律。
            if (BattleEngine.NeedsAllyTarget(def) && Battle.AliveSummonCount > 0)
            {
                EnterAllyTargeting(def, enemyTarget: -1);
                Refresh();
                return;
            }
            BeginCast(def.Id, -1, attackMode: false, libraryIndex: _selectedIndex);
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
        /// 2026-08-23 用户拍板:落位表改由引擎的 <see cref="BattleEngine.PlanSummonSlots"/> 算
        /// —— 顺延时**跳过站着人的位子**,只有空位真的凑不满才顶替,于是「点在有人的格上、
        /// 旁边还空着」不再弹替换确认。表现层不自己推这套规则:它决定召唤物落在哪,
        /// 是引擎语义,而且「不重复、长度恰好」那两条不变式也由引擎那边一并保证
        /// (破坏任一条会让第二只写进同一个槽或被静默吞掉,而 AP 已经扣了)。</summary>
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
                if (!Battle.CanTarget(picked, index)) return;
                // 还要选友方就转第二段,别在这里就出字(2026-08-26)。免选口径与 OnCastPressed
                // 那条同源:场上没有存活召唤物时引擎会自动锁玩家,弹一次没得选的选择纯属白点。
                if (BattleEngine.NeedsAllyTarget(picked) && Battle.AliveSummonCount > 0)
                {
                    EnterAllyTargeting(picked, enemyTarget: index);
                    Refresh();
                    return;
                }
                BeginCast(_selectedChar, index, attackMode: false, libraryIndex: _selectedIndex);
                return;
            }
            // 非选目标态点怪 = 看详情(2026-07-22);此前这里什么也不做
            if (_modal != null) Object.Destroy(_modal);
            _modal = EnemyPreview.Show(transform, Battle.Enemies[index].Def, phase: Battle.Enemies[index].PhaseIndex);
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
                _tutorial?.Notify(TutorialAction.Dismantle, charId);
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
                _tutorial?.Notify(TutorialAction.Compose, charId);
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
            while (true)
            {
                SnapshotPreHp();       // 每个行动者出手前的血量:动画逐记扣
                var preMeters = MeterSnapshot();   // 推进前的计量器:条从这里起步
                bool more = Battle.AdvanceOnce();
                var postMeters = MeterSnapshot();  // 推进后:条的终点
                var events = new System.Collections.Generic.List<BattleEvent>(Battle.LastEvents);
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
                    _juice.Play(events, EnemyAnchor, SummonAnchor, () => done = true, OnImpact, SummonAt);
                    while (!done) yield return null;
                    yield return new WaitForSecondsRealtime(0.12f); // 行动者之间的停顿(替代已删的 Juice.PhaseGap)
                }
                DropActingBar(Battle.LastActor, postMeters); // 动作播完,行动者的条回落到余额
                Refresh();
                if (!more) break;
                if (Battle.Phase == BattlePhase.Won || Battle.Phase == BattlePhase.Lost) break;
            }
            _message = (Battle.Phase == BattlePhase.PlayerTurn
                ? Strings.T("battle.phase.new_turn_prefix", ("turn", Battle.Turn), ("apPerTurn", Battle.ApPerTurn)) : "") + _message;
            OnAnimDone(allDeaths); // 解锁输入(_animsInFlight 归零)+ 清死亡着色 + 归零后重绘
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
                if (step.Events.Any(e => e.Kind != BattleEventKind.ActorActed))
                {
                    bool done = false;
                    var events = new System.Collections.Generic.List<BattleEvent>(step.Events);
                    _juice.Play(events, EnemyAnchor, SummonAnchor, () => done = true, OnImpact);
                    while (!done) yield return null;
                    yield return new WaitForSecondsRealtime(0.12f);
                }
                DropActingBar(step.Actor, post);
                pre = post;
            }

            Refresh();
            OnAnimDone(allDeaths); // 解锁输入 + 清死亡着色 + 归零后重绘(Battle 已 Won 时才出结算)
        }

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

        // 内部桶键,与 CharInfo.ElementName(查表、已迁字符串表)是两套不同用途的实现,别合并:
        // 差字面板拿它当分桶/排序/反查的 key(见 HintBucketOrder / ElementByName),显示则另外
        // 调 CharInfo.ElementName——键翻译了,ElementByName 的反向解析就找不到桶,所以键必须
        // 留原始汉字,显示与键必须分开(2026-08-23,Task 6 元素名分离裁定)。
        private static string ElementKey(Element element) => element switch
        {
            Element.Wood => "木",
            Element.Fire => "火",
            Element.Earth => "土",
            Element.Metal => "金",
            Element.Water => "水",
            Element.Heart => "心",
            _ => "?",
        };
    }
}
