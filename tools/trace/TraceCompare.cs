using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Brushblade.Trace
{
    /// <summary>两份黄金轨迹的比对(spec §10.2 网 1 / 网 2 的判据本体)。
    ///
    /// 判据分两类:
    /// - **骨架列**(Kind / TargetIndex / SecondIndex / 顺序 / 条数 / RandomState / 掉落 / 胜负 / 回合数)
    ///   —— 必须逐字节相同,差一个就是 FAIL;
    /// - **量级列**(Amount / Absorbed / 血 / 盾)—— 逐个归类成 same / scaled / zero / other,
    ///   出现 other 即 FAIL,并把 (旧值 → 新值) 打出来。
    ///
    /// 「哪一列是量级」这份策略故意住在比对器而不是写出端:轨迹文件是纯数据,
    /// T1 若发现某个字段归类判错(典型的是 Burn 的 Amount 是**层数**不是伤害,不该 ×10),
    /// 改这张表重跑比对即可,不必重跑 baseline。
    ///
    /// ⚠ 归类只按记录类型分,不按 <c>E</c> 的 Kind 分 —— 因为 Amount 的语义随 Kind 变
    /// (Burn = 层数、BossPhase = 阶段下标、BossCharging/BossSkillCast = 技能枚举值,都不该 ×10)。
    /// 所以 E 的统计**按 Kind 分组输出**,由人扫一眼「Damage 全 scaled、Burn 全 same」即可复核,
    /// 而不是让工装替这些 Kind 预先拍板。这也意味着 <see cref="UnscaledEventKinds"/>
    /// 里的 Kind 只在「other 也放行」上宽容,不在「必须 ×10」上苛求。</summary>
    public static class TraceCompare
    {
        /// <summary>每种记录里「量级列」的下标(token 0 = 记录类型);其余列一律精确比对。</summary>
        private static readonly Dictionary<string, (int Index, string Name)[]> MagnitudeColumns = new()
        {
            ["B"] = new[] { (5, "enemyMaxHp") },
            ["T"] = new[] { (6, "playerHp"), (7, "shield"), (10, "enemyHp") },
            ["E"] = new[] { (9, "amount"), (10, "absorbed") },
            ["W"] = new[] { (7, "playerHp") },
        };

        /// <summary>Amount 语义不是「数值量级」的事件(层数 / 下标 / 枚举值)——
        /// 这些 Kind 的 amount 该保持 same,归类成 other 时**不判 FAIL**,只在汇总里显形。</summary>
        private static readonly HashSet<string> UnscaledEventKinds = new(StringComparer.Ordinal)
        {
            "Burn", "BossPhase", "BossCharging", "BossSkillCast",
            "EnemyDied", "EnemyTurnBegan", "EnemyRevealed", "Missed",
        };

        private const int MaxReported = 20;

        public static int Run(string oldPath, string newPath, long scale)
        {
            var oldLines = DataLines(oldPath);
            var newLines = DataLines(newPath);

            var failures = new List<string>();
            var buckets = new SortedDictionary<string, Counts>(StringComparer.Ordinal);

            int common = Math.Min(oldLines.Count, newLines.Count);
            for (int i = 0; i < common; i++)
                CompareLine(i + 1, oldLines[i], newLines[i], scale, failures, buckets);

            if (oldLines.Count != newLines.Count)
                failures.Add($"数据行数不同:旧 {oldLines.Count} 行,新 {newLines.Count} 行" +
                             $"(首处结构分叉见上;行数不同意味着骨架已经发散)");

            Console.WriteLine($"scale = ×{scale}");
            Console.WriteLine($"旧:{oldPath}({oldLines.Count} 数据行)");
            Console.WriteLine($"新:{newPath}({newLines.Count} 数据行)\n");

            Console.WriteLine("| 量级列 | same | scaled | zero | other |");
            Console.WriteLine("|---|---|---|---|---|");
            foreach (var pair in buckets)
                Console.WriteLine($"| {pair.Key} | {pair.Value.Same} | {pair.Value.Scaled} " +
                                  $"| {pair.Value.Zero} | {pair.Value.Other} |");

            if (failures.Count == 0)
            {
                Console.WriteLine("\nPASS:骨架逐字节相同,量级列全部落在 same / scaled / zero。");
                return 0;
            }

            Console.WriteLine($"\nFAIL:{failures.Count} 处不合判据(最多列出 {MaxReported} 处):");
            foreach (var line in failures.Take(MaxReported))
                Console.WriteLine("  " + line);
            return 1;
        }

        private sealed class Counts
        {
            public int Same, Scaled, Zero, Other;
        }

        private static void CompareLine(int lineNo, string oldLine, string newLine, long scale,
            List<string> failures, SortedDictionary<string, Counts> buckets)
        {
            if (oldLine == newLine)
            {
                // 快路径也要计数,否则「全 same」的列在汇总里看不见
                Tally(lineNo, oldLine, newLine, scale, failures, buckets);
                return;
            }

            var oldTokens = oldLine.Split(' ');
            var newTokens = newLine.Split(' ');
            if (oldTokens[0] != newTokens[0] || oldTokens.Length != newTokens.Length)
            {
                failures.Add($"第 {lineNo} 行结构不同:\n      旧 {oldLine}\n      新 {newLine}");
                return;
            }
            Tally(lineNo, oldLine, newLine, scale, failures, buckets);
        }

        private static void Tally(int lineNo, string oldLine, string newLine, long scale,
            List<string> failures, SortedDictionary<string, Counts> buckets)
        {
            var oldTokens = oldLine.Split(' ');
            var newTokens = newLine.Split(' ');
            string record = oldTokens[0];
            MagnitudeColumns.TryGetValue(record, out var magnitudes);

            for (int col = 0; col < oldTokens.Length; col++)
            {
                var column = magnitudes?.FirstOrDefault(m => m.Index == col);
                bool isMagnitude = column.HasValue && column.Value.Name != null;
                if (!isMagnitude)
                {
                    if (oldTokens[col] != newTokens[col])
                        failures.Add($"第 {lineNo} 行第 {col} 列(骨架)不同:「{oldTokens[col]}」→「{newTokens[col]}」" +
                                     $"\n      旧 {oldLine}\n      新 {newLine}");
                    continue;
                }

                string bucket = record == "E"
                    ? $"E.{oldTokens[5]}.{column.Value.Name}"
                    : $"{record}.{column.Value.Name}";
                bool lenient = record == "E" && UnscaledEventKinds.Contains(oldTokens[5]);
                TallyMagnitude(lineNo, bucket, oldTokens[col], newTokens[col], scale, lenient,
                    oldLine, newLine, failures, buckets);
            }
        }

        private static void TallyMagnitude(int lineNo, string bucket, string oldToken, string newToken,
            long scale, bool lenient, string oldLine, string newLine,
            List<string> failures, SortedDictionary<string, Counts> buckets)
        {
            var oldParts = oldToken == "-" ? Array.Empty<string>() : oldToken.Split('|');
            var newParts = newToken == "-" ? Array.Empty<string>() : newToken.Split('|');
            if (oldParts.Length != newParts.Length)
            {
                failures.Add($"第 {lineNo} 行 {bucket} 元素个数不同:{oldParts.Length} → {newParts.Length}" +
                             $"\n      旧 {oldLine}\n      新 {newLine}");
                return;
            }

            if (!buckets.TryGetValue(bucket, out var counts))
                buckets[bucket] = counts = new Counts();

            for (int i = 0; i < oldParts.Length; i++)
            {
                if (!long.TryParse(oldParts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out long o) ||
                    !long.TryParse(newParts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out long n))
                {
                    failures.Add($"第 {lineNo} 行 {bucket} 不是整数:「{oldParts[i]}」→「{newParts[i]}」");
                    continue;
                }

                if (o == 0 && n == 0) counts.Zero++;
                else if (o == n) counts.Same++;
                else if (n == o * scale) counts.Scaled++;
                else
                {
                    counts.Other++;
                    if (!lenient)
                        failures.Add($"第 {lineNo} 行 {bucket} 既非等值也非 ×{scale}:{o} → {n}" +
                                     $"\n      旧 {oldLine}\n      新 {newLine}");
                }
            }
        }

        /// <summary>去掉 <c>#</c> 注释行:表头里写了种子/画像/配置摘要,那些**允许**随 T1 改变
        /// (血量上限就在里面),不该参与逐字节比对。</summary>
        private static List<string> DataLines(string path)
        {
            var lines = new List<string>();
            foreach (var line in File.ReadLines(path))
                if (line.Length > 0 && line[0] != '#')
                    lines.Add(line);
            return lines;
        }
    }
}
