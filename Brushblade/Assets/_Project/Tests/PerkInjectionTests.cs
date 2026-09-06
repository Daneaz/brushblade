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
            meta.UnlockedPerks.Add("vigor_1"); // +100
            meta.UnlockedPerks.Add("vigor_2"); // +200
            Assert.That(Build(meta).PlayerMaxHp, Is.EqualTo(baseline + 300));
        }

        /// <summary>满点三层 = +600,与被换掉的「养元」6 级持平 —— 平衡锚点没动。</summary>
        [Test]
        public void VigorBranch_FullyOwnedMatchesTheOldYangyuanTotal()
        {
            var meta = new MetaState { CharacterXp = 0 };
            int baseline = Build(meta).PlayerMaxHp;
            meta.UnlockedPerks.Add("vigor_1");
            meta.UnlockedPerks.Add("vigor_2");
            meta.UnlockedPerks.Add("vigor_3");
            Assert.That(Build(meta).PlayerMaxHp, Is.EqualTo(baseline + 600));
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
            Assert.That(Build(meta).PlayerCritChance, Is.EqualTo(30));
        }

        [Test]
        public void GuardBranch_AddsArmorOnTopOfTheLevelCurve()
        {
            var meta = new MetaState { CharacterXp = 0 };
            int baseline = Build(meta).PlayerDefense;
            meta.UnlockedPerks.Add("guard_1"); // +2
            meta.UnlockedPerks.Add("guard_2"); // +3
            meta.UnlockedPerks.Add("guard_3"); // +5
            Assert.That(Build(meta).PlayerDefense, Is.EqualTo(baseline + 10));
        }

        [Test]
        public void QiBranch_RaisesApPerTurn()
        {
            var meta = new MetaState { CharacterXp = 0 };
            meta.UnlockedPerks.Add("qi_1");
            Assert.That(Build(meta).ApPerTurn, Is.EqualTo(MetaRules.BaseApPerTurn + 1));
            meta.UnlockedPerks.Add("qi_2");
            Assert.That(Build(meta).ApPerTurn, Is.EqualTo(MetaRules.BaseApPerTurn + 2));
        }

        /// <summary>一气封顶 2 层是硬平衡线:表里不许出现第三层。</summary>
        [Test]
        public void QiBranch_IsCappedAtTwoNodes()
        {
            int qiNodes = 0;
            foreach (var def in PerkRules.Nodes)
                if (def.Branch == "qi") qiNodes++;
            Assert.That(qiNodes, Is.EqualTo(2), "AP 上限 2 是硬平衡线(19.2.3)");
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
            Assert.That(PerkRules.Bonus(meta, PerkEffect.StartingCards), Is.EqualTo(0),
                "博闻不该动起手张数");
        }

        /// <summary>「广纳」只加起手张数。</summary>
        [Test]
        public void WideBranch_RaisesStartingCardsOnly()
        {
            var meta = new MetaState { CharacterXp = 0 };
            meta.UnlockedPerks.Add("wide_1");
            Assert.That(PerkRules.Bonus(meta, PerkEffect.StartingCards), Is.EqualTo(1));
            Assert.That(PerkRules.Bonus(meta, PerkEffect.LibraryCapacity), Is.EqualTo(0),
                "广纳不该动字库容量");
        }

        // ---- 被动树:力枝 ----

        [Test]
        public void PowerBranch_ScalesAttackByPercent()
        {
            var meta = new MetaState { CharacterXp = 0 };   // Lv.1,AttackFor(1) = 100
            Assert.That(Build(meta).PlayerAttack, Is.EqualTo(100), "夹具自检");
            meta.UnlockedPerks.Add("power_1"); // +5%
            Assert.That(Build(meta).PlayerAttack, Is.EqualTo(105));
            meta.UnlockedPerks.Add("power_2"); // 累计 +15%
            Assert.That(Build(meta).PlayerAttack, Is.EqualTo(115));
            meta.UnlockedPerks.Add("power_3"); // 累计 +30%
            Assert.That(Build(meta).PlayerAttack, Is.EqualTo(130));
        }

        /// <summary>百分比是加算后一次性乘,不是逐层复利 —— 1.05×1.10×1.15 = 1.328 ≠ 1.30。
        /// 复利会让第三层悄悄比标称值强,而没有任何断言会红。</summary>
        [Test]
        public void PowerBranch_IsAdditiveNotCompounding()
        {
            var meta = new MetaState { CharacterXp = 0 };
            meta.UnlockedPerks.Add("power_1");
            meta.UnlockedPerks.Add("power_2");
            meta.UnlockedPerks.Add("power_3");
            Assert.That(Build(meta).PlayerAttack, Is.EqualTo(130));
            Assert.That(Build(meta).PlayerAttack, Is.Not.EqualTo(132));
        }

        // ---- 五行 L4:四个天花板 ----

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
        public void MetalTierFour_RaisesMoraleCapOnly()
        {
            var meta = new MetaState();
            meta.UnlockedPerks.Add("metal_4");
            var cfg = Build(meta);
            Assert.That(cfg.MoraleCap, Is.EqualTo(7));
            Assert.That(cfg.HeftCap, Is.EqualTo(10));
            Assert.That(cfg.WellspringCap, Is.EqualTo(10));
            Assert.That(cfg.BurnPerStack, Is.EqualTo(20));
        }

        /// <summary>厚与泉共用 MaxResourceStacks 与 GainStacks —— 这两条守着那个陷阱。
        /// 点水脉不许抬高厚的上限,点土脉不许抬高泉的上限。</summary>
        [Test]
        public void WaterTierFour_DoesNotRaiseTheHeftCap()
        {
            var meta = new MetaState();
            meta.UnlockedPerks.Add("water_4");
            var cfg = Build(meta);
            Assert.That(cfg.WellspringCap, Is.EqualTo(14));
            Assert.That(cfg.HeftCap, Is.EqualTo(10), "厚与泉共用常量,拆分没做干净");
        }

        [Test]
        public void EarthTierFour_DoesNotRaiseTheWellspringCap()
        {
            var meta = new MetaState();
            meta.UnlockedPerks.Add("earth_4");
            var cfg = Build(meta);
            Assert.That(cfg.HeftCap, Is.EqualTo(14));
            Assert.That(cfg.WellspringCap, Is.EqualTo(10), "厚与泉共用常量,拆分没做干净");
        }

        [Test]
        public void FireTierFour_RaisesBurnPerStack()
        {
            var meta = new MetaState();
            meta.UnlockedPerks.Add("fire_4");
            Assert.That(Build(meta).BurnPerStack, Is.EqualTo(28));
        }

        // ---- 木脉 L4:木系召唤物速度 +40(spec §3.4.1) ----

        [Test]
        public void EmptySave_GivesNoSummonSpeedBonus()
        {
            Assert.That(Build(new MetaState()).WoodSummonSpeedBonus, Is.EqualTo(0));
        }

        [Test]
        public void WoodTierFour_GrantsFortySpeed()
        {
            var meta = new MetaState();
            meta.UnlockedPerks.Add("wood_4");
            Assert.That(Build(meta).WoodSummonSpeedBonus, Is.EqualTo(40));
        }

        /// <summary>只有木脉给这个加成 —— 土/金也有召唤字,但它们不该吃木系专精的回报。</summary>
        [Test]
        public void OtherBranches_DoNotGrantSummonSpeed()
        {
            var meta = new MetaState();
            meta.UnlockedPerks.Add("earth_4");
            meta.UnlockedPerks.Add("metal_4");
            Assert.That(Build(meta).WoodSummonSpeedBonus, Is.EqualTo(0));
        }
    }
}
