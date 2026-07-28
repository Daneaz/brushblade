#!/usr/bin/env python3
"""字牌素材 SVG → PNG:稀有度框 + 光效层 + 六系属性元件,光栅化进 Unity。

与字怪同一条理由:稿子的材质靠 SVG 渐变、描边、模糊堆出来,离线用 librsvg 渲染最保真
(Unity 的 Vector Graphics 不支持 filter,而元件的水墨虚边全靠 feGaussianBlur)。

用法:
    python3 tools/design/rasterize_cards.py
    python3 tools/design/rasterize_cards.py --check

输入: docs/design/card-refs/assets/*.svg      (框 192×240)
      docs/design/card-refs/elements/*.svg    (属性元件 128×128)
产出: Brushblade/Assets/_Project/Presentation/Cards/Resources/*.png
"""
import argparse
import shutil
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
FRAME_DIR = ROOT / "docs/design/card-refs/assets"
ELEM_DIR = ROOT / "docs/design/card-refs/elements"
OUT_DIR = ROOT / "Brushblade/Assets/_Project/Presentation/Cards/Resources"

FRAME_W, FRAME_H = 192, 240   # 牌面基准画布(0.8 竖版)
ELEM_SIZE = 128               # 属性元件基准画布

TIERS = ("white", "green", "blue", "purple", "orange", "red")
PARTS = ("frame", "glow")
ELEMENTS = ("fire", "water", "wood", "metal", "earth", "heart")


def jobs():
    """[(源文件, 输出名, 宽, 高)],只收实际存在的源。"""
    out = []
    for tier in TIERS:
        for part in PARTS:
            src = FRAME_DIR / f"card_{tier}_{part}.svg"
            if src.exists():
                out.append((src, f"card_{tier}_{part}", FRAME_W, FRAME_H))
    for elem in ELEMENTS:
        src = ELEM_DIR / f"elem_{elem}.svg"
        if src.exists():
            out.append((src, f"elem_{elem}", ELEM_SIZE, ELEM_SIZE))
    return out


def main():
    parser = argparse.ArgumentParser(description="字牌素材 SVG → PNG")
    parser.add_argument("--check", action="store_true", help="只报告,不写文件")
    args = parser.parse_args()

    have = jobs()
    names = {name for _, name, _, _ in have}
    missing_tiers = [t for t in TIERS if f"card_{t}_frame" not in names]
    missing_elems = [e for e in ELEMENTS if f"elem_{e}" not in names]
    print(f"框素材:{len(TIERS) - len(missing_tiers)}/{len(TIERS)} 档"
          f"(缺 {'、'.join(missing_tiers) if missing_tiers else '无'})")
    print(f"光效层:{len([n for n in names if n.endswith('glow')])} 张")
    print(f"属性元件:{len(ELEMENTS) - len(missing_elems)}/{len(ELEMENTS)} 个"
          f"(缺 {'、'.join(missing_elems) if missing_elems else '无'})")
    if args.check:
        return 0

    if shutil.which("rsvg-convert") is None:
        print("需要 rsvg-convert(macOS: brew install librsvg)", file=sys.stderr)
        return 1

    OUT_DIR.mkdir(parents=True, exist_ok=True)
    for src, name, w, h in have:
        subprocess.run(
            ["rsvg-convert", "-w", str(w), "-h", str(h), str(src),
             "-o", str(OUT_DIR / f"{name}.png")],
            check=True)
    print(f"光栅化 {len(have)} 张 → {OUT_DIR.relative_to(ROOT)}")
    print("缺档的稀有度回落纯色圆角框;缺元件的系不跑属性动效,都不影响其余的上")
    return 0


if __name__ == "__main__":
    sys.exit(main())
