# -*- coding: utf-8 -*-
"""chars.json 与 spec §6 目标表的对账(2026-09-07,P2 Task 3)。

P2 要改 57 字的数值与特性。人肉比对必错,所以把「改对了没有」变成一条测试。
目标表由 tools/design/rebalance_2026_09_05.py 从 §1.4 锚点 + §1.4.1 价目表推出
(改规则要回到 spec,不要改这里的期望值)。

⚠ 本测试在 P2 落地完成前会红 —— 那是进度条,不是故障。逐条挂 xfail(strict=True):
落地完成的列/项会先转绿,strict 会在那一刻报 XPASS 提醒删掉对应的 xfail 标记,
而不是让整条测试静默地继续通过。

七列数值对账每列一条测试(直伤 / 护盾 / 治疗 / 护甲 / 召唤 / 终极技 / 灼烧),
理由:T4 是逐列落地的(brief 建议的顺序),逐条挂 xfail 才能让每一列独立转绿,
而不是等 57 字全改完才看到唯一一次 XPASS。
"""
import importlib.util
import contextlib
import io
import json
import re
import subprocess
import sys
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[3]
CHARS = ROOT / "Brushblade/Assets/StreamingAssets/config/chars.json"
SCRIPT = ROOT / "tools/design/rebalance_2026_09_05.py"

# gen_char_doc / export_chars 的 PUA 代理:增补平面字在 UGUI 显示不出代理对,
# 落配置时换成私有区码位。对账要还原回真实字,否则两边永远对不上。
# ⚠ 金四叠字是 𨰻(U+28C3B),不是 䥱(U+4971)——见 export_chars.PUA_PROXY。
PUA_BACK = {"": "𣛧", "": "𨰻"}
RARITY = {"白": "White", "绿": "Green", "蓝": "Blue", "紫": "Purple",
          "金": "Gold", "橙": "Orange", "红": "Red"}

_SUMMON_RE = re.compile(r"(\d+) 只 · (\d+) 血 / (\d+) 攻")
_HEAL_OT_RE = re.compile(r"(\d+)×(\d+)")
_BURN_RE = re.compile(r"(全体)?灼烧 (\d+)")


def _target():
    """目标表的文本形态(逐行 markdown 单元格)—— 只给字集/稀有度两条用。"""
    out = subprocess.run([sys.executable, str(SCRIPT)], capture_output=True,
                         text=True, cwd=ROOT).stdout
    rows = {}
    for line in out.splitlines():
        if not line.startswith("| ") or line.startswith("| 字 ") or line.startswith("|---"):
            continue
        cells = [c.strip() for c in line.split("|")[1:-1]]
        rows[cells[0].split("(")[0]] = cells
    return rows


def _target_rows():
    """目标表的结构化形态 —— 直接从脚本的 `rows`(内部计算结果)取,不再重新解析
    打印出来的文本。这样能拿到 form/traits 等打印表里没有的字段(判断攻击列该对
    应 DamageSingle 还是 DamageAll 需要它),也避免我方再实现一遍格式化/取整逻辑。

    脚本没有 `if __name__ == '__main__'` 保护,import 会连带跑一遍它的诊断 print——
    用 redirect_stdout 吞掉,不弄脏测试输出;不影响它产出的 rows 内容。
    """
    spec = importlib.util.spec_from_file_location("rebalance_2026_09_05", SCRIPT)
    mod = importlib.util.module_from_spec(spec)
    with contextlib.redirect_stdout(io.StringIO()):
        spec.loader.exec_module(mod)
    return {r["id"]: r for r in mod.rows}


def _actual():
    chars = json.loads(CHARS.read_text(encoding="utf-8"))["chars"]
    return {PUA_BACK.get(c["id"], c["id"]): c
            for c in chars if not c.get("component")}


def _effects(c):
    return c.get("effects", []) + c.get("attackEffects", [])


def _sum(effects, kinds):
    return sum(e["value"] for e in effects if e["kind"] in kinds)


