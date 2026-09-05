# -*- coding: utf-8 -*-
"""从 chars.json 生成 docs/design/字选型/字表功能解析.md。配置表即真相,改表后重跑本脚本刷新。

用法:在仓库根目录执行 `python3 tools/design/gen_char_doc.py`。
"""
import json, collections, subprocess, sys, os

SRC = 'Brushblade/Assets/StreamingAssets/config/chars.json'
chars = json.load(open(SRC))['chars']
byid = {c['id']: c for c in chars}
playable = [c for c in chars if not c.get('component')]
components = [c for c in chars if c.get('component')]

def all_effects(c):
    """字的全部效果,不分护/治面(effects)与攻击面(attackEffects)——分类、统计这类
    「这张字有什么机制」的问题不该在乎数值挂在哪一面,只有**展示**(功能列/攻击力列)
    才需要按面拆开(2026-09-02,水土双方向 Task 11:gen_char_doc.py 补双方向渲染)。"""
    return c['effects'] + c.get('attackEffects', [])

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

# 拼音 + 近代字意:字表里没有这两个字段(chars.json 只有 element/rarity/recipe/effects),
# 新华字典原始数据又不入 git、且其释义以「本义」为主(古义),与「近代字意」正相反 ——
# 故此处手工维护。字表新增可出牌字时,下面的断言会直接报错提醒补条目。
READINGS = {
    # 火
    '燚': ('yì', '火剧烈燃烧;今仅见于人名'),
    '燊': ('shēn', '旺盛;今仅见于人名'),
    '焚': ('fén', '烧毁'),
    '焱': ('yàn', '火花、光焰'),
    '炎': ('yán', '炎热;发炎'),
    '爆': ('bào', '爆炸;猛然破裂'),
    '炸': ('zhà', '爆炸;油炸'),
    '烈': ('liè', '猛烈、强烈'),
    '熣': ('cuǐ', '光彩貌,仅见于「熣灿」;今罕用'),
    '灿': ('càn', '灿烂、鲜明'),
    '烬': ('jìn', '燃烧后的余灰'),
    '灭': ('miè', '熄灭;消灭'),
    '焰': ('yàn', '火苗、火焰'),
    '燥': ('zào', '干燥、缺水分'),
    '蒸': ('zhēng', '蒸汽加热;水汽上升'),
    '炑': ('mù', '火盛;今罕用'),
    '灱': ('xiāo', '干燥;今罕用'),
    '烧': ('shāo', '燃烧;加热'),
    '焦': ('jiāo', '烧糊;焦急'),
    '灼': ('zhuó', '烧烫;明亮透彻'),
    '炽': ('chì', '火旺;热烈'),
    '热': ('rè', '温度高;受追捧'),
    '烓': ('wēi', '风炉;明亮;今罕用'),
    '灶': ('zào', '炉灶'),
    '煤': ('méi', '煤炭'),
    # 木
    '\ue625': ('—', '四叠木,现代字典未收通行读音;取「林木极盛」意'),
    '森': ('sēn', '树木茂密;阴森'),
    '林': ('lín', '成片的树木'),
    '楸': ('qiū', '楸树,落叶乔木'),
    '蕉': ('jiāo', '香蕉、芭蕉'),
    '荆': ('jīng', '荆棘;丛生灌木'),
    '柳': ('liǔ', '柳树'),
    '桂': ('guì', '桂树、桂花;广西简称'),
    '柘': ('zhè', '柘树,桑科乔木'),
    '桤': ('qī', '桤木,落叶乔木'),
    '藤': ('téng', '藤本植物的茎'),
    '杖': ('zhàng', '手杖、拐杖;倚仗'),
    '枪': ('qiāng', '枪械;长矛类兵器'),
    '柏': ('bǎi', '柏树'),
    '松': ('sōng', '松树;松散、放松'),
    '梅': ('méi', '梅树、梅花'),
    '棒': ('bàng', '棍棒;(口语)好、强'),
    '棍': ('gùn', '棍子;恶棍'),
    '藻': ('zǎo', '水中丛生的绿色植物'),
    '箭': ('jiàn', '弓弩发射的尖头长杆'),
    '葬': ('zàng', '掩埋亡者'),
    # 水
    '㵘': ('màn', '大水弥漫;今罕用'),
    '溎': ('guì', '水名;今罕用'),
    '淼': ('miǎo', '水面辽阔'),
    '淡': ('dàn', '味淡;色浅;冷淡'),
    '沝': ('zhuǐ', '两水相合;今罕用'),
    '湮': ('yān', '埋没、湮灭'),
    '沏': ('qī', '用开水冲泡'),
    '海': ('hǎi', '海洋;比喻数量极大'),
    '溃': ('kuì', '溃决;溃败;溃烂'),
    '洪': ('hóng', '洪水;宏大'),
    '涛': ('tāo', '大波浪'),
    '淬': ('cuì', '淬火;淬炼'),
    '洼': ('wā', '低洼、水坑'),
    '冰': ('bīng', '冰;冷冻'),
    '冻': ('dòng', '结冰;受冷'),
    '溶': ('róng', '溶解'),
    '凝': ('níng', '凝固;凝聚;凝视'),
    '冷': ('lěng', '温度低;冷淡'),
    '淋': ('lín', '浇淋、淋雨'),
    '澡': ('zǎo', '洗澡'),
    '沐': ('mù', '洗头;沐浴、蒙受'),
    '活': ('huó', '活着;灵活;活儿'),
    '浴': ('yù', '洗澡、沐浴'),
    # 金
    '\ue626': ('—', '四叠金,现代字典未收通行读音;取「金聚极盛」意'),
    '鑫': ('xīn', '金多兴盛,多用于人名、商号'),
    '刲': ('kuī', '刺、割;今罕用'),
    '錰': ('shù', '同「鉥」,长针;今罕用'),
    '锬': ('tán', '长矛;今罕用'),
    '鍂': ('—', '义未详的生僻字,现代无通行读音与用法'),
    '铡': ('zhá', '铡刀;用铡刀切'),
    '剁': ('duò', '用刀向下砍'),
    '锹': ('qiāo', '铁锹'),
    '劈': ('pī', '用刀斧纵向砍开'),
    '刺': ('cì', '扎刺;讽刺'),
    '剑': ('jiàn', '剑'),
    '镰': ('lián', '镰刀'),
    '锥': ('zhuī', '锥子;锥形'),
    '剿': ('jiǎo', '剿灭、围剿'),
    '铸': ('zhù', '铸造;造就'),
    '铠': ('kǎi', '铠甲'),
    '剡': ('yǎn', '削尖、锐利(作地名今读 shàn)'),
    '战': ('zhàn', '战争;发抖'),
    '利': ('lì', '锋利;利益'),
    '锋': ('fēng', '刀刃;前锋'),
    '锐': ('ruì', '锐利;精锐'),
    # 土
    '㙓': ('—', '四叠土,现代字典未收通行读音;取「土积极高」意'),
    '嶘': ('zhàn', '特别高险的山;今罕用'),
    '垚': ('yáo', '土高;多用于人名'),
    '圭': ('guī', '古代玉制礼器;今用于「圭臬」'),
    '塔': ('tǎ', '塔、塔形建筑'),
    '碉': ('diāo', '碉堡、碉楼'),
    '垒': ('lěi', '堡垒;垒砌;棒球的垒'),
    '堡': ('bǎo', '堡垒、城堡'),
    '墙': ('qiáng', '墙壁'),
    '壁': ('bì', '墙壁;陡崖'),
    '漜': ('yě', '泥浆、泥淖;今罕用'),
    '崊': ('lín', '山石;「崊嵚」高峻;今罕用'),
    '崟': ('yín', '山势高峻;今罕用'),
    '杜': ('dù', '杜绝;杜梨;姓'),
    '崩': ('bēng', '崩塌;崩溃'),
    '破': ('pò', '破损;破除;揭破'),
    '砸': ('zá', '砸击;(口语)搞坏、失败'),
    '碾': ('niǎn', '碾压;碾子'),
    '碎': ('suì', '破碎;琐碎'),
}

