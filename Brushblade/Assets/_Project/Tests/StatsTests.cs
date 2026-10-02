using System.Collections.Generic;
using Brushblade.Core;
using Brushblade.Data;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>统计与等级奖励的存档态(spec 2026-10-02 §3)。</summary>
    public class StatsTests
    {
        [Test]
        public void NewMeta_Defaults()
        {
            var meta = new MetaState();
            Assert.That(meta.Stats, Is.Not.Null);
            Assert.That(meta.LevelRewardGranted, Is.EqualTo(1));
            Assert.That(meta.LastSeenLevel, Is.EqualTo(0));
            Assert.That(meta.ClaimedMilestones.Count, Is.EqualTo(0));
            Assert.That(meta.MilestoneOffers.Count, Is.EqualTo(0));
        }

        [Test]
        public void SaveRoundTrip_KeepsAllNewFields()
        {
            var meta = new MetaState { LevelRewardGranted = 7, LastSeenLevel = 7 };
            meta.ClaimedMilestones.Add(5);
            meta.MilestoneOffers[10] = new List<string> { "a", "b", "c" };
            meta.Stats.ChestsOpened[ChestTier.Gilded] = 3;
            meta.Stats.CardsFromChests[CardRarity.Orange] = 2;
            meta.Stats.CardPlays["爆"] = 9;
            meta.Stats.Climbs = 1; meta.Stats.FloorsCleared = 2; meta.Stats.BossesDefeated = 3;
            meta.Stats.Deaths = 4; meta.Stats.Composes = 5; meta.Stats.Dismantles = 6;
            meta.Stats.MaxHit = 777; meta.Stats.InkEarned = 8; meta.Stats.InkSpent = 9; meta.Stats.AdRewards = 10;

            var back = SaveSerializer.FromJson(SaveSerializer.ToJson(meta));

            Assert.That(back.LevelRewardGranted, Is.EqualTo(7));
            Assert.That(back.LastSeenLevel, Is.EqualTo(7));
            Assert.That(back.ClaimedMilestones.Contains(5), Is.True);
            Assert.That(back.MilestoneOffers[10], Is.EqualTo(new List<string> { "a", "b", "c" }));
            Assert.That(back.Stats.ChestsOpened[ChestTier.Gilded], Is.EqualTo(3));
            Assert.That(back.Stats.CardsFromChests[CardRarity.Orange], Is.EqualTo(2));
            Assert.That(back.Stats.CardPlays["爆"], Is.EqualTo(9));
            Assert.That(back.Stats.MaxHit, Is.EqualTo(777));
            Assert.That(back.Stats.AdRewards, Is.EqualTo(10));
            Assert.That(back.Stats.InkSpent, Is.EqualTo(9));
        }

        [Test]
        public void OldSave_WithoutNewFields_LoadsDefaults()
        {
            var back = SaveSerializer.FromJson("{\"CharacterXp\":500,\"Ink\":10}");
            Assert.That(back.CharacterXp, Is.EqualTo(500));
            Assert.That(back.LevelRewardGranted, Is.EqualTo(1));
            Assert.That(back.LastSeenLevel, Is.EqualTo(0));
            Assert.That(back.Stats.Climbs, Is.EqualTo(0));
        }

        private sealed class FakeTime : ITimeSource
        {
            public long NowUnixSeconds { get; set; } = 1_000_000;
        }

        private static RecipeGraph RarityGraph() => new(new[]
        {
            new CharDef("c1", Element.Fire),
            new CharDef("c2", Element.Water),
            new CharDef("w1", Element.Fire, new[] { "c1", "c2" }, rarity: CardRarity.White),
            new CharDef("g1", Element.Fire, new[] { "c1", "c2" }, rarity: CardRarity.Green),
            new CharDef("b1", Element.Fire, new[] { "c1", "c2" }, rarity: CardRarity.Blue),
        });

        [Test]
        public void TryOpen_RecordsTierAndCardCountsByRarity()
        {
            var time = new FakeTime();
            var meta = new MetaState();
            var graph = RarityGraph();
            ChestRules.TryAwardChest(meta, ChestTier.Bamboo, new[] { "w1", "g1", "b1" }, time);
            ChestRules.TryStartOpening(meta, 0, time);
            time.NowUnixSeconds += ChestRules.DurationSeconds[(int)ChestTier.Bamboo - 1];

            Assert.That(ChestRules.TryOpen(meta, 0, time, new GameRandom(3), out var rewards, graph), Is.True);

            Assert.That(meta.Stats.ChestsOpened[ChestTier.Bamboo], Is.EqualTo(1));
            int total = 0;
            foreach (var kv in meta.Stats.CardsFromChests) total += kv.Value;
            int expected = 0;
            foreach (var c in rewards.Counts) expected += c;
            Assert.That(total, Is.EqualTo(expected), "按张数计,不是按捆数");
            for (int i = 0; i < rewards.Cards.Count; i++)
                Assert.That(meta.Stats.CardsFromChests.ContainsKey(graph.Get(rewards.Cards[i]).Rarity), Is.True);
        }

        [Test]
        public void TryOpen_NotReady_RecordsNothing()
        {
            var time = new FakeTime();
            var meta = new MetaState();
            ChestRules.TryAwardChest(meta, ChestTier.Bamboo, new[] { "w1" }, time);
            Assert.That(ChestRules.TryOpen(meta, 0, time, new GameRandom(3), out _, RarityGraph()), Is.False);
            Assert.That(meta.Stats.ChestsOpened.Count, Is.EqualTo(0));
        }

        [Test]
        public void FoldTally_MergesAndClears()
        {
            var meta = new MetaState();
            meta.Stats.CardPlays["爆"] = 2;
            meta.Stats.MaxHit = 50;
            var tally = new BattleTally { Composes = 3, Dismantles = 1, MaxHit = 40 };
            tally.Plays["爆"] = 5; tally.Plays["冷"] = 1;

            StatsRules.FoldTally(meta, tally);
            StatsRules.FoldTally(meta, tally); // 第二次:已清空,不重复计

            Assert.That(meta.Stats.CardPlays["爆"], Is.EqualTo(7));
            Assert.That(meta.Stats.CardPlays["冷"], Is.EqualTo(1));
            Assert.That(meta.Stats.Composes, Is.EqualTo(3));
            Assert.That(meta.Stats.Dismantles, Is.EqualTo(1));
            Assert.That(meta.Stats.MaxHit, Is.EqualTo(50), "取 max,不是覆盖");
            Assert.That(tally.Plays.Count, Is.EqualTo(0));
        }

        [Test]
        public void ClimbFloorDeathAd_Counters()
        {
            var meta = new MetaState();
            StatsRules.RecordClimbStart(meta);
            StatsRules.RecordFloorCleared(meta, isBoss: false);
            StatsRules.RecordFloorCleared(meta, isBoss: true);
            StatsRules.RecordDeath(meta);
            StatsRules.RecordAdReward(meta);
            Assert.That(meta.Stats.Climbs, Is.EqualTo(1));
            Assert.That(meta.Stats.FloorsCleared, Is.EqualTo(2));
            Assert.That(meta.Stats.BossesDefeated, Is.EqualTo(1));
            Assert.That(meta.Stats.Deaths, Is.EqualTo(1));
            Assert.That(meta.Stats.AdRewards, Is.EqualTo(1));
        }

        [Test]
        public void TopPlays_SortedDesc_TieByOrdinalId_SkipsUnknownIds()
        {
            var graph = RarityGraph();
            var meta = new MetaState();
            meta.Stats.CardPlays["g1"] = 5;
            meta.Stats.CardPlays["b1"] = 5;
            meta.Stats.CardPlays["w1"] = 9;
            meta.Stats.CardPlays["幽灵"] = 99; // 字表里删掉的字

            var top = StatsRules.TopPlays(meta, graph, 10);

            Assert.That(top.Count, Is.EqualTo(3));
            Assert.That(top[0].Id, Is.EqualTo("w1"));
            Assert.That(top[1].Id, Is.EqualTo("b1"));
            Assert.That(top[2].Id, Is.EqualTo("g1"));
        }

        [Test]
        public void TopPlays_RespectsCount()
        {
            var graph = RarityGraph();
            var meta = new MetaState();
            meta.Stats.CardPlays["w1"] = 1; meta.Stats.CardPlays["g1"] = 2; meta.Stats.CardPlays["b1"] = 3;
            Assert.That(StatsRules.TopPlays(meta, graph, 2).Count, Is.EqualTo(2));
        }
    }
}
