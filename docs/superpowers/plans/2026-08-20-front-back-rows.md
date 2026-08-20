# 前后排站位系统 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 给《字·斗》的战斗引入前后排站位——我方召唤物 3 前 3 后共 6 个玩家可指定的槽位,敌方同样分排,前排未清空时单体直接伤害够不到后排。

**Architecture:** `BattleEngine._summons` 从紧凑 `List` 改为定长 6 的数组(下标即槽位);敌方保持紧凑 `List`,排位是 `EnemyDef` 上的只读属性,靠生成期不变式维持 Front ≤3 / Back ≤3。所有「打谁」的裁定收进一个新的纯函数文件 `Core/Targeting.cs`,引擎与表现层都只调它,不各自判断。

**Tech Stack:** Unity 6000.5.2f1 / C#(Core 与 Data 层禁止引用 UnityEngine)、NUnit、Python 3 数据管线(pytest)。

**Spec:** `docs/superpowers/specs/2026-08-20-front-back-rows-design.md`

## Global Constraints

以下是项目级硬规则,**每个任务的要求都隐含包含本节**。违反其中任何一条都会造成「工装绿但 Unity 编辑器红」或架构违规。

- **Core 与 Data 禁止引用 UnityEngine**(asmdef 已设 `noEngineReferences: true`)。本计划新增的 `Core/Targeting.cs` 是纯 C#。
- **随机性一律走 Core 内带种子的 `GameRandom`**,禁用 `UnityEngine.Random`。
- **测试断言只用 Unity 版 NUnit 也支持的 API**:禁用 `Is.AnyOf` / `Is.All.AnyOf`。多选一写 `Is.EqualTo(a).Or.EqualTo(b)`,集合子集写 `Has.All.Matches<T>`。
- **测试里定位仓库根只能用 `TestContext.CurrentContext.TestDirectory`**,禁用 `AppContext.BaseDirectory`(后者在 Unity Test Runner 下指向编辑器安装目录)。
- **测试代码禁止直接引用 Newtonsoft**(`JsonConvert` 等)。要测序列化就走 `Data.SaveSerializer` / `Data.ConfigLoader` 这些真实入口。测试只能用 Tests asmdef references 列出的程序集(Core / Data)。
- **`chars.json` 是管线产物**,禁止手改。改字表走 `docs/design/字选型/技能机制详表.md` → `tools/pipeline/extract_values.py` → `tools/pipeline/export_chars.py`。守卫测试:`tools/pipeline/tests/test_export_chars.py::test_shipped_chars_json_is_regenerable_from_spec`。
- **提交信息用 conventional commits**(feat/fix/docs/chore + 范围),中文正文。
- **不做存档迁移**:项目未上线,快照结构直接改。
- **不升 Unity 版本**(6000.5.2f1)。

### 验证命令(本 worktree)

```bash
# Core/Data 单元测试(首选,毫秒级)
cd tools/coretests && /Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet test --nologo -v q
```

```bash
# 只跑某个测试类
cd tools/coretests && /Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet test --nologo -v q --filter "FullyQualifiedName~TargetingTests"
```

```bash
# Presentation 离线编译(改完 Presentation 必跑;worktree 里必须带 -p:ProjectAsm 覆盖)
cd tools/prescompile && /Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet build --nologo -v q -p:ProjectAsm=/Users/eugenewu/code/game/Brushblade/Library/ScriptAssemblies
```

```bash
# 管线测试(worktree 里先补软链,否则 fonts 测试挂 9 条)
ln -s /Users/eugenewu/code/game/tools/fonts/raw tools/fonts/raw 2>/dev/null; python3 -m pytest tools/pipeline/tests/ tools/fonts/tests/ -q
```

只看 `error CS`;`warning MSB3245` 是 Unity 程序集自带的无关引用,忽略。

---
## File Structure

**新建**

| 文件 | 职责 |
|---|---|
| `Brushblade/Assets/_Project/Core/Targeting.cs` | 「打谁」的唯一裁定处。纯静态函数,吃列表与 `GameRandom`,返回下标。引擎与表现层都只调它 |
| `Brushblade/Assets/_Project/Tests/TargetingTests.cs` | 裁定规则的表驱动用例 |
| `Brushblade/Assets/_Project/Tests/SummonSlotTests.cs` | 槽位召唤、顶替、覆盖尸体、复活回原槽、携带保留站位 |
| `Brushblade/Assets/_Project/Tests/EnemyRowTests.cs` | 敌方排位字段、分裂继承、生成期不变式 |

**修改**

| 文件 | 改什么 |
|---|---|
| `Core/BattleEngine.cs` | `_summons` 定长 6 槽;`Cast` 增加 `summonSlots` 参数;敌人与召唤物的目标裁定改调 `Targeting`;Boss `Pierce`/`Devour` 的「最前召唤物」口径 |
| `Core/EnemyDef.cs` | `EnemyDef` 增加 `Row`/`Range`/`Focus`;`SummonState.Capture(int slot)` |
| `Core/RunSnapshot.cs` | `SummonSnapshot` 增加 `Slot` |
| `Core/RunEngine.cs` | `CaptureAliveSummons` 保留槽位 |
| `Core/Endless.cs` | `BuildFloor` 的排位配额与「首位强制前排」 |
| `Core/Campaign.cs` | `Scale` 透传新字段(**不透传会静默丢失**,见 Task 4) |
| `Core/EffectDef.cs` | `EffectDef` 增加 `CanStrikeBackline` |
| `Core/SummonPassive.cs` | `SummonPassive` 增加 `Ranged` |
| `Data/ConfigLoader.cs` | `EnemyDto` 三个新字段 + `EffectDto.Backline` 的接线 |
| `Presentation/BattleView.cs` | `null` 守卫(Task 1)→ 四排布局重排(Task 10)→ 槽位面板与目标置灰(Task 11) |
| `tools/pipeline/extract_values.py` | `Ranged` / `Backline` 两个无数值 token |
| `docs/design/字选型/技能机制详表.md` | 刺 / 灶 / 烓 三行 |
| `Brushblade/Assets/StreamingAssets/config/enemies.json` | 三只新怪 + 现有怪的排位标注 |

**任务依赖**:1 → 2 → 3(我方槽位链);4 → 5 → {6, 7}(排位与裁定链);8 依赖 4;9 独立;10 → 11 依赖 3 与 7。

---

### Task 1: `_summons` 改定长 6 槽(纯重构)

**这一步不引入任何排位规则。** 全部召唤仍按 0..5 顺序填空位,行为与改前逐字节等价。它是纯重构,有现有全套 coretests 兜底;若与排位规则混改,出问题时分不清是重构错了还是规则错了。

**Files:**
- Modify: `Brushblade/Assets/_Project/Core/BattleEngine.cs`(24 处 `_summons` 触点)
- Modify: `Brushblade/Assets/_Project/Presentation/BattleView.cs`(6 处 `null` 守卫)
- Modify: `Brushblade/Assets/_Project/Core/RunEngine.cs:683`(1 处 `null` 守卫,预检裁定 1)
- Test: `Brushblade/Assets/_Project/Tests/SummonSlotTests.cs`(新建)

