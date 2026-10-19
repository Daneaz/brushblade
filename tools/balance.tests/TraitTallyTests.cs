using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Balance.Bot;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Balance.Tests
{
    /// <summary>--triggers 按特性计触发(Plan F · A2 §3):读本体每次动作后的 LastEvents / 出字播报,按特性名计数。</summary>
    public class TraitTallyTests
    {
        private static int Count(TraitTally t, string name) => t.Counts.TryGetValue(name, out int n) ? n : 0;

        [Test]
        public void LabelOf_EffectSource_UsesTraitNameFromGraph()
        {
            var tally = new TraitTally(Fixture.Graph, null);
            Assert.That(tally.LabelOf(EffectSource.ShieldRecoil), Is.EqualTo("反震"));
            Assert.That(tally.LabelOf(EffectSource.ShieldShatter), Is.EqualTo("碎玉"));
        }

        [Test]
        public void Cast_Lv8FeatureTrait_CountsAnnouncedName_AndFace()
        {
            // 冰 Lv8 五行面「冰晶」(出字时触发的主动特性):出一次五行面 → 冰晶 ×1
            var t = new TestBattle(new[] { "冰" }, cardLevel: 8);
            var tally = new TraitTally(Fixture.Graph, t.CardLevels);
            var cast = new BotAction { Kind = ActionKind.Cast, CharId = "冰", AttackMode = false };
            tally.Before(cast, t.Battle);
            Assert.That(cast.ApplyTo(t.Battle), Is.EqualTo(BattleError.None));
            tally.After(cast, t.Battle);
            Assert.That(Count(tally, "冰晶"), Is.EqualTo(1));
            Assert.That(tally.FeatureFaceByChar["冰"], Is.EqualTo(1));
            Assert.That(tally.AttackFaceByChar.ContainsKey("冰"), Is.False);
        }

        [Test]
        public void Cast_BelowLv8_DoesNotCountLv8Trait()
        {
            var t = new TestBattle(new[] { "冰" }, cardLevel: 5);
            var tally = new TraitTally(Fixture.Graph, t.CardLevels);
            var cast = new BotAction { Kind = ActionKind.Cast, CharId = "冰", AttackMode = false };
            tally.Before(cast, t.Battle);
            cast.ApplyTo(t.Battle);
            tally.After(cast, t.Battle);
            Assert.That(Count(tally, "冰晶"), Is.EqualTo(0));
        }

        [Test]
        public void RejectedAction_CountsNothing()
        {
            var t = new TestBattle(new[] { "冰" }, cardLevel: 8, ap: 0);
            var tally = new TraitTally(Fixture.Graph, t.CardLevels);
            var cast = new BotAction { Kind = ActionKind.Cast, CharId = "冰", AttackMode = false };
            tally.Before(cast, t.Battle);
            Assert.That(cast.ApplyTo(t.Battle), Is.Not.EqualTo(BattleError.None));
            // 被拒的动作不调 After;下一个动作的 Before 会覆盖挂起的播报
            var end = new BotAction { Kind = ActionKind.EndTurn };
            tally.Before(end, t.Battle);
            end.ApplyTo(t.Battle);
            tally.After(end, t.Battle);
            Assert.That(Count(tally, "冰晶"), Is.EqualTo(0));
            Assert.That(tally.FeatureFaceByChar.Count, Is.EqualTo(0));
        }

        [Test]
        public void EndTurn_WoodSpiritWithSprout_CountsSplit()
        {
            // 藻灵本命丛生:它那一拍开始时分裂一只小藻 → 敌人回合后的 LastEvents 里有一条 Summon
            var zao = new SummonSnapshot
            {
                Slot = 0, Char = "藻", SourceChar = "藻", Element = Element.Wood, Hp = 1000, MaxHp = 1000,
                Attack = 40, Speed = 100, Statuses = new List<StatusEffect>(),
                Passive = new SummonPassive { SproutPercent = 30, SproutMax = 2 },
            };
            var t = new TestBattle(new[] { "炎" }, summons: new[] { zao });
            var tally = new TraitTally(Fixture.Graph, t.CardLevels);
            var end = new BotAction { Kind = ActionKind.EndTurn };
            tally.Before(end, t.Battle);
            end.ApplyTo(t.Battle);
            tally.After(end, t.Battle);
            Assert.That(Count(tally, TraitTally.SproutLabel), Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void PlayTurn_ReportsEveryExecutedActionToObserver()
        {
            var t = new TestBattle(new[] { "冰" }, cardLevel: 8);
            var seen = new List<ActionKind>();
            var observer = new RecordingObserver(seen);
            var executed = Search.PlayTurn(t.Battle, Fixture.Graph, t.Config, t.CardLevels, SearchOptions.Fast,
                turnSeed: 42, stats: new BotStats(), observer: observer);
            Assert.That(seen, Is.EqualTo(executed.Select(a => a.Kind).ToList()));
            Assert.That(seen.Last(), Is.EqualTo(ActionKind.EndTurn));
        }

        private sealed class RecordingObserver : ITurnObserver
        {
            private readonly List<ActionKind> _seen;
            public RecordingObserver(List<ActionKind> seen) { _seen = seen; }
            public void Before(BotAction a, BattleEngine b) { }
            public void After(BotAction a, BattleEngine b) => _seen.Add(a.Kind);
        }

        [Test]
        public void Climb_WithTriggersOff_HasNoTally_AndOnHasTally()
        {
            var off = RunnerSetup(collect: false);
            var on = RunnerSetup(collect: true);
            var lib = new[] { "冰", "炎", "林" };
            var a = BotRunner.Climb(off, 1, lib, Array.Empty<string>());
            var b = BotRunner.Climb(on, 1, lib, Array.Empty<string>());
            Assert.That(a.Triggers, Is.Null);
            Assert.That(b.Triggers, Is.Not.Null);
            Assert.That(b.DeathDepth, Is.EqualTo(a.DeathDepth), "收集不改对局");
            Assert.That(b.Triggers.AttackFaceByChar.Values.Sum() + b.Triggers.FeatureFaceByChar.Values.Sum(),
                Is.EqualTo(a.CastsByChar.Values.Sum()), "攻 / 五行面细分之和 = 出字总数");
        }

        private static ClimbSetup RunnerSetup(bool collect)
        {
            var owned = new[] { "冰", "炎", "林" };
            var levels = owned.ToDictionary(c => c, _ => 8);
            var campaign = RunFixture.Campaign;
            return new ClimbSetup
            {
                Graph = Fixture.Graph, Campaign = campaign, OwnedCards = owned, CardLevels = levels,
                NewBattleConfig = RunFixture.NewBattleConfig, Search = SearchOptions.Fast,
                Appraiser = new CardAppraiser(Fixture.Graph, campaign.Endless, RunFixture.NewBattleConfig),
                DepthCap = 3, CollectTriggers = collect,
            };
        }
    }
}
