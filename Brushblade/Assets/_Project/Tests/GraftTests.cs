using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>嫁接(D2-0 Task 5,Ruling E5,spec §2.2):木的生面带 allySlot 指向活木灵 = 回满生命并换本命(本场),
    /// 不召新木灵、召唤位满也能用;指向尸体 / 空格 = InvalidTarget;战后复原本命。</summary>
    public class GraftTests
    {
        private static SummonPassive Vine => new() { OnHitFreezeChance = 20, OnHitFreezeTurns = 1 };
        private static SummonPassive Thorny => new() { Thorns = 50 };

        private static CharDef Graftor() => new("藤", Element.Wood,
            effects: new[] { new EffectDef(EffectKind.Summon, 100, summonCount: 1, summonAttack: 10, summonChar: "木", passive: Vine) });

        private static CharDef Sprouter() => new("芽", Element.Wood,
            effects: new[]
            {
                new EffectDef(EffectKind.Summon, 100, summonCount: 1, summonAttack: 10, summonChar: "木", passive: Vine),
                new EffectDef(EffectKind.SummonSapling, 20, summonCount: 1),
            });

        private static SummonSnapshot Wood(int slot, int hp, int maxHp = 100, SummonPassive passive = null) => new()
        {
            Slot = slot, Char = "木", Element = Element.Wood, Hp = hp, MaxHp = maxHp, Attack = 10, Speed = 100,
            Passive = passive,
        };

        private static BattleEngine Battle(IReadOnlyList<SummonSnapshot> summons, params CharDef[] defs) =>
            new(RebalanceFixture.Graph(defs),
                new BattleConfig { PlayerMaxHp = RebalanceFixture.BaseMaxHp, PlayerAttack = 100 },
                defs.SelectMany(d => new[] { d.Id, d.Id }).ToArray(), Array.Empty<string>(),
                new[] { RebalanceFixture.Mob(attack: 0) }, seed: 1, startingSummons: summons);

        [Test]
        public void Graft_RefillsHp_SwapsPassive_NoNewSummon_EmitsEvent()
        {
            var b = Battle(new[] { Wood(0, 30, 100, Thorny) }, Graftor());
            Assert.That(b.Cast("藤", -1, attackMode: false, allySlot: 0), Is.EqualTo(BattleError.None));
            var s = b.Summons[0];
            Assert.That(s.Hp, Is.EqualTo(s.MaxHp));
            Assert.That(s.Passive.OnHitFreezeChance, Is.EqualTo(20));
            Assert.That(s.Passive.Thorns, Is.EqualTo(0));
            Assert.That(s.BasePassive.Thorns, Is.EqualTo(50));
            Assert.That(b.Summons.Count(x => x != null && x.Alive), Is.EqualTo(1), "木灵只数不变");
            Assert.That(b.LastEvents.Any(e => e.Kind == BattleEventKind.Graft && e.SecondIndex == 0), Is.True);
            Assert.That(b.LastEvents.Any(e => e.Kind == BattleEventKind.Summon), Is.False);
        }

        [Test]
        public void Graft_Twice_KeepsFirstBasePassive()
        {
            var b = Battle(new[] { Wood(0, 30, 100, Thorny) }, Graftor());
            b.Cast("藤", -1, attackMode: false, allySlot: 0);
            b.Cast("藤", -1, attackMode: false, allySlot: 0);
            Assert.That(b.Summons[0].BasePassive.Thorns, Is.EqualTo(50));
        }

        [Test]
        public void Graft_WhenSummonRowFull_SucceedsWithoutReplaceSummon()
        {
            int capacity = Battle(null, Graftor()).SummonCapacity;
            var all = Enumerable.Range(0, capacity).Select(i => Wood(i, 40, 100, Thorny)).ToArray();
            var b = Battle(all, Graftor());
            Assert.That(b.Cast("藤", -1, attackMode: false, allySlot: 1), Is.EqualTo(BattleError.None));
            Assert.That(b.Summons.Count(x => x != null && x.Alive), Is.EqualTo(capacity));
            Assert.That(b.Summons[1].Hp, Is.EqualTo(100));
            Assert.That(b.Summons[0].Hp, Is.EqualTo(40), "别的木灵不受影响");
        }

        [Test]
        public void Graft_OnCorpseOrEmptySlot_InvalidTarget_NoApNoConsume()
        {
            var b = Battle(new[] { Wood(0, 50), Wood(1, 0) }, Graftor());
            int ap = b.Ap;
            int library = b.Library.Count;
            Assert.That(b.Cast("藤", -1, attackMode: false, allySlot: 1), Is.EqualTo(BattleError.InvalidTarget), "尸体格");
            Assert.That(b.Cast("藤", -1, attackMode: false, allySlot: 2), Is.EqualTo(BattleError.InvalidTarget), "空格");
            Assert.That(b.Ap, Is.EqualTo(ap));
            Assert.That(b.Library.Count, Is.EqualTo(library));
        }

        [Test]
        public void NoAllySlot_IsOrdinarySummon_NotGraft()
        {
            var b = Battle(new[] { Wood(0, 30, 100, Thorny) }, Graftor());
            Assert.That(b.Cast("藤"), Is.EqualTo(BattleError.None));
            Assert.That(b.Summons.Count(x => x != null && x.Alive), Is.EqualTo(2));
            Assert.That(b.Summons[0].Hp, Is.EqualTo(30));
            Assert.That(b.Summons[0].BasePassive, Is.Null);
        }

        [Test]
        public void Graft_WithSapling_SpawnsSaplingFromGraftTarget()
        {
            var b = Battle(new[] { Wood(0, 30, 200, Thorny) }, Sprouter());
            Assert.That(b.Cast("芽", -1, attackMode: false, allySlot: 0), Is.EqualTo(BattleError.None));
            var sapling = b.Summons.Single(x => x != null && x.Alive && x.Char == "苗");
            Assert.That(sapling.MaxHp, Is.EqualTo(40), "20% × 被嫁接者血上限 200");
            Assert.That(sapling.Attack, Is.EqualTo(2), "20% × 被嫁接者攻 10");
            Assert.That(b.Summons[0].Hp, Is.EqualTo(200));
        }

        // ---- 跨场(RunEngine)----

        private static RecipeGraph RunGraph() => new(new[]
        {
            new CharDef("木", Element.Wood), Graftor(),
            new CharDef("荆", Element.Wood,
                effects: new[] { new EffectDef(EffectKind.Summon, 100, summonCount: 1, summonAttack: 10, summonChar: "木", passive: Thorny) }),
            new CharDef("素", Element.Wood,
                effects: new[] { new EffectDef(EffectKind.Summon, 100, summonCount: 1, summonAttack: 10, summonChar: "木") }),
            new CharDef("焚", Element.Fire,
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 99, shape: TargetArea.All) }),
        });

        private static RunConfig RunCfg() => new()
        {
            Encounters = new[] { new[] { RebalanceFixture.Mob(hp: 10) }, new[] { RebalanceFixture.Mob(hp: 10) } },
            RewardPool = new[] { "焚" },
            FromDepth = 31,
        };

        private static BattleConfig BattleCfg() => new() { DropTable = new[] { "木" }, ApPerTurn = 10 };

        private static RunEngine NewRun(string summoner) => new(RunGraph(), RunCfg(), BattleCfg(),
            startingLibrary: new[] { summoner, "藤", "焚" }, startingPool: Array.Empty<string>(), seed: 7);

        private static void Win(RunEngine run)
        {
            Assert.That(run.Battle.Cast("焚"), Is.EqualTo(BattleError.None));
            Assert.That(run.Battle.Phase, Is.EqualTo(BattlePhase.Won));
            run.AdvanceAfterBattle();
        }

        private static int FirstSlot(BattleEngine b) =>
            Array.FindIndex(b.Summons.ToArray(), s => s != null && s.Alive);

        [Test]
        public void Run_Carry_RestoresOriginalPassive()
        {
            var run = NewRun("荆");
            run.Battle.Cast("荆");
            int slot = FirstSlot(run.Battle);
            run.Battle.Cast("藤", -1, attackMode: false, allySlot: slot);
            Assert.That(run.Battle.Summons[slot].Passive.OnHitFreezeChance, Is.EqualTo(20));
            Win(run);
            var carried = run.CarriedSummons.Single();
            Assert.That(carried.Passive.Thorns, Is.EqualTo(50));
            Assert.That(carried.Passive.OnHitFreezeChance, Is.EqualTo(0));
            Assert.That(carried.BasePassive, Is.Null);
        }

        [Test]
        public void Run_Carry_GraftOntoPassivelessSummon_RestoresToNoEffectivePassive()
        {
            var run = NewRun("素");
            run.Battle.Cast("素");
            int slot = FirstSlot(run.Battle);
            run.Battle.Cast("藤", -1, attackMode: false, allySlot: slot);
            Win(run);
            var carried = run.CarriedSummons.Single();
            Assert.That(carried.Passive?.OnHitFreezeChance ?? 0, Is.EqualTo(0));
            Assert.That(carried.BasePassive, Is.Null);
        }

        [Test]
        public void Snapshot_BasePassive_RoundTripsThroughSaveSerializer_AndStillRestoresAfterBattle()
        {
            var run = NewRun("荆");
            run.Battle.Cast("荆");
            int slot = FirstSlot(run.Battle);
            run.Battle.Cast("藤", -1, attackMode: false, allySlot: slot);

            var meta = new MetaState
            {
                EndlessV2 = new EndlessSaveState
                {
                    Depth = 31, Seed = 7,
                    InProgress = new InProgressRun { FromDepth = 31, Run = run.Capture() },
                },
            };
            var reloaded = Data.SaveSerializer.FromJson(Data.SaveSerializer.ToJson(meta));
            var snap = reloaded.EndlessV2.InProgress.Run;
            Assert.That(snap.Battle.Summons.Single().BasePassive.Thorns, Is.EqualTo(50));

            var resumed = RunEngine.Restore(snap, RunGraph(), RunCfg(), BattleCfg(), null);
            Assert.That(resumed.Battle.Summons[slot].Passive.OnHitFreezeChance, Is.EqualTo(20));
            Win(resumed);
            Assert.That(resumed.CarriedSummons.Single().Passive.Thorns, Is.EqualTo(50));
        }
    }
}
