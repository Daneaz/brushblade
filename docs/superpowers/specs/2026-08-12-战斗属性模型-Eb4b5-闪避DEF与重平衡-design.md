# E-b4+E-b5 闪避 / DEF 改造与全面重新平衡 设计

> 战斗属性模型五步拆分的收尾。父项目:把玩家从「字牌数值即伤害」改造成持有真实属性的战斗实体。
> 本步把**闪避**做成玩家侧真属性、把**防御**统一为点数制 DEF、把整张字表的数值量级抬高一档,
> 并落地 `锐`(穿透)与新部件 `兑`。

**日期**:2026-08-12(2026-08-12 修订并与 E-b5 合并)
**上游**:E-b1 属性载体(已合并)、E-b3-a Buff 组四字(已合并 `26da94d`)
**并行**:E-b2 暴击(分支 `feat/crit`,含 `锋`)
**下游**:无。这是 E-b 系列的最后一批。

---

## 〇 · 修订记录

| 日期 | 修订 |
|---|---|
| 2026-08-12(初稿) | 调研三个 DEF 模型方案(A 推倒重来 / B 两层并存 / C 半迁移),**推荐 B**;把数值决策整体推给 E-b5;子项目名为「命中/闪避/DEF」 |
| **2026-08-12(第二稿)** | 按用户裁定**全面重写**:① DEF 模型改采 **方案 A**(统一点数制),B/C 降级为「已否决方案」(第十六节保留其实测数字);② **E-b4 与 E-b5 合并为一个子项目**(折算率取决于字表量级,分开做等于拿即将作废的数字重写 40 条测试);③ 玩家攻击**永远必中**、敌人无闪避 → **「命中」不再是玩家属性,子项目更名为「闪避/DEF」**;④ 恒等性硬线**主动放弃**,改用第十节的四张安全网;⑤ 数值量级、迁移映射、成长曲线、存档迁移全部在本批定死,不再有「待 E-b5」 |
| **2026-08-12(第三稿,本次)** | 四条待拍板全部裁定,`⚠ 待用户拍板` 一节清空。其中一条**改动了机制设计**:① **破甲不改名,改回原始设计** —— `ArmorBreak` 从「承伤 +25%」变回「削减 DEF 点数」,「易伤 `Vulnerable`」的改名方案作废;连带**守方侧的乘法层彻底清空**(第四、七节重写,6 字数值重定);② 堆甲**允许**把小怪普攻打到 0,对冲交给第八章(新增第 4.6 节的接口要求);③ 敌人 DEF **半速缩放**,并给出有判别力的可执行判据 T3-V4(它把墨渍的 DEF 从折算值 25 压到 20);④ 存档断点**直接丢弃不补发** |

**文件改名**:`2026-08-12-战斗属性模型-Eb4-命中闪避DEF-design.md` → 本文件。

---

## 一 · 为什么 E-b4 与 E-b5 必须是同一个子项目

这是初稿自己算出来的结论,只是当时用它论证「所以先做 B」,现在用它论证「所以合并」:

> **点数制的折算率不唯一,它是字表量级的函数。** 墨渍的承伤 0.7 折成几点 DEF,完全取决于打它的
> 那一击有多大 —— 6 伤的字表下是 1.8 点,60 伤的字表下是 18 点。字表量级不定,折算就无解。

而字表量级正是 E-b5 的产出。于是两条路:

- 先做 E-b4(方案 B,加一层默认 0 的减法),再做 E-b5 —— 恒等性守住,但 `锐` 上线即废字(全场 DEF = 0,穿透 +3 的实际效果是 0),而且 E-b5 到来时**乘法层还要再收编一次**,等于同一批测试红两遍。
- 合并 —— 一次性把量级、模型、数值全部落地,测试只红一遍。

用户裁定合并。本 spec 按合并写。

**代价必须说清楚**:E-b1 的方法论是「机械迁移与数值重平衡严格分离,任何一条测试变红都分不清是
公式错了还是数值调了」。合并会把这两件事焊在一起 —— 除非我们**在子项目内部重建这条分离**。
第十节就是干这个的:把工作切成「可证明等价的量级变换」与「故意发散的模型迁移」两段,
各自配独立的验收判据。**这是本 spec 最重要的一节。**

---

## 二 · 用户裁定(已决,不再讨论)

| # | 裁定 | 本文落点 |
|---|---|---|
| 1 | **DEF = 方案 A,统一点数制**。`伤害 = max(0, 基础×生克 − DEF点数)`。乘法减伤层删除 | 第四节 |
| 2 | **规格冲突以《技能机制详表》为准**,第 10 章相关段落标为 v0.4 旧稿(标注由另一 agent 做)。故 `锐 = 穿透`、`利 = +1 AP`(已实现)、`锋 = 暴击 +20%`(E-b2 在做) | 第七、十二节 |
| 3 | **玩家攻击永远必中,敌人没有闪避**。闪避只做成玩家侧属性 → **「命中」不再是玩家属性** | 第八节 |
| 4 | **多段字(剁)每段各扣 DEF**,与既有「每段完全独立」口径一致;补偿走数值 | 第 4.4 节 |
| 5 | **E-b4 与 E-b5 合并** | 第一节 |
| 6 | **`锐` 与新部件 `兑` 随这批入表** | 第十二节 |
| 7 | **DEF / 闪避成长曲线在这批里定**(参考 `MaxHpFor`/`AttackFor`,封顶级 26) | 第九节 |
| 8 | **暴击不随等级成长**(E-b2 的裁定;属性轴口径:HP/ATK 随等级长,暴击不长) | 第九节 |
| **9** | **破甲改回原始设计 = 削减 DEF 点数**,不改名。用户原话:「破甲的设计初衷就是扣 DEF,只是那时候没有 DEF 的概念。本质上是一个东西。」 | 第 4.5、七、6.5 节 |
| **10** | **堆甲允许把小怪普攻打到 0**,不加 `max(1,…)` 保底;对冲手段(敌人破甲 / 真伤)是第八章的活 | 第 4.6 节 |
| **11** | **敌人 DEF 半速缩放**(HP/ATK 缩放率的一半) | 第 6.3 节 |
| **12** | **存档断点直接丢弃,不补发结算宝箱** | 第十一节 |

裁定 9 的分量最重:它不是命名问题,而是**否掉了「守方侧还留一层乘数」这件事**。
`ArmorBreak = 承伤 +25%` 是 2026-08-05 那次「承伤与护甲」子项目为了绕开「引擎里没有 DEF」
而做的**代偿实现**;DEF 回来了,代偿就该退场。连带结果:守方侧只剩点数 DEF 一层,**零乘数**
(第 4.1 节),`ArmorBreakPercent` 常量删除,方案 A 的「模型唯一」比第二稿更彻底。

---

## 三 · 范围

### 做

- 全字表 / 敌表 / 玩家血量 **数值量级 ×10**
- 防御统一为**点数制 DEF**(玩家 + 敌人),删除乘法减伤层与承伤系数
- **穿透**(`Pierce`)作为伤害侧属性;穿甲(`IgnoreArmor`)并入穿透
- 破甲(`ArmorBreak`)**语义复原为削减 DEF 点数**(名字与枚举序号都不动),6 字重定值
- **闪避**成为玩家侧真属性(敌人无闪避);`SummonPassive.Dodge` 并入同一条通道
- `MetaRules.DefenseFor` / `DodgeFor` 成长曲线
- `锐`(穿透增益)+ 新部件 `兑` 入表
- 存档迁移(登塔快照作废)
- 仿真工装接入新属性,并新增**阳性对照探针画像**

### 不做

| 不做 | 理由 / 归属 |
|---|---|
| 玩家攻击的命中判定、`PlayerHit` 属性、敌人 `Dodge` 属性 | 裁定 3:玩家必中。加了就是死属性 |
| 暴击 | E-b2(并行);本批只负责与它的合流顺序(第十五节) |
| `锋` | 随 E-b2 |
| **裂甲**(每回合护甲 −1 的 DOT 式削甲)与 `刮` / `削` 的字表重设计 | 技术上已可实现,但装甲怪够多之前体验不到增量。记为 E-c 候选(第 6.5.3 节) |
| **补装甲怪、敌人破甲 / 真伤** | 第八章的活。本批交付接口要求(第 4.6.2 节),并把「敌人给玩家挂破甲」的通道打通 |
| 敌人侧 ATK 重构 | E-b1 已裁定不做 |
| `钩`(拉后排到前排) | 模型缺口,与本步无关 |
| `CombatStats` 容器抽取 | 本批之后 `BattleConfig` 上会有 HP/ATK/暴击/DEF/闪避五个平铺字段。抽容器是纯机械重构,单独一个 chore,不塞进这批(这批已经够大) |

---

## 四 · 【核心】统一点数制 DEF

### 4.1 伤害链路

```
玩家打敌人:
  effect.Value
  → ScaleByCardLevel(卡等级)                     既有
  → ScaleByAttack((PlayerAttack + Empower + Morale×10) / 100)
                                                 E-b1 + E-b3-a  ← 剡 / 战意在这里
  → ExecuteBonus / DoubleVsBurning(条件翻倍)      既有
  → WuxingResolver.ResolveEffect(相生 ×3 / 相克 ×1.5 / ×0.5)   既有
  → × 暴击倍率                                    E-b2(并行,乘在最末)
  → floor(...)
  → − EffectiveDefense(敌人)                     【点数层,唯一的守方层】
  → max(0, ...)

其中:
  EffectiveDefense(敌人) = max(0,
        EnemyState.Defense                                  ← 不可变基础属性
      − enemy.Statuses.TotalMagnitude(StatusKind.ArmorBreak) ← 破甲(持续、可叠、本场)
      − (effect.Pierce + _playerStatuses.TotalMagnitude(StatusKind.PierceBuff)))  ← 穿透(本次)
```

```
敌人打玩家:
  enemy.Attack
  → − EffectiveDefense(玩家)                     【点数层。敌人无穿透,但可以有破甲】
  → max(0, ...)
  → 免疫 → 护盾 → HP                              既有,不动

  EffectiveDefense(玩家) = max(0,
        _config.PlayerDefense                                   ← 角色属性
      + _playerStatuses.TotalMagnitude(StatusKind.DefenseBuff)   ← 局内护甲增益(土系字)
      − _playerStatuses.TotalMagnitude(StatusKind.ArmorBreak))   ← 敌人的破甲(第 4.6 节)
```

**守方侧只有一层,而且是纯点数、零乘数。** 第二稿还留着一个「易伤 ×1.25」,裁定 9 之后连它
也没有了 —— `DamageTaken`(承伤系数)、`DamageReductionMultiplier`(乘法减伤)、
`ArmorBreakPercent`(承伤 +25%)、`PierceBonusPercent`(穿甲 +15%)**四个乘数全部删除**。
`DoubleVsBurning` / `ExecuteBonus` / 生克 / 暴击都留着,但它们是**攻方**的条件倍率,不是防御层。

**顺序是硬规格:所有乘法先算完,点数最后减。** 理由:乘法描述「这一击有多重」,点数描述
「这层皮有多厚」。反过来(先减 DEF 再乘生克)会让 DEF 被生克倍率放大 —— 克制方 ×1.5 会连带
把 DEF 的削减也放大 1.5 倍,同一件护甲对不同属性的攻击者厚度不同,无从解释。同理暴击必须
乘在减 DEF **之前**,否则等价于「暴击时护甲变薄」。

### 4.1.1 三条增减益在式子里的位置(裁定 9 的连带检查)

| 机制 | 字 | 作用侧 | 落点 | 载体 |
|---|---|---|---|---|
| **攻击加成** | 剡(`Empower 50`)、战/戮(`Morale`) | 攻方,**乘** | `ScaleByAttack` 的分子,最早 | `StatusKind.AttackBuff` / `Morale`(玩家身上) |
| **穿透** | 锐(`PierceBuff`)、錰/刺/锥(`effect.Pierce`) | 守方,**减** | `EffectiveDefense` 的减数,**本次有效** | `EffectDef.Pierce` + `StatusKind.PierceBuff`(玩家身上) |
| **破甲** | 熔/溃/溶/锤/破/碎(`ArmorBreak`) | 守方,**减** | `EffectiveDefense` 的减数,**本场持续** | `StatusKind.ArmorBreak`(**目标身上**) |

**攻击加成与后两者不可换位**:ATK 放大的是基础值,在生克之前;穿透/破甲削的是目标厚度,在
生克之后。中间隔着 `floor` 与生克乘数,顺序颠倒会得到完全不同的数。

### 4.1.2 【裁定】穿透与破甲同时存在:合并相减,不重复扣

`EffectiveDefense = max(0, 基础DEF − 破甲总量 − 穿透总量)` —— **一个 `max(0,…)`,两项都从同一个
基础 DEF 里减。**

**不写成「分别作用」的理由不是它算错了,而是它多余。** 两种写法在数学上完全等价:

| | `max(0, max(0, D−B) − P)`(分别) | `max(0, D−B−P)`(合并) |
|---|---|---|
| `D−B ≥ 0` | `max(0, D−B−P)` | `max(0, D−B−P)` ✓ 同 |
| `D−B < 0` | `max(0, 0−P) = 0` | `D−B−P < 0` → `0` ✓ 同 |

既然等价,取**少一次钳位**的写法:将来加第三个削减源(比如裂甲,见 6.5.3)时,合并式只是
再减一项,分别式则要决定新的嵌套顺序 —— 那是一个没有正确答案的问题。

**语义上的说法**:护甲只有一层厚度。破甲削掉 20、穿透再穿 30,合起来是穿透了 50 的厚度,
**不是「穿透去穿那 20 点已经被削掉的甲」**。所以两者相加而非相乘、更非嵌套。

**溢出不倒贴**:`max(0, …)` 保证削过头只是归零,绝不变成增伤。守卫测试
`ArmorBreak_AndPierce_DoNotDoubleCount_NorOverflow`:DEF 20 + 破甲 20 + 穿透 30,基础 100
→ 打出 **100**(不是 100+30),且与「DEF 20 + 破甲 50」结果相同。

### 4.2 什么吃 DEF、什么不吃

与 E-b1 的 ATK 表同构。**DEF 只挡「一次挥击」。**

