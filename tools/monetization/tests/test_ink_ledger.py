"""墨锭账目对账:墨锭只能经 MetaRules.GainInk / SpendInk / ApplyInkDelta 变动。

为什么扫源码:角色页的「累计获得 / 花费墨锭」统计挂在这三个函数里。以后新加一个
花钱点、直接写 meta.Ink -= price,编译和单测都照不到,统计就静默漏记。
"""
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
SRC = [ROOT / "Brushblade/Assets/_Project/Core", ROOT / "Brushblade/Assets/_Project/Presentation"]
# 唯一允许直接写 .Ink 的地方:三个入口函数本身所在的文件,且只在它们的函数体里
ALLOWED_FILE = ROOT / "Brushblade/Assets/_Project/Core/Meta.cs"
MUTATION = re.compile(r"\.Ink\s*(\+=|-=|=(?!=))")


def _strip_comments(line: str) -> str:
    return line.split("//", 1)[0]


def test_no_raw_ink_mutation():
    offenders = []
    for root in SRC:
        for path in root.rglob("*.cs"):
            for no, raw in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
                line = _strip_comments(raw)
                if not MUTATION.search(line):
                    continue
                if path == ALLOWED_FILE and "INK-LEDGER" in raw:
                    continue
                offenders.append(f"{path.relative_to(ROOT)}:{no}: {raw.strip()}")
    assert not offenders, "墨锭必须经 MetaRules.GainInk/SpendInk/ApplyInkDelta:\n" + "\n".join(offenders)


def test_allowed_marker_used_exactly_twice():
    text = ALLOWED_FILE.read_text(encoding="utf-8")
    assert text.count("INK-LEDGER") == 2, "GainInk 与 SpendInk 各一处 INK-LEDGER 标记"
