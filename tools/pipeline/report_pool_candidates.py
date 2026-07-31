"""卡池候选筛选表生成(六系通用):candidates.json + 拼音释义 → docs/design/字选型 人工筛选工作表。

用法:tools/pipeline$ python3 report_pool_candidates.py [火 金 木 水 土 心 多属性]
(不带参数则六系加多属性表全量重生成。)
"""
import json
import sys
from pathlib import Path

from enrich_readings import readings_map
from score_chars import (build_combination_graph, char_metrics, score_pool,
                         assign_rarity)

ROOT = Path(__file__).resolve().parent

# 各系:本体/变体部件(与 filter_chars.ATTR_MAP 一致)、已在字表的卡、结构决策提示
ELEMENTS = {
    "金": {
        "parts": ["金", "钅", "刂", "刀", "戈"],
        "in_game": set(),
        "decisions": [
            "**钅旁字群**(钱铁铜锋…一级字 79 个,池子主体):需增加部件「钅」并决定 钅↔金 是否互通(建议不互通,同火系灬)。",
            "**刂/刀/戈 兵器旁**(刺剑戒战…):天然的武器/攻击主题,适合做金系的进攻性格。",
        ],
    },
    "木": {
        "parts": ["木", "艹", "竹"],
        "in_game": {"林"},
        "decisions": [
            "**艹字头字群**(花草药蓝…一级字 128 个):要拍板艹是否算木系部件——收了池子偏「草药」,不收偏「树木」,决定木系性格。",
            "**竹字头**(笔筷简箭…一级 43 个):同上,可做子主题。",
        ],
    },
    "水": {
        "parts": ["水", "氵", "冫"],
        "in_game": set(),
        "decisions": [
            "**氵旁字群**(江河湖海…一级字 211 个,绝对主体):需增加部件「氵」并决定 氵↔水 是否互通。",
            "**冫旁**(冰冷冻凉…一级 20 个):可做「冰霜」子主题,风味极好。",
        ],
    },
    "土": {
        "parts": ["土", "山", "石"],
        "in_game": {"圭", "壁", "堡"},
        "decisions": [
            "**山/石字群**(岩峰崖磊砖碑…):v0.4 已拍板山石属土;筛选时注意土(城墙工事)/山(巍峨)/石(坚硬)三种风味的配比。",
        ],
    },
    "心": {
        "parts": ["心", "忄"],
        "in_game": set(),
        "decisions": [
            "**忄旁字群**(情怕悟恨…一级字 63 个):需增加部件「忄」并决定 忄↔心 是否互通。",
            "**心系中立定位**(3.4):不吃相克也不被克——效果设计宜走控制/辅助/自我强化,与攻击系区分。",
        ],
    },
    "火": {
        "parts": ["火", "灬"],
        "in_game": {"灯", "炎", "烧", "燃", "灼", "炽", "焚", "焱", "燚"},
        "decisions": [
            "**「灬」底字群**(点热照煮…约占常用火字 1/4):需增加部件「灬」,建议 火↔灬 不互通。",
        ],
    },
}

# 当前掉落表(火流派原型;其他系开池时会有各自流派掉落表)
DROPS = {"木", "火", "丁", "尧", "然", "勺", "只", "土"}

# 常见部首(即使不是 GB 单字,玩家也熟悉)
COMMON_RADICALS = set("亻宀艹氵忄扌辶灬冫刂钅礻衤犭饣阝廴彳口日月山石土木火水金心丁一二人厂广户欠少勺包彐廿久")

# 叠字族(两叠/三叠同部件成字):作为配方原料时意味着吃合成字 → 高稀有度
STACK_CHARS = set("林森炎焱炏淼沝垚圭鑫屾砳磊惢昍吅")


def is_displayable(ch):
    """候选主字是否落在 CJK 基本区(U+4E00–9FFF):扩展A/B区多数设备无字形,
    显示为问号/豆腐,游戏字体也渲染不了,直接从筛选表剔除。"""
    return 0x4E00 <= ord(ch) <= 0x9FFF


def gb_level(ch):
    """GB2312 常用度:1=一级常用,2=二级次常用,0=不在 GB2312。"""
    try:
        b = ch.encode("gb2312")
    except UnicodeEncodeError:
        return 0
    return 0 if len(b) != 2 else (1 if 0xB0 <= b[0] <= 0xD7 else 2)


def variant_of(leaves, parts):
    """该字用的是哪一个本系部件(按 parts 顺序取第一个命中);无则 None。"""
    for part in parts:
        if part in leaves:
            return part
    return None


