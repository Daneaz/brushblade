import shutil
import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import build_icons

# spec §5.1 的 17 个 key。C# 侧 Icons.Fallback 必须与它逐个对应(见 test_icons.py 的
# test_csharp_fallback_covers_every_icon)——两边任一多一个少一个都是上线空白。
EXPECTED = {
    "burn", "burn_nodecay", "freeze", "slow", "blind", "silence", "curse",
    "seal", "immunity", "reflect", "attack", "morale", "crit", "pierce",
    "defense", "dodge", "speed",
}


def test_icon_set_matches_spec():
    assert set(build_icons.ICONS) == EXPECTED


def test_every_icon_has_drawable_content():
    """空 path 会出一张全透明 PNG —— 上线是看不见的图标,不是报错。"""
    for key, body in build_icons.ICONS.items():
        assert "<path" in body or "<circle" in body, f"{key} 没有可绘制内容"


def test_svg_is_wellformed():
    """rsvg-convert 对坏 XML 是静默出空图,所以先用 ElementTree 解析一遍。"""
    import xml.etree.ElementTree as ET
    for key in build_icons.ICONS:
        ET.fromstring(build_icons.svg(key))


@pytest.mark.skipif(shutil.which("rsvg-convert") is None,
                    reason="需要 rsvg-convert(macOS: brew install librsvg)")
def test_build_produces_every_png(tmp_path, monkeypatch):
    # SVG_DIR 是模块级常量,不跟 out_dir 走 —— 不 patch 的话跑测试会覆写仓库里的
    # tools/icons/svg/。那会让「改了 ICONS 只跑 pytest」这种情况下 SVG 更新而 PNG 没更新,
    # git status 看着像图标已改,Unity 实际加载的还是旧 PNG。
    monkeypatch.setattr(build_icons, "SVG_DIR", tmp_path / "svg")
    build_icons.main(out_dir=tmp_path)
    for key in build_icons.ICONS:
        png = tmp_path / f"icon_{key}.png"
        assert png.exists(), f"缺 {png.name}"
        assert png.stat().st_size > 200, f"{png.name} 太小,多半是空图"
