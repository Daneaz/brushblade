"""详表里的目标形状 token(Sweep/Cleave/Skewer/ShapePercent/Shots)→ chars.json 字段。

2026-08-22:只修饰单体直伤(DamageSingle),与既有的 Backline/Pierce/HitCount 同属修饰位。
"""
import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from extract_values import _parse_effects, extract


def test_sweep_token_becomes_shape_field():
    assert _parse_effects("`DamageSingle 10` + `Row`", "金") == [
        {"kind": "DamageSingle", "value": 10, "shape": "Row"}]


def test_cleave_token_becomes_shape_field():
    assert _parse_effects("`DamageSingle 10` + `Adjacent`", "金") == [
        {"kind": "DamageSingle", "value": 10, "shape": "Adjacent"}]


def test_skewer_token_becomes_shape_field():
    assert _parse_effects("`DamageSingle 10` + `Column`", "金") == [
        {"kind": "DamageSingle", "value": 10, "shape": "Column"}]


def test_shape_percent_token_becomes_shape_percent_field():
    assert _parse_effects("`DamageSingle 10` + `Row` + `ShapePercent 50`", "金") == [
        {"kind": "DamageSingle", "value": 10, "shape": "Row", "shapePercent": 50}]


def test_shots_token_becomes_volley_shape_plus_shots():
    assert _parse_effects("`DamageSingle 10` + `Shots 3`", "金") == [
        {"kind": "DamageSingle", "value": 10, "shape": "Scatter", "shots": 3}]


def test_no_shape_marker_leaves_shape_field_absent():
    """缺省不写 shape —— 恒等性:87 张既有伤害字重新生成后必须逐字节不变。"""
    effects = _parse_effects("`DamageSingle 10`", "金")
    assert effects == [{"kind": "DamageSingle", "value": 10}]
    assert "shape" not in effects[0]
    assert "shapePercent" not in effects[0]
    assert "shots" not in effects[0]


def test_all_shape_token_on_damage_single():
    """全体(spec v7 §3.2 / §11.6):`All` 是 DamageSingle 的形状修饰,不是独立效果。"""
    effects = _parse_effects("`DamageSingle 30` `All`", "火")
    assert effects == [{"kind": "DamageSingle", "value": 30, "shape": "All"}]


def test_damage_all_token_is_retired():
    """`DamageAll` 已退役:详表里再写它要大声报错,并指明改写成 DamageSingle + All。"""
    with pytest.raises(ValueError, match="DamageSingle"):
        _parse_effects("`DamageAll 30`", "火")


def test_shots_and_shape_percent_do_not_become_standalone_effects():
    """坑 2:通用正则 `(\\w+) (\\d+)` 会把 `Shots 3` / `ShapePercent 50` 当成独立效果收走 ——
    EffectKind 里没有这两个值,落成独立条目会让 ConfigLoader 在加载期直接抛 ConfigException。"""
    effects = _parse_effects("`DamageSingle 10` + `Shots 3` + `ShapePercent 50`", "金")
    assert all(e["kind"] not in ("Shots", "ShapePercent") for e in effects)
    assert len(effects) == 1


# 碾(2026-09-16,土):真伤修饰 —— 跳过整条 DR,单体/AOE 两种伤害都能挂。

def test_true_damage_token_becomes_true_damage_field_on_damage_single():
    assert _parse_effects("`DamageSingle 10` + `TrueDamage`", "土") == [
        {"kind": "DamageSingle", "value": 10, "trueDamage": True}]


def test_true_damage_token_becomes_true_damage_field_on_damage_all():
    assert _parse_effects("`DamageSingle 10` + `All` + `TrueDamage`", "土") == [
        {"kind": "DamageSingle", "value": 10, "trueDamage": True, "shape": "All"}]


def test_no_true_damage_marker_leaves_field_absent():
    """缺省不写 trueDamage —— 恒等性:既有伤害字重新生成后必须逐字节不变。"""
    effects = _parse_effects("`DamageSingle 10`", "土")
    assert "trueDamage" not in effects[0]


def test_true_damage_does_not_become_a_standalone_effect():
    """不挂白名单会被通用正则当成独立效果 kind=TrueDamage 收走 —— EffectKind 里没有这个值,
    会让 ConfigLoader 在加载期直接抛 ConfigException。"""
    effects = _parse_effects("`DamageSingle 10` + `TrueDamage`", "土")
    assert all(e["kind"] != "TrueDamage" for e in effects)
    assert len(effects) == 1


# 镇压(2026-09-16,土):伤害修饰 —— 额外打出自己有效护甲点数的 N%,数值型(同 Pierce 型,
# 不是布尔标记)。

def test_armor_strike_token_becomes_armor_strike_percent_field():
    assert _parse_effects("`DamageSingle 10` + `ArmorStrike 50`", "土") == [
        {"kind": "DamageSingle", "value": 10, "armorStrikePercent": 50}]


def test_no_armor_strike_marker_leaves_field_absent():
    """缺省不写 armorStrikePercent —— 恒等性:既有伤害字重新生成后必须逐字节不变。"""
    effects = _parse_effects("`DamageSingle 10`", "土")
    assert "armorStrikePercent" not in effects[0]


def test_armor_strike_does_not_become_a_standalone_effect():
    """不挂白名单会被通用正则 `(\\w+) (\\d+)` 当成独立效果 kind=ArmorStrike 收走 ——
    EffectKind 里没有这个值,会让 ConfigLoader 在加载期直接抛 ConfigException。"""
    effects = _parse_effects("`DamageSingle 10` + `ArmorStrike 50`", "土")
    assert all(e["kind"] != "ArmorStrike" for e in effects)
    assert len(effects) == 1


# 治疗弹射(2026-09-16,水,海/澡对偶):Chain/ShapePercent 此前只挂在 DamageSingle 上,
# 治疗面(HealSelf)写同样的 token 会被通用循环吞进 consumed、却从没接到 HealSelf 这条
# effect 上 —— 静默丢字段,不报错。

def test_chain_token_becomes_shape_field_on_heal_self():
    assert _parse_effects("`HealSelf 58` + `Chain 3` + `ShapePercent 50`", "水") == [
        {"kind": "HealSelf", "value": 58, "shape": "Chain", "shots": 3, "shapePercent": 50}]


def test_no_shape_marker_leaves_shape_field_absent_on_heal_self():
    """缺省不写 shape —— 恒等性:既有治疗字重新生成后必须逐字节不变。"""
    effects = _parse_effects("`HealSelf 58`", "水")
    assert effects == [{"kind": "HealSelf", "value": 58}]
    assert "shape" not in effects[0]


