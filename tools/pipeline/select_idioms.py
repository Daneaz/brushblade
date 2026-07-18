"""成语 Boss 候选筛选(20.7/Q26):候选判定、意象计分、逐字五行标注。

数据源:data/raw/xinhua_idiom.json(chinese-xinhua,开发期用)。
成语四字 → Boss 四阶段;逐字五行供阶段属性,未知字标 Heart(心中立)。
"""
from report_pool_candidates import gb_level, is_displayable

# 气势/意象字:天地山河与兵戈水火,含者更像 Boss 名号
IMAGERY = set("山河海江湖天地风云雷电火焰水冰石岩金铁剑刀弓马龙虎狮鹰潮浪涛沙尘星月日雪霜雾木林森崩裂轰鸣啸吼")

_ELEMENT_NAMES = {"木": "Wood", "火": "Fire", "土": "Earth", "金": "Metal", "水": "Water", "心": "Heart"}


def is_candidate(word):
    """Boss 候选:恰四字、互不重复、全部基本区且一级常用(玩家全认识)。"""
    if len(word) != 4 or len(set(word)) != 4:
        return False
    return all(is_displayable(ch) and gb_level(ch) == 1 for ch in word)


def imagery_score(word):
    """意象计分:含多少个气势字(排序用,分高者优先给用户看)。"""
    return sum(1 for ch in word if ch in IMAGERY)


def char_element(ch, attrs_map):
    """字的五行(取候选表属性首位);无属性字 → Heart(心中立)。"""
    attrs = attrs_map.get(ch)
    if not attrs:
        return "Heart"
    return _ELEMENT_NAMES[attrs[0]]
