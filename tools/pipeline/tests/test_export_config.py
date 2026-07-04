"""export_config:候选字表导出为 JSON。"""
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from export_config import export_candidates


CANDIDATES = [
    {"codepoint": "U+711A", "char": "焚", "ids": "⿱林火",
     "leaves": ["林", "火"], "attrs": ["木", "火"], "complexity": 2},
]


class TestExportCandidates:
    def test_writes_json_with_meta_and_entries(self, tmp_path):
        out = tmp_path / "candidates.json"
        export_candidates(CANDIDATES, out)
        data = json.loads(out.read_text(encoding="utf-8"))
        assert data["meta"]["count"] == 1
        assert data["candidates"][0]["char"] == "焚"
        assert data["candidates"][0]["attrs"] == ["木", "火"]

    def test_chinese_not_escaped(self, tmp_path):
        out = tmp_path / "candidates.json"
        export_candidates(CANDIDATES, out)
        assert "焚" in out.read_text(encoding="utf-8")

    def test_creates_parent_dirs(self, tmp_path):
        out = tmp_path / "nested" / "dir" / "candidates.json"
        export_candidates(CANDIDATES, out)
        assert out.exists()
