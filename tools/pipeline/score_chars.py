"""部件复用价值打分 → 六档稀有度(v0.7 拍板)。

分数越高稀有度越高,两个维度各占一半:
1. **有效部件数**:递归配方里能和别的部件组成别的字的部件个数(重复部件重复计)。
   燥 = 火+品+木 → 2,「品」在常用字里组不出任何字。
2. **组字总数**:该字各层级出现过的不同部件(一级 ∪ 递归)各自能组成多少字,求和。
   焚 一级 林+火、二级 木+木+火 → 林/火/木 三个部件的组字数之和。

「能组成别的字」只算**一级组合**:噪 = 口+喿,一级部件是「喿」不是「品」,
故「品」不因噪而记分。图的范围是候选表里的 GB2312 一级常用字——玩家认得的字。

两维度在各自池内 min-max 归一到 0~100 后各取一半;六档按金字塔比例从高分往低分切,
同分同档。分档按系分别进行:火系最高分 210、水系 363,统一分档火系永远出不了红卡。
"""
from collections import defaultdict

# 从高到低,每档占池子的比例
RARITY_TIERS = [("红", 0.07), ("橙", 0.08), ("紫", 0.15),
                ("蓝", 0.20), ("绿", 0.25), ("白", 0.25)]


def build_combination_graph(entries):
    """entries: [{char, parts1}] → {部件: {它能组成的字}};只算一级组合边。"""
    graph = defaultdict(set)
    for entry in entries:
        char = entry["char"]
        for part in entry["parts1"]:
            if part != char:
                graph[part].add(char)
    return dict(graph)


def char_metrics(entry, graph):
    """→ (有效部件数, 组字总数);见模块头两个维度的定义。"""
    char, leaves = entry["char"], entry["leaves"]
    effective = sum(1 for leaf in leaves if graph.get(leaf))
    parts = (set(entry["parts1"]) | set(leaves)) - {char}
    production = sum(len(graph.get(part, ())) for part in parts)
    return effective, production


def score_pool(metrics):
    """{字: (有效部件数, 组字总数)} → {字: 0~100 分};两维度池内归一后各占一半。"""
    if not metrics:
        return {}

    def normalized(index):
        values = [m[index] for m in metrics.values()]
        low, high = min(values), max(values)
        span = high - low
        return {char: 0.0 if span == 0 else (m[index] - low) / span * 100
                for char, m in metrics.items()}

    effective, production = normalized(0), normalized(1)
    return {char: effective[char] * 0.5 + production[char] * 0.5 for char in metrics}


def assign_rarity(scored, tiers=RARITY_TIERS):
    """{字: 分数} → {字: 稀有度};按金字塔比例从高分往低分切,同分同档。"""
    if not scored:
        return {}
    by_score = defaultdict(list)
    for char, score in scored.items():
        by_score[score].append(char)

    bounds = []
    cumulative = 0.0
    for name, ratio in tiers:
        cumulative += ratio
        # 取整成字数配额,免得 0.07*100=7.000000000000001 这类浮点误差挤走一个名额
        bounds.append((name, round(cumulative * len(scored))))

    result = {}
    done = 0
    tier = 0
    for score in sorted(by_score, reverse=True):
        while tier < len(bounds) - 1 and done >= bounds[tier][1]:
            tier += 1
        for char in by_score[score]:
            result[char] = bounds[tier][0]
        done += len(by_score[score])
    return result
