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
| `UnitMe.dc.html` | 单位详情 · 执笔人 | `OnPlayerClicked` → `PlayerInfo` |
| `StatusGlossary.dc.html` | 状态词条 30 枚 | `Core/StatusEffect.cs` · `Icons.cs` |
| `UnitSheetAlt.dc.html` | 详情承载方式取舍 | 低保真，无对应实现 |
| `Chests.dc.html` | 七档宝箱立绘 | `Core/Chest.cs` · `MapView.DrawChest` |
| `ChestOpen.dc.html` | 开箱 · 获字 | `ChestRules.TryOpen` · `MapView.ShowChestResult` |
| `CharSheet.dc.html` | 字卡详情 · 字库牌 | `CharPreview.Show`(战斗长按) |
| `CharSheetDual.dc.html` | 字卡详情 · 双方向字 | 同上,水/土 两面 |
| `CharSheetPart.dc.html` | 字卡详情 · 部件 | 同上,部件池入口 |

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
  --artboard CharSheet.dc.html --artboard CharSheetDual.dc.html \
  --artboard CharSheetPart.dc.html \
  --image mob_jiaohen.png \
  --canvas canvas.json
```

产出的 `.html` 内联了整个编辑器 payload（2.7MB），**不入 git**（见 `.gitignore`）。
浏览器里首次渲染要 30 秒以上，不是卡住了。

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

## 卡组已接线(2026-09-03)

`CollectionView.cs` 已按 `Main.dc.html` 重写:顶栏 / 六系筛选栏 / 左网格右详情,
74 张可收集字全列出来(没拿到的走锁态沉底),网格与右栏各自内部滚动,不再翻页。
尺寸常量在 CollectionView 顶部,由稿上的 pt 按 1pt = 2.093 换算,改一边就同步改另一边。

同一轮改到的另外三处:

- **字牌角标抽成一份**(`Cards/CardBadges.cs`):等级 / 稀有度色点 / 出阵带 / 可升徽标 /
  新字角旗 / 锁态。卡组网格与**开箱结果**共用 —— 此前开箱那批牌连等级角标都没有,
  而 `CardStates.dc.html` 与 `ChestOpen.dc.html` 画的本来就是同一张牌。
  角标尺寸一律按牌自己的高宽算比例(稿上的 103×128pt),所以开箱的 174×218 / 126×158 也对。
- **未拥有态**(`Ui.GlyphTile(..., locked: true)`):牌面褪成宣纸灰、字形压浅、动效整套不挂,
  但稀有度框留三成色相 —— 「那张红卡我还没拿到」是收集页最该说清的一件事。
- **翻卡改成真翻牌**(`Cards/CardFlip.cs`):稿已回填,见 `ChestOpen.dc.html` 的注释。

### 稿与实现的三处已知差异(都是稿子过时,不是实现走样)

- **相生 ×3 徽标已从稿里删掉**:相生于 2026-09-02 全局取消(`wuxing-reference.md` v0.8)。
  `Main.dc.html` 的 `CARDS` 里 `"s"` 字段留着没删,只是不再有任何地方读它;
  `StatMapping.dc.html` 里那一段「相生 ×3 计入显示值」同样作废,**尚未改稿**。
- **新字角旗**:稿画的是 45° 斜带 + `overflow:hidden` 切掉牌外那半。uGUI 里旋转的 Image
  会连带旋转它的裁剪矩形,没有等价写法 —— 实现改成右上角一枚朱砂小方标,
  牌外那圈会呼吸的赭金光(`CardHalo`,2.6s)照稿保留。
- **「新字」是存档态**:`MetaState.UnseenCards`,首次获得即入、在卡组页点开即销。
  起手那 15 张**不标新**(`MetaRules.EnsureStartingCollection`)—— 一进游戏 15 面红旗
  在呼吸,这个信号就废了。稿上没交代这条。

另外稿上没画、实现保留的一处:**升级前的确认弹窗**。升级是不可逆支出,
弹窗族的口径是「凡是不可逆的,都要在按下去之前说清楚」。

## 字卡详情弹窗补稿(2026-09-03)

战斗里长按一张字牌弹出来的那个窗,此前**一张稿也没有** —— 而它有三个入口
(字库牌 / 部件池牌 / 战利品候选牌),三处共用同一个 `CharPreview.Show`。
现在的实现是「一张放大的牌 + `CharInfo.Detail` 那一整串文字 + 知道了」,
拼音、释义、稀有度、属性、配方、等级、效果全挤在一个文本块里。

补了三张,对应三类字:`CharSheet`(字库牌 · 单向)、`CharSheetDual`(双方向字 · 水土)、
`CharSheetPart`(部件)。三处入口共用一套版面,只有内容不同。定下的口径:

- **与单位详情同一族**:墨遮罩 + 宣纸圆角卡 + 右上角 ✕,**只读**。
  「长按只看不出手」这条语义不变(`HoldToPreview` 松手不补发点击),所以卡里一个操作钮都没有。
- **卡比 UnitFoe 那三张矮 38px**(760×312、顶边 12)是为了把**被长按的那一张牌**
  留在弹窗下面看得见 —— 「我按的是哪张」与「这张字什么用」得能对上,
  否则弹窗像凭空冒出来的。部件那张同理,底下留的是部件池行。
- **生克对位**是这一屏独有的东西:卡组页只能说「金克木」,战斗里能直接说
  「对场上这四只各是多少」。口径同 `WuxingResolver`(克 ×1.5 / 被克 ×0.5 / 其余 ×1.0);
  护盾与治疗那一面不过生克。
- **数值按本场卡等级缩放**,与卡组页数值格同口径(`MetaRules.ScaleByCardLevel`)——
  卡面印的是这一级真正打出来的数,不是基础值。
- 部件那张不列稀有度与等级(部件两样都没有),身子换成「能凑出什么」,
  与拆合台的可合成列表同一套读法,缺料的标出缺哪一个。

**2026-09-03 当天已接线**(`CharPreview.cs` 重写):`Ui.Sheet` 加了个 `lift` 参数
(卡片相对屏心上抬,默认 0 = 居中),字卡详情传 90 单位 = 43pt,就是为了空出底下那条手牌行。
战斗侧传 `CharPreview.BattleContext`(场上敌人 / 部件池 / 本场可合字);开箱与商城不传,
右栏自动退回卡组页那种静态「克谁 / 被谁克」。

回填进稿的两处:

- 底部那句原写「松手回战场,**再点这张牌才出手**」—— 在**战利品候选牌**那个入口是错的,
  那里点牌是「选这张字」不是出手。三处入口共用一句,只留在三处都成立的那半句。
- 数值格补了「溅射 / 穿透」两格,是与 `StatMapping.dc.html` 那条「穿甲、偷袭这些没量级的
  信息留在功能行里」的**刻意分歧**:那条口径是给卡组页定的,那里你在挑牌;这里你在选目标,
  溅多少、穿几点护甲正是这一下要算的账。功能行照旧把两件事都写全。

⚠ 生克对位读的是 `EnemyState.ApparentElement` 而不是 `Element`:伪装怪显示的是**假**属性、
生僻字受击两次前是 null(印「未现形」)。读错了是静默的 —— 屏上只是多出一个「克制」标签。

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

⚠ ~~这 8 块是**稿子先行**~~ **2026-09-01 全部已接线**（轮三，见
`docs/superpowers/plans/2026-09-01-轮三-局内流程串联.md`）：

- `Ui.Sheet` 抽出统一浮层外壳，`Ui.ModalShell` 与 `UnitSheet` 共用，弹窗卡按稿补上 1pt 描边，
  同屏只留一个弹窗（Task 1）。
- 四个换字入口（战利品 / 回合掉字 / 奇遇 / 广告复活）合并成一版 `DrawReplaceSheet`，
  横排不折行 + 标红告警条（Task 2）。
- 战利品与复活补给的效果说明落到牌下的定高横条，不再拼进标题行（Task 3）。
- 奇遇四件收进一块垂直居中的版面，不再散在三条战场排上（Task 4）。
- 段末横幅改整屏纸色罩 + 居中大字，墨色横带撤掉（Task 5）。
- 安全层与登塔结算改整屏版面，取舍写在钮下面，宝箱行独立成块（Task 6）。

回填的五处「稿比代码旧」（Task 7）：`SafeLayer` / `Settle` / `RunEnd` / `Dialogs`
（离塔弹窗「弃塔」按钮）共四处**墨锭半额**（2026-08-30 已取消——`Dialogs` 这处任务书
清单原没列，是执行时按本节自查命令 `grep 半额/减半/全额结算` 另外查出的），以及 `Reward`
的**部件候选**（2026-08-04 起不再给部件）。此外 `Popups` 另有四处**对齐实现口径变更**——
直伤朱砂改五行分色（8-30）、飘字 18→19 种补 `enemy_mend`、撤相生环图（8-31）、
全屏闪从两处补全到四处并改正颜色描述——性质与前面「稿比代码旧」的五处不同，不计入其中。

## 顺带查出的两处不一致

- `Tutorial.DemoChar` 是「刺」，而 `strings.zh-CN.json` 的四条 `battle.hint.tutorial.*`
  写的是【剑】——新手会被指去点一张手上没有的牌。**2026-08-31 已解决**：不是去补这四条旧文案，
  而是屏底单行提示整体被四步故事弹层取代，新的 12 条 `battle.coach.*` 从头按「刺」写对
  （配方『朿』『刂』）。顺带一提：旧文案描述的是直伤行为，而剑其实是召唤字、刺才是直伤字，
  所以对齐到刺之后文案反而更准确了。详见「2026-08-31 已接线」。
- `chars.json` 的 `Pinyin` / `Gloss` 一条都没填，字段与 `ConfigLoader` 都在，
  卡面拼音位现在是空的。稿里 72 个字的拼音释义是补的，需要过一遍再写进详表。

## 2026-08-29 新增：单位详情弹窗（第四页）

战斗里点敌人 / 召唤物 / 执笔人，弹同一张详情。三件事一张稿：**状态逐条带说明**（含 debuff 与 DoT）、
**基本描述 + 被动/主动的特性说明**、**立绘**。

- 三类单位共用一个骨架（立绘 88 方 + 名与三条 + 左状态右特性 + 底部提示行），只换内容不换版式。
- 状态说明写在名字底下，**不做二级弹窗**——弹窗上再弹一层，就得先关掉这层才看得回战场，
  而玩家点开详情正是为了对着战场看。
- `StatusGlossary.dc.html` 是那句说明的**唯一出处**：30 枚图标 + 4 条走文字 chip 的能力，
  逐条写清机制 / 挂在谁身上 / 时长口径 / 能不能清掉。落地时它是 `status.*` 两族 strings key 的底稿。
- `mob_jiaohen.png` 是敌人立绘，从 `Presentation/Mobs/Resources/` 的四层压平并缩到 256：
  `magick enemy_jiaohen_body.png enemy_jiaohen_face.png -composite enemy_jiaohen_wisp.png -composite
  enemy_jiaohen_state.png -composite -resize 256x256 -strip mob_jiaohen.png`
  （层序取自 `MobAssets.Layers` + MobView 的 state 层）。

四处都已落地（玩家条加点击入口 / `EnemyInfo` `SummonInfo` 从「返回整段文本」改成返回逐条结构 /
`status.*` 的 strings key / 新文案上线前重跑字体子集），从**稿子先行**转**已接线**，
见下面「2026-09-01 已接线：单位详情」。

顺带查出：`Battle.dc.html` 里 碉 写的是「血 60 / 攻 10 / 反伤 20」，而 `chars.json` 现在是
「血 120 / 攻 0 / 反伤 50」（2026-08-25 字表重构之后）——本页按 `chars.json` 画。
**2026-08-31 已回填**，见「2026-08-31 已接线：战斗屏本体」。

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
  ⚠ 但那个开关会让格子**向外**也报出弹性宽（uGUI 的 `GetChildSizes` 里
  `if (childForceExpand) flexible = Mathf.Max(flexible, 1)`），行里多出来的宽就全分给了格子——
  12 张时余量小看不出来，**3 张（素纸匣）时三格瓜分整条右栏**，牌被拉成横条。
  所以格上必须再钉一个 `LayoutElement`：`preferredWidth = 牌宽`、`flexibleWidth = 0`
  （`layoutPriority` 1 压过布局组的 0），`minWidth` 故意不设，留着窄屏仍能整排压窄。
  少于一行的箱（素纸 3 / 竹简 4 / 青瓷 6）是这条的常驻用例，改这块要拿 3 张回归。
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

## 2026-08-31 已接线：战斗屏本体

`Battle.dc.html` 整页从**稿子先行**转**已实现**——上一轮标着「稿子先行、代码待跟进」的
敌人护盾与状态图标两条这次一并接线，连同三栏骨架、格内布局、Boss 跨列、引导弹层等全部落地。
稿本身基本没动，动的是代码去对齐稿子。

- **敌人护盾**：`EnemyState` 加了 `Shield` 字段，`DamageEnemy` 在护甲减法之后、扣血之前插一段
  吸收，`BattleEvent.Absorbed` 带出被吸收的量并写进快照。⚠ **来源留白**：用户 2026-08-30
  拍板不改 `enemies.json`、不做结盾技能——盾的来源将来是「加盾辅助怪给同伴挂 buff」，那类
  小怪还没设计，所以真机上敌人的 `Shield` 恒为 0、盾条看不见，验证全靠 `EnemyShieldTests`
  那 5 条。这一点专门写清楚，否则以后有人会以为没做。刻意**不新增 `BattleEventKind`**，
  复用既有的 `Absorbed` 字段（玩家侧 `EnemyAttack` 同口径）；既有的 `ShieldBroken` 是
  「Boss 倾覆清空**玩家**护盾」，语义不同，没有挪用。
- **相克即破甲穿不穿盾（拍板）**：**不穿，盾照常吸收**。护甲是「硬度」、护盾是「一层临时
  血」，两回事——连盾一起穿会让护盾对带对属性的玩家形同虚设。
- **状态图标扩到 30 枚（不是 29）**：`StatusGlossary.dc.html` 自己记着「持续治疗没有
  图标——29 枚里缺这一枚，落地要补第 30 枚」，补了 `heal`（新画，已回填进
  `StatusGlossary.dc.html` 与 `Battle.dc.html` 两张稿的 `icdefs`）。其余 11 枚逐字抄自
  `Battle.dc.html` 原有的 `icdefs`。
- **三栏骨架换布局组**：`BuildSkeleton` 从「比例锚点 + 手算纵向预算」改成
  `Horizontal/VerticalLayoutGroup + LayoutElement`，直译稿的 flex——顶上那 40 行手算加法
  整段删掉了，它描述的是已不存在的布局，留着比没有更糟。稿的三条 flex 语义：
  `.erow { flex: none }` 四排各自锁高、`.divider { margin: auto 0 }` 用两个
  `flexibleHeight = 1` 的空 Spacer 把富余对半堆到敌我之间、中区 `flex: 1`。
- **安全区内缩补到 `SafeArea.cs` 共用**：`MissingInset()` 从 `MapView` 提出来。每个界面都
  挂在 `SafeAreaFitter` 下，但编辑器 16:9 下 `Screen.safeArea` 等于全屏，那一层什么都不
  做；而稿上 `68 + 6 + 602 + 6 + 132 = 814 = 932 − 59×2`，中区那个 602pt 是被 `.safe`
  内缩定义出来的。不补的话唯一那条弹性轴差 27%。
- **格内竖排改横排**（敌我两侧都是）：立绘在左、信息列在右。中区横向本就富余，横排后
  格高由立绘单独决定，纵向反比竖排省 24px/排，省出来的全给敌我之间的留白。召唤物立绘
  因此反而从 34/28 放大到 48/36pt。
- **每排恒定 4 格 + 列号居中往外**：`Targeting.RowCells` 「两排都 ≤1 只就折叠成一格」
  那条特例整个删掉，改由 `ColumnOrder = {1,2,0,3}` 让单怪自然落在中间偏左——顺带解决了
  那条特例当年要修的毛病（2026-08-23 实机反馈「单怪铺三格会被顶到最左」）。
- **Boss 跨列（列区间）**：`EnemyDef.ColumnSpan`（默认 1），`EnemyState.Column` 语义收紧
  为**起始列**，占据 `[Column, ColumnEnd)`。`Skewer` 改区间相交、`Cleave` 改区间相邻、
  `Chain` 的 `GridDistance` 改半列中心距——`Span = 1` 时与旧写法逐字节等价，既有测试一条
  未动。⚠ 用户拍板「Boss 将来肯定会配小怪」，口子做在 Core 里而不是只留注释。
- **行动条配色照稿改**：稿上 `.foe`/`.ally`/`.me` 三种单位的行动条底色同为 `#3D4E69`
  （= `Theme.InkSoft`），且有 `.soon` 态（>80% 时敌方转朱砂 `#C53637`、我方转绿
  `#2E7D46`）；实现原本全是 `Theme.Gold` 且无 soon 态。
