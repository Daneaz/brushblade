/* 五行配色重设计 · 新旧色板与度量。
   度量口径:对比度 = WCAG 2.x;ΔE = CIE76(L*a*b*,D65);色觉模拟 = Machado 2009 严重度 1.0(线性 RGB)。 */
window.WX = (function () {
  var ORDER = ['Metal', 'Wood', 'Water', 'Fire', 'Earth'];
  var NAME = { Metal: '金', Wood: '木', Water: '水', Fire: '火', Earth: '土' };
  var GLYPH = { Metal: '锋', Wood: '林', Water: '泽', Fire: '炎', Earth: '垒' };
  var PART = { Metal: '钅', Wood: '木', Water: '氵', Fire: '火', Earth: '土' };
  // [主色, 淡底, 深前景, 字形色, 五行面底(= card-face 往淡底 34%,派生)]
  var OLD = {
    Metal: ['#B3A381', '#EEE3CF', '#64532C', '#6B6449', '#F9F4E9'],
    Wood:  ['#348F4F', '#CCE6D0', '#195C2E', '#1F6333', '#EEF5EA'],
    Water: ['#0F74C5', '#C8E1FB', '#0E4F86', '#0A5E9E', '#ECF3F8'],
    Fire:  ['#C53637', '#FED2CE', '#862726', '#B02D2E', '#FFEEE9'],
    Earth: ['#8F7332', '#ECDCBD', '#634800', '#82552A', '#F9F2E3']
  };
  var NEW = {
    Metal: ['#916F0C', '#F7E7AC', '#5B4500', '#735800', '#FCF6DE'],
    Wood:  ['#348F4F', '#CCE6D0', '#195C2E', '#1F6333', '#EEF5EA'],
    Water: ['#0F74C5', '#C8E1FB', '#0E4F86', '#0A5E9E', '#ECF3F8'],
    Fire:  ['#C53637', '#FED2CE', '#862726', '#B02D2E', '#FFEEE9'],
    Earth: ['#784330', '#E3D1C1', '#542A1D', '#683623', '#F5EEE5']
  };
  function hx(h) { h = h.replace('#', ''); return [0, 2, 4].map(function (i) { return parseInt(h.substr(i, 2), 16) / 255; }); }
  function lin(c) { return c <= .04045 ? c / 12.92 : Math.pow((c + .055) / 1.055, 2.4); }
  function unl(c) { c = Math.max(0, Math.min(1, c)); return c <= .0031308 ? 12.92 * c : 1.055 * Math.pow(c, 1 / 2.4) - .055; }
  function lum(h) { var v = hx(h).map(lin); return .2126 * v[0] + .7152 * v[1] + .0722 * v[2]; }
  function cr(a, b) { var x = lum(a), y = lum(b); if (x < y) { var t = x; x = y; y = t; } return (x + .05) / (y + .05); }
  var MM = { deutan: [[0.367322, 0.860646, -0.227968], [0.280085, 0.672501, 0.047413], [-0.011820, 0.042940, 0.968881]],
             protan: [[0.152286, 1.052583, -0.204868], [0.114503, 0.786281, 0.099216], [-0.003882, -0.048116, 1.051998]] };
  function sim(h, k) { if (!k || k === 'normal') return h; var v = hx(h).map(lin), m = MM[k];
    return '#' + m.map(function (r) { var o = r[0] * v[0] + r[1] * v[1] + r[2] * v[2]; return ('0' + Math.round(unl(o) * 255).toString(16)).slice(-2); }).join('').toUpperCase(); }
  function lab(h) { var v = hx(h).map(lin);
    var X = (0.4124 * v[0] + 0.3576 * v[1] + 0.1805 * v[2]) / 0.95047, Y = 0.2126 * v[0] + 0.7152 * v[1] + 0.0722 * v[2], Z = (0.0193 * v[0] + 0.1192 * v[1] + 0.9505 * v[2]) / 1.08883;
    function f(t) { return t > 0.008856 ? Math.cbrt(t) : 7.787 * t + 16 / 116; }
    return [116 * f(Y) - 16, 500 * (f(X) - f(Y)), 200 * (f(Y) - f(Z))]; }
  function de(a, b) { var A = lab(a), B = lab(b); return Math.hypot(A[0] - B[0], A[1] - B[1], A[2] - B[2]); }
  return { ORDER: ORDER, NAME: NAME, GLYPH: GLYPH, PART: PART, OLD: OLD, NEW: NEW, cr: cr, sim: sim, de: de };
})();
