# 削甲 · 状态图标

需求来源:session `card-detail-attribute-tips`(2026-10-21)转用户;依据 spec
`docs/superpowers/specs/2026-10-21-土系破甲归零-design.md` §2.2(削甲 = 金 / 火原扣点破甲改名)与 §3(显示与文案)。

- **破甲**(土系专属,持续期间护甲归零、Boss 减半)沿用现有 `icon_armorbreak`(描边盾 + 闪电裂),不动。
- **削甲**(金 / 火,护甲 −N 点、可叠加、各自计时)要一枚新图标:一眼和破甲分开,同属 `tools/icons/svg/` 那套
  64 画布、纯白、STROKE 6 圆头圆角 / FILL 的家族;用在战斗状态 chip(墨蓝 `ink-soft` 减益组)。

本稿**只出图与规格**,不改 `tools/icons` 管线、不写 Presentation、不动字符串表。

## 画板
- `Icons.dc.html`:三案比选 + 两枚现有参照;敌人格 chip 流(前排信息列,基准机 257 / 16:9 153,3 行)实尺寸与 × 4;
  减益组并排 × 3。控制条:候选 A / B / C · 敌人身上(只有削甲 / 只有破甲 / 破甲 + 削甲 / Boss 破甲 + 削甲 / 挤满)·
  削甲 chip 只图标 / 带点数 · PNG 缺失兜底(图标 /「薄」/「削」)。
- `Spec.dc.html`:推荐案 C 的 ICONS 片段与生成 SVG、五档显示尺寸、实现清单、chip 宽验算、文案 key。
- 数据 `xj.js`:现有图标从 `build_icons.ICONS` 原样抄出(2026-10-21)+ 三枚候选;`xj.css` 的 `:root` 从 `tokens.json` 原值抄录。

## 三案
| 案 | 意象 | 判断 |
| --- | --- | --- |
| A · 削角 | 实心盾右上一角被斜削下来、飞开 | 语义直,但碎角在 chip 实尺寸(18 单位 = 8.6pt,图形本体约 13.5 单位)下只剩两三像素,读出来像「缺角的甲」 |
| B · 刀痕 | 描边盾缺一角 + 一道刀口 | 与破甲同为描边盾,远看两者都是「空心盾 + 一道线」,区分度最差 |
| **C · 削层(推荐)** | 实心盾被削去顶上一层,那一层斜飞起 | 剪影独一份(盾牌家族里只有它中间有横向缺口);实心 = 护甲点数(与头行「甲」的 `defense` 同一轮廓),缺口 = 被削掉的点 —— 与破甲「描边盾裂开 = 归零」一实一空、一削一裂 |

盾牌家族现有:`shield` 描边(护盾,翠玉)、`defense` 实心(护甲点数)、`armorbreak` 描边 + 闪电、`immunity` 描边 + 勾、`block` 描边 + 尖括号、
`guard` / `hurtshield` 小实心盾 + 别的构件。C 是唯一「被切开的实心盾」,不撞。

## 推荐案 C 规格
```python
    # 削甲(2026-10-21,金 / 火扣点破甲改名):实心盾 = 护甲点数,
    # 顶上一层被削起斜飞 —— 与 armorbreak 的「描边盾 + 闪电裂(归零)」一实一空
    "armorshred": (
        f'<path {FILL} d="M10 27H54v6c0 13-11 23-22 27-11-4-22-14-22-27z"/>'
        f'<path {FILL} transform="rotate(-8 31 14)" d="M31 4l22 8v8H9v-8z"/>'
    ),
```
- key 建议 `armorshred`(PNG `icon_armorshred.png`);斜飞那片用 `transform`,`guard` / `hurtshield` 已有先例,rsvg-convert 支持。
- 尺寸:chip 内图标 `Icons.Size` 18 单位(8.6pt),chip 高 20(9.56pt)、字 15、padX 10、间距 4 —— 全是 `BattleView` 现值,本稿不改。
- 底色:墨蓝 `ink-soft`(`BattleChipColor` 落 `_ => InkSoft`,与破甲同组),前景白。

## 需要拍板的取舍
1. **选哪一案**:推荐 C。
2. **削甲 chip 带不带点数**:推荐**只图标**(28 宽)。理由:2026-09-02 拍板「数字只留给跟随回合消亡的 DoT / HoT」,削甲点数挂着期间恒定;
   头行「甲」chip 读 `DisplayedEnemyDefense`,已经是扣完的值,再标点数是重复信息;带「12」的削甲 chip(47.5 宽)与「甲 18」同宽同色,
   并排像两枚护甲。点数放详情弹窗(「削甲 12」)。spec §3 写的「过渡期同一枚图标 + 点数」在新图标落地后撤掉。
3. **兜底字**:需求建议「削」,但 `Icons.Glyphs` 里 `skillcut`(大招削弱)已经用「削」,PNG 缺失时两枚徽章一模一样。
   改荐**「薄」**(甲被削薄)—— 已在两支子集字体里,不用重跑子集。若坚持「削」,就要把 `skillcut` 的兜底字换掉。

## 两档屏宽验算(逻辑单位)
| 项 | 基准机 | 16:9 | 结论 |
| --- | --- | --- | --- |
| 敌前排信息列(battlegrid 稿) | 257 · 3 行 | 153 · 3 行 | — |
| 第一行:攻「12」47.5 + 甲「18」47.5 + 一枚纯图标 28 + 间距 2 × 4 | 131 | 131 | 16:9 余 18(列宽 − 4 的边距后) |
| 纯图标 chip 每行 | 8 枚 | 4 枚 | 挤满局面(破甲、削甲 + 六种减益)16:9 仍在 3 行内,不出「+N」 |
| 削甲带点数「12」 | 47.5 | 47.5 | 16:9 第一行放不下第三枚,减益整体后挪一行 |

图标是定宽件,与屏宽无关;屏宽只影响 chip 流排几行,上表是最窄档。

## 字符串表 key(本稿不写字符串表,实现阶段按 spec §3 定文案)
| key | 状态 |
| --- | --- |
| `status.armorshred.name`「削甲」/ `status.armorshred.desc` | 新增 |
| `term.armor_shred.name` / `.desc` | 新增 |
| `char.effect.armorshred` / `char.effect.augment.target.armorshred` | 新增 |
| `status.armorbreak.*` / `term.armor_break.*` / `char.effect.armorbreak` | 保留,改文案(spec §3) |

## 实现时(不在本稿范围)
- 图标三处对账:`build_icons.ICONS`、`test_icons.EXPECTED`、`Icons.Glyphs`;跑 `python3 tools/icons/build_icons.py`;`.meta` 复制换 guid。
- 战斗状态三处接线:`StatusText.Of`、`BattleView` chip 列表(破甲之后紧跟;召唤物 `AddSummonStatusChips` 同口径)、`CharInfo.EffectsText`。

## 回填记录
- 2026-10-21 · 出稿(待审)。
