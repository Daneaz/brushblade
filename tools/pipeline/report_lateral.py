"""横向组合枢纽字表:哪些字能当部件横向带出别的卡,各自能组出什么。

横向 = 与**别系**部件组合(圭 + 氵 = 洼);同系组合是纵向升阶链(木 + 林 = 森),另节列出。
横向组合能力是枢纽字的全部价值,也是评分表「横向分」的来源。

用法:tools/pipeline$ python3 report_lateral.py
产出:docs/design/字选型/横向组合枢纽字表.md
"""
import datetime
from pathlib import Path

import report_char_scores as S
import report_hub_chars as H

ROOT = Path(__file__).resolve().parent


def combos_of(records):
    """全部记录 → [(记录, 横向组合, 纵向升阶)],按横向组合数降序;没有横向能力的不收。"""
    lateral = S.lateral_index(records)
    vertical = S.vertical_index(records)
    rows = [(rec, sorted(lateral.get(rec["char"], ()), key=lambda kv: kv[1]),
             sorted(vertical.get(rec["char"], ()), key=lambda kv: kv[1]))
            for rec in records if lateral.get(rec["char"])]
    return sorted(rows, key=lambda r: (-len(r[1]), r[0]["char"]))


_HEAD = ("| 字 | 系 | 类型 | 横向组合数 | 横向分 | 能横向组合出的字 | 同系升阶 |\n"
         "|---|---|---|---|---|---|---|")


def _row(rec, lateral, vertical):
    attrs = "/".join(rec["attrs"])
    combos = "、".join(f"{recipe} = **{ch}**" for recipe, ch in lateral)
    ups = "、".join(f"{recipe} = {ch}" for recipe, ch in vertical) or "—"
    return (f"| **{rec['char']}** | {attrs} | {S._type_label(rec)} | {len(lateral)} "
            f"| {S.lateral_score(len(lateral)):g} | {combos} | {ups} |")


def build_report(rows, total, today):
    elements = [r for r in rows if r[0]["group"] == "元素"]
    hubs = [r for r in rows if r[0]["group"] != "元素"]
    out = [f"""# 横向组合枢纽字表

> 生成:{today},`tools/pipeline/report_lateral.py`
> 字源:`字卡评分表.md` 的 {total} 字,其中 **{len(rows)}** 个具备横向组合能力,本表全列。
> ⚠️ 编辑本表请保存为 UTF-8。

## 什么算横向组合

一个字当**部件**用,和**别系**的部件拼出新字,就是一次横向组合:

- 圭(土)→ 木 + 圭 = **桂**、氵 + 圭 = **洼**、圭 + 刂 = **刲**
- 炎(火)→ 氵 + 炎 = **淡**、钅 + 炎 = **锬**

同系组合(火 + 火 = 炎、木 + 林 = 森)**不算横向**——那是纵向的升阶链,单独列在「同系升阶」列里。
两者的区别就是设计文档 D3 的构筑系 / 掠夺系之分:横向能力强的字撑得起一条流派,
只有纵向能力的字(沝 → 淼、鍂 → 鑫)只能自己往上叠。

## 横向分怎么算

| 横向组合数 | 分 |
|---|---|
""" + "\n".join(f"| ≥{floor} | {score} |" for floor, score in S.LATERAL_BANDS[:-1])
           + "\n| 0 | 0 |" + f"""

满分 20,计入评分表总分(现为 135 分制)。

## 一 · 元素部件({len(elements)} 个)

元素部件是万能胶,横向能力天然最强——但它们进部件池,不做成卡(见稀有度表的「🔧 部件·非卡」)。
这一节的价值在于**反过来读**:它告诉你每个元素当部件时能通向哪些成品。
"""]
    out.append(_HEAD)
    out.extend(_row(*r) for r in elements)
    out.append(f"""
## 二 · 枢纽字({len(hubs)} 个)

**这些才是构筑核心**——它们本身是卡,又能当部件带出别的卡。拆一张这样的字,
等于同时拿到「一张成品卡的材料」和「通向另外几张卡的钥匙」。
""")
    out.append(_HEAD)
    out.extend(_row(*r) for r in hubs)
    out.append("")
    return "\n".join(out)


def main():
    by_group = S.build_records()
    records = [r for recs in by_group.values() for r in recs]
    rows = combos_of(records)
    text = build_report(rows, len(records), datetime.date.today().isoformat())
    path = ROOT.parent.parent / "docs/design/字选型/横向组合枢纽字表.md"
    path.write_text(text)
    print("写入", path.name, f"({len(rows)} 个有横向能力,{len(text.splitlines())} 行)")


if __name__ == "__main__":
    main()