def _exotic(leaf):
    """冷僻部件:玩家难认、也难从其他字拆出。"""
    return gb_level(leaf) == 0 and leaf not in COMMON_RADICALS


def rate_pool(pool, graph):
    """池内打分分档 → {字: (分数, 稀有度)};见 score_chars 模块头。"""
    metrics = {c["char"]: char_metrics(c, graph) for c in pool}
    scored = score_pool(metrics)
    rarity = assign_rarity(scored)
    return {ch: (scored[ch], rarity[ch], *metrics[ch]) for ch in scored}


def _row(cand, element, readings, rated):
    ch = cand["char"]
    pinyin, gloss = readings.get(ch, ("—", ""))
    parts = ELEMENTS[element]["parts"]
    score, rarity, effective, production = rated[ch]
    flags = []
    if ch in ELEMENTS[element]["in_game"]:
        flags.append("**已在字表**")
    exotic = [l for l in cand["leaves"] if _exotic(l)]
    if exotic:
        flags.append("⚠部件冷僻:" + " ".join(exotic))
    partner_drops = [l for l in cand["leaves"] if l in DROPS and l not in parts]
    if partner_drops:
        flags.append("部件可刷:" + " ".join(partner_drops))
    return (f"| {ch} | {pinyin} | {gloss} | {' + '.join(cand['leaves'])} "
            f"| {'/'.join(cand['attrs'])} | {score:.1f} | {effective} | {production} "
            f"| {rarity} | | {';'.join(flags)} |")


_HEAD = ("| 字 | 拼音 | 释义 | 配方(一步合成) | 属性 | 分数 | 有效部件 | 组字数 | 建议稀有度 | 选用 | 备注 |\n"
         "|---|---|---|---|---|---|---|---|---|---|---|")


def build_report(element, candidates, readings, today, graph):
    """单系筛选表 markdown。表A一级字按部件变体分节;表B二级字剔除冷僻部件;表C叠字族。"""
    cfg = ELEMENTS[element]
    parts = cfg["parts"]
    pool = [c for c in candidates if element in c["attrs"] and is_displayable(c["char"])]
    undisplayable = sum(1 for c in candidates
                        if element in c["attrs"] and not is_displayable(c["char"]))
    tier1 = sorted((c for c in pool if gb_level(c["char"]) == 1),
                   key=lambda c: (c["complexity"], c["ids"]))
    tier2_all = [c for c in pool if gb_level(c["char"]) == 2]
    tier2 = sorted((c for c in tier2_all if not any(_exotic(l) for l in c["leaves"])),
                   key=lambda c: (c["complexity"], c["ids"]))
    stacked = [c for c in pool
               if set(c["leaves"]) <= (set(parts) | STACK_CHARS) and gb_level(c["char"]) == 0]
    # 分档只在表里实际列出的字之间做梯度:pool 里大半是不进表的生僻字,
    # 让它们占掉白/绿档的话,表内就只剩高档了
    rated = rate_pool(tier1 + tier2 + stacked, graph)

    debut = element == "火"
    scope = ("本表是首发卡池(19.3)的筛选材料。" if debut
             else f"首发仅火系(19.3);本表为后续版本开{element}系池的预备材料,筛法同火系表。")
    out = [f"""# {element}系卡池候选筛选表(人工筛选{"" if debut else " · 非首发预备"})

> 状态:待筛选 | 生成:{today},`tools/pipeline/report_pool_candidates.py`(cjkvi-ids 递归拆解 + 新华字典拼音/释义,发行前需换有授权字典源)
> 配方为**递归拆解**:只拆上下左右结构,逐级判断——某级新拆出的部件里没有五行部件就回退该级
> (森 → 木+木+木;燥 → 火+品+木,再拆「品」无五行故止步;照 → 昭+灬)。部件超 3 个也回退。
> {scope}
> 已剔除 CJK 基本区外的字 {undisplayable} 个(多数设备无字形,游戏字体渲染不了)。⚠️ 编辑本表请保存为 UTF-8。

## 怎么筛

1. 「选用」列填 `✅` 入池,留空不入,拿不准填 `?`。
2. 「分数」按部件复用价值综合打分(见下),分数越高稀有度越高;「建议稀有度」是分数在本系内的档位,终稿你定。
3. 池规模参考:每系 30~50 字(白 6~8 / 绿 8~12 / 蓝 8~12 / 紫 5~8 / 橙 2~4 / 红 1~2)。

## 分数怎么来的

两个维度各占一半,在本系候选内归一化后相加(0~100):

- **有效部件**:递归配方里能和别的部件组成别的字的部件个数(重复计)。燥 = 火+品+木 → 2,「品」组不出字。
- **组字数**:该字各层级出现过的不同部件(一级 ∪ 递归)各自能组成多少字,求和。焚 一级 林+火、二级 木+木+火 → 林/火/木 三者之和。

「能组成别的字」只算一级组合(噪 = 口+喿,记在「喿」头上不记「品」),统计范围是候选表里的 GB2312 一级常用字。
六档按 白25% 绿25% 蓝20% 紫15% 橙8% 红7% 从高分往低分切,同分同档;各系分别切档
(火系最高分与水系差一截,统一切档火系出不了红卡)。同分块大时实际比例会偏离——
水系有 107 个「氵+X」分数完全相同(氵 的组字数主导,搭档部件只差几个),整块落在同一档,
绿档因此被跳过;金系同理。这是分数的真实反映,档位终稿由你人工调。

## 结构决策(筛选时顺带拍板)

""" + "\n".join(f"- {d}" for d in cfg["decisions"])]

    out.append(f"\n## 表 A · 一级常用字({len(tier1)} 个,池子主体,按部件分节)\n")
    for part in parts:
        group = [c for c in tier1 if variant_of(c["leaves"], parts) == part]
        if not group:
            continue
        out.append(f"\n### 「{part}」部({len(group)} 个)\n")
        out.append(_HEAD)
        out.extend(_row(c, element, readings, rated) for c in group)
    rest = [c for c in tier1 if variant_of(c["leaves"], parts) is None]
    if rest:
        out.append(f"\n### 其他({len(rest)} 个,属性来自多重部件)\n")
        out.append(_HEAD)
        out.extend(_row(c, element, readings, rated) for c in rest)

    skipped = len(tier2_all) - len(tier2)
    out.append(f"\n## 表 B · 二级次常用字({len(tier2)} 个,选做点缀;"
               f"另有 {skipped} 个含冷僻部件的未列,要看说一声)\n")
    out.append(_HEAD)
    out.extend(_row(c, element, readings, rated) for c in tier2)

    out.append(f"\n## 表 C · 叠字族生僻字({len(stacked)} 个,结构奇观,天然高稀有度候选)\n")
    out.append(_HEAD)
    out.extend(_row(c, element, readings, rated) for c in stacked)
    out.append("")
    return "\n".join(out)


