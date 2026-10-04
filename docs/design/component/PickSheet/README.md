选字面板：战利品、复活补给、字库补给三处共用的候选选择器；它也是「**首点预览、再点确认**」这条两段式惯例的定义处。实现是 `BattleView.DrawPickSheet`，候选牌是 `BattleView.PickTile`。

## 为什么单独立一张卡
三个入口共用一版，而更要紧的是那条两段式惯例 —— 它横跨战利品、复活补给、奇遇三个屏，却没有任何一处写下来。奇遇是不可逆决策，钮上只有名称，**首点就是盲点**，所以 2026-08-27 拍板：第一下只选中并把效果说明送进屏底，第二下才结算。

## 版面（逻辑单位 → 稿面 pt，1pt = 2.093）
- 外壳 `Ui.Sheet`：1298×520（620×248pt），圆角 18、描边 1.5 `panel-border`、面 `panel-paper`，内边距 24、行距 14；遮罩 `scrim-soft`（42%），`dismissable: false`。内容自上而下排（`UpperCenter`）。
- 自上而下：标题 33（15.8pt 宋体 `text-main`）→ hint 21（10pt 黑体 `text-dim`）→ 候选行 → **定高 63 的 detail 横条** → foot 行。
- 候选行：gap 21（10pt）。每张是 `GlyphTile` 130×163（62×78pt），牌下 8 的间距再接一行「{属性} · {稀有度}」19（9pt）`text-dim`。选中的那张是墨色镶边（`selected: true`）。
- 候选张数 = `BattleConfig.RewardOptionCount` = **5**，点了技能「慧眼」L1 是 **6**（`MetaRules` 组战斗配置时 `5 + PerkRules.Bonus(RewardOptions)`）。6 张也排得下：6×130 + 5×21 = 885 < 1247。
- detail 横条：`Ui.OutlinedPanel(PanelInset, PanelBorder, 17, 2)`，宽 = 内容区净宽 `PickSheetW − Ui.SheetBorder×2 − Ui.SheetPad×2` = 1298 − 3 − 48 = **1247**（贴平内容区左右缘），**定高 63（30pt）**，内部横排 gap 12、padding 左右 23 / 上下 0、`MiddleLeft`。选中后放：效果一行 21 `text-main` → `flexWidth:1` 的 spacer → 「再点一次收下」19 `cinnabar-dark`。
- foot 行：gap 21，宽同 detail，`MiddleLeft`，依次是：
  1. 「看广告 · 字库 +2」`Ui.AdBadge` 280×63（字号 21 宋体，圆角 31，播放三角 15×17）—— 已扩容（`LibraryExpanded`）时不画；
  2. `flexWidth:1` 的 spacer；
  3. 「重抽({left})」`Ui.RoundButton` 220×63、圆角 10、字号 25、`locked-bg` 底 `text-main` 字 —— 只在 `RewardRerollsLeft > 0` 时画（技能「慧眼」L2 明察，2026-10-02）；
  4. 跳过钮 `Ui.RoundButton` 280×63、圆角 10、字号 25、`locked-bg` 底 `text-main` 字。

## 文案（`strings.zh-CN.json` 原文）
| 位置 | key | 原文 |
| --- | --- | --- |
| 标题 · 战利品 | `battle.reward.pick_title` | `战利品 · 选字(还剩 {left})` |
| 标题 · 复活补给 | `battle.revive.pick_title` | `复活补给 · 选字(还剩 {left})` |
| 标题 · 字库补给 | `battle.restock.pick_title` | `字库补给 · 选字(还剩 {left})` |
| hint（三处共用） | `battle.reward.pick_hint` | `字库 {count}/{capacity} · 点一下看效果,再点收下` |
| 牌下一行 | `battle.reward.pick_name` | `{element} · {rarity}`（无属性落「中性」） |
| detail 效果行 | `battle.reward.detail_line` | `{charId} ｜ {brief}`，brief = `CharInfo.EffectsText` |
| detail 右端 | `battle.reward.tap_again_suffix` | `再点一次收下` |
| 广告徽章 | `battle.btn.ad_expand_library` | `看广告 · 字库 +2` |
| 重抽 | `battle.reward.reroll` | `重抽({left})` |
| 跳过 · 战利品 | `battle.btn.reward_skip` | `不要了,开拔` |
| 跳过 · 复活/字库补给 | `battle.btn.revive_skip` | `够了,接着打!` |

## 硬规则
- **detail 横条定高，未选中时靠 `minHeight` 撑住空着。** 定高是为了选中前后**不跳版** —— 玩家的手指正停在牌上，版面一动就点错。代价是这行不开 Wrap，文案长度上限约 30 字，再长会静默溢出、压住右端的「再点一次收下」。
- **底色是 `panel-inset` 不是 `paper-dim`。** `paper-dim` 与描边色 `panel-border` 撞成同一个 `#DED7C9`，渲出来是一块没有描边的灰褐实心板。这两支不要互换。
- 「再点一次收下」右浮（`flexWidth:1` 的 spacer 顶开），色 `cinnabar-dark`。
- 这三个入口**放行长按预览叠在浮层之上**（候选牌挂 `HoldToPreview`，`DrawPickSheet` 不销毁 `_modal`）—— 它们是可选流程。掉字/换字那条不可逆决策要更保守，进面板先销毁预览（见 ReplaceSheet）。
- 重抽会把候选整组换掉，所以同时清掉预览下标与两条满库替换下标（`DrawRerollButton`）。

## 三个入口
| 入口 | 方法 | 标题 key | 跳过钮 key |
| --- | --- | --- | --- |
| 战利品选字 | `DrawRewardCharStep` | `battle.reward.pick_title` | `battle.btn.reward_skip` |
| 广告复活补给 | `DrawReviveCharStep` | `battle.revive.pick_title` | `battle.btn.revive_skip` |
| 字库补给 | `DrawReviveCharStep`（`CurrentSupply == Restock`） | `battle.restock.pick_title` | `battle.btn.revive_skip` |

三者都读同一份 `_run.RewardOptions`，foot 行同构（徽章 → spacer → 重抽 → 跳过）。满库时选中第二下转入 ReplaceSheet。

## 使用方提供
标题、hint、候选字列表、选中回调、跳过回调。detail 的内容由使用方按「选中的那张字会做什么」填。

## 与实现的差异（本系统记录，未改代码）
- 画稿 `Reward.dc.html` 画的是 **3 张**候选，实现是 5 张（慧眼 L1 后 6 张）。稿的张数过时。
- 面板高 520 是估的容器高，内容约 474（24+33+14+21+14+190+14+63+14+63+24），底下留白；此卡按定高画。
