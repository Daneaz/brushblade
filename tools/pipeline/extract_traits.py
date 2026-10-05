"""字卡特性表 → traits(spec v7 §9 / §11.9)。

五张表按系拆在 docs/design/字选型/特性表/{火,金,水,土,木}.md,格式统一:
| 字 | 槽 | 面 | 形态 | 替换 | 名 | 效果配置 | 实现 |
只导出「实现」列以 ✅ 开头的行。效果配置复用详表的 token 语法(_parse_effects),
未知 token / 缺 turns 等由它报错。
"""
import re
from fractions import Fraction
from pathlib import Path

from extract_values import ENEMY_PICK_KINDS, _parse_effects, extract

ELEMENT_FILES = ["火", "金", "水", "土", "木"]
FACE_NAMES = {"攻": "Attack", "燃": "Feature", "铠": "Feature", "润": "Feature",
              "固": "Feature", "生": "Feature", "两面": None}
# 形态 → (form, trigger);「被动·暴击/击杀」= 被动 + 触发类型(spec v7 §2.3)。主动只能是出字时,不接后缀
FORMS = {"主动": (None, None), "被动": ("Passive", None),
         "被动·暴击": ("Passive", "OnCrit"), "被动·击杀": ("Passive", "OnKill")}
SLOTS = {"Lv1", "Lv3", "Lv4", "Lv5", "Lv6", "Lv8"}
_HEADER = ["字", "槽", "面", "形态", "替换", "名", "效果配置", "实现"]

# 系 → 本系五行面名;特性表的面只能是「攻」「两面」或本系面名(Plan B 前置项)
ELEMENT_FACE = {"火": "燃", "金": "铠", "水": "润", "土": "固", "木": "生"}

# 池表(spec v7 D7):只有写 X 的词条按档放大,四舍五入(半入);用 Fraction 避开浮点
TIER_MULTIPLIER = {"White": Fraction(1), "Green": Fraction(6, 5), "Blue": Fraction(29, 20),
                   "Purple": Fraction(7, 4), "Gold": Fraction(21, 10),
                   "Orange": Fraction(5, 2), "Red": Fraction(3)}
GENERAL_POOL_PREFIX = "通"
POOL_PREFIX = "池"
_GENERAL_HEADER = ["名", "效果配置", "X"]
_SYSTEM_HEADER = ["系", "槽", "面", "形态", "名", "效果配置", "X"]
_EMPTY = ("—", "-", "")
_X_TOKEN = re.compile(r"(?<![\w])X(?![\w])")


def scale_x(base, rarity):
    """round_half_up(base × 档位系数)。"""
    return int(base * TIER_MULTIPLIER[rarity] + Fraction(1, 2))


def _pool_cells(line):
    return [c.strip() for c in line.strip().strip("|").split("|")]


def extract_pool(markdown):
    """池.md → {("通", 名): entry, (系, 名): entry};entry = {slot, face, form, trigger?, config, x}。

    两张表按表头区分:通用池(名|效果配置|X,固定 Lv4/两面/被动)与系池(系|槽|面|形态|名|效果配置|X)。"""
    pool = {}
    mode = None
    for raw in markdown.split("\n"):
        line = raw.strip()
        if not line.startswith("|") or line.startswith("|---"):
            continue
        cells = _pool_cells(line)
        if cells == _GENERAL_HEADER:
            mode = "general"
            continue
        if cells == _SYSTEM_HEADER:
            mode = "system"
            continue
        if mode == "general":
            if len(cells) != 3:
                raise ValueError(f"池表:通用池列数应为 3:{line}")
            name, config, x = cells
            entry = {"slot": "Lv4", "face_cn": "两面", "form_cn": "被动"}
            key = (GENERAL_POOL_PREFIX, name)
        elif mode == "system":
            if len(cells) != 7:
                raise ValueError(f"池表:系池列数应为 7:{line}")
            element, slot, face, form, name, config, x = cells
            if element not in ELEMENT_FACE:
                raise ValueError(f"池表:「{name}」系非法:{element}")
            if slot not in SLOTS:
                raise ValueError(f"池表:「{name}」槽位非法:{slot}")
            if face not in ("攻", ELEMENT_FACE[element]):
                raise ValueError(f"池表:「{name}」面「{face}」不属于{element}系")
            if form not in FORMS:
                raise ValueError(f"池表:「{name}」形态非法:{form}")
            entry = {"slot": slot, "face_cn": face, "form_cn": form}
            key = (element, name)
        else:
            raise ValueError(f"池表:表头之前出现数据行:{line}")
        if not name or key in pool:
            raise ValueError(f"池表:名称缺失或重复:{line}")
        has_x = _X_TOKEN.search(config) is not None
        if x in _EMPTY:
            if has_x:
                raise ValueError(f"池表:「{name}」配置写了 X 但 X 列为空")
            entry["x"] = None
        else:
            if not has_x:
                raise ValueError(f"池表:「{name}」X 列有值但配置里没有 X")
            entry["x"] = int(x)
        entry["config"] = config
        pool[key] = entry
    return pool


