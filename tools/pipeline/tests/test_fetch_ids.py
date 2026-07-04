"""fetch_ids:cjkvi-ids 原始行解析 + IDS 叶子提取。"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from fetch_ids import parse_ids_line, extract_leaves, parse_ids_text


class TestParseIdsLine:
    def test_basic_line(self):
        entry = parse_ids_line("U+711A\t焚\t⿱林火")
        assert entry == {"codepoint": "U+711A", "char": "焚", "ids": "⿱林火"}

    def test_strips_region_tags(self):
        entry = parse_ids_line("U+4E2D\t中\t⿻口丨[GTJKV]")
        assert entry["ids"] == "⿻口丨"

    def test_takes_first_of_multiple_ids(self):
        entry = parse_ids_line("U+9AA8\t骨\t⿱⿵冎冖月[GTV]\t⿱⿵冎冖⺼[JK]")
        assert entry["ids"] == "⿱⿵冎冖月"

    def test_comment_line_returns_none(self):
        assert parse_ids_line("#\tcjkvi-ids") is None

    def test_malformed_line_returns_none(self):
        assert parse_ids_line("U+4E00") is None


class TestExtractLeaves:
    def test_flat_ids(self):
        assert extract_leaves("⿱林火") == ["林", "火"]

    def test_nested_ids(self):
        assert extract_leaves("⿱⿰木木火") == ["木", "木", "火"]

    def test_atomic_char_is_its_own_leaf(self):
        assert extract_leaves("火") == ["火"]

    def test_all_idc_operators_stripped(self):
        # IDC 区 U+2FF0..U+2FFF 全部是结构符,不是叶子
        assert extract_leaves("⿲亻丨丶") == ["亻", "丨", "丶"]
        assert extract_leaves("⿴囗口") == ["囗", "口"]

    def test_entity_reference_is_single_opaque_leaf(self):
        # cjkvi-ids 用 &CDP-xxxx; 表示无码位部件,应作为一个整体叶子
        assert extract_leaves("⿰&CDP-8B7C;寸") == ["&CDP-8B7C;", "寸"]

    def test_ext_a_leaf_kept(self):
        # 扩展区叶子不能被丢弃(历史 bug:Unicode 范围过窄)
        assert extract_leaves("⿰㐅木") == ["㐅", "木"]


class TestParseIdsText:
    def test_parses_multiple_lines_skipping_comments(self):
        text = "#\theader\nU+706B\t火\t火\nU+711A\t焚\t⿱林火\n"
        entries = parse_ids_text(text)
        assert len(entries) == 2
        assert entries[0]["char"] == "火"
        assert entries[1]["leaves"] == ["林", "火"]
