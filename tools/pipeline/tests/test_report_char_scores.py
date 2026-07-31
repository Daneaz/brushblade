"""字卡评分表:成本分 / 跨系分 / 特性契合分。"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from report_char_scores import (cost_score, cross_score, affinity_of, affinity_score,
                                total_score, AFFINITY)


class TestCostScore:
    def test_single_element_scores_zero(self):
        assert cost_score(["火"]) == 0

    def test_four_elements_scores_full(self):
        assert cost_score(["火"] * 4) == 30

    def test_monotonic(self):
        assert cost_score(["火"] * 2) < cost_score(["火"] * 3) < cost_score(["火"] * 4)


class TestCrossScore:
    def test_single_attr_scores_zero(self):
        assert cross_score(["火"]) == 0

    def test_two_attrs(self):
        assert cross_score(["火", "土"]) == 12.5

    def test_three_attrs_scores_full(self):
        assert cross_score(["木", "火", "金"]) == 25

    def test_variants_of_same_element_are_one_attr(self):
        assert cross_score(["氵", "冫"]) == 0


class TestAffinity:
    def test_known_char_returns_tier_and_element(self):
        # 锬 = 长矛,金系单体攻击的核心意象
        assert affinity_of("锬") == ("金", "核心")

    def test_unknown_char_has_no_affinity(self):
        assert affinity_of("圸") is None

    def test_score_by_tier(self):
        assert affinity_score("核心") == 45
        assert affinity_score("贴合") == 30
        assert affinity_score("沾边") == 15
        assert affinity_score(None) == 0

    def test_table_only_uses_defined_tiers(self):
        assert all(tier in ("核心", "贴合", "沾边") for _, tier in AFFINITY.values())


class TestTotalScore:
    def test_caps_at_hundred(self):
        # 四元素 + 三属性 + 核心契合 = 30 + 25 + 45
        assert total_score(["火"] * 2 + ["土", "木"], "核心") == 100

    def test_bare_element_scores_only_affinity(self):
        assert total_score(["火"], "核心") == 45
