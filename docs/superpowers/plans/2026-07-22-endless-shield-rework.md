# 无尽护盾改动(B)Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让无尽普通护盾段内持久、堡型护盾跨段保留,为技能系统 A 的「金汤」预留段首护盾注入位。

**Architecture:** 护盾从「单场战斗内状态」提升为「段内携带状态」。`BattleEngine` 停止回合末全清并支持初始护盾注入;`RunEngine` 照抄 `_carriedHp` 模式在战斗间携带两个护盾桶;`EndlessSaveState` + `GameRoot` 负责断点续爬持久化与段末清算。

**Tech Stack:** Unity 6000.5.2f1 / C#(纯 Core 逻辑,无 UnityEngine);NUnit;coretests(dotnet)。

## Global Constraints

- Core/Data 禁止引用 UnityEngine(asmdef `noEngineReferences: true`)。护盾逻辑全在纯 C#。
- 随机性一律走 `GameRandom`,禁 `UnityEngine.Random`。
- 测试断言只用 Unity 版 NUnit 支持的 API:**禁用 `Is.AnyOf`**;多选一用 `Is.EqualTo(a).Or.EqualTo(b)`。
- Core/Data 测试命令:`cd tools/coretests && /Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet test --nologo -v q`
- Presentation 改完必过离线编译:`cd tools/prescompile && /Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet build --nologo -v q`(只看 `error CS`,忽略 `warning MSB3245`)。
- 提交遵循环境规则:仅在用户要求时提交;`main` 分支上先开分支。conventional commits(feat/fix + 范围)。

---

### Task 1: BattleEngine 支持初始护盾注入 + 暴露两桶

**Files:**
- Modify: `Brushblade/Assets/_Project/Core/BattleEngine.cs`(构造 ~89-105 行、属性 ~111 行)
- Test: `Brushblade/Assets/_Project/Tests/BattleEngineTests.cs`

**Interfaces:**
- Produces: `BattleEngine` 构造新增两个可选参数 `int startingNormalShield = 0, int startingPersistShield = 0`(加在现有 `cardLevels` 之后);新增只读属性 `int ShieldNormal`、`int ShieldPersist`。

- [ ] **Step 1: Write the failing test**

在 `BattleEngineTests.cs` 类内加:

```csharp
[Test]
public void Constructor_InjectsInitialShield() // 段初始护盾注入(B)
{
    var engine = new BattleEngine(Graph(), Config(),
        Array.Empty<string>(), Array.Empty<string>(),
        new[] { MetalBoss() }, seed: 42, startingHp: null, cardLevels: null,
        startingNormalShield: 5, startingPersistShield: 2);
    Assert.That(engine.ShieldNormal, Is.EqualTo(5));
    Assert.That(engine.ShieldPersist, Is.EqualTo(2));
    Assert.That(engine.PlayerShield, Is.EqualTo(7));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd tools/coretests && /Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet test --nologo -v q`
Expected: 编译失败——`BattleEngine` 无此构造重载,`ShieldNormal`/`ShieldPersist` 未定义。

- [ ] **Step 3: Write minimal implementation**

在 `BattleEngine.cs` 构造签名(~89 行)末尾加两参数:

```csharp
public BattleEngine(RecipeGraph graph, BattleConfig config,
    IReadOnlyList<string> startingLibrary, IReadOnlyList<string> startingPool,
    IReadOnlyList<EnemyDef> enemies, int seed, int? startingHp = null,
    IReadOnlyDictionary<string, int> cardLevels = null,
    int startingNormalShield = 0, int startingPersistShield = 0)
```

在构造体 `PlayerHp = startingHp ?? config.PlayerMaxHp;`(~102 行)之后、`StartTurn();` 之前加:

```csharp
            _shieldNormal = startingNormalShield;
            _shieldPersist = startingPersistShield;
```

在 `PlayerShield` 属性(~111 行)附近加两个只读属性:

```csharp
        public int ShieldNormal => _shieldNormal;
        public int ShieldPersist => _shieldPersist;
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd tools/coretests && /Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet test --nologo -v q`
Expected: PASS。

- [ ] **Step 5: Commit**

```bash
git add Brushblade/Assets/_Project/Core/BattleEngine.cs Brushblade/Assets/_Project/Tests/BattleEngineTests.cs
git commit -m "feat(core): BattleEngine 支持初始护盾注入与两桶暴露"
```

---

### Task 2: 普通护盾段内持久(删回合末全清)

**Files:**
- Modify: `Brushblade/Assets/_Project/Core/BattleEngine.cs`(~352-354 行)
- Test: `Brushblade/Assets/_Project/Tests/BattleEngineTests.cs`

**Interfaces:**
- Consumes: Task 1 的 `startingNormalShield` 参数与 `ShieldNormal` 属性。
- Produces: 行为变更——普通护盾不再在敌方回合末全清。

- [ ] **Step 1: Write the failing test**

