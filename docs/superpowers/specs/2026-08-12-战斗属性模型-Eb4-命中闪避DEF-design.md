# E-b4 命中/闪避/DEF 改造 设计

> 战斗属性模型五步拆分的第 4 步。父项目:把玩家从「字牌数值即伤害」改造成持有真实属性的战斗实体。
> 本步把**命中 / 闪避 / DEF** 三条从「零散机制」提升为真属性,并落地 `锐`(穿透 +3)。

**日期**:2026-08-12
**上游**:用户裁定「属性集 = HP / ATK / DEF / 暴击 / 命中 / 闪避,单一五行轴」(明确否掉 MATK/MDEF)
**前序**:E-b1 属性载体(已合并)、E-b2 暴击(设计中)、E-b3 Buff 组字
**下游**:E-b5 全面重新平衡

---

## 一 · 为什么这一步与前三步不同

E-b1 是**从无到有**:玩家原本没有攻击力,加一个字段、加一次缩放,零冲突。

E-b4 是**从有到对**:命中、闪避、护甲**今天就已经存在**,但都是「为某几个字临时开的洞」而不是属性:

| 今天的形态 | 位置 | 问题 |
|---|---|---|
| `AttackHits(enemyIndex, dodgePercent)`,命中率 = `100 − 致盲 − 闪避` | `BattleEngine.cs:1484` | `100` 是硬编码常数,不是攻击者的属性 |
| `StatusKind.Blind` | 2 字(熣 50 / 烟 30) | 只能减,没有可加的基准 |
| `SummonPassive.Dodge` | 1 字(柳 50) | 全场唯一的闪避来源,且只有召唤物有 |
| 玩家闪避 | `BattleEngine.cs:1504` 写死传 `0` | 玩家根本没有这条属性 |
| 玩家攻击敌人的命中判定 | **不存在** | `DamageEnemy` 从不调 `AttackHits`,玩家必中 |
| `StatusKind/EffectKind.DamageReduction` | 6 字(铠 20 / 漜 25 / 崊 20 / 崟 15 / 磐 10 / 巍 5) | **乘法百分比**,与「护甲点数」不是同一个模型 |
| `EnemyDef.DamageTaken` / `BossPhaseDef.DamageTaken` | 1 只小怪(墨渍 0.7)+ 3 个 Boss 阶段(山 0.5 / 江 0.75 / 钧 0.75) | 同上,敌人侧的「防御」是个 float 系数 |
| `EffectDef.IgnoreArmor`(穿甲) | 3 字(錰 40 / 刺 13 / 锥 9) | 只取消减免(`taken < 1`)并额外 +15%,是**对乘法层**的操作 |
| `StatusKind.ArmorBreak`(破甲) | 6 字(熔 溃 溶 锤 破 碎,各 2 回合) | 承伤 +25%,同样是**乘法层** |

于是本步的真正内容不是「加属性」,而是**先决定 DEF 是什么模型**,其余(命中/闪避/锐)都挂在那个决定下面。

---

## 二 · 核心张力:`锐` 的「穿透 +3」不兼容今天的防御模型

`锐` 的规格是**穿透 +3** —— 一个**固定点数**。固定点数的穿透只有在被穿的东西也是固定点数时才有意义:

- 若护甲是乘法百分比(今天的 `DamageReduction` / `DamageTaken`),「穿透 3」无从解释——穿 3 个百分点?那 `巍`(5%)被穿掉 3 就只剩 2%,而 `漜`(25%)被穿掉 3 几乎无感,同一个数字在不同来源上量级完全不同。
- 若护甲是固定点数(`伤害 − DEF`),「穿透 3」精确等价于「视目标 DEF 少 3 点」。

**结论:`锐` 的规格本身就在要求一个点数制护甲层。** 这一层今天不存在,E-b4 必须造出来;问题只是「造在哪、和现有乘法层什么关系」。

### 2.1 规格考古:三份文档互相打架(必须先拍板)

调研中发现同一批字在三处有**互不相容**的规格:

| 来源 | `锐` | `利` | `锋` | 破甲(削/刮) | 护甲模型 |
|---|---|---|---|---|---|
| `第10章 10.2 / 10.3.2`(v0.4 稿) | 单体 14,**对护甲 0 的敌人翻倍** | 单体 9,**无视 3 点护甲** | **破甲 5(永久)** | 削 破甲 3 / 刮 破甲 2 + 裂甲 | **点数**:「护甲 X = 每次伤害 −X(最低 0);破甲永久降护甲」 |
| `技能机制详表.md:428`(现行) | **穿透 +3** | +1 AP | 暴击 +20% | — | 未言明 |
| 代码实装 | 未入字表 | 未入字表 | 未入字表 | 熔溃溶锤破碎 = **承伤 +25%,2 回合** | **乘法百分比** |

三点要注意:

1. **第 10 章早就写好了点数制护甲**,而且写得很具体(10.2 资源表 + 10.5 战例二逐回合算过一遍:Boss 10 护甲、锋破 5、削 4 伤被 2 甲减为 2)。今天的实装是在 2026-08-05「承伤与护甲」子项目里**改成乘法**的,那次改动没有回头更新第 10 章。
2. **「无视 3 点护甲」在第 10 章挂的是 `利` 不是 `锐`**;详表把它挪给了 `锐`。任务书采用的是详表口径。
3. **「破甲」一词已经被占用**:第 10 章的破甲 = 永久削减护甲点数;代码的破甲 = 承伤 +25%。若 E-b4 引入点数 DEF,这两个「破甲」会正面撞名,文案上必须区分。

→ 见第十三节「⚠ 待用户拍板」第 2 条。

---

## 三 · 范围

### 做

- 命中率、闪避率成为真属性(玩家 / 敌人 / 召唤物三侧统一走一条公式)
- DEF 成为真属性(玩家 + 敌人),并确定它与既有乘法减伤层的关系
- 穿透(`Pierce`)作为伤害侧属性,落地 `锐`
- `Blind` 并入命中体系、`SummonPassive.Dodge` 并入闪避体系
- 仿真工装接入三条新属性

### 不做

| 不做 | 归属 |
|---|---|
| 暴击 | E-b2(并行) |
| 剡 / 锋 / 战 / 利 / 戮 五字 | E-b3 |
| **DEF / 命中 / 闪避的具体数值、敌人 DEF 曲线、字表量级抬高** | **E-b5**(见第十二节,这是本步最重要的边界) |
| `钩`(拉后排到前排) | 模型缺口,与本步无关 |
| 敌人侧 ATK 重构 | E-b1 已裁定不做 |

---

## 四 · 【核心】DEF 模型:三个方案

### 4.0 先把改动面量化清楚

