"""导出:候选字表 → JSON(中文不转义,便于人工精筛时直读)。"""
import json
from pathlib import Path


def export_candidates(candidates, dest):
    """写出 {meta, candidates} 结构的 JSON,自动建父目录。"""
    dest = Path(dest)
    dest.parent.mkdir(parents=True, exist_ok=True)
    payload = {
        "meta": {"count": len(candidates)},
        "candidates": candidates,
    }
    dest.write_text(
        json.dumps(payload, ensure_ascii=False, indent=1),
        encoding="utf-8",
    )
    return dest
