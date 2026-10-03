"""设计稿本地预览:规范 / 组件 / 现状三层的 preview.html → docs/design/_preview/。

- 规范 docs/design/system/(tokens.json、字体、封面卡 Cover)
- 组件 docs/design/component/<名>/preview.html(共用样式 component/bundle.css)
- 现状 docs/design/current/<名>/preview.html

preview.html 在 artifact 上由 Design System 类型注入 tokens 与 bundle.css,
本地直接打开两样都没有。这里补上那层外壳:
- tokens.css:tokens.json 的全部 token 转成 :root 变量 + 字体 @font-face + 字样类
- <名>.html:preview 原文,只在 <head> 里插两条 <link>
- index.html:按 @dsCard 分组铺开全部卡,并链到各画布画廊
- canvas-<目录>.html:设计稿画布 docs/design/drafts/<目录>/ 按 canvas.json 的分页与标题铺开全部画板

画布的 *.dc.html 写死 <script src="./support.js">,线上由 Design 类型提供。本地由
docs/design/drafts/dc-runtime.js(从 Design 类型取下的运行时,原样)顶上:每个画布目录放一个
指向它的软链接 support.js,源稿不改,直接打开即可。

改了 tokens.json 或任何 preview 后重跑:python3 tools/design/build_ds_preview.py
"""
import html
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
DESIGN = ROOT / "docs/design"
SYSTEM = DESIGN / "system"
COMPONENT = DESIGN / "component"
CURRENT = DESIGN / "current"
BUNDLE = COMPONENT / "bundle.css"
OUT = DESIGN / "_preview"
DRAFTS = DESIGN / "drafts"
DC_RUNTIME = DRAFTS / "dc-runtime.js"
# scenes 画布上的三块升级弹窗只在 levelup/ 存一份(改了名),见 drafts/scenes/README.md
_ALIASES = {("scenes", "LevelUp.dc.html"): "levelup/Main.dc.html",
            ("scenes", "LevelUpMulti.dc.html"): "levelup/MultiLevel.dc.html",
            ("scenes", "LevelUpCapped.dc.html"): "levelup/Capped.dc.html"}

_LINKS = '<link rel="stylesheet" href="tokens.css"><link rel="stylesheet" href="../component/bundle.css">'
_CAMEL = {"fontSize": "font-size", "lineHeight": "line-height",
          "fontWeight": "font-weight", "letterSpacing": "letter-spacing"}


def tokens_css() -> str:
    t = json.loads((SYSTEM / "tokens.json").read_text(encoding="utf-8"))
    fam = t["type"]["families"]
    out = ["/* 由 tools/design/build_ds_preview.py 从 tokens.json 生成,勿手改 */"]
    for f in t["type"]["fonts"]:
        out.append(f'@font-face {{ font-family: "{f["family"]}"; src: url("../system/{f["file"]}"); '
                   f'font-weight: {f["weight"]}; font-style: {f["style"]}; }}')
    out.append(":root {")
    out.append(f"  --font-serif: {fam['serif']};")
    out.append(f"  --font-sans: {fam['sans']};")
    for group in ("color", "spacing", "radius", "shadow", "opacity"):
        for tok in t[group]["tokens"]:
            out.append(f"  --{tok['name']}: {tok['value']};")
    out.append("}")
    for g in t["type"]["groups"]:
        for s in g["styles"]:
            decl = [f"font-family: var(--font-{g['family']})"]
            decl += [f"{css}: {s[k]}" for k, css in _CAMEL.items() if k in s]
            out.append(f".{s['name']} {{ {'; '.join(decl)}; }}")
    return "\n".join(out) + "\n"


def sources():
    return (sorted(SYSTEM.glob("*/preview.html")) + sorted(COMPONENT.glob("*/preview.html"))
            + sorted(CURRENT.glob("*/preview.html")))


def pages() -> dict:
    res = {}
    for p in sources():
        src = p.read_text(encoding="utf-8")
        i = src.index("<head>") + len("<head>")
        res[p.parent.name] = src[:i] + _LINKS + src[i:]
    return res


def _card_meta(src: str) -> dict:
    m = re.match(r"<!--\s*@dsCard(.*?)-->", src)
    attrs = {k: a or b for k, a, b in re.findall(r'(\w+)=(?:"([^"]*)"|(\S+))', m.group(1))} if m else {}
    return {"group": attrs.get("group", "其他"), "height": int(attrs.get("height", 240)),
            "subtitle": attrs.get("subtitle", "")}


def canvas_dirs():
    return sorted(p.parent for p in DRAFTS.glob("*/canvas.json"))


