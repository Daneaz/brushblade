using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Brushblade.Core;

namespace Brushblade.Data
{
    /// <summary>词表里的一种写法(显示名或别名)。<see cref="NotBefore"/> / <see cref="NotAfter"/>:
    /// 紧挨在前 / 在后是这段文字时不算术语(如护甲括注「(减伤」、选择器后缀「全体敌人)」、「我方全体」)。null / 空 = 不排除。
    /// 多种上下文用「|」隔开,任一命中即排除(如减伤前面是左括号或逗号)。</summary>
    public readonly struct TermSurface
    {
        public string Text { get; }
        public GlossaryTerm Term { get; }
        public string NotBefore { get; }
        public string NotAfter { get; }

        public TermSurface(string text, GlossaryTerm term, string notBefore = null, string notAfter = null)
        {
            Text = text;
            Term = term;
            NotBefore = notBefore;
            NotAfter = notAfter;
        }
    }

    /// <summary>一次扫描的结果:<see cref="Spans"/> 按位置升序、互不重叠;<see cref="Terms"/> 按首次出现的顺序去重(气泡列表的顺序)。</summary>
    public sealed class TermScan
    {
        public IReadOnlyList<(int Start, int Length, GlossaryTerm Term)> Spans { get; }
        public IReadOnlyList<GlossaryTerm> Terms { get; }

        public TermScan(IReadOnlyList<(int Start, int Length, GlossaryTerm Term)> spans, IReadOnlyList<GlossaryTerm> terms)
        {
            Spans = spans;
            Terms = terms;
        }

        public static readonly TermScan Empty =
            new TermScan(Array.Empty<(int, int, GlossaryTerm)>(), Array.Empty<GlossaryTerm>());
    }

    /// <summary>在拼好的**纯文本**上标术语(Plan E4 附录 §C 3.2):口径是「玩家看到哪个词就标哪个词」,
    /// 不改拼句结构。测量与截断一律吃纯文本,富文本只在最后赋给 Text 时生成。颜色等标签由调用方以字符串传入(Data 拿不到 Theme)。</summary>
    public static class TermMarkup
    {
        /// <summary>数字:可带一个前缀符号(+ - − ×)、小数、百分号。</summary>
        private static readonly Regex NumberPattern = new Regex(@"[+\-−×]?\d+(\.\d+)?%?", RegexOptions.CultureInvariant);

        /// <summary>Unity 旧版 Text 的富文本标签。</summary>
        private static readonly Regex TagPattern = new Regex(@"</?(b|i|color|size|material|quad)(=[^>]*)?>", RegexOptions.CultureInvariant);

        /// <summary>从左到右、不重叠地最长匹配。同一位置先试最长的写法;被排除上下文挡掉的写法跳过,继续试更短的。</summary>
        public static TermScan Scan(string plain, IReadOnlyList<TermSurface> lexicon)
        {
            if (string.IsNullOrEmpty(plain) || lexicon == null || lexicon.Count == 0) return TermScan.Empty;
            var ordered = lexicon.Where(s => !string.IsNullOrEmpty(s.Text))
                .OrderByDescending(s => s.Text.Length).ToList();   // 稳定排序:同长按词表顺序
            var spans = new List<(int, int, GlossaryTerm)>();
            var terms = new List<GlossaryTerm>();
            int i = 0;
            while (i < plain.Length)
            {
                int matched = 0;
                foreach (var s in ordered)
                {
                    if (i + s.Text.Length > plain.Length || string.CompareOrdinal(plain, i, s.Text, 0, s.Text.Length) != 0) continue;
                    if (Excluded(plain, i, s)) continue;
                    spans.Add((i, s.Text.Length, s.Term));
                    if (!terms.Contains(s.Term)) terms.Add(s.Term);
                    matched = s.Text.Length;
                    break;
                }
                i += matched > 0 ? matched : 1;
            }
            return spans.Count == 0 ? TermScan.Empty : new TermScan(spans, terms);
        }

