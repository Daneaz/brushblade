using System;
using System.Collections.Generic;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>spec A §6.1 鍂·金鸣:Amplify scaleBy Morale = 百分点 × 结算时战意层数。</summary>
    public class AmplifyPerMoraleTests
    {
        private static CharDef Striker(string id, params EffectDef[] amp) => new(id, Element.Heart,
            effects: new[] { new EffectDef(EffectKind.Morale, 3) },
            attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 100) },
            traits: amp.Length == 0 ? null
                : new[] { new TraitDef(TraitSlot.Lv6, TraitFace.Attack, TraitForm.Passive, null, "鸣", amp) });

        private static int Dealt(CharDef def, bool gainMorale)
        {
            var b = new BattleEngine(RebalanceFixture.Graph(def),
                new BattleConfig { PlayerMaxHp = RebalanceFixture.BaseMaxHp, PlayerAttack = 100 },
                new[] { def.Id, def.Id, def.Id }, Array.Empty<string>(), new[] { RebalanceFixture.Mob() }, seed: 1,
                cardLevels: new Dictionary<string, int> { [def.Id] = 6 });
            if (gainMorale) b.Cast(def.Id, -1);
            int hp = b.Enemies[0].Hp;
            b.Cast(def.Id, 0, attackMode: true);
            return hp - b.Enemies[0].Hp;
        }

        [Test]
        public void PerMorale_EqualsFlatAmplifyOfPercentTimesStacks()
        {
            int perMorale = Dealt(Striker("鸣", new EffectDef(EffectKind.Amplify, 5, scope: AmpScope.Damage, scaleBy: ScaleBasis.Morale)), true);
            int flat = Dealt(Striker("平", new EffectDef(EffectKind.Amplify, 15, scope: AmpScope.Damage)), true);
            Assert.That(perMorale, Is.EqualTo(flat), "3 层 × 5% = +15%");
        }

        [Test]
        public void PerMorale_ZeroStacks_NoBonus()
        {
            int perMorale = Dealt(Striker("鸣", new EffectDef(EffectKind.Amplify, 5, scope: AmpScope.Damage, scaleBy: ScaleBasis.Morale)), false);
            Assert.That(perMorale, Is.EqualTo(Dealt(Striker("素"), false)));
        }

        [Test]
        public void ConfigLoader_AcceptsAmplifyScaleByMorale()
        {
            var g = Brushblade.Data.ConfigLoader.LoadGraph(
                @"{""chars"":[{""id"":""鍂"",""element"":""Metal"",""effects"":[{""kind"":""Morale"",""value"":1}],"
                + @"""attackEffects"":[{""kind"":""DamageSingle"",""value"":40},{""kind"":""Amplify"",""value"":5,""scaleBy"":""Morale""}]}]}");
            Assert.That(g.Get("鍂").AttackEffects[1].ScaleBy, Is.EqualTo(ScaleBasis.Morale));
        }
    }
}