_missing = [c['id'] for c in playable if c['id'] not in READINGS]
assert not _missing, f"READINGS 缺字,请补条目:{_missing}"

# 词组:两张**表内字卡**凑成的现代汉语常用词。同样不是字表字段 —— 它是白/绿/蓝三档的
# 定档判据(2026-08-25 字表重构),真相在 docs/design/字选型/词组计分表.md,这里是那张表的
# 可执行副本;两边不许漂,由 tools/design/tests/test_doc_freshness.py 的
# test_phrases_match_the_scoring_doc 交叉钉住。
#
# ⚠ 本列表**保留已移出字表的字的词**(炽热、墙壁、铸剑…)—— 它们是当初移出决策的依据,
# 删掉就看不出那些字为什么走。渲染时按「两个字都还在可出牌字里」过滤,所以文档里只会
# 出现现役的词,不需要在这张表上做删减。
PHRASES = [
    # 火系
    '焦灼', '灼烧', '灼热', '烧焦', '炽热', '炽焰', '爆炸', '焚烧', '炎热', '炽烈', '燥热',
    # 水系
    '冷冻', '冷凝', '海涛', '冷淡', '淋浴', '沐浴', '冰冷', '冰冻', '冰海',
    # 土系
    '墙壁', '壁垒', '堡垒', '碉堡', '碾碎', '崩碎', '砸碎', '破碎',
    # 金系
    '锋利', '锐利', '锋锐', '剑锋', '铸剑', '劈刺',
    # 木系
    '棍棒', '松柏', '森林',
    # 跨系
    '冷战', '冷锋', '冰锥', '松涛', '林海', '冰棒', '热战', '剿灭', '冰壁', '崩溃',
    '湮灭', '剁碎', '枪刺',
]

