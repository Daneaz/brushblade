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


def test_extract_pulls_129_implementable_chars():
    """详表里标 ✅ 的字应全部被抽出,且相生字取基础值。详表入 git,可直接读。"""
    values = extract(SPEC.read_text(encoding="utf-8"))
    assert len(values) == 129
    # 焚含木生火,配置表填基础值 7(引擎结算时 ×3 = 21)
    fen = next(e for e in values["焚"]["effects"] if e["kind"] == "DamageAll")
    assert fen["value"] == 7
    assert values["燚"]["rarity"] == "Red"
    assert values["燚"]["element"] == "Fire"


def test_extract_heal_over_time_parses_turns_and_target_all():
    """润:群体持续治疗,turns/targetAll 要从「效果配置」列的括注里解出来。"""
    values = extract(SPEC.read_text(encoding="utf-8"))
    run = next(e for e in values["润"]["effects"] if e["kind"] == "HealOverTime")
    assert run["turns"] == 2
    assert run["targetAll"] is True

    mu = next(e for e in values["沐"]["effects"] if e["kind"] == "HealOverTime")
    assert mu["turns"] == 3
    assert "targetAll" not in mu


def test_extract_ignore_armor_flag_attaches_to_damage_effect():
    """穿甲三字(锥/刺/錰):`ignoreArmor` 标记要落到 DamageSingle 效果的 ignoreArmor 字段上,
    不是生成独立的效果条目 —— 否则 ConfigLoader 读不到,穿甲效果静默消失。"""
    values = extract(SPEC.read_text(encoding="utf-8"))
    for char in ("锥", "刺", "錰"):
        hit = next(e for e in values[char]["effects"] if e["kind"] == "DamageSingle")
        assert hit.get("ignoreArmor") is True, f"「{char}」应带 ignoreArmor"


def test_summon_passive_is_extracted():
    """召唤行的被动 token 要抽进 effects[0]['passive']。"""
    from extract_values import _parse_effects
    config = "`Summon 1`(20 血/攻 7)+ `SummonSpeed 150` + `Thorns 3`"
    effects = _parse_effects(config, "木")
    assert effects[0]["passive"] == {"speed": 150, "thorns": 3}


def test_summon_burn_aura_all_flag():
    config = "`Summon 1`(22 血/攻 0)+ `OnHitBurn 3` + `OnHitBurnAll`"
    from extract_values import _parse_effects
    effects = _parse_effects(config, "火")
    assert effects[0]["attack"] == 0
    assert effects[0]["passive"] == {"onHitBurn": 3, "onHitBurnAll": True}


def test_summon_shield_is_top_level_not_passive():
    """桂 的护盾发给全场,不是这只召唤物自带的 —— 平铺在 effect 上而非进 passive。"""
    from extract_values import _parse_effects
    effects = _parse_effects("`Summon 2`(22 血/攻 9)+ `SummonShield 6`", "木")
    assert effects[0]["summonShield"] == 6
    assert "passive" not in effects[0]


def test_summon_without_passive_has_no_passive_key():
    from extract_values import _parse_effects
    effects = _parse_effects("`Summon 1`(28 血/攻 3)", "木")
    assert "passive" not in effects[0]
    assert "summonShield" not in effects[0]


def test_jing_uses_manual_recipe_not_rare_ids_part():
    """荆 的 IDS 是 ⿰茾刂,茾 是生僻字 —— 人工兜底成 艹+刂。"""
    from export_chars import MANUAL_RECIPES
    assert MANUAL_RECIPES["荆"] == ["艹", "刂"]


def test_valueless_effect_tokens():
    """`Cleanse` 与 `DispelAll` 是无数值标记,通用正则抓不到,要单独认。"""
    from extract_values import _parse_effects
    assert _parse_effects("`Cleanse`", "水") == [{"kind": "Cleanse", "value": 0}]
    assert _parse_effects("`DispelAll`", "火") == [{"kind": "Dispel", "value": -1}]


def test_dispel_each_becomes_target_all():
    from extract_values import _parse_effects
    effects = _parse_effects("`DamageAll 20` + `DispelEach 1`", "水")
    assert effects[0] == {"kind": "DamageAll", "value": 20}
    assert effects[1] == {"kind": "Dispel", "value": 1, "targetAll": True}


def test_execute_tokens_attach_to_damage_not_become_effects():
    """斩杀是伤害的修饰,不该变成独立效果。"""
    from extract_values import _parse_effects
    kill = _parse_effects("`DamageSingle 20` + `ExecuteKill 25`", "金")
    assert kill == [{"kind": "DamageSingle", "value": 20,
                     "executeBelowPercent": 25, "executeKills": True}]
    bonus = _parse_effects("`DamageSingle 9` + `ExecuteBonus 30`", "金")
    assert bonus == [{"kind": "DamageSingle", "value": 9,
                      "executeBelowPercent": 30, "executeKills": False}]