def expand_pool_entry(entry, rarity, char="池"):
    """池条目 + 档位 → 展开后的 effects(X 先换成 round(X基 × 档位系数))。"""
    config = entry["config"]
    if entry["x"] is not None:
        config = _X_TOKEN.sub(str(scale_x(entry["x"], rarity)), config)
    return _parse_effects(config, char)


def load_pool(root):
    path = Path(root) / "docs/design/字选型/特性表/池.md"
    return extract_pool(path.read_text(encoding="utf-8"))


def is_ref_row(name):
    prefix, sep, _ = name.partition("·")
    return bool(sep) and prefix in (POOL_PREFIX, GENERAL_POOL_PREFIX)


def _body_needs_enemy_target(body):
    """本体效果表是否要玩家选敌方目标(与 Core 的 EffectNeedsTarget 同口径的子集)。"""
    for e in body:
        if e["kind"] == "DamageSingle":
            if e.get("shape") not in ("All", "Scatter"):
                return True
        elif e["kind"] in ENEMY_PICK_KINDS and e.get("pick", "Primary") == "Primary" \
                and not e.get("targetAll"):
            return True
    return False


def _retarget_to_all(effects, body):
    """Ruling 17:池条目落到「不选目标」的面(全体伤害面等)时,单体敌方效果改为作用于全体。
    只补没写 pick 的、EffectPickRules 支持选择器的 kind;已有 pick 不覆盖。"""
    if _body_needs_enemy_target(body):
        return effects
    return [dict(e, pick="All") if e["kind"] in ENEMY_PICK_KINDS and "pick" not in e else e
            for e in effects]


def _expand_reference(char, slot, face, form, replaces, name, config, element, pool, chars):
    """`池·名` / `通·名` → (name, form_name, trigger, effects)。口径见 task-11:
    槽位、面与池条目一致;系池条目的系 = 本字的系;数值按本字档位展开。"""
    prefix, _, short = name.partition("·")
    if form not in _EMPTY or config not in _EMPTY:
        raise ValueError(f"特性表:字「{char}」池引用「{name}」的形态与效果配置必须写 —")
    if replaces not in _EMPTY:
        raise ValueError(f"特性表:字「{char}」池引用「{name}」不能带替换")
    if pool is None:
        raise ValueError(f"特性表:字「{char}」引用了池「{name}」但没有提供池表")
    if prefix == GENERAL_POOL_PREFIX:
        entry = pool.get((GENERAL_POOL_PREFIX, short))
    else:
        if element is None:
            raise ValueError(f"特性表:字「{char}」池引用「{name}」需要知道本字的系")
        entry = pool.get((element, short))
        if entry is None and any(k[1] == short and k[0] != GENERAL_POOL_PREFIX for k in pool):
            raise ValueError(f"特性表:字「{char}」是{element}系,不能引用别系的池条目「{short}」")
    if entry is None:
        raise ValueError(f"特性表:字「{char}」引用的池条目不存在:{name}")
    if slot != entry["slot"] or face != entry["face_cn"]:
        raise ValueError(f"特性表:字「{char}」{slot}/{face} 与池条目「{name}」"
                         f"({entry['slot']}/{entry['face_cn']})不一致")
    info = (chars or {}).get(char)
    if not info:
        raise ValueError(f"特性表:字「{char}」不在详表 ✅ 行中,无法按档位展开池条目「{name}」")
    form_name, trigger = FORMS[entry["form_cn"]]
    effects = expand_pool_entry(entry, info["rarity"], char)
    # 触发类（被动·暴击 / 被动·击杀）的反应自带目标，不随全体面补 pick All
    if entry["face_cn"] != "两面" and trigger is None:
        body = info.get("attackEffects", []) if entry["face_cn"] == "攻" else info.get("effects", [])
        effects = _retarget_to_all(effects, body)
    return short, form_name, trigger, effects


