主界面底部导航页签，四格等分、高 **92**（44pt）、栏内间距 **17**；实现是 `MapView.NavTab`（`MapView.cs:572`）。

## 结构
描边卡（`Ui.OutlinedPanel`，圆角 **19**、描边 **2**，`MapView.cs:576`）+ 横排内容（间距 **15**）：
图标 **36**（`MapView.cs:606–607`）→ 页签名宋体 **29** → 副文 **19**；
红点 **14 方**、钉在右上角、距角 **16**（`MapView.cs:619–628`）。
按下染的是填充面 `face`，不是那条边线（`MapView.cs:578`）。

## 三支色同源
底 = `panel-paper` 往属性淡底走 35%；描边 = 淡底往前景压 22%；前景 = `<element>-fg`（`Theme.cs:216–221`）。
卡组=水、图鉴=木、技能=心、商城 = `shop-nav` 走 `FromAccent`（先往 accent 走 32% 得 soft，再同法派生，`Theme.cs:226–229`）。
四组 `tab-*-bg` / `tab-*-border` token 与这两步 Lerp 逐字节对得上。

## 使用方提供
图标（Icons 组 `icon_nav_*`，按前景色着色；**PNG 缺失时什么都不画**，不回落汉字 —— `MapView.cs:596`）、
页签名、副文计数、红点判据（各页签各问各的 Core，`MapView.cs:557–567`）。

## 注意
副文用 **`text-dim`**（#5D646F，`MapView.cs:588`）—— 2026-09-21 从 `text-faint` 压深一档，见下。
tokens 里没有 `lock-gray` 这个名字 —— `Theme.LockGray` 对应的 token 就是 `text-faint`。

⚠ **对比度**：页签名对各自底色 7.4 / 7.0 / 7.8 / 4.5:1 全部达标；
**副文 2026-09-21 从 `text-faint` 改成 `text-dim`**：原先压四格底只有 4.42 / 4.45 / 4.40 / 4.14:1，四格都差一点到 4.5；改后是 5.23 / 5.21 / 5.18 / 5.17。`text-faint` 的 4.5 只保证 `paper` / `panel-paper` / `card-face` 三种底，页签底不在其列 —— 这是那一支最容易被误用的地方
（`text-faint` 的保证只覆盖 `paper` / `panel-paper` / `card-face`）。
这条不在根 README 的实现侧例外清单里，2026-09-21 新查出。
