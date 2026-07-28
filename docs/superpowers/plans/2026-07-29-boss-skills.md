# Boss 技能系统 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 给成语 Boss 加上蓄力预警制的四个主动技能 + 一个被动标签,技能挂在「字」上,破掉"召唤物顶前排 = Boss 无害"的稳态。

**Architecture:** Boss 在敌方回合走三态状态机(释放 / 蓄力 / 交回普攻),每 3 个敌方回合蓄力一次、下回合释放当前阶段字对应的技能。大招对玩家的伤害绕过召唤物顶前排(护盾仍吸收),这是整套设计成立的关键。字→技能表由 `ConfigLoader` 在构造期解析完毕,Core 运行时不需要知道有这张表。

**Tech Stack:** C# / Unity 6000.5.2f1 / NUnit(Unity 自带版本)/ Newtonsoft.Json(仅 Data 层)

**Spec:** `docs/superpowers/specs/2026-07-28-boss-skills-design.md`

## Global Constraints

- **Core 与 Data 禁止引用 UnityEngine**(asmdef 已设 `noEngineReferences: true`)。
- 依赖单向:`Presentation → {Core, Data, Platform}`,`Data → Core`。
- 随机性一律走 Core 内带种子的 RNG,禁用 `UnityEngine.Random`。
- **测试断言禁用 `Is.AnyOf` / `Is.All.AnyOf`**(Unity 自带 NUnit 没有)。多选一用 `Is.EqualTo(a).Or.EqualTo(b)`,集合子集用 `Has.All.Matches<T>`。
- **测试代码禁止直接引用 Newtonsoft**(`JsonConvert` 等)。要测序列化走 `Data.SaveSerializer` / `Data.ConfigLoader` 真实入口。测试只能用 Tests asmdef references 列出的程序集(Core / Data)。
- 提交信息用 conventional commits(feat/fix/docs/chore + 范围),中文正文。
- Core/Data 每个模块先写失败测试再实现;**改完 Presentation 必须过离线编译**。
- ⚠️ **`Tests/ConfigLoaderTests.cs` 被 dotnet 工装明确排除**(`tools/coretests/*.csproj` 的 `Exclude`),因为它用了 `Application.streamingAssetsPath`。写在那个文件里的测试**工装跑不到**,只能在 Unity Test Runner 里跑。所以本计划的配置解析测试一律用内联 JSON 写进 `BossSkillTests.cs`,实船配置断言才放 `ConfigLoaderTests.cs`。

**验证命令**(每个任务的"跑测试"步骤用):

```bash
cd tools/coretests && /Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet test --nologo -v q
```

Presentation 改动后额外跑(只看 `error CS`,`warning MSB3245` 忽略):

```bash
cd tools/prescompile && /Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet build --nologo -v q
```

## File Structure

| 文件 | 职责 |
|---|---|
| `Brushblade/Assets/_Project/Core/EnemyDef.cs` | `BossSkill` 枚举、`BossPhaseDef.Skill`、`EnemyState` 蓄力字段 |
| `Brushblade/Assets/_Project/Core/BattleEngine.cs` | 蓄力状态机、四个技能结算、AP 惩罚、两个新事件 |
| `Brushblade/Assets/_Project/Core/Campaign.cs` | `Scale` 透传 `Skill` |
| `Brushblade/Assets/_Project/Core/Endless.cs` | `IdiomBossDef.Skills`、`BuildIdiomBoss` 填技能 |
| `Brushblade/Assets/_Project/Core/RunSnapshot.cs` | 蓄力状态与 AP 惩罚的存档字段 |
| `Brushblade/Assets/_Project/Data/ConfigLoader.cs` | 字→技能表解析、`phase.skill`、成语 Boss 技能填充 |
| `Brushblade/Assets/StreamingAssets/config/enemies.json` | `bossSkills` 字表段 |
| `Brushblade/Assets/_Project/Presentation/BattleView.cs` | 蓄力预警 chip + 破阶式消息 |
| `Brushblade/Assets/_Project/Tests/BossSkillTests.cs` | 新建,本特性全部行为测试 |

---

### Task 1: 数据结构(枚举 + 字段 + 缩放透传)

**Files:**
- Modify: `Brushblade/Assets/_Project/Core/EnemyDef.cs`
- Modify: `Brushblade/Assets/_Project/Core/Campaign.cs:76-92`
- Test: `Brushblade/Assets/_Project/Tests/BossSkillTests.cs`(新建)

**Interfaces:**
- Produces: `enum BossSkill { None, Deluge, Pierce, Topple, Devour, Bulwark }`;`BossPhaseDef.Skill`(`BossSkill`,构造参数名 `skill`,默认 `BossSkill.None`,位置在 `damageTaken` 之后);`EnemyState.ChargeCounter`(`int`,`internal set`)、`EnemyState.IsCharging`(`bool`,`internal set`)

- [ ] **Step 1: 写失败测试**

新建 `Brushblade/Assets/_Project/Tests/BossSkillTests.cs`:

```csharp
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>Boss 技能系统(蓄力预警制):spec 见
    /// docs/superpowers/specs/2026-07-28-boss-skills-design.md</summary>
    public class BossSkillTests
    {
        [Test]
        public void PhaseDef_CarriesSkill_DefaultsToNone()
        {
            var withSkill = new BossPhaseDef("海", Element.Water, 16, 10, skill: BossSkill.Deluge);
            var without = new BossPhaseDef("干", Element.Wood, 12, 6);

            Assert.That(withSkill.Skill, Is.EqualTo(BossSkill.Deluge));
            Assert.That(without.Skill, Is.EqualTo(BossSkill.None));
        }

        [Test]
        public void Scale_PreservesSkill()
        {
            var boss = new EnemyDef("试炼", Element.Water, 12, 6, phases: new[]
            {
                new BossPhaseDef("排", Element.Metal, 12, 6, skill: BossSkill.Topple),
                new BossPhaseDef("海", Element.Water, 16, 10, skill: BossSkill.Deluge),
            });

            var scaled = CampaignConfig.Scale(boss, 2f);

            Assert.That(scaled.Phases[0].Skill, Is.EqualTo(BossSkill.Topple));
            Assert.That(scaled.Phases[1].Skill, Is.EqualTo(BossSkill.Deluge));
            Assert.That(scaled.Phases[1].MaxHp, Is.EqualTo(32)); // 数值照常缩放
        }
    }
}
```

> 不测 `EnemyState` 的蓄力初值:它的构造函数是 `internal`,测试程序集 new 不了。该断言由 Task 2 的 `ChargeCycle_TwoNormalAttacks_ThenSilentChargeTurn` 隐含覆盖(计数从 0 走到 1)。

- [ ] **Step 2: 跑测试确认失败**

Run: `cd tools/coretests && /Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet test --nologo -v q`
Expected: 编译失败,`BossSkill` 类型不存在

- [ ] **Step 3: 加枚举**

在 `EnemyDef.cs` 的 `EnemyAbility` 枚举之后、`BossPhaseDef` 之前插入:

```csharp
    /// <summary>Boss 阶段技能(spec 2026-07-28):蓄力一回合后释放。
    /// Bulwark 为被动标签,行为与 None 相同(靠 DamageTaken 减伤),
    /// 分开只为可读性——Bulwark = 设计上就该是肉墙,None = 这字还没配技能。</summary>
    public enum BossSkill
    {
        None,
        Deluge, // 淹没:玩家 + 全部召唤物各挨一下(群攻)
        Pierce, // 贯穿:最前召唤物挨一下 + 玩家挨双倍(穿透)
        Topple, // 倾覆:伤害 + 清空护盾 + 下回合 AP −1(剥夺)
        Devour, // 吞噬:消灭最前召唤物(不回血);无召唤物则普攻玩家
        Bulwark, // 坚壁:被动减伤,该阶段不蓄力
    }
```

