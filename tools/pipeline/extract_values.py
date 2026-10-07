"""从《技能机制详表》抽取可直接落地(标 ✅)的字与数值。

详表是唯一数值真相;这里只做抽取,不做换算 —— 表里「效果配置」列已经是基础值。
"""
import re

ELEMENT = {"火": "Fire", "木": "Wood", "水": "Water", "金": "Metal", "土": "Earth"}
RARITY = {"🟡金": "Gold", "🔴红": "Red", "🟠橙": "Orange", "🟣紫": "Purple",
          "🔵蓝": "Blue", "🟢绿": "Green", "⚪白": "White"}

# 纯二选一双方向的系(2026-09-02):这些系的表多一格「攻击效果配置」,见 _parse_row。
# 水系(Task 10)、土系(Task 11)均已落地。
# 金系(2026-09-08):只为 BUFF 组的 利 / 锋 —— 用户裁定「不能既加攻又能攻击,还给我方 +buff」,
# 两字拆成攻护两面(伤害进攻面、增攻/暴击进护面)。金系其余表全是单面字,多出来的那格
# 一律 —— ,解析侧不受影响(_parse_row 现在把「实现」列排除在反引号候选之外,
# 那才是这条闸原先真正防的东西,见那里的注释)。
# 木系(2026-09-27):召唤字改双面 —— 护面 = 召唤,攻面 = 单体伤害(详表 §三 三张召唤表
# 多挂的那格「攻击效果配置」)。纯攻击字 花 只有一个反引号格,不受影响。
# 火系(2026-10-05,D1 Task 11):为 Task 12 把火字拆攻 / 燃两面预备;现有火表没有第二个
# 反引号格(实现格之前),加进来不改变任何已有输出。
# 2026-10-05(D1 Task 12):55 字全部两面 —— 火 / 金 单面表与木系「攻」表(花)都加了
# 「攻击效果配置(攻面)」一格,第一格改为五行面(燃 / 铠 / 生)。上面「金系其余表全是单面字」
# 「花 只有一个反引号格」两句是当时的状态,已不成立。
DUAL_DIRECTION_ELEMENTS = {"水", "土", "金", "木", "火"}

# 召唤被动 token → chars.json 里 passive 对象的字段名(详表 §召唤·单体·带被动)。
# 「光环」与「攻击附灼烧」是同一个字段:烓/灶 攻 0 靠 OnHitBurn 输出,楸 攻 6 附带 1 层。
def _is_damage(kind):
    """伤害效果(修饰 token 挂它):Damage 开头,但 DamageCut(本回合减伤,D1 Task 7)不是伤害。"""
    return kind.startswith("Damage") and kind != "DamageCut"


SUMMON_PASSIVE = {
    "SummonSpeed": "speed",
    "Thorns": "thorns",
    "HealAlly": "healAlly",
    "OnHitBurn": "onHitBurn",
    "OnHitCurse": "onHitCurse",
    "Dodge": "dodge",
    "OnSummonFreeze": "onSummonFreeze",
    "OnHitFreeze": "onHitFreezeChance",       # 概率(百分点),吃卡等级
    "OnHitFreezeTurns": "onHitFreezeTurns",
    "OnHitSlow": "onHitSlowPercent",          # 幅度(速度点数),吃卡等级
    "OnHitSlowTurns": "onHitSlowTurns",
    "Regen": "regen",                         # 自愈(2026-09-05,藻):每回合只回自己
    # 攻击光环(2026-09-05,𣛧,P2 Task 2):给场上全部召唤物 +N 攻,含自己
    # (SummonPassive.AuraAttack,EnemyDef.cs:44)。字段名对齐引擎,不另起名字。
    "AuraAttack": "auraAttack",
    # 本命新字段(D2-0 Task 6,spec §9 木;字段名对齐 SummonPassive,Newtonsoft 大小写不敏感读入)
    "BackRowBonus": "backRowBonusPercent",    # 远射·强化:打后排 +N%
    "PerAllyAttack": "perAllyAttackPercent",  # 成林:每有 1 只其他存活木灵 +N% 攻
    "SummonArmor": "armor",                   # 坚木:入场自带护甲(吃卡等级)
    "HealAllyTimes": "healAllyTimes",         # 桂香·强化:HealAlly 每回合结算 N 次
    "Sprout": "sproutPercent",                # 丛生:每拍分裂小藻,血攻 = 本体 N%
    "SproutMax": "sproutMax",                 # 丛生:同时存活上限
    "EntrySaplings": "entrySaplings",         # 森然:入场附带 N 只幼苗
    "OnHitCharm": "onHitCharmChance",         # 迷香:出手 N% 魅惑 1 回合(吃卡等级,钳 100)
}


# 无数值的效果 token(布尔标记式):通用正则 `(\w+) (\d+)` 抓不到,单独认。
# 顺带绕开负数 —— `Dispel -1` 那个负号通用正则也认不出。
# ⚠ 追加顺序即结算顺序:若同一行出现两个无数值 token,谁先在这个 dict 里出现谁先被追加。
# 本批(BurnNoDecay/BurnSettleNow/Detonate)每行最多命中一个,不受影响。
VALUELESS_EFFECTS = {
    "Cleanse": {"kind": "Cleanse", "value": 0},
    "DispelAll": {"kind": "Dispel", "value": -1},
    "BurnNoDecay": {"kind": "BurnNoDecay", "value": 0},
    "BurnSettleNow": {"kind": "BurnSettleNow", "value": 0},
    "Detonate": {"kind": "Detonate", "value": 0},
    # 全体引爆(2026-08-26,炸)。必须排在 `Detonate` 之后**且**用整串带反引号匹配 ——
    # `f"\`{token}\`" in config` 拿 "`Detonate`" 去配 "`DetonateAll`" 配不上,两者互不吞。
    "DetonateAll": {"kind": "Detonate", "value": 0, "targetAll": True},
    # 解封(2026-09-16,水):独立效果(与 Cleanse 同型,不是挂在 Damage* 上的修饰位),
    # 6 类纯随机重掷召唤物属性、永久,不带数值。本任务只造机制不配字,这里先补上
    # token 映射,免得 Task 10/11 配字时才发现这张表漏了它。
    "Unseal": {"kind": "Unseal", "value": 0},
    # 改形修饰器(D1 Task 3,附录 M2):不带数值;改什么由同格的 `shape X` / `hits N` 等小写 token 给出,
    # 见 _attach_modifier_tokens。
    "Reshape": {"kind": "Reshape", "value": 0},
    # 保命(D1 Task 7,扎根):Value 不用;落点由 `pick SummonedThisCast` 给出。
    "Endure": {"kind": "Endure", "value": 0},
}

