using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>spec C §7 验收(真实字表)。</summary>
    public class SpecCAcceptanceTests
    {
        private static readonly string[] Fire = { "热", "爆", "炸", "烈", "燥", "蒸", "炎", "灿", "焱", "焚", "燚" };

        [Test]
        public void FireFeatureFace_NoTraitNeedsEnemyTarget_UpToLv8()   // Review Focus 4
        {
            var bad = new List<string>();
            foreach (var id in Fire)
            {
                var d = CharTableTests.RealGraph().Get(id);
                for (int lv = 1; lv <= 8; lv++)
                {
                    if (BattleEngine.NeedsTarget(d, false, lv)) bad.Add($"{id} Lv{lv} 整面");
                    foreach (var t in TraitRules.Unlocked(d, lv).Where(t => t.AppliesTo(CardFace.Feature) && t.Trigger == TraitTrigger.Cast))
                        foreach (var e in t.Effects)
                            if (BattleEngine.EffectNeedsTarget(e)) bad.Add($"{id} Lv{lv} {t.Name} {e.Kind}");
                }
            }
            Assert.That(bad.Distinct().ToList(), Is.Empty, string.Join(",", bad.Distinct()));
        }

        [Test]
        public void FireFeatureFace_LandsOnAllies()
        {
            foreach (var id in Fire)
                Assert.That(CardSheetRules.TargetOf(CharTableTests.RealGraph().Get(id), CardFace.Feature, 8), Is.EqualTo(FaceTarget.Ally), id);
        }

        [TestCase("崩", "厚积薄发", EffectKind.SpendHeft)]
        [TestCase("㙓", "厚积薄发", EffectKind.SpendHeft)]
        [TestCase("溃", "涌泉相报", EffectKind.SpendWellspring)]
        [TestCase("㵘", "涌泉相报", EffectKind.SpendWellspring)]
        public void SpendChars_Lv1IsTheSpendKeyword_BodyHasNoRiders(string id, string name, EffectKind spend)
        {
            var d = CharTableTests.RealGraph().Get(id);
            Assert.That(d.Traits.Single(t => t.Slot == TraitSlot.Lv1 && t.Face == TraitFace.Attack).Name, Is.EqualTo(name));
            Assert.That(d.AttackEffects.Select(e => e.Kind), Is.EqualTo(new[] { EffectKind.DamageSingle, spend }));
        }

        [Test]
        public void Jian_Lv8_IsRowCounter()
        {
            var block = TraitRules.CastEffects(CharTableTests.RealGraph().Get("剑"), CardFace.Feature, 8).Single(e => e.Kind == EffectKind.Block);
            Assert.That(block.CounterRow, Is.True);
        }

        [Test]
        public void Zhe_Lv4_FeatureNatureHasSelfCut()
        {
            var summon = TraitRules.CastEffects(CharTableTests.RealGraph().Get("柘"), CardFace.Feature, 4).First(e => e.Kind == EffectKind.Summon);
            Assert.That(summon.Passive.SelfCut, Is.EqualTo(15));
        }

        // ---- controller 裁定补充 ----
        private static SummonSnapshot WoodSprite(int slot) => new()
        {
            Slot = slot, Char = "木", Element = Element.Wood, Hp = 100000, MaxHp = 100000, Attack = 10, Speed = 100,
        };

        private static BattleEngine Duel(CharDef[] defs, params SummonSnapshot[] summons) =>
            new(RebalanceFixture.Graph(defs),
                new BattleConfig { PlayerMaxHp = RebalanceFixture.BaseMaxHp, PlayerAttack = 100, ApPerTurn = 20 },
                defs.SelectMany(d => new[] { d.Id, d.Id }).ToArray(), System.Array.Empty<string>(),
                new[] { RebalanceFixture.Mob(attack: 0) }, seed: 1, startingSummons: summons);

        [Test]
        public void KindleAllAllies_PlusAttune_KindlesEveryoneButAttunesOnlyTheChosenSummon()
        {
            var face = new CharDef("引", Element.Fire, effects: new[]
            {
                new EffectDef(EffectKind.Kindle, 20, turns: 2, pick: EffectPick.AllAllies), new EffectDef(EffectKind.Attune, 0),
            }, attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 100) });
            var b = Duel(new[] { face }, WoodSprite(0), WoodSprite(1));
            Assert.That(b.Cast("引", -1, attackMode: false, allySlot: 1), Is.EqualTo(BattleError.None));
            Assert.That(b.PlayerStatuses.Has(StatusKind.Kindle), Is.True, "玩家");
            Assert.That(b.Summons[0].Statuses.Has(StatusKind.Kindle), Is.True, "木灵0");
            Assert.That(b.Summons[1].Statuses.Has(StatusKind.Kindle), Is.True, "木灵1");
            Assert.That(b.Summons[1].Element, Is.EqualTo(Element.Fire), "被选中的改属性");
            Assert.That(b.Summons[0].Element, Is.EqualTo(Element.Wood), "另一只不变");
        }

        private static CharDef Graftor() => new("藤", Element.Wood, effects: new[]
        {
            new EffectDef(EffectKind.Summon, 100, summonCount: 1, summonAttack: 10, summonChar: "木",
                passive: new SummonPassive { OnHitFreezeChance = 20, OnHitFreezeTurns = 1 }),
        });

        [Test]
        public void Graft_OnNonWoodSummon_EmitsElementChangedWood()
        {
            var metal = WoodSprite(0); metal.Element = Element.Metal;
            var b = Duel(new[] { Graftor() }, metal);
            Assert.That(b.Cast("藤", -1, attackMode: false, allySlot: 0), Is.EqualTo(BattleError.None));
            Assert.That(b.Summons[0].Element, Is.EqualTo(Element.Wood));
            Assert.That(b.LastEvents.Any(e => e.Kind == BattleEventKind.ElementChanged && e.TargetIndex == 0
                && e.Amount == (int)Element.Wood), Is.True);
            Assert.That(b.LastEvents.Any(e => e.Kind == BattleEventKind.Unseal), Is.False, "嫁接回木不是解封");
        }

        [Test]
        public void Graft_OnWoodSummon_EmitsNoElementChange()
        {
            var b = Duel(new[] { Graftor() }, WoodSprite(0));
            Assert.That(b.Cast("藤", -1, attackMode: false, allySlot: 0), Is.EqualTo(BattleError.None));
            Assert.That(b.LastEvents.Any(e => e.Kind == BattleEventKind.Unseal || e.Kind == BattleEventKind.ElementChanged), Is.False);
        }
    }
}
