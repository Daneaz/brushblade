# CLAUDE.md — 《字·斗》(Brushblade)

汉字拆合养成卡牌(局内肉鸽),Unity,海外移动优先(iOS+Google Play),F2P + 奖励式广告 + 单一月订阅(第14章 v0.6)。

## 架构(详见 docs/architecture.md,硬规则勿破)

- 四层 asmdef,依赖单向:`Presentation → {Core, Data, Platform}`,`Data → Core`。
- **Core 与 Data 禁止引用 UnityEngine**(asmdef 已设 `noEngineReferences: true`)——拆合/战斗/生克/跑图全是纯 C#。
- 随机性一律走 Core 内带种子的 RNG,禁用 `UnityEngine.Random`。
- 玩家可见 UI 文案走字符串表,禁止硬编码;字形/拼音/释义是游戏数据(配置表),不进字符串表。
- 轻服务端:仅校时/存档校验(19.9);变现=奖励式广告+单一月订阅,无强制广告、无货币直购(第14章)。

## 规则的唯一来源

- 五行相克(相克 ×1.5 / 被克 ×0.5 / 心中立;**相生 ×3 已于 2026-09-02 取消**):`docs/design/wuxing-reference.md`,其规格例即 `WuxingResolverTests` 用例。
- 数值:`docs/design/第10章-战斗数值框架.md`,字表数值为**基础值**,乘数结算时套用。
- 配方拍板:一步合成(Mode A,Q1 已关闭)。
- 起手抽卡与战利品共用一条稀有度权重:`MetaRules.RarityWeights`(2026-09-06 起,
  出阵已废止)。起手 = 五行各一张加权抽 + 最高档保底一张,不去重。

## 目录

- `Brushblade/` — Unity 项目(6000.5.2f1,勿升版本);代码在 `Assets/_Project/{Core,Data,Platform,Presentation,Tests}/`。
- `tools/pipeline/` — Python 数据管线(IDS → 候选字表);产出 `out/` 与原始数据 `data/raw/` 不入 git。
- `docs/design/` — GDD 全 18 章 + 五行规格;`docs/architecture.md` — 代码架构。

## 测试与验证

