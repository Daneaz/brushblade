using System.Text;
using Brushblade.Core;
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
        // 召唤物本体/血条按 _summons 下标索引(事件带 SecondIndex 定位承伤/发起者;死后仍在动画期可见)
        private readonly System.Collections.Generic.Dictionary<int, RectTransform> _summonRectByCore = new();
        private readonly System.Collections.Generic.Dictionary<int, (RectTransform fill, UnityEngine.UI.Text label)> _summonBarByCore = new();
        private readonly System.Collections.Generic.Dictionary<int, int> _summonAnimHp = new(); // 出手前血(下标→值);SummonHit 触达按承伤者下标逐记降
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

        private BattleEngine Battle => _run.Battle;

        // 交互状态
        private string _selectedChar;   // 当前选中的字/部件
        private bool _targeting;        // 等待点击敌人
        private GameObject _modal;      // 当前模态弹窗(同屏仅一个)
        private GameObject _rewardModal;// 战利品弹窗:与 _modal 分层,避免提示覆盖选择流程
        private string _message = "点击字库中的字开始行动";

        private string _title;          // 关卡标题(顶栏,可选)
        // 局内奇遇能抬高上限(2026-08-04),故以引擎当场值为准;Init 透传的那份只作 Battle 未就绪时的兜底
        private int _playerMaxHp = 50;
        private int PlayerMaxHp => Battle?.MaxHp ?? _playerMaxHp;

        // 容器
        private Transform _enemyRow;
        private Transform _summonRow;    // 我方前排召唤物:夹在敌我血条之间
        private Transform _topLeft, _topRight, _bottomRow;
        private Transform _statusRow;    // 教程提示/奇遇文案(结束回合钮 2026-07-21 已移出)
        private Transform _endTurnRow;   // 结束回合钮:屏幕右缘垂直居中(2026-07-21)
        private Transform _libraryRow;
        private Transform _poolRow;
        private Transform _suggestRow;
        private Transform _hintColumn;   // 差字面板(屏幕左侧竖排,五行三级目录)
        private Transform _actionRow;
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
            _juice.Play(events, EnemyAnchor, SummonAnchor, () => OnAnimDone(deaths), OnImpact);
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
            for (int i = 0; i < Battle.Summons.Count; i++)
                if (Battle.Summons[i].Alive) _summonAnimHp[i] = Battle.Summons[i].Hp; // 出手前存活者(下标→血);本回合被打死的仍画得出,旧尸不画
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
                case BattleEventKind.EnemyAttack: // Amount 分账:Absorbed 走护盾条,余量才掉血,各自钳到终值
                    _animShield = System.Math.Max(Battle.PlayerShield, _animShield - e.Absorbed);
                    SetShieldBar(_animShield);
                    if (_playerHpBar.fill == null) break;
                    _animPlayerHp = System.Math.Max(Battle.PlayerHp, _animPlayerHp - (e.Amount - e.Absorbed));
                    SetHpBar(_playerHpBar, _animPlayerHp, PlayerMaxHp);
                    break;
                case BattleEventKind.Shield: // 筑盾触达才涨,与掉盾同一条推进(不整屏重绘)
                    _animShield = System.Math.Min(Battle.PlayerShield, _animShield + e.Amount);
                    SetShieldBar(_animShield);
                    _juice.BarPulse(_playerShieldBar.fill, Theme.Jade, Element.Earth); // 土:盾条起势
                    break;
                case BattleEventKind.Heal: // 水系治疗:与群攻同一记里触达,血条即时上推(此前只在末次重绘才涨)
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
                    if (si < 0 || si >= Battle.Summons.Count || !_summonAnimHp.ContainsKey(si)
                        || !_summonBarByCore.TryGetValue(si, out var sbar) || sbar.fill == null) break;
                    _summonAnimHp[si] = System.Math.Max(Battle.Summons[si].Hp, _summonAnimHp[si] - e.Amount);
                    SetHpBar(sbar, _summonAnimHp[si], Battle.Summons[si].MaxHp);
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

        private const float ShieldBarFull = 30f; // 护盾条满格基准值(无上限概念,取常见量级)

        /// <summary>护盾条就地推进(条未画出时静默跳过,如出手前后都无盾)。</summary>
        private void SetShieldBar(int shield)
        {
            if (_playerShieldBar.fill != null)
                Ui.Anchor(_playerShieldBar.fill, Vector2.zero, new Vector2(Mathf.Clamp01(shield / ShieldBarFull), 1),
                    Vector2.zero, Vector2.zero);
            if (_playerShieldBar.label != null) _playerShieldBar.label.text = $"护盾 {shield}";
        }

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
                if (_selectedChar != null || _targeting) CancelSelection();
            });

            // 顶栏:标题 | 墨锭 · 回合 · 退出
            var topBar = Ui.Panel(transform, "TopBar");
            Ui.Anchor((RectTransform)topBar.transform, new Vector2(0.02f, 0.94f), new Vector2(0.98f, 1f), Vector2.zero, Vector2.zero);
            _topLeft = Ui.Row(topBar.transform, "Left", 10).transform;
            Ui.Anchor((RectTransform)_topLeft, new Vector2(0, 0), new Vector2(0.4f, 1), Vector2.zero, Vector2.zero);
            _topRight = Ui.Row(topBar.transform, "Right", 14).transform;
            Ui.Anchor((RectTransform)_topRight, new Vector2(0.4f, 0), new Vector2(1, 1), Vector2.zero, Vector2.zero);

            // 五行速查常驻(2026-07-22;2026-07-29 改为直接摆环图,不再点开弹窗):
            // 挂在消息行两端(那里通常是空白),向下延展 —— 敌人行居中排布,够不到这两角
            // 纵向 0.158×900 ≈ 142px = 标题 20 + 间距 2 + 环图 120
            var keGo = WuxingChart.Mount(transform, sheng: false);
            Ui.Anchor((RectTransform)keGo.transform,
                new Vector2(0.004f, 0.780f), new Vector2(0.086f, 0.938f), Vector2.zero, Vector2.zero);

            var shengGo = WuxingChart.Mount(transform, sheng: true);
            Ui.Anchor((RectTransform)shengGo.transform,
                new Vector2(0.914f, 0.780f), new Vector2(0.996f, 0.938f), Vector2.zero, Vector2.zero);

            var messageGo = Ui.Panel(transform, "Message");
            Ui.Anchor((RectTransform)messageGo.transform, new Vector2(0.02f, 0.900f), new Vector2(0.98f, 0.945f), Vector2.zero, Vector2.zero);
            _messageLabel = Ui.ThemedLabel(messageGo.transform, "", 19, Theme.TextDim);
            Ui.Stretch(_messageLabel.rectTransform);

            // 上三排「敌我对立」(2026-07-20 拍板):敌人 / 召唤物(中间) / 我方血条 AP。
            // 纵向分配按 900 基准高(CanvasScaler 1600×900 按高匹配)预留硬尺寸:
            // 敌人格 208、字牌 118、部件钮 56——各区都留了几像素余量
            _enemyRow = MakeSection("Enemies", 0.648f, 0.898f);  // 225px ≥ 220(2026-07-28 随形象放大,向上吃了消息条几像素)
            _summonRow = MakeSection("Summons", 0.560f, 0.648f); // 79px:50 方块 + 血条(血值上条) + 攻力行
            _bottomRow = MakeSection("PlayerStats", 0.505f, 0.560f); // 50px:HP/AP 横排(血值上条后省一行)

            // 拆合台薄宣纸卡(半透,融层段染色):第一行内容(配方/拆字),第二行动作
            // 2026-07-20 移到最下面;左缘仍避开配字表(0.135 宽,2026-07-19 反馈:曾重叠)
            var workbenchCard = Ui.CardPanel(transform, "Workbench", Theme.PaperCard, 20);
            Ui.Anchor((RectTransform)workbenchCard.transform, new Vector2(0.145f, 0.012f), new Vector2(0.92f, 0.230f), Vector2.zero, Vector2.zero);
            var workbenchStack = Ui.VStack(workbenchCard.transform, "Stack", 8);
            Ui.Stretch((RectTransform)workbenchStack.transform);
            Ui.ThemedLabel(workbenchStack.transform, "拆 合 台", 13, Theme.TextDim, Theme.TitleFont);
            _suggestRow = Ui.Row(workbenchStack.transform, "Content", 10).transform;
            _actionRow = Ui.Row(workbenchStack.transform, "Actions", 8).transform;

            // 差字面板:屏幕最左侧,上下居中,五行三级目录
            var hintGo = Ui.VStack(transform, "HintPanel", 4);
            Ui.Anchor((RectTransform)hintGo.transform, new Vector2(0.002f, 0.16f), new Vector2(0.135f, 0.84f), Vector2.zero, Vector2.zero);
            hintGo.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
            _hintColumn = hintGo.transform;

            _libraryRow = MakeSection("Library", 0.368f, 0.505f); // 123px ≥ 118 字牌
            _poolRow = MakeSection("Pool", 0.300f, 0.368f);       // 61px ≥ 56 部件钮
            _statusRow = MakeSection("Status", 0.230f, 0.300f);  // 63px:教程提示/奇遇文案

            // 结束回合钮:屏幕右缘垂直居中(2026-07-21,右手拇指位)。字库满员 8 张 ×118
            // 居中最宽到 x≈1300(1600 基准),这里从 1376 起,不压字牌行
            var endTurnGo = Ui.Row(transform, "EndTurn");
            Ui.Anchor((RectTransform)endTurnGo.transform,
                new Vector2(0.86f, 0.44f), new Vector2(0.99f, 0.56f), Vector2.zero, Vector2.zero);
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

        private void Refresh()
        {
            // 战利品阶段结束(不论选完、跳过还是引擎自动开拔)→ 本层记账落盘,挂起不丢收益
            if (_lastPhase == RunPhase.Reward && _run.Phase != RunPhase.Reward)
                _onFloorCleared?.Invoke();
            _lastPhase = _run.Phase;

            // 复活补给额度取尽或候选枯竭 → 收尾。
            // 满库**不再**收尾(2026-08-04):看了广告却因满库一无所得是白看,现在转入替换子步,
            // 与战利品 PickRewardReplacing 同口径。
            if (_run.Phase == RunPhase.Reviving
                && !(_run.ReviveCharPicksLeft > 0 && _run.RewardOptions.Count > 0))
                _run.SkipReviveReward();

            if (_run.Phase == RunPhase.InBattle && _run.BattleIndex != _lastBattleIndex)
            {
                _lastBattleIndex = _run.BattleIndex;
                _onNewFloor?.Invoke(); // 新一场开打:携带态已就位,供外层快照
            }
            _tileRects.Clear();
            Ui.Clear(_topLeft);
            Ui.Clear(_topRight);
            Ui.Clear(_enemyRow);
            Ui.Clear(_suggestRow);
            Ui.Clear(_actionRow);
            Ui.Clear(_hintColumn);
            Ui.Clear(_statusRow);
            Ui.Clear(_endTurnRow);
            Ui.Clear(_libraryRow);
            Ui.Clear(_poolRow);
            Ui.Clear(_bottomRow);
            Ui.Clear(_summonRow);
            if (_run.Phase != RunPhase.Reward && _run.Phase != RunPhase.Reviving && _rewardModal != null)
                Destroy(_rewardModal); // 离开战利品/复活阶段:弹窗不能留在战斗界面上

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
                        Ui.ThemedLabel(_statusRow, "结算中……", 20, Theme.TextDim, Theme.TitleFont);
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
                sb.Append('|').Append(summon.Hp);
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
            TutorialStep.DismantleDemo => "选中【剑】点【拆】——拆出两个部件『佥』『刂』",
            TutorialStep.RecomposeDemo => "两个部件能拼回去:点提示里的【合 剑】——拆与合互为表里",
            TutorialStep.CastDemo => "选中【剑】点【出】——金克木,一剑斩掉这只木系字怪",
            TutorialStep.PickReward => "战利品:选中意的字,最多挑 2 个——出过的字不回来,靠拆合再生产",
            _ => "",
        };

        private void DrawTopBar()
        {
            Ui.ThemedLabel(_topLeft, string.IsNullOrEmpty(_title) ? $"战斗 {_run.BattleIndex + 1}" : $"{_title} · 战斗 {_run.BattleIndex + 1}",
                20, Theme.TextMain, Theme.TitleFont, TextAnchor.MiddleLeft);
            Ui.IngotLabel(_topRight, _run.AvailableInk.ToString(), 18);
            Ui.ThemedLabel(_topRight, $"回合 {Battle.Turn}", 18, Theme.TextDim);
            bool suspend = _onExit != null; // 无尽:退出可挂起/弃塔(2026-07-19);否则=认输
            Ui.PillButton(_topRight, "退出", () => // 统一弹窗确认(2026-07-19 拍板)
            {
                if (suspend)
                    ShowModal("离 塔",
                        "挂起:保留进度,下次从本层继续\n弃塔:墨锭半额结算,层数纪录保留",
                        ("挂起离塔", _onExit, Theme.Cinnabar, Color.white),
                        ("弃塔(半额)", () => _onAbandon?.Invoke(), Theme.InkSoft, Color.white),
                        ("继续战斗", null, Theme.LockedBg, Theme.TextMain));
                else
                    ShowModal("退出战斗?", "放弃本关:进度不推进,奇遇墨锭保留",
                        ("确认退出", () => _onRunEnded(false), Theme.Cinnabar, Color.white),
                        ("继续战斗", null, Theme.LockedBg, Theme.TextMain));
            }, Theme.ExitPink, Color.white, 15, new Vector2(90, 38));
        }

        private void DrawPlayerStats()
        {
            var hpStack = Ui.VStack(_bottomRow, "Hp", 3);
            // 血值上条(2026-07-25);动画期间画在出手前值,敌人攻击触达才逐记掉血
            _playerHpBar = HpBar(hpStack.transform, Animating ? _animPlayerHp : Battle.PlayerHp,
                PlayerMaxHp, new Vector2(260, 20));
            // 护盾条(2026-07-25):动画期间画出手前值,敌方一记触达才按吸收量降,与血条同步可见。
            // 出手前/结算后任一有盾就占位画条,免动画中途条消失导致布局跳动
            int shownShield = Animating ? _animShield : Battle.PlayerShield;
            _playerShieldBar = (null, null);
            if (shownShield > 0 || (Animating && Battle.PlayerShield > 0))
            {
                var shieldBar = Ui.Bar(hpStack.transform, Mathf.Clamp01(shownShield / ShieldBarFull), Theme.Jade, new Vector2(260, 7));
                _playerShieldBar = ((RectTransform)shieldBar.transform.Find("Fill"),
                    Ui.ThemedLabel(hpStack.transform, $"护盾 {shownShield}", 12, Theme.Jade));
            }
            // 玩家侧状态一行小字(2026-08-06,子项目 A):封字 / 灼烧 / 免疫。
            // 禁用 emoji —— 字体子集补不出来,上线渲染成空框
            var statusRow = Ui.Row(hpStack.transform, "PlayerStatus", 6);
            int seal = Battle.PlayerStatuses.TotalMagnitude(StatusKind.Seal);
            if (seal > 0)
                Ui.Chip(statusRow.transform, $"封字 −{seal}AP", Theme.InkSoft, Color.white, 12);
            int playerBurn = Battle.PlayerStatuses.TotalMagnitude(StatusKind.Burn);
            if (playerBurn > 0)
                Ui.Chip(statusRow.transform, $"灼烧 {playerBurn}", Theme.Cinnabar, Color.white, 12);
            int immunity = Battle.PlayerStatuses.TotalMagnitude(StatusKind.Immunity);
            if (immunity > 0)
                Ui.Chip(statusRow.transform, $"免疫 {immunity}", Theme.Jade, Color.white, 12);

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
        }

        /// <summary>我方前排召唤物(木系):替玩家承伤并反击。独占一排,夹在敌我血条之间
        /// 形成三排对立(2026-07-20 拍板);无召唤物时该排留空,布局不跳动。</summary>
        private void DrawSummons()
        {
            _summonRectByCore.Clear();
            _summonBarByCore.Clear();
            for (int i = 0; i < Battle.Summons.Count; i++)
            {
                var summon = Battle.Summons[i];
                // 动画期间:本回合被打死的召唤物照常画出(玩家看得到它挨打);平时只画存活的(=我方回合开始清理死尸)
                if (!summon.Alive && !(Animating && _summonAnimHp.ContainsKey(i))) continue;
                var cell = Ui.VStack(_summonRow, $"Summon{i}", 1);
                // 保持着色挨打:HP 掉到 0 + 我方回合开始消失来表达阵亡,不在动画里就变灰(免飘字/掉血还没到就先灰)
                var glyph = Ui.RoundButton(cell.transform, summon.Char, null,
                    Theme.ElementSoft(summon.Element), Theme.ElementSoftFg(summon.Element),
                    23, new Vector2(50, 50), 12);
                _summonRectByCore[i] = (RectTransform)glyph.transform;
                // 血值上条(2026-07-25,带描边保对比度);攻力另起一排置于条下。动画期间画出手前值,SummonHit 触达才降
                int shownHp = Animating && _summonAnimHp.TryGetValue(i, out var pre) ? pre : summon.Hp;
                _summonBarByCore[i] = HpBar(cell.transform, shownHp, summon.MaxHp, new Vector2(54, 15));
                Ui.ThemedLabel(cell.transform, $"攻{summon.Attack}", 11, Theme.TextDim);
                if (summon.Shield > 0)
                    Ui.ThemedLabel(cell.transform, $"盾{summon.Shield}", 11, Theme.Jade);
                string passiveTag = SummonPassiveTag(summon.Passive);
                if (passiveTag.Length > 0)
                    Ui.ThemedLabel(cell.transform, passiveTag, 11, Theme.Cinnabar);
            }
        }

        /// <summary>召唤物被动的一行提示,让玩家看得出这只树跟别的树不一样。
        /// 一只召唤物只有一种被动(数据侧如此),所以取第一个非零项即可。
        /// 禁用 emoji —— 字体子集补不出来,上线渲染成空框。</summary>
        private static string SummonPassiveTag(SummonPassive passive)
        {
            if (passive == null) return "";
            if (passive.OnHitBurn > 0)
                return passive.OnHitBurnAll ? $"全场灼{passive.OnHitBurn}" : $"附灼{passive.OnHitBurn}";
            if (passive.Thorns > 0) return $"反伤{passive.Thorns}";
            if (passive.HealAlly > 0) return $"回血{passive.HealAlly}";
            if (passive.OnHitCurse > 0) return $"诅咒{passive.OnHitCurse}%";
            if (passive.Speed > 100) return "疾";
            return "";
        }

        // 敌人格尺寸(2026-07-28 随形象接入放大:圆头像 104 → 形象 150,格 168×208 → 190×220)。
        // 形象底稿四周留了 10% 白,同直径下视觉体积比实心圆头像小,所以要给得更足
        private const float EnemyPortrait = 150f;
        private const float EnemyCellWidth = 190f;
        private const float EnemyCellHeight = 220f;

        private void DrawEnemies()
        {
            _enemyRects.Clear();
            _enemyMobs.Clear();
            _enemyHpBars.Clear();
            for (int i = 0; i < Battle.Enemies.Count; i++)
            {
                var enemy = Battle.Enemies[i];
                int index = i;
                // 死亡动画进行中的怪:重绘时仍保持着色挨打,置灰交给死亡节拍(GreyOut),别在重绘时就变灰
                bool dying = _dyingEnemies.Contains(index);
                bool showAlive = enemy.Alive || dying;

                var cell = Ui.Panel(_enemyRow, $"Enemy{i}");
                var cellElement = cell.AddComponent<LayoutElement>();
                cellElement.preferredWidth = EnemyCellWidth;
                cellElement.preferredHeight = EnemyCellHeight;

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
                    mob.Init(prefix, EnemyPortrait);
                    mob.SetStateAmount(MobAssets.StateAmountFor(enemy)); // L4 绑战斗状态
                    if (!showAlive) mob.ApplyTint(Theme.LockedBg);
                }
                portrait ??= Ui.CircleGlyph(cell.transform,
                    EnemyInfo.FaceChar(enemy.Def, enemy.PhaseIndex),
                    showAlive ? Theme.ElementColor(enemy.ApparentElement) : Theme.LockedBg,
                    Color.white, EnemyPortrait);
                _enemyMobs.Add(mob);
                Ui.Anchor((RectTransform)portrait.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(-EnemyPortrait / 2f, -EnemyPortrait), new Vector2(EnemyPortrait / 2f, 0));
                if (_targeting && enemy.Alive && mob == null)
                {
                    var outline = portrait.AddComponent<Outline>(); // 圆头像用描边示意可选中
                    outline.effectColor = Theme.Ink;
                    outline.effectDistance = new Vector2(3, 3);
                }

                // 点击区盖满整格:形象各层不吃 raycast(见 MobView),没有它整格点不动
                var hitArea = cell.AddComponent<Image>();
                hitArea.color = _targeting && enemy.Alive
                    ? new Color(Theme.Ink.r, Theme.Ink.g, Theme.Ink.b, 0.07f) // 选目标时整格微亮,提示可点
                    : new Color(0, 0, 0, 0);

                var info = Ui.VStack(cell.transform, "Info", 3);
                Ui.Anchor((RectTransform)info.transform, new Vector2(0, 0), new Vector2(1, 1),
                    Vector2.zero, new Vector2(0, -(EnemyPortrait + 2f)));
                Ui.ThemedLabel(info.transform, BossTitle(enemy), 17, Theme.TextMain, Theme.TitleFont);
                var chips = Ui.Row(info.transform, "Chips", 5);
                Ui.Chip(chips.transform, enemy.ApparentElement is { } apparent ? ElementName(apparent) : "?",
                    Theme.ElementColor(enemy.ApparentElement), Color.white, 12);
                Ui.Chip(chips.transform, $"攻 {enemy.Attack}", Theme.PaperDim, Theme.TextMain, 12);
                if (enemy.DamageTaken < 1f) Ui.Chip(chips.transform, "承伤", Theme.InkSoft, Color.white, 12);
                // 读 ChargingSkill 而不是当前阶段的技能:蓄力期间玩家可能把 Boss 推过阶段,
                // 那时阶段技能已经变了,但预告过的大招不改口(2026-07-29)
                if (enemy.IsCharging && enemy.IsBoss)
                    // 别用 emoji:⚡ 不在 Noto Serif SC 里,子集补不出来,上线渲染成空框
                    // (test_subset_fonts_cover_charset 正是拦这个的)。预警靠朱砂底色已经够显眼
                    Ui.Chip(chips.transform, $"蓄力 · 下回合:{EnemyInfo.BossSkillName(enemy.ChargingSkill)}",
                        Theme.Cinnabar, Color.white, 12);
                int burnStacks = enemy.Statuses.TotalMagnitude(StatusKind.Burn);
                if (burnStacks > 0) Ui.Chip(chips.transform, $"灼烧 {burnStacks}", Theme.Cinnabar, Color.white, 12);
                int curse = enemy.Statuses.TotalMagnitude(StatusKind.Curse);
                if (curse > 0)
                    Ui.Chip(chips.transform, $"诅咒 −{curse}%", Theme.InkSoft, Color.white, 12);
                // 能力 chip 统一走 EnemyInfo(与详情弹窗同一套命名);
                // 机制失效(叠字已分裂/通假已现形/生僻已读懂)时返回空串,不画
                if (enemy.Alive)
                {
                    string abilityChip = EnemyInfo.AbilityChipText(enemy);
                    if (abilityChip.Length > 0)
                        Ui.Chip(chips.transform, abilityChip,
                            Theme.AbilityChipColor(enemy.Def.Ability), Color.white, 12);
                }

                // 存活或濒死(死亡动画中)都画血条:动画期间画出手前值,伤害触达才逐记掉血;
                // 濒死者随死亡节拍置灰,真正死透(动画完)才转「已正」。血值上条,带描边保对比度。
                if (showAlive)
                {
                    int barHp = Animating && i < _animEnemyHp.Count ? _animEnemyHp[i] : enemy.Hp;
                    _enemyHpBars.Add(HpBar(info.transform, barHp, enemy.MaxHp, new Vector2(140, 16)));
                }
                else
                {
                    Ui.ThemedLabel(info.transform, "已正", 14, Theme.LockGray);
                    _enemyHpBars.Add((null, null));
                }

                var button = cell.AddComponent<Button>();
                button.targetGraphic = hitArea;
                button.onClick.AddListener(() => OnEnemyClicked(index));
                button.interactable = enemy.Alive;
                _enemyRects.Add((RectTransform)portrait.transform);
            }

        }

        private void DrawLibrary()
        {
            // 奖励页显示携带字库(出过的字已回归)——这才是下一战的真实字库,也是替换的操作对象
            bool rewardPhase = _run.Phase == RunPhase.Reward;
            var library = rewardPhase ? _run.CarriedLibrary : Battle.Library;
            Ui.ThemedLabel(_libraryRow, $"字库 {library.Count}/{Battle.LibraryCapacity}", 16, Theme.TextDim, Theme.TitleFont);
            if (!_run.LibraryExpanded)
                Ui.AdBadge(_libraryRow, "+2", () => // 原型:点击即生效,SDK 后接
                {
                    _run.TryExpandLibrary();
                    _onExpanded?.Invoke();
                    _message = "字库上限 +2(本次登塔有效)";
                    Refresh();
                }, new Vector2(64, 38));
            if (library.Count == 0)
                Ui.ThemedLabel(_libraryRow, "(空)", 16, Theme.TextDim);
            for (int i = 0; i < library.Count; i++)
            {
                int index = i;
                string charId = library[i];
                var def = _graph.Get(charId);
                bool selected = _selectedChar == charId && !_targeting;
                System.Action tap = () =>
                {
                    if (rewardPhase) OnRewardLibraryClicked(charId);
                    else OnLibraryCharClicked(charId);
                };
                var tile = Ui.GlyphTile(_libraryRow, def, $"{def.ApCost} AP", selected, tap,
                    new Vector2(84, 105));
                // AP 不够就去饱和压暗、属性动效停(《字牌形象关键词包》§4.4):
                // 「用不了」要在点下去之前就看得出来,不能等弹窗告诉你
                if (!rewardPhase)
                    tile.GetComponent<CardFrameView>()?.SetPlayable(def.ApCost <= Battle.Ap);
                HoldToPreview.Attach(tile.gameObject, () => ShowCharPreview(charId));
                if (!rewardPhase) AttachDragToAttack(tile.gameObject, def);
                _tileRects[charId] = (RectTransform)tile.transform;
            }
        }

        /// <summary>拖字打人(2026-07-26):拖到敌人身上松手 = 攻击那个敌人。
        /// 水/土 因此在双击的治疗/加盾之外多一个攻击用法;其余字拖放 = 出字并顺手选中目标。</summary>
        private void AttachDragToAttack(GameObject tile, CharDef def)
        {
            DragToAttack.Attach(tile, def.Id, Theme.ElementColor(def.Element),
                () => _run.Phase == RunPhase.InBattle && Battle.Phase == BattlePhase.PlayerTurn && !Animating,
                screenPos =>
                {
                    int target = EnemyIndexAt(screenPos);
                    if (target < 0) { CancelSelection(); return; } // 没落在敌人身上:当作取消,不出字
                    ExecuteCast(def.Id, target, attackMode: true);
                });
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

        /// <summary>消息条简述(2026-07-21):只给 AP 与等级化效果;拼音/释义/配方走长按 preview。</summary>
        private string Brief(string charId)
        {
            var def = _graph.Get(charId);
            return $"「{charId}」{def.ApCost}AP · {CharInfo.EffectsText(def, _run.CardLevel(charId))}";
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
            Ui.ThemedLabel(_poolRow, $"部件池 {poolChars.Count}/{Battle.PoolCapacity}", 16, Theme.TextDim, Theme.TitleFont);
            if (!_run.PoolExpanded)
                Ui.AdBadge(_poolRow, "+2", () => // 原型:点击即生效,SDK 后接
                {
                    _run.TryExpandPool();
                    _onExpanded?.Invoke();
                    _message = "部件池上限 +2(本次登塔有效)";
                    Refresh();
                }, new Vector2(64, 38));
            foreach (var id in poolChars)
            {
                string charId = id;
                var def = _graph.Get(charId);
                bool selected = _selectedChar == charId && !_targeting;
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
                _tileRects[charId] = (RectTransform)tile.transform; // 同名部件取最后一个,动效近似即可
            }
        }

        private void DrawSuggest()
        {
            // 只提示已收集的字:合不出来的不该出现在拆合台(2026-07-19)
            var suggest = ForgeEngine.Suggest(_graph, Battle.Pool, Battle.Library, Battle.UnlockedChars);
            DrawNearMissHints(suggest.NearMisses); // 左侧差字面板:选中与否都显示
            if (_selectedChar != null || _targeting) return; // 选中态:拆合台交给拆字+动作两行
            if (suggest.Composable.Count == 0)
                Ui.ThemedLabel(_suggestRow, "凑齐部件即可合字", 15, Theme.TextDim);
            // 可合成项每行 4 个自动换行(2026-07-19 反馈:过多时横排溢出被配字表遮盖)
            const int CombosPerRow = 4;
            var comboStack = Ui.VStack(_suggestRow, "ComboRows", 4);
            Transform currentRow = null;
            int inRow = CombosPerRow;
            foreach (var id in suggest.Composable)
            {
                if (inRow >= CombosPerRow)
                {
                    currentRow = Ui.Row(comboStack.transform, $"ComboRow{inRow}", 14).transform;
                    inRow = 0;
                }
                inRow++;
                string charId = id;
                var def = _graph.Get(charId);
                var combo = Ui.Row(currentRow, $"Combo_{charId}", 6); // 触控:组间距/主按钮加大
                foreach (var part in def.Recipe)
                {
                    var partDef = _graph.Get(part);
                    Ui.RoundButton(combo.transform, part, null,
                        Theme.ElementColor(partDef.Element), Color.white, 15, new Vector2(36, 36), 8);
                }
                Ui.ThemedLabel(combo.transform, "=", 14, Theme.TextDim);
                // 结果字牌:白底 + 属性色大字,点击即合(2026-07-19 反馈:去「合」字;不加粗,粗体发糊)
                Ui.RoundButton(combo.transform, charId, () => OnCompose(charId),
                    Color.white, Theme.ElementColor(def.Element), 30, new Vector2(60, 54), 12);
            }
        }

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
                string key = element is { } e ? ElementName(e) : "中性";
                if (!buckets.TryGetValue(key, out var list))
                    buckets[key] = list = new System.Collections.Generic.List<NearMiss>();
                list.Add(miss);
            }

            // 一级:属性胶囊竖排(带可合数),点选/再点收起
            Ui.ThemedLabel(_hintColumn, "配字表", 16, Theme.TextDim, Theme.TitleFont);
            foreach (var key in HintBucketOrder)
            {
                if (!buckets.TryGetValue(key, out var list)) continue;
                bool selected = _hintBucket == key;
                var element = ElementByName(key);
                Ui.RoundButton(_hintColumn, $"{key} {list.Count}", () =>
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
                Ui.ThemedLabel(_hintColumn, $"…共 {bucketChars.Count} 字", 13, Theme.TextDim);

            // 三级:差什么
            if (focused is { } target)
            {
                var def = _graph.Get(target.CharId);
                Ui.ThemedLabel(_hintColumn,
                    $"「{target.CharId}」= {string.Join("+", def.Recipe)},差「{target.MissingIngredient}」",
                    14, Theme.TextMain);
            }
        }

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
            if (_selectedChar == null) return;
            var def = _graph.Get(_selectedChar);

            // 第一行(拆字):选中字 → 部件拆解
            Ui.RoundButton(_suggestRow, def.Id, null, Theme.Ink, Color.white, 22, new Vector2(52, 52), 12);
            if (!def.IsLeaf)
            {
                Ui.ThemedLabel(_suggestRow, "→", 16, Theme.TextDim);
                foreach (var part in def.Recipe)
                    Ui.RoundButton(_suggestRow, part, null,
                        Theme.ElementColor(_graph.Get(part).Element), Color.white, 16, new Vector2(38, 38), 8);
            }
            else
            {
                Ui.ThemedLabel(_suggestRow, "(独体字,不可拆)", 14, Theme.TextDim);
            }

            // 第二行(动作)
            if (_targeting)
            {
                Ui.ThemedLabel(_actionRow, $"「{_selectedChar}」点击目标敌人", 16, Theme.TextMain);
                Ui.RoundButton(_actionRow, "取消", CancelSelection, Theme.LockedBg, Theme.TextMain, 16, new Vector2(88, 50));
                return;
            }
            bool inLibrary = System.Linq.Enumerable.Contains(Battle.Library, _selectedChar);
            string castLabel = def.Effects.Count > 0 ? (inLibrary ? "出字" : "直出") : "兜底一击";
            // 动作按钮 ≥50 高(2026-07-19 iOS 反馈:手指可点性)
            Ui.RoundButton(_actionRow, castLabel, () => OnCastPressed(def), Theme.Cinnabar, Color.white, 17, new Vector2(110, 52));
            if (inLibrary && !def.IsLeaf)
                Ui.RoundButton(_actionRow, "拆", () => OnDismantle(def.Id), Theme.SplitBlue, Color.white, 17, new Vector2(76, 52));
            Ui.RoundButton(_actionRow, "丢弃", () => OnDiscard(def.Id), Theme.ExitPink, Color.white, 17, new Vector2(88, 52));
            Ui.RoundButton(_actionRow, "取消", CancelSelection, Theme.LockedBg, Theme.TextMain, 17, new Vector2(84, 52));
        }

        private void DrawEndTurn()
        {
            Ui.PillButton(_endTurnRow, "结束回合", ConfirmEndTurn, Theme.Cinnabar, Color.white, 21, new Vector2(190, 52));
        }

        /// <summary>回合掉字遇满库(2026-08-04):停下让玩家选替换哪一张,或跳过这次掉落。
        /// 结构照搬 DrawEventReplaceStep —— 同一个「满库换哪张」的心智模型。</summary>
        private void DrawDropChoiceStep()
        {
            string incoming = Battle.PendingDrop;

            if (_modal != null) Object.Destroy(_modal);
            _modal = Ui.ModalShell(transform, $"字库已满 · 用掉落的「{incoming}」换掉哪一张?",
                new Vector2(360, 240), dismissable: false, out var stack);
            Ui.ThemedLabel(stack, "被换掉的字永久失去", 15, Theme.TextDim);

            Transform row = null;
            for (int i = 0; i < Battle.Library.Count; i++)
            {
                if (i % 4 == 0) row = Ui.Row(stack, $"Row{i / 4}", 8).transform;
                int replaceIndex = i;
                var def = _graph.Get(Battle.Library[i]);
                Ui.GlyphTile(row, def, $"{def.ApCost} AP", false, () =>
                {
                    string dropped = Battle.Library[replaceIndex];
                    if (Battle.ResolveDrop(replaceIndex) == BattleError.None)
                    {
                        _message = $"「{incoming}」替换「{dropped}」";
                        if (_modal != null) Object.Destroy(_modal);
                    }
                    Refresh();
                }, new Vector2(74, 96));
            }

            Ui.PillButton(stack, "不要,跳过", () =>
            {
                Battle.SkipDrop();
                if (_modal != null) Object.Destroy(_modal);
                _message = $"弃掉了「{incoming}」";
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
            ShowModal("还有 AP 没用",
                $"本回合还剩 {Battle.Ap} AP,结束后作废。\n确定结束回合?",
                ("结束回合", OnEndTurn, Theme.Cinnabar, Color.white),
                ("再想想", null, Theme.LockedBg, Theme.TextMain));
        }

        private void DrawBattleSettle()
        {
            if (Battle.Phase == BattlePhase.Won)
            {
                ShowVictoryBanner(); // 过关提示走屏幕中央横幅,自动推进(2026-07-21)
                return;
            }
            Ui.ThemedLabel(_actionRow, "败北……", 36, Theme.TextMain, Theme.TitleFont);
            // 无尽塔:整次登塔一次广告复活——满血续战 + 补给,让空手也有再战之力(2026-07-24)
            if (_onExit != null && _run.ReviveAvailable)
                Ui.AdBadge(_actionRow, "看广告复活", () =>
                {
                    _previewRewardIndex = -1;
                    _run.TryRevive();
                    _onExpanded?.Invoke(); // 即时落盘:防「刚看完广告就挂起」白看
                    _message = "满血复活!挑几样补给,接着打";
                    Refresh();
                }, new Vector2(160, 60));
            Ui.PillButton(_actionRow, "结算", AdvanceAfterSettle,
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
            var label = Ui.ThemedLabel(banner.transform, boss ? "B O S S  已 破" : "本 层 告 捷",
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

            if (_pendingRewardIndex >= 0)
                DrawRewardReplaceStep();
            else
                DrawRewardCharStep();
        }

        private void DrawRewardCharStep()
        {
            _rewardModal = Ui.ModalShell(transform, $"战利品 · 选字(还剩 {_run.CharPicksLeft})",
                new Vector2(340, 165), dismissable: false, out var content);
            var preview = _previewRewardIndex >= 0
                ? Brief(_run.RewardOptions[_previewRewardIndex]) + "|再点一次收下"
                : $"字库 {_run.CarriedLibrary.Count}/{Battle.LibraryCapacity} · 点一下看效果,再点收下";
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
                        _message = $"「{id}」入库";
                        CancelSelection(); // 额度归零 → 下次 Refresh 由 Core 侧自动开拔
                        return;
                    }
                    _pendingRewardIndex = index; // 字库已满(3.8.1):转入替换子步
                    Refresh();
                };
                var tile = Ui.GlyphTile(row.transform, def, $"{def.ApCost} AP",
                    index == _previewRewardIndex, tap);
                HoldToPreview.Attach(tile.gameObject, () => ShowCharPreview(id));
            }

            Ui.RoundButton(content, "不要了,开拔", () =>
            {
                _previewRewardIndex = -1;
                _run.SkipReward();
                _tutorial?.Notify(TutorialAction.PickReward); // 跳过也算完成节拍,引导不卡死
                _message = "开拔,下一战!";
                CancelSelection();
            }, Theme.LockedBg, Theme.TextMain, 17, new Vector2(190, 46));
        }

        private void DrawRewardReplaceStep()
        {
            var incoming = _run.RewardOptions[_pendingRewardIndex];
            _rewardModal = Ui.ModalShell(transform,
                $"字库已满 · 用「{incoming}」换掉哪一个?",
                new Vector2(360, 165), dismissable: false, out var content);
            Ui.ThemedLabel(content,
                $"字库 {_run.CarriedLibrary.Count}/{Battle.LibraryCapacity}——被换掉的字永久失去", 16, Theme.TextDim);

            var row = Ui.Row(content, "Library", 8);
            for (int i = 0; i < _run.CarriedLibrary.Count; i++)
            {
                int replaceIndex = i;
                var def = _graph.Get(_run.CarriedLibrary[i]);
                Ui.GlyphTile(row.transform, def, $"{def.ApCost} AP", false, () =>
                {
                    string dropped = _run.CarriedLibrary[replaceIndex];
                    if (_run.PickRewardReplacing(_pendingRewardIndex, replaceIndex))
                    {
                        _pendingRewardIndex = -1;
                        _tutorial?.Notify(TutorialAction.PickReward);
                        _message = $"「{incoming}」替换「{dropped}」入库";
                        CancelSelection();
                    }
                }, new Vector2(74, 96));
            }

            Ui.RoundButton(content, "算了,不换", () =>
            {
                _pendingRewardIndex = -1;
                Refresh();
            }, Theme.LockedBg, Theme.TextMain, 17, new Vector2(150, 46));
        }

        // ---- 复活补给(2026-07-24):以战利品展示方式给字,直接注入当前战斗字库。
        // 2026-08-04:部件补给随 Core 一并删除——五行部件今后只能靠拆字获得;
        // 满库转入替换子步(看了广告不该因满库一无所得),额度尽/候选枯竭才由收尾检查 SkipReviveReward ----

        private int _pendingReviveIndex = -1; // 满库待替换:已选中的候选字下标(-1 = 未进替换子步)

        private void DrawReviveCharStep()
        {
            if (_pendingReviveIndex >= 0) { DrawReviveReplaceStep(); return; }
            if (_rewardModal != null) Destroy(_rewardModal);

            _rewardModal = Ui.ModalShell(transform, $"复活补给 · 选字(还剩 {_run.ReviveCharPicksLeft})",
                new Vector2(340, 165), dismissable: false, out var content);
            Ui.ThemedLabel(content, _previewRewardIndex >= 0
                ? Brief(_run.RewardOptions[_previewRewardIndex]) + "|再点一次收下"
                : $"字库 {Battle.Library.Count}/{Battle.LibraryCapacity} · 点一下看效果,再点收下", 16, Theme.TextDim);

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
                        _message = $"「{id}」入库";
                    Refresh();
                };
                var tile = Ui.GlyphTile(row.transform, def, $"{def.ApCost} AP", index == _previewRewardIndex, tap);
                HoldToPreview.Attach(tile.gameObject, () => ShowCharPreview(id));
            }

            Ui.RoundButton(content, "够了,接着打!", () =>
            {
                _previewRewardIndex = -1;
                _run.SkipReviveReward();
                _message = "重整旗鼓,再战!";
                CancelSelection();
            }, Theme.LockedBg, Theme.TextMain, 17, new Vector2(190, 46));
        }

        /// <summary>复活补给满库替换(2026-08-04):结构同 DrawDropChoiceStep,
        /// 区别是换进来的字来自补给候选而非回合掉落。</summary>
        private void DrawReviveReplaceStep()
        {
            string incoming = _run.RewardOptions[_pendingReviveIndex];

            if (_rewardModal != null) Destroy(_rewardModal);
            _rewardModal = Ui.ModalShell(transform, $"字库已满 · 用补给的「{incoming}」换掉哪一张?",
                new Vector2(360, 240), dismissable: false, out var stack);
            Ui.ThemedLabel(stack, "被换掉的字永久失去", 15, Theme.TextDim);

            Transform row = null;
            for (int i = 0; i < Battle.Library.Count; i++)
            {
                if (i % 4 == 0) row = Ui.Row(stack, $"Row{i / 4}", 8).transform;
                int replaceIndex = i;
                var def = _graph.Get(Battle.Library[i]);
                Ui.GlyphTile(row, def, $"{def.ApCost} AP", false, () =>
                {
                    string dropped = Battle.Library[replaceIndex];
                    if (_run.PickReviveCharReplacing(_pendingReviveIndex, replaceIndex))
                        _message = $"「{incoming}」替换「{dropped}」";
                    _pendingReviveIndex = -1;
                    Refresh();
                }, new Vector2(74, 96));
            }

            Ui.PillButton(stack, "算了,换个字", () =>
            {
                _pendingReviveIndex = -1; // 退回候选列表,额度未动
                Refresh();
            }, Theme.LockedBg, Theme.TextMain, 16, new Vector2(150, 46));
        }

        private int _pendingEventOption = -1; // 部件抵价/任选字:待成交的选项下标
        private int _pendingCharChoice = -1;  // 任选字:已选中的字下标(-1 = 未选)
        private readonly System.Collections.Generic.List<int> _eventPicks = new(); // 已点选的池下标

        private void DrawEvent() // 奇遇(9.6):短情境 + 选择;部件抵价/任选字由玩家点选(2026-07-19)
        {
            var evt = _run.CurrentEvent;
            Ui.ThemedLabel(_enemyRow, $"奇遇 · {evt.Id}", 30, Theme.TextMain, Theme.TitleFont);
            Ui.ThemedLabel(_statusRow, $"{evt.Text}    (墨锭 {_run.AvailableInk})", 18, Theme.TextDim);

            if (_pendingEventOption >= 0)
            {
                var pending = evt.Options[_pendingEventOption];
                if (_eventReplacing) // 字与部件都备齐,只差「换掉哪一张」(2026-07-22)
                {
                    DrawEventReplaceStep(pending);
                    return;
                }
                bool needCharChoice = pending.GainCharChoices.Count > 0 && _pendingCharChoice < 0;
                Ui.ThemedLabel(_actionRow, needCharChoice
                        ? $"{pending.Label}:先点想要的字"
                        : $"{pending.Label}:点 {pending.ComponentCost} 个不要的部件({_eventPicks.Count}/{pending.ComponentCost})",
                    20, Theme.TextMain, Theme.TitleFont);
                Ui.RoundButton(_actionRow, "取消", () =>
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

            for (int i = 0; i < evt.Options.Count; i++)
            {
                int index = i;
                var option = evt.Options[i];
                bool affordable = option.InkCost <= _run.AvailableInk
                    && option.ComponentCost <= _run.CarriedPool.Count
                    && AnyGainable(option); // 给的字都不在出阵列表 → 整个选项置灰(2026-07-20)
                var button = Ui.RoundButton(_actionRow, option.Label, () =>
                {
                    if (option.ComponentCost > 0 || option.GainCharChoices.Count > 0)
                    {
                        _pendingEventOption = index; // 进入选件/选字模式
                        _pendingCharChoice = -1;
                        _eventPicks.Clear();
                        _message = option.GainCharChoices.Count > 0
                            ? "先点想要的字"
                            : $"以物易物:点 {option.ComponentCost} 个不要的部件抵价";
                        Refresh();
                        return;
                    }
                    int inkBefore = _run.AvailableInk;
                    if (_run.ChooseEventOption(index))
                    {
                        _message = option.InkChancePercent > 0 // 赌注:按墨锭变化播报输赢
                            ? (_run.AvailableInk > inkBefore ? $"手气极佳!+{option.Ink} 墨锭" : "输了……愿赌服输")
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
                        _message = $"字库已满:选一个换成「{option.GainChar}」";
                        Refresh();
                        return;
                    }
                    CancelSelection();
                    ShowAlert("这个选不了", option.InkCost > _run.AvailableInk
                        ? $"「{option.Label}」需要 {option.InkCost} 墨锭,你只有 {_run.AvailableInk}。"
                        : $"「{option.Label}」这笔交易没能成立。");
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
            _modal = Ui.ModalShell(transform, $"字库已满 · 用「{incoming}」换掉哪一张?",
                new Vector2(360, 240), dismissable: false, out var stack);
            Ui.ThemedLabel(stack, "被换掉的字永久失去", 15, Theme.TextDim);

            Transform row = null;
            for (int i = 0; i < _run.CarriedLibrary.Count; i++)
            {
                if (i % 4 == 0) row = Ui.Row(stack, $"Row{i / 4}", 8).transform;
                int replaceIndex = i;
                var def = _graph.Get(_run.CarriedLibrary[i]);
                Ui.GlyphTile(row, def, $"{def.ApCost} AP", false, () =>
                {
                    string dropped = _run.CarriedLibrary[replaceIndex];
                    var picks = _eventPicks.Count > 0 ? _eventPicks.ToArray() : null;
                    if (_run.ChooseEventOption(_pendingEventOption, picks, _pendingCharChoice, replaceIndex))
                    {
                        _message = $"成交!「{incoming}」替换「{dropped}」";
                        if (_modal != null) Object.Destroy(_modal);
                        ResetEventSelection();
                        CancelSelection();
                        return;
                    }
                    Refresh();
                }, new Vector2(74, 96));
            }

            Ui.PillButton(stack, "算了,不换", () =>
            {
                if (_modal != null) Object.Destroy(_modal);
                ResetEventSelection();
                _message = "交易取消,部件一个没少";
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
                        _message = $"要「{charId}」:点 {option.ComponentCost} 个不要的部件抵价";
                        Refresh();
                        return;
                    }
                    if (_run.ChooseEventOption(_pendingEventOption, null, choice))
                    {
                        _message = $"成交!得「{charId}」";
                        ResetEventSelection();
                        CancelSelection();
                        return;
                    }
                    if (_run.CarriedLibrary.Count >= Battle.LibraryCapacity)
                    {
                        _eventReplacing = true; // 满库不再是死路:转入「换掉哪一张」
                        _message = $"字库已满:选一个换成「{charId}」";
                        Refresh();
                        return;
                    }
                    ResetEventSelection();
                    CancelSelection();
                    ShowAlert("换不了", $"「{charId}」这笔交易没能成立。");
                }, Theme.ElementSoft(def.Element), Theme.ElementSoftFg(def.Element),
                    26, new Vector2(64, 64), 12);
            }
        }

        /// <summary>抵价选件:携带池平铺,点选高亮,凑够数自动成交。</summary>
        private void DrawEventPoolPicker(EventOption option)
        {
            Ui.ThemedLabel(_poolRow, "部件池", 16, Theme.TextDim, Theme.TitleFont);
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
                            _message = gained != null ? $"成交!得「{gained}」" : $"成交!{option.Label}";
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
            Ui.ThemedLabel(_enemyRow, "部件已满", 30, Theme.TextMain, Theme.TitleFont);
            Ui.ThemedLabel(_statusRow,
                $"用「{incoming}」换掉池中一个(永久失去),或跳过不要。还剩 {overflow.Count} 个待决。",
                18, Theme.TextDim);

            Ui.PillButton(_actionRow, $"跳过「{incoming}」", () =>
            {
                _run.ResolveOverflowSkip();
                _message = $"弃「{incoming}」";
                Refresh();
            }, Theme.LockedBg, Theme.TextMain, 18, new Vector2(160, 56));

            Ui.ThemedLabel(_poolRow, "部件池(点一个换掉)", 16, Theme.TextDim, Theme.TitleFont);
            for (int i = 0; i < _run.CarriedPool.Count; i++)
            {
                int index = i;
                var def = _graph.Get(_run.CarriedPool[i]);
                Ui.RoundButton(_poolRow, def.Id, () =>
                {
                    string dropped = _run.CarriedPool[index];
                    _run.ResolveOverflowReplace(index);
                    _message = $"「{incoming}」换掉「{dropped}」";
                    Refresh();
                }, Theme.ElementSoft(def.Element), Theme.ElementSoftFg(def.Element),
                    22, new Vector2(56, 56), 12);
            }
        }

        private void DrawRunEnd()
        {
            bool won = _run.Phase == RunPhase.RunWon;
            bool tower = _onExit != null; // 无尽:胜=Boss 层告捷进安全层,负=塔结算
            Ui.ThemedLabel(_actionRow, won ? (tower ? "本段告捷——字正!" : "关卡通过——字正!") : "败北",
                40, Theme.TextMain, Theme.TitleFont);
            Ui.PillButton(_actionRow, won && tower ? "前往安全层" : tower ? "结算" : "返回地图",
                () => _onRunEnded(won), Theme.Jade, Color.white, 26, new Vector2(190, 70));
            _message = won
                ? (tower ? "Boss 已破,安全层可收官或深入。" : "通关结算:经验与墨锭入账。")
                : (tower ? "卒……墨锭半额结算,纪录保留。" : "死亡即结算,回地图重整旗鼓。");
        }

        // ---- 交互 ----

        private void OnLibraryCharClicked(string charId)
        {
            if (_selectedChar == charId && !_targeting)
            {
                OnCastPressed(_graph.Get(charId)); // 再点一次选中字 = 直接出字
                return;
            }
            _selectedChar = charId;
            _targeting = false;
            _message = Brief(charId) + "|再点即出";
            Refresh();
        }

        private void OnPoolCharClicked(string charId)
        {
            if (_selectedChar == charId && !_targeting)
            {
                OnCastPressed(_graph.Get(charId)); // 再点一次选中部件 = 直出
                return;
            }
            _selectedChar = charId;
            _targeting = false;
            _message = Brief(charId) + "|直出:部件不入库直接打出|再点即出";
            Refresh();
        }

        private void OnCastPressed(CharDef def)
        {
            if (BattleEngine.NeedsTarget(def) && AliveEnemyCount() > 1)
            {
                _targeting = true;
                _message = $"「{def.Id}」:点击目标敌人";
                Refresh();
                return;
            }
            ExecuteCast(def.Id, -1); // 单敌免选:引擎自动锁定唯一存活目标
        }

        private int AliveEnemyCount()
        {
            int count = 0;
            foreach (var enemy in Battle.Enemies)
                if (enemy.Alive) count++;
            return count;
        }

        private void OnEnemyClicked(int index)
        {
            if (_targeting && _selectedChar != null)
            {
                ExecuteCast(_selectedChar, index);
                return;
            }
            // 非选目标态点怪 = 看详情(2026-07-22);此前这里什么也不做
            if (_modal != null) Object.Destroy(_modal);
            _modal = EnemyPreview.Show(transform, Battle.Enemies[index].Def, phase: Battle.Enemies[index].PhaseIndex);
        }

        private void ExecuteCast(string charId, int target, bool replaceSummon = false, bool attackMode = false)
        {
            bool hasFrom = TryGetTilePos(charId, out var fromPos); // 起点须在重绘销毁字牌前捕获
            SnapshotPreHp(); // 出手前血量:动画期间血条画在此值,伤害触达才逐记掉血
            var error = Battle.Cast(charId, target, replaceSummon, attackMode);
            if (error == BattleError.SummonCapFull) // 前排满员强阻断:AP/字都没动,确认替换才重出
            {
                var def = _graph.Get(charId);
                int replaceCount = Battle.SummonReplaceCountOf(def, attackMode); // 空位不够的那部分才顶人
                ShowModal("前排放不下",
                    $"前排 {Battle.AliveSummonCount}/{Battle.SummonCapacity},「{charId}」召 {Battle.SummonCountOf(def, attackMode)} 只。\n"
                    + $"将从最前起顶掉 {replaceCount} 只。",
                    ($"替换最前 {replaceCount} 只",
                        () => ExecuteCast(charId, target, replaceSummon: true, attackMode), Theme.Cinnabar, Color.white),
                    ("取消", null, Theme.LockedBg, Theme.TextMain));
                _message = "前排已满,出字待确认";
                CancelSelection();
                return;
            }
            if (error == BattleError.None)
                _tutorial?.Notify(TutorialAction.Cast, charId);
            else
                MaybeModalError(error, charId, _graph.Get(charId).ApCost);
            _message = error == BattleError.None ? $"出「{charId}」!" : Describe(error);
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

        /// <summary>召唤替换(Summon 事件带被顶替槽位):抹掉该槽的出手前血量快照,
        /// 让动画期间就画新召唤物的满血,不残留旧值(旧值配新上限会显示成半血)。</summary>
        private void DropReplacedSummonSnapshots()
        {
            foreach (var e in Battle.LastEvents)
                if (e.Kind == BattleEventKind.Summon && e.SecondIndex >= 0)
                    _summonAnimHp.Remove(e.SecondIndex);
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
            _messageLabel.text = "AP 耗尽,自动结束回合 · " + _message;
        }

        private void AppendBossPhaseMessage()
        {
            foreach (var e in Battle.LastEvents)
                if (e.Kind == BattleEventKind.BossPhase)
                {
                    var enemy = Battle.Enemies[e.TargetIndex];
                    _message += $"  破阶!「{enemy.Def.Phases[e.Amount].Char}」现身——{ElementName(enemy.Element)}系";
                }
        }

        private void AppendBossSkillMessage()
        {
            foreach (var e in Battle.LastEvents)
            {
                if (e.Kind == BattleEventKind.BossCharging)
                    _message += $"  蓄力中——下回合「{EnemyInfo.BossSkillName((BossSkill)e.Amount)}」";
                else if (e.Kind == BattleEventKind.BossSkillCast)
                    _message += $"  {EnemyInfo.BossSkillName((BossSkill)e.Amount)}!";
                else if (e.Kind == BattleEventKind.ShieldBroken)
                    _message += $"  护盾被掀空({e.Amount})";
            }
        }

        private void OnDiscard(string charId)
        {
            var error = Battle.Discard(charId);
            _message = error == BattleError.None ? $"丢弃「{charId}」(免 AP)" : Describe(error);
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
            _message = error == BattleError.None ? $"拆「{charId}」" : Describe(error);
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
            _message = error == BattleError.None ? $"合出「{charId}」!" : Describe(error);
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
            SnapshotPreHp(); // 出手前血量:敌方攻击触达才逐记扣血
            Battle.EndTurn();
            _tutorial?.Notify(TutorialAction.EndTurn);
            _message = Battle.Phase == BattlePhase.PlayerTurn ? $"回合 {Battle.Turn}:+{Battle.ApPerTurn} AP,字掉落" : "";
            AppendBossSkillMessage(); // 蓄力/释放/护盾被掀空都发生在敌方回合结算(EndTurn),不是出字动作里
            var deaths = DeathsThisAction();
            _dyingEnemies.UnionWith(deaths); // 登记须在 CancelSelection 重绘前:重绘据此保持死怪着色
            BeginAnim(); // 锁输入:召唤/敌方行动期间不许出字,须在重绘前置位
            CancelSelection();
            PlayAnimated(Battle.LastEvents, deaths);
        }

        private void CancelSelection()
        {
            _selectedChar = null;
            _targeting = false;
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
                ShowModal("AP 不够",
                    $"「{charId}」需要 {neededAp} AP,本回合仅剩 {Battle.Ap} AP。\n结束回合可回满 {Battle.ApPerTurn} AP 并掉落新部件。",
                    ("结束回合", OnEndTurn, Theme.Cinnabar, Color.white),
                    ("再想想", null, Theme.LockedBg, Theme.TextMain));
            else if (error == BattleError.ForgeFailed)
                ShowModal("操作被拒", Describe(error),
                    ("知道了", null, Theme.LockedBg, Theme.TextMain));
        }

        private string Describe(BattleError error) => error switch
        {
            BattleError.NotEnoughAp => "AP 不足",
            BattleError.NotCastable => "此字当前不可出",
            BattleError.InvalidTarget => "目标无效",
            BattleError.BattleOver => "战斗已结束",
            BattleError.ForgeFailed => Battle.LastForgeError switch
            {
                ForgeError.PoolWouldOverflow => "部件池放不下,拆解取消",
                ForgeError.MissingIngredients => "原料不足",
                ForgeError.LibraryFull => "字库已满",
                ForgeError.NotUnlocked => "此字不在出阵列表——登塔前在收集页编入才能合",
                ForgeError.NotDismantlable => "独体字不可拆",
                _ => "操作被拒",
            },
            _ => "",
        };

        private static string ElementName(Element element) => element switch
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
