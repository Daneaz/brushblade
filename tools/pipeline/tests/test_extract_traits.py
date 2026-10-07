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


def test_pool_ref_on_self_only_feature_face_gets_pick_all():
    """五行面本体只作用自身(固面:护盾 + 护甲)、池条目打敌人且没写 pick → 补 pick All;
    同一面里本体的自身效果原样不动,显式 pick 不覆盖。见 _body_needs_enemy_target 的边界注释。"""
    from extract_traits import extract_pool
    chars = {"崩": {"rarity": "Green",
                   "effects": [{"kind": "Shield", "value": 64}, {"kind": "DefenseBuff", "value": 20, "turns": 2}],
                   "attackEffects": [{"kind": "DamageSingle", "value": 65}]}}
    pool = extract_pool("| 系 | 槽 | 面 | 形态 | 名 | 效果配置 | X |\n|---|---|---|---|---|---|---|\n"
                        "| 土 | Lv5 | 固 | 主动 | 震慑 | `Slow 1` `Shield 5` | — |\n"
                        "| 土 | Lv5 | 固 | 主动 | 点震 | `Slow 1` `pick Random` | — |\n")
    md = _H + "| 崩 | Lv5 | 固 | — | — | 池·震慑 | — | ✅ |\n"
    effects = extract_traits(md, "土", pool, chars)["崩"][0]["effects"]
    assert effects[0]["kind"] == "Slow" and effects[0]["pick"] == "All"
    assert effects[1]["kind"] == "Shield" and "pick" not in effects[1]
    md2 = _H + "| 崩 | Lv5 | 固 | — | — | 池·点震 | — | ✅ |\n"
    assert extract_traits(md2, "土", pool, chars)["崩"][0]["effects"][0]["pick"] == "Random"


# ---------------- D2-0 Task 7:拆字 / 成字特性与部件印记 ----------------

def test_glyph_forms_map_to_passive_triggers():
    md = (_H + "| 鍂 | Lv4 | 两面 | 拆字 | — | 双金 | `Morale 2` | ✅ |\n"
          "| 垚 | Lv4 | 两面 | 成字 | — | 三土 | `Shield 30` | ✅ |\n")
    t = extract_traits(md)
    assert t["鍂"][0]["form"] == "Passive" and t["鍂"][0]["trigger"] == "OnDismantle"
    assert t["垚"][0]["form"] == "Passive" and t["垚"][0]["trigger"] == "OnCompose"
    assert "partChar" not in t["鍂"][0]


def test_part_token_parsed_to_part_char_and_count():
    md = _H + "| 炎 | Lv4 | 两面 | 拆字 | — | 双焰 | `part 火 2` `BurnSingle 2` | ✅ |\n"
    t = extract_traits(md)["炎"][0]
    assert t["partChar"] == "火" and t["partCount"] == 2
    assert t["effects"] == [{"kind": "BurnSingle", "value": 2}]


@pytest.mark.parametrize("row", [
    "| 炎 | Lv4 | 两面 | 成字 | — | 坏 | `part 火 2` `BurnSingle 2` | ✅ |",   # 印记只配拆字
    "| 炎 | Lv4 | 两面 | 被动 | — | 坏 | `part 火 2` `BurnSingle 2` | ✅ |",
    "| 炎 | Lv4 | 两面 | 拆字 | — | 坏 | `part 火 2` `part 火 1` | ✅ |",   # 只能一条
    "| 炎 | Lv4 | 两面 | 拆字 | — | 坏 | `part 火 0` `BurnSingle 2` | ✅ |",   # 次数 ≥ 1
])
def test_bad_part_rows_raise(row):
    with pytest.raises(ValueError):
        extract_traits(_H + row + "\n")


def test_build_all_part_char_must_be_in_recipe():
    ok = extract_traits(_H + "| 炎 | Lv4 | 两面 | 拆字 | — | 双焰 | `part 火 2` `BurnSingle 2` | ✅ |\n")
    yan = next(c for c in build_all("", _SPEC, ok)["chars"] if c["id"] == "炎")
    assert yan["traits"][0]["partChar"] == "火" and yan["traits"][0]["partCount"] == 2
    bad = extract_traits(_H + "| 炎 | Lv4 | 两面 | 拆字 | — | 火山 | `part 山 1` `BurnAll 1` | ✅ |\n")
    with pytest.raises(ValueError):
        build_all("", _SPEC, bad)