- **召唤物血条改绿**（稿 `.ally .hpb` 是 `#2E7D46`，玩家与敌人是红）——敌我一眼分清。
- **相生环图去掉**：用户拍板，战斗中要实时查的是「我这张字克不克它」，相生 ×3 由配方
  静态决定、属牌面信息（长按详情弹窗已经显示）。左栏只留相克那张，`WuxingChart.Mount`
  的 `sheng: true` 分支保留不删——图鉴一类页面仍可能用得上，删的只是战斗屏这一个调用点。
- **左栏配字表从五行三级目录改平铺列表**：稿上 `.missing` 就是一行一条「字 缺 N」。左窄栏
  只有 68pt（142 逻辑单位），三级目录的二级那排 4 个 38 宽的钮 + 间距 = 164 已经超出栏宽。
- **新手引导改四步故事弹层**：屏底一行「◆ 提示」改成屏幕中央的「一句道理 → 一个动作 →
  一句结果」。遮罩只压 38%（不是全项目共用的 `Theme.Scrim` 55%）——要看得见被点名的那张
  牌/那只怪。卡片高度内容驱动（`ContentSizeFitter` + `ForceRebuildLayoutImmediate`）。
  旧的单行提示与四条 `battle.hint.tutorial.*` 整体被替换，新的 12 条 `battle.coach.*`
  从头按「刺」写对（配方『朿』『刂』），顺带修正了「顺带查出的两处不一致」里记的那处
  指错。
