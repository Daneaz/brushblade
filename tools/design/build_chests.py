#!/usr/bin/env python3
"""七档宝箱立绘:手写 SVG → PNG,放进 Unity Resources。

用法: python3 tools/design/build_chests.py
前置: rsvg-convert(macOS: brew install librsvg)——与 tools/icons/build_icons.py 同款。

底稿是 `docs/design/ui/scenes/Chests.dc.html`(七档宝箱立绘)。那一页自己写着
「本页的 SVG 可以直接当素材用 —— 箱子是器物不是活物,矢量墨线画得住,还省一套 512 方图」,
所以这里**逐字抄稿上的 path**,只把 `var(--c)` 换成 Theme.ChestColor 的实色。
改稿就重抄一遍,别在这里手改坐标 —— 手改必然和稿漂开(与 build_icons.py 的 nav_* 同一条戒律)。

分层(与 MobAssets 同构,但只两层 —— 箱子不需要 face/wisp):
  body  底图,三态共用。未开始满不透明、计时中压到 45%,一张图两用
  seam  盖缝透出来的光,只有「已就绪」点亮。缝的 y 各档不同,所以必须逐档出

另有两张与箱型无关的叠加层,套在任何一只箱上都成立:
  chest_fx_ready    金光晕 + 七道光芒 + 两颗星(已就绪)
  chest_fx_timing   沙漏角标(计时中)
"""
import shutil
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUT_DIR = ROOT / "Brushblade/Assets/_Project/Presentation/Chests/Resources"
SVG_DIR = Path(__file__).parent / "chests/svg"
CANVAS = 256      # 主界面显示 40pt ≈ 84 逻辑单位,256 留够高 DPI 余量
VIEWBOX = 120     # 稿上的坐标空间,path 逐字照抄,一个数不改

# 档位 slug ↔ Theme.ChestColor 的实色。顺序 = ChestTier 的 1~7,
# C# 侧 ChestAssets.Slugs 必须与它逐个对应(test_chests.py 守着)。
TIERS = {
    "paper":     "#A19E98",  # 素纸匣
    "bamboo":    "#2E9E52",  # 竹简匣
    "celadon":   "#0F74C4",  # 青瓷匣
    "rosewood":  "#7945AB",  # 紫檀匣
    "gilded":    "#C9A94A",  # 鎏金匣
    "vermilion": "#D4602A",  # 朱漆匣
    "crimson":   "#CD262E",  # 赤霄匣
}

# 各档的**盖缝**位置。逐条对着 BODIES 里那条盖沿线量出来的:
# 纸/竹是函套盖的下沿,瓷/金/漆/铁是稿上已经画出的那道 `h` 线,檀是盖与身的交界。
SEAMS = {
    "paper":     "M26 45 h68",
    "bamboo":    "M26 44 h68",
    "celadon":   "M28 58 h64",
    "rosewood":  "M26 45 h68",
    "gilded":    "M26 50 h68",
    "vermilion": "M25 58 h70",
    "crimson":   "M24 58 h72",
}

