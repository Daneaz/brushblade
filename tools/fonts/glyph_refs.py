#!/usr/bin/env python3
"""字形底稿:把字怪用到的汉字渲染成居中等大的 SVG,作为出图 AI 的 ControlNet / 参考图输入。

为什么需要这一步:图像模型画汉字会画歪、缺笔、造出不存在的字,而《敌人形象关键词包》
的「字为骨」方案全靠字形准确。所以字形由字体供给,模型只负责在底稿上生长墨肉。

用法:
    python3 tools/fonts/glyph_refs.py            # 全量出稿 + 总览图 + manifest
    python3 tools/fonts/glyph_refs.py --char 焦   # 只看一个字

前置: tools/fonts/raw/NotoSerifSC[wght].ttf(google/fonts, OFL)。
产出: docs/design/glyph-refs/(入 git —— 它是设计输入,别的 agent 要直接取用)。
"""
import argparse
import json
import re
import shutil
import subprocess
from dataclasses import dataclass
from pathlib import Path

from fontTools.pens.areaPen import AreaPen
from fontTools.pens.boundsPen import BoundsPen
from fontTools.pens.svgPathPen import SVGPathPen
from fontTools.pens.transformPen import TransformPen
from fontTools.svgLib.path import parse_path
from fontTools.ttLib import TTFont

ROOT = Path(__file__).resolve().parents[2]
RAW_FONT = Path(__file__).parent / "raw" / "NotoSerifSC[wght].ttf"
OUT_DIR = ROOT / "docs/design/glyph-refs"
SVG_DIR = OUT_DIR / "svg"   # 矢量底稿(给 Claude Design:它读 path 数据)
PNG_DIR = OUT_DIR / "png"   # 位图底稿(给只吃位图的出图工具/ControlNet)

CANVAS = 512        # 画布边长(§2 交付规范:512×512)
MARGIN = 0.10       # 四周留白 10% —— 受击位移 ±11px 不出框(§2 构图约束 3)
MINION_WEIGHT = 500 # 杂兵字重
BOSS_WEIGHT = 800   # Boss 更重:压迫感从字形本身就开始(§6.0)


@dataclass(frozen=True)
class Job:
    """一个字形任务。owner = 哪只怪 / 哪只 Boss;phase 仅 Boss 有。"""
    kind: str      # minion | boss
    owner: str
    char: str
    slug: str      # 文件名用的 ASCII id
    phase: int = 0
    note: str = ""


# 杂兵 9 只(《敌人形象关键词包》§5)
_MINIONS = [
    Job("minion", "错字鬼", "错", "cuozigui", note="炮灰教学首怪,笔画松散欲散架"),
    Job("minion", "缺笔妖", "缺", "quebiyao", note="右半缺失,断口渗墨;L4=补全笔画"),
    Job("minion", "标点小妖", "、", "biaodianxiaoyao", note="顿号化妖,体型小一号"),
    Job("minion", "叠字怪", "林", "dieziguai", note="中缝可分离,供分裂动效"),
    Job("minion", "夯土妖", "夯", "hangtuyao", note="敦实如界碑,下盘极稳"),
    Job("minion", "通假字", "假", "tongjiazi", note="不上属性色;L4=面具"),
    Job("minion", "生僻字", "龘", "shengpizi", note="笔画繁复糊成墨块;L4=墨雾"),
    Job("minion", "墨渍", "污", "mozi", note="洇开的墨渍,松弛无骨"),
    Job("minion", "焦痕", "焦", "jiaohen", note="烧焦纸质;L4=裂缝火芯"),
]

# Boss 3 只 × 4 阶段(§6)。「倒」「海」两只 Boss 共用 → 底稿去重后 10 张
_BOSSES = [
    ("排山倒海", "paishandaohai", [("排", "金"), ("山", "土"), ("倒", "木"), ("海", "水")]),
    ("翻江倒海", "fanjiangdaohai", [("翻", "木"), ("江", "水"), ("倒", "木"), ("海", "水")]),
    ("雷霆万钧", "leitingwanjun", [("雷", "金"), ("霆", "金"), ("万", "心"), ("钧", "金")]),
]

_SHARED = {"倒": "paishandaohai", "海": "paishandaohai"}  # 共用阶段归属首只 Boss


