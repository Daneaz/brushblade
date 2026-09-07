"""详表里的目标形状 token(Sweep/Cleave/Skewer/ShapePercent/Shots)→ chars.json 字段。

2026-08-22:只修饰单体直伤(DamageSingle),与既有的 Backline/Pierce/HitCount 同属修饰位。
"""
import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from extract_values import _parse_effects, extract


def test_sweep_token_becomes_shape_field():
    assert _parse_effects("`DamageSingle 10` + `Sweep`", "金") == [
        {"kind": "DamageSingle", "value": 10, "shape": "Sweep"}]


def test_cleave_token_becomes_shape_field():
    assert _parse_effects("`DamageSingle 10` + `Cleave`", "金") == [
        {"kind": "DamageSingle", "value": 10, "shape": "Cleave"}]


def test_skewer_token_becomes_shape_field():
    assert _parse_effects("`DamageSingle 10` + `Skewer`", "金") == [
        {"kind": "DamageSingle", "value": 10, "shape": "Skewer"}]


def test_shape_percent_token_becomes_shape_percent_field():
    assert _parse_effects("`DamageSingle 10` + `Sweep` + `ShapePercent 50`", "金") == [
        {"kind": "DamageSingle", "value": 10, "shape": "Sweep", "shapePercent": 50}]


def test_shots_token_becomes_volley_shape_plus_shots():
    assert _parse_effects("`DamageSingle 10` + `Shots 3`", "金") == [
        {"kind": "DamageSingle", "value": 10, "shape": "Volley", "shots": 3}]


def test_no_shape_marker_leaves_shape_field_absent():
    """缺省不写 shape —— 恒等性:87 张既有伤害字重新生成后必须逐字节不变。"""
    effects = _parse_effects("`DamageSingle 10`", "金")
    assert effects == [{"kind": "DamageSingle", "value": 10}]
    assert "shape" not in effects[0]
    assert "shapePercent" not in effects[0]
    assert "shots" not in effects[0]


def test_shots_and_shape_percent_do_not_become_standalone_effects():
    """坑 2:通用正则 `(\\w+) (\\d+)` 会把 `Shots 3` / `ShapePercent 50` 当成独立效果收走 ——
    EffectKind 里没有这两个值,落成独立条目会让 ConfigLoader 在加载期直接抛 ConfigException。"""
    effects = _parse_effects("`DamageSingle 10` + `Shots 3` + `ShapePercent 50`", "金")
    assert all(e["kind"] not in ("Shots", "ShapePercent") for e in effects)
    assert len(effects) == 1


# 召唤物自动攻击的形状(2026-08-22):同一套 token,落进 passive 的 shape/shots/shapePercent,
# 而不是独立 effect —— BattleEngine.cs:1276-1284 读的就是 passive 上这三个字段。

def test_summon_sweep_token_becomes_passive_shape_field():
    effects = _parse_effects("`Summon 1`(10 血/攻 3) + `Sweep`", "刀")
    assert effects == [{"kind": "Summon", "value": 10, "count": 1, "attack": 3,
                         "summonChar": "刀", "passive": {"shape": "Sweep"}}]


def test_summon_cleave_token_becomes_passive_shape_field():
    effects = _parse_effects("`Summon 1`(10 血/攻 3) + `Cleave`", "刀")
    assert effects == [{"kind": "Summon", "value": 10, "count": 1, "attack": 3,
                         "summonChar": "刀", "passive": {"shape": "Cleave"}}]


def test_summon_skewer_token_becomes_passive_shape_field():
    effects = _parse_effects("`Summon 1`(10 血/攻 3) + `Skewer`", "刀")
    assert effects == [{"kind": "Summon", "value": 10, "count": 1, "attack": 3,
                         "summonChar": "刀", "passive": {"shape": "Skewer"}}]


def test_summon_shots_token_becomes_passive_volley_shape_plus_shots():
    effects = _parse_effects("`Summon 1`(10 血/攻 3) + `Shots 3`", "刀")
    assert effects == [{"kind": "Summon", "value": 10, "count": 1, "attack": 3,
                         "summonChar": "刀", "passive": {"shape": "Volley", "shots": 3}}]


def test_summon_shape_percent_token_becomes_passive_field():
    effects = _parse_effects("`Summon 1`(10 血/攻 3) + `Sweep` + `ShapePercent 50`", "刀")
    assert effects == [{"kind": "Summon", "value": 10, "count": 1, "attack": 3,
                         "summonChar": "刀", "passive": {"shape": "Sweep", "shapePercent": 50}}]


