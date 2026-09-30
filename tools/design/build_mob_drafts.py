#!/usr/bin/env python3
"""字怪合并稿生成:字形底稿 + 逐怪纹样 → docs/design/glyph-refs/svg-done/mob_<slug>.svg。

2026-09-30 十层一主题补的七只(炭笔/拓片/印泥/刻刀/铭文/泼墨/晕染)走这里。此前 25 只的
合并稿是逐张出的,没留生成脚本;这里把那套**同一个模板**(见 svg-done/mob_kubi.svg)抽成函数:

  L0 body  墨身(字形 × 两层噪声遮罩 + 位移滤镜)+ 剪到字形里的材质纹样 + 眼窝阴影
  L1 face  眼睛(墨眶 + 属性色虹膜 + 瞳 + 高光)—— MobView 单独驱动眨眼
  L2 wisp  字外的飘散物(火星/水花/锈屑)—— MobView 单独驱动漂浮
  L3 state 可选:战斗状态层(铭文的锈斑,随 Obscure 被读懂而褪去)

《敌人形象关键词包》§5.0 的两条画法照守:护甲不画记号;表面纹理剪裁到字形,溢出物例外。

用法:
    python3 tools/design/build_mob_drafts.py          # 生成七张合并稿
    python3 tools/design/split_layers.py              # 拆层 → tools/design/mobs/svg/
    python3 tools/design/rasterize_mobs.py            # 光栅化 → Unity Resources

重跑逐字节稳定(无随机);要改某只的样子改它下面那一条 spec,别手改 svg。
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
GLYPH_DIR = ROOT / "docs/design/glyph-refs/svg"
OUT_DIR = ROOT / "docs/design/glyph-refs/svg-done"

INK = "#111622"
INK_SOFT = "#3D4E69"
PAPER = "#F6F1E7"

# 五行色(与既有合并稿同一套:虹膜 高光/主色/暗部)
PALETTE = {
    "Wood": ("#7cc08e", "#348F4F", "#1c5730"),
    "Fire": ("#e07a6a", "#C53637", "#7a1f22"),
    "Earth": ("#c2a366", "#997C3C", "#5c4a22"),
    "Metal": ("#d8cba9", "#B3A382", "#6b6049"),
    "Water": ("#6aa9e0", "#0F74C4", "#0a3f6b"),
}
VERDIGRIS = "#4E9A86"   # 铜绿:铭文专用(金系的锈)
CINNABAR = "#C53637"    # 朱砂:印泥专用


def _defs(n: int, element: str) -> str:
    hi, mid, lo = PALETTE[element]
    return f'''<defs>
<filter id="L{n}_rg" x="-8%" y="-8%" width="116%" height="116%"><feTurbulence type="fractalNoise" baseFrequency="0.042" numOctaves="3" seed="7" result="n" /><feDisplacementMap in="SourceGraphic" in2="n" scale="7" /></filter>
<filter id="L{n}_rs" x="-25%" y="-25%" width="150%" height="150%"><feTurbulence type="fractalNoise" baseFrequency="0.06" numOctaves="2" seed="13" result="n" /><feDisplacementMap in="SourceGraphic" in2="n" scale="12" /><feGaussianBlur stdDeviation="0.6" /></filter>
<filter id="L{n}_pa"><feTurbulence type="fractalNoise" baseFrequency="0.012 0.02" numOctaves="2" seed="11" /><feColorMatrix values="0 0 0 0 1 0 0 0 0 1 0 0 0 0 1 0.7 0.7 0.7 0 -0.15" /></filter>
<filter id="L{n}_pb"><feTurbulence type="fractalNoise" baseFrequency="0.016 0.011" numOctaves="2" seed="29" /><feColorMatrix values="0 0 0 0 1 0 0 0 0 1 0 0 0 0 1 1.2 1.2 1.2 0 -1.15" /></filter>
<mask id="L{n}_sm"><rect width="512" height="512" fill="#9a9a9a" /><rect width="512" height="512" filter="url(#L{n}_pa)" /></mask>
<mask id="L{n}_wt"><rect width="512" height="512" fill="#000" /><rect width="512" height="512" filter="url(#L{n}_pb)" /></mask>
<radialGradient id="L{n}_iris" cx="0.5" cy="0.4" r="0.6"><stop offset="0" stop-color="{hi}" /><stop offset="0.6" stop-color="{mid}" /><stop offset="1" stop-color="{lo}" /></radialGradient>
<radialGradient id="L{n}_aura" cx="0.5" cy="0.5" r="0.5"><stop offset="0" stop-color="{mid}" stop-opacity="0.5" /><stop offset="1" stop-color="{mid}" stop-opacity="0" /></radialGradient>
'''


def _f(v: float) -> str:
    return f"{v:.2f}".rstrip("0").rstrip(".")


def _eye(x: float, y: float, scale: float) -> str:
    """与既有合并稿同尺寸的一只眼(scale 1 = 眶 13×12)。"""
    return (f'<g><ellipse cx="{_f(x)}" cy="{_f(y)}" rx="{_f(13 * scale)}" ry="{_f(12 * scale)}" fill="{INK}" />'
            f'<circle cx="{_f(x + .5)}" cy="{_f(y + .5)}" r="{_f(7.44 * scale)}" fill="url(#L1_iris)" />'
            f'<circle cx="{_f(x + .5)}" cy="{_f(y + .5)}" r="{_f(2.9 * scale)}" fill="{INK}" />'
            f'<circle cx="{_f(x - 5.46 * scale)}" cy="{_f(y - 5.64 * scale)}" r="{_f(2.04 * scale)}" fill="{PAPER}" opacity="0.9" /></g>')


def _glyph_path(slug: str) -> str:
    svg = (GLYPH_DIR / f"enemy_{slug}.svg").read_text(encoding="utf-8")
    return re.search(r' d="([^"]+)"', svg).group(1)


def build(spec: dict) -> str:
    d = _glyph_path(spec["slug"])
    el = spec["element"]
    rot = f'rotate({spec["rotate"]} 256 256)'
    mid = PALETTE[el][1]
    texture = spec["texture"].replace("{EL}", mid)
    wisp = spec["wisp"].replace("{EL}", mid)
    eyes = spec["eyes"]
    sockets = "".join(
        f'<ellipse cx="{_f(x)}" cy="{_f(y)}" rx="{_f(22.1 * s)}" ry="{_f(17.4 * s)}" />' for x, y, s in eyes)

    body = (f'<g>{_defs(0, el)}<clipPath id="L0_clip"><path d="{d}" /></clipPath>\n</defs>'
            f'<g transform="{rot}"><g filter="url(#L0_rg)">'
            f'<g mask="url(#L0_sm)"><path d="{d}" fill="{INK_SOFT}" /></g>'
            f'<g mask="url(#L0_wt)"><path d="{d}" fill="{INK}" /></g></g></g>'
            f'<g transform="{rot}" clip-path="url(#L0_clip)" filter="url(#L0_rs)">{texture}</g>'
            + (f'<g transform="{rot}" filter="url(#L0_rs)">{spec["overflow"]}</g>' if spec.get("overflow") else "")
            + f'<g transform="{rot}" fill="{INK_SOFT}" opacity="0.14" filter="url(#L0_rs)">{sockets}</g></g>')
    face = (f'<g>{_defs(1, el)}</defs><g transform="{rot}" filter="url(#L1_rs)">'
            + "".join(_eye(x, y, s) for x, y, s in eyes) + '</g></g>')
    wisp_layer = f'<g>{_defs(2, el)}</defs><g filter="url(#L2_rs)">{wisp}</g></g>'
    state = ""
    if spec.get("state"):
        state = (f'\n<g><defs><filter id="L3_fog" x="-30%" y="-30%" width="160%" height="160%">'
                 f'{spec["state_filter"]}</filter>'
                 '<radialGradient id="L3_fade" cx="0.5" cy="0.5" r="0.5"><stop offset="0.55" stop-color="#fff" />'
                 '<stop offset="1" stop-color="#000" /></radialGradient>'
                 '<mask id="L3_edge"><rect width="512" height="512" fill="url(#L3_fade)" /></mask>'
                 f'</defs>{spec["state"]}</g>')
    return (f'<svg xmlns="http://www.w3.org/2000/svg" width="512" height="512" viewBox="0 0 512 512">\n'
            f'<title>{spec["name"]}</title>\n{body}\n{face}\n{wisp_layer}{state}\n</svg>\n')


def _dots(points, r, fill, opacity):
    return "".join(f'<circle cx="{x}" cy="{y}" r="{r}" fill="{fill}" opacity="{opacity}" />' for x, y in points)


def _line(x0, y0, x1, y1, color, width, opacity):
    return (f'<path d="M{x0} {y0} L{x1} {y1}" fill="none" stroke="{color}" stroke-width="{width}" '
            f'stroke-linecap="round" opacity="{opacity}" />')


# 眼位 (x, y, 眶缩放) 都落在粗笔画上;每只的形象与机制对上(写在 note 里)
SPECS = [
    {
        # 炭笔:火系后排灼身。眼在山字头下那道长横上;火字四笔里透暗红余烬 = 灼烧;
        # 字外是往上飘的火星与灰屑 —— 每次出手挂的那层灼烧从这里溅出去
        "name": "炭笔", "slug": "tanbi", "element": "Fire", "rotate": -1,
        "eyes": [(172, 201, 1.0), (306, 201, 0.92)],
        "texture": (
            '<ellipse cx="200" cy="330" rx="22" ry="40" fill="{EL}" opacity="0.55" />'
            '<ellipse cx="300" cy="360" rx="26" ry="18" fill="{EL}" opacity="0.5" />'
            '<ellipse cx="110" cy="390" rx="18" ry="14" fill="{EL}" opacity="0.45" />'
            + _dots([(160, 250), (228, 280), (262, 330), (340, 330), (150, 350)], 3, INK, 0.5)),
        "wisp": (_dots([(380, 150), (410, 110), (360, 90)], 4, "{EL}", 0.7)
                 + _dots([(430, 70), (392, 50)], 2.5, "{EL}", 0.5)
                 + _line(96, 452, 84, 488, INK, 3, 0.4) + _line(420, 430, 440, 470, INK, 3, 0.35)),
    },
    {
        # 拓片:土系分裂。碑上拓下的纸:墨面满是留白斑点(拓印的纸纹),眼在石字那道横上;
        # 字外右下是一角翘起的纸边 —— 它一拍就多出一张
        "name": "拓片", "slug": "tapian", "element": "Earth", "rotate": 1,
        "eyes": [(296, 229, 0.95), (372, 229, 0.95)],
        "texture": (
            _dots([(130, 110), (140, 200), (118, 300), (300, 150), (270, 300), (360, 260),
                   (330, 380), (410, 320), (220, 110), (390, 100), (140, 400), (420, 400)], 5, PAPER, 0.55)
            + _dots([(150, 150), (290, 360), (410, 250), (250, 200)], 3, PAPER, 0.45)
            + '<rect x="250" y="220" width="170" height="200" fill="{EL}" opacity="0.28" />'),
        "wisp": ('<path d="M420 430 L476 418 L462 478 Z" fill="{EL}" opacity="0.32" />'
                 '<path d="M420 430 L462 478" fill="none" stroke="#111622" stroke-width="2" opacity="0.35" />'
                 + _line(60, 470, 150, 470, "{EL}", 2.5, 0.35) + _line(80, 486, 170, 486, "{EL}", 2, 0.25)),
    },
    {
        # 印泥:土系肉盾。一盒压实的朱红印泥:墨身里压着朱砂色的方印纹;眼小而深陷
        # (同砚台/版牍 —— 它不想打人,只是杵在那里);字外是一枚淡淡的方印落款
        "name": "印泥", "slug": "yinni", "element": "Earth", "rotate": 0,
        "eyes": [(132, 206, 0.8), (190, 206, 0.8)],
        "texture": (
            f'<rect x="60" y="80" width="170" height="290" fill="{CINNABAR}" opacity="0.38" />'
            f'<rect x="250" y="80" width="170" height="350" fill="{CINNABAR}" opacity="0.3" />'
            + _line(70, 300, 230, 300, PAPER, 3, 0.35) + _line(260, 200, 410, 200, PAPER, 3, 0.3)
            + _line(260, 330, 410, 330, "{EL}", 4, 0.4)),
        "wisp": (f'<rect x="398" y="416" width="58" height="58" fill="none" stroke="{CINNABAR}" '
                 f'stroke-width="5" opacity="0.45" />'
                 f'<rect x="412" y="430" width="30" height="30" fill="{CINNABAR}" opacity="0.3" />'
                 + _line(70, 470, 200, 470, INK, 3, 0.35)),
    },
    {
        # 刻刀:金系后排锁人。独眼(同悬针 —— 它只认一个目标);立刀旁的两竖刻着一道道
        # 亮白刀口;字外是刀尖溢出的一线寒光 —— 越过前排直扎你本人的那一刀(溢出物不剪)
        "name": "刻刀", "slug": "kedao", "element": "Metal", "rotate": -2,
        "eyes": [(172, 120, 1.15)],
        "texture": (
            _line(414, 110, 414, 330, PAPER, 3, 0.55) + _line(452, 80, 452, 400, PAPER, 3.5, 0.6)
            + _line(200, 220, 290, 330, PAPER, 2.5, 0.4)
            + '<rect x="400" y="60" width="70" height="380" fill="{EL}" opacity="0.18" />'),
        "overflow": ('<path d="M446 420 L452 486 L460 420 Z" fill="#B3A382" opacity="0.85" />'
                     '<path d="M452 430 L452 480" fill="none" stroke="#F6F1E7" stroke-width="1.5" opacity="0.8" />'),
        "wisp": ('<path d="M470 470 l6 -14 l6 14 l-6 14 Z" fill="#F6F1E7" opacity="0.8" />'
                 + _line(472, 470, 492, 470, "{EL}", 2, 0.6) + _line(476, 460, 490, 450, "{EL}", 1.5, 0.5)),
    },
    {
        # 铭文:金系深藏。青铜古铭:墨面满是铜绿锈斑,眼在名字口的上沿;
        # L3 = 一层铜绿锈雾盖住整个字 —— 属性「?」时罩着,被读懂(受击两次)就褪去,同生僻字的墨雾
        "name": "铭文", "slug": "mingwen", "element": "Metal", "rotate": 1,
        "eyes": [(302, 292, 0.95), (378, 292, 0.95)],
        "texture": (
            f'<ellipse cx="120" cy="160" rx="34" ry="26" fill="{VERDIGRIS}" opacity="0.5" />'
            f'<ellipse cx="300" cy="200" rx="40" ry="30" fill="{VERDIGRIS}" opacity="0.45" />'
            f'<ellipse cx="350" cy="400" rx="46" ry="26" fill="{VERDIGRIS}" opacity="0.5" />'
            f'<ellipse cx="140" cy="380" rx="26" ry="36" fill="{VERDIGRIS}" opacity="0.4" />'
            + '<rect x="260" y="280" width="150" height="160" fill="{EL}" opacity="0.3" />'),
        "wisp": (_dots([(96, 470), (120, 488), (440, 460), (462, 480)], 4, VERDIGRIS, 0.55)
                 + _dots([(80, 440), (470, 440)], 2.5, VERDIGRIS, 0.4)),
        "state_filter": ('<feTurbulence type="fractalNoise" baseFrequency="0.016" numOctaves="3" seed="21" />'
                         '<feColorMatrix values="0 0 0 0 0.20 0 0 0 0 0.42 0 0 0 0 0.37 0 0 0 0.95 0" />'
                         '<feGaussianBlur stdDeviation="5" />'),
        # 锈雾四周淡出成一团(径向遮罩),不留方框的硬边
        "state": ('<g opacity="0.72" mask="url(#L3_edge)">'
                  '<rect x="40" y="40" width="432" height="440" filter="url(#L3_fog)" /></g>'),
    },
    {
        # 泼墨:水系前排。一整砚泼出去的墨:字身里是大团水色墨晕,眼在发字那道横上;
        # 字外四溅的墨点与水花(溢出物,不剪)—— 劈头盖脸
        "name": "泼墨", "slug": "pomo", "element": "Water", "rotate": -2,
        "eyes": [(268, 186, 1.0), (358, 186, 0.95)],
        "texture": (
            '<ellipse cx="300" cy="330" rx="80" ry="60" fill="{EL}" opacity="0.4" />'
            '<ellipse cx="120" cy="260" rx="30" ry="70" fill="{EL}" opacity="0.35" />'
            + _dots([(250, 400), (380, 420)], 8, INK, 0.5)),
        "overflow": _dots([(470, 430), (486, 398)], 7, INK, 0.75) + _dots([(40, 300)], 6, INK, 0.7),
        "wisp": (_dots([(470, 470), (440, 490), (40, 340), (60, 380), (470, 150)], 5, "{EL}", 0.6)
                 + _dots([(490, 360), (30, 250), (480, 110)], 3, "{EL}", 0.45)),
    },
    {
        # 晕染:水系后排治疗。墨在水里晕开:字身下半的水色一层层淡出,眼在日字中横上;
        # 字外是一圈圈淡开的水晕 —— 把同伴的伤口也晕平了(同涂改的奶妈身份)
        "name": "晕染", "slug": "yunran", "element": "Water", "rotate": 0,
        "eyes": [(190, 131, 0.9), (270, 131, 0.9)],
        "texture": (
            '<ellipse cx="256" cy="380" rx="200" ry="70" fill="{EL}" opacity="0.28" />'
            '<ellipse cx="256" cy="300" rx="160" ry="50" fill="{EL}" opacity="0.22" />'
            '<ellipse cx="256" cy="130" rx="110" ry="60" fill="{EL}" opacity="0.18" />'),
        "wisp": ('<ellipse cx="256" cy="470" rx="170" ry="22" fill="none" stroke="{EL}" stroke-width="3" opacity="0.35" />'
                 '<ellipse cx="256" cy="470" rx="220" ry="32" fill="none" stroke="{EL}" stroke-width="2" opacity="0.22" />'
                 '<circle cx="256" cy="256" r="236" fill="url(#L2_aura)" opacity="0.35" />'),
    },
]


def main() -> int:
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    for spec in SPECS:
        out = OUT_DIR / f"mob_{spec['slug']}.svg"
        out.write_text(build(spec), encoding="utf-8")
        print(f"  {spec['name']:<4} → {out.relative_to(ROOT)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
