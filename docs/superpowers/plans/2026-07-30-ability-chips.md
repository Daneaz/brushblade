# 敌人能力 chip Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 统一战斗 chip 与详情文案的命名(改用字怪名),补上缺失的通假/生僻两种,并在详情弹窗加一行与战斗视觉一致的 chip。

**Architecture:** 文案归 `EnemyInfo`(新增 `AbilityChipText`,带实时状态与撤显条件,机制失效时返回空串由调用方据此不画),颜色归 `Theme`(它本来就是颜色的家、且已引用 Core 类型)。战斗侧因此把现有四个 `if` 收成一个;详情侧给 `FormTab` 加 `Chips` 字段,由 `FormsOf` 在构造期填好,`Select` 只负责画。

**Tech Stack:** C# / Unity 6000.5.2f1 / 项目自有 UI 原语(`Ui.*` / `Theme.*`)

**Spec:** `docs/superpowers/specs/2026-07-30-ability-chips-design.md`

**对 spec 的一处偏离(有意)**:spec 第 5 节把 `AbilityChipColor` 记在 `EnemyInfo` 名下,本计划改放 `Theme`。理由:`EnemyInfo` 现在**没有** `using UnityEngine`,是纯文案层;返回 `Color` 会把 UnityEngine 依赖引进来。而 `Theme` 本就是颜色的唯一来源、且已 `using Brushblade.Core`(既有的 `ElementColor(Element?)` 就接受 Core 类型),放它那里零新增依赖。

## Global Constraints

- 依赖单向:`Presentation → {Core, Data, Platform}`。本计划只改 Presentation。
- **没有自动化测试**,这是硬约束:`EnemyInfo` / `EnemyPreview` / `BattleView` / `Theme` 都在 Presentation asmdef,而 Tests asmdef 只引用 Core / Data,**引用不到**它们。不要为此写测试,也不要为可测性把 UI 代码下移到 Core。
- **`EnemyInfo` 不得引入 `using UnityEngine`** —— 它是纯文案层,颜色一律走 `Theme`。
- 玩家可见文案硬编码在 Presentation(字符串表尚未实装,不在本次范围)。
- **chip 文案的每个字必须有字形**:`charset()` 从 .cs 字符串字面量自动收集,但子集字体产物需要重新生成。**每个任务都要跑字体测试**;若红,跑 `python3 tools/fonts/subset_fonts.py` 重新生成并把字体产物一并提交。本特性的前身漏了这步,导致 20+ 字缺字形(已由 `e4ed279` 修复)。
- 提交信息用 conventional commits,正文用中文。
- 外科手术式改动:每个改动行都应能直接追溯到任务要求。
- 工作区有未跟踪文件 `docs/design/五系机制定位初筛.md` —— **用户自己的文件,不要提交、不要修改**。只 add 实际改动的路径,不要用 `git add -A`。

**验证命令**(每个任务三条都要跑):

```bash
cd /Users/eugenewu/code/game/tools/prescompile && /Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet build --nologo -v q
```

只看 `error CS`(`warning MSB3245` 忽略)。

```bash
cd /Users/eugenewu/code/game && python3 -m pytest tools/fonts/tests/ tools/pipeline/tests/ -q
```

基线 **91 passed**。若 `test_subset_fonts_cover_charset` 报缺字形,跑 `python3 tools/fonts/subset_fonts.py` 后重跑,并把 `Presentation/Fonts/Resources/*.ttf` 与 `tools/fonts/charset.txt` 一并提交。

```bash
cd /Users/eugenewu/code/game/tools/coretests && /Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet test --nologo -v q
```

基线 **484/484** —— 本计划不碰 Core,数字应不变。

## File Structure

| 文件 | 职责 |
|---|---|
| `Presentation/UI/EnemyInfo.cs` | 文案唯一来源;新增 `AbilityChipText(EnemyState)` |
| `Presentation/UI/Theme.cs` | 颜色唯一来源;新增 `AbilityChipColor(EnemyAbility)` / `BossSkillChipColor(BossSkill)` |
| `Presentation/BattleView.cs` | 战斗侧:四个能力 chip 分支收成一个,自动含通假/生僻 |
| `Presentation/UI/EnemyPreview.cs` | 详情侧:`FormTab` 加 `Chips`,`FormsOf` 填充,`Select` 画 |