# 斩杀是**伤害的修饰**,不是独立效果:抽出来挂到同一行的伤害效果上。
# 值 = executeKills(True = 直接击杀,False = 残血加伤 ×2)
EXECUTE_TOKENS = {"ExecuteKill": True, "ExecuteBonus": False}

# 需要 turns 的 Kind(白名单):写死给 HealOverTime 会让新加的持续类状态静默丢掉回合数。
# 注意:下面 turns 正则是对整格「效果配置」搜一次,一格只支持一个 turns 值——若将来
# 一行里出现两个不同回合数的持续效果(如 `Blind 50`(turns 2) + `Silence 0`(turns 1)),
# 这里要改成按效果分段解析,现在 YAGNI(2026-09-07 复核 spec §6 全表:没有这种行,见
# P2 Task 2 报告 —— `淋` 的「封禁2 / 减速2」两个 2 不同源,`Slow` 的回合数直接就是
# Value,根本不走这条 turns 正则,不冲突)。
# 魅惑(2026-09-07,花):EffectKind.Charm 的 Value 不用,Turns 才是回合数
# (BattleEngine.cs `Math.Max(1, effect.Turns)`)——漏填 turns 会被引擎兜成 1 回合,
# 看起来能用、实际回合数写死且不吃卡等级,必须强制要求写。
#
# 限时增益(2026-09-05 引擎已支持,2026-09-07 P2 Task 4a 收紧):`Empower` / `CritBuff`
# 曾经历一段 turns **可选**的过渡期(见下方 git blame / task-2-report.md)——当时既有字
# 「锋」是 `CritBuff 20` 不写 turns(本场持久),硬塞进 DURATION_KINDS 会让它当场报错,
# 砸穿恒等性硬线,故临时开了个 OPTIONAL_DURATION_KINDS 口子。P2 落地完成后 利/锋 两字
# 均已改写成限时版(带 `(turns N)`;2026-10-04 起回合数不随卡等级,spec v7 §1),不再需要那个口子——并回 DURATION_KINDS,让 T1 的反方向防线
# (「在表里就必须有 turns」)重新覆盖它们,补住「利/锋 漏写 turns」这类错的防护
# (与 spec §3 第 20 项记的 `壁` 历史 bug 同一个形状:漏 turns → TurnsLeft=0 →
# 状态施加当场清空,卡面照印)。
# 护甲/破甲(2026-09-08 用户裁定「所有 buff 类必须附带回合数,不存在本场生效」):
# 两条都从 TurnsLeft = -1 改成读 turns,只改一边会让护甲轴的正负两半不对称。
# 引擎兜底是 `Math.Max(1, effect.Turns)`,漏写不会崩、只会静默变成 1 回合 ——
# 正是这张白名单存在的理由(同 `壁` 那个历史 bug 的形状)。
# ⚠ 一格只支持一个 turns 值:护甲在护面、破甲在攻面,全表没有同格并存的行(垚/㙓 都是两面分开)。
# 加速/急速(2026-09-16,水,EffectKind.Haste):Value=百分比(50/100)、Turns=持续回合数,
# 与 CritBuff/DefenseBuff 同型——带数值又带 turns,漏进这张白名单的后果同 `壁` 那次:
# turns 写了没人吃,静默消失。
# 减攻(D1 Task 5,EffectKind.Weaken):Value = 百分点、Turns = 回合,漏写 turns 引擎兜成 1 回合 ——
# 与 ArmorBreak 同型,必须强制要求写。
DURATION_KINDS = {"HealOverTime", "Blind", "Silence", "Reflect", "Charm", "Empower", "CritBuff",
                  "DefenseBuff", "ArmorBreak", "Haste", "Weaken", "Seed"}

# 会被 turns 正则认领的全部 Kind,仅用于「turns 写了但没人吃」这条反向检查。
# 标记(D1 Task 6,Vulnerable)吃 turns 但**不强制**:冰缚写法(`Vulnerable 20` + `pick FrozenByThisCast`)
# 不写 turns,回合数由引擎取目标的冻结回合 —— 所以它在这里、不在 DURATION_KINDS。
# 种(Seed)在 DURATION_KINDS:漏写 turns 引擎兜成 1 回合,与减攻同型。
TURN_TAKING_KINDS = DURATION_KINDS | {"Vulnerable"}

# 支持 targetAll 的 Kind
TARGET_ALL_KINDS = {"HealOverTime", "Blind"}

# 分段数是伤害的修饰,不是独立效果(与 ExecuteKill / ExecuteBonus 同处理)
HIT_COUNT_TOKEN = "HitCount"

# 穿透点数同样是伤害的修饰(2026-08-12,E-b4 T3:替代原先的布尔标记 ignoreArmor)。
# 不挂白名单会被通用正则 `(\w+) (\d+)` 当成一条独立效果 kind=Pierce 落进 chars.json,
# 而 EffectKind 里没有这个值 —— ConfigLoader 会在加载期直接抛 ConfigException。
PIERCE_TOKEN = "Pierce"

# 碾(2026-09-16,土):真伤修饰 —— 本次伤害完全跳过目标护甲(仍吃护盾),与穿透是两档
# (穿透削一部分甲值再算 DR,碾直接跳过整条 DR),同样是伤害的修饰而非独立效果。
# 无数值的布尔标记(与 Backline 同型),写法 `TrueDamage`。
# ⚠ 绝不能进 VALUELESS_EFFECTS:那会让它落成一条 kind="TrueDamage" 的独立效果,
# 而 EffectKind 里没有这个值 —— ConfigLoader 会在加载期直接抛 ConfigException
# (与 PIERCE_TOKEN / Backline 头上那两条注释同一个坑)。
TRUE_DAMAGE_TOKEN = "TrueDamage"

# 镇压(2026-09-16,土):伤害修饰 —— 额外打出自己有效护甲点数的 N%(不走生克、不吃目标减伤),
# 与穿透同型(数值型,写法 `ArmorStrike N`),不是布尔标记(与 TrueDamage 不同型)。
# 不挂白名单会被通用正则 `(\w+) (\d+)` 当成一条独立效果 kind=ArmorStrike 落进 chars.json,
# 而 EffectKind 里没有这个值 —— ConfigLoader 会在加载期直接抛 ConfigException
# (与 PIERCE_TOKEN 头上那条注释同一个坑)。
ARMOR_STRIKE_TOKEN = "ArmorStrike"

# 目标形状的两个带数值 token,同样是伤害的修饰(2026-08-22,spec §9.1)。
# 不挂白名单会被通用正则 `(\w+) (\d+)` 当成独立效果 kind=Shots/ShapePercent 落进 chars.json,
# 而 EffectKind 里没有这两个值 —— ConfigLoader 会在加载期直接抛 ConfigException
# (与 PIERCE_TOKEN / HIT_COUNT_TOKEN 同一个坑)。
# 弹射的跳数(2026-08-25):写成 `Chain N`,是**形状 + 跳数**的合写,不是独立效果。
# ⚠ 与 PIERCE_TOKEN / SHOTS_TOKEN 同一个坑:不挂白名单会被通用正则 `(\w+) (\d+)`
# 当成一条 kind="Chain" 的独立效果落进 chars.json,而 EffectKind 里没有这个值 ——
# ConfigLoader 会在加载期直接抛 ConfigException。(2026-08-25 实测踩过一次。)
CHAIN_TOKEN = "Chain"