以下数字全部实测自仓库(2026-08-12 `main`):

**配置侧**

| 项 | 数量 | 明细 |
|---|---|---|
| 配 `DamageReduction` 的字 | **6** | 铠 20 / 漜 25 / 崊 20 / 崟 15 / 磐 10 / 巍 5 |
| 配 `ignoreArmor` 的字 | **3** | 錰 40 / 刺 13 / 锥 9(全部 `DamageSingle`,全部金系) |
| 配 `ArmorBreak` 的字 | **6** | 熔 溃 溶 锤 破 碎(全部 `value: 2` 回合) |
| `damageTaken != 1` 的敌人 | **1** | 墨渍 0.7 |
| `damageTaken != 1` 的 Boss 阶段 | **3** | 排山倒海·山 0.5 / 翻江倒海·江 0.75 / 雷霆万钧·钧 0.75 |
| 配 `Blind` 的字 | 2 | 熣 50 / 烟 30 |
| 配 `passive.dodge` 的字 | 1 | 柳 50 |

**代码侧**(涉及乘法防御层的读写点)

| 文件 | 处数 | 说明 |
|---|---|---|
| `Core/BattleEngine.cs` | ~15 | `DamageReductionMultiplier`(:190)、`ReducedDamage`(:203)及其 **7 个调用点**(:739 / :1662 / :1665 / :1672 / :1673 / :1679 / :1711)、`DamageEnemy` 的 `taken` 算式(:1401-1415)、`ArmorBreakPercent`/`PierceBonusPercent` 常量(:129-130)、效果分发的 `DamageReduction`(:1069)与 `ArmorBreak`(:951)分支、`IgnoreArmor` 两处传参(:899 / :910) |
| `Core/EnemyDef.cs` | 6 | `EnemyDef.DamageTaken` / `BossPhaseDef.DamageTaken` / `EnemyState.DamageTaken` / `Capture` / `Restore` / `ApplyPhaseStats` |
| `Core/Campaign.cs` | 2 | `Scale()` 里两处「承伤系数不缩放」 |
| `Core/RunSnapshot.cs` | 1 | `EnemySnapshot.DamageTaken`(float,**已进存档**) |
| `Core/RunEngine.cs` | 2 | 跨战斗携带态白名单(只带 `DamageReduction`) |
| `Data/ConfigLoader.cs` | 5 | DTO 的 `IgnoreArmor` / `DamageTaken`(敌人级 + 阶段级)与两处装配 |
| `Presentation/`(CharInfo / BattleView / EnemyPreview / EnemyInfo) | ~10 | 全部是「承伤 −X%」「穿甲:无视减伤,额外 +15%」这类文案 |

**测试侧**(795 条中直接断言这套模型的测试方法)

`DamageReduction_*`(BattleEngineTests ×4)、`Minion_DamageTaken_ReducesDamage`、`DamageTaken_AboveOne_SurvivesElementCounter`、`DamageTaken_BelowOne_StillLostToElementCounter`、`ArmorBreak_*`(×4)、`IgnoreArmor_*`(×3)、`NonPiercing_GetsNoFlatBonus`、`PlayerStatuses_HotAndReduction_QueryableByPolarity`、`ShanPhase_HalvesDamageTaken`、`Deluge_AppliesPlayerDamageReduction`、`Pierce_AppliesDamageReductionToSummonHit`、`BulwarkPhase_NeverCasts_ButKeepsCounting`、`Detonate_IgnoresDamageTaken_TotalStaysFullNotHalved`、`LoadCampaign_ParsesMinionDamageTaken`、`Scale_PreservesDamageTaken`、`MultiHit_EachSegmentGoesThroughArmorBreakSeparately`、`RealConfig_KaiIsDamageReductionTwenty`、`RealConfig_PierceChars_CarryIgnoreArmorFlag`、`RealConfig_ArmorBreakChars_CarryTwoTurns`,再加 SnapshotRoundTrip / StatusOps / SaveGuard / StatusBag / RunEngine / Endless / ConfigLoader 各 1~4 条引用。

**合计 ≈ 35~40 条测试方法**直接吃这套模型。

---

### 方案 A · 推倒重来:统一为点数护甲

`伤害 = max(0, 伤害 − max(0, DEF − 穿透))`。删除乘法减伤层,6 个减伤字、6 个破甲字、3 个穿甲字、4 条敌人/阶段承伤系数全部折算成点数。

| 维度 | 量 |
|---|---|
| 改动面 | ~40 处代码,跨 **8 个文件**(Core ×5、Data ×1、Presentation ×4) |
| 既有测试变红 | **≈ 35~40 条**,且是**断言重写**不是删除 |
| 字表要改 | **15 个字**(6 减伤 + 6 破甲 + 3 穿甲)+ `enemies.json` **4 条** |
| 存档 | `EnemySnapshot.DamageTaken`(float)语义作废 → **旧存档需迁移** |
| 与 E-b5 耦合度 | **强制同批**。折算率不唯一:墨渍 0.7 折成几点 DEF 完全取决于打它的伤害量级(6 伤 → 1.8 点;40 伤 → 12 点)。字表量级不定,折算就无解 |
| 恒等性 | **必破**。E-b1 的「逐字节恒等 + Tests 纯追加零删除」硬线守不住 |

**优点**:模型唯一,`锐` 的穿透语义纯净,第 10 章的设计稿(战例二那套破甲流)真正落地。
**缺点**:把「机制迁移」和「数值重算」焊在同一批提交里 —— 这正是 E-b1 spec 第一节明确否掉的做法(「任何一条测试变红都分不清是公式错了还是数值调了」)。

---

### 方案 B · 两层并存,各司其职(**推荐**)

保留乘法层原封不动,**新增一层点数 DEF,基准 0**。

```
伤害链路(打敌人):
  base
  → ScaleByAttack(base)                    E-b1
  → WuxingResolver.ResolveEffect(...)       生克
  → × taken(生克豁免 / 穿甲 / 破甲)         【乘法层,不动】
  → − max(0, 敌人DEF − 本次穿透)             【新增:点数层】
  → max(0, ...)
```

```
伤害链路(打玩家):
  enemy.Attack
  → × DamageReductionMultiplier            【乘法层,不动】
  → − max(0, 玩家DEF − 敌人穿透)             【新增:点数层】
  → 免疫 → 护盾 → HP                        【不动】
```

**两层不是历史包袱,是有语义的划分**:

| 层 | 是什么 | 来源 | 会不会被驱散 |
|---|---|---|---|
| 乘法层(承伤系数 / 减伤 %) | **你现在处于什么状态** | 字(铠/崊/…)、Boss 阶段标签(山/坚壁)、破甲、穿甲 | 会(`DamageReduction` 是 `StatusPolarity.Buff`,`ArmorBreak` 是 Debuff) |
| 点数层(DEF) | **你是谁** | 角色等级、敌人属性、深度缩放 | 不会(是属性不是状态) |

