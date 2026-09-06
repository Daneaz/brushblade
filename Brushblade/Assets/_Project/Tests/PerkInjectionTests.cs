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
    }
}
