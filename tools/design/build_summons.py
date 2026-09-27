#!/usr/bin/env python3
"""召唤物形象 + 召唤物攻击动效贴图:脚本生成 SVG → PNG,放进 Unity Resources。

用法: python3 tools/design/build_summons.py
前置: rsvg-convert(macOS: brew install librsvg)——与 build_chests.py / build_icons.py 同款。

为什么是脚本画而不是出图稿:召唤物全是草木(字表 summonChar 那一列,至今只有木系),
「一丛枝叶围着一个字」这种构图矢量墨线画得住,且十只要风格齐整 —— 同一套笔触函数
(tapered 枝干、尖叶、叶簇)拼出来,比十张分别出的图更像一个系列。
每只的种子固定,重跑逐字节稳定;要改某只的样子改它的 compose 函数,别手改 svg。

构图守《敌人形象关键词包》§0「字为骨」:**字形不画进图里**,由工程侧用子集字体叠在最上层
(SummonView)。图只画字周围的草木 —— 中间 ~56% 的方框刻意留空给字,字永远清晰可认。

分层(与 MobAssets 同构,两层):
  body  墨色枝干 / 地面 / 刺,带半透明,运行时不着色
  leaf  叶、花、叶簇,**画成白色**,运行时按召唤物当前属性的 Theme.GlyphColor 着色 ——
        召唤物属性会被解封(EffectKind.Unseal)重掷,颜色烤死在图里就会与属性对不上

动效贴图(白色,运行时按攻击者属性着色,见 Juice.SummonStrike / SummonShoot):
  summon_fx_slash  近战:一记弧形笔锋劈砍
  summon_fx_arrow  远程:箭矢(箭头朝 +x,运行时按飞行方向旋转)
  summon_fx_leaf   命中:迸散的单片叶(尖朝 +x)

.meta 只在缺失时生成(新 guid),已有的一律不动 —— guid 一变,Unity 里的引用全断。
"""
import math
import random
import re
import shutil
import subprocess
import sys
import uuid
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUT_DIR = ROOT / "Brushblade/Assets/_Project/Presentation/Summons/Resources"
SVG_DIR = Path(__file__).parent / "summons/svg"
META_TEMPLATE = ROOT / "Brushblade/Assets/_Project/Presentation/Mobs/Resources/enemy_mozi_body.png.meta"
CANVAS = 256      # 战斗格立绘最大 100 逻辑单位,256 留够高 DPI 余量
VIEWBOX = 120

INK = "#111622"   # Theme.Ink
WHITE = "#FFFFFF"

# 召唤字 → 资产 slug。键 = chars.json 里 Summon 效果的 summonChar。
# C# 侧 SummonAssets.Slugs 必须逐条相同(test_summon_assets.py 守着)。
# U+E625 是 PUA 四叠木(真码点 𣛧 不在 BMP,见 subset_fonts.py 的 STACKED)。
SLUGS = {
    "林": "lin",
    "森": "sen",
    "": "simu",
    "箭": "jian",
    "楸": "qiu",
    "荆": "jing",
    "藻": "zao",
    "桂": "gui",
    "柘": "zhe",
    "藤": "teng",
}

LAYERS = ("body", "leaf")
FX = ("slash", "arrow", "leaf")


# ---------------------------------------------------------------- 笔触函数

def _f(v: float) -> str:
    return f"{v:.2f}".rstrip("0").rstrip(".")


def stalk(x0, y0, x1, y1, w0, w1, bend=0.0):
    """一根收笔的枝干:从 (x0,y0) 粗 w0 到 (x1,y1) 细 w1,中段向法线方向弯 bend。"""
    dx, dy = x1 - x0, y1 - y0
    length = math.hypot(dx, dy) or 1.0
    nx, ny = -dy / length, dx / length
    cx, cy = (x0 + x1) / 2 + nx * bend, (y0 + y1) / 2 + ny * bend
    wm = (w0 + w1) / 2
    pts = [
        (x0 - nx * w0, y0 - ny * w0), (cx - nx * wm, cy - ny * wm), (x1 - nx * w1, y1 - ny * w1),
        (x1 + nx * w1, y1 + ny * w1), (cx + nx * wm, cy + ny * wm), (x0 + nx * w0, y0 + ny * w0),
    ]
    p = [(_f(a), _f(b)) for a, b in pts]
    return (f'<path d="M{p[0][0]} {p[0][1]} Q{p[1][0]} {p[1][1]} {p[2][0]} {p[2][1]} '
            f'L{p[3][0]} {p[3][1]} Q{p[4][0]} {p[4][1]} {p[5][0]} {p[5][1]} Z"/>')


