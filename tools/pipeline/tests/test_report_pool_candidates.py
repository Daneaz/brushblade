"""卡池候选筛选表生成:GB 常用度分级、部件变体归组、稀有度起点启发式。"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from report_pool_candidates import gb_level, variant_of, suggest_rarity


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


def _cand(leaves):
    return {"leaves": leaves, "complexity": len(leaves)}


BASE = ["木"]  # 木系本体部件


def test_pure_stack_two_is_green():
    assert suggest_rarity(_cand(["木", "木"]), BASE) == "绿"


def test_pure_stack_three_is_orange():
    assert suggest_rarity(_cand(["木", "木", "木"]), BASE) == "橙"


def test_stack_ingredient_is_purple():
    assert suggest_rarity(_cand(["林", "火"]), ["火", "灬"]) == "紫"


def test_three_parts_is_purple():
    assert suggest_rarity(_cand(["木", "口", "口"]), BASE) == "紫"


def test_exotic_leaf_is_blue():
    assert suggest_rarity(_cand(["火", "夆"]), ["火", "灬"]) == "蓝"


def test_droppable_partner_is_white():
    assert suggest_rarity(_cand(["火", "丁"]), ["火", "灬"]) == "白"


def test_plain_partner_is_green():
    assert suggest_rarity(_cand(["火", "会"]), ["火", "灬"]) == "绿"


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
