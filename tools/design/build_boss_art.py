#!/usr/bin/env python3
"""成语 Boss 立绘:四字「讹熔」成一团 + 按成语意象的配饰 → SVG → PNG,进 Unity Resources。

2026-09-30 用户拍板(比稿方案 A「讹熔」+ 意象配饰):**一个成语一张立绘,不再分四个阶段出图**。
世界观:讹把一整条成语熔成了一只怪 —— 四个字挤在一处、互相压叠,只认得出个大概;
正字者每破一个阶段就把一个字从它身上「正」出来,Boss 随之缩小一圈(MobView 的 Boss 骨架负责)。

用法: python3 tools/design/build_boss_art.py
前置: rsvg-convert(macOS: brew install librsvg)、tools/fonts/raw/NotoSerifSC[wght].ttf。

每只 Boss 出这些层(前缀 boss_<slug>),都是 512×512、同一画布坐标:
  c0~c3  四个字各一层,**白色**带墨纹透明度 —— 运行时按阶段着色:当前阶段染本属性色,
         其余染墨色,已破的阶段隐去(字被「正」走)。颜色不烤死,因为同一层要随阶段换色
  halo   纸色眼晕,不着色,垫在虹膜下(浓墨团里眼睛才跳得出来)
  iris   虹膜,白色,运行时染当前阶段的属性色(切阶段时眼睛跟着变色)
  eyes   眼眶/瞳孔/高光(虹膜处挖空),不着色
  wisp   成语意象配饰(枪尖/浪沫/雷纹…),自带颜色,MobView 让它慢漂
  body   四字墨色合成(静态场合用:单位详情页只叠 body/face/wisp 三层)
  face   带中性虹膜的整双眼(静态场合用)
另出一张通用 fx_boss_ring(白色墨环,切阶段时从新阶段的字上炸开)。

重跑逐字节稳定(无随机)。.meta 只在缺失时生成,已有的不动 —— guid 一变 Unity 引用全断。
"""
import math
import re
import shutil
import subprocess
import sys
import uuid
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/fonts"))
import glyph_refs  # noqa: E402

SVG_DIR = Path(__file__).parent / "bosses/svg"
OUT_DIR = ROOT / "Brushblade/Assets/_Project/Presentation/Mobs/Resources"
META_TEMPLATE = OUT_DIR / "enemy_mozi_body.png.meta"
SIZE = 512
INK = "#111622"
PAPER = "#F6F1E7"

# 五行色(与字怪合并稿同一套)
EL = {
    "Wood": "#348F4F", "Fire": "#C53637", "Earth": "#997C3C",
    "Metal": "#B3A382", "Water": "#0F74C4", "Heart": "#974FA7",
}

# 熔团摆位(比稿 A):四字挤向画布中下,互相压叠、轻微错位。[中心 x, 中心 y, 缩放, 旋转°]
# 眼睛开在团块中心 EYES —— 运行时剩下的字向 CENTER 收拢(Boss 越打越小)
LAYOUT = [(208, 214, .6, -9), (308, 228, .58, 8), (214, 320, .56, 5), (312, 318, .58, -7)]
CENTER = (258, 270)
EYES = (258, 262)

LAYER_NAMES = ("c0", "c1", "c2", "c3", "halo", "iris", "eyes", "wisp", "body", "face")


# ---------------------------------------------------------------- 配饰笔触

def _f(v):
    return f"{v:.1f}".rstrip("0").rstrip(".")


def spear(x, y, h=64, color="#6B6449"):
    return (f'<path d="M{x} {y + h} L{x} {y}" stroke="{INK}" stroke-width="5" stroke-linecap="round"/>'
            f'<path d="M{x - 9} {y + 14} L{x} {y - 12} L{x + 9} {y + 14} Z" fill="{color}"/>')


def flame(x, y, s=1.0, color=EL["Fire"]):
    return (f'<path d="M{x} {y - 46 * s} C {x + 22 * s} {y - 20 * s}, {x + 20 * s} {y}, {x} {y} '
            f'C {x - 20 * s} {y}, {x - 22 * s} {y - 20 * s}, {x} {y - 46 * s} Z" fill="{color}" opacity=".7"/>'
            f'<path d="M{x} {y - 22 * s} C {x + 9 * s} {y - 10 * s}, {x + 8 * s} {y}, {x} {y} '
            f'C {x - 8 * s} {y}, {x - 9 * s} {y - 10 * s}, {x} {y - 22 * s} Z" fill="#F2B45A" opacity=".8"/>')