- [ ] **Step 4: `BossPhaseDef` 加 `Skill`**

改 `BossPhaseDef`(整个类替换):

```csharp
    /// <summary>成语 Boss 的单个阶段(8.5:四字成语,四个字 = 四个阶段)。</summary>
    public sealed class BossPhaseDef
    {
        public string Char { get; }
        public Element Element { get; }
        public int MaxHp { get; }
        public int Attack { get; }
        /// <summary>承伤系数(如「山」0.5 = 超高防御),向下取整。</summary>
        public float DamageTaken { get; }
        /// <summary>该阶段的蓄力技能(spec 2026-07-28);由字表决定,None = 纯普攻。</summary>
        public BossSkill Skill { get; }

        public BossPhaseDef(string phaseChar, Element element, int maxHp, int attack,
            float damageTaken = 1f, BossSkill skill = BossSkill.None)
        {
            Char = phaseChar;
            Element = element;
            MaxHp = maxHp;
            Attack = attack;
            DamageTaken = damageTaken;
            Skill = skill;
        }
    }
```

- [ ] **Step 5: `EnemyState` 加蓄力字段**

在 `EnemyState` 的 `public int HitsTaken { get; internal set; }` 之后加:

```csharp
        /// <summary>蓄力计数(spec 2026-07-28):满 BossChargeEvery 即进入蓄力回合。</summary>
        public int ChargeCounter { get; internal set; }
        /// <summary>蓄力中:本回合已不出手,下个敌方回合释放当前阶段技能。</summary>
        public bool IsCharging { get; internal set; }
```

- [ ] **Step 6: `Scale` 透传 `Skill`**

改 `Campaign.cs:83-86` 的 `phases.Add(...)` 一行,补上 `phase.Skill`:

```csharp
                    phases.Add(new BossPhaseDef(phase.Char, phase.Element,
                        (int)Math.Ceiling(phase.MaxHp * scale),
                        (int)Math.Ceiling(phase.Attack * scale),
                        phase.DamageTaken, phase.Skill)); // 承伤系数与技能都不缩放
```

- [ ] **Step 7: 跑测试确认通过**

Run: `cd tools/coretests && /Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet test --nologo -v q`
Expected: PASS,且既有测试无回归

- [ ] **Step 8: 提交**

```bash
git add Brushblade/Assets/_Project/Core/EnemyDef.cs Brushblade/Assets/_Project/Core/Campaign.cs Brushblade/Assets/_Project/Tests/BossSkillTests.cs
git commit -m "feat(boss): BossSkill 枚举与阶段技能字段,缩放透传"
```

---

### Task 2: 蓄力状态机 + 淹没(Deluge)

**Files:**
- Modify: `Brushblade/Assets/_Project/Core/BattleEngine.cs`
- Test: `Brushblade/Assets/_Project/Tests/BossSkillTests.cs`

**Interfaces:**
- Consumes: Task 1 的 `BossSkill`、`BossPhaseDef.Skill`、`EnemyState.ChargeCounter` / `IsCharging`
- Produces:
  - `BattleConfig.BossChargeEvery`(`int`,默认 3)
  - `BattleEventKind.BossCharging`(`TargetIndex` = Boss 下标,`Amount` = `(int)BossSkill`)
  - `BattleEventKind.BossSkillCast`(同上)
  - `BattleEventKind.ShieldBroken`(`TargetIndex` = −1,`Amount` = 被清空的护盾总量)—— Task 6 使用
  - `private void DamagePlayerDirect(int enemyIndex, int damage)`
  - `private void DamageSummon(int enemyIndex, int summonIndex, int damage, Element attacker)`
  - `private bool ResolveBossTurn(int index, EnemyState enemy)` — 返回 true 表示本回合已处理,调用方跳过普攻
  - `private void CastBossSkill(int index, EnemyState enemy)`

- [ ] **Step 1: 写失败测试**

在 `BossSkillTests.cs` 的 `namespace` 内、类的开头加公共 fixture(放在 `public class BossSkillTests {` 之后、已有测试之前):

```csharp
        // 心属性 Boss:对木召唤物 KeMultiplier = 1.0,五行不干扰技能数值断言。
        // 两阶段各 100 血 → 总血 200、阈值 100(jitter=0),玩家打不动就不会换阶。
        private static EnemyDef SkillBoss(BossSkill skill) => new("试炼", Element.Heart, 100, 5,
            phases: new[]
            {
                new BossPhaseDef("甲", Element.Heart, 100, 5, skill: skill),
                new BossPhaseDef("乙", Element.Heart, 100, 5),
            });

        private static RecipeGraph Graph() => new(new[]
        {
            new CharDef("火", Element.Fire,
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 10) }),
            new CharDef("林", Element.Wood,
                effects: new[] { new EffectDef(EffectKind.Summon, 6, summonCount: 2, summonAttack: 2, summonChar: "木") }),
            new CharDef("盾", Element.Earth,
                effects: new[] { new EffectDef(EffectKind.Shield, 20) }),
        });

        private static BattleEngine Engine(BossSkill skill) =>
            new(Graph(), new BattleConfig { BossPhaseJitterPercent = 0 },
                new string[0], new[] { "火", "林", "盾", "火", "林", "盾" },
                new[] { SkillBoss(skill) }, seed: 1);

        /// <summary>推进 n 个敌方回合。</summary>
        private static void EndTurns(BattleEngine engine, int n)
        {
            for (int i = 0; i < n; i++) engine.EndTurn();
        }
```

再加这三个测试:

```csharp
        [Test]
        public void ChargeCycle_TwoNormalAttacks_ThenSilentChargeTurn()
        {
            var engine = Engine(BossSkill.Deluge);
            int full = engine.PlayerHp;

            engine.EndTurn(); // 敌方回合 1:普攻
            Assert.That(engine.PlayerHp, Is.EqualTo(full - 5));
            Assert.That(engine.Enemies[0].ChargeCounter, Is.EqualTo(1));

            engine.EndTurn(); // 敌方回合 2:普攻
            Assert.That(engine.PlayerHp, Is.EqualTo(full - 10));

            engine.EndTurn(); // 敌方回合 3:蓄力,不出手
            Assert.That(engine.PlayerHp, Is.EqualTo(full - 10), "蓄力回合 Boss 不出手");
            Assert.That(engine.Enemies[0].IsCharging, Is.True);
        }

        [Test]
        public void Deluge_HitsPlayerAndEverySummon()
        {
            // 蓄力前才召唤:否则前两回合的普攻会先把最前一只磨死,淹没就打不到两只了
            var engine = Engine(BossSkill.Deluge);
            EndTurns(engine, 2); // 敌方两回合普攻(此时场上无召唤物,伤害落在玩家身上)
            engine.Cast("林");    // 2 只 6 血木召唤
            Assert.That(engine.Summons.Count, Is.EqualTo(2));
            int full = engine.PlayerHp;

            engine.EndTurn(); // 敌方回合 3:蓄力,不出手
            Assert.That(engine.PlayerHp, Is.EqualTo(full), "蓄力回合 Boss 不出手");

            engine.EndTurn(); // 敌方回合 4:释放淹没

            Assert.That(engine.PlayerHp, Is.EqualTo(full - 5), "大招不被召唤物拦截");
            foreach (var summon in engine.Summons)
                Assert.That(summon.Hp, Is.EqualTo(1)); // 6 血挨 5(心对木 ×1.0)
        }

        [Test]
        public void ChargeCounter_ResetsAfterCast()
        {
            var engine = Engine(BossSkill.Deluge);
            EndTurns(engine, 4); // 蓄力 + 释放

            Assert.That(engine.Enemies[0].IsCharging, Is.False);
            Assert.That(engine.Enemies[0].ChargeCounter, Is.EqualTo(0));
        }
```

