import re
import shutil
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import build_chests

CORE_CHEST = build_chests.ROOT / "Brushblade/Assets/_Project/Core/Chest.cs"
CHEST_ASSETS = (build_chests.ROOT
                / "Brushblade/Assets/_Project/Presentation/Chests/ChestAssets.cs")


def test_tier_set_matches_core_enum():
    """七档必须与 ChestTier 一一对应 —— 少一档就是那一档的箱在主界面画不出来。"""
    src = CORE_CHEST.read_text(encoding="utf-8")
    start = src.index("enum ChestTier")
    names = re.findall(r"^\s{8}(\w+) = \d,", src[start:src.index("}", start)], re.M)
    assert len(names) == 7, f"没解析出七档,解析到 {names}"
    assert [n.lower() for n in names] == list(build_chests.TIERS), \
        f"Core 的档位 {names} 与 build_chests.TIERS {list(build_chests.TIERS)} 对不上"


def test_every_tier_has_a_seam():
    """盖缝位置逐档不同,漏一档就是那只箱「已就绪」时不透光。"""
    assert set(build_chests.SEAMS) == set(build_chests.TIERS)


def test_no_unresolved_color_placeholder():
    """{c} 没替掉会被 rsvg 当成非法颜色静默丢弃 —— 出图是一只没上色的箱。"""
    for name, text in build_chests.assets().items():
        assert "{c}" not in text, f"{name} 里还有没替换的 {{c}}"


def test_svg_is_wellformed():
    """rsvg-convert 对坏 XML 是静默出空图,所以先用 ElementTree 解析一遍。"""
    for name, text in build_chests.assets().items():
        ET.fromstring(text)


def test_every_asset_has_drawable_content():
    for name, text in build_chests.assets().items():
        assert "<path" in text or "<rect" in text or "<ellipse" in text or "<circle" in text, \
            f"{name} 没有可绘制内容"


def test_csharp_slugs_cover_every_tier():
    """C# 侧 ChestAssets.Slugs 与 build_chests.TIERS 必须逐个对应。

    对不上的后果不是编译错,是那一档静默回落成色块 + 首字 —— 只有肉眼能发现。
    """
    src = CHEST_ASSETS.read_text(encoding="utf-8")
    slugs = set(re.findall(r'\{\s*ChestTier\.\w+,\s*"(\w+)"\s*\}', src))
    assert slugs == set(build_chests.TIERS), (
        f"C# 多出: {slugs - set(build_chests.TIERS)};C# 缺少: {set(build_chests.TIERS) - slugs}")


@pytest.mark.skipif(shutil.which("rsvg-convert") is None,
                    reason="需要 rsvg-convert(macOS: brew install librsvg)")
def test_build_produces_every_png(tmp_path, monkeypatch):
    # SVG_DIR 是模块级常量,不跟 out_dir 走 —— 不 patch 的话跑测试会覆写仓库里那份
    # (同 test_icons.py 的理由)。
    monkeypatch.setattr(build_chests, "SVG_DIR", tmp_path / "svg")
    build_chests.main(out_dir=tmp_path)
    for name in build_chests.assets():
        png = tmp_path / f"{name}.png"
        assert png.exists(), f"缺 {png.name}"
        assert png.stat().st_size > 200, f"{png.name} 太小,多半是空图"


def test_repo_svgs_match_generator():
    """仓库里的 chests/svg/*.svg 必须是当前生成器的产物(同 test_icons.py 的缺口)。"""
    for name, text in build_chests.assets().items():
        path = build_chests.SVG_DIR / f"{name}.svg"
        assert path.exists(), f"缺 {path.name},跑一次 build_chests.py"
        assert path.read_text(encoding="utf-8") == text, (
            f"{path.name} 与生成器不同步,跑一次 build_chests.py")


def test_unity_resources_have_every_png():
    """Unity Resources 里少一张就是那一档静默回落 —— 出图与提交要一起走。"""
    for name in build_chests.assets():
        png = build_chests.OUT_DIR / f"{name}.png"
        assert png.exists(), f"缺 {png.name},跑一次 build_chests.py"
        assert png.with_suffix(".png.meta").exists(), (
            f"缺 {png.name}.meta —— GUID 不定下来,换机拉码会重新生成一套")
