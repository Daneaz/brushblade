"""枢纽字体系字表(方案 A):纯元素可达判定、配方规范化、枢纽/单系/跨系归类。"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from decompose import build_index
from report_hub_chars import (is_pure, element_cost, canon_map, normalize_parts,
                              classify, hub_tier, downstream_index, merge_readings)


def index_of(pairs):
    return build_index([{"char": c, "ids": i} for c, i in pairs.items()])


class TestIsPure:
    def test_all_element_leaves(self):
        assert is_pure(["火", "火", "氵"])

    def test_non_element_leaf_rejected(self):
        assert not is_pure(["火", "丁"])

    def test_empty_rejected(self):
        assert not is_pure([])


class TestElementCost:
    def test_counts_by_wuxing_not_by_variant(self):
        # 氵与水同属水:成本按属性计,不按写法计
        assert element_cost(["氵", "水", "火"]) == {"水": 2, "火": 1}


class TestCanonMap:
    def test_maps_leafset_to_most_common_char(self):
        # 叶子同为 土+土 的多个字,取 GB 常用度最高者作规范写法
        canon = canon_map({"圭": ["土", "土"], "圸": ["土", "土"]})
        assert canon[("土", "土")] == "圭"


class TestNormalizeParts:
    def test_replaces_out_of_basic_block_part(self):
        # 垚 = ⿱土𪢴,𪢴 是扩展区字(无字形),换成同叶子的基本区「圭」
        idx = index_of({"𪢴": "⿰土土", "土": "土"})
        assert normalize_parts(["土", "𪢴"], idx, {("土", "土"): "圭"}) == ["土", "圭"]

    def test_expands_part_without_canon_equivalent(self):
        # 惢 = ⿱心𢗰,心心 没有基本区字 → 摊平成两个心
        idx = index_of({"𢗰": "⿰心心", "心": "心"})
        assert normalize_parts(["心", "𢗰"], idx, {}) == ["心", "心", "心"]

    def test_keeps_basic_block_parts(self):
        idx = index_of({"炎": "⿱火火", "火": "火"})
        assert normalize_parts(["氵", "炎"], idx, {}) == ["氵", "炎"]


class TestClassify:
    def test_single_element(self):
        assert classify(["木", "木", "木"], is_hub=False) == "木"

    def test_variants_of_same_element_still_single(self):
        assert classify(["氵", "冫"], is_hub=False) == "水"

    def test_cross_element(self):
        assert classify(["木", "土"], is_hub=False) == "跨系"

    def test_hub_wins_over_element(self):
        assert classify(["火", "火"], is_hub=True) == "枢纽"

    def test_element_part_is_not_a_hub(self):
        # 元素部件自身下游极多,但它是元素不是枢纽字
        assert classify(["火"], is_hub=True, is_element=True) == "元素"


class TestHubTier:
    def test_tier_by_element_count(self):
        assert hub_tier(["火", "火"]) == 2
        assert hub_tier(["火", "火", "火"]) == 3


class TestMergeReadings:
    def test_game_char_table_wins(self):
        # 字典对生僻叠字有脏数据(鍂 拼音 "uu"),游戏字表已拍板的以字表为准
        merged = merge_readings({"鍂": ("uu", "")},
                                [{"id": "鍂", "pinyin": "piān", "gloss": "古乐器名"}])
        assert merged["鍂"] == ("piān", "古乐器名")

    def test_keeps_dictionary_entry_when_not_in_game(self):
        merged = merge_readings({"火": ("huǒ", "燃烧")}, [])
        assert merged["火"] == ("huǒ", "燃烧")


class TestDownstreamIndex:
    def test_counts_chars_using_it_as_first_level_part(self):
        # 炎 被 淡/锬 用作一级部件
        idx = index_of({"淡": "⿰氵炎", "锬": "⿰钅炎", "炎": "⿱火火"})
        down = downstream_index(["淡", "锬", "炎"], idx)
        assert down["炎"] == ["淡", "锬"]

    def test_self_not_counted(self):
        idx = index_of({"炎": "⿱火火", "火": "火"})
        assert downstream_index(["炎"], idx).get("炎", []) == []
