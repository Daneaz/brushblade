# -*- coding: utf-8 -*-
"""字表描述三处一致的两道网(2026-08-23)。

改一个字的性能要同时落到三处:**实际功能(引擎)**、**`字表功能解析.md` 的描述**、
**UI 卡面详情**。前两者的连接是 `gen_char_doc.py`(从 chars.json 直出文档),
后者是 `CharInfo` + `strings.zh-CN.json`。既有的网只覆盖了引擎那一条边:

- 引擎行为 → Core 单测守着
- `chars.json` 不被手改 → regenerable 测试守着
- 关键数值 → `CharTableTests` 的锁值测试守着
- **文档是否与 chars.json 同步 → 此前无人守**(本文件 test_doc_is_fresh)
- **卡面与文档说的是不是同一回事 → 此前无人守**(本文件 test_ui_and_doc_agree)

两个真实事故促成了这两条:
1. 2026-08-22 发现文档落后于实装数轮 —— 没人记得改表后重跑脚本。
2. 2026-08-23 `gen_char_doc.py` 的输出路径写死在旧位置,文档被移进「字选型/」后
   脚本一直往老地方生成影子文件,而真正在读的那份纹丝不动 —— 这类脱节
   test_doc_is_fresh 会直接逮住(生成的内容进不了被读的那份文件,比对必不一致)。
3. 同日改「Boss 免疫处决」为「Boss 改吃双倍」,卡面文案与文档措辞各写各的,
   要改就得记得改两处 —— test_ui_and_doc_agree 让漏掉任一处都变红。
"""
import json
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
DOC = ROOT / "docs/design/字选型/字表功能解析.md"
GEN = ROOT / "tools/design/gen_char_doc.py"
CHARS = ROOT / "Brushblade/Assets/StreamingAssets/config/chars.json"
STRINGS = ROOT / "Brushblade/Assets/StreamingAssets/config/strings.zh-CN.json"
SCORING = ROOT / "docs/design/字选型/词组计分表.md"


def _normalize(text):
    """去掉每次提交都会变的元数据行,只比内容。

    文档头部印着 `基线提交:<git short HEAD>`,提交一次就变一次 ——
    不归一化的话这条测试在每次 commit 之后都会红,变成纯噪音。
    """
    return "\n".join(l for l in text.splitlines() if "基线提交" not in l)


def test_doc_is_fresh():
    """重跑生成器必须产出与仓库里逐字一致的文档 —— 改了字表就得重跑。

    测试自身无副作用:无论成败都把文件恢复原样。
    """
    before = DOC.read_text(encoding="utf-8")
    try:
        proc = subprocess.run(
            [sys.executable, "tools/design/gen_char_doc.py"],
            cwd=ROOT, capture_output=True, text=True)
        assert proc.returncode == 0, f"生成器跑挂了:\n{proc.stderr}"
        after = DOC.read_text(encoding="utf-8")
    finally:
        DOC.write_text(before, encoding="utf-8")

    assert _normalize(after) == _normalize(before), (
        "字表功能解析.md 与 chars.json 不同步。\n"
        "改了字表(走详表 → export_chars)之后要重跑:\n"
        "    python3 tools/design/gen_char_doc.py")


# 字段 → (卡面模板 key, 两处都必须提到的概念词)
#
# 校的是**概念**不是措辞:文档是紧凑表格、卡面是弹窗,两边句式松紧本就不同
# (文档「斩杀线 25%→直杀」/ 卡面「残血 25% 直接斩杀」),强行同源会牺牲可读性。
# 所以只要求「说的是同一件事」,怎么说随各自语境。
#
# 加新修饰字段时请在这里登记 —— 漏登记不会报错,但那正是这张网的边界,
# 字段本身是否有卡面渲染由 CardFaceCoverageTests 守。
PAIRED_CONCEPTS = {
    "executeKills": ("char.effect.execute.kill", {"Boss", "双倍", "斩杀"}),
    "backline": ("char.effect.backline", {"偷袭", "前排"}),
    "hitCount": ("char.effect.hitcount", {"段"}),
    "summonShield": ("char.effect.summonshield", {"盾"}),
    # pierce 只要「穿透」:文档紧凑表格写「穿透 15」,卡面弹窗写「无视 15 点护甲」——
    # 后者是前者的展开,强求文档复述它就是在拿可读性换一致性
    "pierce": ("char.effect.piercetext", {"穿透"}),
}


