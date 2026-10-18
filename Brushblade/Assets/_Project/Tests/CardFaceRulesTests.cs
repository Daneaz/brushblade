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
        public void Fire_Heat_AttackHitsEnemy_FeatureLandsOnSelfOrSummons()
        {
            Assert.That(CardFaceRules.HasTwoFaces(Def("热")), Is.True);
            Assert.That(L("热", CardFace.Attack), Is.EqualTo(FaceLanding.Enemy));
            // spec C §5.1 / Q12:火系五行面改燃意,拖到自己或木灵
            Assert.That(L("热", CardFace.Feature), Is.EqualTo(FaceLanding.Self | FaceLanding.Summons));
        }

        [TestCase("利")]
        [TestCase("冷")]
        [TestCase("碉")]
        public void FeatureFace_LandsOnSelfOrSummons(string id)
        {
            Assert.That(L(id, CardFace.Feature), Is.EqualTo(FaceLanding.Self | FaceLanding.Summons));
        }

        /// <summary>D2-水 E26(溃·溃围):润面带 `pick All` 的敌方效果(不需要选敌)不给落点加 Enemy ——
        /// 需要友方目标的面仍只落友方;没有友方需求时照旧(火海这类全体减攻面仍可拖到敌人身上)。</summary>
        [Test]
        public void AllyFace_WithPickAllHostileRider_DoesNotLandOnEnemy()
        {
            var def = new CharDef("溃试", Element.Water,
                effects: new[] { new EffectDef(EffectKind.HealSelf, 30), new EffectDef(EffectKind.Slow, 1, pick: EffectPick.All) },
                attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 30) });
            Assert.That(CardFaceRules.Landing(def, CardFace.Feature, 1), Is.EqualTo(FaceLanding.Self | FaceLanding.Summons));

            var noAlly = new CharDef("溃试二", Element.Water,
                effects: new[] { new EffectDef(EffectKind.Weaken, 25, turns: 2, pick: EffectPick.All) },
                attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 30) });
            Assert.That(CardFaceRules.Landing(noAlly, CardFace.Feature, 1).HasFlag(FaceLanding.Enemy), Is.True,
                "没有友方需求的面:pick All 敌对效果照旧构成 Enemy 落点");
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
        public void Fire_Yan_KindleAllAllies_LandsOnSelfOrSummons()
        {
            // spec C §5.1 / Q12:焱五行面 = 燃意我方全体,不再带全体灼;燃意面落点恒为自己或木灵
            Assert.That(L("焱", CardFace.Feature), Is.EqualTo(FaceLanding.Self | FaceLanding.Summons));
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
                    // D2-水 E26(Q25,溃围 `Slow 1 pick All`):需要友方目标的面,pick All 的敌方附带不选敌,允许与 Self 落点共存;
                    // 落在主目标上的敌对效果仍然不许。
                    // spec C §5.1 / §5.2:燃面落 Self 后,BurnAll(本就全体)、pick Random / MostBurn 都不选敌 ——
                    // 判据改用 EffectNeedsTarget(与 NeedsTarget 同口径),不再只认 pick All
                    foreach (var e in TraitRules.CastEffects(d, CardFace.Feature, lv))
                        Assert.That(CardFaceRules.IsHostileTargeted(e) && BattleEngine.EffectNeedsTarget(e), Is.False,
                            $"{d.Id} Lv{lv} 五行面含 Self 落点却有选主目标的敌对效果 {e.Kind}");
                }
        }

        /// <summary>我方全体类五行面(溃的群治、全体护盾)免选目标,但落点放开到玩家 / 木灵;溃围的敌对 pick All 不另加 Enemy。</summary>
        [Test]
        public void AllyAllFace_LandsOnSelfOrSummons()
        {
            Assert.That(L("溃", CardFace.Feature).HasFlag(FaceLanding.Self), Is.True, "溃 五行面落 Self");
            Assert.That(L("溃", CardFace.Feature).HasFlag(FaceLanding.Summons), Is.True, "溃 五行面落 Summons");
            Assert.That(L("崩", CardFace.Feature), Is.EqualTo(FaceLanding.Self | FaceLanding.Summons));

            foreach (var kind in new[] { EffectKind.ShieldAll, EffectKind.HealAll })
            {
                var def = new CharDef("全体试", Element.Water,
                    effects: new[] { new EffectDef(kind, 10) },
                    attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 30) });
                Assert.That(CardFaceRules.Landing(def, CardFace.Feature, 1), Is.EqualTo(FaceLanding.Self | FaceLanding.Summons), kind.ToString());
            }
        }

        /// <summary>召唤面折入的我方全体效果(桂 Lv5 桂花雨 HealAll)不放开玩家 / 木灵落点:召唤面只落空位 / 嫁接。</summary>
        [Test]
        public void SummonFace_WithFoldedAllyAll_DoesNotLandOnSelf()
        {
            var landing = CardFaceRules.Landing(Def("桂"), CardFace.Feature, 5);
            Assert.That(landing.HasFlag(FaceLanding.Self), Is.False, "桂 Lv5 召唤面不落玩家");
            Assert.That(landing.HasFlag(FaceLanding.EmptySlot), Is.True, "桂 Lv5 召唤面落空位");
        }

        [Test]
        public void AllyAllFace_WithPickAllHostile_KeepsEnemyLanding()
        {
            var def = new CharDef("溃围试", Element.Water,
                effects: new[] { new EffectDef(EffectKind.HealAll, 10), new EffectDef(EffectKind.Slow, 1, pick: EffectPick.All) },
                attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 30) });
            var l = CardFaceRules.Landing(def, CardFace.Feature, 1);
            Assert.That(l.HasFlag(FaceLanding.Self) && l.HasFlag(FaceLanding.Summons), Is.True);
            Assert.That(l.HasFlag(FaceLanding.Enemy), Is.True, "溃围的 Enemy 落点要保留");
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

        [Test]
        public void ReviveFace_LandsOnSummons_SoCorpsesCanBeDropTargets()
        {
            // 2026-10-18 bug:沐「苏」拖到阵亡木灵上没有落点 —— 复活面必须放开木灵落点(尸体格据此画落点)
            var mu = Graph.All.First(d => d.Id == "沐");
            var landing = CardFaceRules.Landing(mu, CardFace.Feature, 1);
            Assert.That((landing & FaceLanding.Summons) != 0, Is.True, landing.ToString());
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
            EffectKind.Retaliate,   // D2-火 Task 4:挂在玩家身上(回敬的对象是攻击者,不是出字时选的敌人)
            EffectKind.SelfCost,    // D2-火 Task 5:扣的是玩家自己的生命
            EffectKind.BlockMod,    // D2-金 Task 1:格挡修饰器,出字前折进本面的 Block
            EffectKind.MoraleOverflowShield, EffectKind.MoraleArmor, EffectKind.MoraleShield,   // D2-金 Task 4:战意族,作用于玩家自己
            EffectKind.DebuffWard,  // D2-水 Task 3:免疫减益挂在落点(玩家或木灵)身上
            EffectKind.HurtHeal, EffectKind.ShieldFrost, EffectKind.TurnPulse,   // D2-水 Task 4:挂在玩家身上
            EffectKind.ShieldSpend, EffectKind.ShieldToMorale,   // D2-土 Task 2:作用于玩家自己的护盾
            EffectKind.HitCap, EffectKind.HurtShield, EffectKind.ShieldShatter,   // D2-土 Task 3:挂在玩家身上
            EffectKind.ApCost,      // D2-土 Task 4:AP 代价挂在玩家身上
            // D2-木 Task 1 预留:木灵光环 / 远程挂玩家袋,并入 / 立即行动 / 折桂作用于我方
            EffectKind.SummonEmpower, EffectKind.SummonThorns, EffectKind.SummonRanged,
            EffectKind.SummonMerge, EffectKind.SummonAct, EffectKind.GrantChar,
            EffectKind.Lifesteal, EffectKind.OverhealShield,   // spec A D8:回复玩家 / 给玩家护盾
            EffectKind.BlockGain, EffectKind.ReviveShield,     // spec A D7 / D8:给玩家格挡 / 给复活木灵护盾
            EffectKind.Kindle, EffectKind.Attune, EffectKind.SpendHeal, EffectKind.SpendShield,   // spec C:作用于我方 / 木灵属性
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
