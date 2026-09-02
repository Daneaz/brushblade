"""字表导出:配方生成、叠字链人工兜底、数值抽取。"""
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from export_chars import STACK_RECIPES, build_chars
from extract_values import extract

SPEC = Path(__file__).resolve().parents[3] / "docs/design/字选型/技能机制详表.md"
IDS = Path(__file__).resolve().parent.parent / "data" / "raw" / "ids.txt"
CHARS_JSON = (Path(__file__).resolve().parents[3]
              / "Brushblade/Assets/StreamingAssets/config/chars.json")

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


def test_extract_pulls_74_implementable_chars():
    """详表里标 ✅ 的字应全部被抽出,且相生字取基础值。详表入 git,可直接读。

    2026-08-09:129 → 132,火系 DOT 三分化(炑/燥/灱)落地。
    2026-08-10:132 → 128,炼/杨/戟/塌 因配方缺口移出字表(见
    test_no_playable_char_is_uncraftable)。
    2026-08-12:128 → 132,BUFF 组 剡/战/戮/利 落地(E-b3-a)。
    2026-08-12:132 → 133,锋 随 E-b2 暴击轴落地。
    2026-08-12(E-b4/T1):全表血量量纲数值 ×10,字数不变、基础值一律十倍。
    2026-08-12(E-b4/T5):133 → 134,锐 随穿透轴落地(PierceBuff 20)。
    2026-08-14:134 → 128,用户裁定移出 埋/坑/溺/桑/桃/槐 六字(配方完好,主动精简)。
    2026-08-14 第二批:128 → 109,再移出 19 字(烟/燎/熔/燃/烫/锯/巍/城/塞/磐/岿/
    剖/割/戮/刮/削/锤/锁/镜)。Bleed / Silence / Reflect 三个 EffectKind 自此无载体。
    2026-08-14 第三批:109 → 105,移出 沸/淹/润/滋/治 五字,新增 铸(绿·金,接手 Reflect)。
    2026-08-25 字表重构:105 → 74,移出 33 字、新增 杖/枪 两字
    (spec docs/superpowers/specs/2026-08-25-字表重构-design.md)。
    """
    values = extract(SPEC.read_text(encoding="utf-8"))
    assert len(values) == 74
    # 焚曾含木生火,配置表填基础值(引擎结算时 ×3);2026-08-25 升橙档:30(×3=90) → 40(×3=120)。
    # 2026-09-02:相生 ×3 取消,基础值改填等值改写后的实战值,40 → 120,战斗结果不变。
    fen = next(e for e in values["焚"]["effects"] if e["kind"] == "DamageAll")
    assert fen["value"] == 120
    assert values["焚"]["rarity"] == "Orange"
    assert values["燚"]["rarity"] == "Red"
    assert values["燚"]["element"] == "Fire"


def test_extract_heal_over_time_parses_turns_and_target_all():
    """turns/targetAll 要从「效果配置」列的括注里解出来。

    2026-08-14 第三批:润(唯一的群体持续治疗)移出字表,targetAll 那半改用 淡 的
    `DispelEach 1` 守 —— 它走的是同一条括注解析路径,是现存唯一带 targetAll 的效果。
    """
    values = extract(SPEC.read_text(encoding="utf-8"))
    dan = next(e for e in values["淡"]["effects"] if e["kind"] == "Dispel")
    assert dan["targetAll"] is True

    mu = next(e for e in values["沐"]["effects"] if e["kind"] == "HealOverTime")
    assert mu["turns"] == 3
    assert "targetAll" not in mu


def test_extract_pierce_points_attach_to_damage_effect():
    """穿透字:`Pierce N` 要落到 DamageSingle 效果的 pierce 字段上,
    2026-08-25 字表重构后只剩 刺 一个载体(锥 转攻击型召唤、錰 移出字表)。
    不是生成独立的效果条目 —— EffectKind 里没有 Pierce 这个值,落成独立条目会让
    ConfigLoader 在加载期直接抛 ConfigException(2026-08-12,E-b4 T3 替代旧的 ignoreArmor 布尔标记)。"""
    values = extract(SPEC.read_text(encoding="utf-8"))
    expected = {"刺": 15}
    for char, points in expected.items():
        effects = values[char]["effects"]
        # 2026-08-15 金系批量挂战意:三字都多了一条 Morale,故断「第一条是伤害且只有一条伤害」
        assert effects[0]["kind"] == "DamageSingle", f"「{char}」不该产出独立的 Pierce 条目"
        assert all(e["kind"] != "Pierce" for e in effects), f"「{char}」不该产出独立的 Pierce 条目"
        assert effects[0].get("pierce") == points, f"「{char}」的穿透点数应为 {points}"
        assert "ignoreArmor" not in effects[0], f"「{char}」不该再带 ignoreArmor"


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


