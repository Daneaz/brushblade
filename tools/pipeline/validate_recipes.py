"""配方图谱 DAG 校验(第 4 章 4.9.6):无环、原料存在、无孤儿部件。

校验对象是人工撰写的游戏配方表;每次内容改动后运行(后续接 CI)。
错误为结构化字典:{"type": "cycle"|"missing"|"orphan", ...}。
"""


def validate_recipes(recipes, components):
    """校验配方表。recipes: {字: [原料...]},components: 部件集合。返回错误列表。"""
    errors = []

    # 原料存在:每个原料要么是部件,要么是另一个已定义配方的字
    defined = set(recipes) | set(components)
    for char, ingredients in recipes.items():
        for ingredient in ingredients:
            if ingredient not in defined:
                errors.append({"type": "missing", "char": char, "ingredient": ingredient})

    # 无环:配方永远指向更原子的对象(DFS 三色标记)
    WHITE, GRAY, BLACK = 0, 1, 2
    color = dict.fromkeys(recipes, WHITE)

    def visit(node):
        color[node] = GRAY
        for ingredient in recipes.get(node, []):
            if ingredient not in recipes:
                continue  # 部件/未定义原料不构成环
            if color[ingredient] == GRAY:
                errors.append({"type": "cycle", "char": ingredient})
                continue
            if color[ingredient] == WHITE:
                visit(ingredient)
        color[node] = BLACK

    for char in recipes:
        if color[char] == WHITE:
            visit(char)

    # 无孤儿部件:每个部件至少被一条配方使用(标记而非致命)
    used = {i for ingredients in recipes.values() for i in ingredients}
    for component in sorted(set(components) - used):
        errors.append({"type": "orphan", "component": component})

    return errors
