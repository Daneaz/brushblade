# 破甲 / 削甲 · 图标与头行护甲格

需求来源:session `card-detail-attribute-tips`(2026-10-21)转用户;依据 spec
`docs/superpowers/specs/2026-10-21-土系破甲归零-design.md` §2(破甲 = 土系专属,护甲归零 / Boss 减半;削甲 = 金 / 火扣点,可叠)与 §3。

Core 已落地(main):`StatusKind.Sunder` = 破甲,`StatusKind.ArmorBreak` = 削甲(旧枚举名保留,文案已改「削甲」)。
本稿**只出图与规格**,不改 `tools/icons` 管线、不写 Presentation、不动字符串表。状态见下「拍板」。

## 画板
- `Icons.dc.html`:两枚图标 + 护甲 / 旧破甲参照;头行护甲格七种局面 × 两档屏宽实尺寸 + 16:9 × 3。
- `Detail.dc.html`:详情弹窗里怎么看回合(三个例子:Boss 破甲 + 削甲两层、杂兵削甲两层、杂兵破甲);玩家侧规则。
- `Spec.dc.html`:两枚 ICONS 片段、显示尺寸、规则表、实现清单、两档屏宽。
- 数据 `xj.js`:`CUR` = `build_icons.ICONS` 原样抄出(含将被替换的旧 `armorbreak`),`NEW` = 本稿两枚;`armorCell()` 是护甲格规则的可执行版。
  `xj.css` 的 `:root` 从 `tokens.json` 原值抄录。

## 两枚图标(64 画布、纯白 FILL,同 `defense` 实心盾轮廓长出来)
| key | 状态 | 意象 | 对应 Core |
| --- | --- | --- | --- |
| `armorshred` | 新增 | **A 削角**:实心盾右上一角被斜削下来、飞开 —— 少了几点,盾还在 | `StatusKind.ArmorBreak`(削甲) |
| `armorbreak` | **替换** | 实心盾从中间锯齿裂成两半、两片向外倒 —— 护甲没了 | `StatusKind.Sunder`(破甲) |

一眼区分:削甲是「一整块缺个角」,破甲是「对称的两片」;剪影一个偏一侧、一个左右对开。旧破甲「描边盾 + 闪电裂」作废。

```python
    # 破甲(2026-10-21 重画,土系专属:护甲归零 / Boss 减半):实心盾从中间
    # 锯齿裂成两半、两片向外倒。替换旧的「描边盾 + 闪电裂」
    "armorbreak": (
        f'<path {FILL} transform="translate(32 33) scale(.86) translate(-32 -33) '
        f'translate(-4 2) rotate(-12 21 52)" d="M32 6L27 19 35 29 27 40 33 50 32 60'
        f'c-11-4-22-14-22-27V14z"/>'
        f'<path {FILL} transform="translate(32 33) scale(.86) translate(-32 -33) '
        f'translate(4 2) rotate(12 43 52)" d="M32 6l22 8v19c0 13-11 23-22 27'
        f'L33 50 27 40 35 29 27 19z"/>'
    ),
    # 削甲(2026-10-21 新增,金 / 火扣点):实心盾右上一角被斜削下来、飞开
    "armorshred": (
        f'<path {FILL} d="M29 7l6 2.2L51 31v3c0 13-11 23-22 27-11-4-22-14-22-27V15z"/>'
        f'<path {FILL} d="M40 5.6L56 11.4v16z"/>'
    ),
```
(片段已与稿里渲染用的数据逐字对过。只用 path + transform,`guard` / `hurtshield` 已有 transform 先例。)

兜底字:`armorshred` →「薄」(用户拍板;「削」已被 `skillcut` 占用),`armorbreak` 仍是「破」。

## 新显示规则:头行护甲格本身变形(用户 2026-10-21 拍板)
敌人头行「射程 + 攻击」之后那枚护甲 chip(墨蓝 `ink-soft`、白图标,外形与尺寸不变)按下表换图标、换数;
**状态 chip 区不再放破甲 / 削甲**;回合只在详情里看。数字一律读 Core `DisplayedEnemyDefense`(破甲先取 0 / Boss 取一半,再扣削甲,最低 0)。

| 身上 | 图标 | 数字 |
| --- | --- | --- |
| 无 | `defense` | 护甲 |
| 削甲 | `armorshred` | 剩余护甲(削到 0 也印「0」) |
| 破甲 · 杂兵 | `armorbreak` | 不印(纯图标 chip) |
| 破甲 · Boss | `armorbreak` | 剩余值(减半后) |
| 破甲 + 削甲 | `armorbreak` | 杂兵不印;Boss 印「减半再扣削甲」 |
| 原本无甲(基准 0) | 不出格 | 同现行 |

