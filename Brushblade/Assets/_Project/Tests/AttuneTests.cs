using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>spec C §3 / §4:Attune 落活木灵 → 属性改为本字所属系(永久、跨场);落玩家空转;被嫁接覆盖回木。
    /// 水字五行面落木灵不再改水。嫁接后本命与木属性跨场保留。柘 SelfCut:本木灵受伤 −N%。</summary>
    public class AttuneTests
    {
        private static SummonPassive Vine => new() { OnHitFreezeChance = 20, OnHitFreezeTurns = 1 };
        private static SummonPassive Thorny => new() { Thorns = 50 };

        private static CharDef Gild() => new("鎏", Element.Metal,
            effects: new[] { new EffectDef(EffectKind.Block, 1, counterPercent: 30), new EffectDef(EffectKind.Attune, 0) },
            attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 10) });

        private static CharDef Wet() => new("润", Element.Water,
            effects: new[] { new EffectDef(EffectKind.HealSelf, 10) },
            attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 10) });

        private static CharDef Graftor() => new("藤", Element.Wood,
            effects: new[] { new EffectDef(EffectKind.Summon, 100, summonCount: 1, summonAttack: 10, summonChar: "木", passive: Vine) });

        private static SummonSnapshot Wood(int slot, SummonPassive passive = null, int hp = 100) => new()
        {
            Slot = slot, Char = "木", Element = Element.Wood, Hp = hp, MaxHp = Math.Max(hp, 100), Attack = 10, Speed = 100,
            Passive = passive,
        };

        private static BattleEngine Battle(IReadOnlyList<SummonSnapshot> summons, params EnemyDef[] enemies)
        {
            var defs = new[] { Gild(), Wet(), Graftor() };
            return new BattleEngine(RebalanceFixture.Graph(defs),
                new BattleConfig { PlayerMaxHp = RebalanceFixture.BaseMaxHp, PlayerAttack = 100, ApPerTurn = 20 },
                defs.SelectMany(d => new[] { d.Id, d.Id }).ToArray(), Array.Empty<string>(),
                enemies.Length > 0 ? enemies : new[] { RebalanceFixture.Mob(attack: 0) }, seed: 1, startingSummons: summons);
        }

        [Test]
        public void Attune_OnSummon_ChangesElementToOwnElement_EmitsEvent()
        {
            var b = Battle(new[] { Wood(0) });
            Assert.That(b.Cast("鎏", -1, attackMode: false, allySlot: 0), Is.EqualTo(BattleError.None));
            Assert.That(b.Summons[0].Element, Is.EqualTo(Element.Metal));
            Assert.That(b.LastEvents.Any(e => e.Kind == BattleEventKind.ElementChanged && e.TargetIndex == 0
                && e.Amount == (int)Element.Metal), Is.True);
            Assert.That(b.LastEvents.Any(e => e.Kind == BattleEventKind.Unseal), Is.False, "改属性不是解封,消息不能印「解封」");
        }

        [Test]
        public void Attune_OnPlayer_DoesNothingToSummons()
        {
            var b = Battle(new[] { Wood(0) });
            Assert.That(b.Cast("鎏", -1, attackMode: false, allySlot: Targeting.PlayerTarget), Is.EqualTo(BattleError.None));
            Assert.That(b.Summons[0].Element, Is.EqualTo(Element.Wood));
            Assert.That(b.LastEvents.Any(e => e.Kind == BattleEventKind.Unseal || e.Kind == BattleEventKind.ElementChanged), Is.False);
        }

        [Test]
        public void Graft_After_Attune_ReturnsToWood()   // Review Focus 2
        {
            var b = Battle(new[] { Wood(0, Thorny, hp: 30) });
            b.Cast("鎏", -1, attackMode: false, allySlot: 0);
            Assert.That(b.Cast("藤", -1, attackMode: false, allySlot: 0), Is.EqualTo(BattleError.None));
            Assert.That(b.Summons[0].Element, Is.EqualTo(Element.Wood));
            Assert.That(b.Summons[0].Passive.OnHitFreezeChance, Is.EqualTo(20));
            Assert.That(b.Summons[0].Passive.Thorns, Is.EqualTo(0));
        }

        [Test]
        public void Wet_OnSummon_NoLongerChangesElement()   // Review Focus 2
        {
            var b = Battle(new[] { Wood(0) });
            Assert.That(b.Cast("润", -1, attackMode: false, allySlot: 0), Is.EqualTo(BattleError.None));
            Assert.That(b.Summons[0].Element, Is.EqualTo(Element.Wood));
            Assert.That(b.LastEvents.Any(e => e.Kind == BattleEventKind.Unseal || e.Kind == BattleEventKind.ElementChanged), Is.False);
        }

        [Test]
        public void Attune_Then_Wet_StaysMetal()
        {
            var b = Battle(new[] { Wood(0) });
            b.Cast("鎏", -1, attackMode: false, allySlot: 0);
            b.Cast("润", -1, attackMode: false, allySlot: 0);
            Assert.That(b.Summons[0].Element, Is.EqualTo(Element.Metal));
        }

        [Test]
        public void SelfCut_ReducesOnlyThisSummonsDamage()
        {
            var cut = Wood(0, new SummonPassive { Taunt = true, SelfCut = 15 }, hp: 100000);
            var b = Battle(new[] { cut }, RebalanceFixture.Mob(attack: 100));
            b.EndTurn();
            Assert.That(b.Summons[0].MaxHp - b.Summons[0].Hp, Is.EqualTo(85), "100 × (1 − 15%)");

            var plain = Wood(0, new SummonPassive { Taunt = true }, hp: 100000);
            var c = Battle(new[] { plain }, RebalanceFixture.Mob(attack: 100));
            c.EndTurn();
            Assert.That(c.Summons[0].MaxHp - c.Summons[0].Hp, Is.EqualTo(100), "对照:没有 SelfCut");
        }

        // ---- 跨场(RunEngine)----

        private static RecipeGraph RunGraph() => new(new[]
        {
            new CharDef("木", Element.Wood), Graftor(), Gild(),
            new CharDef("荆", Element.Wood,
                effects: new[] { new EffectDef(EffectKind.Summon, 100, summonCount: 1, summonAttack: 10, summonChar: "木", passive: Thorny) }),
            new CharDef("焚", Element.Fire, effects: new[] { new EffectDef(EffectKind.DamageSingle, 99, shape: TargetArea.All) }),
        });

        private static RunConfig RunCfg() => new()
        {
            Encounters = new[] { new[] { RebalanceFixture.Mob(hp: 10) }, new[] { RebalanceFixture.Mob(hp: 10) } },
            RewardPool = new[] { "焚" },
            FromDepth = 31,
        };

        private static RunEngine NewRun() => new(RunGraph(), RunCfg(), new BattleConfig { DropTable = new[] { "木" }, ApPerTurn = 10 },
            startingLibrary: new[] { "荆", "藤", "鎏", "焚" }, startingPool: Array.Empty<string>(), seed: 7);

        private static void Win(RunEngine run)
        {
            Assert.That(run.Battle.Cast("焚"), Is.EqualTo(BattleError.None));
            Assert.That(run.Battle.Phase, Is.EqualTo(BattlePhase.Won));
            run.AdvanceAfterBattle();
        }

        private static int FirstSlot(BattleEngine b) => Array.FindIndex(b.Summons.ToArray(), s => s != null && s.Alive);

        [Test]
        public void Attune_PersistsAcrossBattles()   // Review Focus 2
        {
            var run = NewRun();
            run.Battle.Cast("荆");
            int slot = FirstSlot(run.Battle);
            Assert.That(run.Battle.Cast("鎏", -1, attackMode: false, allySlot: slot), Is.EqualTo(BattleError.None));
            Win(run);
            Assert.That(run.CarriedSummons.Single().Element, Is.EqualTo(Element.Metal));
        }

        [Test]
        public void Run_Carry_KeepsGraftedPassive_AndWood()   // Review Focus 2
        {
            var run = NewRun();
            run.Battle.Cast("荆");
            int slot = FirstSlot(run.Battle);
            run.Battle.Cast("鎏", -1, attackMode: false, allySlot: slot);
            Assert.That(run.Battle.Cast("藤", -1, attackMode: false, allySlot: slot), Is.EqualTo(BattleError.None));
            Win(run);
            var carried = run.CarriedSummons.Single();
            Assert.That(carried.Element, Is.EqualTo(Element.Wood));
            Assert.That(carried.Passive.OnHitFreezeChance, Is.EqualTo(20), "嫁接的本命跨场保留");
            Assert.That(carried.Passive.Thorns, Is.EqualTo(0));
        }
    }
}
