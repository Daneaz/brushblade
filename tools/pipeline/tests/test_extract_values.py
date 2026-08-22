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