# ---- 七只箱的主体,逐字取自 Chests.dc.html 的 <symbol id="chest-*"> ----
# 共用画法:墨线 #111622(2.6 主体 / 1.8 细节)、纸底 #FFFDF7,
# 属性色只做**大面积平涂的那一块**(盖或身)—— 40px 缩略图里认色全靠它。
BODIES = {
    # ① 素纸匣:纸函套 + 麻绳十字 + 题签。最朴素,毛边与书口是它唯一的「工」
    "paper": (
        '<ellipse cx="60" cy="102" rx="33" ry="4" fill="#111622" opacity=".10"/>'
        '<path d="M26 30 h68 v62 h-68 z" fill="#FFFDF7" stroke="#111622" stroke-width="2.6" stroke-linejoin="round"/>'
        '<path d="M26 30 h68 v15 h-68 z" fill="{c}" fill-opacity=".55" stroke="#111622" stroke-width="2"/>'
        '<path d="M82 50 v36 M86 50 v36 M90 50 v36" stroke="#C6BCA8" stroke-width="1.6" stroke-linecap="round"/>'
        '<rect x="32" y="52" width="17" height="30" rx="1.5" fill="#F6F1E7" stroke="#111622" stroke-width="1.8"/>'
        '<path d="M40.5 57 v20" stroke="#111622" stroke-width="1.4" opacity=".4" stroke-dasharray="2 3.5"/>'
        '<path d="M26 64 h68" stroke="#8B7E63" stroke-width="3.4" stroke-linecap="round"/>'
        '<path d="M66 30 v62" stroke="#8B7E63" stroke-width="3.4" stroke-linecap="round"/>'
        '<circle cx="66" cy="64" r="4.6" fill="#EFE7D2" stroke="#111622" stroke-width="1.8"/>'
        '<path d="M26 92 q5.7 4 11.3 0 t11.3 0 t11.3 0 t11.3 0 t11.3 0 t11.5 0" fill="none"'
        ' stroke="#111622" stroke-width="2.2" stroke-linecap="round"/>'
    ),
    # ② 竹简匣:竹片竖排 + 两道编绳。轮廓最「瘦」,一眼与纸函分得开
    "bamboo": (
        '<ellipse cx="60" cy="102" rx="32" ry="4" fill="#111622" opacity=".10"/>'
        '<path d="M26 32 h68 v60 h-68 z" fill="{c}" fill-opacity=".30" stroke="#111622" stroke-width="2.6"/>'
        '<path d="M26 32 h68 v12 h-68 z" fill="{c}" fill-opacity=".72" stroke="#111622" stroke-width="2"/>'
        '<path d="M34.5 44 v48 M43 44 v48 M51.5 44 v48 M60 44 v48 M68.5 44 v48 M77 44 v48 M85.5 44 v48"'
        ' stroke="#111622" stroke-width="1.5" opacity=".34"/>'
        '<path d="M30 60 h8 M47 60 h8 M64 60 h8 M81 60 h8" stroke="#111622" stroke-width="1.5" opacity=".3"/>'
        '<path d="M22 54 h76 M22 80 h76" stroke="#6B5B3E" stroke-width="3.6" stroke-linecap="round"/>'
        '<path d="M22 54 h76 M22 80 h76" stroke="#111622" stroke-width="1" opacity=".35"/>'
        '<circle cx="60" cy="80" r="4.4" fill="#EFE7D2" stroke="#111622" stroke-width="1.8"/>'
    ),
    # ③ 青瓷匣:半圆盖 + 冰裂纹 + 圈足。唯一的圆顶轮廓
    "celadon": (
        '<ellipse cx="60" cy="102" rx="31" ry="4" fill="#111622" opacity=".10"/>'
        '<path d="M28 58 h64 v26 a7 7 0 0 1 -7 7 h-50 a7 7 0 0 1 -7 -7 z"'
        ' fill="{c}" fill-opacity=".13" stroke="#111622" stroke-width="2.6" stroke-linejoin="round"/>'
        '<path d="M28 58 a32 26 0 0 1 64 0 z" fill="{c}" fill-opacity=".52" stroke="#111622" stroke-width="2.6"/>'
        '<path d="M52 32 h16 v5 h-16 z" fill="{c}" fill-opacity=".6" stroke="#111622" stroke-width="1.8"/>'
        '<path d="M40 56 l6 -14 M56 50 l-4 -12 M72 56 l-3 -16 M84 54 l-8 -9" stroke="#111622"'
        ' stroke-width="1.2" opacity=".28" fill="none"/>'
        '<path d="M38 66 l9 8 M62 64 l-6 12 M76 68 l8 6" stroke="#111622" stroke-width="1.2" opacity=".24" fill="none"/>'
        '<path d="M28 58 h64" stroke="#111622" stroke-width="2.2"/>'
        '<path d="M36 91 h48" stroke="#111622" stroke-width="2.6" stroke-linecap="round"/>'
        '<path d="M40 50 a24 20 0 0 1 12 -14" fill="none" stroke="#FFFFFF" stroke-width="3.4"'
        ' stroke-linecap="round" opacity=".75"/>'
    ),
    # ④ 紫檀匣:硬木 + 四角包角 + 云头扣。第一只「带五金」的箱
    "rosewood": (
        '<ellipse cx="60" cy="102" rx="34" ry="4" fill="#111622" opacity=".10"/>'
        '<rect x="26" y="44" width="68" height="48" rx="3" fill="{c}" fill-opacity=".30"'
        ' stroke="#111622" stroke-width="2.6"/>'
        '<rect x="22" y="30" width="76" height="15" rx="3" fill="{c}" fill-opacity=".64"'
        ' stroke="#111622" stroke-width="2.6"/>'
        '<path d="M32 56 q14 5 28 0 t28 0 M32 68 q14 -5 28 0 t28 0 M32 80 q14 5 28 0 t28 0"'
        ' fill="none" stroke="#111622" stroke-width="1.3" opacity=".26"/>'
        '<path d="M26 54 v-10 h10 M94 54 v-10 h-10 M26 82 v10 h10 M94 82 v10 h-10"'
        ' fill="none" stroke="#8A7B5A" stroke-width="3" stroke-linejoin="round"/>'
        '<path d="M52 45 h16 v11 a8 8 0 0 1 -16 0 z" fill="#C9A94A" stroke="#111622" stroke-width="1.9"/>'
        '<circle cx="60" cy="53" r="2.2" fill="#111622"/>'
    ),
    # ⑤ 鎏金匣:盝顶盖 + 錾花 + 如意锁牌。轮廓从这一档开始「起脊」
    "gilded": (
        '<ellipse cx="60" cy="102" rx="34" ry="4" fill="#111622" opacity=".10"/>'
        '<rect x="27" y="52" width="66" height="40" rx="2" fill="{c}" fill-opacity=".32"'
        ' stroke="#111622" stroke-width="2.6"/>'
        '<path d="M36 28 h48 l10 22 h-68 z" fill="{c}" fill-opacity=".70"'
        ' stroke="#111622" stroke-width="2.6" stroke-linejoin="round"/>'
        '<path d="M26 50 h68" stroke="#111622" stroke-width="2.4"/>'
        '<circle cx="44" cy="40" r="1.7" fill="#111622" opacity=".45"/>'
        '<circle cx="54" cy="36" r="1.7" fill="#111622" opacity=".45"/>'
        '<circle cx="66" cy="36" r="1.7" fill="#111622" opacity=".45"/>'
        '<circle cx="76" cy="40" r="1.7" fill="#111622" opacity=".45"/>'
        '<path d="M53 52 h14 v13 a7 7 0 0 1 -14 0 z" fill="#F6EDD5" stroke="#111622" stroke-width="1.9"/>'
        '<path d="M60 58 v5" stroke="#111622" stroke-width="1.8" stroke-linecap="round"/>'
        '<circle cx="60" cy="56" r="2.4" fill="#111622"/>'
        '<path d="M33 84 h54" stroke="#C9A94A" stroke-width="2.4" opacity=".9"/>'
        '<path d="M27 92 h66" stroke="#111622" stroke-width="2.6" stroke-linecap="round"/>'
    ),
    # ⑥ 朱漆匣:剔红卷草 + 铜包角。圆角最饱满,漆面用一层高光带出「厚」
    "vermilion": (
        '<ellipse cx="60" cy="102" rx="34" ry="4" fill="#111622" opacity=".10"/>'
        '<rect x="25" y="36" width="70" height="56" rx="8" fill="{c}" fill-opacity=".50"'
        ' stroke="#111622" stroke-width="2.8"/>'
        '<path d="M25 58 h70" stroke="#111622" stroke-width="2.4"/>'
        '<path d="M32 50 c8 -8 18 -2 22 -8 c4 -6 14 -6 18 1 c3 5 10 5 13 1" fill="none" stroke="#111622"'
        ' stroke-width="1.8" opacity=".42" stroke-linecap="round"/>'
        '<path d="M44 46 c-3 -4 -8 -3 -8 1 c0 3 4 4 5 1" fill="none" stroke="#111622"'
        ' stroke-width="1.6" opacity=".38" stroke-linecap="round"/>'
        '<path d="M30 74 c9 -7 19 -1 25 -6 c6 -5 16 -3 20 3 c3 4 9 4 12 1" fill="none" stroke="#111622"'
        ' stroke-width="1.6" opacity=".28" stroke-linecap="round"/>'
        '<path d="M25 48 h12 M83 48 h12" stroke="#C9A94A" stroke-width="3" stroke-linecap="round"/>'
        '<path d="M25 82 h12 M83 82 h12" stroke="#C9A94A" stroke-width="3" stroke-linecap="round"/>'
        '<rect x="53" y="52" width="14" height="12" rx="2.5" fill="#C9A94A" stroke="#111622" stroke-width="1.9"/>'
        '<circle cx="60" cy="58" r="2.2" fill="#111622"/>'
        '<path d="M33 42 q10 -4 20 -1" fill="none" stroke="#FFFFFF" stroke-width="3" opacity=".38" stroke-linecap="round"/>'
    ),
    # ⑦ 赤霄匣:铁函 + 交叉封符 + 缝里透出来的光。全表唯一「关不住」的一只。
    # 封符裁进箱体只留 3px 折边 —— 不裁的话交叉那两道会甩出箱外,与另外六只的画幅对不齐。
    "crimson": (
        '<defs><clipPath id="clip-crimson"><rect x="21" y="31" width="78" height="64" rx="2"/></clipPath></defs>'
        '<ellipse cx="60" cy="102" rx="35" ry="4" fill="#111622" opacity=".10"/>'
        '<rect x="24" y="34" width="72" height="58" rx="2" fill="{c}" fill-opacity=".42"'
        ' stroke="#111622" stroke-width="3"/>'
        '<path d="M24 58 h72" stroke="#FFD470" stroke-width="6" opacity=".6"/>'
        '<path d="M24 58 h72" stroke="#FFF3CF" stroke-width="2.4"/>'
        '<path d="M24 56 h72" stroke="#111622" stroke-width="2.2"/>'
        '<g clip-path="url(#clip-crimson)">'
        '<path d="M22 28 l76 74" stroke="#F6F1E7" stroke-width="9"/>'
        '<path d="M22 28 l76 74" stroke="#111622" stroke-width="1.6" opacity=".5" fill="none"/>'
        '<path d="M98 28 l-76 74" stroke="#F6F1E7" stroke-width="9"/>'
        '<path d="M98 28 l-76 74" stroke="#111622" stroke-width="1.6" opacity=".5" fill="none"/>'
        '</g>'
        '<rect x="50" y="47" width="20" height="20" rx="2" fill="#9B1E22" stroke="#111622" stroke-width="2"/>'
        '<path d="M55 53 h10 M55 57 h10 M55 61 h10" stroke="#F6F1E7" stroke-width="1.6" opacity=".85"/>'
        '<path d="M24 92 h72" stroke="#111622" stroke-width="3" stroke-linecap="round"/>'
    ),
}