同一套划分在攻击侧已经成立了:`ATK`(属性)vs `AttackBuff`/`Curse`(状态)。DEF 沿用它,不发明新形状。

| 维度 | 量 |
|---|---|
| 改动面 | ~15 处**新增**,**0 处改写**。跨 5 个文件(BattleEngine / EnemyDef / EffectDef / ConfigLoader / Presentation) |
| 既有测试变红 | **0 条**(DEF 基准 0、命中基准 100、闪避基准 0、穿透基准 0 → 全链路逐字节恒等) |
| 字表要改 | **0 个**(E-b4 只交付机制;DEF 数值全部留给 E-b5 分配) |
| 存档 | **零新增快照字段**(见第八节) |
| 与 E-b5 耦合度 | **机制不耦合,数值全耦合**(见第十二节) |
| 恒等性 | **守得住**,Tests 纯追加零删除 |

**优点**:与 E-b1 同一套方法论(先搬管道,一个数字都不改);风险最低;E-b2 暴击并行开发时不会撞车。
**缺点**:玩家要理解两种防御(「受伤 −20%」和「护甲 3」);穿甲(破乘法层)与穿透(削点数层)是两个词,文案负担真实存在。

---

### 方案 C · 半迁移:敌人侧转点数,玩家侧留百分比

敌人的 `DamageTaken`(1 小怪 + 3 阶段)折算成 DEF 点数并删除该字段;玩家侧 6 个减伤字保持乘法不动。

| 维度 | 量 |
|---|---|
| 改动面 | ~22 处,跨 7 个文件 |
| 既有测试变红 | **≈ 12~15 条**(`DamageTaken_*`、`ShanPhase_*`、`Minion_*`、`Detonate_IgnoresDamageTaken`、`Scale_PreservesDamageTaken`、`LoadCampaign_ParsesMinionDamageTaken`、`BulwarkPhase_*`、Endless / ConfigLoader / SnapshotRoundTrip 各若干) |
| 字表要改 | `enemies.json` **4 条**;`chars.json` 0 个;但 `IgnoreArmor`(3 字)语义悬空 —— 它专治乘法层,乘法层在敌人侧没了 |
| 存档 | `EnemySnapshot.DamageTaken` 作废 → **需迁移** |
| 与 E-b5 耦合度 | **强制同批**,理由同方案 A(折算率不唯一) |
| 恒等性 | **必破** |

**评价**:承担了 A 的大半代价(测试红、存档迁移、必须与 E-b5 同批),却没拿到 A 的核心收益(模型唯一)—— 玩家侧仍是百分比,而 `锐` 是玩家的字,它穿的到底是哪一层反而更含糊。**不推荐。**

---

### 4.1 推荐:方案 B,并给出退出条件

推荐 **B**,理由不是「B 更好看」,而是:

1. **E-b4 与 E-b5 的分工只有 B 能守住。** A/C 的折算率取决于字表量级,而字表量级是 E-b5 的产出 —— 在 E-b5 之前做 A/C,等于在拿一组马上要作废的数字重写 40 条测试。
2. **恒等性是本项目已经付过费的资产。** E-b1 用它换到了「792 条断言原封不动地绿」,任何一条红都能立刻定位。B 保住它,A/C 都放弃它。
3. **两层并存有真实语义**(属性 vs 状态),不是拖延。

**退出条件(什么时候该回头做 A)**:E-b5 定完字表量级、且实测显示「减伤 % 与 DEF 点数在同一个战斗里同时出现会让玩家算不清伤害」时,把乘法层收编进点数层作为 E-b6 的一个独立子项目——那时折算率是已知的,改动是纯机械的,测试红也红得明白。

---

## 五 · 属性语义:什么吃 DEF,什么不吃

与 E-b1 的 ATK 表同构。**DEF 只挡「一次性的、有攻击者的伤害」。**

| 吃 DEF(每次结算各扣一次) | 不吃 DEF |
|---|---|
| `DamageSingle` / `DamageAll`(逐目标各扣各的) | 灼烧 tick(`BurnTick`) |
| 敌人普攻 | 流血 tick(`BleedTick`) |
| Boss 大招(Deluge / Pierce / Topple 的伤害部分) | 引爆(`Detonate`) |
| 召唤物出手伤害 | 反弹(`Reflect`)与荆的反伤(`Thorns`) |
| | 斩杀直接击杀(`ExecuteKills`,直接抹血不走伤害) |
| | 护盾 / 治疗 / 控制回合数(本就与伤害无关) |

**DOT 不吃 DEF 是硬约束,不是口味问题**:灼烧每层基准 2 伤,任何非零 DEF 都会把它整条归零,火系当场作废。同理流血(锯 = 每回合 1)。

**反弹不吃 DEF 的理由与 E-b1 一致**:反弹按「打到我身上的总伤害」照回去,它不是攻击者的输出,再过一次防御是双重结算。

### 5.1 两个已知副作用(必须写进 spec,免得日后被当 bug 修)

**(a) 点数 DEF 天然惩罚 AOE。** `DamageAll` 对 N 个敌人各扣各的 DEF,总损失 = N × DEF;单体只扣一次。低数值 AOE 在有甲敌群面前会直接归零。这是点数护甲的经典特性(也是它的战术意义:AOE 清杂兵、单体破甲),但 E-b5 定 AOE 数值时必须把它算进去。

**(b) 点数 DEF 双重惩罚多段字。** `剁`(`hitCount: 2`,10 伤 ×2)面对 5 点 DEF 打出 5+5 = 10;同基础值的单段 20 伤打出 15。既有口径是「每段完全独立:各自过生克、破甲、穿甲,也各自过斩杀」(`EffectDef.HitCount` 的注释),DEF 跟随这条口径最一致——但代价是多段字被削得比别人狠一档。见第十三节待拍板第 5 条。

---

## 六 · 命中与闪避

### 6.1 一条公式,三个来源

```csharp
/// <summary>命中判定。命中率 = 攻击者命中 − 攻击者致盲 − 目标闪避,钳到 [0,100]。
/// 基准(命中 100 / 闪避 0 / 无致盲)下恒 ≥ 100 → 短路,一次随机都不摇。</summary>
private bool Hits(int attackerHit, int attackerBlind, int targetDodge)
{
    int hitRate = Math.Clamp(attackerHit - attackerBlind - targetDodge, 0, 100);
    if (hitRate >= 100) return true;       // ← 这条短路是恒等性的全部依据,不许动
    return _random.Next(100) < hitRate;
}
```

