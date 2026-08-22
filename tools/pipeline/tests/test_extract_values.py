"""详表里的目标形状 token(Sweep/Cleave/Skewer/ShapePercent/Shots)→ chars.json 字段。

2026-08-22:只修饰单体直伤(DamageSingle),与既有的 Backline/Pierce/HitCount 同属修饰位。
"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from extract_values import _parse_effects


def test_sweep_token_becomes_shape_field():
    assert _parse_effects("`DamageSingle 10` + `Sweep`", "金") == [
        {"kind": "DamageSingle", "value": 10, "shape": "Sweep"}]


def test_cleave_token_becomes_shape_field():
    assert _parse_effects("`DamageSingle 10` + `Cleave`", "金") == [
        {"kind": "DamageSingle", "value": 10, "shape": "Cleave"}]


def test_skewer_token_becomes_shape_field():
    assert _parse_effects("`DamageSingle 10` + `Skewer`", "金") == [
        {"kind": "DamageSingle", "value": 10, "shape": "Skewer"}]


def test_shape_percent_token_becomes_shape_percent_field():
    assert _parse_effects("`DamageSingle 10` + `Sweep` + `ShapePercent 50`", "金") == [
        {"kind": "DamageSingle", "value": 10, "shape": "Sweep", "shapePercent": 50}]


def test_shots_token_becomes_volley_shape_plus_shots():
    assert _parse_effects("`DamageSingle 10` + `Shots 3`", "金") == [
        {"kind": "DamageSingle", "value": 10, "shape": "Volley", "shots": 3}]


def test_no_shape_marker_leaves_shape_field_absent():
    """缺省不写 shape —— 恒等性:87 张既有伤害字重新生成后必须逐字节不变。"""
    effects = _parse_effects("`DamageSingle 10`", "金")
    assert effects == [{"kind": "DamageSingle", "value": 10}]
    assert "shape" not in effects[0]
    assert "shapePercent" not in effects[0]
    assert "shots" not in effects[0]


def test_shots_and_shape_percent_do_not_become_standalone_effects():
    """坑 2:通用正则 `(\\w+) (\\d+)` 会把 `Shots 3` / `ShapePercent 50` 当成独立效果收走 ——
    EffectKind 里没有这两个值,落成独立条目会让 ConfigLoader 在加载期直接抛 ConfigException。"""
    effects = _parse_effects("`DamageSingle 10` + `Shots 3` + `ShapePercent 50`", "金")
    assert all(e["kind"] not in ("Shots", "ShapePercent") for e in effects)
    assert len(effects) == 1


# 召唤物自动攻击的形状(2026-08-22):同一套 token,落进 passive 的 shape/shots/shapePercent,
# 而不是独立 effect —— BattleEngine.cs:1276-1284 读的就是 passive 上这三个字段。

def test_summon_sweep_token_becomes_passive_shape_field():
    effects = _parse_effects("`Summon 1`(10 血/攻 3) + `Sweep`", "刀")
    assert effects == [{"kind": "Summon", "value": 10, "count": 1, "attack": 3,
                         "summonChar": "刀", "passive": {"shape": "Sweep"}}]


def test_summon_cleave_token_becomes_passive_shape_field():
    effects = _parse_effects("`Summon 1`(10 血/攻 3) + `Cleave`", "刀")
    assert effects == [{"kind": "Summon", "value": 10, "count": 1, "attack": 3,
                         "summonChar": "刀", "passive": {"shape": "Cleave"}}]


def test_summon_skewer_token_becomes_passive_shape_field():
    effects = _parse_effects("`Summon 1`(10 血/攻 3) + `Skewer`", "刀")
    assert effects == [{"kind": "Summon", "value": 10, "count": 1, "attack": 3,
                         "summonChar": "刀", "passive": {"shape": "Skewer"}}]


def test_summon_shots_token_becomes_passive_volley_shape_plus_shots():
    effects = _parse_effects("`Summon 1`(10 血/攻 3) + `Shots 3`", "刀")
    assert effects == [{"kind": "Summon", "value": 10, "count": 1, "attack": 3,
                         "summonChar": "刀", "passive": {"shape": "Volley", "shots": 3}}]


def test_summon_shape_percent_token_becomes_passive_field():
    effects = _parse_effects("`Summon 1`(10 血/攻 3) + `Sweep` + `ShapePercent 50`", "刀")
    assert effects == [{"kind": "Summon", "value": 10, "count": 1, "attack": 3,
                         "summonChar": "刀", "passive": {"shape": "Sweep", "shapePercent": 50}}]


def test_summon_no_shape_marker_leaves_passive_without_shape_keys():
    """缺省不写 shape —— 恒等性:既有召唤字(如带 Ranged 的灶/烓)重新生成后必须逐字节不变,
    passive 里不能凭空多出 shape/shots/shapePercent 三个键。"""
    effects = _parse_effects("`Summon 1`(10 血/攻 3) + `Ranged`", "刀")
    assert effects == [{"kind": "Summon", "value": 10, "count": 1, "attack": 3,
                         "summonChar": "刀", "passive": {"ranged": True}}]
    passive = effects[0]["passive"]
    assert "shape" not in passive
    assert "shapePercent" not in passive
    assert "shots" not in passive


def test_summon_with_no_passive_tokens_has_no_passive_key_at_all():
    """没有任何被动 token 的召唤字(多数已有召唤字)——passive 键本身都不该出现。"""
    effects = _parse_effects("`Summon 1`(10 血/攻 3)", "刀")
    assert effects == [{"kind": "Summon", "value": 10, "count": 1, "attack": 3, "summonChar": "刀"}]
    assert "passive" not in effects[0]
