手牌朝上那一面的记号：一枚实底白字的方印；实现是 `Ui.FaceSeal(parent, def, face, size)`（`Ui.cs`），在战斗字库行由 `Ui.GlyphTile(..., face:)` 画在拼音位。设计稿：`drafts/traits`（`traits.css` 面印段、`HandFlip`）。

同一枚印出现在手牌、详情、升级预览、飘字四处——玩家只认这一个记号。本卡只管印本身。手牌 2026-10 Plan E1 落地；字卡详情与升级预览的 s / m / l 三档和「两」印 2026-10-12 随 Plan E2 落地（稿 `drafts/traits` 的 `CardSheet` / `CardSheetBattle` / `UpgradePreview`）。

## 画法
- **一式**：实底 + 白字，宋体（`TitleFont`）粗体，圆角 = 边长 × 0.19，字高 = 边长 × 0.7。
- **攻击面**：`ink` 底白「攻」，不分五行（与彩色五行面一眼分开）。
- **五行面**：`<el>-glyph` 底白面字（`Theme.GlyphColor(def.Element)`）：火「燃」、金「铠」、水「润」、土「固」、木「生」。心系兜底「心」（字符串表 `face.seal.heart`，稿上没有，两面字目前也没有心系）。
- **两面通用**：`ink-soft` 底白「两」（稿 `.seal.both`）。只用在「这一条两面都有」的地方 —— 升级预览「新解锁 · 两面」小卡（字卡详情已无两面条，spec B）；不出现在手牌上（手牌总有一面朝上）。
  与 PartsAndGraft 的「拆」「合」同底色：三枚都不是某一面，墨蓝底统一表示「不属于攻击 / 五行面」。实现是 `Ui.FaceSeal(parent, text, color, size)` 重载（`Ui.cs:797` 一族），三枚共用；字符串表 `face.seal.both`，取字用 `Ui.FaceSealBothText()`。
- 面字是 UI 记号，进字符串表：`face.seal.attack` / `face.seal.fire` / `.metal` / `.water` / `.earth` / `.wood` / `.heart`；另备 `face.name.attack`「攻击」、`face.name.feature`「五行面」、`face.side.front`「正」、`face.side.back`「背」供翻面钮使用。

## 尺寸档（pt × 2.093 = 逻辑单位）
| 档 | 稿 pt | 逻辑单位 | 字号（逻辑单位） | 用在哪 |
| --- | --- | --- | --- | --- |
| xs | 10 | 21（`Ui.FaceSealXs`） | 15 | 手牌（`BattleView.cs:6069`）、拆合台选中牌（`BattleView.cs:4470`）、升级预览差行前缀（`CollectionView.cs:1057`）、升级预览两面条分面小句前缀（`CardSheetParts.BodyLine`）；字卡详情不再用 xs 印（全平铺后面由左右栏区分）；稿：TriggerFx 飘字签 |
| s | 13 | 27（`Ui.FaceSealS`） | 19 | 升级预览「下一级解锁」预告框（`CollectionView.cs:1024`） |
| m | 17 | 36（`Ui.FaceSealM`） | 25 | 字卡详情战斗版面头（`CardSheetParts.Sizes.BattleSheet`）与头部牌印、升级预览新解锁小卡卡头（`CollectionView.cs:967`，含「两」） |
| l | 24 | 50（`Ui.FaceSealL`） | 34 | 字卡详情铺满版面头（`CardSheetParts.Sizes.Tall`） |

实现里 `size` 是调用方传的边长；`GlyphTile` 传 `min(21, 牌高 × 0.24)`，牌特别矮时按拼音带高收。

## 手牌里的位置
- 面印**占拼音位**：锚 0.06–0.30，水平居中；`face != null` 时拼音不再画。部件池的牌没有面，不传 `face`，仍印拼音。
- 朝上为五行面时，**只把牌面内窗**换成本系 soft 色（`Theme.ElementSoft`，即 `<el>-soft`）；稀有度边框与花纹保持原色（稿 `.htile.back` 只换背景）。实现是框 Image 保持白，在内窗（与内容区同一组 `CardFrames.ContentInset` 让位）垫一块圆角色块 `FeatureTint`，层序在属性层之下。攻击面、`face == null`、未拥有都不建这块。
- `face == null` 时 `GlyphTile` 与改动前逐字节相同，既有八处调用不传。

## 配色出处
`ink` → `Theme.Ink`；`<el>-glyph` → `Theme.GlyphColor`；`<el>-soft` → `Theme.ElementSoft`；白字 `Color.white`。对比度：白压 `<el>-glyph` 均 ≥ 4.5:1（见 `system/` 配色规则）。
