using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>护盾回合末清空与留存护盾(spec v7 §3.1,用户拍板 U1,2026-10-04)。
    /// 「回合结束」= 一整轮结束,即**玩家下一回合开始时**清空普通护盾(玩家普通桶 + 全部召唤物护盾);
    /// 留存护盾(<c>_shieldPersist</c>)不清。本场第一个玩家回合不清(跨场带入 / 开局效果给的盾
    /// 写在构造函数里,第一回合开始就清的话它们一拍都挡不到)。</summary>
    public class ShieldTurnClearTests
    {
        private static RecipeGraph Graph() => new(new[]
        {
            new CharDef("木", Element.Wood),
            new CharDef("垒", Element.Earth,
                effects: new[] { new EffectDef(EffectKind.Shield, 100) }),
            new CharDef("㙓", Element.Earth,
                effects: new[] { new EffectDef(EffectKind.Shield, 200, persistOnce: true) }),
            new CharDef("兵", Element.Wood,
                effects: new[] { new EffectDef(EffectKind.Summon, 100, summonCount: 1, summonAttack: 3, summonChar: "木") }),
            new CharDef("崩", Element.Earth,
                effects: new[] { new EffectDef(EffectKind.ShieldAll, 17) }),
        });

        private static readonly BattleConfig Config = new() { DropTable = new[] { "木" }, PlayerMaxHp = 500 };

        private static BattleEngine Engine(string[] library, int enemyAttack = 0,
            int normal = 0, int persist = 0) =>
            new(Graph(), Config, library, Array.Empty<string>(),
                new[] { new EnemyDef("靶", Element.Heart, 3000, enemyAttack) }, 1,
                startingNormalShield: normal, startingPersistShield: persist);

        [Test]
        public void NormalShield_ClearedAtNextPlayerTurnStart()
        {
            var engine = Engine(new[] { "垒" }, enemyAttack: 30);
            Assert.That(engine.Cast("垒"), Is.EqualTo(BattleError.None));
            Assert.That(engine.PlayerShield, Is.EqualTo(100));
            int turn = engine.Turn;

            engine.EndTurn();

            Assert.That(engine.Turn, Is.GreaterThan(turn), "确认真的进了下一个玩家回合");
            Assert.That(engine.PlayerShield, Is.EqualTo(0), "挨了 30 剩下的普通盾也被清掉");
            Assert.That(engine.ShieldNormal, Is.EqualTo(0));
        }

        [Test]
        public void PersistShield_SurvivesTurnClear()
        {
            var engine = Engine(new[] { "㙓" });
            Assert.That(engine.Cast("㙓"), Is.EqualTo(BattleError.None));
            Assert.That(engine.ShieldPersist, Is.EqualTo(200));
            int turn = engine.Turn;

            engine.EndTurn();

            Assert.That(engine.Turn, Is.GreaterThan(turn));
            Assert.That(engine.ShieldPersist, Is.EqualTo(200), "留存护盾不清");
            Assert.That(engine.ShieldNormal, Is.EqualTo(0));
        }

        [Test]
        public void NormalCleared_PersistKept_WhenBothPresent()
        {
            var engine = Engine(new[] { "垒", "㙓" });
            Assert.That(engine.Cast("垒"), Is.EqualTo(BattleError.None));
            Assert.That(engine.Cast("㙓"), Is.EqualTo(BattleError.None));
            Assert.That(engine.PlayerShield, Is.EqualTo(300));

            engine.EndTurn();

            Assert.That(engine.PlayerShield, Is.EqualTo(200), "只剩留存桶");
        }

        [Test]
        public void SummonShield_ClearedAtNextPlayerTurnStart()
        {
            var engine = Engine(new[] { "兵", "崩" });
            Assert.That(engine.Cast("兵"), Is.EqualTo(BattleError.None));
            Assert.That(engine.Cast("崩"), Is.EqualTo(BattleError.None));
            var alive = engine.Summons.Where(s => s != null && s.Alive).ToList();
            Assert.That(alive, Is.Not.Empty);
            Assert.That(alive.All(s => s.Shield > 0), Is.True, "前置:召唤物确实有盾");

            engine.EndTurn();

            foreach (var summon in engine.Summons.Where(s => s != null && s.Alive))
                Assert.That(summon.Shield, Is.EqualTo(0), "召唤物护盾同样在下一玩家回合开始清空");
        }

        [Test]
        public void FirstPlayerTurn_KeepsCarriedInShield_ThenClearsNextTurn()
        {
            var engine = Engine(new[] { "木" }, normal: 100, persist: 50);
            Assert.That(engine.Turn, Is.EqualTo(1));
            Assert.That(engine.ShieldNormal, Is.EqualTo(100), "第一个玩家回合不清带入的盾");
            Assert.That(engine.ShieldPersist, Is.EqualTo(50));

            engine.EndTurn();

            Assert.That(engine.ShieldNormal, Is.EqualTo(0), "第二回合开始才清");
            Assert.That(engine.ShieldPersist, Is.EqualTo(50));
        }

        [Test]
        public void Snapshot_RoundTripsPlayerTurnsStarted()
        {
            var engine = Engine(new[] { "垒" });
            var first = engine.Capture();
            Assert.That(first.PlayerTurnsStarted, Is.EqualTo(1), "构造函数里开了第一个玩家回合");

            engine.EndTurn();
            var snapshot = engine.Capture();
            Assert.That(snapshot.PlayerTurnsStarted, Is.EqualTo(2));

            // 还原后再进下一回合仍要清盾:计数没丢,不会被当成「第一回合」
            var defs = new Dictionary<string, EnemyDef> { ["靶"] = new EnemyDef("靶", Element.Heart, 3000, 0) };
            var restored = BattleEngine.Restore(snapshot, Graph(), Config, null, defs);
            Assert.That(restored.Capture().PlayerTurnsStarted, Is.EqualTo(2));
        }

        [Test]
        public void Restored_StillClearsOnNextTurn()
        {
            // 第一回合带盾存档,还原后进下一回合:计数 = 1(> 0)所以要清;若计数丢成 0 就会误当「第一回合」不清
            var engine = Engine(new[] { "木" }, normal: 100);
            var defs = new Dictionary<string, EnemyDef> { ["靶"] = new EnemyDef("靶", Element.Heart, 3000, 0) };
            var restored = BattleEngine.Restore(engine.Capture(), Graph(), Config, null, defs);
            Assert.That(restored.ShieldNormal, Is.EqualTo(100));

            restored.EndTurn();

            Assert.That(restored.ShieldNormal, Is.EqualTo(0));
        }

        [Test]
        public void ShieldRecoil_RemovedWhenNormalShieldClearedAndNoPersist()
        {
            var engine = Engine(new[] { "木" }, normal: 100);
            engine.EndTurn();
            Assert.That(engine.PlayerStatuses.Has(StatusKind.ShieldRecoil), Is.False);
        }

        /// <summary>战斗在敌人回合结束(胜利)时剩余护盾按 ShieldCarryPercent 带走:
        /// 带走的是清空前、战斗结束那一刻剩下的量,清盾规则不改这一点。</summary>
        [Test]
        public void Run_CarriesRemainingShieldAtBattleEnd()
        {
            var graph = new RecipeGraph(new[]
            {
                new CharDef("木", Element.Wood),
                new CharDef("垒", Element.Earth,
                    effects: new[] { new EffectDef(EffectKind.Shield, 100) }),
                new CharDef("焚", Element.Fire,
                    effects: new[] { new EffectDef(EffectKind.DamageSingle, 100, shape: TargetArea.All) }),
            });
            var run = new RunEngine(graph,
                new RunConfig
                {
                    Encounters = new[] { new[] { new EnemyDef("枯", Element.Wood, 4, 2) },
                                         new[] { new EnemyDef("枯", Element.Wood, 4, 2) } },
                    RewardPool = new[] { "木" },
                },
                new BattleConfig { DropTable = new[] { "木" }, PlayerMaxHp = 500, ShieldCarryPercent = 50 },
                startingLibrary: new[] { "垒", "焚" }, startingPool: Array.Empty<string>(), seed: 7);
            Assert.That(run.Battle.Cast("垒"), Is.EqualTo(BattleError.None));
            Assert.That(run.Battle.Cast("焚"), Is.EqualTo(BattleError.None));
            Assert.That(run.Battle.Phase, Is.EqualTo(BattlePhase.Won));

            run.AdvanceAfterBattle();

            Assert.That(run.CarriedNormalShield, Is.EqualTo(50), "100 × 50%");
        }
    }
}
