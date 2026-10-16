using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using Brushblade.Data;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>柘 Lv6 生面「磐固」(spec 2026-10-16 §3,替换守护):柘灵每次被敌人挥击命中,自身护甲 +10,本场叠加、至多 +50。
    /// 先结算本击再加层(本击不吃本次加成);免疫抵消 / 打空 / 灼烧跳伤不算「受到攻击」;战后不随木灵跨场。
    /// 加层落在木灵自己的护甲(DefenseBuff)上,所以良弓(SummonDefense × N%)同样吃得到。
    ///
    /// 夹具:Element.Heart 敌人(对木 1.0×)、0 甲;木灵用起手携带直接摆位,本命护甲 60 用「字#armor」的入场护甲表示。</summary>
    public class WoodHardenTests
    {
        private const int BaseArmor = 60;

        private static BattleConfig Config() => new()
        {
            PlayerMaxHp = 6000, PlayerAttack = 100, ApPerTurn = 200, BossPhaseJitterPercent = 0,
            UnlockedSummonSlots = BattleEngine.AllSummonSlotsMask,
        };

        private static SummonPassive HardenP(int returnFire = 0) =>
            new() { Taunt = true, Harden = 10, HardenMax = 50, ReturnFire = returnFire };

        private static SummonSnapshot Zhe(SummonPassive passive, params StatusEffect[] extra) => new()
        {
            Slot = 0, Char = "柘", SourceChar = "柘", Element = Element.Wood, Hp = 100000, MaxHp = 100000,
            Attack = 0, Speed = 100, Passive = passive,
            Statuses = new[] { Armor() }.Concat(extra).ToList(),
        };

        private static StatusEffect Armor() => new()
        {
            Kind = StatusKind.DefenseBuff, Polarity = StatusPolarity.Buff, Magnitude = BaseArmor, TurnsLeft = -1, SourceId = "柘#armor",
        };

        private static BattleEngine Battle(SummonSnapshot zhe, params EnemyDef[] enemies) =>
            new(RebalanceFixture.Graph(), Config(), Enumerable.Repeat("甲", 10).ToArray(), Array.Empty<string>(),
                enemies, seed: 7, startingSummons: new[] { zhe });

        private static EnemyDef Melee(int attack) => new("怔", Element.Heart, 100000, attack);

        private static EnemyDef Shooter(int attack) =>
            new("盯", Element.Heart, 100000, attack, range: AttackRange.Ranged, focus: AttackFocus.Player);

        [Test]
        public void EachHit_AddsTen_CapsAtFifty()
        {
            var b = Battle(Zhe(HardenP()), Melee(100));
            Assert.That(b.SummonDefense(b.Summons[0]), Is.EqualTo(BaseArmor), "夹具前提:本命护甲 60");
            int hits = 0;
            for (int turn = 0; turn < 20 && hits < 7; turn++)
            {
                b.EndTurn();
                hits += b.LastEvents.Count(e => e.Kind == BattleEventKind.SummonHit && e.SecondIndex == 0);
                Assert.That(b.SummonDefense(b.Summons[0]), Is.EqualTo(BaseArmor + Math.Min(50, 10 * hits)), $"受击 {hits} 次");
            }
            Assert.That(hits, Is.GreaterThanOrEqualTo(6), "夹具前提:挨够 6 下才验得到封顶");
            Assert.That(b.Summons[0].Statuses.All.Count(s => s.Kind == StatusKind.DefenseBuff), Is.EqualTo(2),
                "磐固只占一条护甲(同源刷新),不是每击新挂一条");
        }

        [Test]
        public void ThisHit_UsesArmorBeforeItsOwnStack()
        {
            var b = Battle(Zhe(HardenP()), Melee(1000));
            var amounts = new List<int>();
            for (int turn = 0; turn < 10 && amounts.Count < 2; turn++)
            {
                b.EndTurn();
                amounts.AddRange(b.LastEvents.Where(e => e.Kind == BattleEventKind.SummonHit).Select(e => e.Amount));
            }
            Assert.That(amounts[0], Is.EqualTo(BattleEngine.ApplyDefense(1000, BaseArmor)), "第 1 击按旧护甲 60 算");
            Assert.That(amounts[1], Is.EqualTo(BattleEngine.ApplyDefense(1000, BaseArmor + 10)), "第 2 击吃到第 1 击的 +10");
        }

        [Test]
        public void ReturnFire_ReadsHardenedArmor_FromNextHit()
        {
            // 良弓 = SummonDefense × 200%:磐固加的是护甲,良弓照算;本击的良弓仍按加层前的护甲
            var b = Battle(Zhe(HardenP(returnFire: 200)), Melee(1000));
            var fire = new List<int>();
            for (int turn = 0; turn < 10 && fire.Count < 2; turn++)
            {
                b.EndTurn();
                fire.AddRange(b.LastEvents.Where(e => e.Kind == BattleEventKind.Damage && e.Source == EffectSource.ReturnFire)
                    .Select(e => e.Amount));
            }
            Assert.That(fire.Take(2).ToArray(), Is.EqualTo(new[] { BaseArmor * 2, (BaseArmor + 10) * 2 }));
        }

        [Test]
        public void ImmunityBlockedHit_DoesNotStack()
        {
            var immunity = new StatusEffect
                { Kind = StatusKind.Immunity, Polarity = StatusPolarity.Buff, Magnitude = 1, TurnsLeft = -1, SourceId = "杜" };
            var b = Battle(Zhe(HardenP(), immunity), Melee(100));
            for (int turn = 0; turn < 10; turn++)
            {
                b.EndTurn();
                if (b.LastEvents.Any(e => e.Kind == BattleEventKind.ImmunityBlocked)) break;
            }
            Assert.That(b.LastEvents.Count(e => e.Kind == BattleEventKind.ImmunityBlocked), Is.EqualTo(1), "夹具前提:免疫挡下一击");
            int landed = b.LastEvents.Count(e => e.Kind == BattleEventKind.SummonHit);
            Assert.That(b.SummonDefense(b.Summons[0]), Is.EqualTo(BaseArmor + 10 * landed), "被免疫挡下的那一击不加层");
        }

        [Test]
        public void BurnTick_IsNotAnAttack()
        {
            var burn = new StatusEffect { Kind = StatusKind.Burn, Polarity = StatusPolarity.Debuff, Magnitude = 3, TurnsLeft = -1 };
            var b = Battle(Zhe(new SummonPassive { Harden = 10, HardenMax = 50 }, burn), Shooter(10));   // 不嘲讽 + 敌人只打玩家:柘灵只挨灼烧
            int ticks = 0;
            for (int turn = 0; turn < 3; turn++)
            {
                b.EndTurn();
                ticks += b.LastEvents.Count(e => e.Kind == BattleEventKind.SummonBurnTick);
            }
            Assert.That(ticks, Is.GreaterThan(0), "夹具前提:灼烧跳过");
            Assert.That(b.SummonDefense(b.Summons[0]), Is.EqualTo(BaseArmor), "灼烧跳伤不算受到攻击");
        }

        [Test]
        public void Stack_IsNotCarriedIntoNextBattle()
        {
            var config = new RunConfig
            {
                Encounters = new[] { new[] { RebalanceFixture.Mob(hp: 20, attack: 10) }, new[] { RebalanceFixture.Mob(hp: 20) } },
                RewardPool = new[] { "甲" },
            };
            var run = new RunEngine(RebalanceFixture.Graph(), config, Config(), new[] { "甲", "甲" }, Array.Empty<string>(),
                seed: 1, startingSummons: new[] { Zhe(HardenP()) });
            var zhe = run.Battle.Summons.Single(s => s != null);
            for (int turn = 0; turn < 10 && run.Battle.SummonDefense(zhe) == BaseArmor; turn++)
                run.Battle.EndTurn();
            Assert.That(run.Battle.SummonDefense(zhe), Is.GreaterThan(BaseArmor), "夹具前提:本场叠过磐固");
            Assert.That(run.Battle.Cast("甲", 0), Is.EqualTo(BattleError.None));
            run.AdvanceAfterBattle();
            var carried = run.CarriedSummons.Single().Statuses.Where(st => st.Kind == StatusKind.DefenseBuff).ToList();
            Assert.That(carried.Sum(st => st.Magnitude), Is.EqualTo(BaseArmor), "下一场从 +0 起,本命护甲照常携带");
        }

        // ---------------- 真实字表 ----------------

        [Test]
        public void RealData_ZheLv6Feature_IsHarden()
        {
            var graph = CharTableTests.RealGraph();
            Assert.That(graph.TryGet("柘", out var zhe), Is.True);
            var lv6 = zhe.Traits.Single(t => t.Slot == TraitSlot.Lv6 && t.Face == TraitFace.Feature);
            Assert.That(lv6.Name, Is.EqualTo("磐固"));
            var passive = lv6.Effects.Single(e => e.Kind == EffectKind.Summon).Passive;
            Assert.That((passive.Harden, passive.HardenMax), Is.EqualTo((10, 50)));
        }

        [Test]
        public void RealData_ZheLv6_SummonedSpirit_HardensWhenHit()
        {
            var graph = CharTableTests.RealGraph();
            var b = new BattleEngine(graph, Config(), Enumerable.Repeat("柘", 10).ToArray(), Array.Empty<string>(),
                new[] { Melee(100) }, seed: 1, cardLevels: new Dictionary<string, int> { ["柘"] = 6 });
            Assert.That(b.Cast("柘", -1), Is.EqualTo(BattleError.None));
            int slot = b.Summons.ToList().FindIndex(s => s != null && s.Alive);
            int before = b.SummonDefense(b.Summons[slot]);
            int hits = 0;
            for (int turn = 0; turn < 10 && hits == 0; turn++)
            {
                b.EndTurn();
                hits = b.LastEvents.Count(e => e.Kind == BattleEventKind.SummonHit && e.SecondIndex == slot);
            }
            Assert.That(hits, Is.GreaterThan(0), "夹具前提:柘灵挨了打");
            Assert.That(b.SummonDefense(b.Summons[slot]), Is.EqualTo(before + 10 * Math.Min(5, hits)));
        }
    }
}
