"""字卡稀有度与本期入选建议:评分表的 146 字 → 六档稀有度 + 入选建议,供主设计者终审。

稀有度按 D7 的新定义(获取难度 × 字义威力感)——评分表的五个维度正好是这两样的量化,
所以直接按总分绝对分段切档,跨系可比。元素部件不打分也不给稀有度(它们是资源不是卡)。
**一个字都不删**,不入选的也留在表里。

用法:tools/pipeline$ python3 report_rarity.py
产出:docs/design/字选型/字卡稀有度与入选建议.md
"""
import datetime
from pathlib import Path

import report_char_scores as S
import report_hub_chars as H

ROOT = Path(__file__).resolve().parent

# (档位, 分数下限);满分 135,从高往低匹配
RARITY_BANDS = [("红", 95), ("橙", 80), ("紫", 65), ("蓝", 50), ("绿", 35), ("白", 0)]

SELECT_MARK = {"已在字表": "⭐", "建议入选": "✅", "暂不入": "⬜", "部件·非卡": "🔧"}


def rarity_of(score):
    """总分 → 稀有度档位;元素部件不打分(score 为 None),稀有度对它们没意义。"""
    if score is None:
        return "—"
    return next(name for name, floor in RARITY_BANDS if score >= floor)


def selection_of(rec):
    """本期入选建议:元素部件不是卡;已在字表的必留;其余只看契合度。

    生僻/繁体不作为判据(2026-08-01 拍板)——字形有子集字体兜底,读音释义可以自己写。
    五行本体(金木水火土)虽然 chars.json 里有效果,但它同时是部件,一并归「部件·非卡」。
    """
    if rec.get("group") == "元素":
        return "部件·非卡"  # 五行本体与变体都归这里,即使 chars.json 里已有
    if rec["in_game"]:
        return "已在字表"
    return "建议入选" if rec["tier"] in ("核心", "贴合") else "暂不入"


_HEAD = ("| 字 | 拼音 | 释义 | 一步配方 | 元素成本 | 类型 | 总分 | **稀有度** | 本期入选(建议) "
         "| 终审 | 契合判定 |\n|---|---|---|---|---|---|---|---|---|---|---|")


def _row(rec):
    mark = SELECT_MARK[rec["select"]]
    note = S.AFFINITY_NOTE.get(rec["char"], "")
    judged = (f"{rec['tier_attr']}·{rec['tier']}" if rec["tier"] else "—")
    if note:
        judged += f"({note})"
    score = "—" if rec["score"] is None else f"{rec['score']:g}"
    if rec["in_game"] and rec["select"] == "部件·非卡":
        mark += "(已在字表)"
    return (f"| {rec['char']} | {rec['pinyin']} | {rec['gloss']} | {' + '.join(rec['recipe'])} "
            f"| {H._cost_label(rec['leaves'])} | {S._type_label(rec)} "
            f"| {score} | **{rec['rarity']}** "
            f"| {mark} {rec['select']} | | {judged} |")


def _table(records):
    return "\n".join([_HEAD] + [_row(r) for r in records])


def _is_picked(rec):
    """算进本期卡池的:已在字表 + 建议入选(部件·非卡不是卡,不计)。"""
    return rec["select"] in ("已在字表", "建议入选")


