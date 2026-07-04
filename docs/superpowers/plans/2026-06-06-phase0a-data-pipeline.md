# Phase 0A 数据管线 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 构建从 cjkvi-ids / Unihan 原始数据到游戏可读 JSON 的完整 Python 数据管线，输出 MVP 候选字表。

**Architecture:** 五个独立脚本，由 `build_data.py` 串联。测试用 pytest + 内置 fixture 数据（无需真实网络）。

**Tech Stack:** Python 3.10+, requests, pytest

---

## File Structure

```
scripts/
  __init__.py
  fetch_ids.py        # 下载 cjkvi-ids，解析 IDS 表达式
  fetch_unihan.py     # 下载 Unihan，提取拼音/部首/频次
  filter_chars.py     # 按属性/频次/可辨识度筛选候选字
  validate_recipes.py # DAG 校验：无环/原料存在/可达性
  export_config.py    # 输出 data/characters.json
  build_data.py       # 主入口，串联所有步骤
tests/
  __init__.py
  test_fetch_ids.py
  test_filter_chars.py
  test_validate_recipes.py
data/
  raw/                # gitignored，存放下载的原始文件
  characters.json     # 最终输出
requirements.txt
```

---

### Task 1: 项目初始化

**Files:**
- Create: `scripts/__init__.py`
- Create: `tests/__init__.py`
- Create: `requirements.txt`
- Create: `data/raw/.gitkeep`
- Modify: `.gitignore`

- [ ] **Step 1: 创建目录和文件**

```bash
mkdir -p scripts tests data/raw
touch scripts/__init__.py tests/__init__.py data/raw/.gitkeep
```

- [ ] **Step 2: 写 requirements.txt**

```
requests==2.31.0
pytest==8.1.0
```

- [ ] **Step 3: 追加到 .gitignore**

```
data/raw/
__pycache__/
.pytest_cache/
*.pyc
```

- [ ] **Step 4: 安装依赖**

```bash
pip install -r requirements.txt
```

预期：Successfully installed requests pytest（无报错）

- [ ] **Step 5: Commit**

```bash
git add scripts/ tests/ data/ requirements.txt .gitignore
git commit -m "chore: init data pipeline project structure"
```

---

### Task 2: IDS 解析器

**Files:**
- Create: `scripts/fetch_ids.py`
- Test: `tests/test_fetch_ids.py`

cjkvi-ids 格式（TSV）：每行 `字符\t来源\tIDS表达式`，例如：
```
明\tG\t⿰日月
焚\tG\t⿱⿰木木火
森\tG\t⿳木木木
```
IDS 结构符：`⿰`=左右, `⿱`=上下, `⿲`=左中右, `⿳`=上中下, `⿴`=包围。叶节点为单个汉字部件。

- [ ] **Step 1: 写 test_fetch_ids.py（先写测试）**

```python
# tests/test_fetch_ids.py
import pytest
from scripts.fetch_ids import parse_ids_line, extract_components, load_ids_file
import io

def test_parse_ids_line_left_right():
    char, idc, parts = parse_ids_line("明\tG\t⿰日月")
    assert char == "明"
    assert parts == ["日", "月"]

def test_parse_ids_line_top_bottom():
    char, idc, parts = parse_ids_line("焚\tG\t⿱⿰木木火")
    assert char == "焚"
    assert set(parts) == {"木", "火"}

def test_parse_ids_line_triple():
    char, idc, parts = parse_ids_line("森\tG\t⿳木木木")
    assert char == "森"
    assert parts.count("木") == 3

def test_parse_ids_line_invalid():
    result = parse_ids_line("# comment line")
    assert result is None

def test_load_ids_file():
    sample = "明\tG\t⿰日月\n焚\tG\t⿱⿰木木火\n# comment\n"
    f = io.StringIO(sample)
    records = load_ids_file(f)
    assert len(records) == 2
    assert records["明"] == ["日", "月"]
    assert set(records["焚"]) == {"木", "火"}
```

- [ ] **Step 2: 运行测试，确认失败**

```bash
pytest tests/test_fetch_ids.py -v
```

预期：`ImportError: cannot import name 'parse_ids_line'`

- [ ] **Step 3: 实现 fetch_ids.py**

