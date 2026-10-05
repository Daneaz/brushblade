"""设计稿本地预览:规范 / 组件 / 现状三层的 preview.html → docs/design/_preview/。

- 规范 docs/design/system/(tokens.json);封面卡 docs/design/artifact/Cover
- 组件 docs/design/component/<名>/preview.html(共用样式 component/bundle.css)
- 现状 docs/design/current/<名>/preview.html

preview.html 在 artifact 上由 Design System 类型注入 tokens 与 bundle.css,
本地直接打开两样都没有。这里补上那层外壳:
- tokens.css:tokens.json 的全部 token 转成 :root 变量 + 字体 @font-face + 字样类
- <名>.html:preview 原文,只在 <head> 里插两条 <link>
- index.html:按目录顺序(artifact → component → current → drafts → demos)铺开全部卡并链到画布与 demos,标题用英文目录名
- canvas-<目录>.html:设计稿画布 docs/design/drafts/<目录>/ 按 canvas.json 的分页与标题铺开全部画板

画布的 *.dc.html 写死 <script src="./support.js">,线上由 Design 类型提供。本地由
docs/design/drafts/dc-runtime.js(从 Design 类型取下的运行时,原样)顶上:每个画布目录放一个
指向它的软链接 support.js,源稿不改,直接打开即可。

改了 tokens.json 或任何 preview 后重跑:python3 tools/design/build_ds_preview.py
"""
import html
import os
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
DESIGN = ROOT / "docs/design"
SYSTEM = DESIGN / "system"
ARTIFACT = DESIGN / "artifact"
COMPONENT = DESIGN / "component"
CURRENT = DESIGN / "current"
BUNDLE = COMPONENT / "bundle.css"
OUT = DESIGN / "_preview"
DRAFTS = DESIGN / "drafts"
DC_RUNTIME = DRAFTS / "dc-runtime.js"

_LINKS = '<link rel="stylesheet" href="tokens.css"><link rel="stylesheet" href="../component/bundle.css">'
_CAMEL = {"fontSize": "font-size", "lineHeight": "line-height",
          "fontWeight": "font-weight", "letterSpacing": "letter-spacing"}


def tokens_css() -> str:
    t = json.loads((SYSTEM / "tokens.json").read_text(encoding="utf-8"))
    fam = t["type"]["families"]
    out = ["/* 由 tools/design/build_ds_preview.py 从 tokens.json 生成,勿手改 */"]
    for f in t["type"]["fonts"]:
        out.append(f'@font-face {{ font-family: "{f["family"]}"; src: url("{os.path.relpath(ROOT / f["file"], OUT)}"); '
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
    return (sorted(ARTIFACT.glob("*/preview.html")) + sorted(COMPONENT.glob("*/preview.html"))
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
        rel = f"{d.name}/{a['file']}"
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
            body.append(f"<h2>{html.escape(str(pid))} <small>{html.escape(pages.get(pid, ''))}</small></h2>")
        for b in group:
            # 标题用文件名(英文原名,不翻译);canvas.json 里的中文标题降为副文
            body.append(f'<section><h3>{b["file"]} <small>{html.escape(b["title"])} · {b["w"]}×{b["h"]}</small></h3>'
                        f'<iframe src="{b["href"]}" width="{b["w"]}" height="{b["h"]}"></iframe></section>')
    title = c.get("title") or ""
    return ("<!doctype html><html lang=\"zh-CN\"><head><meta charset=\"utf-8\">"
            f"<title>drafts/{d.name}</title>"
            "<link rel=\"stylesheet\" href=\"tokens.css\"><style>"
            "body{margin:0;padding:16px 24px;background:var(--paper);color:var(--text-main);font-family:var(--font-sans)}"
            "h2{font-family:var(--font-serif);margin:28px 0 8px}h3{font-size:13px;margin:16px 0 6px}"
            "small{font-weight:400;color:var(--text-dim);margin-left:8px}"
            "iframe{display:block;border:1px solid var(--panel-border);border-radius:8px;background:#fff}"
            f"</style></head><body><p><a href=\"index.html\">← index</a></p><h1>drafts/{d.name} <small>{html.escape(title)}</small></h1>"
            f"<p>源稿 docs/design/drafts/{d.name}/;由 tools/design/build_ds_preview.py 生成。画板可交互。</p>"
            + "".join(body) + "</body></html>\n")


def canvas_galleries() -> dict:
    return {f"canvas-{d.name}.html": _canvas_gallery(d) for d in canvas_dirs()}


DEMOS = DESIGN / "demos"


def index_html() -> str:
    """总览按 docs/design/ 的目录顺序排:artifact → component → current → drafts → demos。
    标题一律用目录 / 文件的英文原名,不翻译;@dsCard 的中文分组与副标题降为副文。"""
    body = []
    for layer in (ARTIFACT, COMPONENT, CURRENT):
        ps = sorted(layer.glob("*/preview.html"))
        if not ps:
            continue
        body.append(f'<h2 id="{layer.name}">{layer.name}/</h2>')
        for p in ps:
            meta = _card_meta(p.read_text(encoding="utf-8"))
            name = p.parent.name
            sub = " · ".join(x for x in (meta["group"] if meta["group"] != "其他" else "", meta["subtitle"]) if x)
            body.append(f'<section><h3>{name} <small>{html.escape(sub)}</small></h3>'
                        f'<iframe src="{name}.html" style="height:{meta["height"]}px"></iframe></section>')
    body.append('<h2 id="drafts">drafts/</h2><ul>' + "".join(
        f'<li><a href="canvas-{d.name}.html">{d.name}</a></li>' for d in canvas_dirs()) + "</ul>")
    demo_dirs = sorted(p for p in DEMOS.iterdir() if p.is_dir()) if DEMOS.exists() else []
    if demo_dirs:
        body.append('<h2 id="demos">demos/</h2>')
        for d in demo_dirs:
            files = sorted(d.glob("*.html"))
            if files:
                body.append(f"<h3>{d.name}</h3><ul>" + "".join(
                    f'<li><a href="../demos/{d.name}/{f.name}">{html.escape(f.name)}</a></li>' for f in files) + "</ul>")
    toc = " · ".join(f'<a href="#{n}">{n}/</a>' for n in ("artifact", "component", "current", "drafts", "demos"))
    return ("<!doctype html><html lang=\"zh-CN\"><head><meta charset=\"utf-8\">"
            "<title>docs/design · preview</title>"
            "<link rel=\"stylesheet\" href=\"tokens.css\"><style>"
            "body{margin:0;padding:16px 24px;background:var(--paper);color:var(--text-main);font-family:var(--font-sans)}"
            "h2{font-family:var(--font-sans);margin:28px 0 8px}h3{font-size:13px;margin:16px 0 6px}"
            "small{font-weight:400;color:var(--text-dim);margin-left:8px}"
            "iframe{width:100%;border:1px solid var(--panel-border);border-radius:8px;background:var(--paper)}"
            "</style></head><body><h1>docs/design</h1>"
            "<p>由 tools/design/build_ds_preview.py 生成;规则与 token 见 system/README.md、system/tokens.json。</p>"
            f"<p>{toc}</p>" + "".join(body) + "</body></html>\n")


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
