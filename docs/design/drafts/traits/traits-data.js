/* 字卡五特性体系 · 设计稿共用数据(五张示例字 + 词条表)。
   来源:docs/superpowers/specs/2026-10-03-字卡五特性体系-design.md §1 §3 §4 §9;
   本体基础值取自 Brushblade/Assets/StreamingAssets/config/chars.json(仿真定标前的现值)。
   口径(稿内假设,待实现侧确认):
   · 数值每级 +6%(§1);副面按 60%(§2.1)—— 只乘伤害/护盾/治疗/召唤属性这类量,
     层数、回合、次数、百分比不打折(§1「层数、回合、次数、击数不随等级涨」的延伸)。
   文案标记:[词] = 术语(可查词条);{数} = 数字(加粗)。 */
window.ZD = (function () {
  var EL = { Fire: '火', Metal: '金', Water: '水', Earth: '土', Wood: '木' };
  var RAR = { White: '白', Green: '绿', Blue: '蓝', Purple: '紫', Gold: '金', Orange: '橙', Red: '红' };
  var KE = { Wood: 'Earth', Earth: 'Water', Water: 'Fire', Fire: 'Metal', Metal: 'Wood' };   // A 克 KE[A]
  var KE_BY = {}; Object.keys(KE).forEach(function (a) { KE_BY[KE[a]] = a; });                // KE_BY[B] 克 B

  // spec §3:关键词 / 范围 / 存续。气泡与字符串表的唯一文案来源。
  var GLOSS = {
    '灼': '每回合受到 20×层数 伤害，然后少 1 层。最多 10 层',
    '引爆': '把剩下的灼一次烧完，然后清空',
    '冻结': '跳过行动，结束后获得霜抗',
    '霜抗': '这几回合内不能被冻结',
    '冰滞': 'Boss 被冻结时改为冰滞：行动条后退一半，下次行动前受伤 +15%',
    '减速': '速度 −50%',
    '战意': '每层攻击 +10%，最多 5 层，本场保留',
    '格挡': '下一次受到的伤害 −40%，并反击攻击者',
    '泉': '治疗时自动积累，最多 10 层。部分水字的攻击面会全部释放',
    '厚': '获得护盾时自动积累，最多 10 层。部分土字的攻击面会全部释放',
    '护盾': '先于生命扣除。回合结束时清空；战斗结束保留一部分',
    '留存护盾': '回合结束时不清空的护盾',
    '护甲': '按比例减少受到的伤害',
    '破甲': '降低目标的护甲',
    '润泽': '每回合回复一次生命',
    '种': '目标每次行动时，我方生命最低的单位回复生命',
    '木灵': '木系召唤的单位，会攻击，跨场保留到阵亡',
    '本命': '每只木灵自带的被动',
    '幼苗': '小木灵，属性为本体的 20%',
    '嫁接': '木灵回满生命，并获得新的本命',
    '嘲讽': '敌人只能攻击带嘲讽的单位',
    '斩杀': '直接击杀；对 Boss 改为伤害 ×2',
    '流血': '每回合受到固定伤害，持续 3 回合',
    '致命': '期间生命低于 30% 时立即被斩杀',
    '魅惑': '下一次行动改为攻击它的队友',
    '荆棘': '被攻击时反弹一部分伤害',
    '暴击': '伤害 ×1.5',
    '单体': '只打目标',
    '溅射': '打目标及同排左右相邻；目标 100%，其余 50%',
    '横扫': '打一整排；目标 100%，其余 50%',
    '贯穿': '打一整列（前排加后排）；目标 100%，其余 70%',
    '散射': '随机打 N 下，优先后排，可以重复打同一个',
    '弹射': '从目标开始，依次跳到最近的其他敌人，每跳伤害减半',
    '全体': '打全部敌人',
    '本场': '这场战斗结束就消失',
    '跨场': '持续之后的 N 场',
    '跨段': '持续之后的 5 场',
    '标记': '被标记的敌人受到的伤害增加',
    '减攻': '攻击降低，同类只取最强的一份'
  };
  var GLOSS_KIND = {};
  ['单体', '溅射', '横扫', '贯穿', '散射', '弹射', '全体'].forEach(function (k) { GLOSS_KIND[k] = '范围'; });
  ['本场', '跨场', '跨段'].forEach(function (k) { GLOSS_KIND[k] = '存续'; });

  var TGT = { enemy: '拖到敌人', ally: '拖到自己或木灵', summon: '拖到空位召唤 · 拖到木灵上嫁接' };

  function sc(L) { return 1 + 0.06 * (L - 1); }
  function r(x) { return Math.round(x); }

  // faces[0] = 攻击面,faces[1] = 特色面;main = 主面下标。stats(L, p) 里 p = 1(主面)或 0.6(副面)。
  // traits.face:'both' | 'main' | 'sub'
  var CHARS = {
    '炎': {
      el: 'Fire', rar: 'Gold', py: 'yán', gloss: '火苗上炎', rec: ['火', '火'], main: 1, copies: [4, 4], ink: [2480, 2000],
      faces: [
        { s: '焚', role: '攻击面', tgt: 'enemy', area: '单体', lv1: '伤害 + [灼] {3}',
          stats: function (L, p) { return [{ k: '伤害', v: r(168 * sc(L) * p) }, { k: '灼', v: L >= 3 ? 4 : 3, u: '层', t: '灼' }]; } },
        { s: '燃', role: '特色面', tgt: 'enemy', area: '单体', lv1: '[灼] {3}，目标攻击 −{15%}，{2} 回合',
          stats: function (L) { return [{ k: '灼', v: L >= 3 ? 4 : 3, u: '层', t: '灼' }, { k: '目标攻击', v: '−15%', t: '减攻' }, { k: '持续', v: L >= 3 ? 3 : 2, u: '回合' }]; } }
      ],
      traits: [
        { lv: 3, face: 'both', name: '强化', text: '[灼] +{1}；燃改为持续 {3} 回合' },
        { lv: 4, face: 'both', name: '双焰', tag: '拆字', kind: '被动', text: '拆出的 {2} 个「火」本回合出手时各附[灼] {2}' },
        { lv: 5, face: 'sub', name: '火上浇油', kind: '主动', text: '目标带[灼]时，伤害 ×{2}' },
        { lv: 6, face: 'main', name: '上炎', kind: '被动', text: '带本字[灼]的敌人每回合开始 +{1} 层，持续 {3} 回合' },
        { lv: 8, face: 'main', name: '炎炎', kind: '主动', text: '[灼]层数 ×{2}；[跨场]：下一场开局全体 +[灼] {2}' }
      ]
    },
    '剑': {
      el: 'Metal', rar: 'Blue', py: 'jiàn', gloss: '两刃长兵', rec: ['佥', '刂'], main: 0, copies: [6, 6], ink: [3400, 2600],
      faces: [
        { s: '斩', role: '攻击面', tgt: 'enemy', area: '单体', lv1: '伤害 + [战意] {1}',
          stats: function (L, p) { return [{ k: '伤害', v: r(94 * sc(L) * p) }, { k: '战意', v: L >= 3 ? '+2' : '+1', u: '层', t: '战意' }]; } },
        { s: '铠', role: '特色面', tgt: 'ally', area: '单体', lv1: '[战意] {1}，[格挡] {1} 次（反击本体 {30%}）',
          stats: function (L, p) { return [{ k: '战意', v: L >= 3 ? '+2' : '+1', u: '层', t: '战意' }, { k: '格挡', v: L >= 3 ? 2 : 1, u: '次', t: '格挡' }, { k: '反击', v: r(94 * sc(L) * p * (L >= 5 ? 0.5 : 0.3)) }]; } }
      ],
      traits: [
        { lv: 3, face: 'both', name: '强化', text: '[战意] +{1}；[格挡]改为 {2} 次' },
        { lv: 4, face: 'both', name: '克敌', tag: '通用', kind: '被动', text: '本字克制目标时，效果 +{15%}' },
        { lv: 5, face: 'sub', name: '剑意', kind: '主动', text: '[战意] +{1}；[格挡]反击改为本体 {50%}' },
        { lv: 6, face: 'main', name: '迎刃', kind: '被动', text: '击杀时[战意] +{1}' },
        { lv: 8, face: 'main', name: '横扫千军', kind: '主动', text: '改为[横扫]；每多命中 1 名敌人，[战意] +{1}' }
      ]
    },
    '林': {
      el: 'Wood', rar: 'Gold', py: 'lín', gloss: '成片的树', rec: ['木', '木'], main: 1, copies: [1, 4], ink: [800, 2000],
      faces: [
        { s: '刺', role: '攻击面', tgt: 'enemy', area: '单体', lv1: '伤害 + [种] {2} 回合',
          stats: function (L, p) { return [{ k: '伤害', v: r(233 * sc(L) * p) }, { k: '种', v: L >= 3 ? 3 : 2, u: '回合', t: '种' }]; } },
        { s: '生', role: '特色面', tgt: 'summon', area: '召唤', lv1: '召唤林灵（带[本命]「成林」）',
          stats: function (L, p) { return [{ k: '林灵生命', v: r(346 * sc(L) * p), t: '木灵' }, { k: '攻击', v: r(73 * sc(L) * p) }, { k: '本命', v: '成林', t: '本命' }]; } }
      ],
      traits: [
        { lv: 3, face: 'both', name: '强化', text: '[种]改为 {3} 回合；[本命]强化' },
        { lv: 4, face: 'both', name: '双木', tag: '拆字', kind: '被动', text: '拆林时召唤 {2} 只[幼苗]' },
        { lv: 5, face: 'sub', name: '成林', kind: '主动', text: '伤害 + 每只[木灵] {10%}' },
        { lv: 6, face: 'main', name: '林荫', kind: '被动', text: '林灵在场时，每只[木灵]让我方受伤 −{5%}（最多 −{20%}）' },
        { lv: 8, face: 'main', name: '林立', kind: '主动', text: '召唤 {2} 只林灵（各 {60%}）；{3} 回合内全部[木灵]攻击 +{20%}' }
      ]
    },
    '冷': {
      el: 'Water', rar: 'White', py: 'lěng', gloss: '寒而不温', rec: ['冫', '令'], main: 0, copies: [9, 12], ink: [600, 900],
      faces: [
        { s: '涛', role: '攻击面', tgt: 'enemy', area: '单体', lv1: '伤害 + [减速] {1} 回合',
          stats: function (L, p) { return [{ k: '伤害', v: r(45 * sc(L) * p) }, { k: '减速', v: L >= 3 ? 2 : 1, u: '回合', t: '减速' }]; } },
        { s: '润', role: '特色面', tgt: 'ally', area: '单体', lv1: '治疗，并附[润泽] {2} 回合',
          stats: function (L, p) { return [{ k: '治疗', v: r(45 * sc(L) * p) }, { k: '润泽', v: L >= 3 ? 3 : 2, u: '回合', t: '润泽' }]; } }
      ],
      traits: [
        { lv: 3, face: 'both', name: '强化', text: '两面都 +{1} 回合' },
        { lv: 4, face: 'both', name: '克敌', tag: '通用', kind: '被动', text: '本字克制目标时，效果 +{15%}' },
        { lv: 5, face: 'sub', name: '涓流', kind: '主动', text: '额外清除 {1} 个减益' },
        { lv: 6, face: 'sub', name: '护持', kind: '被动', text: '治疗时，额外给治疗量 {20%} 的[护盾]' },
        { lv: 8, face: 'main', name: '冷却', kind: '主动', text: 'Boss 蓄力推迟 {1} 拍（每场 {1} 次）；普通敌人下次攻击 −{50%}' }
      ]
    },
    '㙓': {
      el: 'Earth', rar: 'Red', py: 'lěi', gloss: '土垒极厚', rec: ['土', '垚'], main: 1, copies: [2, 2], ink: [5200, 8000],
      faces: [
        { s: '震', role: '攻击面', tgt: 'enemy', area: '全体', lv1: '伤害 + [破甲] {2} 回合；释放[厚]',
          stats: function (L, p) { return [{ k: '伤害', v: r(235 * sc(L) * p) }, { k: '破甲', v: L >= 3 ? 4 : 2, u: '回合', t: '破甲' }]; } },
        { s: '固', role: '特色面', tgt: 'ally', area: '单体', lv1: '[护盾] + [护甲] {2} 回合',
          stats: function (L, p) { return [{ k: '护盾', v: r(235 * sc(L) * p), t: '护盾' }, { k: '护甲', v: '+' + r(100 * sc(L) * p), t: '护甲' }, { k: '持续', v: (L >= 3 ? 4 : 2) * (L >= 4 ? 2 : 1), u: '回合' }]; } }
      ],
      traits: [
        { lv: 3, face: 'both', name: '强化', text: '[破甲]与[护甲]都改为 {4} 回合' },
        { lv: 4, face: 'both', name: '深厚', kind: '被动', text: '本字[护甲]的持续回合 ×{2}' },
        { lv: 5, face: 'sub', name: '山压', kind: '主动', text: '伤害 + 我方当前[护盾]的 {50%}（不消耗护盾）' },
        { lv: 6, face: 'main', name: '载物', kind: '被动', text: '[护盾]同时给全部[木灵]（每只 {50%}）' },
        { lv: 8, face: 'main', name: '厚德载物', kind: '主动', text: '本次[护盾]翻倍，并成为[留存护盾]' }
      ]
    }
  };

  // "[灼] +{1}" → [{t,cls}];cls: '' | 'term' | 'n'
  function segs(s) {
    return String(s).split(/(\[[^\]]+\]|\{[^}]+\})/).filter(Boolean).map(function (x) {
      if (x[0] === '[') return { t: x.slice(1, -1), cls: 'term' };
      if (x[0] === '{') return { t: x.slice(1, -1), cls: 'n' };
      return { t: x, cls: '' };
    });
  }
  function terms(s) {
    var out = [];
    String(s).replace(/\[([^\]]+)\]/g, function (_, k) { if (out.indexOf(k) < 0) out.push(k); return _; });
    return out;
  }
  function plain(s) { return String(s).replace(/[\[\]{}]/g, ''); }
  // 某张字某一级的全部特性,附解锁态与所属面下标
  function traitsOf(id, L) {
    var c = CHARS[id];
    return c.traits.map(function (t) {
      var fi = t.face === 'main' ? c.main : t.face === 'sub' ? 1 - c.main : -1;
      return Object.assign({}, t, { fi: fi, on: L >= t.lv });
    });
  }
  function faceOf(id, fi, L) {
    var c = CHARS[id], f = c.faces[fi], isMain = fi === c.main;
    return Object.assign({}, f, { fi: fi, isMain: isMain, pct: isMain ? 100 : 60, stats: f.stats(L, isMain ? 1 : 0.6) });
  }
  return { EL: EL, RAR: RAR, KE: KE, KE_BY: KE_BY, GLOSS: GLOSS, GLOSS_KIND: GLOSS_KIND, TGT: TGT, CHARS: CHARS,
    segs: segs, terms: terms, plain: plain, traitsOf: traitsOf, faceOf: faceOf, sc: sc, ORDER: ['炎', '剑', '林', '冷', '㙓'] };
})();

