"""字怪立绘的四方对账:enemies.json ↔ 两张 slug 表 ↔ svg-done 合并稿 ↔ Unity Resources。

为什么要这条:字怪形象经手四个地方,而它们**没有任何一个是从另一个生成的** ——
`rasterize_mobs.MINION_SLUGS`(中文 id → 拼音)、`MobAssets.MINION_SLUGS`(同一张表,C# 侧)、
设计侧的合并稿、以及最终进包的 PNG。改一处漏一处不会有任何东西报错:
真机上那只怪安静地回落成字牌格,而离线编译、Core 单测、字体子集测试**全都是绿的**。
这正是本仓库反复栽过的那类「两张表各改各的」——这条测试就是那根绊线。

⚠ 覆盖名单刻意**从 enemies.json 反查**(与 test_glyph_refs 相反):底稿那条守的是
「新怪该不该配立绘」这个人来拍板的决定,而到了这一层,决定已经做完了 ——
只要 enemies.json 里有它、slug 表里认它,那四份产物就必须齐。
"""
import json
import re
import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

import rasterize_mobs as rm

ROOT = Path(__file__).resolve().parents[3]
ENEMIES = ROOT / "Brushblade/Assets/StreamingAssets/config/enemies.json"
CSHARP = ROOT / "Brushblade/Assets/_Project/Presentation/Mobs/MobAssets.cs"
SVG_DONE = ROOT / "docs/design/glyph-refs/svg-done"
RESOURCES = ROOT / "Brushblade/Assets/_Project/Presentation/Mobs/Resources"


def _config():
    return json.loads(ENEMIES.read_text(encoding="utf-8"))


def _minion_ids():
    """enemies.json 里的杂兵 = 没有 phases 的那些(有 phases 的是成语 Boss)。"""
    return [e["id"] for e in _config()["enemies"] if not e.get("phases")]


def _csharp_slugs():
    """从 MobAssets.cs 里抠出 MINION_SLUGS —— 只取那个字典块,别把 BossStages 也扫进来。"""
    src = CSHARP.read_text(encoding="utf-8")
    block = re.search(r"MinionSlugs = new\(\)\s*\{(.*?)\n        \};", src, re.S)
    assert block, "MobAssets.cs 里找不到 MinionSlugs 字典"
    return dict(re.findall(r'\{\s*"([^"]+)",\s*"([^"]+)"\s*\}', block.group(1)))


def test_python_and_csharp_slug_tables_agree():
    """两张对照表必须逐条相同 —— 它们是同一份事实的两个副本,没有第三方生成它们。"""
    assert _csharp_slugs() == rm.MINION_SLUGS


def test_every_minion_in_config_has_a_slug():
    """enemies.json 里的每只杂兵都要认领一个 slug,否则真机上它回落成字牌格。"""
    missing = [i for i in _minion_ids() if i not in rm.MINION_SLUGS]
    assert missing == [], f"这些怪没有立绘 slug:{missing}"


def test_no_slug_points_at_a_retired_enemy():
    """反向:slug 表里不留幽灵条目(删怪时忘了删这里,是本仓库的老毛病)。"""
    ids = set(_minion_ids())
    ghosts = [i for i in rm.MINION_SLUGS if i not in ids]
    assert ghosts == [], f"slug 表里有 enemies.json 已经没有的怪:{ghosts}"


@pytest.mark.parametrize("layer", ["body", "face", "wisp"])
def test_every_slug_has_all_three_layers_in_resources(layer):
    """三层缺一层就少一半动效:body 缺 = 整只回落,face 缺 = 没有眼睛,wisp 缺 = 不会飘。
    MobView.Init 只在 body 缺失时返回 false,另两层是**静默**跳过的。"""
    missing = [slug for slug in rm.MINION_SLUGS.values()
               if not (RESOURCES / f"enemy_{slug}_{layer}.png").exists()]
    assert missing == [], f"这些怪缺 {layer} 层:{missing}"


def test_every_boss_stage_has_all_three_layers():
    """Boss 按阶段出图,四个阶段是四套;「倒」「海」两阶段是复用的,不重复要求。"""
    missing = []
    for stages in rm.BOSS_STAGES.values():
        for prefix in stages:
            for layer in ("body", "face", "wisp"):
                png = RESOURCES / f"{prefix}_{layer}.png"
                if not png.exists():
                    missing.append(png.name)
    assert sorted(set(missing)) == []


def test_every_png_has_a_unity_meta():
    """没有 .meta 的资产在别人机器上会被 Unity 重新生成一个新 guid ——
    引用不会断(Resources.Load 走路径不走 guid),但每次拉代码都会多出一堆改动。"""
    missing = [p.name for p in sorted(RESOURCES.glob("*.png"))
               if not p.with_suffix(".png.meta").exists()]
    assert missing == [], f"这些 PNG 缺 .meta:{missing}"


def test_design_source_exists_for_every_minion_slug():
    """每只怪都要有设计侧的合并稿 —— 改稿只改这一份,后面三步是脚本。

    ⚠ 2026-08-29 补的五只(涂改/铁画/镇纸/洇痕/衍文)当时是直接出的分层文件,没回写
    合并稿,所以这里放行它们:是一笔待补的账,不是「不需要」。补齐后把它们从豁免里删掉。"""
    without_source = {"tugai", "tiehua", "zhenzhi", "yinhen", "yanwen"}
    missing = [slug for slug in rm.MINION_SLUGS.values()
               if slug not in without_source and not (SVG_DONE / f"mob_{slug}.svg").exists()]
    assert missing == [], f"这些怪没有设计侧合并稿:{missing}"
