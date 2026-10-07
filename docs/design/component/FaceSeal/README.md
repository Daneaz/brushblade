手牌朝上那一面的记号：一枚实底白字的方印；实现是 `Ui.FaceSeal(parent, def, face, size)`（`Ui.cs`），在战斗字库行由 `Ui.GlyphTile(..., face:)` 画在拼音位。设计稿：`drafts/traits`（`traits.css` 面印段、`HandFlip`）。

同一枚印出现在手牌、详情、升级预览、飘字四处——玩家只认这一个记号。本卡只管印本身；本期（Plan E1）只有手牌用到。

## 画法
- **一式**：实底 + 白字，宋体（`TitleFont`）粗体，圆角 = 边长 × 0.19，字高 = 边长 × 0.7。
- **攻击面**：`ink` 底白「攻」，不分五行（与彩色五行面一眼分开）。
- **五行面**：`<el>-glyph` 底白面字（`Theme.GlyphColor(def.Element)`）：火「燃」、金「铠」、水「润」、土「固」、木「生」。心系兜底「心」（字符串表 `face.seal.heart`，稿上没有，两面字目前也没有心系）。
- 面字是 UI 记号，进字符串表：`face.seal.attack` / `face.seal.fire` / `.metal` / `.water` / `.earth` / `.wood` / `.heart`；另备 `face.name.attack`「攻击」、`face.name.feature`「五行面」、`face.side.front`「正」、`face.side.back`「背」供翻面钮使用。

## 尺寸档（pt × 2.093 = 逻辑单位）
| 档 | 稿 pt | 逻辑单位 | 字号（逻辑单位） | 用在哪 |
| --- | --- | --- | --- | --- |
| xs | 10 | 21（`Ui.FaceSealXs`） | 15 | 手牌（战斗字库行） |
| s | 13 | 27 | 19 | 稿：拆合台翻面钮 |
| m | 17 | 36 | 25 | 稿：详情 |
| l | 24 | 50 | 34 | 稿：升级预览 |

实现里 `size` 是调用方传的边长；`GlyphTile` 传 `min(21, 牌高 × 0.24)`，牌特别矮时按拼音带高收。

## 手牌里的位置
- 面印**占拼音位**：锚 0.06–0.30，水平居中；`face != null` 时拼音不再画。部件池的牌没有面，不传 `face`，仍印拼音。
- 朝上为五行面时，牌面内层底色换本系 soft 色（`Theme.ElementSoft`，即 `<el>-soft`）：框素材自带底色，实现是 `Image.color` 相乘（相乘不覆盖，框纹与稀有度色相保留）；攻击面与 `face == null` 保持原样。未拥有（`locked`）不染。
- `face == null` 时 `GlyphTile` 与改动前逐字节相同，既有八处调用不传。

## 配色出处
`ink` → `Theme.Ink`；`<el>-glyph` → `Theme.GlyphColor`；`<el>-soft` → `Theme.ElementSoft`；白字 `Color.white`。对比度：白压 `<el>-glyph` 均 ≥ 4.5:1（见 `system/` 配色规则）。
