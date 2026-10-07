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
        public void Fire_Yan_FeatureBurnAllHitsEnemy()
        {
            Assert.That(L("焱", CardFace.Feature), Is.EqualTo(FaceLanding.Enemy));
        }

        [TestCase("冻")]
        [TestCase("澡")]
        public void Water_Lv5_RandomSlowIsIncidental_StillSelfOrSummons(string id)
        {
            Assert.That(CardFaceRules.Landing(Def(id), CardFace.Feature, 5),
                Is.EqualTo(FaceLanding.Self | FaceLanding.Summons));
        }

        [Test]
        public void SelfLanding_NeverCoexistsWithHostileTargetedEffect()
        {
            foreach (var d in Graph.All.Where(d => !d.IsComponent && CardFaceRules.HasTwoFaces(d)))
                foreach (int lv in new[] { 1, 5, 8 })
                {
                    var landing = CardFaceRules.Landing(d, CardFace.Feature, lv);
                    if ((landing & FaceLanding.Self) == 0) continue;
                    foreach (var e in TraitRules.CastEffects(d, CardFace.Feature, lv))
                        Assert.That(CardFaceRules.IsHostileTargeted(e), Is.False,
                            $"{d.Id} Lv{lv} 五行面含 Self 落点却有敌对效果 {e.Kind}");
                }
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

        private static readonly EffectKind[] NonHostileKinds =
        {
            EffectKind.Shield, EffectKind.ShieldAll, EffectKind.BurnPotency, EffectKind.HealSelf, EffectKind.Summon,
            EffectKind.HealAll, EffectKind.HealOverTime, EffectKind.DefenseBuff, EffectKind.Cleanse,
            EffectKind.Immunity, EffectKind.Revive, EffectKind.Reflect, EffectKind.Empower, EffectKind.Morale,
            EffectKind.ApBoost, EffectKind.CritBuff, EffectKind.PierceBuff, EffectKind.Haste, EffectKind.Unseal,
            EffectKind.Block, EffectKind.Amplify, EffectKind.Reshape, EffectKind.Augment, EffectKind.DamageCut,
            EffectKind.CounterBoost, EffectKind.Endure, EffectKind.SummonSapling, EffectKind.HealSummons,
            EffectKind.ShieldSummons, EffectKind.ShieldFromHeal, EffectKind.AddWellspring, EffectKind.AddHeft,
            EffectKind.ShieldRecoil, EffectKind.Taunt,
        };

        [Test]
        public void EveryEffectKind_IsClassifiedHostileOrNot()
        {
            foreach (EffectKind k in System.Enum.GetValues(typeof(EffectKind)))
            {
                bool hostile = CardFaceRules.HostileKinds.Contains(k);
                bool non = System.Array.IndexOf(NonHostileKinds, k) >= 0;
                Assert.That(hostile != non, Is.True,
                    $"{k} 必须恰在敌对表或非敌对名单之一(hostile={hostile}, nonHostile={non});新增 Kind 要两处择一登记");
            }
        }
    }
}
