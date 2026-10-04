角色页整屏（932×430pt 横屏）：左执笔人档案 · 右上等级里程碑轨道 · 右下统计四页签；按 `CharacterView.cs` 再现。从主界面左栏（整块面板可点）进入，`GameRoot.ShowCharacter` 接线，「返回地图」回 `ShowMap`。
本页所有 pt = 代码常量 ÷ 2.093，所有文案 = `StreamingAssets/config/strings.zh-CN.json` 的取值。`preview.html` 上半是默认页签「宝箱」的整屏，下半是另外三个页签（只换右下面板）。

## 版面（逻辑单位 → 稿面 pt）

- **骨架**（`Rebuild`）：安全区内，顶栏 `TopBarH` 80（38.22）；正文从顶栏下沿到底边上 17（8.12）；左右两列栏距 `Gap` 21（10.03）。正文高 362.66。
- **顶栏**（`BuildTopBar`，骨架照抄 `PerkView`）：标题「执 笔 人」40（19.11）宋体 → 副标「Lv.20 · 秀才 · 最高 43 层」23（10.99）`text-dim` → 弹簧 → `Ui.InkCounter` 字号 25（墨锭 16.72×10.15，数字 11.94，千分位「2,480」）→「返回地图」`Ui.PillButton` 130×63（62.11×30.10、圆角 24 → 11.47、字号 25 → 11.94，`exit-pink`）。行内间距 21（10.03）。
- **左：档案**（`BuildProfile`）：`Ui.OutlinedPanel` 定宽 `ProfW` 419（200.19），圆角 21（10.03）、描边 2（0.96）；内边距 25（11.94）、竖排间距 19（9.08）。
  - 头像行：`Ui.CircleGlyph` 100（47.78，`ink` 底 `paper` 字，字号 44 → 21.02）+ 名「执笔人」36（17.20）宋体 + 等级段位 21（10.03）`text-dim`，行距 21、名与等级间 4（1.91）。
  - 经验：一行「经验 · 距 Lv.21」与「420 / 1050」都是 19（9.08）`text-faint`；下面 `Ui.Bar` 高 10（4.78），轨道 `paper-dim`、填充 `gold`，撑满栏宽。
  - 标题行「属性」25（11.94）宋体 +「下一级」19 `text-faint` + 2 高（0.96）`panel-border` 分隔线。
  - 六行属性（`AttrRow`）：`panel-inset` 圆角 12（5.73）、高 44（21.02）、行距 6（2.87）、左右内边距 15（7.17）、行内间距 12（5.73）。名 21（10.03）`text-dim` 弹性 → 值 27（12.90）宋体（生命 `cinnabar`、其余 `text-main`）→ 增量 18（8.60）定宽 54（25.80）靠右：有增量 `upgrade-text`「+N」，没有写「—」`text-faint`。
  - 底部钉一句封顶说明 19（9.08）`text-faint`，开 Wrap（176pt 宽下折两行）。有欠箱时其下再加一枚 `warn-bg`/`warn-text` chip（字号 19）「待发 N 只每级宝箱 · 箱位已满」—— 本页示例没有欠箱。
- **右上：里程碑轨道**（`BuildMilestones`）：定高 `MsH` 327（156.24），内边距 21/21/21/19（10.03/10.03/10.03/9.08），竖排 17（8.12）。
  - 标题行：「等级里程碑」25 宋体 +「已领 3 / 10 · 每 5 级一档」19 `text-faint` + 分隔线 +「Lv.50 之后每 10 级 · 3,000 墨 + 橙字 3 选 1」19 `text-faint`（后半句读 `MilestoneRules.ForLevel(60)`，不是手抄）。
  - 轨道：十格等分（格距 8 → 3.82，每格约 54.98pt 宽，卡内净宽约 49.25pt）。底线是一根 `Ui.Bar` 高 6（2.87），横跨轨道宽的 5%–95%，竖直中心 = 钉心（距轨道顶 21）；已达成段染 `gold`，比例 = 最后一个 ≤ 当前等级的格序 ÷ 9。
  - 每格（`MilestoneNode`）= 钉 `CircleGlyph` 42（20.07）+ 间距 8（3.82）+ 卡 `OutlinedPanel` 圆角 17（8.12）撑满余高。卡内边距 6/10（2.87/4.78）、竖排 6（2.87）：「Lv.N」27（12.90）宋体 →「墨锭图标 + 1,200」`Ui.IngotLabel` 20（9.56，图标 13.38×8.12）→ 色点 13（6.21，圆角 6 → 2.87）+「金字 3 选 1」17（8.12）`text-dim` → 弹簧 → 底行。
