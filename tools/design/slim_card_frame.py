#!/usr/bin/env python3
"""字牌边框瘦身:把设计原稿的边框按系数收窄,产出实装用的 SVG。

起因(2026-07-28 试玩反馈):紫檀框原稿的木框内挖边界在 24/26px(占牌宽 12.5%),
「看久了不高级,反而笨重」。纯缩放整张图会连字一起缩,所以这里只收**边框几何**:
内挖边界、描金线、木纹层、螺钿点一起按系数内移,牌面留白相应变大。

用法:
    python3 tools/design/slim_card_frame.py            # 默认 0.62(边框占 7.8%)
    python3 tools/design/slim_card_frame.py --k 0.78   # 更厚一点

输入: docs/design/card-refs/original/*.svg(设计原稿,只读)
产出: docs/design/card-refs/assets/*.svg(实装稿,rasterize_cards.py 的输入)
"""
import argparse
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
ORIGINAL = ROOT / "docs/design/card-refs/original"
ASSETS = ROOT / "docs/design/card-refs/assets"

W, H = 192, 240


def slim_purple(svg: str, k: float) -> str:
    """紫檀框:木框内挖 + 描金线 + 五层木纹 + 四角螺钿,全部按 k 内移。"""
    ix, iy = round(24 * k), round(26 * k)
    gx, gy = round(22 * k), round(24 * k)
    lx, ly = 24 * k + 0.5, 26 * k + 0.5

    svg = svg.replace("M24 26 V214 H168 V26", f"M{ix} {iy} V{H - iy} H{W - ix} V{iy}")
    svg = svg.replace('x="24" y="26" width="144" height="188"',
                      f'x="{ix}" y="{iy}" width="{W - 2 * ix}" height="{H - 2 * iy}"')
    svg = svg.replace('x="24.5" y="26.5" width="143" height="187"',
                      f'x="{lx}" y="{ly}" width="{W - 2 * lx}" height="{H - 2 * ly}"')
    svg = svg.replace('x="22" y="24" width="148" height="192"',
                      f'x="{gx}" y="{gy}" width="{W - 2 * gx}" height="{H - 2 * gy}"')

    for x, w, h, r in [(3, 186, 234, 12), (7, 178, 226, 11), (11, 170, 218, 10),
                       (15, 162, 210, 9), (19, 154, 202, 8)]:
        nx = round(x * k, 1)
        svg = svg.replace(f'x="{x}" y="{x}" width="{w}" height="{h}" rx="{r}"',
                          f'x="{nx}" y="{nx}" width="{W - 2 * nx}" height="{H - 2 * nx}" rx="{r}"')

    # 四角螺钿:圆心与半径同步内移缩小,否则细边框压不住原尺寸的钿点
    def corner(match, cx_expr, cy_expr):
        radius = round(float(match.group(1)) * k, 1)
        return f'cx="{cx_expr}" cy="{cy_expr}" r="{radius}"'

    cx, cy = round(13 * k, 1), round(14 * k, 1)
    svg = re.sub(r'cx="13" cy="14" r="([\d.]+)"', lambda m: corner(m, cx, cy), svg)
    svg = re.sub(r'cx="179" cy="14" r="([\d.]+)"', lambda m: corner(m, W - cx, cy), svg)
    svg = re.sub(r'cx="13" cy="226" r="([\d.]+)"', lambda m: corner(m, cx, H - cy), svg)
    svg = re.sub(r'cx="179" cy="226" r="([\d.]+)"', lambda m: corner(m, W - cx, H - cy), svg)
    # 钿点高光
    hx, hy = round(11.4 * k, 1), round(12.4 * k, 1)
    svg = svg.replace('cx="11.4" cy="12.4"', f'cx="{hx}" cy="{hy}"')
    svg = svg.replace('cx="177.4" cy="12.4"', f'cx="{W - round(14.6 * k, 1)}" cy="{hy}"')
    svg = svg.replace('cx="11.4" cy="224.4"', f'cx="{hx}" cy="{H - round(15.6 * k, 1)}"')
    svg = svg.replace('cx="177.4" cy="224.4"',
                      f'cx="{W - round(14.6 * k, 1)}" cy="{H - round(15.6 * k, 1)}"')
    return svg


def slim_glow(svg: str, k: float) -> str:
    n = round(23 * k)
    return svg.replace('x="23" y="23" width="146" height="194"',
                       f'x="{n}" y="{n}" width="{W - 2 * n}" height="{H - 2 * n}"')


def slim_white(svg: str, k: float) -> str:
    """素纸框本来就细(8.5px),按同一系数轻微内移即可。"""
    for old, new in [(8.5, round(8.5 * k, 1)), (12.5, round(12.5 * k, 1))]:
        svg = svg.replace(f'x="{old}" y="{old}"', f'x="{new}" y="{new}"')
    svg = svg.replace('width="175" height="223"',
                      f'width="{W - 2 * round(8.5 * k, 1)}" height="{H - 2 * round(8.5 * k, 1)}"')
    svg = svg.replace('width="167" height="215"',
                      f'width="{W - 2 * round(12.5 * k, 1)}" height="{H - 2 * round(12.5 * k, 1)}"')
    return svg


HANDLERS = {
    "card_purple_frame.svg": slim_purple,
    "card_purple_glow.svg": slim_glow,
    "card_white_frame.svg": slim_white,
}


def main():
    parser = argparse.ArgumentParser(description="字牌边框瘦身")
    parser.add_argument("--k", type=float, default=0.62,
                        help="收窄系数(1.0=原稿;0.62 → 边框占牌宽 7.8%%)")
    args = parser.parse_args()

    if not ORIGINAL.exists():
        print(f"缺少原稿目录 {ORIGINAL.relative_to(ROOT)}", file=sys.stderr)
        return 1

    ASSETS.mkdir(parents=True, exist_ok=True)
    count = 0
    for name, handler in HANDLERS.items():
        source = ORIGINAL / name
        if not source.exists():
            continue
        (ASSETS / name).write_text(handler(source.read_text(), args.k), encoding="utf-8")
        count += 1
    print(f"瘦身 {count} 张(k={args.k},紫檀边框 {round(24 * args.k)}px = "
          f"{round(24 * args.k) / W:.1%} 牌宽)→ {ASSETS.relative_to(ROOT)}")
    print("接着跑 rasterize_cards.py 光栅化进 Unity")
    return 0


if __name__ == "__main__":
    sys.exit(main())