def _boss_jobs():
    jobs = []
    for name, slug, phases in _BOSSES:
        for i, (char, element) in enumerate(phases, start=1):
            jobs.append(Job("boss", name, char, slug, phase=i, note=f"阶段{i}·{element}"))
    return jobs


JOBS = _MINIONS + _boss_jobs()


@dataclass(frozen=True)
class Task:
    """去重后的实际渲染任务(一个字一张稿)。"""
    kind: str
    char: str
    filename: str
    weight: int
    owners: tuple
    note: str = ""


def render_plan():
    """JOBS → 去重后的渲染任务。共用阶段(倒/海)只出一次,归属首只 Boss。"""
    seen = {}
    order = []
    for job in JOBS:
        if job.kind == "boss":
            slug = _SHARED.get(job.char, job.slug)
            key = ("boss", job.char)
            filename = f"boss_{slug}_{job.phase}_{_ascii_id(job.char)}.svg"
            weight = BOSS_WEIGHT
        else:
            key = ("minion", job.char)
            filename = f"enemy_{job.slug}.svg"
            weight = MINION_WEIGHT
        if key in seen:  # 共用阶段:并进 owners,底稿不重复出
            prev = seen[key]
            seen[key] = Task(prev.kind, prev.char, prev.filename, prev.weight,
                             prev.owners + (job.owner,), prev.note)
            continue
        seen[key] = Task(job.kind, job.char, filename, weight, (job.owner,), job.note)
        order.append(key)
    return [seen[k] for k in order]


def _ascii_id(char: str) -> str:
    """字 → 稳定的 ASCII 片段(码位十六进制);文件名必须跨平台安全。"""
    return f"u{ord(char):04x}"


_FONT_CACHE = {}


def _font(weight: int) -> TTFont:
    if weight not in _FONT_CACHE:
        _FONT_CACHE[weight] = TTFont(RAW_FONT)
    return _FONT_CACHE[weight]


def font_cmap():
    return _font(MINION_WEIGHT).getBestCmap()


def render_svg(char: str, weight: int = MINION_WEIGHT) -> str:
    """渲染一个字:autofit 到安全框内、居中、y 轴翻正,输出独立 SVG。

    autofit 是必需的 —— 「、」和「龘」的字身差着数量级,不归一化底稿就没法用。"""
    font = _font(weight)
    glyph_set = font.getGlyphSet(location={"wght": weight})
    cmap = font.getBestCmap()
    if ord(char) not in cmap:
        raise ValueError(f"字体里没有「{char}」(U+{ord(char):04X})")
    glyph = glyph_set[cmap[ord(char)]]

    bounds = BoundsPen(glyph_set)
    glyph.draw(bounds)
    if bounds.bounds is None:
        raise ValueError(f"「{char}」没有可见轮廓")
    x0, y0, x1, y1 = bounds.bounds

    target = CANVAS * (1 - 2 * MARGIN)
    scale = target / max(x1 - x0, y1 - y0)
    # 字体坐标 y 向上、SVG y 向下 → scale(s, -s);平移让字身中心落在画布中心
    tx = CANVAS / 2 - scale * (x0 + x1) / 2
    ty = CANVAS / 2 + scale * (y0 + y1) / 2

    svg_pen = SVGPathPen(glyph_set)
    glyph.draw(TransformPen(svg_pen, (scale, 0, 0, -scale, tx, ty)))
    path = svg_pen.getCommands()

    return (
        f'<svg xmlns="http://www.w3.org/2000/svg" width="{CANVAS}" height="{CANVAS}" '
        f'viewBox="0 0 {CANVAS} {CANVAS}">\n'
        f'  <title>{char}</title>\n'
        f'  <path fill="#111622" d="{path}"/>\n'
        f'</svg>\n'
    )


@dataclass(frozen=True)
class Box:
    x0: float
    y0: float
    x1: float
    y1: float

    @property
    def width(self):
        return self.x1 - self.x0

    @property
    def height(self):
        return self.y1 - self.y0

    @property
    def cx(self):
        return (self.x0 + self.x1) / 2

    @property
    def cy(self):
        return (self.y0 + self.y1) / 2


def _path_data(svg: str) -> str:
    match = re.search(r' d="([^"]+)"', svg)
    if not match:
        raise ValueError("SVG 里没有 path")
    return match.group(1)