```csharp
[Test]
public void Shield_PersistsThroughEnemyTurn() // 段内持久:普通护盾不再回合末全清
{
    // 单敌攻击 3;玩家不出手,EndTurn 后敌方攻击被护盾吸收
    var engine = new BattleEngine(Graph(), Config(),
        Array.Empty<string>(), Array.Empty<string>(),
        new[] { WoodMinion(hp: 100) }, seed: 42, startingHp: 50, cardLevels: null,
        startingNormalShield: 5);
    engine.EndTurn();                 // 敌方攻击 3,护盾吸收
    Assert.That(engine.PlayerShield, Is.EqualTo(2)); // 旧逻辑会清 0;段内持久剩 2
    Assert.That(engine.PlayerHp, Is.EqualTo(50));    // 护盾垫住,血未掉
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd tools/coretests && …dotnet test --nologo -v q`
Expected: FAIL——`PlayerShield` 实测为 0(旧全清逻辑)。

- [ ] **Step 3: Write minimal implementation**

在 `BattleEngine.cs` 删除回合末全清三行(~352-354 行):

```csharp
            // 护盾全清:清算点在敌方行动结束后(10.2);豁免桶挺过本次,降级为普通桶
            _shieldNormal = _shieldPersist;
            _shieldPersist = 0;
```

删除后普通桶与堡桶战斗内都不再回合末清(段内持久)。两桶区别移到段末(RunEngine/GameRoot 层)。

- [ ] **Step 4: Run test to verify it passes**

Run: `cd tools/coretests && …dotnet test --nologo -v q`
Expected: PASS。同时跑全量确认没打破既有护盾/战斗测试。

- [ ] **Step 5: Commit**

```bash
git add Brushblade/Assets/_Project/Core/BattleEngine.cs Brushblade/Assets/_Project/Tests/BattleEngineTests.cs
git commit -m "feat(core): 普通护盾段内持久,删回合末全清"
```

---

### Task 3: RunEngine 段内携带护盾(跨场保留)

**Files:**
- Modify: `Brushblade/Assets/_Project/Core/RunEngine.cs`(字段 ~52-54、构造 ~56-70、CaptureCarried ~220-222、NewBattle ~396-399)
- Test: `Brushblade/Assets/_Project/Tests/RunEngineTests.cs`

**Interfaces:**
- Consumes: Task 1 的 `BattleEngine` 护盾参数与 `ShieldNormal`/`ShieldPersist`。
- Produces: `RunEngine` 构造新增 `int startingNormalShield = 0, int startingPersistShield = 0`(加在 `startingHp` 之后);新增只读属性 `int CarriedNormalShield`、`int CarriedPersistShield`。

- [ ] **Step 1: Write the failing test**

在 `RunEngineTests.cs` 类内加:

```csharp
[Test]
public void Shield_CarriesToNextBattle() // 段内持久:护盾跨层保留
{
    var run = new RunEngine(Graph(), TwoBattles(),
        new BattleConfig { DropTable = new[] { "木" } },
        startingLibrary: new[] { "焚" }, startingPool: Array.Empty<string>(), seed: 7,
        cardLevels: null, startingInk: 0, startingHp: null,
        startingNormalShield: 5);
    Assert.That(run.Battle.PlayerShield, Is.EqualTo(5));
    WinCurrentBattle(run);            // 焚一发清场(不 EndTurn,护盾不变)
    run.AdvanceAfterBattle();
    run.SkipReward();                 // 进入第二场
    Assert.That(run.Phase, Is.EqualTo(RunPhase.InBattle));
    Assert.That(run.Battle.PlayerShield, Is.EqualTo(5)); // 护盾跨场保留
    Assert.That(run.CarriedNormalShield, Is.EqualTo(5));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd tools/coretests && …dotnet test --nologo -v q`
Expected: 编译失败——`RunEngine` 无护盾参数,`CarriedNormalShield` 未定义。

- [ ] **Step 3: Write minimal implementation**

`RunEngine.cs` 字段区(~52-54 行)加:

```csharp
        private int _carriedNormalShield;
        private int _carriedPersistShield;
```

构造签名(~56-59 行)末尾加参数:

```csharp
        public RunEngine(RecipeGraph graph, RunConfig runConfig, BattleConfig battleConfig,
            IReadOnlyList<string> startingLibrary, IReadOnlyList<string> startingPool, int seed,
            IReadOnlyDictionary<string, int> cardLevels = null, int startingInk = 0,
            int? startingHp = null,
            int startingNormalShield = 0, int startingPersistShield = 0)
```

构造体在 `Battle = NewBattle(...)`(~69 行)**之前**初始化携带字段:

```csharp
            _carriedNormalShield = startingNormalShield;
            _carriedPersistShield = startingPersistShield;
```

`CaptureCarried`(~220-222 行,`_carriedHp = Battle.PlayerHp;` 之后)加:

```csharp
            _carriedNormalShield = Battle.ShieldNormal;
            _carriedPersistShield = Battle.ShieldPersist;
```

`NewBattle`(~396-399 行)把携带护盾传入:

