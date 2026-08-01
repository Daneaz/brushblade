"""字卡评分表:枢纽字体系(方案 A)的 145 字 + 心系扩展候补,按六系 + 跨系分类打分。

评分五维度(满分 135):
- **成本分 30**:元素成本越高分越高(拆合代价 = 强度上限)
- **跨系分 25**:属性数越多分越高(跨系字要靠掠夺别系)
- **契合分 45**:字义与本系机制特性的贴合度,人工判定(见 AFFINITY)
- **相生分 15**:配方自带相生对 → 效果 ×3,白捡的强度
- **横向分 20**:它能和别系部件组合出多少字(枢纽字的全部价值)

前两维由结构算出,第三维是字义判断——没有数据源能替代,表在下面明写,可以直接改。

用法:tools/pipeline$ python3 report_char_scores.py
产出:docs/design/字选型/字卡评分表.md
"""
import datetime
import json
import sys
from pathlib import Path

from collections import defaultdict

import report_hub_chars as H
from decompose import build_index
from fetch_ids import parse_ids_text
from filter_chars import attr_of

ROOT = Path(__file__).resolve().parent

# 六系机制特性(2026-07-31 拍板),契合分就是照这个判的
ELEMENT_TRAITS = {
    "火": ("燃烧攻击", "伤害同时挂 DOT"),
    "木": ("召唤", "召唤小兵、生长"),
    "水": ("治疗恢复(主)+ 控制(副)", "回复 HP;冻结、淹没、冲淡削弱"),
    "金": ("单体金属攻击", "刀枪剑戟,单体高伤"),
    "土": ("防御(主)+ 自身增益(副)", "加盾、叠护甲;给自己上 buff"),
    "心": ("精神攻击", "debuff 与控制"),
}

TIER_SCORE = {"核心": 45, "贴合": 30, "沾边": 15}

# 相生环(wuxing-reference):配方含相生对的字,效果 ×3;心中立,不参与生克
SHENG = [("木", "火"), ("火", "土"), ("土", "金"), ("金", "水"), ("水", "木")]

# 字义 → 特性契合度。(判定系, 档位):跨系字按字义最贴近的那一系判。
# 没列进来的字 = 字义与任何系的特性都对不上,或字典未收、字义不明 → 0 分。
AFFINITY = {
    # ---- 火:燃烧攻击 ----
    "火": ("火", "核心"), "炎": ("火", "核心"), "炏": ("火", "核心"),
    "焱": ("火", "核心"), "燚": ("火", "核心"), "灬": ("火", "贴合"),
    "焚": ("火", "核心"), "燊": ("火", "核心"), "炑": ("火", "核心"),
    "灶": ("火", "贴合"), "烓": ("火", "贴合"), "灿": ("火", "贴合"),
    "灱": ("火", "沾边"),
    # ---- 木:召唤 ----
    "木": ("木", "核心"), "林": ("木", "核心"), "森": ("木", "核心"),
    "𣛧": ("木", "核心"), "竹": ("木", "贴合"), "艹": ("木", "贴合"),
    "箖": ("木", "贴合"), "蕊": ("木", "贴合"), "桂": ("木", "贴合"),
    "柘": ("木", "贴合"), "芏": ("木", "贴合"),
    "菻": ("木", "沾边"), "棪": ("木", "沾边"), "杺": ("木", "沾边"),
    "莯": ("木", "沾边"), "菚": ("木", "沾边"), "蓕": ("木", "沾边"),
    "荰": ("木", "沾边"), "茘": ("木", "沾边"), "埜": ("木", "沾边"),
    "芯": ("木", "沾边"), "橤": ("木", "沾边"),
    # ---- 水:治疗(主)+ 控制(副) ----
    "水": ("水", "核心"), "沐": ("水", "核心"), "冰": ("水", "核心"),
    "淼": ("水", "核心"), "㵘": ("水", "核心"), "淋": ("水", "贴合"),
    "沁": ("水", "贴合"), "氵": ("水", "贴合"), "沝": ("水", "贴合"),
    "冫": ("水", "贴合"), "洼": ("水", "贴合"), "淡": ("水", "贴合"),
    "淦": ("水", "沾边"), "沯": ("水", "沾边"), "汕": ("水", "沾边"),
    # ---- 金:单体金属攻击 ----
    "金": ("金", "核心"), "刀": ("金", "核心"), "刂": ("金", "核心"),
    "戈": ("金", "核心"), "釖": ("金", "核心"), "刕": ("金", "核心"),
    "锬": ("金", "核心"), "剡": ("金", "核心"), "刲": ("金", "核心"),
    "剗": ("金", "核心"), "钅": ("金", "贴合"), "划": ("金", "贴合"),
    "戔": ("金", "贴合"), "钊": ("金", "贴合"), "釗": ("金", "贴合"),
    "錟": ("金", "贴合"), "矵": ("金", "沾边"), "鍂": ("金", "沾边"),
    "鑫": ("金", "沾边"), "𨰻": ("金", "沾边"), "惍": ("金", "沾边"),
    # ---- 土:防御(主)+ 自身增益(副) ----
    "土": ("土", "核心"), "山": ("土", "核心"), "石": ("土", "核心"),
    "岩": ("土", "核心"), "垚": ("土", "核心"), "磊": ("土", "核心"),
    "坧": ("土", "核心"), "杜": ("土", "核心"), "㙓": ("土", "贴合"),
    "屾": ("土", "贴合"), "砳": ("土", "贴合"), "崟": ("土", "贴合"),
    "崯": ("土", "贴合"), "崊": ("土", "贴合"), "埊": ("土", "贴合"),
    "嶘": ("土", "贴合"), "圭": ("土", "贴合"), "銈": ("土", "贴合"),
    "漜": ("土", "沾边"), "硅": ("土", "沾边"), "圸": ("土", "沾边"),
    # ---- 心:精神攻击 ----
    "惢": ("心", "核心"), "恚": ("心", "核心"), "忉": ("心", "核心"),
    "惏": ("心", "贴合"), "惔": ("心", "贴合"), "心": ("心", "贴合"),
    "忄": ("心", "贴合"), "𢗰": ("心", "贴合"),
}

