# 斩获 · 掉字飞入

需求来自 session `card-ability-system-redesign`(2026-10-18),spec `docs/superpowers/specs/2026-10-18-群攻入口与头领编组-design.md` §7「斩获」。
现状:敌人回合击杀掉的字走右侧滑入(SplitOutDrawn / GuardedDeal),玩家出字途中击杀掉的字重绘后凭空出现;折桂只飘名签 + 光晕。

## 画板
- `KillDrop.dc.html`:可播放(Web Animations,时长 ÷ 倍率),七种情形 × 两档屏宽,另有「起 / 冒 / 飞 / 落」四个定格帧。
  战场按已拍板的 `battlegrid`(3 × 2);字库牌按 DrawLibrary 的张数同比缩。
- `KillSpec.dc.html`:时间轴、四拍时长与缓动、规则、实现需要。

## 方案一句话
击杀得字(斩获 / 折桂)= 现有 FlyGlyph token 从阵亡格冒出 → 停到本批收尾 → 0.45 s 弧线飞到字库落位 → 弹一下 + 光晕;
回合开始掉字照旧右侧滑入(从哪来就从哪飞)。上限到顶不演;字库满飘划线签 + 计数闪,每回合一次;当拍胜利原地淡出。

## 文件
`kd.js` 几何与时长;`kd.css` 样式(:root 同源 tokens.json);`data.js` 图标(traits-data.js 副本);`support.js` → `../dc-runtime.js`。

## 回填记录
(暂无)
