using System;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>关卡结算的顺序:**先奇遇,后选字**(2026-08-30 用户拍板改序)。
    ///
    /// 改之前是「打赢 → 选字 → 掷奇遇 → 下一战」,现在是「打赢 → 掷奇遇 → 选字 → 下一战」。
    ///
    /// 顺序一换,两件事跟着变,都在这里钉住:
    /// 1. 奇遇给的字先进库,所以它可能把库塞满,随后的选字就得走满库替换那条路
    ///    —— 改之前不可能发生(选字永远在奇遇之前)。
    /// 2. 战利品候选改在**进入选字那一刻**才掷,不再是打赢当场掷。奇遇要是动了携带状态,
    ///    掷出来的候选才反映得了。
    ///
    /// 末层(Boss 层)照旧**不走奇遇**:打完 Boss 直接选字 → 结算通关。这条是改序之前
    /// 就有的口径(原 ProceedAfterReward 的第一个分支),改序不该顺手把它也改了。</summary>
    public sealed class RewardEventOrderTests
    {
        private static RecipeGraph Graph() => new(new[]
        {
            new CharDef("木", Element.Wood),
            new CharDef("火", Element.Fire),
            new CharDef("林", Element.Wood, new[] { "木", "木" }),
            new CharDef("炎", Element.Fire, new[] { "火", "火" },
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 12) }),
            new CharDef("焚", Element.Fire, new[] { "林", "火" }, rarity: CardRarity.Purple,
                effects: new[] { new EffectDef(EffectKind.DamageAll, 18) }),
        });

        /// <summary>只有一个选项、只给一个字的奇遇:选完必然给字,便于观察它对随后选字的影响。</summary>
        private static EventDef GivesChar() => new()
        {
            Id = "测字先生",
            Text = "先生请你抽一字卜算。",
            Options = new[] { new EventOption { Label = "求字", GainChar = "炎" } },
        };

        /// <summary>什么都不给的奇遇:测纯粹的相位流转,不掺状态变化。</summary>
        private static EventDef GivesNothing() => new()
        {
            Id = "路人",
            Text = "他只是路过。",
            Options = new[] { new EventOption { Label = "点头" } },
        };

        private static RunConfig Config(int chance, EventDef ev, int encounters = 2) => new()
        {
            Encounters = Enemies(encounters),
            RewardPool = new[] { "炎" },
            EventPool = new[] { ev },
            EventChancePercent = chance,
        };

        private static EnemyDef[][] Enemies(int count)
        {
            var all = new EnemyDef[count][];
            for (int i = 0; i < count; i++)
                all[i] = new[] { new EnemyDef("枯", Element.Wood, 4, 2) };
            return all;
        }

        private static RunEngine Run(int chance, EventDef ev, int encounters = 2,
            BattleConfig battleConfig = null) =>
            new(Graph(), Config(chance, ev, encounters), battleConfig ?? new BattleConfig(),
                new[] { "焚", "焚" }, Array.Empty<string>(), seed: 7);

        private static void Win(RunEngine run)
        {
            Assert.That(run.Battle.Cast("焚"), Is.EqualTo(BattleError.None));
            run.AdvanceAfterBattle();
        }

        // ---- 相位顺序 ----

        [Test]
        public void AfterWin_EventComesFirst()
        {
            // 打赢直接进奇遇,不再先弹战利品
            var run = Run(chance: 100, GivesNothing());
            Win(run);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Event));
            Assert.That(run.CurrentEvent.Id, Is.EqualTo("路人"));
        }

        [Test]
        public void AfterEvent_RewardComesNext()
        {
            // 奇遇选完进选字,而不是直接开下一战
            var run = Run(chance: 100, GivesNothing());
            Win(run);
            Assert.That(run.ChooseEventOption(0), Is.True);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Reward));
            Assert.That(run.BattleIndex, Is.EqualTo(0), "还没开下一战");
        }

        [Test]
        public void AfterReward_NextBattleBegins()
        {
            var run = Run(chance: 100, GivesNothing());
            Win(run);
            run.ChooseEventOption(0);
            run.SkipReward();
            Assert.That(run.Phase, Is.EqualTo(RunPhase.InBattle));
            Assert.That(run.BattleIndex, Is.EqualTo(1));
        }

        [Test]
        public void NoEvent_WinGoesStraightToReward()
        {
            // 没掷中奇遇时,打赢照旧直接进选字
            var run = Run(chance: 0, GivesNothing());
            Win(run);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Reward));
            run.SkipReward();
            Assert.That(run.Phase, Is.EqualTo(RunPhase.InBattle));
            Assert.That(run.BattleIndex, Is.EqualTo(1));
        }

        // ---- 末层照旧不走奇遇 ----

        [Test]
        public void FinalBattle_SkipsEventEvenAt100Percent()
        {
            // 单场配置 = 第一场就是末场:打赢直接选字,不掷奇遇
            var run = Run(chance: 100, GivesNothing(), encounters: 1);
            Win(run);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Reward), "末层不走奇遇");
        }

        [Test]
        public void FinalBattle_RewardLeadsToRunWon()
        {
            var run = Run(chance: 100, GivesNothing(), encounters: 1);
            Win(run);
            run.SkipReward();
            Assert.That(run.Phase, Is.EqualTo(RunPhase.RunWon));
        }

        // ---- 溢出仍要接回选字 ----

        [Test]
        public void OverflowResolution_LeadsToReward()
        {
            // 奇遇给的部件超池上限 → 逐个决议 → 决议完进**选字**(而不是直接下一战)
            var overflowing = new EventDef
            {
                Id = "背包客",
                Text = "他塞给你一堆东西。",
                Options = new[] { new EventOption { Label = "收下", GainComponents = new[] { "木", "火" } } },
            };
            var run = Run(chance: 100, overflowing,
                battleConfig: new BattleConfig { PoolCapacity = 0 });
            Win(run);
            Assert.That(run.ChooseEventOption(0), Is.True);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.EventOverflow));
            run.ResolveOverflowSkip();
            run.ResolveOverflowSkip();
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Reward), "溢出决议完接的是选字");
        }

        // ---- 顺序换位带来的新交互 ----

        [Test]
        public void EventCharCanFillLibrary_ThenRewardNeedsReplacement()
        {
            // 这是改序**新造出来**的情形:奇遇先给字把库塞满,随后的选字只能走替换。
            // 容量 2、起手带两张「焚」,出掉一张打赢后库里剩 1 张 —— 奇遇再给一张就满了
            var run = Run(chance: 100, GivesChar(),
                battleConfig: new BattleConfig { LibraryCapacity = 2 });
            Win(run);
            Assert.That(run.ChooseEventOption(0), Is.True);
            Assert.That(run.CarriedLibrary.Count, Is.EqualTo(2), "库被奇遇填满");
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Reward));
            Assert.That(run.PickReward(0), Is.False, "满库:直取应当被拒,交给替换那条路");
        }

        [Test]
        public void RewardOptionsRolledAfterEvent()
        {
            // 候选在**进入选字那一刻**才掷:奇遇还开着的时候不该已经有候选摆在那儿
            var run = Run(chance: 100, GivesNothing());
            Win(run);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Event));
            Assert.That(run.RewardOptions.Count, Is.EqualTo(0), "奇遇阶段还没掷候选");
            run.ChooseEventOption(0);
            Assert.That(run.RewardOptions.Count, Is.GreaterThan(0), "进选字才掷");
        }
    }
}
