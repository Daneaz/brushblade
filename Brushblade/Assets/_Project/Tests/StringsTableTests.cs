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

        // ("name",  —— 抓命名占位符实参。跟在 key 之后的元组实参都长这样
        private static readonly Regex ArgRe =
            new Regex(@"\(\s*""(\w+)""\s*,", RegexOptions.Compiled);

        // {name}  —— 抓模板里的占位符
        private static readonly Regex PlaceholderRe =
            new Regex(@"\{(\w+)\}", RegexOptions.Compiled);

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
                    // 从 key 之后一路读到调用的收尾括号,期间的元组实参就是占位符
                    int i = m.Index + m.Length, depth = 1;
                    while (i < src.Length && depth > 0)
                    {
                        if (src[i] == '(') depth++;
                        else if (src[i] == ')') depth--;
                        i++;
                    }
                    var tail = src.Substring(m.Index + m.Length, Math.Max(0, i - m.Index - m.Length));
                    var args = new HashSet<string>(ArgRe.Matches(tail).Cast<Match>().Select(a => a.Groups[1].Value));
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
    }
}
