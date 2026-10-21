/* 削甲图标稿数据。CUR = tools/icons/build_icons.py 的 ICONS 原样抄出(2026-10-21,改管线后此处会过期,仅供并排对比);
   CAND = 本稿三枚候选(64 画布、白色、STROKE 6 圆头圆角 / FILL,与 ICONS 同口径)。 */
window.XJ = (function () {
  var CUR = {
  "armorbreak": "<path fill=\"none\" stroke=\"#fff\" stroke-width=\"6\" stroke-linecap=\"round\" stroke-linejoin=\"round\" d=\"M32 7l21 8v18c0 12-10 21-21 25-11-4-21-13-21-25V15z\"/><path fill=\"none\" stroke=\"#fff\" stroke-width=\"6\" stroke-linecap=\"round\" stroke-linejoin=\"round\" d=\"M37 17l-10 15h11l-10 15\"/>",
  "defense": "<path fill=\"#fff\" d=\"M32 6l22 8v19c0 13-11 23-22 27-11-4-22-14-22-27V14z\"/>",
  "shield": "<path fill=\"none\" stroke=\"#fff\" stroke-width=\"6\" stroke-linecap=\"round\" stroke-linejoin=\"round\" d=\"M32 7l22 8v18c0 13-11 24-22 28-11-4-22-15-22-28V15z\"/>",
  "immunity": "<path fill=\"none\" stroke=\"#fff\" stroke-width=\"6\" stroke-linecap=\"round\" stroke-linejoin=\"round\" d=\"M32 7l21 8v18c0 12-10 21-21 25-11-4-21-13-21-25V15z\"/><path fill=\"none\" stroke=\"#fff\" stroke-width=\"6\" stroke-linecap=\"round\" stroke-linejoin=\"round\" d=\"M23 32l7 8 12-14\"/>",
  "block": "<path fill=\"none\" stroke=\"#fff\" stroke-width=\"6\" stroke-linecap=\"round\" stroke-linejoin=\"round\" d=\"M32 7l22 8v18c0 13-11 24-22 28-11-4-22-15-22-28V15z\"/><path fill=\"none\" stroke=\"#fff\" stroke-width=\"6\" stroke-linecap=\"round\" stroke-linejoin=\"round\" d=\"M38 23l-10 10 10 10\"/>",
  "slow": "<path fill=\"none\" stroke=\"#fff\" stroke-width=\"6\" stroke-linecap=\"round\" stroke-linejoin=\"round\" d=\"M32 10v32M18 30l14 14 14-14\"/>",
  "blind": "<path fill=\"none\" stroke=\"#fff\" stroke-width=\"6\" stroke-linecap=\"round\" stroke-linejoin=\"round\" d=\"M8 32c10-13 38-13 48 0-10 13-38 13-48 0z\"/><circle cx=\"32\" cy=\"32\" r=\"6\" fill=\"#fff\"/><path fill=\"none\" stroke=\"#fff\" stroke-width=\"6\" stroke-linecap=\"round\" stroke-linejoin=\"round\" d=\"M14 50L50 14\"/>",
  "silence": "<path fill=\"none\" stroke=\"#fff\" stroke-width=\"6\" stroke-linecap=\"round\" stroke-linejoin=\"round\" d=\"M16 20h32M16 32h24M16 44h32\"/><path fill=\"none\" stroke=\"#fff\" stroke-width=\"6\" stroke-linecap=\"round\" stroke-linejoin=\"round\" d=\"M14 50L50 14\"/>",
  "curse": "<path fill=\"#fff\" d=\"M32 52L12 18h40z\"/>",
  "healblock": "<path fill=\"#fff\" d=\"M26 12h12v5.5L17.5 38H12V26h14z\"/><path fill=\"#fff\" d=\"M46.5 26H52v12H38v14H26v-5.5z\"/><path fill=\"none\" stroke=\"#fff\" stroke-width=\"6\" stroke-linecap=\"round\" stroke-linejoin=\"round\" d=\"M12 52L52 12\"/>",
  "buffblock": "<path fill=\"#fff\" d=\"M32 10L38.5 21.7 10.2 50H10z\"/><path fill=\"#fff\" d=\"M44.5 32.7L54 50H27.2z\"/><path fill=\"none\" stroke=\"#fff\" stroke-width=\"6\" stroke-linecap=\"round\" stroke-linejoin=\"round\" d=\"M14 54L54 14\"/>",
  "skillcut": "<circle cx=\"32\" cy=\"32\" r=\"22\" fill=\"none\" stroke=\"#fff\" stroke-width=\"6\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/><path fill=\"#fff\" d=\"M32 10a22 22 0 0 0 0 44z\"/>",
  "freeze": "<path fill=\"none\" stroke=\"#fff\" stroke-width=\"6\" stroke-linecap=\"round\" stroke-linejoin=\"round\" d=\"M32 8v48M11 20l42 24M53 20L11 44\"/>",
  "burn": "<path fill=\"#fff\" d=\"M33 5c11 13 17 21 17 30a18 18 0 0 1-36 0c0-6 3-11 7-14-1 5 1 9 4 11 3-9-2-16-5-21 5 1 9-1 13-6z\"/>",
  "bleed": "<path fill=\"#fff\" d=\"M31 6c8 12 13 19 13 25a13 13 0 0 1-26 0c0-6 5-13 13-25z\"/><circle cx=\"15\" cy=\"49\" r=\"5\" fill=\"#fff\"/><circle cx=\"47\" cy=\"52\" r=\"4\" fill=\"#fff\"/>",
  "ranged": "<path fill=\"none\" stroke=\"#fff\" stroke-width=\"6\" stroke-linecap=\"round\" stroke-linejoin=\"round\" d=\"M15 10c13 8 13 36 0 44\"/><path fill=\"none\" stroke=\"#fff\" stroke-width=\"6\" stroke-linecap=\"round\" stroke-linejoin=\"round\" d=\"M15 32h33M39 22l10 10-10 10\"/>",
  "focus": "<circle cx=\"32\" cy=\"32\" r=\"17\" fill=\"none\" stroke=\"#fff\" stroke-width=\"6\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/><path fill=\"none\" stroke=\"#fff\" stroke-width=\"6\" stroke-linecap=\"round\" stroke-linejoin=\"round\" d=\"M32 5v10M32 49v10M5 32h10M49 32h10\"/>"
};
  var CAND = {
  "A": "<path fill=\"#fff\" d=\"M29 7l6 2.2L51 31v3c0 13-11 23-22 27-11-4-22-14-22-27V15z\"/><path fill=\"#fff\" d=\"M40 5.6L56 11.4v16z\"/>",
  "B": "<path fill=\"none\" stroke=\"#fff\" stroke-width=\"6\" stroke-linecap=\"round\" stroke-linejoin=\"round\" d=\"M53 31v2c0 12-10 21-21 25-11-4-21-13-21-25V15l21-8 4 1.5z\"/><path fill=\"none\" stroke=\"#fff\" stroke-width=\"6\" stroke-linecap=\"round\" stroke-linejoin=\"round\" d=\"M44 8l11 15\"/>",
  "C": "<path fill=\"#fff\" d=\"M10 27H54v6c0 13-11 23-22 27-11-4-22-14-22-27z\"/><path fill=\"#fff\" transform=\"rotate(-8 31 14)\" d=\"M31 4l22 8v8H9v-8z\"/>"
};
  function uri(frag) {
    return 'data:image/svg+xml;utf8,' + encodeURIComponent('<svg xmlns="http://www.w3.org/2000/svg" width="64" height="64" viewBox="0 0 64 64">' + frag + '</svg>');
  }
  function icon(key, cand) { return uri(key === 'armorshred' ? CAND[cand] : CUR[key]); }
  /* 逻辑单位(Unity)。BattleView:UnitChipFontSize 15 / PadX 10 / PadY 5;Icons.Size 18 / Gap 3;ChipSpacing 4 */
  var CH = { font: 15, padX: 10, padY: 5, icon: 18, gap: 3, spacing: 4 };
  function textW(t, fs) { var w = 0; for (var i = 0; i < t.length; i++) w += t.charCodeAt(i) < 0x2000 ? 0.55 : 1; return w * fs; }
  function chipW(text, hasIcon) { return textW(text, CH.font) + CH.padX + (hasIcon ? CH.icon + (text ? CH.gap : 0) : 0); }
  return { CUR: CUR, CAND: CAND, uri: uri, icon: icon, CH: CH, textW: textW, chipW: chipW };
})();
