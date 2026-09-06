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

        // ---- 补白名单(2026-09-07,spec §3.3 修订):DefenseBuff/Empower/CritBuff/PierceBuff/Blind ----

        /// <summary>DefenseBuff(护甲增益点数)与已在白名单的 ArmorBreak 是同一个量的正负两面,
        /// 该吃 +15%。20 × 115 / 100 = 23。</summary>
        [Test]
        public void DefenseBuff_TakesTheBonus()
        {
            Assert.That(BattleEngine.ApplyElementPercent(20, 15, EffectKind.DefenseBuff),
                Is.EqualTo(23));
        }

        /// <summary>Empower(本场攻击力加点,剡)是连续量值,该吃 +15%。20 × 115 / 100 = 23。</summary>
        [Test]
        public void Empower_TakesTheBonus()
        {
            Assert.That(BattleEngine.ApplyElementPercent(20, 15, EffectKind.Empower),
                Is.EqualTo(23));
        }

        /// <summary>CritBuff(本场暴击率加点,锋)是连续量值,该吃 +15%。20 × 115 / 100 = 23。</summary>
        [Test]
        public void CritBuff_TakesTheBonus()
        {
            Assert.That(BattleEngine.ApplyElementPercent(20, 15, EffectKind.CritBuff),
                Is.EqualTo(23));
        }

        /// <summary>PierceBuff(本场穿透点数,锐)是连续量值,该吃 +15%。20 × 115 / 100 = 23。</summary>
        [Test]
        public void PierceBuff_TakesTheBonus()
        {
            Assert.That(BattleEngine.ApplyElementPercent(20, 15, EffectKind.PierceBuff),
                Is.EqualTo(23));
        }

        /// <summary>Blind 的 Value 是命中率百分点(回合数在 Turns 字段里,不在 Value 里),
        /// 该吃 +15%。20 × 115 / 100 = 23。</summary>
        [Test]
        public void Blind_TakesTheBonus()
        {
            Assert.That(BattleEngine.ApplyElementPercent(20, 15, EffectKind.Blind),
                Is.EqualTo(23));
        }

        /// <summary>ApBoost 刻意排除(AP 是全局硬平衡资源,不该被五行 L3 绕过);
        /// Silence/Charm 是持续回合数,不是连续量值。三者都不吃。</summary>
        [Test]
        public void ApBoostSilenceCharm_DoNotTakeTheBonus()
        {
            Assert.That(BattleEngine.ApplyElementPercent(1, 15, EffectKind.ApBoost), Is.EqualTo(1));
            Assert.That(BattleEngine.ApplyElementPercent(3, 15, EffectKind.Silence), Is.EqualTo(3));
            Assert.That(BattleEngine.ApplyElementPercent(2, 15, EffectKind.Charm), Is.EqualTo(2));
        }

        /// <summary>穷举守卫(spec §3.3):遍历 <see cref="EffectKind"/> 的每一个成员,
        /// 与写死在这里的期望集合逐条比对。这条断言的意义不在验证今天的行为——今天的行为
        /// 已被上面各条单独断言覆盖——而在**未来**:谁给 EffectKind 加新成员,这条测试
        /// 会因为新成员落在 switch 的默认分支(false)而在这里出现"实际 false / 期望未定义"
        /// 的不一致,从而逼着改动者回到 spec §3.3 的两张穷举表里给新成员定位,而不是
        /// 静默吃 default。</summary>
        [Test]
        public void TakesElementPercent_MatchesSpecTableForEveryMember()
        {
            var takes = new System.Collections.Generic.HashSet<EffectKind>
            {
                EffectKind.DamageSingle, EffectKind.DamageAll,
                EffectKind.HealSelf, EffectKind.HealAll, EffectKind.HealOverTime,
                EffectKind.Shield, EffectKind.ShieldAll,
                EffectKind.Bleed,
                EffectKind.SpendHeft, EffectKind.SpendWellspring,
                EffectKind.Detonate, EffectKind.ArmorBreak,
                EffectKind.DefenseBuff,
                EffectKind.Empower, EffectKind.CritBuff, EffectKind.PierceBuff,
                EffectKind.Blind,
            };
            var doesNotTake = new System.Collections.Generic.HashSet<EffectKind>
            {
                EffectKind.Morale,
                EffectKind.BurnAll, EffectKind.BurnSingle,
                EffectKind.Freeze, EffectKind.Slow,
                EffectKind.Silence, EffectKind.Charm,
                EffectKind.Immunity, EffectKind.Reflect,
                EffectKind.Cleanse, EffectKind.Dispel, EffectKind.Revive,
                EffectKind.Summon,
                EffectKind.BurnPotency, EffectKind.BurnSettleNow, EffectKind.BurnNoDecay,
                EffectKind.ApBoost,
            };

            foreach (EffectKind kind in System.Enum.GetValues(typeof(EffectKind)))
            {
                Assert.That(takes.Contains(kind) || doesNotTake.Contains(kind), Is.True,
                    $"{kind} 未出现在 spec §3.3 的任何一张穷举表里 —— 新增 EffectKind 时必须回去定位");
                Assert.That(BattleEngine.TakesElementPercent(kind), Is.EqualTo(takes.Contains(kind)),
                    $"{kind}");
            }
        }
    }
}