**Interfaces:**
- Produces: `BattleEngine.Summons` 的类型不变(`IReadOnlyList<SummonState>`,C# 数组天然实现该接口),但**长度恒为 6 且元素可为 `null`**。下标即槽位:0/1/2 = 前排,3/4/5 = 后排(本任务尚不使用这层含义)。
- Produces: `private const int SummonCap = 6;`(值不变)、`private const int FrontRowSize = 3;`(新增,本任务先加不用)

- [ ] **Step 1: 写失败测试**

新建 `Brushblade/Assets/_Project/Tests/SummonSlotTests.cs`:

```csharp
using System.Collections.Generic;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>召唤物槽位模型(2026-08-20):_summons 是定长 6 的槽位数组,下标即槽位。</summary>
    [TestFixture]
    public class SummonSlotTests
    {
        /// <summary>建一个只有「梅」(召 1 只,60 血 20 攻)和一只木桩敌人的引擎。</summary>
        private static BattleEngine MakeEngine()
        {
            var graph = new RecipeGraph(new List<CharDef>
            {
                new("木", Element.Wood),
                new("梅", Element.Wood, new[] { "木", "木" },
                    new[] { new EffectDef(EffectKind.Summon, 60, summonCount: 1, summonAttack: 20, summonChar: "梅") }),
            });
            var config = new BattleConfig { PlayerMaxHp = MetaRules.MaxHpFor(1) };
            // 敌人攻 200(一下就能打死 60 血的梅),血 9999(打不死,回合数可控)。
            // 近战默认打前排槽序最小的那只 —— KillFrontSummon 靠这条定向。
            var enemies = new List<EnemyDef> { new("木桩", Element.Earth, 9999, 200) };
            return new BattleEngine(graph, config,
                new[] { "梅", "梅", "梅", "梅", "梅", "梅", "梅" }, new string[0], enemies, seed: 1);
        }

        /// <summary>把 slot 上的召唤物打死,造出一具尸体。
        ///
        /// 不直接写 <c>Summons[slot].Hp = 0</c> —— <c>SummonState.Hp</c> 是 <c>internal set</c>,
        /// Tests 是独立程序集,跨程序集不可见;为测试放开它等于把活体状态的写权交出去。
        /// 走引擎的真实途径(敌人近战必打前排槽序最小的存活者)反而更贴近实际。
        /// 因此**只能用来打死前排里最靠前的那只**。</summary>
        private static void KillFrontSummon(BattleEngine engine, int slot)
        {
            for (int guard = 0; guard < 10
                 && engine.Summons[slot] != null && engine.Summons[slot].Alive; guard++)
                engine.EndTurn();
            Assert.That(engine.Summons[slot], Is.Not.Null);
            Assert.That(engine.Summons[slot].Alive, Is.False, "夹具前提:这只该被打死了");
        }

        [Test]
        public void Summons_IsAlwaysSixSlots_EmptyOnesAreNull()
        {
            var engine = MakeEngine();
            Assert.That(engine.Summons.Count, Is.EqualTo(6), "槽位数组恒长 6");
            for (int i = 0; i < 6; i++)
                Assert.That(engine.Summons[i], Is.Null, $"槽 {i} 开局应为空");
            Assert.That(engine.AliveSummonCount, Is.EqualTo(0));
        }

        [Test]
        public void Cast_FillsLowestEmptySlot()
        {
            var engine = MakeEngine();
            Assert.That(engine.Cast("梅"), Is.EqualTo(BattleError.None));
            Assert.That(engine.Summons[0], Is.Not.Null, "第一只落槽 0");
            Assert.That(engine.Summons[0].Char, Is.EqualTo("梅"));
            Assert.That(engine.Summons[1], Is.Null, "槽 1 仍空");
            Assert.That(engine.AliveSummonCount, Is.EqualTo(1));
        }
    }
}
```

`BattleConfig` / `RecipeGraph` / `CharDef` 的构造签名以仓库现状为准;若与上面不符,照现状改写这个 helper——**不要**改生产代码去迁就测试。

- [ ] **Step 2: 跑测试确认它失败**

```bash
cd tools/coretests && /Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet test --nologo -v q --filter "FullyQualifiedName~SummonSlotTests"
```

预期:`Summons_IsAlwaysSixSlots_EmptyOnesAreNull` FAIL(`Summons.Count` 是 0 不是 6)。

- [ ] **Step 3: 改字段声明与常量**

`Core/BattleEngine.cs:217-218`:

```csharp
// 改前
private readonly List<SummonState> _summons = new();
private const int SummonCap = 6; // 场上存活召唤物上限(2026-08-03:4 → 6)

// 改后
/// <summary>召唤物槽位(2026-08-20):**定长 6,下标即槽位**。0/1/2 = 前排,3/4/5 = 后排。
/// null = 空槽;Hp &lt;= 0 = 尸体,仍占槽,可被复活就地救回(引擎从不移除阵亡召唤物)。
/// 选定长数组而非「紧凑 List + Slot 字段」的理由见 spec §3.1:事件的 SecondIndex、
/// 表现层的血条引用、存档下标现在三者是同一个数,槽位化后仍是同一个数。</summary>
private readonly SummonState[] _summons = new SummonState[SummonCap];
private const int SummonCap = 6;      // 场上召唤物槽位数(2026-08-03:4 → 6)
private const int FrontRowSize = 3;   // 前排槽位数(2026-08-20):槽 [0, FrontRowSize) 为前排
```

- [ ] **Step 4: 逐处补 `null` 守卫**

`Core/BattleEngine.cs` 里 24 处触点,逐处改法如下(行号为改前位置):

| 行 | 改法 |
|---|---|
| 404(构造函数携带) | `foreach (var summon in startingSummons) _summons[NextEmptySlot()] = SummonState.Restore(summon);` — 本任务先按顺序填,Task 2 再改成按 `Slot` 落位 |
| 462(`Capture`) | `for (int s = 0; s < SummonCap; s++) if (_summons[s] != null) snapshot.Summons.Add(_summons[s].Capture());` |
| 494(`Restore`) | 同 404:先按顺序填 |
| 545(`Summons` 属性) | 不动。`SummonState[]` 直接满足 `IReadOnlyList<SummonState>` |
| 816-817(`CaptureOpeningStep`) | `var summons = new int[SummonCap]; for (int i = 0; i < SummonCap; i++) summons[i] = _summons[i]?.ActionMeter ?? 0;` |
| 855-859(`BuildSlots`) | 循环上界改 `SummonCap`,守卫改 `if (_summons[s] == null \|\| !_summons[s].Alive) continue;` |
| 884(`WriteBackMeters`) | 不动 —— `actor.Index` 只可能来自 `BuildSlots`,那里已滤掉 `null` |
| 1031(`ActSummonTurn`) | 首行改 `var summon = _summons[s]; if (summon == null \|\| !summon.Alive) return;` |
| 1128(`StrikeOnceWithSummon`) | 首行后加 `if (summon == null) return;` |
| 1457(`Revive`) | 不动 —— `slot` 来自 `FirstDeadSummonIndex()`,见下 |
| 1642(`_summons.Add(newborn)`) | 改 `_summons[NextEmptySlot()] = newborn;` |
| 1650(顶替) | 不动(`_summons[slot] = newborn`) |
| 1659(`SummonShield`) | `foreach (var summon in _summons) if (summon != null && summon.Alive) summon.Shield += shieldGrant;` |
| 1876(`HealPlayerAndSummons`) | `foreach (var summon in _summons) { if (summon == null \|\| !summon.Alive) continue; … }` |
| 1899(`AliveSummons`) | `foreach (var summon in _summons) if (summon != null && summon.Alive) alive++;` |
| 1910-1911(`FirstDeadSummonIndex`) | `for (int s = 0; s < SummonCap; s++) if (_summons[s] != null && !_summons[s].Alive) return s;` — **`null` 不是尸体**,复活救不回一个从未存在过的召唤物 |
| 1917-1918(`NextAliveSummonIndex`) | `for (int s = from; s < SummonCap; s++) if (_summons[s] != null && _summons[s].Alive) return s;` |
| 2204(`DamageSummon`) | 不动 —— 调用方保证非 `null` |
| 2304-2305(Boss `Deluge`) | 循环上界改 `SummonCap`,守卫改 `if (_summons[s] != null && _summons[s].Alive)` |
| 2347(Boss `Devour`) | 不动 —— `front` 来自 `FirstAliveSummonIndex()` |

**`BattleEngine.cs` 之外还有一处必须一起改**(预检裁定 1):

`Core/RunEngine.cs:683` 的 `CaptureAliveSummons` 直接 `foreach (var summon in Battle.Summons)` 并解引用 `summon.Alive`,槽位数组引入 `null` 后会 NPE,而这条路径被 `RunEngineTests` 覆盖 —— 不补的话本任务的「全套测试全绿」根本过不了:

```csharp
foreach (var summon in Battle.Summons)
{
    if (summon == null || !summon.Alive) continue;   // null = 空槽(2026-08-20)
    // …既有的 Capture 逻辑原样…
}
```

这是**临时守卫**,Task 2 会把整个方法改写成按下标遍历(为了带走槽位)。本任务只要它不 NPE。

⚠ 另外确认一处、但**不要改**:`Presentation/BattleView.cs:119` 的 `SummonAt(int i)` 在空槽上会返回 `null`。它的既有契约本就是「越界返回 `null`」,调用方(`Juice` 的召唤反击飞字)必须已经容忍 `null`。**只需读一眼 `Juice` 侧确认它确实 null-safe**;若发现它不容忍 null,在报告的 concerns 里写出来,不要顺手改 `Juice`。

新增一个私有辅助(放在 `FirstDeadSummonIndex` 旁边):

```csharp
/// <summary>最小的可落位槽:优先真正的空槽,其次尸体槽;全被存活者占满返回 −1。
/// Task 1 阶段这是唯一的落位策略(等价于改前的「List 尾部追加」);Task 3 起
/// 玩家可以指定槽位,本函数退化为「玩家没指定时的兜底」。</summary>
private int NextEmptySlot()
{
    for (int s = 0; s < SummonCap; s++)
        if (_summons[s] == null) return s;
    for (int s = 0; s < SummonCap; s++)
        if (!_summons[s].Alive) return s;
    return -1;
}
```

⚠ **`NextEmptySlot()` 可能返回 −1**。`EffectKind.Summon` 分支里 `if (AliveSummons() < SummonCap)` 那条守卫**不足以**保证有空槽——存活数 < 6 但 6 个槽被「存活者 + 尸体」占满是可能的。落位前必须判:

```csharp
if (AliveSummons() < SummonCap)
{
    int slot = NextEmptySlot();
    if (slot >= 0)
    {
        _summons[slot] = newborn;
        _events.Add(new BattleEvent(BattleEventKind.Summon, -1, value, slot));
        continue;
    }
}
```

注意这里**顺带改了事件**:原先新增召唤发的是 `new BattleEvent(BattleEventKind.Summon, -1, value)`(`SecondIndex` 缺省 −1 = 「新增,非顶替」),现在一律带上落位槽。表现层据此知道该画哪一格。`BattleView.DropReplacedSummonSnapshots()`(`BattleView.cs:2218`)靠 `e.SecondIndex >= 0` 判断「这是顶替」,改动后它会把新增也当成顶替——**必须同步改**:该方法只在 `Cast` 返回 `None` 后跑一次,作用是抹掉被顶替槽的出手前血量快照;对新增槽来说该槽本来就没有快照,`_summonAnimHp.Remove(slot)` 是无害的空操作。**确认无害后保持原样即可,不要改它。**

- [ ] **Step 5: 补 Presentation 的 `null` 守卫**

Core 改完编译能过,但 `BattleView` 会在运行期 `NullReferenceException`。本任务只补守卫,**不动布局**(布局是 Task 10)。

`Presentation/BattleView.cs` 六处:

```csharp
// :155-156  SnapshotPreHp
for (int i = 0; i < Battle.Summons.Count; i++)
    if (Battle.Summons[i] != null && Battle.Summons[i].Alive) _summonAnimHp[i] = Battle.Summons[i].Hp;

// :224-225  SummonHit 事件处理 —— 在既有的越界判断里追加 null
int si = e.SecondIndex;
if (si < 0 || si >= Battle.Summons.Count || Battle.Summons[si] == null
    || !_summonAnimHp.ContainsKey(si)
    || !_summonBarByCore.TryGetValue(si, out var sbar) || sbar.fill == null) break;

// :301-302  MeterSnapshot
var summons = new int[Battle.Summons.Count];
for (int i = 0; i < summons.Length; i++) summons[i] = Battle.Summons[i]?.ActionMeter ?? 0;

// :702  战场指纹(Fingerprint)
foreach (var summon in Battle.Summons)
    sb.Append('|').Append(summon?.Hp ?? -1);

// :896-898  DrawSummons —— 空槽直接跳过(Task 10 会改成画虚框)
var summon = Battle.Summons[i];
if (summon == null) continue;
if (!summon.Alive && !(Animating && _summonAnimHp.ContainsKey(i))) continue;

// :926-927  OnSummonClicked
if (index < 0 || index >= Battle.Summons.Count) return;
var summon = Battle.Summons[index];
if (summon == null || !summon.Alive) return;
```

`:702` 的指纹用 `-1` 代表空槽:它只用来判断「场面变了没有」,只要空槽与任何真实血量不撞就行(血量下钳 0,`-1` 撞不上)。

- [ ] **Step 6: 修既有测试的 `Summons.Count` 口径**

14 处 `Summons.Count` 现在恒为 6。逐处按语义改成 `AliveSummonCount`:

```bash
grep -rn "Summons.Count" Brushblade/Assets/_Project/Tests/*.cs
```

- `AttackStatTests.cs:184`、`BattleEngineTests.cs:935`、`BossSkillTests.cs:84`、`RunEngineTests.cs:988` 等断言「场上有 N 只召唤物」的,一律改成 `engine.AliveSummonCount`。
- `RunEngineTests.cs:985` 与 `:1034` 断言的是 `run.CarriedSummons.Count`(携带列表,仍是紧凑 `List`),**不要改**。
- `RunEngineTests.cs:1015` 那条「死尸不带走,槽位从 0 号重排」在本任务仍成立(携带按顺序填),Task 2 才改口径。

- [ ] **Step 7: 跑全套测试**

```bash
cd tools/coretests && /Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet test --nologo -v q
```

预期:**全绿**。这是纯重构,一条都不该红。任何红都说明重构改变了行为——回去找,不要改测试迁就。

```bash
cd tools/prescompile && /Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet build --nologo -v q -p:ProjectAsm=/Users/eugenewu/code/game/Brushblade/Library/ScriptAssemblies
```

预期:无 `error CS`。

- [ ] **Step 8: 提交**

```bash
git add Brushblade/Assets/_Project/Core/BattleEngine.cs Brushblade/Assets/_Project/Core/RunEngine.cs Brushblade/Assets/_Project/Presentation/BattleView.cs Brushblade/Assets/_Project/Tests/
git commit -m "refactor(core): 召唤物改定长 6 槽数组 —— 下标即槽位,行为不变

为前后排站位铺路。_summons 从紧凑 List 换成定长 6 的数组,null = 空槽,
尸体仍占槽。本次不引入任何排位规则,全部召唤仍按 0..5 顺序填空位。

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---
### Task 2: 存档携带槽位

**Files:**
- Modify: `Brushblade/Assets/_Project/Core/RunSnapshot.cs`(`SummonSnapshot`)
- Modify: `Brushblade/Assets/_Project/Core/EnemyDef.cs`(`SummonState.Capture`)
- Modify: `Brushblade/Assets/_Project/Core/BattleEngine.cs`(构造函数携带 / `Capture` / `Restore`)
- Modify: `Brushblade/Assets/_Project/Core/RunEngine.cs`(`CaptureAliveSummons`)
- Modify: `Brushblade/Assets/_Project/Tests/RunEngineTests.cs`(改一条口径)
- Test: `Brushblade/Assets/_Project/Tests/SummonSlotTests.cs`(追加)

**Interfaces:**
- Consumes: Task 1 的 `_summons` 定长 6 槽数组、`SummonCap`
- Produces: `SummonSnapshot.Slot`(`int`,槽位 0..5);`SummonState.Capture(int slot)` 签名变更(原 `Capture()` 无参)

- [ ] **Step 1: 写失败测试**

追加到 `SummonSlotTests.cs`:

```csharp
[Test]
public void CarriedSummons_KeepTheirSlots_AcrossBattles()
{
    var engine = MakeEngine();
    engine.Cast("梅");                       // 顺序填 → 槽 0
    engine.Cast("梅");                       // 顺序填 → 槽 1
    KillFrontSummon(engine, 0);              // 槽 0 阵亡,槽 1 活着
    var carried = new List<SummonSnapshot>();
    for (int s = 0; s < engine.Summons.Count; s++)
        if (engine.Summons[s] != null && engine.Summons[s].Alive)
            carried.Add(engine.Summons[s].Capture(s));

    Assert.That(carried.Count, Is.EqualTo(1), "只带走活的");
    Assert.That(carried[0].Slot, Is.EqualTo(1), "带走的那只记着它原来的槽位");

    var graph = new RecipeGraph(new List<CharDef> { new("木", Element.Wood) });
    var config = new BattleConfig { PlayerMaxHp = MetaRules.MaxHpFor(1) };
    var next = new BattleEngine(graph, config, new string[0], new string[0],
        new List<EnemyDef> { new("木桩", Element.Earth, 9999, 0) }, seed: 1,
        startingSummons: carried);

    Assert.That(next.Summons[0], Is.Null, "槽 0 不该被顶上来");
    Assert.That(next.Summons[1], Is.Not.Null, "站位原样保留");
    Assert.That(next.AliveSummonCount, Is.EqualTo(1));
}
```

⚠ 本用例用到 Task 3 才引入的 `summonSlots` 参数。**Task 2 先按顺序召唤即可**(`engine.Cast("梅")` 两次,自然落槽 0 与槽 1),等 Task 3 落地后再把这两行改成显式指定槽位——写成显式的版本更能表达意图,但不该让 Task 2 依赖尚不存在的 API。

- [ ] **Step 2: 跑测试确认它失败**

```bash
cd tools/coretests && /Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet test --nologo -v q --filter "FullyQualifiedName~SummonSlotTests"
```

预期:编译失败(`Capture(int)` 不存在 / `SummonSnapshot.Slot` 不存在)。

- [ ] **Step 3: 加 `Slot` 字段**

`Core/RunSnapshot.cs`,`SummonSnapshot` 类里追加:

```csharp
/// <summary>槽位 0..5(2026-08-20):0/1/2 = 前排,3/4/5 = 后排。
/// 携带过场与断点续爬都按它原样落位 —— 玩家布的阵不该被系统打乱。</summary>
public int Slot { get; set; }
```

`Core/EnemyDef.cs`,`SummonState.Capture` 改成吃槽位:

```csharp
// 改前:public SummonSnapshot Capture() => new() { … };
/// <summary>槽位由持有者传入 —— SummonState 自己不知道它站在哪一格
/// (槽位是 BattleEngine._summons 的数组下标,不是这只召唤物的属性,
/// 存成字段会有与下标失配的风险)。</summary>
public SummonSnapshot Capture(int slot) => new()
{
    Slot = slot,
    Char = Char, Element = Element, Hp = Hp, MaxHp = MaxHp, Attack = Attack,
    ActionMeter = ActionMeter, Speed = Speed, Shield = Shield,
    Passive = Passive?.Clone(),
};
```

- [ ] **Step 4: 三处落位改成按 `Slot`**

`Core/BattleEngine.cs`:

```csharp
// 构造函数(原 :402-404)
// 携带的召唤物按原槽位落位(2026-08-20)。Slot 越界或撞车一律回落到最小空槽 ——
// 携带态来源受控,这条只是防越界,不是会触发的分支。
if (startingSummons != null)
    foreach (var summon in startingSummons)
        PlaceCarried(SummonState.Restore(summon), summon.Slot);

// Capture(原 :462)
for (int s = 0; s < SummonCap; s++)
    if (_summons[s] != null) snapshot.Summons.Add(_summons[s].Capture(s));

// Restore(原 :493-494)
foreach (var summon in snapshot.Summons)
    engine.PlaceCarried(SummonState.Restore(summon), summon.Slot);
```

新增私有辅助:

```csharp
/// <summary>把携带/读档来的召唤物放回它记下的槽位;槽位非法或已被占则回落到最小空槽。</summary>
private void PlaceCarried(SummonState summon, int slot)
{
    if (slot < 0 || slot >= SummonCap || _summons[slot] != null)
        slot = NextEmptySlot();
    if (slot < 0) return; // 六槽全满:携带态来源受上限约束,走不到这;留作越界兜底
    _summons[slot] = summon;
}
```

`Core/RunEngine.cs` 的 `CaptureAliveSummons`(约 `:680`)改成按下标遍历:

```csharp
private List<SummonSnapshot> CaptureAliveSummons()
{
    var alive = new List<SummonSnapshot>();
    for (int s = 0; s < Battle.Summons.Count; s++)
    {
        var summon = Battle.Summons[s];
        if (summon == null || !summon.Alive) continue;
        alive.Add(summon.Capture(s));   // 槽位随之带走
    }
    return alive;
}
```

- [ ] **Step 5: 改既有测试的口径**

`Brushblade/Assets/_Project/Tests/RunEngineTests.cs:1015`:

```csharp
// 改前
Assert.That(run.Battle.Summons.Count, Is.EqualTo(1), "死尸不带走,槽位从 0 号重排");
// 改后
Assert.That(run.Battle.AliveSummonCount, Is.EqualTo(1), "死尸不带走");
Assert.That(run.Battle.Summons[0], Is.Null, "站位保留:原槽 0 的死尸不带走,活着的那只不前移");
```

具体断言哪一格非空,取决于该测试里活下来的是原来的第几只——**先跑一次读实际值再写死**,不要猜。

- [ ] **Step 6: 跑测试**

```bash
cd tools/coretests && /Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet test --nologo -v q
```

预期:全绿。

- [ ] **Step 7: 提交**

```bash
git add Brushblade/Assets/_Project/Core/ Brushblade/Assets/_Project/Tests/
git commit -m "feat(core): 召唤物携带保留站位 —— SummonSnapshot 记下槽位

携带过场与断点续爬都按原槽位落位,玩家布的阵不再被系统重排。
SummonState.Capture 改为吃槽位入参:槽位是数组下标不是召唤物属性,
存成字段会有与下标失配的风险。

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 3: 指定槽位召唤

**Files:**
- Modify: `Brushblade/Assets/_Project/Core/BattleEngine.cs`(`Cast` 签名 / `ApplyEffects` 的 `Summon` 分支 / `SummonReplaceCountOf`)
- Test: `Brushblade/Assets/_Project/Tests/SummonSlotTests.cs`(追加)

**Interfaces:**
- Consumes: Task 1 的 `NextEmptySlot()`、`SummonCap`
- Produces: `BattleEngine.Cast(string charId, int targetIndex = -1, bool replaceSummon = false, bool attackMode = false, int libraryIndex = -1, IReadOnlyList<int> summonSlots = null)` —— `summonSlots` 为 `null` 时行为与 Task 1 等价(按 `NextEmptySlot()` 依次填),非 `null` 时第 n 只召唤物落到 `summonSlots[n]`
- Produces: `BattleEngine.SlotOccupancy(int slot)` 返回 `SlotState`(`Empty` / `Corpse` / `Alive`),表现层据此决定点该槽要不要弹顶替确认

- [ ] **Step 1: 写失败测试**

追加到 `SummonSlotTests.cs`:

```csharp
[Test]
public void Cast_WithExplicitSlot_LandsThere()
{
    var engine = MakeEngine();
    Assert.That(engine.Cast("梅", summonSlots: new[] { 4 }), Is.EqualTo(BattleError.None));
    Assert.That(engine.Summons[4], Is.Not.Null, "落在玩家指定的后排槽");
    Assert.That(engine.Summons[0], Is.Null, "不再自动占前排最小槽");
}

[Test]
public void Cast_OntoCorpseSlot_OverwritesWithoutReplaceFlag()
{
    var engine = MakeEngine();
    engine.Cast("梅", summonSlots: new[] { 0 });
    KillFrontSummon(engine, 0);   // 只能打死前排最靠前的那只,所以这里用槽 0
    // 尸体槽是空位的一种:不需要 replaceSummon 确认
    Assert.That(engine.Cast("梅", summonSlots: new[] { 0 }), Is.EqualTo(BattleError.None));
    Assert.That(engine.Summons[0].Alive, Is.True);
    Assert.That(engine.AliveSummonCount, Is.EqualTo(1));
}

[Test]
public void Cast_OntoLivingSlot_NeedsReplaceConfirmation()
{
    var engine = MakeEngine();
    engine.Cast("梅", summonSlots: new[] { 2 });
    Assert.That(engine.Cast("梅", summonSlots: new[] { 2 }), Is.EqualTo(BattleError.SummonCapFull),
        "点存活槽 = 顶替,必须先确认");
    Assert.That(engine.AliveSummonCount, Is.EqualTo(1), "被拒的这次不许改动任何状态");
    Assert.That(engine.Cast("梅", replaceSummon: true, summonSlots: new[] { 2 }), Is.EqualTo(BattleError.None));
    Assert.That(engine.AliveSummonCount, Is.EqualTo(1), "顶替不增员");
}

[Test]
public void SlotOccupancy_ReportsEmptyCorpseAlive()
{
    var engine = MakeEngine();
    Assert.That(engine.SlotOccupancy(0), Is.EqualTo(SlotState.Empty));
    Assert.That(engine.SlotOccupancy(3), Is.EqualTo(SlotState.Empty), "后排空槽同样报 Empty");
    engine.Cast("梅", summonSlots: new[] { 0 });
    Assert.That(engine.SlotOccupancy(0), Is.EqualTo(SlotState.Alive));
    KillFrontSummon(engine, 0);
    Assert.That(engine.SlotOccupancy(0), Is.EqualTo(SlotState.Corpse));
}
```

`KillFrontSummon` 是 Task 1 在本测试类里建好的辅助(走敌人近战的真实路径打死前排最靠前的那只),**直接用,不要另写**。它只能打死前排最靠前的召唤物,所以需要尸体的两条用例都用槽 0。

- [ ] **Step 2: 跑测试确认它失败**

```bash
cd tools/coretests && /Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet test --nologo -v q --filter "FullyQualifiedName~SummonSlotTests"
```

预期:编译失败(`summonSlots` 参数与 `SlotState` 不存在)。

- [ ] **Step 3: 加 `SlotState` 与查询**

`Core/BattleEngine.cs`,放在 `BattleError` 枚举旁边:

```csharp
/// <summary>召唤槽的占用状态(2026-08-20)。表现层据此决定点该槽的后果:
/// Empty / Corpse 直接落位,Alive 要先弹顶替确认。</summary>
public enum SlotState
{
    Empty,   // 空槽
    Corpse,  // 尸体占着(可被覆盖,也可被「复活」就地救回)
    Alive,   // 存活召唤物占着
}
```

在 `AliveSummonCount` 旁边加:

```csharp
/// <summary>前排槽位数(2026-08-20):槽 [0, FrontRow) 为前排,其余为后排。</summary>
public int FrontRow => FrontRowSize;

public SlotState SlotOccupancy(int slot)
{
    if (slot < 0 || slot >= SummonCap || _summons[slot] == null) return SlotState.Empty;
    return _summons[slot].Alive ? SlotState.Alive : SlotState.Corpse;
}
```

- [ ] **Step 4: `Cast` 增加 `summonSlots` 参数**

`Core/BattleEngine.cs:593` 起。签名追加(**放在最后**,保持既有调用点全部不变):

```csharp
/// <param name="summonSlots">玩家为本次召唤指定的槽位,第 n 只落 summonSlots[n]。
/// null = 未指定,按 NextEmptySlot() 依次填(测试与自动路径走这条)。
/// 指定到存活槽 = 顶替,与「六槽全满」同口径,需要 replaceSummon 确认。</param>
public BattleError Cast(string charId, int targetIndex = -1, bool replaceSummon = false,
    bool attackMode = false, int libraryIndex = -1, IReadOnlyList<int> summonSlots = null)
```

`:622` 那条强阻断守卫改成同时看指定槽:

```csharp
// 改前
if (!replaceSummon && SummonReplaceCountOf(def, attackMode) > 0) return BattleError.SummonCapFull;
// 改后
if (!replaceSummon && SummonReplaceCountOf(def, attackMode, summonSlots) > 0) return BattleError.SummonCapFull;
```

`:644` 把参数透传下去:

```csharp
ApplyEffects(def, targetIndex, replaceSummon, attackMode, summonSlots);
```

`SummonReplaceCountOf`(`:755`)增加同名可选参数:

```csharp
/// <summary>本次召唤会顶掉几只**存活**召唤物(0 = 不顶人,可以直接出)。
/// 指定了槽位就数这些槽里有几个是 Alive;没指定就退回「超出上限的部分」。</summary>
public int SummonReplaceCountOf(CharDef def, bool attackMode = false,
    IReadOnlyList<int> summonSlots = null)
{
    int count = SummonCountOf(def, attackMode);
    if (count <= 0) return 0;
    if (summonSlots == null)
        return Math.Max(0, AliveSummons() + count - SummonCap);
    int replaced = 0;
    for (int n = 0; n < count && n < summonSlots.Count; n++)
        if (SlotOccupancy(summonSlots[n]) == SlotState.Alive) replaced++;
    return replaced;
}
```

- [ ] **Step 5: `ApplyEffects` 的 `Summon` 分支按指定槽落位**

`Core/BattleEngine.cs:1619-1652`。把整个 `for (int n = 0; …)` 循环体的落位部分换成:

```csharp
case EffectKind.Summon:
    for (int n = 0; n < effect.SummonCount; n++)
    {
        // (被动/攻击力/满格计量器等既有注释与代码原样保留,只改落位)
        var newborn = new SummonState(effect.SummonChar, attacker, value,
            ScaleByAttack(MetaRules.ScaleByCardLevel(effect.SummonAttack, cardLevel)),
            effect.Passive);
        newborn.ActionMeter = TurnScheduler.Threshold;

        // 落位:玩家指定优先,未指定退回最小空槽(与 Task 1 等价)
        int slot = summonSlots != null && n < summonSlots.Count ? summonSlots[n] : NextEmptySlot();
        if (slot < 0 || slot >= SummonCap) break;          // 越界兜底
        bool occupiedByAlive = _summons[slot] != null && _summons[slot].Alive;
        if (occupiedByAlive && !replaceSummon) break;      // 已在 Cast 拒出,走不到这

        // SecondIndex 一律报落位槽:新增与顶替都要让表现层知道画哪一格。
        // 「是不是顶替」表现层自己看该槽原来有没有活着的召唤物,不靠事件区分。
        _summons[slot] = newborn;
        _events.Add(new BattleEvent(BattleEventKind.Summon, -1, value, slot));
    }
    // (SummonShield 那段原样保留)
    break;
```

**删掉** `replaceCursor` 及其在方法头部的声明(`:1318` 的 `int replaceCursor = 0;`)与 `NextAliveSummonIndex(replaceCursor)` 那条路径 —— 顶替目标现在由玩家指定,不再需要「从最前一只起逐只后移」的游标。`NextAliveSummonIndex` 本身**保留**:`FirstAliveSummonIndex()` 还在用它。

⚠ 未指定槽位时(`summonSlots == null`)且六槽被「存活 + 尸体」占满:`NextEmptySlot()` 返回 −1,这里 `break`,**不召出也不报错**——与改前「`AliveSummons() >= SummonCap` 且 `!replaceSummon` 时 `break`」同口径。

- [ ] **Step 6: 跑测试**

```bash
cd tools/coretests && /Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet test --nologo -v q
```

预期:全绿(新用例通过,既有用例因 `summonSlots` 默认 `null` 而行为不变)。

- [ ] **Step 7: 提交**

```bash
git add Brushblade/Assets/_Project/Core/BattleEngine.cs Brushblade/Assets/_Project/Tests/SummonSlotTests.cs
git commit -m "feat(core): 召唤可指定槽位 —— Cast 增加 summonSlots

第 n 只召唤物落 summonSlots[n];未指定则按最小空槽填(与改前等价)。
点尸体槽 = 覆盖不弹确认,点存活槽 = 顶替,与六槽全满同走 SummonCapFull。
新增 SlotOccupancy 供表现层判断点某格的后果。

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---
### Task 4: 敌方排位数据落地

本任务只把「谁站哪排」这件事存下来并保证不变式,**不改任何定向行为**。

**Files:**
- Modify: `Brushblade/Assets/_Project/Core/EnemyDef.cs`(三个枚举 + `EnemyDef` 三字段 + `EnemyState.Row` + `Capture`/`Restore`)
- Modify: `Brushblade/Assets/_Project/Core/RunSnapshot.cs`(`EnemySnapshot.Row`)
- Modify: `Brushblade/Assets/_Project/Core/BattleEngine.cs`(构造函数分配排位 + 分裂继承)
- Modify: `Brushblade/Assets/_Project/Core/Campaign.cs`(`Scale` 透传)
- Modify: `Brushblade/Assets/_Project/Data/ConfigLoader.cs`(`EnemyDto` 三字段)
- Test: `Brushblade/Assets/_Project/Tests/EnemyRowTests.cs`(新建)

**Interfaces:**
- Produces: `enum EnemyRow { Front, Back }`、`enum AttackRange { Melee, Ranged }`、`enum AttackFocus { Default, Player }`(均在 `Brushblade.Core` 命名空间)
- Produces: `EnemyDef.Row` / `EnemyDef.Range` / `EnemyDef.Focus`(只读,缺省 `Front` / `Melee` / `Default`)
- Produces: `EnemyState.Row`(`EnemyRow`,可写:开场按配额分配后可能与 `Def.Row` 不同)
- Produces: `BattleEngine.EnemyRowCap = 3`(每排敌人上限)

**为什么排位存在 `EnemyState` 而不是只读 `Def.Row`**:一场最多 6 只敌人,而每排上限 3,所以「后排偏好」的怪溢出时要**改判**到前排。若把改判后的排位写回一个新的 `EnemyDef`,同一个 `Id` 就会对应两个不同的 `Def`,而 `BattleEngine.Restore` 是按 `Id` 查 `Def` 的(`enemyDefs` 是 `id → def` 字典)——两只同名怪的排位会在读档时被合并成一个。所以排位必须是**实例状态**,并进快照。

- [ ] **Step 1: 写失败测试**

新建 `Brushblade/Assets/_Project/Tests/EnemyRowTests.cs`:

```csharp
using System.Collections.Generic;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>敌方排位(2026-08-20):Row/Range/Focus 三个正交字段,
    /// 每排上限 3,溢出改判到另一排。</summary>
    [TestFixture]
    public class EnemyRowTests
    {
        private static BattleEngine MakeEngine(params EnemyDef[] enemies)
        {
            var graph = new RecipeGraph(new List<CharDef> { new("木", Element.Wood) });
            var config = new BattleConfig { PlayerMaxHp = MetaRules.MaxHpFor(1) };
            return new BattleEngine(graph, config, new string[0], new string[0],
                new List<EnemyDef>(enemies), seed: 1);
        }

        private static EnemyDef Mob(string id, EnemyRow row = EnemyRow.Front) =>
            new(id, Element.Earth, 100, 0, row: row);

        [Test]
        public void EnemyDef_DefaultsToFrontMeleeDefault()
        {
            var def = new EnemyDef("错字鬼", Element.Wood, 140, 40);
            Assert.That(def.Row, Is.EqualTo(EnemyRow.Front));
            Assert.That(def.Range, Is.EqualTo(AttackRange.Melee));
            Assert.That(def.Focus, Is.EqualTo(AttackFocus.Default));
        }

        [Test]
        public void Rows_HonourPreference_WhenWithinCap()
        {
            var engine = MakeEngine(Mob("a"), Mob("b", EnemyRow.Back), Mob("c"));
            Assert.That(engine.Enemies[0].Row, Is.EqualTo(EnemyRow.Front));
            Assert.That(engine.Enemies[1].Row, Is.EqualTo(EnemyRow.Back));
            Assert.That(engine.Enemies[2].Row, Is.EqualTo(EnemyRow.Front));
        }

        [Test]
        public void Rows_OverflowToTheOtherRow_WhenPreferredIsFull()
        {
            // 四只都想站后排,后排只有 3 格 —— 第四只改判前排
            var engine = MakeEngine(Mob("a", EnemyRow.Back), Mob("b", EnemyRow.Back),
                Mob("c", EnemyRow.Back), Mob("d", EnemyRow.Back));
            int back = 0, front = 0;
            foreach (var e in engine.Enemies)
                if (e.Row == EnemyRow.Back) back++; else front++;
            Assert.That(back, Is.EqualTo(3), "后排不超过 3");
            Assert.That(front, Is.EqualTo(1), "溢出的改判前排");
            Assert.That(engine.Enemies[3].Row, Is.EqualTo(EnemyRow.Front), "改判的是排在后面的那只");
        }

        [Test]
        public void SixEnemies_NeverExceedThreePerRow()
        {
            var engine = MakeEngine(Mob("a"), Mob("b"), Mob("c"), Mob("d"), Mob("e"), Mob("f"));
            int back = 0, front = 0;
            foreach (var e in engine.Enemies)
                if (e.Row == EnemyRow.Back) back++; else front++;
            Assert.That(front, Is.EqualTo(3));
            Assert.That(back, Is.EqualTo(3));
        }
    }
}
```

- [ ] **Step 2: 跑测试确认它失败**

```bash
cd tools/coretests && /Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet test --nologo -v q --filter "FullyQualifiedName~EnemyRowTests"
```

预期:编译失败(`EnemyRow` 等不存在)。

- [ ] **Step 3: 加枚举与 `EnemyDef` 字段**

`Core/EnemyDef.cs`,放在 `EnemyAbility` 枚举之后:

```csharp
/// <summary>站位(2026-08-20)。敌我各 3 前 3 后。前排未清空时,单体直接伤害够不到后排。
/// 排位只决定**能不能被够到**,不决定这只单位自己能不能出手——后排照常攻击。</summary>
public enum EnemyRow { Front, Back }