# 召唤物自动攻击的形状(2026-08-22):同一套 token,落进 passive 的 shape/shots/shapePercent,
# 而不是独立 effect —— BattleEngine.cs:1276-1284 读的就是 passive 上这三个字段。

def test_summon_sweep_token_becomes_passive_shape_field():
    effects = _parse_effects("`Summon 1`(10 血/攻 3) + `Row`", "刀")
    assert effects == [{"kind": "Summon", "value": 10, "count": 1, "attack": 3,
                         "summonChar": "刀", "passive": {"shape": "Row"}}]


def test_summon_cleave_token_becomes_passive_shape_field():
    effects = _parse_effects("`Summon 1`(10 血/攻 3) + `Adjacent`", "刀")
    assert effects == [{"kind": "Summon", "value": 10, "count": 1, "attack": 3,
                         "summonChar": "刀", "passive": {"shape": "Adjacent"}}]


def test_summon_skewer_token_becomes_passive_shape_field():
    effects = _parse_effects("`Summon 1`(10 血/攻 3) + `Column`", "刀")
    assert effects == [{"kind": "Summon", "value": 10, "count": 1, "attack": 3,
                         "summonChar": "刀", "passive": {"shape": "Column"}}]


def test_summon_shots_token_becomes_passive_volley_shape_plus_shots():
    effects = _parse_effects("`Summon 1`(10 血/攻 3) + `Shots 3`", "刀")
    assert effects == [{"kind": "Summon", "value": 10, "count": 1, "attack": 3,
                         "summonChar": "刀", "passive": {"shape": "Scatter", "shots": 3}}]


def test_summon_shape_percent_token_becomes_passive_field():
    effects = _parse_effects("`Summon 1`(10 血/攻 3) + `Row` + `ShapePercent 50`", "刀")
    assert effects == [{"kind": "Summon", "value": 10, "count": 1, "attack": 3,
                         "summonChar": "刀", "passive": {"shape": "Row", "shapePercent": 50}}]


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
    """限时增攻(利):`Empower 30`(turns 2)——管线层只管把 turns 原样落进 effect
    (回合数不随卡等级,spec v7 §1)。"""
    assert _parse_effects("`Empower 30`(turns 2)", "利") == [
        {"kind": "Empower", "value": 30, "turns": 2}]


def test_critbuff_with_turns_attaches_turns():
    """限时暴击(锋):`CritBuff 20`(turns 3)。"""
    assert _parse_effects("`CritBuff 20`(turns 3)", "锋") == [
        {"kind": "CritBuff", "value": 20, "turns": 3}]


def test_critbuff_without_turns_now_raises():
    """2026-09-07(P2 Task 4a,追加 2):`Empower`/`CritBuff` 从 OPTIONAL_DURATION_KINDS
    移进 DURATION_KINDS(P2 落地完成后 利/锋 均已改写成限时版,不再需要那个过渡期口子,
    见 extract_values.py 里的注释)。turns 现在是**强制**的——不写就报错,与 `Reflect`/
    `Silence`/`HealOverTime` 等其余 DURATION_KINDS 成员同一条纪律。"""
    with pytest.raises(Exception) as err:
        _parse_effects("`CritBuff 20`", "锋")
    assert "CritBuff" in str(err.value)


def test_empower_without_turns_now_raises():
    with pytest.raises(Exception) as err:
        _parse_effects("`Empower 50`", "剡")
    assert "Empower" in str(err.value)


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


def test_nature_passive_tokens_map_to_summon_passive_fields():
    """本命新字段(D2-0 Task 6):八个 token 都落进 passive,字段名对齐 SummonPassive;
    `Sprout` 与 `SproutMax` 互不吞(反引号整串匹配),SummonArmor 不被当成 SummonDefense。"""
    config = ("`Summon 1`(100 血/攻 10) + `BackRowBonus 30` + `PerAllyAttack 10` + `SummonArmor 7`"
              " + `HealAllyTimes 2` + `Sprout 40` + `SproutMax 3` + `EntrySaplings 2` + `OnHitCharm 25`")
    effects = _parse_effects(config, "木")
    assert effects == [{"kind": "Summon", "value": 100, "count": 1, "attack": 10, "summonChar": "木",
                        "passive": {"backRowBonusPercent": 30, "perAllyAttackPercent": 10, "armor": 7,
                                    "healAllyTimes": 2, "sproutPercent": 40, "sproutMax": 3,
                                    "entrySaplings": 2, "onHitCharmChance": 25}}]


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


# ---- D1 Task 3:本字修饰器 Amplify / Reshape 与伤害标记的修饰 token ----

def test_amplify_with_scope_and_condition():
    assert _parse_effects("`Amplify 30` + `scope Damage` + `if Burning`", "火") == [
        {"kind": "Amplify", "value": 30, "scope": "Damage", "onlyIf": "Burning"}]


def test_reshape_collects_all_modifier_tokens():
    effects = _parse_effects(
        "`Reshape` + `shape Row` + `shapePercent 50` + `hits 2` + `hitPercent 60` + `forceCrit`"
        " + `armorIgnore 50` + `shieldStrike 40` + `armorStrike 300`", "土")
    assert effects == [{"kind": "Reshape", "value": 0, "shape": "Row", "shapePercent": 50,
                        "hitCount": 2, "hitPercent": 60, "forceCrit": True,
                        "armorIgnorePercent": 50, "shieldStrikePercent": 40,
                        "armorStrikePercent": 300}]


def test_damage_markers_attach_to_damage_single_without_reshape():
    assert _parse_effects("`DamageSingle 100` + `armorIgnore 50` + `forceCrit`", "金") == [
        {"kind": "DamageSingle", "value": 100, "armorIgnorePercent": 50, "forceCrit": True}]


@pytest.mark.parametrize("config, needle", [
    ("`Amplify 30` + `scope Bogus`", "Bogus"),
    ("`Amplify 30` + `if Nope`", "Nope"),
    ("`Reshape` + `shape Ring`", "Ring"),
    ("`Shield 30` + `scope Shield`", "scope"),     # scope 没有 Amplify 可挂
    ("`Shield 30` + `hits 2`", "hits"),            # 标记没有 Reshape / DamageSingle 可挂
])
def test_modifier_token_errors(config, needle):
    with pytest.raises(ValueError) as err:
        _parse_effects(config, "测")
    assert needle in str(err.value)


@pytest.mark.parametrize("config, needle", [
    ("`Amplify 30` + `scope Damage` + `scope Heal`", "scope"),
    ("`Amplify 30` + `if Burning` + `if Slowed`", "if"),
])
def test_multiple_scope_or_if_in_one_cell_raises(config, needle):
    """同格写两个 scope / if 时不能静默共用第一个 —— 第二个会无声消失。"""
    with pytest.raises(ValueError) as err:
        _parse_effects(config, "测")
    assert f"`{needle}`" in str(err.value)


