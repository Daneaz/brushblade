"""validate_recipes:配方图谱 DAG 校验(第 4 章 4.9.6)。"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from validate_recipes import validate_recipes


COMPONENTS = {"木", "火", "土"}


class TestValidRecipes:
    def test_valid_dag_returns_no_errors(self):
        recipes = {
            "林": ["木", "木"],
            "焚": ["林", "火"],  # 原料可以是更低阶的字(DAG)
            "圭": ["土", "土"],
        }
        assert validate_recipes(recipes, COMPONENTS) == []


class TestCycleDetection:
    def test_direct_cycle(self):
        recipes = {"甲": ["乙"], "乙": ["甲"]}
        errors = validate_recipes(recipes, set())
        assert any(e["type"] == "cycle" for e in errors)

    def test_self_reference(self):
        errors = validate_recipes({"甲": ["甲"]}, set())
        assert any(e["type"] == "cycle" for e in errors)


class TestMissingIngredient:
    def test_undefined_ingredient_reported_with_location(self):
        recipes = {"焚": ["林", "火"]}  # 林 未定义、火 不在部件表
        errors = validate_recipes(recipes, {"木"})
        missing = [e for e in errors if e["type"] == "missing"]
        assert {(e["char"], e["ingredient"]) for e in missing} == {
            ("焚", "林"),
            ("焚", "火"),
        }


class TestOrphanComponent:
    def test_unused_component_flagged(self):
        recipes = {"林": ["木", "木"]}
        errors = validate_recipes(recipes, {"木", "火"})
        orphans = [e for e in errors if e["type"] == "orphan"]
        assert len(orphans) == 1
        assert orphans[0]["component"] == "火"