_bad = [w for w in PHRASES if len(w) != 2]
assert not _bad, f"词组必须恰好两个字(两张字卡):{_bad}"
assert len(PHRASES) == len(set(PHRASES)), "PHRASES 有重复词"

# 字 → 它参与的、且两个字都还在可出牌字里的词。顺序按 PHRASES 原序,保证文档可复现。
_PLAYABLE = {c['id'] for c in playable}
PHRASE_OF = {cid: [w for w in PHRASES if cid in w and w[0] in _PLAYABLE and w[1] in _PLAYABLE]
             for cid in _PLAYABLE}


def pinyin(c): return READINGS[c['id']][0]


def gloss(c): return READINGS[c['id']][1]


def phrases(c): return '、'.join(PHRASE_OF[c['id']]) or '—'


EL = {'Metal': '金', 'Wood': '木', 'Water': '水', 'Fire': '火', 'Earth': '土', 'Heart': '心'}
RA = {'White': '白', 'Green': '绿', 'Blue': '蓝', 'Purple': '紫', 'Gold': '金', 'Orange': '橙', 'Red': '红'}
RORDER = ['White', 'Green', 'Blue', 'Purple', 'Gold', 'Orange', 'Red']
EORDER = ['Wood', 'Fire', 'Earth', 'Metal', 'Water', 'Heart']
PUA = {'': '𣛧(木四叠·PUA)', '': '䥱(金四叠·PUA)'}
PASSIVE = {'healAlly': '治疗友军', 'onHitCurse': '命中施诅咒', 'dodge': '闪避',
           'speed': '速度', 'onHitBurn': '命中挂灼烧',
           'onHitBurnAll': '灼烧转全体', 'ranged': '远程:无视敌方前排',
           'taunt': '嘲讽:强制敌人攻击它',
           }

