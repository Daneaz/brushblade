"""build_data:管线编排(解析 → 筛选 → 导出 + 汇总)。"""
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from build_data import build_from_text


IDS_TEXT = (
    "#\theader\n"
    "U+706B\t火\t火\n"                # 独体字:1 叶,火属性 → 候选
    "U+711A\t焚\t⿱林火[G]\n"          # 2 叶,含火 → 候选
    "U+53EF\t可\t⿹丁口\n"             # 无属性 → 淘汰
    "U+572D\t圭\t⿱土土\n"             # 土属性(v0.4)→ 候选
)


class TestBuildFromText:
    def test_summary_counts(self, tmp_path):
        summary = build_from_text(IDS_TEXT, tmp_path / "candidates.json")
        assert summary["parsed"] == 4
        assert summary["candidates"] == 3

    def test_by_attr_breakdown_includes_earth(self, tmp_path):
        summary = build_from_text(IDS_TEXT, tmp_path / "candidates.json")
        assert summary["by_attr"]["火"] == 2
        assert summary["by_attr"]["土"] == 1

    def test_writes_output_file(self, tmp_path):
        out = tmp_path / "candidates.json"
        build_from_text(IDS_TEXT, out)
        data = json.loads(out.read_text(encoding="utf-8"))
        assert data["meta"]["count"] == 3
        assert {c["char"] for c in data["candidates"]} == {"火", "焚", "圭"}

    def test_leaves_are_recursively_decomposed(self, tmp_path):
        # 林 有条目时,焚 应递归到 木+木+火 而非停在一层的 林+火
        text = IDS_TEXT + "U+6797\t林\t⿰木木\nU+6728\t木\t木\n"
        out = tmp_path / "candidates.json"
        build_from_text(text, out)
        data = json.loads(out.read_text(encoding="utf-8"))
        fen = next(c for c in data["candidates"] if c["char"] == "焚")
        assert fen["leaves"] == ["木", "木", "火"]
        assert fen["complexity"] == 3
