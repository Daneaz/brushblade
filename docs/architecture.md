# 《字·斗》代码架构(Brushblade)

> v1.0 · 2026-07-05
> 定位:工程文档,指导 Unity 项目脚手架与后续全部编码;GDD 侧对应第 16 章(技术架构)。
> 原则:**核心逻辑与引擎解耦**。拆合、战斗、生克、跑图全是纯逻辑(第 4/16 章),demo 时代已验证「纯函数引擎 + UI 层分离」的分法,本文档将其正式化。刻意不做:全模块详设、UML、DLC 扩展点预设计——接口细节在实现时由测试驱动长出来。

---

## 1. 分层与程序集(asmdef)

四层,依赖单向向下,`Core` 不知道任何上层存在:

```
Presentation (Unity 场景/UI/动效)
    │ 依赖
    ▼
Platform (平台服务接口 + 实现)      Data (配置加载/存档序列化)
    │                                │ 依赖
    ▼                                ▼
  (无依赖)                     Core (纯 C# 领域逻辑,零 UnityEngine)
```

| 程序集 | 内容 | 依赖 | UnityEngine |
|---|---|---|---|
| `Brushblade.Core` | 领域模型(部件/字/配方/敌人/法宝)、拆合引擎(dismantle / compose / suggest 提示)、五行生克结算、战斗状态机、跑图状态机、带种子的 RNG | 无 | **禁止** |
| `Brushblade.Data` | JSON 配置表的 schema 与解析(反序列化为 Core 模型)、存档序列化、字符串表(i18n) | Core | 禁止(路径由上层注入) |
| `Brushblade.Platform` | `IPlatformSDK` 接口(成就 / 云存档 / 排行榜可选);`SteamworksPlatform`、`NullPlatform`(编辑器与移动端占位) | 无 | 允许 |
| `Brushblade.Presentation` | 场景(战斗 / 地图 / 局外)、UI、动效、音频接入 | Core + Data + Platform | 允许 |
| `Brushblade.Core.Tests` | Core 与 Data 的 EditMode 单元测试 | Core + Data | — |

**硬规则**:

- `Core` 与 `Data` 的 asmdef 不引用 `UnityEngine`(`noEngineReferences: true`),违者编译报错——这是防腐层。
- 所有随机性走 `Core` 内**带种子的 RNG**,不用 `UnityEngine.Random`。跑图生成、掉落、敌人 AI 可复现,为将来的每日挑战 / 录像回放留后路,成本为零。
- `Presentation` 是唯一持有 MonoBehaviour 的层;战斗表现(动效 / 音效)监听 Core 发出的结算事件,不反向驱动逻辑。

## 2. 目录结构

```
/ (repo 根,git)
├── docs/                        # GDD + 本文档
├── tools/pipeline/              # Python 数据管线(IDS → 候选字表 → 游戏 JSON)
└── Brushblade/                  # Unity 项目(Unity 6 LTS,版本在创建时锁定)
    ├── Assets/
    │   ├── _Project/
    │   │   ├── Core/            # Brushblade.Core.asmdef
    │   │   ├── Data/            # Brushblade.Data.asmdef
    │   │   ├── Platform/        # Brushblade.Platform.asmdef
    │   │   ├── Presentation/    # Brushblade.Presentation.asmdef
    │   │   │   ├── Scenes/
    │   │   │   ├── UI/
    │   │   │   └── Audio/
    │   │   └── Tests/           # Brushblade.Core.Tests.asmdef (EditMode)
    │   ├── StreamingAssets/config/   # 管线产出的游戏 JSON(见 §3)
    │   └── (Art/ Fonts/ 等资源目录,接入时建)
    ├── Packages/
    └── ProjectSettings/
```

- `.gitignore`:Unity 标准(Library/ Temp/ Logs/ obj/ 等)+ 管线大中间产物(`tools/pipeline/out/`)。
- Git LFS:美术 / 音频二进制(png / psd / wav / ttf-otf 大字体)。

## 3. 数据流

```
开源数据(cjkvi-ids / Unihan / CC-CEDICT)
  → tools/pipeline (Python:采集 → 筛选 → 人工精选表 → DAG 校验 → 导出)
  → Brushblade/Assets/StreamingAssets/config/*.json   (入 git 的最终小配置表)
  → Brushblade.Data 启动时加载、校验、反序列化为 Core 模型
  → Core 运行时只读
```

- 配方 / 属性 / 数值 / 效果全部数据驱动(第 4 章 4.9.7:改平衡不改代码)。
- 生克规则(相克 ×1.5 / 相生 ×3 / 心中立)的**数值本身也进配置**,规则语义以 [wuxing-reference](design/wuxing-reference.md) 为准。
- DAG 校验脚本(无环 / 原料存在 / 可达性 / 防卡手覆盖,第 4 章 4.9.6)在管线侧跑,后续接 CI;Data 层加载时做轻量二次校验(fail fast)。
- **存档**:Core 状态可序列化(run 进度 / 字库 / 部件池 / 法宝 / 图鉴),Data 层写本地 JSON(`persistentDataPath`),平台云存档(Steam Cloud 等)由 Platform 层同步,单机无服务端(第 16 章 16.8)。

## 4. 测试策略

- **Core / Data:全覆盖单元测试**(Unity Test Framework,EditMode)。拆合引擎、生克结算、战斗状态机、跑图生成是纯逻辑,demo 时代即有测试习惯(engine / battle / RunEngine 测试全绿),Unity 版延续:**每个 Core 模块先写测试再实现**。
- 生克结算的测试用例直接取 wuxing-reference 的规格例(淋 8→24、焚 18→54→81、壁 8→24)。
- Presentation 不强求自动化测试,靠阶段 1 的真人验证闸门。
- 平台层用 `NullPlatform` 保证编辑器内全流程可跑,Steamworks 只在 standalone build 验证。

## 5. i18n(R13,第一天执行)

- 所有玩家可见文本走**字符串表**(key → 文本),禁止硬编码中文字符串在代码里。
- 首发只带 zh-CN 一张表;表结构即预留了后续英 / 日本地化,短期不投翻译资源。
- **注意边界**:字卡的「字形 / 拼音 / 释义」是**游戏数据**(来自配置表),不是 UI 文案,不进字符串表;进字符串表的是按钮 / 提示 / 奇遇文案 / 系统消息等。

## 6. 落地顺序

1. 用户以 Unity 6 LTS 创建空项目 `Brushblade/`(2D 模板,置于仓库根)。
2. 脚手架:目录 + asmdef + `.gitignore` / LFS + 测试框架 + `CLAUDE.md`(记录本文档的硬规则、命名规范、配置表结构,第 16 章 16.5)。
3. **Steamworks 最小验证**(阶段 0 闸门):空项目导出 Windows 可执行 + 成就 / Cloud 跑通,走 `IPlatformSDK` 接口。
4. 管线重建(`tools/pipeline/`)与 Core 第一个模块(生克结算,测试用例现成)并行开工。

---

*架构文档 v1.0 完*
