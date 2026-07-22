# 等级被动技能系统(A)Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 玩家用墨锭在角色等级门槛上解锁并逐级升级一批常驻被动技能,永久改善开局属性/资源。

**Architecture:** 一张扁平静态技能表(`PerkRules.All`)+ `MetaState.PerkLevels`(技能 id→等级)。角色等级只 gate 初次解锁,之后纯墨锭 + 每技能升级上限。效果 = 技能等级 × 每级值,由聚合函数喂给五个现有注入点。存档随整档 JsonConvert 序列化,SaveGuard 整档签名保护。

**Tech Stack:** Unity 6000.5.2f1 / C#;NUnit;coretests(dotnet)。

**依赖:** 「金汤」注入依赖护盾改动 B(`2026-07-22-endless-shield-rework.md`)。**先完成 B。**

## Global Constraints

- Core/Data 禁止引用 UnityEngine。`Perk.cs`、`PerkRules` 全在纯 Core。
- 玩家可见 UI 文案走字符串表;技能名/效果数值是游戏数据(可硬编码在 `PerkRules.All`)。
- 一气(AP)升级上限固定 2:不封顶到 3 级 = 每回合 6 AP 崩盘。上限由 `InkCosts` 数组长度承载。
- 测试断言只用 Unity 版 NUnit 支持的 API,禁 `Is.AnyOf`。
- Core 测试:`cd tools/coretests && …dotnet test --nologo -v q`;Presentation 离线编译:`cd tools/prescompile && …dotnet build --nologo -v q`。
- 提交遵循环境规则:仅用户要求时提交;`main` 上先开分支。

---

### Task 1: Perk 定义表 + MetaState 字段 + 聚合函数

**Files:**
- Create: `Brushblade/Assets/_Project/Core/Perk.cs`
- Modify: `Brushblade/Assets/_Project/Core/Meta.cs`(`MetaState` ~9-23 行加字段)
- Test: `Brushblade/Assets/_Project/Tests/PerkTests.cs`(新建)

**Interfaces:**
- Produces:
  - `MetaState.PerkLevels : Dictionary<string,int>`(缺省 0=未解锁)。
  - `enum PerkEffect { MaxHp, Ink, Shield, Library, Ap }`。
  - `PerkDef { string Id; string Name; PerkEffect Effect; int PerLevelValue; int UnlockLevel; IReadOnlyList<int> InkCosts; int MaxLevel => InkCosts.Count; }`。
  - `PerkRules.All : IReadOnlyList<PerkDef>`、`PerkRules.Get(id)`、`PerkRules.PerkLevel(meta,id)`。
  - 聚合:`ApBonus/HpBonus/InkBonus/LibraryBonus/ShieldBonus(MetaState) : int`。

- [ ] **Step 1: Write the failing test**

新建 `PerkTests.cs`:

```csharp
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    public class PerkTests
    {
        [Test]
        public void PerkLevel_DefaultsToZero()
        {
            var meta = new MetaState();
            Assert.That(PerkRules.PerkLevel(meta, "yangyuan"), Is.EqualTo(0));
        }

        [Test]
        public void Bonus_EqualsLevelTimesPerLevelValue()
        {
            var meta = new MetaState();
            meta.PerkLevels["yangyuan"] = 3;  // 养元 +10/级
            meta.PerkLevels["yiqi"] = 2;      // 一气 +1/级
            Assert.That(PerkRules.HpBonus(meta), Is.EqualTo(30));
            Assert.That(PerkRules.ApBonus(meta), Is.EqualTo(2));
            Assert.That(PerkRules.ShieldBonus(meta), Is.EqualTo(0));
        }

        [Test]
        public void Yiqi_MaxLevelIsTwo() // 平衡硬线:AP 上限 2
        {
            Assert.That(PerkRules.Get("yiqi").MaxLevel, Is.EqualTo(2));
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd tools/coretests && …dotnet test --nologo -v q`
Expected: 编译失败——`PerkRules`/`PerkDef`/`MetaState.PerkLevels` 未定义。

- [ ] **Step 3: Write minimal implementation**

