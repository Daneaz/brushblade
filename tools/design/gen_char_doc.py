# -*- coding: utf-8 -*-
"""从 chars.json 生成 docs/design/字表功能解析.md。配置表即真相,改表后重跑本脚本刷新。

用法:在仓库根目录执行 `python3 tools/design/gen_char_doc.py`。
"""
import json, collections, subprocess, sys, os

SRC = 'Brushblade/Assets/StreamingAssets/config/chars.json'
chars = json.load(open(SRC))['chars']
byid = {c['id']: c for c in chars}
comp = [c for c in chars if 'recipe' in c]
leaf = [c for c in chars if 'recipe' not in c]

# 二级拆解借管线的 IDS 拆解器,与配方生成同一套规则(只拆 ⿰⿱⿲⿳、子部件须是真实字)。
# ids.txt 是不入 git 的原始数据 —— 缺失时二级降级为「只按字表配方展开」。
sys.path.insert(0, os.path.join(os.path.dirname(__file__), '..', 'pipeline'))
try:
    from fetch_ids import parse_ids_text, RAW_PATH
    from decompose import build_index, split_once
    from filter_chars import attr_of
    IDS = build_index(parse_ids_text(RAW_PATH.read_text(encoding='utf-8'))) if RAW_PATH.exists() else None
except Exception:
    IDS, attr_of = None, lambda _: None

EL = {'Metal': '金', 'Wood': '木', 'Water': '水', 'Fire': '火', 'Earth': '土', 'Heart': '心'}
RA = {'White': '白', 'Green': '绿', 'Blue': '蓝', 'Purple': '紫', 'Gold': '金', 'Orange': '橙', 'Red': '红'}
RORDER = ['White', 'Green', 'Blue', 'Purple', 'Gold', 'Orange', 'Red']
EORDER = ['Wood', 'Fire', 'Earth', 'Metal', 'Water', 'Heart']
PUA = {'': '𣛧(木四叠·PUA)', '': '䥱(金四叠·PUA)'}
PASSIVE = {'healAlly': '治疗友军', 'onHitCurse': '命中施诅咒', 'dodge': '闪避',
           'thorns': '荆棘', 'speed': '速度', 'onHitBurn': '命中挂灼烧',
           'onHitBurnAll': '灼烧转全体', 'ranged': '远程:无视敌方前排'}

SHENG = {'Wood': 'Fire', 'Fire': 'Earth', 'Earth': 'Metal', 'Metal': 'Water', 'Water': 'Wood'}
# 走生克结算的效果才吃相生 ×3,与 Core 的 WuxingResolver.ResolveEffect 覆盖面一致
WUXING_SCALED = {'DamageSingle', 'DamageAll', 'Shield', 'HealSelf', 'HealAll', 'HealOverTime'}

def sheng_mult(c):
    """相生「他生我」:配方原料里含生本字属性的 → 3。配置表填的是基础值,×3 才是实战值。"""
    mother = [k for k, v in SHENG.items() if v == c.get('element')]
    if not mother:
        return 1
    return 3 if any(byid.get(p, {}).get('element') == mother[0] for p in c.get('recipe', [])) else 1

def cname(c): return PUA.get(c['id'], c['id'])

def lv1(c):
    """一级组成:字表配方原文(玩家实际拆合到的那一层)。"""
    return ' + '.join(PUA.get(p, p) for p in c['recipe'])

def lv2(c):
    """二级组成:把一级的每个部件再拆一层。

    字表里有配方的部件用字表配方(游戏内口径优先);没有的退回 IDS 拆一级;
    IDS 也拆不动的(冫、隹、里…)保留原样 —— 它已经是这套体系的终点。

    五行部件(土、氵、钅…)一律不拆:管线 decompose 的同一条规则 ——
    「五行部件是终点」,再往下的 土 = 十 + 一 在拆合语义里没有意义。
    """
    out = []
    for p in c['recipe']:
        if attr_of(p):
            out.append(p)
            continue
        sub = byid.get(p, {}).get('recipe') or (split_once(p, IDS) if IDS else None)
        out.extend(sub if sub else [p])
    return ' + '.join(PUA.get(p, p) for p in out)

def passive_txt(p):
    out = []
    for k, v in p.items():
        n = PASSIVE.get(k, k)
        out.append(n if v is True else f"{n} {v}")
    return '/'.join(out)

