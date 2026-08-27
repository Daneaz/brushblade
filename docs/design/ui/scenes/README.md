# UI 基线 · 六屏

《字·斗》全部游戏界面的**视觉与交互基线**。与代码不一致时，先确认是稿子过时还是实现走样——
两边都可能，但任何一边动了都要在这里留痕。

前身是 `docs/design/frame/字斗设计板.dc.html`（已移除），BattleView 的部件池注释曾引它。

## 设备基准

iPhone 16 Pro Max 横屏 **932 × 430pt**（@3x = 2796 × 1290px），锁横屏。
安全区左右各 59pt、底部 21pt（Home Indicator）——所有可点元素都在这个框内。

⚠ `CanvasScaler` 是 `referenceResolution 1600×900` + `match = 1`（按高匹配），
所以这台机上逻辑画布实为 **1950 × 900**：横向比参考宽 350，纵向一分没多。
`1pt = 2.093` 逻辑单位。详见 `Device.dc.html`。

## 画板

| 文件 | 屏 | 对应实现 |
| --- | --- | --- |
| `Home.dc.html` | 主界面 | `MapView.cs` |
| `Main.dc.html` | 卡组（可交互） | `CollectionView.cs` |
| `Battle.dc.html` | 战斗（可交互） | `BattleView.cs` |
| `Bestiary.dc.html` | 怪物图鉴（可交互） | `BestiaryView.cs` |
| `Perks.dc.html` | 技能 | `PerkView.cs` |
| `Shop.dc.html` | 商城 | `ShopView.cs` |
| `Device.dc.html` | 设备基准与适配 | 规范，无对应实现 |
| `CardStates.dc.html` | 字牌状态 | `Ui.GlyphTile` / `CardFrames` |
| `StatMapping.dc.html` | 详情页数值口径 | `CharInfo` |

`canvas.json` 是画布布局（位置、分页、便签）；便签里记着每处改动的理由与待办，
**别只看画面不看便签**。`base.css` 是六屏共用的令牌（色板取自 `Theme.cs`，
安全区、触控与字号阶梯）。`cards.min.json` 是从 `chars.json` 生成的字表快照，
仅供稿子填真实数据。

## 重新出图

在本目录下跑（`<skill>` = Claude Code 的 design skill 目录）：

```
node <skill>/seed-canvas.mjs \
  --template <skill>/payload.template.html \
  --out brushblade-card-collection.html --title "字·斗 界面重设计" \
  --artboard Main.dc.html --artboard Home.dc.html --artboard Battle.dc.html \
  --artboard Bestiary.dc.html --artboard Perks.dc.html --artboard Shop.dc.html \
  --artboard Device.dc.html --artboard CardStates.dc.html --artboard StatMapping.dc.html \
  --canvas canvas.json
```

产出的 `.html` 内联了整个编辑器 payload（2.7MB），**不入 git**（见 `.gitignore`）。
浏览器里首次渲染要 30 秒以上，不是卡住了。

## 稿子先行、代码待跟进

这三项在稿上已经画成，实现还没有：

- **每排 4 格位**：`Targeting.RowCapacity` 3→4、`BattleEngine.EnemyCap` 6→8、
  `SummonCap` 6→8、`FrontRowSize` 3→4。会连带影响 `Targeting` 里
  Cleave / Skewer / Sweep 的按列取目标，属平衡改动。
- **敌人护盾**：`EnemyState` 没有 `Shield` 字段。要加字段 + 在 `DamageEnemy` 的护甲减法之后、
  扣血之前插一段吸收，并补 `ShieldBroken` 事件。另有一条待拍板：
  「相克即破甲」那一击目前无视护甲，要不要连护盾一起穿？
- **状态图标扩到 29 枚**：`tools/icons/build_icons.py` 的 ICONS 表与 `Icons.cs` 的 Glyphs
  兜底表要同步（`test_icons.py` 守着两边一一对应），新增兜底汉字须重跑
  `python3 tools/fonts/subset_fonts.py`。

## 顺带查出的两处不一致

- `Tutorial.DemoChar` 是「刺」，而 `strings.zh-CN.json` 的四条 `battle.hint.tutorial.*`
  写的是【剑】——新手会被指去点一张手上没有的牌。
- `chars.json` 的 `Pinyin` / `Gloss` 一条都没填，字段与 `ConfigLoader` 都在，
  卡面拼音位现在是空的。稿里 72 个字的拼音释义是补的，需要过一遍再写进详表。
