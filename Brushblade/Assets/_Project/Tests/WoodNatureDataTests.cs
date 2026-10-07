using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>D2-0 Task 9:木字生面本体 = 本命(E6/E7/E8)。读真实 chars.json。
    /// Lv1 被动只含本命那一项(加底速);卡 Lv5(Lv3 已解锁)时本命强化覆盖进被动。</summary>
    public class WoodNatureDataTests
    {
        private static RecipeGraph _graph;
        private static RecipeGraph Graph => _graph ??= CharTableTests.RealGraph();

        // 𣛧 在 chars.json 里走 PUA 代理码位(同 TraitDataCoverageTests)
        private const string Ten = "\uE625";

        private static CharDef Def(string id) => Graph.All.First(d => d.Id == id);

        private static SummonPassive PassiveAt(string id, int level)
        {
            var effects = TraitRules.CastEffects(Def(id), CardFace.Feature, level);
            var summons = effects.Where(e => e.Kind == EffectKind.Summon).ToList();
            Assert.That(summons.Count, Is.EqualTo(1), id + " Lv" + level + " 应恰有一条 Summon");
            return summons[0].Passive;
        }

        /// <summary>被动里所有非缺省字段 → "名=值" 的排序列表,断言「只含这一项」用。</summary>
        private static List<string> NonDefault(SummonPassive p)
        {
            var list = new List<string>();
            if (p == null) return list;
            foreach (var prop in typeof(SummonPassive).GetProperties())
            {
                if (!prop.CanRead || prop.GetIndexParameters().Length > 0) continue;
                var v = prop.GetValue(p);
                if (v == null) continue;
                if (v is int i && i == 0) continue;
                if (v is bool b && !b) continue;
                if (v is TargetArea a && a == TargetArea.Single) continue;
                list.Add(prop.Name + "=" + v);
            }
            list.Sort();
            return list;
        }

        private static List<string> Exp(params string[] items)
        {
            var l = items.ToList();
            l.Sort();
            return l;
        }

        // 字 → (Lv1 被动, Lv3 本命强化后被动);底速 Speed=150 是属性,有的字原本就带
        private static IEnumerable<TestCaseData> Cases()
        {
            yield return new TestCaseData("花", Exp("OnHitCharmChance=20"), Exp("OnHitCharmChance=40"));
            yield return new TestCaseData("藤", Exp("OnHitFreezeChance=20", "OnHitFreezeTurns=1"),
                Exp("OnHitFreezeChance=40", "OnHitFreezeTurns=1"));
            yield return new TestCaseData("箭", Exp("Ranged=True"), Exp("Ranged=True", "BackRowBonusPercent=50"));
            yield return new TestCaseData("楸", Exp("OnHitBurn=1"), Exp("OnHitBurn=2"));
            yield return new TestCaseData("荆", Exp("Thorns=50"), Exp("Thorns=80"));
            yield return new TestCaseData("林", Exp("PerAllyAttackPercent=15", "Speed=150"),
                Exp("PerAllyAttackPercent=25", "Speed=150"));
            yield return new TestCaseData("柘", Exp("Armor=30", "Taunt=True"), Exp("Armor=60", "Taunt=True"));
            yield return new TestCaseData("桂", Exp("HealAlly=30"), Exp("HealAlly=30", "HealAllyTimes=2"));
            yield return new TestCaseData("藻", Exp("SproutPercent=30", "SproutMax=2", "Speed=150"),
                Exp("SproutPercent=30", "SproutMax=3", "Speed=150"));
            yield return new TestCaseData("森", Exp("EntrySaplings=1", "Speed=150"),
                Exp("EntrySaplings=2", "Speed=150"));
            yield return new TestCaseData(Ten, Exp("Shape=Adjacent", "ShapePercent=50", "Speed=150"),
                Exp("Shape=Row", "ShapePercent=50", "Speed=150"));
        }

        [TestCaseSource(nameof(Cases))]
        public void Lv1Passive_IsOnlyTheNature(string id, List<string> lv1, List<string> lv3)
        {
            Assert.That(NonDefault(PassiveAt(id, 1)), Is.EqualTo(lv1), id + " Lv1");
        }

        [TestCaseSource(nameof(Cases))]
        public void Lv5Passive_IsTheEnhancedNature(string id, List<string> lv1, List<string> lv3)
        {
            Assert.That(NonDefault(PassiveAt(id, 5)), Is.EqualTo(lv3), id + " Lv5");
        }

        [TestCaseSource(nameof(Cases))]
        public void Lv3Replacement_KeepsHpAttackAndCount(string id, List<string> lv1, List<string> lv3)
        {
            var a = TraitRules.CastEffects(Def(id), CardFace.Feature, 1).First(e => e.Kind == EffectKind.Summon);
            var b = TraitRules.CastEffects(Def(id), CardFace.Feature, 5).First(e => e.Kind == EffectKind.Summon);
            Assert.That((b.Value, b.SummonCount, b.SummonAttack), Is.EqualTo((a.Value, a.SummonCount, a.SummonAttack)), id);
            Assert.That(a.Value, Is.GreaterThan(0), id);
        }

        [Test]
        public void Hua_LingNature_IsMiXiang20Percent()
        {
            Assert.That(PassiveAt("花", 1).OnHitCharmChance, Is.EqualTo(20));
        }
    }
}
