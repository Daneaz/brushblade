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
    }
}