# 召唤物的攻击形状(2026-08-22 引擎侧落地,2026-08-25 起字表里才有载体:剑 / 枪 / 蕉)。
# 不用括号作注 —— 整串被动会被外层「召唤 N 只(…)」括住,再嵌一层括号读起来是套娃。
SHAPE = {'Sweep': '横扫:整排', 'Cleave': '溅射:相邻',
         'Skewer': '贯穿:同列前后排', 'Volley': '连发', 'Chain': '弹射'}

def cname(c): return PUA.get(c['id'], c['id'])

def lv1(c):
    """一级组成:字表配方原文(玩家实际拆合到的那一层)。"""
    return ' + '.join(PUA.get(p, p) for p in c['recipe'])

# 非五行、但也不再往下拆的部件:IDS 能拆(禾 = ⿰丿木),可 丿 + 木 在拆合语义里
# 没有意义 —— 禾 自己已经是一个整部件。发现同类再往这里加。
STOP = {'禾'}

def _expand(parts):
    """把一串部件各拆一层:字表有配方的用字表配方(游戏内口径优先),

    没有的退回 IDS 拆一级;IDS 也拆不动的(冫、隹、里…)保留原样。
    五行部件(土、氵、钅…)与 STOP 里的部件一律不拆。
    """
    out = []
    for p in parts:
        if attr_of(p) or p in STOP:
            out.append(p)
            continue
        sub = byid.get(p, {}).get('recipe') or (split_once(p, IDS) if IDS else None)
        out.extend(sub if sub else [p])
    return out

def lv2_parts(c):
    """二级组成的部件表;不比一级多给出五行信息的返回 None(表里记 "-")。

    一级(字表配方)可能**跳过字形的中间层** —— 荆 的配方是 艹 + 刂,把 茾 整个略去了,
    于是照配方展开原地打转(艹、刂 都是五行部件,拆不动)。这种拆不出新东西的字改按
    字本身的 IDS 拆一层再展开:荆 → 茾 + 刂 → 艹 + 开 + 刂。
    """
    out = _expand(c['recipe'])
    if out == list(c['recipe']):
        ids_parts = split_once(c['id'], IDS) if IDS else None
        if ids_parts:
            out = _expand(ids_parts)
    # 二级只有在「比一级多给出五行信息」时才值得占一列:与一级同形的、
    # 以及整串只落一个五行部件的(松 = 木 + 八 + 厶),一律记 "-"。
    if out == list(c['recipe']) or sum(1 for p in out if attr_of(p)) <= 1:
        return None
    return out

def lv2(c):
    """二级组成列的文本。"""
    parts = lv2_parts(c)
    return '-' if parts is None else ' + '.join(PUA.get(p, p) for p in parts)

def lv2_count(c):
    """排序用的二级部件数:记 "-" 的按一级组成数(拍板口径)。"""
    parts = lv2_parts(c)
    return len(parts) if parts else len(c['recipe'])

def order(c):
    """全表排序键:稀有度升序;同稀有度内二级部件数越多越靠后;再按五行(相生环序)。"""
    return (RORDER.index(c['rarity']), lv2_count(c), EORDER.index(c['element']))

