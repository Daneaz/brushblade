using System;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>斩首(spec 2026-10-18 §4):头领 / 精英 / Boss 阵亡 → 其余存活敌人挂慑(Curse)30%、2 回合,
    /// SourceId 固定 <c>#behead</c>,同源取最强不叠;小怪阵亡不触发;死的是最后一只敌人时不施加、不播报;
    /// Boss 破阶不触发(只在 ResolveDefeat 真死时)。</summary>
    public class BeheadTests
    {
        private const string BeheadSource = "#behead";

        private static RecipeGraph Graph() => new(new[]
        {
            new CharDef("刺", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 100) }),
            new CharDef("扫", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 100, shape: TargetArea.All) }),
            new CharDef("弱", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.Weaken, 50, turns: 3) }),
        });

        private static EnemyDef Mob(string id, int hp, EnemyTier tier = EnemyTier.Minion) =>
            new(id, Element.Heart, hp, 100, tier: tier);

        private static BattleEngine Engine(params EnemyDef[] enemies) =>
            new(Graph(), new BattleConfig { DropTable = new[] { "刺" }, PlayerMaxHp = 500, ApPerTurn = 20, BossPhaseJitterPercent = 0 },
                new[] { "刺", "刺", "扫", "弱" }, Array.Empty<string>(), enemies, seed: 1);

        private static StatusEffect[] BeheadOn(BattleEngine engine, int index) =>
            engine.Enemies[index].Statuses.All
                .Where(s => s.Kind == StatusKind.Curse && s.SourceId == BeheadSource).ToArray();

        private static int BeheadEvents(BattleEngine engine) =>
            engine.LastEvents.Count(e => e.Kind == BattleEventKind.Behead);

        [Test]
        public void Behead_AppendedAtEnd()
        {
            Assert.That((int)BattleEventKind.Behead, Is.EqualTo((int)BattleEventKind.StatusApplied + 1));
            // 斩获掉字再追加 CharGrantBlocked(KillDrawTests 守它的位置)
        }

        [TestCase(EnemyTier.Leader)]
        [TestCase(EnemyTier.Elite)]
        [TestCase(EnemyTier.Boss)]
        public void HighTierDeath_CursesEveryOtherLivingEnemy(EnemyTier tier)
        {
            var engine = Engine(Mob("首", 10, tier), Mob("甲", 1000), Mob("乙", 1000));
            Assert.That(engine.Cast("刺", 0), Is.EqualTo(BattleError.None));
            Assert.That(engine.Enemies[0].Alive, Is.False, "前提:0 号被打死");
            foreach (int i in new[] { 1, 2 })
            {
                var curse = BeheadOn(engine, i);
                Assert.That(curse.Length, Is.EqualTo(1), $"{i} 号挂一条斩首慑");
                Assert.That(curse[0].Magnitude, Is.EqualTo(30));
                Assert.That(curse[0].TurnsLeft, Is.EqualTo(2));
                Assert.That(engine.Enemies[i].Attack, Is.EqualTo(70), "攻击 −30%");
            }
            Assert.That(BeheadOn(engine, 0).Length, Is.EqualTo(0), "尸体不挂");
            Assert.That(BeheadEvents(engine), Is.EqualTo(1), "播报一条斩首");
            var behead = engine.LastEvents.First(e => e.Kind == BattleEventKind.Behead);
            Assert.That(behead.TargetIndex, Is.EqualTo(0), "播报锚在阵亡者");
            Assert.That(engine.LastEvents.Count(e => e.Kind == BattleEventKind.StatusApplied
                && e.Amount == (int)StatusKind.Curse), Is.EqualTo(2), "走现有上状态事件");
        }

        [Test]
        public void MinionDeath_DoesNotTrigger()
        {
            var engine = Engine(Mob("卒", 10), Mob("甲", 1000));
            Assert.That(engine.Cast("刺", 0), Is.EqualTo(BattleError.None));
            Assert.That(engine.Enemies[0].Alive, Is.False);
            Assert.That(BeheadOn(engine, 1).Length, Is.EqualTo(0));
            Assert.That(BeheadEvents(engine), Is.EqualTo(0));
        }

        [Test]
        public void LastEnemyDeath_NoCurseNoBroadcast()
        {
            var engine = Engine(Mob("首", 10, EnemyTier.Leader), Mob("卒", 10));
            Assert.That(engine.Cast("刺", 1), Is.EqualTo(BattleError.None));
            Assert.That(engine.Cast("刺", 0), Is.EqualTo(BattleError.None));
            Assert.That(engine.Enemies.All(e => !e.Alive), Is.True, "前提:全灭");
            Assert.That(BeheadEvents(engine), Is.EqualTo(0), "最后一只阵亡不播报");
            Assert.That(BeheadOn(engine, 1).Length, Is.EqualTo(0), "已死的不挂");
        }

        [Test]
        public void StrongerExistingCurse_Wins()
        {
            var engine = Engine(Mob("首", 10, EnemyTier.Leader), Mob("甲", 1000));
            Assert.That(engine.Cast("弱", 1), Is.EqualTo(BattleError.None));
            Assert.That(engine.Enemies[1].Attack, Is.EqualTo(50), "前提:已有 50% 慑");
            Assert.That(engine.Cast("刺", 0), Is.EqualTo(BattleError.None));
            Assert.That(BeheadOn(engine, 1).Length, Is.EqualTo(1), "斩首慑另挂一条(不同源并存)");
            Assert.That(engine.Enemies[1].ActiveCurse(), Is.EqualTo(50), "多源取最强,不相加");
            Assert.That(engine.Enemies[1].Attack, Is.EqualTo(50));
        }

        [Test]
        public void SeveralHighTierDeathsInOneSweep_DoNotStack()
        {
            var engine = Engine(Mob("首", 10, EnemyTier.Leader), Mob("次", 10, EnemyTier.Boss), Mob("甲", 1000));
            Assert.That(engine.Cast("扫", 0), Is.EqualTo(BattleError.None));
            Assert.That(engine.Enemies[0].Alive || engine.Enemies[1].Alive, Is.False, "前提:两只都死");
            var curse = BeheadOn(engine, 2);
            Assert.That(curse.Length, Is.EqualTo(1), "同源只留一条");
            Assert.That(curse[0].Magnitude, Is.EqualTo(30), "不叠成 60");
            Assert.That(curse[0].TurnsLeft, Is.EqualTo(2), "不叠成 4 回合");
            Assert.That(engine.Enemies[2].Attack, Is.EqualTo(70));
            Assert.That(BeheadEvents(engine), Is.EqualTo(2), "各自触发、各飘一次");
        }

        [Test]
        public void BossPhaseBreak_DoesNotTrigger()
        {
            var boss = new EnemyDef("排山", Element.Heart, 10, 100, tier: EnemyTier.Boss,
                phases: new[]
                {
                    new BossPhaseDef("排", Element.Heart, 50, 100),
                    new BossPhaseDef("山", Element.Heart, 1000, 100),
                });
            var engine = Engine(boss, Mob("甲", 1000));
            Assert.That(engine.Cast("刺", 0), Is.EqualTo(BattleError.None));
            Assert.That(engine.Enemies[0].Alive, Is.True, "前提:只是破阶");
            Assert.That(engine.Enemies[0].PhaseIndex, Is.EqualTo(1), "前提:进入第二阶段");
            Assert.That(BeheadOn(engine, 1).Length, Is.EqualTo(0));
            Assert.That(BeheadEvents(engine), Is.EqualTo(0));
        }
    }
}