def leaf(x, y, angle, length, width):
    """一片尖叶:叶柄在 (x,y),叶尖沿 angle(度)方向伸出 length。"""
    l, w = length, width
    return (f'<path transform="translate({_f(x)} {_f(y)}) rotate({_f(angle)})" '
            f'd="M0 0 Q{_f(l * .42)} {_f(-w)} {_f(l)} 0 Q{_f(l * .42)} {_f(w)} 0 0 Z"/>')


def cluster(rng, x, y, count, spread, length, width, base_angle=-90.0):
    """叶簇:count 片叶从一点向 base_angle 两侧放射,长短、角度各带一点抖动。"""
    out = []
    for i in range(count):
        k = (i / (count - 1) - 0.5) if count > 1 else 0.0
        a = base_angle + k * spread + rng.uniform(-9, 9)
        out.append(leaf(x, y, a, length * rng.uniform(.8, 1.1), width * rng.uniform(.85, 1.1)))
    return "".join(out)


def ground(y=108, x0=10, x1=110):
    """地面一笔:两头尖的横向墨痕。"""
    m = (x0 + x1) / 2
    return (f'<path d="M{x0} {y} Q{_f(m)} {y - 3.2} {x1} {y - .6} '
            f'Q{_f(m)} {y + 2.6} {x0} {y} Z"/>')


def dot(x, y, r):
    return f'<circle cx="{_f(x)}" cy="{_f(y)}" r="{_f(r)}"/>'


def tree(rng, x, base, top, w0, lean=0.0, crown=7, crown_len=15):
    """一棵小树:枝干(body)+ 树冠叶簇(leaf)。返回 (body, leaf)。"""
    tx = x + lean
    body = stalk(x, base, tx, top, w0, w0 * .35, bend=lean * .4)
    # 两根侧枝,从上三分之一处分出去
    by = top + (base - top) * .35
    bx = x + lean * .65
    body += stalk(bx, by, bx - 9, by - 8, w0 * .45, .6, bend=-1.5)
    body += stalk(bx, by + 4, bx + 9, by - 5, w0 * .42, .6, bend=1.5)
    leaves = cluster(rng, tx, top + 2, crown, 150, crown_len, 4.6)
    leaves += cluster(rng, bx - 9, by - 7, 4, 110, crown_len * .7, 3.6, -120)
    leaves += cluster(rng, bx + 9, by - 4, 4, 110, crown_len * .7, 3.6, -60)
    return body, leaves


# ---------------------------------------------------------------- 十只召唤物
# 约定:中央 x∈[32,88]、y∈[26,92] 留给字(SummonView 叠的字形),草木画在四周。

def compose_lin(rng):
    """林:两棵树分立左右。"""
    b1, l1 = tree(rng, 16, 108, 30, 3.4, lean=2)
    b2, l2 = tree(rng, 104, 108, 34, 3.2, lean=-2)
    return ground() + b1 + b2, l1 + l2


def compose_sen(rng):
    """森:左右两棵 + 顶上一道拱起的树冠,三木成森。"""
    b1, l1 = tree(rng, 14, 108, 34, 3.2, lean=2)
    b2, l2 = tree(rng, 106, 108, 36, 3.2, lean=-2)
    arch = "".join(leaf(28 + i * 8, 20 + abs(i - 4) * 1.6, -90 + (i - 4) * 16 + rng.uniform(-6, 6),
                        13, 4.2) for i in range(9))
    return ground() + b1 + b2, l1 + l2 + arch


def compose_simu(rng):
    """四叠木:四角各一棵,树冠连成一圈把字围住 —— 林木极盛。"""
    body, leaves = ground(), ""
    for x, top, lean in ((12, 58, 1.5), (27, 78, .5), (93, 78, -.5), (108, 58, -1.5)):
        b, l = tree(rng, x, 108, top, 2.8, lean=lean, crown=6, crown_len=12)
        body += b
        leaves += l
    ring = []
    for i in range(22):
        a = math.pi + i / 21 * math.pi  # 上半圈
        cx, cy = 60 + math.cos(a) * 52, 58 + math.sin(a) * 46
        ring.append(leaf(cx, cy, math.degrees(a) + rng.uniform(-30, 30), 12, 4))
    return body, leaves + "".join(ring)


