# UI 基线 · 六屏

《字·斗》全部游戏界面的**视觉与交互基线**。与代码不一致时，先确认是稿子过时还是实现走样——
两边都可能，但任何一边动了都要在这里留痕。

前身是 `docs/design/frame/字斗设计板.dc.html`（已移除），BattleView 的部件池注释曾引它。

## 设备基准

iPhone 16 Pro Max 横屏 **932 × 430pt**（@3x = 2796 × 1290px），锁横屏。
安全区左右各 59pt、底部 21pt（Home Indicator）——所有可点元素都在这个框内。

⚠ `CanvasScaler` 是 `referenceResolution 1600×900` + `match = 1`（按高匹配），
所以这台机上逻辑画布实为 **1950 × 900**：横向比参考宽 350，纵向一分没多。
`1pt = 2.093` 逻辑单位。详见 `Device.dc.html`。

## 画板

| 文件 | 屏 | 对应实现 |
| --- | --- | --- |
| `Home.dc.html` | 主界面 | `MapView.cs` |
| `Main.dc.html` | 卡组（可交互） | `CollectionView.cs` |
| `Battle.dc.html` | 战斗（可交互） | `BattleView.cs` |
| `Bestiary.dc.html` | 怪物图鉴（可交互） | `BestiaryView.cs` |
| `Perks.dc.html` | 技能 | `PerkView.cs` |
| `Shop.dc.html` | 商城 | `ShopView.cs` |
| `RunEnd.dc.html` | 段末 · 告捷/败北 | `BattleView.DrawRunEnd` |
| `SafeLayer.dc.html` | 安全层 | `GameRoot.ShowSafeLayer` |
| `Settle.dc.html` | 登塔结算 | `GameRoot.ShowTowerSettle` |
| `Event.dc.html` | 奇遇 | `BattleView.DrawEvent` |
| `Reward.dc.html` | 战利品 · 选字 | `BattleView.DrawRewardCharStep` |
| `Replace.dc.html` | 字库已满 · 换字 | `DrawRewardReplaceStep` 等四处 |
| `Device.dc.html` | 设备基准与适配 | 规范，无对应实现 |
| `CardStates.dc.html` | 字牌状态 | `Ui.GlyphTile` / `CardFrames` |
| `StatMapping.dc.html` | 详情页数值口径 | `CharInfo` |
| `Dialogs.dc.html` | 弹窗族 | `Ui.Modal` / `ModalShell` / `Alert` 的 13 个调用点 |
| `Popups.dc.html` | 飘字与全屏反馈 | `Juice.cs` / `WuxingChart.cs` |
| `UnitFoe.dc.html` | 单位详情 · 敌人 | `OnEnemyClicked` → `EnemyInfo` |
| `UnitAlly.dc.html` | 单位详情 · 召唤物 | `OnSummonClicked` → `SummonInfo` |
| `UnitMe.dc.html` | 单位详情 · 执笔人 | 实现侧**还没有**入口 |
| `StatusGlossary.dc.html` | 状态词条 29 枚 | `Core/StatusEffect.cs` · `Icons.cs` |
| `UnitSheetAlt.dc.html` | 详情承载方式取舍 | 低保真，无对应实现 |
| `Chests.dc.html` | 七档宝箱立绘 | `Core/Chest.cs` · `MapView.DrawChest` |
| `ChestOpen.dc.html` | 开箱 · 获字 | `ChestRules.TryOpen` · `MapView.ShowChestResult` |

`canvas.json` 是画布布局（位置、分页、便签）。**分页按「屏」组织**（2026-08-29 在画布上重排）：
主界面 / 卡组 / 战斗 / 局内流程 / 怪物图鉴 / 技能 / 商城 / 公共 —— 按「哪一屏用得着」找图，
而不是按「哪一轮加的」。便签里记着每处改动的理由与待办，
**别只看画面不看便签**。`base.css` 是六屏共用的令牌（色板取自 `Theme.cs`，
安全区、触控与字号阶梯）。`cards.min.json` 是从 `chars.json` 生成的字表快照，
仅供稿子填真实数据。

## 重新出图

在本目录下跑（`<skill>` = Claude Code 的 design skill 目录）：

