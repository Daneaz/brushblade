using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>怪物图鉴:击败解锁,查阅时才发赏(2026-07-22 拍板)。</summary>
    public class BestiaryTests
    {
        private static EnemyDef Ghost() => new("错字鬼", Element.Wood, 12, 4);

        private static EnemyDef Boss() => new("排山倒海", Element.Water, 12, 6,
            EnemyAbility.None, new[]
            {
                new BossPhaseDef("排", Element.Water, 12, 6),
                new BossPhaseDef("山", Element.Earth, 15, 4, 0.5f),
                new BossPhaseDef("倒", Element.Metal, 12, 8),
                new BossPhaseDef("海", Element.Water, 16, 10),
            });

        [Test]
        public void Defeat_UnlocksOnce()
        {
            var meta = new MetaState();
            Assert.That(BestiaryRules.RecordDefeat(meta, "错字鬼"), Is.True);
            Assert.That(BestiaryRules.RecordDefeat(meta, "错字鬼"), Is.False); // 已解锁
            Assert.That(meta.DefeatedEnemies, Is.EqualTo(new[] { "错字鬼" }));
        }

        [Test]
        public void Claim_PaysOnlyAfterDefeat_AndOnlyOnce()
        {
            var meta = new MetaState();
            Assert.That(BestiaryRules.TryClaim(meta, Ghost()), Is.Zero); // 没打过,不发
            Assert.That(meta.Ink, Is.Zero);

            BestiaryRules.RecordDefeat(meta, "错字鬼");
            Assert.That(BestiaryRules.TryClaim(meta, Ghost()), Is.EqualTo(20));
            Assert.That(meta.Ink, Is.EqualTo(20));
            Assert.That(BestiaryRules.TryClaim(meta, Ghost()), Is.Zero); // 已领
            Assert.That(meta.Ink, Is.EqualTo(20));
        }

        [Test]
        public void Claim_BossPaysFifty()
        {
            var meta = new MetaState();
            BestiaryRules.RecordDefeat(meta, "排山倒海");
            Assert.That(BestiaryRules.TryClaim(meta, Boss()), Is.EqualTo(50));
        }

        [Test]
        public void HasUnclaimed_DrivesHomeRedDot()
        {
            var meta = new MetaState();
            Assert.That(BestiaryRules.HasUnclaimed(meta), Is.False);

            BestiaryRules.RecordDefeat(meta, "错字鬼");
            Assert.That(BestiaryRules.HasUnclaimed(meta), Is.True); // 打过没查阅 → 红点

            BestiaryRules.TryClaim(meta, Ghost());
            Assert.That(BestiaryRules.HasUnclaimed(meta), Is.False); // 查阅领赏后熄灭
        }

        [Test]
        public void Bestiary_SurvivesSaveRoundTrip()
        {
            var meta = new MetaState();
            BestiaryRules.RecordDefeat(meta, "错字鬼");
            BestiaryRules.RecordDefeat(meta, "排山倒海");
            BestiaryRules.TryClaim(meta, Ghost());

            var restored = Data.SaveSerializer.FromJson(Data.SaveSerializer.ToJson(meta));
            Assert.That(restored.DefeatedEnemies, Is.EqualTo(new[] { "错字鬼", "排山倒海" }));
            Assert.That(restored.ClaimedBestiary, Is.EqualTo(new[] { "错字鬼" }));
            Assert.That(BestiaryRules.HasUnclaimed(restored), Is.True); // 排山倒海还没查阅
        }
    }
}
