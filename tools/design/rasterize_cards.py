#!/usr/bin/env python3
"""字牌边框 SVG → PNG:把 Claude Design 出的稀有度框光栅化进 Unity。

与字怪同一条理由:稿子的材质靠 SVG 渐变与描边堆出来,离线用 librsvg 渲染最保真;
且 9-slice 框做成位图后可直接喂 Unity 的 Image.Type.Sliced。

用法:
    python3 tools/design/rasterize_cards.py
    python3 tools/design/rasterize_cards.py --check

输入: docs/design/card-refs/assets/*.svg
产出: Brushblade/Assets/_Project/Presentation/Cards/Resources/*.png
"""
import argparse
import shutil
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SRC_DIR = ROOT / "docs/design/card-refs/assets"
OUT_DIR = ROOT / "Brushblade/Assets/_Project/Presentation/Cards/Resources"

# 素材基准尺寸(设计稿画布);9-slice 的 border 值以此为准
CANVAS_W, CANVAS_H = 192, 240

TIERS = ("white", "green", "blue", "purple", "orange", "red")
PARTS = ("frame", "glow")


def expected():
    return [f"card_{tier}_{part}" for tier in TIERS for part in PARTS]


def present():
    return [name for name in expected() if (SRC_DIR / f"{name}.svg").exists()]


def main():
    parser = argparse.ArgumentParser(description="字牌边框 SVG → PNG")
    parser.add_argument("--check", action="store_true", help="只报告,不写文件")
    args = parser.parse_args()

    have = present()
    missing_tiers = [t for t in TIERS if not (SRC_DIR / f"card_{t}_frame.svg").exists()]
    print(f"框素材:{len([n for n in have if n.endswith('frame')])}/{len(TIERS)} 档"
          f"(缺 {'、'.join(missing_tiers) if missing_tiers else '无'})")
    print(f"光效层:{len([n for n in have if n.endswith('glow')])} 张")
    if args.check:
        return 0

    if shutil.which("rsvg-convert") is None:
        print("需要 rsvg-convert(macOS: brew install librsvg)", file=sys.stderr)
        return 1

    OUT_DIR.mkdir(parents=True, exist_ok=True)
    for name in have:
        subprocess.run(
            ["rsvg-convert", "-w", str(CANVAS_W), "-h", str(CANVAS_H),
             str(SRC_DIR / f"{name}.svg"), "-o", str(OUT_DIR / f"{name}.png")],
            check=True)
    print(f"光栅化 {len(have)} 张 → {OUT_DIR.relative_to(ROOT)}")
    print("缺档的稀有度会回落到现有纯色圆角框,可一级一级地上")
    return 0


if __name__ == "__main__":
    sys.exit(main())
