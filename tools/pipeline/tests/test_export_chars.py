"""字表导出:配方生成、叠字链人工兜底、数值抽取。"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from export_chars import STACK_RECIPES, build_chars
from extract_values import extract

SPEC = Path(__file__).resolve().parents[3] / "docs/design/字选型/技能机制详表.md"

# 内联小词表(格式同 ids.txt:codepoint \t 字 \t IDS)
MINI_IDS = "\n".join([
    "U+71DA\t燚\t⿰炏炏",   # IDS 会拆成 炏+炏 —— 必须被叠字表覆盖掉
    "U+70EB\t烫\t⿰汤火",   # 非叠字,走 IDS 一级拆解
])


def test_stack_recipes_are_component_first():
    assert STACK_RECIPES["森"] == ["木", "林"]
    assert STACK_RECIPES["𣛧"] == ["木", "森"]
    assert STACK_RECIPES["燚"] == ["火", "焱"]
    assert STACK_RECIPES["㙓"] == ["土", "垚"]


def test_stack_chars_use_manual_recipe_not_ids():
    """燚 必须是 火+焱,不能被 IDS 拆成 炏+炏。"""
    chars = build_chars(MINI_IDS, {"燚": {"element": "Fire", "rarity": "Gold", "effects": []}})
    entry = next(c for c in chars["chars"] if c["id"] == "燚")
    assert entry["recipe"] == ["火", "焱"]


def test_non_stack_char_recipe_comes_from_ids():
    """非叠字走 IDS 一级拆解:烫 = 汤+火。"""
    chars = build_chars(MINI_IDS, {"烫": {"element": "Fire", "rarity": "Purple", "effects": []}})
    entry = next(c for c in chars["chars"] if c["id"] == "烫")
    assert entry["recipe"] == ["汤", "火"]


def test_non_element_components_become_leaf_entries():
    """配方里的非五行部件要生成叶子条目(无 recipe、无 effects)。"""
    chars = build_chars(MINI_IDS, {"烫": {"element": "Fire", "rarity": "Purple", "effects": []}})
    tang = next(c for c in chars["chars"] if c["id"] == "汤")
    assert not tang.get("recipe")
    assert not tang.get("effects")


def test_recipe_dag_has_no_cycle():
    chars = build_chars(MINI_IDS,
                        {c: {"element": "Fire", "rarity": "Gold", "effects": []}
                         for c in STACK_RECIPES})
    table = {c["id"]: c.get("recipe", []) for c in chars["chars"]}

    def depth(node, seen=()):
        assert node not in seen, f"环: {node}"
        return 0 if not table.get(node) else 1 + max(
            depth(p, seen + (node,)) for p in table[node])

    for cid in table:
        depth(cid)


def test_extract_pulls_71_implementable_chars():
    """详表里标 ✅ 的字应全部被抽出,且相生字取基础值。详表入 git,可直接读。"""
    values = extract(SPEC.read_text(encoding="utf-8"))
    assert len(values) == 71
    # 焚含木生火,配置表填基础值 7(引擎结算时 ×3 = 21)
    fen = next(e for e in values["焚"]["effects"] if e["kind"] == "DamageAll")
    assert fen["value"] == 7
    assert values["燚"]["rarity"] == "Gold"
    assert values["燚"]["element"] == "Fire"