/// <summary>攻击距离(2026-08-20)。Ranged 无视对方前排。
/// 与 <see cref="EnemyAbility"/> 正交:做成 Ability 的取值会与灯花/焦痕互斥,
/// 而「远程的灯花」是完全合理的组合。</summary>
public enum AttackRange { Melee, Ranged }

/// <summary>够得着玩家时打谁(2026-08-20)。
/// Default = 在「对方存活后排 ∪ 玩家」里均匀随机;Player = 死盯玩家。</summary>
public enum AttackFocus { Default, Player }
```

`EnemyDef` 类里,`Speed` 之后追加三个只读属性:

```csharp
/// <summary>站位偏好(2026-08-20,缺省前排)。实际站位由 <see cref="EnemyState.Row"/> 决定
/// ——每排上限 3,偏好排满了会被改判到另一排。</summary>
public EnemyRow Row { get; }
public AttackRange Range { get; }
public AttackFocus Focus { get; }
```

构造函数追加三个可选参数(**放在最后**,保持既有调用点不变):

```csharp
public EnemyDef(string id, Element element, int maxHp, int attack,
    EnemyAbility ability = EnemyAbility.None, IReadOnlyList<BossPhaseDef> phases = null,
    int defense = 0, int speed = 0,
    EnemyRow row = EnemyRow.Front, AttackRange range = AttackRange.Melee,
    AttackFocus focus = AttackFocus.Default)
{
    // …既有赋值原样…
    Row = row;
    Range = range;
    Focus = focus;
}
```

- [ ] **Step 4: `EnemyState.Row` + 快照往返**

`Core/EnemyDef.cs` 的 `EnemyState` 类里,`Speed` 属性旁追加:

```csharp
/// <summary>实际站位(2026-08-20)。开场由 BattleEngine 按每排上限 3 分配:
/// 优先吃 Def.Row,该排满了改判到另一排。**进快照** —— 同一个 Id 的两只怪
/// 可能站不同排,而 Restore 是按 Id 查 Def 的,不存就会在读档时被合并。</summary>
public EnemyRow Row { get; internal set; }
```

`EnemyState.Capture()` 的对象初始化器里追加 `Row = Row,`;`EnemyState.Restore` 的初始化器里追加 `Row = snapshot.Row,`。

`Core/RunSnapshot.cs` 的 `EnemySnapshot` 类里追加:

```csharp
public EnemyRow Row { get; set; }   // 实际站位(2026-08-20)
```

- [ ] **Step 5: 构造函数按配额分配排位**

`Core/BattleEngine.cs`,常量区加 `private const int EnemyRowCap = 3;`(与 `EnemyCap = 6` 相邻),并公开 `public int EnemyRowCapacity => EnemyRowCap;`。

构造函数里那句 `foreach (var def in enemies) _enemies.Add(new EnemyState(def, …));`(约 `:395`)之后追加排位分配:

```csharp
AssignRows();
```

新增私有方法(放在 `AliveSummons` 附近):

```csharp
/// <summary>按每排上限 3 给场上敌人定实际站位(2026-08-20)。
/// 按 _enemies 顺序依次分:先满足 Def.Row 的偏好,该排已满则改判到另一排。
/// 两排都满走不到 —— EnemyCap 6 = 3 + 3,列表长度天然受限。</summary>
private void AssignRows()
{
    int front = 0, back = 0;
    foreach (var enemy in _enemies)
    {
        bool wantsBack = enemy.Def.Row == EnemyRow.Back;
        if (wantsBack && back < EnemyRowCap) { enemy.Row = EnemyRow.Back; back++; }
        else if (!wantsBack && front < EnemyRowCap) { enemy.Row = EnemyRow.Front; front++; }
        else if (front < EnemyRowCap) { enemy.Row = EnemyRow.Front; front++; }
        else { enemy.Row = EnemyRow.Back; back++; }
    }
}
```

**`Restore` 不调 `AssignRows()`** —— 读档时排位从快照恢复,重算会打乱已经打了一半的阵型。

- [ ] **Step 6: 分裂继承排位**

`Core/BattleEngine.cs:2067` 的叠字怪分裂,守闸从「场上总数 < 6」改成「母体所在排未满」:

```csharp
// 叠字怪:首次受击存活 → 分裂成两个半血(8.3)。2026-08-20:克隆继承母体排位;
// 母体那排满了就落另一排;两排都满(= 场上 6 只)才不分裂。
if (enemy.Def.Ability == EnemyAbility.Split && !IsSilenced(enemy) && !enemy.HasSplit
    && _enemies.Count < EnemyCap)
{
    int half = (enemy.Hp + 1) / 2;
    enemy.Hp = half;
    enemy.HasSplit = true;
    var clone = new EnemyState(enemy.Def)
    {
        Hp = half,
        BaseAttack = enemy.Attack,
        HasSplit = true,
        Row = RowWithSpace(enemy.Row),
    };
    _enemies.Add(clone);
    _events.Add(new BattleEvent(BattleEventKind.EnemySplit, enemyIndex, half));
}
```

```csharp
/// <summary>优先返回 preferred 排(未满时),否则另一排。调用方已保证场上未满 EnemyCap,
/// 所以必有一排有空位。</summary>
private EnemyRow RowWithSpace(EnemyRow preferred)
{
    int count = 0;
    foreach (var e in _enemies)
        if (e.Row == preferred) count++;
    return count < EnemyRowCap ? preferred : (preferred == EnemyRow.Front ? EnemyRow.Back : EnemyRow.Front);
}
```

注意 `RowWithSpace` 数的是**全部**同排敌人(含尸体),与 `AssignRows` 同口径——尸体仍占位,不然清完前排后分裂怪会往前排挤,阵型会跳。

- [ ] **Step 7: `Campaign.Scale` 透传新字段**

`Core/Campaign.cs:90-92`。**不透传会静默丢失**——无尽层的每一只怪都过 `Scale`,漏了这一步排位在无尽模式里全部回落默认值,而单元测试若只测 `BattleEngine` 构造是发现不了的。

```csharp
return new EnemyDef(enemy.Id, enemy.Element,
    Scaled(enemy.MaxHp, scale), Scaled(enemy.Attack, scale),
    enemy.Ability, phases, ScaledDefense(enemy.Defense, scale), enemy.Speed,
    enemy.Row, enemy.Range, enemy.Focus);
