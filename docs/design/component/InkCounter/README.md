墨锭计数：六边形墨锭 + 间距 **6**（2.87pt）+ 数字；实现是 `Ui.InkCounter`（顶栏余额）与 `Ui.IngotLabel`（价格 / 入账 / 奖励额），都在 `UI/Ui.cs`。

⚠ `Ui.InkCounter` / `Ui.IngotLabel` 本身**没有胶囊底**，就是图标加数字直接排在栏里。
唯一带底的是开箱结果面板那枚「+N」：外面套一层 `gold-soft` 圆角卡 190×63（90.78×30.10pt）、圆角 15（7.17pt），
数字色被调用点改成 `gold-deep`（`MapView.BuildResultLeft`）—— 是调用点给的壳，不是组件自带的。

## 尺寸
墨锭宽 = 字号 × **1.4**、高 = 字号 × **0.85**（`IngotLabel` 与 `InkCounter` 内部的 `IngotLabelText` 同一套），所以换字号不用另配图标尺寸。
数字是 `text-main`、黑体。图标色：默认 `ingot-dark`；`IngotLabel(gold: true)` 用 `ingot-gold`。

| 用处 | 组件 | 字号 | 图标色 | 调用点 |
| --- | --- | --- | --- | --- |
| 外层六屏顶栏余额 | InkCounter | 25 | `ingot-dark` | `MapView.BuildTopBar`、`CollectionView`、`BestiaryView`、`PerkView`、`ShopView`、`CharacterView`（各自的顶栏） |
| 局内右上余额 | InkCounter | 18 | `ingot-dark` | `BattleView`（`_topRight`） |
| 结算页右上余额 | InkCounter | 20 | `ingot-dark` | `GameRoot.BalanceCorner` |
| 开箱入账「+N」 | IngotLabel | 22 | `ingot-gold` | `MapView.BuildResultLeft` |
| 登塔结算大数字 | IngotLabel | 84 | `ingot-dark` | `GameRoot.ShowTowerSettle` |
| 角色页里程碑卡的墨锭额 | IngotLabel | 20 | `ingot-dark` | `CharacterView.MilestoneNode` |
| 领取里程碑弹窗的墨锭额 | IngotLabel | 29 | `ingot-dark` | `MilestonePickSheet.Show` |

`Ui.InkCounter` 的默认字号 20 只有 `GameRoot.BalanceCorner` 那处用（显式传 20）；`Ui.IngotLabel` 的默认 20 没有调用点依赖（`CharacterView` 显式传 20）。

## 形状
即 `Theme.Ingot` 的六边形（56×34 凸多边形）：14%,0 86%,0 100%,50% 86%,100% 14%,100% 0,50%。

## 规则
- 墨锭随赚随入账，`Ui.InkCounter` **只吃余额** —— 结算面板的「这趟挣了 N」、安全层累计、商品价签走
  `Ui.IngotLabel`，那些不是同一个账本，混进来会翻出凭空的增减（见 `InkCounter` 的注释）。
- 余额变化时 `InkPulse` 做一次翻牌（2026-08-30 由飘字改翻牌）：数字沿竖轴翻过去亮出增量（进账翠玉 / 支出朱砂），再翻回新余额；转的是数字本身而不是整行。
- 局内右上与外层顶栏同源：2026-08-30 半额取消后塔内预算与账户是同一本账。
- 千分位：余额（`InkCounter`）与开箱入账、登塔结算都**不加**千分位，直接 `ToString()`；
  里程碑那两处（`CharacterView` 里程碑卡、`MilestonePickSheet` 的「墨锭 +{ink}」）是调用点自己用 `ToString("N0")` 格式化，**带**千分位（如「1,200」「墨锭 +1,200」）。