| 吃 DEF(每次结算各扣一次) | 不吃 DEF |
|---|---|
| `DamageSingle` / `DamageAll`(逐目标各扣各的) | 灼烧 tick(`BurnTick`,玩家侧与敌人侧都不吃) |
| 敌人普攻 | 流血 tick(`BleedTick`) |
| Boss 大招(Deluge / Pierce / Topple 的伤害部分) | 引爆(`Detonate`) |
| 召唤物出手伤害(吃**敌人**的 DEF) | 反弹(`Reflect`)与荆的反伤(`Thorns`) |
| | 斩杀直接击杀(`ExecuteKills`,抹血不走伤害) |
| | 护盾吸收量 / 治疗量 / 控制回合数 |
| | 召唤物挨打(召唤物没有 DEF,也不借用玩家的) |

#### DOT 不吃 DEF —— 硬约束,给出完整理由

**数值理由(致命的那条)**:灼烧每层结算 20(×10 后),`焰` 挂 2 层 = 40。面对 DEF 30 的敌人,
若逐 tick 扣 DEF,2 层打出 `max(0, 20−30)×2 = 0`,4 层打出 0,火系对该敌人**完全作废**;
而 6 层却能打出 `(20−30 → 0)`…… 无论多少层都是 0。点数减法作用在「每层一跳」这个小数字上,
是**开关**不是**削减**。流血(锯 = 每回合 10)同理。

**语义理由**:DEF 挡的是外来的一次挥击。DOT 是已经进入体内的东西,再让皮厚度挡一次是错位。

**与 E-b1 的不对称是刻意的,不是遗漏**:E-b1 裁定「灼烧每层伤害**吃** ATK」。ATK 是「你烧得多旺」,
DEF 是「他的皮多厚」—— 一个描述火本身,一个描述挥击的落点。所以 DOT 吃 ATK 不吃 DEF。
这条不对称在 spec 里写死,免得日后被当成 bug「修正」。

**已知的战术后果(是收益不是漏洞)**:高 DEF 敌人天然被 DOT 流派克制。这正是点数甲想要的分工
——金系破甲/穿透、火系绕过、AOE 回避。**反过来的约束**:因此**不给任何敌人配「免疫 DOT」**,
否则高 DEF + 免疫 DOT 会造出无解怪。

### 4.3 一个补丁自动消失了(方案 A 的额外收益)

今天 `DamageEnemy` 里有这一段:

```csharp
// 减免(<1)遭属性克制失效:被克(×1.5)直接按克制结算,不再乘减免。
if (taken < 1f && WuxingResolver.KeMultiplier(attacker, enemy.Element) >= 1.5f) taken = 1f;
```

它存在的原因是**乘法层会吃掉克制的收益**:`100 × 1.5 × 0.5 = 75`,而无甲时是 `100 × 1.5 = 150`
—— 打对属性的奖励被护甲按比例抽走了一半,手感上「克制没用」。

点数制下这条补丁**不需要存在**:

| 场景 | 无甲 | DEF 30 | 克制带来的净收益 |
|---|---|---|---|
| 不克制,基础 100 | 100 | 70 | — |
| 克制 ×1.5,基础 100 | 150 | 120 | **+50,与无甲时完全相同** |

减法对乘法是透明的:克制加的那 50 点原封不动地落到血条上。**打对属性的奖励天然不被护甲侵蚀。**

→ 这条规则**删除**。对应的两条测试(`DamageTaken_AboveOne_SurvivesElementCounter` /
`DamageTaken_BelowOne_StillLostToElementCounter`)改写为断言上表那条恒等式:
「克制收益 = 无甲时的克制收益」。语义更强,而不是更弱。

### 4.4 AOE 与多段:双重惩罚的处理方案

点数甲对「把总量摊成多份」的输出形态有天然惩罚。两个都是真实的,处理方式不同。

#### (a) AOE:`DamageAll` 打 N 个目标,总损失 N × DEF

**不加代码闸,改用配置口径约束**:

> **DEF > 0 的敌人只能是「装甲 / 坚壁」定位,且不成群出现 —— 同一次遭遇里带甲怪不超过 1 只。**
> 本批交付时全部 13 只字怪里带 DEF 的只有墨渍 1 只(DEF 20),其余 12 只 DEF = 0;
> Boss 的 DEF 挂在阶段上,而 Boss 永远单只。

代价量化:一场 3 只怪的遭遇里最多 1 只带 DEF → AOE 的额外损失是 20,而 AOE 字 ×10 后基础值
50~300,损失率 7%~40%(打低阶 AOE 时才痛)。这在可接受区间,而且它就是「AOE 清杂兵、
单体破装甲」这条战术分工的具体形状。

**这条口径要有守卫**,否则日后加怪时没人记得 —— 见第 10.4 节的 `RealConfig_ArmoredEnemiesAreRare`。

⚠ 与第 4.6.2 节**不矛盾**:那里要求第八章**增加带甲怪的总数**(让破甲/穿透两条金系轴活起来),
本节约束的是**同一次遭遇里的并发数**。总数变多而并发数封顶 1,两条一起才是完整口径 ——
第八章落地时 `RealConfig_ArmoredEnemiesAreRare` 要相应从「全表不超过 1 只」改成
「任一 `enemyPool` 抽样的遭遇里不超过 1 只」。

**不选的方案**:「AOE 只扣一次 DEF」(破坏「逐目标各扣各的」这条直觉,而且会让 AOE 变成
破装甲的最优解,与设计意图相反)、「AOE 的 DEF 减半」(凭空的第三个系数)。

#### (b) 多段:`剁`(hitCount 2)每段各扣

裁定 4:每段各扣,与 `EffectDef.HitCount` 注释里「每段完全独立:各自过生克、破甲、穿甲,
也各自过斩杀」的既有口径一致。补偿走数值,规则:

> **多段字的总基础值 = 同档单段字 × (1 + 0.1 × (段数 − 1))**

`剁` 今天 10×2 = 20 总。×10 后本应 100×2 = 200;按补偿规则 → **120 × 2 = 240 总**。

面对 DEF 30 的敌人:补偿后打出 `(120−30)×2 = 180`;同稀有度单段 240 伤打出 210;
补偿前 100×2 只打出 140。仍差 30 点,但多段在「两次过斩杀阈值」「两次触发受击后效」上有
独立收益,不追求完全拉平。

全表目前只有 `剁` 一个多段字,规则先写下来供日后扩表用。

⚠ 破甲变成削 DEF 之后,`MultiHit_EachSegmentGoesThroughArmorBreakSeparately` 这条既有测试的
**语义仍然成立但含义变了**:今天它守「每段各享一次 +25% 承伤」,之后守「每段各按同一个
`EffectiveDefense` 结算」。破甲是**目标身上的持续状态**,两段之间不会变化,所以「每段独立」
在破甲这条上是平凡成立的;真正需要新测的是「每段各扣一次 DEF」(4.4(b))。

---

## 4.5 【裁定 9】破甲 = 削减 DEF 点数(改回原始设计)

### 4.5.1 为什么这不是改名,是复原

`docs/design/第10章-战斗数值框架.md` 的 v0.4 稿早就把破甲写成点数削减,而且写得很具体:

| 出处 | 原文 | 本批的处置 |
|---|---|---|
| `:56` 10.2 资源表 | 「护甲 X = 每次伤害 −X(最低 0);**破甲永久降护甲**」 | ✅ **采纳为原始设计**,不再当 v0.4 旧稿 |
| `:137` 10.5 战例二 | 锋(破 5)+ 削(破 3)+ 刮(破 2)→ Boss 护甲 10 → 0,下一回合收割 | ✅ **采纳**,它是本节数值档位的标定依据(6.5) |

2026-08-05「承伤与护甲」子项目把破甲实现成「承伤 +25%」,不是因为那样更好,而是因为
**当时引擎里没有护甲点数可扣**。那是一次代偿,而且代偿得很合理 —— 没有 DEF 的世界里,
「破甲」唯一能落地的形状就是乘法增伤。

现在 DEF 回来了。**代偿退场,原始设计上线。** 名字一个字不改,序号 7 一动不动。

### 4.5.2 语义变更清单

| 项 | 今天 | 本批之后 |
|---|---|---|
| `EffectKind.ArmorBreak` 的 `Value` | **回合数**(全部 6 字 = 2) | **削减的 DEF 点数**(见 6.5) |
| `ArmorBreakPercent = 25` 常量 | 施加时写进 `Magnitude` | **删除** |
| `StatusKind.ArmorBreak` 的 `Magnitude` | 承伤加成百分比 | **削减的 DEF 点数** |
| 持续时间 | 2 回合(`TurnsLeft = value`) | **本场持久**(`TurnsLeft = -1`),依据 `:56`「永久降护甲」 |
| 叠加 | 不叠层,重复施加只刷新(`SourceId = null`) | **可叠加**,`SourceId` 铸唯一序号(与 `HealOverTime`/`AttackBuff` 同款) |
| 极性 | `Debuff` | **不变** |
| 枚举名与序号 | `ArmorBreak` = 7 | **不变**(存档安全) |
| 结算位置 | 乘法层,`taken += 25%` | 点数层,`EffectiveDefense` 的减数 |

**「可叠加」是必须的,不是可选的**:不叠只刷新的话,六个破甲字互相排斥 —— 先出削 20 的
再出削 10 的会**变弱**。而战例二的整套玩法(三张接力削光)要求叠加。上限由 `max(0, …)`
天然给出:削到 0 就到底,不会有负护甲。

**「本场持久」的连带后果**:`ArmorBreak_ExpiresAfterTwoTurns` 这条既有测试**语义反转**为
`ArmorBreak_PersistsForTheWholeBattle`。这是本批第二条语义反转的测试(第一条是 6.3 的
`Scale_PreservesDamageTaken` → `Scale_ScalesDefense`),两条都单独点名以免评审时误判成回归。

### 4.5.3 【硬约束】点数层只放属性,变动量一律走状态

这是本 spec 自己发现的坑 —— 「破甲改成削 DEF 点数」的天真实现是让 `EnemyState.Defense` 可变,
那会**逼出一个新的 `EnemySnapshot` 字段**(敌人 DEF 变成战中可变状态,和今天的 `DamageTaken`
一样必须进快照)。规避办法写成硬约束:

> **`EnemyState.Defense` / `BattleConfig.PlayerDefense` 在战斗中永不被写。** 它们是不可变的
> 基础属性,由 `EnemyDef`(按 `DefId` 查回)与 `BattleConfig`(由 `GameRoot` 注入)提供。
> **一切对护甲的改变 —— 增(`DefenseBuff`)、减(`ArmorBreak`)、临时穿透(`PierceBuff`)——
> 全部是 `StatusBag` 里的条目**,而 `StatusBag` 本来就进 `BattleSnapshot.PlayerStatuses` /
> `EnemySnapshot.Statuses`。

**结果:零新增快照字段,且净删一个**(`EnemySnapshot.DamageTaken`)。分层成立性的逐条验证:

| 数据 | 战中会变吗 | 存在哪 | 新快照字段? |
|---|---|---|---|
| 敌人基础 DEF | ❌ 永不 | `EnemyDef.Defense`,`Restore` 按 `DefId` 查回 | ❌ |
| 敌人身上的破甲 | ✅ 会 | `EnemyState.Statuses`(已在存) | ❌ |
| 玩家基础 DEF | ❌ 永不 | `BattleConfig.PlayerDefense`,`Restore` 本就接收 config | ❌ |
| 玩家的护甲增益 / 挨的破甲 | ✅ 会 | `_playerStatuses`(已在存) | ❌ |
| 本次攻击的穿透 | ✅ 会(逐次) | `EffectDef.Pierce`(配置,不变)+ `_playerStatuses` 的 `PierceBuff` | ❌ |

**这条硬约束要写进 `EnemyState.Defense` 的 XML 注释里**,否则日后有人为了实现「护甲自然恢复」
之类的东西顺手给它加个 setter,快照会静默漏字段(`RunSnapshot.cs:9` 那条警告说的就是这种事)。

### 4.5.4 破甲也能打在玩家身上

`EffectiveDefense(玩家)` 里减的是**玩家身上**的 `ArmorBreak` 总量。今天没有任何敌人会施加它,
但第 4.6 节的第八章接口要求会用到这条通道 —— **本批把通道打通,不出敌人**。

极性口径自洽:`DefenseBuff` 是 `Buff`、`ArmorBreak` 是 `Debuff` → 净化(`Cleanse`)能洗掉玩家
身上的破甲,驱散(`Dispel`)能洗掉敌人身上的护甲增益。既有的 `StatusOpsTests` 两条断言原样成立。

---

## 4.6 【裁定 10】堆甲可以把伤害打到 0 —— 以及对第八章的接口要求

`伤害 = max(0, 基础×生克 − EffectiveDefense)`,**不加 `max(1, …)` 保底**。

| | 理由 |
|---|---|
| 一致性 | 与既有伤害路径的 `max(0, …)` 口径相同,不为堆甲另开第三种下钳规则 |
| 设计意图 | 土系「铁乌龟」构筑成立,是防御流应得的幻想兑现 |
| 为什么不用 `max(1,…)` | 它会让**穿透在残局失去意义**(反正保底 1 点),而穿透是金系两条轴之一 |

### 4.6.1 这条裁定制造的缺口

铁乌龟一旦成立,就必须有东西能捅破它,否则玩家在某个层段会进入**无限续航**的退化局
(打不死我 → 我慢慢磨 → 塔没有上限 → 局永远不结束)。

同时,点数制还有一个对称的缺口:**破甲与穿透两条金系轴,只在有甲的敌人面前才有意义。**
全塔今天只有 1 只小怪 + 3 个 Boss 阶段带甲(第 6.3 节),意味着 6 个破甲字 + 3 个穿甲字 +
`锐` 一共 10 个字,大部分时间里只发挥它们的伤害部分。

**两个缺口指向同一件事:敌人侧要补。** 那是第八章(字怪)的活,不在本批范围。本批的交付是
**把接口要求写清楚**,作为 T8 的一份产出交给下一个子项目。

### 4.6.2 对第八章的接口要求

**(a) 装甲怪的密度**(让破甲/穿透不是死轴)