今天的 `AttackHits(enemyIndex, dodgePercent)` 就是这个式子把 `attackerHit` 写死成 `100` 的特例。改造 = 把那个常数换成属性读取,**其余一字不动**。

| 攻击者 | 命中来源 | 目标闪避来源 |
|---|---|---|
| 玩家出字打敌人 | `BattleConfig.PlayerHit`(基准 100)+ 局内命中增益 | `EnemyState.Dodge`(基准 0)+ 敌人闪避状态 |
| 敌人打玩家 | `EnemyState.Hit`(基准 100)− `StatusKind.Blind` | `BattleConfig.PlayerDodge`(基准 0)+ 局内闪避增益 |
| 敌人打召唤物 | 同上 | `SummonPassive.Dodge`(柳 50,**零改动直接就是闪避属性**) |
| 召唤物打敌人 | 沿用玩家命中(召唤物是玩家的延伸) | `EnemyState.Dodge` |

`StatusKind.Blind` 的定位随之明确:它是**命中的临时减益**,与 DEF 侧「属性 vs 状态」的划分同构。不需要新增枚举值,不需要改施加逻辑,只是从「唯一的命中修正项」变成「命中属性上的一个修正项」。

### 6.2 成长曲线(形状与 `MaxHpFor`/`AttackFor` 同构)

```csharp
/// <summary>命中成长:100 + 0×(等级−1)。基准即满值,成长为 0 —— 命中不是玩家的成长轴,
/// 它只是「敌人闪避」的对手项;真正会动的是局内增益与将来的进阶特长。</summary>
public static int HitFor(int level) => 100;

/// <summary>闪避成长:0 + 1×(等级−1),上限 25(26 级封顶,与 MaxHpFor/AttackFor 同封顶级)。</summary>
public static int DodgeFor(int level) => Math.Min(25, 1 * (level - 1));

/// <summary>防御成长:0 + …,曲线待 E-b5 定;E-b4 交付时恒为 0。</summary>
public static int DefenseFor(int level) => 0;
```

三条曲线的**具体数字全部属于 E-b5**。E-b4 只负责:曲线函数存在、被 `GameRoot` 注入、被仿真工装读取。上面的 25 / 1 是占位,见待拍板第 4 条。

> ⚠ 闪避上限必须存在且远小于 100。闪避是乘性生存能力,25% 闪避 = 有效血量 ×1.33;若可堆到 60%+,肉鸽后期会出现「摸不到我」的退化局,而且每一次攻击都要摇随机数(见 6.4)。

### 6.3 玩家攻击会不会 miss —— 一个真实的手感问题

今天玩家**必中**(`DamageEnemy` 根本不调 `AttackHits`)。给敌人加闪避 = 玩家花 2 AP 出一张 40 伤紫卡可能打空。这在肉鸽里是最挫败的一类随机性(资源已消耗、回合已过、收益归零)。

但若玩家永不 miss,`PlayerHit` 就是一条死属性,「命中」进属性集也就没有意义。

三个选项见待拍板第 3 条。

### 6.4 一个必须写进注释的性能/可预测性后果

`DamageAll` 打 N 个敌人、`hitCount: 2` 的多段字,每一次结算都要过一次命中判定。基准下全部短路(零随机数);但只要场上有任何一只带闪避的敌人,同一次出牌就会消耗多个随机数,**随机数消耗量随场上敌人数变化**。这不影响存档(`RandomState` 照存),但意味着「同种子同操作」在敌人数不同的分支上会立刻分叉——这本来就成立(`AttackHits` 早就这样),只是敌人闪避会把它从「罕见」变成「常见」。

---

## 七 · RNG 恒等性:能守住,条件是四个基准值

E-b1 的验收硬线是「基准值下逐字节恒等,`Tests/` 纯追加零删除」。**E-b4 在方案 B 下能守住**,依据逐条列清:

| 新属性 | 基准值 | 为什么恒等 |
|---|---|---|
| `PlayerHit` | **100** | `Hits(100, 敌人致盲=0, 敌人闪避=0)` → hitRate 100 → 短路 return true,**不摇随机** |
| `EnemyState.Hit` | **100** | `100 − blind − dodge` 与今天的硬编码常数逐位相同 |
| `PlayerDodge` | **0** | `DamagePlayerDirect` 今天传 `0`,改成传 `PlayerDodge` 后取值仍是 0 |
| `EnemyState.Dodge` | **0** | 玩家侧新增的命中判定 hitRate = 100 → 短路 |
| `DEF`(玩家 + 敌人) | **0** | `max(0, x − max(0, 0 − 0)) == x` |
| `Pierce` | **0** | 同上 |

**关键:玩家侧新增命中判定不破坏恒等,是因为短路发生在摇随机数之前。** 这正是 `AttackHits` 那段注释(`:1481`)守的东西:

> 命中率 ≥ 100 时直接返回,一次随机都不摇 —— `_random` 的唯一既有消费方是 StartTurn 的回合掉字,无条件摇会平移掉落序列,让所有依赖种子的既有测试全红。

`_random` 今天的消费方实测只有三处:`StartTurn` 掉字(`:862`)、`AttackHits`(`:1489`)、`EnemyState` 构造时的 Boss 阶段抖动。E-b4 不新增第四处 —— 玩家侧命中判定复用同一个 `Hits`,同一条短路。

既有测试 `DamageVariantTests.NoBlindNoDodge_DoesNotConsumeRandom`(`:347`)就是这条硬线的守卫,它用「同种子、敌人 Speed 100 vs 200(出手次数不同)、比较掉落序列」来获得判别力。**E-b4 必须补一条对称的**:同种子、玩家出牌次数不同、比较掉落序列 —— 守住「玩家侧命中判定也短路」。

> ⚠ 反例警告:不要写成「两台引擎完全一样 → 序列一致」。那种写法零判别力(两边都无条件摇随机数,烧掉的一样多,序列照样一致)。判别力必须来自**两边的判定次数不同**。

### 7.1 方案 A / C 下的恒等性代价(供对比)

守不住。除上表 35~40 条测试外,还有一批**间接**变红的:任何断言具体伤害数字、而战斗里出现过 墨渍 或 山/江/钧 阶段的测试都会跟着变。这些无法靠 grep 穷举,只能跑一遍才知道 —— 这也是 A/C 真实成本的下限而非上限。

---

## 八 · 快照与存档:方案 B 下零新增字段

