# 敌人技能描述与详情弹窗重构 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 给 Boss 的 5 种技能补上完整文案,并把敌人详情弹窗改成「形态页」结构 —— Boss 用四字 tab 分阶段浏览,小怪退化为单页。

**Architecture:** 文案全部收进 `EnemyInfo`(战斗页与图鉴的唯一来源),`BattleView` 的私有 `BossSkillName` 上移去重。`EnemyPreview` 不按「是不是 Boss」分叉,而是由一个 `FormTab` 形态列表驱动渲染,形态数 >1 才画 tab 行 —— 为后续精英怪多形态留口子,同时比两套分支少一处逻辑。

**Tech Stack:** C# / Unity 6000.5.2f1 / 项目自有 UI 原语(`Ui.*` / `Theme.*`)

**Spec:** `docs/superpowers/specs/2026-07-30-enemy-skill-descriptions-design.md`

## Global Constraints

- 依赖单向:`Presentation → {Core, Data, Platform}`。本计划只改 Presentation。
- **本次没有自动化测试**,这是硬约束不是偷懒:`EnemyInfo` / `EnemyPreview` 都在 **Presentation** asmdef,而 Tests asmdef 只引用 Core / Data,**引用不到**它们。为了可测性把 UI 文案下移到 Core 是错误的取舍。
- 因此**每个任务的验证 = 离线编译 + 人工代码审查 + 实机清单**。
- 玩家可见文案按现有惯例硬编码在 Presentation(字符串表尚未实装,不在本次范围)。
- 提交信息用 conventional commits(feat/fix/docs/chore + 范围),正文用中文。
- 外科手术式改动:每个改动行都应能直接追溯到任务要求,不得顺手"改进"相邻代码。

**验证命令**(每个任务都要跑):

```bash
cd /Users/eugenewu/code/game/tools/prescompile && /Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet build --nologo -v q
```

只看 `error CS`(`warning MSB3245` 是 Unity 程序集自带的无关引用,忽略)。

```bash
cd /Users/eugenewu/code/game/tools/coretests && /Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet test --nologo -v q
```

基线 479/479 —— 本计划不碰 Core,数字应当不变。

## File Structure

| 文件 | 职责 |
|---|---|
| `Presentation/UI/EnemyInfo.cs` | 敌人文案的唯一来源:技能名/技能说明/能力名/能力说明/形态详情/蓄力规则 |
| `Presentation/UI/EnemyPreview.cs` | 详情弹窗:`FormTab` 形态列表 + 统一渲染 + tab 交互 |
| `Presentation/BattleView.cs` | 删除私有 `BossSkillName`,改调 `EnemyInfo` |

## 已核实的 API(计划中的代码依赖这些,勿凭记忆改)

- `Theme.ElementColor(Element?) → Color`、`Theme.PaperDim` / `TextMain` / `TextDim` / `GoldBorder` / `LockedBg`、`Theme.TitleFont`
- `CharInfo.ElementName(Element) → string`
- `Ui.Row(Transform parent, string name, float spacing = 8) → GameObject`
- `Ui.VStack(Transform parent, string name, float spacing = 4) → GameObject`
- `Ui.RoundButton(Transform parent, string text, Action onClick, Color bg, Color fg, int fontSize = 22, Vector2? size = null, int radius = 10) → Button`
- `Ui.ThemedLabel(Transform parent, string text, int size, Color color, Font font = null) → Text`
- `Ui.PillButton(Transform parent, string text, Action onClick, Color bg, Color fg, int fontSize, Vector2? size) → Button`
- `Ui.ModalShell(Transform root, string title, Vector2 halfSize, bool dismissable, out Transform content) → GameObject`(`halfSize` 是**半尺寸**;`content` 是 spacing 12 的 VStack)
- `Ui.Clear(Transform parent)` —— 项目既有的重绘惯例(`BattleView.Refresh` 同款用法)
- `MobAssets.PrefixFor(EnemyDef def, int phaseIndex = 0) → string`

---

### Task 1: EnemyInfo 文案层 + BattleView 去重

**Files:**
- Modify: `Brushblade/Assets/_Project/Presentation/UI/EnemyInfo.cs`(整体重写)
- Modify: `Brushblade/Assets/_Project/Presentation/BattleView.cs`(删私有方法、改调用)

