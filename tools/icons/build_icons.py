#!/usr/bin/env python3
"""状态图标:手写 SVG → PNG,放进 Unity Resources。

用法: python3 tools/icons/build_icons.py
前置: rsvg-convert(macOS: brew install librsvg)——与 tools/fonts/glyph_refs.py 同款。

设计约定(spec §5.2):
  - 64×64 画布,内容留 8px 边距(显示 20px,3 倍余量给高 DPI)
  - 图形一律**白色**,底色由 C# 侧 ChipSpec.Bg 上色 —— 一张图跨底色复用,
    也免得改配色就要重出图
  - 只用 path / circle,不用文字:字要走字体子集,那是另一条管线
"""
import shutil
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUT_DIR = ROOT / "Brushblade/Assets/_Project/Presentation/UI/Resources"
SVG_DIR = Path(__file__).parent / "svg"
CANVAS = 64

# 描边类图形的公共属性:圆头圆角,细节在 20px 下才不糊成一团
STROKE = 'fill="none" stroke="#fff" stroke-width="6" stroke-linecap="round" stroke-linejoin="round"'
FILL = 'fill="#fff"'

# 导航图标(nav_*)另一套:路径直接抄 Home.dc.html,那边是 24 的 viewBox,
# 这里只加一层缩放撑到 64,坐标一个不改 —— 手改坐标必然和稿漂开。
# 线宽也照稿的 1.7(在 24 空间里)而不是上面的 6:导航图标显示在 36 逻辑单位上,
# 是状态 chip 图标(18)的两倍大,按 STROKE 那个粗细会糊成一坨黑。
NAV_SCALE = f'transform="scale({CANVAS / 24})"'
NAV_STROKE = 'fill="none" stroke="#fff" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round"'

