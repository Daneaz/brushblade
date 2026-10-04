import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from extract_traits import extract_traits  # noqa: E402
from export_chars import build_all  # noqa: E402

_TABLE = """# 火系特性表

| 字 | 槽 | 面 | 形态 | 替换 | 名 | 效果配置 | 实现 |
|---|---|---|---|---|---|---|---|
| 炎 | Lv1 | 两面 | 主动 | — | 炎灼 | `BurnSingle 3` | ✅ |
| 炎 | Lv3 | 两面 | 主动 | Lv1 | 炎灼强化 | `BurnSingle 4` | ✅ |
| 炎 | Lv4 | 两面 | 被动 | — | 双焰 | `BurnSingle 2` | ✅ |
| 炎 | Lv5 | 攻 | 主动 | — | 火上浇油 | `DamageSingle 50` | ✅ |
| 炎 | Lv5 | 燃 | 主动 | — | 续火 | `BurnSingle 2` | ⏳ |
"""


def test_only_checked_rows_exported():
    traits = extract_traits(_TABLE)
    names = [t["name"] for t in traits["炎"]]
    assert names == ["炎灼", "炎灼强化", "双焰", "火上浇油"]


def test_face_form_replaces_mapping_omits_defaults():
    t = {x["name"]: x for x in extract_traits(_TABLE)["炎"]}
    assert "face" not in t["炎灼"] and "form" not in t["炎灼"] and "replaces" not in t["炎灼"]
    assert t["炎灼强化"]["replaces"] == "Lv1"
    assert t["双焰"]["form"] == "Passive"
    assert t["火上浇油"]["face"] == "Attack"
    assert t["火上浇油"]["slot"] == "Lv5"
    assert t["火上浇油"]["effects"] == [{"kind": "DamageSingle", "value": 50}]


@pytest.mark.parametrize("bad", [
    "| 炎 | Lv2 | 攻 | 主动 | — | 坏 | `DamageSingle 1` | ✅ |",
    "| 炎 | Lv5 | 飞 | 主动 | — | 坏 | `DamageSingle 1` | ✅ |",
    "| 炎 | Lv5 | 攻 | 半动 | — | 坏 | `DamageSingle 1` | ✅ |",
    "| 炎 | Lv5 | 攻 | 主动 | — |  | `DamageSingle 1` | ✅ |",
    "| 炎 | Lv5 | 攻 | 主动 | — | 坏 | `DamageSingle 1` `TotallyBogus` | ✅ |",
    "| 炎 | Lv5 | 攻 | 主动 | 坏 | `DamageSingle 1` | ✅ |",
    "| 炎 | Lv5 | 攻 | 主动 | — | 坏 | `DamageSingle 1` | 多 | ✅ |",
    "| 炎 | Lv5 | 攻 | 主动 | — | 坏 | `DamageSingle 1` ✅ |",
    "| 炎 | Lv5 | 攻 | 主动 | — | 坏 | DamageSingle 50 | ✅ |",
])
def test_bad_rows_raise(bad):
    md = "| 字 | 槽 | 面 | 形态 | 替换 | 名 | 效果配置 | 实现 |\n|---|---|---|---|---|---|---|---|\n" + bad + "\n"
    with pytest.raises(ValueError):
        extract_traits(md)


def test_duplicate_slot_face_raises():
    md = ("| 字 | 槽 | 面 | 形态 | 替换 | 名 | 效果配置 | 实现 |\n|---|---|---|---|---|---|---|---|\n"
          "| 炎 | Lv5 | 攻 | 主动 | — | 甲 | `DamageSingle 1` | ✅ |\n"
          "| 炎 | Lv5 | 攻 | 主动 | — | 乙 | `DamageSingle 1` | ✅ |\n")
    with pytest.raises(ValueError):
        extract_traits(md)


_SPEC = """## 二 · 火系

| 字 | 稀 | 效果配置(基础值) | 相生 | **最终值** | 实现 |
|---|---|---|---|---|---|
| 炎 | 🟡金 | `DamageSingle 168` | — | 单体 168 | ✅ |

## 七 · 引擎扩展需求清单
"""


def test_build_all_merges_traits_into_char_entry():
    out = build_all("", _SPEC, extract_traits(_TABLE))
    yan = next(c for c in out["chars"] if c["id"] == "炎")
    assert [t["name"] for t in yan["traits"]] == ["炎灼", "炎灼强化", "双焰", "火上浇油"]


def test_build_all_rejects_traits_for_unknown_char():
    with pytest.raises(ValueError):
        build_all("", _SPEC, {"焱": [{"slot": "Lv1", "name": "x", "effects": []}]})


def test_build_all_without_traits_adds_no_key():
    out = build_all("", _SPEC, {})
    yan = next(c for c in out["chars"] if c["id"] == "炎")
    assert "traits" not in yan


_H = "| 字 | 槽 | 面 | 形态 | 替换 | 名 | 效果配置 | 实现 |\n|---|---|---|---|---|---|---|---|\n"


def test_both_and_single_face_same_slot_raises():
    md = (_H + "| 炎 | Lv5 | 两面 | 主动 | — | 两面五 | `DamageSingle 1` | ✅ |\n"
          "| 炎 | Lv5 | 攻 | 主动 | — | 攻五 | `DamageSingle 1` | ✅ |\n")
    with pytest.raises(ValueError):
        extract_traits(md)


def test_both_and_single_face_different_slots_ok():
    md = (_H + "| 炎 | Lv1 | 两面 | 主动 | — | 甲 | `DamageSingle 1` | ✅ |\n"
          "| 炎 | Lv5 | 攻 | 主动 | — | 乙 | `DamageSingle 1` | ✅ |\n")
    assert len(extract_traits(md)["炎"]) == 2


@pytest.mark.parametrize("cfg", ["—", "-", ""])
def test_empty_effect_config_allowed(cfg):
    md = _H + f"| 炎 | Lv5 | 攻 | 被动 | — | 空 | {cfg} | ✅ |\n"
    assert extract_traits(md)["炎"][0]["effects"] == []


def test_duplicate_across_feature_face_names_raises():
    md = (_H + "| 炎 | Lv5 | 燃 | 主动 | — | 甲 | `BurnSingle 1` | ✅ |\n"
          "| 炎 | Lv5 | 铠 | 主动 | — | 乙 | `BurnSingle 1` | ✅ |\n")
    with pytest.raises(ValueError):
        extract_traits(md)
