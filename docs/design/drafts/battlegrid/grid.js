/* 战场 3×2 · 几何、chip 排行、示例局面。
   一切尺寸先按 Unity 逻辑单位(CanvasScaler 1600×900 按高)算,渲染时 ÷ 2.093 成稿面 pt。
   chip 宽同 Ui.ChipWidth:全角 1 个字号、半角 0.55 个字号 + padX;带图标再 + 18(图标)+ 3(有字时)。
   排行同 Ui.ChipFlow:贪心装行,超行则从尾部丢并在末行预留「+N」(padX 4)。 */
window.BG = (function () {
  var K = 2.093;
  function pt(l) { return Math.round(l / K * 100) / 100; }

  // ———— 常量(逻辑单位)。标 ★ 的是本稿新值,其余沿用现状 ————
  var C = {
    RowGap: 17,            // 同排格间距(不变)
    ERowGap: 4,            // 敌方两排之间(不变)
    EFrontH: 138, EBackH: 109,     // 敌人格高(不变:纵向是这块屏最紧的)
    EPortF: 126, EPortB: 96,       // ★ 立绘两档宽都不缩(原 16:9 × 0.73)
    EGap: 13,
    SFrontH: 113, SBackH: 88,
    SPortF: 100, SPortB: 75, SGap: 10,
    HeaderH: 24, InfoSp: 4,
    EHp: 19, EAct: 6, SHp: 17, SAct: 15,
    Font: 15, PadX: 10, ChipH: 20, Icon: 18, IconGap: 3, ChipSp: 4, LineSp: 3, CountPadX: 4,
    LinesEF: 3,            // ★ 前排敌人 2 → 3 行
    LinesEB: 2, LinesSF: 2, LinesSB: 1,
    BossH: 251,            // 后排 109 + 4 + 前排 138
    BossPort: 172,         // ★ 竖块立绘 172(原跨两列 220)
    BossSideLines: 2,      // ★ 竖块右栏只放 攻 / 甲,放不下一行就折两行
    LinesBoss: 2,          // ★ 竖块底部全宽 chip 流 2 行
    TierFont: 13, TierPadX: 8, TierH: 18, TierBorder: 1.5  // ★ 档位签
  };

  var WIDTHS = {
    base:   { n: '基准机', field: 1223, dev: 932, side: 59, label: '基准机 19.5:9 · 932pt · 战场 1223(584pt)' },
    narrow: { n: '16:9',   field: 909,  dev: 764, side: 58.77, label: '16:9 无刘海 · 764pt · 战场 909(434pt)' }
  };

  function textW(t, f) { var w = 0; for (var i = 0; i < t.length; i++) w += t.charCodeAt(i) < 0x2000 ? 0.55 : 1; return w * f; }
  function chipW(c) {
    if (c.count) return textW(c.t, C.Font) + C.CountPadX;
    var w = textW(c.t || '', C.Font) + C.PadX;
    if (c.ic) w += C.Icon + (c.t ? C.IconGap : 0);
    return w;
  }
  function tierW(t) { return textW(t, C.TierFont) + C.TierPadX; }

  // Ui.ChipFlow 的 PackChips + 截断
  function packTake(chips, take, extra, W, maxL) {
    var lines = [[]], x = 0;
    var reserve = extra ? C.ChipSp + chipW(extra) : 0;
    function place(c, isExtra) {
      var w = chipW(c);
      var limit = (!isExtra && reserve > 0 && lines.length === maxL) ? W - reserve : W;
      var cur = lines[lines.length - 1];
      if (cur.length && x + C.ChipSp + w > limit) { lines.push([]); x = 0; cur = lines[lines.length - 1]; }
      x = cur.length ? x + C.ChipSp + w : w;
      cur.push(c);
    }
    for (var i = 0; i < take; i++) place(chips[i], false);
    if (extra) place(extra, true);
    if (!lines[lines.length - 1].length) lines.pop();
    return lines;
  }
  function flow(chips, W, maxL) {
    var lines = packTake(chips, chips.length, null, W, maxL);
    if (lines.length <= maxL) return { lines: lines, hidden: 0 };
    for (var take = chips.length - 1; take >= 0; take--) {
      var ex = { t: '+' + (chips.length - take), count: true };
      var tr = packTake(chips, take, ex, W, maxL);
      if (tr.length <= maxL) return { lines: tr, hidden: chips.length - take };
    }
    return { lines: [], hidden: chips.length };
  }

  // ———— chip 工厂 ————
  var ch = {
    atk: function (n, ranged) { return { t: '' + n, ic: ranged ? 'ranged' : 'melee', k: 'k-atk' }; },
    def: function (n) { return { t: '' + n, ic: 'defense', k: 'k-ctrl' }; },
    charge: function (skill) { return { t: '蓄力 · 下回合:' + skill, k: 'k-dot' }; },
    dot: function (ic, n) { return { t: n ? '' + n : '', ic: ic, k: 'k-dot' }; },
    ctrl: function (ic) { return { t: '', ic: ic, k: 'k-ctrl' }; },
    heal: function (ic, n) { return { t: n ? '' + n : '', ic: ic, k: 'k-heal' }; },
    gold: function (ic, n) { return { t: n ? '' + n : '', ic: ic, k: 'k-gold' }; },
    chill: function () { return { t: '', ic: 'chill', k: 'k-frost' }; }
  };

  var TIERS = {
    minion: { t: '小怪', cls: 't-minion' },
    leader: { t: '头领', cls: 't-leader' },
    elite:  { t: '精英', cls: 't-elite' },
    boss:   { t: 'Boss', cls: 't-boss' }
  };
  var ELN = { Fire: '火', Metal: '金', Water: '水', Earth: '土', Wood: '木' };

  // ———— 示例局面 ————
  // 敌方 col 0..2(左→右)、row 'b' 后排 / 'f' 前排;竖块写 span: true(占 b + f 两格)
  var MOH = {   // 墨海杂兵(46 层,ScaleFor ≈ 5)
    pomo:   { n: '泼墨', el: 'Water', art: 'enemy_pomo' },
    yunran: { n: '晕染', el: 'Water', art: 'enemy_yunran', ranged: true },
    sumo:   { n: '宿墨', el: 'Water', art: 'enemy_sumo' },
    tapian: { n: '拓片', el: 'Earth', art: 'enemy_tapian' },
    yinni:  { n: '印泥', el: 'Earth', art: 'enemy_yinni' },
    kedao:  { n: '刻刀', el: 'Metal', art: 'enemy_kedao', ranged: true },
    mingwen:{ n: '铭文', el: 'Metal', art: 'enemy_mingwen' },
    tiehua: { n: '铁画', el: 'Metal', art: 'enemy_tiehua' },
    zhenzhi:{ n: '镇纸', el: 'Earth', art: 'enemy_zhenzhi' }
  };
  function mob(key, col, row, hp, hpMax, act, chips, extra) {
    var m = MOH[key], o = { kind: 'foe', col: col, row: row, name: m.n, el: m.el, art: m.art, tier: 'minion',
      hp: hp, hpMax: hpMax, act: act, chips: chips };
    for (var k in extra || {}) o[k] = extra[k];
    return o;
  }

  var SUMMONS_FULL = [
    { col: 1, row: 'f', g: '森', src: '森', el: 'Wood', hp: 186, hpMax: 240, act: 62,
      chips: [ch.atk(96), ch.def(18), ch.dot('thorns'), ch.heal('block', 2), ch.gold('morale', 3), ch.heal('heal'), ch.dot('burn', 2)] },
    { col: 0, row: 'f', g: '林', src: '林', el: 'Wood', hp: 120, hpMax: 150, act: 35,
      chips: [ch.atk(64), ch.heal('block', 1), ch.ctrl('taunt')] },
    { col: 2, row: 'f', g: '杉', src: '杉', el: 'Wood', hp: 140, hpMax: 140, act: 86,
      chips: [ch.atk(72, true), ch.gold('attack')] },
    { col: 1, row: 'b', g: '荆', src: '荆', el: 'Wood', hp: 70, hpMax: 110, act: 20,
      chips: [ch.atk(58), ch.def(12), ch.dot('thorns'), ch.heal('block', 2), ch.gold('morale', 2), ch.heal('heal')] },
    { col: 0, row: 'b', g: '藻', src: '藻', el: 'Wood', hp: 60, hpMax: 60, act: 48,
      chips: [ch.atk(40, true)] },
    { col: 2, row: 'b', g: '柘', src: '柘', el: 'Wood', hp: 90, hpMax: 130, act: 70,
      chips: [ch.atk(52), ch.heal('guard')] }
  ];

  var SCENES = {
    minion: {
      n: '普通层 · 满 6 只', title: '墨海 第 46 层 · 普通层 6 只(31 层起满编)', depth: 46,
      foes: [
        mob('yunran', 0, 'b', 412, 600, 48, [ch.atk(140, true), ch.heal('seed'), ch.dot('burn', 9), ch.dot('bleed', 2), ch.ctrl('freeze'), ch.ctrl('slow'), ch.ctrl('curse'), ch.ctrl('armorbreak')]),
        mob('kedao', 1, 'b', 500, 500, 82, [ch.atk(250, true), ch.dot('mark')]),
        mob('tapian', 2, 'b', 760, 900, 30, [ch.atk(150), ch.def(50), ch.ctrl('slow')]),
        mob('mingwen', 0, 'f', 610, 850, 64, [ch.atk(175), ch.dot('mortal'), ch.dot('burn', 3)]),
        mob('pomo', 1, 'f', 520, 800, 88, [ch.atk(210), ch.def(24), ch.dot('burn', 7), ch.dot('bleed', 3), ch.ctrl('freeze'), ch.ctrl('slow'), ch.ctrl('armorbreak'), ch.ctrl('blind')]),
        mob('yinni', 2, 'f', 1150, 1150, 12, [ch.atk(140)])
      ],
      summons: SUMMONS_FULL
    },
    leader: {
      n: '头领层', title: '墨海 第 44 层 · 头领「漩涡」+ 4 只小怪(25 层起必出)', depth: 44,
      foes: [
        { kind: 'block', col: 1, name: '漩涡', el: 'Water', tier: 'leader', leaderArt: ['漩', '涡'],
          hp: 1640, hpMax: 2000, act: 58, side: [ch.atk(150), ch.def(40)],
          chips: [ch.dot('burn', 6), ch.ctrl('slow'), ch.heal('seed'), ch.ctrl('armorbreak')] },
        mob('sumo', 0, 'b', 980, 1200, 40, [ch.atk(190), ch.def(60), ch.dot('burn', 2)]),
        mob('yunran', 2, 'b', 600, 600, 76, [ch.atk(140, true)]),
        mob('pomo', 0, 'f', 800, 800, 20, [ch.atk(210)]),
        mob('pomo', 2, 'f', 310, 800, 90, [ch.atk(210), ch.dot('burn', 4), ch.ctrl('freeze')])
      ],
      summons: SUMMONS_FULL.slice(0, 5)
    },
    boss: {
      n: 'Boss 层', title: '墨海 第 50 层 · 主题 Boss「惊涛骇浪」第 2 阶段 + 4 只随从', depth: 50,
      foes: [
        { kind: 'block', col: 1, name: '惊【涛】骇浪', el: 'Water', tier: 'boss', bossArt: 'boss_jingtaohailang', phase: 1, phaseTint: 'var(--water-glyph)', iris: 'var(--water)',
          hp: 6120, hpMax: 9600, act: 84, shield: 120, side: [ch.atk(286), ch.def(60)],
          chips: [ch.charge('惊涛拍岸'), ch.ctrl('skillcut'), ch.dot('mortal'), ch.chill(), ch.dot('burn', 8), ch.dot('bleed', 4), ch.ctrl('blind'), ch.ctrl('curse'), ch.ctrl('armorbreak')] },
        mob('pomo', 0, 'b', 880, 880, 66, [ch.atk(231), ch.def(24)]),
        mob('yunran', 2, 'b', 300, 660, 40, [ch.atk(154, true), ch.dot('burn', 5), ch.ctrl('slow')]),
        mob('sumo', 0, 'f', 1320, 1320, 22, [ch.atk(209), ch.def(60)]),
        mob('pomo', 2, 'f', 450, 880, 90, [ch.atk(231), ch.dot('bleed', 2), ch.ctrl('freeze'), ch.dot('mark')])
      ],
      summons: SUMMONS_FULL
    },
    elite: {
      n: '精英层', title: '第 15 层 · 精英 Boss「枯木逢春」第 1 阶段(20 层前无随从,召唤槽开 3 格)', depth: 15,
      foes: [
        { kind: 'block', col: 1, name: '【枯】木逢春', el: 'Wood', tier: 'elite', bossArt: 'boss_kumufengchun', phase: 0, phaseTint: 'var(--wood-glyph)', iris: 'var(--wood)',
          hp: 1900, hpMax: 2100, act: 30, side: [ch.atk(96), ch.def(20)],
          chips: [ch.dot('burn', 3)] }
      ],
      summons: [SUMMONS_FULL[1], SUMMONS_FULL[2], SUMMONS_FULL[4]],
      locked: true
    }
  };

  // 召唤槽解锁层(★ 1 / 7 / 13 / 19 / 25 / 31);槽位顺序:前排 中 → 左 → 右,再后排 中 → 左 → 右(同敌方居中优先)
  var SLOT_ORDER = [[1, 'f'], [0, 'f'], [2, 'f'], [1, 'b'], [0, 'b'], [2, 'b']];
  var UNLOCK = [1, 7, 13, 19, 25, 31];

  // ———— 布局 ————
  function geom(wk) {
    var W = WIDTHS[wk], cellW = (W.field - C.RowGap * 2) / 3;
    return { W: W, cellW: cellW, colX: function (c) { return c * (cellW + C.RowGap); } };
  }

  function barModel(hp, hpMax, act, ally) {
    var p = Math.max(0, Math.min(1, hp / hpMax));
    return { hpStyle: 'width:' + (p * 100).toFixed(1) + '%', hpTxt: hp + '/' + hpMax,
      actStyle: 'width:' + act + '%', actCls: 'atb' + (act >= 80 ? ' soon' : '') + (ally ? ' ally' : ''), actTxt: ally ? act + '%' : '' };
  }

  function chipLines(chips, W, maxL) {
    var f = flow(chips, W, maxL);
    return { hidden: f.hidden, lines: f.lines.map(function (ln) {
      return { chips: ln.map(function (c) {
        return { cls: 'sc ' + (c.count ? 'k-count' : c.k), t: c.t, ics: c.ic ? [{ href: '#ic-' + c.ic }] : [],
          style: 'width:' + pt(chipW(c)) + 'px' };
      }) };
    }) };
  }

  // 小怪不挂签(用户 2026-10-18 拍板):只有头领 / 精英 / Boss 出
  function tierModel(key) { if (key === 'minion') return []; var t = TIERS[key]; return [{ t: t.t, cls: 'tier ' + t.cls, style: 'width:' + pt(tierW(t.t)) + 'px' }]; }

  function mobLayers(art) {
    var A = window.ART || {};
    return ['wisp', 'body', 'face'].map(function (l) { return A[art + '_' + l]; }).filter(Boolean).map(function (src) { return { src: src }; });
  }
  function bossLayers(f) {
    var A = window.ART || {}, p = f.bossArt, out = [];
    function img(l) { if (A[p + '_' + l]) out.push({ cls: 'ly', style: 'background-image:url(' + A[p + '_' + l] + ')' }); }
    function mask(l, bg) { if (A[p + '_' + l]) out.push({ cls: 'ly', style: '-webkit-mask-image:url(' + A[p + '_' + l] + ');mask-image:url(' + A[p + '_' + l] + ');background:' + bg }); }
    img('wisp');
    for (var i = 0; i < 4; i++) {
      if (i < f.phase) continue;                       // 已被「正」走的字不画
      mask('c' + i, i === f.phase ? f.phaseTint : 'color-mix(in srgb, var(--ink) 90%, transparent)');
    }
    img('halo'); mask('iris', f.iris); img('eyes');
    return out;
  }

  function model(wk, sk, opts) {
    opts = opts || {};
    var g = geom(wk), S = SCENES[sk], units = [], slots = [], audits = [];
    var yB = 0, yF = C.EBackH + C.ERowGap;
    var used = {};
    S.foes.forEach(function (f) {
      var x = g.colX(f.col);
      if (f.kind === 'block') {
        used[f.col + 'b'] = used[f.col + 'f'] = true;
        var W = g.cellW, sideW = W - C.BossPort - C.EGap;
        var side = chipLines(f.side, sideW - 4, C.BossSideLines);
        var low = chipLines(f.chips, W - 4, C.LinesBoss);
        var b = barModel(f.hp, f.hpMax, f.act);
        var portrait = f.bossArt ? { layers: bossLayers(f), glyphs: [] } : { layers: [], glyphs: f.leaderArt.map(function (c, i) { return { t: c, cls: 'lg lg' + i }; }) };
        units.push({ kind: 'block', cls: 'u blk e-' + f.el + (opts.guides ? ' guided' : ''),
          style: 'left:' + pt(x) + 'px;top:0;width:' + pt(W) + 'px;height:' + pt(C.BossH) + 'px',
          name: f.name, eln: ELN[f.el], tier: tierModel(f.tier),
          ptStyle: 'width:' + pt(C.BossPort) + 'px;height:' + pt(C.BossPort) + 'px', ptCls: 'bpt' + (f.leaderArt ? ' leader' : ''),
          layers: portrait.layers, glyphs: portrait.glyphs,
          shield: f.shield ? [{ t: f.shield }] : [],
          sideStyle: 'width:' + pt(sideW) + 'px', side: side.lines, low: low.lines,
          bar: b });
        audits.push({ who: f.name, kind: '竖块', w: W, info: W - 4, lines: C.LinesBoss, hidden: low.hidden + side.hidden });
        return;
      }
      used[f.col + f.row] = true;
      var front = f.row === 'f', H = front ? C.EFrontH : C.EBackH, P = front ? C.EPortF : C.EPortB;
      var infoW = g.cellW - P - C.EGap, maxL = front ? C.LinesEF : C.LinesEB;
      var cl = chipLines(f.chips, infoW - 4, maxL);
      units.push({ kind: 'foe', cls: 'u foe e-' + f.el + (front ? ' front' : ' rear'),
        style: 'left:' + pt(x) + 'px;top:' + pt(front ? yF : yB) + 'px;width:' + pt(g.cellW) + 'px;height:' + pt(H) + 'px',
        ptStyle: 'width:' + pt(P) + 'px;height:' + pt(P) + 'px', layers: mobLayers(f.art),
        infStyle: 'width:' + pt(infoW) + 'px', name: f.name, eln: ELN[f.el], tier: tierModel(f.tier),
        lines: cl.lines, bar: barModel(f.hp, f.hpMax, f.act) });
      audits.push({ who: f.name, kind: front ? '前排' : '后排', w: g.cellW, info: infoW - 4, lines: maxL, hidden: cl.hidden });
    });
    // 空格位
    [0, 1, 2].forEach(function (c) { ['b', 'f'].forEach(function (r) {
      if (used[c + r]) return;
      var front = r === 'f';
      slots.push({ cls: 'slot', style: 'left:' + pt(g.colX(c)) + 'px;top:' + pt(front ? yF : yB) + 'px;width:' + pt(g.cellW) + 'px;height:' + pt(front ? C.EFrontH : C.EBackH) + 'px' });
    }); });

    // 召唤区:前排在上(贴分隔线)
    var sTop = C.BossH + 26, sF = sTop, sB = sTop + C.SFrontH + C.ERowGap;
    var allies = [], sslots = [];
    var have = {}; S.summons.forEach(function (s) { have[s.col + s.row] = s; });
    SLOT_ORDER.forEach(function (o, i) {
      var c = o[0], r = o[1], front = r === 'f', H = front ? C.SFrontH : C.SBackH, P = front ? C.SPortF : C.SPortB;
      var pos = 'left:' + pt(g.colX(c)) + 'px;top:' + pt(front ? sF : sB) + 'px;width:' + pt(g.cellW) + 'px;height:' + pt(H) + 'px';
      if (UNLOCK[i] > S.depth) { sslots.push({ cls: 'sslot locked', style: pos, t: UNLOCK[i] + ' 层解锁' }); return; }
      var s = have[c + r];
      if (!s) { sslots.push({ cls: 'sslot', style: pos, t: '' }); return; }
      var infoW = g.cellW - P - C.SGap, maxL = front ? C.LinesSF : C.LinesSB;
      var cl = chipLines(s.chips, infoW - 4, maxL);
      allies.push({ cls: 'u ally e-' + s.el + (front ? ' front' : ' rear'), style: pos,
        ptStyle: 'width:' + pt(P) + 'px;height:' + pt(P) + 'px;font-size:' + pt(P * 0.46) + 'px', g: s.g,
        infStyle: 'width:' + pt(infoW) + 'px', name: s.src, eln: ELN[s.el], lines: cl.lines, bar: barModel(s.hp, s.hpMax, s.act, true) });
      audits.push({ who: '召唤·' + s.g, kind: front ? '召唤前排' : '召唤后排', w: g.cellW, info: infoW - 4, lines: maxL, hidden: cl.hidden });
    });

    return { units: units, slots: slots, allies: allies, sslots: sslots, audits: audits,
      fieldStyle: 'width:' + pt(g.W.field) + 'px;height:' + pt(sB + C.SBackH) + 'px',
      dividerStyle: 'top:' + pt(C.BossH + 12) + 'px', cellW: g.cellW };
  }

  // 规格表:每档每种格的宽、立绘、信息列、行数、容量
  function specRows() {
    var rows = [];
    var kinds = [
      ['敌 · 前排', C.EFrontH, C.EPortF, C.EGap, C.LinesEF],
      ['敌 · 后排', C.EBackH, C.EPortB, C.EGap, C.LinesEB],
      ['召唤 · 前排', C.SFrontH, C.SPortF, C.SGap, C.LinesSF],
      ['召唤 · 后排', C.SBackH, C.SPortB, C.SGap, C.LinesSB]
    ];
    var FULL = [ch.atk(210), ch.def(24), ch.dot('burn', 7), ch.dot('bleed', 3), ch.ctrl('freeze'), ch.ctrl('slow'), ch.ctrl('armorbreak'), ch.ctrl('blind')];
    var HEAVY = FULL.concat([ch.ctrl('curse'), ch.heal('seed')]);
    function cap(W) { return packTake(Array(40).fill(ch.ctrl('freeze')), 40, null, W, 99)[0].length; }
    function afterAD(W) { return packTake([ch.atk(210), ch.def(24)].concat(Array(30).fill(ch.ctrl('freeze'))), 32, null, W, 99)[0].length - 2; }
    function fmtFlow(chips, W, L, lead) { lead = lead == null ? 2 : lead; var f = flow(chips, W, L); var shown = f.lines.reduce(function (a, l) { return a + l.filter(function (c) { return !c.count; }).length; }, 0);
      return (shown - lead) + ' / ' + (chips.length - lead) + (f.hidden ? '(收 +' + f.hidden + ')' : '') + ' · ' + f.lines.length + ' 行'; }
    ['base', 'narrow'].forEach(function (wk) {
      var g = geom(wk);
      kinds.forEach(function (k) {
        var info = g.cellW - k[2] - k[3], flowW = info - 4;
        rows.push({ w: WIDTHS[wk].n, kind: k[0], cell: r1(g.cellW) + ' × ' + k[1], cellPt: pt(g.cellW) + ' × ' + pt(k[1]),
          port: k[2] + '(' + pt(k[2]) + ')', info: r1(info), lines: k[4], perLine: cap(flowW), first: afterAD(flowW),
          full: fmtFlow(FULL, flowW, k[4]), heavy: fmtFlow(HEAVY, flowW, k[4]) });
      });
      var bw = g.cellW, side = bw - C.BossPort - C.EGap;
      rows.push({ w: WIDTHS[wk].n, kind: '竖块 · 底部流', cell: r1(bw) + ' × ' + C.BossH, cellPt: pt(bw) + ' × ' + pt(C.BossH),
        port: C.BossPort + '(' + pt(C.BossPort) + ')', info: r1(bw - 4) + '(全宽)', lines: C.LinesBoss, perLine: cap(bw - 4), first: '—',
        full: fmtFlow(SCENES.boss.foes[0].chips, bw - 4, C.LinesBoss, 0) + '(Boss 示例,含蓄力预警)',
        heavy: '右栏 ' + r1(side) + ':攻 / 甲 ' + (packTake([ch.atk(286), ch.def(60)], 2, null, side - 4, 9).length) + ' 行' });
    });
    return rows;
  }
  function r1(v) { return Math.round(v * 10) / 10; }

  return { K: K, pt: pt, C: C, WIDTHS: WIDTHS, SCENES: SCENES, TIERS: TIERS, ch: ch, chipW: chipW, tierW: tierW, textW: textW,
    flow: flow, model: model, specRows: specRows, geom: geom, chipLines: chipLines, tierModel: tierModel, bossLayers: bossLayers, mobLayers: mobLayers };
})();
