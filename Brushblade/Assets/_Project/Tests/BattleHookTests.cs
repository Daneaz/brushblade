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
            Assert.That(restored.Capture().PlayerThresholdCrossed, Is.True, "读档后标记仍在");
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

        // ── fix round 1 追加 ──

        /// <summary>攻 0 的召唤字:不出手(HasStrikeOutput = false),只当靶子。</summary>
        private static CharDef Summoner(int hp = 100) =>
            RebalanceFixture.Char("召", new EffectDef(EffectKind.Summon, hp, summonCount: 1, summonAttack: 0, summonChar: "召"));

        private static int OnlySummonSlot(BattleEngine b)
        {
            for (int s = 0; s < b.Summons.Count; s++)
                if (b.Summons[s] != null) return s;
            return -1;
        }

        private static int ThresholdCount(Recorder r, UnitRef unit) =>
            r.Of(HookKind.HpThresholdCrossed).Count(a => a.Subject == unit);

        [Test]
        public void EnemyHit_AmountIsHpActuallyLost_NoOverkill()
        {
            var (b, r) = Battle(RebalanceFixture.Graph(), new[] { "甲" }, RebalanceFixture.Mob(hp: 5));
            b.Cast("甲", 0);   // 20 伤打 5 血
            Assert.That(r.Of(HookKind.EnemyHit).Single().Amount, Is.EqualTo(5));
        }

        [Test]
        public void PlayerThreshold_HealAboveThenDropAgain_DoesNotRefire()
        {
            var healer = RebalanceFixture.Char("愈", new EffectDef(EffectKind.HealSelf, 250));
            var (b, r) = Battle(RebalanceFixture.Graph(healer), new[] { "愈" }, RebalanceFixture.Mob(attack: 150));
            b.EndTurn();   // 500 → 350
            b.EndTurn();   // 350 → 200,跌破
            Assert.That(ThresholdCount(r, UnitRef.Player), Is.EqualTo(1));
            Assert.That(b.Cast("愈"), Is.EqualTo(BattleError.None));
            Assert.That(b.PlayerHp * 100, Is.GreaterThanOrEqualTo(b.MaxHp * BattleEngine.HpThresholdPercent), "先治回 50% 以上");
            b.EndTurn();
            b.EndTurn();
            Assert.That(b.PlayerHp * 100, Is.LessThan(b.MaxHp * BattleEngine.HpThresholdPercent), "再次跌破");
            Assert.That(ThresholdCount(r, UnitRef.Player), Is.EqualTo(1), "每场一次");
        }

        [Test]
        public void SummonThreshold_FiresOncePerSlot_AndSummonDiedCarriesKiller()
        {
            var (b, r) = Battle(RebalanceFixture.Graph(Summoner()), new[] { "召" }, RebalanceFixture.Mob(attack: 30));
            Assert.That(b.Cast("召"), Is.EqualTo(BattleError.None));
            int slot = OnlySummonSlot(b);
            var unit = UnitRef.Summon(slot);
            for (int i = 0; i < 4 && b.Summons[slot].Alive; i++) b.EndTurn();   // 100 → 70 → 40 → 10 → 0
            Assert.That(b.Summons[slot].Alive, Is.False);
            Assert.That(ThresholdCount(r, unit), Is.EqualTo(1));
            var died = r.Of(HookKind.SummonDied).Single();
            Assert.That(died.Subject, Is.EqualTo(unit));
            Assert.That(died.Other, Is.EqualTo(UnitRef.Enemy(0)), "SummonDied 带击杀者");
        }

        [Test]
        public void SummonThreshold_ReviveDoesNotReset()
        {
            var (b, r) = Battle(RebalanceFixture.Graph(Summoner()), new[] { "召" }, RebalanceFixture.Mob(attack: 30));
            b.Cast("召");
            int slot = OnlySummonSlot(b);
            var unit = UnitRef.Summon(slot);
            b.EndTurn();   // 100 → 70
            b.EndTurn();   // 70 → 40,跌破
            Assert.That(ThresholdCount(r, unit), Is.EqualTo(1));
            b.KillSummonForTest(slot);
            b.ReviveSummonForTest(slot);   // 半血 50 = 恰好 ≥ 50%
            Assert.That(b.Summons[slot].Hp * 100, Is.GreaterThanOrEqualTo(b.Summons[slot].MaxHp * BattleEngine.HpThresholdPercent));
            b.EndTurn();   // 50 → 20,再次跌破
            Assert.That(b.Summons[slot].Hp * 100, Is.LessThan(b.Summons[slot].MaxHp * BattleEngine.HpThresholdPercent));
            Assert.That(ThresholdCount(r, unit), Is.EqualTo(1), "复活不重置标记");
        }

        [Test]
        public void SummonThreshold_NewSummonInSameSlot_ClearsFlag()
        {
            var (b, r) = Battle(RebalanceFixture.Graph(Summoner()), new[] { "召", "召" }, RebalanceFixture.Mob(attack: 30));
            b.Cast("召");
            int slot = OnlySummonSlot(b);
            var unit = UnitRef.Summon(slot);
            b.EndTurn();
            b.EndTurn();   // 跌破
            Assert.That(ThresholdCount(r, unit), Is.EqualTo(1));
            b.KillSummonForTest(slot);
            Assert.That(b.Cast("召", summonSlots: new[] { slot }), Is.EqualTo(BattleError.None));
            Assert.That(b.Summons[slot].Alive, Is.True, "新召唤物落进同一槽");
            b.EndTurn();
            b.EndTurn();   // 新单位跌破
            Assert.That(ThresholdCount(r, unit), Is.EqualTo(2), "新单位不继承旧单位的标记");
        }

        [Test]
        public void BossDevour_RaisesSummonHitAndThreshold_AndKiller()
        {
            var boss = new EnemyDef("试炼", Element.Heart, 100, 5,
                phases: new[]
                {
                    new BossPhaseDef("甲", Element.Heart, 100, 5, skill: BossSkill.Devour),
                    new BossPhaseDef("乙", Element.Heart, 100, 5),
                });
            var (b, r) = Battle(RebalanceFixture.Graph(Summoner()), new[] { "召" }, boss);
            b.EndTurn();   // 先走掉普攻回合(此时场上没有召唤物)
            Assert.That(b.Cast("召"), Is.EqualTo(BattleError.None));
            int slot = OnlySummonSlot(b);
            var unit = UnitRef.Summon(slot);
            Assert.That(b.Summons[slot].Hp, Is.EqualTo(b.Summons[slot].MaxHp), "满血被吞");
            b.EndTurn();   // 蓄力
            b.EndTurn();   // 吞噬
            Assert.That(b.Summons[slot].Alive, Is.False);
            var hits = r.Of(HookKind.SummonHit);
            Assert.That(hits.Count, Is.EqualTo(1));
            Assert.That(hits[0].Subject, Is.EqualTo(unit));
            Assert.That(hits[0].Other, Is.EqualTo(UnitRef.Enemy(0)));
            Assert.That(ThresholdCount(r, unit), Is.EqualTo(1));
            Assert.That(r.Of(HookKind.SummonDied).Single().Other, Is.EqualTo(UnitRef.Enemy(0)));
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
