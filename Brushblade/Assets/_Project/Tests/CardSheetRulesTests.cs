using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>Plan E2 Task 1:字卡详情 / 升级预览的行规则(<see cref="CardSheetRules"/>)与
    /// 「本场已用」查询(<see cref="BattleEngine.TraitSpentThisBattle"/>)。读真实 chars.json,断言确定值。</summary>
    public class CardSheetRulesTests
    {
        private static RecipeGraph _graph;
        private static RecipeGraph Graph => _graph ??= CharTableTests.RealGraph();

        private static List<CharDef> Playable() =>
            Graph.All.Where(d => !d.IsComponent && d.Element != null).ToList();

        private static string Names(IEnumerable<SheetTrait> rows) =>
            string.Join(",", rows.Select(r => $"Lv{(int)r.Slot}:{string.Join("/", r.Names)}:{(r.Unlocked ? "on" : "off")}"));

        [Test]
        public void RealTable_Has55PlayableChars()
        {
            Assert.That(Playable().Count, Is.EqualTo(55));
        }

        // ---------------- LadderMarks ----------------

        [Test]
        public void LadderMarks_AllPlayableChars_FollowSlotRule()
        {
            foreach (var d in Playable())
            {
                foreach (int lv in new[] { 1, 3, 4 })
                    Assert.That(CardSheetRules.LadderMarks(d, lv), Is.EqualTo(new[] { TraitFace.Both }), $"{d.Id} Lv{lv}");
                foreach (int lv in new[] { 5, 8 })
                    Assert.That(CardSheetRules.LadderMarks(d, lv), Is.EqualTo(new[] { TraitFace.Attack, TraitFace.Feature }), $"{d.Id} Lv{lv}");
                var lv6 = d.Traits.Single(t => t.Slot == TraitSlot.Lv6).Face;
                Assert.That(CardSheetRules.LadderMarks(d, 6), Is.EqualTo(new[] { lv6 }), $"{d.Id} Lv6");
                foreach (int lv in new[] { 2, 7, 9, 10 })
                    Assert.That(CardSheetRules.LadderMarks(d, lv), Is.Empty, $"{d.Id} Lv{lv}");
            }
        }

        // ---------------- FaceTraits ----------------

        [Test]
        public void FaceTraits_Yan_Attack_L6()
        {
            var rows = CardSheetRules.FaceTraits(Graph.Get("炎"), CardFace.Attack, 6);
            Assert.That(Names(rows), Is.EqualTo("Lv5:火上浇油:on,Lv8:炎刃:off"));
            Assert.That(rows.All(r => r.Face == TraitFace.Attack && r.Defs.Count == 1), Is.True);
            Assert.That(rows.Select(r => r.IsSignature), Is.EqualTo(new[] { false, true }));
        }

        [Test]
        public void FaceTraits_Yan_Feature_L6()
        {
            var rows = CardSheetRules.FaceTraits(Graph.Get("炎"), CardFace.Feature, 6);
            Assert.That(Names(rows), Is.EqualTo("Lv5:续火:on,Lv6:上炎:on,Lv8:炎炎:off"));
        }

        [Test]
        public void FaceTraits_AllChars_OnlyLv5To8_AscendingAndOnThisFace()
        {
            foreach (var d in Playable())
                foreach (var face in new[] { CardFace.Attack, CardFace.Feature })
                {
                    var rows = CardSheetRules.FaceTraits(d, face, 1);
                    Assert.That(rows.Select(r => (int)r.Slot), Is.Ordered, $"{d.Id} {face}");
                    Assert.That(rows.Any(r => r.Slot == TraitSlot.Lv1 || r.Slot == TraitSlot.Lv3 || r.Slot == TraitSlot.Lv4),
                        Is.False, $"{d.Id} {face}:Lv1/3/4 不进面");
                    Assert.That(rows.All(r => r.Defs.All(t => t.AppliesTo(face))), Is.True, $"{d.Id} {face}");
                    Assert.That(rows.Count(r => r.Slot == TraitSlot.Lv5), Is.EqualTo(1), $"{d.Id} {face} Lv5");
                    Assert.That(rows.Count(r => r.Slot == TraitSlot.Lv8), Is.EqualTo(1), $"{d.Id} {face} Lv8");
                    Assert.That(rows.All(r => !r.Unlocked), Is.True, $"{d.Id} {face}:Lv1 全未解锁");
                }
        }

        // ---------------- SharedTraits(两面条)+ Review Focus 2 ----------------

        [Test]
        public void SharedTraits_EveryChar_OneLv3Row_OneLv4Row_NoLv1()
        {
            foreach (var d in Playable())
            {
                var rows = CardSheetRules.SharedTraits(d, 1);
                Assert.That(rows.Select(r => r.Slot), Is.EqualTo(new[] { TraitSlot.Lv3, TraitSlot.Lv4 }), d.Id);
                Assert.That(rows.All(r => r.Face == TraitFace.Both), Is.True, d.Id);
                Assert.That(rows[0].Defs.Count, Is.EqualTo(2), $"{d.Id}:Lv3 恒分面两条");
                Assert.That(rows[0].Defs.Select(t => t.Face), Is.EqualTo(new[] { TraitFace.Attack, TraitFace.Feature }), d.Id);
            }
        }

        [Test]
        public void ReviewFocus2_Lv4SplitChars_BothDefsPresent()
        {
            int split = 0;
            foreach (var d in Playable())
            {
                var lv4 = d.Traits.Where(t => t.Slot == TraitSlot.Lv4).ToList();
                var row = CardSheetRules.SharedTraits(d, 10).Single(r => r.Slot == TraitSlot.Lv4);
                if (lv4.Count == 2)
                {
                    split++;
                    Assert.That(row.Defs.Count, Is.EqualTo(2), d.Id);
                    Assert.That(row.Defs[0], Is.SameAs(lv4.Single(t => t.Face == TraitFace.Attack)), $"{d.Id} 攻面那条");
                    Assert.That(row.Defs[1], Is.SameAs(lv4.Single(t => t.Face == TraitFace.Feature)), $"{d.Id} 五行面那条");
                }
                else
                {
                    Assert.That(row.Defs.Count, Is.EqualTo(1), d.Id);
                    Assert.That(row.Defs[0].Face, Is.EqualTo(TraitFace.Both), d.Id);
                }
                Assert.That(row.Unlocked, Is.True, d.Id);
            }
            Assert.That(split, Is.EqualTo(22), "Lv4 分面的字数");
        }

        [Test]
        public void ReviewFocus2_Lv3DifferentNames_BothNamesReachable()
        {
            int differ = 0;
            foreach (var d in Playable())
            {
                var lv3 = d.Traits.Where(t => t.Slot == TraitSlot.Lv3).ToList();
                var row = CardSheetRules.SharedTraits(d, 3).First();
                var expected = lv3.OrderBy(t => t.Face).Select(t => t.Name).Distinct().ToList();
                Assert.That(row.Names, Is.EqualTo(expected), d.Id);
                if (expected.Count == 2) differ++;
            }
            Assert.That(differ, Is.EqualTo(11), "Lv3 两面不同名的字数");
            Assert.That(CardSheetRules.SharedTraits(Graph.Get("林"), 3)[0].Names, Is.EqualTo(new[] { "种·强化", "成林·强化" }));
            Assert.That(CardSheetRules.SharedTraits(Graph.Get("炎"), 3)[0].Names, Is.EqualTo(new[] { "灼·强化" }), "同名只出一个名");
        }

        [Test]
        public void SharedTraits_UnlockedFollowsCardLevel()
        {
            var rows = CardSheetRules.SharedTraits(Graph.Get("炎"), 3);
            Assert.That(rows.Select(r => r.Unlocked), Is.EqualTo(new[] { true, false }));
        }

        // ---------------- UnlockedAt / NextUnlock ----------------

        [Test]
        public void UnlockedAt_RowCountsByLevel()
        {
            foreach (var d in Playable())
            {
                Assert.That(CardSheetRules.UnlockedAt(d, 3).Count, Is.EqualTo(1), $"{d.Id} L3(两面合一行)");
                Assert.That(CardSheetRules.UnlockedAt(d, 4).Count, Is.EqualTo(1), $"{d.Id} L4");
                Assert.That(CardSheetRules.UnlockedAt(d, 5).Count, Is.EqualTo(2), $"{d.Id} L5");
                Assert.That(CardSheetRules.UnlockedAt(d, 6).Count, Is.EqualTo(1), $"{d.Id} L6");
                Assert.That(CardSheetRules.UnlockedAt(d, 8).Count, Is.EqualTo(2), $"{d.Id} L8");
                foreach (int lv in new[] { 2, 7, 9, 10 })
                    Assert.That(CardSheetRules.UnlockedAt(d, lv), Is.Empty, $"{d.Id} L{lv}");
                Assert.That(CardSheetRules.UnlockedAt(d, 5).All(r => r.Unlocked), Is.True);
                Assert.That(CardSheetRules.UnlockedAt(d, 5).Select(r => r.Face), Is.EqualTo(new[] { TraitFace.Attack, TraitFace.Feature }));
            }
        }

        [Test]
        public void NextUnlock_Yan()
        {
            var yan = Graph.Get("炎");
            var (l1, rows1) = CardSheetRules.NextUnlock(yan, 1);
            Assert.That(l1, Is.EqualTo(3));
            Assert.That(rows1.Single().Slot, Is.EqualTo(TraitSlot.Lv3));
            var (l6, rows6) = CardSheetRules.NextUnlock(yan, 6);
            Assert.That(l6, Is.EqualTo(8));
            Assert.That(Names(rows6), Is.EqualTo("Lv8:炎刃:off,Lv8:炎炎:off"));
            var (l5, rows5) = CardSheetRules.NextUnlock(yan, 5);
            Assert.That(l5, Is.EqualTo(6));
            Assert.That(Names(rows5), Is.EqualTo("Lv6:上炎:off"));
        }

        [Test]
        public void NextUnlock_AfterLv8_IsNone()
        {
            foreach (var d in Playable())
                foreach (int lv in new[] { 8, 9, 10 })
                {
                    var (level, rows) = CardSheetRules.NextUnlock(d, lv);
                    Assert.That(level, Is.EqualTo(0), $"{d.Id} {lv}");
                    Assert.That(rows, Is.Empty, $"{d.Id} {lv}");
                }
        }

        // ---------------- TargetOf / AreaOf ----------------

        [Test]
        public void TargetOf_AllFaces_ByElement()
        {
            foreach (var d in Playable())
                foreach (int lv in new[] { 1, 10 })
                {
                    Assert.That(CardSheetRules.TargetOf(d, CardFace.Attack, lv), Is.EqualTo(FaceTarget.Enemy), $"{d.Id} 攻面");
                    var expected = d.Element switch
                    {
                        Element.Fire => FaceTarget.Enemy,
                        Element.Wood => FaceTarget.Summon,
                        _ => FaceTarget.Ally,
                    };
                    Assert.That(CardSheetRules.TargetOf(d, CardFace.Feature, lv), Is.EqualTo(expected), $"{d.Id} 五行面");
                }
        }

        [Test]
        public void TargetOf_SelfOnlyFixture()
        {
            var def = new CharDef("锐", Element.Metal,
                effects: new[] { new EffectDef(EffectKind.ApBoost, 1) },
                attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 10) });
            Assert.That(CardSheetRules.TargetOf(def, CardFace.Feature, 1), Is.EqualTo(FaceTarget.SelfOnly));
            Assert.That(CardSheetRules.AreaOf(def, CardFace.Feature, 1), Is.EqualTo(TargetArea.Single));
        }

        [Test]
        public void AreaOf_Jian_Attack_SweepAtLv8()
        {
            var jian = Graph.Get("剑");
            Assert.That(CardSheetRules.AreaOf(jian, CardFace.Attack, 1), Is.EqualTo(TargetArea.Single));
            Assert.That(CardSheetRules.AreaOf(jian, CardFace.Attack, 7), Is.EqualTo(TargetArea.Single));
            Assert.That(CardSheetRules.AreaOf(jian, CardFace.Attack, 8), Is.EqualTo(TargetArea.Row), "横扫千军");
        }

        [Test]
        public void AreaOf_WoodSummonFace_IsNull_OthersNotNull()
        {
            foreach (var d in Playable())
                foreach (var face in new[] { CardFace.Attack, CardFace.Feature })
                {
                    var area = CardSheetRules.AreaOf(d, face, 1);
                    if (d.Element == Element.Wood && face == CardFace.Feature)
                        Assert.That(area, Is.Null, d.Id);
                    else
                        Assert.That(area, Is.Not.Null, $"{d.Id} {face}");
                }
        }

        [Test]
        public void AreaOf_AllTargetsBody()
        {
            Assert.That(CardSheetRules.AreaOf(Graph.Get("燚"), CardFace.Attack, 1), Is.EqualTo(TargetArea.All), "全体伤害");
            Assert.That(CardSheetRules.AreaOf(Graph.Get("焱"), CardFace.Feature, 1), Is.EqualTo(TargetArea.All), "全体灼烧、无伤害");
            Assert.That(CardSheetRules.AreaOf(Graph.Get("炎"), CardFace.Feature, 1), Is.EqualTo(TargetArea.Single));
            Assert.That(CardSheetRules.AreaOf(Graph.Get("冷"), CardFace.Feature, 1), Is.EqualTo(TargetArea.Single));
        }

        // ---------------- BattleEngine.TraitSpentThisBattle(Q19) ----------------

        private static BattleConfig Config => new() { PlayerMaxHp = RebalanceFixture.BaseMaxHp, PlayerAttack = 100 };
        private static readonly Dictionary<string, int> DuLevels = new() { ["杜"] = 8 };

        private static BattleEngine DuBattle() =>
            new(Graph, Config, new[] { "杜", "杜", "杜" }, Array.Empty<string>(),
                new[] { RebalanceFixture.Mob() }, seed: 1, cardLevels: DuLevels);

        private static TraitDef Trait(string ch, string name) => Graph.Get(ch).Traits.Single(t => t.Name == name);

        [Test]
        public void TraitSpent_Dumenbuchu_AfterOneFeatureCast()
        {
            var du = Graph.Get("杜");
            var limited = Trait("杜", "杜门不出");
            Assert.That(limited.MaxPerBattle, Is.EqualTo(1));
            var b = DuBattle();
            Assert.That(b.TraitSpentThisBattle(du, limited), Is.False, "还没出过");
            Assert.That(b.Cast("杜"), Is.EqualTo(BattleError.None));
            Assert.That(b.TraitSpentThisBattle(du, limited), Is.True, "生面出一次就用尽");

            var snap = b.Capture();
            var restored = BattleEngine.Restore(snap, Graph, Config, DuLevels,
                new Dictionary<string, EnemyDef> { ["怔"] = RebalanceFixture.Mob() });
            Assert.That(restored.TraitSpentThisBattle(du, limited), Is.True, "快照往返后仍是用尽");
            // 「下一场重新计」不在这里断:新建一个 engine 天然是 false,证明不了什么 —— 见 TraitSpent_ResetsWhenRunAdvancesToNextBattle
        }

        /// <summary>Task 1 review Minor 1:次数按场重置要在**同一局**里证明。RunEngine 每场新建 BattleEngine(NewBattle),
        /// 次数表不进携带态;这条守的是「打完一场、进下一场后,上一场的用尽不会带过来」,并确认下一场照常记次。</summary>
        [Test]
        public void TraitSpent_ResetsWhenRunAdvancesToNextBattle()
        {
            var du = Graph.Get("杜");
            var limited = Trait("杜", "杜门不出");
            var config = new RunConfig
            {
                Encounters = Enumerable.Range(0, 2).Select(_ => new[] { RebalanceFixture.Mob(hp: 50) }).ToArray(),
                RewardPool = new[] { "杜" },
            };
            var run = new RunEngine(Graph, config, Config, Enumerable.Repeat("杜", 6).ToArray(), Array.Empty<string>(),
                seed: 1, cardLevels: DuLevels);

            Assert.That(run.Battle.Cast("杜"), Is.EqualTo(BattleError.None));
            Assert.That(run.Battle.TraitSpentThisBattle(du, limited), Is.True, "第一场:生面出一次就用尽");
            run.Battle.EndTurn();
            Assert.That(run.Battle.Cast("杜", 0, attackMode: true), Is.EqualTo(BattleError.None));
            Assert.That(run.Battle.Phase, Is.EqualTo(BattlePhase.Won), "夹具前提:攻面一击清场");
            run.AdvanceAfterBattle();
            while (run.Phase == RunPhase.Reward) run.SkipReward();
            Assert.That(run.BattleIndex, Is.EqualTo(1), "夹具前提:已进第二场");

            Assert.That(run.Battle.TraitSpentThisBattle(du, limited), Is.False, "下一场重新计");
            Assert.That(run.Battle.Cast("杜"), Is.EqualTo(BattleError.None));
            Assert.That(run.Battle.TraitSpentThisBattle(du, limited), Is.True, "第二场照常记次");
        }

        /// <summary>折桂(攻面 Lv5、击杀时、每场 1 次):击杀入库即用尽;满库不入时退次,仍未用尽(E49)。</summary>
        [Test]
        public void TraitSpent_Zhegui_OnKill_RefundedWhenLibraryFull()
        {
            var gui = Graph.Get("桂");
            var zhegui = Trait("桂", "折桂");
            Assert.That((zhegui.Slot, zhegui.Face, zhegui.Trigger, zhegui.MaxPerBattle),
                Is.EqualTo((TraitSlot.Lv5, TraitFace.Attack, TraitTrigger.OnKill, 1)));
            BattleEngine GuiBattle(int library) =>
                new(Graph, new BattleConfig
                    {
                        PlayerMaxHp = RebalanceFixture.BaseMaxHp, PlayerAttack = 100, ApPerTurn = 200,
                        LibraryCapacity = library, DropsPerTurn = 0, UnlockedChars = new[] { "花", "藤", "森" },
                    },
                    new[] { "桂", "桂", "桂" }, Array.Empty<string>(),
                    new[] { RebalanceFixture.Mob(hp: 1), RebalanceFixture.Mob() }, seed: 1,
                    cardLevels: new Dictionary<string, int> { ["桂"] = 5 });

            var b = GuiBattle(library: 6);
            Assert.That(b.TraitSpentThisBattle(gui, zhegui), Is.False, "还没击杀");
            Assert.That(b.Cast("桂", 0, attackMode: true), Is.EqualTo(BattleError.None));
            Assert.That(b.Enemies[0].Alive, Is.False, "夹具前提:击杀");
            Assert.That(b.LastEvents.Count(e => e.Kind == BattleEventKind.CharGranted), Is.EqualTo(1));
            Assert.That(b.TraitSpentThisBattle(gui, zhegui), Is.True, "击杀入库 → 本场已用");

            // 字库上限 2、起手 3 张:出一张后剩 2 = 满库 → 不入、退次
            var full = GuiBattle(library: 2);
            Assert.That(full.Cast("桂", 0, attackMode: true), Is.EqualTo(BattleError.None));
            Assert.That(full.Enemies[0].Alive, Is.False, "夹具前提:击杀");
            Assert.That(full.LastEvents.Any(e => e.Kind == BattleEventKind.CharGranted), Is.False, "满库不入");
            Assert.That(full.TraitSpentThisBattle(gui, zhegui), Is.False, "退次后不算用过");
        }

        [Test]
        public void TraitSpent_AttackFaceCast_DoesNotSpendFeatureTrait()
        {
            var du = Graph.Get("杜");
            var b = DuBattle();
            Assert.That(b.Cast("杜", 0, attackMode: true), Is.EqualTo(BattleError.None));
            Assert.That(b.TraitSpentThisBattle(du, Trait("杜", "杜门不出")), Is.False);
        }

        [Test]
        public void TraitSpent_UnlimitedTraits_AlwaysFalse()
        {
            var du = Graph.Get("杜");
            var b = DuBattle();
            b.Cast("杜");
            b.Cast("杜", 0, attackMode: true);
            b.Cast("杜");
            foreach (var t in du.Traits.Where(t => t.MaxPerBattle == 0))
                Assert.That(b.TraitSpentThisBattle(du, t), Is.False, t.Name);
        }
    }
}