```

⚠ 上面顺手把 `enemy.Speed` 也补上了 —— 它是**既有的**同型缺陷(`Scale` 一直没透传 speed,`EnemyDef.Speed` 的文档注释里记着这条通道「只接了一半」)。眼下全部字怪的 speed 都是 0,补它是零行为变化的纯修正,与本任务同因同源,顺手带上。

补一条测试钉住(追加到 `EnemyRowTests.cs`):

```csharp
[Test]
public void Scale_PreservesRowRangeFocus()
{
    var def = new EnemyDef("悬针", Element.Metal, 90, 45,
        row: EnemyRow.Back, range: AttackRange.Ranged, focus: AttackFocus.Player);
    var scaled = CampaignConfig.Scale(def, 2.0f);
    Assert.That(scaled.Row, Is.EqualTo(EnemyRow.Back));
    Assert.That(scaled.Range, Is.EqualTo(AttackRange.Ranged));
    Assert.That(scaled.Focus, Is.EqualTo(AttackFocus.Player));
    Assert.That(scaled.MaxHp, Is.GreaterThan(90), "缩放本身照常生效");
}
```

- [ ] **Step 8: `ConfigLoader` 接线**

`Data/ConfigLoader.cs` 的 `EnemyDto`(约 `:395-403`)追加:

```csharp
public string Row { get; set; }    // "Front" / "Back";缺省前排
public string Range { get; set; }  // "Melee" / "Ranged";缺省近战
public string Focus { get; set; }  // "Default" / "Player";缺省 Default
```

`ParseEnemies`(约 `:372-390`)里,`ability` 解析之后追加,并把三者传给构造函数:

```csharp
var row = EnemyRow.Front;
if (dto.Row != null && !Enum.TryParse(dto.Row, out row))
    throw new ConfigException($"敌人「{dto.Id}」的站位未知:{dto.Row}");
var range = AttackRange.Melee;
if (dto.Range != null && !Enum.TryParse(dto.Range, out range))
    throw new ConfigException($"敌人「{dto.Id}」的攻击距离未知:{dto.Range}");
var focus = AttackFocus.Default;
if (dto.Focus != null && !Enum.TryParse(dto.Focus, out focus))
    throw new ConfigException($"敌人「{dto.Id}」的目标偏好未知:{dto.Focus}");

enemyDefs[dto.Id] = new EnemyDef(dto.Id, element, dto.MaxHp, dto.Attack, ability, phases,
    dto.Defense, speed: 0, row: row, range: range, focus: focus);
```

**抛异常而不是静默回落**:静默忽略正是 `EnemyDef.Speed` 那条注释里记着的坑——配了 `"speed": 200` 会被无声丢掉。新字段不重蹈。

- [ ] **Step 9: 跑测试**

```bash
cd tools/coretests && /Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet test --nologo -v q
```

预期:全绿。所有既有怪都是默认 `Front`/`Melee`/`Default`,没有任何行为变化。

- [ ] **Step 10: 提交**

```bash
git add Brushblade/Assets/_Project/Core/ Brushblade/Assets/_Project/Data/ConfigLoader.cs Brushblade/Assets/_Project/Tests/EnemyRowTests.cs
git commit -m "feat(core): 敌方排位数据落地 —— Row/Range/Focus 三个正交字段

每排上限 3,开场按偏好分配、溢出改判到另一排;实际站位存在 EnemyState
并进快照(同 Id 的两只怪可能站不同排,只存 Def 会在读档时被合并)。
分裂克隆继承母体排位。CampaignConfig.Scale 顺带补上一直漏透传的 speed。
本次不改任何定向行为。

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---
### Task 5: `Core/Targeting.cs` 纯函数

**本任务只写函数与测试,不接进引擎。** 接线是 Task 6 / 7。

**Files:**
- Create: `Brushblade/Assets/_Project/Core/Targeting.cs`
- Create: `Brushblade/Assets/_Project/Tests/TargetingTests.cs`
- Modify: `Brushblade/Assets/_Project/Core/EnemyDef.cs`(`SummonState` 主构造函数 `internal` → `public`)

**Interfaces:**
- Consumes: Task 4 的 `EnemyRow` / `AttackRange` / `AttackFocus`、`EnemyState.Row`;Task 1 的定长槽位语义
- Produces: `Targeting.PlayerTarget`(`const int = -1`)
- Produces: `Targeting.PickAllyTarget(AttackRange range, AttackFocus focus, IReadOnlyList<SummonState> summons, int frontRow, GameRandom random)` → 槽位 `0..5` 或 `PlayerTarget`
- Produces: `Targeting.FrontmostSummon(IReadOnlyList<SummonState> summons, int frontRow)` → 槽位或 `-1`
- Produces: `Targeting.PickEnemyTargetForSummon(IReadOnlyList<EnemyState> enemies, bool ranged)` → 敌人下标或 `-1`
- Produces: `Targeting.CanPlayerHit(IReadOnlyList<EnemyState> enemies, int enemyIndex, bool ignoresRow)` → `bool`

> ### 🔒 硬约束:`pool.Count == 1` 的短路分支不许删
>
> `BattleEngine.RollCrit` 的文档注释立了一条明规矩:
>
> > _random 的既有消费方只有回合掉字、`AttackHits`、`EnemyState` 构造时的 Boss 阈值浮动 —— 无条件摇会平移整条流,让所有依赖种子的既有测试全红。**不得新增第四个无条件消费方。**
>
> `PickAllyTarget` 正是第四个消费方。它之所以合规,**全靠 `pool.Count == 1 ? pool[0] : pool[random.Next(...)]` 这一句短路** —— 与 `RollCrit` / `AttackHits` 两端短路是同一手法。
>
> 没有后排召唤物的战斗(= 绝大多数既有测试)因此一个随机数都不消耗,随机流与改前逐位相同。
>
> **把它「简化」成无条件 `pool[random.Next(pool.Count)]` 会让上千条带种子的既有测试全红**,而且红的方式毫无规律(伤害、掉字、闪避全变),极难定位。`SingleCandidate_ConsumesNoRandomness` 那条测试就是钉这一句的,不许改也不许删。

**先做一件事:把 `SummonState` 的主构造函数从 `internal` 放开成 `public`。**

`Core/EnemyDef.cs:135` 的 `internal SummonState(string summonChar, Element element, int hp, int attack, SummonPassive passive = null)` 跨程序集不可见,而 Tests 是独立程序集——本任务的纯函数测试需要手工摆出各种阵型。放开是合理的:`SummonState` 是个平凡的领域值对象(字/属性/血/攻/被动),`BattleEngine.Summons` 本来就把实例交给外部了,构造函数私有只是历史包袱。

**只放开这一个构造函数**。`Hp` 的 `internal set` **不要动**——那是对活体状态的修改,必须留给引擎。测试要造尸体就 `hp: 0` 构造(`Alive => Hp > 0`),要造活的就给正数。断点存档用的那个私有构造函数也不动。

**不要**引入 `[InternalsVisibleTo]`:那会绕过 Tests asmdef 的 `overrideReferences` 约束,是本项目明令的「工装绿 ≠ 编辑器绿」来源之一。

- [ ] **Step 1: 写失败测试**

新建 `Brushblade/Assets/_Project/Tests/TargetingTests.cs`。本任务只覆盖**我方阵型**那一侧(纯数据可手工摆);敌方那一侧涉及 `EnemyState` 的存活状态,走引擎更自然,放在 Task 7。

