// 字卡详情 · 技能说明区改版的示意数据与排版模型。
// 2026-10-15 出稿;同日按用户拍板修订(r2):配色定 A;取消「两面」条,每一级两面各写一行(效果相同也各写一遍);
// 攻击面也有 Lv1,数值格不再重复 Lv1 特性;五行面数值格按系定格;林的速度改为五行面 Lv5 被动。
// 数值取 chars.json 改写,仅作示意。r3(同日):林 Lv5「林风」;金格挡仍按次数计;火五行面「慑」,水五行面「涓 / 涌 / 苏」,总称「润泽」。正文记号:[词条] {数字}。
window.AT = (function () {
  var EL = { Fire: '火', Metal: '金', Water: '水', Earth: '土', Wood: '木' };
  var RAR = { White: '白', Green: '绿', Blue: '蓝', Purple: '紫', Gold: '金', Orange: '橙', Red: '红' };
  var KE = { Wood: 'Earth', Earth: 'Water', Water: 'Fire', Fire: 'Metal', Metal: 'Wood' };
  var KE_BY = {}; Object.keys(KE).forEach(function (a) { KE_BY[KE[a]] = a; });

  var FIRE_NAME = '慑';
  var WATER_MODES = {
    hot:    { n: '涓 · 多回合少量', name: '涓' },
    burst:  { n: '涌 · 单回合大量', name: '涌' },
    revive: { n: '苏 · 复活', name: '苏' }
  };

  // 词条:icon = 战场上那枚 chip 的图标(无 = 不画 chip),chip = 那枚 chip 的底色类(与战场同色,不新造)。
  var GLOSS = {
    '种':   { icon: 'seed', chip: 'heal', d: '目标每次行动时，我方生命最低的单位回复生命' },
    '木灵': { d: '木系召唤的单位' },
    '本命': { d: '每只木灵自带的被动' },
    '幼苗': { d: '小木灵，属性为本体的 20%' },
    '荆棘': { icon: 'thorns', chip: 'dot', alias: '盾反', d: '被攻击时反弹一部分伤害' },
    '成林': { icon: 'attack', chip: 'gold', d: '每有 1 只其他木灵，攻击提高' },
    '流血': { icon: 'bleed', chip: 'dot', d: '每回合受到固定伤害，持续 3 回合' },
    '荆刺': { icon: 'thornmark', chip: 'ctrl', d: '攻击我方时，自己受到这次伤害的一部分' },
    '保命': { icon: 'lastlife', chip: 'heal', d: '第一次将要阵亡时，保留 1 点生命，然后消失' },
    '减伤': { icon: 'bulwark', chip: 'heal', d: '受到的伤害按比例降低' },
    '速度': { icon: 'speed', chip: 'gold', d: '行动条走得更快' },
    '灼':   { icon: 'burn', chip: 'dot', alias: '灼烧', d: '每回合受到 20×层数 伤害，然后少 1 层。最多 10 层' },
    '慑': { icon: 'curse', chip: 'ctrl', alias: '原「减攻」', d: '攻击力降低，数值见特性' },
    '战意': { icon: 'morale', chip: 'gold', d: '每层攻击 +10%，最多 5 层，本场保留' },
    '格挡': { icon: 'block', chip: 'heal', d: '按次数计：下一次受到的伤害降低，并反击攻击者' },
    '反击': { icon: 'riposte', chip: 'gold', d: '被攻击后立即还手，伤害按反击数值' },
    '破甲': { icon: 'armorbreak', chip: 'ctrl', d: '降低目标的护甲' },
    '护甲': { icon: 'defense', chip: 'heal', d: '按比例减少受到的伤害' },
    '护盾': { icon: 'shield', chip: 'heal', d: '先于生命扣除。回合结束时清空；战斗结束保留一部分' },
    '冻结': { icon: 'freeze', chip: 'ctrl', d: '跳过行动，结束后获得霜抗' },
    '减速': { icon: 'slow', chip: 'ctrl', d: '速度 −50%' },
    '润泽': { icon: 'heal', chip: 'heal', d: '水系五行面的治疗，分涓、涌、苏三种' },
    '横扫': { d: '打一整排；目标 100%，其余 50%' }
  };
  GLOSS['涓'] = { icon: 'heal', chip: 'heal', d: '润泽之一：每回合回复少量生命，持续数回合' };
  GLOSS['涌'] = { icon: 'heal', chip: 'heal', d: '润泽之一：立即回复大量生命' };
  GLOSS['苏'] = { icon: 'lastlife', chip: 'heal', d: '润泽之一：复活一个阵亡的友方单位' };

  function up(L, a, b) { return L >= 3 ? b : a; }
  // 两面效果相同:两边各写一遍
  function both(o) { return [Object.assign({ fi: 0 }, o), Object.assign({ fi: 1 }, o)]; }
  function flat(list) { return [].concat.apply([], list.map(function (x) { return Array.isArray(x) ? x : [x]; })); }
  var ATK = function (v) { return function () { return [{ k: '攻击', v: v }]; }; };

  var CHARS = {
    '荆': { el: 'Wood', rar: 'Purple', py: 'jīng', gloss: '多刺灌木', rec: ['茾', '刂'],
      faces: [
        { s: '攻', role: '攻击', landing: '拖到敌人 · 单体', stats: ATK('144') },
        { s: '生', role: '五行面', landing: '空位召唤 · 木灵嫁接',
          stats: function () { return [{ k: '木灵生命', v: '499', t: '木灵' }, { k: '木灵攻击', v: '0' }]; } }
      ],
      slots: flat([
        { lv: 1, fi: 0, name: '种', kind: '主动', icon: '种', text: function (L) { return '命中后在目标身上种下[种] {18}，持续 {' + up(L, 2, 3) + '} 回合'; } },
        { lv: 1, fi: 1, nature: true, name: '荆棘', kind: '本命 · 被动', icon: '荆棘', extra: ['本命'],
          text: function (L) { return '[木灵]被攻击时举盾，反弹所受伤害的 {' + up(L, 50, 80) + '%}，不走生克、无视护甲'; } },
        { lv: 3, fi: 0, name: '种·强化', kind: '被动', text: '[种]持续 {2} → {3} 回合' },
        { lv: 3, fi: 1, name: '荆棘·强化', kind: '本命强化', icon: '荆棘', text: '本命[荆棘]反弹 {50%} → {80%}' },
        { lv: 4, fi: 0, name: '披荆', kind: '被动', text: '这一面的伤害与[种] +{18%}' },
        { lv: 4, fi: 1, name: '披荆', kind: '本命 · 被动', icon: '流血', text: '[荆棘]反伤时，让攻击者[流血] {35}' },
        { lv: 5, fi: 0, name: '荆刺', kind: '主动', icon: '荆刺', text: '目标挂上[荆刺]：之后 {2} 次行动中攻击我方，自己受到 {30%} 反伤' },
        { lv: 5, fi: 1, name: '沃土', kind: '主动', text: '全部[木灵]回复 {53} 生命' },
        { lv: 6, fi: 1, name: '扎根', kind: '被动', icon: '保命', text: '本次召出的[木灵]获得 {1} 次[保命]' },
        { lv: 8, fi: 0, name: '荆鞭', kind: '主动 · 招牌', icon: '流血', text: '改为连击 {3} 下（每下 {40%}），每下使目标[流血] {35}' },
        { lv: 8, fi: 1, name: '荆天棘地', kind: '主动 · 招牌', icon: '荆棘', text: '全部[木灵]获得[荆棘]（反伤 {30%}），{3} 回合' }
      ]) },

    '林': { el: 'Wood', rar: 'Gold', py: 'lín', gloss: '成片的树木', rec: ['木', '木'],
      faces: [
        { s: '攻', role: '攻击', landing: '拖到敌人 · 单体', stats: ATK('233') },
        { s: '生', role: '五行面', landing: '空位召唤 · 木灵嫁接',
          stats: function () { return [{ k: '木灵生命', v: '346', t: '木灵' }, { k: '木灵攻击', v: '73' }]; } }
      ],
      slots: flat([
        { lv: 1, fi: 0, name: '种', kind: '主动', icon: '种', text: function (L) { return '命中后在目标身上种下[种] {21}，持续 {' + up(L, 2, 3) + '} 回合'; } },
        { lv: 1, fi: 1, nature: true, name: '成林', kind: '本命 · 被动', icon: '成林', extra: ['本命'],
          text: function (L) { return '每有 1 只其他[木灵]，攻击 +{' + up(L, 15, 25) + '%}'; } },
        { lv: 3, fi: 0, name: '种·强化', kind: '被动', text: '[种]持续 {2} → {3} 回合' },
        { lv: 3, fi: 1, name: '成林·强化', kind: '本命强化', icon: '成林', text: '本命[成林]每只 +{15%} → +{25%}' },
        both({ lv: 4, name: '双木', kind: '被动', text: '出手时额外召出 {2} 只[幼苗]（按本字本体属性）' }),
        { lv: 5, fi: 0, name: '众木', kind: '主动', text: '伤害 + 每只[木灵] {10%}' },
        { lv: 5, fi: 1, name: '林风', kind: '被动', icon: '速度', text: '本字召出的[木灵][速度] +{50%}' },
        { lv: 6, fi: 1, name: '林荫', kind: '被动', icon: '减伤', text: '在场时，每只[木灵]使我方[减伤] {5%}（至多 {20%}）' },
        { lv: 8, fi: 0, name: '林海', kind: '主动 · 招牌', text: '所有[木灵]各攻击目标一次（攻击 {70%}）' },
        { lv: 8, fi: 1, name: '林立', kind: '主动 · 招牌', text: '多召 {1} 只（每只 {60%}）；{3} 回合内全部[木灵]攻击 +{20%}' }
      ]) },

    '炎': { el: 'Fire', rar: 'Gold', py: 'yán', gloss: '火光上升', rec: ['火', '火'],
      faces: [
        { s: '攻', role: '攻击', landing: '拖到敌人 · 单体', stats: ATK('168') },
        { s: '燃', role: '五行面', landing: '拖到敌人 · 单体',
          stats: function (L) { return [{ k: FIRE_NAME, v: up(L, '−15%', '−20%'), t: FIRE_NAME }, { k: '回合', v: up(L, '2', '3') }, { k: '灼', v: '1', u: '层', t: '灼' }]; } }
      ],
      slots: flat([
        { lv: 1, fi: 0, name: '灼', kind: '主动', icon: '灼', text: function (L) { return '命中后目标[灼] {' + up(L, 3, 4) + '} 层'; } },
        { lv: 1, fi: 1, name: FIRE_NAME, kind: '主动', icon: FIRE_NAME,
          text: function (L) { return '[' + FIRE_NAME + ']：目标攻击 −{' + up(L, 15, 20) + '%}，持续 {' + up(L, 2, 3) + '} 回合；附带[灼] {1} 层'; } },
        { lv: 3, fi: 0, name: '灼·强化', kind: '被动', text: '[灼] {3} → {4} 层' },
        { lv: 3, fi: 1, name: FIRE_NAME + '·强化', kind: '被动', text: '攻击 −{15%} → −{20%}；持续 {2} → {3} 回合' },
        both({ lv: 4, name: '双焰', kind: '被动', icon: '灼', text: '出手额外[灼] {2} 层' }),
        { lv: 5, fi: 0, name: '火上浇油', kind: '主动', text: '目标[灼]中时，伤害 +{100%}' },
        { lv: 5, fi: 1, name: '续火', kind: '主动', text: '目标[灼]中时，再[灼] {2} 层' },
        { lv: 6, fi: 1, name: '上炎', kind: '被动', icon: '灼', text: '本次点燃的目标，[灼]每回合 +{1} 层，持续 {3} 回合' },
        { lv: 8, fi: 0, name: '炎刃', kind: '主动 · 招牌', text: '改为 {2} 段，每段立即结算一次[灼]（层数保留）' },
        { lv: 8, fi: 1, name: '炎炎', kind: '主动 · 招牌', icon: '灼', text: '[灼]伤害 ×{2}；下一场开场敌方全体[灼] {2} 层' }
      ]) },

    '剑': { el: 'Metal', rar: 'Blue', py: 'jiàn', gloss: '两刃长兵', rec: ['佥', '刂'],
      faces: [
        { s: '攻', role: '攻击', landing: '拖到敌人 · 单体', stats: ATK('94') },
        { s: '铠', role: '五行面', landing: '拖到自己',
          stats: function (L) { return [{ k: '反击', v: up(L, '120%', '140%'), t: '反击' }, { k: '格挡', v: up(L, '1', '2'), u: '次', t: '格挡' }, { k: '战意', v: '1', u: '层', t: '战意' }]; } }
      ],
      slots: flat([
        { lv: 1, fi: 0, name: '战意', kind: '主动', icon: '战意', text: function (L) { return '出手后获得[战意] {' + up(L, 1, 2) + '} 层'; } },
        { lv: 1, fi: 1, name: '格挡', kind: '主动', icon: '格挡',
          text: function (L) { return '[格挡] {' + up(L, 1, 2) + '} 次：之后受到的伤害 −{40%}，并以 {' + up(L, 120, 140) + '%} [反击]攻击者；获得[战意] {1} 层'; } },
        { lv: 3, fi: 0, name: '战意·强化', kind: '被动', text: '出手得[战意] {1} → {2} 层' },
        { lv: 3, fi: 1, name: '格挡·强化', kind: '被动', text: '[格挡] {1} → {2} 次；[反击] {120%} → {140%}' },
        both({ lv: 4, name: '克敌', kind: '被动', text: '对被克的敌人效果 +{20%}' }),
        { lv: 5, fi: 0, name: '破甲', kind: '主动', icon: '破甲', text: '目标[破甲] {20%}，持续 {2} 回合' },
        { lv: 5, fi: 1, name: '剑意', kind: '主动', icon: '反击', text: '[格挡]触发的[反击]额外 +{30%}' },
        { lv: 6, fi: 0, name: '迎刃', kind: '被动', icon: '战意', text: '[战意] ≥ {3} 层时，伤害 +{15%}' },
        { lv: 8, fi: 0, name: '横扫千军', kind: '主动 · 招牌', text: '改为[横扫]；每层[战意]伤害 +{10%}' },
        { lv: 8, fi: 1, name: '反戈', kind: '主动 · 招牌', icon: '反击', text: '[格挡]剩余时，我方任一单位被攻击都由本字[反击]' }
      ]) },

    '冰': { el: 'Water', rar: 'Gold', py: 'bīng', gloss: '水凝而坚', rec: ['冫', '水'],
      faces: [
        { s: '攻', role: '攻击', landing: '拖到敌人 · 单体', stats: ATK('152') },
        { s: '润', role: '五行面', landing: '拖到自己或木灵',
          stats: function (L, mode) {
            if (mode === 'hot') return [{ k: '治疗', v: up(L, '30', '36'), u: '/ 回合', t: '涓' }, { k: '回合', v: '3' }];
            if (mode === 'revive') return [{ k: '复活生命', v: up(L, '40%', '50%'), t: '苏' }];
            return [{ k: '治疗', v: up(L, '152', '182'), t: '涌' }];
          } }
      ],
      slots: flat([
        { lv: 1, fi: 0, name: '冻结', kind: '主动', icon: '冻结', text: '目标[冻结] {1} 回合' },
        { lv: 1, fi: 1, name: '@water', kind: '主动 · 润泽', icon: '@water', extra: ['润泽'],
          text: function (L, mode) {
            if (mode === 'hot') return '[涓]：我方生命最低的单位每回合回复 {' + up(L, 30, 36) + '}，持续 {3} 回合';
            if (mode === 'revive') return '[苏]：复活一个阵亡的友方单位，恢复 {' + up(L, 40, 50) + '%} 生命；无人阵亡时不可出';
            return '[涌]：立即回复我方生命最低的单位 {' + up(L, 152, 182) + '} 生命';
          } },
        { lv: 3, fi: 0, name: '冻结·强化', kind: '被动', text: '[冻结]中的目标受伤 +{15%}' },
        { lv: 3, fi: 1, name: '@water·强化', kind: '被动', text: '回复量 +{20%}' },
        both({ lv: 4, name: '冰封', kind: '被动', text: '对[冻结]中的目标效果 +{30%}' }),
        { lv: 5, fi: 0, name: '凝冰', kind: '主动', icon: '减速', text: '目标[减速]，持续 {2} 回合' },
        { lv: 5, fi: 1, name: '冰甲', kind: '主动', icon: '护盾', text: '回复时附带[护盾] {40}' },
        { lv: 6, fi: 0, name: '寒彻', kind: '被动', icon: '减速', text: '[冻结]结束时再[减速] {1} 回合' },
        { lv: 8, fi: 0, name: '坚冰', kind: '主动 · 招牌', icon: '冻结', text: '敌方全体[冻结] {1} 回合' },
        { lv: 8, fi: 1, name: '冰晶', kind: '主动 · 招牌', text: '回复量 ×{1.5}，溢出部分转为[护盾]' }
      ]) },

    '塔': { el: 'Earth', rar: 'Purple', py: 'tǎ', gloss: '层叠高耸的建筑', rec: ['土', '荅'],
      faces: [
        { s: '攻', role: '攻击', landing: '拖到敌人 · 单体', stats: ATK('106') },
        { s: '固', role: '五行面', landing: '拖到自己',
          stats: function (L) { return [{ k: '护盾', v: '106', t: '护盾' }, { k: '护甲', v: up(L, '+25', '+30'), t: '护甲' }, { k: '回合', v: '2' }]; } }
      ],
      slots: flat([
        { lv: 1, fi: 0, name: '破甲', kind: '主动', icon: '破甲', text: function (L) { return '目标[破甲] {' + up(L, 25, 35) + '%}，持续 {2} 回合'; } },
        { lv: 1, fi: 1, name: '护甲', kind: '主动', icon: '护甲',
          text: function (L) { return '获得[护盾] {106}；我方[护甲] +{' + up(L, 25, 30) + '}，持续 {2} 回合'; } },
        { lv: 3, fi: 0, name: '破甲·强化', kind: '被动', text: '[破甲] {25%} → {35%}' },
        { lv: 3, fi: 1, name: '护甲·强化', kind: '被动', text: '[护甲] +{25} → +{30}' },
        both({ lv: 4, name: '塔基', kind: '被动', icon: '护盾', text: '获得[护盾]时额外 +{15%}' }),
        { lv: 5, fi: 0, name: '塔倾', kind: '主动', text: '目标[破甲]中时，伤害 +{40%}' },
        { lv: 5, fi: 1, name: '加固', kind: '主动', icon: '护甲', text: '[护甲]持续 +{1} 回合' },
        { lv: 6, fi: 1, name: '反震', kind: '被动', text: '被攻击时，按[护甲]反伤 {20%}' },
        { lv: 8, fi: 0, name: '镇塔', kind: '主动 · 招牌', text: '伤害 + 我方[护盾]的 {50%}' },
        { lv: 8, fi: 1, name: '七级浮屠', kind: '主动 · 招牌', icon: '护盾', text: '{7} 回合内每回合获得[护盾] {40}' }
      ]) }
  };

  var SLOTS = [1, 3, 4, 5, 6, 8];

  // 配色:A 已拍板(2026-10-15);B / 现状只留作对照
  var SCHEMES = {
    A:   { name: 'A · 已拍板', body: 'var(--text-quiet)', term: 'var(--elFg)', num: 'var(--text-main)' },
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
  // 水系五行面的名字随治疗模式换(@water 占位)
  function sub(str, mode) { return String(str).replace(/@water/g, (WATER_MODES[mode] || WATER_MODES.burst).name); }
  function textOf(slot, L, mode) { return sub(typeof slot.text === 'function' ? slot.text(L, mode) : slot.text, mode); }
  function chipOf(key, mode) {
    var g = key && GLOSS[sub(key, mode)];
    return g && g.icon ? { icon: g.icon, cls: 'ic k-' + g.chip } : null;
  }

  return { EL: EL, RAR: RAR, KE: KE, KE_BY: KE_BY, GLOSS: GLOSS, CHARS: CHARS, SLOTS: SLOTS, SCHEMES: SCHEMES,
    WATER_MODES: WATER_MODES, segs: segs, terms: terms, textOf: textOf, sub: sub, chipOf: chipOf,
    ORDER: ['荆', '林', '炎', '剑', '冰', '塔'] };
})();
