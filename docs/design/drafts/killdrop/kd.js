/* 斩获 · 掉字飞入:几何、时长、局面、演出。
   战场尺寸取已拍板的 battlegrid(3 × 2 加宽格),逻辑单位 ÷ 2.093 成稿面 pt;字库牌 96 × 117 → 45.87 × 55.9pt。
   时长全部是 1× 的值;按住加速 ×2 / ×3 时一律 ÷ 倍率(与 Juice 的 _rate 同口径)。 */
window.KD = (function () {
  var K = 2.093;
  function pt(l) { return Math.round(l / K * 100) / 100; }

  // ———— 时长(秒,1×) ————
  var T = {
    CAST: 0.22,     // 出字飞牌(现状 CastFlyDuration)
    DIE: 0.30,      // 阵亡置灰(示意)
    POP: 0.18,      // ① 冒字:token 从立绘中心弹出、上升 30
    RISE: 30,       //    上升量(逻辑单位)
    LABEL: 1.30,    //    「斩获」名签:同 TriggerFx 特性名签 1.3s
    PARK_MIN: 0.25, // ② 停:冒出后至少停这么久,让玩家看清「是这只掉的」
    TAIL: 0.42,     //    批尾:Juice 收尾(TailGap 0.3)+ OnAnimDone 重绘 + 等一帧布局 ≈ 0.42
    FLY: 0.45,      // ③ 飞:同回合开始掉字 DealFlyDuration
    STAGGER: 0.12,  //    多张错峰:同 DealStagger
    ARC: 46,        //    弧高(逻辑单位,往上鼓)
    LAND: 0.20,     // ④ 落:字牌 PopTile 弹一下 + 新到手光晕
    FADE: 0.30,     //    当拍胜利:不飞,原地淡出
    MULTI: 0.05     //    群攻逐目标间隔(现状)
  };

  var WIDTHS = {
    base:   { n: '基准机', dev: 932, side: 59, field: 1223, label: '基准机 · 932pt · 战场 1223' },
    narrow: { n: '16:9', dev: 764, side: 58.77, field: 909, label: '16:9 无刘海 · 764pt · 战场 909' }
  };

  // 几何(pt)
  function geom(wk, libCount) {
    var W = WIDTHS[wk];
    var frameL = W.side, frameW = W.dev - W.side * 2;
    var rail = 67.85, bench = 131.87, gap = 6.21;
    var midL = frameL + rail + gap, midW = frameW - rail - bench - gap * 2;
    var fieldW = pt(W.field), fieldL = midL + (midW - fieldW) / 2;
    var cellW = pt((W.field - 34) / 3), cg = pt(17);
    var top = 24;
    var rowB = { y: top, h: pt(109), p: pt(96) }, rowF = { y: top + pt(113), h: pt(138), p: pt(126) };
    var sumTop = top + pt(251 + 26);
    var playerY = sumTop + pt(113 + 4 + 88) + 3;
    var handY = playerY + 40 + 3;
    // 字库牌:同 DrawLibrary,每张宽 = min(96, (_fieldWidth − 96 − 8 × n) / n),宽高同比缩(16:9 满 10 张时 ≈ 37pt 宽)
    var n = Math.max(1, libCount || 6), twL = Math.min(96, (W.field - 96 - 8 * n) / n);
    var tw = pt(twL), th = pt(twL * 117 / 96), tg = 3.82, cap = 45.87;
    function cell(c, r) {
      var R = r === 'b' ? rowB : rowF, x = fieldL + c * (cellW + cg);
      return { x: x, y: R.y, w: cellW, h: R.h, p: R.p, cx: x + R.p / 2, cy: R.y + R.h / 2 };
    }
    function tile(i) { return { x: fieldL + cap + tg + i * (tw + tg), y: handY, w: tw, h: th }; }
    return { W: W, frameL: frameL, frameW: frameW, midL: midL, midW: midW, fieldL: fieldL, fieldW: fieldW, cellW: cellW,
      top: top, sumTop: sumTop, playerY: playerY, handY: handY, cell: cell, tile: tile, tw: tw, th: th, cap: cap,
      benchL: frameL + frameW - bench, railL: frameL };
  }

  var ELC = { Fire: 'fire', Metal: 'metal', Water: 'water', Earth: 'earth', Wood: 'wood' };
  var FOES = [
    { c: 0, r: 'b', g: '晕', n: '晕染', el: 'Water' }, { c: 1, r: 'b', g: '刻', n: '刻刀', el: 'Metal' }, { c: 2, r: 'b', g: '拓', n: '拓片', el: 'Earth' },
    { c: 0, r: 'f', g: '铭', n: '铭文', el: 'Metal' }, { c: 1, r: 'f', g: '泼', n: '泼墨', el: 'Water' }, { c: 2, r: 'f', g: '印', n: '印泥', el: 'Earth' }
  ];
  var LIB = [['冻', 'Water', 'Green'], ['锋', 'Metal', 'Green'], ['炎', 'Fire', 'Blue'], ['林', 'Wood', 'Blue'], ['垒', 'Earth', 'Green'], ['剑', 'Metal', 'Blue']];

  // 局面:kills = 本批阵亡(按时间)与各自掉的字;libStart = 字库起始张数
  var CASES = {
    single: { n: '单杀', note: '出「剑」击杀泼墨 → 冒字「燃」→ 批尾飞入字库第 7 位。', cast: 5, hits: [4], kills: [{ foe: 4, g: '燃', el: 'Fire' }] },
    aoe:    { n: '群攻连杀 2', note: '横扫前排三只,铭文、印泥阵亡:两枚 token 各从自己的格冒出,批尾按落位顺序错开 0.12s 飞入。', cast: 1, hits: [3, 4, 5], kills: [{ foe: 3, g: '江', el: 'Water' }, { foe: 5, g: '桂', el: 'Wood' }] },
    cap:    { n: '本回合已 2 张', note: '本回合已掉满 2 张:第 3 只阵亡不掉字、不提示(上限是规则,不是事件)。', cast: 5, hits: [4], kills: [{ foe: 4, none: true }] },
    full:   { n: '字库满', note: '字库 10/10:阵亡处飘划线的「斩获 · 字库满」(同现状「被挡下」样式),批尾字库计数朱砂闪一次;本回合只提示一次。', cast: 5, hits: [4], kills: [{ foe: 4, full: true }], libStart: 10 },
    zhegui: { n: '折桂(同路径)', note: '折桂改走同一条路径:名签换成「攻」印 + 折桂(特性名签原样),token 与飞行完全一样。', cast: 5, hits: [4], kills: [{ foe: 4, g: '鑫', el: 'Metal', zhegui: true }] },
    win:    { n: '当拍胜利', note: '最后一只被杀、战斗当拍结束:字照样进字库,但结算不画字库 —— token 冒出后原地 0.3s 淡出,不飞。', cast: 5, hits: [4], kills: [{ foe: 4, g: '燃', el: 'Fire' }], win: true },
    deal:   { n: '对照:回合掉字', note: '对照(现状不变):回合开始掉字从字库行右侧滑入。来源不同 → 起点不同:击杀得字从敌人格飞来,回合掉字从右侧滑入。', deal: true, kills: [] }
  };

  return { K: K, pt: pt, T: T, WIDTHS: WIDTHS, geom: geom, FOES: FOES, LIB: LIB, CASES: CASES, ELC: ELC };
})();