## 已核实的现状(计划代码依赖这些,勿凭记忆改)

- `EnemyInfo` 现有 using 仅 `System.Text` + `Brushblade.Core`(**无 UnityEngine**),已有 `AbilityName(EnemyAbility)` / `BossSkillName(BossSkill)` / `PhaseDetail` / `MinionDetail` / `FaceChar`
- `Theme` 已有 `ElementColor(Element?)`(接受 Core 类型,故 Theme 已引用 Core)、`Cinnabar` / `InkSoft` / `Jade` / `PaperDim` / `TextMain`
- `EnemyState` 字段:`Alive` / `RegrowProgress` / `HasSplit` / `ApparentElement`(`Element?`)/ `Element` / `Def`
- 生僻字读懂的实现(`BattleEngine.cs:658`):`ApparentElement == null && HitsTaken >= 2` → 设 `ApparentElement = Element`。故**未读懂 ⇔ `ApparentElement == null`**
- 通假字构造时 `ApparentElement` 与 `Element` **必不相同**,现形时设为相等。故**未现形 ⇔ `ApparentElement != Element`**
- `FormTab` 现有四字段:`Label` / `AssetPhase` / `Detail` / `TabColor`,构造顺序同此
- `Ui.Chip(Transform parent, string text, Color bg, Color fg, int fontSize = 14) → GameObject`
- `Ui.Row(Transform parent, string name, float spacing = 8) → GameObject`

---

### Task 1: 战斗侧 chip 统一命名 + 补通假/生僻

**Files:**
- Modify: `Brushblade/Assets/_Project/Presentation/UI/EnemyInfo.cs`(新增一个方法)
- Modify: `Brushblade/Assets/_Project/Presentation/UI/Theme.cs`(新增一个方法)
- Modify: `Brushblade/Assets/_Project/Presentation/BattleView.cs`(四个 chip 分支收成一个)

**Interfaces:**
- Produces(Task 2 依赖):
  - `EnemyInfo.AbilityChipText(EnemyState enemy) → string`(机制失效时返回 `""`)
  - `Theme.AbilityChipColor(EnemyAbility ability) → Color`

- [ ] **Step 1: `EnemyInfo` 新增 `AbilityChipText`**

在 `EnemyInfo.cs` 的 `AbilityText` 方法之后插入:

```csharp
        /// <summary>战斗中的能力 chip 文案:带实时状态,机制已失效时返回空串(调用方据此不画)。
        /// 与 <see cref="AbilityName"/> 同一套命名 —— 玩家在详情学一次,战斗中看到 chip 就懂。</summary>
        public static string AbilityChipText(EnemyState enemy) => enemy.Def.Ability switch
        {
            EnemyAbility.Regrow => enemy.RegrowProgress >= 3
                ? "缺笔 已补全!" : $"缺笔 {enemy.RegrowProgress}/3",
            EnemyAbility.Split => enemy.HasSplit ? "" : "叠字", // 分裂过就没这威胁了
            EnemyAbility.Buff => "标点",
            // 通假:chip 只说「这属性不可信」,不泄真属性;现形(真伪一致)后撤掉
            EnemyAbility.Disguise => enemy.ApparentElement == enemy.Element ? "" : "通假",
            // 生僻:未读懂时 ApparentElement 为 null(属性显示「?」);被读懂后撤掉
            EnemyAbility.Obscure => enemy.ApparentElement != null ? "" : "生僻",
            EnemyAbility.Scorch => "自燃",
            _ => "",
        };
```

**不要**在 `EnemyInfo.cs` 加 `using UnityEngine` —— 颜色在下一步走 `Theme`。

- [ ] **Step 2: `Theme` 新增 `AbilityChipColor`**

