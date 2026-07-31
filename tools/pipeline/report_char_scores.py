"""字卡评分表:枢纽字体系(方案 A)的 145 字 + 心系扩展候补,按六系 + 跨系分类打分。

评分三维度(满分 100):
- **成本分 30**:元素成本越高分越高(拆合代价 = 强度上限)
- **跨系分 25**:属性数越多分越高(跨系字要靠掠夺别系,且触发生克乘数)
- **契合分 45**:字义与本系机制特性的贴合度,人工判定(见 AFFINITY)

前两维由结构算出,第三维是字义判断——没有数据源能替代,表在下面明写,可以直接改。

用法:tools/pipeline$ python3 report_char_scores.py
产出:docs/design/字选型/字卡评分表.md
"""
import datetime
import json
import sys
from pathlib import Path

import report_hub_chars as H
from decompose import build_index
from fetch_ids import parse_ids_text
from filter_chars import attr_of

ROOT = Path(__file__).resolve().parent

# 六系机制特性(2026-07-31 拍板),契合分就是照这个判的
ELEMENT_TRAITS = {
    "火": ("燃烧攻击", "伤害同时挂 DOT"),
    "木": ("召唤", "召唤小兵、生长"),
    "水": ("治疗恢复", "为自身回复 HP"),
    "金": ("单体金属攻击", "刀枪剑戟,单体高伤"),
    "土": ("防御", "加盾、叠护甲"),
    "心": ("精神攻击", "debuff 与控制"),
}

TIER_SCORE = {"核心": 45, "贴合": 30, "沾边": 15}

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
    "杜": ("木", "贴合"), "柘": ("木", "贴合"), "芏": ("木", "贴合"),
    "菻": ("木", "沾边"), "棪": ("木", "沾边"), "杺": ("木", "沾边"),
    "莯": ("木", "沾边"), "菚": ("木", "沾边"), "蓕": ("木", "沾边"),
    "荰": ("木", "沾边"), "茘": ("木", "沾边"), "埜": ("木", "沾边"),
    "芯": ("木", "沾边"), "橤": ("木", "沾边"),
    # ---- 水:治疗恢复 ----
    "水": ("水", "核心"), "沐": ("水", "核心"), "淋": ("水", "贴合"),
    "沁": ("水", "贴合"), "氵": ("水", "贴合"), "沝": ("水", "贴合"),
    "淼": ("水", "贴合"), "㵘": ("水", "贴合"), "洼": ("水", "沾边"),
    "淦": ("水", "沾边"), "冰": ("水", "沾边"), "冫": ("水", "沾边"),
    "淡": ("水", "沾边"),
    # ---- 金:单体金属攻击 ----
    "金": ("金", "核心"), "刀": ("金", "核心"), "刂": ("金", "核心"),
    "戈": ("金", "核心"), "釖": ("金", "核心"), "刕": ("金", "核心"),
    "锬": ("金", "核心"), "剡": ("金", "核心"), "刲": ("金", "核心"),
    "剗": ("金", "核心"), "钅": ("金", "贴合"), "划": ("金", "贴合"),
    "戔": ("金", "贴合"), "钊": ("金", "贴合"), "釗": ("金", "贴合"),
    "錟": ("金", "贴合"), "矵": ("金", "沾边"), "鍂": ("金", "沾边"),
    "鑫": ("金", "沾边"), "𨰻": ("金", "沾边"), "銈": ("金", "沾边"),
    "惍": ("金", "沾边"),
    # ---- 土:防御 ----
    "土": ("土", "核心"), "山": ("土", "核心"), "石": ("土", "核心"),
    "岩": ("土", "核心"), "垚": ("土", "核心"), "磊": ("土", "核心"),
    "坧": ("土", "核心"), "㙓": ("土", "贴合"), "屾": ("土", "贴合"),
    "砳": ("土", "贴合"), "崟": ("土", "贴合"), "崯": ("土", "贴合"),
    "崊": ("土", "贴合"), "埊": ("土", "贴合"), "嶘": ("土", "沾边"),
    "漜": ("土", "沾边"), "圭": ("土", "沾边"), "硅": ("土", "沾边"),
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
    "桂": "桂树", "杜": "杜梨树", "柘": "柘树", "芏": "草名", "箖": "竹名",
    "埜": "「野」古字,荒野草木",
    "芯": "灯芯,草木制品",
    "沐": "沐浴洗涤;D3 已把它列为治疗载体",
    "淋": "浇灌",
    "沁": "渗入滋润",
    "洼": "蓄水成池,水量而非治疗",
    "淡": "冲淡削弱,偏减益不偏治疗",
    "冰": "凝冻,偏控制不偏治疗",
    "淦": "水入船中",
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
    "銈": "金圭,金属器物",
    "惍": "字义「利」,锋利与心思两解",
    "岩": "山崖,天然屏障",
    "垚": "山高土厚",
    "磊": "垒石",
    "坧": "基址、地基",
    "㙓": "土地广袤",
    "屾": "二山并立", "砳": "石名", "崟": "山高", "崯": "同「崟」",
    "崊": "山石", "埊": "「地」古字",
    "嶘": "山特别高", "漜": "泥浆",
    "圭": "古玉器,土系枢纽但字义不防御",
    "硅": "化学元素名,土系枢纽但字义不防御",
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