# 契合判定的依据(只写需要解释的;元素本体与直白字义不赘述)
AFFINITY_NOTE = {
    "焚": "烧山,火系 AOE 的字面",
    "燊": "火盛,焱+木 自带燃料",
    "炑": "火炽盛貌",
    "灶": "灶火,持续燃烧",
    "烓": "风炉,持续燃烧",
    "灿": "光焰鲜明",
    "灱": "干燥,易燃但不是燃烧本身",
    "蕊": "花蕊,生长意象",
    "桂": "桂树", "柘": "柘树", "芏": "草名", "箖": "竹名",
    "埜": "「野」古字,荒野草木",
    "芯": "灯芯,草木制品",
    "沐": "沐浴洗涤;D3 已把它列为治疗载体",
    "淋": "浇灌",
    "沁": "渗入滋润",
    "洼": "深池陷坑,困住目标",
    "淡": "冲淡削弱,减益向的控制",
    "冰": "冻结,控制的字面",
    "淼": "水势浩大,淹没",
    "㵘": "大水漫溢,淹没",
    "冫": "「冰」古字,凝冻",
    "淦": "水入船中",
    "沯": "水激石,冲击",
    "汕": "鱼游水,水流裹挟",
    "釖": "「刀」异体",
    "刕": "三刀相叠",
    "锬": "长矛",
    "剡": "削、刺",
    "刲": "割、刺",
    "剗": "「刬」繁体,铲削",
    "划": "划破",
    "戔": "「戋」繁体,本义残伤",
    "钊": "磨损、削",
    "釗": "「钊」繁体",
    "錟": "「锬」繁体,长矛",
    "矵": "石+刂,刃器结构但字义不明",
    "鍂": "古乐器,金属但非兵刃",
    "鑫": "财富兴盛,金属但非兵刃",
    "𨰻": "「宝」古字,四金聚宝,非兵刃",

    "惍": "字义「利」,锋利与心思两解",
    "岩": "山崖,天然屏障",
    "垚": "山高土厚",
    "磊": "垒石",
    "坧": "基址、地基",
    "㙓": "土地广袤",
    "屾": "二山并立", "砳": "石名", "崟": "山高", "崯": "同「崟」",
    "崊": "山石", "埊": "「地」古字",
    "漜": "泥浆",
    "圭": "礼器玉圭,受圭即受命——自身增益向",
    "銈": "金圭,礼器加身",
    "杜": "杜绝、堵塞,防御的字面(本义杜梨树)",
    "嶘": "山特别高,天然屏障",
    "圸": "土+山,结构纯土但字义不明",
    "硅": "化学元素名,与防御无关",
    "惢": "三心为惢,心疑不定",
    "恚": "怨恨、愤怒",
    "忉": "忧愁",
    "惏": "「婪」异体,贪",
    "惔": "忧心如焚",
    "𢗰": "二心,惢的下半;无 BMP 码点,字形要自制",
}