- [ ] **Step 2: 跑测试确认失败**

Run: `cd tools/coretests && /Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet test --nologo -v q --filter "FullyQualifiedName~BossSkillTests"`
Expected: FAIL —— 蓄力回合玩家仍掉血、`Deluge_HitsPlayerAndEverySummon` 玩家不掉血

- [ ] **Step 3: 加配置项与事件种类**

`BattleConfig` 的 `BossPhaseJitterPercent` 之后加:

```csharp
        public int BossChargeEvery { get; set; } = 3; // Boss 每 N 个敌方回合蓄力一次(spec 2026-07-28)
```

`BattleEventKind` 枚举末尾(`EnemyRevealed` 之后)加:

```csharp
        BossCharging,   // Boss 进入蓄力回合(Amount = 即将释放的 BossSkill;驱动预警 UI)
        BossSkillCast,  // Boss 释放技能(Amount = BossSkill);随后是各目标的受击事件
        ShieldBroken,   // 护盾被倾覆清空(TargetIndex = −1,Amount = 清掉的总量)
```

- [ ] **Step 4: 提取伤害辅助方法**

在 `BattleEngine` 的 `CheckBossPhase` 方法之前加两个私有方法:

```csharp
        /// <summary>对玩家造成伤害:护盾先吸收(普通桶先扣,豁免桶垫后)。
        /// 大招走这条 = 不经召唤物顶前排(spec 3.3 总则)。</summary>
        private void DamagePlayerDirect(int enemyIndex, int damage)
        {
            int fromNormal = Math.Min(_shieldNormal, damage);
            _shieldNormal -= fromNormal;
            int fromPersist = Math.Min(_shieldPersist, damage - fromNormal);
            _shieldPersist -= fromPersist;
            int absorbed = fromNormal + fromPersist;
            PlayerHp = Math.Max(0, PlayerHp - (damage - absorbed));
            _events.Add(new BattleEvent(BattleEventKind.EnemyAttack, enemyIndex, damage, -1, absorbed));
        }

        /// <summary>对召唤物造成伤害:走五行(与普攻打召唤同规则)。</summary>
        private void DamageSummon(int enemyIndex, int summonIndex, int damage, Element attacker)
        {
            var summon = _summons[summonIndex];
            int taken = WuxingResolver.ResolveEffect(damage, Array.Empty<Element>(), attacker, summon.Element);
            summon.Hp = Math.Max(0, summon.Hp - taken);
            _events.Add(new BattleEvent(BattleEventKind.SummonHit, enemyIndex, taken, summonIndex));
        }
```

- [ ] **Step 5: 现有普攻分支改调 `DamagePlayerDirect`**

把 `EndTurn` 敌人行动循环里的 `else` 分支(现 `BattleEngine.cs:457-466`)整体替换为:

```csharp
                else
                {
                    DamagePlayerDirect(i, damage);
                }
```

同一循环里的 `if (tankIdx >= 0)` 分支同样收敛到 `DamageSummon`:

```csharp
                if (tankIdx >= 0)
                {
                    // 召唤物带属性:敌人打召唤走五行(金克木 ×1.5、木反克土 ×0.5)
                    DamageSummon(i, tankIdx, damage, enemy.Element);
                }
```

- [ ] **Step 6: 加 Boss 三态状态机**

在 `EndTurn` 敌人行动循环内,`if (enemy.Def.Ability == EnemyAbility.Buff && HasOtherAliveEnemy(enemy)) continue;` 之后、`int damage = enemy.Attack;` 之前插入:

```csharp
                if (enemy.IsBoss && ResolveBossTurn(i, enemy))
                    continue; // 已蓄力或已放大招,本回合不走普攻
```

在 `DamageSummon` 之后加状态机与释放入口:

```csharp
        /// <summary>Boss 回合三态(spec 2026-07-28):释放 / 蓄力 / 交回普攻。
        /// 返回 true = 本回合已处理,调用方跳过普通攻击。</summary>
        private bool ResolveBossTurn(int index, EnemyState enemy)
        {
            if (enemy.IsCharging)
            {
                enemy.IsCharging = false;
                enemy.ChargeCounter = 0;
                CastBossSkill(index, enemy);
                return true;
            }

            var skill = enemy.Def.Phases[enemy.PhaseIndex].Skill;
            if (skill == BossSkill.None || skill == BossSkill.Bulwark)
                return false; // 坚壁/无技能阶段:冻结计数,照常普攻

            enemy.ChargeCounter += 1;
            if (enemy.ChargeCounter < _config.BossChargeEvery)
                return false;

            enemy.IsCharging = true;
            _events.Add(new BattleEvent(BattleEventKind.BossCharging, index, (int)skill));
            return true; // 蓄力回合不出手
        }

        /// <summary>释放当前阶段字的技能。先发 BossSkillCast 再发各目标受击事件,
        /// 表现层据此把大招动效与后续伤害分开播。</summary>
        private void CastBossSkill(int index, EnemyState enemy)
        {
            var skill = enemy.Def.Phases[enemy.PhaseIndex].Skill;
            _events.Add(new BattleEvent(BattleEventKind.BossSkillCast, index, (int)skill));

            switch (skill)
            {
                case BossSkill.Deluge: // 淹没:玩家 + 全部召唤物各挨一下
                    DamagePlayerDirect(index, enemy.Attack);
                    for (int s = 0; s < _summons.Count; s++)
                        if (_summons[s].Alive)
                            DamageSummon(index, s, enemy.Attack, enemy.Element);
                    break;
            }
        }
```

- [ ] **Step 7: 跑测试确认通过**

Run: `cd tools/coretests && /Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet test --nologo -v q`
Expected: PASS。若 `BattleEngineTests` / `EndlessTests` 出现回归,先确认是否因新增蓄力节奏导致预期血量变化——**确认改动合理才改断言,不盲改迁就实现**

- [ ] **Step 8: 提交**

```bash
git add Brushblade/Assets/_Project/Core/BattleEngine.cs Brushblade/Assets/_Project/Tests/BossSkillTests.cs
git commit -m "feat(boss): 蓄力预警状态机与淹没技能"
```

---

### Task 3: 坚壁 / 无技能阶段冻结蓄力

**Files:**
- Test: `Brushblade/Assets/_Project/Tests/BossSkillTests.cs`

**Interfaces:**
- Consumes: Task 2 的 `ResolveBossTurn`(已含冻结分支,本任务只补测试锁死行为)

- [ ] **Step 1: 写测试**

```csharp
        [Test]
        public void BulwarkPhase_NeverCharges_AttacksEveryTurn()
        {
            var engine = Engine(BossSkill.Bulwark);
            int full = engine.PlayerHp;

            EndTurns(engine, 4);

            Assert.That(engine.Enemies[0].ChargeCounter, Is.EqualTo(0), "坚壁阶段冻结计数");
            Assert.That(engine.Enemies[0].IsCharging, Is.False);
            Assert.That(engine.PlayerHp, Is.EqualTo(full - 20), "四回合各普攻一次");
        }

        [Test]
        public void NoSkillPhase_NeverCharges()
        {
            var engine = Engine(BossSkill.None);
            int full = engine.PlayerHp;

            EndTurns(engine, 4);

            Assert.That(engine.Enemies[0].ChargeCounter, Is.EqualTo(0));
            Assert.That(engine.PlayerHp, Is.EqualTo(full - 20));
        }
```

- [ ] **Step 2: 跑测试**

Run: `cd tools/coretests && /Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet test --nologo -v q --filter "FullyQualifiedName~BossSkillTests"`
Expected: PASS(Task 2 的实现已覆盖此行为;若 FAIL 说明冻结分支写错了)

- [ ] **Step 3: 提交**

