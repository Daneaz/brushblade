using System;
using System.Collections.Generic;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>择伐(木 L4,2026-09-13):召唤物按生克三档择敌。
    /// 裁定本身的单测在 TargetingTests;这里钉的是「从技能树到引擎这条线接没接上」。</summary>
    public class CounterTargetingTests
    {
        // 与 PerkInjectionTests.cs:14 同款的注入入口。
        private static BattleConfig Build(MetaState meta) =>
            MetaRules.BuildBattleConfig(meta, Array.Empty<string>());

        [Test]
        public void UnlockingWoodFour_TurnsOnCounterTargeting()
        {
            var meta = new MetaState();
            meta.UnlockedPerks.Add("wood_4");
            Assert.That(Build(meta).CounterTargeting, Is.True);
        }

        [Test]
        public void EmptySave_LeavesCounterTargetingOff()
        {
            Assert.That(Build(new MetaState()).CounterTargeting, Is.False,
                "缺省关 —— 一条没点时引擎行为与改前一致");
        }

        [Test]
        public void OtherBranches_DoNotTurnItOn()
        {
            var meta = new MetaState();
            meta.UnlockedPerks.Add("metal_4");
            meta.UnlockedPerks.Add("fire_4");
            Assert.That(Build(meta).CounterTargeting, Is.False);
        }

        // ---- 引擎侧接线(BattleEngine.StrikeOnceWithSummon → Targeting 的 counterTargeting 实参)----
        // 上面三条只证明「配置字段算对了」;配置算对而没人读它,正是 PerkInjection 那一批守的洞。
        // 下面两条把 BattleConfig.CounterTargeting 一路带到「召唤物这一拍打了谁」。

        private static RecipeGraph Graph() => new(new[]
        {
            // 召唤物的元素继承**这张牌**(木),于是:土 = 我克它(档 2),金 = 它克我(档 0)。
            new CharDef("甲", Element.Wood, effects: new[] { new EffectDef(EffectKind.Summon, 400,
                summonCount: 1, summonAttack: 30, summonChar: "木") }),
        });

        /// <summary>两只怪同排(都是缺省前排)、同血、不还手 —— 除元素外一切相同,
        /// 于是「打了谁」的差别只可能来自三档。下标 0 = 土(该打),1 = 金(不该打)。</summary>
        private static BattleEngine Battle(bool counterTargeting, int seed) =>
            new(Graph(), new BattleConfig { PlayerMaxHp = 500, CounterTargeting = counterTargeting },
                new[] { "甲" }, Array.Empty<string>(),
                new[] { new EnemyDef("垚", Element.Earth, 9999, 0),
                        new EnemyDef("钅", Element.Metal, 9999, 0) }, seed: seed);

        /// <summary>召一只,然后一直 EndTurn 到它出手为止,返回它打的那只敌人的下标。</summary>
        private static int FirstSummonTarget(BattleEngine engine)
        {
            Assert.That(engine.Cast("甲", 0), Is.EqualTo(BattleError.None));
            for (int turn = 0; turn < 12 && engine.Phase == BattlePhase.PlayerTurn; turn++)
            {
                engine.EndTurn();
                foreach (var e in engine.LastEvents)
                    if (e.Kind == BattleEventKind.SummonAttack) return e.TargetIndex;
            }
            Assert.Fail("十二个回合里召唤物一次都没出手");
            return -1;
        }

        [Test]
        public void CounterTargetingOn_SummonAlwaysStrikesTheElementItBeats()
        {
            // 换种子重复:择伐是硬筛,土那只是候选池里唯一的最高档,任何种子都只能是它。
            for (int seed = 1; seed <= 20; seed++)
                Assert.That(FirstSummonTarget(Battle(true, seed)), Is.EqualTo(0),
                    $"seed {seed}:木召唤物该打土,不该打克它的金");
        }

        [Test]
        public void CounterTargetingOff_SummonStillReachesTheOtherEnemy()
        {
            // 反面:关着时两只都摇得到。没有这一条,上面那条在「引擎压根没接线、
            // 而随机恰好总挑下标 0」的世界里也会绿。
            var seen = new HashSet<int>();
            for (int seed = 1; seed <= 20; seed++) seen.Add(FirstSummonTarget(Battle(false, seed)));
            Assert.That(seen.Contains(1), Is.True, "关着时金那只也该挨得到打");
        }
    }
}
