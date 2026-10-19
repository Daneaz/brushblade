#!/usr/bin/env python3
"""字怪分层 SVG → PNG:把 Claude Design 出的分层稿光栅化进 Unity。

为什么要转 PNG 而不是直接用 SVG:这些稿子的水墨质感全靠 SVG 滤镜
(feTurbulence / feDisplacementMap / mask),而 Unity 的 Vector Graphics 包不支持滤镜——
直接导入会把墨色浓淡、边缘毛糙、飞白全丢掉。离线用 librsvg 渲染则完整保留。

用法:
    python3 tools/design/rasterize_mobs.py           # 全量
    python3 tools/design/rasterize_mobs.py --check   # 只报告缺什么,不写文件

前置: rsvg-convert(macOS: brew install librsvg)。
输入: tools/design/mobs/svg/*.svg(Claude Design 项目 assets/ 的镜像;光栅化前按系换色,规则见 recolor)
产出: Brushblade/Assets/_Project/Presentation/Mobs/Resources/*.png
"""
import argparse
import json
import re
import shutil
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SVG_DIR = ROOT / "tools/design/mobs/svg"
OUT_DIR = ROOT / "Brushblade/Assets/_Project/Presentation/Mobs/Resources"
ENEMIES = ROOT / "Brushblade/Assets/StreamingAssets/config/enemies.json"
SIZE = 512

# 层序 = 叠放次序(先画的在下)。body 之下没有独立 aura 层——
# 设计把属性气场并进了 wisp(见 enemy_cuozigui_wisp.svg 的 radialGradient)。
LAYERS = ("body", "face", "wisp", "state")

# 战斗代码里 EnemyDef.Id 是中文,资产名是拼音 —— 这张表是唯一的对照来源。
# 与 tools/fonts/glyph_refs.py 的 slug 保持一致。
MINION_SLUGS = {
    "错字鬼": "cuozigui",
    "缺笔妖": "quebiyao",
    "标点小妖": "biaodianxiaoyao",
    "叠字怪": "dieziguai",
    "夯土妖": "hangtuyao",
    "通假字": "tongjiazi",
    "生僻字": "shengpizi",
    "墨渍": "mozi",
    "焦痕": "jiaohen",
    # 2026-08-29 补的五只(六系各补到 3 只)。灯花/墨溅/悬针/败笔 四只旧怪仍无立绘
    "涂改": "tugai",
    "铁画": "tiehua",
    "镇纸": "zhenzhi",
    "洇痕": "yinhen",
    "衍文": "yanwen",
    # 2026-09-03 补的十一只:四只旧欠账(灯花/墨溅/悬针/败笔)+ 七只护甲怪。
    # 至此 enemies.json 的 25 只杂兵全部有立绘,不再有回落到字牌格的怪。
    "灯花": "denghua",
    "墨溅": "mojian",
    "悬针": "xuanzhen",
    "败笔": "baibi",
    "枯笔": "kubi",
    "火漆": "huoqi",
    "砚台": "yantai",
    "铜钤": "tongqian",
    "版牍": "bandu",
    "窑变": "yaobian",
    "宿墨": "sumo",
    # 2026-09-30 十层一主题补的七只(合并稿由 build_mob_drafts.py 生成)
    "炭笔": "tanbi",
    "拓片": "tapian",
    "印泥": "yinni",
    "刻刀": "kedao",
    "铭文": "mingwen",
    "泼墨": "pomo",
    "晕染": "yunran",
}

# Boss 不走这里(2026-09-30):成语 Boss 一只一张「讹熔」立绘,由 build_boss_art.py 直接生成分层 PNG。

# ---------------------------------------------------------------- 光栅化时的换色规则
# 源图 mobs/svg/ 保持 Claude Design 合并稿拆出来的原样(生成它的 build_mob_drafts.py / split_layers.py
# 已于 2026-10-03 退役),换色只在这里按规则做、在内存里改,不回写 SVG —— 规则可重跑、可对账。
#
# 2026-10-19 五行配色金土分家(docs/design/drafts/wuxing-palette/README.md「立绘」节):
# 1. 小怪字身按系着色,与双字头领的一深一亮同一推导(MobView.InitLeaderRig):
#    浅墨笔(第一个字形 mask 组,原 #3D4E69)→ 本系字形色 Theme.GlyphColor;
#    墨字(第二个字形 mask 组,原 #111622)→ Lerp(GlyphColor, Theme.Ink, 0.35)。只动木火土金水五系,心系不动。
# 2. 金、土的属性色板(虹膜渐变 亮 / 主 / 深、气场、配饰)换成 tokens.json 的 *-soft / 主色 / *-fg。
#    合并稿里金、土仍是更早的旧值(#B3A382 / #997C3C 一套),木水火不动。
INK = "#11161F"            # Theme.Ink / tokens.json ink
GLYPH_DEEPEN = 0.35        # 同 MobView.LeaderDeepen
GLYPH = {                  # Theme.GlyphColor / tokens.json *-glyph
    "Wood": "#1F6333", "Fire": "#B02D2E", "Earth": "#6A2B22",
    "Metal": "#8F5C00", "Water": "#0A5E9E",
}
PALETTE_SWAP = {           # 合并稿旧色(小写) → tokens.json
    "Metal": {"#d8cba9": "#F7E7AC", "#d4c9a8": "#F7E7AC", "#d6c9ad": "#F7E7AC",   # 亮 → metal-soft
              "#b3a382": "#916F0C",                                              # 主 → metal
              "#6b6049": "#5B4500", "#6e5f3f": "#5B4500"},                       # 深 → metal-fg
    "Earth": {"#c4a86a": "#E3D1C1", "#c2a366": "#E3D1C1",                        # 亮 → earth-soft
              "#997c3c": "#784330",                                              # 主 → earth
              "#5c4a22": "#542A1D"},                                             # 深 → earth-fg
}
# 字形两层:<g mask="url(#L0_sm…)"><path … fill="…"/> 是浅墨笔,紧跟的 L0_wt… 组是墨字(32 只同构)
_GLYPH_GROUP = re.compile(r'(<g mask="url\(#L0_\w+\)">\s*<path [^>]*?fill=")(#[0-9A-Fa-f]{6})(")')