```bash
git add Brushblade/Assets/_Project/Tests/BossSkillTests.cs
git commit -m "test(boss): 锁死坚壁/无技能阶段不蓄力"
```

---

### Task 4: 换阶取消蓄力(方案支点)

**Files:**
- Modify: `Brushblade/Assets/_Project/Core/EnemyDef.cs`(`ApplyPhaseStats`)
- Test: `Brushblade/Assets/_Project/Tests/BossSkillTests.cs`

**Interfaces:**
- Consumes: Task 2 的蓄力状态机

- [ ] **Step 1: 加薄甲 Boss fixture**

`EnemyState.Hp` 是 `internal set`,测试程序集改不了,只能靠打。而公共 fixture 的 `SkillBoss` 首阶段 100 血、部件池只有 2 个「火」(每发 10 点),磨不到阈值。所以本任务专用一只首阶段极薄的 Boss:

在 `BossSkillTests.cs` 的 `SkillBoss` 之后加:

```csharp
        // 首阶段仅 15 血:总血 115、阈值 100(115−15),两发「火」即可推过 —— 专供换阶取消测试。
        // 次阶段技能为 None:换阶后下个敌方回合必是普攻,便于断言"大招没放出来"。
        private static EnemyDef ThinFirstPhaseBoss() => new("薄甲", Element.Heart, 15, 5,
            phases: new[]
            {
                new BossPhaseDef("甲", Element.Heart, 15, 5, skill: BossSkill.Deluge),
                new BossPhaseDef("乙", Element.Heart, 100, 5),
            });
```

- [ ] **Step 2: 写失败测试**

```csharp
        [Test]
        public void CrossingPhaseThreshold_CancelsCharge()
        {
            var engine = new BattleEngine(Graph(),
                new BattleConfig { BossPhaseJitterPercent = 0 },
                new string[0], new[] { "火", "林", "盾", "火", "林", "盾" },
                new[] { ThinFirstPhaseBoss() }, seed: 1);
            var boss = engine.Enemies[0];
            Assert.That(boss.Hp, Is.EqualTo(115)); // 15 + 100

            engine.Cast("火", 0); // 火 vs 心 ×1.0 = 10 → 105,仍在首阶段(阈值 100)
            Assert.That(boss.PhaseIndex, Is.EqualTo(0));

            EndTurns(engine, 3); // 敌方三回合:普攻、普攻、蓄力
            Assert.That(boss.IsCharging, Is.True);

            engine.Cast("火", 0); // 105 → 95 ≤ 100 → 换阶

            Assert.That(boss.PhaseIndex, Is.EqualTo(1));
            Assert.That(boss.IsCharging, Is.False, "换阶取消蓄力");
            Assert.That(boss.ChargeCounter, Is.EqualTo(0));

            int full = engine.PlayerHp;
            engine.EndTurn();
            Assert.That(engine.PlayerHp, Is.EqualTo(full - 5), "大招没放出来,只有普攻");
        }
```

- [ ] **Step 3: 确认测试可编译且失败**

Run: `cd tools/coretests && /Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet test --nologo -v q --filter "FullyQualifiedName~CrossingPhaseThreshold"`
Expected: FAIL —— 换阶后 `IsCharging` 仍为 true

- [ ] **Step 4: 实现——换阶清蓄力**

`EnemyDef.cs` 的 `ApplyPhaseStats`,在 `Burn = 0;` 之后加两行:

```csharp
        /// <summary>换阶:属性/攻击/承伤切换、灼烧清零;血量连续不重置。</summary>
        internal void ApplyPhaseStats(int index)
        {
            var phase = Def.Phases[index];
            PhaseIndex = index;
            Element = phase.Element;
            ApparentElement = phase.Element; // Boss 阶段属性明示
            Attack = phase.Attack;
            DamageTaken = phase.DamageTaken;
            Burn = 0; // 新字新体,灼烧清零
            ChargeCounter = 0; // 同源:蓄力也归零 → 推过阈值可取消大招(spec 3.2)
            IsCharging = false;
        }
```

- [ ] **Step 5: 跑测试确认通过**

Run: `cd tools/coretests && /Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet test --nologo -v q`
Expected: PASS

- [ ] **Step 6: 提交**

```bash
git add Brushblade/Assets/_Project/Core/EnemyDef.cs Brushblade/Assets/_Project/Tests/BossSkillTests.cs
git commit -m "feat(boss): 推过血量阈值可取消蓄力中的大招"
```

---

### Task 5: 贯穿(Pierce)

**Files:**
- Modify: `Brushblade/Assets/_Project/Core/BattleEngine.cs`(`CastBossSkill`)
- Test: `Brushblade/Assets/_Project/Tests/BossSkillTests.cs`

**Interfaces:**
- Consumes: Task 2 的 `CastBossSkill`、`DamagePlayerDirect`、`DamageSummon`、`FirstAliveSummonIndex`

- [ ] **Step 1: 写失败测试**

```csharp
        [Test]
        public void Pierce_HitsFrontSummonAndPlayerDouble()
        {
            var engine = Engine(BossSkill.Pierce);
            EndTurns(engine, 2); // 先走掉两回合普攻,免得把最前一只磨死
            engine.Cast("林");    // 2 只 6 血
            int full = engine.PlayerHp;

            EndTurns(engine, 2); // 蓄力 + 释放

            Assert.That(engine.PlayerHp, Is.EqualTo(full - 10), "玩家挨双倍且不被拦截");
            Assert.That(engine.Summons[0].Hp, Is.EqualTo(1), "最前一只被穿:6 − 5");
            Assert.That(engine.Summons[1].Hp, Is.EqualTo(6), "只穿一条线,第二只不受伤");
        }

        [Test]
        public void Pierce_WithoutSummons_StillHitsPlayerDouble()
        {
            var engine = Engine(BossSkill.Pierce);
            int full = engine.PlayerHp;

            EndTurns(engine, 4);

            Assert.That(engine.PlayerHp, Is.EqualTo(full - 20)); // 普攻 5+5 + 贯穿 10
        }
```

- [ ] **Step 2: 跑测试确认失败**

Run: `cd tools/coretests && /Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet test --nologo -v q --filter "FullyQualifiedName~Pierce"`
Expected: FAIL —— 玩家未掉血(`Pierce` 分支缺失,大招空放)

- [ ] **Step 3: 实现**

在 `CastBossSkill` 的 `switch` 里,`Deluge` 分支之后加:

```csharp
                case BossSkill.Pierce: // 贯穿:一击穿过前排,同时打中后面的玩家
                {
                    int front = FirstAliveSummonIndex();
                    if (front >= 0)
                        DamageSummon(index, front, enemy.Attack, enemy.Element);
                    DamagePlayerDirect(index, enemy.Attack * 2);
                    break;
                }
```

- [ ] **Step 4: 跑测试确认通过**

Run: `cd tools/coretests && /Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet test --nologo -v q`
Expected: PASS

- [ ] **Step 5: 提交**

```bash
git add Brushblade/Assets/_Project/Core/BattleEngine.cs Brushblade/Assets/_Project/Tests/BossSkillTests.cs
git commit -m "feat(boss): 贯穿技能——穿透前排直击玩家"
```

---

### Task 6: 倾覆(Topple)+ AP 惩罚

**Files:**
- Modify: `Brushblade/Assets/_Project/Core/BattleEngine.cs`(`CastBossSkill`、`StartTurn`、字段)
- Test: `Brushblade/Assets/_Project/Tests/BossSkillTests.cs`

**Interfaces:**
- Consumes: Task 2 的 `CastBossSkill`、`DamagePlayerDirect`、`BattleEventKind.ShieldBroken`
- Produces: `private int _apPenaltyNextTurn`(下回合 AP 扣减量,`StartTurn` 消费后清零)