def test_summon_char_is_the_casting_char_itself():
    """2026-08-15:summonChar 原先填的是那一节的五行(全表「木」/「火」),
    场上一排召唤物长得一模一样,玩家分不出哪只是梅哪只是荆。改填施法字本身。

    断言打在 _parse_row 上而不是 _parse_effects:后者拿到的第二个参数就是要填进
    summonChar 的东西,喂什么它回什么,怎么写都是绿的 —— 真正会填错的是上一层。"""
    from extract_values import _parse_row
    char, spec = _parse_row("| 梅 | 🟢绿 | `Summon 1`(60 血/攻 20) | 1 只 | ✅ |", "木")
    assert char == "梅"
    assert spec["effects"][0]["summonChar"] == "梅"


def test_summon_char_uses_pua_proxy_for_supplementary_plane_char():
    """𣛧(四木,U+23ADF)在增补平面,UGUI Text 显示不出代理对 —— 落地 id 换 PUA 代理,
    summonChar 必须跟着换,否则场上那 4 只召唤物是空框。"""
    from export_chars import build_chars, PUA_PROXY
    spec = {"𣛧": {"element": "Wood", "rarity": "Red",
                   "effects": [{"kind": "Summon", "value": 300, "count": 4,
                                "attack": 120, "summonChar": "𣛧"}]}}
    entry = next(e for e in build_chars("", spec)["chars"] if e.get("effects"))
    assert entry["effects"][0]["summonChar"] == PUA_PROXY["𣛧"]
    assert entry["id"] == PUA_PROXY["𣛧"]


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
    """荆 的 IDS 是 ⿰茾刂,茾 曾因生僻被绕开成 艹+刂;2026-09-01 二级拆解引回 茾
    (茾 = 艹+开,见 COMPONENT_RECIPES),不再是纯 IDS 一级拆解的直接产物。"""
    from export_chars import MANUAL_RECIPES
    assert MANUAL_RECIPES["荆"] == ["茾", "刂"]


def test_burn_no_decay_is_extracted_after_the_valued_effect():
    """炑:不灭是无数值标记,且必须排在 BurnSingle 之后(结算顺序 = 数组顺序)。"""
    from extract_values import _parse_effects
    assert _parse_effects("`BurnSingle 2` + `BurnNoDecay`", "火") == [
        {"kind": "BurnSingle", "value": 2},
        {"kind": "BurnNoDecay", "value": 0}]


def test_burn_settle_now_keeps_potency_before_it():
    """燥:立即结算必须排在 BurnPotency 之后,否则兑现的那一下吃不到 +1 系数。"""
    from extract_values import _parse_effects
    assert _parse_effects("`BurnSingle 2` + `BurnPotency 1` + `BurnSettleNow`", "火") == [
        {"kind": "BurnSingle", "value": 2},
        {"kind": "BurnPotency", "value": 1},
        {"kind": "BurnSettleNow", "value": 0}]


def test_detonate_is_extracted_after_its_own_burn():
    """灱:自带 4 层先加,再引爆。"""
    from extract_values import _parse_effects
    assert _parse_effects("`BurnSingle 4` + `Detonate`", "火") == [
        {"kind": "BurnSingle", "value": 4},
        {"kind": "Detonate", "value": 0}]


def test_detonate_all_carries_target_all():
    # 炸 = 全体 50 + 引爆全部剩余灼烧(2026-08-26)。`DetonateAll` 与 `Detonate`
    # 共用 Kind,只差 targetAll —— 两个 token 必须互不吞
    from extract_values import _parse_effects
    assert _parse_effects("`DamageAll 50` + `DetonateAll`", "火") == [
        {"kind": "DamageAll", "value": 50},
        {"kind": "Detonate", "value": 0, "targetAll": True}]


