"""字怪立绘的三方对账:enemies.json ↔ 两张 slug 表 ↔ Unity Resources。

(2026-10-03 起设计侧合并稿 docs/design/glyph-refs/svg-done 退役,源图以 tools/design/mobs/svg/ 为准,
原第四方「每只怪都有合并稿」那条随之移入 docs/design/deprecated/。)

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

import build_boss_art as bba  # noqa: E402
import rasterize_mobs as rm

ROOT = Path(__file__).resolve().parents[3]
ENEMIES = ROOT / "Brushblade/Assets/StreamingAssets/config/enemies.json"
CSHARP = ROOT / "Brushblade/Assets/_Project/Presentation/Mobs/MobAssets.cs"
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


# 待出图(2026-09-30 十层一主题新增,用户拍板「先上数值,形象后补」):这几只在真机上
# 回落成字头像。是一笔**待补的账**,不是「不需要」—— 出完图、接进两张 slug 表后从这里删掉,
# 下面 test_art_pending_is_really_pending 会逼你删。
ART_PENDING: set = set()   # 2026-09-30 那七只已出图(build_mob_drafts.py),清空


def test_every_minion_in_config_has_a_slug():
    """enemies.json 里的每只杂兵都要认领一个 slug,否则真机上它回落成字牌格。"""
    missing = [i for i in _minion_ids() if i not in rm.MINION_SLUGS and i not in ART_PENDING]
    assert missing == [], f"这些怪没有立绘 slug:{missing}"


def test_art_pending_is_really_pending():
    """豁免名单不许过期:已经删掉的怪、或者已经接上 slug 的怪,都得从 ART_PENDING 里拿掉。"""
    ids = set(_minion_ids())
    assert sorted(ART_PENDING - ids) == [], "豁免名单里有 enemies.json 已经没有的怪"
    assert sorted(ART_PENDING & set(rm.MINION_SLUGS)) == [], "这些怪已经有 slug 了,从 ART_PENDING 删掉"


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


def _boss_ids():
    """enemies.json 里的全部 Boss:带 phases 的固定 Boss + 各层段(含精英池)的成语 Boss。"""
    cfg = _config()
    ids = {e["id"] for e in cfg["enemies"] if e.get("phases")}
    for band in cfg["endless"]["bands"]:
        for key in ("idiomBosses", "eliteIdiomBosses"):
            ids.update(i["chars"] for i in band.get(key, []))
    return ids


def _csharp_boss_slugs():
    src = CSHARP.read_text(encoding="utf-8")
    block = re.search(r"BossSlugs = new\(\)\s*\{(.*?)\n        \};", src, re.S)
    assert block, "MobAssets.cs 里找不到 BossSlugs 字典"
    return dict(re.findall(r'\{\s*"([^"]+)",\s*"([^"]+)"\s*\}', block.group(1)))


def test_boss_slug_tables_agree_and_cover_every_boss():
    """成语 Boss 立绘(2026-09-30,一只一张):build_boss_art.BOSSES 与 MobAssets.BossSlugs
    是同一份事实的两个副本;enemies.json 里的每只 Boss 都要有,且不留已删 Boss 的幽灵条目。"""
    python = {name: slug for name, slug, _, _ in bba.BOSSES}
    assert _csharp_boss_slugs() == python
    assert set(python) == _boss_ids()


def test_boss_elements_match_config():
    """立绘不烤属性色,但 face 静态层的虹膜取首阶段属性 —— 与配置对不上说明表抄错了。"""
    cfg = _config()
    phases = {e["id"]: [p["element"] for p in e["phases"]] for e in cfg["enemies"] if e.get("phases")}
    for band in cfg["endless"]["bands"]:
        for key in ("idiomBosses", "eliteIdiomBosses"):
            phases.update({i["chars"]: i["elements"] for i in band.get(key, [])})
    for name, _, elements, _ in bba.BOSSES:
        assert list(elements) == phases[name], name


@pytest.mark.parametrize("layer", bba.LAYER_NAMES)
def test_every_boss_has_every_layer(layer):
    """四个字层缺一层,那个阶段就没有字可亮;iris/eyes 缺了 Boss 没有眼睛。"""
    missing = [slug for _, slug, _, _ in bba.BOSSES
               if not (RESOURCES / f"boss_{slug}_{layer}.png").exists()]
    assert missing == [], f"这些 Boss 缺 {layer} 层:{missing}"
    assert (RESOURCES / "fx_boss_ring.png").exists()


def test_no_stale_boss_assets():
    """旧的「按阶段出图」资产(boss_<slug>_<n><字>_*)已退役,别让它们残留在包里。"""
    stale = [p.name for p in RESOURCES.glob("boss_*.png") if re.match(r"boss_[a-z]+_\d", p.name)]
    assert stale == []


def test_every_png_has_a_unity_meta():
    """没有 .meta 的资产在别人机器上会被 Unity 重新生成一个新 guid ——
    引用不会断(Resources.Load 走路径不走 guid),但每次拉代码都会多出一堆改动。"""
    missing = [p.name for p in sorted(RESOURCES.glob("*.png"))
               if not p.with_suffix(".png.meta").exists()]
    assert missing == [], f"这些 PNG 缺 .meta:{missing}"
