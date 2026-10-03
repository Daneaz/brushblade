# Chests

七档宝箱立绘，七种材质、七种轮廓：素纸匣 `paper` → 竹简匣 `bamboo` → 青瓷匣 `celadon` → 紫檀匣 `rosewood` → 鎏金匣 `gilded` → 朱漆匣 `vermilion` → 赤霄匣 `crimson`。越往上五金越多、轮廓越「抬头」，赤霄那只关不严、缝里透光。

- 每档两层：`*_body`（箱身，墨线 `#111622` 描边 + 该档稀有度色平涂）与 `*_seam`（盖缝的光）。箱色 = 同序稀有度色（`rarity-*`）。
- 三态只改叠加层：未开始（满不透明）/ 计时中（箱身 `opacity-timing` + `chest_fx_timing` 沙漏角标）/ 已就绪（`chest_fx_ready` 金光晕 + 盖缝透光 + 箱身 2.5pt 起伏，1.6s 同相呼吸）。
- 40pt 缩略图下认的是外形与那一块平涂色。改色要同步 `tools/design/build_chests.py` 的 TIERS 并重出 PNG。
