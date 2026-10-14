// 字卡详情 · 技能说明区改版(2026-10-15 出稿)的示意数据与排版模型。
// 数值取 2026-10-15 的 chars.json,仅作示意(机制改版后会变);文案按字符串表模板手写。
// 正文记号:[词条] {数字}。一面的特性按「槽」排:Lv1 / Lv3 / Lv4 / Lv5 / Lv6 / Lv8;
// fi = 0 攻击面、1 五行面、-1 两面通用(进底部「两面」条)。
window.AT = (function () {
  var EL = { Fire: '火', Metal: '金', Water: '水', Earth: '土', Wood: '木' };
  var RAR = { White: '白', Green: '绿', Blue: '蓝', Purple: '紫', Gold: '金', Orange: '橙', Red: '红' };
  var KE = { Wood: 'Earth', Earth: 'Water', Water: 'Fire', Fire: 'Metal', Metal: 'Wood' };
  var KE_BY = {}; Object.keys(KE).forEach(function (a) { KE_BY[KE[a]] = a; });

  // 词条:icon = 战场上那枚 chip 的图标(无 = 不画 chip),chip = 那枚 chip 的底色类(与战场同色,不新造)。
  var GLOSS = {
    '种':   { icon: 'seed', chip: 'heal', d: '目标每次行动时，我方生命最低的单位回复生命' },
    '木灵': { d: '木系召唤的单位' },
    '本命': { d: '每只木灵自带的被动' },
    '幼苗': { d: '小木灵，属性为本体的 20%' },
    '荆棘': { icon: 'thorns', chip: 'dot', alias: '盾反', d: '被攻击时反弹一部分伤害' },
    '流血': { icon: 'bleed', chip: 'dot', d: '每回合受到固定伤害，持续 3 回合' },
    '荆刺': { icon: 'thornmark', chip: 'ctrl', d: '攻击我方时，自己受到这次伤害的一部分' },
    '保命': { icon: 'lastlife', chip: 'heal', d: '第一次将要阵亡时，保留 1 点生命，然后消失' },
    '灼':   { icon: 'burn', chip: 'dot', alias: '灼烧', d: '每回合受到 20×层数 伤害，然后少 1 层。最多 10 层' },
    '减攻': { icon: 'curse', chip: 'ctrl', d: '攻击力降低，数值见特性' },
    '减伤': { icon: 'bulwark', chip: 'heal', d: '受到的伤害按比例降低' },
    '成林': { icon: 'attack', chip: 'gold', d: '每有 1 只其他木灵，攻击提高' }
  };

  function pct(L, a, b) { return L >= 3 ? b : a; }

  var CHARS = {
    '荆': { el: 'Wood', rar: 'Purple', py: 'jīng', gloss: '多刺灌木', rec: ['茾', '刂'],
      copies: [3, 4], ink: [1800, 2400],
      faces: [
        { s: '攻', role: '攻击', landing: '拖到敌人 · 单体',
          stats: function (L) { return [{ k: '伤害', v: '144' }, { k: '种', v: '18', u: '· ' + pct(L, 2, 3) + ' 回合', t: '种' }]; } },
        { s: '生', role: '五行面', landing: '空位召唤 · 木灵嫁接',
          stats: function () { return [{ k: '木灵生命', v: '499', t: '木灵' }, { k: '木灵攻击', v: '0' }]; } }
      ],
      slots: [
        { lv: 1, fi: 0, name: '种', kind: '主动', icon: '种',
          text: function (L) { return '命中后在目标身上种下[种] {18}，持续 {' + pct(L, 2, 3) + '} 回合'; } },
        { lv: 1, fi: 1, nature: true, name: '荆棘', kind: '本命 · 被动', icon: '荆棘', extra: ['本命'],
          text: function (L) { return '[木灵]被攻击时举盾，反弹所受伤害的 {' + pct(L, 50, 80) + '%}，不走生克、无视护甲'; } },
        { lv: 3, fi: 0, name: '种·强化', kind: '被动', text: '[种]持续 {2} → {3} 回合' },
        { lv: 3, fi: 1, name: '荆棘·强化', kind: '本命强化', icon: '荆棘', text: '本命[荆棘]反弹 {50%} → {80%}' },
        { lv: 4, fi: 0, name: '披荆', kind: '被动', text: '这一面的伤害与[种] +{18%}' },
        { lv: 4, fi: 1, name: '披荆', kind: '本命 · 被动', icon: '流血', text: '[荆棘]反伤时，让攻击者[流血] {35}' },
        { lv: 5, fi: 0, name: '荆刺', kind: '主动', icon: '荆刺', text: '目标挂上[荆刺]：之后 {2} 次行动中攻击我方，自己受到 {30%} 反伤' },
        { lv: 5, fi: 1, name: '沃土', kind: '主动', limited: true, text: '全部[木灵]回复 {53} 生命' },
        { lv: 6, fi: 1, name: '扎根', kind: '被动', icon: '保命', text: '本次召出的[木灵]获得 {1} 次[保命]' },
        { lv: 8, fi: 0, name: '荆鞭', kind: '主动 · 招牌', icon: '流血', text: '改为连击 {3} 下（每下 {40%}），每下使目标[流血] {35}' },
        { lv: 8, fi: 1, name: '荆天棘地', kind: '主动 · 招牌', icon: '荆棘', text: '全部[木灵]获得[荆棘]（反伤 {30%}），{3} 回合' }
      ] },

    '林': { el: 'Wood', rar: 'Gold', py: 'lín', gloss: '成片的树木', rec: ['木', '木'],
      copies: [2, 4], ink: [2600, 3200],
      faces: [
        { s: '攻', role: '攻击', landing: '拖到敌人 · 单体',
          stats: function (L) { return [{ k: '伤害', v: '233' }, { k: '种', v: '21', u: '· ' + pct(L, 2, 3) + ' 回合', t: '种' }]; } },
        { s: '生', role: '五行面', landing: '空位召唤 · 木灵嫁接',
          stats: function () { return [{ k: '木灵生命', v: '346', t: '木灵' }, { k: '木灵攻击', v: '73' }]; } }
      ],
      slots: [
        { lv: 1, fi: 0, name: '种', kind: '主动', icon: '种',
          text: function (L) { return '命中后在目标身上种下[种] {21}，持续 {' + pct(L, 2, 3) + '} 回合'; } },
        { lv: 1, fi: 1, nature: true, name: '成林', kind: '本命 · 被动', icon: '成林', extra: ['本命'],
          text: function (L) { return '每有 1 只其他[木灵]，攻击 +{' + pct(L, 15, 25) + '%}；行动快 {50%}'; } },
        { lv: 3, fi: 0, name: '种·强化', kind: '被动', text: '[种]持续 {2} → {3} 回合' },
        { lv: 3, fi: 1, name: '成林·强化', kind: '本命强化', icon: '成林', text: '本命[成林]每只 +{15%} → +{25%}' },
        { lv: 4, fi: -1, name: '双木', kind: '被动', text: '出手时额外召出 {2} 只[幼苗]（按本字本体属性）' },
        { lv: 5, fi: 0, name: '众木', kind: '主动', text: '伤害 + 每只[木灵] {10%}' },
        { lv: 5, fi: 1, name: '新芽', kind: '主动', text: '额外召出 {1} 只[幼苗]' },
        { lv: 6, fi: 1, name: '林荫', kind: '被动', icon: '减伤', text: '在场时，每只[木灵]使我方[减伤] {5%}（至多 {20%}）' },
        { lv: 8, fi: 0, name: '林海', kind: '主动 · 招牌', text: '所有[木灵]各攻击目标一次（攻击 {70%}）' },
        { lv: 8, fi: 1, name: '林立', kind: '主动 · 招牌', text: '多召 {1} 只（每只 {60%}）；{3} 回合内全部[木灵]攻击 +{20%}' }
      ] },

    '炎': { el: 'Fire', rar: 'Gold', py: 'yán', gloss: '火光上升', rec: ['火', '火'],
      copies: [4, 4], ink: [3400, 3200],
      faces: [
        { s: '攻', role: '攻击', landing: '拖到敌人 · 单体',
          stats: function (L) { return [{ k: '伤害', v: '168' }, { k: '灼', v: L >= 3 ? '4' : '3', u: '层', t: '灼' }]; } },
        { s: '燃', role: '五行面', landing: '拖到敌人 · 单体',
          stats: function (L) { return [{ k: '灼', v: L >= 3 ? '4' : '3', u: '层', t: '灼' }, { k: '目标攻击', v: '−15%', t: '减攻' }, { k: '持续', v: L >= 3 ? '3' : '2', u: '回合' }]; } }
      ],
      slots: [
        { lv: 1, fi: 0, name: '灼', kind: '主动', icon: '灼',
          text: function (L) { return '命中后目标[灼] {' + (L >= 3 ? 4 : 3) + '} 层'; } },
        { lv: 1, fi: 1, name: '灼', kind: '主动', icon: '灼',
          text: function (L) { return '目标[灼] {' + (L >= 3 ? 4 : 3) + '} 层，并[减攻] {15%}，持续 {' + (L >= 3 ? 3 : 2) + '} 回合'; } },
        { lv: 3, fi: 0, name: '灼·强化', kind: '被动', text: '[灼] {3} → {4} 层' },
        { lv: 3, fi: 1, name: '灼·强化', kind: '被动', text: '[灼] {3} → {4} 层；[减攻]持续 {2} → {3} 回合' },
        { lv: 4, fi: -1, name: '双焰', kind: '被动', text: '出手额外[灼] {2} 层' },
        { lv: 5, fi: 0, name: '火上浇油', kind: '主动', text: '目标[灼]中时，伤害 +{100%}' },
        { lv: 5, fi: 1, name: '续火', kind: '主动', text: '目标[灼]中时，再[灼] {2} 层' },
        { lv: 6, fi: 1, name: '上炎', kind: '被动', icon: '灼', text: '本次点燃的目标，[灼]每回合 +{1} 层，持续 {3} 回合' },
        { lv: 8, fi: 0, name: '炎刃', kind: '主动 · 招牌', text: '改为 {2} 段，每段立即结算一次[灼]（层数保留）' },
        { lv: 8, fi: 1, name: '炎炎', kind: '主动 · 招牌', icon: '灼', text: '[灼]伤害 ×{2}；下一场开场敌方全体[灼] {2} 层' }
      ] }
  };

  var SLOTS = [1, 3, 4, 5, 6, 8];

  // 配色方案:A = 推荐;B = 词条只加粗不换色;now = 现状(对照)
  var SCHEMES = {
    A:   { name: 'A · 推荐', body: 'var(--text-quiet)', term: 'var(--elFg)', num: 'var(--text-main)' },
    B:   { name: 'B · 词条不换色', body: 'var(--text-quiet)', term: 'var(--text-main)', num: 'var(--text-main)' },
    now: { name: '现状', body: 'var(--text-dim)', term: 'var(--ink-soft)', num: 'var(--text-main)' }
  };

  function segs(s) {
    return String(s).split(/(\[[^\]]+\]|\{[^}]+\})/).filter(Boolean).map(function (x) {
      if (x[0] === '[') return { t: x.slice(1, -1), cls: 'term' };
      if (x[0] === '{') return { t: x.slice(1, -1), cls: 'n' };
      return { t: x, cls: 'p' };
    });
  }
  function terms(s, extra) {
    var out = (extra || []).slice();
    String(s).replace(/\[([^\]]+)\]/g, function (_, k) { if (out.indexOf(k) < 0) out.push(k); return _; });
    return out;
  }
  function textOf(slot, L) { return typeof slot.text === 'function' ? slot.text(L) : slot.text; }
  function chipOf(key) {
    var g = key && GLOSS[key];
    return g && g.icon ? { icon: g.icon, cls: 'ic k-' + g.chip } : null;
  }
  function svg(icon) { return '<svg viewBox="0 0 64 64"><use href="#ic-' + icon + '"/></svg>'; }

  return { EL: EL, RAR: RAR, KE: KE, KE_BY: KE_BY, GLOSS: GLOSS, CHARS: CHARS, SLOTS: SLOTS, SCHEMES: SCHEMES,
    segs: segs, terms: terms, textOf: textOf, chipOf: chipOf, svg: svg, ORDER: ['荆', '林', '炎'] };
})();
