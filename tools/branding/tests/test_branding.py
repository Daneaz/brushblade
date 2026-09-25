"""品牌资源对账:尺寸清单、居中、自适应安全区、与 Theme.cs 的配色一致性。

这一层测试防的是「图出了但接不上」那类事故 —— 参照 tools/icons/tests/test_icons.py。
配色项尤其重要:Theme.cs 调过色(2026-09-18 WCAG AA 那次动了七个色)而品牌资源
没跟着重出,图标就会和游戏内 UI 对不上,而且**没有任何编译错**能提示你。
"""
import hashlib
import math
import re
import struct
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[3]
OUT = ROOT / "Brushblade/Assets/_Project/Presentation/Branding"
THEME = ROOT / "Brushblade/Assets/_Project/Presentation/UI/Theme.cs"

# 期望产物 → (宽, 高)。改这张表就要同步改 build_branding.build_specs()
EXPECTED: dict[str, tuple[int, int]] = {
    **{f"appicon_ios_{px}": (px, px)
       for px in (1024, 180, 167, 152, 120, 87, 80, 76, 60, 58, 40, 29, 20)},
    **{f"appicon_android_{px}": (px, px) for px in (192, 144, 96, 72, 48)},
    "appicon_play_512": (512, 512),
    "appicon_adaptive_fg": (432, 432),
    "appicon_adaptive_bg": (432, 432),
    "splash_logo": (1600, 900),
    "splash_landscape": (2732, 1536),
}


def png_size(p: Path) -> tuple[int, int]:
    """只读 IHDR,不依赖 Pillow。"""
    data = p.read_bytes()
    assert data[:8] == b"\x89PNG\r\n\x1a\n", f"{p.name} 不是 PNG"
    w, h = struct.unpack(">II", data[16:24])
    return w, h


@pytest.mark.parametrize("name,size", sorted(EXPECTED.items()))
def test_产物存在且尺寸正确(name, size):
    p = OUT / f"{name}.png"
    assert p.exists(), f"缺 {name}.png —— 跑 python3 tools/branding/build_branding.py"
    assert png_size(p) == size


@pytest.mark.parametrize("name", sorted(EXPECTED))
def test_每张图都有_meta(name):
    meta = OUT / f"{name}.png.meta"
    assert meta.exists(), f"{name}.png 没有 .meta,Unity 会自己生成一个随机 GUID"
    assert re.search(r"^guid: [0-9a-f]{32}$", meta.read_text(), re.M), \
        f"{name}.png.meta 的 guid 格式不对"


def test_meta_的_guid_两两不同():
    guids = {}
    for name in EXPECTED:
        g = re.search(r"^guid: ([0-9a-f]{32})$",
                      (OUT / f"{name}.png.meta").read_text(), re.M).group(1)
        assert g not in guids, f"{name} 与 {guids[g]} 的 GUID 撞了"
        guids[g] = name


def theme_color(field: str) -> tuple[int, int, int]:
    """从 Theme.cs 抠出某个语义色的 sRGB 0-255 值。"""
    m = re.search(rf"Color {field} = new\(([\d.]+)f, ([\d.]+)f, ([\d.]+)f\)",
                  THEME.read_text())
    assert m, f"Theme.cs 里找不到 {field}"
    return tuple(round(float(m.group(i)) * 255) for i in (1, 2, 3))


@pytest.mark.parametrize("field,hexval", [
    ("Paper", "#F6F1E7"), ("Ink", "#11161F"),
    ("Cinnabar", "#C53637"), ("Gold", "#CA9D33"),
])
def test_配色与_Theme_一致(field, hexval):
    """build_branding.py 里写死的十六进制必须等于 Theme.cs 的语义色。

    不一致 = 品牌资源和游戏内 UI 脱色,要么重出图要么改脚本,别改这条测试。
    """
    want = tuple(int(hexval[i:i + 2], 16) for i in (1, 3, 5))
    got = theme_color(field)
    assert all(abs(a - b) <= 1 for a, b in zip(want, got)), \
        f"{field}: Theme.cs 是 {got},build_branding.py 用的是 {want}"


def _mask_bbox(path: Path, pick):
    from PIL import Image
    im = Image.open(path).convert("RGBA")
    px = im.load()
    xs, ys = [], []
    for y in range(im.height):
        for x in range(im.width):
            if pick(px[x, y]):
                xs.append(x)
                ys.append(y)
    assert xs, f"{path.name} 没有内容"
    return min(xs), min(ys), max(xs), max(ys), im.width, im.height


def test_自适应前景在安全区内():
    """Android 自适应图标只保证中心 66/108 可见,圆形遮罩会切掉出界的部分。

    2026-09-21:字面按 42% 画布走才过;当初照普通图标的 60% 出图,
    圆形遮罩会切掉「字」的左点和右钩。
    """
    pytest.importorskip("PIL")
    x0, y0, x1, y1, w, h = _mask_bbox(
        OUT / "appicon_adaptive_fg.png", lambda p: p[3] > 128)
    r = w * 66 / 108 / 2
    worst = max(math.hypot(x - w / 2, y - h / 2)
                for x, y in [(x0, y0), (x1, y0), (x0, y1), (x1, y1)])
    assert worst <= r, f"内容最远点 {worst:.1f} 超出安全半径 {r:.1f}"


def test_主图标居中且字面占比合理():
    pytest.importorskip("PIL")
    x0, y0, x1, y1, w, h = _mask_bbox(
        OUT / "appicon_ios_1024.png",
        lambda p: p[0] > 200 and p[1] > 200 and p[2] > 180)  # 反白的「字」
    assert abs((x0 + x1) / 2 - w / 2) <= 6, "水平没居中"
    assert abs((y0 + y1) / 2 - h / 2) <= 8, "垂直没居中"
    frac = (x1 - x0) / w
    assert 0.55 <= frac <= 0.65, f"字面占比 {frac:.1%},超出 55%~65% 的设计区间"