| 层段 | 今天 | 要求 |
|---|---|---|
| 字林(1~10) | 0 只 | ≥ 1 只轻甲(DEF 8~12) |
| 词渊(11~25) | 墨渍 1 只 | ≥ 2 只(DEF 15~20) |
| 文山(26~50) | 墨渍 1 只 | ≥ 3 只(DEF 20~30) |
| 墨海(51+) | 墨渍 1 只 | ≥ 3 只(DEF 25~35) |

- 目标:**任一层段的一次遭遇里出现至少一只带甲怪的概率 ≥ 30%**
- ⚠ 约束不变:**同一次遭遇里带甲怪不超过 1 只**(第 4.4(a) 的 AOE 保护),这是 `BuildFloor`
  的生成约束或 `enemyPool` 的配比约束,由第八章选实现方式

**(b) 对冲铁乌龟的手段**

| 手段 | 实现 | 分布要求 |
|---|---|---|
| **敌人破甲** | 给玩家挂 `StatusKind.ArmorBreak`(削玩家 DEF)。**通道本批已打通**(4.5.4),第八章只需配敌人技能 | 词渊起 ≥ 1 只,文山/墨海各 ≥ 1 只 |
| **真伤** | ⚠ **不要新开「真伤」伤害类型**。最省的实现是复用 `EffectDef.Pierce`,给该敌人技能一个足够大的穿透值(如 999)—— 语义上就是「这一击无视一切护甲」,零新机制、零新快照字段、零新测试维度 | 词渊起 ≥ 1 只,文山/墨海各 ≥ 2 只 |

- **底线判据(可执行)**:**每个层段都必须存在至少一只敌人,其普攻或技能无法被当段可达的
  最厚堆甲完全免疫。** 这条要写成一个跨 `enemies.json` 的配置守卫测试(与
  `RealConfig_ArmoredEnemiesAreRare` 同款),在第八章落地时补。
- ⚠ **仍然不给任何敌人配「免疫 DOT」**(第 4.2 节的既有约束)。高 DEF + 免疫 DOT 会造出无解怪;
  DOT 是玩家对付厚甲的保底手段之一。

---

## 五 · 数值量级:全表 ×10

### 5.1 为什么

E-b1 第十节已经记下了病灶:整数除 `value * ATK / 100` 会吃掉低数值字的加成。

| 卡面 | ATK 102(+2%) | ATK 110(+10%) | ATK 150(+50%) |
|---|---|---|---|
| 6 伤 | 6 | 6 | 9 |
| 60 伤 | 61 | 66 | 90 |

6 伤的字要 ATK 到 150 才动 3 格;60 伤的字每 +2 ATK 就动 1 格。真属性制天然要求更大的数字。

⚠ **不用 `ceil` 绕过**:`ceil(7 × 1.02) = 8` 等于 +14%,低数值反而超额收益,方向是错的
(E-b1 已把这条写进 `ScaleByAttack` 的注释)。

**并且点数制 DEF 也要求大数字**:DEF 是减法,它相对于伤害量级的比例才有意义。6 伤的字表里
「DEF 3」就是砍半,粒度粗到无法配置;60 伤的字表里 DEF 可以在 3~60 之间连续取值。
**量级抬高不是重平衡的附属品,而是点数制成立的前提。**

**倍数取 10**:够大(把粒度从 16% 拉到 1.6%),且是十进制整十倍 —— 所有既有值 ×10 后仍是整数,
生克的 ×1.5 / ×0.5 / ×3 对 10 的倍数精确无舍入,这让第 5.3 节的「等比恒等」证明成立。
×20 / ×100 也满足这条,但 ×10 后最大值 600(錰)已经足够,再大只是让 UI 上的数字变难读。

### 5.2 要 ×10 的 / 不要 ×10 的

**×10(所有「一份血量」量纲的东西)**

| 位置 | 项 |
|---|---|
| `chars.json` | `DamageSingle` / `DamageAll` / `Shield` / `HealSelf` / `HealAll` / `HealOverTime` 的 `value`;`Summon` 的 `value`(召唤物血量)与 `attack`;`Bleed` 的 `value`;`BurnPotency` 的 `value`(它抬的是每层伤害);`passive` 的 `thorns` / `healAlly` |
| `enemies.json` | 全部 `maxHp` / `attack`(小怪级 + Boss 阶段级);`events` 的 `hpDelta`(连同文案里的数字) |
| `Core/Meta.cs` | `MaxHpFor`:`min(100, 50 + 2×(lv−1))` → **`min(1000, 500 + 20×(lv−1))`** |
| `Core/Perk.cs` | 养元 `MaxHp` 每级 10 → **100**;金汤 `Shield` 每级 2 → **20**(墨锭价格**不改**) |
| `Core/BattleEngine.cs` | `_burnPerStack` 默认 2 → **20**;`ScorchGain` 2 → **20** |

**不 ×10(量纲不是血量)**

`ATK`(基准 100,是比值)、暴击率、闪避率、`Blind` 百分比、`Reflect` 百分比、`Curse` 百分比、
`MoralePerStack`(10,ATK 量纲)、`Empower`(50,ATK 量纲)、灼烧**层数**、冻结/减速/HoT 的
**回合数**、免疫**次数**、驱散**条数**、复活**个数**、`ApBoost`、`SummonCount`、`HitCount`、
`ExecuteBelowPercent`、召唤物 `Speed`、`ScaleByCardLevel` 的 `1 + 0.1×(lv−1)` 系数、
墨锭 / 经验 / 价格 / 掉落概率的全部经济数值。

**DEF / 穿透没有「旧值」**,它们是新的,直接按第六节的目标值写。

### 5.3 ×10 是可证明的等价变换 —— 这是安全网的地基

若把「所有血量量纲的数」同乘 10,战斗的**结构**完全不变:回合数、胜负、事件种类与顺序、
随机数消耗、掉落序列逐字节相同,只有每条 `BattleEvent.Amount` 恰好变成 10 倍。

逐条验证乘 10 在既有算式下封闭:

| 算式 | 封闭性 |
|---|---|
| 生克 `floor(base × 相生 × 相克)`,乘数 ∈ {1, 1.5, 0.5, 3, 4.5, 0.75…} | 10 的倍数 × 0.5 仍是整数 → `floor` 无舍入,精确 ×10 ✓ |
| `ScaleByAttack`:`v * ATK / 100` | ATK 恒为 100 时精确;ATK ≠ 100 时**不精确**(`6*102/100=6` 但 `60*102/100=61`)⚠ |
| `ScaleByCardLevel`:`ceil(v × (1+0.1(lv−1)))` | `ceil` 在 10 的倍数上更少触发上取整 → **不精确** ⚠ |
| 灼烧 `floor(层数 × burnPerStack × 克)` | burnPerStack ×10 → 精确 ✓ |
| 引爆 `floor(N(N+1)/2 × burnPerStack × 克)` | 同上 ✓ |
| 分裂 `half = (hp+1)/2` | hp 是 10 的倍数 → 精确 ✓ |
| 斩杀 `hp × 100 < maxHp × pct` | 两边同乘 10,不等式不变 ✓ |
| 护盾吸收、免疫、治疗封顶 | 全是加减与 `min` ✓ |

两条 ⚠ 正是我们**想要**它不精确的地方(ATK 粒度、卡等级粒度就是这次要修的病)。
所以「等比恒等」的验收要在**基准条件**下取:**ATK = 100(1 级)且卡等级全 1**。
在这个切片上 ×10 是逐字节可证的;偏离基准的差异正是本次改动的目的,单独用第 10.5 节的
仿真探针观测。

---

## 六 · 迁移映射表

### 6.1 折算率怎么来的

点数与百分比之间没有普适折算,只有「相对于某个参考打击量」的折算。本 spec 把参考量显式定死:

| 参考量 | 值 | 依据(全部是 ×10 之后的量级) |
|---|---|---|
| `R_in` 玩家挨的一击 | **60** | 敌人 attack 20~110,中位 40;词渊段(11 层起)深度缩放 ×1.5 → 60 |
| `R_mob` 玩家打小怪的一击 | **85** | `DamageSingle` 中位 130,但玩家前中期手里多是白/绿字(30~90),混合中位取 85 |
| `R_boss` 玩家打 Boss 的一击 | **120** | 打 Boss 时玩家会攒大牌,取蓝/紫档 130~200 与白字混合后的中位 |

折算公式:
- 玩家减伤字:`DEF点数 = 旧减伤% × R_in / 100 = 旧% × 0.6`
- 敌人承伤系数:`DEF点数 = (1 − damageTaken) × R`,取 5 的倍数

> 这三个参考量是**设计取值,不是测量值**。T8 校准任务会拿仿真探针复核并微调 —— 但**只调
> json 里的点数,不调公式**。参考量写进 spec 是为了让「为什么是 25 不是 30」这个问题永远有答案。

### 6.2 玩家侧:6 个减伤字 → `DefenseBuff` 点数

**已核实**(2026-08-12,`chars.json` 实测,与初稿清单一致):

| 字 | 稀有度 | 五行 | 旧 `DamageReduction` | 新 `DefenseBuff` 点数 |
|---|---|---|---|---|
| 巍 | — | 土 | 5% | **3** |
| 磐 | — | 土 | 10% | **6** |
| 崟 | — | 土 | 15% | **9** |
| 铠 | 紫 | 金 | 20% | **12** |
| 崊 | — | 土 | 20% | **12** |
| 漜 | 橙 | 土 | 25% | **15** |

**叠加语义变化**:旧值乘法叠加(`0.75 × 0.8 × 0.8 = 0.48`,天然趋近但不达 0),新值**加法**叠加
(`15+12+12 = 39`)。对 `R_in = 60` 的一击:旧 −52%,新 −65%。堆甲变强了,且**理论上可以完全
免疫小怪普攻**(DEF 39 + 玩家等级 DEF 12 = 51,对 attack 40 的怪归零)。

**裁定 10:这是刻意接受的**,理由见第 4.6 节。三条守卫:

1. 敌人 attack 随深度每层 +10% 无上限,而玩家 DEF 有上限(字表点数 + `DefenseFor` 封顶 12)
   → 免疫是**局部且暂时**的。
2. 第 4.6.2 节对第八章的接口要求(敌人破甲 / 真伤)是**堆甲流的对冲**,底线判据是
   「每个层段都存在至少一只无法被完全免疫的敌人」。
3. 第 10.5 节的「土系堆甲探针画像」会量到它。若 P50 爆表,回调的是**字表点数**,不是机制。

### 6.3 敌人侧:承伤系数 → `Defense` 点数

**已核实**(`enemies.json` 实测,与初稿清单一致:1 只小怪 + 3 个 Boss 阶段):

| 敌人 / 阶段 | 旧 `damageTaken` | 参考量 | 折算值 | **最终 `defense`** |
|---|---|---|---|---|
| 墨渍(小怪,水,HP 14→140,atk 3→30) | 0.7 | `R_mob` 85 | 25 | **20** ⚠ 被 T3-V4 判据压低,见 6.3.2 |
| 排山倒海 · 山(第 2 阶段,坚壁) | 0.5 | `R_boss` 120 | 60 | **60** |
| 翻江倒海 · 江(第 2 阶段) | 0.75 | `R_boss` 120 | 30 | **30** |
| 雷霆万钧 · 钧(第 4 阶段) | 0.75 | `R_boss` 120 | 30 | **30** |

其余 12 只小怪与全部其他 Boss 阶段:`defense = 0`(配置里不写,DTO 默认 0)。

### 6.3.1 【裁定 11】DEF 随深度**半速**缩放

`Campaign.Scale()` 今天明写「承伤系数不缩放」(它是比例,缩放会溢出)。点数 DEF 必须缩放
—— 不缩放的话 100 层的坚壁 Boss 血量 ×11 而护甲仍是 60,占玩家单击的比例趋近 0,护甲形同虚设。

但**同速缩放也不行**:点数减法对小数值是**开关**不是削减(第 4.2 节论证 DOT 时的同一条性质),
同速缩放会让深层的低伤字全部归零,玩家在深层只剩几张高伤字可用 —— 字库多样性被护甲单方面
掐死。于是取一半:

```csharp
/// <summary>敌人数值缩放。HP/攻击按 scale 向上取整;
/// **护甲按 scale 的一半增长**(2026-08-12 裁定 11):点数减法对小数值是开关不是削减,
/// 同速缩放会让深层只剩高伤字可用。承伤系数字段已删除。</summary>
float defScale = 1f + (scale - 1f) / 2f;
int scaledDefense = (int)Math.Ceiling(enemy.Defense * defScale);
```

无尽的 `scale = 1 + 0.1 × (depth − 1)`(`Endless.cs:61`),于是:

| 深度 | `scale`(HP/攻击) | `defScale`(护甲) | 墨渍 DEF 20 → | 山阶段 DEF 60 → |
|---|---|---|---|---|
| 1 | 1.00 | 1.00 | 20 | 60 |
| 11 | 2.00 | 1.50 | 30 | 90 |
| 20 | 2.90 | 1.95 | **39** | 117 |
| 51 | 6.00 | 3.50 | 70 | 210 |

→ `Scale_PreservesDamageTaken` 这条测试的语义**反转**为 `Scale_ScalesDefenseAtHalfRate`。
这是本批第一条「测试名和断言方向都反过来」的改动(第二条是 4.5.2 的 `ArmorBreak_Expires…`),
单独点名以免评审时误判成回归。

### 6.3.2 T3-V4:半速缩放的可执行判据(以及它如何压低了墨渍的 DEF)

裁定 11 给的判据:**深层(取深度 20)时,字表里最低伤害档的字打在带甲小怪身上仍要有非零输出。**

算一遍(深度 20,`defScale = 1.95`;典型玩家画像:等级 12 → `AttackFor(12) = 122`;卡等级 4 → ×1.3):

```
最低伤害档字     DamageSingle 3 ×10 = 30
→ ScaleByCardLevel(30, 4) = ceil(30 × 1.3)      = 39
→ ScaleByAttack(39)       = 39 × 122 / 100      = 47   (整数除)
→ 生克中性                                       = 47
→ − 墨渍 DEF
```

| 墨渍基础 DEF | 深度 20 的 DEF | 输出 | 判据 |
|---|---|---|---|
| 25(纯折算值) | `ceil(25×1.95)` = 49 | **0** | ❌ 不通过 |
| **20** | `ceil(20×1.95)` = 39 | **8** | ✅ 通过 |
| 20,但**同速**缩放 | `ceil(20×2.9)` = 58 | **0** | ❌ 不通过 |

