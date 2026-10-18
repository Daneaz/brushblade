/* 木灵尸体标识 · 几何、局面、各态模型。
   召唤格尺寸取已落地的 battlegrid(3 × 2):格宽 396.3 / 291.7,前排 113、后排 88;立绘 100 / 75、间距 10。
   chip 宽同 Ui.ChipWidth(全角 1 字号、半角 0.55 + padX 10,带图标 + 18 + 3);预估签同 BuildEstimateLabel(字 15、padX 20、高 27、探出格顶 19)。 */
window.CP = (function () {
  var K = 2.093;
  function pt(l) { return Math.round(l / K * 100) / 100; }
  function textW(t, f) { var w = 0; for (var i = 0; i < t.length; i++) w += t.charCodeAt(i) < 0x2000 ? 0.55 : 1; return w * f; }
  function chipW(t, ic) { return textW(t || '', 15) + 10 + (ic ? 18 + (t ? 3 : 0) : 0); }
  function signW(t) { return textW(t, 15) + 20; }

  var WIDTHS = {
    base:   { n: '基准机', field: 1223, label: '基准机 · 战场 1223 · 格宽 396' },
    narrow: { n: '16:9', field: 909, label: '16:9 无刘海 · 战场 909 · 格宽 292' }
  };
  var ELN = { Fire: '火', Metal: '金', Water: '水', Earth: '土', Wood: '木' };

  // 召唤区:slot = [col, row]。林、杉是尸体(杉被嫁接成火,复活带回火)。
  var SUMMONS = [
    { c: 1, r: 'f', g: '森', el: 'Wood', alive: true, hp: 186, max: 240, atk: 96, nat: 'thorns' },
    { c: 0, r: 'f', g: '林', el: 'Wood', alive: false, max: 150, atk: 64, nat: 'thorns', natName: '盾反' },
    { c: 2, r: 'f', g: '荆', el: 'Wood', alive: true, hp: 70, max: 110, atk: 58, nat: 'thorns' },
    { c: 1, r: 'b', g: '杉', el: 'Fire', alive: false, max: 140, atk: 72, nat: 'burn', natName: '附灼' }
    // 后排左、右:真空位
  ];
  var CARDS = {
    mu:   { g: '沐', el: 'Water', face: '润', pct: 50, all: false, desc: '沐「苏」· 复活 1 只 · 生命 50%' },
    man:  { g: '㵘', el: 'Water', face: '润', pct: 60, all: true,  desc: '㵘 · 复活全部 · 生命 60%' },
    lin:  { g: '林', el: 'Wood',  face: '生', desc: '林 · 生面(召唤)' }
  };

  // 七态
  var STATES = {
    idle:     { n: '平时', card: null, note: '平时、字库里没有带复活的面:尸体格什么都不画(同现状)—— 只是占槽。' },
    idleRev:  { n: '平时 · 有复活字', card: null, revInHand: true, note: '平时、字库里有带复活的面:尸体格常显淡态(立绘 + 头行,α .5),提醒「这里有一只能救」。没有复活手段时不画,免得成噪音。' },
    pick:     { n: '复活 · 点选', card: 'mu', mode: 'revive', note: '点了沐「苏」:可救的尸体全量显示 + 翠玉描环,血条里预演复活后的血量(斜纹 → 75/150)。' },
    drag:     { n: '复活 · 拖放悬停', card: 'mu', mode: 'revive', drag: true, hover: 0, note: '拖沐「苏」悬停林:预估签「复活 林 · 生命 75 · 盾反」(本命名压浅;签会压住头行,所以签里重写一遍字名)。' },
    dragAll:  { n: '复活全部 · 拖放', card: 'man', mode: 'revive', drag: true, hover: 0, note: '㵘 复活全部:悬停任一具,所有可救的尸体一起加深(同全体面);签只挂悬停那只,写「复活全部 · 2 只」。' },
    slot:     { n: '召唤 · 选位', card: 'lin', mode: 'summon', revInHand: true, note: '点了林的生面选位:空位照旧淡墨块 +「召唤到这里」;尸体格保留身份,chip 行换成「召唤到这里会覆盖它」(字库有复活字时朱砂:覆盖后无法复活)。' },
    slotDrag: { n: '召唤 · 拖到尸体', card: 'lin', mode: 'summon', drag: true, hover: 0, revInHand: true, note: '拖林的生面悬停林的尸体:预估签「覆盖 林 · 无法再复活」(字库里有复活字才接后半句)。' }
  };

  function geom(wk) {
    var W = WIDTHS[wk], cellW = (W.field - 34) / 3;
    return { W: W, cellW: cellW, fieldW: W.field,
      x: function (c) { return c * (cellW + 17); },
      y: function (r) { return r === 'f' ? 0 : 113 + 4; },
      h: function (r) { return r === 'f' ? 113 : 88; },
      p: function (r) { return r === 'f' ? 100 : 75; } };
  }

  function reviveHp(s, pct) { return Math.floor((s.max * pct + 99) / 100); }

  function model(wk, sk, revToggle) {
    var g = geom(wk), S = STATES[sk], card = S.card ? CARDS[S.card] : null;
    var revInHand = revToggle == null ? !!S.revInHand : revToggle;
    var cells = [], audits = [];
    var corpses = SUMMONS.filter(function (s) { return !s.alive; });
    var hovered = S.hover != null ? corpses[S.hover] : null;
    var all = card && card.all;
    SUMMONS.forEach(function (s) {
      var front = s.r === 'f', P = g.p(s.r), infoW = g.cellW - P - 10;
      var box = { left: pt(g.x(s.c)), top: pt(g.y(s.r)), width: pt(g.cellW), height: pt(g.h(s.r)) };
      var o = { cls: 'cell e-' + s.el + (front ? ' front' : ' rear'), box: box, ptS: { width: pt(P), height: pt(P), 'font-size': pt(P * 0.46) }, infW: pt(infoW),
        g: s.g, eln: ELN[s.el], chips: [], hp: null, act: true, tag: [], hint: [], ring: [], tint: [], sign: [], ghost: [] };
      if (s.alive) {
        o.cls += ' alive';
        o.chips = [{ t: '' + s.atk, ic: 'melee', k: 'k-atk' }, { t: '', ic: s.nat, k: 'k-dot' }];
        o.hp = { fill: (s.hp / s.max * 100).toFixed(0) + '%', txt: s.hp + '/' + s.max, prev: null };
      } else {
        var rev = S.mode === 'revive', summ = S.mode === 'summon';
        var show = rev || summ || ((sk === 'idle' || sk === 'idleRev') && revInHand);
        if (!show) { o.cls += ' corpse hide'; cells.push(o); return; }
        o.cls += ' corpse' + ((sk === 'idle' || sk === 'idleRev') ? ' faint' : '');
        o.tag = ['阵亡'];
        o.act = false;
        o.chips = [{ t: '' + s.atk, ic: 'melee', k: 'k-dead' }, { t: '', ic: s.nat, k: 'k-dead' }];
        o.hp = { fill: '0%', txt: '0/' + s.max, prev: null };
        if (rev) {
          var h = reviveHp(s, card.pct);
          o.hp = { fill: '0%', txt: '→ ' + h + '/' + s.max, prev: (h / s.max * 100).toFixed(0) + '%' };
          var isHover = hovered === s || (S.drag && all && hovered);
          if (S.drag) {
            o.tint = [{ cls: 'tint' + (isHover ? ' hov' : '') }];
            o.ring = [{ cls: 'ring w-' + card.el + (isHover ? ' hov' : '') }];
            if (hovered === s) {
              var main = all ? '复活全部 · ' + corpses.length + ' 只' : '复活 ' + s.g + ' · 生命 ' + h;
              var dim = all ? '' : ' · ' + s.natName;
              o.sign = [{ main: main, dim: dim, w: pt(signW(main + dim)) }];
              audits.push({ who: s.g + ' 复活签', w: signW(main + dim), cell: g.cellW });
            }
          } else {
            o.ring = [{ cls: 'ring pick' }];
            o.tint = [{ cls: 'tint jade' }];
          }
        }
        if (summ) {
          o.chips = [];
          o.hint = [{ t: revInHand ? '覆盖后无法复活' : '召唤到这里会覆盖它', cls: 'hint' + (revInHand ? ' warn' : '') }];
          audits.push({ who: s.g + ' 覆盖提示', w: textW(o.hint[0].t, 16), cell: infoW });
          if (S.drag) {
            var hov2 = hovered === s;
            o.tint = [{ cls: 'tint jade' + (hov2 ? ' hov' : '') }];
            o.ring = [{ cls: 'ring w-Wood' + (hov2 ? ' hov' : '') }];
            if (hov2) {
              var m2 = '覆盖 ' + s.g, d2 = revInHand ? ' · 无法再复活' : '';
              o.sign = [{ main: m2, dim: d2, w: pt(signW(m2 + d2)) }];
              audits.push({ who: s.g + ' 覆盖签', w: signW(m2 + d2), cell: g.cellW });
            }
          } else {
            o.tint = [{ cls: 'tint jade' }];
          }
        }
        // 头行宽验算:名字 19 + 6 + 徽章 25 + 6 +「阵亡」13 号 26
        audits.push({ who: s.g + ' 头行', w: 19 + 6 + 25 + 6 + 26, cell: infoW });
        audits.push({ who: s.g + ' chip 行', w: chipW('' + s.atk, true) + 4 + chipW('', true), cell: infoW - 4 });
      }
      cells.push(o);
    });
    // 真空位(后排左右)
    var empties = [];
    [[0, 'b'], [2, 'b']].forEach(function (e) {
      var box = { left: pt(g.x(e[0])), top: pt(g.y(e[1])), width: pt(g.cellW), height: pt(g.h(e[1])) };
      var P = g.p(e[1]);
      var on = S.mode === 'summon';
      empties.push({ box: box, on: on, ghostS: { left: pt(g.cellW / 2 - P / 2), top: 0, width: pt(P), height: pt(P) },
        ring: on && S.drag ? [{ cls: 'ring w-Wood' }] : [], label: on ? ['召唤到这里'] : [] });
    });
    return { cells: cells, empties: empties, audits: audits, card: card, fieldW: pt(g.fieldW), height: pt(113 + 4 + 88), cellW: g.cellW };
  }

  return { K: K, pt: pt, WIDTHS: WIDTHS, STATES: STATES, CARDS: CARDS, SUMMONS: SUMMONS, model: model, chipW: chipW, signW: signW, textW: textW };
})();