`Meta.cs` 的 `MetaState` 加字段(在 `CardLevels` ~11 行附近):

```csharp
        public Dictionary<string, int> PerkLevels { get; set; } = new();  // 技能 id → 等级;缺省 0=未解锁
```

新建 `Core/Perk.cs`:

```csharp
using System.Collections.Generic;

namespace Brushblade.Core
{
    /// <summary>技能效果类别(第 A 章:等级被动技能系统)。</summary>
    public enum PerkEffect { MaxHp, Ink, Shield, Library, Ap }

    /// <summary>单条技能定义:效果 = 等级 × PerLevelValue;角色等级只 gate 初解锁(0→1)。</summary>
    public sealed class PerkDef
    {
        public string Id { get; }
        public string Name { get; }
        public PerkEffect Effect { get; }
        public int PerLevelValue { get; }
        public int UnlockLevel { get; }              // 初次解锁所需角色等级
        public IReadOnlyList<int> InkCosts { get; }  // 索引=目标等级−1;长度=升级上限

        public PerkDef(string id, string name, PerkEffect effect, int perLevelValue,
            int unlockLevel, int[] inkCosts)
        {
            Id = id; Name = name; Effect = effect; PerLevelValue = perLevelValue;
            UnlockLevel = unlockLevel; InkCosts = inkCosts;
        }

        public int MaxLevel => InkCosts.Count;
    }

    /// <summary>技能表与聚合(首版基准,数值可调)。纯函数,状态进出。</summary>
    public static class PerkRules
    {
        public static readonly IReadOnlyList<PerkDef> All = new[]
        {
            new PerkDef("yangyuan", "养元", PerkEffect.MaxHp,  10, 2, new[] { 200, 400, 700, 1100, 1600, 2200 }),
            new PerkDef("runbi",    "润笔", PerkEffect.Ink,    50, 3, new[] { 200, 400, 700, 1100 }),
            new PerkDef("jintang",  "金汤", PerkEffect.Shield,  4, 4, new[] { 400, 700, 1100, 1600, 2200 }),
            new PerkDef("bowen",    "博闻", PerkEffect.Library,  1, 6, new[] { 600, 1200, 2000 }),
            new PerkDef("yiqi",     "一气", PerkEffect.Ap,       1, 6, new[] { 1500, 4000 }), // 上限 2:平衡硬线
        };

        private static readonly Dictionary<string, PerkDef> ById = BuildIndex();

        private static Dictionary<string, PerkDef> BuildIndex()
        {
            var map = new Dictionary<string, PerkDef>();
            foreach (var p in All) map[p.Id] = p;
            return map;
        }

        public static PerkDef Get(string id) => ById[id];

        public static int PerkLevel(MetaState meta, string id) =>
            meta.PerkLevels.TryGetValue(id, out var lvl) ? lvl : 0;

        private static int BonusOf(MetaState meta, PerkEffect effect)
        {
            int sum = 0;
            foreach (var p in All)
                if (p.Effect == effect)
                    sum += PerkLevel(meta, p.Id) * p.PerLevelValue;
            return sum;
        }

        public static int ApBonus(MetaState meta)      => BonusOf(meta, PerkEffect.Ap);
        public static int HpBonus(MetaState meta)      => BonusOf(meta, PerkEffect.MaxHp);
        public static int InkBonus(MetaState meta)     => BonusOf(meta, PerkEffect.Ink);
        public static int LibraryBonus(MetaState meta) => BonusOf(meta, PerkEffect.Library);
        public static int ShieldBonus(MetaState meta)  => BonusOf(meta, PerkEffect.Shield);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd tools/coretests && …dotnet test --nologo -v q`
Expected: PASS。

- [ ] **Step 5: Commit**

```bash
git add Brushblade/Assets/_Project/Core/Perk.cs Brushblade/Assets/_Project/Core/Meta.cs Brushblade/Assets/_Project/Tests/PerkTests.cs
git commit -m "feat(core): 技能定义表与效果聚合"
```

---

### Task 2: 解锁 / 升级规则

