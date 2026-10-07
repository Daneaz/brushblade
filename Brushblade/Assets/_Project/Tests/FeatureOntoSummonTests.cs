using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>润 / 固落木灵(D2-0 Task 4,Ruling E2/E3/E4,spec §2.2):水的五行面落到活着的木灵 = 本场改水属性,
    /// 土的五行面落到活着的木灵 = 本场嘲讽;攻击面、落点是玩家都不触发;战后复原 / 剥离。</summary>
    public class FeatureOntoSummonTests
    {
        private static CharDef Wet() => new("润", Element.Water,
            effects: new[] { new EffectDef(EffectKind.HealSelf, 10) },
            attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 10) });

        private static CharDef Firm() => new("固", Element.Earth,
            effects: new[] { new EffectDef(EffectKind.Shield, 10), new EffectDef(EffectKind.DefenseBuff, 5) },
            attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 10) });

        private static CharDef Opener() => new("解", Element.Water, effects: new[] { new EffectDef(EffectKind.Unseal, 0) });

        private static CharDef Summoner() => new("垛", Element.Wood,
            effects: new[] { new EffectDef(EffectKind.Summon, 200, summonCount: 1, summonAttack: 0, summonChar: "木") });

        private static BattleEngine Battle(int seed = 1)
        {
            var defs = new[] { Wet(), Firm(), Opener(), Summoner(), new CharDef("木", Element.Wood) };
            return new BattleEngine(RebalanceFixture.Graph(defs),
                new BattleConfig { PlayerMaxHp = RebalanceFixture.BaseMaxHp, PlayerAttack = 100 },
                new[] { "润", "润", "固", "固", "解", "垛", "垛" },
                Array.Empty<string>(), new[] { RebalanceFixture.Mob(attack: 0) }, seed: seed);
        }

        private static int SlotOfFirstSummon(BattleEngine b) =>
            Array.FindIndex(b.Summons.ToArray(), s => s != null && s.Alive);

        [Test]
        public void Wet_OnSummon_ChangesElementToWater_RecordsBase_EmitsUnsealEvent()
        {
            var b = Battle();
            b.Cast("垛");
            int slot = SlotOfFirstSummon(b);
            Assert.That(b.Cast("润", -1, attackMode: false, allySlot: slot), Is.EqualTo(BattleError.None));
            Assert.That(b.Summons[slot].Element, Is.EqualTo(Element.Water));
            Assert.That(b.Summons[slot].BaseElement, Is.EqualTo(Element.Wood));
            Assert.That(b.LastEvents.Any(e => e.Kind == BattleEventKind.Unseal && e.TargetIndex == slot
                && e.Amount == (int)Element.Water), Is.True);
        }

        [Test]
        public void Wet_Twice_KeepsOriginalBase()
        {
            var b = Battle();
            b.Cast("垛");
            int slot = SlotOfFirstSummon(b);
            b.Cast("润", -1, attackMode: false, allySlot: slot);
            b.Cast("润", -1, attackMode: false, allySlot: slot);
            Assert.That(b.Summons[slot].BaseElement, Is.EqualTo(Element.Wood), "??= 不覆盖第一次的本来属性");
        }

        [Test]
        public void Wet_AttackFace_Or_OnPlayer_DoesNotChangeElement()
        {
            var b = Battle();
            b.Cast("垛");
            int slot = SlotOfFirstSummon(b);
            Assert.That(b.Cast("润", 0, attackMode: true), Is.EqualTo(BattleError.None));
            Assert.That(b.Cast("润", -1, attackMode: false, allySlot: Targeting.PlayerTarget), Is.EqualTo(BattleError.None));
            Assert.That(b.Summons[slot].Element, Is.EqualTo(Element.Wood));
            Assert.That(b.Summons[slot].BaseElement, Is.Null);
        }

        [Test]
        public void Firm_OnSummon_GivesTauntUntilEndOfBattle()
        {
            var b = Battle();
            b.Cast("垛");
            int slot = SlotOfFirstSummon(b);
            Assert.That(b.Cast("固", -1, attackMode: false, allySlot: slot), Is.EqualTo(BattleError.None));
            var taunt = b.Summons[slot].Statuses.Find(StatusKind.Taunt);
            Assert.That(taunt, Is.Not.Null);
            Assert.That(taunt.TurnsLeft, Is.EqualTo(-1));
            Assert.That(taunt.SourceId, Is.EqualTo("固"));
            Assert.That(b.Summons[slot].Shield + b.Summons[slot].EffectiveDefense, Is.GreaterThan(0), "固面本体照常落木灵");
            Assert.That(b.Summons[slot].Element, Is.EqualTo(Element.Wood), "土不改属性");
        }

        [Test]
        public void Firm_AttackFace_Or_OnPlayer_NoTaunt()
        {
            var b = Battle();
            b.Cast("垛");
            int slot = SlotOfFirstSummon(b);
            b.Cast("固", 0, attackMode: true);
            b.Cast("固", -1, attackMode: false, allySlot: Targeting.PlayerTarget);
            Assert.That(b.Summons[slot].Statuses.Has(StatusKind.Taunt), Is.False);
            Assert.That(b.PlayerStatuses.Has(StatusKind.Taunt), Is.False);
        }

        [Test]
        public void Wet_Then_Unseal_UnsealWins_BaseCleared()
        {
            var b = Battle();
            b.Cast("垛");
            int slot = SlotOfFirstSummon(b);
            b.Cast("润", -1, attackMode: false, allySlot: slot);
            Assert.That(b.Summons[slot].BaseElement, Is.Not.Null);
            b.Cast("解", -1, attackMode: false, allySlot: slot);
            Assert.That(b.Summons[slot].BaseElement, Is.Null, "解封清掉战后复原记号(E4)");
        }

        [Test]
        public void Unseal_Then_Wet_RecordsRerolledElementAsBase()
        {
            for (int seed = 0; seed < 40; seed++)
            {
                var b = Battle(seed);
                b.Cast("垛");
                int slot = SlotOfFirstSummon(b);
                b.Cast("解", -1, attackMode: false, allySlot: slot);
                var rerolled = b.Summons[slot].Element;
                b.Cast("润", -1, attackMode: false, allySlot: slot);
                Assert.That(b.Summons[slot].Element, Is.EqualTo(Element.Water));
                Assert.That(b.Summons[slot].BaseElement, Is.EqualTo(rerolled), $"seed {seed}");
            }
        }

        // ---- 跨场(RunEngine)----

        private static RecipeGraph RunGraph() => new(new[]
        {
            new CharDef("木", Element.Wood), Summoner(), Wet(), Firm(), Opener(),
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

        private static RunEngine NewRun() => new(RunGraph(), RunCfg(), BattleCfg(),
            startingLibrary: new[] { "垛", "润", "固", "解", "焚" }, startingPool: Array.Empty<string>(), seed: 7);

        private static void Win(RunEngine run)
        {
            Assert.That(run.Battle.Cast("焚"), Is.EqualTo(BattleError.None));
            Assert.That(run.Battle.Phase, Is.EqualTo(BattlePhase.Won));
            run.AdvanceAfterBattle();
        }

        [Test]
        public void Run_Carry_RestoresElement_StripsTaunt()
        {
            var run = NewRun();
            Assert.That(run.Battle.Cast("垛"), Is.EqualTo(BattleError.None));
            int slot = SlotOfFirstSummon(run.Battle);
            Assert.That(run.Battle.Cast("润", -1, attackMode: false, allySlot: slot), Is.EqualTo(BattleError.None));
            Assert.That(run.Battle.Cast("固", -1, attackMode: false, allySlot: slot), Is.EqualTo(BattleError.None));
            Win(run);
            var carried = run.CarriedSummons.Single();
            Assert.That(carried.Element, Is.EqualTo(Element.Wood));
            Assert.That(carried.BaseElement, Is.Null);
            Assert.That(carried.Statuses.Any(s => s.Kind == StatusKind.Taunt), Is.False);
        }

        [Test]
        public void Run_Wet_ThenUnseal_KeepsUnsealedElement()
        {
            var run = NewRun();
            Assert.That(run.Battle.Cast("垛"), Is.EqualTo(BattleError.None));
            int slot = SlotOfFirstSummon(run.Battle);
            Assert.That(run.Battle.Cast("润", -1, attackMode: false, allySlot: slot), Is.EqualTo(BattleError.None));
            Assert.That(run.Battle.Cast("解", -1, attackMode: false, allySlot: slot), Is.EqualTo(BattleError.None));
            var unsealed = run.Battle.Summons[slot].Element;
            Win(run);
            Assert.That(run.CarriedSummons.Single().Element, Is.EqualTo(unsealed));
        }

        [Test]
        public void Run_Unseal_ThenWet_RestoresToUnsealedElement()
        {
            var run = NewRun();
            Assert.That(run.Battle.Cast("垛"), Is.EqualTo(BattleError.None));
            int slot = SlotOfFirstSummon(run.Battle);
            Assert.That(run.Battle.Cast("解", -1, attackMode: false, allySlot: slot), Is.EqualTo(BattleError.None));
            var unsealed = run.Battle.Summons[slot].Element;
            Assert.That(run.Battle.Cast("润", -1, attackMode: false, allySlot: slot), Is.EqualTo(BattleError.None));
            Win(run);
            Assert.That(run.CarriedSummons.Single().Element, Is.EqualTo(unsealed));
        }

        [Test]
        public void Snapshot_BaseElement_RoundTripsThroughSaveSerializer_AndStillRestoresAfterBattle()
        {
            var run = NewRun();
            Assert.That(run.Battle.Cast("垛"), Is.EqualTo(BattleError.None));
            int slot = SlotOfFirstSummon(run.Battle);
            Assert.That(run.Battle.Cast("润", -1, attackMode: false, allySlot: slot), Is.EqualTo(BattleError.None));

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
            Assert.That(snap.Battle.Summons.Single().BaseElement, Is.EqualTo(Element.Wood));

            var resumed = RunEngine.Restore(snap, RunGraph(), RunCfg(), BattleCfg(), null);
            Assert.That(resumed.Battle.Summons[slot].Element, Is.EqualTo(Element.Water));
            Assert.That(resumed.Battle.Summons[slot].BaseElement, Is.EqualTo(Element.Wood));
            Win(resumed);
            Assert.That(resumed.CarriedSummons.Single().Element, Is.EqualTo(Element.Wood));
        }
    }
}
