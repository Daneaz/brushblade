# 设计稿(drafts)

出稿 → 拍板 → 实现 → 设计验收 → 回填,全过程的稿都在这里(流程见根目录 `CLAUDE.md`「UI 设计流程」)。
**实现以后算数的是 `../current/`**;这里的稿只记录当时的方案、拍板理由与回填记录,不与现状竞争。

状态四种:`待审`(出稿未拍板)· `已拍板 · 待实现` · `已落地`(实现完、验收差异已分流)· `已取代`(被新稿替代,
确认后移入 `../deprecated/`)。新增一份稿要在下表登记一行,状态变了就改这一行。

| 目录 | 内容 | 状态 | 依据 |
| --- | --- | --- | --- |
| `scenes/` | 全游戏界面基线画布(主界面、卡组、战斗、局内流程、图鉴、商城、怪物设计、技能树…) | 多数 `已落地`,逐块状态与回填记录见 `scenes/README.md` | 各节「已接线」记录 |
| `character/` | 角色页:主界面左栏、角色页、领取里程碑、升级弹窗、奖励表 | `已落地` | `CharacterView.cs`(提交 `126b8b9b` 等)、`LevelUpPopup.cs`(提交 `4a329c6c`) |
| `halo/` | 新到手的牌 · 高亮打磨(六案比选 + 墨晕规格) | `已落地`(方案 C 墨晕) | 提交 `37acb884`,`Cards/CardHalo.cs` |
| `shopmark/` | 商城折扣与捆数标记(三案比选 + 推荐整屏) | `已落地`(方案 A) | 提交 `210ed098` |
| `shopconfirm/` | 商城确认购买弹窗:买字卡 / 买宝箱、字卡墨锭不足、字卡买后效果四种口径、屏比验算 | `已实现 · 待验收`(2026-10-04;宝箱不足前置拦截,见回填记录) | 需求:点价格钮先弹确认,不再直接成交 |
| `traits/` | 字卡五特性 · 两面制:字卡详情(卡组 / 战斗)、词条气泡、手牌翻面与落点、升级预览、状态 chip、跨场效果、触发反馈 | `已拍板 · 待实现`(2026-10-04,spec v6.2;同日按 v7 修订:两面平等、每面各有 Lv5 / Lv8);CardSheet / CardSheetBattle / UpgradePreview `已实现 · 验收跳过`(2026-10-12,Plan E2 `9e5d2cff`;用户指示体验时反馈);StatusChips 补三枚(减伤 / 反击加倍 / 保命)`已实现 · 验收跳过`(2026-10-04 出稿,10-11 拍板;减伤随 D2-木、另两枚随 E3 `2fe45dbb` 实现);StatusChipsFire 火系补三枚(埋雷 / 干涸 / 回敬)`已实现 · 验收跳过`(2026-10-08 出稿,10-09 用户拍板并实现 `95b3c75b`);StatusChipsWood + WoodCues + SummonInfoNature 木系 `已实现 · 验收跳过`(2026-10-11 出稿并拍板,D2-木 `5c7ecd69` 实现;用户指示不做截图验收，体验时再反馈);StatusChipsWaterEarth + WaterEarthCues 水土补八枚与提示、PartsAndGraft 部件印记 / 字形 / 嫁接 / 花灵 `已实现 · 验收跳过`(2026-10-11 出稿并拍板,10-12 Plan E3 `2fe45dbb` 实现;偏差见 `traits/README.md` 末尾);HandFlip / FlipSpec 预估签、WaterEarthCues 本场已用、PartsAndGraft 印记签与山火色描环、TriggerFx、StatusChips 保命、Glossary、CarryOver `已实现 · 验收跳过`(2026-10-13 Plan E4 实现;偏差逐条见 `traits/README.md` 末尾「E4 实现回填」);**CardSheet / CardSheetBattle / Glossary 三块 `已取代`**(2026-10-15,被 `abilitytext/` 取代;待用户确认后移入 `../deprecated/`) | spec `2026-10-03-字卡五特性体系-design.md` §12 |
| `abilitytext/` | 字卡详情 · 技能说明区改版:配色 A(正文 text-quiet、词条本系 -fg)、两面按级全平铺(两面条取消)、五行面数值格按系、各系 Lv1(慑 / 润泽 涓涌苏 / 本命 / 格挡 / 护甲)、行头与气泡加图标 | `已落地`(2026-10-15 出稿并拍板,同日 spec B 实现,分支 `feat/spec-b`,合并提交见 git log;验收跳过,体验时反馈;偏差见 `abilitytext/README.md` 回填记录) | 需求:session card-ability-system-redesign |
| `counterfx/` | 格挡反击动效:挡(盾印)→ 扣(chip 跳格)→ 反(反击线 + 反击数),玩家 / 木灵 × 普通 / 反击加倍、群攻与连续节拍、两档屏宽出画验算 | `已落地`(2026-10-15 出稿并拍板,同日实现,分支 `feat/counterfx`;Core 事件 `BlockUsed`、表现 `JuiceCounter.cs`;验收跳过,体验时反馈;偏差见 `counterfx/README.md` 回填记录) | 需求:session card-ability-system-redesign 转用户提出 |
| `battlegrid/` | 战场 3 × 2:敌我加宽格(立绘不缩、前排 chip 3 行)、Boss / 精英 / 头领一列竖块(立绘 172 + 右栏攻甲 + 全宽 chip 流)、敌人头行档位签(头领 / 精英 / Boss;小怪不挂)、头领立绘 1:1 画幅、两档屏宽逐栏验算 | `已落地`(2026-10-18 用户拍板;小怪不挂档位签;同日实现,现状见 `current/BattleScreen`) | 需求:session card-ability-system-redesign;spec `2026-10-18-群攻入口与头领编组-design.md` §2 / §5 / §6 |

画布稿直接打开即可交互(`support.js` 软链接到本目录的 `dc-runtime.js`);
全部画板的画廊见 `../_preview/index.html`。