```csharp
using System.Collections.Generic;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>定向裁定(2026-08-20,spec §4.1)。纯函数,不碰引擎状态。</summary>
    public class TargetingTests
    {
        private const int FrontRow = 3;

        /// <summary>摆一个 6 槽阵:aliveSlots 里的槽放一只满血召唤物,其余留空。</summary>
        private static SummonState[] Line(params int[] aliveSlots)
        {
            var slots = new SummonState[6];
            foreach (int s in aliveSlots)
                slots[s] = new SummonState($"木{s}", Element.Wood, 100, 10);
            return slots;
        }

        [Test]
        public void Melee_HitsFrontmostFrontRowSummon()
        {
            Assert.That(Targeting.PickAllyTarget(AttackRange.Melee, AttackFocus.Default,
                Line(1, 2, 4), FrontRow, new GameRandom(1)), Is.EqualTo(1),
                "前排里槽序最小的那只");
        }

        [Test]
        public void Melee_IgnoresCorpsesInFrontRow()
        {
            var line = Line(2);
            line[0] = new SummonState("尸", Element.Wood, 0, 0); // Hp 0 = 尸体,占槽但不挡刀
            Assert.That(Targeting.PickAllyTarget(AttackRange.Melee, AttackFocus.Default,
                line, FrontRow, new GameRandom(1)), Is.EqualTo(2));
        }

        [Test]
        public void Melee_WithEmptyFront_PicksAmongBackRowAndPlayer()
        {
            // 后排站 4、5 两只 + 玩家 = 三个候选,均匀随机
            var seen = new HashSet<int>();
            var random = new GameRandom(7);
            for (int i = 0; i < 200; i++)
                seen.Add(Targeting.PickAllyTarget(AttackRange.Melee, AttackFocus.Default,
                    Line(4, 5), FrontRow, random));
            Assert.That(seen.Count, Is.EqualTo(3), "三个候选都摇得到,一个不多");
            Assert.That(seen.Contains(4), Is.True);
            Assert.That(seen.Contains(5), Is.True);
            Assert.That(seen.Contains(Targeting.PlayerTarget), Is.True);
        }

        [Test]
        public void Melee_WithNothingLeft_HitsPlayer()
        {
            Assert.That(Targeting.PickAllyTarget(AttackRange.Melee, AttackFocus.Default,
                Line(), FrontRow, new GameRandom(1)), Is.EqualTo(Targeting.PlayerTarget));
        }

        [Test]
        public void MeleeFocusPlayer_IsStillBlockedByFrontRow()
        {
            Assert.That(Targeting.PickAllyTarget(AttackRange.Melee, AttackFocus.Player,
                Line(0, 4), FrontRow, new GameRandom(1)), Is.EqualTo(0), "前排还在就拦得住");
        }

        [Test]
        public void MeleeFocusPlayer_WithEmptyFront_AlwaysHitsPlayer()
        {
            var random = new GameRandom(3);
            for (int i = 0; i < 50; i++)
                Assert.That(Targeting.PickAllyTarget(AttackRange.Melee, AttackFocus.Player,
                    Line(4, 5), FrontRow, random), Is.EqualTo(Targeting.PlayerTarget),
                    "后排还有人也不管,死盯玩家");
        }

        [Test]
        public void Ranged_IgnoresFrontRow()
        {
            var seen = new HashSet<int>();
            var random = new GameRandom(11);
            for (int i = 0; i < 200; i++)
                seen.Add(Targeting.PickAllyTarget(AttackRange.Ranged, AttackFocus.Default,
                    Line(0, 1, 2, 5), FrontRow, random));
            Assert.That(seen.Count, Is.EqualTo(2), "前排三只全被跳过");
            Assert.That(seen.Contains(5), Is.True);
            Assert.That(seen.Contains(Targeting.PlayerTarget), Is.True);
        }

        [Test]
        public void RangedFocusPlayer_AlwaysHitsPlayer()
        {
            Assert.That(Targeting.PickAllyTarget(AttackRange.Ranged, AttackFocus.Player,
                Line(0, 1, 4), FrontRow, new GameRandom(1)), Is.EqualTo(Targeting.PlayerTarget));
        }

        [Test]
        public void SingleCandidate_ConsumesNoRandomness()
        {
            // 关键性质:候选只有玩家一个时不摇随机数 —— 上千条带种子的既有测试
            // 才不会因为接线而整体位移
            var a = new GameRandom(42);
            var b = new GameRandom(42);
            Targeting.PickAllyTarget(AttackRange.Melee, AttackFocus.Default, Line(), FrontRow, a);
            Assert.That(a.Next(1000), Is.EqualTo(b.Next(1000)), "这次裁定一个随机数都没消耗");
        }

        [Test]
        public void FrontmostSummon_PrefersFrontRowThenBack()
        {
            Assert.That(Targeting.FrontmostSummon(Line(2, 3), FrontRow), Is.EqualTo(2));
            Assert.That(Targeting.FrontmostSummon(Line(4, 5), FrontRow), Is.EqualTo(4), "前排空则取后排");
            Assert.That(Targeting.FrontmostSummon(Line(), FrontRow), Is.EqualTo(-1));
        }
    }
}
```

`Is.EquivalentTo` 刻意没用——`seen.Count` + 逐个 `Contains` 在 Unity 版 NUnit 上一定可用,而集合断言 API 的可用性是本项目踩过的坑。

- [ ] **Step 2: 跑测试确认它失败**

```bash
cd tools/coretests && /Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet test --nologo -v q --filter "FullyQualifiedName~TargetingTests"
```

预期:编译失败(`Targeting` 不存在;`SummonState` 构造函数不可访问)。

- [ ] **Step 3: 写 `Targeting.cs`**

新建 `Brushblade/Assets/_Project/Core/Targeting.cs`:

```csharp
using System.Collections.Generic;

namespace Brushblade.Core
{
    /// <summary>「打谁」的唯一裁定处(2026-08-20,spec §4)。纯函数:吃列表与 RNG,返回下标,
    /// 不持有也不修改任何引擎状态。引擎与表现层都只调它,不各自判断——排位规则一旦分散,
    /// 「玩家看到能点」与「引擎认为能打」就会失配。
    ///
    /// 单独成文件而不是塞进 BattleEngine:那个文件已经 2400 行,而这套规则本身值得独立测试。</summary>
    public static class Targeting
    {
        /// <summary>PickAllyTarget 的返回值:打玩家本人,而不是某个召唤物槽位。</summary>
        public const int PlayerTarget = -1;

        /// <summary>敌人选我方目标。返回召唤物槽位,或 <see cref="PlayerTarget"/>。
        ///
        /// 均匀随机的口径(spec §4.1):把**全部存活后排召唤物与玩家**放进同一个候选池抽一个,
        /// 不是先五五开决定「打后排还是打玩家」。后排站 2 只时玩家挨打概率是 1/3——
        /// 站位越厚玩家越安全。
        ///
        /// ⚠ 候选只有一个时**不摇随机数**。这不是省事,是刻意的:绝大多数既有战斗
        /// (没有后排召唤物)因此完全不消耗随机数,随机流与改前逐位相同,
        /// 上千条带种子的既有测试才不会整体位移。</summary>
        public static int PickAllyTarget(AttackRange range, AttackFocus focus,
            IReadOnlyList<SummonState> summons, int frontRow, GameRandom random)
        {
            if (range == AttackRange.Melee)
            {
                int blocker = FirstAliveSlot(summons, 0, frontRow);
                if (blocker >= 0) return blocker;   // 被前排拦下,后面的规则一概不看
            }

            if (focus == AttackFocus.Player) return PlayerTarget;

            var pool = new List<int>();
            for (int s = frontRow; s < summons.Count; s++)
                if (summons[s] != null && summons[s].Alive) pool.Add(s);
            pool.Add(PlayerTarget);
            return pool.Count == 1 ? pool[0] : pool[random.Next(pool.Count)];
        }

        /// <summary>「最前召唤物」(Boss 贯穿 / 吞噬):前排槽序最小的存活者;前排全空则取后排。
        /// 全空返回 −1。
        ///
        /// 槽位是 0..5 且前排恰是低位段,所以本函数与「从 0 扫到末尾取第一个存活」等价——
        /// 存在的意义是把这条口径写成显式契约,而不是依赖槽位编号的巧合。</summary>
        public static int FrontmostSummon(IReadOnlyList<SummonState> summons, int frontRow)
        {
            int front = FirstAliveSlot(summons, 0, frontRow);
            return front >= 0 ? front : FirstAliveSlot(summons, frontRow, summons.Count);
        }

        /// <summary>召唤物出手选敌。近战打敌方前排(全清则打全场序最靠前的存活者);
        /// 远程优先打后排(后排空了才按近战规则来)。无敌可打返回 −1。
        ///
        /// 排位不影响召唤物**自己**能不能出手:站后排的近战照常攻击(用户 2026-08-20 拍板)。</summary>
        public static int PickEnemyTargetForSummon(IReadOnlyList<EnemyState> enemies, bool ranged)
        {
            if (ranged)
            {
                int back = FirstAliveInRow(enemies, EnemyRow.Back);
                if (back >= 0) return back;
            }
            int front = FirstAliveInRow(enemies, EnemyRow.Front);
            if (front >= 0) return front;
            return FirstAliveInRow(enemies, EnemyRow.Back);
        }

        /// <summary>玩家的**单体直接伤害**能不能打这只敌人(spec §4.2)。
        /// ignoresRow = 该字标了偷袭(刺)。控制类、AOE 一律不调本函数——它们不受排位限制。
        ///
        /// 「前排从未有过」与「前排已被清空」同等对待:一场若全是后排怪,玩家直接全场可点。</summary>
        public static bool CanPlayerHit(IReadOnlyList<EnemyState> enemies, int enemyIndex, bool ignoresRow)
        {
            if (enemyIndex < 0 || enemyIndex >= enemies.Count || !enemies[enemyIndex].Alive) return false;
            if (ignoresRow || enemies[enemyIndex].Row == EnemyRow.Front) return true;
            return FirstAliveInRow(enemies, EnemyRow.Front) < 0;
        }

        private static int FirstAliveSlot(IReadOnlyList<SummonState> summons, int from, int toExclusive)
        {
            for (int s = from; s < toExclusive && s < summons.Count; s++)
                if (summons[s] != null && summons[s].Alive) return s;
            return -1;
        }

        private static int FirstAliveInRow(IReadOnlyList<EnemyState> enemies, EnemyRow row)
        {
            for (int i = 0; i < enemies.Count; i++)
                if (enemies[i].Alive && enemies[i].Row == row) return i;
            return -1;
        }
    }
}
```

- [ ] **Step 4: 跑全套测试**

```bash
cd tools/coretests && /Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet test --nologo -v q
```

预期:`TargetingTests` 全绿,其余全绿(还没接线,行为零变化)。

- [ ] **Step 5: 提交**

```bash
git add Brushblade/Assets/_Project/Core/Targeting.cs Brushblade/Assets/_Project/Core/EnemyDef.cs Brushblade/Assets/_Project/Tests/TargetingTests.cs
git commit -m "feat(core): 新增 Targeting —— 前后排定向的唯一裁定处

纯函数,尚未接进引擎。候选只有一个时不摇随机数:绝大多数既有战斗
因此完全不消耗随机数,随机流与改前逐位相同。
SummonState 主构造函数放开成 public,供测试手工摆阵型。

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---
### Task 6: 接线 —— 敌人打我方

**Files:**
- Modify: `Brushblade/Assets/_Project/Core/BattleEngine.cs`(`ActOneEnemy` / Boss `Pierce` / Boss `Devour`)
- Test: `Brushblade/Assets/_Project/Tests/EnemyRowTests.cs`(追加)

**Interfaces:**
- Consumes: Task 5 的 `Targeting.PickAllyTarget` / `Targeting.FrontmostSummon` / `Targeting.PlayerTarget`;Task 3 的 `Cast(..., summonSlots)`
- Produces: 无新公开 API

- [ ] **Step 1: 写失败测试**

追加到 `EnemyRowTests.cs`。先补两个夹具(放在该类已有的 `MakeEngine` / `Mob` 旁边):

```csharp
/// <summary>召唤字「梅」:1 只 200 血、攻 0 的召唤物。攻 0 是为了让它绝不反击,
/// 敌人血量因此恒定,断言只需要盯玩家与召唤物的血。</summary>
private static RecipeGraph SummonGraph() => new(new[]
{
    new CharDef("梅", Element.Wood,
        effects: new[] { new EffectDef(EffectKind.Summon, 200,
            summonCount: 1, summonAttack: 0, summonChar: "梅") }),
});

/// <summary>「梅」放部件池里直出(叶子字免配方),敌人由调用方给。</summary>
private static BattleEngine SummonEngine(params EnemyDef[] enemies) =>
    new(SummonGraph(), new BattleConfig { PlayerMaxHp = MetaRules.MaxHpFor(1) },
        new string[0], new[] { "梅", "梅", "梅", "梅" }, enemies, seed: 1);
```

再写四条:

```csharp
[Test]
public void MeleeEnemy_IsBlockedByFrontRow()
{
    var engine = SummonEngine(new EnemyDef("错字鬼", Element.Wood, 500, 40));
    engine.Cast("梅", summonSlots: new[] { 1 });
    int playerBefore = engine.PlayerHp;
    int summonBefore = engine.Summons[1].Hp;
    engine.EndTurn();
    Assert.That(engine.Summons[1].Hp, Is.LessThan(summonBefore), "前排替玩家挨了这一下");
    Assert.That(engine.PlayerHp, Is.EqualTo(playerBefore), "玩家一滴不掉");
}

[Test]
public void RangedEnemy_SkipsFrontRow()
{
    // 后排没人 → 远程的候选池只剩玩家 → 必打玩家(确定性,不摇随机)
    var sniper = new EnemyDef("墨溅", Element.Water, 500, 40,
        row: EnemyRow.Back, range: AttackRange.Ranged);
    var engine = SummonEngine(sniper);
    engine.Cast("梅", summonSlots: new[] { 0 });
    int playerBefore = engine.PlayerHp;
    int summonBefore = engine.Summons[0].Hp;
    engine.EndTurn();
    Assert.That(engine.Summons[0].Hp, Is.EqualTo(summonBefore), "前排被整个跳过");
    Assert.That(engine.PlayerHp, Is.LessThan(playerBefore));
}

[Test]
public void MeleeAssassin_DivesForPlayer_WhenFrontIsEmpty()
{
    var assassin = new EnemyDef("败笔", Element.Fire, 500, 40, focus: AttackFocus.Player);
    var engine = SummonEngine(assassin);
    engine.Cast("梅", summonSlots: new[] { 4 });   // 只站后排,前排空
    int playerBefore = engine.PlayerHp;
    int summonBefore = engine.Summons[4].Hp;
    engine.EndTurn();
    Assert.That(engine.Summons[4].Hp, Is.EqualTo(summonBefore), "后排还有人也不管");
    Assert.That(engine.PlayerHp, Is.LessThan(playerBefore));
}