def extract_traits(markdown, element=None, pool=None, chars=None):
    """单张特性表全文 → {字: [trait, ...]};只收 ✅ 行。格式错误抛 ValueError。

    element:本表所属的系(火/金/水/土/木),给定时校验面名只能是 攻 / 两面 / 本系面名,
    并用于池引用的系校验。pool:extract_pool 的结果。chars:详表 extract() 的结果(取档位)。"""
    result = {}
    seen = set()
    for raw in markdown.split("\n"):
        line = raw.strip()
        if not line.startswith("|") or line.startswith("|---"):
            continue
        cells = [c.strip() for c in line.strip("|").split("|")]
        if cells == _HEADER:
            continue
        if len(cells) != len(_HEADER):
            raise ValueError(f"特性表:列数应为 {len(_HEADER)},实际 {len(cells)}:{line}")
        char, slot, face, form, replaces, name, config, impl = cells
        if not impl.startswith("✅"):
            continue
        if slot not in SLOTS:
            raise ValueError(f"特性表:字「{char}」槽位非法:{slot}")
        if face not in FACE_NAMES:
            raise ValueError(f"特性表:字「{char}」作用面非法:{face}")
        if not is_ref_row(name) and form not in FORMS:
            raise ValueError(f"特性表:字「{char}」形态非法:{form}")
        if not name:
            raise ValueError(f"特性表:字「{char}」{slot} 缺名称")
        if element is not None and face not in ("攻", "两面", ELEMENT_FACE[element]):
            raise ValueError(f"特性表:字「{char}」面「{face}」不属于{element}系(只能写 攻 / 两面 / {ELEMENT_FACE[element]})")
        face_key = FACE_NAMES[face]
        key = (char, slot, face_key)
        if key in seen:
            raise ValueError(f"特性表:字「{char}」{slot}/{face} 重复")
        seen.add(key)
        faces_here = {k[2] for k in seen if k[0] == char and k[1] == slot}
        if None in faces_here and len(faces_here) > 1:
            raise ValueError(f"特性表:字「{char}」{slot} 同时有两面特性与单面特性")
        trait = {"slot": slot}
        if FACE_NAMES[face]:
            trait["face"] = FACE_NAMES[face]
        if is_ref_row(name):
            name, form_name, trigger, effects = _expand_reference(
                char, slot, face, form, replaces, name, config, element, pool, chars)
            replaces = "—"
        else:
            if form not in FORMS:
                raise ValueError(f"特性表:字「{char}」形态非法:{form}")
            form_name, trigger = FORMS[form]
            effects = None
        if form_name:
            trait["form"] = form_name
        if trigger:
            trait["trigger"] = trigger
        if replaces not in ("—", "-", ""):
            if replaces not in SLOTS:
                raise ValueError(f"特性表:字「{char}」替换槽位非法:{replaces}")
            trait["replaces"] = replaces
        trait["name"] = name
        if effects is None:
            if config not in _EMPTY and "`" not in config:
                raise ValueError(f"特性表:字「{char}」{slot} 效果配置缺反引号 token:{config}")
            effects = _parse_effects(config, char)
        trait["effects"] = effects
        result.setdefault(char, []).append(trait)
    return result


def load_trait_tables(root, chars=None):
    """读五张特性表并合并;root = 仓库根。chars 缺省时从详表现抽(池引用要按档位展开)。"""
    root = Path(root)
    if chars is None:
        chars = extract((root / "docs/design/字选型/技能机制详表.md").read_text(encoding="utf-8"))
    pool = load_pool(root)
    merged = {}
    for element in ELEMENT_FILES:
        path = root / "docs/design/字选型/特性表" / f"{element}.md"
        tables = extract_traits(path.read_text(encoding="utf-8"), element, pool, chars)
        for char, traits in tables.items():
            merged.setdefault(char, []).extend(traits)
    return merged