def test_new_valueless_tokens_do_not_leak_into_unrelated_rows():
    """负向:没写这些 token 的行不该凭空多出效果。

    只写正向断言的话,把 VALUELESS_EFFECTS 的匹配条件删成恒真都没有测试会红
    ——子项目 D 的教训(白名单方向性覆盖)。
    """
    from extract_values import _parse_effects
    assert _parse_effects("`DamageSingle 16`", "火") == [
        {"kind": "DamageSingle", "value": 16}]
    assert _parse_effects("`BurnSingle 2` + `DamageSingle 3`", "火") == [
        {"kind": "BurnSingle", "value": 2},
        {"kind": "DamageSingle", "value": 3}]


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
    """塞 的 IDS 部件 𡨄 是增补平面(会让整字降级成叶子),换成常见部首 宀。
    湮 的 垔 曾因生僻被绕开成 氵+土;2026-09-01 二级拆解引回 垔
    (垔 = 覀+土,见 COMPONENT_RECIPES)。"""
    from export_chars import MANUAL_RECIPES
    assert MANUAL_RECIPES["塞"] == ["宀", "土"]
    assert MANUAL_RECIPES["湮"] == ["氵", "垔"]


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
    assert _parse_effects("`Summon 1`(8 血/攻 3)+ `Dodge 50`", "柳") == [
        {"kind": "Summon", "value": 8, "count": 1, "attack": 3,
         "summonChar": "柳", "passive": {"dodge": 50}}]


def test_gou_row_is_not_marked_implemented():
    """钩(模型缺口:敌人无排位概念)不该出现在生成物里,但真正挡住它的不是这条测试的
    姊妹守卫(CharTableTests.RealConfig_GouIsNotInTheTable)——那条守的是生成物,读的是
    「效果配置」列有没有可解析 token;只把 ⚠ 改成 ✅ 而不填 token,extract() 一样抽不到
    钩,那条测试依旧全绿,挡不住手滑改标记。这条测试直接读详表的标记列本身,才是真正防
    「有人把 ⚠ 改成 ✅」的那一层。"""
    text = SPEC.read_text(encoding="utf-8")
    row = next(l for l in text.split("\n") if l.startswith("| 钩 |"))
    impl_col = row.split("|")[-2].strip()
    assert not impl_col.startswith("✅"), f"钩 是模型缺口,不该标 ✅:{row}"


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


def test_shipped_chars_json_is_regenerable_from_spec():
    """出货的 chars.json 必须等于「拿当前详表重跑一遍管线」的结果。

    本文件其余测试喂的都是手打字符串,证明的是「**如果**详表这么写,解析器能解对」;
    没有一条碰过游戏真正加载的 chars.json。于是两种事故全程无声:
    改了详表忘跑 export_chars.py(游戏里没生效),或手改 chars.json 图省事
    (下次谁跑一次管线就被静默冲掉)。这条是唯一的网。

    ids.txt 为此破例入 git(.gitignore 有说明)——同目录的 xinhua_*.json 仍不入。
    """
    rebuilt = build_chars(IDS.read_text(encoding="utf-8"),
                          extract(SPEC.read_text(encoding="utf-8")))
    shipped = json.loads(CHARS_JSON.read_text(encoding="utf-8"))
    assert rebuilt == shipped, "chars.json 与详表不同步 —— 跑 python3 tools/pipeline/export_chars.py"


def test_no_playable_char_is_uncraftable():
    """详表里标 ✅ 的字必须生成出配方,否则玩家永远拿不到它。

    没有 recipe 的字在引擎里 IsLeaf == true,而 RunEngine.RollRewardOptions 明确
    `if (... || def.IsLeaf) continue;` —— 叶子永不进奖励池;宝箱/商城的候选池同样
    是奖励池的快照。于是这种字既不能合成、也发不出来,躺在字表里纯占位。
    唯一的信号是 build_chars 里一行 print 警告,数据构建的 stdout 没人看 —— 等于无声。

    2026-08-10 首次断言时有 4 个违例(炼/杨/戟/塌,IDS 各有一侧部件落在增补平面,
    `_blocked_smp_part` 跳过整条配方),用户裁定先移出字表 —— 详表里已标 ⚠,
    `extract()` 抽不到,故这里期望空集。日后补 `MANUAL_RECIPES` 换部件即可复活。
    """
    rebuilt = build_chars(IDS.read_text(encoding="utf-8"),
                          extract(SPEC.read_text(encoding="utf-8")))
    # rarity 只有详表里的字才有(叶子部件条目只有 id/element),与 effects 完全同集
    uncraftable = {c["id"] for c in rebuilt["chars"]
                   if c.get("rarity") and not c.get("recipe")}
    assert uncraftable == set(), (
        f"这些字配方生成失败,玩家永远拿不到: {sorted(uncraftable)} —— "
        "多半是 IDS 部件落在增补平面,补 export_chars.MANUAL_RECIPES 换部件,"
        "或在详表里标 ⚠ 移出字表")


