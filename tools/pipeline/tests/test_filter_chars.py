"""filter_chars:部件→五行属性映射(v0.4 含土)+ 候选字筛选。"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from filter_chars import attr_of, attrs_of_leaves, filter_candidates


class TestAttrOf:
    def test_fire_variants(self):
        assert attr_of("火") == "火"
        assert attr_of("灬") == "火"

    def test_water_variants(self):
        assert attr_of("氵") == "水"
        assert attr_of("水") == "水"
        assert attr_of("冫") == "水"

    def test_wood_variants(self):
        assert attr_of("木") == "木"
        assert attr_of("艹") == "木"
        assert attr_of("竹") == "木"

    def test_metal_variants(self):
        assert attr_of("钅") == "金"
        assert attr_of("金") == "金"
        assert attr_of("刂") == "金"
        assert attr_of("刀") == "金"
        assert attr_of("戈") == "金"

    def test_earth_variants_v04(self):
        # v0.4 五行体系收口:土为真实属性(第 3 章 3.4)
        assert attr_of("土") == "土"
        assert attr_of("山") == "土"
        assert attr_of("石") == "土"

    def test_heart_variants(self):
        assert attr_of("心") == "心"
        assert attr_of("忄") == "心"

    def test_neutral_component(self):
        assert attr_of("丁") is None
        assert attr_of("&CDP-8B7C;") is None


class TestAttrsOfLeaves:
    def test_dedup_and_sorted_stable(self):
        assert attrs_of_leaves(["木", "木", "火"]) == ["木", "火"]

    def test_no_attrs(self):
        assert attrs_of_leaves(["丁", "口"]) == []


def _entry(char, leaves):
    return {"codepoint": "U+0000", "char": char, "ids": "", "leaves": leaves}


class TestFilterCandidates:
    def test_keeps_char_with_attr_and_low_complexity(self):
        result = filter_candidates([_entry("焚", ["木", "木", "火"])])
        assert len(result) == 1
        assert result[0]["attrs"] == ["木", "火"]
        assert result[0]["complexity"] == 3

    def test_earth_char_is_candidate(self):
        result = filter_candidates([_entry("圭", ["土", "土"])])
        assert len(result) == 1
        assert result[0]["attrs"] == ["土"]

    def test_drops_char_without_attr(self):
        assert filter_candidates([_entry("可", ["丁", "口"])]) == []

    def test_drops_char_over_complexity(self):
        entry = _entry("灪", ["氵", "木", "缶", "冖", "鬯", "彡"])
        assert filter_candidates(entry and [entry]) == []

    def test_max_complexity_configurable(self):
        entry = _entry("灪", ["氵", "木", "缶", "冖", "鬯", "彡"])
        assert len(filter_candidates([entry], max_complexity=6)) == 1
