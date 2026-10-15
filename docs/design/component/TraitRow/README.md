字卡特性行：字卡详情与升级预览里「一条特性」的那一行。实现是 `CardSheetParts.TraitRow`（`Presentation/UI/CardSheetParts.cs`，行号以实际为准），逐级取格 `LevelCell`、逐级取高 `LevelHeights`；数据模型 `FaceRow` / `SharedRow`（升级预览仍用），高度 `RowHeight`。调用点：面板里的行（`FacePanel`）。设计稿 `drafts/abilitytext`（取代 `drafts/traits` 的 CardSheet 部分；`CardSheetBattle`、`WaterEarthCues`「本场已用」仍参照 traits）。

## 结构
- **行头**（一行，定高）：Lv 标（胶囊，`Lv5`）→ [拆 / 合 xs 印] → **图标 chip**（13pt → 27，战斗版 25；与战场 chip 同图标、同底色，无图标不画）→ 名（粗）→ 类别小字（「主动」「被动」，Lv8 加「 · 招牌」）→ 尾标（靠右，只在 off / spent 态出现）。
- **正文**（行头下方，折行）：效果句，色 `text-quiet`；词条加粗本系 `-fg`，数字加粗 `text-main`。文案取 `CharInfo.TraitEffectsText` 的机械拼句，16:9 下会折 2–3 行 —— 行高按**实际面宽**用 `Ui.WrappedTextHeight` 算。
- 木系五行面 Lv1 本命行：左缘 2pt 朱砂（off / spent 取 40%）。
- 整行是一个点击区：这一行有术语时点了开词条气泡（`GlossaryBubble`）；没有术语的行不挂、也没有按下态。**战斗长按版也挂**：点行只开气泡、不出字。
- 字形特性（拆字 / 成字时触发）在 Lv 标之后、chip 之前加一枚 xs「拆 / 合」印（`Ui.TraitSeal`）；off / spent 态印压到 α .78。

## 三态
| 态 | 何时 | 底 / 描边 | Lv 标 | 名 / 类别 / 正文 | 尾标 |
| --- | --- | --- | --- | --- | --- |
| **on** 已解锁 | 卡等级 ≥ 解锁级 | 正面（攻击）`panel-paper`、背面（五行面）`card-face`（`Theme.CardWhite`）；描边 2 `panel-border` | `ink` 底白字 | 名 `text-main`；类别 `text-dim`；正文 `text-quiet` | 无 |
| **off** 未解锁 | 卡等级 < 解锁级 | 去底；只剩一圈 `panel-border` 80% 的空心环（`OutlinedPanel` 内层给透明会露出整块描边色，所以另画环） | 去底，`text-faint` 1 描边 + `text-faint` 字 | 全部 `text-faint`（`Theme.LockGray`）；背面（有本系淡底）改 `text-dim` 78%；chip .4 | 锁图标 + 「Lv{n} 解锁」，`text-faint` |
| **spent** 本场已用 | 只在**战斗长按版**：每场一次的特性（杜门不出、折桂）本场已触发 | 去底 | `text-dim` 实底白字 | 名 / 正文 / 词条 / 数字全 `text-dim`；chip .6 | 「本场已用」胶囊：`ink-bar` 底、`text-warm` 字 |
| **待补** | 某面缺某级（防御性占位，spec A 后不出现） | `panel-border` 空心环，不画斜纹 | 无 | `text-mute`「Lv6 · 这一面待补」 | 无 |

- off / spent 是非激活态，不在对比度要求之内（`system/` 配色规则）；靠「去底 + 尾标」与 on 区分，不只靠变浅。
- spent 下一场开始恢复成 on。升级预览在局外，没有「本场」，**不用** spent 态（WaterEarthCues 稿上写到升级预览是笔误，E2 附录 Q19 已指出）。
- 选中态（词条气泡打开时 1.5pt → 3 `ink-soft` 内描边，稿 `.tr.open`）由 `GlossaryBubble` 挂在行上，关气泡时撤掉。

## 尺寸（pt × 2.093 = 逻辑单位）
| 件 | 卡组 / 开箱 / 商城（铺满版） | 战斗长按版 |
| --- | --- | --- |
| 内边距 上下 / 左右 | 4 / 8 pt → 8 / 17 | 3 / 7 pt → 6 / 15 |
| 圆角 | 7 → 15 | 6 → 13（12.6 取整） |
| 行头高 | 14 → 29 | 25 |
| Lv 标 | 高 12 → 25，字 7.5 → 16，左右 4 → 8，圆角 6 → 12.6 | 高 23、字 15 |
| 名 | 11.5 → 24，粗 | 10.5 → 22 |
| 类别小字 | 8 → 17 | 16 |
| 正文 | 9.5 → 20，行高 1.4（28），与行头距 2 → 4 | 9 → 19 |
| 尾标 | 锁 8 → 17 + 字 8 → 17；「本场已用」胶囊高 12 → 25、字 7.5 → 16、左右 5 → 10.5、圆角 6 → 12.6 | 同 |
| 行与行 | 3 → 6 | 8.4 |

尺寸常量在 `CardSheetParts.Sizes.Tall` / `.BattleSheet`（同文件 46 / 57）；战斗版整体按 CardSheetBattle 稿缩一档。

横向贴边（E2 附录 §4.1）：16:9 下面内正文宽 431，行头「Lv 标 + 5 字名 + 主动 · 招牌 + 锁尾标」约 430，贴边。off 态放不下时去掉类别小字，保留锁尾标。

## 全平铺（组合用法）
两面是同一张网格的两列：每级两面各一行，行高取两面较大者（`LevelHeights`），两面效果相同也各写一遍；不再有「两面条」。战斗长按版只列已解锁的级，未解锁每面收成一行。

## 使用方提供
解锁级、特性名、类别、正文、所在面（攻击 / 五行面 / 两面，决定 on 态底色）、状态（on / off / spent）。