| 数据 | 存在哪 | 需不需要新字段 |
|---|---|---|
| 玩家 DEF / 命中 / 闪避 | `BattleConfig`(由 `GameRoot` 按等级注入),`BattleEngine.Restore` 本就接收 config | **不需要**(与 E-b1 的 `PlayerAttack` 同款) |
| 敌人 DEF / 命中 / 闪避 | `EnemyDef`(配置侧)。`Restore` 按 id 查 `enemyDefs` 重新灌入 | **不需要**,前提是它们**不随战斗过程变化** |
| 局内 DEF/闪避/穿透增益 | `_playerStatuses` → `BattleSnapshot.PlayerStatuses`(已在存) | **不需要新快照字段**,但需要**新 `StatusKind` 枚举值** ⚠ |
| 召唤物闪避 | `SummonPassive.Dodge` 已进 `SummonSnapshot` | **不需要** |

### 8.1 ⚠ 新增 `StatusKind` 枚举值必须追加在末尾

`SaveSerializer` 用 `JsonConvert.SerializeObject` 且**没有注册 `StringEnumConverter`** → Newtonsoft 默认把枚举序列化成**整数**。`EndlessSaveState.CarriedStatuses` 里存着玩家的 `DamageReduction`(今天 = 5)。

**在 `StatusKind` 中间插入任何新值,都会让旧存档里的所有状态静默错位**(`DamageReduction` 5 会被读成新插入的那个)。既有枚举是按落地日期依次追加的(`BurnNoDecay` 在最末),这个惯例此前没有被写下来 —— E-b4 要把它写进 `StatusKind` 的注释。

新增值(视待拍板结果,可能是 `DefenseBuff` / `DodgeBuff` / `PierceBuff`)一律**追加到 `BurnNoDecay` 之后**。

### 8.2 一条会逼出新快照字段的设计(要避开)

若把「破甲」改成第 10 章那种「**永久削减敌人 DEF 点数**」,敌人的 DEF 就成了可变状态,必须进 `EnemySnapshot`(和 `DamageTaken` 一样) → 破坏「零新增字段」。

**规避办法**:E-b4 的破甲维持现状(承伤 +25%,乘法层,已在 `Statuses` 快照里)。点数层的 DEF 削减若真要做,用一条**新状态**(`StatusKind.DefenseBreak`,Magnitude = 削减点数)承载,状态本来就进快照 —— 仍是零新增字段。这也是方案 B 「点数层只放属性、变动量一律走状态」这条划分的直接收益。

---

## 九 · `锐` 与穿甲:两个机制,不是两档

在方案 B 下,两个词各管一层:

| | 穿甲 `IgnoreArmor`(已实装) | 穿透 `Pierce`(E-b4 新增) |
|---|---|---|
| 作用层 | **乘法层** | **点数层** |
| 效果 | `taken < 1` 时把 `taken` 提回 1(只取消减免,不取消破甲加成);另外无条件 `taken += 15%` | 目标 DEF 视为 `max(0, DEF − Pierce)` |
| 载体 | `EffectDef.IgnoreArmor`(bool) | `EffectDef.Pierce`(int,默认 0)+ `StatusKind.PierceBuff`(局内增益) |
| 字 | 錰 / 刺 / 锥 | 锐 |
| 一句话 | 「无视对方**摆出来的架势**」 | 「削穿对方**本身的厚度**」 |

**不合并成一个机制的理由**:合并意味着要么把 3 个穿甲字迁到点数层(它们此刻面对的乘法减免会失效,数值全部要重算,回到方案 A 的代价),要么把 `锐` 塞进乘法层(「穿透 +3」无从解释,见第二节)。两个词各自精确,合并反而丢信息。

**文案负担是真实的**,必须在 `CharInfo` 上把两者说清:錰 今天显示「(穿甲:无视减伤,额外 +15%)」,锐 应显示「(穿透 3:无视 3 点护甲)」——两条文案的措辞要能被玩家一眼分开。

### 9.1 `锐` 的两种可能形状

详表把 `锐` 归在 `Buff` 组(「本场穿透 +3」= 持续增益),而第 10 章的对应物 `利` 是「单体 9 伤害,**无视 3 点护甲**」(单次效果)。两者机制不同:

- **Buff 型**:`EffectKind.PierceBuff` → 挂 `StatusKind.PierceBuff` 到玩家,本场全部伤害的 `EffectivePierce = EffectDef.Pierce + TotalMagnitude(PierceBuff)`。与 E-b3 的 `剡`/`战意` 同一条加法通道,与 E-b1 「基准 100 让百分比与固定值可互换」的思路一致。
- **单次型**:只给 `EffectDef.Pierce` 赋值,不挂状态。

E-b4 建议**两者都实现**(`EffectDef.Pierce` 是地基,`PierceBuff` 是它的持续版),因为地基本来就得有;`锐` 走哪一种取决于待拍板第 2 条的规格裁定。

### 9.2 `锐` 入表还缺一块

`锐` 今天不在 `chars.json`(BUFF 组 6 字全部未入表)。第 10 章给的配方是 `钅 + 兑`,而 **`兑` 不在字表里**(`钅` 在)。上线 `锐` 需要先补 `兑` 这个部件,或换配方。这与「炼/杨/戟/塌 四字因配方缺口拿不到」是同一类问题,不要重蹈。

---

## 十 · 涉及文件(方案 B)

| 文件 | 改动性质 |
|---|---|
| `Core/Meta.cs` | 新增 `HitFor` / `DodgeFor` / `DefenseFor`(与 `AttackFor` 并排) |
| `Core/BattleEngine.cs` | `BattleConfig` 加 `PlayerHit`/`PlayerDodge`/`PlayerDefense` 三字段;`AttackHits` 泛化成 `Hits(attackerHit, blind, dodge)`;`DamageEnemy` 末尾减 DEF;`DamagePlayerDirect`/`DamageSummon` 传真实闪避;玩家侧新增命中判定;`EffectKind.PierceBuff` 分支 |
| `Core/EnemyDef.cs` | `EnemyDef`/`EnemyState` 加 `Defense`/`Hit`/`Dodge`(默认 0/100/0) |
| `Core/EffectDef.cs` | 加 `int Pierce`(默认 0);`EffectKind` 追加 `PierceBuff` |
| `Core/StatusEffect.cs` | `StatusKind` **末尾**追加 `PierceBuff`(及视裁定的 `DefenseBuff`/`DodgeBuff`),并把「必须追加在末尾」的存档约束写进注释 |
| `Core/Campaign.cs` | `Scale()` 决定 DEF 是否随深度缩放(推荐**缩放**,与 HP/攻击同款 `Ceiling`;命中/闪避**不缩放**——它们是概率,缩放会溢出上限) |
| `Data/ConfigLoader.cs` | 敌人 DTO 加 `defense`/`hit`/`dodge`(带默认值);效果 DTO 加 `pierce` |
| `Presentation/GameRoot.cs` | 注入三条新属性(与 `PlayerAttack` 并排) |
| `Presentation/MapView.cs` / `CharInfo.cs` / `EnemyInfo.cs` | 属性上屏 + 穿透文案 |
| `tools/balance/Program.cs` | 三档 profile 喂新属性 |