**Interfaces:**
- Produces(Task 2 依赖这些):
  - `EnemyInfo.BossSkillName(BossSkill skill) → string`
  - `EnemyInfo.BossSkillText(BossSkill skill, BossPhaseDef phase) → string`
  - `EnemyInfo.AbilityName(EnemyAbility ability) → string`
  - `EnemyInfo.AbilityText(EnemyDef def) → string`(改为**不含**能力名前缀)
  - `EnemyInfo.DamageTakenText(float damageTaken) → string`
  - `EnemyInfo.PhaseDetail(EnemyDef def, int phaseIndex) → string`
  - `EnemyInfo.MinionDetail(EnemyDef def) → string`
  - `EnemyInfo.ChargeRuleText() → string`
  - `EnemyInfo.FaceChar(EnemyDef def, int phaseIndex) → string`(不变)

- [ ] **Step 1: 整体重写 `EnemyInfo.cs`**

```csharp
using System.Text;
using Brushblade.Core;

namespace Brushblade.Presentation
{
    /// <summary>敌人文案的唯一来源:属性/血攻/小怪能力/Boss 技能。战斗页与图鉴共用。
    /// 文案写精确数值(沿用既有惯例),故平衡改动时需同步这里。</summary>
    public static class EnemyInfo
    {
        /// <summary>蓄力周期,对应 <c>BattleConfig.BossChargeEvery</c>(默认 2)。
        /// 本类拿不到 BattleConfig 实例,故在此写常量;改配置需同步这里。</summary>
        private const int ChargeEvery = 2;

        /// <summary>怪的代表字(圆形头像用):Boss 取当前阶段字,小怪取名字首字。战斗与图鉴共用。</summary>
        public static string FaceChar(EnemyDef def, int phaseIndex) =>
            def.Phases.Count > 0 ? def.Phases[phaseIndex].Char : def.Id.Substring(0, 1);

        // ============ Boss 技能 ============

        public static string BossSkillName(BossSkill skill) => skill switch
        {
            BossSkill.Deluge => "淹没",
            BossSkill.Pierce => "贯穿",
            BossSkill.Topple => "倾覆",
            BossSkill.Devour => "吞噬",
            BossSkill.Bulwark => "坚壁",
            _ => "",
        };

        /// <summary>技能说明。坚壁必须读该阶段实际的 DamageTaken——「山」0.5、「江」「钧」0.75,
        /// 写死"减半"会骗人;并写明被克制时减免完全失效(BattleEngine 就是这么结算的)。</summary>
        public static string BossSkillText(BossSkill skill, BossPhaseDef phase) => skill switch
        {
            BossSkill.Deluge => "对你造 攻×2,同时对每只召唤物造 攻×1(走五行)",
            BossSkill.Pierce => "穿透前排:最前一只召唤物造 攻×1,你造 攻×2",
            BossSkill.Topple => "对你造 攻×2,清空你全部护盾,下回合 AP −1",
            BossSkill.Devour => "吞掉最前一只召唤物(无视其血量);场上无召唤物时改为对你造 攻×1",
            BossSkill.Bulwark => $"承伤 ×{phase.DamageTaken:0.##}:该阶段伤害打折,不放大招"
                + " —— 但用克制它的属性打,减免完全失效",
            _ => "本阶段无大招,只有普攻",
        };

        public static string ChargeRuleText() =>
            $"蓄力:每 {ChargeEvery} 个敌方回合蓄力一次,蓄力回合不出手,下回合放当前字的大招。\n"
            + "大招无视召唤物,直接打到你身上(护盾仍能挡)。";

        // ============ 小怪能力 ============

        public static string AbilityName(EnemyAbility ability) => ability switch
        {
            EnemyAbility.Regrow => "缺笔",
            EnemyAbility.Split => "叠字",
            EnemyAbility.Buff => "标点",
            EnemyAbility.Disguise => "通假",
            EnemyAbility.Obscure => "生僻",
            EnemyAbility.Scorch => "自燃",
            _ => "",
        };

        /// <summary>能力说明(不含能力名前缀,名字走 <see cref="AbilityName"/>)。
        /// 六条统一为「机制 + 战术提示」。</summary>
        public static string AbilityText(EnemyDef def) => def.Ability switch
        {
            EnemyAbility.Regrow => "每回合自补全:攻 +2、回 3 血;第 3 次补全后攻翻倍并回满 —— 拖不得",
            EnemyAbility.Split => "首次受击存活即分裂成两个半血(场上不足 4 只时)—— 一击打死免分裂",
            EnemyAbility.Buff => $"有同伴时每回合给其他怪攻 +{def.Attack}(整场累计不回滚);"
                + "落单则亲自出手 —— 优先清掉",
            EnemyAbility.Disguise => "显示的属性是假的,首次行动后才露真身 —— 别急着按显示的属性配克制",
            EnemyAbility.Obscure => "属性隐藏,受击两次后被「读懂」现形",
            EnemyAbility.Scorch => "每次受击存活,攻 +2 —— 越磨越烫,宜速杀",
            _ => "",
        };

        /// <summary>减伤特性行。与 Boss 坚壁走同一条规则,措辞刻意一致——
        /// 玩家学一次就能套用到所有减伤敌人。</summary>
        public static string DamageTakenText(float damageTaken) =>
            $"承伤 ×{damageTaken:0.##}:伤害打折 —— 但用克制它的属性打,减免完全失效";

        // ============ 形态详情 ============

        /// <summary>Boss 单阶段:第 N/M 阶段 + 属性血攻 + 技能名与说明。</summary>
        public static string PhaseDetail(EnemyDef def, int phaseIndex)
        {
            var phase = def.Phases[phaseIndex];
            var text = new StringBuilder();
            text.Append("第 ").Append(phaseIndex + 1).Append('/').Append(def.Phases.Count)
                .Append(" 阶段 · ").Append(CharInfo.ElementName(phase.Element)).Append("系\n");
            text.Append("血 ").Append(phase.MaxHp).Append(" · 攻 ").Append(phase.Attack).Append('\n');
            if (phase.Skill == BossSkill.None)
                text.Append("\n本阶段无大招,只有普攻");
            else
                text.Append('\n').Append('【').Append(BossSkillName(phase.Skill)).Append("】\n")
                    .Append(BossSkillText(phase.Skill, phase));
            return text.ToString();
        }

        /// <summary>小怪单形态:属性血攻 + 能力 / 减伤 / 无机制(三者互斥)。</summary>
        public static string MinionDetail(EnemyDef def)
        {
            var text = new StringBuilder();
            text.Append(CharInfo.ElementName(def.Element)).Append("系 · 血 ")
                .Append(def.MaxHp).Append(" · 攻 ").Append(def.Attack).Append('\n');
            if (def.Ability != EnemyAbility.None)
                text.Append('\n').Append('【').Append(AbilityName(def.Ability)).Append("】\n")
                    .Append(AbilityText(def));
            else if (def.DamageTaken < 1f)
                text.Append('\n').Append(DamageTakenText(def.DamageTaken));
            else
                text.Append("\n无特殊机制 · 纯数值对拼");
            return text.ToString();
        }
    }
}
```

