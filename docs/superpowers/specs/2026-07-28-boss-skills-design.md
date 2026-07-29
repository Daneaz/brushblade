# Boss 技能系统设计(蓄力预警制 + 字级技能表)

> 2026-07-28 · 设计定稿,待实现
> 影响章节:第 8 章 8.5(Boss 体系)、第 20 章 20.7(成语 Boss 程序生成)

## 1. 问题诊断

玩家体感:「打 Boss 很单调,把木系召唤物顶在前面,剩下的慢慢合成,Boss 也没有群攻,只能一只一只清理,相对很轻松。」

这不是"Boss 技能少",而是 **Boss 的威胁投放通道被完全阻断**。三条规则叠加产生稳态无敌:

1. `BattleEngine.cs:448` 召唤物承伤**整次吸收、不溢出**——一只 1 血的树能吃掉 Boss 一次 20 点重击。
2. Boss 每回合**只出手一次**,每回合最多消灭 1 只召唤物。
3. 玩家每回合能补召唤物(上限 4),补充速度 ≥ 消耗速度 → 玩家血条永远不动。

第 4 条让情况更糟:4 只召唤物每回合各反击一次(`BattleEngine.cs:408`),召唤流不只是免疫,还是稳定的免费输出。

**后果**:Boss 的四阶段属性/攻击/承伤全部沦为摆设,因为攻击数字根本没接触到玩家。三只 Boss 的差异纯粹是数值和五行序列,玩家感知上是"同一个四阶段肉块换了张皮"。

附带发现的第二个漏洞:护盾"整场爬塔通吃"(`BattleEngine.cs:95`),玩家可跨层囤积,无任何机制消耗它。

## 2. 设计决策(已拍板)

| 决策 | 选择 | 理由 |
|---|---|---|
| 改动边界 | **只加 Boss 技能,召唤物承伤规则不动** | 保住木系流派的核心价值;杂兵战体验零变化;风险集中可控 |
| 技能挂载层 | **挂在「字」上** | 一张字→技能表养活无限个 Boss;程序生成的成语 Boss 天然有技能;兑现 8.5.1「成语字面预告机制」 |
| 破局狠度 | **群攻为主 + 少量穿透** | 召唤物仍有价值(分摊、反击、五行抵抗),但不再是免疫。木系从"无敌"变"好用" |
| 触发节奏 | **蓄力预警制** | 回合变成有限资源,直接治"慢慢合成";玩家有决策窗口,失败是自己的锅 |

## 3. 核心机制:蓄力预警

每只 Boss 增加两个状态:`ChargeCounter`(蓄力计数)、`IsCharging`(蓄力中)。

```
敌方回合 1:普通攻击                ChargeCounter → 1
敌方回合 2:普通攻击                ChargeCounter → 2
敌方回合 3:【不出手】进入蓄力 ⚡    ChargeCounter → 3,发 BossCharging,UI 预告「下回合:淹没」
敌方回合 4:释放当前阶段字的技能     发 BossSkillCast,ChargeCounter 归零
```

`ChargeEvery = 3`(可配置)。

### 3.1 状态机(Boss 在敌方行动循环内的分支)

按顺序判定,命中即 `continue`:

1. `IsCharging == true` → 释放当前阶段技能;`IsCharging = false`;`ChargeCounter = 0`
2. 当前阶段技能为主动技(非 `None` / `Bulwark`)→ `ChargeCounter++`;若 `ChargeCounter >= ChargeEvery` 则 `IsCharging = true` 并发 `BossCharging`,**该回合不出手**
3. 否则 → 普通攻击(现有逻辑,含召唤物顶前排)

**坚壁 / 无技能阶段冻结 `ChargeCounter`**(不递增),该阶段正常普攻。进入该阶段前累积的进度保留,离开后继续累加,不浪费。

### 3.2 推过阶段阈值 = 取消大招(方案支点)

**2026-07-29 修正(F1)**:换阶清零改为**有条件**——只在 `IsCharging == true` 时才清
`ChargeCounter = 0`、`IsCharging = false`;非蓄力状态下换阶(还在攒数的半路)保留计数,继续
累加。首版写的是无条件清零,与 3.1"进入坚壁阶段前累积的进度保留,离开后继续累加,不浪费"
自相矛盾,且实战会让薄阶段(如排山倒海 12/15/12/16 血、第 5 层不缩放)一回合打穿一个阶段,
`ChargeCounter` 永远攒不到 `ChargeEvery`,技能整场放不出来——坚壁阶段还额外冻结计数,两头夹死。