def wave(y, amp=16, color=EL["Water"], width=6, opacity=.45, x0=30, x1=482):
    pts = f"M{x0} {y}"
    step = (x1 - x0) / 4
    for i in range(4):
        cx = x0 + step * (i + .5)
        pts += f" Q {_f(cx)} {y - amp if i % 2 == 0 else y + amp} {_f(x0 + step * (i + 1))} {y}"
    return f'<path d="{pts}" fill="none" stroke="{color}" stroke-width="{width}" stroke-linecap="round" opacity="{opacity}"/>'


def dots(points, r, color, opacity=.55):
    return "".join(f'<circle cx="{x}" cy="{y}" r="{r}" fill="{color}" opacity="{opacity}"/>' for x, y in points)


def bolt(x, y, s=1.0, color="#D8B84A"):
    p = [(0, 0), (-14, 34), (2, 34), (-10, 72), (20, 26), (4, 26), (14, 0)]
    d = "M" + " L".join(f"{_f(x + px * s)} {_f(y + py * s)}" for px, py in p) + " Z"
    return f'<path d="{d}" fill="{color}" opacity=".85"/>'


def rock(x, y, r, color=EL["Earth"]):
    pts = [(x + r * math.cos(a), y + r * .8 * math.sin(a)) for a in (0.2, 1.3, 2.3, 3.4, 4.4, 5.5)]
    return f'<path d="M{" L".join(f"{_f(px)} {_f(py)}" for px, py in pts)} Z" fill="{color}" opacity=".6"/>'


def snow(x, y, r, color="#9FC6E6"):
    arms = "".join(
        f'<path d="M{x} {y} L{_f(x + r * math.cos(a))} {_f(y + r * math.sin(a))}" stroke="{color}" '
        f'stroke-width="3" stroke-linecap="round"/>' for a in [k * math.pi / 3 for k in range(6)])
    return f'<g opacity=".8">{arms}</g>'


def blade(x, y, h=70, color="#B3A382"):
    return (f'<path d="M{x - 8} {y + h} L{x} {y} L{x + 8} {y + h} Z" fill="{color}" opacity=".85"/>'
            f'<path d="M{x} {y + 8} L{x} {y + h - 6}" stroke="{PAPER}" stroke-width="1.5" opacity=".7"/>')


def crack(points, color=INK, width=4, opacity=.55):
    d = "M" + " L".join(f"{x} {y}" for x, y in points)
    return f'<path d="{d}" fill="none" stroke="{color}" stroke-width="{width}" stroke-linejoin="bevel" opacity="{opacity}"/>'


def branch(x0, y0, x1, y1, color="#5C4A22", width=6):
    return f'<path d="M{x0} {y0} Q {(x0 + x1) / 2 + 14} {(y0 + y1) / 2 - 18} {x1} {y1}" fill="none" stroke="{color}" stroke-width="{width}" stroke-linecap="round" opacity=".75"/>'


def blossom(x, y, color="#E8A0B4"):
    return "".join(f'<circle cx="{_f(x + 7 * math.cos(a))}" cy="{_f(y + 7 * math.sin(a))}" r="5" fill="{color}" opacity=".85"/>'
                   for a in [k * 2 * math.pi / 5 for k in range(5)]) + f'<circle cx="{x}" cy="{y}" r="3" fill="#F2D45A"/>'


def bricks(color="#8C8676"):
    rows = []
    for r, y in enumerate(range(96, 440, 40)):
        offset = 0 if r % 2 == 0 else 34
        for x in range(28 + offset, 470, 68):
            rows.append(f'<rect x="{x}" y="{y}" width="60" height="32" rx="3" fill="none" stroke="{color}" stroke-width="3"/>')
    return f'<g opacity=".32">{"".join(rows)}</g>'


def vortex(color=EL["Heart"]):
    return "".join(f'<ellipse cx="258" cy="270" rx="{rx}" ry="{rx * .42}" fill="none" stroke="{color}" '
                   f'stroke-width="{w}" opacity="{o}" transform="rotate({rot} 258 270)" stroke-dasharray="{rx * 1.4} {rx * .5}"/>'
                   for rx, w, o, rot in ((236, 4, .35, -12), (206, 3, .28, 10), (176, 2.5, .22, -4)))


def strata(color=EL["Earth"]):
    return "".join(f'<path d="M24 {y} Q 256 {y - 10} 488 {y}" fill="none" stroke="{color}" stroke-width="{w}" opacity="{o}" stroke-linecap="round"/>'
                   for y, w, o in ((452, 10, .45), (474, 7, .35), (492, 5, .25)))