```csharp
        private BattleEngine NewBattle(IReadOnlyList<string> library, IReadOnlyList<string> pool, int? startingHp)
        {
            return new BattleEngine(_graph, _battleConfig, library, pool,
                _runConfig.Encounters[BattleIndex], _random.Next(int.MaxValue), startingHp, _cardLevels,
                _carriedNormalShield, _carriedPersistShield);
        }
```

暴露属性(与 `CarriedLibrary` ~95 行相邻):

```csharp
        public int CarriedNormalShield => _carriedNormalShield;
        public int CarriedPersistShield => _carriedPersistShield;
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd tools/coretests && …dotnet test --nologo -v q`
Expected: PASS。

- [ ] **Step 5: Commit**

```bash
git add Brushblade/Assets/_Project/Core/RunEngine.cs Brushblade/Assets/_Project/Tests/RunEngineTests.cs
git commit -m "feat(core): RunEngine 段内携带护盾跨场保留"
```

---

### Task 4: 存档持久化 + 段末清算(EndlessSaveState + GameRoot)

**Files:**
- Modify: `Brushblade/Assets/_Project/Core/Endless.cs`(`EndlessSaveState` ~162-173 行)
- Modify: `Brushblade/Assets/_Project/Presentation/GameRoot.cs`(`StartTower` ~130-137、`StartSegment` ~169-173、`WriteCarriedSnapshot` ~247-255、`OnSegmentEnded` ~274-283)

**Interfaces:**
- Consumes: Task 3 的 `RunEngine` 护盾参数与 `CarriedNormalShield`/`CarriedPersistShield`。
- Produces: `EndlessSaveState` 新增 `int NormalShield`、`int PersistShield`。段首护盾注入位在此留 `0`(技能系统 A 会替换为金汤值)。

**说明:** 本任务改到 Presentation(GameRoot),coretests 盖不到,验证靠离线编译 + 手测流程。护盾逻辑本身已由 Task 1-3 的 Core 测试覆盖。

- [ ] **Step 1: EndlessSaveState 加护盾字段**

`Endless.cs` 的 `EndlessSaveState` 类内(~172 行 `TopBossDepth` 附近)加:

```csharp
        public int NormalShield { get; set; }   // 段内持久护盾(断点续爬恢复)
        public int PersistShield { get; set; }   // 堡型护盾(跨段保留)
```

- [ ] **Step 2: GameRoot 读快照建 RunEngine 传护盾**

`GameRoot.StartSegment`(~169-173 行)建 `RunEngine` 处,在 `startingHp: snapshot.PlayerHp` 之后加两参数:

```csharp
            var run = new RunEngine(_graph, runConfig, battleConfig,
                snapshot.Library, snapshot.Pool,
                seed: unchecked(snapshot.Seed * 17 + fromDepth), cardLevels: _meta.CardLevels,
                startingInk: _meta.Ink + snapshot.EarnedInk,
                startingHp: snapshot.PlayerHp,
                startingNormalShield: snapshot.NormalShield,
                startingPersistShield: snapshot.PersistShield);
```

- [ ] **Step 3: 层间快照写护盾(断点续爬)**

`WriteCarriedSnapshot`(~247-255 行)末尾加:

```csharp
            snapshot.NormalShield = run.CarriedNormalShield;
            snapshot.PersistShield = run.CarriedPersistShield;
```

- [ ] **Step 4: 段末清普通桶、留堡(为下一段预置)**

`OnSegmentEnded`(~277-283 行,写 `snapshot.PoolExpanded` 附近)加:

```csharp
            // 段末:普通护盾清零(下一段段首重置;技能系统 A 会在此填入金汤护盾),堡型跨段保留
            snapshot.NormalShield = 0;
            snapshot.PersistShield = run.CarriedPersistShield;
```

`StartTower`(~130-137 行)新建 `EndlessSaveState` 时不显式设护盾(默认 0),留注释:

```csharp
                    // NormalShield 默认 0;技能系统 A 会在首段填入金汤护盾
```

- [ ] **Step 5: 过离线编译并提交**

Run: `cd tools/prescompile && /Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet build --nologo -v q`
Expected: 无 `error CS`。

手测(用户在 Unity Play):进段吃护盾 → 下一层护盾仍在;打过 boss 进下一段 → 普通护盾归零、堡护盾仍在;段中退出重进 → 护盾恢复。

```bash
git add Brushblade/Assets/_Project/Core/Endless.cs Brushblade/Assets/_Project/Presentation/GameRoot.cs
git commit -m "feat(endless): 护盾断点续爬持久化与段末清算"
```

---

## 自查(spec 覆盖)

- 普通护盾段内持久 → Task 2。
- 堡跨段保留 → Task 3(携带)+ Task 4(段末 `PersistShield` 保留、`NormalShield` 清)。
- 初始护盾注入 → Task 1;跨场携带 → Task 3;断点续爬 → Task 4。
- 金汤段首注入位 → Task 4 留 `NormalShield = 0`,由 A 替换。
- 测试断言均用 `Is.EqualTo`,无 `Is.AnyOf`。
