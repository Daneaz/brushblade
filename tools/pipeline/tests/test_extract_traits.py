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


def test_form_trigger_suffix_parsed():
    table = _TABLE + "| 炎 | Lv6 | 两面 | 被动·暴击 | — | 暴焰 | `BurnSingle 1` | ✅ |\n" \
                     "| 炎 | Lv8 | 两面 | 被动·击杀 | — | 乘胜 | `BurnSingle 1` | ✅ |\n"
    t = {x["name"]: x for x in extract_traits(table)["炎"]}
    assert t["暴焰"]["form"] == "Passive" and t["暴焰"]["trigger"] == "OnCrit"
    assert t["乘胜"]["form"] == "Passive" and t["乘胜"]["trigger"] == "OnKill"
    assert "trigger" not in t["双焰"]


def test_active_with_trigger_rejected():
    bad = _TABLE + "| 炎 | Lv6 | 两面 | 主动·暴击 | — | 坏 | `BurnSingle 1` | ✅ |\n"
    with pytest.raises(ValueError):
        extract_traits(bad)


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


def test_backticked_turns_token_is_a_modifier_not_an_effect():
    """池表与特性表把回合数写成 `turns N`(带反引号):它是挂在前面效果上的修饰,不是一条 kind=turns 的效果
    (D1 Task 13:曾落成 {"kind": "turns"} 让 ConfigLoader 加载期报「效果类型未知」)。"""
    md = _H + "| 炎 | Lv3 | 燃 | 主动 | Lv1 | 甲 | `BurnSingle 4` `Weaken 15` `turns 3` | ✅ |\n"
    effects = extract_traits(md)["炎"][0]["effects"]
    assert [e["kind"] for e in effects] == ["BurnSingle", "Weaken"]
    assert effects[1]["turns"] == 3


def _ref_effects(chars, pool_rows, name):
    from extract_traits import extract_pool
    pool = extract_pool("| 系 | 槽 | 面 | 形态 | 名 | 效果配置 | X |\n|---|---|---|---|---|---|---|\n" + pool_rows)
    md = _H + f"| 炸 | Lv5 | 攻 | — | — | {name} | — | ✅ |\n"
    return extract_traits(md, "火", pool, chars)["炸"][0]["effects"]


_AOE = {"炸": {"rarity": "Blue", "attackEffects": [{"kind": "DamageSingle", "value": 4, "shape": "All"}]}}
_SINGLE = {"炸": {"rarity": "Blue", "attackEffects": [{"kind": "DamageSingle", "value": 4}]}}
_ROWS = ("| 火 | Lv5 | 攻 | 主动 | 引燃 | `BurnSettleNow` `keep` | — |\n"
         "| 火 | Lv5 | 攻 | 主动 | 凝冰 | `Freeze 1` `pick Random` | — |\n"
         "| 火 | Lv5 | 攻 | 主动 | 爆燃 | `Amplify 30` `scope Damage` | — |\n")


def test_pool_ref_on_all_target_face_gets_pick_all():
    """Ruling 17:池条目落到全体面(本体不选目标)时,单体敌方效果补 pick All。"""
    assert _ref_effects(_AOE, _ROWS, "池·引燃")[0]["pick"] == "All"


def test_pool_ref_on_single_target_face_keeps_primary():
    assert "pick" not in _ref_effects(_SINGLE, _ROWS, "池·引燃")[0]


def test_pool_ref_explicit_pick_not_overridden():
    assert _ref_effects(_AOE, _ROWS, "池·凝冰")[0]["pick"] == "Random"


def test_pool_ref_trigger_entries_not_retargeted():
    """被动·暴击 / 被动·击杀 的反应自带目标，全体面也不补 pick All。"""
    from extract_traits import extract_pool
    pool = extract_pool("| 系 | 槽 | 面 | 形态 | 名 | 效果配置 | X |\n|---|---|---|---|---|---|---|\n"
                        "| 火 | Lv6 | 攻 | 被动·暴击 | 炽烈 | `BurnSingle 2` | — |\n")
    md = _H + "| 炸 | Lv6 | 攻 | — | — | 池·炽烈 | — | ✅ |\n"
    assert "pick" not in extract_traits(md, "火", pool, _AOE)["炸"][0]["effects"][0]


def test_pool_ref_non_enemy_kinds_untouched_on_all_target_face():
    assert "pick" not in _ref_effects(_AOE, _ROWS, "池·爆燃")[0]
