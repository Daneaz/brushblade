using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>R1(spec v7 §10):冻结不叠不延长,结束后得霜抗 N 回合(N = 刚结束的冻结回合数);
    /// 减速不叠加只刷新(取最强、取最长)。</summary>
    public class FreezeRulesTests
    {
        private static readonly CharDef Freeze2 = new("冻", Element.Water,
            effects: new[] { new EffectDef(EffectKind.Freeze, 2) });
        private static readonly CharDef Freeze1 = new("冷", Element.Water,
            effects: new[] { new EffectDef(EffectKind.Freeze, 1) });
        private static readonly CharDef SlowA = new("缓", Element.Water,
            effects: new[] { new EffectDef(EffectKind.Slow, 1) });
        private static readonly CharDef SlowB = new("迟", Element.Water,
            effects: new[] { new EffectDef(EffectKind.Slow, 3) });
        // 霜:出手 100% 冻结 1 回合的召唤物(藤 的确定性版本)
        private static readonly CharDef Frosty = new("霜", Element.Wood,
            effects: new[] { new EffectDef(EffectKind.Summon, 10, summonCount: 1, summonAttack: 3, summonChar: "木",
                passive: new SummonPassive { OnHitFreezeChance = 100, OnHitFreezeTurns = 3 }) });

        private static RecipeGraph FullGraph() => RebalanceFixture.Graph(Freeze2, Freeze1, SlowA, SlowB);

        private static BattleEngine Battle(params string[] library) =>
            RebalanceFixture.Battle(FullGraph(), library, RebalanceFixture.Mob(attack: 1));

        private static StatusBag Bag(BattleEngine b) => b.Enemies[0].Statuses;

        private sealed class RecordingListener : IBattleHookListener
        {
            private readonly List<StatusKind?> _seen;
            public RecordingListener(List<StatusKind?> seen) { _seen = seen; }
            public void OnHook(BattleEngine battle, in HookArgs args)
            {
                if (args.Kind == HookKind.StatusApplied) _seen.Add(args.Status);
            }
        }

        [Test]
        public void Frozen_CannotBeRefrozenOrExtended()
        {
            var b = Battle("冷", "冻", "冻");
            b.Cast("冷", 0);
            b.Cast("冻", 0);
            Assert.That(Bag(b).Find(StatusKind.Freeze).TurnsLeft, Is.EqualTo(1), "第二次冻结被拒,不延长");
        }

        [Test]
        public void FreezeEnd_GrantsFrostResistForSameTurns()
        {
            var b = Battle("冻", "冻", "冻");
            b.Cast("冻", 0);
            b.EndTurn();
            Assert.That(Bag(b).Has(StatusKind.Freeze), Is.True, "2 回合冻结,第一拍后还剩 1");
            b.EndTurn();
            Assert.That(Bag(b).Has(StatusKind.Freeze), Is.False);
            Assert.That(Bag(b).Find(StatusKind.FrostResist).TurnsLeft, Is.EqualTo(2));
        }

        [Test]
        public void FrostResist_BlocksFreeze_UntilItExpires()
        {
            var b = Battle("冷", "冷", "冷", "冷");
            b.Cast("冷", 0);
            b.EndTurn();                       // 冻结那一拍被跳过 → 霜抗 1
            Assert.That(Bag(b).Find(StatusKind.FrostResist).TurnsLeft, Is.EqualTo(1));
            b.Cast("冷", 0);
            Assert.That(Bag(b).Has(StatusKind.Freeze), Is.False, "霜抗中冻不上");
            b.EndTurn();                       // 霜抗随这一拍递减到 0
            Assert.That(Bag(b).Has(StatusKind.FrostResist), Is.False);
            b.Cast("冷", 0);
            Assert.That(Bag(b).Has(StatusKind.Freeze), Is.True, "霜抗结束后可再冻");
        }

        [Test]
        public void RejectedFreeze_DoesNotRaiseStatusAppliedHook()
        {
            var b = Battle("冷", "冻");
            b.Cast("冷", 0);
            var seen = new List<StatusKind?>();
            b.AddHookListener(new RecordingListener(seen));
            b.Cast("冻", 0);
            Assert.That(seen.Contains(StatusKind.Freeze), Is.False);
        }

        [Test]
        public void Slows_FromDifferentCards_DoNotStack_RefreshToLongest()
        {
            var b = Battle("缓", "迟", "缓");
            b.Cast("迟", 0);                   // 3 回合
            b.Cast("缓", 0);                   // 1 回合:不缩短
            var slows = Bag(b).All.Where(s => s.Kind == StatusKind.SpeedModifier && s.Magnitude < 0).ToList();
            Assert.That(slows.Count, Is.EqualTo(1), "只留一条");
            Assert.That(slows[0].Magnitude, Is.EqualTo(-50), "不叠成 -100");
            Assert.That(slows[0].TurnsLeft, Is.EqualTo(3), "刷新不缩短");
        }

        [Test]
        public void FrostResistAndFreezeDuration_SurviveSnapshotRoundTrip()
        {
            var b = Battle("冻", "冻");
            b.Cast("冻", 0);
            b.EndTurn();
            b.EndTurn();
            var restored = BattleEngine.Restore(b.Capture(),
                FullGraph(),
                new BattleConfig { PlayerMaxHp = RebalanceFixture.BaseMaxHp, PlayerAttack = 100 }, null,
                new Dictionary<string, EnemyDef> { ["怔"] = RebalanceFixture.Mob(attack: 1) });
            Assert.That(restored.Enemies[0].Statuses.Find(StatusKind.FrostResist).TurnsLeft, Is.EqualTo(2));
        }

        // ---- 执行者核对补的两条 ----

        private static BattleEngine SummonBattle(bool resistFirst)
        {
            var b = new BattleEngine(RebalanceFixture.Graph(Frosty, new CharDef("木", Element.Wood)),
                new BattleConfig { DropTable = new[] { "木" }, PlayerMaxHp = 500 },
                new[] { "霜" }, Array.Empty<string>(),
                new[] { RebalanceFixture.Mob() }, seed: 1);
            if (resistFirst)
                b.Enemies[0].Statuses.Apply(new StatusEffect
                {
                    Kind = StatusKind.FrostResist, Polarity = StatusPolarity.Buff, TurnsLeft = 5,
                });
            b.Cast("霜");
            b.EndTurn();
            return b;
        }

        [Test]
        public void OnHitFreezeChance_RollsSameRandomCount_WhetherOrNotRejected()
        {
            var plain = SummonBattle(false);
            var resisted = SummonBattle(true);
            Assert.That(plain.Enemies[0].Statuses.Has(StatusKind.Freeze), Is.True, "对照组确实冻上了");
            Assert.That(resisted.Enemies[0].Statuses.Has(StatusKind.Freeze), Is.False, "霜抗组被拒");
            Assert.That(resisted.Capture().RandomState, Is.EqualTo(plain.Capture().RandomState),
                "先摇后拦:被拒也摇过一次,随机数流位置相同");
        }

        [Test]
        public void SummonTargeting_AvoidsFrostResistEnemy()
        {
            var b = new BattleEngine(RebalanceFixture.Graph(Frosty, new CharDef("木", Element.Wood)),
                new BattleConfig { DropTable = new[] { "木" }, PlayerMaxHp = 500 },
                new[] { "霜" }, Array.Empty<string>(),
                new[] { RebalanceFixture.Mob(), RebalanceFixture.Mob() }, seed: 1);
            b.Enemies[0].Statuses.Apply(new StatusEffect
            {
                Kind = StatusKind.FrostResist, Polarity = StatusPolarity.Buff, TurnsLeft = 5,
            });
            b.Cast("霜");
            b.EndTurn();
            Assert.That(b.Enemies[1].Statuses.Has(StatusKind.Freeze), Is.True, "该打没霜抗的那只");
        }
    }
}
