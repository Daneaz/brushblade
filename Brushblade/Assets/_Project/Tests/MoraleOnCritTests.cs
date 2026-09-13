using System;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>金脉 L2「锋芒」(spec 2026-09-13 §2.3):玩家暴击时战意 +1 层。
    ///
    /// 两条限制各有守卫:
    /// ① **每张字至多 +1 层** —— DamageAll 对每个目标各摇一次暴击,不限制的话一张群攻字
    ///    就能顶满上限,战意从「维持型资源」退化成「开局一张群攻就满」。
    /// ② **召唤物的暴击不算** —— 它们读自己的暴击袋子(RollCritForSummon),
    ///    算进来会让木+金 build 白拿双份。
    ///
    /// 用 PlayerCritChance = 100 让暴击必然发生:RollCritWith 在 ≥100 时短路、一次随机都不摇,
    /// 所以这几条测试不会因为随机流而抖动。</summary>
    public class MoraleOnCritTests
    {
        private static RecipeGraph Graph() => new(new[]
        {
            new CharDef("刺", Element.Metal,
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 10) }),
            new CharDef("扫", Element.Metal,
                effects: new[] { new EffectDef(EffectKind.DamageAll, 10) }),
        });

        private static BattleEngine Engine(int moraleOnCrit, int playerCrit = 100,
            int enemyCount = 4) =>
            new(Graph(), new BattleConfig
                {
                    DropTable = new[] { "金" }, PlayerMaxHp = 500, ApPerTurn = 20,
                    PlayerCritChance = playerCrit,
                    MoraleOnCrit = moraleOnCrit,
                },
                new[] { "刺", "刺", "扫", "扫" }, Array.Empty<string>(),
                Enumerable.Range(0, enemyCount)
                    .Select(i => new EnemyDef($"靶{i}", Element.Heart, 9000, 0))
                    .ToArray(),
                seed: 1);

        private static int Morale(BattleEngine engine) =>
            engine.PlayerStatuses.TotalMagnitude(StatusKind.Morale);

        [Test]
        public void SingleTargetCrit_AddsOneStack()
        {
            var engine = Engine(1);
            Assert.That(Morale(engine), Is.EqualTo(0), "前提:起手 0 层");
            Assert.That(engine.Cast("刺", 0), Is.EqualTo(BattleError.None));
            Assert.That(Morale(engine), Is.EqualTo(1));
        }

        [Test]
        public void PerkOff_NeverAddsMorale()
        {
            var engine = Engine(0);
            Assert.That(engine.Cast("刺", 0), Is.EqualTo(BattleError.None));
            Assert.That(Morale(engine), Is.EqualTo(0));
        }

        [Test]
        public void NoCrit_AddsNothing()
        {
            var engine = Engine(1, playerCrit: 0);
            Assert.That(engine.Cast("刺", 0), Is.EqualTo(BattleError.None));
            Assert.That(Morale(engine), Is.EqualTo(0), "没暴击就没有战意");
        }

        [Test]
        public void AoeCritOnFourTargets_StillAddsOnlyOneStack()
        {
            var engine = Engine(1, enemyCount: 4);
            Assert.That(engine.Cast("扫"), Is.EqualTo(BattleError.None));
            Assert.That(Morale(engine), Is.EqualTo(1),
                "四个目标各暴击一次,但一张字只兑现一层");
        }

        [Test]
        public void TwoCards_EachAddOneStack()
        {
            var engine = Engine(1);
            Assert.That(engine.Cast("刺", 0), Is.EqualTo(BattleError.None));
            Assert.That(engine.Cast("刺", 0), Is.EqualTo(BattleError.None));
            Assert.That(Morale(engine), Is.EqualTo(2), "限制的是每张字,不是每回合");
        }

        [Test]
        public void StacksAreCappedByMoraleCap()
        {
            var engine = new BattleEngine(Graph(), new BattleConfig
                {
                    DropTable = new[] { "金" }, PlayerMaxHp = 500, ApPerTurn = 20,
                    PlayerCritChance = 100, MoraleOnCrit = 1, MoraleCap = 2,
                },
                new[] { "刺", "刺", "刺", "刺" }, Array.Empty<string>(),
                new[] { new EnemyDef("靶", Element.Heart, 9000, 0) }, seed: 1);
            for (int i = 0; i < 4; i++)
                Assert.That(engine.Cast("刺", 0), Is.EqualTo(BattleError.None));
            Assert.That(Morale(engine), Is.EqualTo(2), "不越过 MoraleCap");
        }

        [Test]
        public void TierFourRaisesTheCeilingForThisToo()
        {
            var engine = new BattleEngine(Graph(), new BattleConfig
                {
                    DropTable = new[] { "金" }, PlayerMaxHp = 500, ApPerTurn = 20,
                    PlayerCritChance = 100, MoraleOnCrit = 1, MoraleCap = 7,
                },
                new[] { "刺", "刺", "刺", "刺", "刺", "刺", "刺", "刺" }, Array.Empty<string>(),
                new[] { new EnemyDef("靶", Element.Heart, 9000, 0) }, seed: 1);
            for (int i = 0; i < 8; i++)
                Assert.That(engine.Cast("刺", 0), Is.EqualTo(BattleError.None));
            Assert.That(Morale(engine), Is.EqualTo(7), "鏖战抬到 7 后照样能攒到 7");
        }

        // ---- 召唤物的暴击不算(spec §2.3)----

        private static RecipeGraph SummonGraph() => new(new[]
        {
            new CharDef("木", Element.Wood),
            new CharDef("卒", Element.Wood,
                effects: new[] { new EffectDef(EffectKind.Summon, 100, summonCount: 1, summonAttack: 20, summonChar: "木") }),
            // 锋:暴击 +100 个百分点 = 必暴(照 BuffTargetTests 的写法,取 100 而不是真实字表的值,
            // RollCritWith 在 ≥100 时短路不摇骰)
            new CharDef("锋", Element.Metal,
                effects: new[] { new EffectDef(EffectKind.CritBuff, 100) }),
        });

        private static BattleEngine SummonEngine(int moraleOnCrit) =>
            new(SummonGraph(), new BattleConfig
                {
                    DropTable = new[] { "木" }, PlayerMaxHp = 500, ApPerTurn = 20,
                    MoraleOnCrit = moraleOnCrit,
                },
                new[] { "卒", "锋" }, Array.Empty<string>(),
                new[] { new EnemyDef("靶", Element.Heart, 3000, 0) }, seed: 1);

        [Test]
        public void SummonCrit_DoesNotGrantMorale()
        {
            // 给召唤物挂必暴(锋 CritBuff),它出手那一拍必然 RollCritForSummon 命中——
            // 但那条判定只改召唤物自己的伤害,一字不碰玩家战意(RollCrit 才接锋芒)。
            var engine = SummonEngine(1);
            Assert.That(engine.Cast("卒"), Is.EqualTo(BattleError.None));
            Assert.That(engine.Cast("锋", allySlot: 0), Is.EqualTo(BattleError.None));
            Assert.That(Morale(engine), Is.EqualTo(0), "前提:两次施法都没有玩家攻击暴击");
            engine.EndTurn();   // 召唤物这一拍必暴
            Assert.That(Morale(engine), Is.EqualTo(0),
                "召唤物暴击不读玩家战意——RollCritForSummon 与锋芒(RollCrit)是两条互不相连的判定");
        }
    }
}