**注意**:原有的 `Detail(EnemyDef def)` 被 `MinionDetail` / `PhaseDetail` 取代,本步骤直接删掉它。它唯一的调用点在 `EnemyPreview.cs:16`,会在 Task 2 改掉 —— 所以**本任务结束时 `EnemyPreview.cs` 会编译失败**,这是预期的中间态,Step 4 的编译验证要在 Task 2 完成后才会全绿。

为避免 Task 1 单独提交时留下编译不过的仓库状态,**Step 2 先把 `EnemyPreview.cs:16` 那一行改成调 `MinionDetail`**(最小改动,不做重构),把完整重构留给 Task 2。

- [ ] **Step 2: 让 `EnemyPreview.cs` 继续能编译(最小改动)**

把 `Brushblade/Assets/_Project/Presentation/UI/EnemyPreview.cs` 第 16 行:

```csharp
            Ui.ThemedLabel(stack, EnemyInfo.Detail(def), 16, Theme.TextDim);
```

改为:

```csharp
            Ui.ThemedLabel(stack, def.Phases.Count > 0
                ? EnemyInfo.PhaseDetail(def, 0) : EnemyInfo.MinionDetail(def), 16, Theme.TextDim);
```

这只是过渡,Task 2 会整体重写这个文件。

- [ ] **Step 3: `BattleView` 删私有 `BossSkillName`,改调 `EnemyInfo`**