- [ ] **Step 1: 写失败测试**

```csharp
        [Test]
        public void Topple_ClearsAllShieldAndCutsNextTurnAp()
        {
            var engine = Engine(BossSkill.Topple);
            engine.Cast("盾"); // 土系护盾 20
            Assert.That(engine.ShieldTotal, Is.EqualTo(20));

            EndTurns(engine, 4); // 2 普攻(吃盾)+ 蓄力 + 倾覆

            Assert.That(engine.ShieldTotal, Is.EqualTo(0), "剩余护盾被清空");
            Assert.That(engine.Ap, Is.EqualTo(2), "下回合 AP 由 3 降为 2");
        }

        [Test]
        public void ToppleApPenalty_LastsOneTurnOnly()
        {
            var engine = Engine(BossSkill.Topple);
            EndTurns(engine, 4);
            Assert.That(engine.Ap, Is.EqualTo(2));

            engine.EndTurn(); // 再过一个回合
            Assert.That(engine.Ap, Is.EqualTo(3), "惩罚只吃一回合");
        }

        [Test]
        public void ToppleApPenalty_NeverDropsBelowOne()
        {
            var engine = new BattleEngine(Graph(),
                new BattleConfig { BossPhaseJitterPercent = 0, ApPerTurn = 1 },
                new string[0], new[] { "火", "林", "盾", "火", "林", "盾" },
                new[] { SkillBoss(BossSkill.Topple) }, seed: 1);

            EndTurns(engine, 4);

            Assert.That(engine.Ap, Is.EqualTo(1), "AP 下限为 1,玩家至少能做一件事");
        }
```

若 `BattleEngine` 没有 `ShieldTotal` 只读属性,在实现步骤里补一个(见 Step 3)。

- [ ] **Step 2: 跑测试确认失败**

Run: `cd tools/coretests && /Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet test --nologo -v q --filter "FullyQualifiedName~Topple"`
Expected: FAIL(或编译失败于 `ShieldTotal`)

- [ ] **Step 3: 补 `ShieldTotal` 只读属性**

先确认 `BattleEngine` 是否已有等价属性:

```bash
grep -n "ShieldTotal\|public int Shield" Brushblade/Assets/_Project/Core/BattleEngine.cs
```

若没有,在 `public int Ap { get; private set; }` 附近加:

```csharp
        /// <summary>护盾总量(普通桶 + 豁免桶),供 UI 与测试读取。</summary>
        public int ShieldTotal => _shieldNormal + _shieldPersist;
```

若已存在同义属性,测试改用既有名称,不新增。

- [ ] **Step 4: 加 AP 惩罚字段并在 `StartTurn` 消费**

在 `private int _shieldPersist;` 之后加:

```csharp
        private int _apPenaltyNextTurn; // 倾覆造成的下回合 AP 扣减(spec 4.3),消费后清零
```

改 `StartTurn` 的 `Ap = _config.ApPerTurn;` 一行为:

```csharp
            Ap = Math.Max(1, _config.ApPerTurn - _apPenaltyNextTurn); // 下限 1:不出现完全不能动的回合
            _apPenaltyNextTurn = 0;
```

- [ ] **Step 5: 加 `Topple` 分支**

在 `CastBossSkill` 的 `switch` 里,`Pierce` 分支之后加:

```csharp
                case BossSkill.Topple: // 倾覆:先按常规吸伤,再把剩余护盾整个掀掉
                {
                    DamagePlayerDirect(index, enemy.Attack);
                    int broken = _shieldNormal + _shieldPersist;
                    if (broken > 0)
                    {
                        _shieldNormal = 0;
                        _shieldPersist = 0;
                        _events.Add(new BattleEvent(BattleEventKind.ShieldBroken, -1, broken));
                    }
                    _apPenaltyNextTurn = 1;
                    break;
                }
```

- [ ] **Step 6: 跑测试确认通过**

Run: `cd tools/coretests && /Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet test --nologo -v q`
Expected: PASS

- [ ] **Step 7: 提交**

```bash
git add Brushblade/Assets/_Project/Core/BattleEngine.cs Brushblade/Assets/_Project/Tests/BossSkillTests.cs
git commit -m "feat(boss): 倾覆技能——清空护盾并削减下回合 AP"
```

---

### Task 7: 吞噬(Devour)

**Files:**
- Modify: `Brushblade/Assets/_Project/Core/BattleEngine.cs`(`CastBossSkill`)
- Test: `Brushblade/Assets/_Project/Tests/BossSkillTests.cs`

**Interfaces:**
- Consumes: Task 2 的 `CastBossSkill`、`DamagePlayerDirect`、`FirstAliveSummonIndex`

- [ ] **Step 1: 写失败测试**

```csharp
        [Test]
        public void Devour_KillsFrontSummon_AndDoesNotHealBoss()
        {
            var engine = Engine(BossSkill.Devour);
            EndTurns(engine, 2); // 先走掉两回合普攻,免得把最前一只磨死
            engine.Cast("林");    // 2 只 6 血,均满血
            int full = engine.PlayerHp;
            int bossHpBefore = engine.Enemies[0].Hp;

            EndTurns(engine, 2); // 蓄力 + 吞噬

            Assert.That(engine.Summons[0].Alive, Is.False, "最前一只被吞:满血 6 也照删");
            Assert.That(engine.Summons[1].Hp, Is.EqualTo(6), "第二只不受影响");
            Assert.That(engine.PlayerHp, Is.EqualTo(full), "吞噬不打玩家");
            // 召唤物每回合反击 2×2=4,Boss 只会掉血,绝不因吞噬回血
            Assert.That(engine.Enemies[0].Hp, Is.LessThan(bossHpBefore), "不回血");
        }

        [Test]
        public void Devour_WithoutSummons_PlainAttackNotDoubled()
        {
            var engine = Engine(BossSkill.Devour);
            int full = engine.PlayerHp;

            EndTurns(engine, 4);

            Assert.That(engine.PlayerHp, Is.EqualTo(full - 15), "普攻 5+5 + 吞噬空放的普攻 5(不翻倍)");
        }
```

- [ ] **Step 2: 跑测试确认失败**

Run: `cd tools/coretests && /Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet test --nologo -v q --filter "FullyQualifiedName~Devour"`
Expected: FAIL

- [ ] **Step 3: 实现**

在 `CastBossSkill` 的 `switch` 里,`Topple` 分支之后加:

```csharp
                case BossSkill.Devour: // 吞噬:无视血量必杀最前一只(不回血);没得吞就普攻
                {
                    int front = FirstAliveSummonIndex();
                    if (front >= 0)
                    {
                        var victim = _summons[front];
                        int lost = victim.Hp;
                        victim.Hp = 0;
                        _events.Add(new BattleEvent(BattleEventKind.SummonHit, index, lost, front));
                    }
                    else
                    {
                        DamagePlayerDirect(index, enemy.Attack);
                    }
                    break;
                }
```

- [ ] **Step 4: 跑测试确认通过**

Run: `cd tools/coretests && /Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet test --nologo -v q`
Expected: PASS

- [ ] **Step 5: 提交**

```bash
git add Brushblade/Assets/_Project/Core/BattleEngine.cs Brushblade/Assets/_Project/Tests/BossSkillTests.cs
git commit -m "feat(boss): 吞噬技能——无视血量拔除最前召唤物"
```

---

### Task 8: 断点续爬存档

**Files:**
- Modify: `Brushblade/Assets/_Project/Core/RunSnapshot.cs`
- Modify: `Brushblade/Assets/_Project/Core/EnemyDef.cs`(`Capture` / `Restore`)
- Modify: `Brushblade/Assets/_Project/Core/BattleEngine.cs`(`Capture` / `Restore`)
- Test: `Brushblade/Assets/_Project/Tests/BossSkillTests.cs`

