"""cjkvi-ids 数据采集与解析:原始行 → {codepoint, char, ids, leaves}。

数据源:https://raw.githubusercontent.com/cjkvi/cjkvi-ids/master/ids.txt
行格式:U+711A\t焚\t⿱林火[G]\t(可有多个 IDS 字段,带 [GTJKV] 地区标注)
"""
import re
import urllib.request
from pathlib import Path

IDS_URL = "https://raw.githubusercontent.com/cjkvi/cjkvi-ids/master/ids.txt"
RAW_PATH = Path(__file__).parent / "data" / "raw" / "ids.txt"

# IDC 结构描述符区(⿰⿱⿲⿳⿴⿵⿶⿷⿸⿹⿺⿻ 及扩展),是结构标记,不是叶子
_IDC_RANGE = (0x2FF0, 0x2FFF)
_REGION_TAG = re.compile(r"\[[A-Za-z]+\]")
_ENTITY = re.compile(r"&[^;]+;")


def parse_ids_line(line):
    """解析一行,返回 {codepoint, char, ids};注释行/畸形行返回 None。"""
    line = line.strip()
    if not line or line.startswith("#"):
        return None
    fields = line.split("\t")
    if len(fields) < 3:
        return None
    codepoint, char = fields[0], fields[1]
    ids = _REGION_TAG.sub("", fields[2]).strip()
    return {"codepoint": codepoint, "char": char, "ids": ids}


def extract_leaves(ids):
    """从 IDS 序列提取叶子部件。

    - IDC 结构符(U+2FF0..U+2FFF)跳过。
    - &CDP-xxxx; 实体引用作为单个不可分叶子。
    - 其余任意字符(含扩展区)均为叶子——不做 Unicode 范围过滤。
    """
    leaves = []
    i = 0
    while i < len(ids):
        m = _ENTITY.match(ids, i)
        if m:
            leaves.append(m.group())
            i = m.end()
            continue
        ch = ids[i]
        if not (_IDC_RANGE[0] <= ord(ch) <= _IDC_RANGE[1]):
            leaves.append(ch)
        i += 1
    return leaves


def parse_ids_text(text):
    """解析整份 ids.txt 文本,返回 entry 列表(附 leaves 字段)。"""
    entries = []
    for line in text.splitlines():
        entry = parse_ids_line(line)
        if entry is None:
            continue
        entry["leaves"] = extract_leaves(entry["ids"])
        entries.append(entry)
    return entries


def download_ids(url=IDS_URL, dest=RAW_PATH, force=False):
    """下载原始 ids.txt 到 data/raw/(已存在则跳过)。"""
    dest = Path(dest)
    if dest.exists() and not force:
        return dest
    dest.parent.mkdir(parents=True, exist_ok=True)
    with urllib.request.urlopen(url, timeout=60) as resp:
        dest.write_bytes(resp.read())
    return dest
