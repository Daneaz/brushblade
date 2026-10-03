# 现状(current)

游戏**现在**的样子,和 Unity 实现对齐;实现改了可见 UI,同一个提交里更新这里(设计验收的最后一步)。
设计稿与回填记录在 `../drafts/`。

十张卡,按 Unity 实现再现(932×430pt 横屏;2026-10-04 全部照代码重画过一轮):
整屏 `BattleScreen` `BestiaryScreen` `CharacterScreen` `CollectionScreen` `HomeScreen` `PerksScreen`
`SafeLayerScreen` `ShopScreen`,叠在整屏上的弹窗 `LevelUpSheet` `MilestonePickSheet`。每张一个子目录:`README.md` + `preview.html`。

- 来源口径(「实现」/「重设计 · 待实现」)见 `../component/README.md`。
- 规则与 token 见 `../system/README.md`;组件样式 `../component/bundle.css`。
- 本地预览:`../_preview/<名>.html`。直接打开 `preview.html` 是裸 HTML。
