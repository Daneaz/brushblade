墨锭计数：六边形墨锭 + 间距 **6** + 数字；实现是 `Ui.InkCounter`（`Ui.cs:682`，顶栏余额）与 `Ui.IngotLabel`（`Ui.cs:648`，价格/入账）。

⚠ `Ui.InkCounter` / `Ui.IngotLabel` 本身**没有胶囊底**，就是图标加数字直接排在栏里。
唯一带底的是开箱结果面板那枚「+N 墨」：外面套一层 `gold-soft` 圆角卡 190×63、圆角 15，
数字色被调用点改成 `gold-deep`（`MapView.cs:748–754`）—— 是调用点给的壳，不是组件自带的。

## 尺寸
墨锭宽 = 字号 × **1.4**、高 = 字号 × **0.85**（`Ui.cs:656–657`），所以换字号不用另配图标尺寸。

| 用处 | 字号 | 图标色 | 调用点 |
| --- | --- | --- | --- |
| 外层五屏顶栏余额 | 25 | `ingot-dark` | `MapView.cs:153`、`CollectionView.cs:163`、`BestiaryView.cs:202`、`PerkView.cs:511`、`ShopView.cs:98` |
| 局内右上余额 | 18 | `ingot-dark` | `BattleView.cs:1434` |
| 结算页右上余额 | 20 | `ingot-dark` | `GameRoot.cs:616` |
| 开箱入账「+N」 | 22 | `ingot-gold` | `MapView.cs:752` |
| 登塔结算大数字 | 84 | `ingot-dark` | `GameRoot.cs:653` |

`Ui.InkCounter` 的默认字号 20 只有 `GameRoot` 那处显式传；`Ui.IngotLabel` 的默认 20 没有调用点使用。

## 形状
即 `Theme.Ingot` 的六边形（`Theme.cs:389–393`）：14%,0 86%,0 100%,50% 86%,100% 14%,100% 0,50%。

## 规则
- 墨锭随赚随入账，`Ui.InkCounter` **只吃余额** —— 结算面板的「这趟挣了 N」、安全层累计、商品价签走
  `Ui.IngotLabel`，那些不是同一个账本，混进来会翻出凭空的增减（`Ui.cs:678–681`）。
- 余额变化时 `InkPulse` 做一次翻牌（2026-08-30 由飘字改翻牌），转的是数字本身而不是整行。
- 局内右上与外层顶栏同源：2026-08-30 半额取消后塔内预算与账户是同一本账。
- 数字不加千分位（Unity `Text` 没有 `tabular-nums`，网页再现里加上只是为了对齐好看）。
