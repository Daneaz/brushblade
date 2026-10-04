using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>格挡(spec v7 §3.1 / §4 / §5.2):下一次挥击 −40%,反击攻击者,与反弹共用 60% 反伤预算。</summary>
    public class BlockTests
    {
        // 攻击面 100,五行面格挡 1 次 → 反击 = 100 × 30% = 30
        private static CharDef Guard(int charges = 1) => new("铠", Element.Metal,
            effects: new[] { new EffectDef(EffectKind.Block, charges) },
            attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 100) });

        private static BattleEngine Battle(int mobAttack, int level = 1, int charges = 1,
            EnemyAbility ability = EnemyAbility.None, params CharDef[] extra) =>
            new BattleEngine(RebalanceFixture.Graph(new[] { Guard(charges) }.Concat(extra).ToArray()),
                new BattleConfig { PlayerMaxHp = RebalanceFixture.BaseMaxHp, PlayerAttack = 100 },
                new[] { "铠", "铠", "铠" }.Concat(extra.Select(c => c.Id)).ToArray(), Array.Empty<string>(),
                new[] { RebalanceFixture.Mob(attack: mobAttack, ability: ability) }, seed: 1,
                cardLevels: new Dictionary<string, int> { ["铠"] = level });

        private static int HitTaken(BattleEngine b)
        {
            int hp = b.PlayerHp;
            b.EndTurn();
            return hp - b.PlayerHp;
        }

        [Test]
        public void Block_ReducesNextHitBy40Percent_ThenIsConsumed()
        {
            int plain = HitTaken(Battle(100));
            var b = Battle(100);
            b.Cast("铠", -1, attackMode: false);
            Assert.That(b.PlayerStatuses.Find(StatusKind.Block).Magnitude, Is.EqualTo(1));
            Assert.That(HitTaken(b), Is.EqualTo(plain * 60 / 100));
            Assert.That(b.PlayerStatuses.Has(StatusKind.Block), Is.False, "用完即移除");
            Assert.That(HitTaken(b), Is.EqualTo(plain), "第二下不再减");
        }

        [Test]
        public void Block_CountersAttacker_With30PercentOfAttackFace_ScaledByCardLevel()
        {
            var b = Battle(100, level: 1);
            b.Cast("铠", -1, attackMode: false);
            b.EndTurn();
            Assert.That(b.Enemies[0].Hp, Is.EqualTo(100000 - 30));

            var b5 = Battle(100, level: 5);
            b5.Cast("铠", -1, attackMode: false);
            Assert.That(b5.PlayerStatuses.Find(StatusKind.Block).CounterDamage,
                Is.EqualTo(MetaRules.ScaleByCardLevel(100, 5) * 30 / 100));
        }

        [Test]
        public void Counter_IsCappedBy60PercentOfDamageTaken()
        {
            int plain = HitTaken(Battle(10));
            var b = Battle(10);
            b.Cast("铠", -1, attackMode: false);
            int taken = HitTaken(b);
            Assert.That(taken, Is.EqualTo(plain * 60 / 100));
            Assert.That(100000 - b.Enemies[0].Hp, Is.EqualTo(taken * 60 / 100), "反伤封顶 60%");
        }

        [Test]
        public void SecondBlock_TakesStrongest_DoesNotStack()
        {
            var b = Battle(100, charges: 2);
            b.Cast("铠", -1, attackMode: false);
            b.Cast("铠", -1, attackMode: false);
            var block = b.PlayerStatuses.Find(StatusKind.Block);
            Assert.That(block.Magnitude, Is.EqualTo(2), "次数取较大值,不是 4");
            Assert.That(b.PlayerStatuses.All.Count(s => s.Kind == StatusKind.Block), Is.EqualTo(1));
        }

        [Test]
        public void Block_IsNotDiscretelyScaledByCardLevel()
        {
            var b = Battle(100, level: 10, charges: 1);
            b.Cast("铠", -1, attackMode: false);
            Assert.That(b.PlayerStatuses.Find(StatusKind.Block).Magnitude, Is.EqualTo(1), "次数是离散量");
        }

        [Test]
        public void Block_SurvivesSnapshotRoundTrip()
        {
            var b = Battle(100);
            b.Cast("铠", -1, attackMode: false);
            var restored = BattleEngine.Restore(b.Capture(), RebalanceFixture.Graph(Guard()),
                new BattleConfig { PlayerMaxHp = RebalanceFixture.BaseMaxHp, PlayerAttack = 100 }, null,
                new Dictionary<string, EnemyDef> { ["怔"] = RebalanceFixture.Mob(attack: 100) });
            var block = restored.PlayerStatuses.Find(StatusKind.Block);
            Assert.That(block.Magnitude, Is.EqualTo(1));
            Assert.That(block.CounterDamage, Is.EqualTo(30));
        }

        [Test]
        public void Block_SurvivesRealSaveFile_CounterDamageNotLost()
        {
            var enemy = RebalanceFixture.Mob(attack: 100);
            var graph = RebalanceFixture.Graph(Guard());
            var battleConfig = new BattleConfig { PlayerMaxHp = RebalanceFixture.BaseMaxHp, PlayerAttack = 100 };
            var runConfig = new RunConfig
            {
                Encounters = new[] { new[] { enemy }, new[] { enemy } },
                RewardPool = new[] { "铠" },
            };
            var run = new RunEngine(graph, runConfig, battleConfig, new[] { "铠", "铠" }, Array.Empty<string>(), 3,
                startingInk: 50, perFloorNormalShield: 2);
            run.Battle.Cast("铠", -1, attackMode: false);

            var meta = new MetaState
            {
                EndlessV2 = new EndlessSaveState
                {
                    Depth = 3, Seed = 999,
                    InProgress = new InProgressRun { FromDepth = 1, FirstTowerSegment = true, Run = run.Capture() },
                },
            };
            var reloaded = Data.SaveSerializer.FromJson(Data.SaveSerializer.ToJson(meta));
            var restored = RunEngine.Restore(reloaded.EndlessV2.InProgress.Run, graph, runConfig, battleConfig, null,
                startingInk: 50, perFloorNormalShield: 2);
            var block = restored.Battle.PlayerStatuses.Find(StatusKind.Block);
            Assert.That(block.Magnitude, Is.EqualTo(1));
            Assert.That(block.CounterDamage, Is.EqualTo(30));
        }

        [Test]
        public void BarbRecoil_DoesNotConsumeBlock()
        {
            var b = Battle(50, ability: EnemyAbility.Barb);
            b.Cast("铠", -1, attackMode: false);
            int hp = b.PlayerHp;
            b.Cast("铠", 0, attackMode: true); // 打铁画:反噬落到玩家身上
            Assert.That(b.PlayerHp, Is.LessThan(hp), "反噬确实发生了(否则本测试空转)");
            Assert.That(b.PlayerStatuses.Find(StatusKind.Block).Magnitude, Is.EqualTo(1), "反噬不是敌人挥击,不消耗格挡");
        }

        [Test]
        public void ImmunityBlocked_DoesNotConsumeBlock()
        {
            var b = Battle(100, extra: RebalanceFixture.Char("免", new EffectDef(EffectKind.Immunity, 1)));
            b.Cast("铠", -1, attackMode: false);
            b.Cast("免");
            int hp = b.PlayerHp;
            b.EndTurn();
            Assert.That(b.PlayerHp, Is.EqualTo(hp), "免疫挡下");
            Assert.That(b.PlayerStatuses.Find(StatusKind.Block).Magnitude, Is.EqualTo(1), "免疫挡下不消耗格挡");
        }

        [Test]
        public void DodgedAttack_DoesNotConsumeBlock()
        {
            var b = new BattleEngine(RebalanceFixture.Graph(Guard()),
                new BattleConfig { PlayerMaxHp = RebalanceFixture.BaseMaxHp, PlayerAttack = 100, PlayerDodge = 100 },
                new[] { "铠", "铠", "铠" }, Array.Empty<string>(),
                new[] { RebalanceFixture.Mob(attack: 100) }, seed: 1);
            b.Cast("铠", -1, attackMode: false);
            int hp = b.PlayerHp;
            b.EndTurn();
            Assert.That(b.PlayerHp, Is.EqualTo(hp), "闪避 100%:攻击打空");
            Assert.That(b.PlayerStatuses.Find(StatusKind.Block).Magnitude, Is.EqualTo(1), "打空不消耗格挡");
            Assert.That(b.Enemies[0].Hp, Is.EqualTo(100000), "没挨到打也就没有反击");
        }

        [Test]
        public void BlindedAttackerMissing_DoesNotConsumeBlock()
        {
            var b = Battle(100);
            b.Enemies[0].Statuses.Apply(new StatusEffect
            {
                Kind = StatusKind.Blind, Polarity = StatusPolarity.Debuff, Magnitude = 100, TurnsLeft = -1,
            });
            b.Cast("铠", -1, attackMode: false);
            int hp = b.PlayerHp;
            b.EndTurn();
            Assert.That(b.PlayerHp, Is.EqualTo(hp), "致盲 100%:攻击打空");
            Assert.That(b.PlayerStatuses.Find(StatusKind.Block).Magnitude, Is.EqualTo(1), "致盲打空不消耗格挡");
        }
    }
}
