#!/usr/bin/env python3
"""子集化 Noto 字体:收集项目全部用字 → 生成子集 TTF 放进 Unity Resources。

用法: python3 tools/fonts/subset_fonts.py
前置: tools/fonts/raw/ 下已有 NotoSerifSC[wght].ttf / NotoSansSC[wght].ttf(google/fonts, OFL)。
"""
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CONFIG = ROOT / "Brushblade/Assets/StreamingAssets/config"
CODE_DIRS = [ROOT / "Brushblade/Assets/_Project"]
OUT_DIR = ROOT / "Brushblade/Assets/_Project/Presentation/Fonts/Resources"
RAW = Path(__file__).parent / "raw"

# 基线字符:ASCII + 常用 CJK 标点/符号 + 拼音带调字母(CharInfo/GlyphTile 用)
BASE = (
    "".join(chr(c) for c in range(0x20, 0x7F))
    + "、。·「」『』【】《》…—×✓◀▶♥◆？！：；，（）"
    + "āáǎàēéěèīíǐìōóǒòūúǔùǖǘǚǜü"
)

_STRING_RE = re.compile(r'"(?:[^"\\\n]|\\.)*"')


def json_chars(path: Path) -> set:
    def walk(node, out):
        if isinstance(node, str):
            out.update(node)
        elif isinstance(node, list):
            for x in node:
                walk(x, out)
        elif isinstance(node, dict):
            for k, v in node.items():
                out.update(str(k))
                walk(v, out)
    out: set = set()
    walk(json.loads(path.read_text(encoding="utf-8")), out)
    return out


def code_chars(root: Path) -> set:
    out: set = set()
    for cs in root.rglob("*.cs"):
        for lit in _STRING_RE.findall(cs.read_text(encoding="utf-8")):
            out.update(lit[1:-1])
    return out


def charset() -> set:
    chars = set(BASE)
    for name in ("chars.json", "enemies.json"):
        chars |= json_chars(CONFIG / name)
    for d in CODE_DIRS:
        chars |= code_chars(d)
    return {c for c in chars if c == " " or not c.isspace()}


def main():
    from fontTools import subset
    from fontTools.ttLib import TTFont
    from fontTools.varLib.instancer import instantiateVariableFont

    text = "".join(sorted(charset()))
    (Path(__file__).parent / "charset.txt").write_text(text, encoding="utf-8")
    OUT_DIR.mkdir(parents=True, exist_ok=True)

    jobs = [
        ("NotoSerifSC[wght].ttf", 700, "NotoSerifSC-Subset.ttf"),
        ("NotoSansSC[wght].ttf", 500, "NotoSansSC-Subset.ttf"),
    ]
    for src, weight, out_name in jobs:
        font = TTFont(RAW / src)
        if "fvar" in font:
            instantiateVariableFont(font, {"wght": weight}, inplace=True)
        options = subset.Options(layout_features="*", name_IDs="*")
        subsetter = subset.Subsetter(options)
        subsetter.populate(text=text)
        subsetter.subset(font)
        font.save(OUT_DIR / out_name)
        print(f"{out_name}: {(OUT_DIR / out_name).stat().st_size / 1024:.0f} KB, "
              f"{len(text)} chars")


if __name__ == "__main__":
    main()