SHOTS_TOKEN = "Shots"

# D2-火 E5:`bodyPercent N` —— 特性效果的 Value = 本面本体首条 DamageSingle × N%(连爆)。只挂本格唯一的 DamageSingle;
# 不挂白名单会被通用正则当成 kind="bodyPercent" 的独立效果。
BODY_PERCENT_TOKEN = "bodyPercent"

# D2-火 N12:`battles N` —— 它前面最近的那条效果本场不执行,登记为之后 N 场的开局效果(炎炎、星星之火)。
# 一格里可以有同 Kind 的两条(星星之火两条 BurnAll),所以按位置挂,不按 Kind 挂。
BATTLES_TOKEN = "battles"
SHAPE_PERCENT_TOKEN = "ShapePercent"

# ---- D1 Task 3:本字修饰器(附录 M1–M3)的修饰 token ----
# 写法:`Amplify 30` + `scope Damage` + `if Burning`;`Reshape` + `shape Row` + `shapePercent 50` + `hits 2`
# + `hitPercent 60` + `forceCrit` + `armorIgnore 50` + `shieldStrike 40` + `armorStrike 300`。
# 小写开头是刻意的:与既有的大写修饰位(`ShapePercent N` / `ArmorStrike N`)区分,两套互不吞。
# 带数值的那几个必须挂进通用循环的跳过名单,否则会被 `(\w+) (\d+)` 当成独立效果 kind="hits" 落进字表
# (与 PIERCE_TOKEN 头上那条注释同一个坑)。
AMP_SCOPE_TOKEN = "scope"
ONLY_IF_TOKEN = "if"
RESHAPE_SHAPE_TOKEN = "shape"
FORCE_CRIT_TOKEN = "forceCrit"
# token → chars.json 字段
DAMAGE_MARKER_VALUE_TOKENS = {
    "shapePercent": "shapePercent",
    "hits": "hitCount",
    "hitPercent": "hitPercent",
    "armorIgnore": "armorIgnorePercent",
    "shieldStrike": "shieldStrikePercent",
    "armorStrike": "armorStrikePercent",
}
# 与 Core 的 AmpScope / DamageCondition / TargetArea 枚举名一致;写错直接报错,不静默落成缺省
AMP_SCOPES = {"Damage", "Heal", "Shield", "Seed", "Counter", "All", "Burn"}   # Burn:灼的火力(D2-火 G3)
CONDITIONS = {"Burning", "Bleeding", "Controlled", "ArmorBroken", "Slowed", "Frozen",
              "TargetHpAbove70", "TargetHpBelow30", "PlayerHpBelow50", "PlayerHasArmor",
              "FirstCastThisTurn", "Countering",
              "PlayerHpAbove70", "HasSummon"}   # D2-火 Task 1(附录 E1)
# D1 Task 4:Augment 叠加修饰器:`Augment 1` + `of Block` + `field Count`。`Augment N` 走通用循环成 kind=Augment,
# `of X` / `field Y` 在 _attach_modifier_tokens 里挂上去(一条 Augment 配一对 of/field,按出现顺序对应;缺哪个都报错)。
AUGMENT_OF_TOKEN = "of"
AUGMENT_FIELD_TOKEN = "field"
AUGMENT_FIELDS = {"Count", "Turns", "Shots"}
RESHAPE_SHAPES = {"Row", "Adjacent", "Column", "Scatter", "Chain", "All"}

# D1 Task 5:效果目标选择器 `pick X`、条件门 `if X`(非 Amplify)、不减层 `keep`。
# 与 Core 的 EffectPickRules.Supports 同一张名单;写在别的效果上引擎会静默忽略,所以管线拦下。
ENEMY_PICK_KINDS = {"BurnSingle", "Bleed", "Freeze", "Slow", "ArmorBreak", "Blind", "Weaken",
                    "BurnSettleNow", "Detonate", "Seed", "Vulnerable"}
ENEMY_PICKS = {"All", "Random", "HitTargets", "MostBurn", "FrozenByThisCast",
               "Row", "Adjacent", "BurnedByThisCast"}   # D2-火 Task 1(附录 E2)
# D2-火 E3:Reshape 带敌方侧选择器 = 重选目标(本面没有伤害时把主目标效果换成该选择器;烈风)。
# 不进 ENEMY_PICK_KINDS:那张表还管「池条目落到不选目标的面时补 pick All」(extract_traits._retarget_to_all),
# Reshape 是修饰器,不该被补。只作为 `pick` 的挂载点。
RESHAPE_PICK_KINDS = {"Reshape"}
# D1 Task 7:我方侧选择器,各只给一个 kind(与 Core 的 EffectPickRules.Allows 同一张表)。
# 它们也要进 PICK_KINDS —— `pick` token 按位置挂到前一条 PICK_KINDS 效果上。
# D2-0 Task 2:嘲讽 `Taunt N`(N = 回合数,0 = 本场)必须写 pick,落点 Self / SummonedThisCast / AllSummons。
ALLY_PICKS = {"Self": {"Cleanse", "Taunt"}, "SummonedThisCast": {"Endure", "Taunt"}, "AllSummons": {"Taunt"}}
ALLY_PICK_KINDS = set().union(*ALLY_PICKS.values())
PICK_KINDS = ENEMY_PICK_KINDS | ALLY_PICK_KINDS | RESHAPE_PICK_KINDS
PICKS = ENEMY_PICKS | set(ALLY_PICKS)
PICK_TOKEN = "pick"
KEEP_TOKEN = "keep"
# D1 Task 9:附着载体 `rider Burn`(烟熏)。与 Core 的 ConfigLoader 同一张表:目前只有 Blind 能附着、只认 Burn 载体。
# 附着的致盲随灼存续,不写 turns(下面的 missing_turns 检查对它放行)。
RIDER_TOKEN = "rider"
RIDER_CARRIERS = {"Burn"}
RIDER_KINDS = {"Blind"}