def compose_jian(rng):
    """箭:左侧一竿竹(带节)+ 右下斜出一支箭。"""
    body = ground()
    body += stalk(16, 110, 20, 10, 3.0, 2.2, bend=1)
    for y in (88, 64, 40, 18):  # 竹节
        body += f'<path d="M{_f(13.4 + (110 - y) * .04)} {y} h7.2" stroke="{INK}" stroke-width="1.6"/>'
    # 箭杆 + 箭羽(body),箭头(leaf,吃属性色)
    body += stalk(70, 112, 110, 82, 1.1, 1.1)
    body += leaf(72, 110.5, 150, 9, 2.6) + leaf(72, 110.5, 200, 9, 2.6)
    leaves = f'<path d="M106 85 L117 76 L111 89 Z"/>'
    for y in (64, 40, 18):
        leaves += leaf(21, y - 2, -20 + rng.uniform(-8, 8), 17, 3.2)
        leaves += leaf(21, y + 4, 10 + rng.uniform(-8, 8), 14, 3)
    return body, leaves


def compose_qiu(rng):
    """楸:高树,大片心形叶,几片正在飘落(楸的被动是打谁烧谁 —— 落叶如烬)。"""
    body, leaves = tree(rng, 18, 108, 16, 3.6, lean=3, crown=8, crown_len=18)
    body = ground() + body
    for x, y, a in ((96, 30, 20), (104, 58, -30), (92, 86, 50), (108, 100, 10)):
        leaves += leaf(x, y, a, 13, 6)
    return body, leaves


def compose_jing(rng):
    """荆:底部与两侧的荆条拱起,满枝尖刺,只挂零星小叶(嘲讽 + 反伤的肉盾)。"""
    body = ground()
    arcs = ((6, 106, 44, 104, -14), (40, 108, 82, 106, -12), (76, 106, 116, 104, -14),
            (8, 100, 14, 30, 8), (112, 100, 106, 30, -8))
    for x0, y0, x1, y1, bend in arcs:
        body += stalk(x0, y0, x1, y1, 1.8, 1.0, bend=bend)
        dx, dy = x1 - x0, y1 - y0
        ln = math.hypot(dx, dy)
        ux, uy = dx / ln, dy / ln
        for k in (.2, .4, .6, .8):
            # 弧上的点:二次曲线中点按 bend 偏出去,这里取近似 —— 沿弦插值再加一半弯度
            nx, ny = -uy, ux
            bulge = bend * 4 * k * (1 - k)
            px, py = x0 + dx * k + nx * bulge, y0 + dy * k + ny * bulge
            # 刺一律朝**远离字**的方向(画面中心),不会扎进字里、也不会钻到地面下去
            ox, oy = px - 60, py - 60
            ol = math.hypot(ox, oy) or 1.0
            ox, oy = ox / ol, oy / ol
            if py > 100:
                ox, oy = 0.0, -1.0  # 底部那几条贴地,刺往上长
            tx, ty = px + ox * 6, py + oy * 6
            body += (f'<path d="M{_f(px - ux * 2)} {_f(py - uy * 2)} L{_f(tx)} {_f(ty)} '
                     f'L{_f(px + ux * 2)} {_f(py + uy * 2)} Z"/>')
    leaves = "".join(leaf(x, y, a, 8, 3) for x, y, a in
                     ((20, 94, -60), (58, 96, -110), (98, 94, -120), (12, 50, 200), (108, 46, -20)))
    return body, leaves


def compose_zao(rng):
    """藻:水纹 + 从水底摇起的几条长藻。"""
    body = ""
    for y, x0, x1 in ((112, 8, 112), (104, 18, 44), (104, 76, 102)):
        body += (f'<path d="M{x0} {y} q6 -3 12 0 t12 0 t12 0 t12 0 t12 0 t12 0 t12 0 t12 0" '
                 f'fill="none" stroke="{INK}" stroke-width="1.3" opacity=".8" '
                 f'clip-path="url(#w{y}{x0})"/>'
                 f'<clipPath id="w{y}{x0}"><rect x="{x0}" y="{y - 4}" width="{x1 - x0}" height="8"/></clipPath>')
    leaves = ""
    for x, h, sway in ((12, 80, 5), (24, 50, -4), (96, 56, 4), (108, 84, -5)):
        # 一条藻 = 一根摇曳的茎(body)+ 沿茎交错的长叶(leaf)
        prev = (x, 110.0)
        for i in range(1, 7):
            y = 110 - i * h / 6
            cur = (x + sway * math.sin(i * 1.1), y)
            body += stalk(prev[0], prev[1], cur[0], cur[1], 1.1, .8, bend=sway * .3)
            leaves += leaf(cur[0], cur[1] + h / 14, -90 + (-40 if i % 2 else 40), h / 5.4, 2.4)
            prev = cur
    return body, leaves


