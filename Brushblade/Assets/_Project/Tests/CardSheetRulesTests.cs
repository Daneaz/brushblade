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
                // spec A §6:Lv6 两面各一条,与 Lv5 / Lv8 同样标两面;Lv1 按槽位恒标「两面」(两面各一条也是)
                foreach (int lv in new[] { 5, 6, 8 })
                    Assert.That(CardSheetRules.LadderMarks(d, lv), Is.EqualTo(new[] { TraitFace.Attack, TraitFace.Feature }), $"{d.Id} Lv{lv}");
                foreach (int lv in new[] { 2, 7, 9, 10 })
                    Assert.That(CardSheetRules.LadderMarks(d, lv), Is.Empty, $"{d.Id} Lv{lv}");
            }
        }

        // ---------------- FaceTraits ----------------

        [Test]
        public void FaceTraits_Yan_Attack_L6()
        {
            var rows = CardSheetRules.FaceTraits(Graph.Get("炎"), CardFace.Attack, 6);
            // spec A §6.1:炎攻面补专属 Lv6 赤炎
            Assert.That(Names(rows), Is.EqualTo("Lv5:火上浇油:on,Lv6:赤炎:on,Lv8:炎刃:off"));
            Assert.That(rows.All(r => r.Face == TraitFace.Attack && r.Defs.Count == 1), Is.True);
            Assert.That(rows.Select(r => r.IsSignature), Is.EqualTo(new[] { false, false, true }));
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
                    Assert.That(rows.Count(r => r.Slot == TraitSlot.Lv6), Is.EqualTo(1), $"{d.Id} {face} Lv6(spec A 两面各一条)");
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
            Assert.That(split, Is.EqualTo(27), "Lv4 分面的字数(spec A 的 23 + spec C 新拆 柘 / 湮 / 燚 / 燥 四字)");
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
            // spec A §1:Lv1 / Lv3 按面分开命名,55 张字 Lv3 两面名全都不同
            Assert.That(differ, Is.EqualTo(55), "Lv3 两面不同名的字数");
            Assert.That(CardSheetRules.SharedTraits(Graph.Get("林"), 3)[0].Names, Is.EqualTo(new[] { "种·强化", "成林·强化" }));
            Assert.That(CardSheetRules.SharedTraits(Graph.Get("炎"), 3)[0].Names, Is.EqualTo(new[] { "灼·强化", "燃意·强化" }));
        }

        /// <summary>Final review Minor 2:两面条的类别按每条 Def 自己的 Form 取,不能只看 Defs[0]。
        /// 同类别只出一个,不同类别按 Defs 顺序(攻面那条在前)两个都在 —— 与 <see cref="SheetTrait.Names"/> 同一口径。</summary>
        [Test]
        public void SharedTraits_Forms_PerDef_DedupedInDefOrder()
        {
            int mixedLv3 = 0;
            foreach (var d in Playable())
                foreach (var row in CardSheetRules.SharedTraits(d, MetaRules.MaxCardLevel))
                {
                    Assert.That(row.Forms, Is.EqualTo(row.Defs.Select(t => t.Form).Distinct().ToList()), $"{d.Id} Lv{(int)row.Slot}");
                    if (row.Slot == TraitSlot.Lv3 && row.Forms.Count == 2) mixedLv3++;
                }
            Assert.That(mixedLv3, Is.EqualTo(11), "Lv3 两面类别不同的字数(全是木系,含花;审查记的 12 是笔误);Lv4 现无");
            Assert.That(CardSheetRules.SharedTraits(Graph.Get("林"), 3)[0].Forms,
                Is.EqualTo(new[] { TraitForm.Active, TraitForm.Passive }), "种·强化 主动 / 成林·强化 被动");
            Assert.That(CardSheetRules.SharedTraits(Graph.Get("炎"), 3)[0].Forms, Is.EqualTo(new[] { TraitForm.Active }));
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
                Assert.That(CardSheetRules.UnlockedAt(d, 6).Count, Is.EqualTo(2), $"{d.Id} L6(spec A 两面各一条)");
                Assert.That(CardSheetRules.UnlockedAt(d, 8).Count, Is.EqualTo(2), $"{d.Id} L8");
                foreach (int lv in new[] { 2, 7, 9, 10 })
                    Assert.That(CardSheetRules.UnlockedAt(d, lv), Is.Empty, $"{d.Id} L{lv}");
                Assert.That(CardSheetRules.UnlockedAt(d, 5).All(r => r.Unlocked), Is.True);
                Assert.That(CardSheetRules.UnlockedAt(d, 5).Select(r => r.Face), Is.EqualTo(new[] { TraitFace.Attack, TraitFace.Feature }));
                Assert.That(CardSheetRules.UnlockedAt(d, 6).Select(r => r.Face), Is.EqualTo(new[] { TraitFace.Attack, TraitFace.Feature }), d.Id);
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
            Assert.That(Names(rows5), Is.EqualTo("Lv6:赤炎:off,Lv6:上炎:off"), "先攻后五行");
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

        // ---------------- LevelRows / Lv1Effects(spec B §1.2 全平铺)----------------

        private static readonly TraitSlot[] SheetSlots =
            { TraitSlot.Lv1, TraitSlot.Lv3, TraitSlot.Lv4, TraitSlot.Lv5, TraitSlot.Lv6, TraitSlot.Lv8 };

        private static string KindValues(IEnumerable<EffectDef> effects) =>
            string.Join(",", effects.Select(e => $"{e.Kind}:{e.Value}"));

        [Test]
        public void LevelRows_EveryChar_OneRowPerSlot_CellsHoldThatFacesTrait()
        {
            foreach (var d in Playable())
            {
                var rows = CardSheetRules.LevelRows(d, MetaRules.MaxCardLevel);
                var slots = d.Traits.Select(t => t.Slot).Distinct().OrderBy(s => (int)s).ToList();
                Assert.That(rows.Select(r => r.Slot), Is.EqualTo(slots), $"{d.Id} 每个有特性的级一行,升序");
                foreach (var r in rows)
                {
                    Assert.That(SheetSlots.Contains(r.Slot), Is.True, $"{d.Id} Lv{(int)r.Slot} 不在 1/3/4/5/6/8");
                    foreach (var face in new[] { CardFace.Attack, CardFace.Feature })
                    {
                        var want = d.Traits.SingleOrDefault(t => t.Slot == r.Slot && t.AppliesTo(face));
                        var cell = r.Of(face);
                        string why = $"{d.Id} Lv{(int)r.Slot} {face}";
                        if (want == null)
                        {
                            Assert.That(cell, Is.Null, why + ":缺这一级 → null(画待补)");
                            continue;
                        }
                        Assert.That(cell, Is.Not.Null, why);
                        Assert.That(cell.Defs.Count, Is.EqualTo(1), why);
                        Assert.That(ReferenceEquals(cell.Defs[0], want), Is.True, why + ":Both 特性两面各挂一份");
                        Assert.That(cell.Slot, Is.EqualTo(r.Slot), why);
                        Assert.That(cell.Face, Is.EqualTo(face == CardFace.Attack ? TraitFace.Attack : TraitFace.Feature), why);
                    }
                }
            }
        }

        /// <summary>Review Focus 2:无图标本命(花 / 藤 / 箭 / 柘)过去整格被跳过;Lv1 行不依赖图标,两面都要有。</summary>
        [Test]
        public void LevelRows_Lv1_OnBothFaces_ForEveryChar()
        {
            foreach (var d in Playable())
            {
                var first = CardSheetRules.LevelRows(d, 1)[0];
                Assert.That(first.Slot, Is.EqualTo(TraitSlot.Lv1), d.Id);
                Assert.That(first.Attack, Is.Not.Null, d.Id + " 攻击面 Lv1");
                Assert.That(first.Feature, Is.Not.Null, d.Id + " 五行面 Lv1");
            }
            foreach (var id in new[] { "花", "藤", "箭", "柘" })
                Assert.That(CardSheetRules.LevelRows(Graph.Get(id), 1)[0].Feature, Is.Not.Null, id);
        }

        /// <summary>Review Focus 1:未拥有(traitLevel 0)全部 off;解锁按卡等级。</summary>
        [Test]
        public void LevelRows_UnlockedFollowsCardLevel_AndUnownedAllOff()
        {
            foreach (var r in CardSheetRules.LevelRows(Graph.Get("炎"), 5))
            {
                bool want = (int)r.Slot <= 5;
                Assert.That(r.Unlocked, Is.EqualTo(want), $"Lv{(int)r.Slot}");
                if (r.Attack != null) Assert.That(r.Attack.Unlocked, Is.EqualTo(want), $"Lv{(int)r.Slot} 攻");
                if (r.Feature != null) Assert.That(r.Feature.Unlocked, Is.EqualTo(want), $"Lv{(int)r.Slot} 五");
            }
            foreach (var d in Playable())
                Assert.That(CardSheetRules.LevelRows(d, 0).All(r => !r.Unlocked), Is.True, $"{d.Id} 未拥有应全 off");
        }

        [Test]
        public void Lv1Effects_NoDamage_FoldsLv3_UnownedSameAsLevel1()
        {
            var yan = Graph.Get("炎");
            var a1 = CardSheetRules.Lv1Effects(yan, CardFace.Attack, 1);
            Assert.That(a1.Any(e => e.Kind == EffectKind.DamageSingle), Is.False, "伤害在数值格,不进 Lv1 行");
            int burn1 = a1.First(e => e.Kind == EffectKind.BurnSingle).Value;
            int burn3 = CardSheetRules.Lv1Effects(yan, CardFace.Attack, 3).First(e => e.Kind == EffectKind.BurnSingle).Value;
            Assert.That(burn3, Is.GreaterThan(burn1), "Lv3 灼·强化折进 Lv1 行(当前等级的值)");

            foreach (var d in Playable())
                foreach (var face in new[] { CardFace.Attack, CardFace.Feature })
                {
                    var effects = CardSheetRules.Lv1Effects(d, face, 1);
                    Assert.That(effects.Any(e => e.Kind == EffectKind.DamageSingle), Is.False, $"{d.Id} {face}");
                    Assert.That(KindValues(CardSheetRules.Lv1Effects(d, face, 0)), Is.EqualTo(KindValues(effects)),
                        $"{d.Id} {face}:未拥有按 1 级印");
                }

            var jing = CardSheetRules.Lv1Effects(Graph.Get("荆"), CardFace.Feature, 1);
            Assert.That(jing.Any(e => e.Kind == EffectKind.Summon && e.Passive != null), Is.True, "木五行面 Lv1 = 本命:召唤带被动");
        }

        [Test]
        public void Lv1Effects_MetalFeature_BlockBeforeMorale()
        {
            // 2026-10-15 用户:金系五行面 Lv1 先写「格挡」本身,再写附带的「战意」(字表里 Morale 在前)
            foreach (var d in Playable().Where(d => d.Element == Element.Metal))
            {
                var kinds = CardSheetRules.Lv1Effects(d, CardFace.Feature, 1).Select(e => e.Kind).ToList();
                int block = kinds.IndexOf(EffectKind.Block), morale = kinds.IndexOf(EffectKind.Morale);
                if (block < 0 || morale < 0) continue;
                Assert.That(block, Is.LessThan(morale), $"{d.Id}:格挡在战意之前");
            }
            var li = CardSheetRules.Lv1Effects(Graph.Get("利"), CardFace.Feature, 1).Select(e => e.Kind).ToList();
            Assert.That(li.IndexOf(EffectKind.Block), Is.LessThan(li.IndexOf(EffectKind.Morale)), "利 是白档样本,必须两条都有");
        }

        [Test]
        public void FeatureAmpItems_NamesWhatAllScopeActuallyRaises()
        {
            // 2026-10-15 用户:五行面的「本字效果 +X%」要写成具体放大的项 —— 金反击、水治疗、土护盾、木木灵、火灼火力
            Assert.That(CardSheetRules.FeatureAmpItems(Graph.Get("剑"), 1), Is.EqualTo(new[] { AmpItem.Counter }));
            Assert.That(CardSheetRules.FeatureAmpItems(Graph.Get("冷"), 1), Is.EqualTo(new[] { AmpItem.Heal }));
            Assert.That(CardSheetRules.FeatureAmpItems(Graph.Get("崩"), 1), Is.EqualTo(new[] { AmpItem.Shield }));
            Assert.That(CardSheetRules.FeatureAmpItems(Graph.Get("箭"), 1), Is.EqualTo(new[] { AmpItem.Summon }));
            Assert.That(CardSheetRules.FeatureAmpItems(Graph.Get("热"), 1), Is.EqualTo(new[] { AmpItem.Kindle }), "spec C:火面本体 = 燃意,「本字效果 +X%」放大的是燃意 %");
            foreach (var d in Playable())
                Assert.That(CardSheetRules.FeatureAmpItems(d, 1), Is.Not.Empty, $"{d.Id}:五行面至少有一项吃「本字效果」加成");
        }

        // ---------------- TargetOf / AreaOf ----------------

        [Test]
        public void TargetOf_AllFaces_ByElement()
        {
            foreach (var d in Playable())
                foreach (int lv in new[] { 1, 10 })
                {
                    Assert.That(CardSheetRules.TargetOf(d, CardFace.Attack, lv), Is.EqualTo(FaceTarget.Enemy), $"{d.Id} 攻面");
                    // spec A §3:溃 / 㵘 五行面改全体(HealAll,不选友方);F2a 起落点放开到玩家 / 木灵 → Ally
                    var expected = d.Element switch
                    {
                        Element.Fire => FaceTarget.Ally,   // spec C §5:燃意挂我方,火面落点改 Ally(玩家 / 木灵)
                        Element.Wood => FaceTarget.Summon,
                        // 溃 Lv5 溃围(Slow pick All)解锁后面上有了敌对效果、又不选友方 → 落点 Enemy(CardFaceRules.Landing E26 只在选友方时压掉)
                        _ when d.Id == "溃" && lv >= 5 => FaceTarget.Enemy,
                        // 崩 = ShieldAll + DefenseBuff:护甲仍要选友方(EffectNeedsAllyTarget),落点保持 Ally
                        _ => FaceTarget.Ally,
                    };
                    Assert.That(CardSheetRules.TargetOf(d, CardFace.Feature, lv), Is.EqualTo(expected), $"{d.Id} 五行面 Lv{lv}");
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
        public void AreaOf_Jian_Attack_SweepFromLv1_AllAtLv8()
        {
            // 2026-10-18 群攻入口 spec §1:剑 本体横扫(Lv1 起),Lv8 横扫千军改全体 50
            var jian = Graph.Get("剑");
            Assert.That(CardSheetRules.AreaOf(jian, CardFace.Attack, 1), Is.EqualTo(TargetArea.Row));
            Assert.That(CardSheetRules.AreaOf(jian, CardFace.Attack, 7), Is.EqualTo(TargetArea.Row));
            Assert.That(CardSheetRules.AreaOf(jian, CardFace.Attack, 8), Is.EqualTo(TargetArea.All), "横扫千军");
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

        /// <summary>spec A §6 合并后:每张字 Lv1/3/4/5/6/8 六级、每级两面都有(待补占位不再出现)。</summary>
        [Test]
        public void LevelRows_AfterSpecA_SixLevels_BothFaces()
        {
            foreach (var d in Playable())
            {
                var rows = CardSheetRules.LevelRows(d, 1);
                Assert.That(rows.Select(r => (int)r.Slot), Is.EqualTo(new[] { 1, 3, 4, 5, 6, 8 }), d.Id);
                Assert.That(rows.All(r => r.Attack != null && r.Feature != null), Is.True, d.Id + ":每级两面都有");
            }
        }
    }
}
