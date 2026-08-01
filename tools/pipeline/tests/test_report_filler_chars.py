"""补充字池:档位映射、拆解产出分离、入池校验。"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from decompose import build_index
from report_filler_chars import (FILLER, SKIP, TIERS, score_of, selection_of, split_useful,
                                 tier_of, validate)


def index_of(pairs):
    return build_index([{"char": c, "ids": i} for c, i in pairs.items()])


class TestTiers:
    def test_purple_is_the_ceiling(self):
        # 合不出来的字不该压过枢纽字体系,最高只到紫
        assert {rarity for _, rarity in TIERS.values()} == {"紫", "蓝", "绿", "白"}

    def test_score_rises_with_tier(self):
        assert (score_of("核心")[0] > score_of("贴合")[0]
                > score_of("相关")[0] > score_of("沾边")[0])

    def test_lookup(self):
        assert tier_of("烧") == ("火", "核心")
        assert tier_of("炎") is None  # 炎 是纯元素可达字,归枢纽字体系表


class TestSplitUseful:
    def test_separates_element_from_phonetic(self):
        # 烧 = 火 + 尧:火 入池,尧 是废料
        assert split_useful(["火", "尧"]) == (["火"], ["尧"])

    def test_all_elements_means_no_waste(self):
        assert split_useful(["火", "火"]) == (["火", "火"], [])


class TestValidate:
    def test_flags_char_already_in_hub_table(self):
        idx = index_of({"炎": "⿱火火", "火": "火"})
        assert "已在" in validate(["炎"], {"炎"}, idx)["炎"]

    def test_flags_char_without_element_part(self):
        idx = index_of({"们": "⿰亻门", "亻": "亻", "门": "门"})
        assert validate(["们"], set(), idx)["们"] == "拆不出五行部件"

    def test_flags_pure_element_char(self):
        # 全是五行部件 = 能合成 = 该进枢纽字体系表,不该在补充池
        idx = index_of({"炎": "⿱火火", "火": "火"})
        assert "纯元素可达" in validate(["炎"], set(), idx)["炎"]

    def test_accepts_phonetic_compound(self):
        idx = index_of({"烧": "⿰火尧", "火": "火", "尧": "尧"})
        assert validate(["烧"], set(), idx) == {}


class TestSelection:
    def test_default_is_in(self):
        # 这批字的存在意义就是填白绿档,默认全收
        assert selection_of("烧") == "建议入选"

    def test_abstract_word_is_out(self):
        assert selection_of("念") == "暂不入"

    def test_every_skip_has_a_reason(self):
        assert all(SKIP.values())

    def test_skips_are_all_in_the_pool(self):
        assert set(SKIP) <= set(FILLER)


class TestFillerTable:
    def test_every_entry_has_a_known_element_and_tier(self):
        assert all(attr in "火木水金土心" and tier in TIERS
                   for attr, tier in FILLER.values())
