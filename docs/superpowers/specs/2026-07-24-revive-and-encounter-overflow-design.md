# 设计:广告复活 + 奇遇部件溢出替换

日期:2026-07-24
范围:两个独立小改动,合并一份 spec。均属局内(无尽塔)流程。

---

## A. Bug 修复 — 奇遇获得部件超上限时可替换/跳过

### 现状与病灶

`RunEngine.ChooseEventOption`(RunEngine.cs:174–179)在把 `GainComponents` 与
`RandomComponents` 入池时,遇 `_carriedPool.Count >= PoolCapacity` **静默丢弃**
(注释「池满则不入」)。而字库满其实**早已**有替换流(`_eventReplacing` /
`replaceLibraryIndex` / `DrawEventReplaceStep`)。本质:部件没享受和字同等的替换待遇。

### 目标行为

奇遇某选项给的部件会让池超上限时:能装下的先装,**每个溢出项**让玩家二选一:
- **替换**:换掉池中指定一个(被换的永久移除);
- **跳过**:丢弃该溢出项。

用户例:已有 10、上限 12、获得 3 → 先装 2 满池,第 3 个作为溢出项待决议。

### 关键约束(为何不是一行能改完)

- **`RandomComponents` 必须只掷一次并暂存**。字库溢出能套「返回 false → UI 补
  `replaceLibraryIndex` → 重调」是因为**字是确定的**;随机部件重调就**重掷**,UI 每次
  Refresh 会抖、破种子。所以组件溢出走 **stash-pending**:掷定 → 暂存待决议,不重掷。
- 溢出量在 **`ComponentCost` 抵价扣除之后**算(先扣先腾位)。
- 现有数据里没有任何选项同时给「会溢出的字」和「会溢出的部件」(已核对
  enemies.json events),故**字库溢出流与部件溢出流各自独立**,不建组合路径。

### Core 设计(RunEngine)

新增阶段 `RunPhase.EventOverflow`。`ChooseEventOption` 内,处理完确定性后果
(ink/hp/char/`ComponentCost` 扣除)后处理组件:

1. 组装 incoming = `GainComponents`(原序)+ 掷定的 `RandomComponents`(掷序)。
2. 依次填满空位(`_carriedPool.Count < PoolCapacity` 才 Add),填不下的进
   `_pendingOverflow`(List<string>)。
3. 若 `_pendingOverflow` 非空 → `Phase = EventOverflow`,`CurrentEvent` 置 null,返回 true
   (选项已成交,等溢出决议);否则照旧 `BeginNextBattle()`,返回 true。

新增(EventOverflow 阶段):
- `IReadOnlyList<string> PendingOverflow`(暴露给 UI 展示待决议项,队首为当前项);
- `bool ResolveOverflowReplace(int poolIndex)`:`_carriedPool[poolIndex]` = 队首溢出项,
  弹出队首;队空 → `BeginNextBattle()`;
- `void ResolveOverflowSkip()`:丢弃队首;队空 → `BeginNextBattle()`。

一次处理一项(队列),契合「每个溢出项:可替换或跳过」。

### Presentation(BattleView)

- Phase 分发新增 `case RunPhase.EventOverflow:` → `DrawEventOverflowStep()`。
- `DrawEventOverflowStep`:显示当前溢出部件 + 池内各件(点某件 = 替换它 →
  `ResolveOverflowReplace(i)`)+ 一个「跳过此件」钮(→ `ResolveOverflowSkip()`)。
  镜像 `DrawEventReplaceStep` 的版式与色。每次决议后 Refresh。

### 测试(EventTests,先写失败再实现)

- 固定种子:池填到 `PoolCapacity-1`,选一个给 3 个部件的奇遇 → Phase == EventOverflow,
  `PendingOverflow.Count` == 预期溢出数。
- `ResolveOverflowReplace(k)` → 指定位被换成溢出项,池长度 == 上限,队列前进/结束推进战斗。
- `ResolveOverflowSkip()` → 溢出项丢弃,池长度不变,推进战斗。
- 不溢出的老路径不变(回归):池未满时直接入池、进下一战。

---

## B. 增强 — 死亡看广告复活一次(续战 + 补给)

### 语义(已与用户确认)

死亡可看广告复活,**整次登塔一次**。复活后:HP 回满,**接着打这一场**(敌人血量不变)。
因为复活时手里可能已无字无部件,单纯满血没有战斗力 —— 故复活时以**战利品的展示方式**
给一份**补给**注入当前战斗,让玩家有再战之力。Boss 层也可复活。

**数值定死**(用户给的是范围 1–2 字 / 1–3 部件,此处取上限,复活即「重整旗鼓」):
补给 = **2 次选字 + 3 次选部件**。候选复用胜利战利品的生成
(`RollRewardOptions` 字候选 + `ComponentRewardChoices` 五行部件)。

### 不可破的不变量

复活补给的 pick **必须写进 live battle 的 `_forge`(当前战斗的字库/部件池),
不是 `_carriedLibrary/_carriedPool`** —— 后者是「胜后快照」,复活时尚未捕获,写错目标
玩家就拿不到东西。故复活补给与普通战利品**分道**:`PickReward` 不能原样复用其 sink。

### 不做(YAGNI,明确收窄)

复活补给**不做满库/满池替换 UI**。复活的前提就是「手里空了」,库/池近乎为零,溢出几乎
不可能;grant 方法**守住别超上限**(不静默破不变量)即可,不为这个边缘态再搭替换子步。

### Core 设计

