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