**Interfaces:**
- Produces: `EnemySnapshot.ChargeCounter`(`int`)、`EnemySnapshot.IsCharging`(`bool`)

> **偏离 spec 6.1 说明**:spec 写了"`BattleSnapshot` 加 AP 惩罚位",实现时确认**不需要**。`_apPenaltyNextTurn` 在 `EndTurn` 内被倾覆设置,同一个 `EndTurn` 末尾的 `StartTurn` 立刻消费并清零 —— 它永远不会跨越存档点(存档只发生在玩家回合)。加这个字段是死代码,故省略。当前回合已生效的 AP 由既有的 `BattleSnapshot.Ap` 正常存取。

- [ ] **Step 1: 写失败测试**

```csharp
        [Test]
        public void Snapshot_RoundTrips_ChargeState()
        {
            var engine = Engine(BossSkill.Deluge);
            EndTurns(engine, 3); // 停在蓄力中
            Assert.That(engine.Enemies[0].IsCharging, Is.True);

            var restored = BattleEngine.Restore(engine.Capture(), Graph(),
                new BattleConfig { BossPhaseJitterPercent = 0 },
                new[] { SkillBoss(BossSkill.Deluge) });

            Assert.That(restored.Enemies[0].IsCharging, Is.True, "读档不能白嫖取消大招");
            Assert.That(restored.Enemies[0].ChargeCounter, Is.EqualTo(3));

            int full = restored.PlayerHp;
            restored.EndTurn();
            Assert.That(restored.PlayerHp, Is.EqualTo(full - 5), "续爬后照常释放");
        }

        [Test]
        public void Snapshot_RoundTrips_ReducedAp()
        {
            var engine = Engine(BossSkill.Topple);
            EndTurns(engine, 4); // 倾覆已生效,当前回合 AP = 2
            Assert.That(engine.Ap, Is.EqualTo(2));

            var restored = BattleEngine.Restore(engine.Capture(), Graph(),
                new BattleConfig { BossPhaseJitterPercent = 0 },
                new[] { SkillBoss(BossSkill.Topple) });

            Assert.That(restored.Ap, Is.EqualTo(2), "被削过的 AP 走既有 BattleSnapshot.Ap 存取");
        }
```

> 先用下面的命令确认 `Capture` / `Restore` 的真实签名,按实际签名调整测试里的调用:
> ```bash
> grep -n "public BattleSnapshot Capture\|public static BattleEngine Restore" -A 6 Brushblade/Assets/_Project/Core/BattleEngine.cs
> ```

- [ ] **Step 2: 跑测试确认失败**

Run: `cd tools/coretests && /Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet test --nologo -v q --filter "FullyQualifiedName~Snapshot_RoundTrips"`
Expected: FAIL —— 复原后 `IsCharging` 为 false

- [ ] **Step 3: 加存档字段**

`RunSnapshot.cs` 的 `EnemySnapshot`,在 `public int HitsTaken { get; set; }` 之后加:

```csharp
        public int ChargeCounter { get; set; }   // Boss 蓄力进度(spec 2026-07-28)
        public bool IsCharging { get; set; }     // 蓄力中:读档后要照常放大招
```

`BattleSnapshot` 不动(见上方偏离说明)。

- [ ] **Step 4: `EnemyState` 的 Capture / Restore 带上新字段**

`EnemyDef.cs` 的 `Capture()`,在 `HitsTaken = HitsTaken,` 之后加:

```csharp
            ChargeCounter = ChargeCounter,
            IsCharging = IsCharging,
```

`Restore(...)` 的对象初始化器里,在 `HitsTaken = snapshot.HitsTaken,` 之后加:

```csharp
            ChargeCounter = snapshot.ChargeCounter,
            IsCharging = snapshot.IsCharging,
```

- [ ] **Step 5: 跑测试确认通过**

Run: `cd tools/coretests && /Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet test --nologo -v q`
Expected: PASS,`SnapshotRoundTripTests` 无回归

- [ ] **Step 6: 提交**

```bash
git add Brushblade/Assets/_Project/Core/RunSnapshot.cs Brushblade/Assets/_Project/Core/EnemyDef.cs Brushblade/Assets/_Project/Tests/BossSkillTests.cs
git commit -m "feat(boss): 蓄力状态入断点存档"
```

---

### Task 9: 字→技能表配置解析

**Files:**
- Modify: `Brushblade/Assets/_Project/Data/ConfigLoader.cs`
- Modify: `Brushblade/Assets/_Project/Core/Endless.cs`(`IdiomBossDef`、`BuildIdiomBoss`)
- Modify: `Brushblade/Assets/StreamingAssets/config/enemies.json`
- Test: `Brushblade/Assets/_Project/Tests/BossSkillTests.cs`

**Interfaces:**
- Produces: `IdiomBossDef.Skills`(`IReadOnlyList<BossSkill>`,四项);`enemies.json` 顶层 `bossSkills` 段

- [ ] **Step 1: 写失败测试**

```csharp
        [Test]
        public void BuildIdiomBoss_UsesPerCharSkills()
        {
            var idiom = new IdiomBossDef
            {
                Chars = "刀山火海",
                Elements = new[] { Element.Metal, Element.Earth, Element.Fire, Element.Water },
                Skills = new[] { BossSkill.Pierce, BossSkill.Bulwark, BossSkill.Devour, BossSkill.Deluge },
            };

            var boss = Endless.BuildIdiomBoss(idiom);

            Assert.That(boss.Phases[0].Skill, Is.EqualTo(BossSkill.Pierce));
            Assert.That(boss.Phases[1].Skill, Is.EqualTo(BossSkill.Bulwark));
            Assert.That(boss.Phases[3].Skill, Is.EqualTo(BossSkill.Deluge));
        }

        [Test]
        public void BuildIdiomBoss_WithoutSkills_FallsBackToNone()
        {
            var idiom = new IdiomBossDef
            {
                Chars = "刀山火海",
                Elements = new[] { Element.Metal, Element.Earth, Element.Fire, Element.Water },
            };

            var boss = Endless.BuildIdiomBoss(idiom);

            foreach (var phase in boss.Phases)
                Assert.That(phase.Skill, Is.EqualTo(BossSkill.None));
        }
```

配置解析测试也写在 `BossSkillTests.cs`(**不能写 `ConfigLoaderTests.cs`,工装排除了那个文件**)。文件顶部加 `using Brushblade.Data;`,然后追加:

