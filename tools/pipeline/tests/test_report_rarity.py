"""稀有度分档与本期入选建议。"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from report_rarity import rarity_of, RARITY_BANDS, selection_of


class TestRarityOf:
    def test_top_score_is_red(self):
        assert rarity_of(102.5) == "红"

    def test_bottom_score_is_white(self):
        assert rarity_of(0) == "白"

    def test_band_boundaries_are_inclusive(self):
        for name, floor in RARITY_BANDS:
            assert rarity_of(floor) == name

    def test_monotonic(self):
        order = [name for name, _ in RARITY_BANDS][::-1]
        assert order.index(rarity_of(30)) < order.index(rarity_of(90))


def _rec(char="炎", tier="核心", level=1, in_game=False, group="火"):
    return {"char": char, "tier": tier, "gb": level, "in_game": in_game, "group": group}


class TestSelectionOf:
    def test_already_in_game_always_selected(self):
        # 鍂 只有沾边契合,但已经在 chars.json 里,不能因为分低就踢掉
        assert selection_of(_rec("鍂", tier="沾边", level=0, in_game=True)) == "已在字表"

    def test_recognizable_and_fitting_is_recommended(self):
        assert selection_of(_rec("焚", tier="核心", level=1)) == "建议入选"

    def test_rare_char_still_recommended_when_fitting(self):
        # 生僻不作为判据:燊(火盛)契合核心,照收
        assert selection_of(_rec("燊", tier="核心", level=0)) == "建议入选"

    def test_common_but_unfitting_not_recommended(self):
        # 泵 是一级常用字,但字义与土系防御无关
        assert selection_of(_rec("泵", tier=None, level=1)) == "暂不入"

    def test_only_marginal_fit_not_recommended(self):
        assert selection_of(_rec("淦", tier="沾边", level=2)) == "暂不入"

    def test_element_variant_is_a_component_not_a_card(self):
        # 氵 契合贴合又是二级字,但它是部件池里的元素变体,不做成卡
        assert selection_of(_rec("氵", tier="贴合", level=2, group="元素")) == "部件·非卡"

    def test_element_stays_a_component_even_when_in_game(self):
        # 五行本体在 chars.json 里有效果,但它同时是部件,不打分不给稀有度
        assert selection_of(_rec("火", in_game=True, group="元素")) == "部件·非卡"