def test_unknown_lowercase_modifier_token_raises():
    """拼错的无数值修饰 token(`forcecrit`)落进消费记账,不能静默过关。"""
    with pytest.raises(Exception) as err:
        _parse_effects("`Reshape` + `forcecrit`", "测")
    assert "forcecrit" in str(err.value)


# ---- D1 Task 4:Augment 叠加修饰器的 `of X` / `field Y` token ----

def test_augment_with_of_and_field():
    assert _parse_effects("`Augment 1` + `of Block` + `field Count`", "铠") == [
        {"kind": "Augment", "value": 1, "augmentKind": "Block", "augmentField": "Count"}]


@pytest.mark.parametrize("config,needle", [
    ("`Augment 1` + `of Block`", "field"),                      # 缺 field
    ("`Augment 1` + `field Count`", "of"),                      # 缺 of
    ("`Augment 1` + `of Block` + `field Bogus`", "Bogus"),      # field 取值未知
    ("`Shield 30` + `of Block`", "of"),                         # 没有 Augment 可挂
    ("`Augment 1` + `of Block` + `of Freeze` + `field Count`", "of"),   # 同格多个
])
def test_augment_token_errors(config, needle):
    with pytest.raises(ValueError) as err:
        _parse_effects(config, "测")
    assert needle in str(err.value)


# ---- D1 Task 5:Weaken / pick / if(非 Amplify)/ keep ----

def test_weaken_takes_value_and_turns():
    assert _parse_effects("`Weaken 15` turns 2", "火") == [{"kind": "Weaken", "value": 15, "turns": 2}]


def test_weaken_without_turns_raises():
    """Weaken 在 DURATION_KINDS 里:漏写 turns 会被引擎静默兜成 1 回合,管线必须拦下。"""
    with pytest.raises(ValueError):
        _parse_effects("`Weaken 15`", "火")


# ---- D1 Task 6:Seed / Vulnerable ----

def test_seed_takes_value_and_turns():
    assert _parse_effects("`Seed 10` turns 2", "木") == [{"kind": "Seed", "value": 10, "turns": 2}]


def test_seed_without_turns_raises():
    """Seed 在 DURATION_KINDS 里:漏写 turns 会被引擎静默兜成 1 回合,管线必须拦下。"""
    with pytest.raises(ValueError):
        _parse_effects("`Seed 10`", "木")


def test_vulnerable_takes_optional_turns():
    assert _parse_effects("`Vulnerable 20` turns 2", "水") == [{"kind": "Vulnerable", "value": 20, "turns": 2}]


def test_vulnerable_bind_usage_has_no_turns():
    """冰缚:Turns 缺省 0 + pick FrozenByThisCast = 回合数跟随冻结回合,所以 Vulnerable 不进 DURATION_KINDS。"""
    effects = _parse_effects("`Freeze 2` + `Vulnerable 20` + `pick FrozenByThisCast`", "水")
    assert effects == [{"kind": "Freeze", "value": 2},
                       {"kind": "Vulnerable", "value": 20, "pick": "FrozenByThisCast"}]


def test_vulnerable_without_turns_or_bind_pick_raises():
    with pytest.raises(ValueError):
        _parse_effects("`Vulnerable 20`", "水")
    with pytest.raises(ValueError):
        _parse_effects("`Vulnerable 20` + `pick All`", "水")


def test_vulnerable_bind_pick_may_omit_turns():
    assert _parse_effects("`Vulnerable 20` + `pick FrozenByThisCast`", "水") == [
        {"kind": "Vulnerable", "value": 20, "pick": "FrozenByThisCast"}]


def test_seed_and_vulnerable_accept_pick():
    assert _parse_effects("`Seed 5` + `pick Random` turns 2", "木")[0]["pick"] == "Random"
    assert _parse_effects("`Vulnerable 10` + `pick All` turns 1", "水")[0]["pick"] == "All"


def test_pick_attaches_to_preceding_effect_only():
    effects = _parse_effects("`BurnSingle 3` + `Weaken 20` + `pick HitTargets` turns 1", "土")
    assert effects == [{"kind": "BurnSingle", "value": 3},
                       {"kind": "Weaken", "value": 20, "pick": "HitTargets", "turns": 1}]


@pytest.mark.parametrize("pick", ["All", "Random", "HitTargets", "MostBurn", "FrozenByThisCast"])
def test_every_pick_value_is_accepted(pick):
    assert _parse_effects(f"`Slow 1` + `pick {pick}`", "水") == [{"kind": "Slow", "value": 1, "pick": pick}]


def test_if_on_non_amplify_attaches_condition_gate():
    assert _parse_effects("`BurnSingle 2` + `if Burning`", "火") == [
        {"kind": "BurnSingle", "value": 2, "onlyIf": "Burning"}]


def test_keep_attaches_to_burn_settle_now():
    assert _parse_effects("`BurnSettleNow` + `keep` + `pick All`", "火") == [
        {"kind": "BurnSettleNow", "value": 0, "keepStacks": True, "pick": "All"}]


@pytest.mark.parametrize("config, needle", [
    ("`Slow 1` + `pick Bogus`", "Bogus"),                    # 取值未知
    ("`pick All` + `Slow 1`", "pick"),                       # 前面没有可挂的效果
    ("`Shield 3` + `pick All`", "pick"),                     # Shield 不认选择器
    ("`Slow 1` + `keep`", "keep"),                           # keep 只挂 BurnSettleNow
    ("`Slow 1` + `pick All` + `pick Random`", "pick"),       # 同一条效果多个 pick
    ("`BurnSingle 2` + `if Nope`", "Nope"),                  # 条件名未知
])
def test_pick_if_keep_errors(config, needle):
    with pytest.raises(ValueError) as err:
        _parse_effects(config, "测")
    assert needle in str(err.value)


# ---- D1 Task 7:我方侧新效果 ----

def test_ally_side_simple_kinds():
    assert _parse_effects("`DamageCut 20`", "土") == [{"kind": "DamageCut", "value": 20}]
    assert _parse_effects("`CounterBoost 100`", "金") == [{"kind": "CounterBoost", "value": 100}]
    assert _parse_effects("`ShieldFromHeal 20`", "水") == [{"kind": "ShieldFromHeal", "value": 20}]
    assert _parse_effects("`AddWellspring 2`", "水") == [{"kind": "AddWellspring", "value": 2}]
    assert _parse_effects("`AddHeft 2`", "土") == [{"kind": "AddHeft", "value": 2}]
    assert _parse_effects("`SummonStrike 50`", "木") == [{"kind": "SummonStrike", "value": 50}]
    assert _parse_effects("`ShieldSummons 15`", "木") == [{"kind": "ShieldSummons", "value": 15}]


