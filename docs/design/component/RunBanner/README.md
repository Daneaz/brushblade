段末横幅：一趟塔里每个「打完了」的时刻共用的整屏反馈，五种态只换文案与色。实现是 `BattleView.DrawRunEnd` / `DrawBattleSettle` / `ShowVictoryBanner`。

## 为什么单独立一张卡
三个方法、五种态，共用同一组常数（`BannerFont` 92 / `BannerMsgFont` 23 / `BannerPillH` 100）与同一套版面。它还定义了一条别处会用到的规矩：**自动消失的提示态整屏放行点击，需要玩家决策的态必须拦截 —— 但要让开顶栏。**

## 版面
整屏纸罩 `scrim-paper`（纸色 72%，**不压暗战场**）→ 垂直居中一列，gap 31（14.8pt）：

| 元素 | 字号 / 尺寸 | 备注 |
| --- | --- | --- |
| 大字 | 92（44pt）宋体 | 胜 `text-main`、负 `cinnabar-dark`；Boss 告捷也用 `cinnabar-dark` |
| msg | 23（11pt）`text-dim` | 只有 `run-won` / `run-lost` 有 |
| 复活徽章 | `Ui.AdBadge` 300×67（宋体） | 只有 `battle-lost` 有，且仅塔内、本次登塔还没复活过 |
| 主钮 | `Ui.PillButton` 400×100（191×48pt）、圆角 24、字号 36（17.2pt）、黑体 | 胜 `jade`、负 `ink-soft` |

## 五态（文案均为字符串表原文）
| 态 | 大字 | 配套 | 钮 | 拦截 |
| --- | --- | --- | --- | --- |
| `floor-win` | 「本 层 告 捷」`text-main` | — | — | ❌ 停 1.2s + 淡出 0.3s 后自动进下一步，`blocksRaycasts=false` |
| `boss-win` | 「B O S S  已 破」`cinnabar-dark`（S 与「已」之间是两个空格） | — | — | ❌ 停 1.8s + 淡出 0.3s |
| `battle-lost` | 「败北……」`cinnabar-dark` | 「看广告复活」徽章 | 「结算」`ink-soft` | ✅ **但纸罩让开顶栏 `TopBarH`** |
| `run-won` | 「本段告捷——字正!」`text-main` | msg「Boss 已破,安全层可收官或深入。」 | 「前往安全层」`jade` | ✅ 整屏 |
| `run-lost` | 「败北」`cinnabar-dark` | msg「卒……墨锭一分不少,纪录保留。」 | 「结算」`ink-soft` | ✅ 整屏 |

塔外（`_onExit == null`）的分支：`run-won` 大字「关卡通过——字正!」、msg「通关结算:经验与墨锭入账。」、钮「返回地图」；`run-lost` msg「死亡即结算,回地图重整旗鼓。」、钮「返回地图」；`battle-lost` 不出复活徽章。

## 硬规则
- **`battle-lost` 的纸罩必须让开顶栏**（`DrawBattleSettle` 里那块 `Scrim` 子物件 `offsetMax = (0, −TopBarH)`；外层 overlay 不挂 Image）。顶栏的「退出」是阵亡后唯一的离塔出口，罩住它玩家就卡在这一屏。大字那一列仍按**整屏**居中，不跟纸罩下移。`run-lost` 是终态、顶栏本身不绘制，所以整屏罩无碍 —— 两者的区别不是随手写的。
- **罩用 `scrim-paper` 不用 `scrim`。** 纸色罩保留战场可读，玩家看得见自己是怎么死的；压暗会把这一屏变成一块纯色板。
- 提示态（`floor-win` / `boss-win`）不带按钮，也不拦点击 —— 它们只是报一声，不该打断手速。

## 使用方提供
胜负、是否在塔内（决定文案与钮的去向）、复活回调（仅 `battle-lost`）。msg 是固定文案，不带层数 / 墨锭参数。

## 与实现的差异（本系统记录，未改代码）
**败北要连点两屏，两屏几乎一样。** `DrawBattleSettle`（「败北……」+ 结算钮）→ `AdvanceAfterSettle` → `RunEngine.AdvanceAfterBattle` 置 `RunLost` → `DrawRunEnd`（「败北」+ msg + 结算钮）→ 登塔结算页。画稿 `RunEnd.dc.html` 的意图明显是**一屏**（大字 + msg + 复活 + 结算）。告捷侧更长：「B O S S  已 破」→ Boss 层战利品页 →「本段告捷——字正!」→ 安全层「安全层 · 第 N 层告捷」（其上可能再叠首破弹窗与升级弹窗）。合并横幅与安全层是现成的简化。
