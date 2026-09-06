using System;
using NUnit.Framework;
using Brushblade.Core;

namespace Brushblade.Core.Tests
{
    /// <summary>封禁(2026-09-05,平衡重做 P0 任务 9)。复用 0 载体的 EffectKind.Silence,
    /// 把语义从「主动机制哑火」扩到「特殊能力全部失效」:护甲归零、被动不触发、主动机制哑火。
    ///
    /// **对 Boss 降级**为「护甲减半」,被动与大招不受影响 —— 同斩杀对 Boss 从「直杀」
    /// 降为「吃双倍」的设计纪律。P2 的载体:灭(火白)· 海(绿)· 澡(紫)· 湮(紫)·
    /// 沐(金)· 淋(橙,2 回合)。</summary>
    public sealed class SuppressTests
    {
        [Test]
        public void Suppress_ZeroesMobArmor()
        {
            var battle = SuppressBattle(armor: 20, isBoss: false);
            battle.Cast("封", 0);
            Assert.That(HitFor(battle), Is.EqualTo(100), "护甲归零,100 全打进去");
        }

        [Test]
        public void Suppress_HalvesBossArmor()
        {
            var battle = SuppressBattle(armor: 60, isBoss: true);
            battle.Cast("封", 0);
            Assert.That(HitFor(battle), Is.EqualTo(70), "Boss 降级:护甲 60 → 30,100 − 30 = 70");
        }

        [Test]
        public void Suppress_OnBoss_DoesNotSilenceAbility()
        {
            var battle = SuppressBattle(armor: 0, isBoss: true, turns: 2, ability: EnemyAbility.Buff);
            battle.Cast("封", 0);
            battle.EndTurn();
            Assert.That(AbilityFired(battle), Is.True,
                "Boss 的大招不受封禁影响 —— 只削护甲");
        }

        [Test]
        public void Suppress_OnMob_SilencesAbility()
        {
            var battle = SuppressBattle(armor: 0, isBoss: false, turns: 2, ability: EnemyAbility.Buff);
            battle.Cast("封", 0);
            battle.EndTurn();
            Assert.That(AbilityFired(battle), Is.False,
                "杂兵的主动机制哑火");
        }

        [Test]
        public void Suppress_OnBoss_EmitsDowngradeEvent()
        {
            var battle = SuppressBattle(armor: 60, isBoss: true);
            battle.Cast("封", 0);
            bool found = false;
            foreach (var e in battle.LastEvents)
                if (e.Kind == BattleEventKind.SuppressDowngraded) found = true;
            Assert.That(found, Is.True,
                "不发事件的话玩家只会以为是 bug —— 卡面写着全禁,Boss 身上却只掉一半甲");
        }

        [Test]
        public void Suppress_Expires()
        {
            var battle = SuppressBattle(armor: 20, isBoss: false);
            battle.Cast("封", 0);
            battle.EndTurn();
            Assert.That(HitFor(battle), Is.EqualTo(80), "到期后护甲 20 回来");
        }

        /// <summary>Boss 的蓄力/大招机制(ResolveBossTurn 的三态 + Silence case 挂状态那一拍
        /// 曾经的「挂上当下就打断蓄力」)不在 grep "IsSilenced(" 找到的 8 处 EnemyAbility 分支
        /// 之列 —— 它们查的不是 enemy.Def.Ability,而是 enemy.IsCharging/ChargeCounter。
        /// 但这两处此前都会在封禁生效期间打断/清零 Boss 的蓄力,直接违反「大招不受影响」,
        /// 所以本任务一并修了。这条测试同时覆盖两点:
        /// ①持续封禁不阻止蓄力计数推进(ResolveBossTurn 那处早退曾经每回合早退一次);
        /// ②蓄力途中重新施加封禁不会把已攒的蓄力打断清零(Silence case 那处曾经的即时打断)。
        /// 不用 RebalanceFixture.Boss —— 它不支持配 BossSkill,这里直接现造一个 EnemyDef。</summary>
        [Test]
        public void Suppress_DoesNotInterruptBossUltimateCharge()
        {
            var graph = RebalanceFixture.Graph(
                RebalanceFixture.Char("封", new EffectDef(EffectKind.Silence, 0, turns: 5)));
            var boss = new EnemyDef("钧", Element.Heart, 100000, 0,
                phases: new[]
                {
                    new BossPhaseDef("钧", Element.Heart, 100000, 0, skill: BossSkill.Deluge),
                });
            var battle = RebalanceFixture.Battle(graph, new[] { "封", "封" }, boss);

            battle.Cast("封", 0);   // 封禁挂上,持续 5 回合
            battle.EndTurn();       // 蓄力计数 0→1(若封禁削了蓄力,这里会卡在 0)
            battle.EndTurn();       // 蓄力计数 1→2,进入蓄力回合(封禁仍在场上)
            battle.Cast("封", 0);   // 蓄力途中再挂一次封禁 —— 不许把刚攒的蓄力打断清零
            battle.EndTurn();       // 该释放大招了

            bool skillCast = false;
            foreach (var e in battle.LastEvents)
                if (e.Kind == BattleEventKind.BossSkillCast) skillCast = true;
            Assert.That(skillCast, Is.True,
                "封禁不许打断 Boss 的蓄力/大招 —— 无论是持续封禁期间的逐回合检查," +
                "还是蓄力途中重新施加封禁,两条路径都不能削大招");
        }

