using System;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>技能效果**接进战斗配置**的守卫(spec 2026-09-07 §7)。
    ///
    /// 这个文件存在的理由与 MetaRulesBattleConfigTests 相同:2026-08-12 做变异检查时实测,
    /// 把 BuildBattleConfig 里任意一条属性注入整行删掉,967 条测试无一变红。技能注入是
    /// 同一个洞的第二层 —— 聚合函数算得对,不代表算出来的数被人用了。</summary>
    public class PerkInjectionTests
    {
        private static BattleConfig Build(MetaState meta) =>
            MetaRules.BuildBattleConfig(meta, Array.Empty<string>());

        /// <summary>没点任何节点时,战斗配置与引入技能树之前逐字节相同。恒等性硬线。</summary>
        [Test]
        public void EmptySave_ProducesTheBaselineConfig()
        {
            var meta = new MetaState { CharacterXp = 0 };
            var cfg = Build(meta);
            Assert.That(cfg.PlayerMaxHp, Is.EqualTo(MetaRules.MaxHpFor(1)));
            Assert.That(cfg.PlayerAttack, Is.EqualTo(MetaRules.AttackFor(1)));
            Assert.That(cfg.PlayerCritChance, Is.EqualTo(0), "暴击基准恒 0:RollCrit 要能短路");
            Assert.That(cfg.PlayerDefense, Is.EqualTo(MetaRules.DefenseFor(1)));
            Assert.That(cfg.ApPerTurn, Is.EqualTo(MetaRules.BaseApPerTurn));
        }

        [Test]
        public void VigorBranch_RaisesMaxHp()
        {
            var meta = new MetaState { CharacterXp = 0 };
            int baseline = Build(meta).PlayerMaxHp;
            meta.UnlockedPerks.Add("vigor_1"); // 300
            meta.UnlockedPerks.Add("vigor_2"); // 500(不叠加,取最高档)
            Assert.That(Build(meta).PlayerMaxHp, Is.EqualTo(baseline + 500));
        }

        /// <summary>被动树不叠加(2026-10-02 用户拍板):满点三层 = 最高档 +1000,不是三层之和。</summary>
        [Test]
        public void VigorBranch_FullyOwnedTakesTheTopTier()
        {
            var meta = new MetaState { CharacterXp = 0 };
            int baseline = Build(meta).PlayerMaxHp;
            meta.UnlockedPerks.Add("vigor_1");
            meta.UnlockedPerks.Add("vigor_2");
            meta.UnlockedPerks.Add("vigor_3");
            Assert.That(Build(meta).PlayerMaxHp, Is.EqualTo(baseline + 1000));
        }

        /// <summary>暴击率只能靠技能与字给 —— MetaRules 没有 CritFor 曲线(2026-08-12 用户裁定)。</summary>
        [Test]
        public void EdgeBranch_IsTheOnlySourceOfCritChance()
        {
            var meta = new MetaState { CharacterXp = 100000 }; // 高等级也不该自带暴击
            Assert.That(Build(meta).PlayerCritChance, Is.EqualTo(0));
            meta.UnlockedPerks.Add("edge_1");
            meta.UnlockedPerks.Add("edge_2");
            meta.UnlockedPerks.Add("edge_3");
            Assert.That(Build(meta).PlayerCritChance, Is.EqualTo(15), "不叠加,取最高档");
        }

        [Test]
        public void GuardBranch_AddsArmorOnTopOfTheLevelCurve()
        {
            var meta = new MetaState { CharacterXp = 0 };
            int baseline = Build(meta).PlayerDefense;
            meta.UnlockedPerks.Add("guard_1"); // 10
            meta.UnlockedPerks.Add("guard_2"); // 15
            meta.UnlockedPerks.Add("guard_3"); // 25(不叠加,取最高档)
            Assert.That(Build(meta).PlayerDefense, Is.EqualTo(baseline + 25));
        }

        /// <summary>一气(AP)2026-10-02 起改为调息(胜利回血),qi 枝不再动 AP。</summary>
        [Test]
        public void QiBranch_NoLongerRaisesApPerTurn()
        {
            var meta = new MetaState { CharacterXp = 0 };
            meta.UnlockedPerks.Add("qi_1");
            meta.UnlockedPerks.Add("qi_2");
            Assert.That(Build(meta).ApPerTurn, Is.EqualTo(MetaRules.BaseApPerTurn));
        }

        /// <summary>金汤已废止:表里不许再有产出护盾的节点。</summary>
        [Test]
        public void ShieldPerk_IsGone()
        {
            foreach (var def in PerkRules.Nodes)
                Assert.That(def.Branch, Is.Not.EqualTo("jintang"),
                    "金汤职能被御枝(护甲)与土脉(护盾字)双重覆盖,已废止");
        }

        // ---- 机制树:字库容量与起手张数是两条独立的轴 ----

        /// <summary>「博闻」只加容量,不加起手张数。改前这两件事是同一个 LibraryBonus。</summary>
        [Test]
        public void LoreBranch_RaisesCapacityOnly()
        {
            var meta = new MetaState { CharacterXp = 0 };
            int baseCap = MetaRules.LibraryCapacityFor(meta);
            meta.UnlockedPerks.Add("lore_1");
            Assert.That(MetaRules.LibraryCapacityFor(meta), Is.EqualTo(baseCap + 1));
            Assert.That(MetaRules.StartingHandSizeFor(meta), Is.EqualTo(MetaRules.StartingLibrarySize),
                "博闻不该动起手张数");
        }

        /// <summary>「广纳」2026-10-02 起改为空库掉字 +1,不再动起手张数与字库容量。</summary>
        [Test]
        public void WideBranch_GivesEmptyLibraryDrawsOnly()
        {
            var meta = new MetaState { CharacterXp = 0 };
            int baseCap = MetaRules.LibraryCapacityFor(meta);
            meta.UnlockedPerks.Add("wide_1");
            Assert.That(PerkRules.Bonus(meta, PerkEffect.EmptyLibraryDraws), Is.EqualTo(1));
            Assert.That(MetaRules.StartingHandSizeFor(meta), Is.EqualTo(MetaRules.StartingLibrarySize),
                "广纳不再加起手");
            Assert.That(MetaRules.LibraryCapacityFor(meta), Is.EqualTo(baseCap), "广纳不该动字库容量");
        }

        // ---- 被动树:力枝 ----

        [Test]
        public void PowerBranch_ScalesAttackByPercent()
        {
            var meta = new MetaState { CharacterXp = 0 };   // Lv.1,AttackFor(1) = 100
            Assert.That(Build(meta).PlayerAttack, Is.EqualTo(100), "夹具自检");
            meta.UnlockedPerks.Add("power_1"); // +5%
            Assert.That(Build(meta).PlayerAttack, Is.EqualTo(105));
            meta.UnlockedPerks.Add("power_2"); // 不叠加:取最高档 +10%
            Assert.That(Build(meta).PlayerAttack, Is.EqualTo(110));
            meta.UnlockedPerks.Add("power_3"); // +15%
            Assert.That(Build(meta).PlayerAttack, Is.EqualTo(115));
        }

        // ---- 五行 L4:2026-10-02 起不再抬四个上限(断金/涌泉/燎原/积土改为新机制)----

        [Test]
        public void EmptySave_KeepsEveryCapAtItsCurrentValue()
        {
            var cfg = Build(new MetaState());
            Assert.That(cfg.MoraleCap, Is.EqualTo(5), "战意上限现值");
            Assert.That(cfg.HeftCap, Is.EqualTo(10), "厚上限现值");
            Assert.That(cfg.WellspringCap, Is.EqualTo(10), "泉上限现值");
            Assert.That(cfg.BurnPerStack, Is.EqualTo(20), "灼烧每层现值");
        }

        [Test]
        public void TierFourNodes_NoLongerRaiseAnyCap()
        {
            var meta = new MetaState();
            meta.UnlockedPerks.Add("metal_4");
            meta.UnlockedPerks.Add("water_4");
            meta.UnlockedPerks.Add("fire_4");
            meta.UnlockedPerks.Add("earth_4");
            var cfg = Build(meta);
            Assert.That(cfg.MoraleCap, Is.EqualTo(5), "断金不再抬战意上限");
            Assert.That(cfg.HeftCap, Is.EqualTo(10), "积土不再抬厚上限");
            Assert.That(cfg.WellspringCap, Is.EqualTo(10), "涌泉不再抬泉上限");
            Assert.That(cfg.BurnPerStack, Is.EqualTo(20), "燎原不再抬灼烧每层");
        }

        // 木脉 L4(择伐,2026-09-13)的三条注入守卫搬去了 CounterTargetingTests ——
        // 那边还顺带钉住了「配置有没有被引擎读走」这一段。

        // ---- 五行 L3:各系专属机制(spec 2026-09-13 §3.3;2026-10-02 由 L2 挪到 L3)----
        // 每条两侧都断:本系点亮 → 本字段变;别系点亮 → 本字段不变。
        // ElementBonus 的第三个参数传错系既不会编译错、也不会有别的测试自然变红,
        // 这一组是唯一的守卫。

        [Test]
        public void EmptySave_LeavesTierThreeFieldsAtZero()
        {
            var cfg = Build(new MetaState());
            Assert.That(cfg.OverhealDamagePercent, Is.EqualTo(0), "水脉 L3 未点");
            Assert.That(cfg.BurnSpreadPercent, Is.EqualTo(0), "火脉 L3 未点");
            Assert.That(cfg.MoraleOnCrit, Is.EqualTo(0), "金脉 L3 未点");
            Assert.That(cfg.SummonDeathHealPercent, Is.EqualTo(0), "木脉 L3 未点");
        }

        [Test]
        public void WaterTierThree_SetsOverhealDamageOnly()
        {
            var meta = new MetaState();
            meta.UnlockedPerks.Add("water_3");
            var cfg = Build(meta);
            Assert.That(cfg.OverhealDamagePercent, Is.EqualTo(50));
            Assert.That(cfg.BurnSpreadPercent, Is.EqualTo(0), "串系了");
            Assert.That(cfg.MoraleOnCrit, Is.EqualTo(0), "串系了");
            Assert.That(cfg.SummonDeathHealPercent, Is.EqualTo(0), "串系了");
        }

        [Test]
        public void FireTierThree_SetsBurnSpreadOnly()
        {
            var meta = new MetaState();
            meta.UnlockedPerks.Add("fire_3");
            var cfg = Build(meta);
            Assert.That(cfg.BurnSpreadPercent, Is.EqualTo(100));
            Assert.That(cfg.OverhealDamagePercent, Is.EqualTo(0), "串系了");
            Assert.That(cfg.MoraleOnCrit, Is.EqualTo(0), "串系了");
            Assert.That(cfg.SummonDeathHealPercent, Is.EqualTo(0), "串系了");
        }

        [Test]
        public void MetalTierThree_SetsMoraleOnCritOnly()
        {
            var meta = new MetaState();
            meta.UnlockedPerks.Add("metal_3");
            var cfg = Build(meta);
            Assert.That(cfg.MoraleOnCrit, Is.EqualTo(1));
            Assert.That(cfg.OverhealDamagePercent, Is.EqualTo(0), "串系了");
            Assert.That(cfg.BurnSpreadPercent, Is.EqualTo(0), "串系了");
            Assert.That(cfg.SummonDeathHealPercent, Is.EqualTo(0), "串系了");
        }

        [Test]
        public void WoodTierThree_SetsSummonDeathHealOnly()
        {
            var meta = new MetaState();
            meta.UnlockedPerks.Add("wood_3");
            var cfg = Build(meta);
            Assert.That(cfg.SummonDeathHealPercent, Is.EqualTo(30));
            Assert.That(cfg.OverhealDamagePercent, Is.EqualTo(0), "串系了");
            Assert.That(cfg.BurnSpreadPercent, Is.EqualTo(0), "串系了");
            Assert.That(cfg.MoraleOnCrit, Is.EqualTo(0), "串系了");
        }

        [Test]
        public void EarthTierThree_DoesNotLeakIntoOtherBranches()
        {
            // 固本(ShieldCarryPercent)的注入断言在 Task 7 补;这里只守串系
            var meta = new MetaState();
            meta.UnlockedPerks.Add("earth_3");
            var cfg = Build(meta);
            Assert.That(cfg.OverhealDamagePercent, Is.EqualTo(0), "串系了");
            Assert.That(cfg.BurnSpreadPercent, Is.EqualTo(0), "串系了");
            Assert.That(cfg.MoraleOnCrit, Is.EqualTo(0), "串系了");
            Assert.That(cfg.SummonDeathHealPercent, Is.EqualTo(0), "串系了");
        }

        [Test]
        public void TierThreeNodes_DoNotDisturbCapsOrCounterTargeting()
        {
            var meta = new MetaState();
            meta.UnlockedPerks.Add("metal_3");
            meta.UnlockedPerks.Add("wood_3");
            meta.UnlockedPerks.Add("water_3");
            meta.UnlockedPerks.Add("fire_3");
            meta.UnlockedPerks.Add("earth_3");
            var cfg = Build(meta);
            Assert.That(cfg.MoraleCap, Is.EqualTo(5), "战意上限仍是现值");
            Assert.That(cfg.HeftCap, Is.EqualTo(10), "厚上限仍是现值");
            Assert.That(cfg.WellspringCap, Is.EqualTo(10), "泉上限仍是现值");
            Assert.That(cfg.BurnPerStack, Is.EqualTo(20), "灼烧每层仍是现值");
            Assert.That(cfg.CounterTargeting, Is.False, "择伐仍是关的");
        }
    }
}