def _generator_snippet(field, window=200):
    """取生成器里处理该字段那一段的源码文本(以字段名为中心,前后各取一段)。

    按字段名定位再取窗口,而不是精确解析 —— 精确解析会随代码格式变动而碎,
    而窗口够小不至于把邻近字段的词也算进来(mods 那几行都很短)。

    **前后都要取**:字段名可能出现在整段表达式的末尾,比如
    `f"斩杀线 {…}%" + ("→直杀(Boss 改吃双倍)" if e.get('executeKills') else …)`
    —— 只往后取会把这条的全部描述文字都漏掉,反而抓到下一个字段的。
    """
    src = GEN.read_text(encoding="utf-8")
    # 不含右括号 —— 有的字段写作 e.get('hitCount', 1) 带默认值
    m = re.search(re.escape(f"e.get('{field}'"), src)
    assert m, f"gen_char_doc.py 里找不到对 {field} 的处理"
    return src[max(0, m.start() - window):m.start() + window]


def test_ui_and_doc_agree():
    """卡面模板与文档生成器必须提到同一批概念 —— 改了一处忘了另一处就红。"""
    strings = json.loads(STRINGS.read_text(encoding="utf-8"))
    problems = []
    for field, (key, concepts) in PAIRED_CONCEPTS.items():
        assert key in strings, f"卡面表里没有 {key}"
        ui = strings[key]
        doc = _generator_snippet(field)
        for c in concepts:
            if c not in ui:
                problems.append(f"卡面 {key} 没提到「{c}」:{ui!r}")
            if c not in doc:
                problems.append(f"文档生成器处理 {field} 的那段没提到「{c}」")
    assert not problems, (
        "卡面与文档对同一机制的说法不一致:\n  " + "\n  ".join(problems)
        + "\n两处都要改 —— 或者这个概念本来就不该出现在其中一处,那就改 PAIRED_CONCEPTS。")


def test_execute_thresholds_match_across_three_places():
    """斩杀线的数字在 chars.json / 文档 / 卡面三处必须对得上。

    卡面那侧是模板(`{percent}` 占位),所以校的是「模板确实取了这个值」;
    真正的数字一致性由 chars.json ↔ 文档 这两侧钉死。
    """
    chars = {c["id"]: c for c in json.loads(CHARS.read_text(encoding="utf-8"))["chars"]}
    doc = DOC.read_text(encoding="utf-8")
    strings = json.loads(STRINGS.read_text(encoding="utf-8"))

    for key in ("char.effect.execute.kill", "char.effect.execute.double"):
        assert "{percent}" in strings[key], f"{key} 丢了 {{percent}} 占位符,卡面会印死数字"

    checked = 0
    for cid, c in chars.items():
        for e in c.get("effects", []):
            pct = e.get("executeBelowPercent")
            if not pct:
                continue
            rows = [l for l in doc.splitlines() if l.startswith(f"| {cid} |")]
            assert rows, f"文档里找不到「{cid}」的行"
            for row in rows:
                assert f"{pct}%" in row, (
                    f"「{cid}」的斩杀线在 chars.json 是 {pct}%,文档那行却没有这个数:\n  {row}")
            checked += 1
    # 2026-08-25 字表重构:镰 移出字表,斩杀字从 3 个减到 2 个(铡 直杀 / 剿 双倍)
    assert checked >= 2, f"只校到 {checked} 个斩杀字,预期至少 2 个(铡/剿)"


def test_phrases_match_the_scoring_doc():
    """`gen_char_doc.PHRASES` 必须与《词组计分表》§一 的词表逐条一致。

    词组是白/绿/蓝三档的定档判据(2026-08-25 字表重构),真相在设计文档里;
    生成脚本里那份是可执行副本。两边一漂就会出现「文档说 冷 有 5 条词所以进蓝档」
    而《字表功能解析》的词组列只列 4 条 —— 读表的人无从判断哪边错。

    ⚠ 这里**解析源码文本**而不是 import gen_char_doc:那个模块在导入期就会读 chars.json
    并把文档写回磁盘(全部逻辑在模块级),import 一下就等于跑了一次生成器。
    """
    src = GEN.read_text(encoding="utf-8")
    block = src.split("PHRASES = [", 1)[1].split("]", 1)[0]
    in_code = re.findall(r"'([^']+)'", block)

    body = SCORING.read_text(encoding="utf-8").split("## 一 · 词表")[1].split("## 二 ·")[0]
    # 词表行形如 `| 焦灼 | 焦 + 灼 |`,首格恰好两个字
    in_doc = [row.split("|")[1].strip() for row in body.splitlines()
              if row.startswith("| ") and not row.startswith("|---")
              and len(row.split("|")[1].strip()) == 2]

    assert in_doc, "没从《词组计分表》§一 解析出任何词 —— 表格格式变了,先修解析"
    assert sorted(in_doc) == sorted(in_code), (
        "《词组计分表》与 gen_char_doc.PHRASES 不一致:\n"
        f"  只在文档里:{sorted(set(in_doc) - set(in_code))}\n"
        f"  只在脚本里:{sorted(set(in_code) - set(in_doc))}")
