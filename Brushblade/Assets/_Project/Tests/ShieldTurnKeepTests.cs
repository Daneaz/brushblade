using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>护盾战斗内不随回合清空(spec 2026-10-13 §1,取消 2026-10-04 的 U1 回合清盾)。
    /// 护盾一直保留到被打掉、被消耗或被 Boss 倾覆清空;上限仍是最大生命;战斗结束按 ShieldCarryPercent 带入下一场。
    /// 反震 / 冰晶 / 碎玉只在护盾归零(被打空、被消耗、倾覆)时失去载体,跨回合不再因清盾失去。</summary>
    public class ShieldTurnKeepTests
    {
        private static RecipeGraph Graph() => new(new[]
        {
            new CharDef("木", Element.Wood),
            new CharDef("垒", Element.Earth,
                effects: new[] { new EffectDef(EffectKind.Shield, 100) }),
            new CharDef("兵", Element.Wood,
                effects: new[] { new EffectDef(EffectKind.Summon, 100, summonCount: 1, summonAttack: 3, summonChar: "木") }),
            new CharDef("崩", Element.Earth,
                effects: new[] { new EffectDef(EffectKind.ShieldAll, 17) }),
            new CharDef("震", Element.Earth,
                effects: new[] { new EffectDef(EffectKind.Shield, 100), new EffectDef(EffectKind.ShieldRecoil, 30) }),
        });

        private static readonly BattleConfig Config = new() { DropTable = new[] { "木" }, PlayerMaxHp = 500 };

        private static BattleEngine Engine(string[] library, int enemyAttack = 0,
            int normal = 0) =>
            new(Graph(), Config, library, Array.Empty<string>(),
                new[] { new EnemyDef("靶", Element.Heart, 3000, enemyAttack) }, 1,
                startingNormalShield: normal);

        [Test]
        public void NormalShield_KeptAtTurn2And3()
        {
            var engine = Engine(new[] { "垒" });
            Assert.That(engine.Cast("垒"), Is.EqualTo(BattleError.None));
            Assert.That(engine.PlayerShield, Is.EqualTo(100));

            for (int turn = 2; turn <= 3; turn++)
            {
                engine.EndTurn();
                Assert.That(engine.Turn, Is.EqualTo(turn), "确认真的进了下一个玩家回合");
                Assert.That(engine.PlayerShield, Is.EqualTo(100), $"第 {turn} 回合开始护盾不变");
            }
        }

        [Test]
        public void NormalShield_PartiallyAbsorbed_RemainderKept()
        {
            var engine = Engine(new[] { "垒" }, enemyAttack: 30);
            Assert.That(engine.Cast("垒"), Is.EqualTo(BattleError.None));
            int hp = engine.PlayerHp;

            engine.EndTurn();

            Assert.That(engine.PlayerHp, Is.EqualTo(hp), "30 点全被盾吃下");
            Assert.That(engine.PlayerShield, Is.EqualTo(70), "挨了 30,剩下的 70 留到下一回合");
        }

        [Test]
        public void SummonShield_KeptAtTurn2And3()
        {
            var engine = Engine(new[] { "兵", "崩" });
            Assert.That(engine.Cast("兵"), Is.EqualTo(BattleError.None));
            Assert.That(engine.Cast("崩"), Is.EqualTo(BattleError.None));
            var before = engine.Summons.Select(s => s != null && s.Alive ? s.Shield : -1).ToArray();
            Assert.That(before.Any(v => v > 0), Is.True, "前置:召唤物确实有盾");

            for (int turn = 2; turn <= 3; turn++)
            {
                engine.EndTurn();
                var now = engine.Summons.Select(s => s != null && s.Alive ? s.Shield : -1).ToArray();
                Assert.That(now, Is.EqualTo(before), $"第 {turn} 回合开始木灵护盾不变");
            }
        }

        [Test]
        public void CarriedInShield_KeptAfterFirstTurn()
        {
            var engine = Engine(new[] { "木" }, normal: 100);
            Assert.That(engine.ShieldNormal, Is.EqualTo(100));

            engine.EndTurn();
            engine.EndTurn();

            Assert.That(engine.ShieldNormal, Is.EqualTo(100), "跨场带入的盾同样保留");
        }

        [Test]
        public void Restored_KeepsShieldOnNextTurn()
        {
            var engine = Engine(new[] { "木" }, normal: 100);
            var defs = new Dictionary<string, EnemyDef> { ["靶"] = new EnemyDef("靶", Element.Heart, 3000, 0) };
            var restored = BattleEngine.Restore(engine.Capture(), Graph(), Config, null, defs);
            Assert.That(restored.ShieldNormal, Is.EqualTo(100));

            restored.EndTurn();

            Assert.That(restored.ShieldNormal, Is.EqualTo(100));
        }

        [Test]
        public void Shield_StacksAcrossTurns_CappedAtMaxHp()
        {
            var engine = Engine(new[] { "垒" }, normal: 450);
            engine.EndTurn();
            Assert.That(engine.PlayerShield, Is.EqualTo(450), "前置:上一回合的盾还在");

            Assert.That(engine.Cast("垒"), Is.EqualTo(BattleError.None));

            Assert.That(engine.PlayerShield, Is.EqualTo(500), "450 + 100 钳到最大生命 500");
        }

        [Test]
        public void ShieldRecoil_KeptAcrossTurns_WhileShieldRemains()
        {
            var engine = Engine(new[] { "震" });
            Assert.That(engine.Cast("震"), Is.EqualTo(BattleError.None));
            Assert.That(engine.PlayerStatuses.Has(StatusKind.ShieldRecoil), Is.True, "前置:挂上了反震");

            engine.EndTurn();
            engine.EndTurn();

            Assert.That(engine.PlayerShield, Is.EqualTo(100));
            Assert.That(engine.PlayerStatuses.Has(StatusKind.ShieldRecoil), Is.True, "护盾还在,反震载体不失去");
        }

        [Test]
        public void ShieldRecoil_RemovedWhenShieldBroken()
        {
            var engine = Engine(new[] { "震" }, enemyAttack: 150);
            Assert.That(engine.Cast("震"), Is.EqualTo(BattleError.None));

            engine.EndTurn();

            Assert.That(engine.PlayerShield, Is.EqualTo(0), "前置:护盾被打空");
            Assert.That(engine.PlayerStatuses.Has(StatusKind.ShieldRecoil), Is.False, "护盾归零 → 反震失去载体");
        }

        private static RunEngine RunWith(CharDef[] chars, string[] library, EnemyDef enemy, int playerAttack = 100,
            int carryPercent = 50)
        {
            var graph = new RecipeGraph(chars);
            return new RunEngine(graph,
                new RunConfig
                {
                    Encounters = new[] { new[] { enemy }, new[] { enemy } },
                    RewardPool = new[] { "木" },
                },
                new BattleConfig { DropTable = new[] { "木" }, PlayerMaxHp = 500, PlayerAttack = playerAttack,
                    ShieldCarryPercent = carryPercent },
                startingLibrary: library, startingPool: Array.Empty<string>(), seed: 7);
        }

        private static readonly CharDef[] ShieldAndNuke =
        {
            new("木", Element.Wood),
            new("垒", Element.Earth, effects: new[] { new EffectDef(EffectKind.Shield, 100) }),
            new("焚", Element.Fire,
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 100, shape: TargetArea.All) }),
        };

        /// <summary>玩家回合清场(胜利发生在玩家回合内):战斗结束那一刻剩下的护盾按 ShieldCarryPercent 带走。</summary>
        [Test]
        public void Run_PlayerTurnClear_CarriesRemainingShield()
        {
            var run = RunWith(ShieldAndNuke, new[] { "垒", "焚" }, new EnemyDef("枯", Element.Wood, 4, 2));
            Assert.That(run.Battle.Cast("垒"), Is.EqualTo(BattleError.None));
            Assert.That(run.Battle.Cast("焚"), Is.EqualTo(BattleError.None));
            Assert.That(run.Battle.Phase, Is.EqualTo(BattlePhase.Won));

            run.AdvanceAfterBattle();

            Assert.That(run.CarriedNormalShield, Is.EqualTo(50), "100 × 50%");
        }

        /// <summary>护盾在第 1 回合立起、第 2 回合才赢:盾跨回合留到战斗结束,再按保留比例(基础 50 / 固本 75)带走。</summary>
        [TestCase(50, 50)]
        [TestCase(75, 75)]
        public void Run_ShieldFromEarlierTurn_CarriedAtBattleEnd(int carryPercent, int expected)
        {
            var run = RunWith(ShieldAndNuke, new[] { "垒", "焚" }, new EnemyDef("枯", Element.Wood, 4, 0),
                carryPercent: carryPercent);
            Assert.That(run.Battle.Cast("垒"), Is.EqualTo(BattleError.None));
            run.Battle.EndTurn();
            Assert.That(run.Battle.Phase, Is.EqualTo(BattlePhase.PlayerTurn));
            Assert.That(run.Battle.Cast("焚"), Is.EqualTo(BattleError.None));
            Assert.That(run.Battle.Phase, Is.EqualTo(BattlePhase.Won));

            run.AdvanceAfterBattle();

            Assert.That(run.CarriedNormalShield, Is.EqualTo(expected), $"100 × {carryPercent}%");
        }

        /// <summary>胜利发生在**敌方段**(格挡反击打死最后一只敌人):带走的是敌方段结束时剩余的护盾 × 50%。</summary>
        [Test]
        public void Run_EnemyPhaseWin_ByBlockCounter_CarriesRemainingShield()
        {
            var run = RunWith(new[]
            {
                new CharDef("木", Element.Wood),
                new CharDef("垒", Element.Earth, effects: new[] { new EffectDef(EffectKind.Shield, 100) }),
                new CharDef("铠", Element.Metal,
                    effects: new[] { new EffectDef(EffectKind.Block, 1) },
                    attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 100) }),
            }, new[] { "垒", "铠" }, new EnemyDef("靶", Element.Heart, 20, 100));
            Assert.That(run.Battle.Cast("垒"), Is.EqualTo(BattleError.None));
            Assert.That(run.Battle.Cast("铠", -1, attackMode: false), Is.EqualTo(BattleError.None));

            run.Battle.EndTurn();

            Assert.That(run.Battle.Phase, Is.EqualTo(BattlePhase.Won), "反击在敌方段打死了最后一只敌人");
            int remaining = run.Battle.ShieldNormal;
            Assert.That(remaining, Is.GreaterThan(0).And.LessThan(100), "吃了一击但没被打空");
            run.AdvanceAfterBattle();
            Assert.That(run.CarriedNormalShield, Is.EqualTo(remaining * 50 / 100));
        }

        [Test]
        public void Run_EnemyPhaseWin_ByThorns_CarriesRemainingShield()
        {
            var run = RunWith(new[]
            {
                new CharDef("木", Element.Wood),
                new CharDef("垒", Element.Earth, effects: new[] { new EffectDef(EffectKind.Shield, 100) }),
                new CharDef("棘", Element.Wood,
                    effects: new[] { new EffectDef(EffectKind.Summon, 30, summonCount: 1, summonAttack: 0, summonChar: "木",
                        passive: new SummonPassive { Thorns = 100 }) }),
            }, new[] { "垒", "棘" }, new EnemyDef("靶", Element.Heart, 20, 100));
            Assert.That(run.Battle.Cast("垒"), Is.EqualTo(BattleError.None));
            Assert.That(run.Battle.Cast("棘"), Is.EqualTo(BattleError.None));

            run.Battle.EndTurn();

            Assert.That(run.Battle.Phase, Is.EqualTo(BattlePhase.Won), "荆棘反伤在敌方段打死了最后一只敌人");
            int remaining = run.Battle.ShieldNormal;
            Assert.That(remaining, Is.EqualTo(100), "敌人打的是召唤物,玩家的盾原样留到战斗结束");
            run.AdvanceAfterBattle();
            Assert.That(run.CarriedNormalShield, Is.EqualTo(50));
        }
    }
}
