# 设计:无尽护盾改动(B)

> 2026-07-22 · brainstorming 产出 · 数值均首版基准可调
> 关联:A 等级被动技能系统(`2026-07-22-level-perk-system-design.md`)——A 的「金汤」技能注入本模型。
> **实现顺序:先做 B(护盾模型),再做 A 的金汤注入。** B 不阻塞 A 的其余四族。

## 1. 目标与动机

现有护盾在无尽里存在感弱、太快清空(普通护盾回合末即全清),土系护盾流偏弱。本改动让护盾成为**段内可累积的生存资源**,强化土系;并借「安全层/Boss」这一天然层段边界做护盾的清算点,避免跨段无限累积失衡。

这是一个**战斗平衡改动**,独立于等级技能系统,但技能「金汤」的开局护盾注入本模型。

## 2. 现状(改动前)

`BattleEngine` 两个护盾桶:

- `_shieldNormal`:**回合末(敌方行动结束后)全清**——只挡一个敌方回合。
- `_shieldPersist`:**豁免一次全清**,挺过本次后降级为普通桶——实际挡两个敌方回合。字的 `Shield` 效果按 `effect.PersistOnce` 分桶(`BattleEngine` ~409–413 行)。
- 全清逻辑:`BattleEngine` ~352–354 行 `_shieldNormal = _shieldPersist; _shieldPersist = 0;`。
- 对外只暴露 `PlayerShield => _shieldNormal + _shieldPersist`(~111 行)。

`RunEngine` 战斗间携带 HP(`_carriedHp`),`CaptureCarried`(~220 行)捕获、`BeginNextBattle`/`NewBattle`(~392/396 行)注入下一场。段末快照由 `GameRoot.WriteCarriedSnapshot`/`OnFloorAdvanced` 写 `EndlessSaveState`。

## 3. 目标模型(改动后)

已定决策:

- **所有普通护盾(不分属性)段内持久**:战斗内不再回合末清 → 跨层累积,**暂不设上限**,boss/段末清。土系是护盾主属性,de facto 最受益。单桶实现,不追踪来源属性。
- **堡型(`PersistOnce`)重定义为「跨段保留」**:连段末/boss 清都豁免,在**同一次登塔的多个 segment 间继承**(死亡/收官 `SettleTower` 置 `Endless=null` 后自然重置,不跨登塔场次)。这是唯一活过段边界的护盾。
- **金汤(A 的技能)每段只给一次**:段首注入 `PerkShieldBonus`(=金汤等级 ×4)进普通桶,整段不再重发;段内持久所以自动保留累积。

改动后两桶战斗内行为一致(都不再回合末清),**区别只在段末**:普通桶丢弃、堡桶跨段。

## 4. 三层改动

### 4.1 BattleEngine

1. **删回合末全清**(~352–354 行):普通护盾不再每回合清,段内持久的战斗内表现。堡桶原「豁免一次全清」逻辑随之移除。
2. **构造注入初始护盾**:构造函数(~398 行 `new BattleEngine(...)`)加两个参数 `startingNormalShield`、`startingPersistShield`,初始化 `_shieldNormal`/`_shieldPersist`。
3. **暴露两桶值**:新增 `ShieldNormal`、`ShieldPersist` 只读属性(供 `RunEngine` 分别捕获);`PlayerShield` 保留不变。
4. `Shield` 效果分桶逻辑(~409–413 行)不变:`PersistOnce` 仍进 `_shieldPersist`(堡),否则普通桶。

### 4.2 RunEngine

照抄 `_carriedHp` 模式,新增两个携带字段:

- `_carriedNormalShield`(段内)、`_carriedPersistShield`(跨段)。
- 构造函数加参数 `startingNormalShield`、`startingPersistShield`(像 `startingHp`),初始化两个 carried 字段并传入首场 `NewBattle`。
- `CaptureCarried`(~220 行,现捕获 `_carriedHp` 处):一并 `_carriedNormalShield = Battle.ShieldNormal; _carriedPersistShield = Battle.ShieldPersist`。
- `NewBattle`(~396 行):把两个 carried 护盾传给 `BattleEngine` 构造。
- 对外暴露 `CarriedNormalShield`、`CarriedPersistShield`(供 `GameRoot` 写快照)。

### 4.3 存档 + GameRoot

- `EndlessSaveState` 新增 `NormalShield`、`PersistShield` 两字段(断点续爬:段中退出重进不丢护盾)。
- `GameRoot.WriteCarriedSnapshot`/`OnFloorAdvanced`:写入 `snapshot.NormalShield = run.CarriedNormalShield`、`snapshot.PersistShield = run.CarriedPersistShield`。
- `GameRoot.StartSegment` 建 `RunEngine` 时计算段初始护盾:
  - `startingPersistShield = snapshot.PersistShield`(跨段继承)。
  - `startingNormalShield`:**续爬同段**(snapshot 已在段中)→ 用 `snapshot.NormalShield`;**全新段段首**→ 重置为金汤 `PerkShieldBonus`(每段一次)。段首判定:`(fromDepth - 1) % BossEvery == 0`。
- **段末清普通桶**:`OnSegmentEnded`(RunWon 处理)把 `snapshot.NormalShield = 0`(段清)、`snapshot.PersistShield = 堡值`(跨段),供下一段 `StartSegment` 读取。

## 5. 平衡说明

- 普通护盾段内暂不设上限,但**段边界(最多 5 层)强制清零**,累积被自然限制在一段内,不会真无限。
- 金汤每段一次(非每场爬坡),避免和段内持久复合出过强坦克流。
- 堡跨段是唯一长效护盾,强度靠字的稀有度/配方与出现频率控制(数值实测调)。

## 6. 测试要点(Core 先测后实现,TDD)

- 普通护盾**战斗内不再回合末清**(打完一个敌方回合后护盾仍在)。
- 普通护盾**跨层保留**:上一场剩余护盾进下一场初始。
- 堡桶跨场/跨段保留;普通桶段末归零。
- `BattleEngine` 初始护盾注入正确;`ShieldNormal`/`ShieldPersist` 暴露值正确。
- 断点续爬:写快照→重建 `RunEngine`→护盾恢复(段内普通 + 跨段堡)。
- 金汤:段首注入一次,整段不重发;续爬同段不重复注入。
- ⚠️ 断言只用 Unity NUnit 支持的 API(禁 `Is.AnyOf`)。
- Presentation 改完过离线编译(护盾 UI 显示 `PlayerShield` 不变,但值会段内累积)。

## 7. 开放问题(调参,非架构)

- 金汤每级护盾值(首版 +4/级)、堡型字的护盾基数与出现率待实测调。
- 普通护盾是否最终需要一个「软上限」防极端堆叠——首版不设,观察实测。
