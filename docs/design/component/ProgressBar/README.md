细条进度；实现是 `Ui.Bar`：轨道恒为 **`paper-dim`**、圆角 **10**，填充同圆角，**高度由调用点给**。
`Ui.Bar` 只吃比例，叠字由调用方往返回的节点上加（血条走 `BattleView.HpBar`，行动条走 `BattleView.ActionBar`）。

| 用处 | 高 | 填充 | 调用点 |
| --- | --- | --- | --- |
| 经验（主界面角色栏） | 9 | `gold` | `MapView.BuildHeroPanel` |
| 本趟血量（书塔） | 10 | `cinnabar-dark` | `MapView.BuildTowerPanel` |
| 层段进度 | 13 | 当前段 `cinnabar`；其余段（已过段填满、未至段留空）`text-faint` | `MapView.BuildBands` |
| 角色页 · 经验 | 10（定宽 370） | `gold` | `CharacterView.BuildProfile` |
| 角色页 · 里程碑轨道 | 6（不参与布局，钉在里程碑钉下方） | `gold`（已达成那一段） | `CharacterView.BuildMilestones` |
| 角色页 · 保底进度 | 10 | 该档的稀有度色 `rarity-*` | `CharacterView.PityBlock` |
| 图鉴收集进度 | 15 | **该层段的属性色**（Boss 行 `gold`） | `BestiaryView.BuildProgressBar`，色由 `BuildOverview` 传入 |
| 敌人血条 | 19 | `cinnabar`，叠 13 号血值白字 + `ink` 描边 | `BattleView.DrawEnemies` → `HpBar` |
| 召唤物血条 | 17 | `done-green`（友军不用红），同样叠血值 13 | `BattleView.DrawSummons` → `HpBar` |
| 执笔人血条 | 17 | `cinnabar`，**不叠字**（数字在头行） | `BattleView.DrawPlayerStats` |
| 行动条 · 敌人 | 6 | `ink-soft`，>80% 转 `cinnabar`，不叠字 | `BattleView.DrawEnemies`（直调 `Ui.Bar`） |
| 行动条 · 召唤物与执笔人 | 15 | `ink-soft`，>80% 转 `done-green`，叠 13 号百分比 | `BattleView.DrawSummons` / `DrawPlayerStats` → `ActionBar` |
| 单位详情弹窗 · 血 / 盾 / 行动 | 12 / 5 / 5 | `cinnabar` / `rarity-gold` / `ink-soft` | `UnitSheet.BuildBars` |
| 字牌牌脚 · 升级材料 | 牌高 × 4/128 | 可升 `jade`，否则 `text-faint` | `CardBadges.Foot`（属 CardFace 卡） |

叠字：`Theme.TitleFont` 宋体白字，`Outline` 色 `ink`、偏移 1.2。血条字号默认 `clamp(高×0.7, 10, 13)`，召唤格与行动条显式传 13（`UnitBarLabelFontSize`）。

战场上的护盾条 2026-09-05 整体移除：盾量只在立绘左下角的金色角标上（`DrawPlayerStats` / `DrawSummons` 的盾角标）。
**详情弹窗那条 5 高的盾条是留下来的**（`UnitSheet.BuildBars`），它是另一套更细的读数条，不受那次移除影响。

⚠ 敌人/召唤物/行动条那三条叠的白字，只在**填充段**上达标（5.3 / 4.6 / 8.4:1）；
条走到低比例时白字压的是 `paper-dim` 轨道，只有 1.4:1 —— 可读性全靠那圈 1.2 的 `ink` 描边。
根 README 的「靠 1–2px 深色描边保证可读」说的就是这里。

## 使用方提供
当前值与上限（`Ui.Bar` 只吃比例），血条可选叠字与叠字字号（`labelFontSize`，2026-09-19 为召唤格 17 高的条而加）。
