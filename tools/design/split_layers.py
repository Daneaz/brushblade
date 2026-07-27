#!/usr/bin/env python3
"""合并稿拆层:Claude Design 导出的单文件三层稿 → 工程侧要的分层 SVG。

设计侧把 body/face/wisp 合并进一个 SVG,靠 defs 里的 id 前缀(L0_/L1_/L2_)区分归属。
工程侧需要分层资产:三层各跑各的动效周期,合成一坨就没法分别驱动了(见 MobView)。

用法:
    python3 tools/design/split_layers.py            # 拆 svg-done/ → mobs/svg/
    python3 tools/design/split_layers.py --check    # 只报告,不写文件

输入: docs/design/glyph-refs/svg-done/*.svg
产出: tools/design/mobs/svg/<prefix>_<layer>.svg
"""
import argparse
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SRC_DIR = ROOT / "docs/design/glyph-refs/svg-done"
OUT_DIR = ROOT / "tools/design/mobs/svg"

SVG_NS = "http://www.w3.org/2000/svg"
LAYER_NAMES = ("body", "face", "wisp", "state")  # L0→body, L1→face, L2→wisp, L3→state

# 非绘制元素:不计入可见元素,也不参与层归属
SKIP_TAGS = {"defs", "title", "desc", "metadata", "style"}

_REF_RE = re.compile(r"url\(#L(\d+)_")

ET.register_namespace("", SVG_NS)


def _local(tag: str) -> str:
    return tag.split("}", 1)[-1]


def _layer_of(element) -> int | None:
    """元素属于哪一层:递归找它(及子孙)引用的第一个 L{n}_ id。"""
    for value in element.attrib.values():
        match = _REF_RE.search(value)
        if match:
            return int(match.group(1))
    for child in element:
        found = _layer_of(child)
        if found is not None:
            return found
    return None


def count_visible(svg: str) -> int:
    """可见元素数(根级往下的所有绘制节点),用于校验拆分不丢东西。"""
    root = ET.fromstring(svg)
    total = 0
    for child in root:
        if _local(child.tag) in SKIP_TAGS:
            continue
        total += 1 + sum(1 for _ in child.iter()) - 1
    return total


def split(svg: str):
    """拆成 [(层名, 该层 SVG), ...],按层序返回。defs 整份照抄给每一层
    (未被引用的 defs 不影响渲染,照抄免得漏引用)。"""
    root = ET.fromstring(svg)
    header = {k: v for k, v in root.attrib.items()}
    defs = [child for child in root if _local(child.tag) == "defs"]

    buckets: dict[int, list] = {}
    current = 0
    for child in root:
        if _local(child.tag) in SKIP_TAGS:
            continue
        layer = _layer_of(child)
        if layer is None:
            layer = current  # 无引用的元素跟随上一个已判定的层(层是按序排布的)
        current = layer
        buckets.setdefault(layer, []).append(child)

    out = []
    for index in sorted(buckets):
        if index >= len(LAYER_NAMES):
            raise ValueError(f"未知层号 L{index}")
        new_root = ET.Element(f"{{{SVG_NS}}}svg", header)
        for node in defs:
            new_root.append(node)
        for node in buckets[index]:
            new_root.append(node)
        body = ET.tostring(new_root, encoding="unicode")
        out.append((LAYER_NAMES[index], body if body.endswith("\n") else body + "\n"))
    return out


def output_name(source_name: str, layer: str) -> str:
    """mob_cuozigui.svg + body → enemy_cuozigui_body.svg(对齐 MobAssets 的命名)。"""
    stem = Path(source_name).stem
    if stem.startswith("mob_"):
        stem = "enemy_" + stem[len("mob_"):]
    return f"{stem}_{layer}.svg"


def main():
    parser = argparse.ArgumentParser(description="合并稿拆层")
    parser.add_argument("--check", action="store_true", help="只报告,不写文件")
    args = parser.parse_args()

    sources = sorted(SRC_DIR.glob("*.svg"))
    if not sources:
        print(f"没有找到合并稿:{SRC_DIR}", file=sys.stderr)
        return 1

    if not args.check:
        OUT_DIR.mkdir(parents=True, exist_ok=True)
    written = 0
    for source in sources:
        layers = split(source.read_text(encoding="utf-8"))
        for layer, content in layers:
            name = output_name(source.name, layer)
            if not args.check:
                (OUT_DIR / name).write_text(content, encoding="utf-8")
            written += 1
        print(f"  {source.name} → {len(layers)} 层")
    print(f"{'待拆' if args.check else '已拆'} {len(sources)} 只 / {written} 层"
          f"{'' if args.check else f' → {OUT_DIR.relative_to(ROOT)}'}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