def test_summon_no_shape_marker_leaves_passive_without_shape_keys():
    """缺省不写 shape —— 恒等性:既有召唤字(如带 Ranged 的灶/烓)重新生成后必须逐字节不变,
    passive 里不能凭空多出 shape/shots/shapePercent 三个键。"""
    effects = _parse_effects("`Summon 1`(10 血/攻 3) + `Ranged`", "刀")
    assert effects == [{"kind": "Summon", "value": 10, "count": 1, "attack": 3,
                         "summonChar": "刀", "passive": {"ranged": True}}]
    passive = effects[0]["passive"]
    assert "shape" not in passive
    assert "shapePercent" not in passive
    assert "shots" not in passive


def test_summon_with_no_passive_tokens_has_no_passive_key_at_all():
    """没有任何被动 token 的召唤字(多数已有召唤字)——passive 键本身都不该出现。"""
    effects = _parse_effects("`Summon 1`(10 血/攻 3)", "刀")
    assert effects == [{"kind": "Summon", "value": 10, "count": 1, "attack": 3, "summonChar": "刀"}]
    assert "passive" not in effects[0]


# ---- 拼音与释义(第九节,2026-09-03) ----

_READINGS_MD = """## 二 · 火系

| 字 | 稀 | 效果配置(基础值) | 相生 | **最终值** | 实现 |
|---|---|---|---|---|---|
| 灼 | ⚪白 | `DamageSingle 40` | — | 单体 40 | ✅ |

## 七 · 引擎扩展需求清单

## 九 · 拼音与释义

| 字 | 拼音 | 释义 |
|---|---|---|
| 灼 | zhuó | 火烧、烫 |
| 燚 | yì | 火势极盛 |
"""


def test_readings_section_fills_pinyin_and_gloss():
    values = extract(_READINGS_MD)
    assert values["灼"]["pinyin"] == "zhuó"
    assert values["灼"]["gloss"] == "火烧、烫"


def test_readings_for_chars_outside_the_spec_are_ignored():
    """第九节多出来的字(已移出字表的、或还没标 ✅ 的)不该凭空造出条目。"""
    values = extract(_READINGS_MD)
    assert "燚" not in values


def test_missing_readings_leave_no_keys():
    """没在第九节列出的字不该多出空的 pinyin/gloss 键 —— export_chars 只在真值时落地,
    但键存在与否会影响这一层的恒等性判断。"""
    spec = _READINGS_MD.replace("| 灼 | zhuó | 火烧、烫 |\n", "")
    values = extract(spec)
    assert "pinyin" not in values["灼"]
    assert "gloss" not in values["灼"]


# ---- 消费记账:未被消费的 token 与孤立的 turns 一律报错(2026-09-07,P2 Task 1) ----
#
# extract_values 是一串 re.search 找已知 token,认不得的一律静默忽略 ——
# 而 P2 要通过它改 57 行数据。项目已因「手写映射表认不得的标记无声消失」栽过一次。

def test_unknown_valueless_token_raises():
    """无数值的未知 token 必须报错,不能静默忽略。"""
    with pytest.raises(Exception) as err:
        _parse_effects("`DamageSingle 100` + `TotallyBogus`", "测")
    assert "TotallyBogus" in str(err.value)


def test_turns_on_kind_without_duration_raises():
    """turns 挂在不吃它的 kind 上必须报错 —— 否则那个回合数静默消失。"""
    with pytest.raises(Exception) as err:
        _parse_effects("`DamageSingle 100`(turns 2)", "测")
    assert "turns" in str(err.value)


def test_duration_kind_without_turns_raises():
    """反方向(2026-09-07 追加):DURATION_KINDS 效果本身没拿到 turns 也必须报错 ——
    这正是 spec §1.5 第 20 项的历史 bug(`壁` 攻面漏写 turns,TurnsLeft=0 当场清空)。"""
    with pytest.raises(Exception) as err:
        _parse_effects("`Reflect 30`", "测")
    assert "turns" in str(err.value)
    assert "Reflect" in str(err.value)


def test_known_tokens_still_parse():
    """恒等性:既有写法一个都不能被新防线误伤。

    ⚠ 这里的 238/`冰` 只是手打的构造样本,不是从详表抄的快照——T4 改数值时这条测试
    不会因此变红,也不该被当成「详表当前长这样」的参照去同步改动。"""
    got = _parse_effects("`DamageSingle 238` + `DoubleVsControlled`", "冰")
    assert got == [{"kind": "DamageSingle", "value": 238, "doubleVs": "Controlled"}]


# ---- P2 Task 2:补 Charm / AuraAttack / 限时增益 turns / 召唤行并存效果(2026-09-07) ----

def test_charm_takes_turns_not_value():
    """魅惑(花):Value 不用,Turns 才是回合数——写法 `Charm 0`(turns 1)。"""
    assert _parse_effects("`Charm 0`(turns 1)", "花") == [
        {"kind": "Charm", "value": 0, "turns": 1}]


