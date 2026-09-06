using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>五行树 L3「该系字效果值 +15%」(spec §3.3)。</summary>
    public class ElementPerkBonusTests
    {
        /// <summary>缺省(null / 全 0)时 x × 100 / 100 == x,逐字节恒等。
        /// 这是「接线」这一步不需要改任何既有断言的凭据。</summary>
        [Test]
        public void NullPercentTable_IsIdentity()
        {
            var cfg = new BattleConfig();
            Assert.That(cfg.ElementEffectPercent, Is.Null,
                "缺省必须是 null —— 空数组也行,但别给非零值");
        }

        [Test]
        public void BuildBattleConfig_LeavesTheTableEmptyOnABrandNewSave()
        {
            var cfg = MetaRules.BuildBattleConfig(new MetaState(), System.Array.Empty<string>());
            if (cfg.ElementEffectPercent == null) Assert.Pass();
            foreach (int pct in cfg.ElementEffectPercent)
                Assert.That(pct, Is.EqualTo(0));
        }

        [Test]
        public void BuildBattleConfig_FillsOnlyTheOwnedElement()
        {
            var meta = new MetaState();
            meta.UnlockedPerks.Add("fire_3"); // 火系字效果 +15%
            var cfg = MetaRules.BuildBattleConfig(meta, System.Array.Empty<string>());
            Assert.That(cfg.ElementEffectPercent[(int)Element.Fire], Is.EqualTo(15));
            Assert.That(cfg.ElementEffectPercent[(int)Element.Water], Is.EqualTo(0),
                "点火脉不该给水系加成");
        }

        // ---- 白名单:连续量值吃,离散层数/回合数不吃 ----

        /// <summary>伤害吃 +15%。60 × 115 / 100 = 69。</summary>
        [Test]
        public void DamageValue_TakesTheBonus()
        {
            Assert.That(BattleEngine.ApplyElementPercent(60, 15, EffectKind.DamageSingle),
                Is.EqualTo(69));
        }

        [Test]
        public void HealAndShieldValues_TakeTheBonus()
        {
            Assert.That(BattleEngine.ApplyElementPercent(100, 15, EffectKind.HealSelf),
                Is.EqualTo(115));
            Assert.That(BattleEngine.ApplyElementPercent(100, 15, EffectKind.Shield),
                Is.EqualTo(115));
        }

        /// <summary>层数/回合数**不吃** —— +15% 在小数值上会被整数除截断,读数不可预期
        /// (战意 2 层 ×1.15 = 2.3 → 2,毫无变化;7 层 → 8,凭空跳一级)。</summary>
        [Test]
        public void StackAndTurnCounts_DoNotTakeTheBonus()
        {
            Assert.That(BattleEngine.ApplyElementPercent(2, 15, EffectKind.Morale), Is.EqualTo(2));
            Assert.That(BattleEngine.ApplyElementPercent(3, 15, EffectKind.BurnAll), Is.EqualTo(3));
            Assert.That(BattleEngine.ApplyElementPercent(2, 15, EffectKind.Freeze), Is.EqualTo(2));
            Assert.That(BattleEngine.ApplyElementPercent(1, 15, EffectKind.Summon), Is.EqualTo(1));
        }

        [Test]
        public void ZeroBonus_IsIdentityForEveryKind()
        {
            foreach (EffectKind kind in System.Enum.GetValues(typeof(EffectKind)))
                for (int v = 0; v < 200; v += 7)
                    Assert.That(BattleEngine.ApplyElementPercent(v, 0, kind), Is.EqualTo(v),
                        $"{kind} / {v}");
        }
    }
}