**Files:**
- Modify: `Brushblade/Assets/_Project/Core/Perk.cs`(`PerkRules` 内加两方法)
- Test: `Brushblade/Assets/_Project/Tests/PerkTests.cs`

**Interfaces:**
- Consumes: Task 1 的 `PerkRules.Get`、`PerkLevel`、`MetaState.PerkLevels`、`MetaRules.CharacterLevel`。
- Produces: `PerkRules.CanUpgradePerk(meta,id) : bool`、`PerkRules.TryUpgradePerk(meta,id) : bool`。

- [ ] **Step 1: Write the failing test**

在 `PerkTests.cs` 加:

```csharp
[Test]
public void Upgrade_RejectedBelowUnlockLevel() // 角色等级不足→拒(仅 0→1 校验)
{
    var meta = new MetaState { Ink = 9999, CharacterXp = 0 }; // 1 级
    Assert.That(PerkRules.TryUpgradePerk(meta, "yiqi"), Is.False); // 一气需 6 级
    Assert.That(PerkRules.PerkLevel(meta, "yiqi"), Is.EqualTo(0));
    Assert.That(meta.Ink, Is.EqualTo(9999)); // 拒绝不扣墨锭
}

[Test]
public void Upgrade_RejectedWithoutInk()
{
    var meta = new MetaState { Ink = 100, CharacterXp = 100 }; // 2 级,养元解锁 200
    Assert.That(PerkRules.TryUpgradePerk(meta, "yangyuan"), Is.False);
    Assert.That(PerkRules.PerkLevel(meta, "yangyuan"), Is.EqualTo(0));
}

[Test]
public void Upgrade_SucceedsAndDeductsInk()
{
    var meta = new MetaState { Ink = 300, CharacterXp = 100 }; // 2 级
    Assert.That(PerkRules.TryUpgradePerk(meta, "yangyuan"), Is.True); // 解锁到 1 级,扣 200
    Assert.That(PerkRules.PerkLevel(meta, "yangyuan"), Is.EqualTo(1));
    Assert.That(meta.Ink, Is.EqualTo(100));
}

[Test]
public void Upgrade_RejectedAtMaxLevel()
{
    var meta = new MetaState { Ink = 99999, CharacterXp = 100 };
    meta.PerkLevels["yiqi"] = 2; // 一气已满(上限 2)
    Assert.That(PerkRules.TryUpgradePerk(meta, "yiqi"), Is.False);
    Assert.That(meta.Ink, Is.EqualTo(99999));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd tools/coretests && …dotnet test --nologo -v q`
Expected: 编译失败——`TryUpgradePerk` 未定义。

- [ ] **Step 3: Write minimal implementation**

在 `Perk.cs` 的 `PerkRules` 内加:

```csharp
        public static bool CanUpgradePerk(MetaState meta, string id)
        {
            var def = Get(id);
            int lvl = PerkLevel(meta, id);
            if (lvl >= def.MaxLevel) return false;                                    // 已满
            if (lvl == 0 && MetaRules.CharacterLevel(meta.CharacterXp) < def.UnlockLevel)
                return false;                                                          // 初解锁角色等级不足
            return meta.Ink >= def.InkCosts[lvl];                                      // 墨锭足够
        }

        public static bool TryUpgradePerk(MetaState meta, string id)
        {
            if (!CanUpgradePerk(meta, id)) return false;
            var def = Get(id);
            int lvl = PerkLevel(meta, id);
            meta.Ink -= def.InkCosts[lvl];
            meta.PerkLevels[id] = lvl + 1;
            return true;
        }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd tools/coretests && …dotnet test --nologo -v q`
Expected: PASS。

- [ ] **Step 5: Commit**

```bash
git add Brushblade/Assets/_Project/Core/Perk.cs Brushblade/Assets/_Project/Tests/PerkTests.cs
git commit -m "feat(core): 技能解锁与墨锭升级规则"
```

---

### Task 3: 存档往返

**Files:**
- Test: `Brushblade/Assets/_Project/Tests/MetaTests.cs`(加往返测试)

