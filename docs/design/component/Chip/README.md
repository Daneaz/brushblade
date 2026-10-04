小胶囊标签：状态、计数、不可逆告警、红点；实现是 `Ui.Chip`（今天在 `Ui.cs:375`），圆角恒 14 逻辑单位（`Theme.Rounded(14)`，≈6.7pt）—— 是定值圆角，不是高度一半，20 号字的页头 chip（高 32）看得出四角是圆角矩形而非全胶囊。

## 尺寸是文本的纯函数
宽 = 字数 × 字号 + padX（带图标再加 `Icons.Size` 18 + `Icons.Gap` 3），高 = 字号 + padY（`Ui.ChipWidth` / `Ui.ChipHeight`）。
`Ui.ChipFlow` 因此能在建对象之前排好行，不用测量、不用等一帧布局。默认 padX 18 / padY 12（`Ui.ChipPadX` / `Ui.ChipPadY`），默认字号 14。

| 档 | 字号 / padX / padY | 用在哪 | 调用点 |
| --- | --- | --- | --- |
| 单位格 | 15 / 10 / 5 | 战斗里敌人格、召唤物格、执笔人状态栏 | `BattleView` 常数 `UnitChipFontSize/PadX/PadY`；用于执笔人状态 `ChipFlow`、召唤物与敌人格的 `ChipFlow` 与单枚 chip |
| 护盾角标 | 14 / 4 / 3 + `shield` 图标 | 执笔人、召唤物、敌人立绘上的护盾数，`gold` 底 `gold-text` | `BattleView` 常数 `ShieldBadgeFontSize/PadX/PadY`（三种单位共用） |
| 列表 | 18 / 18 / 12 | 卡组详情的特性与词组、筛选页签计数、升级弹窗差异行的 chip、商城宝箱的「几种 · 几张」「几小时」 | `CollectionView.BuildFilterTab` / `DeltaRow`、`CharSheetSections`（特性段）、`ShopView`（宝箱格） |
| 图鉴列表 | 16 / 18 / 12 | 图鉴行内的射程/锁定/护甲 | `BestiaryView`（行内 `ChipFlow`） |
| 页头 | 20 / 18 / 12 | 收集页「未拥有 N」「新字 N」、图鉴「N 条待领赏」 | `CollectionView.BuildTopBar`、`BestiaryView`（顶栏） |
| 升级弹窗等级 | 21 / 18 / 12 | 「升到 Lv.N」，`jade` 底白字 | `CollectionView.ShowUpgradePreview` |
| 告警 | 21 / 23 / 12 | 不可逆后果，`danger-bg` 底 `danger-text` 字（不加粗），如换字弹窗「字库 {count}/{capacity}——被换掉的字永久失去」 | `BattleView` 换字弹窗（另定高 46） |
| 已售印 | 21 / 18 / 12 | 商城牌面正中「已售」「已领」，`ink-soft` 底白字 | `ShopView.SoldSeal` |
| Toast | 21 / 29 / 17 | 商城成交提示，`ink` 底白字 | `ShopView.ShowToast` |
| 引导印章 | 21 / 21 / 23 | 新手引导卡右上的「印」，`rarity-gold` 底 `gold-text` 字 | `CoachOverlay`（`SealFontSize`） |
| 引导「这样做」 | 19 / 13 / 4 | 新手引导卡的动作标，`gold-soft` 底 `gold-deep` 字 | `CoachOverlay`（`DoitKFontSize`） |
| 里程碑标签 | 19 / 18 / 12，**带描边** | 里程碑三选一牌下「新字」（`gold-soft` 底 `gold-deep` 字、`rarity-gold` 边）/「已有 · 重复卡 +1」（`panel-inset` 底 `text-dim` 字、`panel-border` 边） | `MilestonePickSheet` |
| 角色升级 | 21 / 18 / 12 或 25 / 18 / 12，**带描边** | 「Lv.{level} 里程碑已解锁 · …」（`gold-soft` 底 `gold-text`、`gold-border` 边）；宝箱行「{tierName} × {count}」（`Theme.CardWhite` 纯白底 `text-main`、宝箱色 **3 单位**边） | `LevelUpPopup` |
| 图鉴详情头 | 24 / 18 / 12 + 图标 | 怪物能力名，`cinnabar` 底白字 | `BestiaryView`（能力详情头） |
| 单位详情属性 | 13 / 8 / 4 | 详情弹窗头行的属性标 | `UnitSheet`（头行） |
| 单位详情小标 | 12 / 6 / 4 | 详情弹窗的状态 chip 与能力图标 chip；词条小标 12 / 18 / 12（`paper-dim` 底 `text-dim`） | `UnitSheet`（`StatusChipFont` / `AbilityChipFont`） |
| 怪物预览 | 12 / 18 / 12 | `EnemyPreview` 的能力 chip | `EnemyPreview` |
| 同族角标 | 10 / 4 / 4 | 拆合台「≈X」贴在牌角 | `BattleView.PlaceKinBadge` |
| 「+N」计数 | 同本行 / **4** / 同本行 | `ChipFlow` 截断时自己补的标记，`paper-dim` 底 `text-main`，比真 chip 紧得多 | `Ui.ChipCountPadX` |

