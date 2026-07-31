"""枢纽字体系(方案 A)可用字表:纯元素可达字 → 按枢纽/六系/跨系分类的人工筛选工作表。

口径见 `docs/superpowers/specs/2026-07-30-枢纽字体系与拆解经济-design.md`:
部件池只放五行元素 + 少数枢纽字,因此**只有一路拆到底叶子全为五行元素的字**能进体系。
与 report_pool_candidates.py(全量字体系/方案 B,允许声旁入池)是两套并列口径,不互相取代。

用法:tools/pipeline$ python3 report_hub_chars.py
产出:docs/design/字选型/枢纽字体系可用字表.md
"""
import datetime
import json
import sys
from collections import Counter, defaultdict
from pathlib import Path

from decompose import build_index, expand_to_elements, split_once
from enrich_readings import readings_map
from fetch_ids import parse_ids_text
from filter_chars import attr_of
from report_pool_candidates import gb_level, is_displayable, relation_label

ROOT = Path(__file__).resolve().parent

# 相生环顺序(木火土金水)+ 心
ATTR_ORDER = {a: i for i, a in enumerate("木火土金水心")}

# 5.4:不在 GB2312 内但人名高频,玩家实际认得,应计入可用字池
NAME_COMMON = set("焱燚淼鑫垚惢沝鍂炏")

# 字典与游戏字表都没收、但设计文档 D2 点名要用的字
EXTRA_READINGS = {"惢": ("suǒ", "三心为惢,心疑不定"),
                  "刲": ("kuī", "割、刺")}  # 字典源把 刲 误注为日本和字


def is_pure(leaves):
    """叶子是否全为五行元素部件(空表不算)。"""
    return bool(leaves) and all(attr_of(leaf) for leaf in leaves)


def element_cost(leaves):
    """叶子 → {属性: 个数};氵/水/冫 同计为水,成本按属性不按写法。"""
    return dict(Counter(attr_of(leaf) for leaf in leaves))


def canon_map(pure):
    """{字: 叶子} → {叶子多重集: 规范写法};同叶子多个字取 GB 常用度最高者。

    用于把配方里的扩展区部件(𪢴=土土)换成有字形的基本区等价字(圭)。
    """
    best = {}
    for char, leaves in pure.items():
        key = tuple(sorted(leaves))
        rank = {1: 0, 2: 1, 0: 2}[gb_level(char)]
        if key not in best or rank < best[key][0]:
            best[key] = (rank, char)
    return {key: char for key, (_, char) in best.items()}


def normalize_parts(parts, index, canon):
    """一步配方规范化:扩展区部件(无字形、做不成卡)换成同叶子的基本区字,无等价字则摊平。"""
    out = []
    for part in parts:
        if is_displayable(part):
            out.append(part)
            continue
        leaves = expand_to_elements(part, index)
        out.append(canon.get(tuple(sorted(leaves))) or None)
        if out[-1] is None:
            out.pop()
            out.extend(leaves)
    return out


def classify(leaves, is_hub, is_element=False):
    """归类:元素部件 > 枢纽字 > 跨系 > 单系(返回属性名)。"""
    if is_element:
        return "元素"
    if is_hub:
        return "枢纽"
    attrs = {attr_of(leaf) for leaf in leaves}
    return attrs.pop() if len(attrs) == 1 else "跨系"


def hub_tier(leaves):
    """枢纽字阶数 = 元素个数(炎=2 阶,焱=3 阶,燚=4 阶)。"""
    return len(leaves)


def merge_readings(dictionary, game_chars):
    """字典拼音释义 + 游戏字表覆盖:字表已拍板的字以字表为准(字典对生僻叠字有脏数据)。"""
    merged = dict(dictionary)
    for entry in game_chars:
        if entry.get("pinyin"):
            merged[entry["id"]] = (entry["pinyin"], entry.get("gloss", ""))
    return merged