def test_charm_without_turns_raises():
    """Charm 漏填 turns 必须报错——引擎侧 Math.Max(1, effect.Turns) 会把它兜成 1 回合,
    看起来能用、实际回合数写死且不吃卡等级,这是最容易被忽略的一种笔误。"""
    with pytest.raises(Exception) as err:
        _parse_effects("`Charm 0`", "测")
    assert "turns" in str(err.value)
    assert "Charm" in str(err.value)


def test_slow_value_is_the_turn_count_directly():
    """减速(冷/冻/淋):EffectKind.Slow 的 Value 本身就是持续回合数(引擎固定 -50%
    速度、TurnsLeft = value,见 BattleEngine.cs 的 EffectKind.Slow 分支)——不走
    `(turns N)` 机制,不需要进 DURATION_KINDS,通用正则已经能解析,写成 `Slow N` 即可。"""
    assert _parse_effects("`Slow 2`", "冻") == [{"kind": "Slow", "value": 2}]


def test_freeze_value_is_the_turn_count_directly():
    """冻结(冰/淼/㵘):EffectKind.Freeze 同 Slow 同口径,Value 直接是 TurnsLeft
    (BattleEngine.cs 的 EffectKind.Freeze 分支:`TurnsLeft = value`),同样不需要
    `(turns N)`、不需要进 DURATION_KINDS。"""
    assert _parse_effects("`Freeze 2`", "淼") == [{"kind": "Freeze", "value": 2}]


def test_empower_with_turns_attaches_turns():
    """限时增攻(利):`Empower 30`(turns 2)——回合数随卡等级成长(§4.2),但管线层
    只管把 turns 原样落进 effect,缩放是引擎的事。"""
    assert _parse_effects("`Empower 30`(turns 2)", "利") == [
        {"kind": "Empower", "value": 30, "turns": 2}]


def test_critbuff_with_turns_attaches_turns():
    """限时暴击(锋):`CritBuff 20`(turns 3)。"""
    assert _parse_effects("`CritBuff 20`(turns 3)", "锋") == [
        {"kind": "CritBuff", "value": 20, "turns": 3}]


def test_critbuff_without_turns_still_parses_as_persistent():
    """⚠ 恒等性关键回归:既有字「锋」现在就是 `CritBuff 20` 不写 turns(本场持久,
    已在 chars.json 里)。CritBuff/Empower 的 turns 是**可选**的(OPTIONAL_DURATION_KINDS,
    不在强制的 DURATION_KINDS 里)——不写 turns 绝不能报错,否则会砸穿恒等性硬线。"""
    assert _parse_effects("`CritBuff 20`", "锋") == [{"kind": "CritBuff", "value": 20}]


def test_empower_without_turns_still_parses_as_persistent():
    assert _parse_effects("`Empower 50`", "剡") == [{"kind": "Empower", "value": 50}]


def test_turns_lost_on_optional_duration_kind_alone_still_raises():
    """turns 写了、但本行唯一的效果是「不需要 turns 但也认得 turns」之外的东西——
    仍要走「turns 没人吃」这条反向检查(与 DURATION_KINDS 同一张账,只是白名单变宽)。"""
    with pytest.raises(Exception) as err:
        _parse_effects("`DamageSingle 10`(turns 3)", "测")
    assert "turns" in str(err.value)


def test_aura_attack_summon_passive():
    """攻击光环(𣛧):`AuraAttack N` → passive.auraAttack,字段名对齐
    SummonPassive.AuraAttack(EnemyDef.cs / SummonPassive.cs),不另起名字。"""
    effects = _parse_effects("`Summon 2`(465 血/攻 130) + `AuraAttack 20`", "𣛧")
    assert effects == [{"kind": "Summon", "value": 465, "count": 2, "attack": 130,
                         "summonChar": "𣛧", "passive": {"auraAttack": 20}}]


def test_summon_row_also_parses_shield():
    """土系召唤字要补入场护盾(spec §2)——召唤分支不能提前 return 把它吞掉,
    Shield 是给玩家的独立效果,与 Summon 并存于同一个 effects 数组。"""
    got = _parse_effects("`Summon 1`(100 血/攻 0) + `Thorns 50` + `Shield 40`", "碉")
    assert got == [
        {"kind": "Summon", "value": 100, "count": 1, "attack": 0, "summonChar": "碉",
         "passive": {"thorns": 50}},
        {"kind": "Shield", "value": 40},
    ]


def test_summon_row_unknown_token_still_raises():
    """召唤分支不再提前 return 之后,消费记账的收尾挪到了函数末尾——
    真正认不得的 token 在召唤行上仍必须报错,不能被「继续走通用循环」误放行。"""
    with pytest.raises(Exception) as err:
        _parse_effects("`Summon 1`(100 血/攻 0) + `TotallyBogus`", "测")
    assert "TotallyBogus" in str(err.value)