[Test]
public void MeleeAssassin_IsStillBlockedByFrontRow()
{
    var assassin = new EnemyDef("败笔", Element.Fire, 500, 40, focus: AttackFocus.Player);
    var engine = SummonEngine(assassin);
    engine.Cast("梅", summonSlots: new[] { 0 });
    int playerBefore = engine.PlayerHp;
    engine.EndTurn();
    Assert.That(engine.PlayerHp, Is.EqualTo(playerBefore), "刺客也得先过前排这一关");
    Assert.That(engine.Summons[0].Hp, Is.LessThan(200));
}
```

⚠ 若一回合内敌人不止出手一次、或召唤物满格计量器导致节拍与预期不符,**照 `AtbTimingTests.cs` 里的写法调**——那里已经有一套处理行动条节拍的模式。断言写成 `Is.LessThan` / `Is.EqualTo` 而不是精确差值,正是为了不被节拍细节绑死。

- [ ] **Step 2: 跑测试确认它失败**

```bash
cd tools/coretests && /Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet test --nologo -v q --filter "FullyQualifiedName~EnemyRowTests"
```

预期:`RangedEnemy_SkipsFrontRow` 与 `MeleeAssassin_DivesForPlayer_WhenFrontIsEmpty` FAIL(引擎仍走 `FirstAliveSummonIndex`,远程与刺客行为不生效);另两条本来就该绿。

- [ ] **Step 3: 改 `ActOneEnemy` 的选目标**

`Core/BattleEngine.cs:1177` 起。改前是:

```csharp
int damage = enemy.Attack;
int tankIdx = FirstAliveSummonIndex(); // 召唤物顶前排:整次攻击由首个存活召唤物承受(不溢出)
bool hit;
if (tankIdx >= 0)
    hit = DamageSummon(enemyIndex, tankIdx, damage, enemy.Element);
else
    hit = DamagePlayerDirect(enemyIndex, damage);
```

改后:

```csharp
int damage = enemy.Attack;
// 目标裁定(2026-08-20):近战被我方前排拦下;前排清空后在「后排 ∪ 玩家」里均匀随机;
// 远程无视前排;Focus.Player 的够得着玩家时死盯玩家。规则全在 Targeting,这里只执行。
int tankIdx = Targeting.PickAllyTarget(enemy.Def.Range, enemy.Def.Focus,
    _summons, FrontRowSize, _random);
bool hit;
if (tankIdx != Targeting.PlayerTarget)
    hit = DamageSummon(enemyIndex, tankIdx, damage, enemy.Element);
else
    hit = DamagePlayerDirect(enemyIndex, damage);
```

⚠ 判据从 `tankIdx >= 0` 换成 `tankIdx != Targeting.PlayerTarget`。两者数值上等价(`PlayerTarget` 就是 −1),写成后者是为了让「−1 是玩家」这层含义显式,而不是靠「负数即无效」的巧合。

- [ ] **Step 4: 给 Boss 的「最前召唤物」换成显式契约**

`Core/BattleEngine.cs:2311`(`Pierce`)与 `:2344`(`Devour`),把 `FirstAliveSummonIndex()` 换成:

```csharp
int front = Targeting.FrontmostSummon(_summons, FrontRowSize);
```

**这一处是零行为变化的**:槽位 0..5 且前排恰是低位段,`FrontmostSummon` 与「从 0 扫到末尾取第一个存活」逐位等价。换它纯粹是把口径写成显式契约,免得日后有人改了前排格数就悄悄失配。

**不为它新增测试**:语义已由 Task 5 的 `FrontmostSummon_PrefersFrontRowThenBack` 钉死,接线正确性由既有的 `BossSkillTests` 保持绿来证明。为一处零行为变化的替换再造一套 Boss 夹具是浪费。

- [ ] **Step 5: 跑全套测试**

```bash
cd tools/coretests && /Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet test --nologo -v q
```

预期:全绿。若有既有用例变红,先判断是哪一类:

- **红在「有 ≥4 只召唤物且前排被清空」的场景**:预期内。旧行为是确定性地打槽 3,新行为是在「后排 ∪ 玩家」里随机——按新口径改断言。
- **红在其他场景**:不是预期内的,回去查。特别检查随机流有没有被意外消耗(`Targeting.PickAllyTarget` 在候选只有一个时必须不摇)。

- [ ] **Step 6: 提交**

```bash
git add Brushblade/Assets/_Project/Core/BattleEngine.cs Brushblade/Assets/_Project/Tests/EnemyRowTests.cs
git commit -m "feat(core): 敌人选目标走排位裁定 —— 远程越过前排,刺客死盯玩家

ActOneEnemy 的 FirstAliveSummonIndex 换成 Targeting.PickAllyTarget。
Boss 贯穿/吞噬的「最前召唤物」换成 Targeting.FrontmostSummon:零行为
变化,只是把口径写成显式契约。

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---
### Task 7: 接线 —— 我方打敌方

**Files:**
- Modify: `Brushblade/Assets/_Project/Core/EffectDef.cs`(`CanStrikeBackline`)
- Modify: `Brushblade/Assets/_Project/Core/SummonPassive.cs`(`Ranged`)
- Modify: `Brushblade/Assets/_Project/Core/BattleEngine.cs`(`Cast` 的目标校验、`RestrictedToFrontRow`、`CanTarget`、`StrikeOnceWithSummon`)
- Test: `Brushblade/Assets/_Project/Tests/TargetingTests.cs`(追加引擎级用例)

**Interfaces:**
- Consumes: Task 5 的 `Targeting.CanPlayerHit` / `Targeting.PickEnemyTargetForSummon`
- Produces: `EffectDef.CanStrikeBackline`(`bool`,构造函数最后一个可选参数 `canStrikeBackline = false`)
- Produces: `SummonPassive.Ranged`(`bool { get; set; }`,进 `Clone()`)
- Produces: `BattleEngine.RestrictedToFrontRow(CharDef def, bool attackMode = false)`(`public static bool`)
- Produces: `BattleEngine.CanTarget(CharDef def, int enemyIndex, bool attackMode = false)`(`public bool`,表现层置灰用)

- [ ] **Step 1: 写失败测试**

追加到 `TargetingTests.cs`。夹具:三只怪(两前一后),敌人攻击力 0 —— 它们永不还手,`EndTurn` 可以随便推,断言不会被敌方伤害干扰。全部用 `Element.Heart`(心中立,生克恒 1.0x),伤害数字因此可以直算。

```csharp
/// <summary>四张叶子字直出:剑=纯直伤、刺=带偷袭的直伤、藤=纯冻结、湮=直伤+驱散(混合字)。</summary>
private static RecipeGraph DamageGraph() => new(new[]
{
    new CharDef("剑", Element.Heart, effects: new[] {
        new EffectDef(EffectKind.DamageSingle, 50) }),
    new CharDef("刺", Element.Heart, effects: new[] {
        new EffectDef(EffectKind.DamageSingle, 50, canStrikeBackline: true) }),
    new CharDef("藤", Element.Heart, effects: new[] {
        new EffectDef(EffectKind.Freeze, 2) }),
    new CharDef("湮", Element.Heart, effects: new[] {
        new EffectDef(EffectKind.DamageSingle, 20), new EffectDef(EffectKind.Dispel, 1) }),
});

/// <summary>前甲(厚)/ 前乙(40 血,一剑即死)/ 后手。敌人攻 0,不会回手。</summary>
private static BattleEngine Trio() => new(DamageGraph(),
    new BattleConfig { PlayerMaxHp = MetaRules.MaxHpFor(1) },
    new string[0], new[] { "剑", "剑", "刺", "藤", "湮" },
    new[]
    {
        new EnemyDef("前甲", Element.Heart, 400, 0),
        new EnemyDef("前乙", Element.Heart, 40, 0),
        new EnemyDef("后手", Element.Heart, 400, 0, row: EnemyRow.Back),
    }, seed: 1);

[Test]
public void Cast_SingleDamage_RejectsBackRow_WhileTwoFrontAlive()
{
    var engine = Trio();
    int ap = engine.Ap;
    int backHp = engine.Enemies[2].Hp;
    Assert.That(engine.Cast("剑", 2), Is.EqualTo(BattleError.InvalidTarget));
    Assert.That(engine.Enemies[2].Hp, Is.EqualTo(backHp), "被拒的这次一点伤害也不该落下");
    Assert.That(engine.Ap, Is.EqualTo(ap), "AP 不扣");
}

[Test]
public void Cast_ControlEffect_ReachesBackRow_EvenWithFrontAlive()
{
    var engine = Trio();
    Assert.That(engine.Cast("藤", 2), Is.EqualTo(BattleError.None), "控制类不受排位限制");
    Assert.That(engine.Enemies[2].Statuses.TotalTurns(StatusKind.Freeze), Is.GreaterThan(0));
}

[Test]
public void Cast_BackstabDamage_ReachesBackRow()
{
    var engine = Trio();
    Assert.That(engine.Cast("刺", 2), Is.EqualTo(BattleError.None));
    Assert.That(engine.Enemies[2].Hp, Is.LessThan(400), "偷袭字够得着后排");
}

[Test]
public void Cast_MixedCard_TakesTheStrictestRule()
{
    var engine = Trio();
    Assert.That(engine.Cast("湮", 2), Is.EqualTo(BattleError.InvalidTarget),
        "含单体直伤就受限,哪怕它还带一条驱散");
}

[Test]
public void Cast_AutoLocks_WhenExactlyOneLegalTargetRemains()
{
    var engine = Trio();
    engine.Cast("剑", 1);                       // 50 伤打死 40 血的前乙
    Assert.That(engine.Enemies[1].Alive, Is.False);
    // 现在存活的有两只(前甲、后手),但**合法的**只有前甲一只 → 不指定目标应自动锁它
    Assert.That(engine.Cast("剑"), Is.EqualTo(BattleError.None));
    Assert.That(engine.Enemies[0].Hp, Is.EqualTo(350), "自动锁的是前甲");
    Assert.That(engine.Enemies[2].Hp, Is.EqualTo(400), "后手没被碰到");
}
```

⚠ 两处需要按仓库现状核对后再定稿:

1. `Statuses.TotalTurns(StatusKind.Freeze)` —— `StatusBag` 上实际叫什么名字,先 `grep -n "public " Brushblade/Assets/_Project/Core/StatusEffect.cs` 看一眼;若只有 `TotalMagnitude`,冻结的断言改成「该敌人下一回合没有行动」或直接读 `Statuses.All` 里有没有 `Kind == StatusKind.Freeze` 的条目(注意 `Freeze` 刻意不赋 `Magnitude`)。
2. `Cast_AutoLocks_...` 一回合要出两张字,共 2 点 AP。若默认 AP 不够,在两次 `Cast` 之间插一次 `engine.EndTurn()`——敌人攻 0,推一回合不会打乱任何断言。

再追加两条召唤物出手的用例:

```csharp
private static RecipeGraph SummonRangeGraph() => new(new[]
{
    new CharDef("松", Element.Heart, effects: new[] {
        new EffectDef(EffectKind.Summon, 200, summonCount: 1, summonAttack: 30, summonChar: "松") }),
    new CharDef("灶", Element.Heart, effects: new[] {
        new EffectDef(EffectKind.Summon, 200, summonCount: 1, summonAttack: 30, summonChar: "灶",
            passive: new SummonPassive { Ranged = true }) }),
});

/// <summary>一前一后两只怪(攻 0),字库里放指定的召唤字。</summary>
private static BattleEngine SummonRangeDuel(string summonChar) => new(
    SummonRangeGraph(), new BattleConfig { PlayerMaxHp = MetaRules.MaxHpFor(1) },
    new string[0], new[] { summonChar, summonChar },
    new[]
    {
        new EnemyDef("前卫", Element.Heart, 400, 0),
        new EnemyDef("后手", Element.Heart, 400, 0, row: EnemyRow.Back),
    }, seed: 1);

[Test]
public void MeleeSummon_HitsFrontRow()
{
    var engine = SummonRangeDuel("松");
    engine.Cast("松", summonSlots: new[] { 0 });
    engine.EndTurn();   // 新召唤物上场即满格,这一拍就出手
    Assert.That(engine.Enemies[0].Hp, Is.LessThan(400), "近战打前排");
    Assert.That(engine.Enemies[1].Hp, Is.EqualTo(400), "后排一滴不掉");
}

[Test]
public void RangedSummon_PrefersBackRow()
{
    var engine = SummonRangeDuel("灶");
    engine.Cast("灶", summonSlots: new[] { 0 });
    engine.EndTurn();
    Assert.That(engine.Enemies[1].Hp, Is.LessThan(400), "远程越过前排点后排");
    Assert.That(engine.Enemies[0].Hp, Is.EqualTo(400));
}
```

- [ ] **Step 2: 跑测试确认它失败**

```bash
cd tools/coretests && /Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet test --nologo -v q --filter "FullyQualifiedName~TargetingTests"
```

预期:编译失败(`canStrikeBackline` 参数与 `SummonPassive.Ranged` 不存在)。

- [ ] **Step 3: 加两个数据字段**

`Core/EffectDef.cs`,`Pierce` 属性之后:

```csharp
/// <summary>偷袭(2026-08-20):这一发单体伤害无视敌方前排,可直接点后排。
/// 只对 <see cref="EffectKind.DamageSingle"/> 有意义——其余单体效果本来就不受排位限制。
/// 全字表眼下只有「刺」标了它(spec §12)。</summary>
public bool CanStrikeBackline { get; }
```

构造函数追加最后一个可选参数 `bool canStrikeBackline = false` 并赋值。

`Core/SummonPassive.cs`,`Dodge` 之后:

```csharp
/// <summary>远程(2026-08-20):出手时无视敌方前排,优先打后排。灶 / 烓 = true。
/// 与「站哪一槽」无关——排位只决定被不被够到,后排的近战召唤物照常打前排。</summary>
public bool Ranged { get; set; }
```

`Clone()` 里追加 `Ranged = Ranged,`。**漏掉 `Clone` 会让远程属性在存档往返后丢失**——`Clone` 是快照与实体解耦的唯一手段。

- [ ] **Step 4: 加两个判定 API**

`Core/BattleEngine.cs`,紧挨 `NeedsTarget`(`:772`)之后:

```csharp
/// <summary>本次出字是否受敌方前排阻挡(2026-08-20,spec §4.2)。
///
/// **只有 DamageSingle 受限**:控制、减益、灼烧、AOE 一律不受排位限制
/// ——「打不到后面,但够得着冻住、破甲、下毒」。
///
/// 混合字按最严的算:效果里只要含一条 DamageSingle 就受限(如湮 = 直伤 + 全体驱散)。
/// 但只要有任一条直伤标了偷袭,整张字就是偷袭字——偷袭是字的身份,不是单条效果的属性。</summary>
public static bool RestrictedToFrontRow(CharDef def, bool attackMode = false)
{
    bool hasDirectDamage = false;
    foreach (var effect in EffectsOf(def, attackMode))
    {
        if (effect.Kind != EffectKind.DamageSingle) continue;
        if (effect.CanStrikeBackline) return false;
        hasDirectDamage = true;
    }
    return hasDirectDamage;
}

/// <summary>这张字现在能不能点这只敌人(表现层据此置灰;引擎在 Cast 里用同一条判据)。</summary>
public bool CanTarget(CharDef def, int enemyIndex, bool attackMode = false)
{
    if (enemyIndex < 0 || enemyIndex >= _enemies.Count || !_enemies[enemyIndex].Alive) return false;
    if (!RestrictedToFrontRow(def, attackMode)) return true;
    return Targeting.CanPlayerHit(_enemies, enemyIndex, ignoresRow: false);
}
```

