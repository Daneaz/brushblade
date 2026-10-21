using System;
using System.Collections.Generic;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>奇遇两种新代价(2026-10-21):下一战首回合 AP −1(NextBattleSeal)、舍 1 字(LibraryCost)。</summary>
    public class EventCostTests
    {
        private static RecipeGraph Graph() => new(new[]
        {
            new CharDef("木", Element.Wood),
            new CharDef("火", Element.Fire),
            new CharDef("林", Element.Wood, new[] { "木", "木" }),
            new CharDef("焚", Element.Fire, new[] { "林", "火" }, rarity: CardRarity.Purple,
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 18, shape: TargetArea.All) }),
        });

        private static EventDef SealEvent() => new()
        {
            Id = "药铺", Text = "…", Tier = 1,
            Options = new[] { new EventOption { Label = "服药", HpDelta = 5, NextBattleSeal = 1 } },
        };

        private static EventDef DiscardEvent() => new()
        {
            Id = "舍字", Text = "…", Tier = 1,
            Options = new[] { new EventOption { Label = "舍", Ink = 10, LibraryCost = 1 } },
        };

        private static RunEngine Start(EventDef ev, IEnumerable<string> library, bool bossSecond = false)
        {
            var config = new RunConfig
            {
                Encounters = new[]
                {
                    new[] { new EnemyDef("枯", Element.Wood, 4, 2) },
                    new[] { bossSecond
                        ? new EnemyDef("魁", Element.Wood, 400, 1, tier: EnemyTier.Boss)
                        : new EnemyDef("枯", Element.Wood, 400, 1) },
                },
                RewardPool = new[] { "焚" },
                EventPool = new[] { ev },
                EventChancePercent = 100,
            };
            var run = new RunEngine(Graph(), config, new BattleConfig(),
                new List<string>(library), Array.Empty<string>(), 7);
            // 起手字库由构造参数给;用「焚」清掉第一层
            Assert.That(run.Battle.Cast("焚"), Is.EqualTo(BattleError.None));
            run.AdvanceAfterBattle();
            return run;
        }

        [Test]
        public void NextBattleSeal_FirstTurnApMinusOne()
        {
            var run = Start(SealEvent(), new[] { "焚" });
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Event));
            Assert.That(run.ChooseEventOption(0), Is.True);
            run.SkipReward();
            Assert.That(run.Battle.Ap, Is.EqualTo(2), "首回合 −1");
            run.Battle.EndTurn();
            Assert.That(run.Battle.Ap, Is.EqualTo(3), "次回合恢复");
        }

        [Test]
        public void NextBattleSeal_SurvivesSnapshot()
        {
            var run = Start(SealEvent(), new[] { "焚" });
            run.ChooseEventOption(0);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Reward));
            var snap = run.Capture();
            var config = new RunConfig
            {
                Encounters = new[]
                {
                    new[] { new EnemyDef("枯", Element.Wood, 4, 2) },
                    new[] { new EnemyDef("枯", Element.Wood, 400, 1) },
                },
                RewardPool = new[] { "焚" },
                EventPool = new[] { SealEvent() },
                EventChancePercent = 100,
            };
            var restored = RunEngine.Restore(snap, Graph(), config, new BattleConfig(),
                new Dictionary<string, int>());
            restored.SkipReward();
            Assert.That(restored.Battle.Ap, Is.EqualTo(2));
        }

        [Test]
        public void NextBattleSeal_AppliesToBossFloor()
        {
            var run = Start(SealEvent(), new[] { "焚" }, bossSecond: true);
            run.ChooseEventOption(0);
            run.SkipReward();
            Assert.That(run.Battle.Enemies[0].Def.Tier, Is.EqualTo(EnemyTier.Boss));
            Assert.That(run.Battle.Ap, Is.EqualTo(2));
        }

        [Test]
        public void LibraryCost_RemovesChosenChar()
        {
            var run = Start(DiscardEvent(), new[] { "焚", "焚", "林" }); // 打出一张焚后字库 [焚, 林]
            Assert.That(run.ChooseEventOption(0, replaceLibraryIndex: 1), Is.True);
            Assert.That(run.CarriedLibrary, Is.EqualTo(new[] { "焚" }));
        }

        [Test]
        public void LibraryCost_RequiresIndex()
        {
            var run = Start(DiscardEvent(), new[] { "焚", "焚", "林" }); // 打出一张焚后字库 [焚, 林]
            Assert.That(run.ChooseEventOption(0), Is.False);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Event));
            Assert.That(run.CarriedLibrary, Is.EqualTo(new[] { "焚", "林" }));
        }

        [Test]
        public void LibraryCost_LastChar_Locked()
        {
            var run = Start(DiscardEvent(), new[] { "焚", "焚" }); // 打出后只剩一张
            Assert.That(run.IsEventOptionLocked(run.CurrentEvent.Options[0]), Is.True);
            Assert.That(run.ChooseEventOption(0, replaceLibraryIndex: 0), Is.False);
            Assert.That(run.CarriedLibrary.Count, Is.EqualTo(1));
        }
    }
}