```csharp
        /// <summary>最小合法战役 JSON:一只三阶段 Boss + 字表。
        /// 「排」「山」走字表,「槑」故意不在表里 → 应 fallback 到 None。</summary>
        private static string CampaignJson(string phaseSkillField = "") => @"
{
  ""enemies"": [
    { ""id"": ""试炼"", ""element"": ""Water"", ""maxHp"": 12, ""attack"": 6,
      ""phases"": [
        { ""char"": ""排"", ""element"": ""Metal"", ""maxHp"": 12, ""attack"": 6" + phaseSkillField + @" },
        { ""char"": ""山"", ""element"": ""Earth"", ""maxHp"": 15, ""attack"": 4, ""damageTaken"": 0.5 },
        { ""char"": ""槑"", ""element"": ""Wood"", ""maxHp"": 12, ""attack"": 8 }
      ] }
  ],
  ""dropTable"": [""火""],
  ""bossSkills"": { ""排"": ""Topple"", ""山"": ""Bulwark"" },
  ""chapters"": [
    { ""name"": ""测试章"", ""bossPool"": [""试炼""],
      ""stages"": [ { ""encounters"": [[""试炼""]], ""boss"": true } ] }
  ]
}";

        [Test]
        public void LoadCampaign_ResolvesBossSkillsFromCharTable()
        {
            var config = ConfigLoader.LoadCampaign(CampaignJson(), Graph());
            var boss = config.Chapters[0].BossPool[0];

            Assert.That(boss.Phases[0].Skill, Is.EqualTo(BossSkill.Topple));  // 「排」查表
            Assert.That(boss.Phases[1].Skill, Is.EqualTo(BossSkill.Bulwark)); // 「山」查表
        }

        [Test]
        public void LoadCampaign_UnknownCharInPhase_FallsBackToNone()
        {
            var config = ConfigLoader.LoadCampaign(CampaignJson(), Graph());
            var boss = config.Chapters[0].BossPool[0];

            Assert.That(boss.Phases[2].Skill, Is.EqualTo(BossSkill.None), "「槑」不在表里,不报错只留白");
        }

        [Test]
        public void LoadCampaign_ExplicitPhaseSkill_WinsOverTable()
        {
            // 「排」在字表里是 Topple,phase.skill 显式写 Deluge 应当覆盖它
            var config = ConfigLoader.LoadCampaign(CampaignJson(@", ""skill"": ""Deluge"""), Graph());
            var boss = config.Chapters[0].BossPool[0];

            Assert.That(boss.Phases[0].Skill, Is.EqualTo(BossSkill.Deluge));
        }

        [Test]
        public void LoadCampaign_UnknownSkillName_Throws()
        {
            Assert.Throws<ConfigException>(() =>
                ConfigLoader.LoadCampaign(CampaignJson(@", ""skill"": ""不存在的技能"""), Graph()));
        }
```

> `Graph()` 含「火」,恰好满足 `dropTable` 的引用校验。若 `LoadCampaign` 对 `chapters` 还有本计划未覆盖的必填校验,按报错信息补最小字段即可 —— 断言本身不变。

- [ ] **Step 2: 跑测试确认失败**

Run: `cd tools/coretests && /Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet test --nologo -v q`
Expected: 编译失败(`IdiomBossDef.Skills` 不存在)

- [ ] **Step 3: `IdiomBossDef` 加 `Skills`**

`Endless.cs` 的 `IdiomBossDef`:

```csharp
    /// <summary>成语 Boss 定义(20.7):四字成语 → 四阶段,逐字属性由配置指定。</summary>
    public sealed class IdiomBossDef
    {
        public string Chars { get; set; }
        public IReadOnlyList<Element> Elements { get; set; }
        /// <summary>逐字技能(spec 2026-07-28);ConfigLoader 查表填好,空 = 全 None。</summary>
        public IReadOnlyList<BossSkill> Skills { get; set; } = System.Array.Empty<BossSkill>();
    }
```

- [ ] **Step 4: `BuildIdiomBoss` 填技能**

替换 `Endless.BuildIdiomBoss`:

```csharp
        /// <summary>成语 → 四阶段 Boss(20.7):数值模板对齐排山倒海——
        /// 首字均衡(12/6)、次字坚壁(15/4,承伤 0.5)、三字强攻(12/8)、末字狂攻(16/10)。
        /// 技能逐字取自 idiom.Skills(spec 2026-07-28),缺省为 None。</summary>
        public static EnemyDef BuildIdiomBoss(IdiomBossDef idiom)
        {
            BossSkill SkillAt(int i) =>
                idiom.Skills != null && i < idiom.Skills.Count ? idiom.Skills[i] : BossSkill.None;

            var phases = new List<BossPhaseDef>
            {
                new(idiom.Chars[0].ToString(), idiom.Elements[0], 12, 6, 1f, SkillAt(0)),
                new(idiom.Chars[1].ToString(), idiom.Elements[1], 15, 4, 0.5f, SkillAt(1)),
                new(idiom.Chars[2].ToString(), idiom.Elements[2], 12, 8, 1f, SkillAt(2)),
                new(idiom.Chars[3].ToString(), idiom.Elements[3], 16, 10, 1f, SkillAt(3)),
            };
            return new EnemyDef(idiom.Chars, idiom.Elements[0], 12, 6, EnemyAbility.None, phases);
        }
```

- [ ] **Step 5: `ConfigLoader` 解析字表**

`CampaignFileDto` 加字段:

```csharp
            public Dictionary<string, string> BossSkills { get; set; }
```

`PhaseDto` 加字段:

```csharp
            public string Skill { get; set; }
```

在 `ParseEnemies` 之前加解析方法:

```csharp
        /// <summary>字 → Boss 技能表(spec 2026-07-28)。查不到的字一律 None,
        /// 所以往成语库加字永远不会崩,只是没技能。</summary>
        private static Dictionary<string, BossSkill> ParseBossSkills(Dictionary<string, string> dto)
        {
            var table = new Dictionary<string, BossSkill>();
            foreach (var pair in dto ?? new Dictionary<string, string>())
            {
                if (!Enum.TryParse<BossSkill>(pair.Value, out var skill))
                    throw new ConfigException($"字「{pair.Key}」的 Boss 技能未知:{pair.Value}");
                table[pair.Key] = skill;
            }
            return table;
        }

        private static BossSkill SkillFor(Dictionary<string, BossSkill> table, string phaseChar,
            string explicitSkill, string bossId)
        {
            if (!string.IsNullOrEmpty(explicitSkill))
            {
                if (!Enum.TryParse<BossSkill>(explicitSkill, out var declared))
                    throw new ConfigException($"Boss「{bossId}」阶段「{phaseChar}」技能未知:{explicitSkill}");
                return declared; // 显式字段优先于字表
            }
            return table.TryGetValue(phaseChar, out var looked) ? looked : BossSkill.None;
        }
```

`ParseEnemies` 签名加参数并在建 `BossPhaseDef` 时用上:

```csharp
        private static Dictionary<string, EnemyDef> ParseEnemies(List<EnemyDto> enemies,
            Dictionary<string, BossSkill> bossSkills)
```

```csharp
                        phases.Add(new BossPhaseDef(phase.Char, phaseElement, phase.MaxHp, phase.Attack,
                            phase.DamageTaken, SkillFor(bossSkills, phase.Char, phase.Skill, dto.Id)));
```

`LoadCampaign` 的调用点(现 `ConfigLoader.cs:134`):

```csharp
            var bossSkills = ParseBossSkills(file.BossSkills);
            var enemyDefs = ParseEnemies(file.Enemies, bossSkills);
```

`ParseEndless` 签名加同一个参数,调用点(现 `ConfigLoader.cs:229`)改为:

```csharp
                Endless = ParseEndless(file.Endless, enemyDefs, graph, bossSkills),
```

成语 Boss 构造处(现 `ConfigLoader.cs:287`)填技能:

```csharp
                    var skills = new List<BossSkill>();
                    foreach (var c in idiomDto.Chars)
                        skills.Add(bossSkills.TryGetValue(c.ToString(), out var s) ? s : BossSkill.None);
                    idiomBosses.Add(new IdiomBossDef
                    {
                        Chars = idiomDto.Chars, Elements = elements, Skills = skills,
                    });
```

- [ ] **Step 6: 写入 `enemies.json` 字表**

在 `enemies.json` 顶层(与 `enemies` / `dropTable` 平级)加:

```json
  "bossSkills": {
    "海": "Deluge", "江": "Deluge", "河": "Deluge", "啸": "Deluge",
    "崩": "Deluge", "雪": "Deluge", "沙": "Deluge", "气": "Deluge", "万": "Deluge",
    "雷": "Pierce", "霆": "Pierce", "钧": "Pierce",
    "刀": "Pierce", "石": "Pierce", "飞": "Pierce",
    "倒": "Topple", "翻": "Topple", "排": "Topple", "走": "Topple",
    "吞": "Devour", "火": "Devour", "烈": "Devour", "柴": "Devour",
    "山": "Bulwark", "地": "Bulwark", "天": "Bulwark", "冰": "Bulwark"
  },
```