def test_damage_cut_is_not_a_damage_effect():
    """DamageCut 以 Damage 开头,但不是伤害:伤害修饰(分段 / 穿透 / 斩杀 / 碾 / 条件翻倍)不能挂到它身上。"""
    effects = _parse_effects(
        "`DamageSingle 30` + `HitCount 2` + `Pierce 5` + `TrueDamage` + `DoubleVsBurning` + `DamageCut 20`", "土")
    assert effects[1] == {"kind": "DamageCut", "value": 20}
    assert effects[0]["hitCount"] == 2 and effects[0]["pierce"] == 5 and effects[0]["trueDamage"] is True


def test_heal_summons_pct_token():
    assert _parse_effects("`HealSummons 30` + `pct`", "木") == [
        {"kind": "HealSummons", "value": 30, "percentOfMax": True}]
    assert _parse_effects("`HealSummons 30`", "木") == [{"kind": "HealSummons", "value": 30}]
    with pytest.raises(ValueError):
        _parse_effects("`HealSelf 30` + `pct`", "水")


def test_summon_sapling_count_token():
    assert _parse_effects("`SummonSapling 20`", "木") == [{"kind": "SummonSapling", "value": 20}]
    assert _parse_effects("`SummonSapling 20` + `count 2`", "木") == [
        {"kind": "SummonSapling", "value": 20, "count": 2}]
    with pytest.raises(ValueError):
        _parse_effects("`Shield 5` + `count 2`", "土")


def test_endure_and_cleanse_self_picks():
    assert _parse_effects("`Endure` + `pick SummonedThisCast`", "木") == [
        {"kind": "Endure", "value": 0, "pick": "SummonedThisCast"}]
    assert _parse_effects("`DamageSingle 10` + `Cleanse 1` + `pick Self`", "水") == [
        {"kind": "DamageSingle", "value": 10}, {"kind": "Cleanse", "value": 1, "pick": "Self"}]


def test_taunt_effect_with_picks():
    """D2-0 Task 2:`Taunt N`(N = 回合数,0 = 本场)按 pick 落点;不与召唤物被动的布尔 `Taunt` 冲突。"""
    assert _parse_effects("`Taunt 1` + `pick Self`", "土") == [
        {"kind": "Taunt", "value": 1, "pick": "Self"}]
    assert _parse_effects("`Taunt 0` + `pick SummonedThisCast`", "土") == [
        {"kind": "Taunt", "value": 0, "pick": "SummonedThisCast"}]
    assert _parse_effects("`Taunt 2` + `pick AllSummons`", "木") == [
        {"kind": "Taunt", "value": 2, "pick": "AllSummons"}]


@pytest.mark.parametrize("config", [
    "`Taunt 1`",                               # 嘲讽必须写 pick
    "`Taunt 1` + `pick Random`",               # 嘲讽不认敌方侧选择器
    "`Taunt 1` + `pick Self` + `if Burning`",  # 我方侧效果不带条件门
    "`Slow 1` + `pick AllSummons`",            # AllSummons 只给嘲讽
    "`Endure` + `pick AllSummons`",
])
def test_taunt_pick_combos_rejected(config):
    with pytest.raises(ValueError):
        _parse_effects(config, "测")


@pytest.mark.parametrize("config", [
    "`Slow 1` + `pick Self`",                  # Self 只给净化
    "`Cleanse 1` + `pick All`",                # 净化只认 Self
    "`Endure` + `pick Random`",                # 保命只认 SummonedThisCast
    "`Cleanse 1` + `pick SummonedThisCast`",
    "`Cleanse 1` + `if Burning`",              # 条件门只给敌方侧效果
    "`Endure`",                                # 保命必须写 pick SummonedThisCast(Ruling 10)
])
def test_ally_pick_combos_rejected(config):
    with pytest.raises(ValueError):
        _parse_effects(config, "测")


# ---- D1 Task 9:附着载体(烟熏 `rider Burn`)与反震 `ShieldRecoil N` ----

def test_rider_burn_attaches_to_blind_without_turns():
    assert _parse_effects("`Blind 15` + `pick HitTargets` + `rider Burn`", "火") == [
        {"kind": "Blind", "value": 15, "pick": "HitTargets", "riderOf": "Burn"}]


def test_shield_recoil_is_a_plain_kind():
    assert _parse_effects("`ShieldRecoil 30`", "土") == [{"kind": "ShieldRecoil", "value": 30}]


@pytest.mark.parametrize("config, needle", [
    ("`Blind 15` + `rider Freeze`", "Freeze"),               # 载体只认 Burn
    ("`Slow 1` + `rider Burn`", "rider"),                    # 只有 Blind 能附着
    ("`rider Burn` + `Blind 15`", "rider"),                  # 前面没有可挂的效果
    ("`Blind 15`", "turns"),                                 # 不附着的致盲仍必须写 turns
])
def test_rider_errors(config, needle):
    with pytest.raises(ValueError) as err:
        _parse_effects(config, "测")
    assert needle in str(err.value)


# ---- D2-火 Task 1:新条件 / 新选择器 / scope Burn / Reshape 重选目标 / bodyPercent ----

@pytest.mark.parametrize("pick", ["Row", "Adjacent", "BurnedByThisCast"])
def test_d2fire_new_picks_accepted(pick):
    assert _parse_effects(f"`BurnSingle 3` + `pick {pick}`", "火") == [
        {"kind": "BurnSingle", "value": 3, "pick": pick}]


def test_d2fire_smoke_rider_with_burned_by_this_cast():
    assert _parse_effects("`Blind 15` `pick BurnedByThisCast` `rider Burn`", "火") == [
        {"kind": "Blind", "value": 15, "pick": "BurnedByThisCast", "riderOf": "Burn"}]


@pytest.mark.parametrize("cond", ["PlayerHpAbove70", "HasSummon"])
def test_d2fire_new_conditions_accepted(cond):
    assert _parse_effects(f"`Amplify 20` `scope All` `if {cond}`", "火") == [
        {"kind": "Amplify", "value": 20, "scope": "All", "onlyIf": cond}]
    assert _parse_effects(f"`BurnSingle 1` `pick All` `if {cond}`", "火") == [
        {"kind": "BurnSingle", "value": 1, "pick": "All", "onlyIf": cond}]


def test_d2fire_scope_burn_accepted():
    assert _parse_effects("`Amplify 100` `scope Burn`", "火") == [
        {"kind": "Amplify", "value": 100, "scope": "Burn"}]


def test_d2fire_reshape_takes_pick():
    assert _parse_effects("`Reshape` `pick Row`", "火") == [{"kind": "Reshape", "value": 0, "pick": "Row"}]


