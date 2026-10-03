段末横幅：一趟塔里每个「打完了」的时刻共用的整屏反馈，五种态只换文案与色。实现是 `BattleView.DrawRunEnd` / `DrawBattleSettle` / `ShowVictoryBanner`。

## 为什么单独立一张卡
三个方法、五种态，共用同一组常数与同一套版面。它还定义了一条别处会用到的规矩：**自动消失的提示态整屏放行点击，需要玩家决策的态必须拦截 —— 但要让开顶栏。**

## 版面
整屏纸罩 `scrim-paper`（纸色 72%，**不压暗战场**）→ 垂直居中一列，gap 31（14.8pt）：

| 元素 | 字号 / 尺寸 | 备注 |
| --- | --- | --- |
| 大字 | 92（44pt）宋体 | 胜 `text-main`、负 `cinnabar-dark` |
| msg | 23（11pt）`text-dim` | 只有 `run-won` / `run-lost` 有 |
| 复活徽章 | `Ui.AdBadge` 高 67 | 只有 `battle-lost` 有 |
| 主钮 | 400×100（191×48pt）、字号 36（17.2pt） | 胜 `jade`、负 `ink-soft` |

## 五态
| 态 | 大字 | 配套 | 钮 | 拦截 |
| --- | --- | --- | --- | --- |
| `floor-win` | 「本 层 告 捷」`text-main` | — | — | ❌ 1.2s 自动淡出，`blocksRaycasts=false` |
| `boss-win` | 「B O S S 已 破」`cinnabar-dark` | — | — | ❌ 1.8s 自动淡出 |
| `battle-lost` | 「败北……」`cinnabar-dark` | 看广告复活徽章 | 「结算」`ink-soft` | ✅ **但纸罩让开顶栏 `TopBarH`** |
| `run-won` | 「本段告捷——字正！」`text-main` | msg | 「前往安全层」`jade` | ✅ 整屏 |
| `run-lost` | 「败北」`cinnabar-dark` | msg | 「结算」`ink-soft` | ✅ 整屏 |

## 硬规则
- **`battle-lost` 的纸罩必须让开顶栏**（`BattleView.cs:3778`，offsetMax `-TopBarH`）。顶栏的「退出」是阵亡后唯一的离塔出口，罩住它玩家就卡在这一屏。`run-lost` 是终态、顶栏本身不绘制，所以整屏罩无碍 —— 两者的区别不是随手写的。
- **罩用 `scrim-paper` 不用 `scrim`。** 纸色罩保留战场可读，玩家看得见自己是怎么死的；压暗会把这一屏变成一块纯色板。
- 提示态（`floor-win` / `boss-win`）不带按钮，也不拦点击 —— 它们只是报一声，不该打断手速。

## 使用方提供
胜负、是否在塔内（决定文案与钮的去向）、msg 文案、复活回调（仅 `battle-lost`）。

## 与实现的差异（本系统记录，未改代码）
**败北要连点三屏，其中两屏几乎一样。** `DrawBattleSettle`（「败北……」+ 结算钮）→ 置 `RunLost` → `DrawRunEnd`（「败北」+ msg + 结算钮）→ `SettleTower`。画稿 `RunEnd.dc.html` 的意图明显是**一屏**（大字 + msg + 复活 + 结算）。告捷侧同样三连：「B O S S 已 破」→「本段告捷——字正！」→ 安全层「第 30 层告捷」。合并成一屏是现成的简化。
