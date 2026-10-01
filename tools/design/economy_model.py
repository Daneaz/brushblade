"""局外经济日收支模型(2026-10-01):按「每日活跃 N 趟」估算墨锭/卡张数的日收入与消耗进度。

数值表**直接从 Core 源码里正则取**(Shop.cs / Chest.cs / Meta.cs / Perk.cs / enemies.json),
不另抄一份 —— 抄出来的表改一处漏一处,模型会静默地算旧账。
不计首破里程碑与图鉴领赏(一次性来源,不属于稳态日收入)。

用法:python3 tools/design/economy_model.py [--runs 1 3 5]
"""
import argparse
import json
import math
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CORE = ROOT / "Brushblade/Assets/_Project/Core"
CONFIG = ROOT / "Brushblade/Assets/StreamingAssets/config"
RARITIES = "白绿蓝紫金橙红"
TIERS = ["素纸", "竹简", "青瓷", "紫檀", "鎏金", "朱漆", "赤霄"]


def _src(name):
    return (CORE / name).read_text(encoding="utf-8")


def arr(name, file):
    """取 `name = { 1, 2, 3 }` 形式的一维数组。"""
    m = re.search(rf"\b{name}\s*=\s*\{{([^}}]*)\}}", _src(file))
    if not m:
        raise KeyError(f"{file}: {name}")
    return [float(x) if "." in x else int(x) for x in re.findall(r"[\d.]+", m.group(1))]


def table(name, file):
    """取 `name = { new[] {...}, new[] {...} }` 形式的二维表(注释里的数字不算)。"""
    src = _src(file)
    start = src.index(name)
    body = src[src.index("{", start): src.index("};", start)]
    rows = re.findall(r"new\[\]\s*\{([^}]*)\}", body)
    return [[int(x) for x in re.findall(r"\d+", r)] for r in rows]


# ---- 源码数值表 ----
CHEST_INK = arr("InkReward", "Chest.cs")
CHEST_STACKS = arr("StackCount", "Chest.cs")  # 2026-10-01 起每箱开 S 捆,每捆张数 = 字摊同档一份
CHEST_SECONDS = arr("DurationSeconds", "Chest.cs")
CHEST_AD_SECONDS = arr("AdReductionSeconds", "Chest.cs")
TIER_BANDS = table("TierWeightBands", "Chest.cs")
CARD_RARITY_W = table("CardRarityWeights =", "Chest.cs")
INK_AD = arr("InkAdAmounts", "Shop.cs")
SHOP_CHEST_BASE = arr("ChestBasePrice", "Shop.cs")
BUNDLE_SIZE = arr("BundleSizes", "Shop.cs")
BUNDLE_PRICE = arr("BundlePrices", "Shop.cs")
SHOP_SLOT_LV = arr("SlotUnlockLevel", "Shop.cs")
REGULAR_OFF = arr("RegularDiscounts", "Shop.cs")
PREMIUM_OFF = arr("PremiumDiscounts", "Shop.cs")


def avg_discount(premium):
    t = PREMIUM_OFF if premium else REGULAR_OFF
    return sum(t) / len(t) / 100


SHOP_CHEST_PRICE = [p * avg_discount(i >= 5) for i, p in enumerate(SHOP_CHEST_BASE)]  # 当日折后均价
COPIES_UP = arr("CopiesToUpgrade", "Meta.cs")
INK_UP = arr("InkToUpgrade", "Meta.cs")
COPIES_MUL = arr("CopiesMultiplier", "Meta.cs")
INK_MUL = arr("InkMultiplier", "Meta.cs")


def perk_total():
    w, p, m = (arr(n, "Perk.cs") for n in ("WuxingCosts", "PassiveCosts", "MechanicCosts"))
    src = _src("Perk.cs")
    fixed = [int(x) for x in re.findall(r"MechanicGates\[\d\],\s*(\d+),\s*PerkEffect\.Ap", src)]  # 一气
    cross = [int(x) for x in re.findall(r"inkCost:\s*(\d+)", src)]
    # 五行 5 枝 × 4 层、被动 4 枝 × 3 层、机制 lore/wide/insight 三枝走 MechanicCosts
    return 5 * sum(w) + 4 * sum(p) + 3 * sum(m) + sum(fixed) + sum(cross)


PERK_TOTAL = perk_total()

_cfg = json.loads((CONFIG / "enemies.json").read_text(encoding="utf-8"))
EVENT_CHANCE = _cfg["eventChance"] / 100
BOSS_EVERY = _cfg["endless"]["bossEvery"]


