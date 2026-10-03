主界面底部导航页签，四格等分、高 **92**（43.96pt）、格间距 **17**（8.12pt）；实现是 `MapView.NavTab`（由 `MapView.BuildNavBar` 排四格）。

## 结构
描边卡（`Ui.OutlinedPanel(parent, "Tab", palette.Bg, palette.Border, 19, 2f, out face)`：圆角 **19** → 9.08pt、描边 **2** → 0.96pt）+ 横排内容（间距 **15** → 7.17pt，居中）：
图标 **36**（17.20pt，`MapView.NavIcon`）→ 页签名宋体 **29**（13.86pt，前景色）→ 副文 **19**（9.08pt，`text-dim`）；
红点（`MapView.RedDot`）**14 方**（6.69pt）、`cinnabar` 圆、钉在右上角、距角 **16**（7.64pt），不拦点击。
按下染的是填充面 `face`，不是那条边线（`button.targetGraphic = face`）。

## 四格（左 → 右）

| 页签 | 图标 | 名 | 副文 | 红点判据 | 色板 |
| --- | --- | --- | --- | --- | --- |
| 收集 | `nav_deck` | 收集 | 「N 张」= 已拥有张数 | `CollectionHasRedDot`：有没看过的新字 **或** 有字能升 | `DeckTab`（水） |
| 图鉴 | `nav_bestiary` | 图鉴 | 「已解锁 / 全集」，全集 = `BestiaryView.CollectEnemies`（现为 48） | `BestiaryRules.HasUnclaimed`：有已解锁未查阅的条目 | `BestiaryTab`（木） |
| 技能 | `nav_perks` | 技能 | 「已点亮 / 全部」，全部 = `PerkRules.Nodes.Count`（现为 63） | `PerkRules.HasUpgradable`：有节点现在就能点 | `PerkTab`（心） |
| 商城 | `nav_shop` | 商城 | 「每日刷新」（固定文案） | `ShopRules.HasRedDot` | `ShopTab`（`shop-nav`） |

文案取 `map.nav.*`：分数一律写成「{unlocked} / {total}」，斜杠两侧带空格。

## 三支色同源
底 = `panel-paper` 往属性淡底走 35%；描边 = 淡底往前景压 22%；前景 = `<element>-fg`（`Theme.TabPalette.FromElement`）。
卡组=水、图鉴=木、技能=心、商城 = `shop-nav` 走 `TabPalette.FromAccent`（先往 accent 走 32% 得 soft，再同法派生）。
四组 `tab-*-bg` / `tab-*-border` / `tab-*-fg` token 与这两步 Lerp 逐字节对得上。

## 使用方提供
图标（Icons 组 `icon_nav_*`，按前景色着色；**PNG 缺失时什么都不画**，不回落汉字 —— `MapView.NavIcon` 里 `sprite == null` 直接 return）、
页签名、副文计数、红点判据（各页签各问各的 Core，见上表）。

## 注意
副文用 **`text-dim`**（#5D646F）—— 2026-09-21 从 `text-faint` 压深一档：原先压四格底只有 4.42 / 4.45 / 4.40 / 4.14:1，四格都差一点到 4.5；改后是 5.23 / 5.21 / 5.18 / 5.17。
`text-faint` 的 4.5 只保证 `paper` / `panel-paper` / `card-face` 三种底，页签底不在其列 —— 这是那一支最容易被误用的地方。
页签名对各自底色 7.4 / 7.0 / 7.8 / 4.5:1 全部达标。