两件事同时被证明:

1. **墨渍的 DEF 定为 20 而不是折算值 25** —— **判据优先于折算率**。折算率(6.1)只给初值,
   可执行判据才是裁决者。这个因果链要留在 spec 里,否则日后有人看到 `(1−0.7)×85 = 25` 会以为
   配置写错了。
2. **这条判据有判别力** —— 它能区分半速与同速(最后一行),不是一条永远绿的装饰性断言。
   这正是第 10.5 节「先证明仪器能看见,再用它读数」的同一条纪律。

**判据的作用域是小怪,不含 Boss。** Boss 的护甲(山 117 @ 深度 20)本来就该逼玩家出大牌或
先破甲 —— 「用白字磨 Boss」不是要保护的玩法。而小怪要能被任意字清掉,否则杂兵战会卡死。

### 6.4 穿甲 3 字 → 穿透点数(`IgnoreArmor` bool 删除)

**已核实**:錰 40 / 刺 13 / 锥 9,全部 `DamageSingle`,全部金系。

今天 `IgnoreArmor` 做两件事:①`taken < 1` 时提回 1(取消减免);②无条件 `taken += 15%`。
点数制下 ① 的对应物是**穿透点数**,② 是一个与防御无关的增伤补丁。拆开处理:

| 字 | 稀有度 | 旧基础值 | ×10 | **② 的 +15% 固化进基础值** | 新 `pierce` |
|---|---|---|---|---|---|
| 錰 | 金 | 40 | 400 | **460** | **30** |
| 刺 | 蓝 | 13 | 130 | **150** | **15** |
| 锥 | 绿 | 9 | 90 | **105** | **10** |

**把 +15% 固化进基础值是精确等价的**:它今天就是无条件生效的常量乘数(`PierceBonusPercent = 15`),
对无甲目标的收益一分不差地保留下来;对有甲目标则换成了纯净的穿透。**模型少一个常量,行为不变。**

穿透点数的取值依据:錰 30 = 穿光墨渍(25)、穿光江/钧(30);刺 15、锥 10 是按稀有度递减的档位。

`EffectDef.IgnoreArmor`(bool)**删除**,`PierceBonusPercent` 常量**删除**,改为 `EffectDef.Pierce`(int,默认 0)。

### 6.5 破甲 6 字:`value` 从「回合数」变「削减点数」

**已核实**(`chars.json` 实测):6 个字**全部是复合效果**(伤害 + 破甲),`ArmorBreak value: 2`
这个 `2` 今天是**回合数**不是强度。

| 字 | 稀有度 | 五行 | 今天 | ×10 后的伤害 | **新 `ArmorBreak` 削减点数** |
|---|---|---|---|---|---|
| 碎 | ⚪白 | 土 | `DamageSingle 4` + `ArmorBreak 2` | 40 | **10** |
| 溶 | 🟢绿 | 水 | `DamageSingle 6` + `ArmorBreak 2` | 60 | **15** |
| 破 | 🟢绿 | 土 | `DamageSingle 6` + `ArmorBreak 2` | 60 | **15** |
| 熔 | 🔵蓝 | 火 | `DamageSingle 9` + `ArmorBreak 2` | 90 | **20** |
| 溃 | 🔵蓝 | 水 | `DamageSingle 9` + `ArmorBreak 2` | 90 | **20** |
| 锤 | 🔵蓝 | 金 | `DamageSingle 9` + `ArmorBreak 2` | 90 | **20** |

配置形状:`{"kind":"ArmorBreak","value":20}` —— **不再需要 `turns`**,因为破甲本场持久(4.5.2)。

#### 档位的标定依据:战例二

第 10 章 `:137` 战例二的口径是「**三张破甲字一回合削光一个 Boss 的甲**」(锋 5 + 削 3 + 刮 2 = 10,
Boss 护甲 10 → 0)。映射到本批:

| 场景 | 需要几张 |
|---|---|
| 三张蓝档破甲(20×3 = 60)削光 **山阶段 DEF 60** | **3 张** ✅ 与战例二同构 |
| 一张蓝档(20)削光 **墨渍 DEF 20** | 1 张 |
| 两张蓝档(40)削光 **深度 20 的墨渍 DEF 39** | 2 张 |
| 白档 碎(10)削掉墨渍一半 | — |

#### 三条必须一起写进 spec 的后果

1. **对无甲目标,破甲部分是纯浪费** —— 但 6 个字**全部带伤害**,所以退化成一张普通伤害字,
   不是废卡。这是它们当初被设计成复合效果的意外红利。
2. **破甲流的价值取决于装甲怪密度**,而今天全塔只有 4 个带甲目标 → 第 4.6.2 节对第八章的
   接口要求(补装甲怪)**不是锦上添花,是这 10 个金系字能不能活的前提**。
3. **本场持久 + 可叠 + 只对有甲目标有效** = 典型的**工具卡**形状:遇到装甲怪时价值极高,
   否则就是普通伤害字。这正是点数甲想要的战术分工,不是数值失衡。

#### 「易伤」这条路已作废

第二稿曾计划把 `ArmorBreak` 改名 `Vulnerable`(易伤,保留承伤 +25%)。裁定 9 否掉了它:
承伤 +25% 本身就是没有 DEF 时的代偿实现,DEF 回来后它没有存在理由。**`Vulnerable` 这个名字
本批完全不出现**,守方侧也不再有任何乘数(4.1)。

### 6.5.3 `刮` 与「裂甲」的评估:可实现,但本批不做

用户点名要评估 `第10章:137` 的 `刮` = 「破甲 2 + 挂 2 层裂甲(每回合护甲 −1)」。

**现状核实**(`chars.json`):

| 字 | v0.4 稿 | **今天的实装** |
|---|---|---|
| `刮` | 破甲 2 + 2 层裂甲 | `DamageSingle 6` + `Dispel -1`(全驱散),🟢绿,金 |
| `削` | 破甲 3(永久)+ 4 伤害 | `DamageSingle 9` + `Dispel 1`(驱散 1 条),🟢绿,金 |

两个字都**已实装且与 v0.4 稿完全不同** —— 它们今天是驱散字,不是破甲字。

**技术可行性:成本很低。** 裂甲是「DOT 式的护甲削减」,最省的实现是让它**复用 `ArmorBreak` 的
承载**,而不是自己开一条:

```
新增 EffectKind.ArmorDecay + StatusKind.ArmorDecay(Magnitude = 层数,追加末尾)
EndTurn 的状态结算里:给带 ArmorDecay 的目标追加一条 ArmorBreak(Magnitude = 层数),
                      SourceId 铸唯一序号 → 天然累加
```

零新增快照字段(仍然只是状态)、零新增结算分支(`EffectiveDefense` 一个字不改)、
约等于再做一个 DOT 的工作量。**现在确实可以实现了 —— 这是 DEF 回归带来的能力。**

**但本批不做,三条理由:**

1. **`刮` 不是缺口字,改它是字表重设计。** 本批的边界是「机制迁移 + 量级 + 重平衡」,
   不含「把已实装的字改成另一个字」。`刮` 的驱散定位有没有问题是另一个话题
   —— 而且金系今天只有 `刮`/`削` 两张驱散,拿掉一张会在别处开新缺口。
2. **裂甲相对破甲的增量,在装甲怪够多之前体验不到。** 破甲已经是「本场持久 + 可叠 + 即时」,
   裂甲的差异只是「延迟生效换更高总量」。全塔只有 4 个带甲目标时,这个差异是不可感知的。
   要先有第 4.6.2 节的装甲怪,裂甲才有评估的土壤。
3. **本批已经是 E-b 系列最大的一批**(9 个任务、一次量级重定、一次模型推倒)。加一个可延后的
   新机制不划算。

**建议:记为 E-c 候选,与第八章补装甲怪同批评估。** 本批**不动 `刮` / `削` 的字表条目**。
第 10 章 `:137` 的标注维持「v0.4 旧稿」—— 只有 `:56`「破甲永久降护甲」这一处被本批采纳为原始设计。

### 6.6 不动的

| 项 | 为什么不动 |
|---|---|
| `Blind` 2 字(熣 50 / 烟 30) | 百分比,不是血量量纲 |
| `SummonPassive.Dodge` 1 字(柳 50) | 同上,且它就是闪避通道的既有实现,第八节直接复用 |
| `Reflect` 50%、`Curse` 25% | 同上 |

---

## 七 · 命名方案:四个词各管一件事

裁定 9 之后,「破甲」的撞名问题**自行消失**了 —— 代码里的「破甲」与第 10 章 `:56` 的「破甲」
本来就是同一个东西,只是前者在没有 DEF 的年代被实现成了乘法代偿。DEF 回来,两个含义合一。
**本批不发生任何重命名。**

| 词 | 是什么 | 载体 | 序号/兼容 | 本批状态 |
|---|---|---|---|---|
| **护甲 / DEF** | 单位属性,点数,减法。**战斗中永不被写** | `BattleConfig.PlayerDefense` / `EnemyDef.Defense` / `EnemyState.Defense` | 新增 | ✅ 实现 |
| **护甲增益**(`DefenseBuff`) | 给自己**加** DEF 点数的状态 | `StatusKind.DefenseBuff` | 序号 **18**(新建;见下方订正) | ✅ 实现,6 字迁移 |
| **破甲**(`ArmorBreak`) | 给目标**减** DEF 点数的状态,本场持久、可叠 | `StatusKind.ArmorBreak` | 序号 **7**,**名字与序号都不动** | ✅ 语义复原,6 字重定值 |
| **穿透**(`Pierce`) | **本次**攻击视目标 DEF 少 N 点 | `EffectDef.Pierce` + `StatusKind.PierceBuff` | 新增 | ✅ 实现,3 字迁移 + `锐` |

⚠ **2026-08-13 实现订正**:序号 5 **未**原地改名。T2 已新建 `DefenseBuff = 18` 作为唯一载体,序号 5 退休成废弃占位 `ObsoleteDamageReduction`(不删除,删了会让 6 以后的序号全部前移、静默错位旧存档)。理由:复用旧序号且改变单位(百分点 → 点数)正是静默存档损坏的那一类,新建序号零风险。实际序号:`CritBuff=17` / `DefenseBuff=18` / `PierceBuff=19` / `DodgeBuff=20`。

**~~唯一的改名是 `DamageReduction` → `DefenseBuff`(序号 5)~~**,因为它的语义真的变了
(百分比 → 点数),名字不改会持续误导。改名不改序号 → 旧存档里的整数 5 仍指向同一条状态。

**「穿甲」与「易伤」两个词本批之后不再存在**:
- 「穿甲」(`IgnoreArmor`)并入穿透(6.4),它的 +15% 固化进基础值
- 「易伤」(`Vulnerable`)是第二稿的设计,已随裁定 9 作废(6.5 末尾)

**破甲与穿透的一句话区分**(文案要能让玩家一眼分开):

| | 破甲 | 穿透 |
|---|---|---|
| 削的是 | 目标的甲,**削掉就一直是削掉的** | 这一击的**视角**,下一击不算数 |
| 挂在谁身上 | **目标**(敌人 / 玩家) | **攻击者**(或这张字自带) |
| 持续 | 本场 | 本次(`PierceBuff` 则是本场对所有攻击生效) |
| 队友能不能蹭到 | 能 —— 甲是真的薄了,召唤物打它也吃这个便宜 | 不能 —— 是我的视角不是它的甲 |

最后一行是两者最有体感的区别,值得直接写进 UI 文案。

**文案样例**(`CharInfo` / `EnemyInfo`):

```
铠   护甲 +12                        漜   护甲 +15,减速 2 回合
碎   单体 40,破甲 10(本场)          锤   单体 90,破甲 20(本场)
锥   单体 105,穿透 10(本次无视 10 点护甲)
锐   本场穿透 +20
墨渍 护甲 20
```

**分层硬约束见 4.5.3** —— 它是「零新增快照字段」的全部依据,也是 `EnemyState.Defense` 的
XML 注释必须写的东西。

---

## 八 · 闪避:只做玩家单侧

裁定 3:玩家攻击永远必中,敌人没有闪避。**「命中」于是不是玩家属性** —— 玩家侧不存在
「命中率」这个可以被减的量,不加 `PlayerHit`,不加 `MetaRules.HitFor`,`DamageEnemy` 不加命中判定。

### 8.1 公式:签名一字不改,只把硬编码的 0 换成属性

```csharp
// 今天(BattleEngine.cs:1540)
private bool AttackHits(int enemyIndex, int dodgePercent)
{
    int blind = _enemies[enemyIndex].Statuses.TotalMagnitude(StatusKind.Blind);
    int hitRate = Math.Clamp(100 - blind - dodgePercent, 0, 100);
    if (hitRate >= 100) return true;          // ← 这条短路是随机流纪律的全部依据,不许动
    return _random.Next(100) < hitRate;
}
```

**这个式子完全不改。** 改的只有调用点:

| 调用点 | 今天传的 `dodgePercent` | 改成 |
|---|---|---|
| `DamagePlayerDirect`(`:1560`) | 硬编码 `0`,注释写「玩家没有闪避」 | **`EffectivePlayerDodge`** |
| `DamageSummon`(`:1625`) | `summon.Passive?.Dodge ?? 0` | **不动**(柳 50 已经就是闪避属性) |

```csharp
/// <summary>本场生效的玩家闪避 = 角色属性(config)+ 局内增益,钳到 [0,100]。
/// 与 EffectiveAttack / EffectiveCritChance 同形。</summary>
public int EffectivePlayerDodge =>
    Math.Clamp(_config.PlayerDodge + _playerStatuses.TotalMagnitude(StatusKind.DodgeBuff), 0, 100);
```

`StatusKind.Blind` 的定位随之明确:它是**敌人命中率的临时减益**,不是玩家属性的对手项。
不改施加逻辑,不新增枚举值。

### 8.2 随机流纪律:闪避 0 时仍然短路

这是本批唯一**仍然守得住恒等性**的地方,必须守住:玩家闪避基准 0 → `hitRate = 100 − 0 − 0 = 100`
→ 短路 return true → **一次随机都不摇**。既有的 `DamageVariantTests.NoBlindNoDodge_DoesNotConsumeRandom`
就是这条的守卫,**它一个字都不许改**。

