#!/usr/bin/env python3
"""品牌资源:App 图标 + 启动图。手写 SVG → PNG,产物入 Unity Assets。

用法: python3 tools/branding/build_branding.py
前置: rsvg-convert(macOS: brew install librsvg)——与 tools/icons/build_icons.py 同款。

设计定稿(2026-09-21 用户拍板 A 稿):
  - 图标 = 朱砂印章(白文印):满幅朱砂 #C53637,「字」反白,思源宋体 wght 900
  - 启动图 = 宣纸底 + 《字·斗》横向锁定图 + 右下朱砂闲章
  - 字形一律转成 path 嵌进 SVG:不依赖渲染机器装没装字体,也和 build_icons.py
    「SVG 里不放 <text>」的约定一致

⚠ 字重必须 900。思源宋体是高对比度宋体,700 以下横画在 80px 图标里会整根消失
  (2026-09-21 实测对比:见 tools/branding/tests/test_branding.py 的尺寸清单)。

⚠ Android 自适应图标前景有安全区:内容只保证中心 66/108 可见,所以前景层的字面
  按 42% 画布走(普通图标是 60%),否则圆形遮罩会切掉「字」的左点和右钩。
"""
import hashlib
import shutil
import subprocess
import sys
from pathlib import Path

from fontTools.pens.boundsPen import BoundsPen
from fontTools.pens.svgPathPen import SVGPathPen
from fontTools.ttLib import TTFont
from fontTools.varLib import instancer

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "Brushblade/Assets/_Project/Presentation/Branding"
SVG_DIR = Path(__file__).parent / "svg"
FONT_RAW = ROOT / "tools/fonts/raw"

# Theme.cs 语义色(勿在这里改配色:这几个值要和 Theme.cs 对得上)
PAPER = "#F6F1E7"   # Theme.Paper 宣纸底
INK = "#11161F"     # Theme.Ink 墨黑
CINNA = "#C53637"   # Theme.Cinnabar 朱砂
GOLD = "#CA9D33"    # Theme.Gold 赭金

WEIGHT = 900
_fonts: dict = {}


def _font(family: str, weight: int) -> TTFont:
    key = (family, weight)
    if key not in _fonts:
        src = FONT_RAW / f"Noto{family}SC[wght].ttf"
        _fonts[key] = instancer.instantiateVariableFont(
            TTFont(src), {"wght": weight}, inplace=False)
    return _fonts[key]


def glyph(ch: str, cx: float, cy: float, size: float,
          family: str = "Serif", weight: int = WEIGHT) -> str:
    """字形轮廓 → <g>,缩放到 size 见方、以 (cx, cy) 为视觉中心。"""
    f = _font(family, weight)
    gs = f.getGlyphSet()
    name = f.getBestCmap()[ord(ch)]
    pen = SVGPathPen(gs)
    gs[name].draw(pen)
    d = pen.getCommands()
    bp = BoundsPen(gs)
    gs[name].draw(bp)
    x0, y0, x1, y1 = bp.bounds
    gw, gh = x1 - x0, y1 - y0
    s = size / max(gw, gh)
    # 字形坐标 Y 向上,SVG Y 向下 → scale(1,-1);bbox 中心对齐到 (cx, cy)
    tx, ty = cx - s * (x0 + gw / 2), cy + s * (y0 + gh / 2)
    return (f'<g transform="translate({tx:.2f},{ty:.2f}) '
            f'scale({s:.5f},{-s:.5f})"><path d="{d}"/></g>')


def icon_svg(canvas: int, glyph_frac: float, bg: str | None) -> str:
    """朱砂印章图标。bg=None → 透明底(自适应图标前景层用)。"""
    back = f'<rect width="{canvas}" height="{canvas}" fill="{bg}"/>' if bg else ""
    g = glyph("字", canvas / 2, canvas * 0.505, canvas * glyph_frac)
    return (f'<svg xmlns="http://www.w3.org/2000/svg" width="{canvas}" '
            f'height="{canvas}" viewBox="0 0 {canvas} {canvas}">'
            f'{back}<g fill="{PAPER}">{g}</g></svg>')


def wordmark(w: int, h: int, bg: str | None, ink: str) -> str:
    """《字·斗》横向锁定图:两字 + 朱砂中点 + 右下闲章 + 赭金细线。"""
    cx, cy = w / 2, h / 2
    gs = h * 0.195           # 单字大小
    gap = gs * 1.34          # 字心间距
    # 视觉居中:内容框(字 + 闲章 + 金线)整体比几何中心再抬约 1%,
    # 纯几何居中会因为下方那条金线显得整组偏低(2026-09-21 实测 +33px/1536)
    base = cy - h * 0.045

    zi = glyph("字", cx - gap / 2, base, gs)
    dou = glyph("斗", cx + gap / 2, base, gs)
    dot = f'<circle cx="{cx:.1f}" cy="{base:.1f}" r="{gs * 0.052:.1f}" fill="{CINNA}"/>'

    sz = gs * 0.46
    sx, sy = cx + gap / 2 + gs * 0.62, base + gs * 0.30
    seal = (f'<rect x="{sx - sz / 2:.1f}" y="{sy - sz / 2:.1f}" width="{sz:.1f}" '
            f'height="{sz:.1f}" rx="{sz * 0.13:.1f}" fill="{CINNA}"/>'
            f'<g fill="{PAPER}">{glyph("斗", sx, sy, sz * 0.68)}</g>')
    line = (f'<rect x="{cx - gs * 1.05:.1f}" y="{base + gs * 0.92:.1f}" '
            f'width="{gs * 2.10:.1f}" height="{max(2, h * 0.002):.1f}" '
            f'fill="{GOLD}" opacity="0.75"/>')

    # 闲章挂在「斗」右外侧,会把内容框整体撑向右 —— 只按 cx 排版的话,
    # 「字·斗」自己是正的,整组看上去却偏右。把含闲章的内容框作为一个单位回正:
    #   左缘 = cx - gap/2 - gs/2 ,右缘 = sx + sz/2
    #   内容中心 - cx = (gs*0.62 + sz/2 - gs/2)/2 = 0.175*gs  (代入 sz = 0.46*gs)
    shift = -0.175 * gs

    back = f'<rect width="{w}" height="{h}" fill="{bg}"/>' if bg else ""
    return (f'<svg xmlns="http://www.w3.org/2000/svg" width="{w}" height="{h}" '
            f'viewBox="0 0 {w} {h}">{back}'
            f'<g transform="translate({shift:.2f},0)">'
            f'<g fill="{ink}">{zi}{dou}</g>{dot}{seal}{line}</g></svg>')


