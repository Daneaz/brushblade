"""召唤物形象 + 攻击动效贴图的对账:chars.json ↔ 两张 slug 表 ↔ svg 镜像 ↔ Unity Resources。

与 test_mob_assets.py 同一根绊线:召唤物形象经手的几处**没有一处是从另一处生成的**——
`build_summons.SLUGS`、`SummonAssets.Slugs`(C# 侧同一张表)、字表的 summonChar 列、
最终进包的 PNG/.meta。改一处漏一处不报任何错:真机上那只召唤物安静地回落成纯字牌格,
攻击动效回落成圆角色块,而离线编译、Core 单测、字体子集测试**全都是绿的**。
"""
import json
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import build_summons as bs

CHARS = bs.ROOT / "Brushblade/Assets/StreamingAssets/config/chars.json"
CSHARP = bs.ROOT / "Brushblade/Assets/_Project/Presentation/Summons/SummonAssets.cs"


def _summon_chars():
    """字表里所有 Summon 效果的 summonChar(去重)。"""
    out = set()
    for c in json.loads(CHARS.read_text(encoding="utf-8"))["chars"]:
        for e in c.get("effects", []):
            if e.get("kind") == "Summon" and e.get("summonChar"):
                out.add(e["summonChar"])
    return out


def _csharp_slugs():
    src = CSHARP.read_text(encoding="utf-8")
    block = re.search(r"Slugs = new\(\)\s*\{(.*?)\n        \};", src, re.S)
    assert block, "SummonAssets.cs 里找不到 Slugs 字典"
    pairs = re.findall(r'\{\s*"([^"]+)",\s*"([^"]+)"\s*\}', block.group(1))
    # C# 源里 PUA 字写成  转义,这里还原成字符再比
    unescape = lambda s: re.sub(r"\\u([0-9a-fA-F]{4})", lambda m: chr(int(m.group(1), 16)), s)
    return {unescape(k): v for k, v in pairs}


def test_python_and_csharp_slug_tables_agree():
    assert _csharp_slugs() == bs.SLUGS


def test_every_summon_char_has_a_slug():
    """字表新增召唤字而没配形象 → 那只召唤物回落成纯字牌格。"""
    missing = sorted(_summon_chars() - set(bs.SLUGS))
    assert missing == [], f"这些召唤字没有形象 slug:{missing}"


def test_no_slug_points_at_a_retired_summon():
    ghosts = sorted(set(bs.SLUGS) - _summon_chars())
    assert ghosts == [], f"slug 表里有字表已经不召的字:{ghosts}"


def test_every_slug_has_a_composer():
    assert set(bs.COMPOSERS) == set(bs.SLUGS.values())


def test_svg_is_wellformed_and_drawable():
    """rsvg-convert 对坏 XML 是静默出空图。"""
    for name, text in bs.assets().items():
        ET.fromstring(text)
        assert any(tag in text for tag in ("<path", "<rect", "<circle")), f"{name} 没有可绘制内容"


def _expected_names():
    names = [f"summon_{slug}_{layer}" for slug in bs.SLUGS.values() for layer in bs.LAYERS]
    return names + [f"summon_fx_{fx}" for fx in bs.FX]


def test_asset_names_match_csharp_layers_and_fx():
    """C# 侧按 Layers / Fx(name) 拼 key 取图,与脚本产出的名字必须同一套。"""
    src = CSHARP.read_text(encoding="utf-8")
    layers = re.search(r'Layers = \{(.*?)\};', src).group(1)
    assert re.findall(r'"(\w+)"', layers) == list(bs.LAYERS)
    assert set(bs.assets()) == set(_expected_names())


def test_svg_mirror_is_current():
    """仓库里的 svg 镜像要与脚本当前产出逐字相同 —— 改了脚本没重跑,PNG 就是旧的。"""
    stale = [name for name, text in bs.assets().items()
             if not (bs.SVG_DIR / f"{name}.svg").exists()
             or (bs.SVG_DIR / f"{name}.svg").read_text(encoding="utf-8") != text]
    assert stale == [], f"这些 svg 与脚本产出不一致,重跑 build_summons.py:{stale}"


@pytest.mark.parametrize("name", _expected_names())
def test_png_and_meta_exist(name):
    png = bs.OUT_DIR / f"{name}.png"
    assert png.exists(), f"缺 {png.name}"
    # PNG 头里的宽高(IHDR),与 CANVAS 对得上才说明是脚本出的图而不是手放的
    head = png.read_bytes()[:24]
    assert head[:8] == b"\x89PNG\r\n\x1a\n"
    w, h = int.from_bytes(head[16:20], "big"), int.from_bytes(head[20:24], "big")
    assert (w, h) == (bs.CANVAS, bs.CANVAS), f"{png.name} 尺寸 {w}×{h}"
    assert png.with_name(png.name + ".meta").exists(), f"缺 {png.name}.meta"


def test_meta_guids_are_unique():
    """.meta 是从模板复制的,忘了换 guid 会让 Unity 把两张图认成同一个资产。"""
    guids = {}
    for meta in bs.OUT_DIR.glob("*.meta"):
        guid = re.search(r"^guid: ([0-9a-f]{32})$", meta.read_text(encoding="utf-8"), re.M).group(1)
        assert guid not in guids, f"{meta.name} 与 {guids.get(guid)} guid 相同"
        guids[guid] = meta.name
    template = re.search(r"^guid: (\w+)$", bs.META_TEMPLATE.read_text(encoding="utf-8"), re.M).group(1)
    assert template not in guids, "有 .meta 沿用了模板的 guid"


def test_no_stray_pngs():
    """反向:Resources 里不留脚本不再产出的图(删召唤字时忘了删 PNG)。"""
    expected = set(_expected_names())
    stray = sorted(p.stem for p in bs.OUT_DIR.glob("*.png") if p.stem not in expected)
    assert stray == [], f"Resources 里有脚本不产出的图:{stray}"
