"""D1 Task 11:池表、池引用展开、面名校验。"""
import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from extract_traits import (TIER_MULTIPLIER, expand_pool_entry, extract_pool,  # noqa: E402
                            extract_traits, load_pool, scale_x)
from extract_values import _parse_effects  # noqa: E402

_H = "| 字 | 槽 | 面 | 形态 | 替换 | 名 | 效果配置 | 实现 |\n|---|---|---|---|---|---|---|---|\n"

_POOL = """# 池
## 通用池
| 名 | 效果配置 | X |
|---|---|---|
| 精进 | `Amplify X` `scope All` | 10 |
| 克敌 | `Amplify 15` `scope All` `if Countering` | — |

## 系池
| 系 | 槽 | 面 | 形态 | 名 | 效果配置 | X |
|---|---|---|---|---|---|---|
| 火 | Lv5 | 燃 | 主动 | 续火 | `BurnSingle 2` `if Burning` | — |
| 火 | Lv6 | 攻 | 被动·暴击 | 炽烈 | `BurnSingle 2` | — |
| 金 | Lv5 | 攻 | 主动 | 破甲 | `ArmorBreak X` `turns 3` | 15 |
"""
_CHARS = {"炎": {"element": "Fire", "rarity": "Gold"}, "火": {"element": "Fire", "rarity": "White"},
          "灯": {"element": "Fire", "rarity": "Blue"}}


def _ref(rows, element="火"):
    return extract_traits(_H + rows, element, extract_pool(_POOL), _CHARS)


def test_pool_reference_expands_with_tier_multiplier():
    t = _ref("| 炎 | Lv4 | 两面 | — | — | 通·精进 | — | ✅ |\n"
             "| 炎 | Lv5 | 燃 | — | — | 池·续火 | — | ✅ |\n"
             "| 炎 | Lv6 | 攻 | — | — | 池·炽烈 | — | ✅ |\n")["炎"]
    by = {x["name"]: x for x in t}
    assert by["精进"]["effects"] == [{"kind": "Amplify", "value": 21, "scope": "All"}]  # 10 × 2.1
    assert by["精进"]["form"] == "Passive" and by["精进"]["slot"] == "Lv4" and "face" not in by["精进"]
    assert by["续火"]["face"] == "Feature" and "form" not in by["续火"]
    assert by["炽烈"]["face"] == "Attack" and by["炽烈"]["trigger"] == "OnCrit"
    # 写死的数字不放大
    t2 = _ref("| 炎 | Lv4 | 两面 | — | — | 通·克敌 | — | ✅ |\n")["炎"]
    assert t2[0]["effects"][0]["value"] == 15


def test_x_rounding_half_up():
    assert scale_x(10, "Blue") == 15      # 14.5 → 15(Python round 会给 14)
    assert scale_x(15, "Blue") == 22      # 21.75
    assert scale_x(15, "Purple") == 26    # 26.25
    assert scale_x(15, "Green") == 18
    assert scale_x(10, "White") == 10
    assert scale_x(10, "Red") == 30
    e = _ref("| 灯 | Lv4 | 两面 | — | — | 通·精进 | — | ✅ |\n")["灯"][0]
    assert e["effects"][0]["value"] == 15


@pytest.mark.parametrize("row", [
    "| 炎 | Lv5 | 攻 | — | — | 池·续火 | — | ✅ |",       # 面不一致(续火是燃)
    "| 炎 | Lv6 | 燃 | — | — | 池·续火 | — | ✅ |",       # 槽不一致
    "| 炎 | Lv5 | 两面 | — | — | 通·精进 | — | ✅ |",     # 通用池必须 Lv4
    "| 炎 | Lv4 | 攻 | — | — | 通·精进 | — | ✅ |",       # 通用池必须两面
])
def test_pool_reference_face_mismatch_rejected(row):
    with pytest.raises(ValueError):
        _ref(row + "\n")


@pytest.mark.parametrize("row", [
    "| 炎 | Lv5 | 攻 | — | — | 池·破甲 | — | ✅ |",       # 破甲是金系,炎是火
    "| 炎 | Lv5 | 燃 | — | — | 池·不存在 | — | ✅ |",
    "| 炎 | Lv5 | 燃 | 主动 | — | 池·续火 | — | ✅ |",    # 形态必须写 —
    "| 炎 | Lv5 | 燃 | — | — | 池·续火 | `BurnSingle 2` | ✅ |",
    "| 路 | Lv5 | 燃 | — | — | 池·续火 | — | ✅ |",       # 字不在详表
])
def test_pool_element_mismatch_rejected(row):
    with pytest.raises(ValueError):
        _ref(row + "\n")


@pytest.mark.parametrize("element,face,ok", [
    ("火", "燃", True), ("火", "攻", True), ("火", "两面", True), ("火", "铠", False),
    ("金", "铠", True), ("金", "燃", False), ("水", "润", True), ("土", "固", True),
    ("木", "生", True), ("木", "固", False),
])
def test_face_name_must_belong_to_element(element, face, ok):
    md = _H + f"| 炎 | Lv5 | {face} | 主动 | — | 甲 | `BurnSingle 1` | ✅ |\n"
    if ok:
        assert extract_traits(md, element)
    else:
        with pytest.raises(ValueError):
            extract_traits(md, element)


def test_lv1_row_name_only_allowed():
    md = _H + "| 炎 | Lv1 | 两面 | 主动 | — | 灼烧 | — | ✅ |\n"
    t = extract_traits(md, "火")["炎"][0]
    assert t["name"] == "灼烧" and t["effects"] == []


def test_pool_x_column_must_match_config():
    with pytest.raises(ValueError):
        extract_pool(_POOL.replace("| 15 |", "| — |"))
    with pytest.raises(ValueError):
        extract_pool(_POOL.replace("`BurnSingle 2` `if Burning` | — |", "`BurnSingle 2` `if Burning` | 3 |"))


def test_augment_multiple_pairs_follow_order():
    # 冰锁:一格两条 Augment,of / field 按出现顺序一一对应
    assert _parse_effects("`Augment 1` `of Freeze` `field Turns` + `Augment 2` `of Slow` `field Count`", "冰") == [
        {"kind": "Augment", "value": 1, "augmentKind": "Freeze", "augmentField": "Turns"},
        {"kind": "Augment", "value": 2, "augmentKind": "Slow", "augmentField": "Count"}]


def test_real_pool_every_entry_parses_at_every_tier():
    """池表与 extract_values 的 token 对账守卫:任何一条在任何档位解析失败即红。"""
    pool = load_pool(Path(__file__).resolve().parents[3])
    assert len(pool) >= 50
    for key, entry in pool.items():
        for rarity in TIER_MULTIPLIER:
            try:
                effects = expand_pool_entry(entry, rarity)
            except ValueError as err:
                pytest.fail(f"池条目 {key} 在 {rarity} 档解析失败:{err}")
            assert effects, f"池条目 {key} 解析为空"
