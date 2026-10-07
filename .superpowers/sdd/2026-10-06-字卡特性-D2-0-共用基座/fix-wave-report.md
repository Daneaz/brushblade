# D2-0 终审修复轮报告

基线 HEAD d532d3d8;coretests 2613/2614(唯一失败 EveryTableKey_IsUsed 既有);pytest 592+2s;prescompile / balance / chestsim / trace 编译 0 error;trace f1 = 7235e89f…、f1-strict = 883954a3…(与 e9 / e9-strict 一致)。

## I1 拆 / 合收尾顺序
- 改了什么:`FireGlyphTraits` 返回 bool(即时类是否结算过),不再自己 RefreshSummonAura / CheckWin;新增 `FinishForgeAction(glyphApplied)`:`DrainReactions → (仅 glyphApplied)RefreshSummonAura → CheckWin`,Dismantle / Compose 末尾调用。无字形数据时 glyphApplied=false,与旧行为恒等(trace 校验)。
- 测试:`GlyphTraitTests.Dismantle_GlyphTrait_KillsLastEnemy_PhaseWon`、`..._BurnSettleKillsLastEnemy_PhaseWon`。
- 红绿:这两条在修复前就是绿的(DamageEnemy 死亡分支自己会 CheckWin),属回归守卫,没能构造出「顺序错误才红」的用例;顺序修正是按审查意见与 Cast 对齐。

## I2 嫁接被动一致
- 改了什么:嫁接处新被动 `Speed` 沿用被嫁接者原被动(null = 0),`Armor`、`EntrySaplings` 清零。
- 测试:`SummonNatureTests.Graft_DoesNotGrantArmorOrEntrySaplings` 改断言 Armor == 0 / EntrySaplings == 0;新增 `Graft_KeepsTargetSpeed_FromOriginalPassive`(150 速、100 速、null 三种)。
- 红绿:撤掉修复后两条红,恢复后绿。

## I3
- (a) `GraftTests.Graft_AtLv5_UsesLv3EnhancedPassive_ScaledByCardLevel`:Lv3 强化 40,卡 Lv5 → min(100, ScaleByCardLevel(40,5));修复前后均绿(守卫)。
- (b) `GraftTests.Run_Carry_StackedGraftWetFirmArmorEndure_RestoresAllAndKeepsEntryArmorAndShield`:保命 + 入场护甲 + 嫁接 + 润 + 固 + 铠叠在同一只木灵,战后 Element / Passive 复原,Taunt / Block / Endure 剥离,入场 DefenseBuff(TurnsLeft<0)保留,护盾 = 战中值 × ShieldCarryPercent / 100;现有行为已满足,绿(守卫)。
- (c) `FeatureOntoSummonTests`(跨场区全部)与 `GraftTests` 的跨场 Cast 全补 `Is.EqualTo(BattleError.None)`。

## 6 ValidateGlyph
- `Data/ConfigLoader.cs`:OnCompose / OnDismantle 字形特性(含印记)效果里有 SummonSapling → ConfigException。测试 `Loader_GlyphSapling_OnCompose_Throws` / `OnDismantle_Throws`(本体面带 Summon,绕开「同面须有召唤」旧校验);撤掉修复后红。
- 异常文案避开「径」(字体子集扫描 .cs 字面量,原用「口径」让 test_subset 红了一次,已改)。
