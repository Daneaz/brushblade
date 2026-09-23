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
    "TryExpandPool(",      # 局内扩容·部件池
    "TryRevive(",          # 复活位
]

# 第 14 章 14.2 那张表,一位一枚。改这里要同步改 Platform/Ads.cs 的枚举
EXPECTED_PLACEMENTS = {
    "ChestBoost", "ShopRefresh", "ShopInk",
    "BattleLibrary", "BattleParts", "Revive",
}

# 已接进 UI 的广告位 —— 六位全接。这张表存在就是为了让「接了但忘了记」变成红灯。
# 2026-09-23:BattleParts 曾被我误判为「没有 UI 入口」,实际一直有
# (DrawPoolAdSlot);见下面 test_发奖方法名都真实存在 的注释
WIRED_PLACEMENTS = {
    "ChestBoost", "ShopRefresh", "ShopInk",
    "BattleLibrary", "BattleParts", "Revive",
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


def test_发奖方法名都真实存在():
    """GRANTING_CALLS 里的名字必须真能在 Core 里找到。

    2026-09-23 教训:这张表原本写的是 `TryExpandParts(`,而 Core 里的真名是
    `TryExpandPool(` —— 一个不存在的名字**匹配不到任何东西,于是那条扫描恒绿**,
    部件池广告位没过闸门却一路全绿。打错一个方法名就让守卫静默失效,
    这种测试比没有测试更危险,所以这里反过来钉死:名字对不上就红。
    """
    core = ROOT / "Brushblade/Assets/_Project/Core"
    haystack = "\n".join(p.read_text(encoding="utf-8") for p in core.rglob("*.cs"))
    missing = [c for c in GRANTING_CALLS if c.rstrip("(") not in haystack]
    assert not missing, (
        f"这些方法名在 Core 里不存在,对应的扫描是空过的:{missing}")


# ---- 广告 SDK 接入(2026-09-23)----

ADS_DIR = PLATFORM


def test_AdMob_适配器整份被_define_包起来():
    """SDK 不在工程里,不加 define 时这个文件必须等于空文件,否则工程编不过。

    这是「装了 SDK 的机器上绿、没装的机器上红」那类环境差异 bug 的源头,
    而本仓库的离线编译工装**不装 SDK**,所以漏了这道 guard 会当场炸。
    """
    text = (ADS_DIR / "AdMobAdService.cs").read_text(encoding="utf-8")
    code = [l for l in text.splitlines()
            if l.strip() and not l.strip().startswith("//")]
    assert code[0].strip() == "#if BRUSHBLADE_ADMOB", \
        "AdMobAdService.cs 的第一行有效代码必须是 #if BRUSHBLADE_ADMOB"
    assert code[-1].strip() == "#endif", "AdMobAdService.cs 必须以 #endif 收尾"
    assert "using GoogleMobileAds" in text, "适配器没引用 SDK,那它适配的是什么?"


def test_测试单元_ID_是_Google_官方那几个():
    """拿真单元做开发测试会被判无效流量,严重的会封号。

    这几个是 Google 公开的测试常量(developers.google.com/admob/unity/test-ads),
    被人「顺手」换成真 ID 就得红。
    """
    text = (ADS_DIR / "AdUnitIds.cs").read_text(encoding="utf-8")
    official = {
        "TestAppIdAndroid": "ca-app-pub-3940256099942544~3347511713",
        "TestAppIdIos": "ca-app-pub-3940256099942544~1458002511",
        "TestRewardedAndroid": "ca-app-pub-3940256099942544/5224354917",
        "TestRewardedIos": "ca-app-pub-3940256099942544/1712485313",
    }
    for name, value in official.items():
        assert re.search(rf'{name}\s*=\s*"{re.escape(value)}"', text), \
            f"{name} 不是 Google 官方测试单元(应为 {value})"


def test_每个广告位都有生产单元的位置():
    """生产表必须六位齐全 —— 少一位就意味着那位永远用测试单元,一分钱不进账。"""
    text = (ADS_DIR / "AdUnitIds.cs").read_text(encoding="utf-8")
    listed = set(re.findall(r"\[AdPlacement\.([A-Z][A-Za-z]*)\]", text))
    assert listed == EXPECTED_PLACEMENTS, \
        f"生产单元表与广告位对不上:少了 {EXPECTED_PLACEMENTS - listed}"


def test_白名单后门不发广告请求():
    """白名单绕过的是**广告请求**,不是发奖逻辑。

    绕开请求 = 不产生曝光,没问题;若改成「照常请求但伪造结果」那就是伪造曝光,
    属于广告欺诈。这条测试钉的就是这个边界:bypass 分支里不能调 _inner。
    """
    text = (ADS_DIR / "DeviceWhitelist.cs").read_text(encoding="utf-8")
    body = re.search(r"public void ShowRewarded\((.*?)\n        \}", text, re.S).group(1)
    bypass = body.split("_inner.ShowRewarded")[0]
    assert "IsWhitelisted" in bypass and "AdResult.Rewarded" in bypass, \
        "白名单分支没有直接发奖"
    assert bypass.count("_inner") == 0, \
        "白名单分支不该碰内层服务 —— 那会真的发出广告请求"


def test_回调恰好一次的闸在():
    """AdMob 会同时触发「拿到奖励」和「全屏关闭」,不加闸就发两次奖。"""
    text = (ADS_DIR / "AdMobAdService.cs").read_text(encoding="utf-8")
    assert "bool done = false" in text and "if (done) return;" in text, \
        "ShowRewarded 少了「恰好回调一次」的闸"


def test_装配链把白名单套在最外层():
    # 去注释:文档注释里有 `Monetization.Ads = new AdMobAdService(...)` 的示例,
    # 那是说明文字不是装配代码,扫进来会误报(2026-09-23 当场踩到)
    text = "\n".join(strip_comments(l)
                     for l in (ADS_DIR / "Monetization.cs")
                     .read_text(encoding="utf-8").splitlines()
                     if not l.strip().startswith("///"))
    for branch in re.findall(r"Ads = ([^;]+);", text):
        assert branch.strip().startswith("new WhitelistBypassAdService("), \
            f"这条装配分支没套白名单装饰器:{branch.strip()}"
