弹窗族：遮罩上的一张宣纸描边卡。一条继承链 `Ui.Sheet` → `Ui.ModalShell` → `Ui.Modal` / `Ui.Alert`（都在 `UI/Ui.cs`）。

## 外壳（`Ui.Sheet`，全站浮层共用）
遮罩（默认 `scrim` 55%）+ `Ui.OutlinedPanel` 宣纸卡 + 带内边距的竖排容器：
- 卡：面 `panel-paper`、描边 `panel-border` **1.5**、圆角 **18**（常量 `SheetBorder` / `SheetRadius`）。浅卡压浅底必须带这条边。
- 内容容器：左右上下内边距 **24**、行距 **14**（常量 `SheetPad` / `SheetSpacing`），默认对齐 `UpperCenter`。
- 卡片外层挂一个 `transition: None` 的 Button 只为吃掉点击，不让它穿透到遮罩（`targetGraphic` 指向内层 face）。
- `lift` 参数把卡片整体上抬，留出底下那张被长按的牌（字卡详情用）；带 `out card` 的重载把卡片 RectTransform 交给调用方改锚（技能节点贴右缘用）。

## 小弹窗（`Ui.ModalShell` / `Ui.Modal` / `Ui.Alert`）
- 卡片 **620×300**（`Ui.Modal` 默认 `halfSize` 310×150），`ModalShell` 把内容改成**垂直居中**。
- 标题宋体 **24** `text-main`；正文 **17** `text-dim`；按钮行间距 **14**。
- 按钮 `Ui.PillButton` **150×52**、圆角 24、字号 **18**、黑体；点任一钮先关弹窗再执行回调。
- `Ui.Alert` 是单按钮版：「知道了」，按钮底 `locked-bg`、字 `text-main`。操作被拒类提示统一走它。
- 正文多行的弹窗传更大的 `halfSize`（`EnemyPreview.Show` 传 420×340 / Boss 420×400）。
- `Ui.Modal` 一律 `dismissable: true`（点遮罩即关）。

直连 `ModalShell` 的只剩 2 处：`Ui.Modal` 本身与 `EnemyPreview.Show`（`Ui.cs` 里 `ModalShell` 的注释已同步）。

## 现有调用里的几种组合（preview 三例取自这里）
| 弹窗 | 钮（左 → 右） | 调用点 |
| --- | --- | --- |
| 离塔「离 塔」 | 挂起离塔 `cta` · 弃塔朱砂描边（`Ui.DangerOutline`）· 继续战斗 `locked-bg` | `BattleView.DrawTopBar` 的退出钮（塔内） |
| 退出确认（塔外） | 确认退出朱砂描边（`Ui.DangerOutline`）· 继续战斗 `locked-bg` | 同上 |
| AP 不够 / 还有 AP 未用 | 结束回合 `cta` · 再想想 `locked-bg` | `BattleView.MaybeModalError` / `ConfirmEndTurn` |
| 首破「首破 · {段名}」 | 知道了 `cta` | `GameRoot.ShowSafeLayer` |
| 各类被拒提示 | 知道了 `locked-bg`（`Ui.Alert`） | 商城 / 战斗 / 卡组等 |

## 同一套外壳的大浮层（都走 `Ui.Sheet`，尺寸各自定）
| 浮层 | 尺寸（逻辑单位，基准机） | 16:9 宽 | 遮罩 | 点遮罩关 | 调用点 |
| --- | --- | --- | --- | --- | --- |
| 战斗换字 | 1633×460 | **1354**（`min(1633, .safe 框)`） | `scrim-soft` | 否 | `BattleView.DrawReplaceSheet` |
| 战斗选字 | 1298×520 | 1298（`min(1298, .safe 框)`，余 56） | `scrim-soft` | 否 | `BattleView.DrawPickSheet` |
| 单位详情 | 1280×760 | 1280（定宽，框内余 74） | `scrim` | 是 | `UnitSheet.Show` |
| 字卡详情 | 1591×670（高版按屏算） | **1354**（`min(1591, .safe 框)`，内部两栏 757 → 638） | `scrim` | 是 | `CharPreview.Show` |
| 升级前后对比 | 1088×670 | 1088（定宽，余 266） | `scrim` | 是 | `CollectionView.ShowUpgradePreview` |
| 技能节点（贴右缘） | 宽 396，上下铺满 | 396（贴安全区右缘，不让 .safe 那 123；抽屉式，左侧还剩 1204） | `scrim` | 是 | `PerkNodeSheet.Show` |
| 里程碑领取（墨锭 + 字卡 3 选 1） | 1256×785（内边距 42/29、行距 14） | 1256（定宽，余 98；内部横排另算） | `scrim` | 是 | `MilestonePickSheet.Show` |
| 角色升级 | 1256×745（内边距改 38/29、行距 21） | 1256（定宽，余 98；内部横排另算） | `scrim` | 否 | `LevelUpPopup.Show` |
| 小弹窗 | 620×300 | 620 | `scrim` | 是 | `Ui.Modal` / `Ui.Alert` |

**16:9 宽**的口径（2026-10-04）：`CanvasScaler` 1600×900 按高匹配，16:9 画布宽 1600、无刘海机两侧各补 `SafeArea.MissingInset()` 123 → 稿上 .safe 框 **1354**（基准机 ≈1705）。浮层挂在 SafeAreaFitter 之内、铺满它，所以**卡宽 > 1354 就会伸进 .safe 边、贴屏边**。稿宽超过 1354 的浮层一律 `min(稿宽, SafeArea.FrameWidth())`，内部按夹后的净宽（− 描边 3 − 内边距 48）反算，不缩字号。「余」= 1354 − 卡宽。

## 规则
- **不是「同屏只留一个」**：`Ui.Sheet` 的互斥是**按 `name`** 的，且要调用方显式传 `replaceSameName: true`。
  `ModalShell` 传的是 `false` —— 战利品弹窗与长按预览是刻意分层、要同屏共存的两张浮层，
  2026-09-02 的修复就是把「全站任意两个浮层互斥」收成「同族按名排队」（见 `Ui.Sheet` 的文档注释）。
  真正按名排队的是 `UnitSheet` / `CharSheet`（字卡详情）/ `PerkNodeSheet` / `UpgradeSheet` / 两张 `BattleSheet` / `MilestonePick` / `LevelUp`。
  各页面自己另有「同屏一个 `_modal`」的约定（新弹窗前先销毁旧的），那是调用方的事，不在外壳里。
- 点遮罩即关闭；必须做出选择的流程（换字、选字、升级弹窗）传 `dismissable: false`。
- 按钮顺序：主行动在左，取消档在最右。
- 局内浮层用 `scrim-soft`（42%）留住战场，段末横幅用 `scrim-paper`。

⚠ **「不可逆后果标红加粗」是拍板的规则，实现里还没有**：字符串表里 0 条富文本，
`Ui.Modal` 把整段正文画成单色 `text-dim`。实现侧现有的不可逆提示是
`danger-bg` / `danger-text` 的告警 chip（`BattleView.DrawReplaceSheet` 里那条），不是正文内标红。

## 使用方提供
标题（两字标题字间加空格）、正文（多行走 `\n`）、1–3 个按钮（文案 + 底色 + 字色 + 回调）。