def downstream_index(chars, index):
    """{部件: [以它为一步配方部件的字]};只统计传入字集内部的下游。"""
    down = defaultdict(list)
    pool = set(chars)
    for char in chars:
        for part in set(split_once(char, index) or []):
            if part in pool and part != char:
                down[part].append(char)
    return down


# ---- 报表 ----

def _level_label(char):
    return {1: "一级", 2: "二级", 0: "人名高频" if char in NAME_COMMON else "生僻"}[gb_level(char)]


def _cost_label(leaves):
    cost = element_cost(leaves)
    return "·".join(f"{a}×{n}" if n > 1 else a
                    for a, n in sorted(cost.items(), key=lambda kv: ATTR_ORDER[kv[0]]))


def _known(records):
    """玩家认得的字数:GB2312 一二级 + 人名高频档(5.4)。"""
    return sum(1 for r in records
               if gb_level(r["char"]) in (1, 2) or r["char"] in NAME_COMMON)


def _sort_key(rec):
    return ({1: 0, 2: 1, 0: 2 if rec["char"] not in NAME_COMMON else 1}[gb_level(rec["char"])],
            len(rec["leaves"]), ord(rec["char"]))


_HEAD = ("| 字 | 拼音 | 释义 | 一步配方 | 元素成本 | 常用度 | 组字数(全库/表内) | 选用 | 备注 |\n"
         "|---|---|---|---|---|---|---|---|---|")

_HUB_HEAD = ("| 字 | 拼音 | 释义 | 一步配方 | 元素成本 | 常用度 | 组字数(全库/表内) | 表内下游字 "
             "| 选用 | 备注 |\n|---|---|---|---|---|---|---|---|---|---|")


def _row(rec, extra=None):
    flags = []
    if rec["in_game"]:
        flags.append("**已在字表**")
    if rec["attrs_label"]:
        flags.append(rec["attrs_label"])
    return (f"| {rec['char']} | {rec['pinyin']} | {rec['gloss']} | {' + '.join(rec['recipe'])} "
            f"| {_cost_label(rec['leaves'])} | {_level_label(rec['char'])} "
            f"| {rec['down_all']} / {rec['down_inner']} "
            + (f"| {extra} " if extra is not None else "")
            + f"| | {';'.join(flags)} |")


def _table(records, hub=False):
    rows = sorted(records, key=_sort_key)
    if hub:
        return "\n".join([_HUB_HEAD] + [_row(r, "".join(r["down_chars"])) for r in rows])
    return "\n".join([_HEAD] + [_row(r) for r in rows])


