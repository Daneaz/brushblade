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

        /// <summary>终审修复项 2(2026-09-06):回合数不吃卡等级。此前用
        /// <c>MetaRules.ScaleTurnsByCardLevel</c> 缩放,10 级卡会把 1 回合的魅惑
        /// 缩成 1+10/5=3 回合,打穿 spec §2.3 的封禁定价梯度(卡 10 级的绿档「花」
        /// 会比橙档「淋」买的 2 回合封禁还长)。改法与 Silence/Blind/Freeze 同口径:
        /// 直接读 <c>effect.Turns</c>,卡等级只影响数值(Value),不影响回合数。</summary>
        [Test]
        public void Charm_TurnsDoNotScaleWithCardLevel()
        {
            var graph = RebalanceFixture.Graph(
                RebalanceFixture.Char("魅", new EffectDef(EffectKind.Charm, 0, turns: 1)));
            var battle = new BattleEngine(graph,
                new BattleConfig { PlayerMaxHp = RebalanceFixture.BaseMaxHp, PlayerAttack = 100 },
                new[] { "魅" }, Array.Empty<string>(),
                new[] { RebalanceFixture.Mob(attack: 50), RebalanceFixture.Mob(attack: 0) },
                seed: 1,
                cardLevels: new System.Collections.Generic.Dictionary<string, int> { ["魅"] = 10 });

            battle.Cast("魅", 0);

            var charm = battle.Enemies[0].Statuses.Find(StatusKind.Charm);
            Assert.That(charm, Is.Not.Null, "夹具前提:魅惑必须已经挂上");
            Assert.That(charm.TurnsLeft, Is.EqualTo(1),
                "卡 10 级不该把 1 回合的魅惑缩放成 1+10/5=3 回合");
        }

        /// <summary>终审修复项 3(2026-09-06):被魅惑的敌人打自己队友这一记,不该算成
        /// 「我方主动的挥击」—— <c>allowBarb</c> 缺省 <c>true</c> 时,若队友带铁画
        /// (<c>EnemyAbility.Barb</c>),铁画的受击反噬会走 <c>DamagePlayerDirect</c>
        /// 打到一个全程没出手的玩家身上。改法是显式传 <c>allowBarb: false</c>。</summary>
        [Test]
        public void Charm_DoesNotTriggerBarbRecoilOnPlayer()
        {
            var graph = RebalanceFixture.Graph(
                RebalanceFixture.Char("魅", new EffectDef(EffectKind.Charm, 0, turns: 1)));
            var enemies = new[]
            {
                RebalanceFixture.Mob(attack: 50),
                RebalanceFixture.Mob(hp: 100000, attack: 0, ability: EnemyAbility.Barb),
            };
            var battle = RebalanceFixture.Battle(graph, new[] { "魅", "魅", "魅" }, enemies);
            battle.Cast("魅", 0);
            int playerHpBefore = battle.PlayerHp;

            battle.EndTurn();

            Assert.That(battle.PlayerHp, Is.EqualTo(playerHpBefore),
                "被魅惑的杂兵打自己队友(带铁画)不该反噬到没出手的玩家");
        }

        /// <summary>终审修复项 3(2026-09-06):<c>attackerBag</c> 缺省 <c>null</c> 时,
        /// <c>EffectiveEnemyDefense</c> 会落到 <c>_playerStatuses</c>——玩家身上的穿透
        /// (锐)会帮被魅惑的敌人破它队友的甲。改法是显式传 <c>attackerBag: enemy.Statuses</c>
        /// (攻击者/被魅惑者自己的袋子,目前恒为空)。</summary>
        [Test]
        public void Charm_DamageIgnoresPlayersPierceBuff()
        {
            var graph = RebalanceFixture.Graph(
                RebalanceFixture.Char("魅", new EffectDef(EffectKind.Charm, 0, turns: 1)),
                RebalanceFixture.Char("锐", new EffectDef(EffectKind.PierceBuff, 20)));
            var enemies = new[]
            {
                RebalanceFixture.Mob(attack: 50),
                RebalanceFixture.Mob(hp: 100000, attack: 0, armor: 8),
            };
            var battle = RebalanceFixture.Battle(graph, new[] { "魅", "锐" }, enemies);

            battle.Cast("锐");     // 玩家身上挂 20 点穿透
            battle.Cast("魅", 0);
            int allyHpBefore = battle.Enemies[1].Hp;

            battle.EndTurn();

            Assert.That(battle.Enemies[1].Hp, Is.EqualTo(allyHpBefore - (50 - 8)),
                "应扣 50-8=42(队友自身 8 点甲全额生效);玩家的穿透不该帮被魅惑的敌人破队友的甲");
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
