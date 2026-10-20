/* 升级成本说明 · 数据。两张 7 × 9 表原样抄自需求(本批同时落进 Core);实现时一格都不手抄,从 Core 的公开表读。
   verify() 按规则重算一遍,与抄录值逐格对账,不一致的格子在画板上标红 —— 防止稿上的数与 Core 走岔。
   ⚠ 重复卡的口径是「倍率^(n−1) 再向上取整」(绿 2.5^2 = 6.25 → 7),不是「逐级取整后再乘」(那样会得 3 × 2.5 → 8)。
     需求原文写的是「逐级向上取整」,抄录值符合前者,见 README「待确认」。 */
window.UHC = (function () {
  var RAR = [
    { k: 'white', n: '白' }, { k: 'green', n: '绿' }, { k: 'blue', n: '蓝' }, { k: 'purple', n: '紫' },
    { k: 'gold', n: '金' }, { k: 'orange', n: '橙' }, { k: 'red', n: '红' }
  ];
  var COPIES = [
    [1, 3, 9, 27, 81, 243, 729, 2187, 6561],
    [1, 3, 7, 16, 40, 98, 245, 611, 1526],
    [1, 3, 7, 16, 40, 98, 245, 611, 1526],
    [1, 2, 4, 8, 16, 32, 64, 128, 256],
    [1, 2, 4, 8, 16, 32, 64, 128, 256],
    [1, 2, 3, 4, 6, 8, 12, 18, 26],
    [1, 2, 3, 4, 6, 8, 12, 18, 26]
  ];
  var INK = [
    [10, 25, 60, 150, 350, 750, 1500, 3000, 6000],
    [15, 37, 90, 225, 525, 1125, 2250, 4500, 9000],
    [20, 50, 120, 300, 700, 1500, 3000, 6000, 12000],
    [25, 62, 150, 375, 875, 1875, 3750, 7500, 15000],
    [30, 75, 180, 450, 1050, 2250, 4500, 9000, 18000],
    [35, 87, 210, 525, 1225, 2625, 5250, 10500, 21000],
    [40, 100, 240, 600, 1400, 3000, 6000, 12000, 24000]
  ];
  var COPY_MUL = [3, 2.5, 2.5, 2, 2, 1.5, 1.5];
  var INK_MUL = [1, 1.5, 2, 2.5, 3, 3.5, 4];
  function sum(a) { return a.reduce(function (s, x) { return s + x; }, 0); }
  // 墨锭千分位 = Ui.InkText(N0 + InvariantCulture);张数不加(规范「文案」一节)
  function ink(n) { return String(n).replace(/\B(?=(\d{3})+(?!\d))/g, ','); }
  function verify() {
    var bad = [];
    for (var r = 0; r < 7; r++) for (var c = 0; c < 9; c++) {
      var cp = Math.ceil(Math.pow(COPY_MUL[r], c) - 1e-9);
      var ik = Math.floor(INK[0][c] * INK_MUL[r] + 1e-9);
      if (cp !== COPIES[r][c]) bad.push('copies ' + RAR[r].n + ' ' + (c + 1) + '→' + (c + 2));
      if (ik !== INK[r][c]) bad.push('ink ' + RAR[r].n + ' ' + (c + 1) + '→' + (c + 2));
    }
    return bad;
  }
  return { RAR: RAR, COPIES: COPIES, INK: INK, COPY_MUL: COPY_MUL, INK_MUL: INK_MUL, sum: sum, ink: ink, verify: verify };
})();
