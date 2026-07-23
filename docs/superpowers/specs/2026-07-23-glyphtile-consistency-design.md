# 图鉴形象对齐战斗 & 技能牌化 — 设计

日期:2026-07-23 · 范围:Presentation 层视觉呈现,不碰 Core/Data。

## 目标

1. 怪物图鉴里怪牌展示的「形象」与战斗中一致(战斗:圆形头像·五行实色底·白色单字)。
2. 技能页从纯文字行改为以牌(GlyphTile 风格)展示,参考字库牌与怪牌。

## 现状

- 战斗怪物形象(`BattleView.cs:365-382`):`Theme.Circle` 实色底(`ElementColor(ApparentElement)`)+ 44pt 白色单字。小怪取 `Id[0]`,Boss 取 `Phases[PhaseIndex].Char`。
- 图鉴怪牌(`EnemyPreview.Tile`):圆角方牌 + 五行柔色底(`ElementSoft`)+ 柔色前景 + 完整词(`Id` 如「错字鬼」)+ 血攻。形状、配色、显示字全不同。
- 技能页(`PerkView`):`PerkDef`(Name=养元/金汤/博闻/一气,Effect=MaxHp/Shield/Library/Ap,无五行)渲染成纵向文字行 + 升级按钮。

## 设计

### 1. 共享「圆形字头像」原语

- 新增 `Ui.CircleGlyph(Transform parent, string face, Color faceColor, Color glyphColor, float diameter)`:画 `Theme.Circle` 实色底 + 单字,返回 GameObject(不含锚定/按钮,由调用方套壳)。
- 新增 `EnemyInfo.FaceChar(EnemyDef def, int phaseIndex)`:Boss → `Phases[phaseIndex].Char`,小怪 → `Id[0]`。收敛战斗与图鉴的取字规则到一处。
- `BattleView` 圆形头像改用 `Ui.CircleGlyph` + `EnemyInfo.FaceChar(def, PhaseIndex)`,保留原锚定/targeting 描边/点击按钮。保证战斗与图鉴同源不漂移。

### 2. 图鉴怪牌 `EnemyPreview.Tile` 重画

- 主视觉换成 `Ui.CircleGlyph`:`ElementColor(def.Element)` 实色底 + 白字 `FaceChar(def, 0)`。
- 外框:Boss 描金边、普通 `Shadow` 边(沿用现状)。
- 圆下方保留完整名(`Id`,`TextMain`)+ 血攻(`血{MaxHp} 攻{Attack}`,`TextDim`)。
- 未解锁 locked:灰底圆(`LockedBg`)+「?」(`LockGray`)+「未遇」。
- `EnemyPreview.Show` 弹窗顶部同样用新版 Tile/头像。

### 3. 技能牌 `PerkView` 重画

- 4 张牌排成 2×2 网格(替换现有纵向文字行)。
- 每张牌参考 GlyphTile:圆角方牌 + 主题色淡染底 + 主题色两字大名(TitleFont)+ `Lv{level}/{max}` + 下一级文案(`+{PerLevelValue}` / 「已满」)。
- 主题色映射(按 Effect / 技能):养元 `Cinnabar`、金汤 `Gold`、博闻 `SplitBlue`、一气 `Jade`。淡染底 = 主题色向 `CardWhite` 高比例 Lerp。
- 牌下按钮:解锁 / 升级 / 「需角色 {UnlockLevel} 级」(gate 置灰),逻辑与现 `BuildPerkRow` 完全一致,仅换外观。

## 验证

- 必跑 `prescompile` 离线编译(改了 Presentation);`coretests` 兜底(理应无影响)。
- 视觉由用户在 Unity/模拟器验收:图鉴怪牌与战斗头像一致、技能 2×2 牌配色正确、gate/升级/满级态正常。

## 实施顺序

1. `Ui.CircleGlyph` + `EnemyInfo.FaceChar`。
2. `EnemyPreview.Tile`/`Show` 改用之。
3. `BattleView` 头像改用之(去重取字逻辑)。
4. `PerkView` 牌化 + 主题色。
5. prescompile + coretests。
