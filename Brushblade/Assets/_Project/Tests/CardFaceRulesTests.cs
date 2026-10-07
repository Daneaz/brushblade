using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>Plan E1 Task 1:两面字落点规则(F2/F4)。读真实 chars.json,Lv1。</summary>
    public class CardFaceRulesTests
    {
        private static RecipeGraph _graph;
        private static RecipeGraph Graph => _graph ??= CharTableTests.RealGraph();
        private static CharDef Def(string id) => Graph.All.First(d => d.Id == id);

        private static FaceLanding L(string id, CardFace f) => CardFaceRules.Landing(Def(id), f, 1);

        [Test]
        public void Fire_Heat_BothFacesHitEnemy()
        {
            Assert.That(CardFaceRules.HasTwoFaces(Def("热")), Is.True);
            Assert.That(L("热", CardFace.Attack), Is.EqualTo(FaceLanding.Enemy));
            Assert.That(L("热", CardFace.Feature), Is.EqualTo(FaceLanding.Enemy));
        }

        [TestCase("利")]
        [TestCase("冷")]
        [TestCase("碉")]
        public void FeatureFace_LandsOnSelfOrSummons(string id)
        {
            Assert.That(L(id, CardFace.Feature), Is.EqualTo(FaceLanding.Self | FaceLanding.Summons));
        }

        [Test]
        public void Metal_Li_AttackHitsEnemy()
        {
            Assert.That(L("利", CardFace.Attack), Is.EqualTo(FaceLanding.Enemy));
        }

        [Test]
        public void Wood_Teng_FeatureSummonsAndGrafts_AttackHitsEnemy()
        {
            Assert.That(L("藤", CardFace.Feature), Is.EqualTo(FaceLanding.EmptySlot | FaceLanding.Graft));
            Assert.That(L("藤", CardFace.Attack), Is.EqualTo(FaceLanding.Enemy));
        }

        [Test]
        public void AllTargetAttack_Bao_HitsEnemy()
        {
            Assert.That(L("爆", CardFace.Attack), Is.EqualTo(FaceLanding.Enemy));
        }

        [Test]
        public void SingleFaced_CannotFlip()
        {
            var single = Graph.All.FirstOrDefault(d => !CardFaceRules.HasTwoFaces(d));
            Assert.That(single, Is.Not.Null);
            Assert.That(CardFaceRules.HasTwoFaces(single), Is.False);
        }

        [Test]
        public void EveryTwoFacedChar_HasLandingOnBothFaces()
        {
            var two = Graph.All.Where(d => !d.IsComponent && CardFaceRules.HasTwoFaces(d)).ToList();
            Assert.That(two.Count, Is.GreaterThan(10), "夹具有效性");
            foreach (var d in two)
            {
                Assert.That(CardFaceRules.Landing(d, CardFace.Attack, 1), Is.Not.EqualTo(FaceLanding.None), d.Id + " 攻击面");
                Assert.That(CardFaceRules.Landing(d, CardFace.Feature, 1), Is.Not.EqualTo(FaceLanding.None), d.Id + " 五行面");
            }
        }
    }
}
