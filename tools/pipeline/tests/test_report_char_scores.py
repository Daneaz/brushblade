"""字卡评分表:成本分 / 跨系分 / 特性契合分。"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from report_char_scores import (cost_score, cross_score, affinity_of, affinity_score,
                                sheng_score, sheng_pairs, total_score, AFFINITY)


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
        # 鈛 字典只给了读音,字义不明 → 不进判定表
        assert affinity_of("鈛") is None

    def test_score_by_tier(self):
        assert affinity_score("核心") == 45
        assert affinity_score("贴合") == 30
        assert affinity_score("沾边") == 15
        assert affinity_score(None) == 0

    def test_table_only_uses_defined_tiers(self):
        assert all(tier in ("核心", "贴合", "沾边") for _, tier in AFFINITY.values())


class TestShengScore:
    def test_sheng_pair_scores(self):
        # 焚 = 木×2 + 火,木生火
        assert sheng_score(["木", "木", "火"]) == 15

    def test_ke_pair_scores_zero(self):
        # 淡 = 氵 + 炎,水克火——相克不加分
        assert sheng_score(["氵", "火", "火"]) == 0

    def test_single_element_scores_zero(self):
        assert sheng_score(["火", "火"]) == 0

    def test_heart_is_neutral(self):
        # 心中立,不参与生克
        assert sheng_score(["忄", "木", "木"]) == 0

    def test_pairs_are_named(self):
        assert sheng_pairs(["木", "木", "火"]) == [("木", "火")]


class TestTotalScore:
    def test_caps_at_one_fifteen(self):
        # 四元素 + 三属性 + 核心契合 + 相生 = 30 + 25 + 45 + 15
        assert total_score(["木", "火", "火", "土"], "核心") == 115

    def test_bare_element_scores_only_affinity(self):
        assert total_score(["火"], "核心") == 45