def test_d2fire_reshape_rejects_ally_pick():
    with pytest.raises(ValueError):
        _parse_effects("`Reshape` `pick Self`", "火")


def test_d2fire_body_percent_attaches_to_damage():
    assert _parse_effects("`DamageSingle 0` `All` `bodyPercent 100`", "火") == [
        {"kind": "DamageSingle", "value": 0, "shape": "All", "bodyPercent": 100}]


def test_d2fire_body_percent_without_damage_raises():
    with pytest.raises(ValueError) as err:
        _parse_effects("`BurnAll 2` `bodyPercent 100`", "火")
    assert "bodyPercent" in str(err.value)


# ---- D2-火 Task 1(第二批):开局登记 `battles N` 挂在它前面最近的那条效果上 ----

def test_d2fire_battles_attaches_to_preceding_effect():
    effects = _parse_effects("`BurnAll 2` + `Weaken 20` `pick All` `turns 2` + `BurnAll 2` `battles 5`", "焱")
    assert effects[0] == {"kind": "BurnAll", "value": 2}
    assert effects[2] == {"kind": "BurnAll", "value": 2, "openingBattles": 5}


def test_d2fire_battles_without_preceding_effect_raises():
    with pytest.raises(ValueError) as err:
        _parse_effects("`battles 1` `BurnAll 2`", "炎")
    assert "battles" in str(err.value)


# ---- D2-火 Task 2:灼操作族 token(附录 N1 / N2 / N3 / N4 / N4b) ----

def test_d2fire_burn_scale_and_equalize():
    assert _parse_effects("`BurnScale 200` `pick All`", "炎") == [{"kind": "BurnScale", "value": 200, "pick": "All"}]
    assert _parse_effects("`BurnSingle 2` + `BurnScale 200`", "燥") == [
        {"kind": "BurnSingle", "value": 2}, {"kind": "BurnScale", "value": 200}]
    assert _parse_effects("`BurnEqualize`", "烈") == [{"kind": "BurnEqualize", "value": 0}]


def test_d2fire_detonate_retain_and_portion():
    assert _parse_effects("`Detonate` `pick All` `retain 50`", "炸") == [
        {"kind": "Detonate", "value": 0, "pick": "All", "retainPercent": 50}]
    assert _parse_effects("`DetonateAll` `retain 33`", "燚") == [
        {"kind": "Detonate", "value": 0, "targetAll": True, "retainPercent": 33}]
    assert _parse_effects("`Amplify 10` `scope Damage` `per BurnStack` + `Detonate` `portion 50`", "燥") == [
        {"kind": "Amplify", "value": 10, "scope": "Damage", "scaleBy": "BurnStack"},
        {"kind": "Detonate", "value": 0, "portionPercent": 50}]


def test_d2fire_per_and_cap():
    assert _parse_effects("`Amplify 5` `scope Damage` `per BurnStack` `cap 50`", "燥") == [
        {"kind": "Amplify", "value": 5, "scope": "Damage", "scaleBy": "BurnStack", "scaleCap": 50}]
    assert _parse_effects("`HealSelf 20` `per BurningEnemy`", "蒸") == [
        {"kind": "HealSelf", "value": 20, "scaleBy": "BurningEnemy"}]


def test_d2fire_hit_sugar_becomes_per_hit():
    assert _parse_effects("`Reshape` `hits 2` `hitSettle`", "炎") == [
        {"kind": "Reshape", "value": 0, "hitCount": 2,
         "perHit": [{"kind": "BurnSettleNow", "value": 0, "keepStacks": True}]}]
    assert _parse_effects("`Reshape` `hits 4` `hitPercent 30` `hitBurn 1`", "燚") == [
        {"kind": "Reshape", "value": 0, "hitCount": 4, "hitPercent": 30,
         "perHit": [{"kind": "BurnSingle", "value": 1}]}]
    assert _parse_effects("`Reshape` `shape Scatter` `shots 4` `shotPercent 50` `hitBurn 1`", "焱") == [
        {"kind": "Reshape", "value": 0, "shape": "Scatter", "shots": 4, "shotPercent": 50,
         "perHit": [{"kind": "BurnSingle", "value": 1}]}]


def test_d2fire_generic_per_hit_section():
    # Q23 通用形态:`perHit [N]` 之后的全部 token 是每击附带的效果,各自照常解析(turns / pick / keep 都认)
    assert _parse_effects("`Reshape` `hits 3` `perHit 2` `ArmorBreak 5` `turns 2` + `Morale 1`", "金") == [
        {"kind": "Reshape", "value": 0, "hitCount": 3, "perHitFrom": 2,
         "perHit": [{"kind": "ArmorBreak", "value": 5, "turns": 2}, {"kind": "Morale", "value": 1}]}]
    assert _parse_effects("`DamageSingle 40` `perHit` `BurnSettleNow` `keep`", "炎") == [
        {"kind": "DamageSingle", "value": 40, "perHit": [{"kind": "BurnSettleNow", "value": 0, "keepStacks": True}]}]


def test_d2fire_segment_keeps_authoring_order_one_effect_per_plus():
    # 终审 5:`perHit` / `onHit` 段写在格子末尾,段内每个 `+` 分段恰好一条效果,按书写顺序落表(不按解析器内部顺序重排),
    # 修饰 token(turns 等)只归本分段那条效果
    assert _parse_effects("`DamageSingle 40` `perHit` `BurnSettleNow` `keep` + `BurnSingle 1`", "炎")[0]["perHit"] == [
        {"kind": "BurnSettleNow", "value": 0, "keepStacks": True}, {"kind": "BurnSingle", "value": 1}]
    assert _parse_effects("`DamageSingle 40` `perHit` `Weaken 5` `turns 2` + `Weaken 7` `turns 1`", "金")[0]["perHit"] == [
        {"kind": "Weaken", "value": 5, "turns": 2}, {"kind": "Weaken", "value": 7, "turns": 1}]
    assert _parse_effects("`Retaliate` `onHit` `Weaken 5` `turns 2` + `BurnSingle 2`", "烈")[0]["perHit"] == [
        {"kind": "Weaken", "value": 5, "turns": 2}, {"kind": "BurnSingle", "value": 2}]


@pytest.mark.parametrize("config, needle", [
    ("`DamageSingle 40` `perHit` `BurnSingle 1` `Morale 1`", "perHit"),          # 一个分段写了两条效果(漏了 +)
    ("`Retaliate` `onHit` `BurnSingle 1` `Bleed 2`", "onHit"),                   # 同上(回敬段)
    ("`DamageSingle 40` `perHit` `BurnSingle 1` + ", "perHit"),                  # 空分段
])
def test_d2fire_segment_one_effect_per_plus_errors(config, needle):
    with pytest.raises(ValueError) as err:
        _parse_effects(config, "测")
    assert needle in str(err.value)