# ---- 与箱型无关的两张叠加层(稿上的 #fx-ready 与 #ic-glass)----
EFFECTS = {
    # 已就绪:金光晕 + 七道光芒 + 两颗星。C# 侧整层做 .45↔1 的呼吸
    "fx_ready": (
        '<defs><radialGradient id="g-ready" cx="50%" cy="50%" r="50%">'
        '<stop offset="0%" stop-color="#FFD470" stop-opacity=".9"/>'
        '<stop offset="52%" stop-color="#FFCF5E" stop-opacity=".38"/>'
        '<stop offset="100%" stop-color="#FFCF5E" stop-opacity="0"/>'
        '</radialGradient></defs>'
        '<ellipse cx="60" cy="60" rx="46" ry="44" fill="url(#g-ready)"/>'
        '<g stroke="#C9A94A" stroke-width="3" stroke-linecap="round">'
        '<path d="M60 20 v-7"/><path d="M33 29 l-5 -6"/><path d="M87 29 l5 -6"/>'
        '<path d="M21 57 h-7"/><path d="M99 57 h7"/>'
        '<path d="M27 85 l-6 5"/><path d="M93 85 l6 5"/>'
        '</g>'
        '<g fill="#FFF3CF" stroke="#C9A94A" stroke-width="1.2">'
        '<path d="M30 38 l2.6 5.4 5.4 2.6 -5.4 2.6 -2.6 5.4 -2.6 -5.4 -5.4 -2.6 5.4 -2.6z"/>'
        '<path d="M92 70 l2.2 4.6 4.6 2.2 -4.6 2.2 -2.2 4.6 -2.2 -4.6 -4.6 -2.2 4.6 -2.2z"/>'
        '</g>'
    ),
    # 计时中:右下角沙漏。稿上是 r=15 的圆底 + 24 见方的 #ic-glass,
    # 这里把沙漏的 32 坐标空间缩放平移到 120 空间里的 (80,78)~(104,102)
    "fx_timing": (
        '<circle cx="92" cy="90" r="15" fill="#EFEADF" stroke="#111622" stroke-width="2.4"/>'
        '<g transform="translate(80 78) scale(0.75)"'
        ' fill="none" stroke="#111622" stroke-width="3" stroke-linecap="round">'
        '<path d="M9 5h14M9 27h14"/>'
        '<path d="M11 5c0 7 5 9 5 11s-5 4-5 11M21 5c0 7-5 9-5 11s5 4 5 11"/>'
        '</g>'
    ),
}

