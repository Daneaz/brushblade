# deprecated —— 待确认删除

2026-10-03 归档整合时从 `docs/design/` 移入,保留原目录层级。**用户确认后整目录删除**;
在那之前不要再引用这里的内容,也不要往这里加新文件。

| 文件 | 为什么废弃 | 现行替代 |
| --- | --- | --- |
| `第15章-留存与可持续性.md` | v0.3 Premium 买断制的留存模型,章首自己声明「v0.5 起已废止」 | 第 19 章(F2P 日循环)、第 14 章 |
| `怪物设计提示词.md` | 2026-07 给出图 AI 的一次性提示词(只要「错字鬼」一只),字怪已全部按 SVG 管线出图 | `敌人形象关键词包.md`、`敌人形象设计清单.md`、`tools/design/` 的字怪管线 |
| `字牌设计提示词.md` | 2026-07 给出图 AI 的一次性提示词(白/紫两档牌框),牌框素材已交付 | `字牌形象关键词包.md`、`card-refs/` |
| `ui/scenes/Perks.dc.html` | 旧扁平四条技能页;2026-09-08 用户要求从画布删页 | `ui/scenes/SkillOrbit*.dc.html`(技能树星域) |
| `字选型/人工筛选.md` | 2026-08-02 的 76 字人工筛选表,早于 2026-08-25 字表重构(现行 55 字) | `字选型/字表功能解析.md`(由 `chars.json` 生成) |
| `字选型/词组补全建议.md` | 基于上面那份 76 字表的词组补全建议,同样早于重构 | `字选型/词组计分表.md` |
| `字选型/技能四类判定-评分版.md` | 重构前的判类与评分稿;所引的 `技能四类判定.md` 早已不在库里 | `字选型/技能机制详表.md` |
| `字选型/五行共享字.md` | 同部件五系字族的头脑风暴清单,未被任何文档或工具采用 | — |
| `字选型/五行基础字/`(7 张) | 2026-07-31 由 `tools/pipeline/report_pool_candidates.py` 出的卡池候选筛选表,筛选已结束 | `chars.json` / `字表功能解析.md` |
| `glyph-refs/png/`(42 张) | `tools/fonts/glyph_refs.py --png` 出的位图底稿,给只吃位图的出图工具(ControlNet)用;字怪已全部改走 SVG 管线,没有工具或测试读它 | `glyph-refs/svg/` → `svg-done/` → `tools/design/` 字怪管线 |
| `wuxing/五行相生.svg` | 相生环图 2026-08-31 已从战斗屏撤掉,相生 ×3 于 2026-09-02 取消 | `wuxing/五行相克.svg`(战斗屏相克图的源) |
| `demos/review/实现对齐稿.html` | 2026-09-18 的稿与实现对齐快照,其后 09-21 的三方对账已把结论收进各现状卡与组件卡 | `current/`、`component/` 各卡的「与实现的差异」段 |
| `drafts/levelup/LevelChest.dc.html` | 「等级区间 → 宝箱」草案,被 2026-10-02 角色页 spec 取代(整体升一档、去掉素纸匣) | `drafts/character/`(`LevelUp.dc.html`、`RewardTable.dc.html`) |
| `card-refs/`(牌框 12 + 属性元件 6) | 用户确认素材已在项目内(`Brushblade/.../Cards/Resources/*.png`);出图脚本随之退役 | `Cards/Resources/` 的 PNG |
| `glyph-refs/svg/`、`svg-done/`、`manifest.json`、`_contact-sheet.svg` | 字怪字形底稿与合并稿;用户确认已在项目内,改形象直接改分层图 | `tools/design/mobs/svg/` → `rasterize_mobs.py` |
| `wuxing/五行相克.svg` | 用户确认已在项目内 | `Presentation/UI/Resources/` 的相克图 PNG |
| `tools/design/build_mob_drafts.py`、`split_layers.py`、`rasterize_cards.py` | 上面三组源图的出图脚本,输入已退役 | — |
| `tools/design/tests/test_split_layers.py`、`test_mob_assets_design_source.py` | 拆层测试,及从 `test_mob_assets.py` 摘出的「每只怪都有合并稿」一条 | `test_mob_assets.py` 其余三方对账 |

`tools/fonts/glyph_refs.py` **没退役**:`build_boss_art.py` 拿它从字体渲染 Boss 字形;它的命令行出稿仍写到
`docs/design/glyph-refs/`,别再用那个出口。
`glyph_refs.py --png` 重跑会把位图写回 `docs/design/glyph-refs/png/`(不是这里)。
`report_pool_candidates.py` 仍在库里,重跑会把筛选表写回 `docs/design/字选型/`(不是这里)。
