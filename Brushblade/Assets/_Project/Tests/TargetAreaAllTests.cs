using System;
using System.Linq;
using Brushblade.Core;
using Brushblade.Data;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>spec v7 §3.2 / §11.6:全体 = DamageSingle + TargetArea.All,DamageAll 退役。
    /// 口径:All 下每个目标都是主目标(斩杀/穿透/多段/100%),不需要选目标。</summary>
    public class TargetAreaAllTests
    {
        [Test]
        public void All_IsAppendedAtOrdinalSix() =>
            Assert.That((int)TargetArea.All, Is.EqualTo(6));

        [Test]
        public void ExpandTargets_All_ReturnsEveryLivingEnemyOnce_IgnoresPrimary()
        {
            var enemies = new[]
            {
                new EnemyState(RebalanceFixture.Mob()),
                new EnemyState(RebalanceFixture.Mob()),
                new EnemyState(RebalanceFixture.Mob()),
            };
            enemies[1].Hp = 0;
            var targets = Targeting.ExpandTargets(enemies, -1, TargetArea.All, 0);
            Assert.That(targets, Is.EqualTo(new[] { 0, 2 }));
        }

        /// <summary>跨排 Boss 在 All 下只出现一次 —— 不走 AddHits 的「同一下标记两次」,
        /// 与退役的 DamageAll 逐目标各打一下同口径。</summary>
        [Test]
        public void ExpandTargets_All_TwoRowBossAppearsOnce()
        {
            var enemies = new[]
            {
                new EnemyState(new EnemyDef("钧", Element.Heart, 100, 0, columnSpan: 2, rowSpan: 2)),
                new EnemyState(RebalanceFixture.Mob()),
            };
            var targets = Targeting.ExpandTargets(enemies, 0, TargetArea.All, 0);
            Assert.That(targets, Is.EqualTo(new[] { 0, 1 }));
        }

        [Test]
        public void Cast_All_WithoutTarget_HitsEveryEnemyFullValue()
        {
            var def = new CharDef("全", Element.Heart, effects: new[]
            {
                new EffectDef(EffectKind.DamageSingle, 30, shape: TargetArea.All),
            });
            var b = RebalanceFixture.Battle(RebalanceFixture.Graph(def), new[] { "全", "全" },
                RebalanceFixture.Mob(), RebalanceFixture.Mob());
            Assert.That(BattleEngine.NeedsTarget(def), Is.False, "全体不选目标");
            Assert.That(BattleEngine.EffectNeedsTarget(def.Effects[0]), Is.False, "全体不选目标");
            b.Cast("全", -1);
            Assert.That(b.Enemies.Select(e => e.Hp).ToArray(), Is.EqualTo(new[] { 100000 - 30, 100000 - 30 }));
        }

        [Test]
        public void Cast_All_ExecutesEachTargetIndependently()
        {
            var def = new CharDef("斩", Element.Heart, effects: new[]
            {
                new EffectDef(EffectKind.DamageSingle, 1, shape: TargetArea.All,
                    executeBelowPercent: 50, executeKills: true),
            });
            var b = RebalanceFixture.Battle(RebalanceFixture.Graph(def), new[] { "斩", "斩" },
                RebalanceFixture.Mob(hp: 100), RebalanceFixture.Mob(hp: 100));
            b.Enemies[0].Hp = 40;
            b.Enemies[1].Hp = 40;
            b.Cast("斩", -1);
            Assert.That(b.Enemies.All(e => !e.Alive), Is.True, "两个都在斩杀线下,各自被斩");
        }

        [Test]
        public void AttackShapeOf_AllChar_ReturnsAllWithZeroShots()
        {
            var def = new CharDef("全", Element.Heart, effects: new[]
            {
                new EffectDef(EffectKind.DamageSingle, 30, shape: TargetArea.All),
            });
            Assert.That(BattleEngine.AttackShapeOf(def), Is.EqualTo((TargetArea.All, 0)));
        }

        [Test]
        public void ConfigLoader_ParsesAllShape_AndRejectsDamageAll()
        {
            const string ok = "{\"chars\":[{\"id\":\"全\",\"element\":\"Heart\",\"effects\":[{\"kind\":\"DamageSingle\",\"value\":30,\"shape\":\"All\"}]}]}";
            var graph = ConfigLoader.LoadGraph(ok);
            Assert.That(graph.Get("全").Effects[0].Shape, Is.EqualTo(TargetArea.All));

            const string retired = "{\"chars\":[{\"id\":\"全\",\"element\":\"Heart\",\"effects\":[{\"kind\":\"DamageAll\",\"value\":30}]}]}";
            var ex = Assert.Throws<ConfigException>(() => ConfigLoader.LoadGraph(retired));
            // 报错要说清改法,不能只是「效果类型未知:DamageAll」(那句也含 "All")
            Assert.That(ex.Message, Does.Contain("退役"));
            Assert.That(ex.Message, Does.Contain("DamageSingle"));
            Assert.That(ex.Message, Does.Contain("All"));
        }
    }
}