- **新增基础件 `Ui.ScrollList`**：本仓库第一个 `ScrollRect`，为拆合台的「可合成」列表
  （稿 `.craft` 是 `overflow-y: auto`）。此前列表长了会把「结束回合」钮顶出卡片。

### 落地时量出的稿自身毛病

- **`.divider` 在稿里定义了两次**：`:115` 的 `margin: auto 0; width: 86%; rgba(...,.26)`
  与 `:189` 的 `width: 74%; height: 1px; rgba(...,.3)`。同特异度、后者在后，浏览器实际
  渲染的是 74%/.3。已在稿里去重（保留渲染生效的值，把 `:115` 独有的 `margin: auto 0`
  并进去）。
- **部件池的「下回合掉 1 个」是过时文案**：`BattleEngine` 里明写「部件不再掉落——五行
  部件只能靠拆字获得（拆免 AP 是这条的对冲）」。已从 `Battle.dc.html` 删掉这条提示
  （`.poolnote`）。
- **「碉」的数值过期**：稿写「血 60 / 攻 10 / 反伤 20」，而 `chars.json` 现在是
  「血 120 / 攻 0 / 反伤 50」（2026-08-25 字表重构之后）。README 早就记着这条待回填，
  这次一并按 `chars.json` 改了稿。

## 2026-09-01 已接线：单位详情

