# 设计:等级被动技能系统(A)

> 2026-07-22 · brainstorming 产出 · 数值均首版基准可调
> 关联:B 无尽护盾改动(`2026-07-22-endless-shield-rework-design.md`)——本系统的「金汤」技能依赖 B 的护盾模型。

## 1. 目标与动机

局外养成的一条新支线:玩家用**墨锭**在**角色等级**门槛上解锁并升级一批常驻被动技能,永久改善开局属性/资源。

动机来自试玩体感:无尽 15 层起压力陡增(第 13 层起满编 4 敌、15 层敌人 ×2.4 强度,对固定 3 AP),字卡卡手打不出、AOE 清不掉场、被围攻。技能系统是玩家「撞墙 → 多试几次攒经验升级 → 解锁救命技能 → 突破」的养成出路。

**为何 gate 用角色等级而非最高层数(BestDepth):** 死亡/弃塔会清空 `_meta.Endless`(`GameRoot.SettleTower`),经验幂等守卫是 per-climb 的 `snapshot.Depth`,无 `BestDepth` 守卫——所以每次重爬都从第 1 层重新累积经验。角色等级因此衡量「累计尝试/游玩时长」,是**撞墙玩家多打几次必然增长**的正反馈轴。用 BestDepth 则会死锁:卡在 15 层→BestDepth 不涨→拿不到帮他突破的技能。

> 注:本系统只软化那堵墙,不能完全修复其结构性成因(4 敌 ×2.4 vs 3 AP)。彻底解决需另议敌人缩放曲线,不在本 spec 范围。

## 2. 数据模型

复用卡等级系统的**数据形状**(`Dictionary<string,int>` + 每级墨锭成本),但**不复用升级规则**:卡需「重复卡 + 墨锭」,技能没有「重复技能」概念,故**纯墨锭升级**。不要套用 `MetaRules.TryUpgradeCard`。

`MetaState` 新增:

```csharp
public Dictionary<string, int> PerkLevels { get; set; } = new();  // 技能 id → 当前等级;缺省 0 = 未解锁
```

- `PerkLevel = 0`:未解锁(默认)。
- `PerkLevel = L`(1..max):已升到 L 级,效果 = `L × 每级值`。

进存档;进 `MetaStore` 的防篡改校验(与 `CardLevels` 同待遇);`RemoveUnknownKeys` 清理未知键(与 `CardLevels` 一致)。

## 3. 技能表(静态定义)

在 `MetaRules`(或新建 `PerkRules`)内定义一张静态表,每条:`{ id, 名称, 效果类型, 每级值, 初解锁角色等级门槛, 升级墨锭成本序列 }`。升级上限 = 成本序列长度。

| id | 名称 | 效果/级 | 初解锁角色等级 | 上限 | 升级墨锭成本序列(索引 = 目标等级−1) |
|---|---|---|---|---|---|
| yangyuan | 养元 | 生命上限 +10 | 2 | 6 | 200, 400, 700, 1100, 1600, 2200 |
| runbi | 润笔 | 开局字摊墨锭预算 +50 | 3 | 4 | 200, 400, 700, 1100 |
| jintang | 金汤 | 每段开局 +4 持久护盾 | 4 | 5 | 400, 700, 1100, 1600, 2200 |
| bowen | 博闻 | 起手字库 +1 格 | 6 | 3 | 600, 1200, 2000 |
| yiqi | 一气 | 每回合 AP 上限 +1 | 6 | **2** | 1500, 4000 |

**一气(AP)上限固定为 2**(最多 +2 AP,3→5)。这是全表唯一 load-bearing 的平衡数字:不封顶则 3 级 = 每回合 6 AP、翻倍、崩盘。上限由「升级上限」字段承载,不用额外机制。

**命名** 均水墨/汉字风,可换。

## 4. 解锁 / 升级规则

```
TryUpgradePerk(meta, perkId):
  L = PerkLevel(meta, perkId)            // 当前等级,默认 0
  若 L >= 上限:                          失败(已满)
  若 L == 0(首次解锁):
      要求 CharacterLevel(meta.CharacterXp) >= 初解锁角色等级门槛,否则失败
  cost = 升级墨锭成本序列[L]              // L=0 取序列[0] = 解锁到 1 级的价
  若 meta.Ink < cost:                     失败
  meta.Ink -= cost
  meta.PerkLevels[perkId] = L + 1
  成功
```