def cost_score(leaves):
    """元素成本分(0~30):元素越多越贵,强度上限越高。"""
    return min(len(leaves) - 1, 3) * 10


def cross_score(leaves):
    """跨系分(0~25):属性数 1/2/3 → 0/12.5/25。"""
    return min(len({attr_of(leaf) for leaf in leaves}) - 1, 2) * 12.5


def affinity_of(char):
    """字 → (判定系, 档位);未收录返回 None。"""
    return AFFINITY.get(char)


def affinity_score(tier):
    """契合档位 → 分数;None(字义无关或不明)为 0。"""
    return TIER_SCORE.get(tier, 0)


# 横向组合数 → 分数(下限, 分)。枢纽字的核心价值:它能带出多少张别的卡
LATERAL_BANDS = [(8, 20), (5, 15), (3, 10), (1, 5), (0, 0)]


def attrs_of(char, by_char):
    """字的属性集:元素部件查部首表,合成字查它自己的记录。"""
    if attr_of(char):
        return {attr_of(char)}
    rec = by_char.get(char)
    return set(rec["attrs"]) if rec else set()


def lateral_index(records):
    """{部件: {(搭档串, 合成字)}} —— 只收**跨系**组合(搭档带进了本字没有的属性)。

    圭 → 木+圭=桂、氵+圭=洼、圭+刂=刲;炎 → 氵+炎=淡、钅+炎=锬。
    同系升阶(炎→焱、林→森)不算横向,它是纵向的升阶链。
    """
    by_char = {r["char"]: r for r in records}
    index = defaultdict(set)
    for rec in records:
        parts = rec["recipe"]
        if len(parts) < 2 or parts[0].startswith("("):
            continue
        for part in set(parts):
            if part not in by_char:
                continue
            partners = [p for p in parts if p != part] or [part]
            own = attrs_of(part, by_char)
            brought = set().union(*(attrs_of(p, by_char) for p in partners)) - own
            if brought:
                index[part].add((" + ".join(parts), rec["char"]))
    return index


def vertical_index(records):
    """{部件: {(配方, 合成字)}} —— 同系组合,即升阶链(火+火=炎、木+林=森)。"""
    by_char = {r["char"]: r for r in records}
    index = defaultdict(set)
    for rec in records:
        parts = rec["recipe"]
        if len(parts) < 2 or parts[0].startswith("("):
            continue
        for part in set(parts):
            if part not in by_char:
                continue
            partners = [p for p in parts if p != part] or [part]
            own = attrs_of(part, by_char)
            if not set().union(*(attrs_of(p, by_char) for p in partners)) - own:
                index[part].add((" + ".join(parts), rec["char"]))
    return index


def lateral_score(count):
    """横向组合分(0~20):能横向组合出的字越多,这个部件越是构筑核心。"""
    return next(score for floor, score in LATERAL_BANDS if count >= floor)


def sheng_pairs(leaves):
    """字里含的相生对(木生火、火生土、土生金、金生水、水生木);心中立不参与生克。"""
    attrs = {attr_of(leaf) for leaf in leaves}
    return [(a, b) for a, b in SHENG if a in attrs and b in attrs]


def sheng_score(leaves):
    """相生分(0~15):配方自带相生对 → 效果 ×3(wuxing-reference §乘数),白捡的强度。"""
    return 15 if sheng_pairs(leaves) else 0


def total_score(leaves, tier, lateral=0):
    return (cost_score(leaves) + cross_score(leaves) + affinity_score(tier)
            + sheng_score(leaves) + lateral_score(lateral))


# ---- 报表 ----