def _lerp_hex(a, b, t):
    ca = [int(a[i:i + 2], 16) for i in (1, 3, 5)]
    cb = [int(b[i:i + 2], 16) for i in (1, 3, 5)]
    return "#" + "".join(f"{round(x + (y - x) * t):02X}" for x, y in zip(ca, cb))


def glyph_tints(element):
    """(浅墨笔, 墨字) 两色;非五行(心系)返回 None。"""
    light = GLYPH.get(element)
    return None if light is None else (light, _lerp_hex(light, INK, GLYPH_DEEPEN))


def recolor(svg: str, element, layer: str) -> str:
    """按上面两条规则给一层 SVG 换色。无需换色时原样返回。"""
    swap = PALETTE_SWAP.get(element)
    if swap:
        svg = re.sub(r"#[0-9A-Fa-f]{6}\b", lambda m: swap.get(m.group(0).lower(), m.group(0)), svg)
    tints = glyph_tints(element)
    if layer == "body" and tints:
        groups = list(_GLYPH_GROUP.finditer(svg))
        if len(groups) != 2:
            raise ValueError(f"body 层应有 2 个字形 mask 组(浅墨笔 + 墨字),实有 {len(groups)}")
        it = iter(tints)
        svg = _GLYPH_GROUP.sub(lambda m: m.group(1) + next(it) + m.group(3), svg)
    return svg


def minion_elements():
    """slug → 属性(读 enemies.json,与战斗代码同一份)。"""
    cfg = json.loads(ENEMIES.read_text(encoding="utf-8"))
    by_id = {e["id"]: e.get("element") for e in cfg["enemies"]}
    return {slug: by_id.get(cid) for cid, slug in MINION_SLUGS.items()}


def expected_prefixes():
    """全部杂兵资产前缀(不含层后缀)。"""
    return [f"enemy_{slug}" for slug in MINION_SLUGS.values()]


def present_layers(prefix):
    """该前缀在 svg 目录里实际有哪些层(state 只有部分怪有)。"""
    return [layer for layer in LAYERS if (SVG_DIR / f"{prefix}_{layer}.svg").exists()]


def rasterize(svg: Path, png: Path, element=None, layer=""):
    """换色(见 recolor)后经 stdin 交给 rsvg-convert;源 SVG 不改。"""
    src = recolor(svg.read_text(encoding="utf-8"), element, layer)
    subprocess.run(
        ["rsvg-convert", "-w", str(SIZE), "-h", str(SIZE), "-o", str(png)],
        input=src.encode("utf-8"), check=True)


def main():
    parser = argparse.ArgumentParser(description="字怪分层 SVG → PNG")
    parser.add_argument("--check", action="store_true", help="只报告缺什么,不写文件")
    args = parser.parse_args()

    prefixes = expected_prefixes()
    missing = [p for p in prefixes if not present_layers(p)]
    have = [p for p in prefixes if present_layers(p)]

    print(f"资产前缀 {len(prefixes)} 个:已有 {len(have)},缺 {len(missing)}")
    if missing:
        print("  缺:" + "、".join(missing))
    if args.check:
        return 0

    if shutil.which("rsvg-convert") is None:
        print("需要 rsvg-convert(macOS: brew install librsvg)", file=sys.stderr)
        return 1

    OUT_DIR.mkdir(parents=True, exist_ok=True)
    elements = minion_elements()
    count = 0
    for prefix in have:
        element = elements[prefix.removeprefix("enemy_")]
        for layer in present_layers(prefix):
            rasterize(SVG_DIR / f"{prefix}_{layer}.svg", OUT_DIR / f"{prefix}_{layer}.png", element, layer)
            count += 1
    print(f"光栅化 {count} 张 → {OUT_DIR.relative_to(ROOT)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
