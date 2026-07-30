# 敌人能力 chip:命名统一 + 详情露出

> 2026-07-30 · 设计定稿,待实现
> 承接 `2026-07-30-enemy-skill-descriptions-design.md`(该特性建立了 `EnemyInfo` 文案层与形态页弹窗)

## 1. 问题

用户要求:「给这些特殊能力都加上 chips,战斗中和详情里都加上,便于识别。」

摸清现状后,真正的缺口比字面需求小、但夹着一处更要紧的问题:

1. **战斗中已有 9 种 chip**(属性 / 攻 N / 承伤 / 蓄力预告 / 灼烧 N / 补全 N/3 / 受击分裂 / 增益辅助 / 受击加攻),真正缺的只有 2 种:`Disguise`(通假)、`Obscure`(生僻)。
2. **详情弹窗完全没有 chip** —— 只有纯文本说明。
3. **一处命名分裂**:战斗 chip 用行为描述(`受击分裂` / `增益辅助` / `受击加攻`),而 `EnemyInfo.AbilityName` 用字怪名(`叠字` / `标点` / `自燃`)。两套术语并存,玩家学不到统一说法 —— 这与上一个特性把「坚壁」确立为 Boss 专属技能名的方向相悖。

## 2. 设计决策(已拍板)

| 决策 | 选择 | 理由 |
|---|---|---|
| 命名 | **统一用字怪名** | 与详情里的【叠字】+ 说明逐字对应;玩家在详情学一次,战斗中看到 chip 就懂 |
| 通假 / 生僻 | **挂 chip,但不泄真相** | chip 只说「属性不可信」/「属性未知」,不显示真属性 —— 信息隐藏的核心(不知道真属性是什么)完好,玩家不再因为看不清形象而被坑 |
| 缺笔进度 | **保留 `2/3` 与感叹号** | 「要不要抢在补全前打死它」是该怪唯一的决策点,删掉进度是功能回退;`已补全!` 的感叹号是危险信号(补全后攻翻倍、血回满),一并保留。命名统一的目标是消除术语分裂,不是删状态 |
| 详情 chip | **不带实时状态** | 图鉴是静态资料,显示 `缺笔` 而非 `缺笔 2/3` |
| 颜色 | **沿用现有语义,不引入新色** | 见第 4 节 |

## 3. chip 清单

### 3.1 战斗中(`BattleView` 敌人格)

| 能力 | 现文案 | 改为 | 显示条件 |
|---|---|---|---|
| `Regrow` | `补全 2/3` / `已补全!` | `缺笔 2/3` / `缺笔 已补全!` | 存活 |
| `Split` | `受击分裂` | `叠字` | 存活且未分裂 |
| `Buff` | `增益辅助` | `标点` | 存活 |
| `Scorch` | `受击加攻` | `自燃` | 存活 |
| `Disguise` | (无) | `通假` | 存活且**尚未现形** |
| `Obscure` | (无) | `生僻` | 存活且**尚未被读懂** |

**撤 chip 的时机是机制的一部分**:

- `通假` 在现形后撤掉 —— 判据 `enemy.ApparentElement == enemy.Element`(现形即真伪一致)
- `生僻` 在被读懂后撤掉 —— 判据 `enemy.ApparentElement != null`(未读懂时为 null,属性显示 `?`)

撤掉的那一刻本身就是给玩家的信号:伪装已破 / 已读懂。

不改动的既有 chip:属性、`攻 N`、`承伤`、`蓄力 · 下回合:X`、`灼烧 N`。

### 3.2 详情弹窗(`EnemyPreview`,形象与说明文本之间加一行)

| 怪种 | chip |
|---|---|
| 小怪·有能力 | 能力名(`缺笔` / `叠字` / `标点` / `通假` / `生僻` / `自燃`) |
| 小怪·仅减伤 | `承伤`(墨渍) |
| 小怪·无机制 | 不画 chip 行(错字鬼 / 夯土妖) |
| Boss | 该阶段技能名(`淹没` / `贯穿` / `倾覆` / `吞噬` / `坚壁`)+ 该阶段有减伤时追加 `承伤` |