        private static bool Excluded(string plain, int start, in TermSurface s)
        {
            if (!string.IsNullOrEmpty(s.NotBefore))
                foreach (var before in s.NotBefore.Split('|'))
                    if (before.Length > 0 && start >= before.Length
                        && string.CompareOrdinal(plain, start - before.Length, before, 0, before.Length) == 0)
                        return true;
            int end = start + s.Text.Length;
            if (!string.IsNullOrEmpty(s.NotAfter))
                foreach (var after in s.NotAfter.Split('|'))
                    if (after.Length > 0 && end + after.Length <= plain.Length
                        && string.CompareOrdinal(plain, end, after, 0, after.Length) == 0)
                        return true;
            return false;
        }

        /// <summary>按「显示名 + 别名」拼词表,并给需要的术语挂排除上下文(附录 §C 3.2 第 2 条、C-Q5)。
        /// 表现层(从字符串表取名)与对账测试共用这一个入口,排除规则只有一份。
        /// <paramref name="entries"/>:每条术语的显示名与别名(多个用「|」隔开;null / 空 = 无别名)。输出按入参顺序,显示名在前。</summary>
        public static IReadOnlyList<TermSurface> BuildLexicon(IEnumerable<(GlossaryTerm Term, string Name, string Aliases)> entries)
        {
            var list = new List<TermSurface>();
            if (entries == null) return list;
            foreach (var (term, name, aliases) in entries)
            {
                var (notBefore, notAfter) = ExclusionsOf(term);
                if (!string.IsNullOrEmpty(name)) list.Add(new TermSurface(name, term, notBefore, notAfter));
                if (string.IsNullOrEmpty(aliases)) continue;
                foreach (var alias in aliases.Split('|'))
                    if (alias.Length > 0) list.Add(new TermSurface(alias, term, notBefore, notAfter));
            }
            return list;
        }

        /// <summary>减伤:护甲括注「(减伤 N%)」「,减伤 N%」是护甲的换算,不是减伤状态;
        /// 全体:选择器后缀「(全体敌人)」与「我方全体」不是范围(C-Q5;「全体灼烧」「治疗改为全体」照标)。</summary>
        private static (string NotBefore, string NotAfter) ExclusionsOf(GlossaryTerm term) => term switch
        {
            GlossaryTerm.DamageCut => ("(|,", null),
            GlossaryTerm.AreaAll => ("我方", "敌人)"),
            _ => (null, null),
        };

        /// <summary>纯文本里的数字段(位置升序)。</summary>
        public static IReadOnlyList<(int Start, int Length)> Numbers(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return Array.Empty<(int, int)>();
            var list = new List<(int, int)>();
            foreach (Match m in NumberPattern.Matches(plain)) list.Add((m.Index, m.Length));
            return list;
        }

        /// <summary>给术语段套 termOpen/termClose、数字段套 numOpen/numClose;与术语重叠的数字段不套。
        /// 其余字符原样保留,所以 <c>Strip(Rich(x)) == x</c>。</summary>
        public static string Rich(string plain, TermScan scan, string termOpen, string termClose, string numOpen, string numClose)
        {
            if (string.IsNullOrEmpty(plain)) return "";
            var marks = new List<(int Start, int Length, bool Term)>();
            var spans = scan?.Spans ?? TermScan.Empty.Spans;
            foreach (var s in spans) marks.Add((s.Start, s.Length, true));
            foreach (var n in Numbers(plain))
            {
                bool overlaps = false;
                foreach (var s in spans)
                    if (n.Start < s.Start + s.Length && s.Start < n.Start + n.Length) { overlaps = true; break; }
                if (!overlaps) marks.Add((n.Start, n.Length, false));
            }
            marks.Sort((a, b) => a.Start.CompareTo(b.Start));
            var sb = new StringBuilder(plain.Length + marks.Count * 16);
            int at = 0;
            foreach (var m in marks)
            {
                if (m.Start < at || m.Start + m.Length > plain.Length) continue;   // 防御:扫描结果与文本不符时不越界、不重叠
                sb.Append(plain, at, m.Start - at);
                sb.Append(m.Term ? termOpen : numOpen);
                sb.Append(plain, m.Start, m.Length);
                sb.Append(m.Term ? termClose : numClose);
                at = m.Start + m.Length;
            }
            sb.Append(plain, at, plain.Length - at);
            return sb.ToString();
        }

        /// <summary>剥掉富文本标签,还原纯文本。</summary>
        public static string Strip(string rich) => string.IsNullOrEmpty(rich) ? "" : TagPattern.Replace(rich, "");
    }
}