def _positional_hosts(config, effects):
    """认选择器的效果(PICK_KINDS)在配置格里的位置:[(pos, effect)],按位置升序。
    修饰 token 挂**它前面最近的**那条效果(`Slow 1` + `pick Random` 的 pick 属于 Slow)。
    同一格里同 Kind 出现两次时,后一条从前一条之后开始找位置。"""
    found, cursor = [], {}
    for e in effects:
        kind = e["kind"]
        if kind not in PICK_KINDS:
            continue
        needles = [f"`{kind} ", f"`{kind}`"] + (["`DetonateAll`"] if kind == "Detonate" else [])
        start = cursor.get(kind, 0)
        hits = [pos for pos in (config.find(n, start) for n in needles) if pos >= 0]
        if not hits:
            continue
        pos = min(hits)
        cursor[kind] = pos + 1
        found.append((pos, e))
    return sorted(found, key=lambda t: t[0])


def _attach_positional(config, char, effects, consumed, token, field, parse, allowed_kinds=None):
    """把每个 `` `token ...` `` 挂到它前面最近的 PICK_KINDS 效果上。parse(raw) 返回要写进字段的值。"""
    pattern = rf"`{token}(?: (\w+))?`"
    hosts = _positional_hosts(config, effects)
    seen = set()
    for m in re.finditer(pattern, config):
        consumed.add(token)
        before = [e for pos, e in hosts if pos < m.start()]
        if not before:
            raise ValueError(
                f"{char}:配置格「{config}」写了 `{token}`,但它前面没有可挂的效果"
                f"(只认 {sorted(PICK_KINDS)})—— 它会静默消失。")
        host = before[-1]
        if allowed_kinds is not None and host["kind"] not in allowed_kinds:
            raise ValueError(f"{char}:`{token}` 只能挂在 {sorted(allowed_kinds)} 上,当前挂到了 {host['kind']}")
        if id(host) in seen or field in host:
            raise ValueError(f"{char}:配置格「{config}」里同一条 {host['kind']} 写了多个 `{token}`,只能有一个")
        seen.add(id(host))
        host[field] = parse(m.group(1))


def _attach_battles(config, char, effects, consumed):
    """`battles N`(D2-火 N12)→ 它前面最近的那条效果的 openingBattles。位置按 `` `Kind `` 在格里的出现顺序认,
    同 Kind 多条时后一条从前一条之后找(与 _positional_hosts 同手法,但不限 PICK_KINDS)。"""
    found = list(re.finditer(rf"`{BATTLES_TOKEN} (\d+)`", config))
    if not found:
        return
    consumed.add(BATTLES_TOKEN)
    hosts, cursor = [], {}
    for e in effects:
        kind = e["kind"]
        start = cursor.get(kind, 0)
        hits = [pos for pos in (config.find(n, start) for n in (f"`{kind} ", f"`{kind}`")) if pos >= 0]
        if not hits:
            continue
        cursor[kind] = min(hits) + 1
        hosts.append((min(hits), e))
    hosts.sort(key=lambda t: t[0])
    for m in found:
        before = [e for pos, e in hosts if pos < m.start()]
        if not before:
            raise ValueError(f"{char}:配置格「{config}」写了 `battles`,但它前面没有可登记的效果 —— 它会静默消失。")
        host = before[-1]
        if "openingBattles" in host or int(m.group(1)) < 1:
            raise ValueError(f"{char}:配置格「{config}」的 `battles` 须 ≥ 1,且一条效果只能写一个")
        host["openingBattles"] = int(m.group(1))


def _attach_modifier_tokens(config, char, effects, consumed):
    """D1 Task 3:把修饰 token 挂到同格的修饰器上(与 `turns` / `Pierce` 同一套「挂在本格效果上」的机制)。

    - `scope X` / `if X` → 本格的 Amplify(没有 Amplify 就报错:那个条件会静默消失)。
    - `shape X`、DAMAGE_MARKER_VALUE_TOKENS、`forceCrit` → 本格的 Reshape;本格没有 Reshape 时
      挂到 DamageSingle 上(伤害标记也可以直接写在本体伤害上);两者都没有就报错。
    - 名字不在枚举表里的值(`scope Bogus` / `if Nope` / `shape Ring`)报错。"""
    amps = [e for e in effects if e["kind"] == "Amplify"]
    hosts = [e for e in effects if e["kind"] == "Reshape"] or \
        [e for e in effects if e["kind"] == "DamageSingle"]

    def need(host_list, token, what):
        if not host_list:
            raise ValueError(f"{char}:配置格「{config}」写了修饰 token `{token}`,但本格没有{what} —— 它会静默消失。")

    for token, field, allowed in ((AMP_SCOPE_TOKEN, "scope", AMP_SCOPES),
                                  (ONLY_IF_TOKEN, "onlyIf", CONDITIONS)):
        found = re.search(rf"`{token} (\w+)`", config)
        if not found:
            continue
        if token == ONLY_IF_TOKEN and not amps:
            continue   # 没有 Amplify:条件门挂在前一条敌方侧效果上(下面的 _attach_positional)
        consumed.add(token)
        # 同格多个 scope / if:只认第一个会让后面的静默消失 —— 一格一条 Amplify 一个条件,多了就拆格
        if len(re.findall(rf"`{token} \w+`", config)) > 1:
            raise ValueError(f"{char}:配置格「{config}」写了多个 `{token}`,只能有一个(多条加成请分开写)")
        if found.group(1) not in allowed:
            raise ValueError(f"{char}:`{token} {found.group(1)}` 的取值未知,只认 {sorted(allowed)}")
        need(amps, token, " Amplify")
        for e in amps:
            e[field] = found.group(1)

    def _parse_condition(raw):
        if raw not in CONDITIONS:
            raise ValueError(f"{char}:`if {raw}` 的取值未知,只认 {sorted(CONDITIONS)}")
        return raw

    def _parse_pick(raw):
        if raw not in PICKS:
            raise ValueError(f"{char}:`pick {raw}` 的取值未知,只认 {sorted(PICKS)}")
        return raw

    if not amps:
        _attach_positional(config, char, effects, consumed, ONLY_IF_TOKEN, "onlyIf", _parse_condition)
    _attach_positional(config, char, effects, consumed, PICK_TOKEN, "pick", _parse_pick)
    _attach_positional(config, char, effects, consumed, KEEP_TOKEN, "keepStacks", lambda _raw: True,
                       allowed_kinds={"BurnSettleNow"})

    def _parse_rider(raw):
        if raw not in RIDER_CARRIERS:
            raise ValueError(f"{char}:`rider {raw}` 的载体未知,只认 {sorted(RIDER_CARRIERS)}")
        return raw

    _attach_positional(config, char, effects, consumed, RIDER_TOKEN, "riderOf", _parse_rider,
                       allowed_kinds=RIDER_KINDS)

    augments = [e for e in effects if e["kind"] == "Augment"]
    for token, field, allowed in ((AUGMENT_OF_TOKEN, "augmentKind", None),
                                  (AUGMENT_FIELD_TOKEN, "augmentField", AUGMENT_FIELDS)):
        found = re.findall(rf"`{token} (\w+)`", config)
        if found:
            consumed.add(token)
        if len(found) > len(augments) and augments:
            raise ValueError(f"{char}:配置格「{config}」写了多个 `{token}`,一条 Augment 只能配一个")
        if found and not augments:
            raise ValueError(f"{char}:配置格「{config}」写了 `{token} {found[0]}`,但本格没有 Augment —— 它会静默消失。")
        if augments and len(found) < len(augments):
            raise ValueError(f"{char}:配置格「{config}」的 Augment 缺 `{token} X`")
        # 一格可有多条 Augment(冰锁:冻结、减速各 +1),of / field 按出现顺序一一对应
        for e, value in zip(augments, found):
            if allowed is not None and value not in allowed:
                raise ValueError(f"{char}:`{token} {value}` 的取值未知,只认 {sorted(allowed)}")
            e[field] = value

    shape = re.search(rf"`{RESHAPE_SHAPE_TOKEN} (\w+)`", config)
    if shape:
        consumed.add(RESHAPE_SHAPE_TOKEN)
        if shape.group(1) not in RESHAPE_SHAPES:
            raise ValueError(f"{char}:`shape {shape.group(1)}` 的形状未知,只认 {sorted(RESHAPE_SHAPES)}")
        need(hosts, RESHAPE_SHAPE_TOKEN, " Reshape 或 DamageSingle")
        for e in hosts:
            e["shape"] = shape.group(1)
    for token, field in DAMAGE_MARKER_VALUE_TOKENS.items():
        found = re.search(rf"`{token} (\d+)`", config)
        if not found:
            continue
        consumed.add(token)
        need(hosts, token, " Reshape 或 DamageSingle")
        for e in hosts:
            e[field] = int(found.group(1))
    if f"`{FORCE_CRIT_TOKEN}`" in config:
        consumed.add(FORCE_CRIT_TOKEN)
        need(hosts, FORCE_CRIT_TOKEN, " Reshape 或 DamageSingle")
        for e in hosts:
            e["forceCrit"] = True


