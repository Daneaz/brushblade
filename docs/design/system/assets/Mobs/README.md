# Mobs

字怪立绘。真机素材在 `Brushblade/Assets/_Project/Presentation/Mobs/Resources/`，由
`tools/design/rasterize_mobs.py` 从 `tools/design/mobs/svg/` 的手写 SVG 出图
（109 张 SVG ↔ 109 张 PNG ↔ 109 个 `.meta`，一一对账）。**PNG 是产物，不能手写。**

## 规格
- 512×512 方、透明底，放在 `paper` 或层段背景上。
- 分层 PNG：`body` / `face` / `wisp`，按此序叠放（`MobAssets.Layers`）；战斗里另叠 `state` 层。
  属性气场并在 `wisp` 里，没有独立 aura 层。
- 属性色只在眼与要害，不超过画面两成，其余是墨。字形由 Noto Serif SC 供给，一笔不改。
- 构图硬约束：`wisp` 不与 `body` 相接（它要能独立飘出去），`face` 不画进 `body`
  （body 对应位置只留一个浅墨窝）；轮廓要经得起 ±4.5% 的呼吸缩放。
- 验收尺度：图鉴格 80pt（立绘占 82% = 66pt）下必须认得出是哪个字；48pt 可以糊，但不能糊到分不清彼此。
- 中文 id ↔ 拼音 slug 的对照表**有两份且必须一致**：`MobAssets.MinionSlugs` / `MobAssets.BossStages`
  与 `rasterize_mobs.py` 的 `MINION_SLUGS` / `BOSS_STAGES`（2026-09-21 实测两份逐字相同）。

## 库存（2026-09-21 实测）
`enemies.json` 共 28 只 = 25 只杂兵 + 3 只固定 Boss；图鉴另有 6 条运行时生成的成语 Boss，共 34 条。

- **杂兵 25 只，全部有立绘**：错字鬼 缺笔妖 标点小妖 叠字怪 夯土妖 通假字 生僻字 墨渍 焦痕 ·
  涂改 铁画 镇纸 洇痕 衍文 · 灯花 墨溅 悬针 败笔 枯笔 火漆 砚台 铜钤 版牍 窑变 宿墨。
  与 `enemies.json` 一一对上，图鉴网格里没有一只回落成字形。
- **固定 Boss 3 只 × 四相 = 10 套图**：排山倒海（4）、雷霆万钧（4）、翻江倒海（2 + 复用排山倒海的「倒」「海」）。
- **`state` 层只有 4 只**：缺笔妖（残笔补全进度）、通假字（面具）、生僻字（墨雾）、焦痕（火芯），
  即 `MobAssets.StateAmountFor` 有分支的那四只。战斗里由实际状态驱动，图鉴里按 0.55 静态露出。
- **成语 Boss 6 条没有立绘**：刀山火海 / 山崩海啸 / 冰天雪地 / 烈火干柴 / 飞沙走石 / 气吞山河。
  它们的 id 是四字成语，不在 `MobAssets.BossStages` 里 → `PrefixFor` 返回 null，
  图鉴格回落成该相的单字字形（`<el>-glyph` 压 `<el>-soft` 底，`BestiaryView.cs:414`），
  战斗里回落成属性色圆形字头像（`BattleView.cs:2600`）。**这是眼下唯一的形象缺口，占图鉴 34 条里的 6 条。**

## 已知不齐
- `衍文`（Regrow）与 `洇痕`（Split）有机制状态却没有 `state` 层：`StateAmountFor` 的 `Regrow`
  分支会照常算出数值，但 `MobView._stateImage` 是 null，`SetStateAmount` 空转 —— 不报错，也看不见。
  缺笔妖有、衍文没有，同一个能力两种表现。

## 画布预览
`MobArt.dc.html` / `MobScale.dc.html` 只带 2026-09-03 那批 12 张（灯花 墨溅 悬针 败笔 枯笔 火漆
砚台 铜钤 版牍 窑变 宿墨 + 焦痕）的压平预览，不是全量；`MobLayers.dc.html` 是窑变的三层拆解示例。
稿上标的血 / 攻 / 护甲数字已过时（护甲那三组差得最远），一律以 `enemies.json` 为准。