```
node <skill>/seed-canvas.mjs \
  --template <skill>/payload.template.html \
  --out brushblade-card-collection.html --title "字·斗 界面重设计" \
  --artboard Main.dc.html --artboard Home.dc.html --artboard Battle.dc.html \
  --artboard Bestiary.dc.html --artboard Perks.dc.html --artboard Shop.dc.html \
  --artboard Device.dc.html --artboard CardStates.dc.html --artboard StatMapping.dc.html \
  --artboard RunEnd.dc.html --artboard SafeLayer.dc.html --artboard Settle.dc.html \
  --artboard Event.dc.html --artboard Reward.dc.html --artboard Replace.dc.html \
  --artboard Dialogs.dc.html --artboard Popups.dc.html \
  --artboard UnitFoe.dc.html --artboard UnitAlly.dc.html --artboard UnitMe.dc.html \
  --artboard StatusGlossary.dc.html --artboard UnitSheetAlt.dc.html \
  --artboard Chests.dc.html --artboard ChestOpen.dc.html \
  --image mob_jiaohen.png \
  --canvas canvas.json
```

产出的 `.html` 内联了整个编辑器 payload（2.7MB），**不入 git**（见 `.gitignore`）。
浏览器里首次渲染要 30 秒以上，不是卡住了。

## 稿子先行、代码待跟进

这三项在稿上已经画成，实现还没有：

- **每排 4 格位**：`Targeting.RowCapacity` 3→4、`BattleEngine.EnemyCap` 6→8、
  `SummonCap` 6→8、`FrontRowSize` 3→4。会连带影响 `Targeting` 里
  Cleave / Skewer / Sweep 的按列取目标，属平衡改动。
- **敌人护盾**：`EnemyState` 没有 `Shield` 字段。要加字段 + 在 `DamageEnemy` 的护甲减法之后、
  扣血之前插一段吸收，并补 `ShieldBroken` 事件。另有一条待拍板：
  「相克即破甲」那一击目前无视护甲，要不要连护盾一起穿？
- **状态图标扩到 29 枚**：`tools/icons/build_icons.py` 的 ICONS 表与 `Icons.cs` 的 Glyphs
  兜底表要同步（`test_icons.py` 守着两边一一对应），新增兜底汉字须重跑
  `python3 tools/fonts/subset_fonts.py`。

## Home 已接线（2026-08-28）

`MapView.cs` 已按 `Home.dc.html` 重排（三栏 + 底部导航），当天又依试玩反馈改了几轮，
**稿子已回填到与实现一致**。尺寸常量在 MapView 顶部，由稿上的 pt 按 1pt = 2.093 换算，
改一边就同步改另一边。

回填进稿的那几项：左右两栏收窄（角色 214 → 187pt；宝箱先收到 195pt，量出格内两颗 mini
按钮并排放不下、「18墨」被挤成两行，又还回到 232pt），让宽给书塔——净结果书塔 352 → 375pt。
另外书塔内容居中、续爬按钮只留动作而层数下沉成 `.resumedetail`、出阵字牌全量折行不再折叠成
「+N」、底部四页签各一套配色。四枚页签图标的 SVG 路径逐字取自本稿，只加一层 scale
撑到 64 画布（`tools/icons/build_icons.py` 的 `nav_*`）——改稿就重抄一遍，别在脚本里手改坐标。

红点是**动态判据**，稿上画的只是「恰好这三格有事」的一种示例（判据见稿内 `.reddot` 的注释）。

回填时在稿里量出两个它自己的毛病，一并修了：

- `.tower > *` 与 `.mark` 同特异度且排在后面，把水印**拽回了文档流**，那 250px 行高
  把血条/账目/提示三行顶出面板再被 `overflow:hidden` 剪掉（实测内容 478px vs 面板 319px）。
  改成 `.tower > *:not(.mark)`。
- `.who` / `.chests` 是默认的 `content-box`，写着 `width: 214px` 实际占 240px ——
  与实现里那两个「总宽」常量不是一个口径。三栏改 `border-box`，两边的数才对得上。
- `.mini` 会折行，而实现侧 Unity 的 `Text` 是 `horizontalOverflow = Overflow`（**从不折行**）——
  窄格里稿会折成两行还把按钮撑高，实现则是直接顶出按钮，两边看到的不是同一件事。
  稿加 `white-space: nowrap`：现在放不下就直接顶出来，一眼能发现。

两件功能稿子想要而实现还没有，版面上已按「做得到的事」措辞：

- **设置按钮**：项目还没有设置界面，顶栏那颗是占位钮，点了弹说明。真设置做出来时
  只换回调，版面不动。