def ridge(color="#6B6449"):
    return f'<path d="M20 330 L110 200 L170 260 L250 130 L330 250 L400 180 L492 320" fill="none" stroke="{color}" stroke-width="7" stroke-linejoin="round" opacity=".32"/>'


# ---------------------------------------------------------------- 16 只成语 Boss

BOSSES = [
    # (成语, slug, 四字属性, 意象配饰)
    ("排山倒海", "paishandaohai", ("Metal", "Earth", "Wood", "Water"),
     lambda: ridge() + wave(470, 18) + wave(492, 12, opacity=.3)),
    ("翻江倒海", "fanjiangdaohai", ("Wood", "Water", "Wood", "Water"),
     lambda: wave(462, 22) + wave(486, 14, opacity=.3) + '<path d="M60 150 C 20 250, 90 330, 60 420" fill="none" stroke="#0F74C4" stroke-width="7" opacity=".35" stroke-linecap="round"/>'
     + '<path d="M452 120 C 492 220, 420 300, 456 400" fill="none" stroke="#0F74C4" stroke-width="7" opacity=".35" stroke-linecap="round"/>'),
    ("雷霆万钧", "leitingwanjun", ("Metal", "Metal", "Heart", "Metal"),
     lambda: bolt(92, 58, 1.1) + bolt(412, 40, 1.25) + bolt(448, 300, .8) + bolt(50, 330, .7)),
    ("刀山火海", "daoshanhuohai", ("Metal", "Earth", "Fire", "Water"),
     lambda: "".join(blade(x, y) for x, y in ((120, 40), (190, 22), (322, 22), (392, 40)))
     + "".join(flame(x, 488, s) for x, s in ((90, 1), (170, .8), (340, .8), (420, 1)))),
    ("山崩海啸", "shanbenghaixiao", ("Earth", "Earth", "Water", "Heart"),
     lambda: rock(70, 70, 22) + rock(440, 90, 18) + rock(110, 130, 12) + rock(400, 150, 14)
     + wave(466, 24) + dots([(60, 430), (450, 420), (480, 450)], 7, EL["Water"])),
    ("冰天雪地", "bingtianxuedi", ("Water", "Heart", "Heart", "Earth"),
     lambda: "".join(snow(x, y, r) for x, y, r in ((70, 80, 20), (440, 70, 24), (470, 300, 16), (48, 320, 14), (130, 470, 12), (390, 480, 14)))
     + '<path d="M20 496 Q 256 470 492 496" fill="none" stroke="#DDEBF5" stroke-width="12" opacity=".9" stroke-linecap="round"/>'),
    ("烈火干柴", "liehuoganchai", ("Fire", "Fire", "Heart", "Wood"),
     lambda: branch(90, 500, 420, 440, width=12) + branch(420, 500, 90, 440, width=12)
     + "".join(flame(x, y, s) for x, y, s in ((150, 460, 1.3), (256, 450, 1.6), (362, 460, 1.3), (70, 150, .7), (446, 130, .8)))),
    ("飞沙走石", "feishazoushi", ("Heart", "Water", "Earth", "Earth"),
     lambda: dots([(40 + i * 23, 120 + int(60 * math.sin(i * .7))) for i in range(20)], 3, "#C2A366", .55)
     + rock(60, 440, 20) + rock(140, 470, 13) + rock(440, 450, 18) + rock(470, 170, 12)),
    ("气吞山河", "qitunshanhe", ("Heart", "Heart", "Earth", "Water"),
     lambda: vortex() + ridge() + wave(478, 12, width=5, opacity=.3)),
    ("草木皆兵", "caomujiebing", ("Wood", "Wood", "Heart", "Metal"),
     lambda: "".join(spear(x, y) for x, y in ((96, 64), (170, 40), (344, 40), (420, 64)))
     + '<path d="M30 492 Q 90 440 120 492 Q 170 430 210 492 Q 300 440 330 492 Q 400 430 482 492" fill="#348F4F" opacity=".35"/>'),
    ("枯木逢春", "kumufengchun", ("Wood", "Wood", "Heart", "Wood"),
     lambda: branch(40, 120, 150, 60) + branch(472, 110, 360, 50) + branch(30, 400, 110, 330) + branch(480, 380, 400, 320)
     + blossom(150, 60) + blossom(362, 50) + blossom(110, 330) + blossom(402, 320) + blossom(60, 106)),
    ("星火燎原", "xinghuoliaoyuan", ("Heart", "Fire", "Fire", "Earth"),
     lambda: dots([(80, 90), (130, 60), (400, 70), (450, 110), (430, 40)], 5, "#F2B45A", .85)
     + "".join(flame(x, 494, .7) for x in range(50, 480, 56))),
    ("积土成山", "jituchengshan", ("Earth", "Earth", "Heart", "Earth"),
     lambda: strata() + rock(60, 400, 16) + rock(452, 396, 18) + '<path d="M256 26 L286 70 L226 70 Z" fill="#997C3C" opacity=".5"/>'),
    ("山崩地裂", "shanbengdilie", ("Earth", "Earth", "Earth", "Metal"),
     lambda: crack([(20, 470), (120, 450), (160, 488), (250, 456), (300, 496), (380, 452), (492, 478)], width=6)
     + crack([(250, 456), (262, 420)], width=4) + rock(80, 80, 20) + rock(430, 60, 24) + rock(470, 150, 12)),
    ("铜墙铁壁", "tongqiangtiebi", ("Metal", "Earth", "Metal", "Earth"),
     lambda: bricks()),
    ("惊涛骇浪", "jingtaohailang", ("Water", "Water", "Heart", "Water"),
     lambda: '<path d="M30 480 C 60 330, 200 330, 210 420 C 150 400, 120 440, 150 480 Z" fill="#0F74C4" opacity=".3"/>'
     + wave(470, 20) + dots([(60, 300), (90, 250), (470, 230), (490, 280), (440, 190), (210, 400)], 7, EL["Water"])),
]