先定位:

```bash
grep -n "BossSkillName" Brushblade/Assets/_Project/Presentation/BattleView.cs
```

删掉 `BattleView` 里的私有方法(整块):

```csharp
        private static string BossSkillName(BossSkill skill) => skill switch
        {
            BossSkill.Deluge => "淹没",
            BossSkill.Pierce => "贯穿",
            BossSkill.Topple => "倾覆",
            BossSkill.Devour => "吞噬",
            _ => "",
        };
```

把该文件内所有 `BossSkillName(` 调用改为 `EnemyInfo.BossSkillName(`。**共三处调用点**(已核实):`BattleView.cs:643` 的预警 chip、`AppendBossSkillMessage` 里的 `BossCharging` 与 `BossSkillCast` 两个分支。用 grep 结果逐一核对,不要漏。

注意:被删掉的这个私有版本**没有 `Bulwark` 分支**(它落到 `_ => ""`),而 `EnemyInfo.BossSkillName` 有。上移后坚壁阶段能正确显示名字 —— 这是顺带修掉的一个缺失,不是回归。

- [ ] **Step 4: 跑离线编译**

Run: `cd /Users/eugenewu/code/game/tools/prescompile && /Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet build --nologo -v q`
Expected: `Build succeeded`,无 `error CS`

- [ ] **Step 5: 跑 coretests 确认没碰到 Core**

Run: `cd /Users/eugenewu/code/game/tools/coretests && /Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet test --nologo -v q`
Expected: 479/479 通过(与基线一致)

- [ ] **Step 6: 自查文案(代替自动化测试)**

逐条对照 spec 第 5 节,确认:

1. 【坚壁】用的是 `phase.DamageTaken` 插值,**不是**写死的"减半"
2. 【坚壁】与 `DamageTakenText` 都写了"减免完全失效"(不是"打折""可破"之类含糊说法)
3. 【吞噬】空放写的是"对你造 攻×1",不是"普攻"
4. `AbilityText` 六条都**不含**能力名前缀(名字由 `AbilityName` 提供)
5. `ChargeRuleText` 第二句写了"大招无视召唤物,直接打到你身上(护盾仍能挡)"

- [ ] **Step 7: 提交**

```bash
git add Brushblade/Assets/_Project/Presentation/UI/EnemyInfo.cs Brushblade/Assets/_Project/Presentation/UI/EnemyPreview.cs Brushblade/Assets/_Project/Presentation/BattleView.cs
git commit -m "feat(enemy): 敌人文案收进 EnemyInfo,补齐 Boss 五技能说明"
```

---

### Task 2: EnemyPreview 形态驱动重构

**Files:**
- Modify: `Brushblade/Assets/_Project/Presentation/UI/EnemyPreview.cs`(重写 `Show`,给 `Tile` 加尾部可选参数)

**Interfaces:**
- Consumes: Task 1 的 `EnemyInfo.PhaseDetail` / `MinionDetail` / `ChargeRuleText` / `FaceChar`
- Produces: `EnemyPreview.Tile(Transform parent, EnemyDef def, Vector2 size, bool locked = false, int phaseIndex = 0)` —— `phaseIndex` 必须是**尾部可选参数**,否则 `BestiaryView.cs:119` 的四参数调用会断

- [ ] **Step 1: 给 `Tile` 加 `phaseIndex` 尾部可选参数**

把 `Tile` 的签名改为:

```csharp
        /// <summary>怪牌:战斗同款圆形字头像(五行实色 + 白字代表字)+ 名字 + 血攻;Boss 描金边。未解锁时打码。
        /// phaseIndex:取形象与代表字用的形态下标(Boss 的阶段;小怪恒为 0)。</summary>
        public static GameObject Tile(Transform parent, EnemyDef def, Vector2 size,
            bool locked = false, int phaseIndex = 0)
```

方法体内两处硬编码的 `0` 改为 `phaseIndex`:

```csharp
                string prefix = MobAssets.PrefixFor(def, phaseIndex);
```

```csharp
            portrait ??= Ui.CircleGlyph(inner.transform,
                locked ? "?" : EnemyInfo.FaceChar(def, phaseIndex),
```