def build_report(by_group, today):
    everything = [r for recs in by_group.values() for r in recs]
    cards = [r for r in everything if S.is_card(r)]
    picked = [r for r in cards if _is_picked(r)]

    def count(records, key):
        return sum(1 for r in records if r["rarity"] == key)

    out = [f"""# 字卡稀有度与本期入选建议

> 生成:{today},`tools/pipeline/report_rarity.py`
> 字源:`字卡评分表.md` 的 {len(everything)} 字,其中 **{len(cards)} 张是卡**(打分、给稀有度),
> 另外 18 个是元素部件(火灬 / 水氵冫 / 木艹竹 / 金钅刂刀戈 / 土山石 / 心忄),
> 它们是部件池里的资源,不打分也不给稀有度,表里照列但分数与稀有度列打横杠。
> **一个字都没删**——不建议入选的也留在表里,你可能有我看不到的理由要用它。
> ⚠️ 编辑本表请保存为 UTF-8。

## 稀有度怎么定的

D7 把稀有度重定义为「**获取难度 × 字义威力感**」,评分表的五维正好是这两样的量化
(成本分 + 跨系分 = 获取难度,契合分 + 相生分 + 横向分 = 威力感),所以直接按总分绝对分段:

| 档位 | 分数区间 | 字数 | 其中建议入选 |
|---|---|---|---|
""" + "\n".join(
        f"| **{name}** | {floor} ~ {RARITY_BANDS[i-1][1] - 0.5 if i else 135} "
        f"| {count(cards, name)} | {count(picked, name)} |"
        for i, (name, floor) in enumerate(RARITY_BANDS)) + f"""

**读这张表要注意两件事**:

1. **分布不是金字塔**,中间两档最厚(紫 {count(cards, '紫')} + 蓝 {count(cards, '蓝')})。
   低分区堆的是字义与本系特性对不上的字——它们分数低是因为契合分吃 0,不是因为难合。
2. **入选池的档位天然偏高**:入选判据就是「契合 ≥ 贴合」,而契合分占 45,
   所以选出来的必然是高分字。要凑成卡池金字塔(白 6~8 / 绿 8~12 / 蓝 8~12 / 紫 5~8 /
   橙 2~4 / 红 1~2),得在这 {len(picked)} 张入选池内部按相对位次重切一遍,
   或者用掉落权重去配平——稀有度标的是**绝对强度**,不是出现频率。这是你的判断,我没有替你改。

## 入选建议怎么给的

| 标记 | 含义 | 判据 |
|---|---|---|
| ⭐ 已在字表 | {sum(1 for r in everything if r['select'] == '已在字表')} 个 | 已经在 `chars.json` 里,不动 |
| ✅ 建议入选 | {sum(1 for r in everything if r['select'] == '建议入选')} 个 | 契合 ≥ 贴合 |
| ⬜ 暂不入 | {sum(1 for r in everything if r['select'] == '暂不入')} 个 | 契合只到沾边,或字义与本系特性对不上 |
| 🔧 部件·非卡 | {sum(1 for r in everything if r['select'] == '部件·非卡')} 个 | 元素本体与变体,只进部件池,不打分也不给稀有度(五行本体 金木水火土 在 `chars.json` 里有效果,标「已在字表」) |

合计建议入选 **{len(picked)}** 字。**生僻与繁体不作为判据**——字形有子集字体兜底
(`tools/fonts/subset_fonts.py` 连四叠字都能拼),读音释义可以自己写,玩家在卡面上认的是字形和效果。
「终审」列留空——你填最终决定,`✅` 收 / `❌` 砍 / `?` 再议。

## 各系入选情况

| 系 | 卡 | 部件 | 建议入选 | 入选的字 |
|---|---|---|---|---|
""" + "\n".join(
        f"| {name if name == '跨系' else name + '系'} | {sum(1 for r in recs if S.is_card(r))} "
        f"| {sum(1 for r in recs if not S.is_card(r))} "
        f"| {sum(1 for r in recs if _is_picked(r))} | "
        + ("".join(r["char"] for r in recs if _is_picked(r)) or "—") + " |"
        for name, recs in by_group.items()) + "\n"]

    for i, (name, recs) in enumerate(by_group.items()):
        title = f"{name}系" if name != "跨系" else "跨系字"
        picked_here = sum(1 for r in recs if _is_picked(r))
        cards_here = sum(1 for r in recs if S.is_card(r))
        out.append(f"\n## {'一二三四五六七'[i]} · {title}"
                   f"({cards_here} 张卡 + {len(recs) - cards_here} 个部件,"
                   f"建议入选 {picked_here})\n")
        out.append(_table(recs))
    out.append("")
    return "\n".join(out)


def main():
    by_group = S.build_records()
    for recs in by_group.values():
        for rec in recs:
            rec["gb"] = H.gb_level(rec["char"])
            rec["rarity"] = rarity_of(rec["score"])
            rec["select"] = selection_of(rec)
        recs.sort(key=lambda r: (r["score"] is None, -(r["score"] or 0), r["char"]))

    text = build_report(by_group, datetime.date.today().isoformat())
    path = ROOT.parent.parent / "docs/design/字选型/字卡稀有度与入选建议.md"
    path.write_text(text)
    total = sum(len(v) for v in by_group.values())
    print("写入", path.name, f"({total} 字,{len(text.splitlines())} 行)")


if __name__ == "__main__":
    main()