_HEAD = ("| 字 | 拼音 | 释义 | 一步配方 | 元素成本 | 类型 | 常用度 | 成本分 | 跨系分 | 契合分 "
         "| 相生分 | 横向分 | **总分** | 契合判定 | 选用 |"
         "\n|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|")


def _sheng_label(leaves):
    """相生分后面缀上是哪一对,如 "(木生火)";无相生对留空。"""
    pairs = sheng_pairs(leaves)
    return "(" + "、".join(f"{a}生{b}" for a, b in pairs) + ")" if pairs else ""


def _lateral_label(rec):
    """横向分后面缀上组合出几个字,如 "(8 字)";组合不出留空。"""
    return f"({len(rec['lateral'])} 字)" if rec["lateral"] else ""


def _type_label(rec):
    if rec["group"] == "元素":
        return "元素"
    return "枢纽" if rec["down_inner"] else "成品"


def _row(rec):
    tier = rec["tier"]
    note = AFFINITY_NOTE.get(rec["char"], "")
    if tier:
        judged = f"{rec['tier_attr']}·{tier}" + (f"({note})" if note else "")
    else:
        judged = "—" + (f"({note})" if note else "")
    flags = "**已在字表**" if rec["in_game"] else ""
    return (f"| {rec['char']} | {rec['pinyin']} | {rec['gloss']} | {' + '.join(rec['recipe'])} "
            f"| {H._cost_label(rec['leaves'])} | {_type_label(rec)} "
            f"| {H._level_label(rec['char'], rec['in_game'])} "
            f"| {cost_score(rec['leaves']):g} | {cross_score(rec['leaves']):g} "
            f"| {affinity_score(tier):g} "
            f"| {sheng_score(rec['leaves']):g}{_sheng_label(rec['leaves'])} "
            f"| {lateral_score(len(rec['lateral'])):g}{_lateral_label(rec)} "
            f"| **{rec['score']:g}** | {judged} | {flags} |")


def _table(records):
    return "\n".join([_HEAD] + [_row(r) for r in sorted(records, key=lambda r: (-r["score"],
                                                                               r["char"]))])


def _usable(by_group, attr):
    """某系契合 ≥ 贴合的字(跨系字按契合判定归系),按分数降序。"""
    pool = [r for recs in by_group.values() for r in recs
            if r["tier_attr"] == attr and r["tier"] in ("核心", "贴合")]
    return sorted(pool, key=lambda r: -r["score"])