@pytest.mark.xfail(reason="P2 落地完成前预期失败,见 docs/superpowers/plans/2026-09-07-字表平衡重做-P2-数值落地.md Task 4", strict=True)
def test_roster_matches_target():
    """字表的字集与目标一致 —— 多一个少一个都要报出来。"""
    target, actual = set(_target()), set(_actual())
    assert not (target - actual), f"目标表有、配置里缺:{sorted(target - actual)}"
    assert not (actual - target), f"配置里有、目标表已移出:{sorted(actual - target)}"


@pytest.mark.xfail(reason="P2 落地完成前预期失败,见 docs/superpowers/plans/2026-09-07-字表平衡重做-P2-数值落地.md Task 4", strict=True)
def test_rarity_matches_target():
    target, actual = _target(), _actual()
    bad = [(k, actual[k]["rarity"], RARITY[target[k][2]])
           for k in sorted(set(target) & set(actual))
           if actual[k]["rarity"] != RARITY[target[k][2]]]
    assert not bad, "稀有度与目标表不符(字, 实际, 目标):\n  " + "\n  ".join(map(str, bad))


@pytest.mark.xfail(reason="P2 落地完成前预期失败,见 docs/superpowers/plans/2026-09-07-字表平衡重做-P2-数值落地.md Task 4", strict=True)
def test_atk_matches_target():
    """攻击列:atk 形态期望 DamageSingle,aoe/群疗/群盾期望 DamageAll。

    分 N 段(hitCount)按总伤害折算 —— 目标列是折算前的总量,不是单段。
    """
    target, actual = _target_rows(), _actual()
    bad = []
    for k in sorted(set(target) & set(actual)):
        r, c = target[k], actual[k]
        want = r["atk"] if isinstance(r["atk"], int) else 0
        is_group = r["form"] == "aoe" or ({"群疗", "群盾"} & set(r["traits"]))
        kind = "DamageAll" if is_group else "DamageSingle"
        got = sum(e["value"] * e.get("hitCount", 1) for e in _effects(c) if e["kind"] == kind)
        if got != want:
            bad.append((k, got, want))
    assert not bad, "攻击列不符(字, 实际, 目标):\n  " + "\n  ".join(map(str, bad))


@pytest.mark.xfail(reason="P2 落地完成前预期失败,见 docs/superpowers/plans/2026-09-07-字表平衡重做-P2-数值落地.md Task 4", strict=True)
def test_shield_matches_target():
    """护盾列 → Shield / ShieldAll。"""
    target, actual = _target_rows(), _actual()
    bad = []
    for k in sorted(set(target) & set(actual)):
        r, c = target[k], actual[k]
        want = r["sh"] if isinstance(r["sh"], int) else 0
        got = _sum(_effects(c), ("Shield", "ShieldAll"))
        if got != want:
            bad.append((k, got, want))
    assert not bad, "护盾列不符(字, 实际, 目标):\n  " + "\n  ".join(map(str, bad))


@pytest.mark.xfail(reason="P2 落地完成前预期失败,见 docs/superpowers/plans/2026-09-07-字表平衡重做-P2-数值落地.md Task 4", strict=True)
def test_heal_matches_target():
    """治疗列 → HealSelf / HealAll,持续治疗(「N×3」)→ HealOverTime(value=N, turns=3)。"""
    target, actual = _target_rows(), _actual()
    bad = []
    for k in sorted(set(target) & set(actual)):
        r, c = target[k], actual[k]
        hl, effects = r["hl"], _effects(c)
        if isinstance(hl, str) and hl:
            m = _HEAL_OT_RE.fullmatch(hl)
            want = (int(m.group(1)), int(m.group(2)))
            hot = [e for e in effects if e["kind"] == "HealOverTime"]
            got = (hot[0]["value"], hot[0].get("turns")) if hot else (0, 0)
        else:
            want = hl if isinstance(hl, int) else 0
            got = _sum(effects, ("HealSelf", "HealAll"))
        if got != want:
            bad.append((k, got, want))
    assert not bad, "治疗列不符(字, 实际, 目标):\n  " + "\n  ".join(map(str, bad))


