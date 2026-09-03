"""从《技能机制详表》抽取可直接落地(标 ✅)的字与数值。

详表是唯一数值真相;这里只做抽取,不做换算 —— 表里「效果配置」列已经是基础值。
"""
import re

ELEMENT = {"火": "Fire", "木": "Wood", "水": "Water", "金": "Metal", "土": "Earth"}
RARITY = {"🟡金": "Gold", "🔴红": "Red", "🟠橙": "Orange", "🟣紫": "Purple",
          "🔵蓝": "Blue", "🟢绿": "Green", "⚪白": "White"}

# 纯二选一双方向的系(2026-09-02):这些系的表多一格「攻击效果配置」,见 _parse_row。
# 水系(Task 10)、土系(Task 11)均已落地。
DUAL_DIRECTION_ELEMENTS = {"水", "土"}

# 召唤被动 token → chars.json 里 passive 对象的字段名(详表 §召唤·单体·带被动)。
# 「光环」与「攻击附灼烧」是同一个字段:烓/灶 攻 0 靠 OnHitBurn 输出,楸 攻 6 附带 1 层。
SUMMON_PASSIVE = {
    "SummonSpeed": "speed",
    "Thorns": "thorns",
    "HealAlly": "healAlly",
    "OnHitBurn": "onHitBurn",
    "OnHitCurse": "onHitCurse",
    "Dodge": "dodge",
    "OnSummonFreeze": "onSummonFreeze",
    "OnHitFreeze": "onHitFreezeChance",       # 概率(百分点),吃卡等级
    "OnHitFreezeTurns": "onHitFreezeTurns",
    "OnHitSlow": "onHitSlowPercent",          # 幅度(速度点数),吃卡等级
    "OnHitSlowTurns": "onHitSlowTurns",
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
    # 全体引爆(2026-08-26,炸)。必须排在 `Detonate` 之后**且**用整串带反引号匹配 ——
    # `f"\`{token}\`" in config` 拿 "`Detonate`" 去配 "`DetonateAll`" 配不上,两者互不吞。
    "DetonateAll": {"kind": "Detonate", "value": 0, "targetAll": True},
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

# 目标形状的两个带数值 token,同样是伤害的修饰(2026-08-22,spec §9.1)。
# 不挂白名单会被通用正则 `(\w+) (\d+)` 当成独立效果 kind=Shots/ShapePercent 落进 chars.json,
# 而 EffectKind 里没有这两个值 —— ConfigLoader 会在加载期直接抛 ConfigException
# (与 PIERCE_TOKEN / HIT_COUNT_TOKEN 同一个坑)。
# 弹射的跳数(2026-08-25):写成 `Chain N`,是**形状 + 跳数**的合写,不是独立效果。
# ⚠ 与 PIERCE_TOKEN / SHOTS_TOKEN 同一个坑:不挂白名单会被通用正则 `(\w+) (\d+)`
# 当成一条 kind="Chain" 的独立效果落进 chars.json,而 EffectKind 里没有这个值 ——
# ConfigLoader 会在加载期直接抛 ConfigException。(2026-08-25 实测踩过一次。)
CHAIN_TOKEN = "Chain"

SHOTS_TOKEN = "Shots"
SHAPE_PERCENT_TOKEN = "ShapePercent"


def extract(markdown):
    """详表全文 → {字: {element, rarity, effects, pinyin?, gloss?}},只收标 ✅ 的字。"""
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
    _merge_readings(result, extract_readings(markdown))
    return result


def extract_readings(markdown):
    """第九节「拼音与释义」→ {字: (拼音, 释义)}。没有这一节就返回空表。

    单独成节而不是给五张逐字表各加两列:那五张表的列数本来就不齐(水/土 多一格
    「攻击效果配置」),而 _parse_row 靠「哪一格带反引号 / 哪一格是稀有度」认列 ——
    往里塞自由文本列是给那套启发式添反例。这一节是纯查找表,与数值无关。"""
    if "## 九 · 拼音与释义" not in markdown:
        return {}
    section = markdown.split("## 九 · 拼音与释义")[1].split("\n## ")[0]
    readings = {}
    for line in section.split("\n"):
        if not line.startswith("| ") or line.startswith("|---"):
            continue
        cells = [c.strip() for c in line.split("|")[1:-1]]
        if len(cells) != 3 or len(cells[0]) != 1:
            continue  # 表头「| 字 | 拼音 | 释义 |」在这里被滤掉
        readings[cells[0]] = (cells[1], cells[2])
    return readings


def _merge_readings(values, readings):
    """把拼音/释义并进已抽出的字条目。

    ⚠ 只并**已在 values 里**的字:第九节列的是全字表,而 values 只含标 ✅ 的字 ——
    拿第九节反过来建条目会把移出字表的字重新塞回 chars.json。
    空串不写键:export_chars 只在真值时落地,这里也别留空键(恒等性)。"""
    for char, spec in values.items():
        pinyin, gloss = readings.get(char, ("", ""))
        if pinyin:
            spec["pinyin"] = pinyin
        if gloss:
            spec["gloss"] = gloss


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
    effects = _parse_effects(config, char)
    if not effects:
        return None
    entry = {"element": ELEMENT[element], "rarity": rarity, "effects": effects}

    # 双方向字(2026-09-02,Task 10):水/土两系的表在「效果配置」右边多挂了一格
    # 「攻击效果配置」——同一行的第二个反引号格,语法与治疗面完全一样,复用
    # _parse_effects。⚠ 只对 DUAL_DIRECTION_ELEMENTS 生效:其余系的「实现」备注列
    # 里常年散落着引用 token 名的反引号(如「装配 `DoubleVsControlled`」),不加这道
    # 元素闸,通用的「第二个反引号格」判据会把那些说明文字误当成攻击效果去解析。
    if element in DUAL_DIRECTION_ELEMENTS:
        backticked = [c for c in cells if "`" in c]
        if len(backticked) > 1:
            attack_effects = _parse_effects(backticked[1], char)
            if attack_effects:
                entry["attackEffects"] = attack_effects
    return char, entry


def _parse_effects(config, char):
    """「`DamageAll 30` + `BurnAll 4`」→ [{kind, value}, …];召唤单独处理。

    char 只被召唤分支用到(当 summonChar),其余 kind 一概不看第二个参数。"""
    summon = re.search(r"`Summon (\d+)`\((\d+) 血/攻 (\d+)\)", config)
    if summon:
        # summonChar = 施法字本身(2026-08-15):场上显示的就是这个字。原先填那一节的
        # 五行,全表召唤物都叫「木」/「火」,一排下来分不出哪只是梅哪只是荆。
        # 增补平面字(𣛧)的 PUA 代理换在 export_chars._output_id 那一层,与 id 同口径。
        effect = {"kind": "Summon", "value": int(summon.group(2)),
                  "count": int(summon.group(1)),
                  "attack": int(summon.group(3)), "summonChar": char}
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
        if "`Ranged`" in config:            # 无数值的布尔标记(2026-08-20,灶/烓)
            passive["ranged"] = True
        if "`Taunt`" in config:             # 无数值的布尔标记(2026-08-25,荆/堡)
            passive["taunt"] = True
        # 目标形状(2026-08-22,spec §9.1):召唤物自动攻击也能带形状,与伤害侧同一套 token,
        # 落进 passive 的 shape/shots/shapePercent —— **不**新增独立 effect(与 Ranged 同处理,
        # 头上 SHOTS_TOKEN/SHAPE_PERCENT_TOKEN 注释说的坑在这条分支不适用:本分支在通用
        # `(\w+) (\d+)` 那个 for 循环之前就 return 了,数值 token 不会被那个循环二次吞掉)。
        for token in ("Sweep", "Cleave", "Skewer"):
            if f"`{token}`" in config:
                passive["shape"] = token
                break
        chain = re.search(rf"`{CHAIN_TOKEN} (\d+)`", config)
        if chain:
            passive["shape"] = "Chain"
            passive["shots"] = int(chain.group(1))
        percent = re.search(rf"`{SHAPE_PERCENT_TOKEN} (\d+)`", config)
        if percent:
            passive["shapePercent"] = int(percent.group(1))
        shots = re.search(rf"`{SHOTS_TOKEN} (\d+)`", config)
        if shots:
            passive.setdefault("shape", "Volley")   # 同上:Chain 也用 Shots
            passive["shots"] = int(shots.group(1))
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
        if kind in (SHOTS_TOKEN, SHAPE_PERCENT_TOKEN, CHAIN_TOKEN):
            continue  # 目标形状的修饰,下面统一挂到伤害上
        effect = {"kind": kind, "value": int(value)}
        if kind == "DispelEach":       # 全体各驱散 N 条(淡)
            effect["kind"] = "Dispel"
            effect["targetAll"] = True
        # 条件加成(2026-08-25 由 DoubleVsBurning 泛化成四选一)。写成条件名而不是布尔位,
        # 新增条件时只动这张表,不用再加一个平行的 bool —— 与 EffectKind 同口径。
        if kind.startswith("Damage"):
            for token in ("Burning", "Bleeding", "Controlled", "ArmorBroken"):
                if f"`DoubleVs{token}`" in config:
                    effect["doubleVs"] = token
                    break
        # 偷袭(2026-08-20):无视敌方前排。只修饰单体直伤 —— 其余单体效果本就不受排位限制。
        # ⚠ 绝不能进 VALUELESS_EFFECTS:那会让它落成一条 kind="Backline" 的独立效果,
        #   而 EffectKind 里没有这个值,ConfigLoader 会在加载期直接抛 ConfigException
        #   (与 PIERCE_TOKEN 头上那条注释同一个坑)。
        if kind == "DamageSingle" and "`Backline`" in config:
            effect["backline"] = True
        # 目标形状(2026-08-22,spec §9.1):只修饰单体直伤,与 Backline / Pierce / HitCount 同为**修饰位**。
        # ⚠ 绝不能进 VALUELESS_EFFECTS:那会让它落成一条 kind="Sweep" 的独立效果,
        #   而 EffectKind 里没有这个值,ConfigLoader 会在加载期直接抛 ConfigException
        #   (与 PIERCE_TOKEN / Backline 头上那两条注释同一个坑)。
        if kind == "DamageSingle":
            for token in ("Sweep", "Cleave", "Skewer"):
                if f"`{token}`" in config:
                    effect["shape"] = token
                    break
            chain = re.search(rf"`{CHAIN_TOKEN} (\d+)`", config)
            if chain:                       # `Chain N` = 形状 Chain + 跳数 N
                effect["shape"] = "Chain"
                effect["shots"] = int(chain.group(1))
            percent = re.search(rf"`{SHAPE_PERCENT_TOKEN} (\d+)`", config)
            if percent:
                effect["shapePercent"] = int(percent.group(1))
            shots = re.search(rf"`{SHOTS_TOKEN} (\d+)`", config)
            if shots:
                # 2026-08-25:Chain 也用 Shots 表示跳数,所以 Shots 不再无条件蕴含 Volley ——
                # 只有没显式写形状时才当连发(保住 `Shots N` 单写即连发的旧口径)
                effect.setdefault("shape", "Volley")
                effect["shots"] = int(shots.group(1))
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