@pytest.mark.parametrize("config, needle", [
    ("`BurnSingle 2` `retain 50`", "retain"),                          # retain 只挂 Detonate
    ("`Detonate` `retain 50` `portion 50`", "portion"),                # 二选一
    ("`Amplify 5` `per Bogus`", "per"),                                # 取值未知
    ("`Shield 5` `per BurnStack`", "per"),                             # 只挂 Amplify / HealSelf
    ("`Amplify 5` `cap 50`", "cap"),                                   # cap 没有 per
    ("`HealSelf 5` `per BurningEnemy` `cap 50`", "cap"),               # cap 只给 Amplify
    ("`BurnAll 2` `hitBurn 1`", "perHit"),                             # 没有伤害 / Reshape
    ("`Reshape` `hitBurn 1` `perHit` `BurnSingle 1`", "perHit"),       # 糖与通用写法混用
    ("`Reshape` `perHit` `DamageSingle 5`", "perHit"),                 # 每击附带里不能再有伤害
    ("`Reshape` `perHit` `SelfCost 20`", "perHit"),                    # 每击附带里不能自损(每击扣一次血)
    ("`Reshape` `perHit`", "perHit"),                                  # 空的每击附带
    ("`Reshape` `perHit 1` `BurnSingle 1` `perHit` `Morale 1`", "perHit"),  # 只能有一段
])
def test_d2fire_task2_token_errors(config, needle):
    with pytest.raises(ValueError) as err:
        _parse_effects(config, "测")
    assert needle in str(err.value)


# ---- D2-火 Task 3:灼附着族(附录 N5)—— 附录 §2 的拟写行 ----

@pytest.mark.parametrize("config, expected", [
    # 干涸
    ("`HealBlock` `pick BurnedByThisCast` `rider Burn`",
     {"kind": "HealBlock", "value": 0, "pick": "BurnedByThisCast", "riderOf": "Burn"}),
    # 上炎:保留自己的回合数
    ("`BurnGrow 1` `turns 3` `pick BurnedByThisCast` `rider Burn`",
     {"kind": "BurnGrow", "value": 1, "turns": 3, "pick": "BurnedByThisCast", "riderOf": "Burn"}),
    # 四火
    ("`BurnHold` `pick BurnedByThisCast` `rider Burn`",
     {"kind": "BurnHold", "value": 0, "pick": "BurnedByThisCast", "riderOf": "Burn"}),
    # 焚城
    ("`BurnBurst` `pick BurnedByThisCast` `rider Burn`",
     {"kind": "BurnBurst", "value": 0, "pick": "BurnedByThisCast", "riderOf": "Burn"}),
    # 焚身(载体)
    ("`BurnBacklash` `pick BurnedByThisCast` `rider Burn`",
     {"kind": "BurnBacklash", "value": 0, "pick": "BurnedByThisCast", "riderOf": "Burn"}),
    # 炽焰:附着减攻 + 门槛,不写 turns
    ("`Weaken 30` `pick BurnedByThisCast` `rider Burn` `minBurn 5`",
     {"kind": "Weaken", "value": 30, "pick": "BurnedByThisCast", "riderOf": "Burn", "minBurn": 5}),
])
def test_d2fire_rider_family(config, expected):
    assert _parse_effects(config, "火") == [expected]


def test_d2fire_rider_family_after_burn_keeps_order():
    """附着写在点灼之后(结算顺序 = 列表顺序):先上灼,附着才找得到本字的灼。"""
    effects = _parse_effects("`BurnSingle 2` + `BurnBurst` `pick BurnedByThisCast` `rider Burn`", "火")
    assert [e["kind"] for e in effects] == ["BurnSingle", "BurnBurst"]
    assert effects[1]["riderOf"] == "Burn" and "riderOf" not in effects[0]


@pytest.mark.parametrize("config, needle", [
    ("`HealBlock` `pick BurnedByThisCast`", "rider"),                 # 附着族必须写 rider
    ("`BurnGrow 1` `turns 3`", "rider"),
    ("`Weaken 30` `turns 2` `minBurn 5`", "minBurn"),                  # minBurn 只给附着的减攻
    ("`Blind 15` `rider Burn` `minBurn 5`", "minBurn"),                # minBurn 只给减攻
    ("`minBurn 5` `Weaken 30` `rider Burn`", "minBurn"),               # 前面没有可挂的效果
    ("`Seed 10` `turns 2` `rider Burn`", "rider"),                     # 名单外的 Kind 不能附着
])
def test_d2fire_rider_family_errors(config, needle):
    with pytest.raises(ValueError) as err:
        _parse_effects(config, "测")
    assert needle in str(err.value)


# ---- D2-火 Task 4:埋雷(Mine)与受击回敬(Retaliate + onHit 段)----

@pytest.mark.parametrize("config, expected", [
    ("`Mine 50`", {"kind": "Mine", "value": 50}),
    ("`Mine` `bodyPercent 200`", {"kind": "Mine", "value": 0, "bodyPercent": 200}),
    ("`Mine 30` `pick All`", {"kind": "Mine", "value": 30, "pick": "All"}),
])
def test_d2fire_mine(config, expected):
    assert _parse_effects(config, "炸") == [expected]


def test_d2fire_retaliate_on_hit_section():
    """烈焰护身:`onHit` 之后的 token 是回敬效果,挂到本格唯一的 Retaliate 上(写法同 perHit 段)。"""
    effects = _parse_effects("`BurnAll 2` + `Retaliate` `onHit` `BurnSingle 2`", "烈")
    assert effects == [{"kind": "BurnAll", "value": 2},
                       {"kind": "Retaliate", "value": 0, "perHit": [{"kind": "BurnSingle", "value": 2}]}]
    capped = _parse_effects("`Retaliate 1` `onHit` `Bleed 3` + `ArmorBreak 2` `turns 2`", "金")
    assert capped == [{"kind": "Retaliate", "value": 1,
                       "perHit": [{"kind": "Bleed", "value": 3}, {"kind": "ArmorBreak", "value": 2, "turns": 2}]}]


@pytest.mark.parametrize("config, needle", [
    ("`Retaliate`", "onHit"),                                          # 回敬必须写 onHit 段
    ("`BurnAll 2` `onHit` `BurnSingle 2`", "Retaliate"),               # onHit 没有宿主
    ("`Retaliate` `onHit` `DamageSingle 5`", "onHit"),                 # 回敬里不能有伤害
    ("`Retaliate` `onHit` `Weaken 10` `turns 2` `if Burning`", "onHit"),   # 不能带条件门
    ("`Retaliate` `onHit` `Blind 10` `turns 2` `pick All`", "onHit"),            # 不能带选择器(对象就是攻击者)
    ("`Retaliate` `onHit`", "onHit"),                                  # 空段
    ("`Mine`", "Mine"),                                                # 地雷没有伤害量
])
def test_d2fire_task4_token_errors(config, needle):
    with pytest.raises(ValueError) as err:
        _parse_effects(config, "测")
    assert needle in str(err.value)