> ⚠ 反例警告(初稿的发现,保留):补测试时不要写成「两台引擎完全一样 → 序列一致」。那种写法
> 零判别力(两边都无条件摇随机,烧掉的一样多,序列照样一致)。判别力必须来自**两边的判定次数不同**
> —— 既有那条用的是「敌人 Speed 100 vs 200,出手次数不同」。

### 8.3 闪避上限必须存在

闪避是乘性生存能力:25% 闪避 = 有效血量 ×1.33。若可堆到 60%+,肉鸽后期会出现「摸不到我」的
退化局,而且每一次敌人攻击都要摇随机数。`DodgeFor` 封顶 25(第九节),字表侧本批**不出闪避字**
(柳的 50 是召唤物被动,不进玩家池)。

---

## 九 · 成长曲线(裁定 7)

三条角色属性曲线统一形状 `min(上限, 基数 + k×(等级−1))`,统一封顶级 **26**。

```csharp
/// <summary>生命成长(量级 ×10):500 + 20×(等级−1),上限 1000。</summary>
public static int MaxHpFor(int level) => Math.Min(1000, 500 + 20 * (level - 1));

/// <summary>攻击成长:100 + 2×(等级−1),上限 150。ATK 是比值不是血量,不随量级 ×10。</summary>
public static int AttackFor(int level) => Math.Min(150, 100 + 2 * (level - 1));   // 不变

// ⚠ 没有 CritFor。暴击率不是角色成长属性 —— 见下方「裁定 8 的落点」。

/// <summary>防御成长:0 + 0.5×(等级−1),上限 12。整数除表达 k = 1/2。
/// 起点 0:护甲是土系字给的,不是白送的。</summary>
public static int DefenseFor(int level) => Math.Min(12, (level - 1) / 2);

/// <summary>闪避成长:0 + 1×(等级−1),上限 25。
/// 起点 0 与 DEF 一致(防御资源不白送);k = 1 而非 DEF 的 1/2,因为闪避是
/// 概率轴 —— 满级 25% 的期望减伤与 DEF 12 对 R_in=60 的 −20% 同量级,
/// 两条防御轴的成长感因此对齐。</summary>
public static int DodgeFor(int level) => Math.Min(25, level - 1);
```

| 等级 | HP | ATK | 暴击 | DEF | 闪避 | 综合 |
|---|---|---|---|---|---|---|
| 1 | 500 | 100 | 0 | 0 | 0 | 基准 |
| 11 | 700 | 120 | **0** | 5 | 10% | — |
| 26(封顶) | 1000 | 150 | **0** | 12 | 25% | 输出 ×1.5(暴击另计)、有效血量约 ×2.9 |

**DEF 起点 0、k = 1/2、上限 12 的依据**:对 `R_in = 60` 的一击,满级 DEF 12 = −20%,与 ATK 的 +50%、
HP 的 +100% 处在同一个「成长感」量级;更高会让等级压过字表,让土系防御字失去存在意义。

**「暴击不随等级成长」的裁定 8 在这里的落点(2026-08-12 订正)**:暴击列**恒为 0**,
**不存在 `CritFor`**。

本节第二稿曾写有 `CritFor(level) => Math.Min(25, level - 1)`,并把裁定 8 解释成
「暴击**倍率**不随等级成长」—— **这是误读,已作废**。用户当时被问的是「暴击**率**的
成长曲线」,而 `min(25, 等级−1)` 正是被否掉的那个选项的原文;选中的是「不随等级成长,
只靠字 / 养成给」。`CritMultiplierPercent` 是常量这件事从来不在选项里。

对照实现:E-b2 已落地,`Meta.cs` **一行未改**,没有 `CritFor`;`BattleConfig.PlayerCritChance`
默认 0 且 `GameRoot` 不注入。暴击率的唯一来源是 `锋`(`CritBuff 20`,吃卡等级)与将来的
养成技能。本批**不得**引入 `CritFor`。

⚠ 连带影响:`DodgeFor` 的形状原本是「与 `CritFor` 完全相同」推出来的,那个锚点已经不存在,
上面换成了独立依据(与 `DefenseFor` 对齐)。闪避随等级成长本身仍然成立 —— 它与 HP/DEF
同属防御轴,而防御轴是随等级长的;暴击属于进攻轴里用户特意留给构筑的那一条。

---

## 十 · 【最重要】放弃恒等性之后,拿什么当安全网

E-b1 用一整个子项目买下了「基准值下逐字节恒等、`Tests/` 纯追加零删除」。方案 A + 量级 ×10
必然打破它。**必须先说清楚失去了什么**:

> 恒等性的真正价值不是「测试全绿好看」,而是**判别力**:任何一条红都能立刻定位到「你改错了」,
> 因为正确的改动**不应该**产生任何行为差异。放弃它之后,「测试红了」不再携带任何信息 ——
> 红可能是对的(数值本来就该变),也可能是错的。**安全网的任务就是把这个信息补回来。**

### 10.1 四张网

| # | 网 | 抓什么 | 自动化 |
|---|---|---|---|
| 1 | **等比恒等 + 黄金轨迹**(阶段一) | 量级 ×10 里的任何非等比错误 | ✅ 全自动 |
| 2 | **DEF = 0 恒等**(阶段二上半) | 点数层接线的任何行为改变 | ✅ 全自动 |
| 3 | **定向对照测试**(阶段二下半) | 折算率算错(每条映射一测) | ✅ 全自动 |
| 4 | **仿真阳性对照探针** | 「接错了导致没生效」与「生效了但数值不对」的区分 | ⚠ 半自动,读数需人判 |

外加一张不算网但必须提的:**编译器**。删除 `EnemyDef.DamageTaken` / `EffectDef.IgnoreArmor` 会
让所有读点变成编译错误 —— 这是「改动面完整性」的机械保证。反过来,**量级 ×10 没有任何编译器
保护**(数字就是数字),这正是它必须单独一个阶段、单独用网 1 证明的原因。

### 10.2 两阶段与各自的验收标准

#### 阶段一 · 量级 ×10(可证明等价)

**行为不该变,只该等比放大。** 验收:

```
在基准切片上(ATK = 100 即 1 级、卡等级全 1、DEF 全 0、闪避 0):
  同种子、同操作序列下
    BattleEvent.Amount        必须恰好是旧值 × 10
    其余全部字段(Kind / TargetIndex / SecondIndex / 顺序 / 条数)  逐字节相同
    BattleSnapshot.RandomState / 掉落序列 / 胜负 / 回合数           逐字节相同
```

**黄金轨迹(golden trace)工装**:`tools/trace/`(或给 `tools/balance` 加 `--trace` 开关),
输入种子集合,跑完整无尽爬塔,把 `(depth, battleIndex, turn, eventKind, targetIndex, amount)`
序列落成文本文件。阶段一开工前先在 `main` 上生成 `baseline.txt`(不入 git,写进任务报告的
校验和即可),改完再生成 `after.txt`,断言上面那条规则。

**为什么这张网比「测试全绿」强得多**:它把「35~40 条断言重写」从「凭感觉改到绿」变成
**有判据的机械变换**。任何一条改动如果不是「乘 10」,就是 bug —— 这个判据是可复核的,
评审时只需要扫一遍 diff 里的数字有没有非 ×10 的。

⚠ **网 1 的盲区**:它只在基准切片上成立。ATK ≠ 100 / 卡等级 > 1 的舍入差异是本次**想要**的
变化,网 1 看不见,交给网 4。

#### 阶段二上半 · DEF 机制接线(仍然恒等!)

这是本 spec 的**结构性发现**:「把点数层接进去」和「给字/敌人配点数」是两件事,前者仍然可以
守恒等硬线,因为 `max(0, x − max(0, 0 − 0)) == x`。

于是把它拆成两个任务:

- **T2 接线**(`Defense` / `Pierce` 字段 + 结算点减法,全场值为 0,乘法层暂不删)
  → 验收:黄金轨迹与阶段一末尾**逐字节相同**。一条断言都不该红。
- **T3 配值**(删乘法层 + 写入第六节的全部映射)→ 必然发散,交给网 3。

**不这么拆的后果**:接线的 bug(比如减错了位置、DOT 误吃了 DEF)会混在数值发散里,和折算率
算错长得一模一样。拆开之后,T2 的任何一条红都 100% 是接线 bug。

#### 阶段二下半 · 配值(故意发散)

验收 = **网 3 的定向对照测试**:对第六节的每一条映射,写一条成对断言,把「折算率对不对」
变成可测的东西。

| 对照测试 | 旧模型的等价值 | 新模型 | 断言 |
|---|---|---|---|
| 墨渍 DEF 20 挨 85 伤 | `floor(85×0.7) = 59` | `85−20 = 65` | `Is.EqualTo(59).Within(9)`(±15%)。⚠ 20 是被 T3-V4 判据从折算值 25 压下来的(6.3.2),对照带宽刚好容得下 |
| 山阶段 DEF 60 挨 120 伤 | `floor(120×0.5) = 60` | `120−60 = 60` | `Within(9)` |
| 江/钧 DEF 30 挨 120 伤 | `90` | `90` | `Within(9)` |
| 铠 DEF 12,挨 60 伤 | `floor(60×0.8) = 48` | `48` | `Within(9)` |
| 漜 DEF 15,挨 60 伤 | `45` | `45` | `Within(9)` |
| 巍/磐/崟/崊 各一条 | 同式 | 同式 | `Within(9)` |
| 錰 pierce 30 打墨渍(DEF 20) | 旧:`taken` 提回 1 再 +15% → `floor(400×1.15)=460` | `460 − max(0,20−30) = 460` | 精确相等 |
| 刺 pierce 15 打墨渍 | `floor(130×1.15)=149`(旧 taken 提回 1) | `150 − max(0,20−15) = 145` | `Within(15)` |
| 锥 pierce 10 打墨渍 | `floor(90×1.15)=103` | `105 − max(0,20−10) = 95` | `Within(15)` |

带宽 ±15% 是刻意宽松的:折算本来就是近似,测试守的是「量级没错」而不是「数字精确」。
**一条超出带宽 = 折算率算错或者参考量选错**,两者都是要人来判的设计问题,不是代码 bug。

### 10.3 必然要改的测试(数值断言 —— 具体到文件与方法名)

改法一律先走网 1 的机械 ×10,再走网 3 的点数重算。

| 文件 | 方法 | 改什么 |
|---|---|---|
| `BattleEngineTests.cs` | `DamageReduction_MultipliesAcrossDifferentChars` | 乘法系数 0.68 消失 → 断言 `DefenseBuff` 合计 24(12+12);方法改名 `DefenseBuff_AddsAcrossDifferentChars` |
| | `DamageReduction_SameCharDoesNotStack` | 同上,断言 12 而非 0.8 |
| | `DamageReduction_AppliesToIncomingDamage` | 点数减法 |
| | `DamageReduction_InjectedViaConstructor_AppliesImmediately` | 同上 |
| | `Minion_DamageTaken_ReducesDamage` | `damageTaken` 字段没了 → 改用 `defense` |
| | `DamageTaken_AboveOne_SurvivesElementCounter` | **语义升级**为 4.3 的恒等式 |
| | `DamageTaken_BelowOne_StillLostToElementCounter` | 同上;两条合并或改名 `Defense_DoesNotEatCounterBonus` |
| | `ArmorBreak_RaisesDamageTakenByQuarter` | **完全改写**为 `ArmorBreak_ReducesEffectiveDefense`(承伤 +25% → 削 DEF 点数,裁定 9) |
| | `ArmorBreak_DoesNotStack_OnlyRefreshes` | ⚠ **语义反转** → `ArmorBreak_StacksAcrossChars`(4.5.2:必须可叠,否则六字互相排斥) |
| | `ArmorBreak_ExpiresAfterTwoTurns` | ⚠ **语义反转** → `ArmorBreak_PersistsForTheWholeBattle`(4.5.2:第 10 章 `:56`「永久降护甲」) |
| | `ArmorBreak_IsDebuffPolarity` | **断言不动**(极性没变,见 10.4) |
| | `IgnoreArmor_BypassesReductionAndAddsFlatBonus` | 改写为 `Pierce_ReducesEffectiveDefense` |
| | `IgnoreArmor_FlatBonusAppliesToUnarmoredToo` | **删除**(+15% 已固化进基础值,机制不存在了)⚠ 这是本批唯一的测试删除 |
| | `IgnoreArmor_StacksWithArmorBreak` | 改写为 `ArmorBreak_AndPierce_DoNotDoubleCount_NorOverflow`(4.1.2 的裁定) |
| | `Shield_PersistsThroughEnemyTurn` | 数值 ×10 |
| `BossPhaseTests.cs` | `ShanPhase_HalvesDamageTaken` | 改名 `ShanPhase_HasHeavyArmor`,断言 DEF 60 |
| | `LoadCampaign_ParsesPhases` / `ChapterScale_ScalesPhases` | 数值 ×10;后者加断言「DEF 也缩放」 |
| `BossSkillTests.cs` | `Deluge_AppliesPlayerDamageReduction` | 改名 `_AppliesPlayerDefense`,点数 |
| | `Pierce_AppliesDamageReductionToSummonHit` | 同上(注意:此处 `Pierce` 指 Boss 技能「贯穿」,与本批的穿透同名不同物,**不要顺手改名**) |
| `BurnVariantTests.cs` | `Detonate_IgnoresDamageTaken_TotalStaysFullNotHalved` | 改名 `Detonate_IgnoresDefense_*`;数值 ×10 |
| `CampaignTests.cs` | `LoadCampaign_ParsesMinionDamageTaken` | 改名 `_ParsesMinionDefense` |
| | `Scale_PreservesDamageTaken` | **语义反转** → `Scale_ScalesDefense`(见 6.3) |
| `CharTableTests.cs` | `RealConfig_KaiIsDamageReductionTwenty` | → `RealConfig_KaiIsDefenseTwelve` |
| | `RealConfig_PierceChars_CarryIgnoreArmorFlag` | → `_CarryPiercePoints`,断言 30/15/10 |
| | `RealConfig_ArmorBreakChars_CarryTwoTurns` | ⚠ **语义反转** → `RealConfig_ArmorBreakChars_CarryTheirPoints`,断言 6 字的削减点数(10/15/15/20/20/20);`value` 不再是回合数 |
| | `RealConfig_DuoIsTwoSegments` | 数值 → 120×2 |
| `ConfigLoaderTests.cs` | `LoadGraph_ParsesHitCountAndNewEffectKinds` | DTO 字段改名 |
| `DamageVariantTests.cs` | `MultiHit_EachSegmentGoesThroughArmorBreakSeparately` | 数值改写(破甲从 +25% 变削 DEF,见 4.4 末尾的说明);**追加**一条 `MultiHit_SubtractsDefensePerSegment` |
| | `HitCountDefaultsToOne_ExistingDamageUnchanged` / `HitCountZeroOrNegative_TreatedAsOne_NotADud` | 数值 ×10 |
| `EndlessTests.cs` | `IdiomBoss_FourPhases_FromTemplate` | 数值 ×10 |
| `RunEngineTests.cs` | `DamageReduction_CarriesToNextBattle` | → `DefenseBuff_CarriesToNextBattle` |
| `SnapshotRoundTripTests.cs` | `Battle_DamageReduction_Survives` / `CarriedDamageReductions_RoundTrip_AcrossFloorBreak` | 改名 + 点数 |
| `MetaTests.cs` | `MaxHpFor` 的全部 `TestCase` | ×10 |
| | `AttackFor_*` | **不改**(ATK 不 ×10) |
| `PerkTests.cs` | `HpBonus` 断言 30 | → 300 |
| `AttackStatTests.cs` | 全部数值断言 | ×10 |
| `BuffCharTests.cs`(E-b3-a) | 全部数值断言 | ×10 |
| `CritStatTests.cs`(E-b2 在写) | 全部数值断言 | ×10,**合流后由本批负责**(第十五节) |