在 `Theme.cs` 的 `ElementColor` 方法之后插入:

```csharp
        /// <summary>能力 chip 底色:朱砂 = 增长的威胁,翠玉 = 恢复,深灰蓝 = 防御/辅助/信息类。</summary>
        public static Color AbilityChipColor(EnemyAbility ability) => ability switch
        {
            EnemyAbility.Scorch => Cinnabar, // 越磨越烫
            EnemyAbility.Regrow => Jade,     // 自我修复
            _ => InkSoft,                    // 叠字/标点/通假/生僻
        };
```

(已核实 `Theme.cs` 顶部有 `using Brushblade.Core;`,邻近的 `ElementColor(Element? element)` 也是不带前缀的写法 —— 保持一致。)

- [ ] **Step 3: `BattleView` 四个 chip 分支收成一个**

先定位现有四段:

```bash
grep -n "补全 \|受击分裂\|增益辅助\|受击加攻" Brushblade/Assets/_Project/Presentation/BattleView.cs
```

把这四段整体替换:

```csharp
                if (enemy.Def.Ability == EnemyAbility.Regrow && enemy.Alive)
                    Ui.Chip(chips.transform, enemy.RegrowProgress >= 3 ? "已补全!" : $"补全 {enemy.RegrowProgress}/3",
                        Theme.Jade, Color.white, 12);
                if (enemy.Def.Ability == EnemyAbility.Split && enemy.Alive && !enemy.HasSplit)
                    Ui.Chip(chips.transform, "受击分裂", Theme.InkSoft, Color.white, 12);
                if (enemy.Def.Ability == EnemyAbility.Buff && enemy.Alive)
                    Ui.Chip(chips.transform, "增益辅助", Theme.InkSoft, Color.white, 12);
                if (enemy.Def.Ability == EnemyAbility.Scorch && enemy.Alive)
                    Ui.Chip(chips.transform, "受击加攻", Theme.Cinnabar, Color.white, 12);
```

替换为:

```csharp
                // 能力 chip 统一走 EnemyInfo(与详情弹窗同一套命名);
                // 机制失效(叠字已分裂/通假已现形/生僻已读懂)时返回空串,不画
                if (enemy.Alive)
                {
                    string abilityChip = EnemyInfo.AbilityChipText(enemy);
                    if (abilityChip.Length > 0)
                        Ui.Chip(chips.transform, abilityChip,
                            Theme.AbilityChipColor(enemy.Def.Ability), Color.white, 12);
                }
```

这一步同时兑现了「补上通假/生僻」—— 它们由 `AbilityChipText` 覆盖,不需要额外分支。

- [ ] **Step 4: 跑离线编译**

Run: `cd /Users/eugenewu/code/game/tools/prescompile && /Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet build --nologo -v q`
Expected: `Build succeeded`,无 `error CS`

- [ ] **Step 5: 跑字体测试(本步不可省)**

Run: `cd /Users/eugenewu/code/game && python3 -m pytest tools/fonts/tests/ tools/pipeline/tests/ -q`
Expected: `91 passed`

若 `test_subset_fonts_cover_charset` 报缺字形:跑 `python3 tools/fonts/subset_fonts.py`,重跑测试确认转绿,并把 `Brushblade/Assets/_Project/Presentation/Fonts/Resources/*.ttf` 与 `tools/fonts/charset.txt` 一并纳入本任务提交。

- [ ] **Step 6: 跑 coretests**

Run: `cd /Users/eugenewu/code/game/tools/coretests && /Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet test --nologo -v q`
Expected: 484/484

- [ ] **Step 7: 自查(代替自动化测试)**

1. `EnemyInfo.cs` **没有** `using UnityEngine`
2. `AbilityChipText` 的六个分支文案与 spec 3.1 表格逐字一致(含 `缺笔 已补全!` 的感叹号)
3. 通假的撤显判据是 `ApparentElement == enemy.Element`(现形即真伪一致),生僻是 `ApparentElement != null`
4. `BattleView` 里已无 `受击分裂` / `增益辅助` / `受击加攻` / `补全 ` 这四段旧文案(grep 确认)
5. 未改动属性 / `攻 N` / `承伤` / `灼烧 N` / `蓄力 · 下回合` 五种既有 chip

