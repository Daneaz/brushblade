/* 卡详情「?」五行说明弹窗 · 数据与排版估算。
   文案 = 用户 2026-10-21 审定稿,逐字照抄,不改写(条目「标签:正文」在第一个全角冒号处拆开,只为给标签上色)。
   尺寸单位 = Unity 逻辑单位(稿面 pt = 逻辑 / 2.093)。 */
window.WX = (function () {
  var EL = {
    fire:  { k: 'fire',  c: '火', title: '火 · 烈焰焚身', tag: '主攻系，靠叠灼压血，群攻字最多。',
      items: ['攻击：造成伤害并上灼。灼每回合都会掉血，层数越高掉得越多。半数火字打敌方全体，适合清一大群怪。',
              '五行面 · 燃意：拖到自己或木灵身上，提高攻击，之后每次攻击都给敌人追加灼。群攻火字的燃意覆盖我方全体。'],
      ke: 'metal', by: 'water', rel: '克金，被水克。',
      card: { id: '焱', py: 'yàn', gloss: '火花飞迸' } },
    metal: { k: 'metal', c: '金', title: '金 · 锋芒逼人', tag: '单体爆发系，靠攒战意蓄力。',
      items: ['攻击：造成伤害并获得战意，每层战意攻击 +10%。多数金字只打单体，专门处理精英和 Boss。',
              '五行面 · 铠：获得战意和格挡。格挡能减少下一次受到的伤害，并反击攻击者。',
              '锋刃回鞘：战意满 5 层时打出金系攻击，这一击伤害 +{pct}%{note}，还能吸血；击杀敌人时这张字回到字库。'],
      ke: 'wood', by: 'fire', rel: '克木，被火克。',
      card: { id: '鑫', py: 'xīn', gloss: '三金聚,大盛' } },
    water: { k: 'water', c: '水', title: '水 · 润物无声', tag: '续航控制系，边打边奶。',
      items: ['攻击：造成伤害，并冻结或减速目标，同时按造成的伤害攒「泉」。',
              '五行面 · 润：回复生命，也会攒泉。部分水字的攻击能一次放出全部泉，打出高额伤害。'],
      ke: 'fire', by: 'earth', rel: '克火，被土克。',
      card: { id: '淼', py: 'miǎo', gloss: '水大无际' } },
    earth: { k: 'earth', c: '土', title: '土 · 厚德载物', tag: '防守反击系，最能扛。',
      items: ['攻击：造成伤害，并让目标进入破甲状态：持续期间护甲归零，Boss 护甲减半。同时按造成的伤害攒「厚」。',
              '五行面 · 固：获得护盾和护甲，也会攒厚。部分土字的攻击能一次放出全部厚，打出高额伤害。护盾在整场战斗里一直保留，战斗结束还能带一部分进下一场。'],
      ke: 'water', by: 'wood', rel: '克水，被木克。',
      card: { id: '垚', py: 'yáo', gloss: '三土聚,山高' } },
    wood:  { k: 'wood',  c: '木', title: '木 · 生生不息', tag: '召唤系，靠木灵占场。',
      items: ['攻击：造成伤害并在敌人身上种下「种」。敌人每次行动，我方生命最低的单位都会回血。',
              '五行面 · 生：召唤木灵。木灵会自己攻击，能跨场保留到阵亡。拖到已有的木灵上是嫁接：回满它的生命，并换上新的本命。'],
      ke: 'earth', by: 'metal', rel: '克土，被金克。',
      card: { id: '森', py: 'sēn', gloss: '树木丛生' } }
  };
  var ORDER = ['fire', 'metal', 'water', 'earth', 'wood'];
  var CH = { metal: '金', wood: '木', water: '水', fire: '火', earth: '土' };

  /* 弹窗规格(逻辑单位) */
  var S = { W: 1120, border: 1.5, pad: 24, gap: 14, headH: 46, titleFs: 34, close: 46,
            tagFs: 22, tagLh: 30, itemFs: 23, lh: 1.35, itemGap: 12, markCol: 22, markGap: 6, mark: 10,
            relH: 44, relFs: 22, relPadX: 16 };
  S.inner = S.W - 2 * S.border - 2 * S.pad;                 // 1069
  S.textW = S.inner - S.markCol - S.markGap;                 // 1041
  S.lineH = S.itemFs * S.lh;                                 // 31.05

  /* 金系「锋刃回鞘」的伤害加成是动态值(用户 2026-10-21 补充;spec 2026-10-19-金系锋刃回鞘 §2):
     未学断金 = 100;已学 = 100 + N,N = 断金(metal_4,PerkEffect.MoraleRelease)当前数值 —— Core 的 RunConfig.MoraleReleasePercent。
     已学时数字后加小注「（含断金）」。perk = 0 表示未学。 */
  var BASE_PCT = 100, NOTE = '（含断金）';
  function fill(line, perk) { return line.replace('{pct}', String(BASE_PCT + (perk || 0))).replace('{note}', perk ? NOTE : ''); }
  /* 拆成上色用的段:label(本系 -fg 加粗)/ txt / dyn(动态数字)/ note(小注) */
  function segs(line, perk) {
    var i = line.indexOf('：'), label = line.slice(0, i), body = line.slice(i + 1), out = [{ t: label, cls: 'lb' }, { t: '：', cls: 'tx' }];
    var j = body.indexOf('+{pct}%');
    if (j < 0) { out.push({ t: body, cls: 'tx' }); return out; }
    out.push({ t: body.slice(0, j), cls: 'tx' });
    out.push({ t: '+' + (BASE_PCT + (perk || 0)) + '%', cls: 'dyn' });
    var rest = body.slice(j + '+{pct}%'.length).replace('{note}', '');
    if (perk) out.push({ t: NOTE, cls: 'note' });
    out.push({ t: rest, cls: 'tx' });
    return out;
  }
  /* Ui.WrappedTextHeight:perLine = ⌊宽 / 字号⌋,行数 = ⌈字数 / perLine⌉(每个字都按全角算,宁多不少) */
  function wrapLines(text, fs, width) { var per = Math.max(1, Math.floor(width / fs)); return Math.max(1, Math.ceil(text.length / per)); }
  /* Ui.EstimateWidth:全角 1 × 字号,半角 0.55 × 字号 */
  function estW(text, fs) { var w = 0; for (var i = 0; i < text.length; i++) w += text.charCodeAt(i) < 0x2000 ? 0.55 : 1; return w * fs; }
  function metrics(k, perk) {
    var e = EL[k], items = e.items.map(function (l0) { var l = fill(l0, perk); var n = wrapLines(l, S.itemFs, S.textW); return { len: l.length, lines: n, h: n * S.lineH }; });
    var itemsH = items.reduce(function (a, b) { return a + b.h; }, 0) + S.itemGap * (items.length - 1);
    var H = 2 * S.border + 2 * S.pad + S.headH + S.gap + S.tagLh + S.gap + itemsH + S.gap + S.relH;
    return { items: items, itemsH: itemsH, H: H, titleW: estW(e.title, S.titleFs), tagW: estW(e.tag, S.tagFs), relW: estW(e.rel, S.relFs) };
  }

  /* WCAG 对比度 */
  function lum(hex) { var c = hex.replace('#', ''); return [0, 2, 4].map(function (i) { var v = parseInt(c.substr(i, 2), 16) / 255; return v <= 0.03928 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4); })
    .reduce(function (a, v, i) { return a + v * [0.2126, 0.7152, 0.0722][i]; }, 0); }
  function contrast(a, b) { var x = lum(a), y = lum(b); return (Math.max(x, y) + 0.05) / (Math.min(x, y) + 0.05); }
  var T = { 'panel-paper': '#FBF8F1', 'panel-inset': '#F1EBDE', 'text-quiet': '#2C333D', 'text-dim': '#5D646F',
    'metal-glyph': '#936506', 'metal-fg': '#5B4500', 'wood-glyph': '#1F6333', 'wood-fg': '#195C2E',
    'water-glyph': '#0A5E9E', 'water-fg': '#0E4F86', 'fire-glyph': '#B02D2E', 'fire-fg': '#862726',
    'earth-glyph': '#6A2B22', 'earth-fg': '#542A1D' };

  return { EL: EL, ORDER: ORDER, CH: CH, S: S, fill: fill, segs: segs, NOTE: NOTE, BASE_PCT: BASE_PCT, wrapLines: wrapLines, estW: estW, metrics: metrics, contrast: contrast, T: T };
})();
