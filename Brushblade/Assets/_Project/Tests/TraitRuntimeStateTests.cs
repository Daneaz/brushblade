using System;
using System.Collections.Generic;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>特性运行时状态(D1 Task 2):独立随机流、次数阀、本回合出字数,以及它们的快照往返。</summary>
    public class TraitRuntimeStateTests
    {
        private static CharDef Hit() => new("击", Element.Metal,
            effects: new[] { new EffectDef(EffectKind.DamageSingle, 10) },
            attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 10) });

        private static BattleConfig Config() =>
            new BattleConfig { PlayerMaxHp = RebalanceFixture.BaseMaxHp, PlayerAttack = 100 };

        private static BattleEngine Battle(int seed = 7) =>
            new BattleEngine(RebalanceFixture.Graph(Hit()), Config(),
                new[] { "击", "击", "击" }, Array.Empty<string>(),
                new[] { RebalanceFixture.Mob(attack: 1) }, seed: seed);

        private static BattleEngine RestoreFrom(BattleSnapshot snap) =>
            BattleEngine.Restore(snap, RebalanceFixture.Graph(Hit()), Config(), null,
                new Dictionary<string, EnemyDef> { [RebalanceFixture.Mob().Id] = RebalanceFixture.Mob(attack: 1) });

        [Test]
        public void TraitRandom_IsSeparateStream()
        {
            var a = Battle();
            var b = Battle();
            b._traitRandom.Next(1000);
            a.Cast("击", 0);
            b.Cast("击", 0);
            Assert.That(b.Capture().RandomState, Is.EqualTo(a.Capture().RandomState), "主流不受特性流影响");
            Assert.That(b.Capture().TraitRandomState, Is.Not.EqualTo(a.Capture().TraitRandomState));
        }

        [Test]
        public void TraitRandom_IsNotUsedWithoutTraits()
        {
            var a = Battle();
            uint before = a.Capture().TraitRandomState;
            a.Cast("击", 0);
            a.EndTurn();
            Assert.That(a.Capture().TraitRandomState, Is.EqualTo(before));
        }

        [Test]
        public void TryUseTrait_PerTurnResetsOnStartTurn()
        {
            var b = Battle();
            Assert.That(b.TryUseTrait("k", 1, 0), Is.True);
            Assert.That(b.TryUseTrait("k", 1, 0), Is.False, "本回合已用");
            Assert.That(b.TryUseTrait("other", 1, 0), Is.True, "key 互不影响");
            b.EndTurn();
            Assert.That(b.TryUseTrait("k", 1, 0), Is.True, "新回合重置");
        }

        [Test]
        public void TryUseTrait_PerBattleNeverResets()
        {
            var b = Battle();
            Assert.That(b.TryUseTrait("k", 0, 1), Is.True);
            b.EndTurn();
            Assert.That(b.TryUseTrait("k", 0, 1), Is.False);
        }

        [Test]
        public void TryUseTrait_ZeroMeansUnlimited_AndFailedUseDoesNotConsume()
        {
            var b = Battle();
            for (int i = 0; i < 5; i++) Assert.That(b.TryUseTrait("k", 0, 0), Is.True);
            Assert.That(b.TryUseTrait("m", 2, 1), Is.True);
            Assert.That(b.TryUseTrait("m", 2, 1), Is.False, "本场上限先到");
            b.EndTurn();
            Assert.That(b.TryUseTrait("m", 2, 1), Is.False, "本场仍满");
        }

        [Test]
        public void TraitKey_IsStable()
        {
            Assert.That(BattleEngine.TraitKey("炎", TraitSlot.Lv3, TraitFace.Attack), Is.EqualTo("炎/3/Attack"));
        }

        [Test]
        public void CastsThisTurn_CountsAndResets()
        {
            var b = Battle();
            Assert.That(b.CastsThisTurn, Is.EqualTo(0));
            Assert.That(b.Cast("不存在"), Is.Not.EqualTo(BattleError.None));
            Assert.That(b.CastsThisTurn, Is.EqualTo(0), "失败不计");
            Assert.That(b.Cast("击", 0), Is.EqualTo(BattleError.None));
            Assert.That(b.Cast("击", 0), Is.EqualTo(BattleError.None));
            Assert.That(b.CastsThisTurn, Is.EqualTo(2));
            b.EndTurn();
            Assert.That(b.CastsThisTurn, Is.EqualTo(0));
        }

        [Test]
        public void Snapshot_RoundTrips_TraitRandomCountersAndCasts()
        {
            var b = Battle();
            b._traitRandom.Next(1000);
            b.TryUseTrait("a", 2, 3);
            b.TryUseTrait("a", 2, 3);
            b.TryUseTrait("b", 0, 1);
            b.Cast("击", 0);

            var meta = new MetaState
            {
                EndlessV2 = new EndlessSaveState
                {
                    Depth = 1, Seed = 1,
                    InProgress = new InProgressRun
                    {
                        FromDepth = 1, FirstTowerSegment = true,
                        Run = new RunSnapshot { Battle = b.Capture() },
                    },
                },
            };
            var reloaded = Data.SaveSerializer.FromJson(Data.SaveSerializer.ToJson(meta));
            var restored = RestoreFrom(reloaded.EndlessV2.InProgress.Run.Battle);

            Assert.That(restored.CastsThisTurn, Is.EqualTo(1));
            Assert.That(restored.TryUseTrait("a", 2, 3), Is.False, "本回合 a 已用 2 次");
            Assert.That(restored.TryUseTrait("b", 0, 1), Is.False, "本场 b 已用满");
            Assert.That(restored.TryUseTrait("c", 1, 1), Is.True);
            Assert.That(restored._traitRandom.Next(1000000), Is.EqualTo(b._traitRandom.Next(1000000)));
        }

        [Test]
        public void Snapshot_WithoutTraitFields_RebuildsStreamAndEmptyCounters()
        {
            var snap = Battle().Capture();
            snap.TraitRandomState = 0;       // 旧快照缺字段 = 默认值
            snap.TraitUsesThisTurn = null;
            snap.TraitUsesThisBattle = null;
            var restored = RestoreFrom(snap);
            Assert.That(restored.CastsThisTurn, Is.EqualTo(0));
            Assert.That(restored.TryUseTrait("x", 1, 1), Is.True);
            Assert.That(restored.Capture().TraitRandomState, Is.Not.EqualTo(0u));
        }
    }
}