# 产物清单:文件名 → (SVG 源, 宽, 高)
def build_specs() -> dict[str, tuple[str, int, int]]:
    specs: dict[str, tuple[str, int, int]] = {}
    master = icon_svg(1024, 0.60, CINNA)

    # iOS(Unity Player Settings > iPhone/iPad 图标槽位)
    for px in (1024, 180, 167, 152, 120, 87, 80, 76, 60, 58, 40, 29, 20):
        specs[f"appicon_ios_{px}"] = (master, px, px)
    # Android 传统图标(mdpi → xxxhdpi)+ Play 商店图
    for px in (192, 144, 96, 72, 48):
        specs[f"appicon_android_{px}"] = (master, px, px)
    specs["appicon_play_512"] = (master, 512, 512)
    # Android 自适应图标:前景透明 + 纯朱砂背景层,字面收到 42% 避开圆形遮罩
    specs["appicon_adaptive_fg"] = (icon_svg(432, 0.42, None), 432, 432)
    specs["appicon_adaptive_bg"] = (
        f'<svg xmlns="http://www.w3.org/2000/svg" width="432" height="432">'
        f'<rect width="432" height="432" fill="{CINNA}"/></svg>', 432, 432)

    # 启动图:透明锁定图(Unity Splash 的 logo 槽)+ 横屏整图(iOS 启动屏/安卓)
    specs["splash_logo"] = (wordmark(1600, 900, None, INK), 1600, 900)
    specs["splash_landscape"] = (wordmark(2732, 1536, PAPER, INK), 2732, 1536)
    return specs


META_GUID_NS = "brushblade.branding."


def meta_for(name: str) -> str:
    """稳定 GUID:按文件名哈希,重跑不变 —— 变了会把 Unity 里的引用全断掉。"""
    guid = hashlib.md5((META_GUID_NS + name).encode()).hexdigest()
    return f"""fileFormatVersion: 2
guid: {guid}
TextureImporter:
  internalIDToNameTable: []
  externalObjects: {{}}
  serializedVersion: 13
  mipmaps:
    mipMapMode: 0
    enableMipMap: 0
    sRGBTexture: 1
  isReadable: 0
  grayScaleToAlpha: 0
  textureFormat: 1
  maxTextureSize: 2048
  textureSettings:
    serializedVersion: 2
    filterMode: 1
    aniso: 1
    mipBias: 0
    wrapU: 1
    wrapV: 1
  nPOTScale: 0
  spriteMode: 0
  alphaUsage: 1
  alphaIsTransparency: 1
  textureType: 0
  textureShape: 1
  platformSettings:
  - serializedVersion: 4
    buildTarget: DefaultTexturePlatform
    maxTextureSize: 2048
    textureFormat: -1
    textureCompression: 0
    compressionQuality: 100
    crunchedCompression: 0
    overridden: 0
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""


def main() -> int:
    if shutil.which("rsvg-convert") is None:
        print("缺 rsvg-convert(macOS: brew install librsvg)", file=sys.stderr)
        return 1
    OUT.mkdir(parents=True, exist_ok=True)
    SVG_DIR.mkdir(parents=True, exist_ok=True)

    specs = build_specs()
    # SVG 源按形态存一份进仓库(和 build_icons.py 一样,便于 review 和对账)
    for tag, key in [("icon_master", "appicon_ios_1024"),
                     ("icon_adaptive_fg", "appicon_adaptive_fg"),
                     ("splash_logo", "splash_logo"),
                     ("splash_landscape", "splash_landscape")]:
        (SVG_DIR / f"{tag}.svg").write_text(specs[key][0])

    for name, (svg, w, h) in sorted(specs.items()):
        tmp = SVG_DIR / f".{name}.tmp.svg"
        tmp.write_text(svg)
        png = OUT / f"{name}.png"
        subprocess.run(["rsvg-convert", "-w", str(w), "-h", str(h),
                        "-o", str(png), str(tmp)], check=True)
        tmp.unlink()
        meta = OUT / f"{name}.png.meta"
        # 已存在的 .meta 不覆盖:Unity 里调过的导入设置要留住
        if not meta.exists():
            meta.write_text(meta_for(name))
    print(f"品牌资源 {len(specs)} 张 → {OUT.relative_to(ROOT)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
