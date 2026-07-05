# CLAUDE.md — 《字·斗》(Brushblade)

汉字拆合肉鸽卡牌,Unity 单机,Steam 优先 + 海外移动端,Premium 买断制。中文沟通。

## 架构(详见 docs/architecture.md,硬规则勿破)

- 四层 asmdef,依赖单向:`Presentation → {Core, Data, Platform}`,`Data → Core`。
- **Core 与 Data 禁止引用 UnityEngine**(asmdef 已设 `noEngineReferences: true`)——拆合/战斗/生克/跑图全是纯 C#。
- 随机性一律走 Core 内带种子的 RNG,禁用 `UnityEngine.Random`。
- 玩家可见 UI 文案走字符串表,禁止硬编码;字形/拼音/释义是游戏数据(配置表),不进字符串表。
- 纯单机:无服务端、无账号、无内购、无广告。

## 规则的唯一来源

- 五行生克(相克 ×1.5 / 相生 ×3 去重不叠乘 / 心中立):`docs/design/wuxing-reference.md`,其规格例即 `WuxingResolverTests` 用例。
- 数值:`docs/design/第10章-战斗数值框架.md`,字表数值为**基础值**,乘数结算时套用。
- 配方拍板:一步合成(Mode A,Q1 已关闭)。

## 目录

- `Brushblade/` — Unity 项目(6000.5.2f1,勿升版本);代码在 `Assets/_Project/{Core,Data,Platform,Presentation,Tests}/`。
- `tools/pipeline/` — Python 数据管线(IDS → 候选字表);产出 `out/` 与原始数据 `data/raw/` 不入 git。
- `docs/design/` — GDD 全 18 章 + 五行规格;`docs/architecture.md` — 代码架构。

## 测试与验证(先测试后实现,TDD)

```bash
# 管线(pytest)
python3 -m pytest tools/pipeline/tests/ -q

# Core/Data 单元测试(首选,不依赖编辑器锁,毫秒级;用 Unity 自带 dotnet SDK)
cd tools/coretests && /Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet test --nologo -v q

# Unity EditMode(集成验证;编辑器开着时会因项目锁失败,让用户在 Test Runner 里跑)
/Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -projectPath Brushblade -runTests -testPlatform EditMode \
  -testResults /tmp/results.xml -logFile /tmp/unity_test.log
```

- Core/Data 每个模块:先写失败测试再实现;Presentation 不强求自动化测试。
- 提交信息用 conventional commits(feat/fix/docs/chore + 范围)。

## 当前阶段

阶段 0→1:脚手架已就绪,Core 首模块生克结算已落地。接下来按第 17 章阶段 1 做战斗原型(拆合引擎 → 战斗状态机 → 火流派完整),Steamworks 验证放最后。