# ---- D2-火 Task 5:其余单点效果(附录 N9 / N10 / N11)----

@pytest.mark.parametrize("config, char, expected", [
    # 星火:每命中一名出字前带灼的敌人,对随机敌人追加本体 30%
    ("`ExtraStrike 30` `pick Random` `perBurningHit`", "焱",
     [{"kind": "ExtraStrike", "value": 30, "pick": "Random", "perBurningHit": True}]),
    # 烈焚:对灼层最高者追加本体 50%
    ("`ExtraStrike 50` `pick MostBurn`", "焚", [{"kind": "ExtraStrike", "value": 50, "pick": "MostBurn"}]),
    # 玉石俱焚
    ("`SelfCost 20` + `Amplify 150` `scope Damage` + `BurnAll 3`", "焚",
     [{"kind": "SelfCost", "value": 20}, {"kind": "Amplify", "value": 150, "scope": "Damage"},
      {"kind": "BurnAll", "value": 3}]),
    # 光耀
    ("`Reveal` + `Vulnerable 15` `turns 1`", "灿",
     [{"kind": "Vulnerable", "value": 15, "turns": 1}, {"kind": "Reveal", "value": 0}]),
    # 水火相激的解冻(本格没有 Amplify 时条件门按位置挂在解冻上)
    ("`Thaw` `if Controlled`", "蒸", [{"kind": "Thaw", "value": 0, "onlyIf": "Controlled"}]),
    ("`Thaw` `pick All`", "蒸", [{"kind": "Thaw", "value": 0, "pick": "All"}]),
])
def test_d2fire_task5_single_ops(config, char, expected):
    assert _parse_effects(config, char) == expected


@pytest.mark.parametrize("config", [
    "`BurnSingle 2` `perBurningHit`",                          # perBurningHit 没有 ExtraStrike 宿主
    "`ExtraStrike 30` + `ExtraStrike 20` `perBurningHit`",     # 宿主不唯一
])
def test_d2fire_task5_per_burning_hit_needs_one_extra_strike(config):
    with pytest.raises(ValueError) as err:
        _parse_effects(config, "测")
    assert "perBurningHit" in str(err.value)


# ---- D2-金 Task 1:共用小扩展(附录 E6 / E7 / E8 / E9 / E10 / E12 / E13;E11 只验证每击附带的写法)----

@pytest.mark.parametrize("config, char, expected", [
    # 剑意:BlockMod 反击百分比
    ("`Morale 1` + `BlockMod` `counter 50`", "剑",
     [{"kind": "Morale", "value": 1}, {"kind": "BlockMod", "value": 0, "counterPercent": 50}]),
    # 横扫千军:战意 × 多命中的敌人数
    ("`Reshape` `shape Row` `shapePercent 50` + `Morale 1` `per ExtraHitTarget`", "剑",
     [{"kind": "Morale", "value": 1, "scaleBy": "ExtraHitTarget"},
      {"kind": "Reshape", "value": 0, "shape": "Row", "shapePercent": 50}]),
    # 大卸八块:击数 = 1 + 战意
    ("`Reshape` `hitPercent 35` `hitsPerMorale`", "剁",
     [{"kind": "Reshape", "value": 0, "hitPercent": 35, "scaleBy": "Morale"}]),
    # 双金合璧:格挡次数 = 战意(至少 2)+ 开局登记
    ("`BlockMod` `countPerMorale` `min 2` + `Morale 2` `battles 1`", "鍂",
     [{"kind": "Morale", "value": 2, "openingBattles": 1},
      {"kind": "BlockMod", "value": 0, "scaleBy": "Morale", "scaleMin": 2}]),
    # 放血:流血读 turns
    ("`Bleed 50` `turns 2`", "刲", [{"kind": "Bleed", "value": 50, "turns": 2}]),
    # 割取
    ("`HealSelf 10` `ofVictimMaxHp`", "刲", [{"kind": "HealSelf", "value": 10, "ofVictimMaxHp": True}]),
    # 三金破 / 刚:MoraleFull 条件
    ("`Reshape` `hits 3` `hitPercent 50` + `Amplify 100` `scope Damage` `if MoraleFull`", "鑫",
     [{"kind": "Amplify", "value": 100, "scope": "Damage", "onlyIf": "MoraleFull"},
      {"kind": "Reshape", "value": 0, "hitCount": 3, "hitPercent": 50}]),
    ("`Amplify 30` `scope All` `if MoraleFull`", "𨰻",
     [{"kind": "Amplify", "value": 30, "scope": "All", "onlyIf": "MoraleFull"}]),
    # 千锤 / 金刚:补满 + BlockMod
    ("`MoraleFill` + `BlockMod` `counter 50`", "𨰻",
     [{"kind": "Morale", "value": 0, "fill": True}, {"kind": "BlockMod", "value": 0, "counterPercent": 50}]),
    ("`Augment 3` `of Block` `field Count` + `BlockMod` `counter 60` + `MoraleFill`", "𨰻",
     [{"kind": "Augment", "value": 3, "augmentKind": "Block", "augmentField": "Count"},
      {"kind": "Morale", "value": 0, "fill": True}, {"kind": "BlockMod", "value": 0, "counterPercent": 60}]),
    # 铡刀落:斩杀挂在 Reshape 上
    ("`Reshape` `ExecuteKill 35`", "铡",
     [{"kind": "Reshape", "value": 0, "executeBelowPercent": 35, "executeKills": True}]),
    # E11:金系每击附带(破甲带自己的 turns、战意、流血从第 2 击起)
    ("`Reshape` `hits 2` `hitPercent 60` `perHit` `ArmorBreak 20` `turns 3`", "鍂",
     [{"kind": "Reshape", "value": 0, "hitCount": 2, "hitPercent": 60,
       "perHit": [{"kind": "ArmorBreak", "value": 20, "turns": 3}]}]),
    ("`Reshape` `hits 2` `perHit` `Morale 1`", "鍂",
     [{"kind": "Reshape", "value": 0, "hitCount": 2, "perHit": [{"kind": "Morale", "value": 1}]}]),
    ("`Reshape` `perHit 2` `Bleed 35`", "剁",
     [{"kind": "Reshape", "value": 0, "perHit": [{"kind": "Bleed", "value": 35}], "perHitFrom": 2}]),
])
def test_d2metal_task1_tokens(config, char, expected):
    assert _parse_effects(config, char) == expected


