"""从《技能机制详表》抽取可直接落地(标 ✅)的字与数值。

详表是唯一数值真相;这里只做抽取,不做换算 —— 表里「效果配置」列已经是基础值。
"""
import re

ELEMENT = {"火": "Fire", "木": "Wood", "水": "Water", "金": "Metal", "土": "Earth"}
RARITY = {"🟡金": "Gold", "🔴红": "Red", "🟠橙": "Orange", "🟣紫": "Purple",
          "🔵蓝": "Blue", "🟢绿": "Green", "⚪白": "White"}

# 召唤被动 token → chars.json 里 passive 对象的字段名(详表 §召唤·单体·带被动)。
# 「光环」与「攻击附灼烧」是同一个字段:烓/灶 攻 0 靠 OnHitBurn 输出,楸 攻 6 附带 1 层。
SUMMON_PASSIVE = {
    "SummonSpeed": "speed",
    "Thorns": "thorns",
    "HealAlly": "healAlly",
    "OnHitBurn": "onHitBurn",
    "OnHitCurse": "onHitCurse",
    "Dodge": "dodge",
}


# 无数值的效果 token(布尔标记式):通用正则 `(\w+) (\d+)` 抓不到,单独认。
# 顺带绕开负数 —— `Dispel -1` 那个负号通用正则也认不出。
# ⚠ 追加顺序即结算顺序:若同一行出现两个无数值 token,谁先在这个 dict 里出现谁先被追加。
# 本批(BurnNoDecay/BurnSettleNow/Detonate)每行最多命中一个,不受影响。
VALUELESS_EFFECTS = {
    "Cleanse": {"kind": "Cleanse", "value": 0},
    "DispelAll": {"kind": "Dispel", "value": -1},
    "BurnNoDecay": {"kind": "BurnNoDecay", "value": 0},
    "BurnSettleNow": {"kind": "BurnSettleNow", "value": 0},
    "Detonate": {"kind": "Detonate", "value": 0},
}

# 斩杀是**伤害的修饰**,不是独立效果:抽出来挂到同一行的伤害效果上。
# 值 = executeKills(True = 直接击杀,False = 残血加伤 ×2)
EXECUTE_TOKENS = {"ExecuteKill": True, "ExecuteBonus": False}

# 需要 turns 的 Kind(白名单):写死给 HealOverTime 会让新加的持续类状态静默丢掉回合数。
# 注意:下面 turns 正则是对整格「效果配置」搜一次,一格只支持一个 turns 值——若将来
# 一行里出现两个不同回合数的持续效果(如 `Blind 50`(turns 2) + `Silence 0`(turns 1)),
# 这里要改成按效果分段解析,现在 YAGNI。
DURATION_KINDS = {"HealOverTime", "Blind", "Silence", "Reflect"}

# 支持 targetAll 的 Kind
TARGET_ALL_KINDS = {"HealOverTime", "Blind"}

# 分段数是伤害的修饰,不是独立效果(与 ExecuteKill / ExecuteBonus 同处理)
HIT_COUNT_TOKEN = "HitCount"

# 穿透点数同样是伤害的修饰(2026-08-12,E-b4 T3:替代原先的布尔标记 ignoreArmor)。
# 不挂白名单会被通用正则 `(\w+) (\d+)` 当成一条独立效果 kind=Pierce 落进 chars.json,
# 而 EffectKind 里没有这个值 —— ConfigLoader 会在加载期直接抛 ConfigException。
PIERCE_TOKEN = "Pierce"


def extract(markdown):
    """详表全文 → {字: {element, rarity, effects}},只收标 ✅ 的字。"""
    body = markdown.split("## 二 · 火系")[1].split("## 七 · 引擎扩展")[0]
    parts = re.split(r"^## [三四五六] · (\S+?)系", body, flags=re.M)
    sections = [("火", parts[0])] + [(parts[i], parts[i + 1])
                                     for i in range(1, len(parts), 2)]
    result = {}
    for element, section in sections:
        for line in section.split("\n"):
            entry = _parse_row(line, element)
            if entry:
                result[entry[0]] = entry[1]
    return result