def passive_txt(p):
    """召唤被动的卡面文案。形状三件套(shape / shapePercent / shots)合成一句 ——
    分开写会渲染成「溅射/非主目标 50%」这种断句,读起来像两条独立被动。"""
    out = []
    for k, v in p.items():
        if k in ('shape', 'shapePercent', 'shots'):
            continue
        if k == 'onSummonFreeze':   # 单位是回合数,套「名字 + 数字」的模板会读成「回合数 1」
            out.append(f"入场冻结 1 敌 {v} 回合")
            continue
        if k == 'thorns':   # 2026-08-25 起单位是「受到伤害的百分比」,不是固定点数
            out.append(f"被打反弹 {v}% 伤害")
            continue
        if k == 'onHitFreezeChance':
            out.append(f"出手 {v}% 冻结 {max(1, p.get('onHitFreezeTurns', 1))} 回合")
            continue
        if k == 'onHitSlowPercent':
            out.append(f"出手减速 {v},持续 {max(1, p.get('onHitSlowTurns', 1))} 回合")
            continue
        if k in ('onHitFreezeTurns', 'onHitSlowTurns'):
            continue   # 已并进上面那两句
        n = PASSIVE.get(k, k)
        out.append(n if v is True else f"{n} {v}")
    if p.get('shape'):
        shape = SHAPE.get(p['shape'], p['shape'])
        if p['shape'] == 'Volley' and p.get('shots'):
            shape += f" {p['shots']} 发"
        if p.get('shape') == 'Chain' and p.get('shots'):
            shape += f" {p['shots']} 跳"
        if p.get('shapePercent'):
            shape += f",非主目标 {p['shapePercent']}%"
        out.append(shape)
    return '/'.join(out)

def desc(e):
    k, t, all_ = e['kind'], e.get('turns', 0), e.get('targetAll')
    v = e.get('value', 0)
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
        'Morale': f"战意 +{v} 层(每层 +10% 攻,上限 5)", 'ApBoost': f"AP 上限 +{v}(本场)",
        'CritBuff': f"暴击率 +{v}%(本场)",
        'Summon': f"召唤 {e.get('count',1)} 只(血 {v}/攻 {e.get('attack',0)}"
                  + (f",{passive_txt(e['passive'])}" if e.get('passive') else "") + ")",
        # 发势 / 泻(2026-09-02,水土双方向):清空全部势/水势,按层数 × value 打全体。
        'SpendMomentum': f"发势:清空全部势,全体伤害 = 层数×{v}",
        'SpendWaterPower': f"泻:清空全部水势,全体伤害 = 层数×{v}",
    }.get(k, f"{k} {v}")
    mods = []
    # 伤害侧的目标形状(2026-08-25 装配到 碾/砸/刺):与召唤被动侧共用 SHAPE 表。
    # 此前只有召唤物带形状,伤害字一个都没有,所以这一段从来没被渲染过。
    if e.get('shape') and e['shape'] != 'Single':
        label = SHAPE.get(e['shape'], e['shape'])
        if e['shape'] == 'Volley' and e.get('shots'): label += f" {e['shots']} 发"
        if e['shape'] == 'Chain' and e.get('shots'): label += f" {e['shots']} 跳"
        pct = e.get('shapePercent')
        suffix = (f",每跳 ×{pct}%" if e["shape"] == "Chain" else f",非主目标 {pct}%") if pct and pct != 100 else ""
        mods.append(label + suffix)
    cond = {'Burning': "对灼烧目标双倍", 'Bleeding': "对流血目标双倍",
            'Controlled': "对冻结/减速目标双倍", 'ArmorBroken': "对破甲目标双倍"}.get(e.get('doubleVs'))
    if cond: mods.append(cond)
    if e.get('persistOnce'): mods.append("免一次清盾")
    # mods 会被外层括号整体包住,这里不能再带括号,否则嵌套成「(穿透 10(…))」
    if e.get('pierce'): mods.append(f"穿透 {e['pierce']}")
    if e.get('backline'): mods.append("偷袭:无视敌方前排")
    if e.get('hitCount', 1) > 1: mods.append(f"{e['hitCount']} 段独立结算")
    if e.get('executeBelowPercent'):
        mods.append(f"斩杀线 {e['executeBelowPercent']}%"
                    + ("→直杀(Boss 改吃双倍)" if e.get('executeKills') else "→双倍"))
    if e.get('summonShield'): mods.append(f"全场召唤物 +{e['summonShield']} 盾")
    return s + ("(" + "、".join(mods) + ")" if mods else "")