def test_d2metal_execute_splash_rides_with_execute_kill():
    """D2-金 J4(铡刀落):`executeSplash N` 跟斩杀挂在同一个宿主上(Reshape 优先)。"""
    assert _parse_effects("`Reshape` `ExecuteKill 35` `executeSplash 20`", "铡") == [
        {"kind": "Reshape", "value": 0, "executeBelowPercent": 35, "executeKills": True, "executeSplashPercent": 20}]
    assert _parse_effects("`DamageSingle 30` `ExecuteKill 20` `executeSplash 10`", "铡") == [
        {"kind": "DamageSingle", "value": 30, "executeBelowPercent": 20, "executeKills": True, "executeSplashPercent": 10}]


@pytest.mark.parametrize("config", [
    "`DamageSingle 30` `executeSplash 20`",                    # 没有斩杀
    "`DamageSingle 30` `ExecuteBonus 20` `executeSplash 20`",  # 残血加伤不是斩杀
    "`DamageSingle 30` `ExecuteKill 20` `executeSplash 0`",    # 0 = 静默无效
    "`DamageSingle 30` `ExecuteKill 20` `executeSplash 101`",
])
def test_d2metal_execute_splash_errors(config):
    with pytest.raises(ValueError, match="executeSplash"):
        _parse_effects(config, "铡")


def test_d2metal_execute_still_attaches_to_damage_without_reshape():
    assert _parse_effects("`DamageSingle 30` `ExecuteKill 20`", "铡") == [
        {"kind": "DamageSingle", "value": 30, "executeBelowPercent": 20, "executeKills": True}]


@pytest.mark.parametrize("config, needle", [
    ("`Block 1` `counter 50`", "counter"),                          # counter 没有 BlockMod 宿主
    ("`BlockMod` `min 2`", "min"),                                  # min 没配 countPerMorale
    ("`Block 1` `countPerMorale`", "countPerMorale"),               # countPerMorale 没有 BlockMod 宿主
    ("`BlockMod` `countPerMorale`", "min"),                         # 次数按战意必须写下限
    ("`Shield 5` `hitsPerMorale`", "hitsPerMorale"),                # 没有伤害 / Reshape
    ("`Shield 5` `ofVictimMaxHp`", "ofVictimMaxHp"),                # 没有 HealSelf
    ("`Amplify 5` `per ExtraHitTarget`", "per"),                    # ExtraHitTarget 只给 Morale
    ("`Morale 1` `per BurnStack`", "per"),                          # BurnStack 不给 Morale
    ("`BlockMod`", "BlockMod"),                                     # 什么也没改
])
def test_d2metal_task1_token_errors(config, needle):
    with pytest.raises(ValueError) as err:
        _parse_effects(config, "测")
    assert needle in str(err.value)


# ---- D2-金 Task 2:格挡附带(附录 J1)----

@pytest.mark.parametrize("config, char, expected", [
    # 得利
    ("`Morale 2` + `BlockMod` `killRefund 1`", "利",
     [{"kind": "Morale", "value": 2}, {"kind": "BlockMod", "value": 0, "killRefundAp": 1}]),
    # 锥立
    ("`Augment 1` `of Block` `field Count` + `BlockMod` `counterShape Column`", "锥",
     [{"kind": "Augment", "value": 1, "augmentKind": "Block", "augmentField": "Count"},
      {"kind": "BlockMod", "value": 0, "counterColumn": True}]),
    # 坚营
    ("`Augment 1` `of Block` `field Count` + `BlockMod` `blockMorale 1`", "剿",
     [{"kind": "Augment", "value": 1, "augmentKind": "Block", "augmentField": "Count"},
      {"kind": "BlockMod", "value": 0, "blockMorale": 1}]),
    # 剁截
    ("`BlockMod` `counterHits 3` `counter 40`", "剁",
     [{"kind": "BlockMod", "value": 0, "counterPercent": 40, "counterHits": 3}]),
    # 刀山 / 匿锋
    ("`Augment 2` `of Block` `field Count` + `BlockMod` `blockBleed 35`", "剁",
     [{"kind": "Augment", "value": 2, "augmentKind": "Block", "augmentField": "Count"},
      {"kind": "BlockMod", "value": 0, "blockBleed": 35}]),
    ("`Morale 2` + `BlockMod` `blockBleed 50`", "刲",
     [{"kind": "Morale", "value": 2}, {"kind": "BlockMod", "value": 0, "blockBleed": 50}]),
    # 立威
    ("`BlockMod` `counterExecute 20`", "铡", [{"kind": "BlockMod", "value": 0, "counterExecuteBelow": 20}]),
])
def test_d2metal_task2_block_rider_tokens(config, char, expected):
    assert _parse_effects(config, char) == expected


@pytest.mark.parametrize("config, needle", [
    ("`Block 1` `counterHits 3`", "counterHits"),          # 没有 BlockMod 宿主
    ("`Block 1` `counterShape Column`", "counterShape"),
    ("`BlockMod` `counterShape Row`", "counterShape"),      # 只认 Column
    ("`BlockMod` `counterExecute 100`", "counterExecute"),  # 阈值须 < 100
    ("`BlockMod` `killRefund 0`", "killRefund"),            # 须 ≥ 1
])
def test_d2metal_task2_block_rider_token_errors(config, needle):
    with pytest.raises(ValueError) as err:
        _parse_effects(config, "测")
    assert needle in str(err.value)


# ---- D2-金 Task 3(附录 J5):致命 ----

def test_d2metal_doom_value_is_turns_and_takes_pick():
    assert _parse_effects("`Doom 2`", "刲") == [{"kind": "Doom", "value": 2}]
    assert _parse_effects("`Doom 2` + `pick All`", "刲") == [{"kind": "Doom", "value": 2, "pick": "All"}]
    assert _parse_effects("`Doom 2` + `if TargetHpAbove70`", "刲") == [
        {"kind": "Doom", "value": 2, "onlyIf": "TargetHpAbove70"}]


# ---- D2-金 Task 4(附录 J7 / J8):战意族,通用 `Kind N` 解析,无需映射表 ----

def test_d2metal_morale_family_values():
    assert _parse_effects("`Morale 3` + `MoraleOverflowShield 30`", "鑫") == [
        {"kind": "Morale", "value": 3}, {"kind": "MoraleOverflowShield", "value": 30}]
    assert _parse_effects("`MoraleArmor 5`", "鑫") == [{"kind": "MoraleArmor", "value": 5}]
    assert _parse_effects("`MoraleShield 40`", "鍂") == [{"kind": "MoraleShield", "value": 40}]
