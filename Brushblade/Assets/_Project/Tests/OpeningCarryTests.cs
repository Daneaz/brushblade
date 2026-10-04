using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using Brushblade.Data;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>开局效果表跨段携带(Plan A 裁定 R7):无尽每段 new RunEngine,携带态经 EndlessSaveState。</summary>
    public class OpeningCarryTests
    {
        private static RunConfig Config(int battles) => new RunConfig
        {
            Encounters = Enumerable.Range(0, battles).Select(_ => new[] { RebalanceFixture.Mob(hp: 20) }).ToArray(),
            RewardPool = new[] { "甲" },
        };

        private static BattleConfig BattleCfg() => new BattleConfig { PlayerMaxHp = 500, PlayerAttack = 100 };

        private static void WinAndAdvance(RunEngine run)
        {
            run.Battle.Cast("甲", 0);
            run.AdvanceAfterBattle();
            while (run.Phase == RunPhase.Reward) run.SkipReward();
        }

        [Test]
        public void StartingOpenings_ApplyInFirstBattle_AndAreCloned()
        {
            var opening = new OpeningEffect { SourceCharId = "甲", Element = Element.Metal, Kind = EffectKind.Morale, Value = 2, BattlesLeft = 2 };
            var input = new List<OpeningEffect> { opening };
            var run = new RunEngine(RebalanceFixture.Graph(), Config(2), BattleCfg(),
                new[] { "甲", "甲" }, Array.Empty<string>(), seed: 1, startingOpenings: input);
            Assert.That(run.Battle.PlayerStatuses.TotalMagnitude(StatusKind.Morale), Is.EqualTo(2), "新段第一场开局生效");
            WinAndAdvance(run);
            Assert.That(opening.BattlesLeft, Is.EqualTo(2), "入参未被 run 内递减改动(已 Clone)");
            Assert.That(run.CarriedOpenings.Single().BattlesLeft, Is.EqualTo(1));
            run.CarriedOpenings.Single().BattlesLeft = 99;
            Assert.That(run.CarriedOpenings.Single().BattlesLeft, Is.EqualTo(1), "getter 返回副本");
        }

        [Test]
        public void Openings_CarryAcrossSegment_ViaGetterAndConstructor()
        {
            var run = new RunEngine(RebalanceFixture.Graph(), Config(1), BattleCfg(),
                new[] { "甲" }, Array.Empty<string>(), seed: 1);
            run.Battle.RegisterOpening(new OpeningEffect
                { SourceCharId = "甲", Element = Element.Metal, Kind = EffectKind.Morale, Value = 2, BattlesLeft = 3 });
            run.Battle.Cast("甲", 0);
            run.AdvanceAfterBattle();   // 段内最后一场:合并后 BattlesLeft = 3
            var carried = run.CarriedOpenings;
            Assert.That(carried.Single().BattlesLeft, Is.EqualTo(3));

            var next = new RunEngine(RebalanceFixture.Graph(), Config(1), BattleCfg(),
                new[] { "甲" }, Array.Empty<string>(), seed: 2, startingOpenings: carried);
            Assert.That(next.Battle.PlayerStatuses.TotalMagnitude(StatusKind.Morale), Is.EqualTo(2), "下一段第一场开局生效");
        }

        [Test]
        public void EndlessSaveState_CarriedOpenings_SurviveSaveFile()
        {
            var meta = new MetaState { EndlessV2 = new EndlessSaveState() };
            meta.EndlessV2.CarriedOpenings.Add(new OpeningEffect
                { SourceCharId = "焱", Element = Element.Fire, Kind = EffectKind.BurnAll, Value = 2, BattlesLeft = 5 });
            var loaded = SaveSerializer.FromJson(SaveSerializer.ToJson(meta));
            var o = loaded.EndlessV2.CarriedOpenings.Single();
            Assert.That(o.SourceCharId, Is.EqualTo("焱"));
            Assert.That(o.Kind, Is.EqualTo(EffectKind.BurnAll));
            Assert.That(o.Value, Is.EqualTo(2));
            Assert.That(o.BattlesLeft, Is.EqualTo(5));
        }

        [Test]
        public void LegacySave_WithoutCarriedOpenings_LoadsEmpty()
        {
            var json = "{\"EndlessV2\":{\"Depth\":3,\"Seed\":999}}";
            var loaded = SaveSerializer.FromJson(json);
            Assert.That(loaded.EndlessV2.CarriedOpenings, Is.Not.Null);
            Assert.That(loaded.EndlessV2.CarriedOpenings.Count, Is.EqualTo(0));
        }
    }
}
