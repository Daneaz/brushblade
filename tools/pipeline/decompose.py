"""IDS 树递归拆解:字 → 配方部件表。

规则(v0.7 拍板):
- **只拆上下左右**(⿰⿱⿲⿳);包围/重叠等结构(⿴⿵⿸⿻…)不是拆合语义,整体保留。
- **逐级判定**:第 1 级(字本身的配方)无条件拆;第 2 级起,某级新拆出的直接子部件里
  若一个五行部件都没有,则回退该级。例:燥 → 火+喿 → 火+品+木,再拆 品=口+吅 无五行,
  回退,定稿 火+品+木。
- **部件必须是真实字**:子树若反查不到对应的字(如「肰」在 cjkvi 里写作 ⿰⿴𠂊冫犬),
  该字不拆——配方里不放没有字形、做不成卡的 IDS 串。
- 五行部件是终点(火 = ⿱八人 不再往下拆);某级展开后部件数超上限也回退该级。
"""
import re

from filter_chars import attr_of

# IDC 结构描述符 → 操作数个数
_ARITY = {0x2FF0: 2, 0x2FF1: 2, 0x2FF2: 3, 0x2FF3: 3, 0x2FF4: 2, 0x2FF5: 2,
          0x2FF6: 2, 0x2FF7: 2, 0x2FF8: 2, 0x2FF9: 2, 0x2FFA: 2, 0x2FFB: 2}

# 可拆结构:左右、上下、左中右、上中下
SPLIT_IDC = {"⿰", "⿱", "⿲", "⿳"}

_ENTITY = re.compile(r"&[^;]+;")


def _tokenize(ids):
    """IDS 串 → token 列表(&CDP-xxxx; 实体算一个 token)。"""
    tokens = []
    i = 0
    while i < len(ids):
        m = _ENTITY.match(ids, i)
        if m:
            tokens.append(m.group())
            i = m.end()
            continue
        tokens.append(ids[i])
        i += 1
    return tokens


def _parse(tokens, i):
    """前缀式解析,返回 (节点, 下一位置);畸形返回 (None, i)。"""
    if i >= len(tokens):
        return None, i
    token = tokens[i]
    arity = _ARITY.get(ord(token)) if len(token) == 1 else None
    if arity is None:
        return token, i + 1
    children = []
    i += 1
    for _ in range(arity):
        child, i = _parse(tokens, i)
        if child is None:
            return None, i
        children.append(child)
    return (token, children), i


def parse_ids_tree(ids):
    """IDS 串 → 树:叶子为 str,枝节点为 (idc, [子节点]);畸形返回 None。"""
    node, _ = _parse(_tokenize(ids), 0)
    return node


def flatten_tree(node):
    """树 → IDS 串(反查用的规范形)。"""
    if isinstance(node, str):
        return node
    return node[0] + "".join(flatten_tree(child) for child in node[1])


def build_index(entries):
    """entry 列表 → {"ids": {字: ids}, "chars": {ids: 字}}(反查取先到者)。"""
    by_char = {}
    by_ids = {}
    for entry in entries:
        char, ids = entry["char"], entry["ids"]
        by_char.setdefault(char, ids)
        if ids != char:
            by_ids.setdefault(ids, char)
    return {"ids": by_char, "chars": by_ids}


def split_once(char, index):
    """拆一级:返回直接子部件(都是真实字);不可拆返回 None。"""
    ids = index["ids"].get(char)
    if not ids or ids == char:
        return None
    node = parse_ids_tree(ids)
    if not isinstance(node, tuple) or node[0] not in SPLIT_IDC:
        return None
    parts = []
    for child in node[1]:
        if isinstance(child, str):
            parts.append(child)
            continue
        resolved = index["chars"].get(flatten_tree(child))
        if resolved is None:
            return None  # 子树不对应任何真实字,整字不拆
        parts.append(resolved)
    return parts


def expand_to_elements(char, index, _seen=()):
    """字 → 一路拆到底的叶子表(五行部件为终点),不回退也不限部件数。

    方案 A 枢纽字体系的口径:叶子全为五行元素的字才是「纯元素可达」,能进部件池体系。
    与 decompose 的区别是不做逐级判定与复杂度回退——那是全量字体系(方案 B)的筛法。
    """
    if attr_of(char) or char in _seen:
        return [char]
    parts = split_once(char, index)
    if not parts:
        return [char]
    leaves = []
    for part in parts:
        leaves.extend(expand_to_elements(part, index, _seen + (char,)))
    return leaves


def decompose(char, index, max_complexity=3):
    """字 → 递归拆解后的部件表(见模块头规则)。不可拆时返回 [char]。"""
    level = split_once(char, index)
    if level is None:
        return [char]
    expanded = {char}
    while True:
        nxt = []
        advanced = set()
        for part in level:
            parts = None if (part in expanded or attr_of(part)) else split_once(part, index)
            if parts and any(attr_of(p) for p in parts):
                nxt.extend(parts)
                advanced.add(part)
            else:
                nxt.append(part)
        if not advanced or len(nxt) > max_complexity:
            return level
        level = nxt
        expanded |= advanced