# ---- 多属性字(跨属性组合,第 6 章) ----

# 叠字族自身的属性(部件表之外):递归拆解超限回退时会留下叠字部件,如 燚=炏+炏 → 火
STACK_ATTR = {"林": "木", "森": "木", "炎": "火", "焱": "火", "炏": "火",
              "淼": "水", "沝": "水", "垚": "土", "圭": "土",
              "磊": "土", "砳": "土", "屾": "土", "鑫": "金", "惢": "心"}

_ATTR_ORDER = {a: i for i, a in enumerate("木火土金水心")}
_SHENG = {("木", "火"), ("火", "土"), ("土", "金"), ("金", "水"), ("水", "木")}
_KE = {("木", "土"), ("土", "水"), ("水", "火"), ("火", "金"), ("金", "木")}


def extended_attrs(leaves):
    """叶子 → 去重属性(部件表 + 叠字族),按相生环顺序排;识别双属性字用。"""
    from filter_chars import ATTR_MAP
    attrs = {ATTR_MAP.get(l) or STACK_ATTR.get(l) for l in leaves} - {None}
    return sorted(attrs, key=_ATTR_ORDER.get)


def relation_label(pair):
    """属性对 → 生克关系标签:相生「木生火」/相克「木克土」/含心「心+木」。"""
    a, b = pair
    if "心" in pair:
        return "心+" + (b if a == "心" else a)
    for x, y in ((a, b), (b, a)):
        if (x, y) in _SHENG:
            return f"{x}生{y}"
        if (x, y) in _KE:
            return f"{x}克{y}"
    return f"{a}+{b}"


_MULTI_HEAD = ("| 字 | 拼音 | 释义 | 配方(一步合成) | 属性组合 | 常用度 | 分数 | 建议稀有度 | 选用 | 备注 |\n"
               "|---|---|---|---|---|---|---|---|---|---|")

_ALL_IN_GAME = set().union(*(cfg["in_game"] for cfg in ELEMENTS.values()))