def build_report(records, today):
    """分类字表 markdown:枢纽字 → 六系单系 → 跨系。"""
    hubs = [r for r in records if r["group"] == "枢纽"]
    cross = [r for r in records if r["group"] == "跨系"]
    single = {a: [r for r in records if r["group"] == a] for a in "火木水金土心"}
    elements = [r for r in records if r["group"] == "元素"]

    out = [f"""# 枢纽字体系可用字表(方案 A)

> 状态:待筛选 | 生成:{today},`tools/pipeline/report_hub_chars.py`(cjkvi-ids + 新华字典拼音/释义,发行前需换有授权字典源)
> 口径:`docs/superpowers/specs/2026-07-30-枢纽字体系与拆解经济-design.md`。部件池只放**五行元素 + 枢纽字**,
> 所以本表只收**纯元素可达字**——一路拆到底(只拆上下左右)叶子全为五行元素部件的字,
> 共 **{len(records)}** 个(含 {len(elements)} 个元素部件本身)。这与全量字体系(方案 B)的
> 七张卡池候选筛选表是两套并列口径:那边允许「咅」「敖」这类声旁入池,本表不允许。
> 已剔除 CJK 基本区外的字(多数设备无字形,渲染不了);配方里的扩展区部件已换成同叶子的基本区字(垚 = 圭 + 土)。
> ⚠️ 编辑本表请保存为 UTF-8。

## 怎么读

- **一步配方**:该字的直接部件(IDS 一级),天然就是设计文档 D2 要的递进式(焚 = 林 + 火,不是 木+木+火)。
- **元素成本**:一路拆到底的元素总账,按属性合并(氵/水/冫 同计为水)。
- **组字数(全库/表内)**:该字被多少个字用作一步配方部件。**全库** = 全 CJK 基本区(含繁体、生僻);
  **表内** = 只算本表这 {len(records)} 个纯元素可达字。差值就是设计文档 D3 说的「假希望」——
  戔 全库 23 个下游但全是繁体(淺錢賤踐),简体不可达。
- **常用度**:GB2312 一级/二级;「人名高频」= 焱燚淼鑫垚惢 这类不在 GB 内但玩家实际认得的字(5.4)。
- 拼音/释义空白的是新华字典未收的字(繁体与生僻叠字),这批本来也基本不该入池;已在 `chars.json`
  的字直接采用游戏内已拍板的拼音释义。
- 「选用」列填 `✅` 入池,留空不入,拿不准填 `?`。**稀有度不在本表**——D7 已把稀有度重定义为
  「获取难度 × 字义威力感」,由主设计者拍板,不再由结构机械打分。

## 分类规则

一个字只出现一次,优先级 **元素部件 > 枢纽字 > 跨系 > 单系**:

1. **元素部件** = 六系本体与变体(火灬 / 水氵冫 / 木艹竹 / 金钅刂刀戈 / 土山石 / 心忄),拆合的终点。
2. **枢纽字** = 表内至少有一个下游字(即它能当别的字的部件)。这是「可入池」的实际判据。
3. **跨系** = 元素成本含 ≥2 个属性。
4. **单系** = 六系各自的纯本系字,只做成品卡,不当部件。

## 总览

「字数」是结构上可达的全量;「玩家认得」= GB2312 一二级 + 人名高频档(5.4),才是实际能上卡的量。

| 分类 | 字数 | 其中玩家认得 |
|---|---|---|
| 元素部件 | {len(elements)} | {_known(elements)} |
| 枢纽字 | {len(hubs)} | {_known(hubs)} |
""" + "\n".join(f"| {a}系 | {len(single[a])} | {_known(single[a])} |" for a in "火木水金土心") + f"""
| 跨系 | {len(cross)} | {_known(cross)} |
| **合计** | **{len(records)}** | **{_known(records)}** |

## 零 · 元素部件({len(elements)} 个)

拆合的终点,不是卡而是资源。同系变体之间是否互通(火↔灬、水↔氵↔冫、金↔钅↔刂↔刀↔戈)仍待拍板。
"""]
    out.append(_table(elements))
    out.append(f"\n## 一 · 枢纽字({len(hubs)} 个)\n")
    out.append("能当部件的字。单系枢纽即 D2 的升阶链;跨系枢纽是被别的字吃进去的混合字。\n")
    for attr in "火木水金土心":
        group = [r for r in hubs if r["group_detail"] == attr]
        if not group:
            continue
        out.append(f"\n### {attr}系枢纽({len(group)} 个)\n")
        out.append(_table(group, hub=True))
    cross_hubs = [r for r in hubs if r["group_detail"] == "跨系"]
    if cross_hubs:
        out.append(f"\n### 跨系枢纽({len(cross_hubs)} 个)\n")
        out.append(_table(cross_hubs, hub=True))

    # ---- 六系 ----
    for i, attr in enumerate("火木水金土心", start=2):
        num = "一二三四五六七八"[i - 1]
        out.append(f"\n## {num} · {attr}系({len(single[attr])} 个)\n")
        out.append("纯本系字里**没有下游**的那些——只能当成品卡打出去,不能再当部件。"
                   f"本系能当部件的已上移到枢纽字节。\n")
        out.append(_table(single[attr]) if single[attr] else "(无)")

    # ---- 跨系 ----
    out.append(f"\n## 八 · 跨系字({len(cross)} 个)\n")
    out.append("按五行关系分节。相生对配方自带效果 ×3(wuxing-reference §乘数),"
               "相克对是第 6 章组合技候选。\n")
    by_pair = defaultdict(list)
    for rec in cross:
        by_pair[rec["pair"]].append(rec)
    order = ([("木", "火"), ("火", "土"), ("土", "金"), ("金", "水"), ("水", "木")]
             + [("木", "土"), ("土", "水"), ("水", "火"), ("火", "金"), ("金", "木")]
             + [("心", x) for x in "木火土金水"])
    for pair in order:
        key = tuple(sorted(pair, key=ATTR_ORDER.get))
        group = by_pair.pop(key, [])
        if group:
            out.append(f"\n### {relation_label(pair)}({len(group)} 个)\n")
            out.append(_table(group))
    rest = [r for group in by_pair.values() for r in group]
    if rest:
        out.append(f"\n### 三属性及以上({len(rest)} 个)\n")
        out.append(_table(rest))
    out.append("")
    return "\n".join(out)