# ---------------------------------------------------------------- 生成

FILTERS = '''<filter id="rg" x="-8%" y="-8%" width="116%" height="116%"><feTurbulence type="fractalNoise" baseFrequency="0.042" numOctaves="3" seed="7" result="n"/><feDisplacementMap in="SourceGraphic" in2="n" scale="7"/></filter>
<filter id="rs" x="-25%" y="-25%" width="150%" height="150%"><feTurbulence type="fractalNoise" baseFrequency="0.06" numOctaves="2" seed="13" result="n"/><feDisplacementMap in="SourceGraphic" in2="n" scale="12"/><feGaussianBlur stdDeviation="0.6"/></filter>
<filter id="pa"><feTurbulence type="fractalNoise" baseFrequency="0.012 0.02" numOctaves="2" seed="11"/><feColorMatrix values="0 0 0 0 1 0 0 0 0 1 0 0 0 0 1 0.7 0.7 0.7 0 -0.15"/></filter>
<mask id="sm"><rect width="512" height="512" fill="#c8c8c8"/><rect width="512" height="512" filter="url(#pa)"/></mask>'''


def _svg(title, inner):
    return (f'<svg xmlns="http://www.w3.org/2000/svg" width="{SIZE}" height="{SIZE}" viewBox="0 0 {SIZE} {SIZE}">'
            f'<title>{title}</title><defs>{FILTERS}</defs>{inner}</svg>\n')


def _glyph(char):
    return re.search(r' d="([^"]+)"', glyph_refs.render_svg(char, glyph_refs.BOSS_WEIGHT)).group(1)


def _placed(char, i, fill):
    x, y, s, r = LAYOUT[i]
    return (f'<g transform="translate({x} {y}) rotate({r}) scale({s}) translate(-256 -256)">'
            f'<path d="{_glyph(char)}" fill="{fill}"/></g>')


def _char_layer(char, i):
    """白色墨字:位移滤镜毛边 + 噪声遮罩的浓淡 —— 运行时整层染色,浓淡跟着保留。"""
    return f'<g filter="url(#rg)"><g mask="url(#sm)">{_placed(char, i, "#FFFFFF")}</g></g>'


def _eye_positions():
    # 眼要比字怪的大一号:熔团墨色太浓,小眼睛会淹在里面(2026-09-30 预览实测)
    x, y = EYES
    return ((x - 44, y, 1.5), (x + 44, y, 1.38))


def _iris():
    return "".join(f'<circle cx="{_f(x + .5)}" cy="{_f(y + .5)}" r="{_f(10 * s)}" fill="#FFFFFF"/>'
                   for x, y, s in _eye_positions())