### 10.4 改了就说明出 bug 的测试(结构/口径断言 —— 不许动断言)

这些测试里出现 `DamageReduction` / `ArmorBreak` 只是**借它当样本 Kind**,或者守的是与量级、
与防御模型无关的口径。**改名导致的编译跟随是允许的;任何一个 `Is.EqualTo(...)` 的值发生变化,
都要当成 bug 回头查。**

| 文件 | 方法 | 守什么 |
|---|---|---|
| `DamageVariantTests.cs` | `NoBlindNoDodge_DoesNotConsumeRandom` | **随机流短路硬线**(第 8.2 节) |
| | `NoBlindNoDodge_AlwaysHits` / `FullBlind_AlwaysMisses` | 命中公式口径 —— 本批不改这条式子 |
| | `Dodge_NeededForDeterministicMiss_WithPartialBlind` | 钳位口径 |
| | `Blind_MultipleSourcesStack_ClampedHitRateStaysZero` | 多源致盲相加 |
| | `Blind_ExpiresAfterItsTurns` / `BlindAll_HitsEveryEnemy` / `NeedsTarget_BlindAll_False_BlindSingle_True` | 致盲的施加与目标口径 |
| | `Miss_EmitsMissedEvent` / `Miss_DoesNotConsumeImmunityOrShield` | **打空的代价口径**(免疫不消耗、护盾不掉) |
| | `Reflect_DoesNotFireWhenSummonDodgesTheAttack` | 打空不触发反弹 |
| | `Dodge_IsCarriedOntoTheSummon` / `BlindPlusDodge_SummonTakesNothing` / `Dodge_SurvivesSaveRoundTrip` | 召唤物闪避通道(柳 50 不变) |
| | `Blind_DoesNotStopToppleShieldBreak` / `_DoesNotStopDevour` / `_StopsSearFromBurningPlayer` | 「攻击是否发生」的 gate 口径 |
| `StatusBagTests.cs` | 全部 4 条 | 状态容器语义(SourceId 覆盖、极性、TickTurns) |
| `BattleEngineTests.cs` | `ArmorBreak_IsDebuffPolarity` | 极性没变(4.5.2 表最后一行) |
| `StatusOpsTests.cs` | `Dispel_DoesNotTouchPlayerBuffs` / `Cleanse_RemovesPlayerDebuffs_KeepsBuffs` | 极性口径 —— `DefenseBuff` 必须仍是 `Buff`,`ArmorBreak` 必须仍是 `Debuff`(4.5.4) |
| `SaveGuardTests.cs` | `LegacyCarriedDamageReductions_DictShape_DoesNotWipeSave` | 旧键容错 —— **加了迁移逻辑后必须仍绿**(第十一节) |
| | `LegacyUnsealedSave_NotOpenable` | 存档封条 |
| `WuxingResolverTests.cs` | **全部** | 生克是唯一来源。它的基础值取自 `wuxing-reference.md` 的规格例,是抽象数字不是字表数字 —— **量级 ×10 绝不能波及这个文件** |
| `GameRandomTests.cs` | 全部 | 随机流 |

**新增的守卫测试**(本批要补的):

| 测试 | 守什么 |
|---|---|
| `RealConfig_ArmoredEnemiesAreRare`:遍历 `enemies.json`,断言小怪级 `defense > 0` 的不超过 1 只 | 第 4.4(a) 的配置口径,否则日后加怪时 AOE 会静默变废 |
| `Defense_DoesNotAffectBurnTick` / `_BleedTick` / `_Detonate` / `_Reflect` / `_Thorns` | 第 4.2 的负向清单(DOT 不吃 DEF)——**缺这几条,把 DEF 加到 DOT 上会让火系整条归零而无一条测试红** |
| `Defense_DoesNotAffectShieldOrHeal` | DEF 泄漏到防御资源 |
| `Defense_DoesNotBlockExecuteKill` | `ExecuteKills` 是抹血不是伤害 |
| `Pierce_OnlyOffsets_NeverOverflows`:DEF 5 + 穿透 99,伤害 20 → 打出 20 而非 114 | `max(0, …)` 的外层钳位 |
| `ArmorBreak_AndPierce_DoNotDoubleCount_NorOverflow`:DEF 20 + 破甲 20 + 穿透 30,基础 100 → 打出 **100**,且与「DEF 20 + 破甲 50」结果相同 | **裁定(4.1.2)**:两者从同一个基础 DEF 里减,不嵌套、不重复扣、不倒贴 |
| `ArmorBreak_StacksAcrossChars`:碎(10)+ 锤(20)→ 目标 DEF −30 | 可叠加(4.5.2)。**缺这条,回退成「只刷新」不会有任何测试红,而六个破甲字会静默互斥** |
| `ArmorBreak_PersistsForTheWholeBattle`:过 5 个回合仍在 | 本场持久(4.5.2) |
| `ArmorBreak_OnPlayer_ReducesPlayerDefense` | 4.5.4 的通道 —— 第八章配敌人破甲时直接可用 |
| `Defense_IsNeverMutatedDuringBattle`:施破甲前后断言 `EnemyState.Defense` 原值不变 | **4.5.3 的硬约束**。它是「零新增快照字段」的全部依据,必须可测 |
| `Order_CritBeforeDefense`:与 E-b2 合流后补(暴击乘完再减 DEF) | 结算顺序(4.1)。⚠ 第二稿的 `Order_VulnerableBeforeDefense` 随易伤方案一起作废 |
| `PlayerDodge_HundredPercent_EnemyAttackAlwaysMisses` | 玩家闪避接线(今天写死 0) |
| `PlayerDodge_Zero_ConsumesNoRandom` | 短路(与既有那条对称,判别力来自判定次数不同) |
| `PlayerAlwaysHits_EvenAgainstAnyEnemy` | **裁定 3 的守卫**:防止日后有人给 `DamageEnemy` 加命中判定 |
| `Scale_HalvesDefenseGrowth`:深度 20 的墨渍 DEF = 39 而非 58 | **裁定 11**(6.3.1) |
| `LowestTierChar_StillDentsArmoredMobAtDepth20`(= T3-V4) | **半速缩放的可执行判据**(6.3.2)。它对「同速缩放」这个变异有判别力,不是装饰性断言 |
| `(int)StatusKind.DefenseBuff == 5` / `(int)StatusKind.ArmorBreak == 7` | **枚举序号锁值**(第十一节)。枚举错位是静默破坏旧存档,现有测试全都用新写的对象、不读旧 JSON 字节,没有任何一条能发现 |

### 10.5 仿真工装:怎么让它有判别力

⚠ 用户点名的坑:**「没变化」和「测不出来」在仿真数据里长得一模一样。** E-a 时工装的
`UnlockedChars` 锁在 9 个火系字上,新字进不了池,「P50 没变」被误读成「没有影响」。

E-b1 的解法是梯度证明(1 级恒等 + 3/10 级发散)。仿真侧的同构物是 **阳性对照探针**:

> **先让工装证明它能看见 DEF,再用它读数。**

`tools/balance/Program.cs` 在现有三档画像之外,新增两档**探针画像**(它们不是平衡目标,
只是仪器的自检):

> ⚠ **2026-08-13 实现订正:下表的原方案有两处会给出错误结论,已按实测改掉。**
>
> **(a) 堆甲探针的卡组「6 张护甲字 + 1 输出字」会给出反向结论。**实测 P50 = 3,
> **低于**对照 —— 六张护甲字占掉 6/7 掉字、同字只刷新不叠加,机器人打不动怪,
> 60 回合僵局被记成「卒于当层」。那套探针量的是**僵局判定**,会把「接好了」误报成
> 「接坏了」,**比没有探针更糟**。改成**只换起手四张**(铠漜崊崟),出阵卡组与对照
> 逐字相同 —— 变量收敛到一个。
>
> **(b) 探针必须从词渊(11 层)起爬,否则遇不到甲。**三档基线实测遇到带甲敌人
> 0.0 / 0.0 / 0.1 次,是瞎的。改后每次爬塔 1.4 / 0.8 次带甲多怪遭遇。
>
> **(c) 需要第三档「探针·对照」**(火系,同样深启 11)。三档基线从 1 层起爬、
> 从没见过甲,拿它们当参照物两个探针的方向都无从判起。所以仿真是**六档**不是五档。

| 探针 | 卡组 | 预期方向(不满足 = 工装瞎了或接线错了) |
|---|---|---|
| **探针·对照**(火系,深启 11) | 与堆甲探针出阵卡组逐字相同 | 参照物本身,无方向要求 |
| **土系堆甲**(起手 铠漜崊崟,深启 11) | 出阵表同对照 | P50 必须**高于**对照。持平 → `DefenseBuff` 没接进伤害链路。**实测 15 vs 14 ✓;断掉接线后跌到 13 ✓(判别力证明)** |
| **AOE 专精**(全 `DamageAll` 字,深启 11) | 全 AOE | P50 必须**低于**对照。**⚠ 实测虽满足(12 vs 14),但它是因为错误的原因通过的** —— 变异「墨渍 DEF 20 → 0」只让它动 0.2 层,那 2 层差距主要来自字表数值(AOE 池 50~70 vs 火系 炎 200/燚 300)。**T8 不得拿它校准 AOE 轴**,要观测点数 DEF 对 AOE 的 N 倍惩罚需要一档**数值对齐的单体对照**,现有字表凑不出来 |

另外必须做的接线(否则 E-b5 的调值是瞎调):

- `Profile` 增加 `Defense` / `Dodge` 字段,三档画像喂 `MetaRules.DefenseFor(level)` / `DodgeFor(level)`
  (E-b2 会同时加 `Crit`,合流时三个字段并排)
- `FireCards` 那张画像出阵表要**扩到覆盖新字**(`锐` / `兑`),否则又重演 E-a 的「工装看不见新字」

**读数纪律**:

- 记录基线:阶段一末尾跑一次(DEF 全 0)、T3 末尾、T4 末尾、T8 末尾各跑一次,把
  五档画像的 `(均卒层, P50, P90, 达 11/26/51 层比例)` 全部写进任务报告
- **P50 的绝对变化不是通过/失败判据。** 判据只有一条:**探针按预期方向动了。**
  P50 的数值用于 T8 的调值决策,由人判断
- 阶段一的仿真结果应当与改动前**完全相同**(×10 是等比的,机器人的贪心 `Power()` 排序也等比)
  —— 这是网 1 在仿真侧的免费复核。⚠ `Power()` 里 `Shield: value/2` 与 `HealSelf: value/2` 是
  整数除,×10 后排序**可能**变 —— 若阶段一仿真结果不同,先查这里再怀疑别处

---

## 十一 · 快照与存档迁移

### 11.1 问题的规模比 DEF 大

用户问的是 `EnemySnapshot.DamageTaken` 怎么办,但真正的问题是量级 ×10:

| 存档里的数据 | ×10 后是否失效 |
|---|---|
| `EndlessSaveState.PlayerHp` | ✅ 失效(旧 50,新上限 500) |
| `EndlessSaveState.NormalShield` / `PersistShield` | ✅ 失效 |
| `EndlessSaveState.CarriedSummons[].Hp/MaxHp/Attack` | ✅ 失效 |
| `RunSnapshot.CarriedStatuses[]` 里的 `DamageReduction 20` | ✅ 失效(百分比 → 点数,20 点 DEF ≈ 铠 的 1.7 倍) |
| `RunSnapshot.CarriedHp` / `MaxHpBonus` | ✅ 失效 |
| `BattleSnapshot.PlayerHp` / `BurnPerStack` / 全部 `EnemySnapshot.Hp/MaxHp/BaseAttack` | ✅ 失效 |
| `EnemySnapshot.DamageTaken` | ✅ 语义作废 |
| **`Ink` / `CardLevels` / `PerkLevels` / 图鉴 / 出战牌组 / `CharacterXp`** | ❌ **不失效**(经济与长期资产不动) |

**逐字段迁移是不可验证的**:要写 8 个字段的一次性转换代码,只为一次版本切换服务,而且**没有
任何旧存档样本可以用来测试它**。丢弃是可验证的。

### 11.2 方案:改键名让登塔快照优雅作废

**沿用仓库里已经验证过的前例** —— `CarriedDamageReductions` → `CarriedStatuses` 那次改名
(`Endless.cs:191` 的注释完整记录了理由):改键名 = 旧数据变成未知键 = Newtonsoft 直接忽略 =
优雅降级,而不是抛 `JsonException` 被 `SaveSerializer.FromJson` 兜底成**整份存档清空**。

