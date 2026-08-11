using System.Runtime.CompilerServices;

// Tests 是独立 asmdef,默认看不见 Core 的 internal。放开是为了让「只为测试存在」的入口
// (BattleEngine.ApplyPlayerAttackBuff)不必挂到生产 API 面上。
// 程序集名取自 Tests/Brushblade.Core.Tests.asmdef 的 name 字段,改名要同步改这里。
[assembly: InternalsVisibleTo("Brushblade.Core.Tests")]