def test_dispel_all_marker_does_not_swallow_counted_dispel():
    """`Dispel 1` 里不含 `DispelAll` 这个带反引号的整词,别误判。"""
    from extract_values import _parse_effects
    effects = _parse_effects("`DamageSingle 9` + `Dispel 1`", "金")
    assert effects == [{"kind": "DamageSingle", "value": 9}, {"kind": "Dispel", "value": 1}]


def test_manual_recipes_avoid_smp_and_rare_parts():
    """塞 的 IDS 部件 𡨄 是增补平面(会让整字降级成叶子);湮 的 垔 生僻。"""
    from export_chars import MANUAL_RECIPES
    assert MANUAL_RECIPES["塞"] == ["宀", "土"]
    assert MANUAL_RECIPES["湮"] == ["氵", "土"]


def test_turns_applies_to_all_duration_kinds_not_just_hot():
    """Blind / Silence / Reflect 都要 turns —— 写死给 HealOverTime 会静默丢掉数值。"""
    from extract_values import _parse_effects
    assert _parse_effects("`Blind 50`(turns 2)", "火") == [
        {"kind": "Blind", "value": 50, "turns": 2}]
    assert _parse_effects("`Silence 0`(turns 1)", "金") == [
        {"kind": "Silence", "value": 0, "turns": 1}]
    assert _parse_effects("`Reflect 50`(turns 2)", "金") == [
        {"kind": "Reflect", "value": 50, "turns": 2}]


def test_blind_supports_target_all():
    from extract_values import _parse_effects
    assert _parse_effects("`Blind 30`(turns 1, targetAll)", "火") == [
        {"kind": "Blind", "value": 30, "turns": 1, "targetAll": True}]


def test_hit_count_token_attaches_to_damage():
    """剁 的分段数是伤害的修饰,不是独立效果。"""
    from extract_values import _parse_effects
    assert _parse_effects("`DamageSingle 10` + `HitCount 2`", "金") == [
        {"kind": "DamageSingle", "value": 10, "hitCount": 2}]


def test_suo_uses_manual_recipe_not_supplementary_plane_part():
    """锁 的 IDS 部件 𭕆(U+2D546)在增补平面,会让整字降级成叶子。"""
    from export_chars import MANUAL_RECIPES
    assert MANUAL_RECIPES["锁"] == ["钅", "贝"]


def test_summon_passive_dodge_is_extracted():
    """柳 的闪避:SUMMON_PASSIVE 缺 Dodge 会被静默丢弃,50% 闪避在引擎里凭空消失。"""
    from extract_values import _parse_effects
    assert _parse_effects("`Summon 1`(8 血/攻 3)+ `Dodge 50`", "木") == [
        {"kind": "Summon", "value": 8, "count": 1, "attack": 3,
         "summonChar": "木", "passive": {"dodge": 50}}]


def test_turns_and_target_all_do_not_leak_to_non_duration_kinds():
    """白名单的「限制」方向:非白名单 Kind 不该拿到 turns/targetAll,伤害也不该被误挂。"""
    from extract_values import _parse_effects
    assert _parse_effects("`DamageSingle 16` + `Blind 50`(turns 2)", "火") == [
        {"kind": "DamageSingle", "value": 16},
        {"kind": "Blind", "value": 50, "turns": 2}]
    assert _parse_effects("`DamageAll 20` + `HealOverTime 3`(turns 2, targetAll)", "水") == [
        {"kind": "DamageAll", "value": 20},
        {"kind": "HealOverTime", "value": 3, "turns": 2, "targetAll": True}]
    # Silence 在 DURATION_KINDS 里但不在 TARGET_ALL_KINDS 里——拿 turns 但不该拿 targetAll。
    assert _parse_effects("`Silence 0`(turns 1, targetAll)", "金") == [
        {"kind": "Silence", "value": 0, "turns": 1}]
    # Immunity 完全不在 DURATION_KINDS 里(它的 value 是挡伤次数,不是回合数)——不该拿 turns。
    assert _parse_effects("`Immunity 2`(turns 3)", "土") == [
        {"kind": "Immunity", "value": 2}]
    # HitCount 只修饰伤害效果,同行的非伤害效果(灼烧)不该被误挂。
    assert _parse_effects("`DamageSingle 10` + `Burn 3` + `HitCount 2`", "火") == [
        {"kind": "DamageSingle", "value": 10, "hitCount": 2},
        {"kind": "Burn", "value": 3}]