def collect(entries, index, readings, in_game):
    """全 CJK 基本区 → 纯元素可达字记录表(含分类、配方、下游统计)。"""
    basic = [e["char"] for e in entries
             if len(e["char"]) == 1 and is_displayable(e["char"])]
    pure = {}
    for char in basic:
        leaves = expand_to_elements(char, index)
        if is_pure(leaves):
            pure[char] = leaves

    canon = canon_map(pure)
    down_inner = downstream_index(list(pure), index)
    down_all = defaultdict(int)
    for char in basic:
        for part in set(split_once(char, index) or []):
            down_all[part] += 1

    records = []
    for char, leaves in pure.items():
        attrs = sorted({attr_of(leaf) for leaf in leaves}, key=ATTR_ORDER.get)
        is_element = attr_of(char) is not None
        is_hub = bool(down_inner.get(char))
        group = classify(leaves, is_hub, is_element)
        pinyin, gloss = readings.get(char, ("—", ""))
        records.append({
            "char": char,
            "leaves": leaves,
            # 元素部件是拆合的终点,不给配方(IDS 里的 火=八+人 不是游戏语义)
            "recipe": (["(元素,不可拆)"] if is_element
                       else normalize_parts(split_once(char, index) or [char], index, canon)),
            "pinyin": pinyin,
            "gloss": gloss,
            "attrs": attrs,
            "pair": tuple(attrs),
            "group": group,
            "group_detail": attrs[0] if len(attrs) == 1 else "跨系",
            "down_all": down_all.get(char, 0),
            "down_inner": len(down_inner.get(char, [])),
            "down_chars": sorted(down_inner.get(char, [])),
            "in_game": char in in_game,
            "attrs_label": ("/".join(attrs) if len(attrs) > 1
                            else f"{hub_tier(leaves)} 阶" if is_hub and len(leaves) > 1 else ""),
        })
    return records


def main():
    sys.setrecursionlimit(10000)
    entries = parse_ids_text((ROOT / "data/raw/ids.txt").read_text(encoding="utf-8"))
    index = build_index(entries)
    chars_json = (ROOT.parent.parent
                  / "Brushblade/Assets/StreamingAssets/config/chars.json")
    game_chars = json.load(open(chars_json))["chars"]
    readings = merge_readings(readings_map(json.load(open(ROOT / "data/raw/xinhua_word.json"))),
                              game_chars)
    readings.update(EXTRA_READINGS)

    records = collect(entries, index, readings, {c["id"] for c in game_chars})
    text = build_report(records, datetime.date.today().isoformat())
    path = ROOT.parent.parent / "docs/design/字选型/枢纽字体系可用字表.md"
    path.write_text(text)
    print("写入", path.name, f"({len(records)} 字,{len(text.splitlines())} 行)")


if __name__ == "__main__":
    main()
