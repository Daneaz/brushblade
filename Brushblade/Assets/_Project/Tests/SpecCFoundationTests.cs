using System;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>spec C F0:新枚举只在末尾追加、新字段可加载、Fold 透传、AmpTerms 第 5 项。只加名不加行为。</summary>
    public class SpecCFoundationTests
    {
        [Test]
        public void Enums_AppendedAtTheEnd()
        {
            Assert.That(new[] { EffectKind.Kindle, EffectKind.Attune, EffectKind.SpendHeal, EffectKind.SpendShield }.Select(k => (int)k),
                Is.EqualTo(Enumerable.Range((int)EffectKind.ReviveShield + 1, 4)));
            Assert.That(Enum.GetValues(typeof(EffectKind)).Cast<int>().Max(), Is.EqualTo((int)EffectKind.SpendShield));
            Assert.That((int)StatusKind.Kindle, Is.EqualTo((int)StatusKind.ThornMark + 1));
            Assert.That(Enum.GetValues(typeof(StatusKind)).Cast<int>().Max(), Is.EqualTo((int)StatusKind.Kindle));
            Assert.That((int)AmpScope.Spend, Is.EqualTo((int)AmpScope.Burn + 1));
            Assert.That((int)ScaleBasis.Spent, Is.EqualTo((int)ScaleBasis.SummonCount + 1));
        }

        [Test]
        public void InScope_SpendOnlyForSpendKinds_AllCoversKindleButNotSpend()
        {
            Assert.That(TraitRules.InScope(AmpScope.Spend, EffectKind.SpendHeft), Is.True);
            Assert.That(TraitRules.InScope(AmpScope.Spend, EffectKind.SpendWellspring), Is.True);
            Assert.That(TraitRules.InScope(AmpScope.Spend, EffectKind.DamageSingle), Is.False);
            Assert.That(TraitRules.InScope(AmpScope.All, EffectKind.Kindle), Is.True);
            Assert.That(TraitRules.InScope(AmpScope.All, EffectKind.SpendHeft), Is.False, "All 不覆盖释放伤害(Q6)");
        }

        [Test]
        public void Kindle_AllowsAllAllies_AndTurnsLiveOnTurns()
        {
            Assert.That(EffectPickRules.Allows(EffectKind.Kindle, EffectPick.AllAllies), Is.True);
            Assert.That(EffectPickRules.Allows(EffectKind.Kindle, EffectPick.All), Is.False);
            Assert.That(TraitRules.TurnsOf(new EffectDef(EffectKind.Kindle, 20, turns: 3)), Is.EqualTo(3));
        }

        [Test]
        public void Fold_CarriesCounterRowToBlock_AndMinSpentIntoAmpTerms()
        {
            var def = new CharDef("扫", Element.Metal,
                effects: new[] { new EffectDef(EffectKind.Block, 1, counterPercent: 30), new EffectDef(EffectKind.BlockMod, 0, counterRow: true) },
                attackEffects: new[]
                {
                    new EffectDef(EffectKind.SpendHeft, 10),
                    new EffectDef(EffectKind.Amplify, 100, scope: AmpScope.Spend, minSpent: 8),
                });
            var feature = TraitRules.CastEffects(def, CardFace.Feature, 1);
            Assert.That(feature.Single().CounterRow, Is.True);
            var attack = TraitRules.CastEffects(def, CardFace.Attack, 1);
            var term = attack.Single().AmpTerms.Single();
            Assert.That((term.Percent, term.MinSpent), Is.EqualTo((100, 8)));
        }

        [Test]
        public void StatusEffect_Clone_CarriesCounterRow_SummonPassive_CarriesSelfCut()
        {
            Assert.That(new StatusEffect { Kind = StatusKind.Block, CounterRow = true }.Clone().CounterRow, Is.True);
            Assert.That(new SummonPassive { SelfCut = 15 }.Clone().SelfCut, Is.EqualTo(15));
            Assert.That(TraitRules.MergePassive(new SummonPassive { Thorns = 5 }, new SummonPassive { SelfCut = 15 }).SelfCut, Is.EqualTo(15));
        }

        private static RecipeGraph Load(string effects, string attack = @"{""kind"":""DamageSingle"",""value"":40}") =>
            Brushblade.Data.ConfigLoader.LoadGraph(
                @"{""chars"":[{""id"":""测"",""element"":""Earth"",""effects"":[" + effects
                + @"],""attackEffects"":[" + attack + "]}]}");

        [Test]
        public void ConfigLoader_LoadsNewFields()
        {
            var g = Load(@"{""kind"":""Block"",""value"":1,""counterPercent"":30},{""kind"":""BlockMod"",""counterRow"":true},"
                + @"{""kind"":""Kindle"",""value"":20,""turns"":2,""pick"":""AllAllies""},{""kind"":""Attune"",""value"":0}",
                @"{""kind"":""SpendHeft"",""value"":10},{""kind"":""Amplify"",""value"":100,""scope"":""Spend"",""minSpent"":8},"
                + @"{""kind"":""Freeze"",""value"":1,""pick"":""All"",""minSpent"":6},"
                + @"{""kind"":""ArmorBreak"",""value"":2,""turns"":2,""pick"":""All"",""scaleBy"":""Spent""},"
                + @"{""kind"":""SpendHeal"",""value"":30},{""kind"":""SpendShield"",""value"":50}");
            var d = g.Get("测");
            Assert.That(d.Effects[1].CounterRow, Is.True);
            Assert.That((d.Effects[2].Kind, d.Effects[2].Pick, d.Effects[2].Turns), Is.EqualTo((EffectKind.Kindle, EffectPick.AllAllies, 2)));
            Assert.That(d.Effects[3].Kind, Is.EqualTo(EffectKind.Attune));
            Assert.That((d.AttackEffects[1].Scope, d.AttackEffects[1].MinSpent), Is.EqualTo((AmpScope.Spend, 8)));
            Assert.That(d.AttackEffects[2].MinSpent, Is.EqualTo(6));
            Assert.That(d.AttackEffects[3].ScaleBy, Is.EqualTo(ScaleBasis.Spent));
        }

        [Test]
        public void ConfigLoader_LoadsSelfCutPassive()
        {
            var g = Brushblade.Data.ConfigLoader.LoadGraph(
                @"{""chars"":[{""id"":""柘"",""element"":""Wood"",""effects"":[{""kind"":""Summon"",""value"":100,""count"":1,""attack"":0,"
                + @"""summonChar"":""柘"",""passive"":{""selfCut"":15}}]}]}");
            Assert.That(g.Get("柘").Effects[0].Passive.SelfCut, Is.EqualTo(15));
        }

        [TestCase(@"{""kind"":""Shield"",""value"":5,""minSpent"":3}")]                                   // minSpent 只给 Freeze / Slow / scope Spend 的 Amplify
        [TestCase(@"{""kind"":""Amplify"",""value"":30,""scope"":""All"",""minSpent"":3}")]
        [TestCase(@"{""kind"":""Freeze"",""value"":1,""pick"":""All"",""minSpent"":-1}")]
        [TestCase(@"{""kind"":""Freeze"",""value"":1,""pick"":""All"",""minSpent"":6,""openingBattles"":2}")]   // 开局登记丢 minSpent(终审修复 4)
        [TestCase(@"{""kind"":""Shield"",""value"":5,""scaleBy"":""Spent""}")]                            // per Spent 只给 ArmorBreak
        [TestCase(@"{""kind"":""Kindle"",""value"":20}")]                                                // 燃意必须写回合
        [TestCase(@"{""kind"":""SpendHeal"",""value"":0}")]                                              // 1–100
        [TestCase(@"{""kind"":""SpendShield"",""value"":101}")]
        [TestCase(@"{""kind"":""Block"",""value"":1,""counterRow"":true}")]                               // 横扫只写在 BlockMod 上
        public void ConfigLoader_RejectsMisplacedSpecCFields(string effect)
        {
            Assert.Throws<Brushblade.Data.ConfigException>(() => Load(effect));
        }
    }
}
