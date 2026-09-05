using System;
using NUnit.Framework;
using Brushblade.Core;

namespace Brushblade.Core.Tests
{
    /// <summary>魅惑(2026-09-05,平衡重做 P0 任务 10)。被魅惑的敌人这一回合
    /// 攻击**自己阵营**,而不是玩家。
    ///
    /// 与冻结的区别就是定价的依据:冻结只是少一次出手,魅惑把那次出手转成对敌方
    /// 自己的伤害 —— 控制与输出两头都占,故 0.40 > 冻结 0.35。</summary>
    public sealed class CharmTests
    {
        [Test]
        public void Charm_MakesEnemyHitItsOwnSide()
        {
            var battle = CharmBattle(enemyCount: 2, attack: 50);
            battle.Cast("魅", 0);
            int allyHpBefore = battle.Enemies[1].Hp;
            int playerHpBefore = battle.PlayerHp;

            battle.EndTurn();

            Assert.That(battle.Enemies[1].Hp, Is.LessThan(allyHpBefore), "打了自己队友");
            Assert.That(battle.PlayerHp, Is.EqualTo(playerHpBefore), "没打玩家");
        }

        [Test]
        public void Charm_SoleEnemy_DoesNothing()
        {
            var battle = CharmBattle(enemyCount: 1, attack: 50);
            battle.Cast("魅", 0);
            int selfHpBefore = battle.Enemies[0].Hp;
            int playerHpBefore = battle.PlayerHp;

            battle.EndTurn();

            Assert.That(battle.Enemies[0].Hp, Is.EqualTo(selfHpBefore),
                "场上只剩它一只时不自伤 —— 那会让魅惑在单敌局面变成纯伤害");
            Assert.That(battle.PlayerHp, Is.EqualTo(playerHpBefore), "也不许打玩家");
        }

        [Test]
        public void Charm_Expires()
        {
            var battle = CharmBattle(enemyCount: 2, attack: 50);
            battle.Cast("魅", 0);
            battle.EndTurn();

            int playerHpBefore = battle.PlayerHp;
            battle.EndTurn();
            Assert.That(battle.PlayerHp, Is.LessThan(playerHpBefore), "1 回合到期,恢复正常攻击玩家");
        }

        [Test]
        public void Charm_KillingOwnAlly_StillCountsForWin()
        {
            var battle = CharmBattle(enemyCount: 2, attack: 50, allyHp: 10);
            battle.Cast("魅", 0);
            battle.EndTurn();
            Assert.That(battle.Enemies[1].Alive, Is.False, "被魅惑者打死了队友");
            // 死亡必须走同一条 ResolveDefeat 通路 —— 掉落/计数/胜利判定都挂在那里。
            bool sawDeath = false;
            foreach (var e in battle.LastEvents)
                if (e.Kind == BattleEventKind.EnemyDied) sawDeath = true;
            Assert.That(sawDeath, Is.True, "魅惑致死也要发 EnemyDied,否则掉落与计数会静默丢失");
        }

        /// <summary>一场带「魅」(魅惑 N 回合)的战斗。enemyCount 只能是 1 或 2 ——
        /// 单敌那条测的正是「场上只剩它一只时空转」。allyHp 给小值可测「魅惑致死队友」。
        ///
        /// ⚠ 队友(index 1)攻击力固定 0,不吃 attack 参数(与 brief 原稿的差异,
        /// 2026-09-06 变异检查时发现):两只怪速度相同时 ATB 调度器会在**同一次** EndTurn()
        /// 里让两只都出手(见 ActEnemyTurn 的调用顺序),若队友也带攻击力,它会在魅惑者
        /// 之后**正常**打玩家一下,"没打玩家" 那条断言会被队友自己的普攻戳破 ——
        /// 这与魅惑机制本身无关,纯粹是"队友这一步没被魅惑,理应正常出手"这件事的副作用。
        /// 队友只用来当"被魅惑者能不能打到它"的靶子,不需要自己也有输出。</summary>
        private static BattleEngine CharmBattle(int enemyCount, int attack, int turns = 1,
            int allyHp = 100000)
        {
            var graph = RebalanceFixture.Graph(
                RebalanceFixture.Char("魅", new EffectDef(EffectKind.Charm, 0, turns: turns)));
            var enemies = enemyCount == 1
                ? new[] { RebalanceFixture.Mob(attack: attack) }
                : new[] { RebalanceFixture.Mob(attack: attack),
                          RebalanceFixture.Mob(hp: allyHp, attack: 0) };
            return RebalanceFixture.Battle(graph, new[] { "魅", "魅", "魅" }, enemies);
        }
    }
}
