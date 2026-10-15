using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>Plan E2 Task 1:每面数值格(<see cref="FaceStats"/>)。口径 Q5(Lv1 本体 + Lv3 替换,按卡等级缩放,
    /// 不并入 Lv4+)、Q6(每面 1–3 格,主量优先)、Q2(Lv3 本体差)、Q7(生面本命格)。读真实 chars.json。
    /// 期望值按 spec A(2026-10-15)重算:单体 = 锚点 × 70%(金 195 / 蓝 90 / 红 420 …),五行面附带灼 / 战意恒 1,
    /// 慑 / 格挡 / 护甲按档梯度,Lv3 只强化主手段。</summary>
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
            Assert.That(Cells(FaceStats.Body(yan, CardFace.Attack, 1)), Is.EqualTo("Damage:195,Burn:3"));
            Assert.That(Cells(FaceStats.Body(yan, CardFace.Attack, 3)), Is.EqualTo($"Damage:{Scale(195, 3)},Burn:4"), "Lv3 灼 3→4");
            Assert.That(Cells(FaceStats.Body(yan, CardFace.Attack, 10)), Is.EqualTo($"Damage:{Scale(195, 10)},Burn:4"),
                "伤害按等级缩放;层数不缩放,也不并入 Lv4+");
        }

        [Test]
        public void Body_Yan_Feature_WeakenThenDuration()
        {
            var yan = Graph.Get("炎");
            // 金档慑 25% / 3 回合、灼恒 1;Lv3 慑·强化 = 30% / 4 回合,灼不动
            Assert.That(Cells(FaceStats.Body(yan, CardFace.Feature, 1)), Is.EqualTo("Burn:1,Weaken:25,Duration:3"));
            Assert.That(Cells(FaceStats.Body(yan, CardFace.Feature, 10)), Is.EqualTo($"Burn:1,Weaken:{Scale(30, 10)},Duration:4"));
        }

        [Test]
        public void Body_OtherElements()
        {
            // 剑(蓝):格挡 2 次、反击 40%;Lv3 格挡·强化 3 次、反击 50%;五行面战意恒 1(spec A §4.2)
            Assert.That(Cells(FaceStats.Body(Graph.Get("剑"), CardFace.Attack, 1)), Is.EqualTo("Damage:90,Morale:1"));
            Assert.That(Cells(FaceStats.Body(Graph.Get("剑"), CardFace.Feature, 1)), Is.EqualTo("Morale:1,Block:2,CounterPercent:40"));
            Assert.That(Cells(FaceStats.Body(Graph.Get("剑"), CardFace.Feature, 10)),
                Is.EqualTo("Morale:1,Block:3,CounterPercent:50"), "反击统一按 %(2026-10-15 用户):不随卡等级变");
            // 木灵 血 = 单体 × 1.5、攻 = 单体 × 0.35;荆肉盾 血 = 单体 × 3、攻 0(spec A §4.5)
            Assert.That(Cells(FaceStats.Body(Graph.Get("林"), CardFace.Attack, 1)), Is.EqualTo("Damage:195,Seed:2"));
            Assert.That(Cells(FaceStats.Body(Graph.Get("林"), CardFace.Feature, 1)), Is.EqualTo("SummonHp:293,SummonAttack:68,Nature:0"));
            Assert.That(Cells(FaceStats.Body(Graph.Get("荆"), CardFace.Feature, 1)), Is.EqualTo("SummonHp:399,Nature:0"), "攻击 0 不出格");
            Assert.That(Cells(FaceStats.Body(Graph.Get("花"), CardFace.Attack, 3)), Is.EqualTo($"Damage:{Scale(42, 3)},Seed:3"), "花攻击面魅惑 → 种(§5)");
            Assert.That(Cells(FaceStats.Body(Graph.Get("冷"), CardFace.Attack, 1)), Is.EqualTo("Damage:42,Slow:1"));
            // 涓(冷):本体只有 HealOverTime 17 × 3 回合。现行 FaceStats 的 HealOverTime 格只印回合数,
            // 每回合量没有格 —— 已在 G1 报告里列为待 controller 裁定(spec B 出格或 FaceStats 补格)
            Assert.That(Cells(FaceStats.Body(Graph.Get("冷"), CardFace.Feature, 1)), Is.EqualTo("HealOverTime:3"));
            Assert.That(Cells(FaceStats.Body(Graph.Get("海"), CardFace.Feature, 1)), Is.EqualTo("Heal:90"), "涌:只有治疗");
            Assert.That(Cells(FaceStats.Body(Graph.Get("沐"), CardFace.Feature, 1)), Is.EqualTo("Heal:98,ReviveHp:50"), "苏:治疗 + 复活生命 %(spec B Task 9,D19 收口)");
            Assert.That(Cells(FaceStats.Body(Graph.Get("㵘"), CardFace.Feature, 1)), Is.EqualTo("Heal:210,ReviveHp:60"), "㵘 = 苏(全体),复活生命 60%");
            Assert.That(Cells(FaceStats.Body(Graph.Get("崩"), CardFace.Feature, 1)), Is.EqualTo("Shield:45,Armor:20,Duration:2"), "崩五行面全体盾 45");
            Assert.That(Cells(FaceStats.Body(Graph.Get("冰"), CardFace.Attack, 3)), Is.EqualTo($"Damage:{Scale(195, 3)},Freeze:2"));
            Assert.That(Cells(FaceStats.Body(Graph.Get("㙓"), CardFace.Attack, 1)), Is.EqualTo("Damage:420,ArmorBreak:2,SpendHeft:60"),
                "厚积薄发出耗厚格(final review Important 1)");
            Assert.That(Cells(FaceStats.Body(Graph.Get("㙓"), CardFace.Feature, 1)), Is.EqualTo("Shield:420,Armor:70,Duration:3"));
            Assert.That(Cells(FaceStats.Body(Graph.Get("㙓"), CardFace.Feature, 4)),
                Is.EqualTo($"Shield:{Scale(420, 4)},Armor:{Scale(70, 4)},Duration:4"), "Lv3 回合 +1;不并入 Lv4 深厚的回合加成(Q5)");
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
                Is.EqualTo($"Attack.Damage:195->{Scale(195, 2)},Feature.Weaken:25->{Scale(25, 2)}"));
        }

        [Test]
        public void Deltas_Yan_L2ToL3_IncludesStacksAndTurns()
        {
            Assert.That(Diffs(FaceStats.Deltas(Graph.Get("炎"), 2, 3)),
                Is.EqualTo($"Attack.Damage:{Scale(195, 2)}->{Scale(195, 3)},Attack.Burn:3->4,"
                    + $"Feature.Weaken:{Scale(25, 2)}->{Scale(30, 3)},Feature.Duration:3->4"));
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
            Assert.That(Diffs(FaceStats.Lv3Change(yan, CardFace.Feature)),
                Is.EqualTo($"Feature.Weaken:{Scale(25, 3)}->{Scale(30, 3)},Feature.Duration:3->4"), "慑·强化:+5%、回合 +1,灼不动");
        }

        [Test]
        public void Lv3Change_OtherElements()
        {
            Assert.That(Diffs(FaceStats.Lv3Change(Graph.Get("剑"), CardFace.Feature)),
                Is.EqualTo("Feature.Block:2->3,Feature.CounterPercent:40->50"), "格挡·强化:+1 次、反击 40%→50%(统一按 %)");
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

        /// <summary>Final review Important 1:涌泉相报 / 厚积薄发出「耗泉 / 耗厚」格,值 = 每层伤害(与引擎 SpendResource
        /// 收到的 value 同口径:缺省连续量,吃卡等级),排在攻面本体最后。</summary>
        [Test]
        public void Body_SpendCells_PerStackDamage_Scaled()
        {
            var lei = Graph.Get("㙓");
            Assert.That(Cells(FaceStats.Body(lei, CardFace.Attack, 1)), Is.EqualTo("Damage:420,ArmorBreak:2,SpendHeft:60"));
            Assert.That(FaceStats.Body(lei, CardFace.Attack, 10).Last().Value, Is.EqualTo(Scale(60, 10)));
            Assert.That(Cells(FaceStats.Body(Graph.Get("崩"), CardFace.Attack, 1)), Is.EqualTo("Damage:45,ArmorBreak:2,SpendHeft:13"));
            Assert.That(Cells(FaceStats.Body(Graph.Get("㵘"), CardFace.Attack, 1)), Is.EqualTo("Damage:210,Freeze:1,SpendWellspring:60"));
            Assert.That(Cells(FaceStats.Body(Graph.Get("溃"), CardFace.Attack, 3)), Is.EqualTo($"Damage:{Scale(45, 3)},Slow:2,SpendWellspring:{Scale(13, 3)}"));
            Assert.That(Diffs(FaceStats.Deltas(lei, 1, 2).Where(d => d.Kind == FaceStatKind.SpendHeft)),
                Is.EqualTo($"Attack.SpendHeft:60->{Scale(60, 2)}"), "升级预览的差行带上耗厚");
            Assert.That(FaceStats.Scales(FaceStatKind.SpendHeft) && FaceStats.Scales(FaceStatKind.SpendWellspring), Is.True);
        }

        // ---------------- Sheet(spec B §1.3;依赖 spec A 数据)----------------

        private static string KindsOf(IEnumerable<FaceStat> cells) => string.Join(",", cells.Select(c => c.Kind));

        [Test]
        public void Sheet_AttackFace_OnlyDamage()
        {
            foreach (var d in Playable())
                foreach (var lv in new[] { 1, 3, 10 })
                {
                    var cells = FaceStats.Sheet(d, CardFace.Attack, lv);
                    Assert.That(KindsOf(cells), Is.EqualTo("Damage"), $"{d.Id} L{lv}");
                    Assert.That(cells[0].Value, Is.EqualTo(
                        FaceStats.Body(d, CardFace.Attack, lv).First(c => c.Kind == FaceStatKind.Damage).Value), $"{d.Id} L{lv}");
                }
        }

        [Test]
        public void Sheet_FeatureFace_KindsByElement()
        {
            foreach (var d in Playable())
                foreach (var lv in new[] { 1, 3, 10 })
                {
                    string got = KindsOf(FaceStats.Sheet(d, CardFace.Feature, lv));
                    string why = $"{d.Id}({d.Element}) L{lv}";
                    switch (d.Element)
                    {
                        case Element.Metal: Assert.That(got, Is.EqualTo("CounterPercent,Block,Morale"), why); break;
                        case Element.Fire: Assert.That(got, Is.EqualTo("Weaken,Duration,Burn"), why); break;
                        case Element.Wood: Assert.That(got, Is.EqualTo("SummonHp,SummonAttack"), why); break;
                        case Element.Earth: Assert.That(got, Is.EqualTo("Shield,Armor,Duration"), why); break;
                        case Element.Water:
                            // 涌 = 治疗;涓 = 每回合治疗 + 回合;苏 = 治疗 + 复活生命 %
                            Assert.That(got, Is.EqualTo("Heal").Or.EqualTo("Heal,Duration").Or.EqualTo("Heal,ReviveHp"), why);
                            break;
                        default: Assert.Fail(why + ":可玩字不该有第六系"); break;
                    }
                    Assert.That(got.Contains("Nature"), Is.False, why + ":数值格不再出本命格");
                }
        }

        [Test]
        public void Sheet_FeatureValues_ReadFromFoldedBody()
        {
            foreach (var d in Playable())
                foreach (var lv in new[] { 1, 3, 10 })
                {
                    var cells = FaceStats.Sheet(d, CardFace.Feature, lv);
                    var fx = FaceStats.FoldedBody(d, CardFace.Feature, lv, TraitSlot.Lv4);
                    string why = $"{d.Id} L{lv}";
                    switch (d.Element)
                    {
                        case Element.Metal:
                            var block = fx.First(e => e.Kind == EffectKind.Block);
                            Assert.That(cells[0].Value, Is.EqualTo(TraitRules.FaceBlockCounterPercent(fx)), why + ":反击 % 每字携带,Lv3 折叠后");
                            Assert.That(cells[1].Value, Is.EqualTo(block.Value), why);
                            Assert.That(cells[2].Value, Is.EqualTo(1), why + ":五行面战意恒 1(spec A §4.2)");
                            break;
                        case Element.Fire:
                            var weaken = fx.First(e => e.Kind == EffectKind.Weaken);
                            Assert.That(cells[0].Value, Is.EqualTo(MetaRules.ScaleEffectValue(weaken.Kind, weaken.Value, lv)), why);
                            Assert.That(cells[1].DurationOf, Is.EqualTo(FaceStatKind.Weaken), why);
                            Assert.That(cells[1].Value, Is.EqualTo(TraitRules.TurnsOf(weaken)), why);
                            Assert.That(cells[2].Value, Is.EqualTo(1), why + ":五行面灼恒 1(spec A §4.1)");
                            break;
                        case Element.Water:
                            if (cells.Count == 2 && cells[1].Kind == FaceStatKind.ReviveHp)
                            {
                                Assert.That(cells[0].Source, Is.EqualTo(EffectKind.Revive), why + ":苏的治疗格 Source 记 Revive(终审 I1,词条不判涌)");
                                Assert.That(cells[1].Source, Is.EqualTo(EffectKind.Revive), why);
                            }
                            else if (cells.Count == 2)
                            {
                                var hot = fx.First(e => e.Kind == EffectKind.HealOverTime);
                                Assert.That(cells[0].Source, Is.EqualTo(EffectKind.HealOverTime), why + ":涓");
                                Assert.That(cells[0].Value, Is.EqualTo(MetaRules.ScaleEffectValue(hot.Kind, hot.Value, lv)), why + ":涓每回合量");
                                Assert.That(cells[1].DurationOf, Is.EqualTo(FaceStatKind.Heal), why);
                                Assert.That(cells[1].Value, Is.EqualTo(TraitRules.TurnsOf(hot)), why);
                            }
                            else
                                Assert.That(cells[0].Source == EffectKind.HealSelf || cells[0].Source == EffectKind.HealAll, Is.True, why + ":涌");
                            break;
                        case Element.Wood:
                            var summon = fx.First(e => e.Kind == EffectKind.Summon);
                            Assert.That(cells[0].Value, Is.EqualTo(MetaRules.ScaleEffectValue(summon.Kind, summon.Value, lv)), why);
                            Assert.That(cells[1].Value, Is.EqualTo(MetaRules.ScaleByCardLevel(summon.SummonAttack, lv)), why);
                            break;
                        case Element.Earth:
                            Assert.That(cells[2].DurationOf, Is.EqualTo(FaceStatKind.Armor), why);
                            break;
                    }
                }
            Assert.That(FaceStats.Sheet(Graph.Get("荆"), CardFace.Feature, 1).Single(c => c.Kind == FaceStatKind.SummonAttack).Value,
                Is.EqualTo(0), "荆 木灵攻击 0 也出格(稿;Review Focus 2)");
        }

        [Test]
        public void Sheet_Water_HealTermFollowsMode()
        {
            // 苏(沐/㵘)面本体含 Revive:治疗格不挂涌;海=涌、冷=涓(终审 I1)
            foreach (var ch in new[] { "沐", "㵘" })
            {
                var heal = FaceStats.Sheet(Graph.Get(ch), CardFace.Attack, 1).Concat(FaceStats.Sheet(Graph.Get(ch), CardFace.Feature, 1))
                    .Where(c => c.Kind == FaceStatKind.Heal).ToList();
                Assert.That(heal.Count, Is.GreaterThan(0), ch);
                foreach (var c in heal)
                    Assert.That(GlossaryRules.TermOf(c) == GlossaryTerm.Surge, Is.False, ch + " 治疗格不应是涌");
            }
            var all = new System.Func<string, CardFace, FaceStat[]>((n, f) => FaceStats.Sheet(Graph.Get(n), f, 1).Where(c => c.Kind == FaceStatKind.Heal).ToArray());
            foreach (var f in new[] { CardFace.Attack, CardFace.Feature })
            {
                foreach (var c in all("海", f)) Assert.That(GlossaryRules.TermOf(c), Is.EqualTo(GlossaryTerm.Surge), "海");
                foreach (var c in all("冷", f)) Assert.That(GlossaryRules.TermOf(c), Is.EqualTo(GlossaryTerm.Trickle), "冷");
            }
        }

        [Test]
        public void Sheet_Metal_CounterPercentRisesAtLv3()
        {
            // 剑(蓝):反击 40%,Lv3 格挡·强化 → 50%(同 Body_OtherElements)
            Assert.That(Cells(FaceStats.Sheet(Graph.Get("剑"), CardFace.Feature, 1)), Is.EqualTo("CounterPercent:40,Block:2,Morale:1"));
            Assert.That(Cells(FaceStats.Sheet(Graph.Get("剑"), CardFace.Feature, 3)), Is.EqualTo("CounterPercent:50,Block:3,Morale:1"));
        }

        [Test]
        public void Sheet_WaterModes_SpecA()
        {
            var leng = FaceStats.Sheet(Graph.Get("冷"), CardFace.Feature, 1);
            Assert.That(Cells(leng), Is.EqualTo("Heal:17,Duration:3"), "冷 = 涓:每回合 17 × 3 回合(spec A 终审遗留:每回合量出格)");
            Assert.That(KindsOf(FaceStats.Sheet(Graph.Get("海"), CardFace.Feature, 1)), Is.EqualTo("Heal"), "海 = 涌");
            Assert.That(Cells(FaceStats.Sheet(Graph.Get("沐"), CardFace.Feature, 1)), Is.EqualTo("Heal:98,ReviveHp:50"), "沐 = 苏:治疗 + 复活生命 50%(spec A §4.3)");
            Assert.That(KindsOf(FaceStats.Sheet(Graph.Get("㵘"), CardFace.Feature, 1)), Is.EqualTo("Heal,ReviveHp"), "㵘 = 苏(全体)");
        }

        // ---------------- 覆盖 ----------------

        /// <summary>本体效果里**不出数值格**的 EffectKind,每项写明它的信息在字卡详情的哪里能看到。
        /// 加一项之前先确认:玩家不看这一格,也不会漏掉改变出牌决策的信息。</summary>
        private static readonly Dictionary<EffectKind, string> NoCellExempt = new()
        {
            // 空:苏的 Revive 已于 spec B Task 9 出「复活生命 %」格(D19 收口)
        };

        /// <summary>Final review Important 1:本体(Lv1 + Lv3 替换)里出现的每一种 EffectKind,要么出数值格,要么进
        /// <see cref="NoCellExempt"/>。㵘 / 溃 的涌泉相报、㙓 / 崩 的厚积薄发曾在 AddCells 的 default 里静默掉,
        /// 新字卡详情里整张找不到 —— 以后新增的资源类效果不许再这样消失。逐次出现都断(同一种效果每张字都要出格)。</summary>
        [Test]
        public void EveryBodyEffectKind_HasCellOrIsExempt()
        {
            var missing = new SortedSet<string>();
            var seen = new HashSet<EffectKind>();
            foreach (var d in Playable())
                foreach (var face in new[] { CardFace.Attack, CardFace.Feature })
                    foreach (int lv in new[] { 1, 3 })
                    {
                        bool attack = face == CardFace.Attack;
                        var body = TraitRules.Fold(BattleEngine.EffectsOf(d, attack), d, BattleEngine.FaceOf(d, attack), lv,
                            skip: t => t.Slot >= TraitSlot.Lv4);
                        var sources = new HashSet<EffectKind>(
                            FaceStats.AllCells(d, face, lv, lv, TraitSlot.Lv4).Select(c => c.Source));
                        foreach (var e in body)
                        {
                            seen.Add(e.Kind);
                            if (!NoCellExempt.ContainsKey(e.Kind) && !sources.Contains(e.Kind))
                                missing.Add($"{e.Kind}({d.Id} {face} L{lv})");
                        }
                    }
            Assert.That(missing, Is.Empty, "这些本体效果既不出数值格、也不在豁免名单里:\n  " + string.Join("\n  ", missing));
            var stale = NoCellExempt.Keys.Where(k => !seen.Contains(k)).ToList();
            Assert.That(stale, Is.Empty, "豁免名单里有真实字表本体不再出现的效果,删掉:" + string.Join(",", stale));
        }
    }
}