**不新建 `CombatStats` 容器。** E-b1 已裁定「到三四个属性并肩时再抽」;E-b4 之后 `BattleConfig` 上会有 HP/ATK/DEF/命中/闪避五个平铺字段(加 E-b2 的暴击是六个)—— **抽容器的真实依据到 E-b5 才齐**,那时一并做,不在本步顺手抽。

### 10.1 `enemies.json` 与 ConfigLoader 测试

新字段一律**带默认值**(`defense = 0` / `hit = 100` / `dodge = 0`),`enemies.json` 13 只怪**一条都不用改**。`ConfigLoaderTests` 现有 16 条断言不会红(Newtonsoft 对缺失字段用 DTO 的默认值)。

需要**新增**的测试:缺字段时的默认值(否则将来有人把默认值写错成 `dodge = 100` 也没人发现)。

---

## 十一 · 仿真工装

`tools/balance` 三档 profile 今天喂 `MaxHpFor(level)` + `AttackFor(level)`(E-b1 补的)。E-b4 必须一并喂 `HitFor` / `DodgeFor` / `DefenseFor`。

**不接的后果有前科**:E-a 时工装的 `UnlockedChars` 锁在 9 个火系字上,新字进不了池,「P50 没变」被误读成「没有影响」。E-b5 要靠工装看 DEF 对生存曲线的影响,漏接就是瞎调。

---

## 十二 · E-b4 与 E-b5:机制不必同批,数值必须同批

**结论:方案 B 下 E-b4 可以独立交付并合并,但 `锐` 这个字必须与 E-b5 同批上线。**

推理:

1. **机制侧不耦合。** 方案 B 只加一层默认 0 的减法和一条泛化的命中式子,恒等性守住,不需要任何数值决策。
2. **数值侧完全耦合。** DEF 的量级由字表伤害量级决定:今天字表 6~40 伤,3 点 DEF 就把 6 伤的白卡砍半、把灼烧归零;E-b5 抬到 60 伤之后,3 点 DEF 才是「5% 的削减」这个合理量级。**在 E-b5 之前给任何敌人配非零 DEF 都是错的。**
3. **于是 `锐` 卡在中间。** E-b4 交付时全场 DEF = 0 → 「穿透 +3」的实际效果 = 0。`锐` 上线即废字,和「炼/杨/戟/塌 四个玩家永远拿不到的死条目」是同一类问题的不同形状。

**建议的交付切法**:

| 批次 | 交付物 |
|---|---|
| E-b4 | 三条属性的载体、公式、快照、测试;`EffectDef.Pierce` / `PierceBuff` 机制;全部基准值(恒等)。**`锐` 不入 `chars.json`** |
| E-b5 | 字表量级抬高 → 敌人 DEF 曲线 → 玩家 DEF/闪避成长曲线 → **`锐` 连同 `兑` 部件一起入表** |

若用户希望 `锐` 早点上线,备选是在 E-b4 里给敌人配一批**保守的 DEF 初值**(比如只给 墨渍 和 山 阶段各 1~2 点,由现有 `DamageTaken` 折算)——但那就吃进了 E-b5 的一部分,恒等性随之破掉。见待拍板第 6 条。

---

## 十三 · ⚠ 待用户拍板

### 1. DEF 模型(**最重要**)

| 选项 | 改动面 | 测试红 | 字表改动 | 恒等性 | 与 E-b5 |
|---|---|---|---|---|---|
| **A** 推倒重来:统一点数护甲 | ~40 处 / 8 文件 | **35~40 条** | 15 字 + 4 条敌表 | 破 | **必须同批** |
| **B** 两层并存:乘法层不动,新增点数 DEF(基准 0) | ~15 处**新增** / 5 文件 | **0 条** | **0 字** | **守住** | 机制不耦合、数值耦合 |
| **C** 半迁移:敌人转点数,玩家留百分比 | ~22 处 / 7 文件 | 12~15 条 | 4 条敌表 | 破 | **必须同批** |

**推荐 B。** 理由见 4.1:A/C 的折算率取决于 E-b5 才会产出的字表量级,现在做等于拿即将作废的数字重写 40 条测试;而 B 保住了 E-b1 已经付费买到的恒等性资产。
**B 的代价**:两种防御概念(减伤 % / 护甲点)并存,以及穿甲/穿透两个近义词,文案负担真实存在。
**退出条件**:E-b5 定完量级后,若实测显示玩家算不清伤害,再把乘法层收编为独立子项目——那时折算是机械的。

---

### 2. `锐` 的规格采哪一份(三份文档互斥)

| 选项 | 内容 | 代价 |
|---|---|---|
| **(a)** 采 `技能机制详表` 口径 | `锐` = **本场穿透 +3**(Buff 型,持续到本场结束) | 需要 `StatusKind.PierceBuff`;要在第 10 章 10.3.2 打上「v0.4 旧稿,以详表为准」的标注 |
| **(b)** 采 `第10章` 口径 | `锐` = 单体 14,**对护甲 0 的敌人翻倍**;`利` = 单体 9 **无视 3 点护甲** | 「穿透」落到 `利` 身上,而 `利` 属于 E-b3;E-b4 变成只做「护甲 0 加成」这个条件判定,`Pierce` 机制推迟 |
| **(c)** 折中 | `锐` = 单体伤害 + **该次穿透 3**(单次型,不挂状态) | 不需要新枚举值、不需要 `PierceBuff`;但与详表的「Buff」定位不符,E-b3 的战意/剡 那条 Buff 通道少一个同类 |

**推荐 (a)**,并顺手在第 10 章 10.3.2 加一行标注说明它是 v0.4 稿。理由:任务书采用的是详表口径;Buff 型穿透与 E-b3 的 `剡`/`战意` 共用同一条加法通道,形状统一;`EffectDef.Pierce` 作为地基无论如何都要有,(c) 只是少做一半。

> 附带必须一起拍的:**「破甲」一词的归属**。第 10 章的破甲 = 削护甲点数;代码的破甲 = 承伤 +25%(熔溃溶锤破碎 6 字已实装)。建议保留代码口径叫「破甲」,点数层的削减若将来要做,另起名(如「裂甲」——第 10 章 `刮` 已经用过这个词)。

---

### 3. 玩家攻击会不会 miss