「2026-08-29 新增：单位详情弹窗」那节记的四处缺口——玩家条没有点击入口、`EnemyInfo`/`SummonInfo`
只会拼一整段文本、`status.*` 没有 strings key、新文案没过字体子集——本轮全部落地，从**稿子先行**
转**已接线**。新增 `UI/UnitSheet.cs`（骨架）+ `UI/UnitDetail.cs`（三类单位共用的数据结构）+
`UI/PlayerInfo.cs`（执笔人的详情数据，全新，此前不存在），`EnemyInfo`/`SummonInfo` 各加一个
`Sheet(...)` 方法（**追加**，老的整段文本方法一个没删——`EnemyPreview` 与图鉴还在用）。

- **三类单位共用一张骨架，只换内容不换版式**：`UnitSheet` 只认 `UnitDetail`，不认识
  `EnemyState`/`SummonState`/`BattleEngine`，内部没有任何「如果是敌人/召唤物/执笔人」的分支——
  三张权威稿（`UnitFoe`/`UnitAlly`/`UnitMe`）本来就是同一张骨架，内容差异全靠 `UnitDetail`
  的字段为 null 表达（比如执笔人 `Element`/`Wuxing` 恒 null，没有立绘时 `PortraitPrefix` 为
  null 落成墨底字块）。这样以后要改版式只改一处，三类单位一起跟着变，不会有「敌人那屏改了
  召唤物那屏忘了」的漂移。