这把"血池换阶"从玩家只能被动挨着的机制,变成玩家手里的**主动工具**。蓄力回合的决策变成一道真题:

> 还差 8 点伤害就能推过阈值。全力抢血取消淹没,还是老实堆护盾硬吃?抢失败就是这回合既没防也没输出,白给。

推过阶段不只取消这次,还把下一次大招换成新字的技能。

### 3.3 总则:大招的玩家份不被召唤物拦截

**所有主动技能对玩家造成的伤害,不经过召唤物顶前排;护盾仍可吸收。**(吞噬除外,它本就不打玩家。)

这是"大招"的定义性特征,规则单一好记,也是整套设计能真正解决问题的关键——否则加多少技能都会被树吃掉。

## 4. 技能规格

四个主动 + 一个被动标签。刻意压到最小集:组合爆炸来自"四字排列",不靠技能数量堆。

### 4.1 淹没 Deluge(群攻)

- 对玩家造 `Attack × 2` 伤害(护盾可吸收,不被召唤物拦截;倍率见 2026-07-29 修正说明)
- **同时**对每只存活召唤物各造 `Attack` 伤害(不翻倍),各自走五行(`ResolveEffect(Attack, [], boss.Element, summon.Element)`)
- 事件:先发 `BossSkillCast`,再对每个受击目标发 `EnemyAttack` / `SummonHit`(复用现有动效管线)

定位:主力破召唤,惩罚"多而脆"的召唤阵。

> **2026-07-29 修正(F3)**:玩家份从 `Attack` 抬到 `Attack × 2`。4 个敌方回合(2 普攻 + 1 蓄力
> 不出手 + 1 释放)若玩家份不翻倍,总投放只有 `3×Attack`,反而低于无技能 Boss 的 `4×Attack`——
> 技能对无召唤流玩家是净减伤,与"破局狠度"的设计目标相反。召唤物那份维持 `Attack`(仍是分摊主力,
> 不需要额外加码)。

### 4.2 贯穿 Pierce(穿透)

- 对**最前一只**存活召唤物造 `Attack` 伤害(走五行)
- 对玩家造 `Attack × 2`(护盾可吸收,不被召唤物拦截)
- 场上无召唤物时:只打玩家那份

定位:深度打击,一击穿过前排。与淹没的区分是**广度 vs 深度**,不撞车。

### 4.3 倾覆 Topple(剥夺)

按顺序结算:

1. 对玩家造 `Attack × 2` 伤害(护盾正常吸收,不被召唤物拦截;倍率见 2026-07-29 修正说明)
2. **清空剩余全部护盾**(普通桶 `_shieldNormal` 与豁免桶 `_shieldPersist` 都清零)
3. 置 `_apPenaltyNextTurn = 1`

先吸收再清空,护盾不至于完全白费。

AP 惩罚在 `StartTurn` 生效:`Ap = max(1, ApPerTurn - penalty)`,用完清零。**下限 1**,保证玩家至少能做一件事,不出现完全不能动的回合。

定位:治囤护盾流 + 直接掐住"慢慢合成"。

> **2026-07-29 修正(F3)**:伤害那份从 `Attack` 抬到 `Attack × 2`,理由与淹没相同(见 4.1 修正
> 说明)——四回合总投放不翻倍会低于无技能 Boss。清盾与 AP−1 不变。

### 4.4 吞噬 Devour(拔除)

- 有召唤物:**消灭最前一只**(`Hp = 0`),**不回血**。发 `SummonHit`,`Amount` = 消灭前的 Hp
- 无召唤物:普通攻击玩家 `Attack`(护盾可吸收,**不 ×2**)

定位:无视血量必杀一只,惩罚"少而肉"的召唤阵。与淹没互补——淹没打不死 12 血的森树时,吞噬照删。

### 4.5 坚壁 Bulwark(被动标签)

承伤系数 `damageTaken < 1`,**就是现有的 `damageTaken` 字段,零新增结算代码**。该阶段不进入蓄力循环。

行为上与 `None` 完全相同。保留独立枚举值仅为可读性:`Bulwark` 表示"设计上就该是肉墙",`None` 表示"这字还没配技能"。

## 5. 字 → 技能表

覆盖现有 9 只 Boss(固定 3 + 程序生成占位 6)用到的全部 28 个去重字:

