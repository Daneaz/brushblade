"""score_chars:部件复用价值打分 → 六档稀有度。"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from score_chars import (build_combination_graph, char_metrics, score_pool,
                         assign_rarity, RARITY_TIERS)


def entry(char, parts1, leaves):
    return {"char": char, "parts1": parts1, "leaves": leaves}


class TestBuildCombinationGraph:
    def test_counts_first_level_combinations(self):
        # 林 = 木+木,森 = 木+林 → 木 能组 林/森,林 能组 森
        graph = build_combination_graph([
            entry("林", ["木", "木"], ["木", "木"]),
            entry("森", ["木", "林"], ["木", "木", "木"]),
        ])
        assert graph["木"] == {"林", "森"}
        assert graph["林"] == {"森"}

    def test_only_first_level_edges_count(self):
        # 噪 = 口+喿(一级),递归到 口+品+木 —— 「品」不算能组成噪
        graph = build_combination_graph([
            entry("噪", ["口", "喿"], ["口", "品", "木"]),
        ])
        assert graph["喿"] == {"噪"}
        assert "品" not in graph

    def test_self_reference_excluded(self):
        graph = build_combination_graph([entry("木", [], ["木"])])
        assert "木" not in graph


class TestCharMetrics:
    def setup_method(self):
        # 火 能组 灯/炎,木 能组 林,品 组不出任何字
        self.graph = {"火": {"灯", "炎"}, "木": {"林"}, "喿": {"噪"}}

    def test_effective_parts_skips_dead_components(self):
        # 燥 = 火+品+木 → 有效部件 2(火/木),「品」组不出字
        eff, _ = char_metrics(entry("燥", ["火", "喿"], ["火", "品", "木"]), self.graph)
        assert eff == 2

    def test_effective_parts_counts_repeats(self):
        eff, _ = char_metrics(entry("森", ["木", "林"], ["木", "木", "木"]), self.graph)
        assert eff == 3

    def test_production_spans_both_levels(self):
        # 燥:一级部件 火+喿,递归部件 火+品+木 → 去重后 火/喿/品/木 各自组字数之和
        _, prod = char_metrics(entry("燥", ["火", "喿"], ["火", "品", "木"]), self.graph)
        assert prod == 2 + 1 + 0 + 1

    def test_production_counts_each_component_once(self):
        # 森 的木出现三次,组字数只计一次
        _, prod = char_metrics(entry("森", ["木", "林"], ["木", "木", "木"]), self.graph)
        assert prod == 1  # 木=1,林 不在图中

    def test_self_not_counted_as_own_component(self):
        eff, prod = char_metrics(entry("木", [], ["木"]), self.graph)
        assert (eff, prod) == (1, 0)


class TestScorePool:
    def test_two_dimensions_weigh_half_each(self):
        # A 两维都最高 → 100;D 两维都最低 → 0;B/C 各占一维之长 → 50
        scored = score_pool({"A": (3, 100), "B": (3, 0), "C": (1, 100), "D": (1, 0)})
        assert scored["A"] == 100
        assert scored["D"] == 0
        assert scored["B"] == 50
        assert scored["C"] == 50

    def test_flat_dimension_contributes_nothing(self):
        # 池内某维度全同(无区分度)时不应造成 0 除
        scored = score_pool({"A": (2, 10), "B": (2, 0)})
        assert scored["A"] == 50
        assert scored["B"] == 0

    def test_single_char_pool(self):
        assert score_pool({"A": (2, 10)}) == {"A": 0}


class TestAssignRarity:
    def test_pyramid_ratios(self):
        # 100 字按 白25/绿25/蓝20/紫15/橙8/红7 切
        scored = {f"c{i:03d}": float(i) for i in range(100)}
        result = assign_rarity(scored)
        counts = {name: sum(1 for r in result.values() if r == name)
                  for name, _ in RARITY_TIERS}
        assert counts == {"红": 7, "橙": 8, "紫": 15, "蓝": 20, "绿": 25, "白": 25}

    def test_highest_score_gets_red(self):
        scored = {f"c{i:03d}": float(i) for i in range(100)}
        result = assign_rarity(scored)
        assert result["c099"] == "红"
        assert result["c000"] == "白"

    def test_ties_share_one_tier(self):
        # 同分必须同档,不能因排序位置被切到两档
        scored = {f"c{i}": 1.0 for i in range(10)}
        result = assign_rarity(scored)
        assert len(set(result.values())) == 1

    def test_covers_every_char(self):
        scored = {f"c{i}": float(i % 7) for i in range(50)}
        result = assign_rarity(scored)
        assert set(result) == set(scored)
        assert all(r in dict(RARITY_TIERS) for r in result.values())

    def test_empty_pool(self):
        assert assign_rarity({}) == {}