Boss 的 chip **随 tab 切换**,与形象、数值、说明同步。

## 4. 颜色语义(沿用现有惯例)

| 色 | 含义 | 用于 |
|---|---|---|
| `Theme.Cinnabar` 朱砂 | 增长的威胁 | `自燃`、`灼烧 N`、`蓄力 · 下回合`、Boss 主动技能(淹没/贯穿/倾覆/吞噬) |
| `Theme.InkSoft` 深灰蓝 | 防御与辅助、信息类 | `叠字`、`标点`、`承伤`、`坚壁`、**`通假`**、**`生僻`** |
| `Theme.Jade` 翠玉 | 恢复 | `缺笔 N/3` |

`通假` / `生僻` 归深灰蓝:它们是信息类而非增长型威胁,不该抢朱砂的注意力。

## 5. 代码落点

| 文件 | 改动 |
|---|---|
| `Presentation/UI/EnemyInfo.cs` | 新增 `AbilityChipText(EnemyState enemy)` —— 战斗用,带实时状态与撤显条件;新增 `AbilityChipColor(EnemyAbility)` |
| `Presentation/UI/EnemyPreview.cs` | `FormTab` 加 `Chips` 字段(`IReadOnlyList<(string Text, Color Bg)>`),由 `FormsOf` 构造期填好;`Select` 只负责画 |
| `Presentation/BattleView.cs` | 四处既有能力 chip 改用统一命名;新增通假 / 生僻两处 |

**`FormTab` 加第五个字段是符合其设计意图的**:该结构的职责就是把「Boss 与小怪的差异」收进构造期,让渲染只有一条路径。chip 的判断逻辑属于差异,因此归 `FormsOf`。

## 6. 验证

**没有自动化测试**(与前一特性同一硬约束:`EnemyInfo` / `EnemyPreview` / `BattleView` 都在 Presentation asmdef,Tests asmdef 只引用 Core / Data,引用不到)。

**必跑三条**:

```bash
cd tools/prescompile && /Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet build --nologo -v q
```

```bash
python3 tools/fonts/subset_fonts.py && python3 -m pytest tools/fonts/tests/ tools/pipeline/tests/ -q
```

```bash
cd tools/coretests && /Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet test --nologo -v q
```

**字体子集这一步不可省。** `charset()` 从 .cs 字符串字面量自动收集,新增 chip 文案后若不重跑子集脚本,`test_subset_fonts_cover_charset` 会红、上线渲染成空框。本特性的前身就是漏了这步导致 20+ 字缺字形(已由 `e4ed279` 修复)。coretests 基线 484/484,fonts+pipeline 基线 91。

**实机清单**:

1. 通假字未现形时有 `通假` chip;**首次行动现形后 chip 消失**
2. 生僻字未读懂时有 `生僻` chip 且属性显示 `?`;**受击两次读懂后 chip 消失、属性显示真值**
3. 缺笔妖 chip 随补全进度走(`缺笔 0/3` → `缺笔 已补全`)
4. 叠字怪分裂后 `叠字` chip 消失(既有行为,确认未破坏)
5. 战斗中与详情弹窗的同一能力,chip 文案**逐字相同**
6. Boss 详情切 tab 时 chip 随之变(如「山」显示 `坚壁` + `承伤`)
7. 错字鬼 / 夯土妖的详情**没有空的 chip 行**

## 7. 非目标

- 不改动属性 / `攻 N` / `灼烧 N` / `蓄力 · 下回合` 四种既有 chip
- 不给 chip 加 tooltip 或点击交互(详情弹窗已承担完整说明)
- 不引入新主题色
- 不动缺笔妖双形象、精英怪多形态(仍是前一特性记下的后续项)
- 不实装字符串表