# key → SVG 内容片段(不含 <svg> 外壳,由 svg() 补)。
# 形状取「一眼能认」而非写实:20px 下能分辨的特征最多两三个。
ICONS = {
    # ---- 敌方 7 ----
    # 灼烧:火焰轮廓(左侧留缺口做出"内焰"的不对称感,避免和水滴撞形)
    "burn": f'<path {FILL} d="M33 5c11 13 17 21 17 30a18 18 0 0 1-36 0c0-6 3-11 7-14-1 5 1 9 4 11 3-9-2-16-5-21 5 1 9-1 13-6z"/>',
    # 不灭:火焰 + 底座横杠(= 不再衰减)
    "burn_nodecay": (
        f'<path {FILL} d="M32 4c9 10 14 17 14 24a15 15 0 0 1-30 0c0-5 2-9 6-11-1 4 1 7 3 9 2-7-2-13-4-17 4 1 7-1 11-5z"/>'
        f'<path {STROKE} d="M16 54h32"/>'
    ),
    # 冻结:六角雪花(三条交叉线)
    "freeze": f'<path {STROKE} d="M32 8v48M11 20l42 24M53 20L11 44"/>',
    # 减速:向下箭头(速度往下掉)
    "slow": f'<path {STROKE} d="M32 10v32M18 30l14 14 14-14"/>',
    # 致盲:眼睛 + 斜杠
    "blind": (
        f'<path {STROKE} d="M8 32c10-13 38-13 48 0-10 13-38 13-48 0z"/>'
        f'<circle cx="32" cy="32" r="6" {FILL}/>'
        f'<path {STROKE} d="M14 50L50 14"/>'
    ),
    # 沉默:三横(言)+ 斜杠
    "silence": (
        f'<path {STROKE} d="M16 20h32M16 32h24M16 44h32"/>'
        f'<path {STROKE} d="M14 50L50 14"/>'
    ),
    # 诅咒:倒三角(向下压)
    "curse": f'<path {FILL} d="M32 52L12 18h40z"/>',

    # ---- 玩家 10 ----
    # 封字:锁
    "seal": (
        f'<path {STROKE} d="M21 30v-8a11 11 0 0 1 22 0v8"/>'
        f'<path {FILL} d="M15 30h34v24H15z"/>'
    ),
    # 免疫:盾 + 勾
    "immunity": (
        f'<path {STROKE} d="M32 7l21 8v18c0 12-10 21-21 25-11-4-21-13-21-25V15z"/>'
        f'<path {STROKE} d="M23 32l7 8 12-14"/>'
    ),
    # 反弹:双向箭头(打进来的照回去)
    "reflect": f'<path {STROKE} d="M10 32h44M22 22L12 32l10 10M42 22l10 10-10 10"/>',
    # 攻击:刀锋
    "attack": (
        f'<path {FILL} d="M52 8l-4 14-26 26-8-8L40 14z"/>'
        f'<path {STROKE} d="M18 46l-8 8"/>'
    ),
    # 战意:旗
    "morale": (
        f'<path {STROKE} d="M18 8v48"/>'
        f'<path {FILL} d="M18 12h30l-8 10 8 10H18z"/>'
    ),
    # 暴击:八角星爆
    "crit": f'<path {FILL} d="M32 4l7 19 19-7-7 19 7 19-19-7-7 19-7-19-19 7 7-19-7-19 19 7z"/>',
    # 穿透:箭穿板
    "pierce": (
        f'<path {STROKE} d="M8 32h42M40 20l12 12-12 12"/>'
        f'<path {STROKE} d="M30 12v40"/>'
    ),
    # 护甲:实心盾
    "defense": f'<path {FILL} d="M32 6l22 8v19c0 13-11 23-22 27-11-4-22-14-22-27V14z"/>',
    # 闪避:残影(三道弧)
    "dodge": f'<path {STROKE} d="M42 12c-16 10-16 30 0 40M30 15c-12 8-12 26 0 34M19 19c-8 6-8 20 0 26"/>',
    # 护盾:描边盾(与 defense 的实心盾刻意区分 —— 那个是常驻的护甲点数,
    # 这个是会被打空的盾条;同为盾形,靠「空心 vs 实心」一眼分开)
    "shield": f'<path {STROKE} d="M32 7l22 8v18c0 13-11 24-22 28-11-4-22-15-22-28V15z"/>',
    # 速度:速度线 + 箭头
    "speed": (
        f'<path {STROKE} d="M8 20h26M8 32h34M8 44h26"/>'
        f'<path {STROKE} d="M40 20l14 12-14 12"/>'
    ),

    # ---- 主界面底部导航 4(2026-08-28)----
    # 路径**逐字取自** docs/design/ui/scenes/Home.dc.html 的四枚页签 SVG,
    # 只用一层 scale 把稿上的 24 viewBox 撑到 64 画布(见 NAV_* 的注释)。
    # 改稿就重抄一遍,别在这里手改坐标 —— 手改必然和稿漂开。
    # 卡组:一张正牌 + 一张斜插的牌
    "nav_deck": (
        f'<g {NAV_SCALE} {NAV_STROKE}>'
        f'<rect x="3" y="5" width="11" height="15" rx="2"/>'
        f'<path d="M17 7 L20.5 8.2 A1.5 1.5 0 0 1 21.4 10 L18 19.5"/>'
        f'</g>'
    ),
    # 图鉴:书(书脊在左,底下一道翻口)
    "nav_bestiary": (
        f'<g {NAV_SCALE} {NAV_STROKE}>'
        f'<path d="M4 4.5 A1.5 1.5 0 0 1 5.5 3 H19 v18 H5.5 A1.5 1.5 0 0 1 4 19.5 Z"/>'
        f'<path d="M4 17.5 A1.5 1.5 0 0 1 5.5 16 H19"/>'
        f'</g>'
    ),
    # 技能:四角星芒
    "nav_perks": (
        f'<g {NAV_SCALE} {NAV_STROKE}>'
        f'<path d="M12 3 L14.6 9.4 L21 12 L14.6 14.6 L12 21 L9.4 14.6 L3 12 L9.4 9.4 Z"/>'
        f'</g>'
    ),
    # 商城:购物袋
    "nav_shop": (
        f'<g {NAV_SCALE} {NAV_STROKE}>'
        f'<path d="M3.5 8 h17 l-1.3 11.2 A1.5 1.5 0 0 1 17.7 20.5 H6.3 A1.5 1.5 0 0 1 4.8 19.2 Z"/>'
        f'<path d="M8.5 8 V6 a3.5 3.5 0 0 1 7 0 v2"/>'
        f'</g>'
    ),

    # ---- 战斗稿补齐 11(2026-08-30)----
    # 逐字抄自 docs/design/ui/scenes/Battle.dc.html 的 <svg class="icdefs">,
    # 每枚是一个 <symbol id="ic-KEY">;坐标一个字不改 —— 与 nav_* 同一条戒律。
    # 状态类 —— 破甲 / 流血
    "armorbreak": (
        f'<path {STROKE} d="M32 7l21 8v18c0 12-10 21-21 25-11-4-21-13-21-25V15z"/>'
        f'<path {STROKE} d="M37 17l-10 15h11l-10 15"/>'
    ),
    "bleed": (
        f'<path {FILL} d="M31 6c8 12 13 19 13 25a13 13 0 0 1-26 0c0-6 5-13 13-25z"/>'
        f'<circle cx="15" cy="49" r="5" {FILL}/>'
        f'<circle cx="47" cy="52" r="4" {FILL}/>'
    ),
    # 敌人天生能力 —— 焦痕 / 灯花 / 叠字分裂 / 生僻(属性隐藏) / 反伤
    "scorch": (
        f'<path {FILL} d="M26 6c9 11 14 18 14 24a14 14 0 0 1-28 0c0-5 2-9 5-11-1 4 1 7 3 9 2-7-1-13-3-17 4 1 7-1 9-5z"/>'
        f'<path {STROKE} d="M52 52V24M44 32l8-8 8 8"/>'
    ),
    # stroke-width 是稿上的 5,不是 6,不塞进 {STROKE}
    "sear": (
        f'<path {FILL} d="M32 5c9 11 14 18 14 24a14 14 0 0 1-28 0c0-5 2-9 5-11-1 4 1 7 3 9 2-7-1-13-3-17 4 1 7-1 9-5z"/>'
        f'<path fill="none" stroke="#fff" stroke-width="5" stroke-linecap="round" stroke-linejoin="round" d="M9 51c5-6 10 6 15 0s10 6 16 0 8 4 15 0"/>'
    ),
    "split": (
        f'<path {FILL} d="M27 6a26 26 0 0 0 0 52z"/>'
        f'<path {FILL} d="M37 6a26 26 0 0 1 0 52z"/>'
    ),
    "obscure": (
        f'<path {STROKE} d="M13 12h38v40H13z"/>'
        f'<path {FILL} d="M13 33c8-6 13 6 21 0s8 4 17 0v19H13z"/>'
    ),
    "thorns": (
        f'<circle cx="32" cy="32" r="11" {STROKE}/>'
        f'<path {STROKE} d="M32 6v7M32 51v7M6 32h7M51 32h7M14 14l5 5M45 45l5 5M50 14l-5 5M19 45l-5 5"/>'
    ),
    # 攻击模式 —— 远程 / 死盯玩家 / 横扫 / 贯穿
    "ranged": (
        f'<path {STROKE} d="M15 10c13 8 13 36 0 44"/>'
        f'<path {STROKE} d="M15 32h33M39 22l10 10-10 10"/>'
    ),
    "focus": (
        f'<circle cx="32" cy="32" r="17" {STROKE}/>'
        f'<path {STROKE} d="M32 5v10M32 49v10M5 32h10M49 32h10"/>'
    ),
    "sweep": (
        f'<path {STROKE} d="M53 11C28 11 11 28 11 53"/>'
        f'<path {STROKE} d="M53 27C37 27 27 37 27 53"/>'
    ),
    # 圆圈 stroke-width 是稿上的 4,不是 6,不塞进 {STROKE}
    "skewer": (
        f'<path {STROKE} d="M6 32h44M40 22l10 10-10 10"/>'
        f'<circle cx="19" cy="32" r="8" fill="none" stroke="#fff" stroke-width="4"/>'
        f'<circle cx="35" cy="32" r="8" fill="none" stroke="#fff" stroke-width="4"/>'
    ),

    # 持续治疗(2026-08-30 新画,第 30 枚):十字 + 向上的小箭头 = 每回合回一点。
    # StatusGlossary.dc.html 记着「29 枚里缺这一枚」,本枚已回填进那一页与 Battle.dc.html 的 icdefs。
    # 与 defense(实心盾)、immunity(盾+对勾)刻意都不像 —— 那两个是「挡」,这个是「回」。
    "heal": (
        f'<path {FILL} d="M26 12h12v14h14v12H38v14H26V38H12V26h14z"/>'
    ),
}


def svg(key: str) -> str:
    """一个图标的完整 SVG 文本。"""
    return (f'<svg xmlns="http://www.w3.org/2000/svg" width="{CANVAS}" height="{CANVAS}" '
            f'viewBox="0 0 {CANVAS} {CANVAS}">{ICONS[key]}</svg>')


def main(out_dir: Path = None) -> None:
    out_dir = Path(out_dir) if out_dir else OUT_DIR
    out_dir.mkdir(parents=True, exist_ok=True)
    SVG_DIR.mkdir(parents=True, exist_ok=True)

    for key in ICONS:
        (SVG_DIR / f"icon_{key}.svg").write_text(svg(key), encoding="utf-8")

    if shutil.which("rsvg-convert") is None:
        print("跳过 PNG:未找到 rsvg-convert(macOS: brew install librsvg)")
        return

    for key in ICONS:
        subprocess.run(
            ["rsvg-convert", "-w", str(CANVAS), "-h", str(CANVAS),
             str(SVG_DIR / f"icon_{key}.svg"), "-o", str(out_dir / f"icon_{key}.png")],
            check=True)
    print(f"{len(ICONS)} 个图标 → {out_dir}")


if __name__ == "__main__":
    sys.exit(main())