## 配色
- 战斗状态（自成一套身份色，不套五色语义，2026-10-04 用户拍板保留）：朱砂 = 持续伤害与威胁；`ink-soft` = 控制与减益（冻结、减速、致盲、沉默、破甲）；铜绿 = 恢复与防御增益；赭金 = 攻击类增益与护盾，**赭金底一律压 `gold-text`**（白字只有 2.5:1）。
- 单位格的属性徽章压的是 **`<el>-glyph` 字形色**，不是 `<el>` 原色（`BattleView` 召唤物格与敌人格两处，今天在 `2010` `2798`，2026-09-19 改）。
- 卡组详情的特性 chip 走 `CardTraits.ChipColor`：灼烧族朱砂、冻结族水字形色、控制族心字形色、护盾族土字形色、荆棘族木字形色、其余 `ink-soft`。
- 页头计数：`panel-inset` 底配 **`text-dim`**（2026-09-21 从 `text-faint` 压深，4.29 → 5.05:1），或朱砂底白字。
  ⚠ 没有 `lock-gray` 这个 token —— `Theme.LockGray` 对应的是 `text-faint`，别在卡里写前者。
- 词组 chip（卡组详情与升级弹窗里没有图标的那一档）：`locked-bg` 底配 **`text-main`**。
  2026-09-21 从 `text-dim`（4.17:1）压深；**底不能换成 `panel-inset`** —— 升级弹窗的 `DeltaRow` 行底
  本身就是 `panel-inset`，换了 chip 会与行底同色、形状整个消失。

✅ **白字压属性色的地方一律压 `<el>-glyph`，不压 `<el>` 原色。** 2026-09-21 把落下的 7 处
一并改掉（金 2.48→5.93、木 4.05→7.27）：图鉴怪物格角标、图鉴行属性 chip、克制两格的属性圆点、
详情弹窗属性 chip、怪物预览的两处形态页签底、成语 Boss 回落的圆形字头像。战斗屏的敌人格与
召唤物格早在 2026-09-19 就改过。拆合台的同族角标（`BattleView.PlaceKinBadge`）是**部件底**不是属性
徽章，走 `<el>` 原色是刻意的 —— 它要与手牌上的属性色块认成同一件东西。

## 描边（2026-09-21 新增的一档）
`border` 非空时在 chip 外圈留一条边线，默认 1 逻辑单位；做法与 `Ui.OutlinedPanel` 同 —— 外层换成描边色、内层填充色四边内缩，图标与文字画在填充之上。

起因是「浅卡压浅底」：收集页与图鉴的筛选栏在选中「全部」页签时传 `panel-border`（`CollectionView.BuildFilterTab`、`BestiaryView` 筛选页签）——那一格的页签底正是 `panel-inset`，与 chip 自己的底同色，不描边就整个糊掉。属性页签铺 `<el>-soft`、Boss 页签铺 `ink`，本来就分得开，不传。

此后又有三处把描边当强调用（2026-10-02 起），一共五个调用点：
- `MilestonePickSheet`：「新字」`gold-soft` 底配 `rarity-gold` 边，「已有 · 重复卡 +1」`panel-inset` 底配 `panel-border` 边；
- `LevelUpPopup`：里程碑解锁行 `gold-soft` 底配 `gold-border` 边；宝箱行 `Theme.CardWhite` 纯白底配该档宝箱色边，**边宽 3**（`borderThickness: 3f`，唯一不用默认 1 的地方）。

第六个调用点（2026-10-04，spec v7 R1）：
- `BattleView.DrawEnemies`：战斗敌人状态·霜抗，`panel-paper` 底、`water-glyph` 字/图标（`frostguard`）与 1 单位 `water-glyph` 描边，无数字。经 `Ui.ChipSpec.Border` 传入（缺省 `null` = 实底），`ChipFlow` 转给 `Ui.Chip(border:)`。设计稿：`drafts/traits` 的 `k-ring`。

两条不变式：
- **描边不吃宽高。** 尺寸仍是下面那两个纯函数算出来的，边线画在原尺寸之内 —— `ChipFlow` 要在建对象之前把行排好，靠的就是它们。
- **默认值即原行为。** `border` 缺省 `null`，走原来的单 Image 分支；新参数排在 `iconKey` 之后，既有的位置实参不受影响。

## 图标
来自 Icons 资产组（白色单色 SVG，64 方画布），在 chip 里左贴 padX/2、竖直居中，按前景色着色；
PNG 取不到时回落成 `Icons.Fallback` 的汉字，**占同样宽度**，布局不受资产有无影响（`Ui.Chip` 的 `iconKey` 分支）。

## 规则
红点是动态判据，亮着就必须点进去真有事可做；每格最多 `ChipMaxLines` 2 行 chip（召唤物背排 1 行），
超出从尾部丢、末尾补「+N」——**计数优先于多显示一个 chip**（`Ui.ChipFlow`）。
