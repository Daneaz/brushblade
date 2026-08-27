import shutil
import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import build_icons

# spec §5.1 的 17 个 key,2026-08-26 加护盾共 18 个。C# 侧 Icons.Fallback 必须与它逐个对应
# (见 test_icons.py 的 test_csharp_fallback_covers_every_icon)——两边任一多一个少一个都是上线空白。
EXPECTED = {
    "burn", "burn_nodecay", "freeze", "slow", "blind", "silence", "curse",
    "seal", "immunity", "reflect", "attack", "morale", "crit", "pierce",
    "defense", "dodge", "speed",
    # 护盾(2026-08-26):玩家与召唤物的盾条都用它,取代原来的「护盾 N」/「盾 N」文字。
    # 与 defense(实心盾 = 护甲点数)刻意做成**描边 vs 实心**两种画法 —— 同为盾形,
    # 一眼要分得出「这是可消耗的盾条」还是「这是常驻的减伤点数」。
    "shield",
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


def test_repo_svgs_match_icons():
    """仓库里的 svg/*.svg 必须是当前 ICONS 的产物。

    test_build_produces_every_png 把 SVG_DIR monkeypatch 到 tmp(避免测试写工作树),
    副作用是没人再检查仓库里那份还新不新 —— 改了 ICONS 只跑 pytest 会全绿,
    而 Unity 加载的还是旧图。这条补上那个缺口。
    """
    for key in build_icons.ICONS:
        path = build_icons.SVG_DIR / f"icon_{key}.svg"
        assert path.exists(), f"缺 {path.name},跑一次 build_icons.py"
        assert path.read_text(encoding="utf-8") == build_icons.svg(key), (
            f"{path.name} 与 ICONS 不同步,跑一次 build_icons.py")


def test_csharp_fallback_covers_every_icon():
    """C# 侧 Icons.Glyphs 与 build_icons.ICONS 必须逐个对应。

    对不上的后果不是编译错,是上线后那个状态显示成「?」——只有肉眼能发现。
    """
    import re
    src = (build_icons.ROOT
           / "Brushblade/Assets/_Project/Presentation/UI/Icons.cs").read_text(encoding="utf-8")
    keys = set(re.findall(r'\{\s*"([a-z_]+)"\s*,\s*"[^"]+"\s*\}', src))
    assert keys == set(build_icons.ICONS), (
        f"C# 多出: {keys - set(build_icons.ICONS)};C# 缺少: {set(build_icons.ICONS) - keys}")