def _raise_unconsumed_tokens(char, config, unknown):
    """消费记账收尾(2026-09-07,P2 Task 1):把「哪个 token 没人认」翻成能照着改的报错。

    extract_values 是一串 re.search 找已知 token,认不得的一律静默忽略 —— 而字表数据
    全靠这里落地。项目已栽过一次(「手写映射表认不得的标记会无声消失」)。"""
    names = "、".join(f"`{tok}`" for tok in sorted(unknown))
    raise ValueError(
        f"{char}:配置格「{config}」里的 {names} 没有被 _parse_effects 的任何分支消费,"
        "会静默从 chars.json 里消失。两条修法二选一——"
        "① 这个 token 本该有效果:去 extract_values.py 补一个消费分支"
        "(无数值的布尔标记挂进 VALUELESS_EFFECTS;带参数的仿照 SUMMON_PASSIVE/DoubleVs "
        "等既有分支写,并在分支里 consumed.add(token));"
        "② 这个 token 是详表笔误或多余标记:把它从配置格里删掉。")


def extract(markdown):
    """详表全文 → {字: {element, rarity, effects, pinyin?, gloss?}},只收标 ✅ 的字。"""
    body = markdown.split("## 二 · 火系")[1].split("## 七 · 引擎扩展")[0]
    parts = re.split(r"^## [三四五六] · (\S+?)系", body, flags=re.M)
    sections = [("火", parts[0])] + [(parts[i], parts[i + 1])
                                     for i in range(1, len(parts), 2)]
    result = {}
    for element, section in sections:
        for line in section.split("\n"):
            entry = _parse_row(line, element)
            if entry:
                result[entry[0]] = entry[1]
    _merge_readings(result, extract_readings(markdown))
    return result


def extract_readings(markdown):
    """第九节「拼音与释义」→ {字: (拼音, 释义)}。没有这一节就返回空表。

    单独成节而不是给五张逐字表各加两列:那五张表的列数本来就不齐(水/土 多一格
    「攻击效果配置」),而 _parse_row 靠「哪一格带反引号 / 哪一格是稀有度」认列 ——
    往里塞自由文本列是给那套启发式添反例。这一节是纯查找表,与数值无关。"""
    if "## 九 · 拼音与释义" not in markdown:
        return {}
    section = markdown.split("## 九 · 拼音与释义")[1].split("\n## ")[0]
    readings = {}
    for line in section.split("\n"):
        if not line.startswith("| ") or line.startswith("|---"):
            continue
        cells = [c.strip() for c in line.split("|")[1:-1]]
        if len(cells) != 3 or len(cells[0]) != 1:
            continue  # 表头「| 字 | 拼音 | 释义 |」在这里被滤掉
        readings[cells[0]] = (cells[1], cells[2])
    return readings


def _merge_readings(values, readings):
    """把拼音/释义并进已抽出的字条目。

    ⚠ 只并**已在 values 里**的字:第九节列的是全字表,而 values 只含标 ✅ 的字 ——
    拿第九节反过来建条目会把移出字表的字重新塞回 chars.json。
    空串不写键:export_chars 只在真值时落地,这里也别留空键(恒等性)。"""
    for char, spec in values.items():
        pinyin, gloss = readings.get(char, ("", ""))
        if pinyin:
            spec["pinyin"] = pinyin
        if gloss:
            spec["gloss"] = gloss


def _parse_row(line, element):
    if not line.startswith("| ") or line.startswith("|---"):
        return None
    cells = [c.strip() for c in line.split("|")[1:-1]]
    if len(cells) < 4 or len(cells[0]) != 1:
        return None
    char = cells[0]
    rarity = next((RARITY[c] for c in cells if c in RARITY), None)
    impl = next((c for c in cells if c.startswith("✅") or c.startswith("⚠")), None)
    if not rarity or not impl or not impl.startswith("✅"):
        return None

    config = next((c for c in cells if "`" in c), "")
    effects = _parse_effects(config, char)
    if not effects:
        return None
    entry = {"element": ELEMENT[element], "rarity": rarity, "effects": effects}

    # 双方向字(2026-09-02,Task 10):水/土两系的表在「效果配置」右边多挂了一格
    # 「攻击效果配置」——同一行的第二个反引号格,语法与治疗面完全一样,复用
    # _parse_effects。⚠ 只对 DUAL_DIRECTION_ELEMENTS 生效:其余系的「实现」备注列
    # 里常年散落着引用 token 名的反引号(如「装配 `DoubleVsControlled`」),不加这道
    # 元素闸,通用的「第二个反引号格」判据会把那些说明文字误当成攻击效果去解析。
    if element in DUAL_DIRECTION_ELEMENTS:
        # 排除「实现」那一格(2026-09-08):它的备注文字里常年散落着引用 token 名的反引号
        # (如「装配 `DoubleVsControlled`」),不排掉的话「第二个反引号格」会把说明文字
        # 当成攻击效果解析 —— 这正是这道元素闸原先要防的东西,现在按格排除更直接,
        # 金系才能安全地加进来。
        # 2026-09-27(木系双面):「实现」格**之后**的续写格同理要排掉 —— 详表里有行在
        # 实现格后面又用 `|` 接了一段沿革(如 花/𣛧),那一段同样散落着反引号。
        backticked = [c for c in cells[:cells.index(impl)] if "`" in c]
        if len(backticked) > 1:
            attack_effects = _parse_effects(backticked[1], char)
            if attack_effects:
                entry["attackEffects"] = attack_effects
    return char, entry


