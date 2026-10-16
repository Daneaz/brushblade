#!/usr/bin/env python3
"""改档工具:处理 save.json 的 SaveGuard 签名(Data/SaveGuard.cs)。

存档格式 = "BB1:" + HMAC-SHA256(十六进制) + ":" + 明文 JSON。
直接改 JSON 会导致验签失败,游戏读档时回全新状态(等于清档)。

用法:
  python3 tools/save/save_edit.py unpack   # 可选:去掉签名,把 save.json 展开成好读的 JSON
  (手动改 save.json,例如 "Ink": 99999)
  python3 tools/save/save_edit.py          # 重新签名,游戏即可正常读取
  python3 tools/save/save_edit.py check    # 只检查签名是否有效

  --path <文件>  指定存档(默认 macOS 编辑器下的 persistentDataPath)
每次写入前都会在同目录留一份 save.json.bak-<时间戳>。改档时请先退出 Play 模式,
否则游戏下一次保存会覆盖你的修改。
"""
import argparse
import hashlib
import hmac
import json
import shutil
import sys
import time
from pathlib import Path

HEADER = "BB1:"
KEY = b"brushblade-zidou-2026-v1"  # 与 SaveGuard.Key 一致
DEFAULT_PATH = Path.home() / "Library/Application Support/Eugene Wu/Brushblade/save.json"


def sign(payload: str) -> str:
    return hmac.new(KEY, payload.encode("utf-8"), hashlib.sha256).hexdigest()


def seal(payload: str) -> str:
    return HEADER + sign(payload) + ":" + payload


def strip(text: str) -> str:
    """取出 JSON 正文:带签名的去掉签名(不验签),纯 JSON 原样返回。"""
    if text.startswith(HEADER):
        sig_end = text.find(":", len(HEADER))
        if sig_end < 0:
            raise ValueError("存档头格式非法")
        return text[sig_end + 1:]
    return text


def is_valid(text: str) -> bool:
    if not text.startswith(HEADER):
        return False
    sig_end = text.find(":", len(HEADER))
    return sig_end >= 0 and hmac.compare_digest(text[len(HEADER):sig_end], sign(text[sig_end + 1:]))


def backup(path: Path) -> Path:
    bak = path.with_name(path.name + ".bak-" + time.strftime("%Y%m%d-%H%M%S"))
    shutil.copy2(path, bak)
    return bak


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description="save.json 签名工具")
    ap.add_argument("command", nargs="?", default="seal", choices=["seal", "unpack", "check"])
    ap.add_argument("--path", type=Path, default=DEFAULT_PATH)
    args = ap.parse_args(argv)

    path: Path = args.path
    if not path.exists():
        print(f"找不到存档:{path}", file=sys.stderr)
        return 1
    text = path.read_text(encoding="utf-8-sig")

    if args.command == "check":
        ok = is_valid(text)
        print("签名有效" if ok else "签名无效(游戏会当作清档)")
        return 0 if ok else 2

    try:
        data = json.loads(strip(text))
    except (ValueError, json.JSONDecodeError) as e:
        print(f"JSON 语法错误,未写入:{e}", file=sys.stderr)
        return 1

    if args.command == "unpack":
        out = json.dumps(data, ensure_ascii=False, indent=2) + "\n"
    else:
        out = seal(json.dumps(data, ensure_ascii=False, separators=(",", ":")))

    bak = backup(path)
    path.write_text(out, encoding="utf-8")
    print(f"{'已展开' if args.command == 'unpack' else '已签名'}:{path}\n备份:{bak.name}")
    if isinstance(data, dict) and "Ink" in data:
        print(f"Ink = {data['Ink']}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