def _halo():
    """纸色眼晕:垫在虹膜之下,让眼睛从浓墨团里跳出来。单独一层 —— 画进 eyes 会盖住虹膜。"""
    return "".join(f'<ellipse cx="{_f(x)}" cy="{_f(y)}" rx="{_f(24 * s)}" ry="{_f(20 * s)}" fill="{PAPER}" opacity=".78"/>'
                   for x, y, s in _eye_positions())


def _eyes(with_iris=None):
    out = [_halo()] if with_iris else []
    for x, y, s in _eye_positions():
        rx, ry, ir = 17 * s, 15 * s, 10 * s
        # 眶:外椭圆减去虹膜圆(evenodd 挖空),运行时虹膜层从洞里透出属性色
        ring = (f'M{_f(x - rx)} {y} A {_f(rx)} {_f(ry)} 0 1 0 {_f(x + rx)} {y} A {_f(rx)} {_f(ry)} 0 1 0 {_f(x - rx)} {y} Z '
                f'M{_f(x + .5 - ir)} {_f(y + .5)} A {_f(ir)} {_f(ir)} 0 1 0 {_f(x + .5 + ir)} {_f(y + .5)} '
                f'A {_f(ir)} {_f(ir)} 0 1 0 {_f(x + .5 - ir)} {_f(y + .5)} Z')
        if with_iris:
            out.append(f'<circle cx="{_f(x + .5)}" cy="{_f(y + .5)}" r="{_f(ir)}" fill="{with_iris}"/>')
        out.append(f'<path d="{ring}" fill="{INK}" fill-rule="evenodd"/>')
        out.append(f'<circle cx="{_f(x + .5)}" cy="{_f(y + .5)}" r="{_f(3.8 * s)}" fill="{INK}"/>')
        out.append(f'<circle cx="{_f(x - 6.5 * s)}" cy="{_f(y - 6.5 * s)}" r="{_f(2.7 * s)}" fill="{PAPER}" opacity=".9"/>')
    return f'<g filter="url(#rs)">{"".join(out)}</g>'


def build(name, slug, elements, deco):
    layers = {f"c{i}": _svg(f"{name} {c}", _char_layer(c, i)) for i, c in enumerate(name)}
    layers["halo"] = _svg(f"{name} halo", f'<g filter="url(#rs)">{_halo()}</g>')
    layers["iris"] = _svg(f"{name} iris", f'<g filter="url(#rs)">{_iris()}</g>')
    layers["eyes"] = _svg(f"{name} eyes", _eyes())
    layers["wisp"] = _svg(f"{name} 意象", f'<g filter="url(#rs)">{deco()}</g>')
    body = "".join(f'<g filter="url(#rg)"><g mask="url(#sm)">{_placed(c, i, INK)}</g></g>' for i, c in enumerate(name))
    layers["body"] = _svg(f"{name} body", f'<g opacity=".92">{body}</g>')
    layers["face"] = _svg(f"{name} face", _eyes(with_iris=EL[elements[0]]))
    return layers


def ring_fx():
    return _svg("boss ring", '<g filter="url(#rs)"><circle cx="256" cy="256" r="200" fill="none" stroke="#FFFFFF" stroke-width="26"/>'
                '<circle cx="256" cy="256" r="232" fill="none" stroke="#FFFFFF" stroke-width="8" opacity=".6"/></g>')


def _write_meta(png: Path):
    meta = png.with_suffix(".png.meta")
    if meta.exists():
        return
    template = META_TEMPLATE.read_text(encoding="utf-8")
    meta.write_text(re.sub(r"guid: [0-9a-f]{32}", f"guid: {uuid.uuid4().hex}", template, count=1), encoding="utf-8")


def main():
    if shutil.which("rsvg-convert") is None:
        print("缺 rsvg-convert(brew install librsvg)", file=sys.stderr)
        return 1
    SVG_DIR.mkdir(parents=True, exist_ok=True)
    jobs = {"fx_boss_ring": ring_fx()}
    for name, slug, elements, deco in BOSSES:
        for layer, svg in build(name, slug, elements, deco).items():
            jobs[f"boss_{slug}_{layer}"] = svg
    for key, svg in jobs.items():
        src = SVG_DIR / f"{key}.svg"
        src.write_text(svg, encoding="utf-8")
        png = OUT_DIR / f"{key}.png"
        subprocess.run(["rsvg-convert", "-w", str(SIZE), "-h", str(SIZE), str(src), "-o", str(png)], check=True)
        _write_meta(png)
    print(f"成语 Boss {len(BOSSES)} 只 / {len(jobs)} 张 → {OUT_DIR.relative_to(ROOT)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
