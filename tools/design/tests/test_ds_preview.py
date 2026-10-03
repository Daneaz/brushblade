"""设计系统本地预览(docs/design/system/_local/)的对账。

preview.html 在 artifact 上由 Design System 类型注入 tokens 与 bundle.css;
本地直接打开时两样都没有,整张卡变成裸 HTML。build_ds_preview.py 补这层外壳。
"""
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import build_ds_preview as b


def test_every_component_preview_has_a_local_page():
    """漏一张就是那张卡本地还是裸 HTML。"""
    pages = b.pages()
    for prev in b.SYSTEM.glob("components/*/preview.html"):
        assert prev.parent.name in pages, f"{prev.parent.name} 没生成本地页"


def test_every_css_var_used_is_defined():
    """var(--x) 没定义,浏览器静默回落成初始值 —— 正是「本地和线上差很多」的那种差。"""
    defined = set(re.findall(r"--([a-z0-9-]+)\s*:", b.tokens_css()))
    bundle = (b.SYSTEM / "components/bundle.css").read_text(encoding="utf-8")
    defined |= set(re.findall(r"--([a-z0-9-]+)\s*:", bundle))
    for name, html in b.pages().items():
        defined_here = defined | set(re.findall(r"--([a-z0-9-]+)\s*:", html))
        missing = set(re.findall(r"var\(--([a-z0-9-]+)", html + bundle)) - defined_here
        assert not missing, f"{name} 用到未定义的变量 {sorted(missing)}"


def test_local_page_links_tokens_and_bundle_inside_head():
    for name, html in b.pages().items():
        head = html[: html.index("</head>")]
        assert 'href="tokens.css"' in head and 'href="../components/bundle.css"' in head, name


def test_fonts_point_at_files_that_exist():
    for url in re.findall(r"url\(\"([^\"]+)\"\)", b.tokens_css()):
        assert (b.LOCAL / url).resolve().exists(), f"字体 {url} 不存在"


def test_committed_output_is_regenerable():
    """改了 tokens.json 或 preview 却没重跑脚本,本地页会悄悄过期。"""
    expected = {"tokens.css": b.tokens_css(), "index.html": b.index_html()}
    expected.update({f"{n}.html": h for n, h in b.pages().items()})
    on_disk = {p.name for p in b.LOCAL.glob("*") if p.is_file()}
    assert on_disk == set(expected), "本地页集合与生成结果不一致,重跑 build_ds_preview.py"
    for fname, text in expected.items():
        assert (b.LOCAL / fname).read_text(encoding="utf-8") == text, \
            f"{fname} 过期,重跑 python3 tools/design/build_ds_preview.py"