def build_report(by_group, today):
    total = sum(len(v) for v in by_group.values())
    out = [f"""# 字卡评分表(枢纽字体系 · 方案 A)

> 生成:{today},`tools/pipeline/report_char_scores.py`
> 字源:`枢纽字体系可用字表.md` 的 {total} 字(纯元素可达字 + 心系扩展区候补 𢗰)。
> 枢纽字不再单列,归回各自的系;跨系字统一为一类。⚠️ 编辑本表请保存为 UTF-8。

## 评分规则(满分 135)

| 维度 | 满分 | 怎么算 |
|---|---|---|
| **成本分** | 30 | 元素成本越高越贵:1 元素 0 分,2 元素 10,3 元素 20,4 元素 30 |
| **跨系分** | 25 | 属性数越多越难凑:单系 0 分,双系 12.5,三系 25 |
| **契合分** | 45 | 字义与本系机制特性的贴合度:核心 45 / 贴合 30 / 沾边 15 / 无关或字义不明 0 |
| **相生分** | 15 | 配方自带相生对(木生火 / 火生土 / 土生金 / 金生水 / 水生木)→ +15,否则 0 |
| **横向分** | 20 | 它能和**别系**部件组合出多少字:≥8 → 20,5~7 → 15,3~4 → 10,1~2 → 5,0 → 0 |

满分 **135**。横向组合是枢纽字的全部价值(圭 能带出 桂/洼/刲…),明细见
`横向组合枢纽字表.md`;同系升阶(炎→焱)不算横向,那是纵向升阶链。相生对的配方效果 ×3(`wuxing-reference` §乘数),是白捡的强度,所以单列加分;
相克(×1.5)不加分,心中立不参与生克。常用度(GB 一二级 / 生僻)**不参与打分**,只作为信息列保留。

前两维是结构算出来的;**契合分是字义判断**,判定表写在 `report_char_scores.py` 的 `AFFINITY` 里,
不服直接改那张表重跑。跨系字按**字义最贴近的那一系**判,「契合判定」列写明按哪系判的。

## 六系机制特性

| 系 | 特性 | 机制 |
|---|---|---|
""" + "\n".join(f"| {a} | {t[0]} | {t[1]} |" for a, t in ELEMENT_TRAITS.items()) + """

## 各系字数与最高分

| 系 | 字数 | 榜首 |
|---|---|---|
""" + "\n".join(
        f"| {name} | {len(recs)} | " +
        "、".join(f"{r['char']} {r['score']:g}"
                  for r in sorted(recs, key=lambda r: -r["score"])[:3]) + " |"
        for name, recs in by_group.items()) + f"""

## 各系契合字

契合 ≥ 贴合的字,跨系字按契合判定归到它偏向的那一系。常用度不参与筛选,也不参与打分。

| 系 | 契合字数 | 具体是哪些 |
|---|---|---|
""" + "\n".join(f"| {a} | {len(_usable(by_group, a))} | "
                + ("、".join(f"{r['char']}({r['score']:g})" for r in _usable(by_group, a))
                   or "—") + " |"
                for a in "火木水金土心") + "\n"]

    for i, (name, recs) in enumerate(by_group.items()):
        num = "一二三四五六七"[i]
        title = f"{name}系" if name != "跨系" else "跨系字"
        out.append(f"\n## {num} · {title}({len(recs)} 个)\n")
        if name == "心":
            out.append("心系是六系里字最少的——纯元素可达的心系字只有 惢/心/忄 三个,"
                       "所以把附录里唯一的心系扩展区候补 **𢗰**(二心,无 BMP 码点)也并进来了。\n")
        if name == "跨系":
            out.append("含 ≥2 个属性的字。跨系分拉满,但契合分要看字义偏向哪一系——"
                       "**契合判定**列的系名就是判定依据。\n")
        out.append(_table(recs))
    out.append("")
    return "\n".join(out)


def build_records():
    """全部 146 字 → 打完分的记录表,按六系 + 跨系分组(稀有度表也用这份)。"""
    sys.setrecursionlimit(10000)
    entries = parse_ids_text((ROOT / "data/raw/ids.txt").read_text(encoding="utf-8"))
    index = build_index(entries)
    chars_json = (ROOT.parent.parent
                  / "Brushblade/Assets/StreamingAssets/config/chars.json")
    game_chars = json.load(open(chars_json))["chars"]
    readings = H.merge_readings(
        H.readings_map(json.load(open(ROOT / "data/raw/xinhua_word.json"))), game_chars)
    readings.update(H.EXTRA_READINGS)
    in_game = {H.resolve_pua(c["id"]) for c in game_chars}

    records = H.collect(entries, index, readings, in_game)
    # 心系唯一的扩展区候补:主表把它挡在收录范围外,但心系太薄,补进来
    stacks = H.collect_stack_candidates(entries, index, {r["char"] for r in records}, in_game)
    records += [dict(r, pinyin=readings.get(r["char"], ("—", ""))[0],
                     gloss=readings.get(r["char"], ("—", ""))[1],
                     attrs=[r["attr"]], group=r["attr"], down_inner=0, in_game=r["in_game"])
                for r in stacks if r["attr"] == "心"]

    lateral = lateral_index(records)
    by_group = {a: [] for a in "火木水金土心"}
    by_group["跨系"] = []
    for rec in records:
        tier_info = affinity_of(rec["char"])
        rec["tier_attr"], rec["tier"] = tier_info if tier_info else (None, None)
        rec["lateral"] = sorted(lateral.get(rec["char"], ()), key=lambda kv: kv[1])
        rec["score"] = total_score(rec["leaves"], rec["tier"], len(rec["lateral"]))
        by_group["跨系" if len(rec["attrs"]) > 1 else rec["attrs"][0]].append(rec)
    return by_group


def main():
    by_group = build_records()
    records = [r for recs in by_group.values() for r in recs]
    text = build_report(by_group, datetime.date.today().isoformat())
    path = ROOT.parent.parent / "docs/design/字选型/字卡评分表.md"
    path.write_text(text)
    print("写入", path.name, f"({len(records)} 字,{len(text.splitlines())} 行)")


if __name__ == "__main__":
    main()
