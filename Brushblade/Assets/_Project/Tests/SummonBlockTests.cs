using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>木灵格挡(D2-0 Task 3,Ruling E1 / spec §2.2):铠落到木灵身上,格挡挂在木灵自己的袋子里,
    /// 战意仍给玩家;木灵挨敌人挥击时与玩家侧同口径(−40%、反击、60% 反伤预算、打空与免疫不耗次数)。</summary>
    public class SummonBlockTests
    {
        // 铠:攻击面 100 → 反击 30;五行面 战意 2 + 格挡 1
        private static CharDef Guard() => new("铠", Element.Metal,
            effects: new[] { new EffectDef(EffectKind.Morale, 2), new EffectDef(EffectKind.Block, 1) },
            attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 100) });

        private static CharDef Summoner(string id, SummonPassive passive = null) => new(id, Element.Wood,
            effects: new[] { new EffectDef(EffectKind.Summon, 200, summonCount: 1, summonAttack: 0, summonChar: "木",
                passive: passive) });

        private static BattleEngine Battle(int mobAttack, params CharDef[] extra)
        {
            var defs = new[] { Guard() }.Concat(extra).Append(new CharDef("木", Element.Wood)).ToArray();
            return new BattleEngine(RebalanceFixture.Graph(defs),
                new BattleConfig { PlayerMaxHp = RebalanceFixture.BaseMaxHp, PlayerAttack = 100 },
                defs.Where(d => d.Id != "木").SelectMany(d => new[] { d.Id, d.Id }).ToArray(),
                Array.Empty<string>(), new[] { RebalanceFixture.Mob(attack: mobAttack) }, seed: 1);
        }

        private static int SlotOfFirstSummon(BattleEngine b) =>
            Array.FindIndex(b.Summons.ToArray(), s => s != null && s.Alive);

        private static int EnemyDamage(BattleEngine b) => 100000 - b.Enemies[0].Hp;

        [Test]
        public void Guard_OnSummon_BlockLandsOnSummon_MoraleStaysOnPlayer()
        {
            var b = Battle(0, Summoner("垛"));
            Assert.That(b.Cast("垛"), Is.EqualTo(BattleError.None));
            int slot = SlotOfFirstSummon(b);
            Assert.That(b.Cast("铠", -1, attackMode: false, allySlot: slot), Is.EqualTo(BattleError.None));
            var block = b.Summons[slot].Statuses.Find(StatusKind.Block);
            Assert.That(block, Is.Not.Null);
            Assert.That(block.Magnitude, Is.EqualTo(1));
            Assert.That(block.CounterDamage, Is.EqualTo(30), "反击出字时定死");
            Assert.That(b.PlayerStatuses.Has(StatusKind.Block), Is.False, "玩家袋子里没有");
            Assert.That(b.PlayerStatuses.TotalMagnitude(StatusKind.Morale), Is.EqualTo(2), "战意仍给玩家");
        }

        [Test]
        public void SummonBlock_CutsDamage40Percent_ConsumedOnce_CountersAttacker()
        {
            var b = Battle(100, Summoner("垛"));
            b.Cast("垛");
            int slot = SlotOfFirstSummon(b);
            b.Cast("铠", -1, attackMode: false, allySlot: slot);
            b.EndTurn();
            Assert.That(b.Summons[slot].Hp, Is.EqualTo(200 - 60), "100 × 60%");
            Assert.That(b.Summons[slot].Statuses.Has(StatusKind.Block), Is.False, "次数归零即移除");
            Assert.That(EnemyDamage(b), Is.EqualTo(30), "反击 = 出字时定死的 CounterDamage");
            int hp = b.Summons[slot].Hp;
            b.EndTurn();
            Assert.That(hp - b.Summons[slot].Hp, Is.EqualTo(100), "第二下不再减");
        }

        [Test]
        public void SummonBlock_CounterBoostOnPlayer_DoublesCounter()
        {
            var boost = new CharDef("戈", Element.Metal,
                effects: new[] { new EffectDef(EffectKind.CounterBoost, 100) });
            var b = Battle(100, Summoner("垛"), boost);
            b.Cast("垛");
            int slot = SlotOfFirstSummon(b);
            b.Cast("铠", -1, attackMode: false, allySlot: slot);
            Assert.That(b.Cast("戈"), Is.EqualTo(BattleError.None));
            Assert.That(b.PlayerStatuses.Has(StatusKind.CounterBoost), Is.True);
            b.EndTurn();
            Assert.That(EnemyDamage(b), Is.EqualTo(36), "30 × 2 = 60,被 60% 预算钳到 36");
        }

        [Test]
        public void SummonBlock_SharesThe60PercentBudget_WithThorns_ThornsFirst()
        {
            var b = Battle(100, Summoner("棘", new SummonPassive { Thorns = 50 }));
            b.Cast("棘");
            int slot = SlotOfFirstSummon(b);
            b.Cast("铠", -1, attackMode: false, allySlot: slot);
            b.EndTurn();
            // 承伤 60:荆棘 30,预算余 36 − 30 = 6,反击 30 被钳到 6;合计 36 = 60%
            Assert.That(EnemyDamage(b), Is.EqualTo(36));
        }

        [Test]
        public void SummonBlock_PlusPlayerDamageCut_CappedAt60Percent()
        {
            var cut = new CharDef("壁", Element.Earth, effects: new[] { new EffectDef(EffectKind.DamageCut, 30) });
            var b = Battle(100, Summoner("垛"), cut);
            b.Cast("垛");
            int slot = SlotOfFirstSummon(b);
            b.Cast("铠", -1, attackMode: false, allySlot: slot);
            Assert.That(b.Cast("壁"), Is.EqualTo(BattleError.None));
            b.EndTurn();
            Assert.That(b.Summons[slot].Hp, Is.EqualTo(200 - 40), "30 + 40 = 70 → 封顶 60");
            Assert.That(EnemyDamage(b), Is.EqualTo(24), "反击被 40 × 60% 钳到 24");
        }

        [Test]
        public void SummonBlock_NotConsumedWhenAttackMisses()
        {
            var b = Battle(100, Summoner("闪", new SummonPassive { Dodge = 100 }));
            b.Cast("闪");
            int slot = SlotOfFirstSummon(b);
            b.Cast("铠", -1, attackMode: false, allySlot: slot);
            b.EndTurn();
            Assert.That(b.Summons[slot].Hp, Is.EqualTo(200), "打空");
            Assert.That(b.Summons[slot].Statuses.Find(StatusKind.Block)?.Magnitude, Is.EqualTo(1), "不耗次数");
            Assert.That(EnemyDamage(b), Is.EqualTo(0));
        }

        [Test]
        public void SummonBlock_NotConsumedWhenImmunityBlocks()
        {
            var imm = new CharDef("杜", Element.Earth, effects: new[] { new EffectDef(EffectKind.Immunity, 1) });
            var b = Battle(100, Summoner("垛"), imm);
            b.Cast("垛");
            int slot = SlotOfFirstSummon(b);
            b.Cast("铠", -1, attackMode: false, allySlot: slot);
            Assert.That(b.Cast("杜", -1, attackMode: false, allySlot: slot), Is.EqualTo(BattleError.None));
            b.EndTurn();
            Assert.That(b.Summons[slot].Hp, Is.EqualTo(200), "免疫完全挡下");
            Assert.That(b.Summons[slot].Statuses.Find(StatusKind.Block)?.Magnitude, Is.EqualTo(1));
            Assert.That(EnemyDamage(b), Is.EqualTo(0));
        }

        [Test]
        public void Guard_WithNoSummonOnField_StillLandsOnPlayer_NoAllySlotNeeded()
        {
            var b = Battle(0);
            Assert.That(b.Cast("铠", -1, attackMode: false), Is.EqualTo(BattleError.None));
            Assert.That(b.PlayerStatuses.Find(StatusKind.Block)?.Magnitude, Is.EqualTo(1));
        }

        [Test]
        public void Guard_DefaultAllySlot_WithSummonOnField_StillLandsOnPlayer()
        {
            var b = Battle(0, Summoner("垛"));
            b.Cast("垛");
            Assert.That(b.Cast("铠", -1, attackMode: false), Is.EqualTo(BattleError.None));
            Assert.That(b.PlayerStatuses.Find(StatusKind.Block)?.Magnitude, Is.EqualTo(1), "拖到玩家 / 缺省 = 玩家");
        }

        [Test]
        public void NeedsAllyTarget_GuardFeatureFace_True_AttackFace_False()
        {
            Assert.That(BattleEngine.NeedsAllyTarget(Guard(), attackMode: false), Is.True);
            Assert.That(BattleEngine.NeedsAllyTarget(Guard(), attackMode: true), Is.False);
        }

        [Test]
        public void Run_CarriedSummon_LosesBlock()
        {
            var graph = new RecipeGraph(new[]
            {
                new CharDef("木", Element.Wood),
                Summoner("垛"),
                Guard(),
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
                startingLibrary: new[] { "垛", "铠", "焚" }, startingPool: Array.Empty<string>(), seed: 7);
            run.Battle.Cast("垛");
            int slot = SlotOfFirstSummon(run.Battle);
            Assert.That(run.Battle.Cast("铠", -1, attackMode: false, allySlot: slot), Is.EqualTo(BattleError.None));
            Assert.That(run.Battle.Summons[slot].Statuses.Has(StatusKind.Block), Is.True);
            Assert.That(run.Battle.Cast("焚"), Is.EqualTo(BattleError.None));
            Assert.That(run.Battle.Phase, Is.EqualTo(BattlePhase.Won));
            run.AdvanceAfterBattle();
            Assert.That(run.CarriedSummons.Count, Is.EqualTo(1));
            Assert.That(run.CarriedSummons[0].Statuses.Any(s => s.Kind == StatusKind.Block), Is.False);
        }
    }
}