- **角色等级只 gate 首次解锁(0→1)**;第 2 级起纯墨锭。**不加**「每级还要卡角色等级」的第四机制(避免过度可配置)。
- 三个节奏杠杆已足够:解锁门槛、递增墨锭成本、升级上限。
- 一次性买断,不可退。

## 5. 效果聚合与注入点

`MetaRules`(或 `PerkRules`)提供聚合读取,值 = `PerkLevel × 每级值`:

| 聚合函数 | 注入点(现有代码) | 改法 |
|---|---|---|
| `PerkApBonus(meta)` | `GameRoot.StartSegment` 建 `BattleConfig` 处 | `ApPerTurn = 3 + PerkApBonus`(BattleEngine 默认 3) |
| `PerkHpBonus(meta)` | `MaxHpFor` 的三处调用(`StartTower`、`StartSegment`、`MapView`) | `MaxHpFor(lv) + PerkHpBonus`;数学上 `min(A,B)+b = min(A+b,B+b)`,天然抬高 100 封顶 |
| `PerkLibBonus(meta)` | `MetaRules.StartingLibrary` | 截断上限用 `StartingLibrarySize + PerkLibBonus` |
| `PerkInkBonus(meta)` | `GameRoot.StartSegment` 传 `startingInk` 处 | `startingInk = meta.Ink + snapshot.EarnedInk + PerkInkBonus` |
| `PerkShieldBonus(meta)` | 注入 B 的护盾模型(段初始护盾) | 见 B spec:每段开局把该值注入普通(段内持久)护盾桶,**每段仅一次** |

**HP 抬封顶注意:** 三处 `MaxHpFor` 调用必须都改成含 bonus,否则快照初始血量与战斗上限对不上(`StartTower` 建 `snapshot.PlayerHp`、`StartSegment` 算 `PlayerMaxHp` 要走同一含加成的值)。

**金汤(每段一次)注入:** 段起始(`StartSegment` 初始化 run 时)把 `PerkShieldBonus` 作为该段初始护盾注入第一场战斗的普通桶;段内持久所以自动保留累积,后续层不重发。断点续爬:护盾已作为段内 carried 值存快照(见 B),恢复即可,不重发。

## 6. UI(Presentation,离线编译必过)

- 新增技能页(参考 `CollectionView`/`MapView` 风格):列出各技能、当前等级/上限、下一级效果与墨锭价、是否达等级门槛、解锁/升级按钮。
- `MapView` 已显示「经验 / HP 上限」,HP 上限展示值要含 `PerkHpBonus`。
- 文案走字符串表(硬规则),技能名/效果数值是游戏数据。

## 7. 测试要点(Core 先测后实现,TDD)

- `PerkLevel` 默认 0;升级后为 L。
- `TryUpgradePerk`:等级门槛不足→拒;墨锭不足→拒;满级→拒;成功则扣墨锭、+1 级。拒绝时不改动任何状态。
- 聚合函数 = 等级 × 每级值;多技能独立。
- 一气升级上限 = 2,第 3 次升级被拒。
- 存档往返:`PerkLevels` 正确序列化/反序列化;`RemoveUnknownKeys` 清理未知键。
- 注入正确性:`ApPerTurn`、`MaxHpFor+bonus`、`StartingLibrary` 容量、`startingInk`、段初始护盾。
- HP 抬封顶:高等级(封顶 100)下 `MaxHpFor(lv)+PerkHpBonus` > 100 生效。
- ⚠️ 断言只用 Unity NUnit 支持的 API(禁 `Is.AnyOf`)。

## 8. 开放问题(调参,非架构)

- 各技能每级值、墨锭成本序列、升级上限、解锁门槛均待实测调。
- 一气是否 6 级解锁(用户举例 5 级)——落在「撞 15 层墙几次后」即可,细调。
- 是否后续再加技能族(如 AOE 增强向)——留扩展位,首版不做。
