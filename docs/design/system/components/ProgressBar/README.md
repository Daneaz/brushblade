细条进度；实现是 `Ui.Bar`（`Ui.cs:590`）：轨道恒为 **`paper-dim`**、圆角 **10**，填充同圆角，**高度由调用点给**。
`Ui.Bar` 只吃比例，叠字由调用方往返回的节点上加。

| 用处 | 高 | 填充 | 调用点 |
| --- | --- | --- | --- |
| 经验（角色栏） | 9 | `gold` | `MapView.cs:193` |
| 本趟血量（书塔） | 10 | `cinnabar-dark` | `MapView.cs:358` |
| 层段进度 | 13 | 当前段 `cinnabar`；未到达 `text-faint` | `MapView.cs:402` |
| 图鉴收集进度 | 15 | **该层段的属性色**（Boss 行 `gold`） | `BestiaryView.cs:630`，色由 `603–604` 传入 |
| 敌人血条 | 19 | `cinnabar`，叠 13 号血值白字 + `ink` 描边 | `BattleView.cs:2744`（`HpBar` 见 `551`） |
| 召唤物血条 | 17 | `done-green`（友军不用红），同样叠血值 13 | `BattleView.cs:1945` |
| 执笔人血条 | 17 | `cinnabar`，**不叠字**（数字在头行） | `BattleView.cs:1658` |
| 行动条 · 敌人 | 6 | `ink-soft`，>80% 转 `cinnabar`，不叠字 | `BattleView.cs:2761` |
| 行动条 · 召唤物与执笔人 | 15 | `ink-soft`，>80% 转 `done-green`，叠 13 号百分比 | `BattleView.cs:1952` `1668`（`ActionBar` 见 `578`） |
| 单位详情弹窗 · 血 / 盾 / 行动 | 12 / 5 / 5 | `cinnabar` / `rarity-gold` / `ink-soft` | `UnitSheet.cs:233` `240` `243` |
| 字牌牌脚 · 升级材料 | 牌高 × 4/128 | 可升 `jade`，否则 `text-faint` | `CardBadges.cs:174`（属 CardFace 卡） |

战场上的护盾条 2026-09-05 整体移除：盾量只在立绘左下角的金色角标上（`BattleView.cs:1647` 等）。
**详情弹窗那条 5 高的盾条是留下来的**（`UnitSheet.cs:240`），它是另一套更细的读数条，不受那次移除影响。

⚠ 敌人/召唤物/行动条那三条叠的白字，只在**填充段**上达标（5.3 / 4.6 / 8.4:1）；
条走到低比例时白字压的是 `paper-dim` 轨道，只有 1.4:1 —— 可读性全靠那圈 1.2 的 `ink` 描边
（`BattleView.cs:563–566`）。根 README 的「靠 1–2px 深色描边保证可读」说的就是这里。

## 使用方提供
当前值与上限（`Ui.Bar` 只吃比例），血条可选叠字与叠字字号（`labelFontSize`，2026-09-19 为召唤格 17 高的条而加）。
