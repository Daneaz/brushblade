# 新到手的牌 · 高亮打磨（2026-08-30）

拆出的部件、合出的字、选中的战利品、奇遇拿到的东西——**2.4 秒内要能在一排牌里被认出来**，
然后安静退场。第一版做成了 `Rounded(8)` + `fillCenter=false` 的 10px 实边，粗且盖住牌面，
56 见方的部件牌被吃掉大半。这两块板是重做时的选型与规格。

| 画板 | 内容 |
| --- | --- |
| `Main.dc.html` | 六种方案并排：A 现状实边 / B 细描环 / **C 墨晕（选中）** / D 钤印 / E 挑角 / F 提纸 |
| `HaloSpec.dc.html` | 墨晕的落地规格：贴图剖面、三种属性色的成品、时序、与现状的逐项差 |

## 为什么是 C

判据两条：① 在 56 见方的部件牌上也成立；② 不与稀有度框 / 选中描环 / 同源徽标抢位置。

发光是唯一不占版面的强调方式——牌面、四角、描线一样都不动，只在牌**之外**加一圈会呼吸的光；
而「洇」本来就是墨在宣纸上自己会做的事。落选的两个也值得记：

- **D 钤印**可读性其实最强（朱印是水墨里最正统的「新收之物」标记），但部件牌四角已被同源徽标占满。
- **B 细描环**最省事，可它和稀有度框、选中描环是同一种语言，三层线叠在一起会读混。

## 实现落点

- `Theme.Halo(radius)` — 径向渐变九宫格：牌内全透明 → 牌沿冲到 1 → 向外 `Theme.HaloPad`(10px)
  按**平方**衰减到 0（线性衰减看着像硬边框）。几何与 `Theme.Rounded` 同一套，差别是圆角矩形
  内缩 `HaloPad`，腾出纹理边缘给洇开的尾巴。
- `Juice.Glow(tile, 属性色, 到期时刻)` — Image 挂牌下、`SetAsFirstSibling`、外扩量与 `HaloPad`
  一致（对不上的话渐变峰值就不落在牌沿）。呼吸**只改 alpha**（.26↔.55，0.75s 一个来回），
  末段乘 remain 收暗——尺寸一动，一排牌就会跟着挤。
- `BattleView._freshGlyphs` — 字 → 到期时刻。记时刻不记时长：BattleView 全量重绘，
  存时长的话每次重绘都会重置倒计时。

重新出图（在本目录跑）：

```
node <design skill>/seed-canvas.mjs --template <skill>/payload.template.html \
  --out card-highlight-study.html --title "新到手的牌 · 高亮打磨" \
  --artboard Main.dc.html --artboard HaloSpec.dc.html --canvas canvas.json
```