def _multi_row(cand, attrs, readings, rated):
    ch = cand["char"]
    pinyin, gloss = readings.get(ch, ("—", ""))
    level = {1: "一级", 2: "二级", 0: "生僻"}[gb_level(ch)]
    flags = []
    if ch in _ALL_IN_GAME:
        flags.append("**已在字表**")
    drops = [l for l in cand["leaves"] if l in DROPS]
    if drops:
        flags.append("部件可刷:" + " ".join(drops))
    score, rarity, _, _ = rated[ch]
    return (f"| {ch} | {pinyin} | {gloss} | {' + '.join(cand['leaves'])} | {'/'.join(attrs)} "
            f"| {level} | {score:.1f} | {rarity} | | {';'.join(flags)} |")


def build_multi_report(candidates, readings, today, graph):
    """多属性字筛选表:按生克关系分节(相生五对→相克五对→含心),常用优先、
    生僻字只收部件全熟悉的。"""
    groups = {}
    tri = []
    skipped = 0
    for cand in candidates:
        attrs = extended_attrs(cand["leaves"])
        if len(attrs) < 2:
            continue
        familiar = all(not _exotic(l) for l in cand["leaves"])
        if not is_displayable(cand["char"]) or (gb_level(cand["char"]) == 0 and not familiar):
            skipped += 1
            continue
        if len(attrs) >= 3:
            tri.append((cand, attrs))
        else:
            groups.setdefault(tuple(attrs), []).append((cand, attrs))

    rated = rate_pool([c for c, _ in tri] + [c for items in groups.values() for c, _ in items],
                      graph)

    def sort_key(item):
        cand, _ = item
        return ({1: 0, 2: 1, 0: 2}[gb_level(cand["char"])], cand["complexity"], cand["ids"])

    out = [f"""# 多属性字候选筛选表(跨属性组合,第 6 章)

> 状态:待筛选 | 生成:{today},`tools/pipeline/report_pool_candidates.py 多属性`
> 双属性字 = 跨属性组合技的天然载体:**相生对配方自带效果 ×3**(wuxing-reference §乘数),
> 相克对是第 6 章组合技(焦土/披坚执锐/破土而出/水来土掩…)的候选字库。
> 建议稀有度仅按结构参考;组合技定位通常 ≥蓝,终稿你定。
> 配方为递归拆解(只拆上下左右,某级无五行部件则回退该级),属性识别含叠字族(燚=炏+炏 → 火);
> 已剔除基本区外无字形字与含冷僻部件的生僻字共 {skipped} 个。
> ⚠️ 编辑本表请保存为 UTF-8。

## 第一部分 · 相生五对(配方 ×3,优先筛)
"""]
    sheng_order = [("木", "火"), ("火", "土"), ("土", "金"), ("金", "水"), ("水", "木")]
    ke_order = [("木", "土"), ("土", "水"), ("水", "火"), ("火", "金"), ("金", "木")]

    def emit(pair_list):
        for pair in pair_list:
            key = tuple(sorted(pair, key=_ATTR_ORDER.get))
            items = sorted(groups.pop(key, []), key=sort_key)
            if not items:
                continue
            out.append(f"\n### {relation_label(pair)}({len(items)} 个)\n")
            out.append(_MULTI_HEAD)
            out.extend(_multi_row(c, a, readings, rated) for c, a in items)

    emit(sheng_order)
    out.append("\n## 第二部分 · 相克五对(组合技候选)\n")
    emit(ke_order)
    out.append("\n## 第三部分 · 含心组合(心系中立,宜控制/辅助向)\n")
    emit([("心", x) for x in "木火土金水"])
    if tri:
        out.append(f"\n## 第四部分 · 三属性及以上({len(tri)} 个)\n")
        out.append(_MULTI_HEAD)
        out.extend(_multi_row(c, a, readings, rated) for c, a in sorted(tri, key=sort_key))
    out.append("")
    return "\n".join(out)


def main(elements):
    candidates = json.load(open(ROOT / "out/candidates.json"))["candidates"]
    readings = readings_map(json.load(open(ROOT / "data/raw/xinhua_word.json")))
    # 组字关系只在 GB2312 一级常用字里统计——玩家认得的字才算"能组成别的字"
    graph = build_combination_graph([c for c in candidates if gb_level(c["char"]) == 1])
    import datetime
    today = datetime.date.today().isoformat()
    docs = ROOT.parent.parent / "docs/design/字选型"
    for element in elements:
        if element == "多属性":
            text = build_multi_report(candidates, readings, today, graph)
            path = docs / "多属性字候选筛选表.md"
        else:
            text = build_report(element, candidates, readings, today, graph)
            path = docs / f"{element}系卡池候选筛选表.md"
        path.write_text(text)
        print("写入", path.name, f"({len(text.splitlines())} 行)")


if __name__ == "__main__":
    main(sys.argv[1:] or ["火", "金", "木", "水", "土", "心", "多属性"])