其余不动。`BestiaryView.cs:119` 的调用无需修改(它传 4 个参数,`phaseIndex` 取默认 0)。

- [ ] **Step 2: 加 `FormTab` 结构与形态列表构造**

在 `EnemyPreview` 类内、`Show` 之前插入:

```csharp
        /// <summary>详情弹窗的一个形态页。Boss 的四阶段、小怪的单形态、将来精英怪的多形态
        /// 都归成这一种结构 —— 渲染按「形态数」驱动,不按「是不是 Boss」分叉(2026-07-30)。</summary>
        private readonly struct FormTab
        {
            public readonly string Label;    // tab 上的字
            public readonly int AssetPhase;  // 取形象用的形态下标
            public readonly string Detail;   // 该形态的数值 + 技能/能力说明
            public readonly Color TabColor;  // 选中态底色(该形态的五行色)

            public FormTab(string label, int assetPhase, string detail, Color tabColor)
            {
                Label = label;
                AssetPhase = assetPhase;
                Detail = detail;
                TabColor = tabColor;
            }
        }

        /// <summary>把敌人摊成形态列表。将来精英怪多形态只需在这里多生成几个,
        /// 渲染与交互都不用改。</summary>
        private static List<FormTab> FormsOf(EnemyDef def)
        {
            var forms = new List<FormTab>();
            if (def.Phases.Count > 0)
            {
                for (int i = 0; i < def.Phases.Count; i++)
                    forms.Add(new FormTab(def.Phases[i].Char, i, EnemyInfo.PhaseDetail(def, i),
                        Theme.ElementColor(def.Phases[i].Element)));
            }
            else
            {
                forms.Add(new FormTab(EnemyInfo.FaceChar(def, 0), 0,
                    EnemyInfo.MinionDetail(def), Theme.ElementColor(def.Element)));
            }
            return forms;
        }
```

文件顶部加 `using System.Collections.Generic;`(现有 using 只有 `Brushblade.Core` / `UnityEngine` / `UnityEngine.UI`)。

- [ ] **Step 3: 重写 `Show`**

整体替换 `Show` 方法:

```csharp
        /// <param name="bounty">&gt;0 时在窗内播报本次查阅领到的图鉴赏钱。</param>
        public static GameObject Show(Transform root, EnemyDef def, int bounty = 0)
        {
            bool isBoss = def.Phases.Count > 0;
            var forms = FormsOf(def);
            var overlay = Ui.ModalShell(root, isBoss ? "Boss 图鉴" : "怪物图鉴",
                new Vector2(420, isBoss ? 400 : 340), dismissable: true, out var stack);

            // 标题行:Boss 附总血——四阶段是一条总血池,只看单阶段血量会误解
            int totalHp = 0;
            foreach (var phase in def.Phases) totalHp += phase.MaxHp;
            Ui.ThemedLabel(stack, isBoss ? $"{def.Id} · 总血 {totalHp}" : def.Id,
                22, Theme.TextMain, Theme.TitleFont);

            // 先建 tab 行再建内容容器:VStack 按添加顺序排版,tab 必须在形象之上。
            // 按钮稍后填充(onClick 要捕获下面的 Select)
            var tabRow = forms.Count > 1 ? Ui.Row(stack, "Tabs", 6) : null;
            var content = Ui.VStack(stack, "Form", 8);
            var buttons = new List<Button>();

            void Select(int index)
            {
                Ui.Clear(content.transform); // 只重绘内容容器:重建整窗会闪,赏钱行也会丢
                var form = forms[index];
                Tile(content.transform, def, new Vector2(210, 230), false, form.AssetPhase);
                Ui.ThemedLabel(content.transform, form.Detail, 16, Theme.TextDim);
                for (int i = 0; i < buttons.Count; i++)
                {
                    if (buttons[i].targetGraphic is Image image)
                        image.color = i == index ? forms[i].TabColor : Theme.PaperDim;
                    var label = buttons[i].GetComponentInChildren<Text>();
                    if (label != null) label.color = i == index ? Color.white : Theme.TextMain;
                }
            }

            if (tabRow != null)
                for (int i = 0; i < forms.Count; i++)
                {
                    int index = i; // 闭包捕获:直接用 i 会让所有按钮都指向末位
                    buttons.Add(Ui.RoundButton(tabRow.transform, forms[i].Label, () => Select(index),
                        Theme.PaperDim, Theme.TextMain, 22, new Vector2(64, 64), 12));
                }

            Select(0); // 默认首个形态;顺带把 tab 高亮刷成初始态

            if (isBoss)
                Ui.ThemedLabel(stack, EnemyInfo.ChargeRuleText(), 14, Theme.TextDim);
            if (bounty > 0)
                Ui.ThemedLabel(stack, $"◆ 首次查阅赏 {bounty} 墨锭", 18, Theme.GoldBorder, Theme.TitleFont);
            Ui.PillButton(stack, "知道了", () => Object.Destroy(overlay),
                Theme.LockedBg, Theme.TextMain, 18, new Vector2(150, 48));
            return overlay;
        }
```