- **不做二级弹窗**：状态说明直接写在详情面板的左列（`Ui.ScrollList`），不再弹一层新窗——弹窗
  上再弹一层，就得先关掉上面那层才能看回战场，而玩家点开详情本来就是想对着战场核对信息
  （这只怪还剩多少甲、我身上这层减速还剩几回合），中间插一层「关闭再看」的动作正好打断这件事。
- **`status.*` 文案只有三种时长口径**（`StatusText.cs` 的注释原话）：按回合递减
  （`status.duration.turns`，「剩 N 回合」）、按层数/次数消耗（`.stacks`/`.charges`，不随
  回合掉，用掉才减）、`TurnsLeft = -1` 的本场持久（`.persistent`，固定文案不带数字）。另外
  还有几个不挂在这三类上的固定态（`.next_turn` 下回合生效、`.ability`/`.until_revealed`/
  `.persistent_trait` 这几个「清不掉的天生特性」），但玩家真正会盯着看「还剩多少」的状态
  只走前三种口径。
- **执笔人第一次有了入口**：`_bottomRow`（玩家条）自己身上挂了个透明 `Button`，点击触发
  `OnPlayerClicked`——与 `OnEnemyClicked`/`OnSummonClicked` 同一套纪律，**选目标态优先**：
  `_allyTargeting` 时够不到治疗目标（`!CanHealSlot`）直接忽略，不落到看详情分支，同样是为了
  不让玩家以为自己点歪了；`AttachAllyTargetPicker` 的选中覆盖层挂在 `_bottomRow` 的子物件上，
  子物件的 Graphic 天然盖住父物件自己的 Graphic，点击先命中那层，两套响应不需要额外互斥判断。
