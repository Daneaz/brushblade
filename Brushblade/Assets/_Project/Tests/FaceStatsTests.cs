using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>Plan E2 Task 1:每面数值格(<see cref="FaceStats"/>)。口径 Q5(Lv1 本体 + Lv3 替换,按卡等级缩放,
    /// 不并入 Lv4+)、Q6(每面 1–3 格,主量优先)、Q2(Lv3 本体差)、Q7(生面本命格)。读真实 chars.json。</summary>
    public class FaceStatsTests
    {
        private static RecipeGraph _graph;
        private static RecipeGraph Graph => _graph ??= CharTableTests.RealGraph();

        private static List<CharDef> Playable() =>
            Graph.All.Where(d => !d.IsComponent && d.Element != null).ToList();

        private static string Cells(IEnumerable<FaceStat> cells) =>
            string.Join(",", cells.Select(c => $"{c.Kind}:{c.Value}"));

        private static string Diffs(IEnumerable<StatDelta> deltas) =>
            string.Join(",", deltas.Select(d => $"{d.Face}.{d.Kind}:{d.From}->{d.To}"));

        private static int Scale(int v, int lv) => MetaRules.ScaleByCardLevel(v, lv);

        // ---------------- Body ----------------

        [Test]
        public void Body_Yan_Attack()
        {
            var yan = Graph.Get("炎");
            Assert.That(Cells(FaceStats.Body(yan, CardFace.Attack, 1)), Is.EqualTo("Damage:168,Burn:3"));
            Assert.That(Cells(FaceStats.Body(yan, CardFace.Attack, 3)), Is.EqualTo($"Damage:{Scale(168, 3)},Burn:4"), "Lv3 灼 3→4");
            Assert.That(Cells(FaceStats.Body(yan, CardFace.Attack, 10)), Is.EqualTo($"Damage:{Scale(168, 10)},Burn:4"),
                "伤害按等级缩放;层数不缩放,也不并入 Lv4+");
        }

        [Test]
        public void Body_Yan_Feature_WeakenThenDuration()
        {
            var yan = Graph.Get("炎");
            Assert.That(Cells(FaceStats.Body(yan, CardFace.Feature, 1)), Is.EqualTo("Burn:3,Weaken:15,Duration:2"));
            Assert.That(Cells(FaceStats.Body(yan, CardFace.Feature, 10)), Is.EqualTo($"Burn:4,Weaken:{Scale(15, 10)},Duration:3"));
        }

        [Test]
        public void Body_OtherElements()
        {
            Assert.That(Cells(FaceStats.Body(Graph.Get("剑"), CardFace.Attack, 1)), Is.EqualTo("Damage:94,Morale:1"));
            Assert.That(Cells(FaceStats.Body(Graph.Get("剑"), CardFace.Feature, 1)), Is.EqualTo($"Morale:1,Block:1,Counter:{94 * 30 / 100}"));
            Assert.That(Cells(FaceStats.Body(Graph.Get("剑"), CardFace.Feature, 10)),
                Is.EqualTo($"Morale:2,Block:2,Counter:{Scale(94, 10) * 30 / 100}"), "反击 = 攻面本体(吃等级)× 30%");
            Assert.That(Cells(FaceStats.Body(Graph.Get("林"), CardFace.Attack, 1)), Is.EqualTo("Damage:233,Seed:2"));
            Assert.That(Cells(FaceStats.Body(Graph.Get("林"), CardFace.Feature, 1)), Is.EqualTo("SummonHp:346,SummonAttack:73,Nature:0"));
            Assert.That(Cells(FaceStats.Body(Graph.Get("荆"), CardFace.Feature, 1)), Is.EqualTo("SummonHp:499,Nature:0"), "攻击 0 不出格");
            Assert.That(Cells(FaceStats.Body(Graph.Get("花"), CardFace.Attack, 3)), Is.EqualTo($"Damage:{Scale(36, 3)},Charm:2"));
            Assert.That(Cells(FaceStats.Body(Graph.Get("冷"), CardFace.Attack, 1)), Is.EqualTo("Damage:45,Slow:1"));
            Assert.That(Cells(FaceStats.Body(Graph.Get("冷"), CardFace.Feature, 1)), Is.EqualTo("Heal:45,HealOverTime:2"));
            Assert.That(Cells(FaceStats.Body(Graph.Get("冰"), CardFace.Attack, 3)), Is.EqualTo($"Damage:{Scale(152, 3)},Freeze:2"));
            Assert.That(Cells(FaceStats.Body(Graph.Get("㙓"), CardFace.Attack, 1)), Is.EqualTo("Damage:235,ArmorBreak:2"));
            Assert.That(Cells(FaceStats.Body(Graph.Get("㙓"), CardFace.Feature, 1)), Is.EqualTo("Shield:235,Armor:100,Duration:2"));
            Assert.That(Cells(FaceStats.Body(Graph.Get("㙓"), CardFace.Feature, 4)),
                Is.EqualTo($"Shield:{Scale(235, 4)},Armor:{Scale(100, 4)},Duration:4"), "不并入 Lv4 深厚的回合加成(Q5)");
        }

        [Test]
        public void Body_NatureCell_CarriesSummonPassive()
        {
            var cell = FaceStats.Body(Graph.Get("林"), CardFace.Feature, 3).Single(c => c.Kind == FaceStatKind.Nature);
            Assert.That(cell.Passive, Is.Not.Null);
            Assert.That(cell.Source, Is.EqualTo(EffectKind.Summon));
            var hp = FaceStats.Body(Graph.Get("林"), CardFace.Feature, 3).Single(c => c.Kind == FaceStatKind.SummonHp);
            Assert.That(hp.Passive, Is.Null, "只有本命格带被动");
        }

        [Test]
        public void Body_AllFaces_OneToThreeCells_PrimaryFirst()
        {
            foreach (var d in Playable())
                foreach (var face in new[] { CardFace.Attack, CardFace.Feature })
                    foreach (int lv in new[] { 1, 3, 10 })
                    {
                        var cells = FaceStats.Body(d, face, lv);
                        Assert.That(cells.Count, Is.InRange(1, 3), $"{d.Id} {face} L{lv}");
                        if (face == CardFace.Attack)
                            Assert.That(cells[0].Kind, Is.EqualTo(FaceStatKind.Damage), $"{d.Id} 攻面首格");
                        if (d.Element == Element.Wood && face == CardFace.Feature)
                        {
                            Assert.That(cells[0].Kind, Is.EqualTo(FaceStatKind.SummonHp), $"{d.Id} 生面首格是血");
                            var summon = TraitRules.CastEffects(d, CardFace.Feature, 1).First(e => e.Kind == EffectKind.Summon);
                            Assert.That(cells.Any(c => c.Kind == FaceStatKind.SummonAttack), Is.EqualTo(summon.SummonAttack > 0),
                                $"{d.Id} 生面攻击格");
                        }
                    }
        }

        [Test]
        public void Body_DiscreteCellsDoNotScale()
        {
            foreach (var d in Playable())
                foreach (var face in new[] { CardFace.Attack, CardFace.Feature })
                {
                    var l3 = FaceStats.Body(d, face, 3);
                    var l10 = FaceStats.Body(d, face, 10);
                    Assert.That(l10.Select(c => c.Kind), Is.EqualTo(l3.Select(c => c.Kind)), $"{d.Id} {face}");
                    for (int i = 0; i < l3.Count; i++)
                        if (!FaceStats.Scales(l3[i].Kind))
                            Assert.That(l10[i].Value, Is.EqualTo(l3[i].Value), $"{d.Id} {face} {l3[i].Kind}");
                }
        }

        /// <summary>Task 1 review Minor 4:Body 截前 3 格、Deltas 用不截断的全部格,两者口径一致的前提是
        /// 真实字表里每面每级的**未截断**格数 ≤ 3;Deltas 遇到「新出现的格」静默跳过,前提是相邻两级格种类序列不变。</summary>
        [Test]
        public void AllCells_RealTable_AtMostThree_AndStableAcrossAdjacentLevels()
        {
            foreach (var d in Playable())
                foreach (var face in new[] { CardFace.Attack, CardFace.Feature })
                    for (int lv = 1; lv <= MetaRules.MaxCardLevel; lv++)
                    {
                        var cells = FaceStats.AllCells(d, face, lv, lv, TraitSlot.Lv4);
                        Assert.That(cells.Count, Is.LessThanOrEqualTo(FaceStats.MaxCells), $"{d.Id} {face} L{lv}:{Cells(cells)}");
                        if (lv == 1) continue;
                        var prev = FaceStats.AllCells(d, face, lv - 1, lv - 1, TraitSlot.Lv4);
                        Assert.That(cells.Select(c => c.Kind), Is.EqualTo(prev.Select(c => c.Kind)),
                            $"{d.Id} {face} L{lv - 1}→L{lv}:{Cells(prev)} → {Cells(cells)}");
                    }
        }

        /// <summary>Task 1 review Minor 2:<see cref="FaceStats.Scales"/> 是手写分类,引擎缩放走
        /// <see cref="MetaRules.ScalesWithCardLevel"/>。L1→L2 没有特性变化,只差等级系数(×1.06,向上取整,
        /// 任何正数都会变),所以「这一格变了」⇔「这一格缩放」。两个方向都断:Meta 把某种改成离散而 Scales 没跟,
        /// 或 Scales 漏标一种连续量,都会红。并要求每种标了缩放的格在真实字表里都出现过,免得对账落空。</summary>
        [Test]
        public void Scales_ReconcilesWithEngineScaling()
        {
            var observed = new HashSet<FaceStatKind>();
            foreach (var d in Playable())
                foreach (var face in new[] { CardFace.Attack, CardFace.Feature })
                {
                    var l1 = FaceStats.Body(d, face, 1);
                    var l2 = FaceStats.Body(d, face, 2);
                    Assert.That(l2.Select(c => c.Kind), Is.EqualTo(l1.Select(c => c.Kind)), $"{d.Id} {face}");
                    for (int i = 0; i < l1.Count; i++)
                    {
                        if (l1[i].Value <= 0) continue;   // 本命格恒 0
                        bool changed = l2[i].Value != l1[i].Value;
                        Assert.That(changed, Is.EqualTo(FaceStats.Scales(l1[i].Kind)),
                            $"{d.Id} {face} {l1[i].Kind}({l1[i].Source}) {l1[i].Value}→{l2[i].Value}");
                        if (changed) observed.Add(l1[i].Kind);
                    }
                }
            var unobserved = System.Enum.GetValues(typeof(FaceStatKind)).Cast<FaceStatKind>()
                .Where(k => FaceStats.Scales(k) && !observed.Contains(k)).ToList();
            Assert.That(unobserved, Is.Empty, "标了缩放、真实字表里却没出现过的格:" + string.Join(",", unobserved));
        }

        // ---------------- Deltas ----------------

        [Test]
        public void Deltas_Yan_L1ToL2_OnlyScaledValues()
        {
            Assert.That(Diffs(FaceStats.Deltas(Graph.Get("炎"), 1, 2)),
                Is.EqualTo($"Attack.Damage:168->{Scale(168, 2)},Feature.Weaken:15->{Scale(15, 2)}"));
        }

        [Test]
        public void Deltas_Yan_L2ToL3_IncludesStacksAndTurns()
        {
            Assert.That(Diffs(FaceStats.Deltas(Graph.Get("炎"), 2, 3)),
                Is.EqualTo($"Attack.Damage:{Scale(168, 2)}->{Scale(168, 3)},Attack.Burn:3->4,"
                    + $"Feature.Burn:3->4,Feature.Weaken:{Scale(15, 2)}->{Scale(15, 3)},Feature.Duration:2->3"));
        }

        [Test]
        public void Deltas_AllChars_L1ToL2_OnlyScalable_L2ToL3_HasDiscrete()
        {
            foreach (var d in Playable())
            {
                var up2 = FaceStats.Deltas(d, 1, 2);
                Assert.That(up2, Is.Not.Empty, d.Id);
                Assert.That(up2.All(x => FaceStats.Scales(x.Kind)), Is.True, $"{d.Id}:{Diffs(up2)}");
                var up3 = FaceStats.Deltas(d, 2, 3);
                Assert.That(up3.Any(x => !FaceStats.Scales(x.Kind)), Is.True, $"{d.Id}:{Diffs(up3)}");
                // CardFace 枚举是 Feature = 0、Attack = 1,不能用 Is.Ordered
                var faces = up3.Select(x => x.Face).ToList();
                Assert.That(faces.SkipWhile(f => f == CardFace.Attack).All(f => f == CardFace.Feature), Is.True,
                    $"{d.Id}:先攻面后五行面");
                Assert.That(up3.All(x => x.From != x.To), Is.True, d.Id);
            }
        }

        [Test]
        public void Deltas_NoChange_Empty()
        {
            var yan = Graph.Get("炎");
            Assert.That(FaceStats.Deltas(yan, 5, 5), Is.Empty);
            Assert.That(FaceStats.Deltas(yan, 1, 1), Is.Empty);
            var flat = new CharDef("锐", Element.Metal, effects: new[] { new EffectDef(EffectKind.Morale, 1) },
                attackEffects: new[] { new EffectDef(EffectKind.Morale, 2) });
            Assert.That(FaceStats.Deltas(flat, 1, 2), Is.Empty, "只有离散量、没有特性:升级不变");
        }

        // ---------------- Lv3Change(Q2) ----------------

        [Test]
        public void Lv3Change_Yan()
        {
            var yan = Graph.Get("炎");
            Assert.That(Diffs(FaceStats.Lv3Change(yan, CardFace.Attack)), Is.EqualTo("Attack.Burn:3->4"));
            Assert.That(Diffs(FaceStats.Lv3Change(yan, CardFace.Feature)), Is.EqualTo("Feature.Burn:3->4,Feature.Duration:2->3"));
        }

        [Test]
        public void Lv3Change_OtherElements()
        {
            Assert.That(Diffs(FaceStats.Lv3Change(Graph.Get("剑"), CardFace.Feature)), Is.EqualTo("Feature.Morale:1->2,Feature.Block:1->2"));
            Assert.That(Diffs(FaceStats.Lv3Change(Graph.Get("㙓"), CardFace.Attack)), Is.EqualTo("Attack.ArmorBreak:2->4"));
            Assert.That(Diffs(FaceStats.Lv3Change(Graph.Get("林"), CardFace.Attack)), Is.EqualTo("Attack.Seed:2->3"));
            Assert.That(FaceStats.Lv3Change(Graph.Get("林"), CardFace.Feature), Is.Empty, "本命强化只改被动,没有数值差");
        }

        [Test]
        public void Lv3Change_ComparesAtSameLevel_NoScalingNoise()
        {
            // 同一级比「有 / 无 Lv3」:伤害不会因为等级系数出现在差里
            var yan = Graph.Get("炎");
            Assert.That(Diffs(FaceStats.Lv3Change(yan, CardFace.Attack, 10)), Is.EqualTo("Attack.Burn:3->4"));
            Assert.That(Diffs(FaceStats.Lv3Change(yan, CardFace.Attack, 1)), Is.EqualTo("Attack.Burn:3->4"), "未解锁时按 Lv3 印(Q17)");
        }

        // ---------------- 持续格的归属(E2 Task 3 review Minor 4) ----------------

        /// <summary>一面同时有「减攻 + 持续」与「护甲 + 持续」两组:每条持续格 / 持续差都记着它跟的是哪一组,
        /// Presentation 按它配前缀(「减攻 2→3 回合」「护甲 2→4 回合」),不再去本体里找第一条持续格。</summary>
        [Test]
        public void Duration_CarriesOwner_TwoGroupsOnOneFace()
        {
            var def = new CharDef("锐", Element.Metal,
                effects: new[]
                {
                    new EffectDef(EffectKind.Weaken, 15, turns: 2),
                    new EffectDef(EffectKind.DefenseBuff, 50, turns: 2),
                },
                attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 10) },
                traits: new[]
                {
                    new TraitDef(TraitSlot.Lv1, TraitFace.Both, TraitForm.Passive, null, "锐", new EffectDef[0]),
                    new TraitDef(TraitSlot.Lv3, TraitFace.Feature, TraitForm.Passive, TraitSlot.Lv1, "锐·强化", new[]
                    {
                        new EffectDef(EffectKind.Weaken, 15, turns: 3),
                        new EffectDef(EffectKind.DefenseBuff, 50, turns: 4),
                    }),
                });

            var body = FaceStats.Body(def, CardFace.Feature, 1).Where(c => c.Kind == FaceStatKind.Duration).ToList();
            Assert.That(body.Select(c => c.DurationOf), Is.EqualTo(new FaceStatKind?[] { FaceStatKind.Weaken }),
                "Body 截前 3 格:减攻 / 持续 / 护甲,第二条持续被截掉");

            var diffs = FaceStats.Lv3Change(def, CardFace.Feature).Where(d => d.Kind == FaceStatKind.Duration).ToList();
            Assert.That(diffs.Select(d => $"{d.DurationOf}:{d.From}->{d.To}"),
                Is.EqualTo(new[] { "Weaken:2->3", "Armor:2->4" }));
            Assert.That(FaceStats.Lv3Change(def, CardFace.Feature).Where(d => d.Kind != FaceStatKind.Duration)
                .All(d => d.DurationOf == null), Is.True, "只有持续格带归属");
        }
    }
}
