"""从《技能机制详表》抽取可直接落地(标 ✅)的字与数值。

详表是唯一数值真相;这里只做抽取,不做换算 —— 表里「效果配置」列已经是基础值。
"""
import re

ELEMENT = {"火": "Fire", "木": "Wood", "水": "Water", "金": "Metal", "土": "Earth"}
RARITY = {"🟡金": "Gold", "🔴红": "Red", "🟠橙": "Orange", "🟣紫": "Purple",
          "🔵蓝": "Blue", "🟢绿": "Green", "⚪白": "White"}


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
        return [{"kind": "Summon", "value": int(summon.group(2)),
                 "count": int(summon.group(1)),
                 "attack": int(summon.group(3)), "summonChar": element}]

    effects = []
    for kind, value in re.findall(r"`(\w+) (\d+)`", config):
        effect = {"kind": kind, "value": int(value)}
        if kind.startswith("Damage") and "DoubleVsBurning" in config:
            effect["doubleVsBurning"] = True
        if kind.startswith("Damage") and "ignoreArmor" in config:
            effect["ignoreArmor"] = True
        if kind == "Shield" and "PersistOnce" in config:
            effect["persistOnce"] = True
        effects.append(effect)

    turns = re.search(r"turns (\d+)", config)
    for effect in effects:
        if effect["kind"] == "HealOverTime":
            if turns:
                effect["turns"] = int(turns.group(1))
            if "targetAll" in config:
                effect["targetAll"] = True
    return effects