- **`EnemyPreview` 为什么保留**：它按 `EnemyDef` 画（图鉴用的是配置数据，不是某一场具体战斗
  里的敌人实例），`BestiaryView` 传的也是 `EnemyDef`，拿不到 `Shield`/`Statuses`/`ActionMeter`
  这些只有 `EnemyState` 才有的实时字段。战斗里改走 `EnemyInfo.Sheet(EnemyState)`，正是因为
  详情弹窗要显示的是「这只怪现在带了多少甲、身上挂着什么」——图鉴要的是静态资料，战斗要的是
  实时状态，两件事拿的是两种不同形状的数据，没必要也不该合成一个方法。
- **`MetaState` 穿透进 `BattleView`**：执笔人详情要显示「养成技能 · 局外」那四条（每回合行动点
  /字库容量/起始生命上限/每关护盾），这几个数字挂在 `MetaState` 上，`BattleEngine` 只吃养成
  算好的最终数值、不认识「哪一级」「哪个技能」这些养成层概念，`PlayerInfo.Sheet` 要自己算就
  得拿到 `MetaState`。`BattleView.Init` 因此新增一个 `MetaState meta` 参数，`GameRoot` 唯一
  调用点传它已有的 `_meta` 静态实例——不新建、不重新 `MetaStore.Load()`，与 `MapView`/
  `CollectionView`/`BestiaryView`/`PerkView` 四个界面拿 `MetaState` 的既有模式一致。