- **主界面弃塔**：弃塔入口只在战斗内的退出菜单（`BattleView` 的 `onAbandon`），
  主界面点不到 —— 稿上原本那句「也可放弃本趟从第 1 层重开」等于在屏上写一件玩家
  做不到的事，两边都改成了「断点续爬 · 接着上次停下的那层打」。补了弃塔钮再把那句改回来。

宝箱格的按钮文案沿用实现侧原有的（「开箱!」/「开始开启」），没有跟稿改成
「开 启」/「排队等位」—— 纯文案差，改了要连带重跑字体子集，留待一并处理。

## 2026-08-28 补齐：局内流程与弹窗

按「代码里有、稿上没有」把 Presentation 层翻了一遍，补了 8 块。挖出来的缺口：

- `GameRoot` 有 8 处 `NewView`，稿只画了 6 处 —— **安全层**与**登塔结算**两个整屏从来没有版面依据。
- `BattleView` 一个屏里还藏着 5 个独立阶段（战利品选字／字库已满换字／广告复活／奇遇／段末横幅），
  合起来是「一趟塔从打完到结算」的全部决策点，此前一处都没画。
- `Ui.Modal / ModalShell / Alert` 有 13 个调用点散在 6 个界面里，没有任何一张图能回答
  「我们的弹窗长什么样」。
- `Juice.cs` 的 18 种飘字**一直没有稿**。战斗屏 2026-08-15 撤掉顶部提示带之后，
  「这一下打中没有、打了多少」全靠它 —— 等于最要紧的那层反馈没人管口径。

新增的稿都从代码取真实数据（奇遇文案取自 `enemies.json` 的 events，弹窗正文逐条对
`strings.zh-CN.json` 的 key，飘字配色对 `Juice.cs` 里的 `Theme.*`），不是编的。
画的时候定死了一条共有规矩：**凡是不可逆的，都要在按下去之前说清楚** ——
安全层把「深入 / 撤退」的取舍写在钮下面而不是一行小字，换字把「永久失去」做成标红告警条。

⚠ 这 8 块是**稿子先行**：实现侧那几屏还是旧版面（`GameRoot` 里的安全层与结算是两张
朴素的居中卡，`BattleView` 的各阶段沿用 `ModalShell` 默认外壳）。要落地按稿改，
不是反过来把稿改回去。

## 顺带查出的两处不一致

- `Tutorial.DemoChar` 是「刺」，而 `strings.zh-CN.json` 的四条 `battle.hint.tutorial.*`
  写的是【剑】——新手会被指去点一张手上没有的牌。
- `chars.json` 的 `Pinyin` / `Gloss` 一条都没填，字段与 `ConfigLoader` 都在，
  卡面拼音位现在是空的。稿里 72 个字的拼音释义是补的，需要过一遍再写进详表。

## 2026-08-29 新增：单位详情弹窗（第四页）

战斗里点敌人 / 召唤物 / 执笔人，弹同一张详情。三件事一张稿：**状态逐条带说明**（含 debuff 与 DoT）、
**基本描述 + 被动/主动的特性说明**、**立绘**。

- 三类单位共用一个骨架（立绘 88 方 + 名与三条 + 左状态右特性 + 底部提示行），只换内容不换版式。
- 状态说明写在名字底下，**不做二级弹窗**——弹窗上再弹一层，就得先关掉这层才看得回战场，
  而玩家点开详情正是为了对着战场看。
- `StatusGlossary.dc.html` 是那句说明的**唯一出处**：29 枚图标 + 4 条走文字 chip 的能力，
  逐条写清机制 / 挂在谁身上 / 时长口径 / 能不能清掉。落地时它是 `status.*` 两族 strings key 的底稿。
- `mob_jiaohen.png` 是敌人立绘，从 `Presentation/Mobs/Resources/` 的四层压平并缩到 256：
  `magick enemy_jiaohen_body.png enemy_jiaohen_face.png -composite enemy_jiaohen_wisp.png -composite
  enemy_jiaohen_state.png -composite -resize 256x256 -strip mob_jiaohen.png`
  （层序取自 `MobAssets.Layers` + MobView 的 state 层）。

⚠ **稿子先行**，实现侧还是老样子：现在点敌人/召唤物走 `Ui.Modal(title, body)`——一段
StringBuilder 拼出来的长文本，没有立绘、没有图标；**点执笔人根本没有入口**。要落地得动四处：
玩家条加点击入口；`EnemyInfo` / `SummonInfo` 从「返回整段文本」改成返回逐条结构；新建
`status.*` 的 strings key；以及新文案上线前**重跑字体子集**。

