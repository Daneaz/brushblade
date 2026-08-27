using System.Globalization;
using System.IO;
using System.Text;
using Brushblade.Core;
using Brushblade.Data;
using NUnit.Framework;

namespace Brushblade.CoreTests
{
    /// <summary>奇遇选项的**排版契约**(2026-08-27):钮上只放名称,效果说明走 Detail。
    ///
    /// 奇遇选项钮是固定宽 260、字号 22 的单行钮(BattleView.DrawEvent),而标签是
    /// horizontalOverflow = Overflow —— 文案超宽**不会换行也不会省略号**,而是溢出到钮外
    /// 被相邻选项钮的底图盖掉,玩家看到的就是「描述展示不全」。
    /// 「入炉淬骨(八成 上限 +30%,两成 反噬 −30%)」曾是 39 个半宽 = 429px,溢出 85px/侧,
    /// 后半截整个看不见。修法是把效果从 Label 挪进 Detail,由表现层列在通栏的情境正文下方。
    ///
    /// 本文件守两条,都是**离线编译与 Presentation 那一层发现不了**的纯配置回归:
    ///   ① Label 压在钮的容量内 —— 拦住「把效果又塞回 Label」;
    ///   ② 有效果的选项必须写了 Detail —— 拦住「加了新选项但忘了写说明」,那会让玩家
    ///      对着一个只有名字的钮做不可逆决策。
    ///
    /// 容量口径:CJK/全角字符按 2 个半宽计、其余按 1,一个半宽 ≈ fontSize / 2 = 11px。
    /// 260 / 11 = 23.6 → 上限 23 个半宽。抬这个数**必须**同步 DrawEvent 的钮宽,而钮宽受
    /// _centerRow 那一排的邻区挤压(见 BattleView 里那段注释),不是随便能加宽的。</summary>
    public sealed class EventLabelWidthTests
    {
        /// <summary>选项钮容得下的半宽数(钮宽 260 / 字号 22)。改它必须同步 DrawEvent 的钮宽。</summary>
        private const int MaxHalfWidths = 23;

        private static string ConfigDir()
        {
            // ⚠ 锚点只能是 TestContext.CurrentContext.TestDirectory —— AppContext.BaseDirectory
            // 在 Unity Test Runner 下指向编辑器安装目录,往上找不到含 Brushblade/ 的父目录
            // (2026-08-15 已让 DefenseValuesTests 整类变红,而 dotnet 工装一直是绿的)。
            var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Brushblade")))
                dir = dir.Parent;
            Assert.That(dir, Is.Not.Null, "找不到仓库根目录");
            return Path.Combine(dir.FullName, "Brushblade", "Assets", "StreamingAssets", "config");
        }

        private static CampaignConfig RealCampaign()
        {
            var graph = ConfigLoader.LoadGraph(File.ReadAllText(Path.Combine(ConfigDir(), "chars.json")));
            return ConfigLoader.LoadCampaign(
                File.ReadAllText(Path.Combine(ConfigDir(), "enemies.json")), graph);
        }

        /// <summary>半宽计数。CJK 统一表意文字 / 全角标点 / 假名算 2,其余算 1 —— 与 Unity 的
        /// Text 逐像素度量不完全相等,但对「中文 + 数字 + 百分号」这类文案足够准,且阈值本身
        /// 留了 6px 余量(23 × 11 = 253 ≤ 260)。</summary>
        private static int HalfWidths(string text)
        {
            int total = 0;
            foreach (char c in text)
                total += IsWide(c) ? 2 : 1;
            return total;
        }

        private static bool IsWide(char c) =>
            (c >= 0x1100 && c <= 0x115F) ||     // 谚文字母
            (c >= 0x2E80 && c <= 0xA4CF) ||     // CJK 部首 / 假名 / 统一表意文字 / 注音
            (c >= 0xAC00 && c <= 0xD7A3) ||     // 谚文音节
            (c >= 0xF900 && c <= 0xFAFF) ||     // CJK 兼容表意文字
            (c >= 0xFE30 && c <= 0xFE6F) ||     // CJK 兼容形式 / 小写变体
            (c >= 0xFF00 && c <= 0xFF60) ||     // 全角 ASCII
            (c >= 0xFFE0 && c <= 0xFFE6);       // 全角符号

        [Test]
        public void ShippedEvents_EveryOptionLabel_FitsInTheOptionButton()
        {
            var campaign = RealCampaign();
            Assert.That(campaign.Events.Count, Is.GreaterThan(0), "enemies.json 里一条奇遇都没有");

            var overflowing = new StringBuilder();
            int scanned = 0;
            foreach (var evt in campaign.Events)
                foreach (var option in evt.Options)
                {
                    scanned++;
                    int width = HalfWidths(option.Label);
                    if (width > MaxHalfWidths)
                        overflowing.Append(string.Format(CultureInfo.InvariantCulture,
                            "\n  「{0}」的「{1}」= {2} 半宽(约 {3}px),超上限 {4}",
                            evt.Id, option.Label, width, width * 11, MaxHalfWidths));
                }

            Assert.That(scanned, Is.GreaterThan(0), "扫到的奇遇选项数为 0");
            Assert.That(overflowing.Length, Is.EqualTo(0),
                "这些奇遇选项的 Label 会溢出选项钮、后半截被邻钮盖掉。"
                + $"效果说明要写进 detail 字段,不要塞回 label:{overflowing}");
        }

        [Test]
        public void ShippedEvents_OptionsWithEffects_AllCarryADetail()
        {
            var campaign = RealCampaign();

            var missing = new StringBuilder();
            int withEffects = 0;
            foreach (var evt in campaign.Events)
                foreach (var option in evt.Options)
                {
                    if (!HasEffect(option)) continue;   // 「拱手离开」这类空选项不需要说明
                    withEffects++;
                    if (string.IsNullOrWhiteSpace(option.Detail))
                        missing.Append($"\n  「{evt.Id}」的「{option.Label}」");
                }

            Assert.That(withEffects, Is.GreaterThan(0), "一个带效果的奇遇选项都没扫到,读表姿势不对");
            Assert.That(missing.Length, Is.EqualTo(0),
                "这些奇遇选项有实际效果却没写 detail,玩家会对着一个只有名字的钮做不可逆决策:"
                + missing);
        }

        /// <summary>这个选项点下去有没有实际后果。**新增 EventOption 字段时要一并加进来** ——
        /// 漏一个,那种只用新字段的选项会被当成「离开」类而免检 detail。</summary>
        private static bool HasEffect(EventOption option) =>
            option.HpDelta != 0
            || option.Ink != 0
            || option.InkCost != 0
            || option.ComponentCost != 0
            || option.RandomComponents != 0
            || option.InkChancePercent != 0
            || option.MaxHpPercent != 0
            || option.MaxHpChancePercent != 0
            || option.GainChar != null
            || option.GainComponents.Count > 0
            || option.GainCharChoices.Count > 0;
    }
}
