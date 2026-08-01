"""补充字池:档位映射、拆解产出分离、入池校验。"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from decompose import build_index
from report_filler_chars import (FILLER, POWER, POWER_TIERS, RARITY_BANDS, SKIP, TIERS,
                                 power_of, rarity_of, selection_of, split_useful, tier_of,
                                 total_score, validate)


def index_of(pairs):
    return build_index([{"char": c, "ids": i} for c, i in pairs.items()])


class TestTiers:
    def test_purple_is_the_ceiling(self):
        # 合不出来的字不该压过枢纽字体系,最高只到紫
        assert {name for name, _ in RARITY_BANDS} == {"紫", "蓝", "绿", "白"}

    def test_fit_score_rises_with_tier(self):
        assert TIERS["核心"] > TIERS["贴合"] > TIERS["相关"] > TIERS["沾边"]

    def test_power_score_rises_with_tier(self):
        assert (POWER_TIERS["极强"] > POWER_TIERS["强"]
                > POWER_TIERS["中"] > POWER_TIERS["弱"])

    def test_lookup(self):
        assert tier_of("烧") == ("火", "核心")
        assert tier_of("炎") is None  # 炎 是纯元素可达字,归枢纽字体系表


class TestPower:
    def test_unjudged_char_is_weak(self):
        assert power_of("灯") == "弱"

    def test_power_lifts_a_poorly_fitting_char(self):
        # 灭:贴合只到沾边(机制是熄火不是燃烧),但威力拉满 → 从白提到绿
        assert power_of("灭") == "极强"
        assert rarity_of(total_score("沾边", "极强")) == "绿"
        assert rarity_of(total_score("沾边", "弱")) == "白"

    def test_purple_needs_both_dimensions_high(self):
        assert rarity_of(total_score("核心", "强")) == "紫"
        assert rarity_of(total_score("核心", "中")) == "蓝"   # 贴合再好,没气势也上不了紫
        assert rarity_of(total_score("贴合", "极强")) == "蓝"

    def test_every_power_entry_is_in_the_pool(self):
        assert set(POWER) <= set(FILLER)


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