顺带查出：`Battle.dc.html` 里 碉 写的是「血 60 / 攻 10 / 反伤 20」，而 `chars.json` 现在是
「血 120 / 攻 0 / 反伤 50」（2026-08-25 字表重构之后）——本页按 `chars.json` 画，战斗稿那格待回填。

## 2026-08-29 新增：七档宝箱立绘

七只箱是**七种材质**不是七种颜色。现在 `MapView.DrawChest` 画的是一个 `Theme.ChestColor` 色块
加「素」「竹」这样的首字——七档只有色相之差，而它们的等待时长差着 5 分钟到 12 小时。

`Chests.dc.html` 把工艺按档位排开：纸 → 竹 → 瓷 → 木 → 金 → 漆 → 铁函，越往上五金越多、
轮廓越「抬头」，赤霄那只干脆关不严（缝里透光）。轮廓各不相同是刻意的：40px 缩略图下细节
全看不见，认的是外形与那一块平涂色。

- **这一页的 SVG 可以直接当素材**：箱子是器物不是活物，矢量墨线画得住，不必像 mob 那样出
  512 方图。要与 mob 那批 painterly 立绘统一，页内附了逐档的中英文出图关键词——底稿用本页
  SVG 作 ControlNet 输入，只许长材质与光、不许改轮廓。
- **三态只改叠加层**：未开始（满不透明）／计时中（45% + 沙漏角标）／已就绪（金光晕 + 七道光芒
  + 盖缝透光，箱身 1.6s 一起一落）。一只箱一张素材，省 21 张图。已就绪那层与箱型无关
  （`fx-ready`），套在任何一只箱下面都成立。
- 数值与色值逐条取自 `ChestRules` 与 `Theme.ChestColor`，改平衡要同步这一页。

落地四处已全部做完，见「2026-08-30 已接线」。

## 2026-08-29 补：开箱 · 获字，与分页重排

这三样是**在画布上直接改的，已原样回填**（线上版本 `1787998827-1bdf`）：

- **分页按屏重组**：原来的四页（六屏 / 局内流程 / 规范 / 单位详情）拆成八页，每块图归到
  它服务的那一屏——宝箱立绘进「主界面」，单位详情进「战斗」。
- **新增 `ChestOpen.dc.html`（开箱 · 获字）**：宝箱是未收集字的唯一来源，而「开了之后
  发生什么」此前一张稿也没有，只写在 `MapView.ShowChestResult` 里。这一页画全了五步流程、
  结果面板实尺、牌脚三态（新字 / 重复字 / 满级）、`TryOpen` 的五道闸与七档产出表。
- **`Home.dc.html` 的宝箱格接上立绘**：33×25 色块 + 首字 → 40×40 方图，「已就绪」的
  光晕 / 起伏 / 盖缝三段动效一并接上（格内容高 98 → 113px，栏宽与格数没动）。

该页自记的落地清单（左右分栏、定尺、新字牌脚做重）已全部做完，见「2026-08-30 已接线」。
**箱名与卡名已分家**（2026-08-29）：卡组稿原先给稀有度另起了一套雅名（素纸 / 竹青 /
青瓷 / 紫檀 / 鎏金 / 赤金 / 朱漆），与七档箱名撞车——「朱漆」同时是第 6 档箱和第 7 档卡。
雅名已全稿移除：**卡按 `strings.zh-CN.json` 的 `char.rarity.*` 叫白绿蓝紫金橙红，
箱按材质叫 xx 匣**；往后单字说的是卡、带「匣」说的是箱。

## 2026-08-30 已接线：宝箱立绘 + 开箱结果面板

「主界面」这一页的三张稿（`Home.dc.html` 宝箱格 / `Chests.dc.html` 七档立绘 /
`ChestOpen.dc.html` 开箱获字）从**稿子先行**转为**已实现**。稿没动，动的是代码。

### 立绘走的是这一页的 SVG 本身

`Chests.dc.html` 自己写着「本页的 SVG 可以直接当素材用」——照办了，没有再出一批 painterly 图。
新增 `tools/design/build_chests.py`：七只箱的 path **逐字抄稿**，只把 `var(--c)` 换成
`Theme.ChestColor` 的实色，rsvg-convert 出 256 方 PNG 进
`Presentation/Chests/Resources/`。改稿就重抄一遍，别在脚本里手改坐标——与
`build_icons.py` 的 `nav_*` 同一条戒律。页内那张「出图关键词」表仍然有效：往后要与 mob
那批统一，拿现在这批 SVG 作 ControlNet 底稿即可，轮廓已经定死了。

