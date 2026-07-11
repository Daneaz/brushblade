"""拼音/释义充实:新华字典数据 → 字 → (拼音, 短释义)。"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from enrich_readings import normalize_pinyin, short_gloss, readings_map


def test_normalize_pinyin_fixes_script_g():
    assert normalize_pinyin("dēnɡ") == "dēng"


def test_normalize_pinyin_strips_whitespace():
    assert normalize_pinyin(" fén ") == "fén"


def test_short_gloss_prefers_benyi_sentence():
    text = "灯    (形声。从火,登声。本写作镫。本义置烛用以照明的器具。镫在古代还指别的)"
    assert short_gloss(text) == "置烛用以照明的器具"


def test_short_gloss_falls_back_to_first_clause():
    text = "爝    拔火    爝,苣火祓也。从火,爵声。--《说文》"
    assert short_gloss(text) == "拔火"


def test_short_gloss_caps_length():
    text = "本义" + "很" * 60 + "长。"
    assert len(short_gloss(text)) <= 24


def test_short_gloss_empty_input():
    assert short_gloss("") == ""


def test_readings_map_builds_and_skips_missing():
    entries = [
        {"word": "灯", "pinyin": "dēnɡ", "explanation": "灯 本义置烛用以照明的器具。"},
        {"word": "焚", "pinyin": " fén ", "explanation": "焚 烧。"},
    ]
    m = readings_map(entries)
    assert m["灯"] == ("dēng", "置烛用以照明的器具")
    assert m["焚"][0] == "fén"
    assert "燚" not in m