| 技能 | 字 |
|---|---|
| 淹没 Deluge | 海 江 河 啸 崩 雪 沙 气 万 |
| 贯穿 Pierce | 雷 霆 钧 刀 石 飞 |
| 倾覆 Topple | 倒 翻 排 走 |
| 吞噬 Devour | 吞 火 烈 柴 |
| 坚壁 Bulwark | 山 地 天 冰 |
| None(纯普攻) | 干 |

**查表未命中一律落到 `None`。** 这保证以后往成语库加字永远不会崩,只是没技能而已。保留 `None` 这一档也是故意的:不是每阶段都得有大招,留白让有大招的阶段更有分量。

### 5.1 组合出来的 Boss 性格(验证字表有效)

**排山倒海** — 排`倾覆` → 山`坚壁` → 倒`倾覆` → 海`淹没`
开场剥护盾和 AP,中段撞上不蓄力的肉墙(喘息但难啃),末段淹没全洗召唤。字面完全兑现:先"排"开防御,"山"挡路,"倒""海"淹没。

**翻江倒海** — 翻`倾覆` → 江`淹没` → 倒`倾覆` → 海`淹没`
剥夺与群攻交替,持续压制型。

**雷霆万钧** — 雷`贯穿` → 霆`贯穿` → 万`淹没` → 钧`贯穿`
三段穿透,**木系召唤流在这只面前基本失效**,逼玩家换构筑。流派检定型 Boss,零专属代码。

**冰天雪地**(程序生成)— 冰`坚壁` → 天`坚壁` → 雪`淹没` → 地`坚壁`
三段坚壁一段淹没 = 超肉磨血型。**这是程序生成自动产生的性格,无人手工设计。**

28 个字的排列自动长出"穿透特化""磨血肉墙""剥夺压制"等不同性格,而只需维护一张表。

## 6. 数据结构与配置

### 6.1 枚举与字段(Core)

```csharp
public enum BossSkill { None, Deluge, Pierce, Topple, Devour, Bulwark }
```

- `BossPhaseDef` 加 `BossSkill Skill`(默认 `None`)
- `EnemyState` 加 `int ChargeCounter`、`bool IsCharging`
- `IdiomBossDef` 加 `IReadOnlyList<BossSkill> Skills`(四项)

### 6.2 配置格式(`enemies.json`)

新增顶层 `bossSkills` 段存字→技能表,平衡调整不改代码:

```json
"bossSkills": { "海": "Deluge", "山": "Bulwark", "雷": "Pierce", ... }
```

`phase.skill` 显式字段优先,缺省则查表——固定三只 Boss 想手工微调时有后门,程序生成的成语 Boss 走查表。

### 6.3 字表不进 Core

**`ConfigLoader` 负责查表**:构造 `BossPhaseDef` 时就把字解析成技能填好,构造 `IdiomBossDef` 时填好 `Skills` 数组。Core 运行时完全不需要知道有张表,`Endless.BuildIdiomBoss` 直接用 `idiom.Skills[i]`。

少一个 Core 新文件,也更贴合"Core 只放纯规则"的架构约束。

### 6.4 新增战斗事件

- `BossCharging` — 驱动预警 UI(`TargetIndex` = Boss 下标)
- `BossSkillCast` — 技能释放(`Amount` = `BossSkill` 枚举值)

沿用 `EnemyTurnBegan` 已确立的"用事件显式划分段落"做法。代码注释记录了靠事件种类猜边界已出过两次动画错乱,不重蹈。

## 7. 代码落点

