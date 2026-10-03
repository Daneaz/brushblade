using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>战斗钩子总线(spec v6 §5 / §11.4)。</summary>
    public class BattleHookTests
    {
        private sealed class Recorder : IBattleHookListener
        {
            public readonly List<HookArgs> Log = new();
            public Action<BattleEngine> OnFirst;
            public void OnHook(BattleEngine battle, in HookArgs args)
            {
                Log.Add(args);
                var f = OnFirst; OnFirst = null; f?.Invoke(battle);
            }
            public List<HookArgs> Of(HookKind k) => Log.Where(a => a.Kind == k).ToList();
        }

        private static (BattleEngine, Recorder) Battle(RecipeGraph graph, string[] library, params EnemyDef[] enemies)
        {
            var b = RebalanceFixture.Battle(graph, library, enemies);
            var r = new Recorder();
            b.AddHookListener(r);
            return (b, r);
        }

        [Test]
        public void CastKill_RaisesEnemyHitAndKilled_WithPlayerAsKiller()
        {
            var (b, r) = Battle(RebalanceFixture.Graph(), new[] { "甲" }, RebalanceFixture.Mob(hp: 20));
            b.Cast("甲", 0);
            var hit = r.Of(HookKind.EnemyHit).Single();
            Assert.That(hit.Subject, Is.EqualTo(UnitRef.Enemy(0)));
            Assert.That(hit.Other, Is.EqualTo(UnitRef.Player));
            Assert.That(hit.Amount, Is.EqualTo(20));
            var kill = r.Of(HookKind.EnemyKilled).Single();
            Assert.That(kill.Other, Is.EqualTo(UnitRef.Player));
            Assert.That(kill.Depth, Is.EqualTo(0));
        }

        [Test]
        public void BurnKill_SourceIsBurn()
        {
            var burner = RebalanceFixture.Char("燃", new EffectDef(EffectKind.BurnSingle, 5));
            var (b, r) = Battle(RebalanceFixture.Graph(burner), new[] { "燃" }, RebalanceFixture.Mob(hp: 30));
            b.Cast("燃", 0);
            b.EndTurn();   // 敌人行动开头结算灼烧:5 层 × 20 = 100 ≥ 30
            var kill = r.Of(HookKind.EnemyKilled).Single();
            Assert.That(kill.Source, Is.EqualTo(EffectSource.Burn));
            Assert.That(kill.Other, Is.EqualTo(UnitRef.Player));
        }

        [Test]
        public void EnemyAttack_RaisesPlayerHit_WithAttacker()
        {
            var (b, r) = Battle(RebalanceFixture.Graph(), new[] { "甲" }, RebalanceFixture.Mob(attack: 10));
            b.EndTurn();
            var hit = r.Of(HookKind.PlayerHit).First();
            Assert.That(hit.Subject, Is.EqualTo(UnitRef.Player));
            Assert.That(hit.Other, Is.EqualTo(UnitRef.Enemy(0)));
            Assert.That(hit.Amount, Is.GreaterThan(0));
        }

        [Test]
        public void StatusApplied_CarriesTargetStatusAndApplier()
        {
            var burner = RebalanceFixture.Char("燃", new EffectDef(EffectKind.BurnSingle, 2));
            var (b, r) = Battle(RebalanceFixture.Graph(burner), new[] { "燃" }, RebalanceFixture.Mob());
            b.Cast("燃", 0);
            var s = r.Of(HookKind.StatusApplied).Single(a => a.Status == StatusKind.Burn);
            Assert.That(s.Subject, Is.EqualTo(UnitRef.Enemy(0)));
            Assert.That(s.Other, Is.EqualTo(UnitRef.Player));
        }

        [Test]
        public void EndTurn_RaisesTurnEndedForPlayer_ThenEnemyTurnBoundaries()
        {
            var (b, r) = Battle(RebalanceFixture.Graph(), new[] { "甲" }, RebalanceFixture.Mob());
            b.EndTurn();
            var kinds = r.Log.Where(a => a.Kind == HookKind.TurnStarted || a.Kind == HookKind.TurnEnded)
                .Select(a => (a.Kind, a.Subject)).ToList();
            Assert.That(kinds.First(), Is.EqualTo((HookKind.TurnEnded, UnitRef.Player)));
            Assert.That(kinds.Contains((HookKind.TurnStarted, UnitRef.Enemy(0))), Is.True);
            Assert.That(kinds.Contains((HookKind.TurnEnded, UnitRef.Enemy(0))), Is.True);
            Assert.That(kinds.Last(), Is.EqualTo((HookKind.TurnStarted, UnitRef.Player)));
        }

        [Test]
        public void PlayerThreshold_FiresOncePerBattle_AndSurvivesSnapshot()
        {
            var graph = RebalanceFixture.Graph();
            var (b, r) = Battle(graph, new[] { "甲" }, RebalanceFixture.Mob(attack: 150));
            b.EndTurn();   // 500 → 350
            b.EndTurn();   // 350 → 200,跌破 250
            Assert.That(r.Of(HookKind.HpThresholdCrossed).Count, Is.EqualTo(1));
            Assert.That(r.Of(HookKind.HpThresholdCrossed)[0].Subject, Is.EqualTo(UnitRef.Player));

            var restored = BattleEngine.Restore(b.Capture(), graph,
                new BattleConfig { PlayerMaxHp = RebalanceFixture.BaseMaxHp, PlayerAttack = 100 }, null,
                new Dictionary<string, EnemyDef> { ["怔"] = RebalanceFixture.Mob(attack: 150) });
            var r2 = new Recorder();
            restored.AddHookListener(r2);
            restored.EndTurn();   // 200 → 50,仍在 50% 以下,不再触发
            Assert.That(r2.Of(HookKind.HpThresholdCrossed), Is.Empty, "标记进快照,读档后不重复触发");
        }

        [Test]
        public void ComposeAndDismantle_RaiseHooks()
        {
            var graph = RebalanceFixture.Graph(new CharDef("木", Element.Wood), new CharDef("林", Element.Wood, new[] { "木", "木" }));
            var (b, r) = Battle(graph, new[] { "林" }, RebalanceFixture.Mob());
            Assert.That(b.Dismantle("林"), Is.EqualTo(BattleError.None));
            Assert.That(b.Compose("林"), Is.EqualTo(BattleError.None));
            Assert.That(r.Of(HookKind.Dismantled).Single().CharId, Is.EqualTo("林"));
            Assert.That(r.Of(HookKind.Composed).Single().CharId, Is.EqualTo("林"));
        }

        [Test]
        public void ListenerAddingListenerDuringCallback_DoesNotThrow()
        {
            var (b, r) = Battle(RebalanceFixture.Graph(), new[] { "甲" }, RebalanceFixture.Mob(hp: 20));
            r.OnFirst = battle => battle.AddHookListener(new Recorder());
            Assert.DoesNotThrow(() => b.Cast("甲", 0));
        }

        [Test]
        public void NoListener_NothingChanges()
        {
            var a = RebalanceFixture.Battle(RebalanceFixture.Graph(), new[] { "甲" }, RebalanceFixture.Mob(attack: 10));
            a.Cast("甲", 0); a.EndTurn();
            Assert.That(a.TriggerDepth, Is.EqualTo(0));
        }
    }
}