**Interfaces:**
- Consumes: Task 1 的 `MetaState.PerkLevels`;现有 `SaveSerializer.ToJson/FromJson`(Newtonsoft,原生支持 Dictionary)。
- Produces: 无新代码——验证 `PerkLevels` 随整档序列化保留。

**说明:** `PerkLevels` 是代码常量 id,不是字表数据,**不进** `PruneUnknownCards`(那是给下架字清洗卡 id 的)。首版不做未知技能 id 清理(技能 id 稳定)。

- [ ] **Step 1: Write the failing test**

在 `MetaTests.cs` 加:

```csharp
[Test]
public void Save_RoundTrips_PerkLevels()
{
    var meta = new MetaState();
    meta.PerkLevels["yangyuan"] = 3;
    meta.PerkLevels["yiqi"] = 1;
    var restored = SaveSerializer.FromJson(SaveSerializer.ToJson(meta));
    Assert.That(PerkRules.PerkLevel(restored, "yangyuan"), Is.EqualTo(3));
    Assert.That(PerkRules.PerkLevel(restored, "yiqi"), Is.EqualTo(1));
}
```

- [ ] **Step 2: Run test to verify it fails or passes**

Run: `cd tools/coretests && …dotnet test --nologo -v q`
Expected: 大概率直接 PASS(JsonConvert 自动处理)。若 `SaveSerializer` 有显式字段白名单则会 FAIL——那时把 `PerkLevels` 补进序列化字段。

- [ ] **Step 3: (仅当 Step 2 失败)补序列化字段**

若 `SaveSerializer.cs` 用了显式 DTO/字段列表,把 `PerkLevels` 照 `CardLevels` 的写法补上。否则跳过。

- [ ] **Step 4: Confirm pass**

Run: `cd tools/coretests && …dotnet test --nologo -v q`
Expected: PASS。

- [ ] **Step 5: Commit**

```bash
git add Brushblade/Assets/_Project/Tests/MetaTests.cs
git commit -m "test(core): 技能等级存档往返"
```

---

### Task 4: 效果注入(五个注入点 + 金汤接入 B)

**Files:**
- Modify: `Brushblade/Assets/_Project/Core/Meta.cs`(`StartingLibrary` ~172-186 行)
- Modify: `Brushblade/Assets/_Project/Presentation/GameRoot.cs`(`StartTower` ~130-137、`StartSegment` ~162-173、`OnSegmentEnded` 段末护盾)
- Modify: `Brushblade/Assets/_Project/Presentation/MapView.cs`(HP 上限显示 ~79 行)
- Test: `Brushblade/Assets/_Project/Tests/PerkTests.cs`(StartingLibrary 部分)

**Interfaces:**
- Consumes: Task 1 聚合函数;B 的 `EndlessSaveState.NormalShield`、`BattleConfig.ApPerTurn`、`MetaRules.MaxHpFor`、`MetaRules.StartingLibrarySize`。

- [ ] **Step 1: Write the failing test(起手字库 +Library)**

在 `PerkTests.cs` 加(参考 `Meta.cs` 现有 `StartingLibrary(meta)` 签名):

```csharp
[Test]
public void StartingLibrary_GrowsWithBowen() // 博闻:起手字库 +1 格/级
{
    // StartingLibrary 去重且要求字在 OwnedCards:备 8 个不同的已拥有出阵字,
    // 验证截断上限 = StartingLibrarySize(6) + LibraryBonus
    var meta = new MetaState();
    foreach (var c in new[] { "火", "木", "水", "金", "土", "心", "林", "炎" })
    {
        meta.OwnedCards.Add(c);
        meta.Deck.Add(c);
    }
    Assert.That(MetaRules.StartingLibrary(meta).Count, Is.EqualTo(6)); // 默认容量
    meta.PerkLevels["bowen"] = 1;
    Assert.That(MetaRules.StartingLibrary(meta).Count, Is.EqualTo(7)); // 博闻 +1 格
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd tools/coretests && …dotnet test --nologo -v q`
Expected: FAIL——容量未随博闻增长。