def atk(c):
    # 双方向字(水/土,2026-09-02):攻击力是它作为输出字的强度,取攻击面 ——
    # 单方向字没有 attackEffects,这里退回原来的 effects,行为不变。
    effects = c.get('attackEffects') or c['effects']
    parts = []
    for e in effects:
        if e['kind'] in ('DamageSingle', 'DamageAll'):
            n = e.get('hitCount', 1)
            parts.append(f"{e['value']}" + (f"×{n} 段" if n > 1 else "")
                         + ("(AOE)" if e['kind'] == 'DamageAll' else ""))
    if parts: return '+'.join(parts)
    for e in effects:
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
    # 归类看的是「这张字有什么机制」,与机制挂在护/治面还是攻击面无关 ——
    # 双方向字(水/土)用两面的并集,单方向字 all_effects 就等于 effects,行为不变。
    effects = all_effects(c)
    kinds = {e['kind'] for e in effects}
    if any(e.get('executeBelowPercent') for e in effects): return '斩杀'
    if any(e.get('pierce') for e in effects) and 'ArmorBreak' not in kinds: return '破甲 / 穿透'
    for nm, ks in CATS:
        if kinds & ks: return nm
    return '其他'

def func_desc(c):
    """功能列。双方向字(水/土,2026-09-02)选「护」走 effects、选「攻」走 attackEffects,
    两面互斥地打出来才对得上真实玩法,格式定为 `攻:… / 护:…`;单方向字没有 attackEffects,
    退回原来的单段拼接,字节不变。"""
    support = "；".join(desc(e) for e in c['effects'])
    if c.get('attackEffects'):
        attack = "；".join(desc(e) for e in c['attackEffects'])
        return f"攻:{attack} / 护:{support}"
    return support

def row5(c):
    return (f"| {cname(c)} | {pinyin(c)} | {gloss(c)} | {EL[c['element']]} | {RA[c['rarity']]} | "
            f"{phrases(c)} | {atk(c)} | {lv1(c)} | {lv2(c)} | " + func_desc(c) + " |")

def row4(c):
    return (f"| {cname(c)} | {pinyin(c)} | {gloss(c)} | {RA[c['rarity']]} | "
            f"{phrases(c)} | {atk(c)} | {lv1(c)} | {lv2(c)} | " + func_desc(c) + " |")

H5 = ("| 字 | 拼音 | 近代字意 | 五行 | 稀有度 | 词组 | 攻击力 | 一级组成 | 二级组成 | 功能 |\n"
      "|---|---|---|---|---|---|---|---|---|---|")
H4 = ("| 字 | 拼音 | 近代字意 | 稀有度 | 词组 | 攻击力 | 一级组成 | 二级组成 | 功能 |\n"
      "|---|---|---|---|---|---|---|---|---|")

head = subprocess.run(['git', 'rev-parse', '--short', 'HEAD'], capture_output=True, text=True).stdout.strip()
rc = collections.Counter(c['rarity'] for c in playable)
ec = collections.Counter(c['element'] for c in playable)
kc = collections.Counter(e['kind'] for c in playable for e in all_effects(c))

