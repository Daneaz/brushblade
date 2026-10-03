# 2026-10-03 从 tools/design/tests/test_mob_assets.py 摘出:设计侧合并稿(glyph-refs/svg-done)退役。
# 原文保留备查,不再被 pytest 收集(不在 CLAUDE.md 的测试目录里)。

def test_design_source_exists_for_every_minion_slug():
    """每只怪都要有设计侧的合并稿 —— 改稿只改这一份,后面三步是脚本。

    ⚠ 2026-08-29 补的五只(涂改/铁画/镇纸/洇痕/衍文)当时是直接出的分层文件,没回写
    合并稿,所以这里放行它们:是一笔待补的账,不是「不需要」。补齐后把它们从豁免里删掉。"""
    without_source = {"tugai", "tiehua", "zhenzhi", "yinhen", "yanwen"}
    missing = [slug for slug in rm.MINION_SLUGS.values()
               if slug not in without_source and not (SVG_DONE / f"mob_{slug}.svg").exists()]
    assert missing == [], f"这些怪没有设计侧合并稿:{missing}"

