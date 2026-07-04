"""候选字筛选:部件→五行属性映射(v0.4 真五行含土,见第 3 章 3.4)+ 复杂度筛选。"""

# 部首/部件 → 属性(第 3 章 3.4 部首即属性表;心系中立但仍是可玩属性家族)
ATTR_MAP = {
    "火": "火", "灬": "火",
    "氵": "水", "水": "水", "冫": "水",
    "木": "木", "艹": "木", "竹": "木",
    "钅": "金", "金": "金", "刂": "金", "刀": "金", "戈": "金",
    "土": "土", "山": "土", "石": "土",
    "心": "心", "忄": "心",
}

# 展示顺序:相生环顺序(木火土金水)+ 心
_ATTR_ORDER = {a: i for i, a in enumerate(["木", "火", "土", "金", "水", "心"])}


def attr_of(component):
    """部件的五行属性;中性部件返回 None。"""
    return ATTR_MAP.get(component)


def attrs_of_leaves(leaves):
    """叶子列表 → 去重后的属性列表(按相生环顺序稳定排序)。"""
    attrs = {attr_of(leaf) for leaf in leaves} - {None}
    return sorted(attrs, key=_ATTR_ORDER.__getitem__)


def filter_candidates(entries, max_complexity=3):
    """筛选候选字:至少含一个带属性部件,且复杂度(叶子数)≤ max_complexity。

    输出在 entry 上附加 attrs 与 complexity 字段。
    """
    candidates = []
    for entry in entries:
        attrs = attrs_of_leaves(entry["leaves"])
        complexity = len(entry["leaves"])
        if attrs and complexity <= max_complexity:
            candidates.append({**entry, "attrs": attrs, "complexity": complexity})
    return candidates