o = []
A = o.append
A("# 《字·斗》字表功能解析")
A("")
A(f"> 生成日期:2026-08-23 · 基线提交:`{head}`  ")
A(f"> 数据源:`{SRC}`(唯一真相)。本文由 `tools/design/gen_char_doc.py` 从配置表直出 —— **改表后重跑该脚本刷新,勿手工编辑**。")
A("")
A("## 口径说明")
A("")
A(f"- **收录范围**:配置表 {len(chars)} 条中的 **{len(playable)} 个可出牌字**(有配方 + 有效果)。另 {len(components)} 个部件/枢纽字只作合成原料(其中部分自身也带配方,可再向下拆一层 —— 2026-09-01 二级拆解新增的中间层),`IsComponent` 会被奖励池过滤,玩家拿不到牌,故不入表。")
A("- **攻击力**:字表没有独立的攻击力字段,此列取**直伤效果的 value**(已是 2026-08-12 全表 ×10 后的量级)。")
A("  纯辅助字记 `—`;召唤字记 `召 攻×只数`(实际输出在召唤物身上);纯 DOT 字记 DOT 量。")
A("  **双方向字(水/土,2026-09-02)取攻击面的值** —— 那是它作为输出字的强度,与功能列的护/治面分开算。")
A("- **相克 ×1.5 / 被克 ×0.5**:配置表填的**就是实战值**——相生 ×3 已于 2026-09-02 取消")
A("  (全表 74 字里原本只有 4 字吃得到,是条空转规则;焚/蒸/刲 已等值改写进基础值,战斗结果不变)。")
A("  本表的攻击力与功能列因此不再需要 `70×3=210` 这类换算式,直接就是实战数字;卡面(CharInfo)同口径。")
A("- **AP 消耗**:全表一律 1(2026-08-03 拍板与稀有度解耦),故不设列。")
A("- **稀有度**:白 < 绿 < 蓝 < 紫 < 金 < 橙 < 红,枚举名 = 皮肤色 = 强度序。")
A("- **拼音 / 近代字意**:字表(`chars.json`)没有这两个字段,新华字典原始数据又不入 git、")
A("  且其释义以「本义」(古义)为主,与「近代字意」正相反 —— 故这两列在 `gen_char_doc.py` 的 `READINGS` 表里**手工维护**。")
A("  近代字意取**今天还在用的常用义**;古义已废、现代只作人名/商号或字典生僻条目的字,直接标「今罕用」并注明来历。")
A("  四叠字(𣛧 / 䥱 / 㙓)与 鍂 现代字典无通行读音,拼音记 `—`。")
A("- **词组**:两张**表内字卡**凑成的现代汉语常用词,正倒序算一条(炽热 = 热炽,只记一次)。")
A("  这是白/绿/蓝三档的**定档判据**(2026-08-25 字表重构):`≥3 条 → 蓝`、`= 2 条 → 绿`、`≤1 条 → 白`,")
A("  同分比搭档字的档位;紫及以上由结构(五行部件数)定档,不看词组。收词规则与完整词表见")
A("  `docs/design/字选型/词组计分表.md`,本列由 `gen_char_doc.py` 的 `PHRASES` 直出,两边由测试钉住。")
A("  **不收**:本体部件词(焦土 / 火海 / 木棍 / 镰刀 / 碎石…)、树种 + 林(松林 / 柏林)、")
A("  边缘书面词、以及另一字不在表内的(藤蔓 / 荆棘 / 灰烬)。")
A("  只列**两个字都还在字表里**的词 —— 随字被移出而失效的词(炽热 / 墙壁 / 铸剑…)不出现在本表,")
A("  但仍留在 `PHRASES` 里作为当初移出决策的依据。")
A("- **一级组成**:字表 `recipe` 原文,即玩家在局内实际拆出/合成的那一层。")
A("- **二级组成**:只在**比一级多给出五行信息**时才填,否则记 `-` —— 两种记 `-` 的情况:")
A("  ① 二级拆出来与一级完全一致(冰 = 冫 + 水,再拆还是 冫 + 水);")
A("  ② 整串二级里只落一个五行部件(松 = 木 + 八 + 厶,只有一个「木」),多出来的中性部件对生克读表没有意义。")
A("  填出来的那一层规则是:把一级的每个部件再拆一层 —— 部件自己在字表里有配方的用字表配方(游戏内口径优先),")
A("  没有的退回管线的 IDS 拆解器(`decompose.split_once`,只拆 ⿰⿱⿲⿳ 且子部件须是真实字),")
A("  两者都拆不动的(冫、隹、里…)保留原样,它已是这套体系的终点。")
A("  **禾 也不再拆** —— IDS 能拆成 丿 + 木,但 禾 自己已经是一个整部件,再拆没有拆合语义。")
A("  **五行部件(土、氵、钅…)一律不拆** —— 与管线同一条规则,再往下的「土 = 十 + 一」在拆合语义里没有意义。")
A("  ⚠ **一级(字表配方)可能跳过字形的中间层**:荆 的配方是 艹 + 刂,把 茾 整个略去了,照配方展开原地打转。")
A("  这类字改按字本身的 IDS 拆一层再展开 —— 荆 → 茾 + 刂 → 艹 + 开 + 刂。")
A("  ⚠ **二级里由 IDS 补出来的部件(七、几、勹…)不是游戏内对象** —— 字表里没有,玩家拿不到、也合不出。")
A("- **排序**:三张表一律**稀有度升序**;同稀有度内**二级组成部件数越多排得越后**;再按五行(木火土金水)。")
A("  同稀有度里拆得更深的字排在后面(楸 = 木 + 禾 + 火 三部件,排在林之后);")
A("  记 `-` 的行按**一级组成**的部件数计(全表配方都是两部件),故一律排在填出二级的字之前。")
A("- **PUA 字**:木/金的四叠字在 Unicode 无合适码点,用私有区 U+E625 / U+E626 + 自造字形,文中标注 `(PUA)`。")
A("")
A("## 总览")
A("")
A("| 维度 | 分布 |")
A("|---|---|")
A("| 稀有度 | " + " / ".join(f"{RA[r]} {rc[r]}" for r in RORDER if rc[r]) + " |")
A("| 五行 | " + " / ".join(f"{EL[e]} {ec[e]}" for e in EORDER if ec[e]) + f" / 心 0 |")
# 枚举总数是手动同步的字面量(EffectDef.cs 的 EffectKind 不在本脚本的解析范围内)——
# 2026-09-02 发现这里已经飘了(SpendMomentum/SpendWaterPower 上线后枚举实际是 32),
# 顺手修正;以后新增 Kind 记得同步这个数。
A(f"| 效果条目 | {sum(kc.values())} 条,覆盖 {len(kc)} 种 `EffectKind`(枚举共 32 种) |")
A("| 单效果 / 双效果 / 三效果字 | " + " / ".join(str(collections.Counter(len(all_effects(c)) for c in playable)[n]) for n in (1, 2, 3)) + " |")
A("")
A("**心系 0 字** —— 第 5 章摄心流在字表侧没有任何载体。")
A("")
A("---")
A("")
A("# 一 · 按功能类型归类")
A("")
A("一个字只归一组,取其**最有辨识度的机制**(特殊机制优先于纯伤害)。所以「冰」「埋」这类带控的伤害字都进硬控组,便于横向比同类字的数值。")
groups = collections.defaultdict(list)
for c in playable: groups[cat_of(c)].append(c)
for nm, _ in CATS + [('其他', set())]:
    g = groups.get(nm)
    if not g: continue
    g.sort(key=order)
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
for c in sorted(playable, key=order):
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
    g = [c for c in playable if c['element'] == el]
    if not g: continue
    g.sort(key=order)
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
A("6. **`AttackEffects` 双方向字**(2026-09-02):水系 15 字 + 土系 13 字共 28 个可出牌字")
A("   配了 attackEffects —— 双击选「攻」/拖到敌人身上走这套,选「护」走 effects,两者互斥。")
A("   功能列格式为 `攻:… / 护:…`;攻击力列取攻击面的值。碉/堡/塔 三张召唤字例外:")
A("   攻击面沿用原有召唤效果(血/攻/被动),只是新增了护盾面。")
A("")

# 2026-08-23 文档被移进「字选型/」,这里的路径当时没跟着改 ——
# 脚本于是往老位置生成了一份影子文件,而真正在读的那份纹丝不动。
DEST = 'docs/design/字选型/字表功能解析.md'
open(DEST, 'w', encoding='utf-8').write('\n'.join(o) + '\n')
print(f"写入 {DEST},共 {len(o)} 行")