def path_bounds(svg: str) -> Box:
    """从 SVG 文本反解真实轮廓边界(含曲线极值,不是控制点包围盒)。"""
    pen = BoundsPen(None)
    parse_path(_path_data(svg), pen)
    return Box(*pen.bounds)


def path_area(svg: str) -> float:
    """轮廓填充面积 —— 衡量笔画粗细(字重)的正确度量,不受坐标位数影响。"""
    pen = AreaPen(None)
    parse_path(_path_data(svg), pen)
    return abs(pen.value)


def contact_sheet(tasks, columns=5) -> str:
    """总览图:全部底稿排成网格,一眼看全,便于人工审字形。"""
    cell = 180
    pad = 14
    label = 22
    rows = (len(tasks) + columns - 1) // columns
    width = columns * cell + pad * 2
    height = rows * (cell + label) + pad * 2

    parts = [
        f'<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" '
        f'viewBox="0 0 {width} {height}">',
        f'  <rect width="{width}" height="{height}" fill="#f6f1e7"/>',
    ]
    for i, task in enumerate(tasks):
        col, row = i % columns, i // columns
        ox = pad + col * cell
        oy = pad + row * (cell + label)
        inner = re.search(r' d="([^"]+)"', render_svg(task.char, task.weight)).group(1)
        s = cell / CANVAS
        tone = "#c53637" if task.kind == "boss" else "#111622"
        parts.append(f'  <g transform="translate({ox},{oy}) scale({s:.5f})">')
        parts.append(f'    <path fill="{tone}" d="{inner}"/>')
        parts.append("  </g>")
        parts.append(
            f'  <text x="{ox + cell / 2:.0f}" y="{oy + cell + 14}" text-anchor="middle" '
            f'font-family="PingFang SC,sans-serif" font-size="11" fill="#5d6470">'
            f'{"/".join(task.owners)}</text>')
    parts.append("</svg>\n")
    return "\n".join(parts)


def _export_png(tasks):
    """SVG → PNG(rsvg-convert)。SVG 是矢量主产物,PNG 供只吃位图的出图工具。"""
    if shutil.which("rsvg-convert") is None:
        print("跳过 PNG:未找到 rsvg-convert(macOS: brew install librsvg)")
        return
    PNG_DIR.mkdir(parents=True, exist_ok=True)
    for task in tasks:
        subprocess.run(
            ["rsvg-convert", "-w", str(CANVAS), "-h", str(CANVAS),
             str(SVG_DIR / task.filename), "-o", str(PNG_DIR / task.filename.replace(".svg", ".png"))],
            check=True)
    print(f"PNG {len(tasks)} 张 → {PNG_DIR.relative_to(ROOT)}")


def main():
    parser = argparse.ArgumentParser(description="渲染字怪字形底稿")
    parser.add_argument("--char", help="只渲染单个字,打到 stdout")
    parser.add_argument("--weight", type=int, default=MINION_WEIGHT)
    parser.add_argument("--png", action="store_true",
                        help="同时出 PNG(多数出图工具只吃位图;需 rsvg-convert)")
    args = parser.parse_args()

    if args.char:
        print(render_svg(args.char, args.weight))
        return

    tasks = render_plan()
    SVG_DIR.mkdir(parents=True, exist_ok=True)
    manifest = []
    for task in tasks:
        (SVG_DIR / task.filename).write_text(render_svg(task.char, task.weight), encoding="utf-8")
        manifest.append({
            "file": task.filename,
            "char": task.char,
            "kind": task.kind,
            "owners": list(task.owners),
            "weight": task.weight,
            "note": task.note,
        })
    if args.png:
        _export_png(tasks)

    (OUT_DIR / "_contact-sheet.svg").write_text(contact_sheet(tasks), encoding="utf-8")
    (OUT_DIR / "manifest.json").write_text(
        json.dumps({"canvas": CANVAS, "margin": MARGIN, "glyphs": manifest},
                   ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

    minions = sum(1 for t in tasks if t.kind == "minion")
    bosses = len(tasks) - minions
    print(f"底稿 {len(tasks)} 张(杂兵 {minions} + Boss {bosses})→ {OUT_DIR.relative_to(ROOT)}")
    print("总览:_contact-sheet.svg   索引:manifest.json")


if __name__ == "__main__":
    main()