def test_backline_token_attaches_to_single_damage():
    from extract_values import _parse_effects
    assert _parse_effects("`DamageSingle 135`,`Pierce 15` + `Backline` + `Morale 1`", "金") == [
        {"kind": "DamageSingle", "value": 135, "pierce": 15, "backline": True},
        {"kind": "Morale", "value": 1}]


def test_backline_does_not_become_a_standalone_effect():
    """Backline 是伤害的修饰,不是效果。若它落成一条独立效果,
    EffectKind 里没有这个值,ConfigLoader 会在加载期直接抛 ConfigException。"""
    from extract_values import _parse_effects
    effects = _parse_effects("`DamageSingle 50` + `Backline`", "金")
    assert all(e["kind"] != "Backline" for e in effects)
    assert len(effects) == 1


def test_ranged_token_lands_in_summon_passive():
    from extract_values import _parse_effects
    assert _parse_effects("`Summon 1`(110 血/攻 20)+ `OnHitBurn 2` + `Ranged`", "灶") == [
        {"kind": "Summon", "value": 110, "count": 1, "attack": 20, "summonChar": "灶",
         "passive": {"onHitBurn": 2, "ranged": True}}]


def test_shipped_chars_json_carries_the_new_row_fields():
    """三张改造字在**出货的 chars.json 里**确实带上了新字段。

    上面几条喂的是手打字符串;这条读真实产物 —— token 表漏接线是无声的,
    只有真产物能证明「详表写了」与「游戏读得到」之间没有断点。"""
    shipped = json.loads(CHARS_JSON.read_text(encoding="utf-8"))
    by_id = {c["id"]: c for c in shipped["chars"]}

    # 伤害侧的目标形状(2026-08-25 装配到 刺/碾/砸):Backline 已换成 Skewer
    ci = by_id["刺"]["effects"][0]
    assert ci["kind"] == "DamageSingle" and ci["shape"] == "Skewer" and ci["shapePercent"] == 60
    assert ci.get("backline") is None, "偷袭换成贯穿后不该还留着"
    assert by_id["碾"]["effects"][0]["shape"] == "Sweep"
    assert by_id["砸"]["effects"][0]["shape"] == "Cleave"

    # 远程:2026-08-25 起唯一载体是 楸(荆 改前排肉盾让出;灶/烓 更早移出字表)
    qiu = by_id["楸"]["effects"][0]
    assert qiu["kind"] == "Summon"
    assert qiu["passive"].get("ranged") is True, "楸 应为远程"

    # 召唤被动的形状与出手控场(2026-08-25):都是「token 表漏接线就静默丢」的字段
    assert by_id["剑"]["effects"][0]["passive"] == {"shape": "Sweep", "shapePercent": 50}
    assert by_id["枪"]["effects"][0]["passive"] == {"shape": "Skewer", "shapePercent": 70}
    assert by_id["锥"]["effects"][0]["passive"] == {"shape": "Volley", "shots": 2}
    assert by_id["藤"]["effects"][0]["passive"] == {"onHitFreezeChance": 10, "onHitFreezeTurns": 1}
    assert by_id["蕉"]["effects"][0]["passive"] == {"onHitSlowPercent": 50, "onHitSlowTurns": 2}

    # 条件加成(2026-08-25 由 doubleVsBurning 泛化):四系各一个收割位
    assert {c["id"]: e["doubleVs"] for c in shipped["chars"]
            for e in c.get("effects", []) if e.get("doubleVs")} == {
        "灼": "Burning", "铡": "Bleeding", "冰": "Controlled", "垚": "ArmorBroken"}


