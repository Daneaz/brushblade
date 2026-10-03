弹窗族：遮罩上的一张宣纸描边卡。一条继承链 `Ui.Sheet`（`Ui.cs:209`）→ `Ui.ModalShell`（`282`）→ `Ui.Modal`（`305`）/ `Ui.Alert`（`324`）。

## 外壳（`Ui.Sheet`，全站浮层共用）
遮罩（默认 `scrim` 55%）+ `Ui.OutlinedPanel` 宣纸卡 + 带内边距的竖排容器：
- 卡：面 `panel-paper`、描边 `panel-border` **1.5**、圆角 **18**（`Ui.cs:264` `270`）。浅卡压浅底必须带这条边。
- 内容容器：左右上下内边距 **24**、行距 **14**（`Ui.cs:271–272`），默认对齐 `UpperCenter`。
- 卡片本体挂一个 `transition: None` 的 Button 只为吃掉点击，不让它穿透到遮罩（`Ui.cs:238–240`）。
- `lift` 参数把卡片整体上抬，留出底下那张被长按的牌（字卡详情用）。

## 小弹窗（`Ui.ModalShell` / `Ui.Modal` / `Ui.Alert`）
- 卡片 **620×300**（`halfSize` 310×150，`Ui.cs:302`），内容改成**垂直居中**（`Ui.cs:295`）。
- 标题宋体 **24** `text-main`（`Ui.cs:296`）；正文 **17** `text-dim`（`309`）；按钮行间距 **14**（`310`）。
- 按钮 **150×52**、圆角 24、字号 **18**（`Ui.cs:318`）。
- `Ui.Alert` 是单按钮版：按钮底 `locked-bg`、字 `text-main`（`Ui.cs:325`）。
- 正文多行的弹窗传更大的 `halfSize`（`EnemyPreview.cs:77` 传 420×340 / Boss 420×400）。

直连 `ModalShell` 的只剩 5 处：`PerkView` / `CollectionView` / `CharPreview` / `EnemyPreview` / `Ui.Modal` 本身。

## 同一套外壳的大浮层（都走 `Ui.Sheet`，尺寸各自定）
| 浮层 | 尺寸（逻辑单位） | 遮罩 | 调用点 |
| --- | --- | --- | --- |
| 战斗换字 | 1633×460 | `scrim-soft` | `BattleView.cs:3644` |
| 战斗选字 | 1298×520 | `scrim-soft` | `BattleView.cs:3902` |
| 单位详情 | 1280×760 | `scrim` | `UnitSheet.cs:79` |
| 字卡详情 | 1591×670（高版按屏算） | `scrim` | `CharPreview.cs:85` |
| 升级前后对比 | 1088×670 | `scrim` | `CollectionView.cs:607` |
| 技能节点（贴右缘） | 宽 396，上下铺满 | `scrim` | `PerkNodeSheet.cs:59` |

## 规则
- **不是「同屏只留一个」**：`Ui.Sheet` 的互斥是**按 `name`** 的，且要调用方显式传 `replaceSameName: true`。
  `ModalShell` 传的是 `false`（`Ui.cs:286`）—— 战利品弹窗与长按预览是刻意分层、要同屏共存的两张浮层，
  2026-09-02 的修复就是把「全站任意两个浮层互斥」收成「同族按名排队」（`Ui.cs:170–190`）。
  真正按名排队的是 `UnitSheet` / `CharPreview` / `PerkNodeSheet` / `UpgradeSheet` / 两张 `BattleSheet`。
- 点遮罩即关闭；必须做出选择的流程（战利品、换字）传 `dismissable: false`。
- 按钮顺序：主行动在左，取消档在最右。
- 局内浮层用 `scrim-soft`（42%）留住战场，段末横幅用 `scrim-paper`。

⚠ **「不可逆后果标红加粗」是拍板的规则，实现里还没有**：字符串表里 0 条富文本，
`Ui.Modal` 把整段正文画成单色 `text-dim`（`Ui.cs:309`）。实现侧现有的不可逆提示是
`warn-bg` / `warn-text` 的告警 chip（`BattleView.cs:3648`），不是正文内标红。

## 使用方提供
标题（两字标题字间加空格）、正文（多行走 `\n`）、1–3 个按钮。