def compose_gui(rng):
    """桂:右侧一棵桂树,叶间缀满小簇的花(桂的被动是自带护盾 —— 花团锦簇)。"""
    body, leaves = tree(rng, 104, 108, 22, 3.8, lean=-4, crown=8, crown_len=16)
    body = ground() + body
    body += stalk(20, 108, 16, 60, 2.2, .8, bend=-2)
    leaves += cluster(rng, 16, 60, 5, 130, 11, 3.6)
    flowers = ""
    for _ in range(26):
        a = rng.uniform(0, math.tau)
        r = rng.uniform(0, 1) ** .5
        cx = 94 + math.cos(a) * 18 * r
        cy = 28 + math.sin(a) * 16 * r
        if 32 < cx < 88 and 26 < cy < 92:
            continue
        flowers += dot(cx, cy, rng.uniform(1.1, 1.9))
    for x, y in ((14, 52), (20, 56), (10, 60), (24, 64)):
        flowers += dot(x, y, 1.5)
    return body, leaves + flowers


def compose_zhe(rng):
    """柘:一段粗壮的木身压在一块石上(柘木坚 —— 嘲讽 + 反伤的厚肉盾)。"""
    body = ground()
    body += '<path d="M4 110 Q6 92 22 90 Q40 88 44 104 Q44 112 30 112 L8 112 Z"/>'  # 石
    body += '<path d="M76 112 Q78 100 92 98 Q110 97 116 108 L116 112 Z"/>'
    body += stalk(14, 92, 12, 22, 6.2, 3.4, bend=-2)
    body += stalk(106, 100, 108, 30, 5.6, 3.0, bend=2)
    leaves = cluster(rng, 12, 24, 6, 160, 14, 6.2)
    leaves += cluster(rng, 108, 32, 6, 160, 14, 6.2)
    leaves += leaf(14, 60, 200, 11, 5.2) + leaf(106, 64, -20, 11, 5.2)
    return body, leaves


def compose_teng(rng):
    """藤:一根藤沿框边从左下爬到右上,甩出卷须,叶子沿途生(藤的被动是缠住 —— 冻结)。"""
    pts = [(10, 110), (8, 70), (14, 30), (40, 12), (80, 12), (106, 30), (112, 60)]
    body = ""
    for (x0, y0), (x1, y1) in zip(pts, pts[1:]):
        body += stalk(x0, y0, x1, y1, 1.9, 1.5, bend=-3)
    for cx, cy, r in ((22, 20, 5), (96, 18, 4.4), (112, 78, 4)):
        body += (f'<path d="M{cx} {cy} m{r} 0 a{r} {r} 0 1 1 -{r} -{r} '
                 f'a{r * .6:.2f} {r * .6:.2f} 0 1 1 {r * .6:.2f} {r * .6:.2f}" '
                 f'fill="none" stroke="{INK}" stroke-width="1.1"/>')
    leaves = ""
    for (x0, y0), (x1, y1) in zip(pts, pts[1:]):
        mx, my = (x0 + x1) / 2, (y0 + y1) / 2
        a = math.degrees(math.atan2(y1 - y0, x1 - x0))
        leaves += leaf(mx, my, a - 60 + rng.uniform(-10, 10), 11, 4.4)
        leaves += leaf(mx, my, a + 70 + rng.uniform(-10, 10), 9, 3.8)
    return body, leaves


COMPOSERS = {
    "lin": compose_lin, "sen": compose_sen, "simu": compose_simu, "jian": compose_jian,
    "qiu": compose_qiu, "jing": compose_jing, "zao": compose_zao, "gui": compose_gui,
    "zhe": compose_zhe, "teng": compose_teng,
}

# ---------------------------------------------------------------- 动效贴图

