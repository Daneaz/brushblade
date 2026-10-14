// 两块画板共用的排版模型:把一张字、一档等级排成「两面网格」的格子列表。
// 两面是**同一张网格的两列**:每一级(槽)占一行,行高取两面里较高的那一格 —— 两面结构对称、同级并排。
// Unity 侧同理:先按槽算两面各自的行高、取大,再画(FaceHeight 已是先算后画)。
(function () {
  var AT = window.AT;
  var LOCK = '#ic-lockmini';

  function cellPos(row, col) { return 'grid-row:' + row + ';grid-column:' + col + ';'; }

  // opts: { L, battle, up (0/1/null), spent: {key:true}, bubKey, pick(e,key,title,terms) }
  AT.model = function (id, opts) {
    var c = AT.CHARS[id], L = opts.L;
    var faceSlots = [[], []], shared = [];
    c.slots.forEach(function (s) {
      if (s.fi < 0) shared.push(s); else faceSlots[s.fi].push(s);
    });

    function rowOf(s, fi) {
      var key = 'lv' + s.lv + '-' + fi, on = L >= s.lv, spent = on && opts.spent && opts.spent[key];
      var text = AT.textOf(s, L);
      var chip = AT.chipOf(s.icon);
      var cls = 'tr' + (fi === 1 ? ' bk' : fi === 0 ? ' fr' : ' sh') + (s.nature ? ' nat' : '')
        + (!on ? ' off' : spent ? ' spent' : '') + (opts.bubKey === key ? ' open' : '');
      return {
        key: key, lv: s.lv, name: s.name, kind: s.kind, cls: cls,
        chip: chip ? [{ cls: chip.cls, ic: '#ic-' + chip.icon }] : [],
        lock: on ? [] : [{ t: 'Lv' + s.lv + ' 解锁' }],
        used: spent ? [{ t: '本场已用' }] : [],
        segs: AT.segs(text),
        pick: function (e) { opts.pick(e, key, s.name, AT.terms(text, s.extra)); }
      };
    }

    var rows = [], voids = [], locks = [];
    var gridRow = 3;
    var lockedNames = [[], []];
    AT.SLOTS.forEach(function (lv) {
      var a = faceSlots[0].filter(function (s) { return s.lv === lv; })[0];
      var b = faceSlots[1].filter(function (s) { return s.lv === lv; })[0];
      if (!a && !b) return;
      // 战斗版:未解锁的槽不占行,收进每面底部的一行
      if (opts.battle && L < lv) {
        if (a) lockedNames[0].push('Lv' + lv + ' ' + a.name);
        if (b) lockedNames[1].push('Lv' + lv + ' ' + b.name);
        return;
      }
      [a, b].forEach(function (s, fi) {
        var pos = cellPos(gridRow, fi + 1);
        if (s) { var r = rowOf(s, fi); r.pos = pos; rows.push(r); }
        else voids.push({ pos: pos, t: '这一面没有 Lv' + lv, cls: 'void' + (L < lv ? ' off' : '') });
      });
      gridRow++;
    });
    if (opts.battle && (lockedNames[0].length || lockedNames[1].length)) {
      [0, 1].forEach(function (fi) {
        if (lockedNames[fi].length) locks.push({ pos: cellPos(gridRow, fi + 1), t: lockedNames[fi].join(' · ') + ' 未解锁', cls: 'lockln' + (fi ? ' bk' : '') });
      });
      gridRow++;
    }

    var heads = [], stats = [], bgs = [];
    c.faces.forEach(function (f, fi) {
      var up = opts.up === fi;
      bgs.push({ cls: 'fbg ' + (fi ? 'back' : 'front') + (up ? ' up' : ''), pos: 'grid-row:1 / ' + (gridRow + 1) + ';grid-column:' + (fi + 1) + ';' });
      heads.push({ pos: cellPos(1, fi + 1), s: f.s, sealCls: 'seal l main' + (f.s === '攻' ? ' atk' : ''), role: f.role, landing: f.landing,
        upTag: up ? [{ t: '朝上' }] : [] });
      stats.push({ pos: cellPos(2, fi + 1), cells: f.stats(L).map(function (st, i) {
        var key = 'st' + fi + i;
        return { k: st.k, v: st.v, u: st.u || '', cls: 'sb' + (fi ? ' bk' : '') + (st.t ? ' t' : '') + (opts.bubKey === key ? ' open' : ''),
          pick: function (e) { if (st.t) opts.pick(e, key, st.k, [st.t]); } };
      }) });
    });
    var ends = [{ pos: cellPos(gridRow, 1) }, { pos: cellPos(gridRow, 2) }];

    var sharedRows = shared.map(function (s) { return rowOf(s, -1); });
    if (opts.battle) sharedRows = sharedRows.filter(function (r) { return r.lock.length === 0; });
    return { bgs: bgs, heads: heads, stats: stats, rows: rows, voids: voids, locks: locks, ends: ends, shared: sharedRows };
  };

  // 气泡:每个词条一行 = 图标 chip(与战场同色)+ 词条名(+ 别名)+ 说明
  AT.bubbleItems = function (terms) {
    return terms.map(function (k) {
      var g = AT.GLOSS[k] || { d: '(词条表缺此条)' }, chip = AT.chipOf(k);
      return { k: k, alias: g.alias ? '（' + g.alias + '）' : '', v: g.d,
        chip: chip ? [{ cls: chip.cls, ic: '#ic-' + chip.icon }] : [], blank: chip ? [] : [{}] };
    });
  };

  AT.schemeStyle = function (key) {
    var s = AT.SCHEMES[key];
    return '--body:' + s.body + ';--term:' + s.term + ';--num:' + s.num + ';';
  };

  // 小锁(尾标用),补进图标表
  function addLock() {
    var defs = document.querySelector('#zd-icons defs');
    if (!defs) return setTimeout(addLock, 30);
    if (document.getElementById('ic-lockmini')) return;
    defs.insertAdjacentHTML('beforeend', '<symbol id="ic-lockmini" viewBox="0 0 64 64"><path fill="none" stroke="currentColor" stroke-width="7" stroke-linecap="round" d="M21 30v-8a11 11 0 0 1 22 0v8"/><path fill="currentColor" d="M15 30h34v24H15z"/></symbol>');
  }
  if (document.body) addLock(); else document.addEventListener('DOMContentLoaded', addLock);
  AT.LOCK = LOCK;
})();
