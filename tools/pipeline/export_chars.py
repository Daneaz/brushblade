"""字表导出:配方(IDS 一级拆解 + 叠字人工兜底)+ 数值 → chars.json。

配方口径(见 docs/design/字选型/技能机制详表.md 1.5):
- 五系叠字链 15 条**人工写死**,不走 IDS —— IDS 会把 燚 拆成 炏+炏,污染叠字链。
- 其余字用 decompose.split_once 取一级拆解。
- 配方一律「部件在前、低阶字在后」。
"""
import json
from pathlib import Path

from decompose import build_index, split_once
from extract_values import extract
from fetch_ids import parse_ids_text
from filter_chars import attr_of

ELEMENTS = ["金", "木", "水", "火", "土"]
_ELEMENT_NAME = {"金": "Metal", "木": "Wood", "水": "Water", "火": "Fire", "土": "Earth"}

STACK_RECIPES = {
    "林": ["木", "木"], "森": ["木", "林"], "𣛧": ["木", "森"],
    "沝": ["水", "水"], "淼": ["水", "沝"], "㵘": ["水", "淼"],
    "炎": ["火", "火"], "焱": ["火", "炎"], "燚": ["火", "焱"],
    "鍂": ["金", "金"], "鑫": ["金", "鍂"], "𨰻": ["金", "鑫"],
    "圭": ["土", "土"], "垚": ["土", "圭"], "㙓": ["土", "垚"],
}

# Unity UGUI Text 不支持增补平面(SMP,码位 > 0xFFFF)代理对显示(第10章 10.3.0)。
# 𣛧(木系四叠字)、𨰻(金系四叠字)因此一律用 PUA 代理码位落地(subset_fonts.py 的
# STACKED 会为这两个码位拼合 2×2 部件字形)。
PUA_PROXY = {"𣛧": "\ue625", "𨰻": "\ue626"}


# 复合部件的五行属性(filter_chars.ATTR_MAP 只覆盖部首,非部首的复合部件落不到属性)。
# 口径见 docs/design/wuxing-reference.md「复合部件的属性判定」:递归到部首取属性,
# 多个属性部首时取能成相生的那个。相生已收紧为「他生我」,故这里只需覆盖真正撑起
# 某条相生链的部件 —— 切(= 七+刀,刀属金)让 沏(水系)吃到金生水。
#
# 2026-08-12(E-b4 T5):兑 是第二个,理由与 切 不同 —— 它**不撑相生**(锐 也是金,
# 金不生金),纯粹是设计拍板的展示属性(八卦之兑属金,spec §12.2 写死 element: Metal)。
# 部件的五行会在字卡上屏,留空会让 锐 的两个原料一金一无属性,读起来像漏配。
COMPOUND_ATTR = {"切": "金", "兑": "金"}


def _output_id(char):
    """真实字 → 落地用 id:SMP 且有 PUA 代理的字换成代理码位,其余原样。"""
    return PUA_PROXY.get(char, char)


def _output_effect(effect):
    """效果里的落地 id 换算:summonChar 与 id/recipe 同口径走 _output_id
    (𣛧 召的 4 只显示的是它自己,不换代理码位就是 4 个空框)。"""
    if "summonChar" not in effect:
        return effect
    return {**effect, "summonChar": _output_id(effect["summonChar"])}


def _blocked_smp_part(recipe):
    """配方原料里第一个「SMP 且无 PUA 代理」的部件;没有则 None。
    UGUI Text 显示不出代理对,这类部件没法作为配方原料展示,只能让含它的字退化为叶子。"""
    for part in recipe:
        if len(part) == 1 and ord(part) > 0xFFFF and part not in PUA_PROXY:
            return part
    return None


