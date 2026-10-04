"""字卡特性表 → traits(spec v7 §9 / §11.9)。

五张表按系拆在 docs/design/字选型/特性表/{火,金,水,土,木}.md,格式统一:
| 字 | 槽 | 面 | 形态 | 替换 | 名 | 效果配置 | 实现 |
只导出「实现」列以 ✅ 开头的行。效果配置复用详表的 token 语法(_parse_effects),
未知 token / 缺 turns 等由它报错。
"""
from pathlib import Path

from extract_values import _parse_effects

ELEMENT_FILES = ["火", "金", "水", "土", "木"]
FACE_NAMES = {"攻": "Attack", "燃": "Feature", "铠": "Feature", "润": "Feature",
              "固": "Feature", "生": "Feature", "两面": None}
# 形态 → (form, trigger);「被动·暴击/击杀」= 被动 + 触发类型(spec v7 §2.3)。主动只能是出字时,不接后缀
FORMS = {"主动": (None, None), "被动": ("Passive", None),
         "被动·暴击": ("Passive", "OnCrit"), "被动·击杀": ("Passive", "OnKill")}
SLOTS = {"Lv1", "Lv3", "Lv4", "Lv5", "Lv6", "Lv8"}
_HEADER = ["字", "槽", "面", "形态", "替换", "名", "效果配置", "实现"]


def extract_traits(markdown):
    """单张特性表全文 → {字: [trait, ...]};只收 ✅ 行。格式错误抛 ValueError。"""
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
        if form not in FORMS:
            raise ValueError(f"特性表:字「{char}」形态非法:{form}")
        if not name:
            raise ValueError(f"特性表:字「{char}」{slot} 缺名称")
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
        form_name, trigger = FORMS[form]
        if form_name:
            trait["form"] = form_name
        if trigger:
            trait["trigger"] = trigger
        if replaces not in ("—", "-", ""):
            if replaces not in SLOTS:
                raise ValueError(f"特性表:字「{char}」替换槽位非法:{replaces}")
            trait["replaces"] = replaces
        trait["name"] = name
        if config not in ("—", "-", "") and "`" not in config:
            raise ValueError(f"特性表:字「{char}」{slot} 效果配置缺反引号 token:{config}")
        trait["effects"] = _parse_effects(config, char)
        result.setdefault(char, []).append(trait)
    return result


def load_trait_tables(root):
    """读五张特性表并合并;root = 仓库根。"""
    merged = {}
    for element in ELEMENT_FILES:
        path = Path(root) / "docs/design/字选型/特性表" / f"{element}.md"
        for char, traits in extract_traits(path.read_text(encoding="utf-8")).items():
            merged.setdefault(char, []).extend(traits)
    return merged