- **右下：统计**（`BuildStats`）：弹性高（196.39），内边距 19/21/21（9.08/10.03/10.03），竖排 17（8.12）。
  - 页签（`TabButton`）：`OutlinedPanel` 圆角 25（11.94）、描边 2；宽 = 字数 × 23 + 58 = 104（49.69）、高 50（23.89）；字 23（10.99）**宋体**（`Theme.TitleFont`；规范里页签走宋体，不算按钮）。选中 `ink-soft` 底白字，未选 `panel-paper` 底 `panel-border` 边 `text-dim` 字。页签间距 13（6.21）。
  - 页面区：横排、间距 29（13.86），竖分隔线 2（0.96）`panel-border`。

### 四个页签

| 页签 | 内容 |
| --- | --- |
| 宝箱（`BuildChestPane`） | ① 开箱列定宽 268（128.05）：表头「开箱 · 共 N 只」19 `text-faint` + 七行（`ListRow` 高 33 → 15.77：色点 17 → 8.12 + 档名 21 + 数 21 靠右）。② 开出字卡列定宽 234（111.80）：七档稀有度。数为 0 的整行连色点一起压成 `text-faint`。③ 保底进度（弹性宽，竖排 8 → 3.82）：标题一行 + 金 → 橙 → 红三块（`PityBlock`：色点 17 +「金字 · 鎏金匣及以上」21 + 「3 / 5」27 宋体，布局盒高按同行正文给 30（14.33），数字照常居中画出；`Ui.Bar` 高 10 填稀有度色；提示 18 `text-faint`「再开 2 只鎏金匣及以上必出金字」）。 |
| 登塔（`BuildTowerPane`） | 一排五张 `StatTile`：登塔次数 / 累计清层 / 最高层 / 击败 Boss / 阵亡。 |
| 战斗（`BuildBattlePane`） | 左：「出手最多 · 前 10」+ 5 × 2 小牌（`Ui.MiniGlyphTile` 64×80 → 30.58×38.22 + `Ui.RankBadge(tile, i+1, 27f, 10f, 17)` → `ink-soft` 圆 12.90、探出牌外 4.78、白色粗体 8.12；牌右间距 10 → 4.78 + 次数 21 → 10.03 定宽 50 → 23.89）。行内格距 21（10.03）、行距 10（4.78）。没有数据时只一行 `map.hero.top_empty`。右：合成 / 拆解并排 + 最高单次伤害通栏。 |
| 经济（`BuildEconPane`） | 一排三张 `StatTile`：累计获得墨锭 / 累计花费墨锭 / 看广告领奖。 |

`StatTile`：`OutlinedPanel` `card-face`、圆角 17（8.12）、高 130（62.11），内边距 21/19（10.03/9.08）、间距 8（3.82）；标签 20（9.56）`text-dim`、数值 46（21.98）宋体千分位。贴顶排，不被页高拉长。

## 读法 / 交互

- **窄屏横向滚动**：右栏（里程碑 + 统计）是 `Ui.HScrollFill` 横向滚动区，最小排版宽 `RightMinW` = 1265（932pt 基准机上右栏的实际宽）。宽屏铺满、拖不动；16:9（画布宽 1600）下右栏视口约 914，按 1265 排、横向滚出约 351。首次打开若有可领的里程碑，自动滚到能看见它；切页签、领取后整页重建时保持滚动位置。左栏档案不滚。

