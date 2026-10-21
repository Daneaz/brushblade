# Task 2 报告

- 新增 Core/ArmorCellRules.cs(ArmorCellKind / ArmorCell / ArmorCellRules.ForEnemy、ForUnit,均 public)。
- MetaRules.EdgeSheatheShownPercent(meta) = 100 + 断金;BattleEngine.EdgeSheatheShownPercent 实例属性,edgePercent 改读它(行为不变)。
- 测试:ArmorCellRulesTests(9 条 + ForUnit 1 条)、EdgeSheatheShownPercentTests(3 条)。coretests 全量 4166 绿;prescompile、balance、chestsim、trace 均 0 error CS。
- 说明:ArmorCell 字段用只读属性实现(readonly struct),调用方式相同。测试在实现之后补写(实现很薄),未先见红。
