# CLAUDE.md — 《字·斗》(Brushblade)

汉字拆合养成卡牌(局内肉鸽),Unity,海外移动优先(iOS+Google Play),F2P + 奖励式广告 + 单一月订阅(第14章 v0.6)。中文沟通。

## 架构(详见 docs/architecture.md,硬规则勿破)

- 四层 asmdef,依赖单向:`Presentation → {Core, Data, Platform}`,`Data → Core`。
- **Core 与 Data 禁止引用 UnityEngine**(asmdef 已设 `noEngineReferences: true`)——拆合/战斗/生克/跑图全是纯 C#。
- 随机性一律走 Core 内带种子的 RNG,禁用 `UnityEngine.Random`。
- 玩家可见 UI 文案走字符串表,禁止硬编码;字形/拼音/释义是游戏数据(配置表),不进字符串表。
- 轻服务端:仅校时/存档校验(19.9);变现=奖励式广告+单一月订阅,无强制广告、无货币直购(第14章)。

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
python3 -m pytest tools/pipeline/tests/ tools/fonts/tests/ -q

# Core/Data 单元测试(首选,不依赖编辑器锁,毫秒级;用 Unity 自带 dotnet SDK)
cd tools/coretests && /Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet test --nologo -v q

# Presentation 离线编译(改完 Presentation 必跑;coretests 盖不到这一层)
cd tools/prescompile && /Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet build --nologo -v q

# Unity EditMode(集成验证;编辑器开着时会因项目锁失败,让用户在 Test Runner 里跑)
/Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -projectPath Brushblade -runTests -testPlatform EditMode \
  -testResults /tmp/results.xml -logFile /tmp/unity_test.log
```

- ⚠️ **新增/改动任何玩家可见中文文案后,必须重跑字体子集**:游戏打包的是**子集字体**
  (只含实际用到的字形),新字不在里面就会在真机上显示成空白/豆腐块——而离线编译和
  Core 单测**都发现不了**,只有 `pytest tools/fonts/tests/` 会红。修法:
  `python3 tools/fonts/subset_fonts.py`,然后复跑该测试。
  重新生成必然产生几百 KB 的时间戳 churn,**光看 diff 大小说明不了变没变,要比 cmap**。
  (2026-08-22「洞穿/横扫/连发/玩家」、2026-08-23「填」各栽过一次。)

- Core/Data 每个模块:先写失败测试再实现;Presentation 不强求自动化测试,**但改完必须过离线编译**
  ——工装只编译 Core/Data,Presentation 的编译错会一路漏到用户打开 Unity 才炸(已发生过两次)。
  离线编译依赖 `Brushblade/Library/ScriptAssemblies/`(Unity 至少打开过本工程一次)。
  只看 `error CS`,`warning MSB3245` 是 Unity 程序集自带的无关引用,忽略。
- ⚠️ **在 git worktree 里跑验证要补两条前置**——不入 git 的目录在新 worktree 里不存在。
  进 worktree 后先建软链,再用参数覆盖程序集路径(2026-08-18 实测:补齐后 coretests 1072、
  pytest 246、prescompile 全绿):
  ```bash
  # pytest:tools/fonts/raw/ 不入 git,而 glyph_refs.py 把路径写死成 __file__/../raw,只能软链
  ln -s /Users/eugenewu/code/game/tools/fonts/raw tools/fonts/raw   # 否则 fonts 测试挂 9 条
  # prescompile:Brushblade/Library/ 不入 git,借主检出的程序集(否则 CS0006 找不到 UI/TMP)
  dotnet build --nologo -v q -p:ProjectAsm=/Users/eugenewu/code/game/Brushblade/Library/ScriptAssemblies
  ```
  coretests 不依赖二者,worktree 里直接跑;`tools/pipeline/data/raw/` 也不用管(管线测试只吃
  白名单里的 `ids.txt`)。
- ⚠️ 测试断言只用 Unity 版 NUnit 也支持的 API:**禁用 `Is.AnyOf`/`Is.All.AnyOf`**(dotnet 工装的
  NUnit 3.14 有、Unity 自带 NUnit 没有,工装绿≠编辑器绿)。多选一用 `Is.EqualTo(a).Or.EqualTo(b)`,
  集合子集用 `Has.All.Matches<T>`。
- ⚠️ 测试里定位仓库根**只能用 `TestContext.CurrentContext.TestDirectory`**,禁用
  `AppContext.BaseDirectory` —— 后者在 Unity Test Runner 下指向**编辑器安装目录**
  (`Unity.app/Contents`),往上永远找不到含 `Brushblade/` 的父目录,读真实字表的测试会整类变红;
  而 dotnet 工装下两者都指向 `bin/`,一直是绿的(2026-08-15 已发生:DefenseValuesTests 15 条
  + PierceBuffCharTests 4 条)。
- ⚠️ 测试代码**禁止直接引用 Newtonsoft**(`JsonConvert` 等):Tests asmdef 是
  `overrideReferences: true` 且只放行 `nunit.framework.dll`,而工装 csproj 有 Newtonsoft
  的 PackageReference —— 又一个工装绿≠编辑器绿(已犯过)。要测序列化就走 `Data.SaveSerializer`
  / `Data.ConfigLoader` 这些真实入口,顺带把真实路径也覆盖了。
  同理:测试只能用 Tests asmdef references 列出的程序集(Core / Data)。
- 提交信息用 conventional commits(feat/fix/docs/chore + 范围)。

## 当前阶段

v0.7(2026-07-13 拍板):**层段化无尽为唯一核心玩法**(第 20 章),章节关卡制废止。局内拆合战斗/宝箱/商城/收集不变;实现顺序:Core 无尽引擎(层段/缩放/遭遇生成/结算)→ 断点续爬存档 → 无尽 UI 替换章节地图。后端分期 P0 校时(就绪)→ P1 云存档 → P2 排行。