- [ ] **Step 8: 提交**

```bash
git add Brushblade/Assets/_Project/Presentation/UI/EnemyInfo.cs Brushblade/Assets/_Project/Presentation/UI/Theme.cs Brushblade/Assets/_Project/Presentation/BattleView.cs
git commit -m "feat(enemy): 战斗 chip 统一字怪名命名,补上通假与生僻"
```

(若 Step 5 需要重新生成字体,把字体产物与 `charset.txt` 加进同一次提交。)

---

### Task 2: 详情弹窗加 chip 行

**Files:**
- Modify: `Brushblade/Assets/_Project/Presentation/UI/Theme.cs`(再加一个方法)
- Modify: `Brushblade/Assets/_Project/Presentation/UI/EnemyPreview.cs`(`FormTab` 加字段、`FormsOf` 填充、`Select` 画)

**Interfaces:**
- Consumes: Task 1 的 `Theme.AbilityChipColor(EnemyAbility)`;既有的 `EnemyInfo.AbilityName(EnemyAbility)` / `EnemyInfo.BossSkillName(BossSkill)`
- Produces: `Theme.BossSkillChipColor(BossSkill skill) → Color`

- [ ] **Step 1: `Theme` 新增 `BossSkillChipColor`**

紧挨 Task 1 加的 `AbilityChipColor` 之后插入:

```csharp
        /// <summary>Boss 技能 chip 底色:主动技能走朱砂(威胁),坚壁走深灰蓝(防御)。</summary>
        public static Color BossSkillChipColor(BossSkill skill) =>
            skill == BossSkill.Bulwark ? InkSoft : Cinnabar;
```

- [ ] **Step 2: `FormTab` 加 `Chips` 字段**

把 `FormTab` 结构整体替换:

```csharp
        private readonly struct FormTab
        {
            public readonly string Label;    // tab 上的字
            public readonly int AssetPhase;  // 取形象用的形态下标
            public readonly string Detail;   // 该形态的数值 + 技能/能力说明
            public readonly Color TabColor;  // 选中态底色(该形态的五行色)
            /// <summary>该形态的识别 chip(与战斗中同一套命名,但不带实时状态——图鉴是静态资料)。</summary>
            public readonly IReadOnlyList<(string Text, Color Bg)> Chips;

            public FormTab(string label, int assetPhase, string detail, Color tabColor,
                IReadOnlyList<(string Text, Color Bg)> chips)
            {
                Label = label;
                AssetPhase = assetPhase;
                Detail = detail;
                TabColor = tabColor;
                Chips = chips;
            }
        }
```

- [ ] **Step 3: `FormsOf` 填充 chips**

把 `FormsOf` 整体替换:

```csharp
        /// <summary>把敌人摊成形态列表。将来精英怪多形态只需在这里多生成几个,
        /// 渲染与交互都不用改。chip 的判断也归这里 —— 让 Select 只负责画。</summary>
        private static List<FormTab> FormsOf(EnemyDef def)
        {
            var forms = new List<FormTab>();
            if (def.Phases.Count > 0)
            {
                for (int i = 0; i < def.Phases.Count; i++)
                {
                    var phase = def.Phases[i];
                    var chips = new List<(string, Color)>();
                    if (phase.Skill != BossSkill.None)
                        chips.Add((EnemyInfo.BossSkillName(phase.Skill),
                            Theme.BossSkillChipColor(phase.Skill)));
                    if (phase.DamageTaken < 1f)
                        chips.Add(("承伤", Theme.InkSoft));
                    forms.Add(new FormTab(phase.Char, i, EnemyInfo.PhaseDetail(def, i),
                        Theme.ElementColor(phase.Element), chips));
                }
            }
            else
            {
                var chips = new List<(string, Color)>();
                if (def.Ability != EnemyAbility.None)
                    chips.Add((EnemyInfo.AbilityName(def.Ability),
                        Theme.AbilityChipColor(def.Ability)));
                else if (def.DamageTaken < 1f)
                    chips.Add(("承伤", Theme.InkSoft)); // 墨渍:没能力,减伤就是它的特征
                forms.Add(new FormTab(EnemyInfo.FaceChar(def, 0), 0,
                    EnemyInfo.MinionDetail(def), Theme.ElementColor(def.Element), chips));
            }
            return forms;
        }
```

