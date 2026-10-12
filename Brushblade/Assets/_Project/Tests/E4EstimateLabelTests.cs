using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>Plan E4 Task 4:预估签写哪几段的纯规则(已定口径 §A:A-Q6 多段、A-Q7 全体前缀、A-Q8 主数 + 至多 1 状态段
    /// + 至多 1 已用段、A-Q4 护盾入账 0 照写、R5 不认识的效果不出段)。文案拼装在 Presentation。</summary>
    public class E4EstimateLabelTests
    {
        private static EffectDef E(EffectKind kind, int value = 0, ScaleBasis scaleBy = ScaleBasis.None) =>
            new(kind, value, scaleBy: scaleBy);

        private static TraitDef Trait(string name) =>
            new(TraitSlot.Lv5, TraitFace.Feature, TraitForm.Active, null, name,
                new[] { E(EffectKind.DamageCut, 60) }, maxPerBattle: 1);

        private static TargetHit Hit(int index, int damage, int hits = 1, int burn = 0, bool ke = false, bool countered = false) =>
            new(index, damage, hits, burn, ke, countered, false, false);

        private static CastEstimate Est(IReadOnlyList<EffectDef> effects, params TargetHit[] hits) =>
            new() { Effects = effects, Enemies = hits };

        // ================= 敌人签 =================

        [Test]
        public void Enemy_DamageWithKe_AndFirstStatus()
        {
            var est = Est(new[] { E(EffectKind.DamageSingle, 50), E(EffectKind.BurnSingle, 2) },
                Hit(0, 197, burn: 4, ke: true));
            var label = EstimateLabelRules.ForEnemy(est, 0);
            Assert.That(label, Is.Not.Null);
            Assert.That(label.All, Is.False);
            Assert.That(label.Main.Value.Kind, Is.EqualTo(EstimateSegKind.Damage));
            Assert.That(label.Main.Value.Amount, Is.EqualTo(197));
            Assert.That(label.Ke, Is.True);
            Assert.That(label.Countered, Is.False);
            Assert.That(label.Hits, Is.EqualTo(0), "单段不写段数");
            Assert.That(label.Status.Value.Kind, Is.EqualTo(EstimateSegKind.Burn));
            Assert.That(label.Status.Value.Amount, Is.EqualTo(4), "灼取 Burn 事件实际层数,不取效果表的值");
            Assert.That(label.Spent, Is.Null);
        }

        [Test]
        public void Enemy_Neutral_NoMultiplier_Countered_Flagged()
        {
            var effects = new[] { E(EffectKind.DamageSingle, 50) };
            var neutral = EstimateLabelRules.ForEnemy(Est(effects, Hit(0, 40)), 0);
            Assert.That(neutral.Ke || neutral.Countered, Is.False);
            var countered = EstimateLabelRules.ForEnemy(Est(effects, Hit(0, 20, countered: true)), 0);
            Assert.That(countered.Countered, Is.True);
            Assert.That(countered.Ke, Is.False);
        }

        [Test]
        public void Enemy_PicksHoveredTargetOnly_WithAllPrefix()
        {
            var est = Est(new[] { E(EffectKind.DamageSingle, 30), E(EffectKind.BurnSingle, 1) },
                Hit(0, 42, burn: 1), Hit(2, 63, burn: 1, ke: true));
            est.AllEnemies = true;
            var label = EstimateLabelRules.ForEnemy(est, 2);
            Assert.That(label.All, Is.True);
            Assert.That(label.Main.Value.Amount, Is.EqualTo(63), "数字 = 打在悬停这一只身上的合计");
            Assert.That(label.Ke, Is.True);
            Assert.That(label.Status.Value.Kind, Is.EqualTo(EstimateSegKind.Burn));
        }

        [Test]
        public void Enemy_MultiHit_WritesHitCount()
        {
            var effects = new[] { E(EffectKind.DamageSingle, 30) };
            Assert.That(EstimateLabelRules.ForEnemy(Est(effects, Hit(0, 90, hits: 3)), 0).Hits, Is.EqualTo(3));
            Assert.That(EstimateLabelRules.ForEnemy(Est(effects, Hit(0, 30, hits: 1)), 0).Hits, Is.EqualTo(0));
        }

        [Test]
        public void Enemy_AtMostOneStatus_FirstWritableInEffectOrder()
        {
            var est = Est(new[]
            {
                E(EffectKind.DamageSingle, 30), E(EffectKind.Empower, 5), E(EffectKind.BurnSingle, 2),
                E(EffectKind.Slow, 2), E(EffectKind.ArmorBreak, 3),
            }, Hit(0, 30, burn: 0));   // 灼被免疫:实际 0 层 → 跳过,取下一条可写的
            var label = EstimateLabelRules.ForEnemy(est, 0);
            Assert.That(label.Status.Value.Kind, Is.EqualTo(EstimateSegKind.Slow));
        }

        [Test]
        public void Enemy_BurnOnlyFace_BurnIsMain()
        {
            var est = Est(new[] { E(EffectKind.BurnSingle, 3), E(EffectKind.Weaken, 15) }, Hit(0, 0, hits: 0, burn: 3));
            var label = EstimateLabelRules.ForEnemy(est, 0);
            Assert.That(label.Main.Value.Kind, Is.EqualTo(EstimateSegKind.Burn));
            Assert.That(label.Main.Value.Amount, Is.EqualTo(3));
            Assert.That(label.Status.Value.Kind, Is.EqualTo(EstimateSegKind.Weaken));
            Assert.That(label.Ke || label.Countered, Is.False, "倍率只跟伤害主数");
        }

        [Test]
        public void Enemy_MoraleGain_WrittenAsStatus()
        {
            var est = Est(new[] { E(EffectKind.DamageSingle, 30), E(EffectKind.Morale, 1) }, Hit(0, 30));
            est.MoraleGain = 2;
            var label = EstimateLabelRules.ForEnemy(est, 0);
            Assert.That(label.Status.Value.Kind, Is.EqualTo(EstimateSegKind.Morale));
            Assert.That(label.Status.Value.Amount, Is.EqualTo(2));
        }

        [Test]
        public void Enemy_IgnoresAllySideEffects_AndUnknownKinds()
        {
            var est = Est(new[] { E(EffectKind.DamageSingle, 30), E(EffectKind.Shield, 50), E(EffectKind.CritBuff, 5) },
                Hit(0, 30));
            est.ShieldGranted = 50;
            Assert.That(EstimateLabelRules.ForEnemy(est, 0).Status, Is.Null);
        }

        [Test]
        public void Enemy_Rejected_NoLabel()
        {
            var est = Est(new[] { E(EffectKind.DamageSingle, 30) }, Hit(0, 30));
            est.Error = BattleError.InvalidTarget;
            Assert.That(EstimateLabelRules.ForEnemy(est, 0), Is.Null);
        }

        [Test]
        public void Enemy_NothingToWrite_NoLabel()
        {
            var est = Est(new[] { E(EffectKind.CritBuff, 5) });
            Assert.That(EstimateLabelRules.ForEnemy(est, 0), Is.Null);
        }

        [Test]
        public void Enemy_StatusOnlyTarget_StatusBecomesMain()
        {
            var est = Est(new[] { E(EffectKind.Freeze, 1), E(EffectKind.Seed, 20) });   // 没有伤害事件,没进 Enemies
            var label = EstimateLabelRules.ForEnemy(est, 1);
            Assert.That(label.Main.Value.Kind, Is.EqualTo(EstimateSegKind.Freeze));
            Assert.That(label.Status.Value.Kind, Is.EqualTo(EstimateSegKind.Seed));
        }

        [Test]
        public void Enemy_Killed_NoStatusSegment()
        {
            var est = Est(new[] { E(EffectKind.DamageSingle, 30), E(EffectKind.Slow, 2), E(EffectKind.BurnSingle, 2) },
                new TargetHit(0, 500, 1, 2, false, false, false, killed: true));
            var label = EstimateLabelRules.ForEnemy(est, 0);
            Assert.That(label.Main.Value.Kind, Is.EqualTo(EstimateSegKind.Damage));
            Assert.That(label.Status, Is.Null, "目标这一下就死了,不会再挂状态");
        }

        // ================= 全体 + Primary:不能共享一次试算(fix round 1 Important 1)=================

        /// <summary>燚 五行面 Lv5:火海 BurnAll 2(全体)+ 灼·强化 BurnSingle 5(主目标)。打全体但仍要选主目标 ——
        /// 悬停第 j 只的签必须按 j 为主目标单独试算;拿以第 0 只为主目标的那份去读第 j 只会少掉 BurnSingle 的 5 层。
        /// BattleView 因此只在 !NeedsTargetNow 时共享全体试算。</summary>
        [Test]
        public void AllWithPrimary_EstimatePerTarget_MatchesRealCast()
        {
            var graph = CharTableTests.RealGraph();
            var config = new BattleConfig
            {
                PlayerMaxHp = 5000, PlayerAttack = 100, ApPerTurn = 200, LibraryCapacity = 60, PoolCapacity = 20,
                PlayerCritChance = 0, BossPhaseJitterPercent = 0,
            };
            var enemies = new[]
            {
                new EnemyDef("甲", Element.Heart, 100000, 0),
                new EnemyDef("乙", Element.Heart, 100000, 0),
                new EnemyDef("丙", Element.Heart, 100000, 0),
            };
            var levels = new Dictionary<string, int> { ["燚"] = 5 };
            var b = new BattleEngine(graph, config, new[] { "燚", "燚" }, Array.Empty<string>(), enemies, seed: 1,
                cardLevels: levels);
            Assert.That(b.HitsAllEnemiesNow("燚", attackMode: false), Is.True, "夹具前提:打全体");
            Assert.That(b.NeedsTargetNow("燚", attackMode: false), Is.True, "夹具前提:仍有主目标效果");

            int BurnAt(CastEstimate est, int i) => est.Enemies.Where(h => h.Index == i).Sum(h => h.Burn);
            var own = b.EstimateCast("燚", 2, attackMode: false);
            var shared = b.EstimateCast("燚", 0, attackMode: false);
            var defs = enemies.ToDictionary(e => e.Id);
            var twin = BattleEngine.Restore(b.Capture(), graph, config, levels, defs);
            Assert.That(twin.Cast("燚", 2, replaceSummon: true, attackMode: false), Is.EqualTo(BattleError.None));
            int real = twin.LastEvents.Where(e => e.Kind == BattleEventKind.Burn && e.TargetIndex == 2).Sum(e => e.Amount);
            Assert.That(BurnAt(own, 2), Is.EqualTo(real), "按悬停那只单独试算 == 实际");
            Assert.That(BurnAt(shared, 2), Is.Not.EqualTo(real), "以别的敌人为主目标的试算读不出这一只的主目标灼");
        }

        // ================= 友方签 =================

        [Test]
        public void Ally_ShieldAndDamageCut_ThenSpentReplacesIt()
        {
            var usable = new CastEstimate
            {
                Effects = new[] { E(EffectKind.Shield, 200), E(EffectKind.DamageCut, 60) },
                ShieldGranted = 280,
            };
            var a = EstimateLabelRules.ForAlly(usable);
            Assert.That(a.Main.Value.Kind, Is.EqualTo(EstimateSegKind.Shield));
            Assert.That(a.Main.Value.Amount, Is.EqualTo(280));
            Assert.That(a.Status.Value.Kind, Is.EqualTo(EstimateSegKind.DamageCut));
            Assert.That(a.Status.Value.Amount, Is.EqualTo(60));
            Assert.That(a.Spent, Is.Null);

            // 杜门不出已用:Core 已把 DamageCut 从效果表扣掉,SpentTraits 带上它 → 已用段替代
            var spentTrait = Trait("杜门不出");
            var spent = new CastEstimate
            {
                Effects = new[] { E(EffectKind.Shield, 200) },
                ShieldGranted = 280,
                SpentTraits = new[] { spentTrait },
            };
            var b = EstimateLabelRules.ForAlly(spent);
            Assert.That(b.Main.Value.Kind, Is.EqualTo(EstimateSegKind.Shield));
            Assert.That(b.Status, Is.Null);
            Assert.That(b.Spent, Is.SameAs(spentTrait));
        }

        [Test]
        public void Ally_ShieldFull_WritesZero()
        {
            var est = new CastEstimate { Effects = new[] { E(EffectKind.Shield, 200) }, ShieldGranted = 0 };
            var label = EstimateLabelRules.ForAlly(est);
            Assert.That(label.Main.Value.Kind, Is.EqualTo(EstimateSegKind.Shield));
            Assert.That(label.Main.Value.Amount, Is.EqualTo(0));
        }

        [Test]
        public void Ally_MoraleAndBlock_HealAndHot()
        {
            var armor = new CastEstimate
            {
                Effects = new[] { E(EffectKind.Morale, 1), E(EffectKind.Block, 2) },
                MoraleGain = 1,
            };
            var a = EstimateLabelRules.ForAlly(armor);
            Assert.That(a.Main.Value.Kind, Is.EqualTo(EstimateSegKind.Morale));
            Assert.That(a.Main.Value.Amount, Is.EqualTo(1));
            Assert.That(a.Status.Value.Kind, Is.EqualTo(EstimateSegKind.Block));
            Assert.That(a.Status.Value.Amount, Is.EqualTo(2));

            var perMorale = new CastEstimate { Effects = new[] { E(EffectKind.Block, 1, ScaleBasis.Morale) } };
            Assert.That(EstimateLabelRules.ForAlly(perMorale).Main.Value.Kind, Is.EqualTo(EstimateSegKind.BlockPerMorale));

            var moist = new CastEstimate
            {
                Effects = new[] { E(EffectKind.HealSelf, 100), E(EffectKind.HealOverTime, 20) },
                HealAmount = 90,
            };
            var m = EstimateLabelRules.ForAlly(moist);
            Assert.That(m.Main.Value.Kind, Is.EqualTo(EstimateSegKind.Heal));
            Assert.That(m.Main.Value.Amount, Is.EqualTo(90));
            Assert.That(m.Status.Value.Kind, Is.EqualTo(EstimateSegKind.HealOverTime));
        }

        [Test]
        public void Ally_DuplicateKinds_CollapseToOneSegment()
        {
            var est = new CastEstimate
            {
                Effects = new[] { E(EffectKind.Shield, 100), E(EffectKind.ShieldAll, 50), E(EffectKind.DamageCut, 30) },
                ShieldGranted = 150,
            };
            var label = EstimateLabelRules.ForAlly(est);
            Assert.That(label.Main.Value.Kind, Is.EqualTo(EstimateSegKind.Shield));
            Assert.That(label.Status.Value.Kind, Is.EqualTo(EstimateSegKind.DamageCut), "同类护盾只算一段");
        }

        [Test]
        public void Ally_IgnoresEnemySideEffects()
        {
            var est = new CastEstimate { Effects = new[] { E(EffectKind.DamageSingle, 30), E(EffectKind.Slow, 2) } };
            Assert.That(EstimateLabelRules.ForAlly(est), Is.Null);
        }

        [Test]
        public void SpentOnly_StillProducesLabel_FirstSpentWins()
        {
            var first = Trait("折桂");
            var est = new CastEstimate
            {
                Effects = new[] { E(EffectKind.CritBuff, 5) },
                SpentTraits = new[] { first, Trait("杜门不出") },
            };
            var label = EstimateLabelRules.ForAlly(est);
            Assert.That(label, Is.Not.Null);
            Assert.That(label.Main, Is.Null);
            Assert.That(label.Spent, Is.SameAs(first));
            Assert.That(EstimateLabelRules.SpentOf(est), Is.SameAs(first));
            Assert.That(EstimateLabelRules.SpentOf(new CastEstimate()), Is.Null);
        }
    }
}