- [ ] **Step 5: `Cast` 的目标校验改按合法目标**

`Core/BattleEngine.cs:605-618`,整块换成:

```csharp
// 单体效果需要有效的存活目标;未指定或不合法时,**合法目标**恰好一个则自动锁定
// (3.8.3 单敌免选;2026-08-20 从「存活目标」改口径为「合法目标」——前排还剩一只时
//  点后排的字应当直接锁那一只,而不是弹一次没得选的选目标)
if (NeedsTarget(def, attackMode))
{
    bool restricted = RestrictedToFrontRow(def, attackMode);
    bool legal = targetIndex >= 0 && targetIndex < _enemies.Count && _enemies[targetIndex].Alive
        && (!restricted || Targeting.CanPlayerHit(_enemies, targetIndex, ignoresRow: false));
    if (!legal)
    {
        int sole = -1;
        for (int i = 0; i < _enemies.Count; i++)
        {
            if (!_enemies[i].Alive) continue;
            if (restricted && !Targeting.CanPlayerHit(_enemies, i, ignoresRow: false)) continue;
            if (sole >= 0) { sole = -1; break; } // 合法目标多于一个:交给 UI 去选
            sole = i;
        }
        if (sole < 0) return BattleError.InvalidTarget;
        targetIndex = sole;
    }
}
```

⚠ 这里**故意**在「玩家点了一个够不到的后排怪、而合法目标只剩一个」时静默改打那一个,而不是报错——它与既有的「点了个死人就改打唯一存活者」是同一条容错,口径一致。合法目标不止一个时才 `InvalidTarget`;表现层会把够不到的敌人置灰,这条正常玩不出来。

**不新增 `BattleError` 取值**:`InvalidTarget` 语义已经涵盖,加一个只服务于「表现层已经拦住了的情况」的错误码是投机。

- [ ] **Step 6: 召唤物出手改走裁定**

`Core/BattleEngine.cs:1126-1136`:

```csharp
private void StrikeOnceWithSummon(int summonIndex)
{
    var summon = _summons[summonIndex];
    if (summon == null) return;
    // 近战打敌方前排、远程优先打后排(2026-08-20)。全部敌人默认前排时,
    // 本行与改前的「从 0 扫到第一个存活」逐位等价 —— 既有战斗零行为变化。
    int target = Targeting.PickEnemyTargetForSummon(_enemies, summon.Passive?.Ranged ?? false);
    if (target < 0) return;
    _events.Add(new BattleEvent(BattleEventKind.SummonAttack, target, summon.Attack, summonIndex));
    if (summon.Attack > 0)
        DamageEnemy(target, summon.Attack, Array.Empty<Element>(), summon.Element);
    ApplySummonOnHit(summon, target);
}
```

- [ ] **Step 7: 跑全套测试**

```bash
cd tools/coretests && /Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet test --nologo -v q
```

预期:**全绿,一条都不该红**。现有 `enemies.json` 里所有怪都是默认前排,`CanPlayerHit` 对它们恒为 `true`,`PickEnemyTargetForSummon` 与改前逐位等价。若有红,说明接线改变了「全前排」场景下的行为——回去找。

```bash
cd tools/prescompile && /Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet build --nologo -v q -p:ProjectAsm=/Users/eugenewu/code/game/Brushblade/Library/ScriptAssemblies
```

- [ ] **Step 8: 提交**

```bash
git add Brushblade/Assets/_Project/Core/ Brushblade/Assets/_Project/Tests/TargetingTests.cs
git commit -m "feat(core): 我方定向走排位裁定 —— 只有单体直伤够不到后排

控制/减益/灼烧/AOE 一律不受排位限制;混合字含直伤即受限;标了偷袭的
直伤不受限。单敌免选的口径从「存活目标」改为「合法目标」。
召唤物近战打前排、远程优先打后排。

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---
### Task 8: 三只新怪与生成期约束

**Files:**
- Modify: `Brushblade/Assets/StreamingAssets/config/enemies.json`
- Modify: `Brushblade/Assets/_Project/Core/Endless.cs`(`BuildFloor`)
- Test: `Brushblade/Assets/_Project/Tests/EnemyRowTests.cs`(追加)

**Interfaces:**
- Consumes: Task 4 的 `EnemyDef.Row/Range/Focus` 与 `ConfigLoader` 接线

- [ ] **Step 1: 写失败测试**

追加到 `EnemyRowTests.cs`:

```csharp
[Test]
public void RealConfig_HasThreeRowAwareMobs()
{
    // 读真实 enemies.json(照 ConfigLoaderTests 里既有的加载写法),断言:
    //   墨溅 = Back / Ranged / Default
    //   悬针 = Back / Ranged / Player
    //   败笔 = Front / Melee / Player
    // ⚠ 定位仓库根只能用 TestContext.CurrentContext.TestDirectory
}

[Test]
public void BuildFloor_AlwaysStartsWithAFrontRowMob()
{
    // 连摇 200 层,每层第一只的 Def.Row 必须是 Front
    // ——不加这条会摇出全员后排的怪场,排位规则整场失效
}

[Test]
public void BuildFloor_NeverPutsMoreThanThreeInEitherRow()
{
    // 连摇 200 层,构造 BattleEngine 后每排存活数 <= 3
}
```

- [ ] **Step 2: 跑测试确认它失败**

```bash
cd tools/coretests && /Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet test --nologo -v q --filter "FullyQualifiedName~EnemyRowTests"
```

- [ ] **Step 3: 加三只新怪**

`Brushblade/Assets/StreamingAssets/config/enemies.json` 的 `enemies` 数组末尾追加:

```json
{ "id": "墨溅", "element": "Water", "maxHp": 100, "attack": 30, "row": "Back", "range": "Ranged" },
{ "id": "悬针", "element": "Metal", "maxHp": 90,  "attack": 45, "row": "Back", "range": "Ranged", "focus": "Player" },
{ "id": "败笔", "element": "Fire",  "maxHp": 150, "attack": 55, "focus": "Player" }
```

命名沿用现有「书写事故」族系(错字鬼 / 缺笔妖 / 通假字 / 生僻字 / 焦痕 / 灯花 / 墨渍)。数值对齐现有档位;两只后排怪血刻意薄——它们难被够到,清了前排或掏出刺之后就该能迅速收掉。**这批是初值,待 balance harness 仿真校准。**

现有 13 只怪**一个字段都不加**:缺省就是 `Front` / `Melee` / `Default`,正是它们该有的行为。

把三只新怪加进无尽的怪池。`enemies.json` 里 `chapters` 各段的 `enemyPool` / `stages.encounters` 具体结构以文件现状为准——**先读一遍 `EndlessConfig` 是怎么从这份配置里取 `band.EnemyPool` 的**(`Core/Endless.cs` + `Data/ConfigLoader.cs`),再决定往哪儿加。加进**中后段**的池,别放进第一段:新玩家还没有刺、也没学会前后排,开场就吃专射玩家的悬针会很挫败。

**形象缺失是可接受的**:`MobAssets.PrefixFor` 对未登记的怪返回 `null`,`BattleView` 回落到字牌圆格(`Ui.CircleGlyph`),与 19 只形象分批入库时的过渡态一致。不要为此新增占位图。

- [ ] **Step 4: `BuildFloor` 首位强制前排**

`Core/Endless.cs:154-172`。现有代码已有「辅助型每场最多 1 只,且不单独成场」的同型规则,照它的形状加一条:

```csharp
// 辅助型(Buff)每场最多 1 只,且不单独成场(2026-07-19)
var nonSupport = new List<EnemyDef>();
foreach (var enemy in band.EnemyPool)
    if (enemy.Ability != EnemyAbility.Buff)
        nonSupport.Add(enemy);

// 首位强制前排(2026-08-20):不加这条会摇出全员后排的怪场 —— 我方单体直伤
// 立刻全场可点,排位规则整场失效,后排怪也就失去了「够不到」这个身份。
var frontOpeners = new List<EnemyDef>();
foreach (var enemy in nonSupport)
    if (enemy.Row == EnemyRow.Front)
        frontOpeners.Add(enemy);

int count = 1 + Math.Min(5, (depth - 1) / 4);
bool hasSupport = false;
for (int i = 0; i < count; i++)
{
    List<EnemyDef> pool;
    if (i == 0 && frontOpeners.Count > 0) pool = frontOpeners;
    else if ((i == 0 || hasSupport) && nonSupport.Count > 0) pool = nonSupport;
    else pool = new List<EnemyDef>(band.EnemyPool);
    var pick = pool[random.Next(pool.Count)];
    if (pick.Ability == EnemyAbility.Buff) hasSupport = true;
    floor.Add(CampaignConfig.Scale(pick, scale));
}
```

⚠ 上面把原来的 `band.EnemyPool`(`IReadOnlyList`)包了一层 `new List<EnemyDef>(...)` 只为让三个分支类型一致;若 `band.EnemyPool` 本来就是 `List<EnemyDef>`,**去掉这层拷贝**——每层多分配一个 List 是白费。以文件现状为准。

⚠ **随机流会变**:多了一个子池就多了一次不同上界的 `random.Next`。`EndlessTests` 里钉死具体层内容的用例会红——按新结果更新,这是预期内的。**先确认红的是「层内容变了」而不是「层数量/结构错了」**。

- [ ] **Step 5: 跑测试**

```bash
cd tools/coretests && /Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet test --nologo -v q
```

- [ ] **Step 6: 提交**

```bash
git add Brushblade/Assets/StreamingAssets/config/enemies.json Brushblade/Assets/_Project/Core/Endless.cs Brushblade/Assets/_Project/Tests/EnemyRowTests.cs
git commit -m "feat(data): 三只排位怪入库 —— 墨溅/悬针/败笔

墨溅后排远程随机、悬针后排远程死盯玩家、败笔前排近战前排一清就扑玩家。
BuildFloor 首位强制从前排子池抽,否则会摇出全员后排的怪场让排位失效。
数值为初值,待 balance harness 校准。

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---
### Task 9: 管线与三张字卡

**Files:**
- Modify: `docs/design/字选型/技能机制详表.md`(刺 `:559` / 烓 `:432` / 灶 `:433`)
- Modify: `tools/pipeline/extract_values.py`
- Modify: `tools/pipeline/tests/test_export_chars.py`
- Modify: `Brushblade/Assets/_Project/Data/ConfigLoader.cs`(`EffectDto.Backline`)
- Regenerate: `Brushblade/Assets/StreamingAssets/config/chars.json`(**跑管线产出,禁止手改**)

**Interfaces:**
- Consumes: Task 7 的 `EffectDef.CanStrikeBackline` 与 `SummonPassive.Ranged`

- [ ] **Step 1: 写失败的管线测试**

追加到 `tools/pipeline/tests/test_export_chars.py`。**这条测试是必需的**:`extract_values` 的 token 表认不得的标记是**无声丢弃**的,漏接线会一路沉默到运行期。

```python
def test_backline_token_attaches_to_single_damage():
    assert _parse_effects("`DamageSingle 135`,`Pierce 15` + `Backline` + `Morale 1`", "金") == [
        {"kind": "DamageSingle", "value": 135, "pierce": 15, "backline": True},
        {"kind": "Morale", "value": 1}]


def test_backline_does_not_become_a_standalone_effect():
    """Backline 是伤害的修饰,不是效果。若它落成一条独立效果,
    EffectKind 里没有这个值,ConfigLoader 会在加载期直接抛 ConfigException。"""
    effects = _parse_effects("`DamageSingle 50` + `Backline`", "金")
    assert all(e["kind"] != "Backline" for e in effects)
    assert len(effects) == 1


def test_ranged_token_lands_in_summon_passive():
    assert _parse_effects("`Summon 1`(110 血/攻 20)+ `OnHitBurn 2` + `Ranged`", "灶") == [
        {"kind": "Summon", "value": 110, "count": 1, "attack": 20, "summonChar": "灶",
         "passive": {"onHitBurn": 2, "ranged": True}}]


def test_shipped_chars_json_carries_the_new_row_fields():
    """三张改造字在**出货的 chars.json 里**确实带上了新字段。

    上面几条喂的是手打字符串;这条读真实产物 —— token 表漏接线是无声的,
    只有真产物能证明「详表写了」与「游戏读得到」之间没有断点。"""
    shipped = json.loads(CHARS_JSON.read_text(encoding="utf-8"))
    by_id = {c["id"]: c for c in shipped["chars"]}

    ci = by_id["刺"]["effects"][0]
    assert ci["kind"] == "DamageSingle" and ci.get("backline") is True

    for char, attack in (("灶", 20), ("烓", 30)):
        summon = by_id[char]["effects"][0]
        assert summon["kind"] == "Summon"
        assert summon["attack"] == attack, f"{char} 的基础攻击"
        assert summon["passive"].get("ranged") is True, f"{char} 应为远程"
```

- [ ] **Step 2: 跑测试确认它失败**

```bash
ln -s /Users/eugenewu/code/game/tools/fonts/raw tools/fonts/raw 2>/dev/null; python3 -m pytest tools/pipeline/tests/test_export_chars.py -q
```

预期:四条新测试 FAIL。

- [ ] **Step 3: 改详表三行**

`docs/design/字选型/技能机制详表.md`:

```
:432  烓 的效果配置格
  改前  `Summon 1`(220 血/攻 0)+ `OnHitBurn 3` + `OnHitBurnAll`
  改后  `Summon 1`(220 血/攻 30)+ `OnHitBurn 3` + `OnHitBurnAll` + `Ranged`

:433  灶 的效果配置格
  改前  `Summon 1`(110 血/攻 0)+ `OnHitBurn 2`
  改后  `Summon 1`(110 血/攻 20)+ `OnHitBurn 2` + `Ranged`

:559  刺 的效果配置格
  改前  `DamageSingle 135`,`Pierce 15` + `Morale 1`
  改后  `DamageSingle 135`,`Pierce 15` + `Backline` + `Morale 1`
```

同时按详表既有的体例,在这三行的**状态列**追加带日期的说明:

- 烓 / 灶:`**2026-08-20**:转远程(无视敌方前排,优先打后排)并补基础攻 30 / 20 —— 同档紫 40~70、金 90,给到明显低于同档是因为它们各自还带 3 / 2 层灼烧`
- 刺:`**2026-08-20**:加偷袭 —— 全字表唯一能点敌方后排的直伤字`