- **刷新是事件驱动，跟 `Refresh` 走，不是每帧**：详情开着时，`BattleView.Refresh()` 每次都会
  用 `_unitSheetSource`（记着当前详情该拿哪份数据）重新取一份 `UnitDetail`，整体重建一次
  `UnitSheet`；数据源返回 null（比如召唤物被打死）就顺手关掉详情，不抱着空数据崩。全量重建
  的代价是 `Ui.ScrollList` 的滚动条会被弹回顶部——`UnitSheet.Show` 因此在重建前记下旧实例的
  `ScrollRect.normalizedPosition`，重建后原样恢复，玩家翻到第 5 条以后不会被冷不丁弹回开头。

### 落地时量出的稿自身毛病

- **`StatusGlossary.dc.html` 的横扫词条曾写错机制**：改前的原文是「按**列**溅射到相邻目标」，
  错两处——横扫是命中主目标所在整**排**，不是列；「溅射到相邻」说的其实是 `Cleave`
  （溅射：主目标 + 同排左右相邻）。判定是稿错而不是代码错的依据是 `Core/TargetShape.cs`
  这个枚举本身的注释：`Sweep`（横扫）= 主目标所在整排（≤3）、`Cleave`（溅射）= 主目标 +
  同排左右相邻（≤3）、`Skewer`（贯穿）= 主目标所在整列，前排 + 后排（≤2）——这是判定生克/
  连锁等一切目标选取逻辑的唯一权威来源，代码这边只有这一处定义，没有第二份互相矛盾的口径
  可去怀疑。已在稿里改成「命中主目标所在**整排**（≤3），百分比是溅射伤害占比」。⚠ 同一节的
  **贯穿**词条（`ic-skewer` 那条，稿上标「穿刺」，说明写「按列贯穿到后一排」）核对下来是对的，
  与 `Skewer` 的定义一致，没有被误改。
- **稿上画的 `✕`（U+2715）关闭钮，两支源字体都不含这个字形**：`tools/fonts/raw/` 下的
  `NotoSerifSC[wght].ttf`/`NotoSansSC[wght].ttf` 逐一查过 cmap，两支都没有 U+2715——真上线用
  这个符号会渲染成空框/豆腐块。判定是稿的问题而不是代码的问题：代码这边 `UnitSheet.cs` 的
  关闭钮用的从来不是这个字形，而是 `×`（U+00D7，数学乘号），两支源字体都含 U+00D7，运行时
  显示正常，稿上画的符号和代码实际使用的符号本来就不是同一个字符。⚠ 顺带给以后提个醒：
  **稿上能画出来的符号，不等于游戏字体里真的有**——同样查过三张详情稿其余的非 CJK 符号
  （`· × — → − ≥ ±`），逐个核对下来两支源字体都含，这一批没有潜伏风险，只有 `✕` 这一个
  例外。

