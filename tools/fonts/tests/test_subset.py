import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import subset_fonts


def test_charset_covers_config():
    """按目录 glob 而不是列文件名:2026-08-22 之前这里列的是 ("chars.json",
    "enemies.json"),strings.zh-CN.json 加进 config/ 后没人补这个列表,
    238 个玩家可见字因此从子集里消失、这条测试却全程绿灯。
    过滤规则与 charset() 保持一致:排除空格以外的空白(比如文案里手写的换行 \\n
    用来做弹窗折行,不是要上字体的字形)。"""
    chars = subset_fonts.charset()
    for path in sorted(subset_fonts.CONFIG.glob("*.json")):
        raw = {c for c in subset_fonts.json_chars(path) if c == " " or not c.isspace()}
        missing = raw - chars
        assert not missing, f"{path.name} 缺字: {missing}"


def test_charset_covers_code_literals():
    chars = subset_fonts.charset()
    for d in subset_fonts.CODE_DIRS:
        missing = {c for c in subset_fonts.code_chars(d) if not c.isspace()} - chars
        assert not missing, f"代码文案缺字: {missing}"


def test_stacked_pua_glyphs_are_synthesized():
    """四叠字(四木 U+E625、四金代理 U+E626)Noto 无字形,由部件字形 2×2 拼合生成。"""
    from fontTools.ttLib import TTFont
    for name in ("NotoSerifSC-Subset.ttf", "NotoSansSC-Subset.ttf"):
        font = TTFont(subset_fonts.OUT_DIR / name)
        cmap = font.getBestCmap()
        for code in subset_fonts.STACKED:
            assert code in cmap, f"{name} 缺 U+{code:04X}"
            glyph = font["glyf"][cmap[code]]
            assert glyph.isComposite() and len(glyph.components) == 4


def test_subset_fonts_cover_charset():
    """子集产物的 cmap 必须覆盖 charset(动态字体缺字显示为空,这是安全网)。"""
    from fontTools.ttLib import TTFont
    for name in ("NotoSerifSC-Subset.ttf", "NotoSansSC-Subset.ttf"):
        path = subset_fonts.OUT_DIR / name
        assert path.exists(), f"{name} 未生成"
        cmap = TTFont(path).getBestCmap()
        missing = {c for c in subset_fonts.charset()
                   if ord(c) >= 0x20 and ord(c) not in cmap}
        # 原始字体本身缺的字直接报错——宁可换字体也不上线空字形
        assert not missing, f"{name} 缺字形: {sorted(missing)[:20]}"
