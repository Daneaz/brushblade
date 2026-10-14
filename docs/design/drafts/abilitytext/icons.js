window.ZD = window.ZD || {};
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
    // —— NEW · 10-04 补(待审):池词条坚壁 / 反戈 / 扎根挂在我方的三种状态 ——
    // 减伤:城垛实心墙 + 镂空城门(evenodd);与五种盾形刻意不像
    bulwark: '<path ' + F + ' fill-rule="evenodd" d="M8 56V18h10v10h9V18h10v10h9V18h10v38zM26 56V47a6 6 0 0 1 12 0v9z"/>',
    // 反击加倍:格挡图标里那道回击尖括号,叠成两道
    riposte: '<path ' + S + ' d="M30 16L14 32l16 16M50 16L34 32l16 16"/>',
    // 保命:描边心 + 心尖一小截实心(只剩一点,但还在);根形缩小后像「木」字,弃用
    lastlife: '<path ' + S + ' d="M32 54C14 42 8 32 8 23a12 12 0 0 1 24-3 12 12 0 0 1 24 3c0 9-6 19-24 31z"/><path ' + F + ' d="M32 54c-8-5.5-12.5-10-15-14h30c-2.5 4-7 8.5-15 14z"/>',
    // —— NEW · 10-08 补(待审):D2-火 新增的三种状态 ——
    // 埋雷:实心炸弹 + 引信(与灼的火苗、暴击的八角星、标记的菱形都不像)
    mine: '<circle cx="27" cy="38" r="20" ' + F + '/><path ' + F + ' d="M44 13l8 8-7 7-8-8z"/><path ' + S5 + ' d="M50 15c2-5 5-8 9-9"/>',
    // 干涸(禁疗):润泽那枚实心加号,沿斜杠切开一道缝再压一道斜杠(同致盲「眼 + 斜杠」的说法)
    healblock: '<path ' + F + ' d="M26 12h12v5.5L17.5 38H12V26h14z"/><path ' + F + ' d="M46.5 26H52v12H38v14H26v-5.5z"/><path ' + S + ' d="M12 52L52 12"/>',
    // 回敬:折返箭头 + 一点火花(打回去的是「效果」不是伤害;不绑火,金系挂流血 / 破甲也用它)
    retaliate: '<path ' + S + ' d="M10 20h26a13 13 0 0 1 0 26H18"/><path ' + S + ' d="M26 38l-8 8 8 8"/><path ' + F + ' d="M51 3l2.8 7.2L61 13l-7.2 2.8L51 23l-2.8-7.2L41 13l7.2-2.8z"/>',
    // —— NEW · 10-10 补(待审):D2-水 / D2-土 的状态(StatusChipsWaterEarth 画板) ——
    // 禁增益:减攻的 ▼ 倒过来成 ▲(增益),沿斜杠切开再压一杠 —— 与干涸同一种「不能」说法
    buffblock: '<path ' + F + ' d="M32 10L38.5 21.7 10.2 50H10z"/><path ' + F + ' d="M44.5 32.7L54 50H27.2z"/><path ' + S + ' d="M14 54L54 14"/>',
    // 免疫减益:霜抗那道环(「这几回合不会被怎样」)+ 环里一枚 ▼(减益);只拦灼时环里换成火苗
    ward: '<circle cx="32" cy="32" r="25" ' + S5 + '/><path ' + F + ' d="M32 45L20 23h24z"/>',
    wardburn: '<circle cx="32" cy="32" r="25" ' + S5 + '/><path ' + F + ' transform="translate(13.8 13.5) scale(.57)" d="M33 5c11 13 17 21 17 30a18 18 0 0 1-36 0c0-6 3-11 7-14-1 5 1 9 4 11 3-9-2-16-5-21 5 1 9-1 13-6z"/>',
    // 受击回复 / 受击得盾:左上一支打进来的箭(受击)+ 右下得到什么(加号 / 实心盾),同一个骨架换载荷
    hurtheal: '<path ' + S + ' d="M8 8l14 14M23 11v12H11"/><path ' + F + ' d="M35 24h10v11h11v10H45v11H35V45H24V35h11z"/>',
    hurtshield: '<path ' + S + ' d="M8 8l14 14M23 11v12H11"/><path ' + F + ' transform="translate(20.5 17.5) scale(.66)" d="' + SHIELD + '"/>',
    // 每回合效果(回合脉冲):顺时针回环箭头 + 圆心一点;护盾回流(浮屠)把圆心那点换成小盾
    turnpulse: '<path ' + S + ' d="M54 32A22 22 0 1 1 47.6 16.4"/><path ' + S + ' d="M37.6 16.4h10v-10"/><circle cx="32" cy="32" r="7" ' + F + '/>',
    shieldcarry: '<path ' + S + ' d="M54 32A22 22 0 1 1 47.6 16.4"/><path ' + S + ' d="M37.6 16.4h10v-10"/><path ' + F + ' transform="translate(19.8 17.4) scale(.38)" d="' + SHIELD + '"/>',
    // 大招削弱(杜塞):半实心圆 —— 「打一半」
    skillcut: '<circle cx="32" cy="32" r="22" ' + S + '/><path ' + F + ' d="M32 10a22 22 0 0 0 0 44z"/>',
    // —— NEW · 10-11 补(待审):D2-木 与土·铜墙铁壁(StatusChipsWood 画板) ——
    // 荆刺:标记的菱形 + 四角倒刺 —— 「打我方时自己被扎」;不用 thorns(那是敌人能力「倒刺」与木灵本命盾反)
    thornmark: '<path ' + S5 + ' d="M32 16l16 16-16 16-16-16z"/><circle cx="32" cy="32" r="5" ' + F + '/><path ' + F + ' d="M40 24l12-12-3 15zM40 40l12 12-15-3zM24 40L12 52l3-15zM24 24L12 12l15 3z"/>',
    // 被缠(攀附):一根桩 + 三道藤绕上去
    bind: '<path ' + S + ' d="M32 6v52"/><path ' + S5 + ' d="M20 14c16 4 16 10 0 14M44 28c-16 4-16 10 0 14M20 42c16 4 16 10 0 14"/>',
    // 守护(柘灵本命):实心盾挡在一个人前面
    guard: '<circle cx="44" cy="20" r="8" ' + F + '/><path ' + F + ' d="M30 50a14 14 0 0 1 28 0v6H30z"/><path ' + F + ' transform="translate(2 14) scale(.62)" d="' + SHIELD + '"/>',
    // 受伤上限(铜墙铁壁):≤ —— 「单次不超过」;与减伤的城垛分开(一个封顶、一个打折)
    hitcap: '<path ' + S + ' d="M46 12L18 26l28 14"/><path ' + S + ' d="M18 52h28"/>',
    // 现有,只作对照(木系画板)
    ranged: '<path ' + S + ' d="M15 10c13 8 13 36 0 44"/><path ' + S + ' d="M15 32h33M39 22l10 10-10 10"/>',
    melee: '<path ' + S + ' d="M14 50L50 14M50 50L14 14"/><path ' + S + ' d="M18 39l7 7M46 39l-7 7"/>',
    bleed: '<path ' + F + ' d="M31 6c8 12 13 19 13 25a13 13 0 0 1-26 0c0-6 5-13 13-25z"/><circle cx="15" cy="49" r="5" ' + F + '/><circle cx="47" cy="52" r="4" ' + F + '/>',
    dodge: '<path ' + S + ' d="M42 12c-16 10-16 30 0 40M30 15c-12 8-12 26 0 34M19 19c-8 6-8 20 0 26"/>',
    speed: '<path ' + S + ' d="M8 20h26M8 32h34M8 44h26"/><path ' + S + ' d="M40 20l14 12-14 12"/>',
    // 现有,只作对照(StatusChipsWaterEarth 的易混图标排与封字两态)
    immunity: '<path ' + S + ' d="M32 7l21 8v18c0 12-10 21-21 25-11-4-21-13-21-25V15z"/><path ' + S + ' d="M23 32l7 8 12-14"/>',
    seal: '<path ' + S + ' d="M21 30v-8a11 11 0 0 1 22 0v8"/><path ' + F + ' d="M15 30h34v24H15z"/>',
    // 现有,只作对照(StatusChipsFire 的易混图标排)
    crit: '<path ' + F + ' d="M32 4l7 19 19-7-7 19 7 19-19-7-7 19-7-19-19 7 7-19-7-19 19 7z"/>',
    blind: '<path ' + S + ' d="M8 32c10-13 38-13 48 0-10 13-38 13-48 0z"/><circle cx="32" cy="32" r="6" ' + F + '/><path ' + S + ' d="M14 50L50 14"/>',
    reflect: '<path ' + S + ' d="M10 32h44M22 22L12 32l10 10M42 22l10 10-10 10"/>',
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
