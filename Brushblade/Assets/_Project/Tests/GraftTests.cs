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
        public void Graft_AtLv5_UsesLv3EnhancedPassive_ScaledByCardLevel()
        {
            // 藤 Lv1 缠绕 20%,Lv3 强化为 40%(E8:Summon 0 + 被动,合并只覆盖非缺省字段)。
            // 卡 Lv5 嫁接:Lv3 已解锁 → 40,再吃等级缩放 = min(100, ScaleByCardLevel(40, 5))
            var enhance = new TraitDef(TraitSlot.Lv3, TraitFace.Feature, TraitForm.Active, TraitSlot.Lv1, "强化",
                new[] { new EffectDef(EffectKind.Summon, 0, passive: new SummonPassive { OnHitFreezeChance = 40 }) });
            var vine = new CharDef("藤", Element.Wood,
                effects: new[] { new EffectDef(EffectKind.Summon, 100, summonCount: 1, summonAttack: 10, summonChar: "木", passive: Vine) },
                traits: new[] { enhance });
            var b = new BattleEngine(RebalanceFixture.Graph(vine),
                new BattleConfig { PlayerMaxHp = RebalanceFixture.BaseMaxHp, PlayerAttack = 100 },
                new[] { "藤" }, Array.Empty<string>(), new[] { RebalanceFixture.Mob(attack: 0) }, seed: 1,
                startingSummons: new[] { Wood(0, 30, 100, Thorny) }, cardLevels: new Dictionary<string, int> { ["藤"] = 5 });
            Assert.That(b.Cast("藤", -1, attackMode: false, allySlot: 0), Is.EqualTo(BattleError.None));
            int expected = Math.Min(100, MetaRules.ScaleByCardLevel(40, 5));
            Assert.That(expected, Is.GreaterThan(40), "夹具自检:Lv5 缩放确实放大");
            Assert.That(b.Summons[0].Passive.OnHitFreezeChance, Is.EqualTo(expected));
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
            Assert.That(run.Battle.Cast("荆"), Is.EqualTo(BattleError.None));
            int slot = FirstSlot(run.Battle);
            Assert.That(run.Battle.Cast("藤", -1, attackMode: false, allySlot: slot), Is.EqualTo(BattleError.None));
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
            Assert.That(run.Battle.Cast("素"), Is.EqualTo(BattleError.None));
            int slot = FirstSlot(run.Battle);
            Assert.That(run.Battle.Cast("藤", -1, attackMode: false, allySlot: slot), Is.EqualTo(BattleError.None));
            Win(run);
            var carried = run.CarriedSummons.Single();
            Assert.That(carried.Passive?.OnHitFreezeChance ?? 0, Is.EqualTo(0));
            Assert.That(carried.BasePassive, Is.Null);
        }

        [Test]
        public void Snapshot_BasePassive_RoundTripsThroughSaveSerializer_AndStillRestoresAfterBattle()
        {
            var run = NewRun("荆");
            Assert.That(run.Battle.Cast("荆"), Is.EqualTo(BattleError.None));
            int slot = FirstSlot(run.Battle);
            Assert.That(run.Battle.Cast("藤", -1, attackMode: false, allySlot: slot), Is.EqualTo(BattleError.None));

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

        [Test]
        public void Run_Carry_StackedGraftWetFirmArmorEndure_RestoresAllAndKeepsEntryArmorAndShield()
        {
            // 同一只木灵同场叠:保命 + 入场护甲(Armor 12)+ 嫁接 + 润(改水)+ 固(嘲讽 + 盾)+ 铠(格挡)
            var basePassive = new SummonPassive { Thorns = 7, Armor = 12 };
            var graph = new RecipeGraph(new[]
            {
                new CharDef("木", Element.Wood),
                new CharDef("扎", Element.Wood, effects: new[]
                {
                    new EffectDef(EffectKind.Summon, 100, summonCount: 1, summonAttack: 10, summonChar: "木", passive: basePassive),
                    new EffectDef(EffectKind.Endure, 0, pick: EffectPick.SummonedThisCast),
                }),
                Graftor(),
                new CharDef("润", Element.Water, effects: new[] { new EffectDef(EffectKind.HealSelf, 10) }),
                new CharDef("固", Element.Earth, effects: new[] { new EffectDef(EffectKind.Shield, 40) }),
                new CharDef("铠", Element.Metal, effects: new[] { new EffectDef(EffectKind.Block, 1) }),
                new CharDef("焚", Element.Fire,
                    effects: new[] { new EffectDef(EffectKind.DamageSingle, 99, shape: TargetArea.All) }),
            });
            var runCfg = new RunConfig
            {
                Encounters = new[] { new[] { RebalanceFixture.Mob(hp: 10) }, new[] { RebalanceFixture.Mob(hp: 10) } },
                RewardPool = new[] { "焚" }, FromDepth = 31,
            };
            var battleCfg = new BattleConfig { DropTable = new[] { "木" }, ApPerTurn = 10 };
            var run = new RunEngine(graph, runCfg, battleCfg,
                startingLibrary: new[] { "扎", "藤", "润", "固", "铠", "焚" }, startingPool: Array.Empty<string>(), seed: 7);

            Assert.That(run.Battle.Cast("扎"), Is.EqualTo(BattleError.None));
            int slot = FirstSlot(run.Battle);
            foreach (var id in new[] { "藤", "润", "固", "铠" })
                Assert.That(run.Battle.Cast(id, -1, attackMode: false, allySlot: slot), Is.EqualTo(BattleError.None), id);
            var live = run.Battle.Summons[slot];
            Assert.That(live.Element, Is.EqualTo(Element.Water), "夹具自检:润生效");
            Assert.That(live.Statuses.Has(StatusKind.Taunt) && live.Statuses.Has(StatusKind.Block)
                && live.Statuses.Has(StatusKind.Endure), Is.True, "夹具自检:本场三态都挂上了");
            Assert.That(live.Passive.Armor, Is.EqualTo(0), "嫁接后被动不带护甲");
            int shieldBefore = live.Shield;
            Assert.That(shieldBefore, Is.GreaterThan(0));

            Win(run);
            var carried = run.CarriedSummons.Single();
            Assert.That(carried.Element, Is.EqualTo(Element.Wood));
            Assert.That(carried.BaseElement, Is.Null);
            Assert.That(carried.Passive.Thorns, Is.EqualTo(7));
            Assert.That(carried.Passive.Armor, Is.EqualTo(12));
            Assert.That(carried.Passive.OnHitFreezeChance, Is.EqualTo(0));
            Assert.That(carried.BasePassive, Is.Null);
            foreach (var kind in new[] { StatusKind.Taunt, StatusKind.Block, StatusKind.Endure })
                Assert.That(carried.Statuses.Any(st => st.Kind == kind), Is.False, kind.ToString());
            Assert.That(carried.Statuses.Any(st => st.Kind == StatusKind.DefenseBuff && st.TurnsLeft < 0), Is.True,
                "入场护甲(DefenseBuff,TurnsLeft −1)保留");
            Assert.That(carried.Shield, Is.EqualTo(shieldBefore * battleCfg.ShieldCarryPercent / 100), "护盾按比例保留");
        }
    }
}