三处易错点,写的时候留意:

1. **`int index = i;` 不能省** —— 直接在 lambda 里用 `i`,所有按钮都会指向循环末值。
2. **`tabRow` 必须在 `content` 之前创建**,否则 tab 会排到形象下面(`VStack` 按添加顺序排版)。
3. **`Select` 必须在按钮创建之前声明**(C# 局部函数不能前向引用),但 `Select(0)` 要在按钮创建**之后**调用 —— 否则 `buttons` 还是空的,首次高亮刷不上。

- [ ] **Step 4: 跑离线编译**

Run: `cd /Users/eugenewu/code/game/tools/prescompile && /Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet build --nologo -v q`
Expected: `Build succeeded`,无 `error CS`

- [ ] **Step 5: 跑 coretests**

Run: `cd /Users/eugenewu/code/game/tools/coretests && /Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet test --nologo -v q`
Expected: 479/479(本任务不碰 Core)

- [ ] **Step 6: 自查(代替自动化测试)**

1. `BestiaryView.cs:119` 的 `Tile(cell.transform, def, new Vector2(190, 210), !isUnlocked)` 仍是四参数调用,未被破坏
2. `EnemyInfo.Detail` 已无任何引用(`grep -rn "EnemyInfo.Detail" Brushblade/Assets/_Project/`应无输出)
3. `Show` 里没有残留的 `def.Phases.Count > 0 ? ... : ...` 三元(Task 1 Step 2 那个过渡写法应已被整体替换掉)
4. 形态数为 1 时 `tabRow == null`,不会画出只有一个按钮的 tab 行

- [ ] **Step 7: 提交**

```bash
git add Brushblade/Assets/_Project/Presentation/UI/EnemyPreview.cs
git commit -m "feat(enemy): 详情弹窗改形态页驱动,Boss 四字 tab 分阶段浏览"
```

---

## 实机验证(必须人工过,自动化盖不到)

两个任务都完成后,在 Unity 里跑一局,逐条确认:

1. 点小怪 → 详情显示【能力名】+ 说明,文案与 spec 5.3 一致
2. 点**墨渍** → 显示减伤特性行,措辞与 Boss 坚壁一致(都写"减免完全失效")
3. 点**错字鬼** → 显示「无特殊机制 · 纯数值对拼」,没有空白的【】卡片
4. 点 Boss → 四字 tab 出现,默认选中第 1 阶段(底色为该阶段五行色 + 白字)
5. **依次点四个 tab** → 形象、阶段数值、技能卡片三者同步切换;未选中的 tab 恢复灰底黑字
6. Boss 详情**不再**出现「无特殊能力」那行
7. Boss 详情底部有蓄力规则说明
8. 战斗中蓄力时的预警 chip 文案未变(实际现文案是「蓄力 · 下回合:淹没」,`BattleView.cs:643`)
9. 图鉴页(`BestiaryView`)的怪牌列表显示正常,未解锁的仍打码

第 5 条是本次改动的核心交互,重点看形象有没有真的换脸(排/山/倒/海 四张不同底稿)。

## 后续项(不在本计划范围)

- 缺笔妖双形象(缺偏旁 → 逐步补全):用户 2026-07-30 指为后续
- 精英怪多形态:本计划只留结构口子(`FormsOf` 多生成几个 `FormTab` 即可),不实装
- 字符串表实装(全项目待办)
