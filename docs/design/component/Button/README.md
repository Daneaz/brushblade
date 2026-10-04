胶囊/圆角按钮，**黑体、不加粗**（`ThemedLabel` 默认 `BodyFont`），底色与字色由调用点给；实现是 `Ui.PillButton`（圆角恒 24）与 `Ui.RoundButton`（圆角由调用点给，**默认 10**）。全站只有 `Ui.AdBadge` 的字是宋体（`TitleFont`）。

⚠ 圆角**不是**高度的一半：主行动 523×109 配圆角 24（约 11.5pt），视觉上是圆角矩形而不是胶囊。
Unity `Text` 没有字距：「续 爬」「登 塔」字间那一格是文案里的空格，不是 letter-spacing。

## 尺寸取自真实调用点（逻辑单位；pt = 逻辑单位 ÷ 2.093）
| 用处 | 尺寸 · 圆角 · 字号 | 底色 / 字色 | 调用点 |
| --- | --- | --- | --- |
| 主界面「续 爬 / 登 塔」 | 523×109 · 24 · 38 | `cta` 石青 / 白 | `MapView.BuildTowerPanel` |
| 外层页顶栏「返回地图」（卡组/图鉴/技能/商城/角色） | 130×63 · 24 · 25 | `exit-pink` / 白 | 各页 `BuildTopBar` |
| 主界面顶栏「设置」 | 130×63 · **16**（RoundButton） · 25 | `exit-pink` / 白 | `MapView.BuildTopBar` |
| 战斗顶栏「设置」 | 72×38 · 24 · 15 | `ink-soft` / 白 | `BattleView.DrawTopBar` |
| 战斗顶栏「退出」 | 90×38 · 24 · 15 | `exit-pink` / 白 | `BattleView.DrawTopBar` |
| 战斗顶栏倍速开关 | 96×38 · 24 · 15 | `locked-bg` 起（开关态另染） | `BattleView.DrawSpeedToggle` |
| 弹窗按钮行（`Ui.Modal`） | 150×52 · 24 · 18 | 按语义；取消档 `locked-bg` / `text-main`；不可逆档朱砂描边 3 + `cinnabar-dark` 字 + `panel-paper` 底 | `Ui.Modal` |
| 战斗行动钮（出 / 拆 / 弃） | 76×52 · **10**（RoundButton 默认） · 17 | `cta` / `split-blue` / `exit-pink`，白字 | `BattleView.DrawActions` |
| 结束回合 | 190×52 · 24 · 21 | `cta` / 白 | `BattleView.DrawEndTurn` |
| 选字页「跳过」 | 280×63 · **10**（RoundButton） · 25 | `locked-bg` / `text-main` | `BattleView.DrawRewardCharStep` / `DrawReviveCharStep` |
| 选字页「重抽(N)」 | 220×63 · **10**（RoundButton） · 25 | `locked-bg` / `text-main` | `BattleView.DrawRerollButton` |
| 换字页取消 | 300×63 · 24 · 25 | `locked-bg` / `text-main` | `BattleView.DrawReplaceSheet` |
| 宝箱「开始开启」 | 150×46 · 14 · 19 | `ink-soft` / 白 | `MapView.DrawChest` |
| 宝箱「开箱!」 | 150×50 · 14 · 22 | `gold` / `gold-text` | `MapView.DrawChest` |
| 宝箱「{cost}墨」花墨加速 | 72×46 · 14 · 19 | `gold` / `gold-text` | `MapView.DrawChest` |
| 段末横幅钮 | 400×100 · 24 · 36 | 过关 `jade` / 其余 `ink-soft`，白字 | `BattleView.DrawRunEnd` · `DrawBattleSettle` |
| 卡组底部整行钮 | 弹性宽×75 · 24 · 24 | 可升 `jade`/白 · 材料不足 `panel-inset`/`text-faint` · 满级 `gold-soft`/`gold-deep` · 未拥有 `shop-nav`/白 | `CollectionView.SheetActions` |
| 图鉴底部整行钮 | 弹性宽×71 · 24 · 24（未解锁 22） | 可领 `cta`/白 · 其余 `panel-inset`/`text-faint` | `BestiaryView.BuildSideFoot` |
| 技能节点「解锁 · {cost}墨」 | 211×56（内容宽 345 − 14 − 120）· 24 · 18 | 可解锁 `gold`/`gold-text` · 否则 `locked-bg`/`text-faint` | `PerkNodeSheet.BuildFooter` |
| 技能节点关闭 | 120×56 · 24 · 18 | `panel-inset` / `text-dim` | `PerkNodeSheet.BuildFooter` |
| 弹窗关闭「×」 | 28×28 · 14 · 14 | `paper-dim` / `text-dim` | `UnitSheet.BuildNameRow` |