分层比 mob 少两层（箱子是器物不是活物）：`body` 三态共用，`seam` 是盖缝的光、逐档出
（缝的 y 各档不同）。加上与箱型无关的 `chest_fx_ready` / `chest_fx_timing`，共 16 张。

- `ChestAssets.cs` —— 与 `MobAssets` 同构的前缀表 + 缓存；取不到返回 null。
- `ChestView.cs` —— 三态。计时中把 `body` 压到 45% 加沙漏角标；已就绪跑稿上那 1.6s
  的三段（光晕 .45↔1、盖缝 .55↔1、箱身起落 2.5/120），三段**同相**，合起来是一次呼吸。
- `MapView.ChestArt` —— 素材缺失回落成色块 + 首字（`Icons.cs` 的双轨）。图标位
  33×25 → **40×40**（84 逻辑单位）；格高吃的是 2×2 网格原有的余量，`ChestW` 没动。
- `tools/design/tests/test_chests.py` —— 守四条：七档与 `ChestTier` 一一对应、
  C# 的 slug 表与生成器一致、仓库 SVG 与生成器同步、Resources 里 16 张 PNG 与 `.meta` 齐全。

### 开箱结果面板：竖排 → 左右分栏

`ShowChestResult` 拆成 `BuildResultLeft` / `BuildResultGrid` / `ResultFoot` 三段。

- **定尺**：`0.16~0.84` 的比例锚点换成安全区内左右吃满、上下各留 15pt。根节点已在
  `SafeAreaFitter` 之内，所以这就是稿上的 814×380（16 Pro Max）。
- **牌网格**：≤12 张走 6 列、>12 张走 8 列，牌 174×218 / 126×158 逻辑单位。这两个尺寸是
  **上限不是定值**——格子在行里可被压窄，比 16:9 更方的屏上牌一起变小而不是溢出。
  牌与牌脚同缩靠的是格内 VStack 的 `childForceExpandWidth`。
- **牌脚三态**：新字实心胭脂底（`ExitPink` = 稿上的 `#7A3F5C`）／重复字未凑满走
  `PaperDim` 中性底、凑满转 `AdGreenBg` 翠玉底／满级走 `GoldSoft`。此前新卡与重复卡
  只差颜色（`ExitPink` vs `AdGreenBg`），一眼扫过去分不出。
- **新增三条文案**（稿上左栏画着的）：`map.chest.result_count` / `result_hint` /
  `result_tip`。⚠ 引入了「事听屏情详」五个新字形，**已重跑** `tools/fonts/subset_fonts.py`。
- `Theme` 加了 `GoldSoft` / `GoldDeep` 两色（稿上 `#F6EDD5` / `#8F6B09`），墨锭条与
  满级牌脚共用。

### 箱色与卡色并成一套（拍板）

原先是两张表，而它们**只差橙这一档**：朱漆 `#D4602A` vs 稀有度橙 `#E1791B`，其余六档
逐字节相同。并的方向是**归到稀有度色**——箱档与卡档本就是同一条白绿蓝紫金橙红的阶梯。

- `Theme.ChestColor` 改成 `RarityColor(RarityOf(tier))`，**不再抄一遍数值**。
  新增的 `RarityOf` 是显式一张对照表而不是 `(CardRarity)(int)tier` 强转——强转在两个枚举
  长度再次分家时会静默错位，显式表则编译不过。（2026-08-29 之前拆成两张表，正是因为
  当时 `ChestTier` 只有六档，强转让赤霄匣拿到橙色；补上朱漆匣之后七档一一对应，坑没了。）
- `build_chests.py` 的 TIERS 同步改橙档并重出图——立绘的属性色是**烤进 PNG 的**，
  只改 C# 会让同一档箱在「有素材」与「没素材」两条路上是两个颜色，编译不报、只有肉眼能发现。
- `test_chests.py` 补两条锁：`ChestColor` 必须仍是那句委托；TIERS 的七个色值逐通道
  （容 1，C# 是浮点）对得上 `Theme.RarityColor`。改一边不改另一边就红。
- 稿上朱漆那档的 `--c`、hex 标签、出图关键词表的色格一并改成 `#E1791B`。

⚠ 遮罩仍是 `Theme.Scrim`（55%）而稿上写 62%——那是**全项目共用**的模态遮罩，
为一屏改它会牵动另外十几个弹窗，没动。

