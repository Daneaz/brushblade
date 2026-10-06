using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using Brushblade.Data;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>嘲讽状态(D2-0 Task 2,spec §3.1 / Ruling E3 / E11)与木灵战后剥离「本场」状态。</summary>
    public class TauntTests
    {
        private const int FrontRow = 3;

        private static StatusEffect Taunt(int turns = -1) => new()
        {
            Kind = StatusKind.Taunt, Polarity = StatusPolarity.Buff, Magnitude = 0, TurnsLeft = turns, SourceId = "测",
        };

        private static SummonState[] LineWithStatusTaunt(int[] aliveSlots, params int[] tauntSlots)
        {
            var slots = new SummonState[6];
            foreach (int s in aliveSlots)
            {
                slots[s] = new SummonState($"木{s}", Element.Wood, 100, 10);
                if (Array.IndexOf(tauntSlots, s) >= 0) slots[s].Statuses.Apply(Taunt());
            }
            return slots;
        }

        // ---------------- 木灵身上的嘲讽状态 ----------------

        [Test]
        public void Melee_OnlyHitsStatusTaunter_InsideTheFrontRowSegment()
        {
            var line = LineWithStatusTaunt(new[] { 1, 2, 4 }, 2);
            var random = new GameRandom(5);
            for (int i = 0; i < 100; i++)
                Assert.That(Targeting.PickAllyTarget(AttackRange.Melee, AttackFocus.Default,
                    line, FrontRow, random), Is.EqualTo(2));
        }

        [Test]
        public void Melee_BackRowStatusTaunter_DoesNotPullWhileFrontRowAlive()
        {
            // 站位先于嘲讽:后排的嘲讽者要等前排清空
            var line = LineWithStatusTaunt(new[] { 1, 4 }, 4);
            Assert.That(Targeting.PickAllyTarget(AttackRange.Melee, AttackFocus.Default,
                line, FrontRow, new GameRandom(1)), Is.EqualTo(1));
        }

        [Test]
        public void Ranged_StatusTaunter_PullsEvenFocusPlayer()
        {
            var line = LineWithStatusTaunt(new[] { 0, 5 }, 5);
            Assert.That(Targeting.PickAllyTarget(AttackRange.Ranged, AttackFocus.Player,
                line, FrontRow, new GameRandom(1)), Is.EqualTo(5));
        }

        // ---------------- 玩家嘲讽 ----------------

        [Test]
        public void PlayerTaunting_ReturnsPlayerTarget_WithoutDrawingRandom()
        {
            var line = LineWithStatusTaunt(new[] { 0, 1, 2 }, 1);   // 木灵嘲讽也压不过玩家嘲讽
            var random = new GameRandom(9);
            uint before = random.State;
            foreach (var range in new[] { AttackRange.Melee, AttackRange.Ranged })
                Assert.That(Targeting.PickAllyTarget(range, AttackFocus.Default, line, FrontRow, random,
                    playerTaunting: true), Is.EqualTo(Targeting.PlayerTarget));
            Assert.That(random.State, Is.EqualTo(before), "不摇随机数");
        }

        [Test]
        public void PlayerTauntingFalse_IsIdenticalToOldSignature()
        {
            var line = LineWithStatusTaunt(new[] { 0, 1, 2 });
            var a = new GameRandom(3);
            var b = new GameRandom(3);
            for (int i = 0; i < 50; i++)
                Assert.That(Targeting.PickAllyTarget(AttackRange.Ranged, AttackFocus.Default, line, FrontRow, a),
                    Is.EqualTo(Targeting.PickAllyTarget(AttackRange.Ranged, AttackFocus.Default, line, FrontRow, b, false)));
        }

        // ---------------- 引擎夹具 ----------------

        private static BattleConfig Config => new() { PlayerMaxHp = RebalanceFixture.BaseMaxHp, PlayerAttack = 100 };

        private static BattleEngine Battle(CharDef[] defs, params EnemyDef[] enemies) =>
            new(RebalanceFixture.Graph(defs), Config,
                defs.SelectMany(d => new[] { d.Id, d.Id, d.Id }).ToArray(), Array.Empty<string>(),
                enemies.Length > 0 ? enemies : new[] { RebalanceFixture.Mob() }, seed: 1);

        private static CharDef Def(string id, params EffectDef[] effects) => new(id, Element.Heart, effects: effects);

        [Test]
        public void EffectTaunt_SelfValue1_PlayerGetsOneTurn_ExpiresNextPlayerTurn()
        {
            var d = Def("对", new EffectDef(EffectKind.Taunt, 1, pick: EffectPick.Self));
            var b = Battle(new[] { d });
            Assert.That(b.Cast("对"), Is.EqualTo(BattleError.None));
            var s = b.PlayerStatuses.Find(StatusKind.Taunt);
            Assert.That(s, Is.Not.Null);
            Assert.That(s.TurnsLeft, Is.EqualTo(1));
            Assert.That(s.Polarity, Is.EqualTo(StatusPolarity.Buff));
            Assert.That(s.SourceId, Is.EqualTo("对"));
            Assert.That(BattleEngine.NeedsAllyTarget(d), Is.False, "落点由选择器给出,不选友方");
            b.EndTurn();
            Assert.That(b.PlayerStatuses.Has(StatusKind.Taunt), Is.False, "玩家下一回合开始后消失");
        }

        [Test]
        public void EffectTaunt_SelfValue0_IsBattleLong()
        {
            var d = Def("对", new EffectDef(EffectKind.Taunt, 0, pick: EffectPick.Self));
            var b = Battle(new[] { d });
            b.Cast("对");
            Assert.That(b.PlayerStatuses.Find(StatusKind.Taunt).TurnsLeft, Is.EqualTo(-1));
            b.EndTurn();
            b.EndTurn();
            Assert.That(b.PlayerStatuses.Has(StatusKind.Taunt), Is.True);
        }

        [Test]
        public void PlayerTaunt_MakesEnemyMeleeHitThePlayer_EvenWithSummonOutThere()
        {
            var d = Def("对",
                new EffectDef(EffectKind.Summon, 1000, summonCount: 1, summonChar: "木"),
                new EffectDef(EffectKind.Taunt, 0, pick: EffectPick.Self));
            var b = Battle(new[] { d }, RebalanceFixture.Mob(attack: 10));
            b.Cast("对");
            int slot = Array.FindIndex(b.Summons.ToArray(), s => s != null);
            int summonHp = b.Summons[slot].Hp;
            int hp = b.PlayerHp;
            b.EndTurn();
            Assert.That(b.PlayerHp, Is.LessThan(hp), "敌人打了玩家");
            Assert.That(b.Summons[slot].Hp, Is.EqualTo(summonHp), "木灵没挨打");
        }

        [Test]
        public void EffectTaunt_SummonedThisCast_AttachesToSummon_NoTurnsMeansBattleLong()
        {
            var d = Def("固",
                new EffectDef(EffectKind.Summon, 10, summonCount: 1, summonChar: "木"),
                new EffectDef(EffectKind.Taunt, 0, pick: EffectPick.SummonedThisCast));
            var b = Battle(new[] { d });
            b.Cast("固");
            int slot = Array.FindIndex(b.Summons.ToArray(), s => s != null);
            var s = b.Summons[slot].Statuses.Find(StatusKind.Taunt);
            Assert.That(s, Is.Not.Null);
            Assert.That(s.TurnsLeft, Is.EqualTo(-1));
            Assert.That(b.PlayerStatuses.Has(StatusKind.Taunt), Is.False);
        }

        [Test]
        public void EffectTaunt_SummonTaunt_TicksOnSummonsOwnBeat()
        {
            var d = Def("固",
                new EffectDef(EffectKind.Summon, 10, summonCount: 1, summonChar: "木"),
                new EffectDef(EffectKind.Taunt, 1, pick: EffectPick.SummonedThisCast));
            var b = Battle(new[] { d });
            b.Cast("固");
            int slot = Array.FindIndex(b.Summons.ToArray(), s => s != null);
            Assert.That(b.Summons[slot].Statuses.Find(StatusKind.Taunt).TurnsLeft, Is.EqualTo(1));
            for (int i = 0; i < 3 && b.Summons[slot].Statuses.Has(StatusKind.Taunt); i++) b.EndTurn();
            Assert.That(b.Summons[slot].Statuses.Has(StatusKind.Taunt), Is.False, "木灵自己行动一拍后到期");
        }

        [Test]
        public void EffectTaunt_AllSummons_HitsEveryAliveSummon()
        {
            var many = Def("群",
                new EffectDef(EffectKind.Summon, 10, summonCount: 2, summonChar: "木"),
                new EffectDef(EffectKind.Taunt, 0, pick: EffectPick.AllSummons));
            var b = Battle(new[] { many });
            b.Cast("群");
            var alive = b.Summons.Where(s => s != null && s.Alive).ToList();
            Assert.That(alive.Count, Is.EqualTo(2));
            Assert.That(alive.All(s => s.Statuses.Has(StatusKind.Taunt)), Is.True);
        }

        [Test]
        public void EffectTaunt_SameSourceRefreshes_NotStacks()
        {
            var d = Def("对", new EffectDef(EffectKind.Taunt, 2, pick: EffectPick.Self));
            var b = Battle(new[] { d });
            b.Cast("对");
            b.Cast("对");
            Assert.That(b.PlayerStatuses.All.Count(s => s.Kind == StatusKind.Taunt), Is.EqualTo(1));
        }

        // ---------------- 快照 ----------------

        [Test]
        public void TauntStatus_SurvivesSnapshotRoundTrip()
        {
            var d = Def("全",
                new EffectDef(EffectKind.Taunt, 2, pick: EffectPick.Self),
                new EffectDef(EffectKind.Summon, 10, summonCount: 1, summonChar: "木"),
                new EffectDef(EffectKind.Taunt, 0, pick: EffectPick.SummonedThisCast));
            var b = Battle(new[] { d });
            b.Cast("全");
            int slot = Array.FindIndex(b.Summons.ToArray(), s => s != null);
            var restored = BattleEngine.Restore(b.Capture(), RebalanceFixture.Graph(d), Config, null,
                new Dictionary<string, EnemyDef> { ["怔"] = RebalanceFixture.Mob() });
            Assert.That(restored.PlayerStatuses.Find(StatusKind.Taunt).TurnsLeft, Is.EqualTo(2));
            Assert.That(restored.Summons[slot].Statuses.Find(StatusKind.Taunt).TurnsLeft, Is.EqualTo(-1));
        }

        // ---------------- 战后剥离「本场」状态 ----------------

        [Test]
        public void IsBattleScoped_ByKind_NotByTurnsLeft()
        {
            foreach (var k in new[] { StatusKind.Taunt, StatusKind.Block, StatusKind.Endure,
                         StatusKind.DamageCut, StatusKind.CounterBoost })
                Assert.That(StatusRules.IsBattleScoped(k), Is.True, k.ToString());
            Assert.That(StatusRules.IsBattleScoped(StatusKind.DefenseBuff), Is.False, "入场护甲跨场保留");
            Assert.That(StatusRules.IsBattleScoped(StatusKind.AttackBuff), Is.False);
        }

        [Test]
        public void Run_StripsBattleScopedStatusesFromCarriedSummons_KeepsEntryArmor()
        {
            var graph = new RecipeGraph(new[]
            {
                new CharDef("木", Element.Wood),
                new CharDef("固", Element.Wood,
                    effects: new[]
                    {
                        new EffectDef(EffectKind.Summon, 50, summonCount: 1, summonChar: "木"),
                        new EffectDef(EffectKind.Taunt, 0, pick: EffectPick.SummonedThisCast),
                        new EffectDef(EffectKind.Endure, 0, pick: EffectPick.SummonedThisCast),
                    }),
                new CharDef("焚", Element.Fire,
                    effects: new[] { new EffectDef(EffectKind.DamageSingle, 99, shape: TargetArea.All) }),
            });
            var run = new RunEngine(graph,
                new RunConfig
                {
                    Encounters = new[] { new[] { RebalanceFixture.Mob(hp: 10) }, new[] { RebalanceFixture.Mob(hp: 10) } },
                    RewardPool = new[] { "焚" },
                    FromDepth = 31,
                },
                new BattleConfig { DropTable = new[] { "木" } },
                startingLibrary: new[] { "固", "焚" }, startingPool: Array.Empty<string>(), seed: 7);
            Assert.That(run.Battle.Cast("固"), Is.EqualTo(BattleError.None));
            int slot = Array.FindIndex(run.Battle.Summons.ToArray(), s => s != null);
            Assert.That(run.Battle.Summons[slot].Statuses.Has(StatusKind.Taunt), Is.True);
            run.Battle.Summons[slot].Statuses.Apply(new StatusEffect
            {
                Kind = StatusKind.DefenseBuff, Polarity = StatusPolarity.Buff, Magnitude = 12, TurnsLeft = -1, SourceId = "入场",
            });
            Assert.That(run.Battle.Cast("焚"), Is.EqualTo(BattleError.None));
            Assert.That(run.Battle.Phase, Is.EqualTo(BattlePhase.Won));

            run.AdvanceAfterBattle();
            Assert.That(run.CarriedSummons.Count, Is.EqualTo(1));
            var kinds = run.CarriedSummons[0].Statuses.Select(s => s.Kind).ToList();
            Assert.That(kinds.Contains(StatusKind.Taunt), Is.False, "本场嘲讽战后剥离");
            Assert.That(kinds.Contains(StatusKind.Endure), Is.False, "保命随召唤物跨场的前置项一并解决");
            Assert.That(kinds.Contains(StatusKind.DefenseBuff), Is.True, "入场护甲(TurnsLeft = -1)仍在");
        }

        // ---------------- 选择器与 ConfigLoader ----------------

        [Test]
        public void PickRules_Taunt_OnlySelfSummonedThisCastAllSummons()
        {
            Assert.That(EffectPickRules.Allows(EffectKind.Taunt, EffectPick.Self), Is.True);
            Assert.That(EffectPickRules.Allows(EffectKind.Taunt, EffectPick.SummonedThisCast), Is.True);
            Assert.That(EffectPickRules.Allows(EffectKind.Taunt, EffectPick.AllSummons), Is.True);
            Assert.That(EffectPickRules.Allows(EffectKind.Taunt, EffectPick.Primary), Is.False);
            Assert.That(EffectPickRules.Allows(EffectKind.Taunt, EffectPick.All), Is.False);
            Assert.That(EffectPickRules.Allows(EffectKind.Taunt, EffectPick.Random), Is.False);
            Assert.That(EffectPickRules.Allows(EffectKind.Freeze, EffectPick.AllSummons), Is.False);
            Assert.That(EffectPickRules.Allows(EffectKind.Endure, EffectPick.AllSummons), Is.False);
        }

        [Test]
        public void Config_TauntWithAllowedPicks_IsAccepted_OthersRejected()
        {
            foreach (var pick in new[] { "Self", "SummonedThisCast", "AllSummons" })
                Assert.DoesNotThrow(() => ConfigLoader.LoadGraph(
                    @"{""chars"":[{""id"":""甲"",""element"":""Wood"",""effects"":[{""kind"":""Taunt"",""value"":1,""pick"":""" + pick + @"""}]}]}"), pick);
            Assert.Throws<ConfigException>(() => ConfigLoader.LoadGraph(
                @"{""chars"":[{""id"":""甲"",""element"":""Wood"",""effects"":[{""kind"":""Taunt"",""value"":1}]}]}"));
            Assert.Throws<ConfigException>(() => ConfigLoader.LoadGraph(
                @"{""chars"":[{""id"":""甲"",""element"":""Wood"",""effects"":[{""kind"":""Taunt"",""value"":1,""pick"":""All""}]}]}"));
        }
    }
}