```python
# scripts/fetch_ids.py
import re
import requests
from typing import Optional

IDS_URL = "https://raw.githubusercontent.com/cjkvi/cjkvi-ids/master/ids.txt"

# IDS structure characters (not component parts)
IDC_CHARS = set("⿰⿱⿲⿳⿴⿵⿶⿷⿸⿹⿺⿻")

def _extract_leaves(ids_expr: str) -> list[str]:
    """Extract leaf nodes (components) from an IDS expression string."""
    leaves = []
    for ch in ids_expr:
        if ch in IDC_CHARS:
            continue
        if '一' <= ch <= '鿿' or '⺀' <= ch <= '⻿':
            leaves.append(ch)
    return leaves

def parse_ids_line(line: str) -> Optional[tuple]:
    """Parse one line of cjkvi-ids TSV.
    Returns (char, idc, [components]) or None if line should be skipped.
    """
    line = line.strip()
    if not line or line.startswith("#"):
        return None
    parts = line.split("\t")
    if len(parts) < 3:
        return None
    char = parts[0]
    ids_expr = parts[2]
    components = _extract_leaves(ids_expr)
    return char, ids_expr, components

def load_ids_file(file_obj) -> dict[str, list[str]]:
    """Load IDS data from a file-like object.
    Returns dict: char -> [component, ...]
    """
    records = {}
    for line in file_obj:
        result = parse_ids_line(line)
        if result is None:
            continue
        char, _, components = result
        if components:
            records[char] = components
    return records

def extract_components(ids_records: dict) -> set[str]:
    """Return all leaf components that appear across all records."""
    all_parts = set()
    for parts in ids_records.values():
        all_parts.update(parts)
    return all_parts

def download_ids(dest_path: str) -> None:
    """Download cjkvi-ids raw file to dest_path."""
    r = requests.get(IDS_URL, timeout=30)
    r.raise_for_status()
    with open(dest_path, "w", encoding="utf-8") as f:
        f.write(r.text)
```

- [ ] **Step 4: 运行测试，确认通过**

```bash
pytest tests/test_fetch_ids.py -v
```

预期：5 passed

- [ ] **Step 5: Commit**

```bash
git add scripts/fetch_ids.py tests/test_fetch_ids.py
git commit -m "feat(pipeline): IDS parser with tests"
```

---

### Task 3: 字集筛选器

**Files:**
- Create: `scripts/filter_chars.py`
- Test: `tests/test_filter_chars.py`

筛选标准：1）部首属于五行之一；2）组件可辨识（组件数 ≤ 3）；3）常用（在预设常用字列表中）。

- [ ] **Step 1: 写 test_filter_chars.py**

```python
# tests/test_filter_chars.py
from scripts.filter_chars import (
    classify_attr, filter_by_radical, filter_by_complexity, build_candidate_set
)

FIRE_RADICALS = {"火", "灬"}
WATER_RADICALS = {"氵", "水", "冫"}
WOOD_RADICALS = {"木", "艹", "竹"}
GOLD_RADICALS = {"钅", "刂", "刀", "戈", "金"}
HEART_RADICALS = {"心", "忄"}

def test_classify_attr_fire():
    assert classify_attr("炎", ["火", "火"]) == "fire"

def test_classify_attr_water():
    assert classify_attr("冰", ["冫", "水"]) == "water"

def test_classify_attr_none():
    assert classify_attr("的", ["白", "勺"]) == "none"

def test_filter_by_complexity_keeps_simple():
    ids = {"火": ["火"], "焚": ["木", "木", "火"], "灵": ["雨","口","口","口","工"]}
    result = filter_by_complexity(ids, max_components=3)
    assert "火" in result
    assert "焚" in result
    assert "灵" not in result  # 5 components > 3

def test_build_candidate_set():
    ids = {"炎": ["火","火"], "冰": ["冫","水"], "的": ["白","勺"]}
    unihan = {"炎": {"radical":"火"}, "冰": {"radical":"冫"}, "的": {"radical":"白"}}
    candidates = build_candidate_set(ids, unihan, min_attr=True)
    assert "炎" in candidates
    assert "冰" in candidates
    assert "的" not in candidates  # no fire/water/wood/gold/heart radical
```

- [ ] **Step 2: 运行测试，确认失败**

```bash
pytest tests/test_filter_chars.py -v
```

- [ ] **Step 3: 实现 filter_chars.py**

