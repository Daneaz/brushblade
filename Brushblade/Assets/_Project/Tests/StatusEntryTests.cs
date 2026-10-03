using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>状态施加的唯一入口(spec v6 §11.4):BattleEngine 里除 ApplyStatus 方法体外,
    /// 不许直接调 StatusBag.Apply —— 否则 StatusApplied 钩子会静默漏掉那一路。</summary>
    public class StatusEntryTests
    {
        [Test]
        public void BattleEngine_AppliesStatusesOnlyThroughApplyStatus()
        {
            var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Brushblade"))) dir = dir.Parent;
            Assert.That(dir, Is.Not.Null);
            var src = File.ReadAllText(Path.Combine(dir.FullName, "Brushblade/Assets/_Project/Core/BattleEngine.cs"));
            src = Regex.Replace(src, @"//.*", "");   // 去行注释
            var lines = src.Split('\n')
                .Select((l, i) => (l, i + 1))
                .Where(x => Regex.IsMatch(x.l, @"\.Apply\(") && !x.l.Contains("bag.Apply(effect)"))
                .Select(x => $"{x.Item2}: {x.l.Trim()}")
                .ToList();
            Assert.That(lines, Is.Empty, "以下行绕过了 ApplyStatus:\n" + string.Join("\n", lines));
        }
    }
}