**Core**(纯 C#,不碰 UnityEngine)

| 文件 | 改动 |
|---|---|
| `EnemyDef.cs` | `BossSkill` 枚举;`BossPhaseDef.Skill`;`EnemyState.ChargeCounter` / `IsCharging` |
| `BattleEngine.cs` | Boss 三态状态机;四个技能结算;`_apPenaltyNextTurn`(`StartTurn` 生效);`CheckBossPhase` 换阶清蓄力;两个新事件 |
| `Campaign.cs` | `Scale` 透传 `Skill`(与承伤系数同样不缩放) |
| `Endless.cs` | `IdiomBossDef.Skills`;`BuildIdiomBoss` 逐字填技能 |
| `RunSnapshot.cs` | `EnemySnapshot` 加蓄力两字段;`BattleSnapshot` 加 AP 惩罚位 |

**Data**

| 文件 | 改动 |
|---|---|
| `ConfigLoader.cs` | 解析 `bossSkills` 字表;解析 `phase.skill`;填充 `IdiomBossDef.Skills` |

**Presentation**

| 文件 | 改动 |
|---|---|
| `BattleView.cs` | 蓄力预警显示(Boss 头顶「⚡ 下回合:淹没」)+ 技能释放动效 |

预警 UI 是**必须项不是收尾**:预警看不见,蓄力制就退化成随机挨打,整套设计不成立。

技能名文案按现有惯例硬编码在 Presentation(字符串表尚未实装,不在本次扩范围)。

## 8. 测试

TDD,先写失败测试。新建 `BossSkillTests.cs`:

| 用例 | 断言 |
|---|---|
| 蓄力周期 | 2 回合普攻 → 蓄力回合玩家**不掉血** → 第 4 回合释放 |
| **换阶取消蓄力** | 蓄力中推过血量阈值 → `IsCharging` 假、`ChargeCounter` 归零、下回合不释放 |
| 淹没 | 玩家 + 全部召唤物同时掉血;召唤物侧走五行(金系阶段 ×1.5、土系阶段 ×0.5) |
| 贯穿 | 最前召唤物掉血 + 玩家掉 `Attack×2`;第二只召唤物**不掉血** |
| 贯穿·无召唤物 | 玩家掉 `Attack×2` |
| 倾覆 | 两个护盾桶都归零 + 下回合 AP 少 1;AP 下限为 1 |
| 吞噬 | 最前召唤物死亡;Boss 血量**不变**(不回血) |
| 吞噬·无召唤物 | 玩家掉 `Attack`(非 ×2) |
| 坚壁阶段 | `ChargeCounter` 冻结,正常普攻,永不蓄力 |
| 字表 fallback | 未知字 → `None` → 纯普攻 |
| 大招不被拦截 | 召唤物满场时,淹没/贯穿/倾覆的玩家份仍然生效 |

`SnapshotRoundTripTests` 补蓄力状态存档往返——断点续爬不能丢蓄力状态,否则玩家读档就能白嫖取消大招。

**约束**:断言只用 Unity 版 NUnit 支持的 API(禁用 `Is.AnyOf`);测试不碰 Newtonsoft,序列化走 `Data.SaveSerializer` / `Data.ConfigLoader` 真实入口。

**验证命令**:

```bash
cd tools/coretests && /Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet test --nologo -v q
cd tools/prescompile && /Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet build --nologo -v q
```

## 9. 风险与后续

### 9.1 现有测试回归(预期内)

`BattleEngineTests` / `EndlessTests` 中若有 Boss 战的玩家血量断言,加大招后预期值会变。逐个确认改动合理,**不盲改断言迁就实现**。

### 9.2 难度上台阶,需重新校准数值

第 20 章记录的仿真卒层「新手 8.6 / 卡3级 P50=10 / 卡5级 P50=15」是在 **Boss 打不到玩家**的前提下测的。加技能后这些数字必然下滑。

实现后用 `tools/balance/` 跑一轮重新校准 `bossScaleBonus`——**很可能要往下调**:Boss 实际威胁投放量涨了一大截,而现有数值缩放是为"打不到人的 Boss"配的。

### 9.3 待校准的具体数值

首版取值均为设计基准,以仿真结果为准:

- `ChargeEvery = 3`
- 贯穿玩家份 `×2`(该技能既伤召唤物又打玩家,倍率可能偏高)
- **2026-07-29 修正(F3)**:淹没、倾覆的玩家份也已从 `Attack` 抬到 `Attack × 2`,与贯穿看齐——
  四个大招现在对玩家统一是 `2×Attack`(吞噬空放例外,仍是 `Attack`)。修正前的口径(淹没/倾覆
  玩家份 `×1`)会让技能对无召唤流玩家变成净减伤,已在实战验证并改正;此处 `×2` 同样只是设计
  基准,仍需仿真校准。
- 淹没对召唤物的伤害 = 全额 `Attack`(不受本次修正影响)

### 9.4 明确的非目标(YAGNI)

- 不动召唤物承伤规则(不做溢出穿透)
- 不加玩家侧 DoT 状态(敌方灼烧需要新系统,超范围)
- 不做 8.5.4 的欺骗池 / 禁锢池(改属性显示、锁部首/锁合成的机制脚本另议)
- 不做 8.5.5 终 Boss「讹」
- 不实装字符串表