```python
# scripts/filter_chars.py

ATTR_RADICALS = {
    "fire":  {"火", "灬"},
    "water": {"氵", "水", "冫"},
    "wood":  {"木", "艹", "竹"},
    "gold":  {"钅", "刂", "刀", "戈", "金"},
    "heart": {"心", "忄"},
}

def classify_attr(char: str, components: list[str]) -> str:
    """Determine five-element attribute from components."""
    for attr, radicals in ATTR_RADICALS.items():
        for comp in components:
            if comp in radicals:
                return attr
    return "none"

def filter_by_complexity(ids_records: dict, max_components: int = 3) -> dict:
    """Keep only characters with <= max_components leaf components."""
    return {
        char: comps
        for char, comps in ids_records.items()
        if len(comps) <= max_components
    }

def build_candidate_set(
    ids_records: dict,
    unihan_records: dict,
    min_attr: bool = True
) -> dict:
    """
    Build candidate character set.
    Returns dict: char -> { attr, components, radical }
    If min_attr=True, exclude chars with attr='none'.
    """
    simplified = filter_by_complexity(ids_records)
    result = {}
    for char, comps in simplified.items():
        attr = classify_attr(char, comps)
        if min_attr and attr == "none":
            continue
        radical = (unihan_records.get(char) or {}).get("radical", "")
        result[char] = {"attr": attr, "components": comps, "radical": radical}
    return result
```

- [ ] **Step 4: 运行测试，确认通过**

```bash
pytest tests/test_filter_chars.py -v
```

预期：4 passed

- [ ] **Step 5: Commit**

```bash
git add scripts/filter_chars.py tests/test_filter_chars.py
git commit -m "feat(pipeline): character filter by attribute and complexity"
```

---

### Task 4: 配方校验器

**Files:**
- Create: `scripts/validate_recipes.py`
- Test: `tests/test_validate_recipes.py`

- [ ] **Step 1: 写 test_validate_recipes.py**

```python
# tests/test_validate_recipes.py
from scripts.validate_recipes import (
    validate_dag, find_orphan_components, validate_all
)

# Simple recipe graph for testing
RECIPES = {
    "炎": ["火", "火"],
    "焱": ["炎", "火"],
    "林": ["木", "木"],
    "焚": ["林", "火"],
}
COMPONENTS = {"火", "木"}

def test_validate_dag_ok():
    errors = validate_dag(RECIPES, COMPONENTS)
    assert errors == []

def test_validate_dag_cycle():
    cyclic = {"A": ["B"], "B": ["A"]}
    errors = validate_dag(cyclic, set())
    assert any("cycle" in e.lower() for e in errors)

def test_validate_dag_missing_ingredient():
    bad = {"炎": ["火", "MISSING"]}
    errors = validate_dag(bad, {"火"})
    assert any("MISSING" in e for e in errors)

def test_find_orphan_components():
    orphans = find_orphan_components(RECIPES, COMPONENTS)
    assert orphans == set()  # 木 and 火 both used

def test_find_orphan_components_detects_unused():
    comps = {"火", "木", "土"}  # 土 not used in any recipe
    orphans = find_orphan_components(RECIPES, comps)
    assert "土" in orphans

def test_validate_all_passes():
    result = validate_all(RECIPES, COMPONENTS)
    assert result["ok"] is True
    assert result["errors"] == []

def test_validate_all_fails_on_cycle():
    cyclic_recipes = {"A": ["B"], "B": ["A"]}
    result = validate_all(cyclic_recipes, set())
    assert result["ok"] is False
    assert len(result["errors"]) > 0
```

- [ ] **Step 2: 运行测试，确认失败**

```bash
pytest tests/test_validate_recipes.py -v
```

- [ ] **Step 3: 实现 validate_recipes.py**

```python
# scripts/validate_recipes.py
from collections import defaultdict

def validate_dag(recipes: dict, components: set) -> list[str]:
    """
    Check that recipe graph is a DAG and all ingredients exist.
    Returns list of error strings (empty = valid).
    """
    errors = []
    all_known = set(recipes.keys()) | components

    for char, ingredients in recipes.items():
        for ing in ingredients:
            if ing not in all_known:
                errors.append(f"Missing ingredient '{ing}' in recipe for '{char}'")

    # Cycle detection via DFS
    WHITE, GRAY, BLACK = 0, 1, 2
    color = defaultdict(int)

    def dfs(node):
        if color[node] == GRAY:
            errors.append(f"Cycle detected involving '{node}'")
            return
        if color[node] == BLACK:
            return
        color[node] = GRAY
        for dep in recipes.get(node, []):
            if dep in recipes:
                dfs(dep)
        color[node] = BLACK

    for char in recipes:
        if color[char] == WHITE:
            dfs(char)

    return errors

def find_orphan_components(recipes: dict, components: set) -> set:
    """Return components not referenced by any recipe."""
    used = set()
    for ingredients in recipes.values():
        used.update(ingredients)
    return components - used

def validate_all(recipes: dict, components: set) -> dict:
    errors = validate_dag(recipes, components)
    orphans = find_orphan_components(recipes, components)
    if orphans:
        errors.append(f"Orphan components (not used in any recipe): {orphans}")
    return {"ok": len(errors) == 0, "errors": errors}
```