        /// <summary>一场带「封」(封禁 N 回合)与「打」(单体 100)两张字的战斗。
        /// 靶子按 isBoss 选杂兵或 Boss —— Boss 必须带 phases,IsBoss 判的就是 Phases.Count > 0。
        /// 两条 EnemyAbility 测试要造**两只**带 ability 的靶子:Buff 的前置是
        /// HasOtherAliveEnemy,单只时它本来就不发动,那样测试会双双「通过」而什么都没测到。
        /// 被封禁的是 index 0。</summary>
        private static BattleEngine SuppressBattle(int armor, bool isBoss, int turns = 1,
            int enemyAttack = 0, EnemyAbility ability = EnemyAbility.None)
        {
            var graph = RebalanceFixture.Graph(
                RebalanceFixture.Char("封", new EffectDef(EffectKind.Silence, 0, turns: turns)),
                RebalanceFixture.Char("打", new EffectDef(EffectKind.DamageSingle, 100)));
            EnemyDef[] targets = ability == EnemyAbility.None
                ? new[]
                {
                    isBoss
                        ? RebalanceFixture.Boss(attack: enemyAttack, armor: armor)
                        : RebalanceFixture.Mob(attack: enemyAttack, armor: armor),
                }
                : new[]
                {
                    isBoss
                        ? RebalanceFixture.Boss(attack: enemyAttack, armor: armor, ability: ability)
                        : RebalanceFixture.Mob(attack: enemyAttack, armor: armor, ability: ability),
                    isBoss
                        ? RebalanceFixture.Boss(attack: enemyAttack, armor: armor, ability: ability)
                        : RebalanceFixture.Mob(attack: enemyAttack, armor: armor, ability: ability),
                };
            return RebalanceFixture.Battle(graph, new[] { "封", "打", "打" }, targets);
        }

        /// <summary>出「打」并返回敌人掉了多少血。</summary>
        private static int HitFor(BattleEngine battle)
        {
            int before = battle.Enemies[0].Hp;
            battle.Cast("打", 0);
            return before - battle.Enemies[0].Hp;
        }

        /// <summary>index 0(被封禁的那只)的 EnemyAbility 这一回合发动了吗。
        /// 用 EnemyAbility.Buff 造靶子 —— 它给「另一只存活敌人」加攻,
        /// 所以要两只敌人才看得见效果(HasOtherAliveEnemy 是它的前置)。
        ///
        /// ⚠ 两只靶子都带 Buff(brief 的要求,免得单只不发动),这就意味着 index 1
        /// 自己的 Buff **也会**在同一回合独立触发,把 AttackBuff 挂到 index 0 身上 ——
        /// 如果判据是「随便哪只敌人身上有没有 AttackBuff」,index 0 就永远会被 index 1
        /// 的独立动作误判成「发动了」,两条测试因此双双测不出封禁有没有生效(实测:
        /// 按这个判据写,Suppress_OnMob_SilencesAbility 会稳定断言失败,即使
        /// IsAbilitySilenced 实现完全正确)。真正能分辨「index 0 自己有没有发动」的
        /// 只有 index 1 有没有被 index 0 加成 —— index 1 收到 buff 只可能来自 index 0,
        /// 不受 index 1 自己那份独立动作干扰。</summary>
        private static bool AbilityFired(BattleEngine battle) =>
            battle.Enemies[1].Statuses.TotalMagnitude(StatusKind.AttackBuff) > 0;
    }
}