- [ ] **Step 3: 注入 StartingLibrary(Core)**

`Meta.cs` 的 `StartingLibrary`(~172-186 行)把截断上限 `StartingLibrarySize` 改为 `StartingLibrarySize + PerkRules.LibraryBonus(meta)`(所有比较 `>= StartingLibrarySize` 的地方一并改)。

- [ ] **Step 4: 注入 GameRoot / MapView(Presentation)**

`GameRoot.StartTower`(~133 行):
```csharp
                    PlayerHp = MetaRules.MaxHpFor(level) + PerkRules.HpBonus(_meta),
```
且新建 `EndlessSaveState` 时首段金汤护盾:
```csharp
                    NormalShield = PerkRules.ShieldBonus(_meta), // 金汤:首段段首护盾
```

`GameRoot.StartSegment`:
```csharp
            int maxHp = MetaRules.MaxHpFor(MetaRules.CharacterLevel(_meta.CharacterXp))
                + PerkRules.HpBonus(_meta);                                   // ~162 行
            var battleConfig = new BattleConfig
            {
                DropTable = _campaign.DropTable,
                PlayerMaxHp = maxHp,
                UnlockedChars = _meta.Deck,
                ApPerTurn = 3 + PerkRules.ApBonus(_meta),                     // 一气
            };
            // ~172 行 startingInk:
            startingInk: _meta.Ink + snapshot.EarnedInk + PerkRules.InkBonus(_meta),
```

`GameRoot.OnSegmentEnded`(B 里设 `snapshot.NormalShield = 0` 处)改为金汤:
```csharp
            snapshot.NormalShield = PerkRules.ShieldBonus(_meta); // 下一段段首金汤护盾(每段一次)
            snapshot.PersistShield = run.CarriedPersistShield;
```

`MapView`(~79 行)HP 上限显示:
```csharp
                $"经验 {_meta.CharacterXp}    HP 上限 {MetaRules.MaxHpFor(level) + PerkRules.HpBonus(_meta)}",
```

- [ ] **Step 5: 测试 + 离线编译 + 提交**

Run: `cd tools/coretests && …dotnet test --nologo -v q`(StartingLibrary 测试 PASS)
Run: `cd tools/prescompile && …dotnet build --nologo -v q`(无 `error CS`)

```bash
git add Brushblade/Assets/_Project/Core/Meta.cs Brushblade/Assets/_Project/Presentation/GameRoot.cs Brushblade/Assets/_Project/Presentation/MapView.cs Brushblade/Assets/_Project/Tests/PerkTests.cs
git commit -m "feat(perk): 技能效果注入五点并接入金汤护盾"
```

---

### Task 5: 技能页 UI(PerkView)

**Files:**
- Create: `Brushblade/Assets/_Project/Presentation/PerkView.cs`
- Modify: `Brushblade/Assets/_Project/Presentation/MapView.cs`(加「技能」入口按钮)

**Interfaces:**
- Consumes: `PerkRules.All`、`PerkLevel`、`CanUpgradePerk`、`TryUpgradePerk`、`MetaRules.CharacterLevel`、`MetaStore.Save`;`Ui.*`/`Theme.*` helper(用法参考 `GameRoot.ShowSafeLayer`)。

**说明:** Presentation,coretests 盖不到,靠离线编译 + 手测外观。`Ui` helper 的确切方法名以现有 `MapView`/`GameRoot` 为准(下方骨架若某 helper 名不符,按现有代码替换)。

- [ ] **Step 1: 创建 PerkView**

新建 `Presentation/PerkView.cs`:

