"""变现接线对账:广告发奖必须走 AdGate,闸门只能有一处。

为什么是 python 扫源码而不是 C# 单测:Tests asmdef 只引用 Core/Data,够不着
Platform 与 Presentation;而 Presentation 本来就「不强求自动化测试」(CLAUDE.md)。
这层扫描守的是接线本身 —— 它抓不到逻辑错,但能抓住下面两类真实会发生的回归:

  1. 新加一个广告位,忘了过 AdGate,于是点一下白送奖励;
  2. 改别的东西时把某个调用点改回「点击即发奖」,而**六个调用点里改错一个
     不会有任何编译错**,离线编译和 Core 单测也照不到。

⚠️ 这不能替代真机验证:占位实现 DirectGrantAdService 是同步回调,
   接真 SDK 后回调变异步,「点完立刻读发奖后状态」这类 bug 本测试看不出来。
"""
import re
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[3]
PRES = ROOT / "Brushblade/Assets/_Project/Presentation"
PLATFORM = ROOT / "Brushblade/Assets/_Project/Platform"

# Core 里「发奖」的方法:调用它们等于给玩家好处,必须先看完广告
GRANTING_CALLS = [
    "TryClaimInkAd(",      # 商城墨锭位
    "TryAdRefresh(",       # 商城免费刷新
    "TryApplyAdBoost(",    # 宝箱加速
    "TryExpandLibrary(",   # 局内扩容·字库
    "TryExpandParts(",     # 局内扩容·部件池
    "TryRevive(",          # 复活位
]

# 第 14 章 14.2 那张表,一位一枚。改这里要同步改 Platform/Ads.cs 的枚举
EXPECTED_PLACEMENTS = {
    "ChestBoost", "ShopRefresh", "ShopInk",
    "BattleLibrary", "BattleParts", "Revive",
}

# 已接进 UI 的广告位。BattleParts 尚无调用点(部件池扩容按钮还没做),
# 接上时把它挪进来 —— 这张表存在就是为了让「接了但忘了记」变成红灯
WIRED_PLACEMENTS = {
    "ChestBoost", "ShopRefresh", "ShopInk", "BattleLibrary", "Revive",
}

GATE_WINDOW = 8  # 发奖调用离 AdGate.Watch( 最多隔几行


def strip_comments(line: str) -> str:
    """去掉行注释 —— 注释里提到方法名不算调用(BattleView 有好几处)。"""
    return line.split("//", 1)[0]


def source_lines(path: Path) -> list[str]:
    return [strip_comments(l) for l in path.read_text(encoding="utf-8").splitlines()]


def presentation_files() -> list[Path]:
    return sorted(PRES.rglob("*.cs"))


@pytest.mark.parametrize("call", GRANTING_CALLS)
def test_发奖调用都在_AdGate_回调里(call):
    offenders = []
    for cs in presentation_files():
        if cs.name == "AdGate.cs":
            continue
        lines = source_lines(cs)
        for i, line in enumerate(lines):
            if call not in line:
                continue
            window = lines[max(0, i - GATE_WINDOW):i + 1]
            if not any("AdGate.Watch(" in w for w in window):
                offenders.append(f"{cs.relative_to(ROOT)}:{i + 1}  {line.strip()}")
    assert not offenders, (
        f"{call} 没有过广告闸门,点一下就白送:\n  " + "\n  ".join(offenders))


def test_闸门只有一处():
    """ShowRewarded 只准 AdGate 调 —— 绕过它就等于绕过「看完才发奖」。"""
    callers = []
    for cs in presentation_files():
        if "ShowRewarded(" in "".join(source_lines(cs)):
            callers.append(cs.name)
    assert callers == ["AdGate.cs"], f"这些文件绕开了 AdGate 直接播广告:{callers}"


def test_广告位枚举与第14章一致():
    text = (PLATFORM / "Ads.cs").read_text(encoding="utf-8")
    body = re.search(r"enum AdPlacement\s*\{(.*?)\}", text, re.S).group(1)
    names = set(re.findall(r"^\s*([A-Z][A-Za-z]*),", body, re.M))
    assert names == EXPECTED_PLACEMENTS, (
        f"广告位枚举与第 14 章 14.2 对不上:多了 {names - EXPECTED_PLACEMENTS},"
        f"少了 {EXPECTED_PLACEMENTS - names}")


def test_已接线的广告位与登记一致():
    used = set()
    for cs in presentation_files():
        used |= set(re.findall(r"AdPlacement\.([A-Z][A-Za-z]*)",
                               "\n".join(source_lines(cs))))
    assert used == WIRED_PLACEMENTS, (
        f"接线情况变了但没更新登记表:新接 {used - WIRED_PLACEMENTS},"
        f"掉线 {WIRED_PLACEMENTS - used}")


def test_占位实现还在但有明确警示():
    """占位实现必须留着(没接 SDK 时游戏要能跑),但不能悄无声息。"""
    text = (PLATFORM / "Monetization.cs").read_text(encoding="utf-8")
    assert "IsUsingPlaceholders" in text, "少了占位实现的自检开关"
    ads = (PLATFORM / "Ads.cs").read_text(encoding="utf-8")
    assert "出包给玩家之前必须换成真实现" in ads, "占位实现少了出包前的警示注释"
