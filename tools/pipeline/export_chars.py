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

# 2026-09-05:沝 随字表调整移出,水系链的中间环换成 冰(冫+水)—— 见详表 §1.5 的例外说明。
STACK_RECIPES = {
    "林": ["木", "木"], "森": ["木", "林"], "𣛧": ["木", "森"],
    "淼": ["水", "冰"], "㵘": ["水", "淼"],
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
# 某条相生链的部件。
#
# 2026-08-12(E-b4 T5):兑 是第二个,理由与 切 不同 —— 它**不撑相生**(锐 也是金,
# 金不生金),纯粹是设计拍板的展示属性(八卦之兑属金,spec §12.2 写死 element: Metal)。
# 部件的五行会在字卡上屏,留空会让 锐 的两个原料一金一无属性,读起来像漏配。
#
# 2026-09-05:切 随 沏 移出字表一并退场(它当初只为 沏 的金生水而存在,而相生 ×3 早在
# 2026-09-02 已取消)。现在只剩 兑 —— 它不撑相生,纯粹是设计拍板的展示属性。
COMPOUND_ATTR = {"兑": "金"}


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
MANUAL_RECIPES = {
    # 荆:IDS 是 ⿰茾刂。2026-08 曾改成 艹+刂 绕开生僻的 茾,2026-09-01 二级拆解**推翻**
    # 这个绕开 —— 照 艹+刂 展开会原地打转(两个都是五行部件,拆不动),文档里的二级
    # 艹+开+刂 在游戏里永远不可达。引回 茾(茾 = 艹+开,见 COMPONENT_RECIPES)。
    # 已知代价:拆一次的产出从 2 个五行部件降到 1 个(spec §六,用户复核后仍拍板引回)。
    "荆": ["茾", "刂"],
    # 塞 的 IDS 是 ⿱𡨄土,𡨄(U+21A04)在增补平面 —— _blocked_smp_part 会因此把整个字
    # 降级成不可拆的叶子(只能靠掉落获得)。换成常见部首 宀 直接绕开。
    "塞": ["宀", "土"],
    # 湮:IDS 是 ⿰氵垔。同 荆 —— 2026-08 改成 氵+土 绕开生僻的 垔,2026-09-01 引回,
    # 让文档的二级 氵+覀+土 可达(垔 = 覀+土,见 COMPONENT_RECIPES)。
    "湮": ["氵", "垔"],
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
    # 葬:IDS 是 ⿳艹死廾 三部件,而全表配方一律两部件(一步合成 Mode A)。
    # 取 艹 + 死 —— 草覆其尸,义形都通;死 是常用字,不像 荆 的 茾 那样是字体子集负担。
    "葬": ["艹", "死"],
}


# 部件的配方(2026-09-01「二级拆解」):让部件自己也能再拆一层。
#
# 取值范围 = docs/design/字选型/字表功能解析.md「二级组成」列蕴含的那一批,不做 IDS 全量
# 展开 —— 全展开会引入 𠅃 / 𭕄 / 㳟 / 龴 这类生僻与增补平面部件(40 个),正是下面
# MANUAL_RECIPES 当初刻意绕开的东西。
#
# 判据(spec §六):拆是**单向变现**,价值 = 换到五行部件(五行部件能去合金/橙/红那 25 个
# 纯五行字),中性部件是残渣。选中的这 12 条**每条都恰好产出 1 个五行部件**;没选的 17 个
# 部件拆出来 0/17 产五行部件。守卫测试:test_component_recipes_yield_one_element_part_each。
#
# 注释里的字是这条配方服务的可出牌字。
COMPONENT_RECIPES = {
    "秋": ["禾", "火"],   # 楸
    "岂": ["山", "己"],   # 桤、铠
    "荅": ["艹", "合"],   # 塔
    "列": ["歹", "刂"],   # 烈
    "喿": ["品", "木"],   # 燥、澡
    "烝": ["丞", "灬"],   # 蒸
    "则": ["贝", "刂"],   # 铡
    "朵": ["几", "木"],   # 剁
    # 荆 / 湮 的一级配方引回中间层后新出现的两个(见 MANUAL_RECIPES 里那两条的注释)
    "茾": ["艹", "开"],   # 荆
    "垔": ["覀", "土"],   # 湮
}

# 己 合 歹 品 丞 贝 几 七 开 覀 是**终点**,不再往下拆(2026-09-01 拍板:只做两级)。
# 品 = 口+口+口、合 = 亼+口 再往下就是笔画,没有拆合语义 —— 与「五行部件不拆」
# 「禾 不拆」同一条规则。


def recipe_for(char, index):
    """叠字取人工表,人工兜底表次之,其余取 IDS 一级拆解;不可拆返回 []。"""
    if char in STACK_RECIPES:
        return list(STACK_RECIPES[char])
    if char in MANUAL_RECIPES:
        return list(MANUAL_RECIPES[char])
    return split_once(char, index) or []


def build_chars(ids_text, values):
    """values: {字: {element, rarity, effects, attackEffects?, pinyin?, gloss?}} → {"chars": [...]}"""
    index = build_index(parse_ids_text(ids_text))

    # COMPONENT_RECIPES 的 key 必须是纯部件 —— 若某个 key 同时是可出牌字,它的配方会被
    # recipe_for 那条路无声顶掉。同「管线 token 表静默丢弃」是一类坑,宁可炸也不要静默。
    overlap = COMPONENT_RECIPES.keys() & values.keys()
    assert not overlap, f"COMPONENT_RECIPES 的 key 与可出牌字撞车,配方会被无声忽略:{sorted(overlap)}"

    # component: true 是 Core 侧 CharDef.IsComponent 的唯一来源 —— 部件属部件池,
    # 不进奖励池/宝箱池/商城/收集/叠字前置。与「有没有配方」正交(2026-09-01 二级拆解:
    # 部件也可以有配方),所以必须显式标,不能让 Core 去推导。
    entries = [{"id": e, "element": _ELEMENT_NAME[e], "component": True} for e in ELEMENTS]

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
        if spec.get("attackEffects"):
            entry["attackEffects"] = [_output_effect(e) for e in spec["attackEffects"]]
        entries.append(entry)

    # 部件条目:先做闭包 —— 部件自己也可能有配方(COMPONENT_RECIPES),它的原料同样要落地。
    # 写成 worklist 而不是「再来一轮」,是为了将来加第三级时不会静默漏掉一层。
    # 收敛之后再按字典序输出,保证 chars.json 的部件段顺序稳定(否则 diff 会无谓翻腾)。
    emitted = set()
    pending = sorted(components)
    while pending:
        part = pending.pop()
        if part in values or part in emitted:
            continue
        emitted.add(part)
        pending.extend(p for p in COMPONENT_RECIPES.get(part, []) if p not in ELEMENTS)

    for part in sorted(emitted):
        leaf = {"id": _output_id(part)}
        attr = attr_of(part) or COMPOUND_ATTR.get(part)
        if attr:
            leaf["element"] = _ELEMENT_NAME[attr]
        leaf["component"] = True
        sub = COMPONENT_RECIPES.get(part)
        if sub:
            leaf["recipe"] = [_output_id(p) for p in sub]
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