# 盖缝透光的两道笔:宽笔铺金晕,细笔压亮心。稿上 stroke-width 9 / 3.4
SEAM_TEMPLATE = ('<path d="{d}" stroke="#FFD470" stroke-width="9" opacity=".55"/>'
                 '<path d="{d}" stroke="#FFF3CF" stroke-width="3.4"/>')


def svg(body: str) -> str:
    """一张素材的完整 SVG 文本。"""
    return (f'<svg xmlns="http://www.w3.org/2000/svg" width="{VIEWBOX}" height="{VIEWBOX}" '
            f'viewBox="0 0 {VIEWBOX} {VIEWBOX}">{body}</svg>')


def assets() -> dict:
    """资产名 → SVG 文本。资产名 = Unity Resources 里的 key(不含扩展名)。"""
    out = {}
    for slug, color in TIERS.items():
        out[f"chest_{slug}_body"] = svg(BODIES[slug].replace("{c}", color))
        out[f"chest_{slug}_seam"] = svg(SEAM_TEMPLATE.format(d=SEAMS[slug]))
    for key, body in EFFECTS.items():
        out[f"chest_{key}"] = svg(body)
    return out


def main(out_dir: Path = None) -> None:
    out_dir = Path(out_dir) if out_dir else OUT_DIR
    out_dir.mkdir(parents=True, exist_ok=True)
    SVG_DIR.mkdir(parents=True, exist_ok=True)

    built = assets()
    for name, text in built.items():
        (SVG_DIR / f"{name}.svg").write_text(text, encoding="utf-8")

    if shutil.which("rsvg-convert") is None:
        print("跳过 PNG:未找到 rsvg-convert(macOS: brew install librsvg)")
        return

    for name in built:
        subprocess.run(
            ["rsvg-convert", "-w", str(CANVAS), "-h", str(CANVAS),
             str(SVG_DIR / f"{name}.svg"), "-o", str(out_dir / f"{name}.png")],
            check=True)
    print(f"{len(built)} 张宝箱素材 → {out_dir}")


if __name__ == "__main__":
    sys.exit(main())
