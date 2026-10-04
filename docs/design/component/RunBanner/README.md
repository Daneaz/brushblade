段末横幅：一趟塔里每个「打完了」的时刻共用的整屏反馈，三种态只换文案与色。实现是 `BattleView.DrawRunEnd` / `DrawBattleSettle` / `ShowVictoryBanner`。

## 为什么单独立一张卡
三个方法、三种态（外加塔外兜底的 `run-won`），共用同一组常数（`BannerFont` 92 / `BannerMsgFont` 23 / `BannerPillH` 100）与同一套版面。它还定义了一条别处会用到的规矩：**自动消失的提示态整屏放行点击，需要玩家决策的态必须拦截 —— 但要让开顶栏。**

## 版面
整屏纸罩 `scrim-paper`（纸色 72%，**不压暗战场**）→ 垂直居中一列，gap 31（14.8pt）：

| 元素 | 字号 / 尺寸 | 备注 |
| --- | --- | --- |
| 大字 | 92（44pt）宋体 | 胜 `text-main`、负 `cinnabar-dark`；`boss-win` 也用 `cinnabar-dark` |
| msg | 23（11pt）`text-dim` | 只有 `battle-lost`（及塔外 `run-won`）有 |
| 复活徽章 | `Ui.AdBadge` 300×67（宋体） | 只有 `battle-lost` 有，且仅塔内、本次登塔还没复活过 |
| 主钮 | `Ui.PillButton` 400×100（191×48pt）、圆角 24、字号 36（17.2pt）、黑体 | 负 `ink-soft`；塔外 `run-won` 用 `cta` |

## 三态（文案均为字符串表原文）
| 态 | 大字 | 配套 | 钮 | 拦截 |
| --- | --- | --- | --- | --- |
| `floor-win` | 「本 层 告 捷」`text-main` | — | — | ❌ 停 1.2s + 淡出 0.3s 后自动进下一步，`blocksRaycasts=false` |
| `boss-win` | 「B O S S  已 破」`cinnabar-dark`（S 与「已」之间是两个空格） | — | — | ❌ 停 1.8s + 淡出 0.3s |
| `battle-lost` | 「败北」`cinnabar-dark` | msg「卒……墨锭一分不少,纪录保留。」+「看广告复活」徽章（msg 在上、徽章在下） | 「结算」`ink-soft`，一按直进登塔结算页 | ✅ **但纸罩让开顶栏 `TopBarH`** |

塔外（`_onExit == null`）的分支：多一态 `run-won`（`DrawRunEnd` 胜利支，整屏拦截）—— 大字「关卡通过——字正!」`text-main`、msg「通关结算:经验与墨锭入账。」、钮「返回地图」`jade`；`battle-lost` msg「死亡即结算,回地图重整旗鼓。」、钮「返回地图」、不出复活徽章。

**告捷不再单独一屏**（2026-10-04 用户拍板，与败北侧对称合并）：塔内 Boss 层战利品选完 / 跳过 / 换字后 `RunEngine.Phase` 落到 `RunWon`，`BattleView.Refresh` 开头直接 `_onRunEnded(true)` 进安全层（只触发一次），不再画「本段告捷——字正!」+「前往安全层」。安全层标题「安全层 · 第 N 层告捷」已说清告捷，原 msg 不挪。

**败北只有一屏**（2026-10-04 用户拍板两屏合一）：`DrawBattleSettle` 的「结算」钮走 `SettleDefeat` —— 先 `RunEngine.AdvanceAfterBattle()`（补记最后一击的墨锭、`Phase` 置 `RunLost`），紧接着 `_onRunEnded(false)` 进登塔结算页，不再经 `DrawRunEnd` 中转。`DrawRunEnd` 的败北支只留作 `Phase` 停在 `RunLost` 时的兜底，版式同 `battle-lost`（整屏罩、无复活徽章）。

## 硬规则
- **`battle-lost` 的纸罩必须让开顶栏**（`DrawBattleSettle` 里那块 `Scrim` 子物件 `offsetMax = (0, −TopBarH)`；外层 overlay 不挂 Image）。顶栏的「退出」是阵亡后唯一的离塔出口，罩住它玩家就卡在这一屏。大字那一列仍按**整屏**居中，不跟纸罩下移。塔外 `run-won` 是终态、顶栏本身不绘制，所以整屏罩无碍 —— 两者的区别不是随手写的。
- **罩用 `scrim-paper` 不用 `scrim`。** 纸色罩保留战场可读，玩家看得见自己是怎么死的；压暗会把这一屏变成一块纯色板。
- 提示态（`floor-win` / `boss-win`）不带按钮，也不拦点击 —— 它们只是报一声，不该打断手速。

## 使用方提供
胜负、是否在塔内（决定文案与钮的去向）、复活回调（仅 `battle-lost`）。msg 是固定文案，不带层数 / 墨锭参数。

## 与实现的差异
无。告捷侧已于 2026-10-04 合并，现流程：「B O S S  已 破」（自动淡出）→ Boss 层战利品页 → 安全层「安全层 · 第 N 层告捷」（其上可能再叠首破弹窗与升级弹窗）。
