using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>快速施法(spec 2026-10-20):落点阵营决定两面字用哪一面。敌方 = 攻击面、我方 = 五行面;
    /// 一侧两面都能落 → 按朝上的面;一侧哪面都落不了 → null。</summary>
    public class QuickCastTests
    {
        private static RecipeGraph _graph;
        private static RecipeGraph Graph => _graph ??= CharTableTests.RealGraph();
        private static CharDef Def(string id) => Graph.All.First(d => d.Id == id);

        private static readonly int[] Levels = { 1, 5, 8, 10 };

        [Test]
        public void RealTable_EnemySide_IsAttackFace_UnlessFeatureAlsoLandsEnemy()
        {
            foreach (var def in Graph.All.Where(CardFaceRules.HasTwoFaces))
                foreach (int lv in Levels)
                {
                    bool featureHitsEnemy = (CardFaceRules.Landing(def, CardFace.Feature, lv) & FaceLanding.Enemy) != 0;
                    var expectWhenFeatureUp = featureHitsEnemy ? CardFace.Feature : CardFace.Attack;
                    Assert.That(CardFaceRules.QuickCastFace(def, FaceSide.Enemy, CardFace.Attack, lv),
                        Is.EqualTo(CardFace.Attack), $"{def.Id} Lv{lv} 攻击面朝上");
                    Assert.That(CardFaceRules.QuickCastFace(def, FaceSide.Enemy, CardFace.Feature, lv),
                        Is.EqualTo(expectWhenFeatureUp), $"{def.Id} Lv{lv} 五行面朝上");
                }
        }

        [Test]
        public void RealTable_AllySide_IsFeatureFace_WhicheverFaceIsUp()
        {
            foreach (var def in Graph.All.Where(CardFaceRules.HasTwoFaces))
                foreach (int lv in Levels)
                    foreach (var up in new[] { CardFace.Attack, CardFace.Feature })
                        Assert.That(CardFaceRules.QuickCastFace(def, FaceSide.Ally, up, lv),
                            Is.EqualTo(CardFace.Feature), $"{def.Id} Lv{lv} 朝上 {up}");
        }

        /// <summary>溃 Lv5+ 的五行面也能落敌方:敌侧两面都行 → 按朝上的面(用户 2026-10-20 拍板)。</summary>
        [Test]
        public void Kui_Lv5_EnemySide_FollowsFaceUp()
        {
            var def = Def("溃");
            Assume.That(CardFaceRules.Landing(def, CardFace.Feature, 5) & FaceLanding.Enemy, Is.Not.EqualTo(FaceLanding.None));
            Assert.That(CardFaceRules.QuickCastFace(def, FaceSide.Enemy, CardFace.Feature, 5), Is.EqualTo(CardFace.Feature));
            Assert.That(CardFaceRules.QuickCastFace(def, FaceSide.Enemy, CardFace.Attack, 5), Is.EqualTo(CardFace.Attack));
        }

        [Test]
        public void FeatureFaceOnlyHitsEnemies_AllySideIsNull_EnemySideFollowsFaceUp()
        {
            var def = new CharDef("全敌试", Element.Water,
                effects: new[] { new EffectDef(EffectKind.Weaken, 25, turns: 2, pick: EffectPick.All) },
                attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 30) });
            Assert.That(CardFaceRules.QuickCastFace(def, FaceSide.Ally, CardFace.Attack, 1), Is.Null);
            Assert.That(CardFaceRules.QuickCastFace(def, FaceSide.Enemy, CardFace.Feature, 1), Is.EqualTo(CardFace.Feature));
            Assert.That(CardFaceRules.QuickCastFace(def, FaceSide.Enemy, CardFace.Attack, 1), Is.EqualTo(CardFace.Attack));
        }

        [Test]
        public void SingleFaceChar_IsNull()
        {
            var def = new CharDef("单面试", Element.Metal,
                effects: new EffectDef[0],
                attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 30) });
            Assert.That(CardFaceRules.HasTwoFaces(def), Is.False);
            Assert.That(CardFaceRules.QuickCastFace(def, FaceSide.Enemy, CardFace.Attack, 1), Is.Null);
            Assert.That(CardFaceRules.QuickCastFace(def, FaceSide.Ally, CardFace.Attack, 1), Is.Null);
        }

        [Test]
        public void Settings_QuickCast_DefaultsOff_AndSurvivesSave()
        {
            Assert.That(new SettingsState().QuickCast, Is.False);
            var meta = new MetaState();
            meta.Settings.QuickCast = true;
            var restored = Data.SaveSerializer.FromJson(Data.SaveSerializer.ToJson(meta));
            Assert.That(restored.Settings.QuickCast, Is.True);
        }
    }
}
