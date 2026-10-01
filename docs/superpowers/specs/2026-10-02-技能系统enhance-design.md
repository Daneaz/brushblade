# 技能系统 enhance — design

日期:2026-10-02 · 状态:待用户审阅 · 前置:2026-09-07 技能树重构、2026-09-13 五行 L2 特色机制

## 0. 范围

一次性改动三棵树 + 新增五条战斗机制 + 五行树 L1/L2 改为「三段式」节点。跨树三节点(相济/融会/博采)不动。

## 1. 数据模型:三段式节点

**方案 A(采用)**:每一段是一个独立的 `PerkNodeDef`,新增两个字段:

- `Stage`:1~3;单段节点恒为 1。
- `NodeKey`:同节点三段共用(如 `metal_1`),用于字符串表 key、画布布局、图标。单段节点 `NodeKey == Id`。

Id 规则:单段节点 `{branch}_{depth}`(不变);三段节点 `{branch}_{depth}_s{stage}`。

`Value` 存**本段增量**,于是 `Bonus` / `ElementBonus` 的求和逻辑不变,点满三段 = 最终值。

前置(`PrereqMet` 的隐式推导扩展):
- 三段节点第 k 段(k ≥ 2)需同节点第 k−1 段。
- 某层第 1 段需上一层**任意 ≥1 段**(上一层是三段节点时即其 `_s1`)。
- 跨树节点的显式谓词照旧;`CountOwned` 按段计数 —— 只影响「≥1 个」判定,与现有前置(都是 count=1)结果一致;缩放计数 `PerDeepWuxingNode` 只数 Depth ≥3,L3/L4 都是单段,不受影响。

存档:`UnlockedPerks` 仍是 id 列表。项目未上线,不写迁移;旧 id(如 `metal_1` 原起手多抽)若残留,`ById.TryGetValue` 已会忽略。

备选 B(`MetaState.PerkLevels` 字典)要改存档、求和、解锁三处,不采用。

## 2. 五行树

新层序:**L1 新专精(三段)→ L2 本系效果值(三段,原 L3)→ L3 专属机制(原 L2)→ L4 天花板**。
原 L1「该系起手格额外抽 1 次」整条删除(`PerkEffect.ElementDrawRolls` 删除;`MetaRules` 起手抽取次数只剩基础 + 博采)。

门槛 / 定价:

| 层 | 段 | 角色等级 | 墨锭 |
|---|---|---|---|
| L1 | s1/s2/s3 | 4 / 6 / 8 | 100 / 200 / 300 |
| L2 | s1/s2/s3 | 10 / 12 / 14 | 200 / 400 / 600 |
| L3 | — | 16 | 1200 |
| L4 | — | 22 | 2400 |

### 2.1 L1(新,三段;只对**打出那张字的元素 = 本系**生效)

| 枝 | 名 | 效果 | 每段增量 → 满 |
|---|---|---|---|
| 金 | 砺刃 | 金系字暴击率 +N% | 5/5/5 → 15 |
| 木 | 深根 | 木系字召唤的召唤物最大生命 +N% | 10/10/10 → 30 |
| 水 | 甘霖 | 水系字治疗量 +N% | 10/10/10 → 30 |
| 火 | 添薪 | 灼烧每层伤害 +N | 3/3/4 → 10 |
| 土 | 筑垒 | 土系字护盾量 +N% | 10/10/10 → 30 |

- 判据与 L2「本系效果值」同源:`CharDef.Element`。现有字表中灼烧/治疗/护盾/召唤分别只出现在火/水/土/木字上。
- 深根:召唤物生成时按百分比放大 MaxHp 与初始 Hp(向下取整)。
- 甘霖:乘在水系字的治疗量(含 HoT 每跳、复活回血)上;**不**放大归根的阵亡回血(那是木系来源)。
- 添薪:**只作用于敌人身上的灼烧**。新增 `BattleConfig.EnemyBurnPerStackBonus`,仅在敌人侧结算(`SettleBurnOn`)时加到每层伤害上:`层数 × (_burnPerStack + bonus)`。玩家侧 `SettlePlayerBurn` 与召唤物侧 `SettleSummonBurn` 不读它 —— 敌方给我方上的灼烧不享有添薪。`BattleConfig.BurnPerStack` 恢复为纯基础值 20,不再由 perk 写入。
- 新增 `PerkEffect`:`ElementCritChance`、`SummonHpPercent`、`HealPercent`、`ShieldPercent`、`EnemyBurnBonus`(原 `BurnPerStack` 效果删除)。BattleConfig 新增对应字段;**缺省 0 = 逐字节恒等**。

### 2.2 L2(原 L3,改三段)

锐锋/荣木/润下/炎上/敦厚:本系字效果值 +5/+5/+5 → 满 15%。效果不变,只是拆段。

### 2.3 L3(原 L2,单段)

| 枝 | 名 | 变化 |
|---|---|---|
| 金 | 锋芒 | 不变(暴击 +1 层战意) |
| 木 | 归根 | 召唤物阵亡回复其最大生命 20% → **30%** |
| 水 | 溢流 | 不变 |
| 火 | 余烬 | 不变 |
| 土 | **固本**(原反震,重做) | 战斗结束护盾衰减 50% → **25%**(玩家与召唤物一致) |

固本:`PerkEffect.ShieldReflectPercent` 及其战斗内反弹逻辑删除;新增 `ShieldCarryPercent`(perk 值 25)。RunEngine 两处 `/= 2`(`_carriedNormalShield`/`_carriedPersistShield`、`CaptureAliveSummons`)改为 `× (50 + bonus) / 100`,bonus = 0 时与 `/2` 整数结果一致。

### 2.4 L4(单段)

