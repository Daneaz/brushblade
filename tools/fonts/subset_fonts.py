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
# 测试代码不扫:测试数据里的字(天干、生僻叠字等)玩家永远看不到,
# 打进字体只是白占体积,还会让「改个测试用例」把字体安全网弄红。
SKIP_DIRS = {"Tests"}
OUT_DIR = ROOT / "Brushblade/Assets/_Project/Presentation/Fonts/Resources"
RAW = Path(__file__).parent / "raw"

# 基线字符:ASCII + 常用 CJK 标点/符号 + 拼音带调字母(CharInfo/GlyphTile 用)
BASE = (
    "".join(chr(c) for c in range(0x20, 0x7F))
    + "、。·「」『』【】《》…—×✓◀▶♥◆？！：；，（）"
    + "āáǎàēéěèīíǐìōóǒòūúǔùǖǘǚǜü"
)

# 四叠字合成:Noto 无字形的叠字用部件字形 2×2 拼合生成(OFL 允许衍生)。
# 四木未编码,用业界惯用 PUA U+E625;四金真身 𨰻 U+28C3B 在 Ext-B,
# UGUI Text 不支持增补平面代理对 → 用 U+E626 作 BMP 显示代理。值 = 部件字。
STACKED = {0xE625: "木", 0xE626: "金"}

_STRING_RE = re.compile(r'"(?:[^"\\\n]|\\.)*"')
_CHAR_RE = re.compile(r"'(?:[^'\\\n]|\\.)'")


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
        if SKIP_DIRS.intersection(cs.relative_to(root).parts[:-1]):
            continue
        text = cs.read_text(encoding="utf-8")
        for lit in _STRING_RE.findall(text):
            out.update(lit[1:-1])
        for lit in _CHAR_RE.findall(text):
            out.update(lit[1:-1])
    return out


def charset() -> set:
    chars = set(BASE)
    for name in ("chars.json", "enemies.json"):
        chars |= json_chars(CONFIG / name)
    for d in CODE_DIRS:
        chars |= code_chars(d)
    return {c for c in chars if c == " " or not c.isspace()}


def add_stacked_glyphs(font):
    """为 STACKED 里的码位合成 2×2 叠字复合字形(部件半缩放,平铺原字形包围盒)。"""
    from fontTools.pens.boundsPen import BoundsPen
    from fontTools.pens.ttGlyphPen import TTGlyphPen

    cmap = font.getBestCmap()
    glyph_set = font.getGlyphSet()
    order = font.getGlyphOrder()
    for code, base_char in STACKED.items():
        if code in cmap:
            continue
        base_name = cmap.get(ord(base_char))
        if base_name is None:
            raise SystemExit(f"叠字部件「{base_char}」不在子集中,无法合成 U+{code:04X}")

        bounds = BoundsPen(glyph_set)
        glyph_set[base_name].draw(bounds)
        x_min, y_min, x_max, y_max = bounds.bounds
        half_w = (x_max - x_min) / 2
        half_h = (y_max - y_min) / 2

        pen = TTGlyphPen(glyph_set)
        for col in (0, 1):
            for row in (0, 1):
                pen.addComponent(base_name, (0.5, 0, 0, 0.5,
                    x_min / 2 + col * half_w, y_min / 2 + row * half_h))

        name = f"uni{code:04X}"
        order.append(name)
        font.setGlyphOrder(order)
        font["glyf"][name] = pen.glyph()
        font["hmtx"][name] = (font["hmtx"][base_name][0], int(x_min))
        if "vmtx" in font:
            font["vmtx"][name] = font["vmtx"][base_name]
        font["maxp"].numGlyphs = len(order)
        for table in font["cmap"].tables:
            if table.isUnicode():
                table.cmap[code] = name


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
        add_stacked_glyphs(font)
        font.save(OUT_DIR / out_name)
        print(f"{out_name}: {(OUT_DIR / out_name).stat().st_size / 1024:.0f} KB, "
              f"{len(text)} chars")


if __name__ == "__main__":
    main()