| 改动 | 效果 |
|---|---|
| `MetaState.Endless` 属性改名 → **`EndlessV2`**(类型仍是 `EndlessSaveState`) | 旧存档的 `"Endless"` 键成为未知键被忽略 → `EndlessV2` 为 null → 玩家的**进行中登塔作废**,回到主界面可重新开塔 |
| `EnemySnapshot.DamageTaken` 字段 **删除** | 随整个登塔快照一起失效,零风险 |
| ~~`StatusKind` 序号 5 原地改名~~ ⚠ **已作废,见第七节订正**:序号 5 退休成 `ObsoleteDamageReduction`,`DefenseBuff = 18` 新建 | 旧存档里的整数 5 不再被任何活代码解读;它随 `EndlessV2` 一起丢了,读不到 |
| `StatusKind` 序号 7 **完全不动**(`ArmorBreak`) | 裁定 9 之后连改名都不需要;它只出现在 `EnemySnapshot.Statuses` 里,同样随登塔快照丢弃 |

**零迁移代码、零新增版本字段。**

### 11.2.1 【裁定 12】断点直接丢弃,不补发 —— 以及这条路径的语义确认

用户裁定:**不写补发结算宝箱的逻辑。** 理由(采纳):v0.7 仍在开发期未上线,现实里受影响的
只有开发者自己的测试存档;补发逻辑是一段一次性代码,**写完即成死代码**。

**确认这条路径在「丢弃断点但保留养成外层」这个语义下确实成立** —— 逐条核对丢与留的边界:

| 数据 | 存在哪 | 改键名后 |
|---|---|---|
| 角色等级 / 经验 `CharacterXp` | `MetaState` 顶层 | ✅ **保留** |
| 墨锭 `Ink` | `MetaState` 顶层 | ✅ **保留** |
| 字库 / 卡等级 `CardLevels` / 出战牌组 `Deck` | `MetaState` 顶层 | ✅ **保留** |
| 养成技能 `PerkLevels` | `MetaState` 顶层 | ✅ **保留** |
| 图鉴 `Bestiary` | `MetaState` 顶层 | ✅ **保留** |
| 历史最高 Boss 层 `TopBossDepth` | ⚠ **在 `EndlessSaveState` 里面** | ❌ 随登塔一起丢 |
| 进行中登塔(HP / 护盾 / 携带召唤物 / 携带状态 / 战中快照) | `EndlessSaveState` | ❌ 丢(**这正是目的**) |

⚠ **一处必须点名的不对齐**:`TopBossDepth`(`Endless.cs:185`)记的是「**本次**登塔已破的最高
Boss 层」,它是**本次登塔的**字段而不是历史最高 —— 所以它随登塔作废是语义正确的,不是资产丢失。
但要在实现时确认:**结算宝箱档位读的是 `TopBossDepth`,而结算发生在登塔结束时**,登塔都作废了
自然没有结算。若日后要做「历史最高层」这种真·长期资产,它必须放在 `MetaState` 顶层而不是
`EndlessSaveState` 里 —— 这条顺手记进 spec,免得将来重蹈。

**需要补的测试**:

- `LegacyEndlessKey_IsIgnored_KeepsMetaProgress`:喂一份带 `"Endless":{...}` 的旧存档 JSON,
  断言 `Ink` / `CardLevels` / `PerkLevels` / `CharacterXp` / 图鉴**全部完好**,且 `EndlessV2 == null`
- 既有的 `LegacyCarriedDamageReductions_DictShape_DoesNotWipeSave` 必须**仍绿**
  —— 它守的正是同一条机制(改键名 ≠ 整份存档清空),两条测试互为佐证

### 11.3 新增 `StatusKind` 一律追加末尾

`SaveSerializer` 用 `JsonConvert.SerializeObject` 且**没有注册 `StringEnumConverter`** →
Newtonsoft 默认把枚举序列化成**整数**。在 `StatusKind` 中间插入任何新值,都会让旧存档里的
所有状态静默错位。

当前末尾状态(2026-08-12,E-b2 分支上):`… Morale(15), ApBoost(16), CritBuff(17)`。

本批新增的状态一律**追加到 `CritBuff(17)` 之后**。⚠ **实际落地序号:`DefenseBuff=18` / `PierceBuff=19` / `DodgeBuff=20`** —— `DefenseBuff` 因为改用新建(见第七节订正)占掉了 18,`DodgeBuff` 顺延到 20。
⚠ **与 E-b2 的合流顺序决定序号** —— 见第十五节。

这条惯例此前只写在 E-b2 spec 里,本批要把它写进 `StatusKind` 的定义处注释,并用
第 10.4 节的锁值测试把它从「靠人记得」变成「可测」。

### 11.4 战中快照:零新增字段

| 数据 | 存在哪 | 新字段? |
|---|---|---|
| 玩家 DEF / 闪避 | `BattleConfig`(`GameRoot` 按等级注入),`Restore` 本就接收 config | ❌ 不需要(与 E-b1 的 `PlayerAttack` 同款) |
| 敌人 DEF | `EnemyDef`(配置侧),`Restore` 按 `DefId` 查回 | ❌ 不需要,**前提是它在战斗中永不被写**(第七节的规则) |
| 局内 DEF / 闪避 / 穿透增益 | `_playerStatuses` → `BattleSnapshot.PlayerStatuses`(已在存) | ❌ 不需要新快照字段,但需要新 `StatusKind` 值 |
| 召唤物闪避 | `SummonPassive.Dodge` 已进 `SummonSnapshot` | ❌ 不需要 |

**净结果:删掉一个字段(`EnemySnapshot.DamageTaken`),不加任何字段。**

---

## 十二 · `锐` 与 `兑` 入表(裁定 6)

### 12.1 规格

以《技能机制详表》为准(裁定 2):`锐` = **穿透 +N**,Buff 型(「本场」)。
第 10 章 10.3.2 的「单体 14,对护甲 0 的敌人翻倍」是 v0.4 旧稿,不采用。

| 项 | 值 |
|---|---|
| 字 | `锐` |
| 配方 | `钅 + 兑`(第 10 章给的配方,配方本身可用) |
| 稀有度 / 五行 / AP | ⚪白 / 金 / 1 |
| 效果 | `PierceBuff 20` —— **本场穿透 +20**,可叠加、本场持久 |
| 详表里的「穿透 +3」 | 那是 6 伤量级下的数字;×10 之后 30,再按「一张白字不应单独穿光坚壁 Boss(60)」下调到 **20** |

`20` 的定位:穿光墨渍(25)的 80%、穿光江/钧(30)的 2/3;两张 `锐` 叠满 40,配合 錰 的
本体穿透 30 可以打穿山阶段的 60。**穿透成为一条要投入的轴,而不是白送。**

新增:
- `EffectKind.PierceBuff`(追加末尾)
- `StatusKind.PierceBuff`(追加末尾,`Polarity = Buff`,`TurnsLeft = -1`,SourceId 铸唯一序号以允许叠加 —— 与 `Empower` 同款)
- `EffectivePierce = effect.Pierce + _playerStatuses.TotalMagnitude(StatusKind.PierceBuff)`

### 12.2 `兑` 是配方缺口,必须先补

**已核实**:`chars.json` 里 `钅` 在、**`兑` 不在**、`锐` 不在。

这与 MEMORY 里记的「炼/杨/戟/塌 4 个字因配方缺口拿不到」是**同一类问题**,而且那次的教训是
「管线只 `print` 警告,不报错」—— 补 `兑` 时要顺带确认管线不会静默吞掉它。

`兑` 作为部件入表(与 `钅` / `刂` 同款:只有 `id` + `element`,无 `effects`、无 `recipe`):

```json
{"id": "兑", "element": "Metal"}
```

⚠ 部件的获取路径是**拆字**(2026-08-04:五行部件改为只能靠拆字获得)。`兑` 只被 `锐` 一个字用,
要确认它能通过拆解某个已有字拿到,否则 `锐` 仍然是拿不到的死条目 —— **这是 T5 的验收项**,
不是可选检查。

### 12.3 详表要同步更新

`docs/design/字选型/技能机制详表.md:431` 的 `锐` 行从「⚠ 待 E-b4」改为已落地,并写明
`PierceBuff 20`;第 435 / 580 / 609 行的「待 E-b4 穿透」表述一并更新。
⚠ 该文件 E-b2 也会改(`锋` 那行),**改之前先看有没有冲突**。

---

## 十三 · 涉及文件

| 文件 | 改动性质 |
|---|---|
| `Core/Meta.cs` | `MaxHpFor` ×10;新增 `DefenseFor` / `DodgeFor` |
| `Core/Perk.cs` | 养元 / 金汤 的 `PerLevelValue` ×10 |
| `Core/BattleEngine.cs` | `BattleConfig` 加 `PlayerDefense` / `PlayerDodge`;新增 `EffectiveDefense(敌人/玩家)` / `EffectivePlayerDodge` / `EffectivePierce`;`DamageEnemy` 末尾减 DEF、删 `taken` 那一整段(含「被克制则减免失效」补丁);`DamagePlayerDirect` 删 `ReducedDamage` 改减 DEF、传真实闪避;`DamageReductionMultiplier` / `ReducedDamage` **删除**(及其 7 个调用点);`ArmorBreakPercent` / `PierceBonusPercent` **两个常量都删除**;`_burnPerStack` / `ScorchGain` ×10;`ArmorBreak` 施加分支改写(`Value` = 点数、`TurnsLeft = -1`、`SourceId` 铸唯一序号);新增 `DefenseBuff` / `DodgeBuff` / `PierceBuff` 效果分支 |
| `Core/EnemyDef.cs` | `EnemyDef` / `BossPhaseDef` / `EnemyState` 的 `DamageTaken` **删除**,换成 `Defense`(int,默认 0,**只读、无 internal setter** —— 4.5.3 的硬约束靠这个在类型层面兜住);`Capture` / `Restore` / `ApplyPhaseStats` 跟随 |
| `Core/EffectDef.cs` | `IgnoreArmor`(bool)**删除** → `Pierce`(int,默认 0);`EffectKind.DamageReduction` → `DefenseBuff`;`EffectKind.ArmorBreak` **名字不动、`Value` 语义从回合数变削减点数**(改注释);追加 `PierceBuff` / `DodgeBuff` |
| `Core/StatusEffect.cs` | ⚠ 序号 5 退休成 `ObsoleteDamageReduction`(**不删除**),`DefenseBuff` 新建于 18;**序号 7 `ArmorBreak` 名字不动,注释改成「削减 DEF 点数」**;末尾追加 `DodgeBuff` / `PierceBuff`;把「新值一律追加末尾」的存档约束写进注释 |
| `Core/Campaign.cs` | `Scale()` 的「承伤系数不缩放」→ **DEF 按 `1 + (scale−1)/2` 半速 `Ceiling` 缩放**(裁定 11) |
| `Core/RunSnapshot.cs` | `EnemySnapshot.DamageTaken` **删除** |
| `Core/Meta.cs`(`MetaState`) | `Endless` → `EndlessV2` |
| `Core/RunEngine.cs` | 跨战斗携带态白名单 `DamageReduction` → `DefenseBuff` |
| `Data/ConfigLoader.cs` | 敌人 DTO `damageTaken` → `defense`(int,默认 0);效果 DTO `ignoreArmor` → `pierce`(int,默认 0);`kind` 字符串 `DamageReduction` → `DefenseBuff`(`ArmorBreak` 字符串不变) |
| `StreamingAssets/config/chars.json` | 231 字的数值 ×10;15 个字的迁移(6 减伤字换 `kind` + 6 破甲字的 `value` 从回合数换成点数 + 3 穿甲字换 `pierce` 并把 +15% 固化进基础值);新增 `兑` / `锐`。**`刮` / `削` 不动**(6.5.3) |
| `StreamingAssets/config/enemies.json` | 13 只怪 + 全部 Boss 阶段的 HP/攻击 ×10;4 条 `damageTaken` → `defense`;`events` 的 `hpDelta` 与文案数字 ×10 |
| `Presentation/GameRoot.cs` | 注入 `PlayerDefense` / `PlayerDodge`(与 `PlayerAttack` 并排) |
| `Presentation/MapView.cs` | 属性上屏(HP / 攻击 / 暴击 / 护甲 / 闪避) |
| `Presentation/CharInfo.cs` / `EnemyInfo.cs` / `EnemyPreview.cs` / `BattleView.cs` | 第七节的四词文案;敌人护甲 chip 替代承伤 chip |
| `tools/balance/Program.cs` | `Profile` 加 `Defense` / `Dodge`;新增两档探针画像;`FireCards` 扩表 |
| `tools/trace/`(新) | 黄金轨迹工装(第 10.2 节) |
| `tools/pipeline/` | 补 `兑` 部件与 `锐` 的配方 |
| `docs/design/字选型/技能机制详表.md` | `锐` 落地;**7.1 节「破甲 = 提高承伤系数」整段重写为「破甲 = 削减 DEF 点数」**(那一节自己就写着「引擎没有护甲概念,所以按承伤系数处理」—— 前提消失了);6 个破甲字的效果列改点数 |
| `docs/design/第10章-战斗数值框架.md` | ⚠ **本批只需改一处**:`:56`「破甲永久降护甲」的 v0.4 旧稿标注**撤回** —— 它是原始设计,本批采纳(4.5.1)。`:137` 战例二的标注**保留**(`刮`/`削` 不动,6.5.3)。⚠ 该文件另一个 agent 也在改,动之前先看冲突 |

---

## 十四 · 任务切分与并行性

`BattleEngine.cs` 是所有人的必争之地 —— 任何两个改它的任务都必须串行,或者接受手工合并。