FX_BODIES = {
    # 一记笔锋:起笔圆、收笔尖的新月弧,外沿一道淡的飞白
    "slash": ('<path d="M14 96 Q34 30 104 16 Q52 44 22 100 Z" fill="#FFFFFF"/>'
              '<path d="M26 102 Q52 50 110 26 Q66 50 30 106 Z" fill="#FFFFFF" opacity=".45"/>'),
    # 箭:朝 +x。杆 + 三角箭头 + 两片羽
    "arrow": ('<rect x="14" y="57.6" width="78" height="4.8" rx="2.4" fill="#FFFFFF"/>'
              '<path d="M86 48 L114 60 L86 72 L92 60 Z" fill="#FFFFFF"/>'
              '<path d="M12 57 L34 57 L26 45 L6 45 Z M12 63 L34 63 L26 75 L6 75 Z" '
              'fill="#FFFFFF" opacity=".8"/>'),
    # 一片叶,尖朝 +x,带一道叶脉(镂空)
    "leaf": ('<path d="M8 60 Q50 20 112 60 Q50 100 8 60 Z" fill="#FFFFFF"/>'
             '<path d="M14 60 Q60 58 104 60" stroke="#000000" stroke-opacity=".28" '
             'stroke-width="3" fill="none"/>'),
}

# 笔触毛边:轻微的湍流位移,让矢量边缘带一点墨的毛糙(rsvg 支持这两个滤镜原语)
ROUGH = ('<filter id="rough" x="-5%" y="-5%" width="110%" height="110%">'
         '<feTurbulence type="fractalNoise" baseFrequency=".9" numOctaves="2" seed="{seed}"/>'
         '<feDisplacementMap in="SourceGraphic" scale="1.8"/></filter>')


def svg(inner: str) -> str:
    return (f'<svg xmlns="http://www.w3.org/2000/svg" width="{VIEWBOX}" height="{VIEWBOX}" '
            f'viewBox="0 0 {VIEWBOX} {VIEWBOX}">{inner}</svg>')


def _seed(slug: str) -> int:
    return sum(ord(c) * (i + 1) for i, c in enumerate(slug))


def assets() -> dict:
    """资产名 → SVG 文本。资产名 = Unity Resources 里的 key(不含扩展名)。"""
    out = {}
    for slug in SLUGS.values():
        rng = random.Random(_seed(slug))
        body, leaves = COMPOSERS[slug](rng)
        seed = _seed(slug) % 97
        # 墨:半透明,压在属性浅底上是淡墨,不抢中间那个字
        out[f"summon_{slug}_body"] = svg(
            ROUGH.format(seed=seed)
            + f'<g filter="url(#rough)" fill="{INK}" opacity=".5">{body}</g>')
        # 叶:纯白,运行时着色;不透明度留一点,叠在墨枝上透得出枝干
        out[f"summon_{slug}_leaf"] = svg(
            ROUGH.format(seed=seed + 1)
            + f'<g filter="url(#rough)" fill="{WHITE}" opacity=".88">{leaves}</g>')
    for key, body in FX_BODIES.items():
        out[f"summon_fx_{key}"] = svg(body)
    return out


def _meta_for_new_png() -> str:
    text = META_TEMPLATE.read_text(encoding="utf-8")
    return re.sub(r"^guid: [0-9a-f]{32}$", f"guid: {uuid.uuid4().hex}", text, count=1, flags=re.M)


def main(out_dir: Path = None) -> int:
    out_dir = Path(out_dir) if out_dir else OUT_DIR
    out_dir.mkdir(parents=True, exist_ok=True)
    SVG_DIR.mkdir(parents=True, exist_ok=True)

    built = assets()
    for name, text in built.items():
        (SVG_DIR / f"{name}.svg").write_text(text, encoding="utf-8")

    if shutil.which("rsvg-convert") is None:
        print("跳过 PNG:未找到 rsvg-convert(macOS: brew install librsvg)")
        return 1

    for name in built:
        png = out_dir / f"{name}.png"
        subprocess.run(
            ["rsvg-convert", "-w", str(CANVAS), "-h", str(CANVAS),
             str(SVG_DIR / f"{name}.svg"), "-o", str(png)],
            check=True)
        meta = png.with_name(png.name + ".meta")
        if not meta.exists():
            meta.write_text(_meta_for_new_png(), encoding="utf-8")
    print(f"{len(built)} 张召唤物素材 → {out_dir}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