SAPLING_COUNT_TOKEN = "count"
PERCENT_OF_MAX_TOKEN = "pct"


def _attach_ally_tokens(config, char, effects, consumed):
    """D1 Task 7:`count N` → 本格 SummonSapling 的只数;`pct` → 本格 HealSummons 按最大生命百分比;
    我方侧选择器只能配它自己的 kind,我方侧效果不带条件门(Core 的 ConfigLoader 同样拦)。"""
    for token, field, host_kind, value_of in (
            (SAPLING_COUNT_TOKEN, "count", "SummonSapling", lambda m: int(m.group(1))),
            (PERCENT_OF_MAX_TOKEN, "percentOfMax", "HealSummons", lambda m: True)):
        pattern = rf"`{token} (\d+)`" if field == "count" else rf"`{token}`"
        found = list(re.finditer(pattern, config))
        if not found:
            continue
        consumed.add(token)
        hosts = [e for e in effects if e["kind"] == host_kind]
        if not hosts:
            raise ValueError(f"{char}:配置格「{config}」写了 `{token}`,但本格没有 {host_kind} —— 它会静默消失。")
        if len(found) > 1 or len(hosts) > 1:
            raise ValueError(f"{char}:配置格「{config}」的 `{token}` 只能配一条 {host_kind}")
        hosts[0][field] = value_of(found[0])
    for e in effects:
        pick = e.get("pick")
        if pick is None:
            continue
        ally_kinds = ALLY_PICKS.get(pick)
        if ally_kinds is not None and e["kind"] not in ally_kinds:
            raise ValueError(f"{char}:`pick {pick}` 只能挂在 {sorted(ally_kinds)} 上,当前挂到了 {e['kind']}")
        if ally_kinds is None and e["kind"] not in ENEMY_PICK_KINDS | RESHAPE_PICK_KINDS:
            raise ValueError(f"{char}:{e['kind']} 不认敌方侧选择器 `pick {pick}`(只认 "
                             f"{sorted(p for p, ks in ALLY_PICKS.items() if e['kind'] in ks)})")
    for e in effects:
        # 保命必须写 `pick SummonedThisCast`(Ruling 10):缺省写法引擎选不到召唤物,会静默空转
        if e["kind"] == "Endure" and e.get("pick") != "SummonedThisCast":
            raise ValueError(f"{char}:`Endure` 必须配 `pick SummonedThisCast`(落点是本次召出的召唤物)")
        # 嘲讽同理:必须写 pick Self / SummonedThisCast / AllSummons(Primary 写法 ConfigLoader 会拒绝)
        if e["kind"] == "Taunt" and e.get("pick") not in ALLY_PICKS:
            raise ValueError(f"{char}:`Taunt` 必须配 `pick Self` / `pick SummonedThisCast` / `pick AllSummons`")
        if "onlyIf" in e and e["kind"] in ALLY_PICK_KINDS:
            raise ValueError(f"{char}:{e['kind']} 不能带条件门 `if`(只给 Amplify 与敌方侧效果)")