- Boss / 精英 / 头领竖块:护甲 chip 在右栏,同规则。
- 详情弹窗:「甲」数值格用同一枚图标 + 剩余值,注释沿用「基准 N · 破甲 −x · 削甲 −y」;状态列表里破甲一行「剩 N 回合」,削甲**每层一行**带点数、各自「剩 N 回合」。
- 玩家侧(敌人对玩家施加,目前无配置):玩家栏护甲格同规则;破甲按杂兵口径不印数(`EffectivePlayerDefense` 归零);回合在玩家详情看。

## 两档屏宽验算(逻辑单位)
| 位置 | 基准机 | 16:9 | 结论 |
| --- | --- | --- | --- |
| 敌前排信息列 | 257 | 153 | 最宽一行 攻「999」55.75 + 甲「999」55.75 + 4 = 115.5 ≤ 149,余 33.5 |
| Boss 竖块右栏(≤ 2 行) | 211 | 107 | 两枚三位数 115.5 > 103 折两行(现行即如此) |
| 杂兵破甲纯图标 chip | 28 | 28 | 比原「甲 30」47.5 还窄 |
| 状态 chip 区 | — | — | 少了破甲 / 削甲两枚,挤满时少丢一枚 |

chip 外形不变、只换图标,两档屏宽的排行与现行一致,不需要 `HScrollFill`。

## 仍需拍板
1. **新破甲图标**(裂成两半)的样子 —— 本次改稿的重点。
2. **削到 0 时**:削甲图标 +「0」照出(推荐,玩家能看到「被削光了」);还是像现在一样数到 0 就不出格。
3. **原本无甲的敌人**被挂破甲 / 削甲:不出格(代拍,同现行;挂了也没有可破的甲)。
4. ~~削甲时长~~:已更正 —— Core 削甲是限时的(`BattleEngine` 的 ArmorBreak case:`TurnsLeft = Math.Max(1, effect.Turns)`,
   2026-09-08 起,每层各自计时);`EffectDef` / `StatusEffect` 注释里的「本场持久」是过时注释,实现时一起改。不再需要拍板。
5. 封禁(Silence)也会让护甲归零 / 减半:本稿不改,头行仍是 `defense` 图标 + 剩余值,杂兵归零时不出格。

## 实现清单(不在本稿范围)
- ⚠ key 与 StatusKind 交叉:Sunder → `"armorbreak"`(新图)、ArmorBreak → `"armorshred"`。按玩家看到的名字对 key,别按枚举名。
- 图标三处对账:`ICONS`(改 `armorbreak`、加 `armorshred`)、`test_icons.EXPECTED`、`Icons.Glyphs`;跑 `python3 tools/icons/build_icons.py`;新 PNG 的 `.meta` 复制换 guid。
- `BattleView`:头行护甲 chip 按上表;删掉状态 chip 区里 Sunder / ArmorBreak 两枚(敌人格、玩家栏、召唤物)。
- `StatusText.Of`:ArmorBreak 的图标 key 改 `"armorshred"`;时长由「本场持久」改走 `Duration(turnsLeft)`(「剩 N 回合」)。

## 字符串表
无新增(护甲格不印字;`status.sunder.*` / `status.armorbreak.*` 已在字符串表里)。兜底字「薄」已在子集里。

## 拍板(2026-10-21,用户)
全部按推荐,状态 `已拍板 · 待实现`:破甲新图(裂成两半)通过;削到 0 照印「0」;原本无甲的敌人不出格;封禁不动。

## 回填记录
- 2026-10-21 · 出稿(待审):三案比选(A 削角 / B 刀痕 / C 削层,推荐 C),削甲 chip 只图标进状态区,兜底「薄」。
- 2026-10-21 · 改稿(仍待审)· 改了什么:削甲定 **A 削角**、兜底「薄」;**破甲重画**为实心盾裂成两半,取代「描边盾 + 闪电裂」;
  显示规则由「状态 chip 区加一枚」改为**头行护甲格本身换图标换数**(杂兵破甲不印数、Boss 印减半值),回合只在详情看;
  画板改为 Icons(两图标 + 七种护甲格 × 两档)/ Detail(详情回合)/ Spec(两枚 ICONS 片段 + 规则 + 清单);删去 B / C 候选。
  为什么:用户拍板 —— 破甲 / 削甲本质是「护甲变了」,放在护甲格上比在状态区多挂一枚更直接,也给挤的状态区腾位;
  破甲旧图与削甲同为「盾 + 一道线」,裂成两半更能和「缺一角」一眼分开。
- 2026-10-21 · 回填 · 改了什么:Detail 画板里削甲每层的时长由「本场持久」改为各自「剩 N 回合」;README 第 4 条拍板点撤销,实现清单补「StatusText 削甲时长改走 Duration」。
  为什么:更正 —— Core 削甲 2026-09-08 起就是限时(`TurnsLeft = Math.Max(1, effect.Turns)`,每层各自计时),「本场持久」是过时注释与现行 StatusText 文案。