```csharp
using Brushblade.Core;
using UnityEngine;

namespace Brushblade.Presentation
{
    /// <summary>技能页(A):列出各技能等级/上限/下一级墨锭价,墨锭买断解锁与升级。</summary>
    public sealed class PerkView : MonoBehaviour
    {
        private MetaState _meta;
        private System.Action _onBack;

        public void Init(MetaState meta, System.Action onBack)
        {
            _meta = meta;
            _onBack = onBack;
            Build();
        }

        private void Build()
        {
            foreach (Transform child in transform) Destroy(child.gameObject);

            var card = Ui.CardPanel(transform, "Panel");
            Ui.Anchor((RectTransform)card.transform,
                new Vector2(0.1f, 0.06f), new Vector2(0.9f, 0.94f), Vector2.zero, Vector2.zero);
            var stack = Ui.VStack(card.transform, "Stack", 10);
            Ui.Stretch((RectTransform)stack.transform);

            Ui.ThemedLabel(stack.transform, "技能", 28, Theme.TextMain, Theme.TitleFont);
            Ui.IngotLabel(stack.transform, $"墨锭 {_meta.Ink}", 18);

            int charLevel = MetaRules.CharacterLevel(_meta.CharacterXp);
            foreach (var def in PerkRules.All)
                BuildPerkRow(stack.transform, def, charLevel);

            Ui.PillButton(stack.transform, "返回",
                () => _onBack(), Theme.InkSoft, Color.white, 18, new Vector2(200, 46));
        }

        private void BuildPerkRow(Transform parent, PerkDef def, int charLevel)
        {
            int level = PerkRules.PerkLevel(_meta, def.Id);
            var row = Ui.VStack(parent, def.Id, 4);

            string status = level >= def.MaxLevel
                ? $"{def.Name}  Lv{level}/{def.MaxLevel}  已满"
                : $"{def.Name}  Lv{level}/{def.MaxLevel}  ·  下一级 +{def.PerLevelValue}  ·  {def.InkCosts[level]} 墨锭";
            Ui.ThemedLabel(row.transform, status, 16, Theme.TextMain);

            if (level >= def.MaxLevel) return;

            bool gated = level == 0 && charLevel < def.UnlockLevel;
            string label = gated ? $"需角色 {def.UnlockLevel} 级"
                                  : (level == 0 ? "解锁" : "升级");
            Ui.PillButton(row.transform, label, () =>
            {
                if (PerkRules.TryUpgradePerk(_meta, def.Id))
                {
                    MetaStore.Save(_meta);
                    Build(); // 成功后刷新
                }
            }, gated ? Theme.InkSoft : Theme.Cinnabar, Color.white, 15, new Vector2(140, 40));
        }
    }
}
```

- [ ] **Step 2: 在 MapView 加入口**

在 `MapView` 的按钮区(参考现有导航按钮,如打开收集/图鉴的 `PillButton`)加一个「技能」按钮,点击切到 `PerkView`:创建挂 `PerkView` 的视图并 `Init(_meta, onBack: 返回地图)`。具体视图创建/切换用 `MapView` 现有的 `NewView`/导航模式。

- [ ] **Step 3: 离线编译**

Run: `cd tools/prescompile && /Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet build --nologo -v q`
Expected: 无 `error CS`。

- [ ] **Step 4: 手测(用户在 Unity Play)**

地图 → 技能 → 未达等级的技能按钮显示「需角色 N 级」且不可点;达标且墨锭足→解锁/升级,墨锭扣减,等级 +1;一气升到 2 级后按钮消失(已满)。重开游戏技能等级保留。

- [ ] **Step 5: Commit**

```bash
git add Brushblade/Assets/_Project/Presentation/PerkView.cs Brushblade/Assets/_Project/Presentation/MapView.cs
git commit -m "feat(ui): 技能页与地图入口"
```

---

## 自查(spec 覆盖)

- 数据模型 `PerkLevels` → Task 1;技能表(养元/润笔/金汤/博闻/一气,一气上限 2)→ Task 1。
- 解锁只 gate 0→1、之后纯墨锭 + 上限 → Task 2。
- 存档 → Task 3(纠正 spec:不进 `PruneUnknownCards`)。
- 五注入点(AP/HP/Library/Ink/Shield)+ HP 抬封顶 + 金汤每段一次 → Task 4。
- UI → Task 5。
- 类型一致:`PerkRules.PerkLevel/TryUpgradePerk/HpBonus…`、`PerkDef.MaxLevel`、`MetaState.PerkLevels` 全程同名。
- 断言无 `Is.AnyOf`。