顺带同步 `docs/design/字表功能解析.md` 里这三个字的行(它是人读的汇总表,不进管线,但放着不改会与详表打架)。

- [ ] **Step 4: 改 `extract_values.py`**

`_parse_effects` 的召唤分支里,紧挨 `OnHitBurnAll` 那两行之后:

```python
if "`OnHitBurnAll`" in config:      # 无数值的布尔标记(烓)
    passive["onHitBurnAll"] = True
if "`Ranged`" in config:            # 无数值的布尔标记(2026-08-20,灶/烓)
    passive["ranged"] = True
```

伤害分支里,紧挨 `DoubleVsBurning` 那两行之后:

```python
if kind.startswith("Damage") and "DoubleVsBurning" in config:
    effect["doubleVsBurning"] = True
# 偷袭(2026-08-20):无视敌方前排。只修饰单体直伤 —— 其余单体效果本就不受排位限制。
# ⚠ 绝不能进 VALUELESS_EFFECTS:那会让它落成一条 kind="Backline" 的独立效果,
#   而 EffectKind 里没有这个值,ConfigLoader 会在加载期直接抛 ConfigException
#   (与 PIERCE_TOKEN 头上那条注释同一个坑)。
if kind == "DamageSingle" and "`Backline`" in config:
    effect["backline"] = True
```

**召唤物攻击力零改动**——现有正则 `` `Summon (\d+)`\((\d+) 血/攻 (\d+)\) `` 原样吃下 0 → 20 / 30。

- [ ] **Step 5: 重生成 `chars.json`**

```bash
python3 tools/pipeline/export_chars.py
```

```bash
git diff --stat Brushblade/Assets/StreamingAssets/config/chars.json
```

预期:**只有刺 / 灶 / 烓 三个条目变**。若 diff 波及其他字,说明详表被顺手改了别的东西——回退重来。

- [ ] **Step 6: `ConfigLoader` 接 `backline`**

`Data/ConfigLoader.cs` 的 `EffectDto` 追加:

```csharp
public bool Backline { get; set; }   // 偷袭:该发单体直伤无视敌方前排(2026-08-20)
```

`ParseEffects`(约 `:481`)的 `new EffectDef(...)` 末尾追加 `effect.Backline`。

⚠ **`ranged` 不需要 `ConfigLoader` 改动**:`EffectDto.Passive` 的类型就是 Core 的 `SummonPassive`,Newtonsoft 直接反序列化到它的属性上,加了 `Ranged` 属性就自动通了。**不要**为了「对称」给 `EffectDto` 加一个 `Ranged` 字段——那是死代码。

- [ ] **Step 7: 跑全套**

```bash
ln -s /Users/eugenewu/code/game/tools/fonts/raw tools/fonts/raw 2>/dev/null; python3 -m pytest tools/pipeline/tests/ tools/fonts/tests/ -q
```

```bash
cd tools/coretests && /Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet test --nologo -v q
```

预期:pytest 全绿;coretests 里**涉及灶/烓 的用例会红**——它们的攻击力从 0 变成 20/30,`StrikeOnceWithSummon` 里 `if (summon.Attack > 0)` 那道闸第一次走通,伤害开始过五行生克(火系打木 ×1.5、打水 ×0.5)。**这是新口径不是回归**,按实际数值更新断言,并在测试注释里写明原因。

先定位受影响的用例:

```bash
grep -rln "灶\|烓" Brushblade/Assets/_Project/Tests/
```

- [ ] **Step 8: 提交**

```bash
git add docs/design/ tools/pipeline/ Brushblade/Assets/StreamingAssets/config/chars.json Brushblade/Assets/_Project/Data/ConfigLoader.cs Brushblade/Assets/_Project/Tests/
git commit -m "feat(data): 刺转偷袭、灶/烓 转远程并补基础攻

详表加两个无数值 token:Ranged 进召唤被动、Backline 修饰单体直伤。
Backline 刻意不进 VALUELESS_EFFECTS —— 落成独立效果会让 ConfigLoader
在加载期抛异常。灶/烓 攻 0 → 20/30 后首次走 DamageEnemy,伤害开始过
五行生克,相关用例按新口径更新。

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 10: 战斗界面四排布局

**Files:**
- Modify: `Brushblade/Assets/_Project/Presentation/BattleView.cs`(`BuildLayout` 一段 + `DrawSummons` + 敌人绘制)

**Interfaces:**
- Consumes: Task 3 的 `BattleEngine.FrontRow`、Task 4 的 `EnemyState.Row`
- Produces: `_enemyBackRow` / `_enemyFrontRow` / `_summonFrontRow` / `_summonBackRow` 四个 `Transform`(取代原来的 `_enemyRow` / `_summonRow`)

**背景**:现状纵向预算闭合不了。`BattleView.cs:451` 的注释明写「**这个区域闭合不了**」——我方召唤区 72px 一排,单格内容最坏 111px,当前就溢出 39px。解法是把拆合台从底部卡片(`0.012~0.230`,约 196px)移到右侧竖栏,底部让出的近 200px 喂给两侧各多出的一排。

- [ ] **Step 1: 挪拆合台到右侧竖栏**

`BattleView.cs:466-472`。拆合台卡片现在锚在 `(0.145, 0.012) ~ (0.92, 0.230)`,改成右侧竖条,并把内部的 `_suggestRow` / `_actionRow` 从横排改成竖排(`Ui.VStack`)。结束回合钮(`:490-493`,现锚在 `(0.86, 0.44) ~ (0.99, 0.56)`)移到该竖栏底部。

参考排版(用户 2026-08-20 提供):

```
┌ 相克 ┬────── 「字林」1~5层·战斗2   墨锭 · 回合 · 退出 ──────┬ 相生 ┐
│      │                                                      │      │
│ 配字表│   敌方后排  [ ][ ][ ]                                │ 拆合台│
│      │   敌方前排  [ ][ ][ ]                                │  林+水│
│      │  ─────────────────────                               │  火+火│
│      │   我方前排  [ ][ ][ ]                                │  炎+火│
│      │   我方后排  [ ][ ][ ]                                │      │
│      │        执笔者 HP / 行动条 / AP                        │      │
│      │        字库  [字][字][字]…            ▶+2            │ 结束 │
│      │        部件池 [部][部][部]…           ▶+2            │ 回合 │
└──────┴──────────────────────────────────────────────────────┴──────┘
```

- [ ] **Step 2: 拆成四个 section**

`BattleView.cs:450-462`,把两个 `MakeSection` 换成四个。纵向预算粗算(900px 基准):顶栏 60 + 敌方两排 2×110 + 我方两排 2×90 + 玩家条 70 + 字库 120 + 部件池 65 ≈ **795px**,余 105px 给间距与分隔线。

⚠ **精确比例必须自己算,不要照抄上面的粗算值**。`BattleView.cs:457-461` 有一串「内容最坏高度」的逐项加法,注释明确要求「**改动内容高度时请重算这串加法**」。按新的每排格数(6 → 3,格宽翻倍)重算一遍,把结果写进注释替换旧的那串。

**本次可以撤销一项旧妥协**:`:452-455` 的注释说「要真闭合只能把盾与被动移进详情弹窗,那是删信息,不在本次范围」。腾出空间后**不需要删信息了**——攻 / 盾 / 被动三项在格宽翻倍后横排放得下。保留它们,并把 `:451-455` 那段「闭合不了」的注释改写成新的预算说明。

- [ ] **Step 3: 按排分组绘制**

`DrawSummons`(`:891`)改成画两排、每排 3 格,**空槽也画**(虚框占位,布局不跳动):

```csharp
for (int i = 0; i < Battle.Summons.Count; i++)
{
    var row = i < Battle.FrontRow ? _summonFrontRow : _summonBackRow;
    var summon = Battle.Summons[i];
    if (summon == null) { DrawEmptySlot(row, i); continue; }
    // …既有的格子绘制原样搬过来,父节点从 _summonRow 换成 row…
}
```

敌人绘制(`:981` 起)按 `enemy.Row` 分到 `_enemyFrontRow` / `_enemyBackRow`。注意 `_enemyMobs` 是**按敌人下标**索引的(`:182`、`:216-217` 靠 `e.TargetIndex` 取),分排绘制时**下标必须仍与 `Battle.Enemies` 对齐**——不能按排重排列表。稳妥写法:先 `for (int i = 0; i < count; i++) _enemyMobs.Add(null);` 占位,再按下标写回。

- [ ] **Step 4: 前后排的视觉区分**

- 我方前排与敌方前排之间画一条分隔线。
- 后排格略缩到约 85%,让纵深读得出来。
- 后排为空时该排仍占位,布局不跳动。

- [ ] **Step 5: 编译并试玩**

```bash
cd tools/prescompile && /Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet build --nologo -v q -p:ProjectAsm=/Users/eugenewu/code/game/Brushblade/Library/ScriptAssemblies
```

预期:无 `error CS`。

Presentation 无自动化测试(项目口径),**这一步必须由用户在 Unity 里实际跑一场**:确认四排都画得出、空槽有占位、拆合台在右侧可用、结束回合钮点得到、横屏 1600×900 下不溢出。

- [ ] **Step 6: 提交**

```bash
git add Brushblade/Assets/_Project/Presentation/BattleView.cs
git commit -m "feat(ui): 战斗界面改四排布局 —— 拆合台移到右侧竖栏

敌方后排/前排 + 我方前排/后排各 3 格。拆合台从底部卡片移到右侧,让出的
近 200px 喂给新增的两排,顺带撤销了「把盾与被动移进详情弹窗」那项旧妥协
—— 格宽翻倍后横排放得下,不用删信息了。

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 11: 槽位选择面板与目标置灰

**Files:**
- Modify: `Brushblade/Assets/_Project/Presentation/BattleView.cs`

**Interfaces:**
- Consumes: Task 3 的 `Cast(..., summonSlots)` / `SlotOccupancy` / `SlotState` / `FrontRow`;Task 7 的 `CanTarget`

- [ ] **Step 1: 出召唤字时弹槽位面板**

`OnCastPressed`(`:2138`)里,若 `Battle.SummonCountOf(def, attackMode) > 0`,先进「选槽位」态而不是直接 `ExecuteCast`。面板画 3 + 3 网格,每格按 `Battle.SlotOccupancy(slot)` 决定表现与后果:

| `SlotState` | 表现 | 点下去 |
|---|---|---|
| `Empty` | 虚框 | 直接落位 |
| `Corpse` | 灰底 + 原字 | 覆盖,**不弹确认** |
| `Alive` | 正常格 + 当前召唤物 | 走既有的顶替确认弹窗(`:2175-2183`) |

一次召多只(林 2、桂 2、四叠字 4)则连点 N 次,已选的槽位当场标记、不可重复选。收集完 N 个槽位后一次性 `ExecuteCast(charId, target, replaceSummon, attackMode, libraryIndex, slots)`。

**连选途中任何一步取消 = 整张字回滚**:AP 不扣、字不消耗、已点过的槽位一律不生效。这条天然成立——槽位是攒在表现层的局部列表里,没调 `Cast` 之前引擎一无所知。

- [ ] **Step 2: 顶替确认弹窗改口径**

`:2175-2183` 现在的文案是「前排 {AliveSummonCount}/{SummonCapacity},「{charId}」召 {n} 只」。改成按玩家实际点的槽位说话,例如「槽位 2 上的「柏」会被顶替」。`Battle.SummonReplaceCountOf(def, attackMode, slots)`(Task 3 已支持传槽位)给出会顶掉几只。

- [ ] **Step 3: 不可选的敌人置灰**

`OnCastPressed` 的免选判断(`:2139`)从 `AliveEnemyCount() > 1` 改成**合法目标数 > 1**:

```csharp
private int LegalTargetCount(CharDef def, bool attackMode)
{
    int count = 0;
    for (int i = 0; i < Battle.Enemies.Count; i++)
        if (Battle.CanTarget(def, i, attackMode)) count++;
    return count;
}
```

```csharp
if (BattleEngine.NeedsTarget(def) && LegalTargetCount(def, attackMode) > 1)
{
    _targeting = true;
    _message = $"「{def.Id}」:点击目标敌人";
    Refresh();
    return;
}
```

不改这一处的话,前排只剩 1 只时还会弹一次没得选的选目标。

`_targeting` 态下的敌人格(`:1107` 挂 `onClick` 那一处)按 `Battle.CanTarget(_graph.Get(_selectedChar), i, attackMode)` 置灰并禁用点击。灰色用 `Theme.LockedBg`(与既有「已锁定/不可用」同一套视觉语汇)。

`OnEnemyClicked`(`:2157`)里补一道守卫:`_targeting` 态下点到不合法目标直接忽略,不要落到「看详情」分支——那会让玩家以为点错了。

- [ ] **Step 4: 编译并试玩**

```bash
cd tools/prescompile && /Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet build --nologo -v q -p:ProjectAsm=/Users/eugenewu/code/game/Brushblade/Library/ScriptAssemblies
```

**由用户在 Unity 里实际跑一场**,逐条确认:

- 出「梅」→ 弹槽位面板 → 点后排槽 → 召唤物出现在后排
- 出「林」(召 2)→ 连点两格 → 两只各就各位
- 连选途中取消 → AP 与字库一滴不动
- 点已有存活召唤物的槽 → 弹顶替确认
- 出「刺」→ 后排敌人可点;出「剑」→ 前排还在时后排敌人置灰
- 出「藤」(纯冻结)→ 后排敌人可点

- [ ] **Step 5: 提交**

```bash
git add Brushblade/Assets/_Project/Presentation/BattleView.cs
git commit -m "feat(ui): 召唤槽位面板与目标置灰

出召唤字先选 3+3 槽位,多只连选,途中取消整张字回滚。够不到的敌人在
选目标态置灰不可点;单敌免选的判据从「存活敌人数」改为「合法目标数」,
前排只剩一只时不再弹没得选的选目标。

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## 收尾验证

全部任务完成后,跑一遍完整验证:

```bash
cd tools/coretests && /Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet test --nologo -v q
```

```bash
ln -s /Users/eugenewu/code/game/tools/fonts/raw tools/fonts/raw 2>/dev/null; python3 -m pytest tools/pipeline/tests/ tools/fonts/tests/ -q
```

```bash
cd tools/prescompile && /Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet build --nologo -v q -p:ProjectAsm=/Users/eugenewu/code/game/Brushblade/Library/ScriptAssemblies
```

Unity EditMode 集成验证由用户在 Test Runner 里跑(编辑器开着时命令行会因项目锁失败)。

**已知遗留**(spec §12,不在本计划范围):

- 三只新怪的数值是初值,未过 balance harness 仿真。
- 三只新怪没有形象,战斗与图鉴里回落到字牌圆格。
- 全字表只有「刺」能点后排;若实测后排太难够到,加字只需在详表里多写一个 `` `Backline` ``。