def _card_counts():
    data = json.loads((CONFIG / "chars.json").read_text(encoding="utf-8"))
    items = data if isinstance(data, list) else data.get("chars", data)
    items = items.values() if isinstance(items, dict) else items
    counts = [0] * 7
    for c in items:
        r = c.get("rarity")
        if r:
            counts[["White", "Green", "Blue", "Purple", "Gold", "Orange", "Red"].index(r)] += 1
    return counts


CARDS_BY_RARITY = _card_counts()


# ---- 局内(镜像 EndlessRules / EndlessGenerator)----
ESCORT_FROM, ESCORT_CAP = 20, 4


def is_boss(d):
    return d % BOSS_EVERY == 0


def floor_ink(d):
    return (5 if is_boss(d) else 2) << ((d - 1) // 10)


def kill_ink(d):
    per = 1 + d // 10
    if is_boss(d):
        escorts = 0 if d < ESCORT_FROM else min(ESCORT_CAP, (d - ESCORT_FROM) // BOSS_EVERY + 1)
        return 5 * per + escorts * per
    return (1 + min(7, (d - 1) // 6)) * per


# 奇遇墨锭期望(12 个奇遇均匀抽):宝库必取 +80(换 200 HP);书院抄书 +20、赌书押 30
# 赢 100(EV +20)各按一半玩家会选。其余奇遇不产墨锭。
EVENT_INK_EV = (80 + 0.5 * 20 + 0.5 * 20) / 12


def run_income(depth):
    """一趟爬到 depth(死在 depth+1)的墨锭/经验。"""
    ink = {"层清算": 0.0, "击杀": 0.0, "奇遇": 0.0}
    xp = 0
    for d in range(1, depth + 1):
        ink["层清算"] += floor_ink(d)
        ink["击杀"] += kill_ink(d)
        if not is_boss(d):
            ink["奇遇"] += EVENT_CHANCE * EVENT_INK_EV
        xp += 50 if is_boss(d) else 10
    return ink, xp


def settle_chest_dist(depth):
    """结算箱档位分布(EndlessRules.ChestTierFor:最高 Boss 层定档,90/10)。"""
    top = depth // BOSS_EVERY * BOSS_EVERY
    dist = [0.0] * 7
    if top == 0:
        return dist
    bounds = [(5, 0, 1), (10, 1, 2), (20, 2, 3), (35, 3, 4), (50, 4, 5), (70, 5, 6)]
    lo, hi = 6, 6
    for lim, a, b in bounds:
        if top < lim:
            lo, hi = a, b
            break
    dist[lo] += 0.9
    dist[hi] += 0.1
    return dist


def level_dist(level):
    w = TIER_BANDS[min(max(0, (level - 1) // 5), len(TIER_BANDS) - 1)]
    s = sum(w)
    return [x / s for x in w]


def chest_value(dist):
    ink = sum(p * CHEST_INK[i] for i, p in enumerate(dist))
    cards = [0.0] * 7
    hours = 0.0
    for i, p in enumerate(dist):
        hours += p * max(0, CHEST_SECONDS[i] - CHEST_AD_SECONDS[i]) / 3600  # 每箱看一次加速广告
        for r in range(7):
            cards[r] += p * CHEST_STACKS[i] * CARD_RARITY_W[i][r] / 1000 * BUNDLE_SIZE[r]
    return ink, cards, hours


def skip_cost(seconds):
    return max(1, (seconds + 119) // 120)


def upgrade_cost(rarity, to_level):
    copies = sum(max(1, math.ceil(COPIES_UP[l] * COPIES_MUL[rarity])) for l in range(to_level - 1))
    ink = sum(int(INK_UP[l] * INK_MUL[rarity]) for l in range(to_level - 1))
    return copies, ink


# 墨锭并行培养的主力字数:墨锭可以集中投,卡张数是随机散落的 —— 平衡线取
# 「一张主力字升满,攒卡天数 ≈ 墨锭分给 FOCUS 张主力字时的攒墨天数」(2026-10-01 宝箱成捆定档依据)
FOCUS = 8

# ---- 玩家画像:角色等级 ↔ 一趟典型能爬到的层 ----
STAGES = [("新手", 5, 10), ("中期", 15, 25), ("后期", 30, 45)]


def day(level, depth, runs, spend_shop=False):
    ink_parts, xp_run = run_income(depth)
    out = {k: v * runs for k, v in ink_parts.items()}

    settle = settle_chest_dist(depth)
    s_ink, s_cards, s_hours = chest_value(settle)
    out["结算箱"] = s_ink * runs
    cards = [c * runs for c in s_cards]
    hours = s_hours * runs

    ad_ev = sum(p * INK_AD[i] for i, p in enumerate(level_dist(level)))
    out["商城墨锭广告"] = ad_ev

    # 商城箱(按等级掷档)——只记开出来的收益,买价在「可选消耗」里单列
    shop_dist = level_dist(level)
    sh_ink, sh_cards, sh_hours = chest_value(shop_dist)
    shop_chest_price = sum(p * SHOP_CHEST_PRICE[i] for i, p in enumerate(shop_dist))

    # 商城字卡广告:绿 ×10 / 蓝 ×5 / 紫 ×1
    ad_cards = [0, 10, 5, 1, 0, 0, 0]
    cards = [c + a for c, a in zip(cards, ad_cards)]

    return {
        "ink": out, "ink_total": sum(out.values()), "xp": xp_run * runs,
        "cards": cards, "chest_hours": hours, "chests": runs if depth >= BOSS_EVERY else 0,
        "ad_ev": ad_ev, "shop_chest": (shop_chest_price, sh_ink, sh_cards, sh_hours),
    }


def xp_to_level(level):
    return sum(100 + 50 * (n - 1) for n in range(1, level))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--runs", type=int, nargs="+", default=[1, 3, 5])
    args = ap.parse_args()

    print(f"技能树全点满 {PERK_TOTAL:,} 墨锭;奇遇墨锭期望 {EVENT_INK_EV:.1f}/次,触发 {EVENT_CHANCE:.0%}(非 Boss 层)")
    print("卡数/稀有度:", dict(zip(RARITIES, CARDS_BY_RARITY)))
    print()
    for name, lv, depth in STAGES:
        print(f"=== {name}:Lv{lv},每趟爬到 {depth} 层 ===")
        for n in args.runs:
            d = day(lv, depth, n)
            parts = "  ".join(f"{k} {v:,.0f}" for k, v in d["ink"].items())
            ad_share = d["ad_ev"] / d["ink_total"]
            print(f"  N={n}: 日墨锭 {d['ink_total']:,.0f}  [{parts}]  墨锭广告占 {ad_share:.0%}")
            cards = "  ".join(f"{RARITIES[r]} {c:.1f}" for r, c in enumerate(d["cards"]) if c >= 0.05)
            print(f"        日卡张数 [{cards}]  日经验 {d['xp']:,}  结算箱计时(已减广告) {d['chest_hours']:.1f}h")
            print(f"        技能树点满需 {PERK_TOTAL / d['ink_total']:.0f} 天(全部墨锭都投技能)")
        price, sh_ink, sh_cards, sh_hours = d["shop_chest"]
        sh_c = sum(sh_cards)
        print(f"  商城箱:均价 {price:.0f},开出墨锭 {sh_ink:.0f} + {sh_c:.1f} 张卡 → 每张卡净价 {(price - sh_ink) / sh_c:.0f}")
        # 升级进度:一张该稀有度卡,平均每天分到多少张同名(按该稀有度字数均分)
        d3 = day(lv, depth, 3)
        print(f"  N=3 时一张卡的升级天数(卡张数瓶颈 / 墨锭瓶颈;卡均分到该档每一个字,墨锭分给 {FOCUS} 张主力字):")
        for r in range(7):
            per_card = d3["cards"][r] / max(1, CARDS_BY_RARITY[r])
            row = []
            for to in (5, 10):
                c, i = upgrade_cost(r, to)
                dc = c / per_card if per_card > 0 else float("inf")
                di = i * FOCUS / d3["ink_total"]
                row.append(f"→{to}级 卡{dc:,.0f}d/墨{di:,.0f}d")
            print(f"    {RARITIES[r]}: 每字 {per_card:.2f} 张/日  " + "  ".join(row))
        print()

    print("=== 墨锭跳过开箱计时 vs 箱子本身产墨 ===")
    for i, t in enumerate(TIERS):
        sec = max(0, CHEST_SECONDS[i] - CHEST_AD_SECONDS[i])
        cost = skip_cost(sec) if sec > 0 else 0
        print(f"  {t}: 看完广告后剩 {sec / 3600:.1f}h,跳过 {cost} 墨锭;箱产墨 {CHEST_INK[i]}"
              f"  → 跳过成本/箱产墨 {cost / CHEST_INK[i]:.0%}")
    print()
    print("=== 商城墨锭广告期望(按等级段)===")
    for lv in (1, 6, 11, 16, 21, 26):
        print(f"  Lv{lv}+: {sum(p * INK_AD[i] for i, p in enumerate(level_dist(lv))):.0f}")
    print()
    print("=== 角色等级累计经验 ===")
    for lv in (5, 10, 15, 22, 30, 50):
        print(f"  Lv{lv}: {xp_to_level(lv):,}")


if __name__ == "__main__":
    main()