# ---------------- D2-0 Task 8:目标条件通用词条在不选敌的五行面上按精进生效(U6) ----------------
def _general_pool():
    from extract_traits import extract_pool
    return extract_pool("| 名 | 效果配置 | X |\n|---|---|---|\n"
                        "| 精进 | `Amplify X` `scope All` | 10 |\n"
                        "| 克敌 | `Amplify 15` `scope All` `if Countering` | — |\n"
                        "| 先声 | `Amplify X` `scope All` `if FirstCastThisTurn` | 15 |\n")


def _general_pool_effects(short, rarity):
    from extract_traits import expand_pool_entry
    return expand_pool_entry(_general_pool()[("通", short)], rarity)


_JIAN = {"剑": {"rarity": "Blue", "effects": [{"kind": "Shield", "value": 10}],
               "attackEffects": [{"kind": "DamageSingle", "value": 4}]}}


def test_target_conditional_general_splits_on_non_targeting_wuxing_face():
    """剑(金,蓝):铠面不选敌 → 克敌拆成攻击面原条目 + 五行面按蓝档精进(10×1.45→15)。"""
    md = _H + "| 剑 | Lv4 | 两面 | — | — | 通·克敌 | — | ✅ |\n"
    traits = extract_traits(md, "金", _general_pool(), _JIAN)["剑"]
    assert [t["face"] for t in traits] == ["Attack", "Feature"]
    assert all(t["name"] == "克敌" and t["form"] == "Passive" for t in traits)
    assert traits[0]["effects"] == _general_pool_effects("克敌", "Blue")
    assert traits[1]["effects"] == _general_pool_effects("精进", "Blue")
    assert traits[1]["effects"][0]["value"] == 15


def test_target_conditional_general_stays_one_both_face_when_wuxing_face_targets():
    """炸(火):燃面选敌 → 克敌仍是一条两面(无 face)。"""
    chars = {"炸": {"rarity": "Blue", "effects": [{"kind": "BurnSingle", "value": 3}],
                   "attackEffects": [{"kind": "DamageSingle", "value": 4}]}}
    md = _H + "| 炸 | Lv4 | 两面 | — | — | 通·克敌 | — | ✅ |\n"
    traits = extract_traits(md, "火", _general_pool(), chars)["炸"]
    assert len(traits) == 1 and "face" not in traits[0]


def test_non_target_conditional_general_not_split():
    """先声不看目标,不拆。"""
    md = _H + "| 剑 | Lv4 | 两面 | — | — | 通·先声 | — | ✅ |\n"
    traits = extract_traits(md, "金", _general_pool(), _JIAN)["剑"]
    assert len(traits) == 1 and "face" not in traits[0]


def test_split_pair_conflicts_with_explicit_single_face_row():
    """拆出的两条按 (槽, 面) 记:同槽再写一条攻面仍报重复。"""
    md = (_H + "| 剑 | Lv4 | 两面 | — | — | 通·克敌 | — | ✅ |\n"
          "| 剑 | Lv4 | 攻 | 被动 | — | 甲 | `BurnSingle 2` | ✅ |\n")
    with pytest.raises(ValueError):
        extract_traits(md, "金", _general_pool(), _JIAN)


# ---------------- D2-火 Task 1:特性级出字内次数上限 `limit N` ----------------

def test_d2fire_limit_token_parsed_to_max_per_cast():
    md = _H + "| 爆 | Lv8 | 攻 | 被动·击杀 | — | 连爆 | `DamageSingle 0` `All` `bodyPercent 100` `limit 2` | ✅ |\n"
    t = extract_traits(md)["爆"][0]
    assert t["maxPerCast"] == 2
    assert t["effects"] == [{"kind": "DamageSingle", "value": 0, "shape": "All", "bodyPercent": 100}]


@pytest.mark.parametrize("row", [
    "| 爆 | Lv8 | 攻 | 被动·击杀 | — | 坏 | `Morale 1` `limit 0` | ✅ |",            # ≥ 1
    "| 爆 | Lv8 | 攻 | 被动·击杀 | — | 坏 | `Morale 1` `limit 1` `limit 2` | ✅ |",   # 只能一条
])
def test_d2fire_bad_limit_rows_raise(row):
    with pytest.raises(ValueError):
        extract_traits(_H + row + "\n")