/* 图标精灵:现有 44 枚里用得到的几枚(路径逐字取自 tools/icons/svg/,fill/stroke 换成 currentColor 以便着色)
   + 本稿新增 8 枚(标 NEW,要按 CLAUDE.md「图标三处对账」进管线)。用法:<svg><use href="#ic-burn"></use></svg> */
(function () {
  var S = 'fill="none" stroke="currentColor" stroke-width="6" stroke-linecap="round" stroke-linejoin="round"';
  var S5 = 'fill="none" stroke="currentColor" stroke-width="5" stroke-linecap="round" stroke-linejoin="round"';
  var F = 'fill="currentColor"';
  var SHIELD = 'M32 7l22 8v18c0 13-11 24-22 28-11-4-22-15-22-28V15z';
  var ICONS = {
    // —— 现有 ——
    burn: '<path ' + F + ' d="M33 5c11 13 17 21 17 30a18 18 0 0 1-36 0c0-6 3-11 7-14-1 5 1 9 4 11 3-9-2-16-5-21 5 1 9-1 13-6z"/>',
    morale: '<path ' + S + ' d="M18 8v48"/><path ' + F + ' d="M18 12h30l-8 10 8 10H18z"/>',
    freeze: '<path ' + S + ' d="M32 8v48M11 20l42 24M53 20L11 44"/>',
    slow: '<path ' + S + ' d="M32 10v32M18 30l14 14 14-14"/>',
    shield: '<path ' + S + ' d="' + SHIELD + '"/>',
    curse: '<path ' + F + ' d="M32 52L12 18h40z"/>',
    heal: '<path ' + F + ' d="M26 12h12v14h14v12H38v14H26V38H12V26h14z"/>',
    wellspring: '<path ' + S + ' d="M12 38a20 20 0 0 0 40 0"/><path ' + S + ' d="M32 30V10M22 24l4-8M42 24l-4-8"/>',
    heft: '<path ' + S + ' d="M10 50h44M15 36h34M21 22h22"/>',
    attack: '<path ' + F + ' d="M52 8l-4 14-26 26-8-8L40 14z"/><path ' + S + ' d="M18 46l-8 8"/>',
    defense: '<path ' + F + ' d="M32 6l22 8v19c0 13-11 23-22 27-11-4-22-14-22-27V14z"/>',
    armorbreak: '<path ' + S + ' d="M32 7l21 8v18c0 12-10 21-21 25-11-4-21-13-21-25V15z"/><path ' + S + ' d="M37 17l-10 15h11l-10 15"/>',
    thorns: '<circle cx="32" cy="32" r="11" ' + S + '/><path ' + S + ' d="M32 6v7M32 51v7M6 32h7M51 32h7M14 14l5 5M45 45l5 5M50 14l-5 5M19 45l-5 5"/>',
    // —— NEW ——
    block: '<path ' + S + ' d="' + SHIELD + '"/><path ' + S + ' d="M38 23l-10 10 10 10"/>',
    seed: '<path ' + S + ' d="M32 56V30"/><path ' + F + ' d="M32 33C20 33 11 25 11 12c13 0 21 8 21 21z"/><path ' + F + ' d="M32 29c0-11 7-18 20-18 0 11-7 18-20 18z"/>',
    frostguard: '<circle cx="32" cy="32" r="25" ' + S5 + '/><path ' + S5 + ' d="M32 15v34M17.3 23.5l29.4 17M46.7 23.5l-29.4 17"/>',
    chill: '<path ' + S + ' d="M21 10v44M8 20l26 24M34 20L8 44"/><path ' + S + ' d="M60 32H43M50 24l-8 8 8 8"/>',
    mortal: '<path ' + S + ' d="M18 15h28M32 5v10"/><path ' + F + ' d="M26 15h12v25l-6 10-6-10z"/><path ' + S5 + ' d="M8 58h48"/>',
    mark: '<path ' + S + ' d="M32 6l26 26-26 26L6 32z"/><circle cx="32" cy="32" r="7" ' + F + '/>',
    taunt: '<path ' + S + ' d="M8 25h12l24-13v40L20 39H8z"/><path ' + S + ' d="M52 24c4 5 4 11 0 16"/>',
    keepshield: '<path ' + S + ' d="' + SHIELD + '"/><path ' + F + ' d="M25 19h14v23l-7-6-7 6z"/>',
    chevron: '<path ' + S + ' d="M24 14L42 32L24 50"/>'
  };
  window.ZD.ICONS = ICONS;
  function inject() {
    if (document.getElementById('zd-icons')) return;
    var defs = Object.keys(ICONS).map(function (k) { return '<symbol id="ic-' + k + '" viewBox="0 0 64 64">' + ICONS[k] + '</symbol>'; }).join('');
    var d = document.createElement('div');
    d.innerHTML = '<svg id="zd-icons" width="0" height="0" style="position:absolute" aria-hidden="true"><defs>' + defs + '</defs></svg>';
    document.body.appendChild(d.firstChild);
  }
  if (document.body) inject(); else document.addEventListener('DOMContentLoaded', inject);
})();