| # | 任务 | 交付物 | 验收 | 依赖 | 碰 `BattleEngine.cs`? |
|---|---|---|---|---|---|
| **T0** | 黄金轨迹工装 | `tools/trace/` | 同 commit 跑两次逐字节相同;换种子不同 | 无 | ❌ |
| **T1** | 量级 ×10 | 两个 json + `Meta.cs` + `Perk.cs` + 两个常量 | **网 1**:轨迹 amount 全 ×10、其余逐字段同;既有数值断言机械 ×10 | T0 | ✅(仅两个常量) |
| **T2** | 点数层接线(值全 0) | `Defense` / `Pierce` 字段 + 结算点减法,乘法层暂留 | **网 2**:轨迹与 T1 末尾**逐字节相同**,零断言变红 | T1 | ✅ |
| **T3** | 删乘法层 + 写入映射 + **破甲语义复原** | 第六节全部数值;`DamageReduction` → `DefenseBuff` 改名;**`ArmorBreak` 改语义(削点数 / 本场 / 可叠)**;`Scale` 半速缩放 | **网 3**:12 条定向对照;编译器指出的读点全处理;10.4 清单零变动;**外加 T3-V1~V4**(见下) | T2 | ✅ |
| **T4** | 玩家闪避 | `PlayerDodge` / `DodgeFor` / `DodgeBuff` | 闪避 0 时轨迹逐字节同;闪避 100 必空;`PlayerAlwaysHits` 守卫 | T3 | ✅ |
| **T5** | `锐` + `兑` 入表 | 管线 + 字表 + `PierceBuff` + 详表 | `锐` 可合成**且 `兑` 可通过拆字获得**(12.2) | T3(要量级与穿透量纲) | ✅ |
| **T6** | 存档迁移 | `Endless` → `EndlessV2`;删 `EnemySnapshot.DamageTaken` | 旧存档 Ink/卡等级/图鉴完好且 `EndlessV2 == null` | T1 | ❌ |
| **T7** | 表现层 + 仿真接线 | 四词文案、属性上屏、探针画像 | `prescompile` 0 error;探针按预期方向动(10.5) | T4 | ❌(只读 API) |
| **T8** | 重平衡校准 | **只改 json** + **一份给第八章的接口要求清单**(4.6.2) | 五档画像读数写进报告;探针方向正确;P50 落在目标带 | T7 | ❌ |

### 并行性

```
T0 ──────────────────────────────────────────────────  (全程可并行,不碰 Core)
     └─ T1 ─ T2 ─ T3 ─┬─ T4 ─ T7 ─ T8
                      ├─ T5                (T5 与 T4 都碰 BattleEngine → 串行或手工合并)
                      └─ (T6 从 T1 起即可并行,只碰 Data/Core 的存档面)
```

- **T0 与全部任务并行** —— 它是纯工装,不碰 `Core`。**且它必须最先启动**,因为 T1 的验收依赖它。
- **T1 → T2 → T3 → T4 严格串行**,全部碰 `BattleEngine.cs`,且后一个的验收基线是前一个的末尾状态。
- **T5 的管线部分**(把 `兑` 加进 IDS 候选表)可从 T0 起并行;**字表部分**要等 T3(穿透量纲)。
- **T6 从 T1 完成起可与 T2~T5 并行**(改 `MetaState` / `RunSnapshot` / `SaveGuard`,与 `BattleEngine` 不重叠)。
- **T7 的 `tools/balance` 那半**可与 T3 并行;**Presentation 那半**必须等 T4(API 定型)。
- **T8 必须最后**,且它**只改 json** —— 这条是纪律:校准阶段一旦开始改代码,网 3 的对照测试就
  失去基线,分不清是折算错还是公式错。

### T3 的四条追加验收(裁定 9 与 11 的落点)

T3 是本批最大的一块,裁定 9(破甲复原)与裁定 11(半速缩放)都落在它身上。除网 3 之外:

| # | 验收 | 对应守卫测试 |
|---|---|---|
| **T3-V1** | 破甲**可叠**:碎(10)+ 锤(20)→ 目标 DEF −30 | `ArmorBreak_StacksAcrossChars` |
| **T3-V2** | 破甲**本场持久**:过 5 个回合仍在 | `ArmorBreak_PersistsForTheWholeBattle` |
| **T3-V3** | 破甲与穿透**不重复扣、不倒贴**,且 `EnemyState.Defense` 原值未被写 | `ArmorBreak_AndPierce_DoNotDoubleCount_NorOverflow` + `Defense_IsNeverMutatedDuringBattle` |
| **T3-V4** | **半速缩放判据**:深度 20 时最低伤害档的字打墨渍仍有非零输出;且把缩放改成同速时这条必须变红 | `LowestTierChar_StillDentsArmoredMobAtDepth20` + `Scale_HalvesDefenseGrowth` |

T3-V4 的「改成同速必须变红」是关键 —— 它保证这条判据有判别力而不是装饰(6.3.2 已算过三组数)。

### 任务粒度的取舍

E-b1 是 6 个任务,本批是 9 个 —— 多出来的三个(T0 工装、T2 接线独立、T8 只改 json)**全部是
为了安全网服务的切分**,不是工作量本身变多了。若把 T2 并进 T3,就丢掉网 2;若把 T8 并进 T3,
就丢掉「代码冻结后调值」这条纪律。**这三刀是本 spec 的主要产出之一,不要合并。**

**裁定 9~12 没有改变任务数。** 逐条核对:破甲语义复原落在 T3 内(它本来就要改这 6 个字与
`ArmorBreakPercent`,只是改法不同);半速缩放是 T3 里 `Campaign.Scale` 的一行;堆甲归零是
「不写 `max(1,…)`」= 零工作量,它的产出(第八章接口要求)是 **T8 的一份文档交付物**,不是新任务;
存档不补发 = 删掉一段原本要写的代码。裂甲不做(6.5.3)也没有新增任务。

---

## 十五 · 与 E-b2 的合流纪律

E-b2(暴击,分支 `feat/crit`)与本批在 `BattleEngine.cs` 的同一段代码上作业。

| 项 | 纪律 |
|---|---|
| **合流顺序** | **E-b2 先合并到 `main`,本批的 T1 才开工。** 本批会 ×10 全部数值,而 E-b2 正在写的 `CritStatTests.cs` 里全是旧量级的断言 —— 反过来合并意味着 E-b2 的测试要在合并时重写一遍 |
| **结算顺序** | `floor(基础 × 生克 × 暴击) − max(0, DEF − 破甲 − 穿透)`。**暴击乘在最末,DEF 减在暴击之后。** 若反过来(先减 DEF 再暴击),暴击会把 DEF 的削减也放大,等价于「暴击时护甲变薄」。⚠ 裁定 9 之后守方侧**没有任何乘数**,E-b2 那条「暴击乘在 `taken` 之后」的表述要跟着更新 —— `taken` 不存在了 |
| **`StatusKind` 序号** | 实际落地:`CritBuff=17` / `DefenseBuff=18` / `PierceBuff=19` / `DodgeBuff=20`。**若合流顺序改变,序号跟着变** —— 第 10.4 节的锁值测试要写实际值,不要写「末尾」 |
| **随机流** | 两边都要守 `hitRate >= 100` / `critChance <= 0` 的短路。合流后**必须重跑一次** `NoBlindNoDodge_DoesNotConsumeRandom`,并补一条「暴击 0 + 闪避 0 时不消耗随机数」的交叉验证 |
| **仿真 `Profile`** | 合流后三个新字段并排:`Crit`(E-b2)/ `Defense` / `Dodge`(本批) |

---

## 十六 · 已否决方案与理由

保留它们的实测数字 —— 那些数字是本次决策的依据,也是日后回头质疑时的材料。

### 方案 B · 两层并存(初稿的推荐,已否决)

保留乘法层原封不动,新增一层点数 DEF、基准 0。

| 维度 | 量 |
|---|---|
| 改动面 | ~15 处**新增**,0 处改写,跨 5 个文件 |
| 既有测试变红 | **0 条**(全链路逐字节恒等) |
| 字表要改 | **0 个** |
| 存档 | 零新增快照字段 |
| 恒等性 | **守得住** |

**否决理由**:① 用户裁定 A;② 它把 `锐` 卡在中间(全场 DEF = 0 时「穿透 +3」的实际效果是 0,
上线即废字);③ 乘法层迟早要收编,B 只是把同一批测试的红推迟一次并让它红两遍;
④ 玩家要同时理解「受伤 −20%」和「护甲 12」两种防御,以及「穿甲」「穿透」两个近义词。

**B 的分析里仍然有效、已被本 spec 吸收的部分**:
- 「属性 vs 状态」的划分(点数层只放属性,变动量一律走状态)—— 本 spec 第七节采用了它,
  用来规避「破甲削 DEF 会逼出新快照字段」这个坑
- `Hits` 短路是随机流纪律的全部依据 —— 第 8.2 节原样保留

### 方案 C · 半迁移:敌人转点数,玩家留百分比(已否决)

| 维度 | 量 |
|---|---|
| 改动面 | ~22 处,跨 7 个文件 |
| 既有测试变红 | ≈ 12~15 条 |
| 字表要改 | `enemies.json` 4 条;`chars.json` 0 个 |
| 存档 | `EnemySnapshot.DamageTaken` 作废 → 需迁移 |
| 恒等性 | 破 |

**否决理由**:承担了 A 的大半代价(测试红、存档迁移、必须与 E-b5 同批),却没拿到 A 的核心收益
(模型唯一)。玩家侧仍是百分比,而 `锐` 是玩家的字 —— 它穿的到底是哪一层反而更含糊。

### 初稿实测的改动面数字(核实结论见下)

| 项 | 初稿数字 | 本次核实 |
|---|---|---|
| `DamageReduction` 字 | 6(铠20/漜25/崊20/崟15/磐10/巍5) | ✅ 一致 |
| `ignoreArmor` 字 | 3(錰40/刺13/锥9) | ✅ 一致,且全部 `DamageSingle` / 全部金系 |
| `ArmorBreak` 字 | 6(熔溃溶锤破碎,各 2 回合) | ✅ 一致 |
| `damageTaken ≠ 1` 的敌人 | 1(墨渍 0.7) | ✅ 一致 |
| `damageTaken ≠ 1` 的 Boss 阶段 | 3(山 0.5 / 江 0.75 / 钧 0.75) | ✅ 一致 |
| `Blind` 字 | 2(熣50/烟30) | ✅ 一致 |
| `passive.dodge` 字 | 1(柳 50) | ✅ 一致 |
| 直接吃这套模型的测试方法 | 35~40 | ⚠ **数对了一半**。按 `DamageReduction`/`DamageTaken`/`ArmorBreak`/`IgnoreArmor`/`Blind`/`Dodge`/`HitCount` 扫描,命中 **69 个方法**:其中 **≈ 38 条要改断言**(10.3),**≈ 31 条只需跟随改名、断言一个字不动**(10.4)。初稿数的是前者,量级对;但它**漏掉了后 31 条的存在** —— 而那 31 条恰恰是本 spec 第 10.4 节安全网的主力。另外初稿完全没算到量级 ×10 会波及 `MetaTests` / `PerkTests` / `AttackStatTests` / `BuffCharTests`(它们一个防御 token 都不含,却全是数值断言)|
| 多段字 | 只有 `剁`(10×2) | ✅ 一致 |

---

## 十七 · 裁定归档:已全部拍板,无待决项

第二稿留下的四条待拍板,2026-08-12 全部裁定。**本节只作归档,不再有开放问题。**

### 1. ✅ 破甲不改名,改回原始设计(裁定 9)

第二稿问的是「破甲 → 易伤 的玩家可见文案改不改」,三个选项全部作废 —— 用户给的是**第四种答案**:

> 破甲的设计初衷就是扣 DEF,只是那时候没有 DEF 的概念。本质上是一个东西。

于是正确的结论不是命名取舍,而是**代偿实现退场**:`ArmorBreak = 承伤 +25%` 是 2026-08-05
「承伤与护甲」子项目为了绕开「引擎里没有 DEF」而做的代偿;DEF 回来了,它就该变回削 DEF 点数。
名字与枚举序号一个字不改。

**连带后果比命名大得多**:守方侧的最后一个乘数消失 → **点数 DEF 成为唯一的防御层,零乘数**
(4.1)。方案 A 的「模型唯一」比第二稿更彻底。落点:4.5(语义与硬约束)、6.5(6 字重定值)、
七(命名归位)、4.1.1 / 4.1.2(结算顺序与不重复扣)。

### 2. ✅ 堆甲允许把小怪普攻打到 0(裁定 10)

不加 `max(1, …)` 保底,`伤害 = max(0, 基础×生克 − EffectiveDefense)`。土系铁乌龟构筑成立。

对冲手段(敌人破甲 / 真伤)是**第八章的活**,本批交付两样东西:① 4.5.4 把「敌人给玩家挂
破甲」的通道打通;② 4.6.2 的接口要求清单(装甲怪密度 + 对冲手段分布 + 底线判据),
作为 T8 的文档交付物。

**为什么不选 `max(1,…)`**:它会让穿透在残局失去意义(反正保底 1 点),而穿透是金系两条轴之一。

### 3. ✅ 敌人 DEF 半速缩放(裁定 11)

`defScale = 1 + (scale − 1) / 2`。理由:点数减法对小数值是**开关**不是削减,同速缩放会让深层
只剩高伤字可用。

这条裁定带来一个**有判别力的可执行判据 T3-V4**(6.3.2),而判据反过来**把墨渍的 DEF 从
折算值 25 压到 20** —— 判据优先于折算率。这个因果链留在 spec 里,免得日后有人看到
`(1−0.7)×85 = 25` 以为配置写错。

### 4. ✅ 断点作废,不补发(裁定 12)

不写补发结算宝箱的逻辑。理由采纳:v0.7 未上线,受影响的只有开发者自己的测试存档;
补偿代码写完即成死代码。

改键名(`Endless` → `EndlessV2`)这条路径在「丢弃断点但保留养成外层」语义下**确认成立**,
逐字段核对见 11.2.1 —— 并顺手记下一处不对齐:`TopBossDepth` 在 `EndlessSaveState` 里面,
它是「本次登塔」的字段而非历史最高,随登塔作废语义正确;将来若要做真·历史最高层,
必须放在 `MetaState` 顶层。

### 附:本次没有新增待决项

裁定 9 改动了机制设计,但没有留下新的开放问题。两个可能被误认为待决的,都已就地裁定:

| 看起来像待决 | 实际 |
|---|---|
| 破甲要不要可叠、要不要本场持久 | ✅ **都要**(4.5.2)。不叠则六字互斥,不持久则战例二那套玩法不成立 |
| 穿透与破甲同时存在怎么算 | ✅ **合并相减,一个 `max(0,…)`**(4.1.2)。与「分别嵌套」数学等价,取少一次钳位的写法 |
| `刮` / 裂甲要不要一起做 | ✅ **不做**,记为 E-c 候选(6.5.3)。技术上已可实现,但装甲怪够多之前体验不到增量;且 `刮` 已实装为驱散字,改它是字表重设计 |
