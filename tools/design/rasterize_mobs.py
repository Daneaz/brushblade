#!/usr/bin/env python3
"""字怪分层 SVG → PNG:把 Claude Design 出的分层稿光栅化进 Unity。

为什么要转 PNG 而不是直接用 SVG:这些稿子的水墨质感全靠 SVG 滤镜
(feTurbulence / feDisplacementMap / mask),而 Unity 的 Vector Graphics 包不支持滤镜——
直接导入会把墨色浓淡、边缘毛糙、飞白全丢掉。离线用 librsvg 渲染则完整保留。

用法:
    python3 tools/design/rasterize_mobs.py           # 全量
    python3 tools/design/rasterize_mobs.py --check   # 只报告缺什么,不写文件

前置: rsvg-convert(macOS: brew install librsvg)。
输入: tools/design/mobs/svg/*.svg(Claude Design 项目 assets/ 的镜像)
产出: Brushblade/Assets/_Project/Presentation/Mobs/Resources/*.png
"""
import argparse
import shutil
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SVG_DIR = ROOT / "tools/design/mobs/svg"
OUT_DIR = ROOT / "Brushblade/Assets/_Project/Presentation/Mobs/Resources"
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
}

# Boss 形象按阶段出:同一只 Boss 四个阶段是四套图。
# 值 = 每阶段的资产前缀;「倒」「海」两阶段复用排山倒海的稿(设计侧已去重)。
BOSS_STAGES = {
    "排山倒海": [
        "boss_paishandaohai_1pai", "boss_paishandaohai_2shan",
        "boss_paishandaohai_3dao", "boss_paishandaohai_4hai",
    ],
    "翻江倒海": [
        "boss_fanjiangdaohai_1fan", "boss_fanjiangdaohai_2jiang",
        "boss_paishandaohai_3dao", "boss_paishandaohai_4hai",  # ♻ 复用
    ],
    "雷霆万钧": [
        "boss_leitingwanjun_1lei", "boss_leitingwanjun_2ting",
        "boss_leitingwanjun_3wan", "boss_leitingwanjun_4jun",
    ],
}


def expected_prefixes():
    """全部资产前缀(不含层后缀)。Boss 复用阶段去重。"""
    prefixes = [f"enemy_{slug}" for slug in MINION_SLUGS.values()]
    seen = set(prefixes)
    for stages in BOSS_STAGES.values():
        for stage in stages:
            if stage not in seen:
                seen.add(stage)
                prefixes.append(stage)
    return prefixes


def present_layers(prefix):
    """该前缀在 svg 目录里实际有哪些层(state 只有部分怪有)。"""
    return [layer for layer in LAYERS if (SVG_DIR / f"{prefix}_{layer}.svg").exists()]


def rasterize(svg: Path, png: Path):
    subprocess.run(
        ["rsvg-convert", "-w", str(SIZE), "-h", str(SIZE), str(svg), "-o", str(png)],
        check=True)


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
    count = 0
    for prefix in have:
        for layer in present_layers(prefix):
            rasterize(SVG_DIR / f"{prefix}_{layer}.svg", OUT_DIR / f"{prefix}_{layer}.png")
            count += 1
    print(f"光栅化 {count} 张 → {OUT_DIR.relative_to(ROOT)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