def desc(e, mult=1):
    k, t, all_ = e['kind'], e.get('turns', 0), e.get('targetAll')
    base = e.get('value', 0)
    scaled = k in WUXING_SCALED and mult > 1
    v = base * mult if scaled else base
    # 相生字保留算式(70×3=210):只写最终值会让人以为配置表填的就是它,
    # 只写基础值又与实战不符 —— 两个数都在,读表的人不用回头查倍率。
    if scaled:
        v = f"{base}×{mult}={base * mult}"
    s = {
        'DamageSingle': f"单体伤害 {v}", 'DamageAll': f"全体伤害 {v}",
        'BurnSingle': f"灼烧 {v} 层", 'BurnAll': f"全体灼烧 {v} 层",
        'BurnPotency': f"灼烧威力 +{v}/层(本场)", 'BurnNoDecay': "本场灼烧不衰减",
        'BurnSettleNow': "立即结算一次灼烧", 'Detonate': "引爆全部剩余灼烧",
        'Bleed': f"流血 {v}/回合", 'Shield': f"护盾 {v}",
        'HealSelf': f"治疗自身 {v}", 'HealAll': f"群体治疗 {v}",
        'HealOverTime': f"持续治疗 {v}×{t} 回合" + ("(含召唤物)" if all_ else ""),
        'Revive': f"复活 {v} 名召唤物(回半血)", 'Freeze': f"冻结 {v} 回合",
        'Slow': f"减速 {v} 回合", 'Silence': f"沉默 {t} 回合",
        'Blind': f"致盲 −{v}% 命中×{t} 回合" + ("(全体)" if all_ else ""),
        'Dispel': ("驱散敌方全部增益" if v == -1 else f"驱散敌方 {v} 条增益") + ("(全体各清)" if all_ else ""),
        'Cleanse': "净化自身全部减益", 'Immunity': f"免疫 {v} 次伤害",
        'Reflect': f"反弹 {v}% 伤害×{t} 回合", 'DefenseBuff': f"护甲 +{v}(本场)",
        # 破甲 2026-08-13 起是「削目标护甲 v 点」,不再是「承伤 +25% 持续 t 回合」
        'ArmorBreak': f"破甲 {v}(削目标护甲,本场,可叠)", 'Empower': f"攻击力 +{v}(本场)",
        'PierceBuff': f"穿透 +{v}(本场)", 'DodgeBuff': f"闪避 +{v}%(本场)",
        'Morale': f"战意 +{v} 层(每层 +10 攻,上限 5)", 'ApBoost': f"AP 上限 +{v}(本场)",
        'CritBuff': f"暴击率 +{v}%(本场)",
        'Summon': f"召唤 {e.get('count',1)} 只(血 {v}/攻 {e.get('attack',0)}"
                  + (f",{passive_txt(e['passive'])}" if e.get('passive') else "") + ")",
    }.get(k, f"{k} {v}")
    mods = []
    if e.get('doubleVsBurning'): mods.append("对灼烧目标双倍")
    if e.get('persistOnce'): mods.append("免一次清盾")
    # mods 会被外层括号整体包住,这里不能再带括号,否则嵌套成「(穿透 10(…))」
    if e.get('pierce'): mods.append(f"穿透 {e['pierce']}")
    if e.get('backline'): mods.append("偷袭:无视敌方前排")
    if e.get('hitCount', 1) > 1: mods.append(f"{e['hitCount']} 段独立结算")
    if e.get('executeBelowPercent'):
        mods.append(f"斩杀线 {e['executeBelowPercent']}%" + ("→直杀(Boss 免疫)" if e.get('executeKills') else "→双倍"))
    if e.get('summonShield'): mods.append(f"全场召唤物 +{e['summonShield']} 盾")
    return s + ("(" + "、".join(mods) + ")" if mods else "")

def atk(c):
    m = sheng_mult(c)
    parts = []
    for e in c['effects']:
        if e['kind'] in ('DamageSingle', 'DamageAll'):
            n = e.get('hitCount', 1)
            val = f"{e['value']}×{m}={e['value'] * m}" if m > 1 else f"{e['value']}"
            parts.append(val + (f"×{n} 段" if n > 1 else "")
                         + ("(AOE)" if e['kind'] == 'DamageAll' else ""))
    if parts: return '+'.join(parts)
    for e in c['effects']:
        if e['kind'] == 'Summon': return f"召 {e.get('attack',0)}×{e.get('count',1)}"
        if e['kind'] in ('BurnSingle', 'BurnAll'): return f"DOT {e['value']} 层"
        if e['kind'] == 'Bleed': return f"DOT {e['value']}/回合"
    return "—"

