using System.IO;
using System.Linq;
using Brushblade.Core;
using Brushblade.Data;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>spec C §6:读释放层数的效果必须排在第一条 SpendHeft / SpendWellspring 之后,
    /// 否则引擎读到 Spent == 0 会静默跳过。pytest 侧的顺序锁假定「折叠时本体先于特性」,这里锁住这个前提。</summary>
    public class SpendFoldOrderTests
    {
        private static RecipeGraph RealGraph()
        {
            var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Brushblade")))
                dir = dir.Parent;
            Assert.That(dir, Is.Not.Null, "找不到仓库根目录");
            return ConfigLoader.LoadGraph(File.ReadAllText(Path.Combine(
                dir.FullName, "Brushblade", "Assets", "StreamingAssets", "config", "chars.json")));
        }

        private static bool IsSpend(EffectDef e) => e.Kind == EffectKind.SpendHeft || e.Kind == EffectKind.SpendWellspring;

        private static bool ReadsSpent(EffectDef e) =>
            e.MinSpent > 0 || e.ScaleBy == ScaleBasis.Spent
            || e.Kind == EffectKind.SpendHeal || e.Kind == EffectKind.SpendShield;

        [TestCase("崩")]
        [TestCase("㙓")]
        [TestCase("溃")]
        [TestCase("㵘")]
        public void SpendChars_AtLv8_EveryReaderFollowsTheFirstSpend(string id)
        {
            var def = RealGraph().Get(id);
            var effects = TraitRules.CastEffects(def, CardFace.Attack, 8);
            var firstSpend = effects.FindIndex(IsSpend);
            Assert.That(firstSpend, Is.GreaterThanOrEqualTo(0), id + " 攻击面应有 Spend");
            var readers = effects.Select((e, i) => (e, i)).Where(x => ReadsSpent(x.e)).ToList();
            Assert.That(readers.Count, Is.GreaterThan(0), id + " Lv8 应有读层数的效果");
            foreach (var (e, i) in readers)
                Assert.That(i, Is.GreaterThan(firstSpend), $"{id}: {e.Kind} 排在第一条 Spend 之前");
        }
    }
}
