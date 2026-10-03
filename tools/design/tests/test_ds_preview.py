"""设计稿本地预览(docs/design/_preview/)的对账。

规范 / 组件 / 成品三层的 preview.html 在 artifact 上由 Design System 类型注入
tokens 与 bundle.css;本地直接打开时两样都没有,整张卡变成裸 HTML。
build_ds_preview.py 补这层外壳。
"""
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import build_ds_preview as b


def test_every_preview_has_a_local_page():
    """漏一张就是那张卡本地还是裸 HTML。"""
    pages = b.pages()
    for prev in b.sources():
        assert prev.parent.name in pages, f"{prev.parent.name} 没生成本地页"


def test_spec_layer_holds_no_components_or_screens():
    """规范层只放规则(README.md)与 token(tokens.json);组件进 component/,整屏现状进 current/,artifact 外壳进 artifact/。"""
    stray = [str(p.relative_to(b.SYSTEM)) for p in b.SYSTEM.rglob("*") if p.is_file()
             and p.name not in ("README.md", "tokens.json")]
    assert not stray, f"system/ 里混进了规范以外的东西:{stray}"
    assert not list(b.COMPONENT.glob("*Screen")), "整屏卡应在 current/"


def test_every_css_var_used_is_defined():
    """var(--x) 没定义,浏览器静默回落成初始值 —— 正是「本地和线上差很多」的那种差。"""
    bundle = b.BUNDLE.read_text(encoding="utf-8")
    defined = set(re.findall(r"--([a-z0-9-]+)\s*:", b.tokens_css() + bundle))
    for name, html in b.pages().items():
        defined_here = defined | set(re.findall(r"--([a-z0-9-]+)\s*:", html))
        missing = set(re.findall(r"var\(--([a-z0-9-]+)", html + bundle)) - defined_here
        assert not missing, f"{name} 用到未定义的变量 {sorted(missing)}"


def test_local_page_links_tokens_and_bundle_inside_head():
    for name, html in b.pages().items():
        head = html[: html.index("</head>")]
        assert 'href="tokens.css"' in head and 'href="../component/bundle.css"' in head, name


def test_linked_files_exist():
    assert (b.OUT / "../component/bundle.css").resolve() == b.BUNDLE.resolve()
    for url in re.findall(r"url\(\"([^\"]+)\"\)", b.tokens_css()):
        assert (b.OUT / url).resolve().exists(), f"字体 {url} 不存在"


def test_committed_output_is_regenerable():
    """改了 tokens.json 或 preview 却没重跑脚本,本地页会悄悄过期。"""
    expected = {"tokens.css": b.tokens_css(), "index.html": b.index_html()}
    expected.update({f"{n}.html": h for n, h in b.pages().items()})
    expected.update(b.canvas_galleries())
    on_disk = {p.name for p in b.OUT.glob("*") if p.is_file()}
    assert on_disk == set(expected), "本地页集合与生成结果不一致,重跑 build_ds_preview.py"
    for fname, text in expected.items():
        assert (b.OUT / fname).read_text(encoding="utf-8") == text, \
            f"{fname} 过期,重跑 python3 tools/design/build_ds_preview.py"


# ---- 画布 docs/design/drafts/<目录>/*.dc.html ----

def test_every_canvas_dir_has_support_js_pointing_at_runtime():
    """*.dc.html 都写死 <script src="./support.js">;缺了这个文件,本地打开是没跑模板的裸 HTML。"""
    for d in b.canvas_dirs():
        s = d / "support.js"
        assert s.exists(), f"{d.name}/ 缺 support.js"
        assert s.resolve() == b.DC_RUNTIME.resolve(), f"{d.name}/support.js 没指向 {b.DC_RUNTIME.name}"


def test_every_canvas_board_resolves_to_a_file():
    """canvas.json 里登记的画板必须找得到文件。"""
    for d in b.canvas_dirs():
        for board in b.canvas_boards(d):
            assert board["src"].exists(), f"{d.name}: {board['file']} → {board['src']} 不存在"


def test_every_canvas_has_a_gallery_listing_all_boards():
    galleries = b.canvas_galleries()
    for d in b.canvas_dirs():
        page = galleries[f"canvas-{d.name}.html"]
        for board in b.canvas_boards(d):
            assert board["href"] in page, f"canvas-{d.name}.html 漏了 {board['file']}"
    index = b.index_html()
    for name in galleries:
        assert f'href="{name}"' in index, f"index.html 没链到 {name}"
