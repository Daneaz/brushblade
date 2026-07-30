"""decompose:IDS 树递归拆解(只拆上下左右;逐级判定,无五行则回退该级)。"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from decompose import parse_ids_tree, flatten_tree, build_index, split_once, decompose


def index_of(pairs):
    """{字: ids} → 索引(测试用小词表)。"""
    return build_index([{"char": c, "ids": i} for c, i in pairs.items()])


class TestParseIdsTree:
    def test_atomic(self):
        assert parse_ids_tree("火") == "火"

    def test_binary(self):
        assert parse_ids_tree("⿰木木") == ("⿰", ["木", "木"])

    def test_nested(self):
        assert parse_ids_tree("⿱⿰木木火") == ("⿱", [("⿰", ["木", "木"]), "火"])

    def test_ternary_idc(self):
        # ⿲/⿳ 是三元结构符
        assert parse_ids_tree("⿲亻丨丶") == ("⿲", ["亻", "丨", "丶"])

    def test_entity_is_one_token(self):
        assert parse_ids_tree("⿰&CDP-8B7C;寸") == ("⿰", ["&CDP-8B7C;", "寸"])

    def test_malformed_returns_none(self):
        # 操作数不够
        assert parse_ids_tree("⿰木") is None

    def test_flatten_roundtrip(self):
        assert flatten_tree(parse_ids_tree("⿱⿰木木火")) == "⿱⿰木木火"


class TestSplitOnce:
    def test_splits_left_right(self):
        idx = index_of({"林": "⿰木木"})
        assert split_once("林", idx) == ["木", "木"]

    def test_splits_top_bottom(self):
        idx = index_of({"森": "⿱木林", "林": "⿰木木"})
        assert split_once("森", idx) == ["木", "林"]

    def test_enclosing_structure_not_split(self):
        # ⿴ 包围结构不是上下左右,不拆
        idx = index_of({"囚": "⿴囗人"})
        assert split_once("囚", idx) is None

    def test_overlap_structure_not_split(self):
        idx = index_of({"中": "⿻口丨"})
        assert split_once("中", idx) is None

    def test_atomic_char_not_split(self):
        idx = index_of({"木": "木"})
        assert split_once("木", idx) is None

    def test_unknown_char_not_split(self):
        assert split_once("木", index_of({})) is None

    def test_compound_child_resolved_by_reverse_lookup(self):
        # 焱 = ⿱火⿰火火,子树 ⿰火火 反查得「炏」
        idx = index_of({"焱": "⿱火⿰火火", "炏": "⿰火火"})
        assert split_once("焱", idx) == ["火", "炏"]

    def test_unresolvable_compound_child_blocks_split(self):
        # 子树查不到对应真实字 → 整字不拆(不产出无字形部件)
        idx = index_of({"然": "⿱⿰⿴𠂊冫犬灬"})
        assert split_once("然", idx) is None


class TestDecompose:
    def test_forest_expands_all_the_way(self):
        # 森:1级 木+林 → 2级 木+木+木(林拆出五行,接受)
        idx = index_of({"森": "⿱木林", "林": "⿰木木", "木": "木"})
        assert decompose("森", idx) == ["木", "木", "木"]

    def test_zao_stops_at_pin(self):
        # 燥:1级 火+喿 → 2级 火+品+木 → 3级 品=口+吅 无五行,回退
        idx = index_of({"燥": "⿰火喿", "喿": "⿱品木", "品": "⿱口吅",
                        "吅": "⿰口口", "口": "口", "木": "木", "火": "⿱八人"})
        assert decompose("燥", idx) == ["火", "品", "木"]

    def test_level_without_wuxing_rolls_back(self):
        # 照:1级 昭+灬(灬为火,接受) → 2级 昭=日+召 无五行,回退到 昭+灬
        idx = index_of({"照": "⿱昭灬", "昭": "⿰日召", "召": "⿱刀口", "灬": "灬"})
        assert decompose("照", idx) == ["昭", "灬"]

    def test_first_level_always_splits(self):
        # 第1级是字本身的配方,即使直接子部件无五行也要拆
        idx = index_of({"燚": "⿱炎炎", "炎": "⿱火火", "火": "⿱八人"})
        assert decompose("燚", idx, max_complexity=4) == ["火", "火", "火", "火"]

    def test_rolls_back_level_that_exceeds_max_complexity(self):
        # 燚 全展开 4 个 > 3 → 回退到上一级 炎+炎
        idx = index_of({"燚": "⿱炎炎", "炎": "⿱火火", "火": "⿱八人"})
        assert decompose("燚", idx, max_complexity=3) == ["炎", "炎"]

    def test_wuxing_component_is_terminal(self):
        # 火 本身是五行部件,不再往下拆成 八+人
        idx = index_of({"灯": "⿰火丁", "火": "⿱八人", "丁": "丁"})
        assert decompose("灯", idx) == ["火", "丁"]

    def test_unsplittable_char_returns_itself(self):
        assert decompose("木", index_of({"木": "木"})) == ["木"]

    def test_repeated_component_expands_on_both_sides(self):
        # 同一部件在同层出现两次,两边都要展开
        idx = index_of({"棽": "⿱林林", "林": "⿰木木", "木": "木"})
        assert decompose("棽", idx, max_complexity=4) == ["木", "木", "木", "木"]

    def test_cycle_does_not_hang(self):
        # 互相引用的畸形数据不能死循环
        idx = index_of({"甲": "⿰乙木", "乙": "⿰甲木", "木": "木"})
        assert decompose("甲", idx, max_complexity=9)
