"""合并稿拆层——Claude Design 导出的是单文件三层合并稿,工程侧需要分层资产。

层归属靠元素引用的 filter/gradient id 前缀(L0_/L1_/L2_)判定。
最硬的验收在 test_layers_recompose_to_original:三层分别渲染再叠合,
必须与整稿渲染逐像素一致 —— 拆错、丢元素、层序颠倒都会在这条上暴露。
"""
import shutil
import subprocess
import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

import split_layers as sl

SRC = Path(__file__).resolve().parents[3] / "docs/design/glyph-refs/svg-done"


@pytest.fixture(scope="module")
def sample():
    return (SRC / "mob_cuozigui.svg").read_text(encoding="utf-8")


def test_splits_into_three_layers(sample):
    layers = sl.split(sample)
    assert [name for name, _ in layers] == ["body", "face", "wisp"]


def test_each_layer_is_wellformed_svg_with_same_canvas(sample):
    for _, svg in sl.split(sample):
        assert svg.startswith("<svg") and svg.rstrip().endswith("</svg>")
        assert 'viewBox="0 0 512 512"' in svg
        assert 'width="512"' in svg


def test_layer_assignment_follows_id_prefix(sample):
    layers = dict(sl.split(sample))
    # 各层只引用自己那一档的 defs;引用错档说明归属判错了
    assert "L0_rough" in layers["body"] and "L1_fRough" not in _body_of(layers["body"])
    assert "L1_fRough" in layers["face"]
    assert "L2_wRough" in layers["wisp"]


def _body_of(svg: str) -> str:
    """去掉 defs 段,只留可见内容——defs 是整份照抄的,不能用来判断归属。"""
    start = svg.find("</defs>")
    return svg[start:] if start >= 0 else svg


def test_no_visible_element_is_dropped(sample):
    """拆完的可见元素总数 = 原稿的可见元素总数,一个都不能丢。"""
    total = sum(sl.count_visible(svg) for _, svg in sl.split(sample))
    assert total == sl.count_visible(sample)


def test_output_names_map_mob_prefix_to_enemy():
    """资产名对齐工程侧:导出用 mob_,代码里的映射表用 enemy_。"""
    assert sl.output_name("mob_cuozigui.svg", "body") == "enemy_cuozigui_body.svg"
    assert sl.output_name("boss_paishandaohai_2shan.svg", "wisp") == "boss_paishandaohai_2shan_wisp.svg"


@pytest.mark.skipif(shutil.which("rsvg-convert") is None or shutil.which("magick") is None,
                    reason="需要 librsvg 与 imagemagick")
def test_layers_recompose_to_original(sample, tmp_path):
    """端到端:三层分别渲染 → 按序叠合 → 必须与整稿渲染一致。"""
    whole = tmp_path / "whole.png"
    (tmp_path / "whole.svg").write_text(sample, encoding="utf-8")
    _render(tmp_path / "whole.svg", whole)

    stacked = tmp_path / "stacked.png"
    parts = []
    for name, svg in sl.split(sample):
        (tmp_path / f"{name}.svg").write_text(svg, encoding="utf-8")
        _render(tmp_path / f"{name}.svg", tmp_path / f"{name}.png")
        parts.append(str(tmp_path / f"{name}.png"))
    subprocess.run(["magick", parts[0], *sum(([p, "-composite"] for p in parts[1:]), []),
                    str(stacked)], check=True)

    # RMSE:滤镜渲染有极微舍入差,给一点余量;拆错层会是数量级的差距
    result = subprocess.run(["magick", "compare", "-metric", "RMSE", str(whole), str(stacked), "null:"],
                            capture_output=True, text=True)
    normalized = float(result.stderr.split("(")[1].split(")")[0])
    assert normalized < 0.02, f"叠合结果与整稿差异过大: {result.stderr}"


def _render(svg: Path, png: Path):
    subprocess.run(["rsvg-convert", "-w", "512", "-h", "512", str(svg), "-o", str(png)], check=True)