CATS = [
    ('冻结 / 减速(硬控)', {'Freeze', 'Slow'}),
    ('召唤', {'Summon'}),
    ('斩杀', set()),
    ('灼烧 / 火系 DOT 操作', {'BurnSingle', 'BurnAll', 'BurnPotency', 'BurnNoDecay', 'BurnSettleNow', 'Detonate'}),
    ('流血', {'Bleed'}),
    ('治疗 / 复活', {'HealSelf', 'HealAll', 'HealOverTime', 'Revive'}),
    ('护盾 / 护甲 / 免疫 / 反弹', {'Shield', 'DefenseBuff', 'Immunity', 'Reflect'}),
    ('破甲 / 穿透', {'ArmorBreak', 'PierceBuff'}),
    ('状态操作(驱散 / 净化 / 致盲 / 沉默)', {'Dispel', 'Cleanse', 'Blind', 'Silence'}),
    ('自强增益(攻 / 暴击 / AP / 战意 / 闪避)', {'Empower', 'Morale', 'ApBoost', 'CritBuff', 'DodgeBuff'}),
    ('纯伤害', {'DamageSingle', 'DamageAll'}),
]

def cat_of(c):
    kinds = {e['kind'] for e in c['effects']}
    if any(e.get('executeBelowPercent') for e in c['effects']): return '斩杀'
    if any(e.get('pierce') for e in c['effects']) and 'ArmorBreak' not in kinds: return '破甲 / 穿透'
    for nm, ks in CATS:
        if kinds & ks: return nm
    return '其他'

def row5(c):
    return (f"| {cname(c)} | {EL[c['element']]} | {RA[c['rarity']]} | {atk(c)} | {lv1(c)} | {lv2(c)} | "
            + "；".join(desc(e, sheng_mult(c)) for e in c['effects']) + " |")

def row4(c):
    return (f"| {cname(c)} | {RA[c['rarity']]} | {atk(c)} | {lv1(c)} | {lv2(c)} | "
            + "；".join(desc(e, sheng_mult(c)) for e in c['effects']) + " |")

H5 = "| 字 | 五行 | 稀有度 | 攻击力 | 一级组成 | 二级组成 | 功能 |\n|---|---|---|---|---|---|---|"
H4 = "| 字 | 稀有度 | 攻击力 | 一级组成 | 二级组成 | 功能 |\n|---|---|---|---|---|---|"

head = subprocess.run(['git', 'rev-parse', '--short', 'HEAD'], capture_output=True, text=True).stdout.strip()
rc = collections.Counter(c['rarity'] for c in comp)
ec = collections.Counter(c['element'] for c in comp)
kc = collections.Counter(e['kind'] for c in comp for e in c['effects'])

o = []
A = o.append
A("# 《字·斗》字表功能解析")
A("")
A(f"> 生成日期:2026-08-13 · 基线提交:`{head}`  ")
A(f"> 数据源:`{SRC}`(唯一真相)。本文由 `tools/design/gen_char_doc.py` 从配置表直出 —— **改表后重跑该脚本刷新,勿手工编辑**。")
A("")
A("## 口径说明")
A("")
A(f"- **收录范围**:配置表 {len(chars)} 条中的 **{len(comp)} 个可出牌字**(有配方 + 有效果)。另 {len(leaf)} 个无配方的部件/枢纽字只作合成原料,`IsLeaf` 会被奖励池过滤,玩家拿不到牌,故不入表。")
A("- **攻击力**:字表没有独立的攻击力字段,此列取**直伤效果的 value**(已是 2026-08-12 全表 ×10 后的量级)。")
A("  纯辅助字记 `—`;召唤字记 `召 攻×只数`(实际输出在召唤物身上);纯 DOT 字记 DOT 量。")
A("- **相生 ×3**:配置表填的是**基础值**,配方原料含「生本字属性」的字(燊/焚/蒸/炑/沏/刲)实战 ×3。")
A("  本表的攻击力与功能列写成 `70×3=210` —— 基础值与实战值都留着,读表不必回头查倍率;卡面(CharInfo)同口径。只有走生克结算的效果吃这个倍率,灼烧层数/召唤血攻/驱散条数是平值。")
A("- **AP 消耗**:全表一律 1(2026-08-03 拍板与稀有度解耦),故不设列。")
A("- **稀有度**:白 < 绿 < 蓝 < 紫 < 金 < 橙 < 红,枚举名 = 皮肤色 = 强度序。")
A("- **一级组成**:字表 `recipe` 原文,即玩家在局内实际拆出/合成的那一层。")
A("- **二级组成**:把一级的每个部件再拆一层 —— 部件自己在字表里有配方的用字表配方(游戏内口径优先),")
A("  没有的退回管线的 IDS 拆解器(`decompose.split_once`,只拆 ⿰⿱⿲⿳ 且子部件须是真实字),")
A("  两者都拆不动的(冫、隹、里…)保留原样,它已是这套体系的终点。")
A("  **五行部件(土、氵、钅…)一律不拆** —— 与管线同一条规则,再往下的「土 = 十 + 一」在拆合语义里没有意义。")
A("  ⚠ **二级里由 IDS 补出来的部件(七、几、勹…)不是游戏内对象** —— 字表里没有,玩家拿不到、也合不出。")
A("- **PUA 字**:木/金的四叠字在 Unicode 无合适码点,用私有区 U+E625 / U+E626 + 自造字形,文中标注 `(PUA)`。")
A("")
A("## 总览")
A("")
A("| 维度 | 分布 |")
A("|---|---|")
A("| 稀有度 | " + " / ".join(f"{RA[r]} {rc[r]}" for r in RORDER if rc[r]) + " |")
A("| 五行 | " + " / ".join(f"{EL[e]} {ec[e]}" for e in EORDER if ec[e]) + f" / 心 0 |")
A(f"| 效果条目 | {sum(kc.values())} 条,覆盖 {len(kc)} 种 `EffectKind`(枚举共 29 种) |")
A("| 单效果 / 双效果 / 三效果字 | " + " / ".join(str(collections.Counter(len(c['effects']) for c in comp)[n]) for n in (1, 2, 3)) + " |")
A("")
A("**心系 0 字** —— 第 5 章摄心流在字表侧没有任何载体。")
A("")
A("---")
A("")
A("# 一 · 按功能类型归类")
A("")
A("一个字只归一组,取其**最有辨识度的机制**(特殊机制优先于纯伤害)。所以「冰」「埋」这类带控的伤害字都进硬控组,便于横向比同类字的数值。")
groups = collections.defaultdict(list)
for c in comp: groups[cat_of(c)].append(c)
for nm, _ in CATS + [('其他', set())]:
    g = groups.get(nm)
    if not g: continue
    g.sort(key=lambda c: (RORDER.index(c['rarity']), EORDER.index(c['element'])))
    A("");  A(f"## {nm} · {len(g)} 字");  A("");  A(H5)
    o.extend(row5(c) for c in g)