def _parse_effects(config, char):
    """「`DamageSingle 30` + `All` + `BurnAll 4`」→ [{kind, value}, …];召唤单独处理。

    char 只被召唤分支用到(当 summonChar),其余 kind 一概不看第二个参数。"""
    # 消费记账(2026-09-07,P2 Task 1):本函数是一串 re.search 找已知 token,
    # 认不得的一律静默忽略 —— 而字表数据全靠这里落地。项目已栽过一次
    # (「手写映射表认不得的标记会无声消失」)。所以结尾对一遍账:
    # 配置格里出现过的 token 减去被消费的,剩下的一律报错。
    all_tokens = set(re.findall(r"`(\w+)", config))
    consumed = set()
    effects = []

    summon = re.search(r"`Summon (\d+)`\((\d+) 血/攻 (\d+)\)", config)
    if summon:
        consumed.add("Summon")
        # summonChar = 施法字本身(2026-08-15):场上显示的就是这个字。原先填那一节的
        # 五行,全表召唤物都叫「木」/「火」,一排下来分不出哪只是梅哪只是荆。
        # 增补平面字(𣛧)的 PUA 代理换在 export_chars._output_id 那一层,与 id 同口径。
        effect = {"kind": "Summon", "value": int(summon.group(2)),
                  "count": int(summon.group(1)),
                  "attack": int(summon.group(3)), "summonChar": char}
        # 桂 的护盾发给全场召唤物,不是这只自带的 —— 平铺在 effect 上,不进 passive
        shield = re.search(r"`SummonShield (\d+)`", config)
        if shield:
            effect["summonShield"] = int(shield.group(1))
            consumed.add("SummonShield")
        # 塔(2026-09-08):这几只**自带**的护甲,与上面发给全场的护盾不是一回事。
        # 落地是往召唤物自己的状态袋挂 DefenseBuff,由 SummonState.EffectiveDefense 读走。
        # 不进 DURATION_KINDS:它随单位存在(TurnsLeft = -1),不是场上飘着的玩家 buff。
        summon_def = re.search(r"`SummonDefense (\d+)`", config)
        if summon_def:
            effect["summonDefense"] = int(summon_def.group(1))
            consumed.add("SummonDefense")
        passive = {}
        for token, field in SUMMON_PASSIVE.items():
            found = re.search(rf"`{token} (\d+)`", config)
            if found:
                passive[field] = int(found.group(1))
                consumed.add(token)
        if "`OnHitBurnAll`" in config:      # 无数值的布尔标记(烓)
            passive["onHitBurnAll"] = True
            consumed.add("OnHitBurnAll")
        if "`Ranged`" in config:            # 无数值的布尔标记(2026-08-20,灶/烓)
            passive["ranged"] = True
            consumed.add("Ranged")
        if "`Taunt`" in config:             # 无数值的布尔标记(2026-08-25,荆/堡)
            passive["taunt"] = True
            consumed.add("Taunt")
        # 目标形状(2026-08-22,spec §9.1):召唤物自动攻击也能带形状,与伤害侧同一套 token,
        # 落进 passive 的 shape/shots/shapePercent —— **不**新增独立 effect(与 Ranged 同处理)。
        # Chain/ShapePercent/Shots 这三个带数值的 token 不会被下面的通用循环二次吞掉
        # (2026-09-07,P2 Task 2 之后本分支不再提前 return,但通用循环本身已把
        # SHOTS_TOKEN/SHAPE_PERCENT_TOKEN/CHAIN_TOKEN 挂了白名单跳过,见那三行 continue)。
        for token in ("Row", "Adjacent", "Column"):
            if f"`{token}`" in config:
                passive["shape"] = token
                consumed.add(token)
                break
        chain = re.search(rf"`{CHAIN_TOKEN} (\d+)`", config)
        if chain:
            passive["shape"] = "Chain"
            passive["shots"] = int(chain.group(1))
            consumed.add(CHAIN_TOKEN)
        percent = re.search(rf"`{SHAPE_PERCENT_TOKEN} (\d+)`", config)
        if percent:
            passive["shapePercent"] = int(percent.group(1))
            consumed.add(SHAPE_PERCENT_TOKEN)
        shots = re.search(rf"`{SHOTS_TOKEN} (\d+)`", config)
        if shots:
            passive.setdefault("shape", "Scatter")   # 同上:Chain 也用 Shots
            passive["shots"] = int(shots.group(1))
            consumed.add(SHOTS_TOKEN)
        if passive:
            effect["passive"] = passive
        effects.append(effect)
        # 召唤行上的非召唤 token(2026-09-07,P2 Task 1 第 3 类静默丢失,Task 2 落地):
        # 这条分支曾经在通用循环之前就 return,任何不在上面这张单子里的 token 都会被无声
        # 吞掉(如土系召唤字的入场护盾 `Shield N`)。现在不再提前 return —— 追加完召唤
        # 效果后继续往下走通用循环,让同行的非召唤 token 也能被解析成独立效果。
        # unknown 记账挪到函数末尾统一做一次(通用循环消费的 token 也要计入)。

    # 通用循环:召唤行(如果匹配了上面的 Summon 分支)也会走到这里。SUMMON_HANDLED
    # 收录「已经在召唤分支里消费过数值的 token 名」——`Summon` 本身、桂的 SummonShield、
    # 以及全部 SUMMON_PASSIVE token——不跳过的话会被这条通用正则重新匹配一遍,产出
    # 一条残缺的独立效果(比如缺 count/attack/summonChar 的 kind="Summon")。
    SUMMON_HANDLED = {"Summon", "SummonShield", "SummonDefense"} | set(SUMMON_PASSIVE)
    for kind, value in re.findall(r"`(\w+) (\d+)`", config):
        if kind in SUMMON_HANDLED:
            continue
        if kind == "turns":
            # 带反引号的 `turns N`(池表 / 特性表的写法)是回合数修饰,下面统一用 turns 正则挂到
            # 吃回合的效果上;不是一条 kind=turns 的效果(D1 Task 13)
            consumed.add(kind)
            continue
        consumed.add(kind)
        if kind in EXECUTE_TOKENS:
            continue  # 斩杀是修饰而非效果,下面统一挂到伤害上
        if kind == HIT_COUNT_TOKEN:
            continue  # 分段数是修饰而非效果,下面统一挂到伤害上
        if kind == PIERCE_TOKEN:
            continue  # 穿透点数是修饰而非效果,下面统一挂到伤害上
        if kind == ARMOR_STRIKE_TOKEN:
            continue  # 镇压百分比是修饰而非效果,下面统一挂到伤害上
        if kind in (SHOTS_TOKEN, SHAPE_PERCENT_TOKEN, CHAIN_TOKEN):
            continue  # 目标形状的修饰,下面统一挂到伤害上
        if kind in DAMAGE_MARKER_VALUE_TOKENS:
            continue  # 修饰器 / 伤害标记的数值(D1 Task 3),由 _attach_modifier_tokens 挂
        if kind == SAPLING_COUNT_TOKEN:
            continue  # 幼苗只数(D1 Task 7),下面挂到 SummonSapling 上
        if kind == BODY_PERCENT_TOKEN:
            continue  # 本体百分比(D2-火 E5),下面挂到 DamageSingle 上
        if kind == BATTLES_TOKEN:
            continue  # 开局登记场数(D2-火 N12),下面按位置挂到前一条效果上
        # 全体伤害(spec v7 §11.6):DamageAll 已退役,EffectKind 里没有这个值了 ——
        # 落进 chars.json 会让 ConfigLoader 加载期报错,这里先在管线大声拦下并给出改法。
        if kind == "DamageAll":
            raise ValueError(
                f"{char}:`DamageAll {value}` 已退役(spec v7 §11.6),"
                f"改写成 `DamageSingle {value}` + `All`。")
        effect = {"kind": kind, "value": int(value)}
        if kind == "DispelEach":       # 全体各驱散 N 条(淡)
            effect["kind"] = "Dispel"
            effect["targetAll"] = True
        # 条件加成(2026-08-25 由 DoubleVsBurning 泛化成四选一)。写成条件名而不是布尔位,
        # 新增条件时只动这张表,不用再加一个平行的 bool —— 与 EffectKind 同口径。
        if _is_damage(kind):
            for token in ("Burning", "Bleeding", "Controlled", "ArmorBroken"):
                if f"`DoubleVs{token}`" in config:
                    effect["doubleVs"] = token
                    consumed.add(f"DoubleVs{token}")
                    break
        # 偷袭(`Backline`)2026-09-30 取消:我方字卡不再有前后排限制,这个修饰位随之删除。
        # 不留解析分支是刻意的 —— 详表里再写 `Backline` 会落进 _raise_unconsumed_tokens 大声报错,
        # 而不是被悄悄吞掉、生成一张「以为能偷袭」的字。
        # 碾(2026-09-16,土):跳过整条 DR,单体/全体两种伤害都能挂(BattleEngine 的
        # DamageSingle 分支对形状展开的每个目标都接了 effect.TrueDamage → bypassDefense)。
        if _is_damage(kind) and f"`{TRUE_DAMAGE_TOKEN}`" in config:
            effect["trueDamage"] = True
            consumed.add(TRUE_DAMAGE_TOKEN)
        # 目标形状(2026-08-22,spec §9.1):修饰单体直伤,与 Backline / Pierce / HitCount 同为**修饰位**。
        # ⚠ 绝不能进 VALUELESS_EFFECTS:那会让它落成一条 kind="Row" 的独立效果,
        #   而 EffectKind 里没有这个值,ConfigLoader 会在加载期直接抛 ConfigException
        #   (与 PIERCE_TOKEN / Backline 头上那两条注释同一个坑)。
        # HealSelf 也认(2026-09-16,水,治疗弹射「海/澡」对偶):此前这里只判 DamageSingle,
        # 治疗面写 `Chain N` + `ShapePercent N` 会被上面的通用循环吞进 consumed、却从没被
        # 挂到 HealSelf 这条 effect 上——config 里的 token 静默消失,不报错也看不出来。
        # 全体 `All`(spec v7 §3.2 / §11.6)同为形状修饰:原 `DamageAll N` 改写成 `DamageSingle N` + `All`。
        if kind in ("DamageSingle", "HealSelf"):
            for token in ("Row", "Adjacent", "Column", "All"):
                if f"`{token}`" in config:
                    effect["shape"] = token
                    consumed.add(token)
                    break
            chain = re.search(rf"`{CHAIN_TOKEN} (\d+)`", config)
            if chain:                       # `Chain N` = 形状 Chain + 跳数 N
                effect["shape"] = "Chain"
                effect["shots"] = int(chain.group(1))
                consumed.add(CHAIN_TOKEN)
            percent = re.search(rf"`{SHAPE_PERCENT_TOKEN} (\d+)`", config)
            if percent:
                effect["shapePercent"] = int(percent.group(1))
                consumed.add(SHAPE_PERCENT_TOKEN)
            shots = re.search(rf"`{SHOTS_TOKEN} (\d+)`", config)
            if shots:
                # 2026-08-25:Chain 也用 Shots 表示跳数,所以 Shots 不再无条件蕴含 Scatter ——
                # 只有没显式写形状时才当连发(保住 `Shots N` 单写即连发的旧口径)
                effect.setdefault("shape", "Scatter")
                effect["shots"] = int(shots.group(1))
                consumed.add(SHOTS_TOKEN)
        # 群体护盾(2026-09-05)也认这个修饰:引擎侧 ShieldAll 与 Shield 走同一个豁免桶判断,
        # 只认单体会让「将来配一张带 PersistOnce 的群盾字」在管线这一层静默丢掉那个标志
        if kind in ("Shield", "ShieldAll") and "PersistOnce" in config:
            effect["persistOnce"] = True
            consumed.add("PersistOnce")
        effects.append(effect)

    for token, spec in VALUELESS_EFFECTS.items():
        if f"`{token}`" in config:
            effects.append(dict(spec))
            consumed.add(token)

    for token, kills in EXECUTE_TOKENS.items():
        found = re.search(rf"`{token} (\d+)`", config)
        if not found:
            continue
        consumed.add(token)
        for effect in effects:
            if _is_damage(effect["kind"]):
                effect["executeBelowPercent"] = int(found.group(1))
                effect["executeKills"] = kills

    hit_count = re.search(rf"`{HIT_COUNT_TOKEN} (\d+)`", config)
    if hit_count:
        consumed.add(HIT_COUNT_TOKEN)
        for effect in effects:
            if _is_damage(effect["kind"]):
                effect["hitCount"] = int(hit_count.group(1))

    pierce = re.search(rf"`{PIERCE_TOKEN} (\d+)`", config)
    if pierce:
        consumed.add(PIERCE_TOKEN)
        for effect in effects:
            if _is_damage(effect["kind"]):
                effect["pierce"] = int(pierce.group(1))

    armor_strike = re.search(rf"`{ARMOR_STRIKE_TOKEN} (\d+)`", config)
    if armor_strike:
        consumed.add(ARMOR_STRIKE_TOKEN)
        for effect in effects:
            if _is_damage(effect["kind"]):
                effect["armorStrikePercent"] = int(armor_strike.group(1))

    body_percent = re.findall(rf"`{BODY_PERCENT_TOKEN} (\d+)`", config)
    if body_percent:
        consumed.add(BODY_PERCENT_TOKEN)
        hosts = [e for e in effects if e["kind"] == "DamageSingle"]
        if len(body_percent) > 1 or len(hosts) != 1:
            raise ValueError(f"{char}:配置格「{config}」的 `bodyPercent` 只能配本格唯一的一条 DamageSingle")
        hosts[0]["bodyPercent"] = int(body_percent[0])

    _attach_battles(config, char, effects, consumed)
    _attach_modifier_tokens(config, char, effects, consumed)
    _attach_ally_tokens(config, char, effects, consumed)

    turns = re.search(r"turns (\d+)", config)
    for effect in effects:
        if effect["kind"] in TURN_TAKING_KINDS:
            if turns:
                effect["turns"] = int(turns.group(1))
        if effect["kind"] in TARGET_ALL_KINDS and "targetAll" in config:
            effect["targetAll"] = True
    # turns 挂在不吃它的 kind 上(2026-09-07,P2 Task 1 第 2 类静默丢失):上面这段循环
    # 只会把 turns 写进 TURN_TAKING_KINDS 里的效果,若本行压根没有一个吃
    # turns 的效果,这个回合数就静默消失,卡面却可能仍印着它。
    if turns and not any(e["kind"] in TURN_TAKING_KINDS for e in effects):
        raise ValueError(
            f"{char}:配置里写了 turns {turns.group(1)},但本行没有任何吃 turns 的效果"
            f"(TURN_TAKING_KINDS = {sorted(TURN_TAKING_KINDS)})—— 那个回合数会静默消失。"
            "要么把这个 kind 加进 DURATION_KINDS,要么删掉 turns。")

    # 反方向(2026-09-07,追加):DURATION_KINDS 里的效果**没拿到** turns 也要报错——
    # 这才是 spec §1.5 第 20 项描述的那个历史 bug(`壁` 攻面写了 `Reflect 30` 却漏了
    # `(turns N)`,TurnsLeft = 0 会被 TickTurns 当场清掉,卡面照印着这个效果,状态施加
    # 那一刻就已经失效)。比「turns 挂错 kind」更常见,是详表最容易漏写的一种笔误。
    missing_turns = [e["kind"] for e in effects
                     if "turns" not in e and "riderOf" not in e   # 附着的效果随载体存续(D1 Task 9)
                     and (e["kind"] in DURATION_KINDS
                          # 标记(Vulnerable):只有冰缚写法(pick FrozenByThisCast)可省 turns,
                          # 回合数由引擎取目标的冻结回合;其余缺 turns 同减攻 / 种报错
                          or (e["kind"] == "Vulnerable" and e.get("pick") != "FrozenByThisCast"))]
    if missing_turns:
        raise ValueError(
            f"{char}:`{missing_turns[0]}` 是需要 turns 的效果(在 DURATION_KINDS 里),"
            f"但配置格「{config}」没写 turns —— TurnsLeft 会是 0,状态施加当场就被 "
            "TickTurns 清空,卡面还照印着这个效果,实际大概率不生效(`壁` 攻面漏过一次)。"
            "两条修法二选一——① 这是详表笔误:在配置格里补上 `(turns N)`;"
            "② 这个效果本来就该瞬发/无持续:把它的 kind 从 DURATION_KINDS 里移除。")

    unknown = all_tokens - consumed
    if unknown:
        _raise_unconsumed_tokens(char, config, unknown)
    return effects