def total_score(leaves, tier):
    return cost_score(leaves) + cross_score(leaves) + affinity_score(tier)


# ---- 报表 ----

_HEAD = ("| 字 | 拼音 | 释义 | 一步配方 | 元素成本 | 类型 | 常用度 | 成本分 | 跨系分 | 契合分 "
         "| **总分** | 契合判定 | 选用 |\n|---|---|---|---|---|---|---|---|---|---|---|---|---|")


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
            f"| {affinity_score(tier):g} | **{rec['score']:g}** | {judged} | {flags} |")


def _table(records):
    return "\n".join([_HEAD] + [_row(r) for r in sorted(records, key=lambda r: (-r["score"],
                                                                               r["char"]))])


def _usable(by_group, attr):
    """某系实际能做卡的字:玩家认得 + 契合 ≥ 贴合(跨系字按契合判定归系),按分数降序。"""
    pool = [r for recs in by_group.values() for r in recs
            if r["tier_attr"] == attr and r["tier"] in ("核心", "贴合")
            and (H.gb_level(r["char"]) in (1, 2) or r["char"] in H.NAME_COMMON or r["in_game"])]
    return sorted(pool, key=lambda r: -r["score"])


def build_report(by_group, today):
    total = sum(len(v) for v in by_group.values())
    out = [f"""# 字卡评分表(枢纽字体系 · 方案 A)

> 生成:{today},`tools/pipeline/report_char_scores.py`
> 字源:`枢纽字体系可用字表.md` 的 {total} 字(纯元素可达字 + 心系扩展区候补 𢗰)。
> 枢纽字不再单列,归回各自的系;跨系字统一为一类。⚠️ 编辑本表请保存为 UTF-8。

## 评分规则(满分 100)

| 维度 | 满分 | 怎么算 |
|---|---|---|
| **成本分** | 30 | 元素成本越高越贵:1 元素 0 分,2 元素 10,3 元素 20,4 元素 30 |
| **跨系分** | 25 | 属性数越多越难凑:单系 0 分,双系 12.5,三系 25 |
| **契合分** | 45 | 字义与本系机制特性的贴合度:核心 45 / 贴合 30 / 沾边 15 / 无关或字义不明 0 |

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

## 各系实际可用度

只数**玩家认得**(GB 一二级 / 人名高频 / 游戏用字)且**契合 ≥ 贴合**的字——
生僻字排得再高也上不了卡,这一列才是各系真正能做卡的量。跨系字按契合判定归到它偏向的那一系。

| 系 | 可用字数 | 具体是哪些 |
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
                       "**契合判定**列的系名就是判定依据。\n\n"
                       "⚠️ 结构分(成本 30 + 跨系 25)能压过契合分,所以 嶘/漜 这类"
                       "字义沾边的生僻字会排在 淡/洼 这类常用字前面。**先看常用度列再看总分**"
                       "——生僻字排得再高也上不了卡。\n")
        out.append(_table(recs))
    out.append("")
    return "\n".join(out)


def main():
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

    by_group = {a: [] for a in "火木水金土心"}
    by_group["跨系"] = []
    for rec in records:
        tier_info = affinity_of(rec["char"])
        rec["tier_attr"], rec["tier"] = tier_info if tier_info else (None, None)
        rec["score"] = total_score(rec["leaves"], rec["tier"])
        by_group["跨系" if len(rec["attrs"]) > 1 else rec["attrs"][0]].append(rec)

    text = build_report(by_group, datetime.date.today().isoformat())
    path = ROOT.parent.parent / "docs/design/字选型/字卡评分表.md"
    path.write_text(text)
    print("写入", path.name, f"({len(records)} 字,{len(text.splitlines())} 行)")


if __name__ == "__main__":
    main()