# 人工配方兜底(非叠字):IDS 拆出来的部件太生僻,字体子集与玩家辨识都是负担。
# 荆 的 IDS 是 ⿰茾刂,茾(U+833E)基本没人认得;评分版口径就是 艹+刂,照它来。
MANUAL_RECIPES = {
    "荆": ["艹", "刂"],
    # 塞 的 IDS 是 ⿱𡨄土,𡨄(U+21A04)在增补平面 —— _blocked_smp_part 会因此把整个字
    # 降级成不可拆的叶子(只能靠掉落获得)。换成常见部首 宀 直接绕开。
    "塞": ["宀", "土"],
    # 湮 的 IDS 是 ⿰氵垔,垔 生僻;它的核心就是土,义通(水土掩埋),且零新部件。
    "湮": ["氵", "土"],
    # 锁 的 IDS 是 ⿰钅𭕆,𭕆(U+2D546)在增补平面 —— _blocked_smp_part 会把整字
    # 降级成不可拆的叶子。换成 贝,义也通(锁住财物)。
    "锁": ["钅", "贝"],
    # 戮 的 IDS 是 ⿰翏戈,翏(U+7FCF)极其生僻,同 荆 的 茾 —— 字体子集与玩家辨识
    # 都是负担。翏 = 羽 + 㐱,取 羽 义形都通(羽饰之戈),且 戈 已在
    # filter_chars.ATTR_MAP 里定为金,与 戮 的金系身份一致。
    "戮": ["羽", "戈"],
    # 锋 的 IDS 是 ⿰钅夆,夆(U+5906)非常生僻,同 荆 的 茾 / 戮 的 翏 —— 字体子集与
    # 玩家辨识都是负担。夆 = 夂 + 丰,取 丰 义形都通(锋刃之丰锐),且 丰 是常用字。
    # 钅+丰 与字表现有配方无撞车(全表配方无重复,已核)。
    "锋": ["钅", "丰"],
}


def recipe_for(char, index):
    """叠字取人工表,人工兜底表次之,其余取 IDS 一级拆解;不可拆返回 []。"""
    if char in STACK_RECIPES:
        return list(STACK_RECIPES[char])
    if char in MANUAL_RECIPES:
        return list(MANUAL_RECIPES[char])
    return split_once(char, index) or []


def build_chars(ids_text, values):
    """values: {字: {element, rarity, effects, pinyin?, gloss?}} → {"chars": [...]}"""
    index = build_index(parse_ids_text(ids_text))
    entries = [{"id": e, "element": _ELEMENT_NAME[e]} for e in ELEMENTS]

    components = set()
    for char, spec in values.items():
        recipe = recipe_for(char, index)
        entry = {"id": _output_id(char), "rarity": spec["rarity"]}
        if spec.get("element"):
            entry["element"] = spec["element"]
        if recipe:
            blocked = _blocked_smp_part(recipe)
            if blocked:
                print(f"警告:{char} 的配方含增补平面部件「{blocked}」(U+{ord(blocked):05X},"
                      f"UGUI Text 不支持代理对显示且无 PUA 代理)—— 跳过配方,{char} 退化为"
                      "叶子(只能靠掉落获得)")
            else:
                entry["recipe"] = [_output_id(part) for part in recipe]
                for part in recipe:
                    if part not in ELEMENTS:
                        components.add(part)
        for optional in ("pinyin", "gloss"):
            if spec.get(optional):
                entry[optional] = spec[optional]
        if spec.get("effects"):
            entry["effects"] = [_output_effect(e) for e in spec["effects"]]
        entries.append(entry)

    for part in sorted(components):
        if part not in values:
            leaf = {"id": _output_id(part)}
            attr = attr_of(part) or COMPOUND_ATTR.get(part)
            if attr:
                leaf["element"] = _ELEMENT_NAME[attr]
            entries.append(leaf)

    return {"chars": entries}


def main():
    here = Path(__file__).parent
    ids_text = (here / "data" / "raw" / "ids.txt").read_text(encoding="utf-8")
    spec = here.parent.parent / "docs/design/字选型/技能机制详表.md"
    values = extract(spec.read_text(encoding="utf-8"))
    out = build_chars(ids_text, values)
    dest = here.parent.parent / "Brushblade/Assets/StreamingAssets/config/chars.json"
    dest.write_text(json.dumps(out, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"已写入 {dest}: {len(out['chars'])} 条(字 {len(values)})")


if __name__ == "__main__":
    main()