「干」不列入,走 fallback 到 `None`(spec 5:留白让有大招的阶段更有分量)。

- [ ] **Step 7: 跑测试确认通过**

Run: `cd tools/coretests && /Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet test --nologo -v q`
Expected: PASS,`EndlessConfigTests` 无回归。(Step 8 的实船断言不在工装内,见收尾节)

- [ ] **Step 8: 加实船配置断言**

在 `Brushblade/Assets/_Project/Tests/ConfigLoaderTests.cs` 追加(**该文件工装跑不到,需在 Unity Test Runner 里验证**):

```csharp
        /// <summary>实船 enemies.json 的字表接线:三只固定 Boss 与程序生成成语 Boss
        /// 都应从 bossSkills 拿到技能(spec 5.1)。</summary>
        [Test]
        public void ShippedConfig_BossesGetSkillsFromCharTable()
        {
            var configDir = Path.Combine(Application.streamingAssetsPath, "config");
            var graph = ConfigLoader.LoadGraph(File.ReadAllText(Path.Combine(configDir, "chars.json")));
            var campaign = ConfigLoader.LoadCampaign(
                File.ReadAllText(Path.Combine(configDir, "enemies.json")), graph);

            var paiShan = campaign.Endless.Bands[0].BossPool[0];
            Assert.That(paiShan.Id, Is.EqualTo("排山倒海"));
            Assert.That(paiShan.Phases[0].Skill, Is.EqualTo(BossSkill.Topple));  // 排
            Assert.That(paiShan.Phases[1].Skill, Is.EqualTo(BossSkill.Bulwark)); // 山
            Assert.That(paiShan.Phases[2].Skill, Is.EqualTo(BossSkill.Topple));  // 倒
            Assert.That(paiShan.Phases[3].Skill, Is.EqualTo(BossSkill.Deluge));  // 海

            // 墨海层段(最后一个 band)的成语 Boss 也要拿到技能
            var moHai = campaign.Endless.Bands[campaign.Endless.Bands.Count - 1];
            var daoShan = moHai.IdiomBossPool[0];
            Assert.That(daoShan.Chars, Is.EqualTo("刀山火海"));
            Assert.That(daoShan.Skills[0], Is.EqualTo(BossSkill.Pierce));  // 刀
            Assert.That(daoShan.Skills[1], Is.EqualTo(BossSkill.Bulwark)); // 山
            Assert.That(daoShan.Skills[2], Is.EqualTo(BossSkill.Devour));  // 火
            Assert.That(daoShan.Skills[3], Is.EqualTo(BossSkill.Deluge));  // 海
        }
```

对照 spec 5.1 核对另两只:翻江倒海 = 倾覆/淹没/倾覆/淹没;雷霆万钧 = 贯穿/贯穿/淹没/贯穿。

- [ ] **Step 9: 提交**

```bash
git add Brushblade/Assets/_Project/Data/ConfigLoader.cs Brushblade/Assets/_Project/Core/Endless.cs Brushblade/Assets/StreamingAssets/config/enemies.json Brushblade/Assets/_Project/Tests/
git commit -m "feat(boss): 字→技能表配置解析,成语 Boss 自动获得技能"
```

---

### Task 10: 表现层蓄力预警

**Files:**
- Modify: `Brushblade/Assets/_Project/Presentation/BattleView.cs`

**Interfaces:**
- Consumes: `EnemyState.IsCharging`、`BossPhaseDef.Skill`、`BattleEventKind.BossSkillCast` / `ShieldBroken`

预警看不见,蓄力制就退化成随机挨打 —— 本任务是设计成立的前提,不是收尾。

- [ ] **Step 1: 加技能名辅助方法**

在 `BattleView.cs` 的 `AppendBossPhaseMessage` 附近加:

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

- [ ] **Step 2: 敌人格加预警 chip**

在 `BattleView.cs:600` 的坚壁 chip 之后加(与既有 chip 同一风格,朱砂色表危险):

```csharp
                if (enemy.IsCharging && enemy.IsBoss)
                    Ui.Chip(chips.transform,
                        $"⚡ 下回合:{BossSkillName(enemy.Def.Phases[enemy.PhaseIndex].Skill)}",
                        Theme.Cinnabar, Color.white, 12);
```

- [ ] **Step 3: 加释放消息**

在 `AppendBossPhaseMessage` 方法体的 `foreach` 之后追加同级循环(或新增方法并在同一调用点调用):

```csharp
        private void AppendBossSkillMessage()
        {
            foreach (var e in Battle.LastEvents)
            {
                if (e.Kind == BattleEventKind.BossCharging)
                    _message += $"  蓄力中——下回合「{BossSkillName((BossSkill)e.Amount)}」";
                else if (e.Kind == BattleEventKind.BossSkillCast)
                    _message += $"  {BossSkillName((BossSkill)e.Amount)}!";
                else if (e.Kind == BattleEventKind.ShieldBroken)
                    _message += $"  护盾被掀空({e.Amount})";
            }
        }
```

- [ ] **Step 4: 在 `AppendBossPhaseMessage` 的调用点旁调用新方法**

```bash
grep -n "AppendBossPhaseMessage()" Brushblade/Assets/_Project/Presentation/BattleView.cs
```

在每处调用之后补一行 `AppendBossSkillMessage();`。

- [ ] **Step 5: 过离线编译**

Run: `cd tools/prescompile && /Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet build --nologo -v q`
Expected: 无 `error CS`(`warning MSB3245` 忽略)

- [ ] **Step 6: 确认护盾条表现**

`ShieldBroken` 事件目前只驱动文字消息。确认动画结束后的重绘会把护盾条归零;若护盾条走增量推进而残留旧值,在 `BattleView.cs:171` 的 `Shield` 事件处理旁补一个 `ShieldBroken` 分支,把护盾条直接推到 0。

- [ ] **Step 7: 跑全量测试 + 编译**

```bash
cd tools/coretests && /Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet test --nologo -v q
cd ../prescompile && /Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet build --nologo -v q
```

Expected: 测试全绿,编译无 `error CS`

- [ ] **Step 8: 提交**

```bash
git add Brushblade/Assets/_Project/Presentation/BattleView.cs
git commit -m "feat(boss): 蓄力预警 chip 与技能释放提示"
```

---

## 收尾:Unity 侧验证

Task 9 Step 8 的实船配置断言写在 `ConfigLoaderTests.cs`,**dotnet 工装排除了该文件**,必须另跑一次 EditMode 才算验过:

```bash
/Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -projectPath Brushblade -runTests -testPlatform EditMode \
  -testResults /tmp/results.xml -logFile /tmp/unity_test.log
```

编辑器开着时会因项目锁失败 —— 那就让用户在 Test Runner 里跑。

另外,蓄力预警是玩家体感的核心,**必须实机看一眼**:进一场 Boss 战,确认预警 chip 在蓄力回合出现、下回合技能如实释放、推过阶段阈值能取消大招。

## 后续:文档与数值校准

这两项**不属于本计划的实现范围**,但必须在合入后跟进,否则 GDD 与实现脱节、数值失衡:

1. **更新第 8 章 8.5**:补 Boss 技能体系一节,把字表和四个技能写进 GDD;8.5.2 表格加技能列。
2. **重跑平衡仿真**:`tools/balance/` 跑一轮,重新校准 `bossScaleBonus`。第 20 章现有的"新手 8.6 / 卡3级 P50=10 / 卡5级 P50=15"是在 **Boss 打不到玩家**的前提下测的,加技能后必然下滑,`bossScaleBonus` 很可能要往下调。校准后同步更新第 20 章 20.4 的数字。

待校准的首版取值(spec 9.3):`BossChargeEvery = 3`、贯穿玩家份 `×2`、淹没对召唤物全额 `Attack`。