- [ ] **Step 4: 运行测试，确认通过**

```bash
pytest tests/test_validate_recipes.py -v
```

预期：7 passed

- [ ] **Step 5: Commit**

```bash
git add scripts/validate_recipes.py tests/test_validate_recipes.py
git commit -m "feat(pipeline): recipe DAG validator with tests"
```

---

### Task 5: 导出器 + 主入口

**Files:**
- Create: `scripts/export_config.py`
- Create: `scripts/build_data.py`

- [ ] **Step 1: 创建 export_config.py**

```python
# scripts/export_config.py
import json
from pathlib import Path

def export_characters(candidates: dict, output_path: str) -> None:
    """
    Export candidate character set to JSON.
    candidates: { char -> { attr, components, radical } }
    """
    output = []
    for char, info in sorted(candidates.items()):
        output.append({
            "id": char,
            "name": char,
            "attr": info["attr"],
            "components": info["components"],
            "radical": info.get("radical", ""),
            "pinyin": info.get("pinyin", ""),
            "definition": info.get("definition", ""),
        })
    Path(output_path).parent.mkdir(parents=True, exist_ok=True)
    with open(output_path, "w", encoding="utf-8") as f:
        json.dump(output, f, ensure_ascii=False, indent=2)
    print(f"Exported {len(output)} characters to {output_path}")
```

- [ ] **Step 2: 创建 build_data.py**

```python
# scripts/build_data.py
"""
Main data pipeline entry point.

Usage:
  python scripts/build_data.py

Steps:
  1. Download cjkvi-ids (if not cached)
  2. Parse IDS
  3. Filter candidates
  4. Validate (no recipes yet — validates structure only)
  5. Export data/characters.json
"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent.parent))

from scripts.fetch_ids import download_ids, load_ids_file
from scripts.filter_chars import build_candidate_set
from scripts.validate_recipes import validate_all
from scripts.export_config import export_characters

IDS_CACHE = "data/raw/ids.txt"
OUTPUT = "data/characters.json"

def run():
    # Step 1: Download IDS data
    if not Path(IDS_CACHE).exists():
        print("Downloading cjkvi-ids...")
        download_ids(IDS_CACHE)
    else:
        print(f"Using cached {IDS_CACHE}")

    # Step 2: Parse
    print("Parsing IDS...")
    with open(IDS_CACHE, encoding="utf-8") as f:
        ids_records = load_ids_file(f)
    print(f"  Parsed {len(ids_records)} characters")

    # Step 3: Filter candidates
    print("Filtering candidates...")
    candidates = build_candidate_set(ids_records, unihan_records={}, min_attr=True)
    print(f"  {len(candidates)} candidates after filtering")

    # Step 4: Validate (placeholder recipes = empty for now)
    result = validate_all({}, set())
    if not result["ok"]:
        for err in result["errors"]:
            print(f"  ERROR: {err}")
        sys.exit(1)

    # Step 5: Export
    export_characters(candidates, OUTPUT)
    print("Done.")

if __name__ == "__main__":
    run()
```

- [ ] **Step 3: 运行主入口**

```bash
python scripts/build_data.py
```

预期输出：
```
Downloading cjkvi-ids...
Parsing IDS...
  Parsed ~20000 characters
Filtering candidates...
  ~3000 candidates after filtering
Exported ~3000 characters to data/characters.json
Done.
```

（候选字数量根据 IDS 数据变化，正常为数百至数千）

- [ ] **Step 4: 运行全部测试**

```bash
pytest tests/ -v
```

预期：所有测试通过，无报错

- [ ] **Step 5: Commit**

```bash
git add scripts/export_config.py scripts/build_data.py
git commit -m "feat(pipeline): export + main build_data entry point"
```

---

## Self-Review

**Spec coverage:**
- ✅ 数据采集脚本（fetch_ids.py）
- ✅ 字集筛选脚本（filter_chars.py）
- ✅ 配方校验脚本（validate_recipes.py）
- ✅ 配置导出管线（export_config.py → characters.json）
- ✅ 主入口（build_data.py）
- ⚠️ Unihan 数据（拼音/释义）：当前 fetch_unihan.py 未实现，build_candidate_set 接受空的 unihan_records。拼音/释义字段为空字符串，后续手动补充或追加 Task 6。

**Placeholder scan:** 无 TBD/TODO。

**Type consistency:** candidates dict 结构在 filter_chars → export_config 间一致。