- **里程碑三态**：已领 = 金钉 + 白色 `check` 图标（钉内缩 10 → 4.78）+ 卡 `panel-inset` 底、Lv 字 `text-dim`、底行「已领」18（8.60）`text-faint`；可领 = 金钉（无图标）+ 卡 `gold-soft` 底 3（1.43）`gold` 描边、Lv 字 `text-main`、底行「领 取」`Ui.PillButton`（`gold` 底 `gold-text` 字、21 → 10.03、高 44 → 21.02、撑满卡宽）；未达 = `panel-border` 圆 + 内缩 3 的 `panel-paper` 圆（看着是一圈描边）+ 卡 `card-face` 底、底行「差 N 级」。
- 「领 取」不用朱砂：一屏可能同时有好几格可领，朱砂一屏只许一颗主钮（spec §6.2）。点了打开 `MilestonePickSheet`（见 `current/MilestonePickSheet`），领完回调 `Rebuild` 整页重画。
- **轨道窗口**（`Window`）：Lv.50 及以内恒显示表内十档 5…50；过了 50，从最早一个没领的那档起往后 10 档，全领完取最后 10 档，不足 10 档向前补；候选多取一个步长，好露出「当前等级之后的下一档」。
- 属性值读 `MetaRules.BuildBattleConfig`（与局内、主界面同源，含技能加成）；「下一级」增量只按等级曲线算 `f(L+1) − f(L)`，AP 恒写「—」。暴击不上屏。
- 页签切换 = 改 `_tab` 后整页 `Rebuild`（只保留右栏横向滚动位置 `_rightScrollX`，其余状态不保留）。
- 保底的展示顺序是金 → 橙 → 红（把 `ChestRules.PityRules` 的红 → 橙 → 金倒过来读），读的是真实计数 `GoldPity / OrangePity / RedPity`。

## 文案

`character.*` 一组（标题、副标、经验行、属性标题、封顶说明、里程碑、页签、统计各格）；复用 `map.hero.*`（头像字、名、等级段位、属性名、百分号、经验值、Top10 空态）、`levelup.stat_delta`（「+N」）、`common.back_to_map`；稀有度名走 `CharInfo.RarityName`，箱名走 `ChestRules.TierName`。

## 示例数据（`preview.html`，当前规则下合法）

- Lv.20：`XpToReach(20)` = 10,450，本级需 100 + 50 × 19 = 1050，示例已得 420。
- 未点技能：生命 880 / 攻击 138 / 护甲 12 / 闪避 10% / 速度 110 / AP 3；下一级增量 +20 / +2 / 0 / 0 / 0。
- 里程碑已领 5、10、15，Lv.20 可领；底线 3/9。
- 宝箱：开箱 90 只、开出 407 张。保底计数与开箱数对得上 —— 金只数鎏金及以上（19 只）、橙只数朱漆及以上（8 只）、红只数赤霄（2 只，红字 0 张 → 红保底 2/20）。稿上的「红 12/20」配「赤霄 0 只」在规则下不可能，已换。
- 墨锭余额 2,480 = 累计获得 48,230 − 累计花费 45,750。

## 与拍板稿的差异（`drafts/character/Character.dc.html`，按代码画）

