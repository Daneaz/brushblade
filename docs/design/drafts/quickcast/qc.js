/* 快速施法 · 设计稿共用数据与战场几何。
   来源:spec docs/superpowers/specs/2026-10-20-快速施法-design.md §1 / §3;战场尺寸照 current/BattleScreen(battlegrid 3×2,已落地)。
   几何一律先按 Unity 逻辑单位算,渲染时 ÷ 2.093 成稿面 pt。
   示例数值只为示意(伤害按 Lv 每级 +6% 粗算,×1.5 / ×0.5 按五行相克);实现以 EstimateCast 试算为准。 */
window.QC = (function () {
  var K = 2.093;
  function pt(l) { return Math.round(l / K * 100) / 100; }

  // 两档屏宽。side = 安全区 / MissingInset 让出的两侧;field = _fieldWidth(current/BattleScreen「屏比」表)
  var WIDTHS = {
    base:   { dev: 932, side: 59, field: 1223, msgL: 59, msgW: 814, label: '基准机 19.5:9 · 932pt · 战场 1223(584pt)' },
    narrow: { dev: 764, side: 58.77, field: 909, msgL: 0, msgW: 764, label: '16:9 无刘海 · 764pt · 战场 909(434pt)' }
  };

  // 战场纵向(逻辑):敌后排 109 / 排距 4 / 前排 138 → 251;分隔 12;召唤前排 113 / 排距 4 / 后排 88
  var G = { RowGap: 17, EB: 109, EF: 138, EPB: 96, EPF: 126, SF: 113, SB: 88, SPF: 100, SPB: 75 };
  var Y = { eB: 0, eF: 113, div: 263, sF: 275, sB: 392, H: 480 };

  var EL = { Fire: '火', Metal: '金', Water: '水', Earth: '土', Wood: '木' };
  var KE = { Wood: 'Earth', Earth: 'Water', Water: 'Fire', Fire: 'Metal', Metal: 'Wood' };   // A 克 KE[A]
  function mul(a, b) { return KE[a] === b ? 1.5 : KE[b] === a ? 0.5 : 1; }

  /* 示例字。faces[0] = 攻击面,faces[1] = 五行面;land = 该面可落的落点(spec §2 FaceLanding):
     E 敌人 · P 玩家 · S 活木灵(加护)· G 活木灵(嫁接 / 替换)· O 空召唤位 */
  var CHARS = {
    lin: { id: '林', el: 'Wood', rar: 'Gold', lv: 6, py: 'lín',
      faces: [{ s: '攻', role: '攻击', land: 'E', all: false, est: function (f) { return '伤 ' + Math.round(254 * mul('Wood', f.el)) + (mul('Wood', f.el) === 1 ? '' : ' ×' + mul('Wood', f.el)) + ' · 种'; } },
              { s: '生', role: '五行面', land: 'GO', all: false, est: function () { return '嫁接 / 替换'; } }],
      brief: '「林」攻:伤害 254、种 / 护:召唤林灵', craftHint: '木+木 → 林' },
    jian: { id: '剑', el: 'Metal', rar: 'Blue', lv: 5, py: 'jiàn',
      faces: [{ s: '攻', role: '攻击', land: 'E', all: false, est: function (f) { var m = mul('Metal', f.el); return '伤 ' + Math.round(84 * m) + (m === 1 ? '' : ' ×' + m) + ' · 战意 +1'; } },
              { s: '铠', role: '五行面', land: 'PS', all: false, est: function () { return '战意 +1 · 格挡 +2'; } }],
      brief: '「剑」攻:伤害 84、战意 +1 / 护:战意 +1、格挡 +2' },
    kui: { id: '溃', el: 'Water', rar: 'Blue', lv: 5, py: 'kuì',
      faces: [{ s: '攻', role: '攻击', land: 'E', all: true, est: function (f) { var m = mul('Water', f.el); return '全体 伤 ' + Math.round(56 * m) + (m === 1 ? '' : ' ×' + m) + ' · 引泉'; } },
              // Lv5 溃围:五行面也能落敌方(spec §1 唯一一张「某一侧两面都能落」的字)
              { s: '润', role: '五行面', land: 'EPS', all: true, est: function (f) { return f ? '全体减速 · 我方治疗 56' : '全体治疗 56'; } }],
      brief: '「溃」攻:全体伤害 56、引泉 / 护:全体治疗 56、溃围' },
    // 部件(单面,没有面):选中后 suffix_direct_cast;拆合台不出翻面钮
    huo: { id: '火', el: 'Fire', rar: 'White', lv: 1, py: 'huǒ', part: true,
      faces: [{ s: '', role: '', land: 'E', all: false, est: function () { return ''; } }, { s: '', role: '', land: '', all: false, est: function () { return ''; } }],
      brief: '「火」伤害 30(示意)' }
  };

  var FOES = {
    e0: { n: '墨溅', el: 'Water', img: 'mojian', hp: [222, 300], atk: 90 },
    e2: { n: '悬针', el: 'Metal', img: 'xuanzhen', hp: [270, 270], atk: 110 },
    e3: { n: '焦痕', el: 'Fire', img: 'jiaohen', hp: [221, 480], atk: 96, burn: 3 },
    e4: { n: '败笔', el: 'Fire', img: 'baibi', hp: [450, 450], atk: 120 },
    e5: { n: '灯花', el: 'Fire', img: 'denghua', hp: [288, 360], atk: 84 }
  };
  var ERB = ['e0', '', 'e2'], ERF = ['e3', 'e4', 'e5'];
  var SUMS = { s0: { n: '林', g: '林', hp: [300, 346], atk: 68 }, s4: { n: '藤', g: '藤', hp: [104, 141], atk: 40 } };
  var SRF = ['s0', 's1', 's2'], SRB = ['s3', 's4', 's5'];
  var HAND = ['lin', 'jian', 'kui', 'yan', 'leng'];
  var EXTRA = { yan: { id: '炎', el: 'Fire', rar: 'Gold', py: 'yán' }, leng: { id: '冷', el: 'Water', rar: 'Green', py: 'lěng' } };

  /* 快速施法的面选择(spec §2 CardFaceRules.QuickCastFace):
     这一侧可落的面只有一面 → 那一面;两面都能 → 朝上的面;没有 → null。未开快速施法:两侧都 = 朝上的面。 */
  function hasAny(face, keys) { for (var i = 0; i < keys.length; i++) if (face.land.indexOf(keys[i]) >= 0) return true; return false; }
  function sideFace(c, side, up, quick) {
    if (!quick) { var f = c.faces[up]; return hasAny(f, side === 'E' ? 'E' : 'PSGO') ? up : null; }
    var keys = side === 'E' ? 'E' : 'PSGO', ok = [0, 1].filter(function (i) { return hasAny(c.faces[i], keys); });
    if (ok.length === 2) return up;
    return ok.length ? ok[0] : null;
  }
  function ringOf(c, fi) { return fi === 0 ? 'var(--ink)' : 'var(--' + c.el.toLowerCase() + '-glyph)'; }
  function sealCls(fi, size) { return 'seal ' + (fi === 0 ? 'atk' : 'feat') + (size ? ' ' + size : ''); }

  /* 整屏战斗模型。o = { w, ch, up, quick, mode: 'idle' | 'drag' | 'pick', hover, sel } */
  function battle(o) {
    var W = WIDTHS[o.w], F = Math.min(W.field, 1e9), cellW = (F - 2 * G.RowGap) / 3;
    var c = CHARS[o.ch], up = o.up;
    var eFace = sideFace(c, 'E', up, o.quick), aFace = sideFace(c, 'A', up, o.quick);
    var drag = o.mode === 'drag', pick = o.mode === 'pick';
    var hov = o.hover || '';
    var hovSide = hov ? (FOES[hov] ? 'E' : 'A') : '';
    var hovAllE = drag && hovSide === 'E' && eFace !== null && c.faces[eFace].all;
    function colX(i) { return i * (cellW + G.RowGap); }

    function mark(side, kind) {    // kind: 'E' | 'P' | 'S' | 'G' | 'O'
      var fi = side === 'E' ? eFace : aFace;
      if (fi === null) return null;
      var f = c.faces[fi];
      if (kind === 'S' || kind === 'G') return (f.land.indexOf('S') >= 0 || f.land.indexOf('G') >= 0) ? fi : null;
      return f.land.indexOf(kind) >= 0 ? fi : null;
    }

    var units = [], slots = [];
    function foe(id, col, front) {
      var x = colX(col), y = front ? Y.eF : Y.eB, h = front ? G.EF : G.EB, port = front ? G.EPF : G.EPB;
      var pos = 'left:' + pt(x) + 'px;top:' + pt(y) + 'px;width:' + pt(cellW) + 'px;height:' + pt(h) + 'px';
      if (!id) { slots.push({ cls: 'eslot', style: pos }); return; }
      var e = FOES[id], fi = (drag || pick) ? mark('E', 'E') : null;
      var isHov = drag && (hov === id || (hovAllE && fi !== null));
      var u = { id: id, foe: true, el: e.el, n: e.n, eln: EL[e.el], img: '../scenes/mob_' + e.img + '.png',
        style: pos, ptStyle: 'width:' + pt(port) + 'px;height:' + pt(port) + 'px',
        hp: 'width:' + Math.round(e.hp[0] / e.hp[1] * 100) + '%', hpt: e.hp[0] + '/' + e.hp[1],
        atb: 'width:' + (30 + (id.charCodeAt(1) * 17) % 55) + '%', chips: [{ cls: 'sc', t: '⚔ ' + e.atk }].concat(e.burn ? [{ cls: 'sc dot', t: '灼 ' + e.burn }] : []) };
      decorate(u, fi, isHov, drag && hov === id ? c.faces[fi === null ? 0 : fi].est(e) : '', 'ink', fi === null ? '' : sealCls(fi), fi === null ? '' : c.faces[fi].s);
      units.push(u);
    }
    function ally(id, col, front) {
      var x = colX(col), y = front ? Y.sF : Y.sB, h = front ? G.SF : G.SB, port = front ? G.SPF : G.SPB;
      var pos = 'left:' + pt(x) + 'px;top:' + pt(y) + 'px;width:' + pt(cellW) + 'px;height:' + pt(h) + 'px';
      var s = SUMS[id];
      if (!s) {
        var fo = (drag || pick) ? mark('A', 'O') : null;
        var sl = { id: id, cls: 'sslot', style: pos, lt: '', lr: '', lrCls: 'lr', lbl: '', ghost: '' };
        if (fo !== null) {
          sl.lt = 'background:rgba(52,134,75,.10)';
          sl.ghost = '召唤到这里';
          if (drag) { sl.lr = '--ring:' + ringOf(c, fo); sl.lrCls = 'lr' + (hov === id ? ' hov' : ''); }
        }
        slots.push(sl); return;
      }
      var fi = (drag || pick) ? mark('A', 'S') : null;
      var u = { id: id, foe: false, el: 'Wood', n: s.n, eln: '木', g: s.g,
        style: pos, ptStyle: 'width:' + pt(port) + 'px;height:' + pt(port) + 'px;font-size:' + pt(port * 0.46) + 'px',
        hp: 'width:' + Math.round(s.hp[0] / s.hp[1] * 100) + '%', hpt: s.hp[0] + '/' + s.hp[1], atb: 'width:46%', chips: [{ cls: 'sc', t: '⚔ ' + s.atk }] };
      decorate(u, fi, drag && hov === id, drag && hov === id && fi !== null ? c.faces[fi].est(null) : '', 'ink', fi === null ? '' : sealCls(fi), fi === null ? '' : c.faces[fi].s);
      units.push(u);
    }
    function decorate(u, fi, isHov, lbl, tint, sc, s) {
      u.cls = 'u ' + (u.foe ? 'foe' : 'ally') + ' e-' + u.el;
      u.lt = ''; u.lr = ''; u.lrCls = 'lr'; u.lbl = ''; u.lblSeal = ''; u.lblSealCls = '';
      if (!(drag || pick)) return;
      if (fi === null) { if (drag) u.cls += ' tg-no'; return; }   // 双向态不压暗非目标(现状)
      if (pick) { u.lt = 'background:rgba(17,22,31,.07)'; return; }     // 双向态沿用现状:整格压墨 7%,不描环
      u.lt = 'background:rgba(17,22,31,' + (isHov ? '.09' : '.05') + ')';
      u.lr = '--ring:' + ringOf(c, fi); u.lrCls = 'lr' + (isHov ? ' hov' : '');
      if (lbl) { u.lbl = lbl; }
    }
    ERB.forEach(function (id, i) { foe(id, i, false); });
    ERF.forEach(function (id, i) { foe(id, i, true); });
    SRF.forEach(function (id, i) { ally(id, i, true); });
    SRB.forEach(function (id, i) { ally(id, i, false); });

    // 执笔人条
    var pf = (drag || pick) ? mark('A', 'P') : null, pl = { cls: 'pl', lt: '', lr: '', lrCls: 'lr', lbl: '' };
    if (drag || pick) {
      if (pf === null) { if (drag) pl.cls += ' tg-no'; }
      else if (pick) pl.lt = 'background:rgba(17,22,31,.07)';
      else { pl.lt = 'background:rgba(17,22,31,' + (hov === 'p' ? '.09' : '.05') + ')'; pl.lr = '--ring:' + ringOf(c, pf); pl.lrCls = 'lr' + (hov === 'p' ? ' hov' : ''); if (hov === 'p') pl.lbl = c.faces[pf].est(null); }
    }

    // 跟手牌:悬停哪一侧就翻到那一侧会出的面(★ 本稿新增);没悬停在落点上 = 朝上的面
    var gFace = up;
    if (drag && hovSide === 'E' && eFace !== null) gFace = eFace;
    if (drag && hovSide === 'A' && aFace !== null) gFace = aFace;

    var hand = HAND.map(function (k, i) {
      var h = CHARS[k] || EXTRA[k], two = !!CHARS[k], isC = k === o.ch;
      var fi = isC ? up : 0;
      return { g: h.id, py: h.py, seal: two || k === 'yan' || k === 'leng' ? (fi === 0 ? '攻' : (h.faces ? h.faces[1].s : '')) : '', sealCls: sealCls(fi),
        cls: 'tile e-' + h.el + ' r-' + h.rar + (fi === 1 ? ' back' : '') + (isC && (o.sel || pick) ? ' sel' : '') + (isC && drag ? ' lifted' : ''), k: k };
    });

    var quickBoth = o.quick && eFace !== null && aFace !== null && eFace !== aFace;
    return {
      W: W, F: F, cellW: cellW, eFace: eFace, aFace: aFace, quickBoth: quickBoth, gFace: gFace,
      devStyle: 'width:' + W.dev + 'px', frameStyle: 'left:' + W.side + 'px;right:' + W.side + 'px',
      guide: ['left:' + W.side + 'px', 'left:' + (W.dev - W.side) + 'px'],
      msgStyle: 'left:' + (W.msgL + W.msgW * 0.02) + 'px;right:auto;width:' + (W.msgW * 0.96) + 'px',
      fieldStyle: 'width:' + pt(F) + 'px;height:' + pt(Y.H) + 'px',
      dividerStyle: 'top:' + pt(Y.div) + 'px',
      plStyle: 'width:' + pt(F) + 'px', rowStyle: 'width:' + pt(F) + 'px',
      units: units, slots: slots, pl: pl, hand: hand
    };
  }

  // 文案(字符串表 key → 中文)。★ = 本稿新增
  var STR = {
    'settings.quick_cast': '快速施法',
    'settings.quick_cast.desc': '两面字不用先翻面：对敌方出攻击面，对我方出五行面',
    'battle.hint.drag_release': '松手出手 · 拖回手牌行取消',
    'battle.hint.drag_release_quick': '松手出手 · 敌方用攻击面，我方用五行面 · 拖回手牌行取消',
    'battle.hint.suffix_tap_again_flip': '|再点翻面|拖出按朝上的面出手',
    'battle.hint.suffix_tap_again_quick': '|再点翻面|拖出：敌方用攻击面，我方用五行面',
    'battle.hint.suffix_tap_again_cast': '|拖到目标上出手',
    'battle.hint.suffix_direct_cast': '|直出:部件不入库直接打出|拖到目标上出手',
    'battle.hint.flip_selected': '再点这张牌：翻面\n拖出：按朝上的那一面出手',
    'battle.hint.flip_selected_quick': '再点这张牌：翻面\n拖出：敌方用攻击面，我方用五行面',
    'battle.hint.drag_to_cast': '拖到目标上出手',
    'charsheet.foot.battle': '长按看详情，不耗 AP · 点已选中的牌翻面，拖出时用朝上的那一面',
    'charsheet.foot.battle_quick': '长按看详情，不耗 AP · 快速施法：对敌方用攻击面，对我方用五行面'
  };
  function T(k, args) { var s = STR[k]; if (args) Object.keys(args).forEach(function (a) { s = s.replace('{' + a + '}', args[a]); }); return s; }
  // Ui.ChipWidth 口径的文字宽:全角 1 × 字号、半角 0.55 × 字号(逻辑单位)
  function textW(t, f) { var w = 0; for (var i = 0; i < t.length; i++) w += t.charCodeAt(i) < 0x2000 ? 0.55 : 1; return w * f; }

  return { K: K, pt: pt, WIDTHS: WIDTHS, CHARS: CHARS, FOES: FOES, SUMS: SUMS, EL: EL, battle: battle, sideFace: sideFace, ringOf: ringOf, sealCls: sealCls, STR: STR, T: T, textW: textW };
})();
