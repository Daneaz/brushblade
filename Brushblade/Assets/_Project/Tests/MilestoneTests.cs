using System.Collections.Generic;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>等级里程碑(spec 2026-10-02 §2.2 / §5.3)。</summary>
    public class MilestoneTests
    {
        private static RecipeGraph Graph() => new(new[]
        {
            new CharDef("c1", Element.Fire),
            new CharDef("c2", Element.Water),
            new CharDef("b1", Element.Fire, new[] { "c1", "c2" }, rarity: CardRarity.Blue),
            new CharDef("b2", Element.Water, new[] { "c1", "c2" }, rarity: CardRarity.Blue),
            new CharDef("b3", Element.Wood, new[] { "c1", "c2" }, rarity: CardRarity.Blue),
            new CharDef("b4", Element.Earth, new[] { "c1", "c2" }, rarity: CardRarity.Blue),
            new CharDef("p1", Element.Fire, new[] { "c1", "c2" }, rarity: CardRarity.Purple),
            new CharDef("g1", Element.Fire, new[] { "c1", "c2" }, rarity: CardRarity.Gold),
        });

        private static readonly string[] Pool = { "b1", "b2", "b3", "b4", "p1", "g1" };

        private static MetaState AtLevel(int level) => new() { CharacterXp = MetaRules.XpToReach(level) };

        [Test]
        public void Table_Values()
        {
            var m5 = MilestoneRules.ForLevel(5).Value;
            Assert.That(m5.Ink, Is.EqualTo(300));
            Assert.That(m5.Rarity, Is.EqualTo(CardRarity.Blue));
            var m50 = MilestoneRules.ForLevel(50).Value;
            Assert.That(m50.Ink, Is.EqualTo(5000));
            Assert.That(m50.Rarity, Is.EqualTo(CardRarity.Red));
            var m70 = MilestoneRules.ForLevel(70).Value;
            Assert.That(m70.Ink, Is.EqualTo(3000));
            Assert.That(m70.Rarity, Is.EqualTo(CardRarity.Orange));
            Assert.That(MilestoneRules.ForLevel(55).HasValue, Is.False);
            Assert.That(MilestoneRules.ForLevel(7).HasValue, Is.False);
        }

        [TestCase(4, 0)]
        [TestCase(5, 1)]
        [TestCase(49, 9)]
        [TestCase(50, 10)]
        [TestCase(59, 10)]
        [TestCase(60, 11)]
        [TestCase(75, 12)]
        public void MilestonesUpTo_Count(int level, int count) =>
            Assert.That(MilestoneRules.MilestonesUpTo(level).Count, Is.EqualTo(count));

        [Test]
        public void IsClaimable_LevelAndClaimed()
        {
            var meta = AtLevel(12);
            Assert.That(MilestoneRules.IsClaimable(meta, 10), Is.True);
            Assert.That(MilestoneRules.IsClaimable(meta, 15), Is.False, "未达");
            Assert.That(MilestoneRules.IsClaimable(meta, 7), Is.False, "不是里程碑");
            meta.ClaimedMilestones.Add(10);
            Assert.That(MilestoneRules.IsClaimable(meta, 10), Is.False, "已领");
            Assert.That(MilestoneRules.HasClaimable(meta), Is.True, "Lv.5 还没领");
            meta.ClaimedMilestones.Add(5);
            Assert.That(MilestoneRules.HasClaimable(meta), Is.False);
        }

        [Test]
        public void Offer_ThreeDistinctOfRarity_Persisted()
        {
            var meta = AtLevel(5);
            var offer = MilestoneRules.GetOrCreateOffer(meta, 5, Pool, Graph(), new GameRandom(7));
            Assert.That(offer.Count, Is.EqualTo(MilestoneRules.OfferSize));
            Assert.That(new HashSet<string>(offer).Count, Is.EqualTo(3));
            Assert.That(offer, Has.All.Matches<string>(id => Graph().Get(id).Rarity == CardRarity.Blue));
            Assert.That(meta.MilestoneOffers[5], Is.EqualTo(offer));
        }

        [Test]
        public void Offer_SecondCall_SameAndNoRandomUse()
        {
            var meta = AtLevel(5);
            var first = new List<string>(MilestoneRules.GetOrCreateOffer(meta, 5, Pool, Graph(), new GameRandom(7)));
            var random = new GameRandom(99);
            uint before = random.State;
            var second = MilestoneRules.GetOrCreateOffer(meta, 5, Pool, Graph(), random);
            Assert.That(second, Is.EqualTo(first));
            Assert.That(random.State, Is.EqualTo(before));
        }

        [Test]
        public void Offer_PoolSmallerThanThree_GivesAll()
        {
            var meta = AtLevel(20); // Lv.20 = 金,池里只有 g1
            var offer = MilestoneRules.GetOrCreateOffer(meta, 20, Pool, Graph(), new GameRandom(1));
            Assert.That(offer, Is.EqualTo(new List<string> { "g1" }));
        }

        [Test]
        public void Claim_GrantsInkAndCard_RecordsAndClearsOffer_NoPityChange()
        {
            var meta = AtLevel(5);
            meta.GoldPity = 2; meta.OrangePity = 3; meta.RedPity = 4;
            var offer = MilestoneRules.GetOrCreateOffer(meta, 5, Pool, Graph(), new GameRandom(7));

            Assert.That(MilestoneRules.TryClaim(meta, 5, offer[0], Graph()), Is.True);

            Assert.That(meta.Ink, Is.EqualTo(300));
            Assert.That(meta.Stats.InkEarned, Is.EqualTo(300));
            Assert.That(meta.OwnedCards.Contains(offer[0]), Is.True);
            Assert.That(meta.ClaimedMilestones.Contains(5), Is.True);
            Assert.That(meta.MilestoneOffers.ContainsKey(5), Is.False);
            Assert.That(meta.GoldPity, Is.EqualTo(2));
            Assert.That(meta.OrangePity, Is.EqualTo(3));
            Assert.That(meta.RedPity, Is.EqualTo(4));
        }

        [Test]
        public void Claim_OwnedCard_BecomesCopy()
        {
            var meta = AtLevel(5);
            var offer = MilestoneRules.GetOrCreateOffer(meta, 5, Pool, Graph(), new GameRandom(7));
            meta.OwnedCards.Add(offer[1]);
            Assert.That(MilestoneRules.TryClaim(meta, 5, offer[1], Graph()), Is.True);
            Assert.That(meta.CardCopies[offer[1]], Is.EqualTo(1));
        }

        [Test]
        public void Claim_Rejections_LeaveStateUntouched()
        {
            var meta = AtLevel(5);
            var offer = MilestoneRules.GetOrCreateOffer(meta, 5, Pool, Graph(), new GameRandom(7));

            Assert.That(MilestoneRules.TryClaim(meta, 5, "p1", Graph()), Is.False, "不在候选里");
            Assert.That(MilestoneRules.TryClaim(meta, 10, offer[0], Graph()), Is.False, "未达");
            Assert.That(meta.Ink, Is.EqualTo(0));
            Assert.That(meta.ClaimedMilestones.Count, Is.EqualTo(0));

            Assert.That(MilestoneRules.TryClaim(meta, 5, offer[0], Graph()), Is.True);
            Assert.That(MilestoneRules.TryClaim(meta, 5, offer[0], Graph()), Is.False, "已领");
            Assert.That(meta.Ink, Is.EqualTo(300));
        }

        [Test]
        public void Claim_WithoutOffer_Rejected()
        {
            var meta = AtLevel(5);
            Assert.That(MilestoneRules.TryClaim(meta, 5, "b1", Graph()), Is.False);
        }

        [Test]
        public void Claim_UnknownPick_Rejected()
        {
            var meta = AtLevel(5);
            meta.MilestoneOffers[5] = new List<string> { "幽灵", "b1" }; // 字表删字后的旧候选
            Assert.That(MilestoneRules.TryClaim(meta, 5, "幽灵", Graph()), Is.False);
            Assert.That(MilestoneRules.TryClaim(meta, 5, "b1", Graph()), Is.True);
        }
    }
}