| 枝 | 名 | 效果 |
|---|---|---|
| 金 | **断金**(原鏖战) | 打出金系字时若战意 ≥ 5:本张字伤害 +300%(×4),结算后清空战意 |
| 木 | 择伐 | 不变 |
| 水 | 涌泉(重做) | 涌泉相报伤害 +150%(×2.5),结算后返还所耗层数的 50%(向下取整) |
| 火 | 燎原(重做) | 敌人灼烧结算(扣血、自减一层)之后若**仍有层数**,其上下左右相邻的存活敌人各 +1 层灼烧 |
| 土 | **积土**(原磐固) | 厚积薄发伤害 +150%(×2.5),结算后返还所耗层数的 50%(向下取整) |

- 战意上限固定 5、厚/泉上限固定 10:`MoraleCap`/`HeftCap`/`WellspringCap` 三个 perk 效果删除,BattleConfig 字段保留缺省值。
- **断金**(取「其利断金」):不新增存档状态 —— 出字时检查战意层数即可。只对金系**字**生效,金系**部件**(从部件池直出的)不触发也不消耗。只放大伤害类连续值(DamageSingle/DamageAll 等,白名单与 `TakesElementPercent` 的伤害部分同源),乘在既有全部乘区之后。同一张字结算中途因暴击涨到 5 层的,不在本张触发。不造成伤害的金系字(纯 buff)不消耗战意。
- **涌泉/积土**:SpendWellspring/SpendHeft 清空 n 层并结算伤害后,返还 ⌊n × 50%⌋ 层(10→5、7→3、1→0)。0 层释放整条空转,维持既有纪律。
- **燎原**:相邻 = 同列另一排 + 同排列号 ±1;跨 2 列的 Boss 按其占据的每一列算邻居。
  - 触发点只有一处:该敌人自己的灼烧结算。先扣血、自减一层,剩余层数 > 0 才扩散。例:行动前 2 层 → 结算后 1 层 → 扩散;行动前 1 层 → 结算后 0 层 → 不扩散。
  - 扩散施加的那 1 层本身不触发扩散(施加不是触发点);要等收到它的敌人下一次自己结算灼烧,且结算后仍有层数,才会继续扩散。
  - 结算致死的敌人不扩散(余烬那条照常转移)。不灭(BurnNoDecay)下层数不减,只要有层就扩散。

## 3. 被动树

元枝 HP:100/200/300 → **300/500/1000,不叠加**:生命加成取已点亮的最高一档(点满 = +1000)。实现:元枝三层 Value 存档位值,`MetaRules` 对 `MaxHp` 取已点节点的最大值而非求和(新增 `PerkRules.MaxBonus`)。⚠ +1000 仍超出原「不压过等级曲线 +500」锚点 —— 用户拍板,同步删改 Perk.cs 那条锚点注释。

## 4. 机制树

| 枝 | 节点 | 效果 |
|---|---|---|
| 博闻 | 博闻/强识 | 不变 |
| 广纳 | 广纳 / 兼收 | 回合开始掉字时若字库为 0,本回合掉字数 +1 / 合计 +2 |
| 慧眼 | 慧眼 / 明察 | 战利品候选 5 → 6 / 每轮选字可整组重抽 1 次 |
| **调息**(原一气) | **调息** / **吐纳** | 每场战斗胜利后回复最大生命 5% / 合计 10% |

- 广纳:`PerkEffect.StartingCards` → 新 `EmptyLibraryDraws`;`MetaRules.StartingHandSizeFor` 不再加 perk。BattleConfig 新字段 `EmptyLibraryExtraDraws`,在 `StartTurn` 掉字前判 `Library.Count == 0`。满库挂起逻辑不变。
- 慧眼:`PerkEffect.DrawRolls` 不再由慧眼提供(博采仍用它);新 `RewardOptions`、`RewardRerolls`。RunEngine 的 `RewardOptionCount` 常量改为构造注入。复活补给/字库补给共用同一套候选 → 同样适用。重抽:每轮(每一次 5/6 选 2)限 1 次,进快照(断点续爬不白送)。表现层在选字弹窗加「重抽」按钮。
- 调息:新 `PerkEffect.VictoryHealPercent`;RunEngine 判胜捕获 `_carriedHp` 时 `+ EffectiveMaxHp × N / 100`,夹上限。
- `PerkEffect.Ap`、`LootDrawRolls` 删除;`BaseApPerTurn` 不变。
- 跨树「融会」按机制树节点数缩放,节点数不变,不受影响。

## 5. 表现层

- 画布:三段节点画成一个节点 + 3 颗段位点;点开详情弹窗显示「第 k/3 段」与下一段的门槛/价格,解锁按钮解锁下一段。
- 状态三处对账(CLAUDE.md):断金若加「蓄势」可见提示,走 StatusText / BattleView chip / CharInfo 三处 —— 本版不加新状态,战意 5 层本身已可见。
- 字符串表:新/改节点 name/desc/detail;删掉不再使用的 key(字符串表孤儿检查)。
- 图标:优先复用现有 key(crit / summon / heal / burn / shield 类),缺的走 SVG 管线三处对账。
- 新文案后重跑 `tools/fonts/subset_fonts.py`。

## 6. 测试

- Core(TDD):Perk 表结构(段/前置/门槛/价格)、每个新效果注入 BattleConfig/RunEngine、五条新机制各自的行为测试、恒等性(新字段缺省 0 时既有测试不动)。
- 全套 pytest 六目录、coretests、prescompile(worktree 覆盖程序集路径)、balance/chestsim/trace 三个工程 build。
- 设计文档同步:第 19 章养成节、`字选型/技能机制详表.md` 中受影响条目。
