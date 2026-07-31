"""卡池候选筛选表生成:GB 常用度分级、部件变体归组、池内打分分档。"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from report_pool_candidates import gb_level, variant_of, rate_pool


def test_gb_level_common():
    assert gb_level("火") == 1


def test_gb_level_secondary():
    assert gb_level("焱") == 2


def test_gb_level_rare():
    assert gb_level("㷋") == 0


def test_variant_of_picks_element_part():
    assert variant_of(["氵", "工"], ["水", "氵", "冫"]) == "氵"


def test_variant_of_none_when_absent():
    assert variant_of(["工", "口"], ["水", "氵", "冫"]) is None


def _cand(char, parts1, leaves):
    return {"char": char, "parts1": parts1, "leaves": leaves,
            "complexity": len(leaves)}


def test_rate_pool_ranks_by_component_reuse():
    # 甲 的部件能组两个字、乙 的组一个、丙 的组不出 → 分数递减
    pool = [_cand("甲", ["火"], ["火"]), _cand("乙", ["木"], ["木"]),
            _cand("丙", ["品"], ["品"])]
    graph = {"火": {"灯", "炎"}, "木": {"林"}}
    rated = rate_pool(pool, graph)
    assert rated["甲"][0] > rated["乙"][0] > rated["丙"][0]


def test_rate_pool_exposes_both_metrics():
    pool = [_cand("燥", ["火", "喿"], ["火", "品", "木"])]
    graph = {"火": {"灯"}, "木": {"林"}, "喿": {"噪"}}
    _, _, effective, production = rate_pool(pool, graph)["燥"]
    assert effective == 2          # 火/木 有效,品 组不出字
    assert production == 3         # 火1 + 喿1 + 木1 + 品0


def test_rate_pool_assigns_a_rarity_to_every_char():
    pool = [_cand(c, ["火"], ["火"]) for c in "甲乙丙丁戊己庚辛"]
    rated = rate_pool(pool, {"火": {"灯"}})
    assert all(r[1] in "白绿蓝紫橙红" for r in rated.values())


# ---- 多属性字(跨属性组合,第 6 章) ----

from report_pool_candidates import extended_attrs, relation_label


def test_extended_attrs_resolves_stack_chars():
    assert extended_attrs(["氵", "林"]) == ["木", "水"]


def test_extended_attrs_plain_parts():
    assert extended_attrs(["火", "土"]) == ["火", "土"]


def test_extended_attrs_dedup_and_ignore_neutral():
    assert extended_attrs(["火", "灬", "口"]) == ["火"]


def test_relation_sheng():
    assert relation_label(("木", "火")) == "木生火"


def test_relation_sheng_reversed_input():
    assert relation_label(("木", "水")) == "水生木"


def test_relation_ke():
    assert relation_label(("木", "土")) == "木克土"


def test_relation_heart():
    assert relation_label(("土", "心")) == "心+土"


def test_displayable_basic_cjk():
    from report_pool_candidates import is_displayable
    assert is_displayable("火") is True


def test_displayable_rejects_ext_b():
    from report_pool_candidates import is_displayable
    assert is_displayable("𣏹") is False


def test_displayable_rejects_ext_a():
    from report_pool_candidates import is_displayable
    assert is_displayable("㷋") is False
