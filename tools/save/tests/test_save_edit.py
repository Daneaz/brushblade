import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import save_edit  # noqa: E402


def _write(tmp_path, text):
    p = tmp_path / "save.json"
    p.write_text(text, encoding="utf-8")
    return p


def test_unpack_edit_seal_roundtrip(tmp_path):
    p = _write(tmp_path, save_edit.seal('{"Ink":208,"CardLevels":{"剑":4}}'))
    assert save_edit.main(["unpack", "--path", str(p)]) == 0
    data = json.loads(p.read_text(encoding="utf-8"))
    data["Ink"] = 99999
    p.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")

    assert save_edit.main(["--path", str(p)]) == 0
    text = p.read_text(encoding="utf-8")
    assert save_edit.is_valid(text)
    assert json.loads(save_edit.strip(text)) == {"Ink": 99999, "CardLevels": {"剑": 4}}


def test_seal_directly_on_edited_signed_file(tmp_path):
    # 不 unpack、直接在签名文件里改数字,也能重签
    p = _write(tmp_path, save_edit.seal('{"Ink":208}').replace("208", "5000"))
    assert not save_edit.is_valid(p.read_text(encoding="utf-8"))
    assert save_edit.main(["--path", str(p)]) == 0
    assert save_edit.is_valid(p.read_text(encoding="utf-8"))


def test_bad_json_is_not_written(tmp_path):
    p = _write(tmp_path, '{"Ink":')
    assert save_edit.main(["--path", str(p)]) == 1
    assert p.read_text(encoding="utf-8") == '{"Ink":'
    assert not list(tmp_path.glob("save.json.bak-*"))