A("")
A("---")
A("")
A("# 二 · 按稀有度排序(五行混排)")
A("")
A("看同一档位里五个系各拿到什么强度,用于横向校平。")
A("")
A(H5)
for c in sorted(comp, key=lambda c: (RORDER.index(c['rarity']), EORDER.index(c['element']))):
    A(row5(c))
A("")
A("---")
A("")
A("# 三 · 按五行分表(表内按稀有度)")
A("")
A("看单系的成长曲线是否连续、定位是否收敛。")
SUB = {'Wood': '召唤流唯一载体', 'Fire': 'DOT 与 AOE', 'Earth': '防御与破甲',
       'Metal': '高单体、斩杀、自强', 'Water': '治疗与控场'}
for el in EORDER:
    g = [c for c in comp if c['element'] == el]
    if not g: continue
    g.sort(key=lambda c: RORDER.index(c['rarity']))
    A("");  A(f"## {EL[el]}系 · {len(g)} 字 —— {SUB.get(el,'')}");  A("");  A(H4)
    o.extend(row4(c) for c in g)
A("")
A("---")
A("")
A("# 四 · 扫表观察")
A("")
A("以下为读表时的数值现象记录,**未做任何改动**,供后续 E-b4b5 的 T8 重平衡阶段取用。")
A("")
A("1. **同档同效的重复字**:墙 / 壁(护盾 70 完全一致)、埋 / 坑(伤 60 + 冻结 1)、涛 / 淹(AOE 50)、")
A("   桑 / 梅(召 血 60/攻 20)、割 / 剖 / 沸 / 碾(单体 60)。这批字玩家看不出差别,属于凑量。")
A("2. **档位内跳变不均**:金档单体从 刲 150 直接跳到 鑫 / 錰 400;紫档金系 4 个字挤在 160~200 无区分度。")
A("3. **紫档低攻召唤**:蕉(血 280/攻 30)、柘(血 320/攻 40)是纯肉盾定位,攻击力低于绿档的槐(攻 40),")
A("   紫档的价值全押在血量上,牌面读感偏弱。")
A("4. **远程召唤**:灶(攻 20)/ 烓(攻 30)是仅有的远程召唤物,无视敌方前排优先打后排;")
A("   **2026-08-20** 前攻击力曾为 0,输出全在「命中挂灼烧」被动上,补基础攻后灼烧仍是主输出。")
A("5. **心系空缺**:摄心流(第 5 章)与心属性的生克中立设定都已在引擎里,但没有一个心系字可出牌。")
A("6. **`AttackEffects` 未启用**:`CharDef` 有「拖到敌人身上改用另一套效果」的字段(2026-07-26),")
A("   但配置表里一条都没配,水 / 土 的「治疗/加盾之外多一个攻击选项」目前不存在。")
A("")

open('docs/design/字表功能解析.md', 'w').write('\n'.join(o) + '\n')
print("写入 docs/design/字表功能解析.md,共", len(o), "行")