注意小怪侧是 `else if`:有能力就显示能力名,没能力才用承伤补位 —— 与 `EnemyInfo.MinionDetail` 的三分支(能力 / 减伤 / 无机制)口径一致。Boss 侧是两个独立 `if`,因为技能与减伤可以并存(如「山」= 坚壁 + 承伤 0.5)。

- [ ] **Step 4: `Select` 画 chip 行**

在 `Select` 里,`Tile(...)` 那一行之后、`Ui.ThemedLabel(content.transform, form.Detail, ...)` 之前插入:

```csharp
                if (form.Chips.Count > 0) // 无机制的怪(错字鬼/夯土妖)不画空行
                {
                    var chipRow = Ui.Row(content.transform, "Chips", 5);
                    foreach (var (text, bg) in form.Chips)
                        Ui.Chip(chipRow.transform, text, bg, Color.white, 12);
                }
```

- [ ] **Step 5: 跑离线编译**

Run: `cd /Users/eugenewu/code/game/tools/prescompile && /Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet build --nologo -v q`
Expected: `Build succeeded`,无 `error CS`

- [ ] **Step 6: 跑字体测试**

Run: `cd /Users/eugenewu/code/game && python3 -m pytest tools/fonts/tests/ tools/pipeline/tests/ -q`
Expected: `91 passed`(若红,按 Global Constraints 里的字体流程处理并把产物纳入本任务提交)

- [ ] **Step 7: 跑 coretests**

Run: `cd /Users/eugenewu/code/game/tools/coretests && /Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet test --nologo -v q`
Expected: 484/484

- [ ] **Step 8: 自查(代替自动化测试)**

1. Boss 的「山」阶段应有**两个** chip(`坚壁` + `承伤`);「排」阶段只有 `倾覆`
2. 墨渍(无能力、承伤 0.7)显示 `承伤` 一个 chip
3. 错字鬼 / 夯土妖 `Chips.Count == 0`,**不画 chip 行**(不是画一个空 Row)
4. chip 行位置在形象与说明文本**之间**
5. 详情 chip **不带**实时状态(是 `缺笔` 而非 `缺笔 2/3`)
6. `BestiaryView` 对 `Show` / `Tile` 的调用未受影响(本任务未改这两个签名)

- [ ] **Step 9: 提交**

```bash
git add Brushblade/Assets/_Project/Presentation/UI/Theme.cs Brushblade/Assets/_Project/Presentation/UI/EnemyPreview.cs
git commit -m "feat(enemy): 详情弹窗加识别 chip 行,与战斗视觉一致"
```

---

## 实机验证(必须人工过,自动化盖不到)

两个任务完成后在 Unity 里跑一局,逐条确认:

1. 通假字未现形时有 `通假` chip;**首次行动现形后 chip 消失**
2. 生僻字未读懂时有 `生僻` chip 且属性显示 `?`;**受击两次读懂后 chip 消失、属性显示真值**
3. 缺笔妖 chip 随补全进度走(`缺笔 0/3` → `缺笔 已补全!`)
4. 叠字怪分裂后 `叠字` chip 消失
5. 战斗中与详情弹窗的同一能力,chip 文案**逐字相同**
6. Boss 详情切 tab 时 chip 随之变(「山」显示 `坚壁` + `承伤`)
7. 错字鬼 / 夯土妖的详情没有空的 chip 行
8. 所有 chip 文案**没有出现方块**(缺字形的症状)

第 8 条是字体子集那条约束的实机确认 —— 单元测试只能保证 cmap 覆盖,渲染要眼看。