@pytest.mark.xfail(reason="P2 落地完成前预期失败,见 docs/superpowers/plans/2026-09-07-字表平衡重做-P2-数值落地.md Task 4", strict=True)
def test_armor_matches_target():
    """护甲列 → DefenseBuff(点数制护甲增益,尚未被任何字使用过)。"""
    target, actual = _target_rows(), _actual()
    bad = []
    for k in sorted(set(target) & set(actual)):
        r, c = target[k], actual[k]
        want = r["ar"] if isinstance(r["ar"], int) else 0
        got = _sum(_effects(c), ("DefenseBuff",))
        if got != want:
            bad.append((k, got, want))
    assert not bad, "护甲列不符(字, 实际, 目标):\n  " + "\n  ".join(map(str, bad))


@pytest.mark.xfail(reason="P2 落地完成前预期失败,见 docs/superpowers/plans/2026-09-07-字表平衡重做-P2-数值落地.md Task 4", strict=True)
def test_summon_matches_target():
    """召唤列「N 只 · H 血 / A 攻」→ Summon(count, value=血, attack)。"""
    target, actual = _target_rows(), _actual()
    bad = []
    for k in sorted(set(target) & set(actual)):
        r, c = target[k], actual[k]
        sm = r["sm"]
        summons = [e for e in _effects(c) if e["kind"] == "Summon"]
        got = ((summons[0].get("count", 1), summons[0]["value"], summons[0].get("attack", 0))
               if summons else (0, 0, 0))
        if sm:
            m = _SUMMON_RE.fullmatch(sm)
            want = (int(m.group(1)), int(m.group(2)), int(m.group(3)))
        else:
            want = (0, 0, 0)
        if got != want:
            bad.append((k, got, want))
    assert not bad, "召唤列不符(字, 实际(只/血/攻), 目标):\n  " + "\n  ".join(map(str, bad))


@pytest.mark.xfail(reason="P2 落地完成前预期失败,见 docs/superpowers/plans/2026-09-07-字表平衡重做-P2-数值落地.md Task 4", strict=True)
def test_ultimate_matches_target():
    """终极技/层列 → SpendHeft(土)/ SpendWellspring(水)的 value(每层伤害)。"""
    target, actual = _target_rows(), _actual()
    bad = []
    for k in sorted(set(target) & set(actual)):
        r, c = target[k], actual[k]
        want = r["ul"] if isinstance(r["ul"], int) else 0
        got = _sum(_effects(c), ("SpendHeft", "SpendWellspring"))
        if got != want:
            bad.append((k, got, want))
    assert not bad, "终极技/层列不符(字, 实际, 目标):\n  " + "\n  ".join(map(str, bad))


@pytest.mark.xfail(reason="P2 落地完成前预期失败,见 docs/superpowers/plans/2026-09-07-字表平衡重做-P2-数值落地.md Task 4", strict=True)
def test_burn_matches_target():
    """灼烧列「(全体)?灼烧 N」→ BurnSingle / BurnAll 的 value=N。"""
    target, actual = _target_rows(), _actual()
    bad = []
    for k in sorted(set(target) & set(actual)):
        r, c = target[k], actual[k]
        burn, effects = r["burn"], _effects(c)
        got_single = _sum(effects, ("BurnSingle",))
        got_all = _sum(effects, ("BurnAll",))
        if burn:
            m = _BURN_RE.fullmatch(burn)
            n = int(m.group(2))
            want = (0, n) if m.group(1) else (n, 0)
        else:
            want = (0, 0)
        if (got_single, got_all) != want:
            bad.append((k, (got_single, got_all), want))
    assert not bad, "灼烧列不符(字, 实际(单体,全体), 目标):\n  " + "\n  ".join(map(str, bad))
