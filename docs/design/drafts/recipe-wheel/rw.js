/* 配方转盘 · 数据与几何。
   概率:高档 10%,4 个低档各 22.5%。扇区角度 = 概率 × 360°(低档 81°、高档 36°)—— 面积即概率,不误导。
   样例字、配方取自 chars.json(按稀有度与层段分档,spec §2)。 */
window.RW = (function () {
  var TIERS = {
    t11: { n: '11–20 层', depth: 15, kind: '精英', low: 'Blue', high: 'Purple',
      lows: [['炸', 'Fire', ['火', '乍']], ['海', 'Water', ['氵', '每']], ['锥', 'Metal', ['钅', '隹']], ['堡', 'Earth', ['保', '土']]],
      hi: ['烈', 'Fire', ['列', '灬']] },
    t21: { n: '21–30 层', depth: 30, kind: 'Boss', low: 'Purple', high: 'Gold',
      lows: [['楸', 'Wood', ['木', '秋']], ['湮', 'Water', ['氵', '垔']], ['铡', 'Metal', ['钅', '则']], ['燥', 'Fire', ['火', '喿']]],
      hi: ['沐', 'Water', ['氵', '木']] },
    t31: { n: '31–40 层', depth: 35, kind: '精英', low: 'Gold', high: 'Orange',
      lows: [['炎', 'Fire', ['火', '火']], ['林', 'Wood', ['木', '木']], ['冰', 'Water', ['冫', '水']], ['圭', 'Earth', ['土', '土']]],
      hi: ['森', 'Wood', ['木', '林']] },
    t41: { n: '41 层起', depth: 50, kind: 'Boss', low: 'Orange', high: 'Red',
      lows: [['焚', 'Fire', ['林', '火']], ['淼', 'Water', ['水', '冰']], ['鑫', 'Metal', ['金', '鍂']], ['桂', 'Wood', ['木', '圭']]],
      hi: ['燚', 'Fire', ['火', '焱']] }
  };
  var RAR = { Blue: '蓝', Purple: '紫', Gold: '金', Orange: '橙', Red: '红' };
  var EL = { Fire: '火', Water: '水', Wood: '木', Metal: '金', Earth: '土' };
  // 扇区顺序(顺时针,从指针正下起):低 低 高 低 低;高档夹在中间,转动时它会一次次从指针下掠过
  function slots(t) {
    var T = TIERS[t];
    return [
      { c: T.lows[0], rar: T.low, p: 22.5 }, { c: T.lows[1], rar: T.low, p: 22.5 },
      { c: T.hi, rar: T.high, p: 10, hi: true },
      { c: T.lows[2], rar: T.low, p: 22.5 }, { c: T.lows[3], rar: T.low, p: 22.5 }
    ];
  }
  var R = 104;   // 转盘半径(pt)
  function pt(a, r) { var t = (a - 90) * Math.PI / 180; return [Math.cos(t) * r, Math.sin(t) * r]; }
  function arc(a0, a1, r) {
    var p0 = pt(a0, r), p1 = pt(a1, r), big = a1 - a0 > 180 ? 1 : 0;
    return 'M0 0 L' + p0[0].toFixed(2) + ' ' + p0[1].toFixed(2) + ' A' + r + ' ' + r + ' 0 ' + big + ' 1 ' + p1[0].toFixed(2) + ' ' + p1[1].toFixed(2) + ' Z';
  }
  function geom(t) {
    var a = 0;
    return slots(t).map(function (s, i) {
      var span = s.p * 3.6, a0 = a, a1 = a + span, mid = a + span / 2; a = a1;
      var g = pt(mid, R * 0.6), lab = pt(mid, R * 0.86);
      return { i: i, s: s, a0: a0, a1: a1, mid: mid, d: arc(a0, a1, R), gx: g[0], gy: g[1], lx: lab[0], ly: lab[1] };
    });
  }
  return { TIERS: TIERS, RAR: RAR, EL: EL, slots: slots, geom: geom, R: R };
})();