| 选项 | 说明 | 代价 |
|---|---|---|
| **(a)** 对称:玩家攻击也过命中判定 | 敌人可以配 `dodge`,玩家 `PlayerHit` 是它的对手项 | 出一张 2 AP 的大牌可能打空 —— 肉鸽里最挫败的随机性;需要 UI 明确预告命中率 |
| **(b)** 玩家恒必中,`dodge` 只给敌人当**减伤**用 | 「闪避 30%」= 该敌人受到的伤害期望 −30%,但确定性结算 | `PlayerHit` 成死属性,「命中」进属性集失去意义;且与敌人侧的真闪避不对称 |
| **(c)** 擦过:玩家攻击不 miss,判定失败改为**半伤** | 保留随机性但去掉「零收益」 | 新概念(第三种伤害结果),表现层要新事件;与敌人侧口径不一致 |

**推荐 (a)**,但**敌人基准闪避 0**,只给少数几只「灵巧」定位的字怪配 10~20 点,且在敌人信息栏显式展示。理由:属性集要成立就必须双向;把痛点压在「稀有 + 可见 + 数值低」上,而不是靠去掉机制。
**(a) 的代价**:需要 UI 侧配合(出牌前展示命中率),否则打空会被读成 bug。

---

### 4. 玩家 DEF / 闪避的成长曲线

| 选项 | 说明 | 代价 |
|---|---|---|
| **(a)** 随等级成长(如闪避 `min(25, 等级−1)`,DEF 曲线由 E-b5 定) | 与 `MaxHpFor`/`AttackFor` 同形同封顶级(26 级) | 数值全部要等 E-b5;E-b4 只能填占位数 |
| **(b)** 只做挂点、不随等级成长 | 曲线恒返回基准值;DEF/闪避只来自技能、局内增益、将来的进阶特长 | 角色等级的收益面变窄(只有 HP/ATK 会动) |
| **(c)** 不给玩家 DEF,只保留既有的减伤 % | 玩家侧一层、敌人侧一层 | 与用户拍板的「属性集含 DEF」冲突;`锐` 打敌人仍成立,但玩家永远没有护甲 |

**推荐 (a)**,曲线数字全部标为「E-b5 待定」,E-b4 里 `DefenseFor` 恒返回 0(保恒等)、`DodgeFor` 也恒返回 0。
**(a) 的代价**:E-b4 交付的是三条「已接线但读数为 0」的属性,验收时看不到任何行为变化——只能靠测试证明它接对了。这是方案 B 的必然形状,不是遗漏。

---

### 5. 多段伤害(`剁`)与 DEF

| 选项 | 说明 | 代价 |
|---|---|---|
| **(a)** 每段各扣一次 DEF | 与既有口径一致(`HitCount` 注释:「每段完全独立:各自过生克、破甲、穿甲,也各自过斩杀」) | 多段字被 DEF 双重惩罚,E-b5 定 `剁` 数值时要额外补偿 |
| **(b)** 整张字只扣一次 DEF | 多段不吃亏 | 破坏「每段完全独立」这条已被测试锁死的口径(`MultiHit_EachSegmentGoesThroughArmorBreakSeparately`);需要在 `ApplyEffects` 里维护「本次出牌是否已扣过 DEF」的状态 |

**推荐 (a)**:一致性优先,补偿留给 E-b5。眼下只有 `剁` 一个多段字,影响面可控。

---

### 6. `锐` 什么时候上线

| 选项 | 说明 | 代价 |
|---|---|---|
| **(a)** 机制随 E-b4,字随 E-b5 | E-b4 交付 `EffectDef.Pierce` + `PierceBuff` 但不入表 | Buff 组 6 字里 `锐` 会比其余几个晚一批;E-b3 收尾时它还是空的 |
| **(b)** E-b4 内给敌人配保守 DEF 初值,`锐` 一起上 | 由现有 `DamageTaken` 折算,只给 墨渍 / 山 各 1~2 点 | **恒等性破**(那几只怪的伤害数字全变),约 8~12 条测试要改断言;而且这批初值到 E-b5 必然重算 |

**推荐 (a)**。理由见第十二节:DEF = 0 时「穿透 +3」实际效果为 0,`锐` 提前上线就是又一个死条目。
**顺带**:`锐` 入表还要先补 `兑` 部件(`钅` 已在表,`兑` 不在),这条与 炼/杨/戟/塌 的配方缺口同类,别忘。

---

## 十四 · 测试策略

测试字沿用 `AttackStatTests` 的约定:**一律 `Element.Heart` 且不给配方** —— 心对全属性生克 1.0x、无配方则无相生 ×3,断言里看到的数字就是被测机制本身。

### 14.1 验收硬线(方案 B)

```
coretests   795 passed + 新增,断言零修改、零删除
pytest      244 passed
prescompile 0 error CS
```

### 14.2 恒等性测试

| 测试 | 守什么 |
|---|---|
| 基准四值(命中 100 / 闪避 0 / DEF 0 / 穿透 0)下伤害逐字节等于改造前 | **总硬线** |
| `new BattleConfig()` 的四个默认值分别是 100 / 0 / 0 / 0 | 默认值不许被人改坏 |
| 玩家出牌次数不同的两台同种子引擎,掉落序列一致 | **玩家侧命中判定必须短路不摇随机**(与既有 `NoBlindNoDodge_DoesNotConsumeRandom` 对称;判别力必须来自两边判定次数不同) |
| 敌人 `Hit` 缺省(不改 `enemies.json`)时行为与今天逐位相同 | 配置默认值 |

### 14.3 正向测试(随机性的控制手段)

命中/闪避的正向断言不能靠「摇出来碰巧」。三条确定性手段:

1. **把命中率压到 0**:`Hits` 返回前钳到 [0,100],hitRate = 0 时 `_random.Next(100) < 0` 恒 false → **必空**。既有 `Dodge_NeededForDeterministicMiss_WithPartialBlind` 就用这招(致盲 60 + 闪避 50)。
2. **把命中率顶到 ≥100**:短路 → **必中**。
3. **`BattleEngine.Restore(...)` 的 `GameRandom` 入口**:构造一个已知状态的 `GameRandom` 喂进去,让 `Next(100)` 的前几个输出可预测——用于测「命中率 50 时确实摇了随机」。⚠ 这条路要走 `Capture()/Restore()` 往返,`GameRandom.FromState(state)` 是 public,但 `BattleEngine` 接受 `GameRandom` 的构造是 private(`:244`),测试只能经 `Restore` 拿到。若不想绕,退而求其次:用「同种子两台引擎、一台有 50 闪避一台没有 → 掉落序列必须分叉」来证明「摇了」。