1. **顶栏排布**：稿是「返回」钮在最左、标题其后、墨锭在最右；代码按全站惯例「标题在左，墨锭 + 返回在右」，钮文案是「返回地图」（`CharacterView.BuildTopBar`，`CharacterView.cs:74–97`，类注释自认这一条）。
2. **墨锭数**：千分位与稿一致（「2,480」，`Ui.InkText`）；稿的墨锭图标带金色内框、数字粗体，代码是纯 `ingot-dark` 六边形、常规字重（`BuildTopBar`）。
3. **字距**：稿标题、头像名、页签、「领 取」都有 `letter-spacing`；代码 Unity `Text` 没有字距，间隔只靠字符串里的空格。
4. **经验条**：稿轨道 `quiet`（#E4DDCE）、填充 `rarity-gold`（#C9A94A）、高 5；代码 `Ui.Bar` 轨道 `paper-dim`、填充 `gold`（#CA9D33）、高 4.78（`BuildProfile`，`CharacterView.cs:129`）。
5. **属性行**：稿底色 #F2EEE4、生命值 `fire-glyph`（#B02D2E）；代码底色 `panel-inset`（#F1EBDE）、生命值 `cinnabar`（`AttrRow`，`CharacterView.cs:140, 168`）。
6. **封顶说明**：稿把「Lv.50」挑出来加深；代码一整条 `text-faint` 标签（`BuildProfile`，`CharacterView.cs:155`）。代码另有稿上没有的欠箱 chip（`CharacterView.cs:159–161`，来自 spec §5.1）。
7. **里程碑标题**：稿「等级里程碑」`text-quiet` 12 加字距；代码 `text-main` 11.94、无字距（`TitleRow`，`CharacterView.cs:557–563`）。
8. **里程碑卡**：
   - 未达卡稿用 `card-face` + `box-border`（#E7E1D4）描边；代码 `CardWhite` + `panel-border`（`MilestoneNode`，`CharacterView.cs:262–264`）。
   - 已领卡稿把 Lv 与墨锭都压成 `text-warm`；代码 Lv 用 `text-dim`、墨锭数仍是 `text-main`（`CharacterView.cs:273–275`）。
   - 可领钉稿外套一圈 3pt `gold-soft` 光环；代码只有金色实心圆（`CharacterView.cs:244`）。
   - 墨锭：稿是带金色内框的小墨锭 + 粗体数；代码 `Ui.IngotLabel` 纯墨锭 + 常规字重（`CharacterView.cs:275`）。
   - 色点：稿 7×7 圆角 2；代码 13 → 6.21、圆角 6 → 2.87（`Dot`，`CharacterView.cs:277`）。
   - 「领 取」：稿宋体 700、高 20、圆角 10；代码 `Ui.PillButton` 黑体 500、高 21.02、圆角 11.47（`CharacterView.cs:286–289`）。
   - 轨道底线：稿横跨 4%–96%、高 3；代码 5%–95%、高 2.87（`BuildMilestones`，`CharacterView.cs:218–224`）。
   - 格距：稿 6；代码 3.82（`space-4`）。稿的 6 下每格卡内净宽只有约 47pt，而「色点 + 间距 +『金字 3 选 1』」按 Noto 字宽实算 47.7pt，Unity `Text` 不折行会探出卡边；收窄格距换出约 2pt。
9. **宝箱页**：
   - 表头：稿把总数挑出来加粗 `text-main`；代码一整条 `text-faint` 标签（`Column`，`CharacterView.cs:573`）。
   - 清单行：稿数值加粗，零值行字压成 `text-mute`、色点保留稀有度色；代码数值常规字重，零值行字与色点一起压成 `text-faint`（`ListRow`，`CharacterView.cs:578–587`）。
   - 红保底：稿写「红字 · 赤霄匣」；代码模板统一是「{rarity}字 · {tierName}及以上」→「赤霄匣及以上」（`PityBlock`，`CharacterView.cs:430–431`）。
   - 保底栏：稿竖排 7；代码 3.82（`space-4`），「3 / 5」的布局盒压到同行正文行高。按 uGUI 实算，原值下三块首选高合计 333 > 页面区 304（逻辑单位），竖排会按比例把各行压扁；现在 287，放得下。
   - 金保底提示：稿「再开 2 只鎏金匣及以上,下一只必出金字」；代码三条同一模板「再开 2 只鎏金匣及以上必出金字」（`character.pity.hint`）。
10. **登塔 / 经济页**：稿每格数值带单位（次 / 层 / 只）并有一行说明（「含断点续爬算一次」「弃塔不算阵亡」「登塔 · 宝箱 · 图鉴 · 里程碑 · 广告」…）；代码 `StatTile` 只有标签 + 数字（`StatTile`，`CharacterView.cs:508–520`）。
11. **战斗页 Top10**：稿小牌是「五行淡底 + 2pt 稀有度框」的 30×38 方块，次数下有一行「次」；代码是 `Ui.MiniGlyphTile`（稀有度牌框素材 30.58×38.22），只写次数（`BuildBattlePane`，`CharacterView.cs:478–482`）。名次圆标 13 / 探出 5 / 8 号字与稿一致（`Ui.RankBadge(tile, i + 1, 27f, 10f, 17)`，`CharacterView.cs:479`，37e70f97）。合成 / 拆解 / 最高伤害三格同第 10 条，没有单位。