def test_component_entries_are_flagged():
    """部件条目带 component: true;可出牌字不带这个字段。

    Core 侧的 CharDef.IsComponent 靠它区分「部件池成员」和「可出牌字」——
    不能从 recipe/effects 推导,二级拆解之后部件也会有配方。
    """
    chars = build_chars(MINI_IDS, {"烫": {"element": "Fire", "rarity": "Purple", "effects": []}})
    byid = {c["id"]: c for c in chars["chars"]}
    assert byid["汤"]["component"] is True, "配方原料合成出来的部件要标 component"
    assert byid["火"]["component"] is True, "开头硬编码的五行五条也是部件"
    assert "component" not in byid["烫"], "可出牌字不带 component 字段"


def test_real_table_flags_every_component():
    """实船字表:74 个可出牌字都不带 component,其余全部带。"""
    chars = json.loads(CHARS_JSON.read_text(encoding="utf-8"))["chars"]
    playable = {c["id"] for c in chars if "effects" in c}
    assert len(playable) == 74
    for c in chars:
        if c["id"] in playable:
            assert "component" not in c, f"{c['id']} 是可出牌字,不该带 component"
        else:
            assert c.get("component") is True, f"{c['id']} 是部件,该带 component"


def test_component_recipes_yield_one_element_part_each():
    """选中的 12 条部件配方,每条都恰好产出 1 个五行部件。

    这是范围的判据(spec §六):拆的价值 = 换五行部件,中性部件是残渣。
    文档「二级组成」的筛选条件「比一级多给出五行信息」与这个价值模型是同一件事。
    """
    from export_chars import COMPONENT_RECIPES, COMPOUND_ATTR
    from filter_chars import attr_of
    assert len(COMPONENT_RECIPES) == 12
    for part, recipe in COMPONENT_RECIPES.items():
        hits = [p for p in recipe if attr_of(p) or p in COMPOUND_ATTR]
        assert len(hits) == 1, f"{part} = {' + '.join(recipe)} 产出 {hits},应当恰好 1 个五行部件"


def test_real_table_has_component_recipes():
    """实船字表:12 个部件带上了配方,10 个新部件条目在场。"""
    chars = json.loads(CHARS_JSON.read_text(encoding="utf-8"))["chars"]
    byid = {c["id"]: c for c in chars}
    expected = {
        "秋": ["禾", "火"], "崔": ["山", "隹"], "岂": ["山", "己"], "荅": ["艹", "合"],
        "列": ["歹", "刂"], "喿": ["品", "木"], "烝": ["丞", "灬"], "则": ["贝", "刂"],
        "朵": ["几", "木"], "切": ["七", "刀"], "茾": ["艹", "开"], "垔": ["覀", "土"],
    }
    for part, recipe in expected.items():
        assert byid[part]["recipe"] == recipe, f"{part} 的配方不对"
        assert byid[part]["component"] is True, f"{part} 有了配方,但仍然必须是部件"
    for part in "己合歹品丞贝几七开覀":
        assert part in byid, f"新部件 {part} 不在字表里"
        assert "recipe" not in byid[part], f"{part} 是终点,不该有配方"


def test_jing_and_yan_recipes_route_through_the_middle_layer():
    """荆 / 湮 的一级配方引回中间层(2026-09-01 用户复核后拍板)。"""
    chars = json.loads(CHARS_JSON.read_text(encoding="utf-8"))["chars"]
    byid = {c["id"]: c for c in chars}
    assert byid["荆"]["recipe"] == ["茾", "刂"]
    assert byid["湮"]["recipe"] == ["氵", "垔"]


def test_real_table_entry_count():
    # 74 字不变;部件从 57 → 69:12 条 COMPONENT_RECIPES 里 10 个原料是全新终点部件
    # (己合歹品丞贝几七开覀),另外 2 个(茾、垔)本身也是全新条目 —— 荆/湮 之前的
    # 一级配方(艹+刂、氷+土)绕开了它们,回收前 chars.json 里并不存在这两个部件。
    # 新增条目 = 12,不是「12 条配方」暗示的 10:任务纪要曾按「12 个部件带配方,
    # 新增 10 个终点部件」估算总数为 141,漏算了 茾/垔 自身也是新条目,实测 143。
    chars = json.loads(CHARS_JSON.read_text(encoding="utf-8"))["chars"]
    playable = [c for c in chars if "effects" in c]
    assert len(playable) == 74
    assert len(chars) == 143, "74 字 + 69 部件"