def canvas_boards(d: Path) -> list:
    """canvas.json 有两种写法:artboards 列表(带 page)与 boards 字典 + order。"""
    c = json.loads((d / "canvas.json").read_text(encoding="utf-8"))
    if "artboards" in c:
        raw = [dict(a) for a in c["artboards"]]
    else:
        raw = [dict(c["boards"][f], file=f) for f in c.get("order", c["boards"])]
    out = []
    for a in raw:
        rel = _ALIASES.get((d.name, a["file"]), f"{d.name}/{a['file']}")
        out.append({"file": a["file"], "title": a.get("title") or a["file"], "page": a.get("page"),
                    "w": a.get("w", 932), "h": a.get("h", 430), "src": DRAFTS / rel, "href": f"../drafts/{rel}"})
    return out


def _canvas_gallery(d: Path) -> str:
    c = json.loads((d / "canvas.json").read_text(encoding="utf-8"))
    pages = {p["id"]: p.get("title", p["id"]) for p in c.get("pages", []) if isinstance(p, dict)}
    boards = canvas_boards(d)
    order = list(pages) + sorted({b["page"] for b in boards} - set(pages), key=str)
    body = []
    for pid in order or [None]:
        group = [b for b in boards if b["page"] == pid] if order else boards
        if not group:
            continue
        if pid is not None:
            body.append(f"<h2>{html.escape(pages.get(pid, str(pid)))}</h2>")
        for b in group:
            body.append(f'<section><h3>{html.escape(b["title"])} <small>{b["file"]} · {b["w"]}×{b["h"]}</small></h3>'
                        f'<iframe src="{b["href"]}" width="{b["w"]}" height="{b["h"]}"></iframe></section>')
    title = c.get("title") or d.name
    return ("<!doctype html><html lang=\"zh-CN\"><head><meta charset=\"utf-8\">"
            f"<title>画布 · {html.escape(title)}</title>"
            "<link rel=\"stylesheet\" href=\"tokens.css\"><style>"
            "body{margin:0;padding:16px 24px;background:var(--paper);color:var(--text-main);font-family:var(--font-sans)}"
            "h2{font-family:var(--font-serif);margin:28px 0 8px}h3{font-size:13px;margin:16px 0 6px}"
            "small{font-weight:400;color:var(--text-dim);margin-left:8px}"
            "iframe{display:block;border:1px solid var(--panel-border);border-radius:8px;background:#fff}"
            f"</style></head><body><p><a href=\"index.html\">← 总览</a></p><h1>画布 · {html.escape(title)}</h1>"
            f"<p>源稿 docs/design/drafts/{d.name}/;由 tools/design/build_ds_preview.py 生成。画板可交互。</p>"
            + "".join(body) + "</body></html>\n")


def canvas_galleries() -> dict:
    return {f"canvas-{d.name}.html": _canvas_gallery(d) for d in canvas_dirs()}


def index_html() -> str:
    groups = {}
    for p in sources():
        groups.setdefault(_card_meta(p.read_text(encoding="utf-8"))["group"], []).append(p)
    body = []
    for g, ps in groups.items():
        body.append(f"<h2>{html.escape(g)}</h2>")
        for p in ps:
            meta = _card_meta(p.read_text(encoding="utf-8"))
            name = p.parent.name
            body.append(f'<section><h3>{name} <small>{html.escape(meta["subtitle"])}</small></h3>'
                        f'<iframe src="{name}.html" style="height:{meta["height"]}px"></iframe></section>')
    return ("<!doctype html><html lang=\"zh-CN\"><head><meta charset=\"utf-8\">"
            "<title>字·斗 设计系统 · 本地预览</title>"
            "<link rel=\"stylesheet\" href=\"tokens.css\"><style>"
            "body{margin:0;padding:16px 24px;background:var(--paper);color:var(--text-main);font-family:var(--font-sans)}"
            "h2{font-family:var(--font-serif);margin:28px 0 8px}h3{font-size:13px;margin:16px 0 6px}"
            "small{font-weight:400;color:var(--text-dim);margin-left:8px}"
            "iframe{width:100%;border:1px solid var(--panel-border);border-radius:8px;background:var(--paper)}"
            "</style></head><body><h1>字·斗 设计系统 · 本地预览</h1>"
            "<p>由 tools/design/build_ds_preview.py 生成;规范见 system/README.md,组件见 component/,现状见 current/,设计稿见 drafts/。</p>"
            + "<h2>画布</h2><ul>" + "".join(f'<li><a href="canvas-{d.name}.html">{d.name}</a></li>' for d in canvas_dirs())
            + "</ul>" + "".join(body) + "</body></html>\n")


def main():
    OUT.mkdir(exist_ok=True)
    for f in OUT.glob("*"):
        f.unlink()
    (OUT / "tokens.css").write_text(tokens_css(), encoding="utf-8")
    (OUT / "index.html").write_text(index_html(), encoding="utf-8")
    for name, text in pages().items():
        (OUT / f"{name}.html").write_text(text, encoding="utf-8")
    for name, text in canvas_galleries().items():
        (OUT / name).write_text(text, encoding="utf-8")
    print(f"wrote {len(pages())} pages → {OUT.relative_to(ROOT)}")


if __name__ == "__main__":
    main()
