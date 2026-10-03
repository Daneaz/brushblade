# Icons

44 枚自绘图标：30 枚战斗状态（灼烧、冻结、护盾、破甲…）、4 枚底部导航（`icon_nav_*`）、9 枚技能树节点（`icon_perk_*`），外加攻击模式等。

- **墨色是纯白 `#fff`**，64×64 画布，粗描边圆头（stroke 5–6）。放在浅底上看不见是预期的：它们永远压在有色 chip 上（`cinnabar` / `jade-deep` / `shield-blue` / `earth` / `ink-soft`），或在导航页签上被着成 `tab-*-fg`。
- 这些 SVG 是 `tools/icons/build_icons.py` 的产物，真机用的是它们栅格出的 PNG；不要手改 SVG 或手画 PNG。
- 每枚都有一个兜底汉字（`Icons.cs` 的 `Glyphs`：burn=炎、freeze=冰、shield=盾、heal=愈…），PNG 缺失时回落成汉字徽章。新增一枚要同改 `ICONS`、`test_icons.EXPECTED`、`Glyphs` 三处。
