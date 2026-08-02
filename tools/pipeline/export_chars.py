"""字表导出:配方(IDS 一级拆解 + 叠字人工兜底)+ 数值 → chars.json。

配方口径(见 docs/design/字选型/技能机制详表.md 1.5):
- 五系叠字链 15 条**人工写死**,不走 IDS —— IDS 会把 燚 拆成 炏+炏,污染叠字链。
- 其余字用 decompose.split_once 取一级拆解。
- 配方一律「部件在前、低阶字在后」。
"""
import json
from pathlib import Path

from decompose import build_index, split_once
from extract_values import extract
from fetch_ids import parse_ids_text
from filter_chars import attr_of

ELEMENTS = ["金", "木", "水", "火", "土"]
_ELEMENT_NAME = {"金": "Metal", "木": "Wood", "水": "Water", "火": "Fire", "土": "Earth"}

STACK_RECIPES = {
    "林": ["木", "木"], "森": ["木", "林"], "𣛧": ["木", "森"],
    "沝": ["水", "水"], "淼": ["水", "沝"], "㵘": ["水", "淼"],
    "炎": ["火", "火"], "焱": ["火", "炎"], "燚": ["火", "焱"],
    "鍂": ["金", "金"], "鑫": ["金", "鍂"], "𨰻": ["金", "鑫"],
    "圭": ["土", "土"], "垚": ["土", "圭"], "㙓": ["土", "垚"],
}


def recipe_for(char, index):
    """叠字取人工表,其余取 IDS 一级拆解;不可拆返回 []。"""
    if char in STACK_RECIPES:
        return list(STACK_RECIPES[char])
    return split_once(char, index) or []


def build_chars(ids_text, values):
    """values: {字: {element, rarity, effects, pinyin?, gloss?}} → {"chars": [...]}"""
    index = build_index(parse_ids_text(ids_text))
    entries = [{"id": e, "element": _ELEMENT_NAME[e]} for e in ELEMENTS]

    components = set()
    for char, spec in values.items():
        recipe = recipe_for(char, index)
        entry = {"id": char, "rarity": spec["rarity"]}
        if spec.get("element"):
            entry["element"] = spec["element"]
        if recipe:
            entry["recipe"] = recipe
            for part in recipe:
                if part not in ELEMENTS and not attr_of(part):
                    components.add(part)
        for optional in ("pinyin", "gloss"):
            if spec.get(optional):
                entry[optional] = spec[optional]
        if spec.get("effects"):
            entry["effects"] = spec["effects"]
        entries.append(entry)

    for part in sorted(components):
        if part not in values:
            entries.append({"id": part})

    return {"chars": entries}


def main():
    here = Path(__file__).parent
    ids_text = (here / "data" / "raw" / "ids.txt").read_text(encoding="utf-8")
    spec = here.parent.parent / "docs/design/字选型/技能机制详表.md"
    values = extract(spec.read_text(encoding="utf-8"))
    out = build_chars(ids_text, values)
    dest = here.parent.parent / "Brushblade/Assets/StreamingAssets/config/chars.json"
    dest.write_text(json.dumps(out, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"已写入 {dest}: {len(out['chars'])} 条(字 {len(values)})")


if __name__ == "__main__":
    main()