`Ui.RoundButton` 不传尺寸时是 120×56、圆角 10，`Ui.PillButton` 是 120×56、圆角 24 —— **没有调用点吃这两个默认值**，全部显式给尺寸。

### 看广告（`Ui.AdBadge`）：圆角与字号是派生的，不是自由档
以「稿 .adbadge 高 63」为基准按高度整体缩放：`scale = 高 ÷ 63`，字号 `round(21×scale)`、圆角 `round(31×scale)`、间距 `10×scale`、播放三角 `15×17 ×scale`；面 `ad-green-bg`，1.5 描边 `ad-green`，字 `ad-green-text` 宋体。

| 调用点 | 尺寸 | 圆角 · 字号 |
| --- | --- | --- |
| 满库扩容「看广告 · 字库 +2」（`BattleView.DrawAdExpandBadge`） | 280×63 | 31 · 21（基准档） |
| 败北「看广告复活」（`BattleView.DrawBattleSettle`） | 300×67 | 33 · 22 |
| 商城补给两枚（`ShopView.BuildSupplies`） | 弹性宽×64 | 31 · 21 |
| 商城卡牌广告位（`ShopView.BuildAdSlot`） | 卡宽×46 | 23 · 15 |
| 宝箱加速「-{N}m」（`MapView.DrawChest`） | 72×46 | 23 · 15 |

所以 46 高那两枚是**圆角 23 / 字号 15**，不是宝箱钮那档的 14 / 19。

## 禁用态
代码不换底色：`interactable = false` 走 Unity `Button` 默认的 ColorTint，`disabledColor` = (0.78, 0.78, 0.78, 0.5)，即**原底色压暗到 78% 再半透明**（如「开始开启」在已有宝箱计时时）。需要「看起来是灰钮」的地方由调用点自己传 `panel-inset` / `locked-bg` 底（卡组、图鉴、技能节点）。

## 配色语义
`cta` 石青 = 推进（一屏一颗实底主钮）· 朱砂描边 `Ui.DangerButton`（`Ui.Modal` 元组传 `Ui.DangerOutline`）= 不可逆（弃塔、确认退出）· `ink-soft` 中性但改变状态 · `gold` 花钱 · `locked-bg` 取消（同一行时排最右）· `exit-pink` 顶栏功能钮与「弃」。

⚠ **赭金底一律压 `gold-text`**（白字只有 2.5:1）。宝箱两枚钮、技能「解锁」主钮与护盾角标都按这条做。

## 使用方提供
按钮文案（走字符串表）、底色与字色、尺寸、圆角（RoundButton）、`interactable`。
⚠ 触控高度：实现里最矮的可点件是弹窗关闭「×」28（13.4pt），战斗顶栏 38（18pt），弹窗/宝箱/战斗行动钮 46–52（22–25pt），
只有「续爬」109（52pt）与底部导航 92（44pt）到得了 44pt。`tokens.json` 的 `tap-min` 写的是 44**pt**
（＝92 逻辑单位），与实现现状差一个量级 —— 这条口径待拍板，不要拿它当已达成。

## 不要
一行里放两个石青钮；把朱砂做成按钮实底（朱砂只表示危险 / 威胁）；给两字文案加空格以外的装饰（只有标题式按钮「续 爬」才字间加空格）。