def _parse_row(line, element):
    if not line.startswith("| ") or line.startswith("|---"):
        return None
    cells = [c.strip() for c in line.split("|")[1:-1]]
    if len(cells) < 4 or len(cells[0]) != 1:
        return None
    char = cells[0]
    rarity = next((RARITY[c] for c in cells if c in RARITY), None)
    impl = next((c for c in cells if c.startswith("✅") or c.startswith("⚠")), None)
    if not rarity or not impl or not impl.startswith("✅"):
        return None

    config = next((c for c in cells if "`" in c), "")
    effects = _parse_effects(config, element)
    if not effects:
        return None
    return char, {"element": ELEMENT[element], "rarity": rarity, "effects": effects}


def _parse_effects(config, element):
    """「`DamageAll 30` + `BurnAll 4`」→ [{kind, value}, …];召唤单独处理。"""
    summon = re.search(r"`Summon (\d+)`\((\d+) 血/攻 (\d+)\)", config)
    if summon:
        effect = {"kind": "Summon", "value": int(summon.group(2)),
                  "count": int(summon.group(1)),
                  "attack": int(summon.group(3)), "summonChar": element}
        # 桂 的护盾发给全场召唤物,不是这只自带的 —— 平铺在 effect 上,不进 passive
        shield = re.search(r"`SummonShield (\d+)`", config)
        if shield:
            effect["summonShield"] = int(shield.group(1))
        passive = {}
        for token, field in SUMMON_PASSIVE.items():
            found = re.search(rf"`{token} (\d+)`", config)
            if found:
                passive[field] = int(found.group(1))
        if "`OnHitBurnAll`" in config:      # 无数值的布尔标记(烓)
            passive["onHitBurnAll"] = True
        if passive:
            effect["passive"] = passive
        return [effect]

    effects = []
    for kind, value in re.findall(r"`(\w+) (\d+)`", config):
        if kind in EXECUTE_TOKENS:
            continue  # 斩杀是修饰而非效果,下面统一挂到伤害上
        if kind == HIT_COUNT_TOKEN:
            continue  # 分段数是修饰而非效果,下面统一挂到伤害上
        if kind == PIERCE_TOKEN:
            continue  # 穿透点数是修饰而非效果,下面统一挂到伤害上
        effect = {"kind": kind, "value": int(value)}
        if kind == "DispelEach":       # 全体各驱散 N 条(淡)
            effect["kind"] = "Dispel"
            effect["targetAll"] = True
        if kind.startswith("Damage") and "DoubleVsBurning" in config:
            effect["doubleVsBurning"] = True
        if kind == "Shield" and "PersistOnce" in config:
            effect["persistOnce"] = True
        effects.append(effect)

    for token, spec in VALUELESS_EFFECTS.items():
        if f"`{token}`" in config:
            effects.append(dict(spec))

    for token, kills in EXECUTE_TOKENS.items():
        found = re.search(rf"`{token} (\d+)`", config)
        if not found:
            continue
        for effect in effects:
            if effect["kind"].startswith("Damage"):
                effect["executeBelowPercent"] = int(found.group(1))
                effect["executeKills"] = kills

    hit_count = re.search(rf"`{HIT_COUNT_TOKEN} (\d+)`", config)
    if hit_count:
        for effect in effects:
            if effect["kind"].startswith("Damage"):
                effect["hitCount"] = int(hit_count.group(1))

    pierce = re.search(rf"`{PIERCE_TOKEN} (\d+)`", config)
    if pierce:
        for effect in effects:
            if effect["kind"].startswith("Damage"):
                effect["pierce"] = int(pierce.group(1))

    turns = re.search(r"turns (\d+)", config)
    for effect in effects:
        if effect["kind"] in DURATION_KINDS:
            if turns:
                effect["turns"] = int(turns.group(1))
        if effect["kind"] in TARGET_ALL_KINDS and "targetAll" in config:
            effect["targetAll"] = True
    return effects
