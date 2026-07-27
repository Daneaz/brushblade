"""字形底稿(出图 AI 的 ControlNet 输入)——覆盖表与渲染约束。

底稿的唯一职责:把字形准确、居中、等视觉大小地摆进画布。
模型画不准汉字,所以字形必须由字体渲染供给,这些测试守的就是这条线。
"""
import re
import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

import glyph_refs as gr


# ---- 覆盖表 ----

def test_covers_all_nine_minions():
    """9 只杂兵每只一个字形(《敌人形象关键词包》§5)。"""
    minions = {
        "错字鬼", "缺笔妖", "标点小妖", "叠字怪", "夯土妖",
        "通假字", "生僻字", "墨渍", "焦痕",
    }
    assert {job.owner for job in gr.JOBS if job.kind == "minion"} == minions


def test_covers_three_bosses_four_phases_each():
    """3 只 Boss × 4 阶段(§6)。"""
    phases = [(job.owner, job.char) for job in gr.JOBS if job.kind == "boss"]
    assert len(phases) == 12
    for boss in ("排山倒海", "翻江倒海", "雷霆万钧"):
        assert sum(1 for owner, _ in phases if owner == boss) == 4


def test_shared_phases_render_once():
    """「倒」「海」两阶段两只 Boss 共用,底稿只出一次 —— 12 张降到 10 张(§6.2)。"""
    boss_chars = [job.char for job in gr.JOBS if job.kind == "boss"]
    assert boss_chars.count("倒") == 2 and boss_chars.count("海") == 2  # 两只 Boss 都用
    rendered = gr.render_plan()
    boss_files = [t.filename for t in rendered if t.kind == "boss"]
    assert len(boss_files) == len(set(boss_files)) == 10


def test_every_glyph_exists_in_font():
    """选用的字必须在 Noto Serif SC 里有字形 —— 生僻字尤其容易踩空。"""
    cmap = gr.font_cmap()
    missing = [t.char for t in gr.render_plan() if ord(t.char) not in cmap]
    assert missing == []


# ---- 渲染约束 ----

@pytest.fixture(scope="module")
def svg_of():
    cache = {}

    def render(char):
        if char not in cache:
            cache[char] = gr.render_svg(char)
        return cache[char]

    return render


def test_svg_is_wellformed_with_canvas_size(svg_of):
    svg = svg_of("错")
    assert svg.startswith("<svg") and svg.rstrip().endswith("</svg>")
    assert f'viewBox="0 0 {gr.CANVAS} {gr.CANVAS}"' in svg
    assert "<path" in svg and ' d="M' in svg  # 有真实轮廓数据


def test_glyph_stays_inside_safe_margin(svg_of):
    """字不能出框:受击位移(±11px)要有余量,§2 构图约束第 3 条。"""
    box = gr.path_bounds(svg_of("龘"))  # 最繁复的字,最容易撑破
    safe = gr.CANVAS * gr.MARGIN
    assert box.x0 >= safe - 1 and box.y0 >= safe - 1
    assert box.x1 <= gr.CANVAS - safe + 1 and box.y1 <= gr.CANVAS - safe + 1


@pytest.mark.parametrize("char", ["错", "龘", "一", "、"])
def test_autofit_normalizes_visual_size(svg_of, char):
    """等视觉大小:顿号这种小字符与繁复字撑满同一个框。
    不做 autofit 的话「、」会渲染成一个小点,底稿就废了。"""
    box = gr.path_bounds(svg_of(char))
    target = gr.CANVAS * (1 - 2 * gr.MARGIN)
    assert max(box.width, box.height) == pytest.approx(target, abs=2)


def test_glyph_is_centered(svg_of):
    box = gr.path_bounds(svg_of("焦"))
    center = gr.CANVAS / 2
    assert box.cx == pytest.approx(center, abs=2)
    assert box.cy == pytest.approx(center, abs=2)


def test_weight_axis_applied():
    """可变轴 wght:Boss 用更重的字重,压迫感从字形本身就开始(§6.0)。
    度量用填充面积——同一外框内笔画越粗、墨色覆盖越大。"""
    light = gr.render_svg("山", weight=300)
    heavy = gr.render_svg("山", weight=900)
    assert gr.path_area(heavy) > gr.path_area(light) * 1.5
    # autofit 在字重之后生效:两者较长边仍归一到同一目标(笔画变粗会改宽高比,故只比长边)
    target = gr.CANVAS * (1 - 2 * gr.MARGIN)
    for svg in (light, heavy):
        box = gr.path_bounds(svg)
        assert max(box.width, box.height) == pytest.approx(target, abs=2)


def test_filenames_are_stable_and_ascii():
    """文件名要能安全跨平台传给出图 agent:纯 ASCII + 拼音 id。"""
    for task in gr.render_plan():
        assert re.fullmatch(r"[a-z0-9_]+\.svg", task.filename), task.filename