```bash
# 管线(pytest)。⚠ 六个目录都要跑:漏掉 tools/icons/ 会让「手写了一张 PNG、
# 绕过整条 SVG→PNG 管线」这种改法全绿通过(2026-09-04 栽过一次);漏掉 tools/design/
# 就看不到数值脚本(rebalance_2026_09_05.py)与 chars.json 的对账测试;
# 漏掉 tools/branding/ 看不到 App 图标与 Theme.cs 的配色对账;
# 漏掉 tools/monetization/ 看不到「广告发奖有没有过 AdGate」的接线对账 ——
# 后两个是 2026-09-21/22 新增的,加新目录记得同步这一行
python3 -m pytest tools/pipeline/tests/ tools/fonts/tests/ tools/icons/tests/ \
  tools/design/tests/ tools/branding/tests/ tools/monetization/tests/ -q

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

- ⚠️ **图标(`icon_*.png`)是产物,不能手写**:它们由 `tools/icons/build_icons.py` 从手写 SVG
  生成。加一枚要同时改**三处** —— `build_icons.ICONS`(SVG 片段)、`test_icons.EXPECTED`、
  `Icons.cs` 的 `Glyphs`(PNG 取不到时的兜底汉字,新汉字还要重跑字体子集)。缺任一处的后果
  不是编译错,是上线渲染成「?」。改完跑 `python3 tools/icons/build_icons.py`(同时刷新
  `svg/*.svg`,仓库里那份也在对账)。`.meta` 脚本不生成,从同目录别的 `icon_*.png.meta`
  复制并换掉 `guid:`。(2026-09-04 加 icon_melee 时手写过一张 PNG,三处对账一处都没接。)

- TDD 的项目范围:Core/Data 每个模块必须走;Presentation 不强求自动化测试,**但改完必须过离线编译**
  ——工装只编译 Core/Data,Presentation 的编译错会一路漏到用户打开 Unity 才炸(已发生过两次)。
  离线编译依赖 `Brushblade/Library/ScriptAssemblies/`(Unity 至少打开过本工程一次)。
  只看 `error CS`,`warning MSB3245` 是 Unity 程序集自带的无关引用,忽略。
- ⚠️ **离线编译只证明「能编过」,不证明「接上了」。** Presentation 没有自动化测试,
  下面两类缺陷全绿的测试一条都抓不到,只能靠人看 —— 2026-09-02 势/水势那批**两样都栽了**:
  1. **新增玩家可见的战斗状态,三处必须同时接**:`StatusText.Of` 的 `case`(不加就落
     `default: return None`,详情弹窗静默跳过这一行)、`BattleView` 的状态 chip 列表(手写的
     kind 列表,不在里面就不显示)、`CharInfo` 的效果文案(`EffectsText` 的 switch,缺分支会
     在卡面上印出**英文枚举名**)。当时 1494 条测试全绿,而势/水势在游戏里从头到尾不可见。
  2. **给共享的交互状态赋予新含义时,要回头查它所有的读取点。** 同一个字段被两条路径读、
     只改了其中一条,是这一层最常见的静默 bug:`_pendingAttackMode` 曾被拖放路径漏设、
     `_allyTargeting` 从「纯友方字专用」扩成「双向态也用」后 `onDrop` 那条旧分支没跟上,
     两次都是**方向判反且 AP 照扣**。改完 `grep` 一遍那个字段的每一处读写,别只改手上这条路径。
- ⚠️ **改 Core 的公开签名或删除 Core 的公开成员时,`tools/coretests`/`tools/prescompile` 之外
  还有三个工程 `Compile Include` Core 的源码**——`tools/balance`、`tools/chestsim`、`tools/trace`,
  CLAUDE.md 现有的验证命令清单扫不到它们,得逐个 build:
  ```bash
  cd tools/balance   && /Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet build --nologo -v q
  cd tools/chestsim  && /Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet build --nologo -v q
  cd tools/trace     && /Applications/Unity/Hub/Editor/6000.5.2f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet build --nologo -v q
  ```
- ⚠️ **在 git worktree 里跑 prescompile 要覆盖程序集路径**——`Brushblade/Library/` 不入 git,
  新 worktree 里不存在,得借主检出的(否则 CS0006 找不到 UI/TMP):
  ```bash
  dotnet build --nologo -v q -p:ProjectAsm=/Users/eugenewu/code/game/Brushblade/Library/ScriptAssemblies
  ```
  coretests 与 pytest 在 worktree 里直接跑,无需前置(`tools/fonts/raw/` 自 2026-08-25 起
  已入 git —— 旧稿要求的那条软链**不再需要**,2026-09-02 实测确认;`tools/pipeline/data/raw/`
  本来就不用管,管线测试只吃白名单里的 `ids.txt`)。
- ⚠️ **worktree 与主检出共享同一个 stash 栈**:`git stash pop` 会弹出别人未提交的改动。
  要对比改动前后用 `git show HEAD:<path>` 或 `git diff`,**别用 stash**(2026-09-02 有两个
  agent 各踩一次,其中一次在 BattleView.cs 上留下了别的分支的冲突标记)。
- ⚠️ 测试断言只用 Unity 版 NUnit 也支持的 API:**禁用 `Is.AnyOf`/`Is.All.AnyOf`**(dotnet 工装的
  NUnit 3.14 有、Unity 自带 NUnit 没有,工装绿≠编辑器绿)。多选一用 `Is.EqualTo(a).Or.EqualTo(b)`,
  集合子集用 `Has.All.Matches<T>`。
  同一个坑:**`Does.Not.Contain(x)` 只在 x 是 string 时能用** —— Unity 自带 NUnit 只有字符串
  子串那个重载,传 int/枚举是编译期 CS1503(2026-08-27 栽过一次)。集合判包含一律写成
  `Assert.That(list.Contains(x), Is.True/False, "…")`,断在 bool 上最稳。
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

## UI 设计流程(硬规则)

- **先设计、后实现**:任何玩家可见的 UI 界面(新增或改版)都必须先经 Claude Design 出稿,
  用户确认通过后才能动 Presentation 代码。设计稿未通过 = 不开工。
- **本项目全流程不发布 artifact**(`.claude/settings.json` 已设 `enableArtifact: false`)。
- **设计统一由常驻 session `designer` 完成**,本 session 不自己出稿:
  1. 先 `ListAgents` 查找名为 `designer` 的 session;
  2. 存在 → 用 `SendMessage` 把需求(界面、场景、约束、要落库的路径)发给它,等它交稿;
  3. 不存在 → **停下**,提示用户开启或恢复 `designer` session,不要自己代为设计。
- **一个界面从设计到上线走六步**,每步的产物落在下表对应目录:
  1. **出稿**:`designer` 出稿,落 `drafts/<功能>/`,在 `drafts/README.md` 登记一行,状态 `待审`。
  2. **拍板**:用户确认 → 状态改 `已拍板 · 待实现`。未拍板不开工。
  3. **实现**:尺寸、颜色、文案取自拍板稿与 `system/`(稿上 pt × 2.093 = 逻辑单位),不自己发明新值;
     需要新 token / 新组件 → 先补进 `system/` / `component/`。
  4. **设计验收(硬性,不做不算完工)**:实机或编辑器截图与拍板稿**逐条**并排比对,列出全部差异。
  5. **差异分流**,每条只有两个去向:
     - **实现走样**(漏做、尺寸算错、用错色)→ 改代码,稿不动;
     - **稿子不成立**(技术做不到、真机放不下、试玩后改主意)→ **回填**:改稿,并在该稿目录的
       `README.md` 记一条「改了什么 · 为什么」。回填只写在稿旁边,不散落到别处。
  6. **归档**:稿状态改 `已落地`;同一个提交里把 `current/` 对应的现状卡按代码更新;被新稿替代的旧稿
     状态改 `已取代` 并移入 `deprecated/`(用户确认后删)。
  **同一屏任何时刻只有一份算数**:实现前是拍板稿,实现后是 `current/`。两边冲突以 `current/` 为准。

  | 层 | 目录(`docs/design/` 下) | 放什么 |
  | --- | --- | --- |
  | 规范 | `system/` | 原则、文案、颜色与对比度、字体等规则;`tokens.json`(token 唯一出处)、字体、资产规格 |
  | 组件 | `component/<名>/` | 可复用件的卡(`README.md` + `preview.html`);共用样式 `component/bundle.css` |
  | 现状 | `current/<名>/` | 游戏现在的样子,与实现对齐(`README.md` + `preview.html`) |
  | 设计稿 | `drafts/<功能>/` | 画布稿 `canvas.json` + `*.dc.html` + 回填记录 `README.md`;索引与状态见 `drafts/README.md` |

  规范只放规则与 token,不放画好的东西。单文件 HTML 演示放 `demos/<主题>/`。
- **落盘的稿必须附带「本地直接打开就能看」的版本**,只存源稿不算落盘完成。
  `preview.html` 与 `*.dc.html` 离开 artifact 运行时是裸 HTML —— 类型注入的 tokens、`bundle.css`、
  `support.js` 都不在源稿里,本地看与线上差很多(2026-10-03 栽过)。
  1. 本地预览页由脚本从源稿生成、不手写,并配对账测试(每份源稿都有本地页、`var(--x)` 全有定义、
     产物可再生)。现成范例:`tools/design/build_ds_preview.py` → `docs/design/_preview/`
     (规范 / 组件 / 现状三层 + 每份设计稿画布一页 `canvas-<目录>.html`),测试 `tools/design/tests/test_ds_preview.py`。
     改了 `tokens.json`、任何 `preview.html` 或 `canvas.json` 后重跑脚本。
     画布 `*.dc.html` 写死 `<script src="./support.js">`:本地由 `docs/design/drafts/dc-runtime.js`(从 Design 类型
     原样取下的运行时)顶上,**新建画布目录时要加软链接** `ln -s ../dc-runtime.js docs/design/drafts/<目录>/support.js`
     (测试会查)。画布稿直接打开即可交互。
  2. 验收要在浏览器里实看,不能只比 sha256(sha256 只证明源稿一致,不证明看得到)。用
     `python3 -m http.server` 起本地服务再打开;预览面板直接开 `file://` 会变成静态快照,相对路径的 CSS 不加载。
  3. `.gitignore` 忽略 `docs/design/drafts/**/*.html`(只放行 `*.dc.html`),别把 `preview.html` 或单文件 HTML 放进 `drafts/`。

## 当前阶段

v0.7(2026-07-13 拍板):**层段化无尽为唯一核心玩法**(第 20 章),章节关卡制废止。局内拆合战斗/宝箱/商城/收集不变;实现顺序:Core 无尽引擎(层段/缩放/遭遇生成/结算)→ 断点续爬存档 → 无尽 UI 替换章节地图。后端分期 P0 校时(就绪)→ P1 云存档 → P2 排行。