| 测试 | 守什么 |
|---|---|
| 敌人闪避 100 → 玩家出字必空,发 `Missed` 事件 | 玩家侧命中判定接线 |
| 敌人闪避 100 → 玩家的 AP 照常消耗、字照常出库 | 打空的代价口径(与既有「打空不消耗免疫/护盾」对称) |
| 玩家命中 100 + 敌人闪避 40 → 命中率 60,同种子下与闪避 0 的引擎掉落序列**分叉** | 闪避确实进了算式且确实摇了随机 |
| 玩家闪避 100 → 敌人普攻必空,`PlayerHp` 不掉 | 玩家闪避接线(今天写死 0) |
| 敌人 `Hit = 50` + 玩家闪避 50 → 命中率 0 → 必空 | 敌人命中属性接线(今天是硬编码 100) |
| 敌人 DEF 5,`DamageSingle 20` → 打出 15 | 点数 DEF 生效 |
| 敌人 DEF 5,`DamageSingle 3` → 打出 0(不是负数) | 下钳位 |
| 敌人 DEF 5 + `Pierce 3`,`DamageSingle 20` → 打出 18 | 穿透 |
| 敌人 DEF 5 + `Pierce 99`,`DamageSingle 20` → 打出 20(不是 20 + 94) | **穿透只抵消,不倒贴**(`max(0, DEF − Pierce)`) |
| 敌人 DEF 5 + 承伤 0.5,`DamageSingle 20` → `floor(20×0.5) − 5 = 5` | **两层的先后顺序**:先乘法后点数 |
| 敌人 DEF 5 + 破甲(+25%),`DamageSingle 20` → `floor(20×1.25) − 5 = 20` | 同上,破甲仍在乘法层 |
| 玩家 DEF 3,敌人攻 8 → 掉 5 | 玩家侧点数 DEF |
| 玩家 DEF 3 + 减伤 50%,敌人攻 8 → `floor(8×0.5) − 3 = 1` | 玩家侧两层顺序与敌人侧一致 |
| `PierceBuff` 挂上后,后续所有伤害都吃穿透 | Buff 型穿透 |
| `PierceBuff` 快照往返后仍在 | 存档(零新增字段) |
| 召唤物 `Passive.Dodge = 50` 与新的闪避通道读同一个值 | `Dodge` 并入而非另起一套 |

### 14.4 **负向测试清单**(E-b1 终审的教训:负向覆盖不足)

| 测试 | 若缺失会漏掉什么变异 |
|---|---|
| DEF 5 时**灼烧 tick** 伤害不变 | 给 `BurnTick` 加上减 DEF → 火系整条归零而无一条测试红 |
| DEF 5 时**流血 tick** 伤害不变 | 同上 |
| DEF 5 时**引爆**(`Detonate`)总伤不变 | 引爆是 N(N+1)/2 求和,减 DEF 会按层数倍数惩罚 |
| DEF 5 时**反弹**(`Reflect`)与**荆的反伤**(`Thorns`)不变 | 反弹按「打过来的总伤害」反,再过一次防御是双重结算 |
| DEF 5 时**斩杀直接击杀**照常触发 | `ExecuteKills` 是抹血不是伤害,加了 DEF 判定会让残血怪杀不掉 |
| DEF 5 时**护盾吸收量 / 治疗量**不变 | DEF 泄漏到防御资源上 |
| 穿透 3 时**承伤系数**不变(墨渍 0.7 仍是 0.7) | 把 `Pierce` 误接到乘法层 → 与 `IgnoreArmor` 语义重叠而无人察觉 |
| `IgnoreArmor`(穿甲)**不**削减点数 DEF | 反向:把穿甲误接到点数层 |
| 命中 100 / 闪避 0 时**不消耗随机数** | 恒等性(见 14.2) |
| 玩家闪避 100 时,敌人打空**不消耗免疫、不掉护盾、不触发反弹** | 与既有 `Miss_DoesNotConsumeImmunityOrShield` / `Reflect_DoesNotFireWhenAttackMisses` 对称,新增的是玩家闪避这条来源 |
| 敌人闪避不影响**灼烧 / 流血 / 引爆**(它们没有命中判定) | 把命中判定误加到 DOT 上 → 火系变成概率游戏 |
| 闪避 `DodgeFor` 有上限(封顶级之后不再涨) | 上限被删掉 → 后期无敌 |

### 14.5 变异检查表

「吃不吃 DEF」不存在白名单数据结构,纯粹取决于**哪些结算点做了减法**,所以变异要在结算点上做:

| 变异 | 必须有测试红 |
|---|---|
| **删**:`DamageEnemy` 末尾的减 DEF 去掉 | 正向:敌人 DEF 5 那几条 |
| **删**:`max(0, DEF − Pierce)` 里的 `max(0, ...)` 去掉 | 穿透 99 那条(否则倒贴伤害) |
| **删**:`Hits` 的 `if (hitRate >= 100) return true` 短路去掉 | 掉落序列恒等那两条 |
| **删**:`DamagePlayerDirect` 传的玩家闪避改回硬编码 `0` | 玩家闪避 100 那条 |
| **删**:`Hits` 的 `attackerHit` 改回硬编码 `100` | 敌人 `Hit = 50` 那条 |
| **加**:给 `BurnTick` / `BleedTick` / `Detonate` / `Reflect` / `Thorns` 任一个**加上**减 DEF | 负向清单对应行 |
| **加**:给护盾或治疗**加上**减 DEF | 负向清单对应行 |
| **换**:两层顺序对调(先减 DEF 再乘承伤系数) | 「DEF 5 + 承伤 0.5」那条 |
| **换**:`Pierce` 从点数层挪到乘法层 | 「穿透不改承伤系数」那条 |
| **换**:`StatusKind` 新值插到枚举中间 | ⚠ 无自动化测试能抓 —— 只能靠注释约束 + 评审。建议补一条「`(int)StatusKind.DamageReduction == 5`」的锁值测试把它变成可测的 |

> 最后一条尤其值得做:枚举值错位是**静默**破坏旧存档,现有 795 条测试全都用新写的对象、不读旧 JSON 字节,没有任何一条能发现。

---

## 十五 · 与后续子项目的接口

| 子项目 | 依赖 E-b4 的什么 |
|---|---|
| E-b2 暴击(并行) | 与 E-b4 都在 `Hits` 之后、DEF 之前那一段插逻辑。**两边都要守同一条 RNG 短路纪律**:暴击率 0 时不摇随机,否则两个子项目合并后掉落序列一起平移。建议合并前先跑一次交叉验证 |
| E-b3 Buff 组 5 字 | `PierceBuff` 与 `AttackBuff` 共用加法通道;`锐` 是 Buff 组第 6 字但排在 E-b4 |
| E-b5 重新平衡 | 字表量级 → 敌人 DEF 曲线 → 玩家 DEF/闪避曲线 → `锐` 入表 → 仿真工装看新属性下的生存/输出曲线;以及决定要不要把乘法层收编(方案 B 的退出条件) |
