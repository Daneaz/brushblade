using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Brushblade.Data;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>真实字符串表与真实调用点的三条对账(spec §9)。
    ///
    /// ⚠ 这三条**读 .cs 源码文本**做正则扫描,不是反射 —— Tests asmdef 是
    /// overrideReferences 且只引 Core/Data,根本引不到 Presentation 程序集。
    ///
    /// ⚠ 仓库根只能用 TestContext.CurrentContext.TestDirectory 往上找。
    /// AppContext.BaseDirectory 在 Unity Test Runner 下指向编辑器安装目录,
    /// 往上永远找不到含 Brushblade/ 的父目录(项目已犯过两次)。</summary>
    public sealed class StringsTableTests
    {
        // Strings.T("key"  —— 抓调用点的 key 字面量
        private static readonly Regex CallRe =
            new Regex(@"Strings\.T\(\s*""([^""]+)""", RegexOptions.Compiled);

        // 紧跟在顶层 "(" 之后的 "name", —— 一个顶层元组实参的名字
        private static readonly Regex ArgHeadRe =
            new Regex(@"^\s*""(\w+)""\s*,", RegexOptions.Compiled);

        // {name}  —— 抓模板里的占位符
        private static readonly Regex PlaceholderRe =
            new Regex(@"\{(\w+)\}", RegexOptions.Compiled);

        /// <summary>从「key 之后到调用收尾括号」这段文本里,只挑出**顶层**元组实参的名字。
        ///
        /// 只在括号深度为 0(即紧跟在 T(...) 的参数列表最外层)时才把 `("name",` 认成一个占位符实参;
        /// 深度 ≥1 的括号——比如 `MetaStore.GetInt("gold_key", 0)`、`x.ToString("F2", CultureInfo...)`
        /// 这类嵌套调用——一律不认,避免把嵌套调用的首个字符串实参误当成调用点提供的占位符
        /// (2026-08-22 code review 抓到:会在完全正确的代码上报假红)。</summary>
        internal static HashSet<string> TopLevelArgNames(string tail)
        {
            var names = new HashSet<string>();
            int depth = 0;
            for (int i = 0; i < tail.Length; i++)
            {
                if (tail[i] == '(')
                {
                    if (depth == 0)
                    {
                        var head = ArgHeadRe.Match(tail.Substring(i + 1));
                        if (head.Success) names.Add(head.Groups[1].Value);
                    }
                    depth++;
                }
                else if (tail[i] == ')')
                {
                    depth--;
                    if (depth < 0) break; // 这个 ')' 关闭的是外层 T(...) 调用本身,后面不再有实参
                }
            }
            return names;
        }

        private static DirectoryInfo RepoRoot()
        {
            var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Brushblade")))
                dir = dir.Parent;
            Assert.That(dir, Is.Not.Null, "找不到含 Brushblade/ 的仓库根");
            return dir;
        }

        private static string TablePath() => Path.Combine(RepoRoot().FullName,
            "Brushblade", "Assets", "StreamingAssets", "config", "strings.zh-CN.json");

        private static string[] PresentationFiles() => Directory.GetFiles(
            Path.Combine(RepoRoot().FullName, "Brushblade", "Assets", "_Project", "Presentation"),
            "*.cs", SearchOption.AllDirectories);

        /// <summary>一处调用:key + 它提供的占位符名 + 出处(报错时给人看)。</summary>
        private static List<(string Key, HashSet<string> Args, string Where)> Calls()
        {
            var calls = new List<(string, HashSet<string>, string)>();
            foreach (var file in PresentationFiles())
            {
                var src = File.ReadAllText(file);
                foreach (Match m in CallRe.Matches(src))
                {
                    // 从 key 之后一路读到调用的收尾括号,期间的顶层元组实参就是占位符
                    int i = m.Index + m.Length, depth = 1;
                    while (i < src.Length && depth > 0)
                    {
                        if (src[i] == '(') depth++;
                        else if (src[i] == ')') depth--;
                        i++;
                    }
                    var tail = src.Substring(m.Index + m.Length, Math.Max(0, i - m.Index - m.Length));
                    var args = TopLevelArgNames(tail);
                    int line = src.Take(m.Index).Count(c => c == '\n') + 1;
                    calls.Add((m.Groups[1].Value, args, $"{Path.GetFileName(file)}:{line}"));
                }
            }
            return calls;
        }

        [SetUp]
        public void LoadRealTable() => Strings.Load(File.ReadAllText(TablePath()));

        [Test]
        public void EveryCalledKey_ExistsInTable()
        {
            var missing = Calls()
                .Where(c => !Strings.Keys.Contains(c.Key))
                .Select(c => $"{c.Where} → {c.Key}")
                .Distinct().OrderBy(s => s).ToArray();
            Assert.That(missing, Is.Empty, "调用了表里没有的 key:\n" + string.Join("\n", missing));
        }

        [Test]
        public void EveryTableKey_IsUsed()
        {
            var used = new HashSet<string>(Calls().Select(c => c.Key));
            var orphans = Strings.Keys.Where(k => !used.Contains(k)).OrderBy(k => k).ToArray();
            Assert.That(orphans, Is.Empty, "表里有没人用的孤儿 key:\n" + string.Join("\n", orphans));
        }

        [Test]
        public void Placeholders_MatchBetweenTableAndCallSites()
        {
            var problems = new List<string>();
            foreach (var call in Calls())
            {
                if (!Strings.Keys.Contains(call.Key)) continue; // 由第一条测试负责报
                var template = Strings.T(call.Key);
                var needed = new HashSet<string>(
                    PlaceholderRe.Matches(template).Cast<Match>().Select(m => m.Groups[1].Value));

                foreach (var miss in needed.Except(call.Args).OrderBy(s => s))
                    problems.Add($"{call.Where} [{call.Key}] 模板要 {{{miss}}} 但调用点没给");
                foreach (var extra in call.Args.Except(needed).OrderBy(s => s))
                    problems.Add($"{call.Where} [{call.Key}] 调用点给了 {extra} 但模板里没有");
            }
            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        }

        [Test]
        public void TopLevelArgNames_IgnoresNestedCallArgs()
        {
            // 复现 review 抓到的假红:嵌套调用 MetaStore.GetInt("gold_key", 0) 的首个字符串实参
            // 不该被当成 T(...) 的顶层占位符。
            var names = TopLevelArgNames(@", (""count"", MetaStore.GetInt(""gold_key"", 0)))");
            Assert.That(names, Is.EquivalentTo(new[] { "count" }));
        }

        [Test]
        public void TopLevelArgNames_IgnoresToStringFormatAndCulture()
        {
            // 换成常见的 .ToString("F2", CultureInfo.InvariantCulture) 同样不该误抓 "F2"。
            var names = TopLevelArgNames(
                @", (""val"", x.ToString(""F2"", CultureInfo.InvariantCulture)))");
            Assert.That(names, Is.EquivalentTo(new[] { "val" }));
        }

        [Test]
        public void TopLevelArgNames_HandlesPlainInlineTuple()
        {
            var names = TopLevelArgNames(@", (""count"", 3))");
            Assert.That(names, Is.EquivalentTo(new[] { "count" }));
        }

        [Test]
        public void TopLevelArgNames_HandlesMultipleTopLevelTuples()
        {
            var names = TopLevelArgNames(@", (""a"", 1), (""b"", 2))");
            Assert.That(names, Is.EquivalentTo(new[] { "a", "b" }));
        }

        [Test]
        public void TopLevelArgNames_HandlesZeroArgs()
        {
            var names = TopLevelArgNames(")");
            Assert.That(names, Is.Empty);
        }
    }
}
