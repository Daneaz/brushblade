"""成语 Boss 候选筛选(20.7/Q26):四字全常用不重复、意象计分、逐字五行标注。"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from select_idioms import is_candidate, imagery_score, char_element


def test_candidate_four_common_distinct_chars():
    assert is_candidate("排山倒海") is True


def test_candidate_rejects_repeated_chars():
    assert is_candidate("头头是道") is False


def test_candidate_rejects_rare_chars():
    assert is_candidate("魑魅魍魉") is False


def test_candidate_rejects_non_four():
    assert is_candidate("一衣带水情") is False


def test_imagery_score_counts_nature_chars():
    assert imagery_score("排山倒海") == 2  # 山、海
    assert imagery_score("不可思议") == 0


def test_char_element_from_attrs():
    attrs = {"海": ["水"], "山": ["土"]}
    assert char_element("海", attrs) == "Water"
    assert char_element("山", attrs) == "Earth"


def test_char_element_neutral_when_unknown():
    attrs = {}
    assert char_element("倒", attrs) == "Heart"
