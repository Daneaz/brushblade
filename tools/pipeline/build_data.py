"""管线主入口:下载 → 解析 → 筛选 → 导出候选字表。

用法:python3 build_data.py [--max-complexity 3]
产出:out/candidates.json(不入 git;供人工精筛 MVP 字表)
"""
import argparse
from collections import Counter
from pathlib import Path

from fetch_ids import download_ids, parse_ids_text
from filter_chars import filter_candidates
from export_config import export_candidates

OUT_PATH = Path(__file__).parent / "out" / "candidates.json"


def build_from_text(text, dest, max_complexity=3):
    """从 ids.txt 文本跑完整管线,返回汇总统计。"""
    entries = parse_ids_text(text)
    candidates = filter_candidates(entries, max_complexity=max_complexity)
    export_candidates(candidates, dest)
    by_attr = Counter(attr for c in candidates for attr in c["attrs"])
    return {
        "parsed": len(entries),
        "candidates": len(candidates),
        "by_attr": dict(by_attr),
    }


def main():
    parser = argparse.ArgumentParser(description="字·斗 数据管线")
    parser.add_argument("--max-complexity", type=int, default=3)
    args = parser.parse_args()

    raw = download_ids()
    summary = build_from_text(
        raw.read_text(encoding="utf-8"), OUT_PATH, args.max_complexity
    )
    print(f"解析 {summary['parsed']} 字,筛出候选 {summary['candidates']} 字 → {OUT_PATH}")
    print("属性分布:", summary["by_attr"])


if __name__ == "__main__":
    main()