**BattleEngine**(战斗层,最小面):
- `void Revive()`:仅当 `Phase == Lost` 生效 → `PlayerHp = _config.PlayerMaxHp`;
  `Phase = PlayerTurn`;调 `StartTurn()` 刷 AP。
  *已核对 `StartTurn()`(BattleEngine.cs:367):只 +Turn / 刷 AP / 部件掉落,**无对玩家的
  DoT、无敌方起手**,故复活瞬间不会被二次归零。*
- `bool GrantLibraryChar(string charId)`:池/库满则返回 false 不入;否则重建 `_forge`
  (仿 `Cast` 写法)把 char 加入 Library。守 `LibraryCapacity`。
- `bool GrantPoolComponent(string componentId)`:同理加入 Pool,守 `PoolCapacity`。

**RunEngine**(编排):
- 新增阶段 `RunPhase.Reviving`;新增 `bool Revived { get; private set; }`。
- `bool ReviveAvailable`:`Battle.Phase == Lost && !Revived`。
- `bool TryRevive()`:守 `ReviveAvailable` → `Revived = true`;`Battle.Revive()`;
  复用 `RollRewardOptions()` 填字候选、`_componentOptions` 填 `ComponentRewardChoices`;
  `ReviveCharPicksLeft = 2`;`ReviveComponentPicksLeft = 3`;`Phase = Reviving`;返回 true。
- `bool PickReviveChar(int index)`:写 `Battle.GrantLibraryChar(...)`,成功才减额度、移除候选;
  额度尽或候选尽 → 检查收尾。
- `bool PickReviveComponent(int index)`:写 `Battle.GrantPoolComponent(...)`,同上。
- `void SkipReviveReward()`:直接收尾。
- 收尾:两排额度都尽(或跳过)→ `Phase = InBattle`;战斗已是 `PlayerTurn`,玩家接着打。
- `void MarkRevived()`:断点续爬恢复用(见持久化),仅置 `Revived = true`。

*注:复活补给复用 `RollRewardOptions`/`_rewardOptions`/`_componentOptions` 的**候选池**,
但 pick 的 sink 是 `Battle`,与 `PickReward`(sink=`_carried*`)互不干扰。额度用独立字段
`ReviveCharPicksLeft`/`ReviveComponentPicksLeft`,不复用 `CharPicksLeft`。*

### 持久化(两处都要,镜像广告扩容先例)

- `EndlessSaveState` 新增 `bool Revived`(Endless.cs,紧邻 `LibraryExpanded`)。
- 授予即时落盘:`TryRevive()` 后由表现层调 `_onExpanded`,`OnExpanded`
  (GameRoot.cs:213)加写 `snapshot.Revived = run.Revived`。防「刚看完广告就挂起」白看。
- 断点续爬恢复:GameRoot 建 run 后(挨着 `if (snapshot.LibraryExpanded) run.TryExpandLibrary()`,
  GameRoot.cs:187)加 `if (snapshot.Revived) run.MarkRevived();`,防重进本层二次复活。
  *(注:复活发生在层内;中途挂起重进会从层首快照重打,补给字/部件随之丢失——与既有
  「层内挂起丢层内进度」一致;唯一必须持久的是 `Revived` 标志,不能二次复活。)*

### Presentation(BattleView)

- `DrawBattleSettle`(BattleView.cs:678,`Battle.Phase == Lost` 分支):当
  `_onExit != null`(无尽塔)且 `_run.ReviveAvailable` 时,「结算」旁加一个
  `Ui.AdBadge`「看广告复活」钮 → `_run.TryRevive(); _onExpanded?.Invoke(); Refresh();`。
  非塔或已复活过则不显示,行为不变。
- Phase 分发新增 `case RunPhase.Reviving:` → `DrawReviveReward()`(仿 `DrawReward` 版式:
  字候选一排 + 部件候选一排 + 剩余额度 + 「够了,开打」跳过钮)。pick 调
  `PickReviveChar/PickReviveComponent`,跳过调 `SkipReviveReward`。收尾后回 InBattle,
  战斗界面照常渲染,玩家满血接着打。

### 测试

**BattleEngineTests**(Core,先写失败再实现):
- 造一个必死局打到 `Lost` → `Revive()` → `PlayerHp == MaxHp`、`Phase == PlayerTurn`、`Ap` 满。
- `Revive()` 在非 Lost 态无效(幂等守卫)。
- `GrantLibraryChar` 满库返回 false 不入;未满则 Library 含新字且不超 `LibraryCapacity`。
- `GrantPoolComponent` 同理守 `PoolCapacity`。

**RunEngineTests**(Core):
- 打到 `RunLost` 前的 Lost 态:`ReviveAvailable == true`;`TryRevive()` → `Phase==Reviving`、
  `Revived==true`、`ReviveCharPicksLeft==2`、`ReviveComponentPicksLeft==3`。
- `PickReviveChar`/`PickReviveComponent` 把候选写进 `Battle.Library`/`Battle.Pool`(**非**
  `CarriedLibrary/CarriedPool`);额度递减;取尽 → `Phase==InBattle`。
- `TryRevive()` 第二次返回 false(一次性);`SkipReviveReward()` 立即回 InBattle。

**Presentation**:改完过离线编译(prescompile);不强求自动化测试。

---

## 交付顺序

1. Bug(奇遇溢出):EventTests → RunEngine → BattleView → 编译。
2. 增强(复活):BattleEngineTests/RunEngineTests → BattleEngine/RunEngine → 持久化
   (Endless/GameRoot)→ BattleView → 编译。
3. `coretests` 全绿 + `prescompile` 无 `error CS`;分两个 conventional-commit 提交
   (`fix(event):` 与 `feat(battle):`)。
