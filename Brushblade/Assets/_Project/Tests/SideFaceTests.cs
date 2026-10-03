using System;
using Brushblade.Core;
using Brushblade.Data;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>两面模型(spec v6 §2.1):副面本体只按 SideFacePercent 结算连续量;MainFace 缺省时恒等。</summary>
    public class SideFaceTests
    {
        private static CharDef Dual(CardFace? main) => new("沙", Element.Heart,
            effects: new[] { new EffectDef(EffectKind.Freeze, 1) },
            attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 100) },
            mainFace: main);

        private static BattleEngine Battle(CharDef def) =>
            RebalanceFixture.Battle(RebalanceFixture.Graph(def), new[] { def.Id, def.Id },
                RebalanceFixture.Mob());

        [Test]
        public void NoMainFace_IsIdentity()
        {
            var b = Battle(Dual(null));
            b.Cast("沙", 0, attackMode: true);
            Assert.That(b.Enemies[0].Hp, Is.EqualTo(100000 - 100));
        }

        [Test]
        public void MainFace_TakesFullValue()
        {
            var b = Battle(Dual(CardFace.Attack));
            b.Cast("沙", 0, attackMode: true);
            Assert.That(b.Enemies[0].Hp, Is.EqualTo(100000 - 100));
        }

        [Test]
        public void SideFace_ContinuousValue_ScaledTo60Percent()
        {
            var b = Battle(Dual(CardFace.Feature));
            b.Cast("沙", 0, attackMode: true);
            Assert.That(b.Enemies[0].Hp, Is.EqualTo(100000 - 60));
        }

        [Test]
        public void SideFace_DiscreteValue_NotScaled()
        {
            var b = Battle(Dual(CardFace.Attack));   // 冻结面是副面
            b.Cast("沙", 0);
            Assert.That(b.Enemies[0].Statuses.Find(StatusKind.Freeze).TurnsLeft, Is.EqualTo(1),
                "冻结回合是离散量,副面也不缩放");
        }

        [Test]
        public void FaceOf_SingleListChar_IsFeature()
        {
            var single = new CharDef("单", Element.Heart, effects: new[] { new EffectDef(EffectKind.DamageSingle, 1) });
            Assert.That(BattleEngine.FaceOf(single, attackMode: true), Is.EqualTo(CardFace.Feature));
            Assert.That(BattleEngine.FaceOf(Dual(null), attackMode: true), Is.EqualTo(CardFace.Attack));
            Assert.That(BattleEngine.FaceOf(Dual(null), attackMode: false), Is.EqualTo(CardFace.Feature));
        }

        [Test]
        public void ConfigLoader_ParsesMainFace()
        {
            var graph = ConfigLoader.LoadGraph(@"{""chars"":[
                {""id"":""甲"",""element"":""Fire"",""mainFace"":""Attack"",""effects"":[{""kind"":""DamageSingle"",""value"":1}]},
                {""id"":""乙"",""element"":""Fire"",""effects"":[{""kind"":""DamageSingle"",""value"":1}]}]}");
            Assert.That(graph.Get("甲").MainFace, Is.EqualTo(CardFace.Attack));
            Assert.That(graph.Get("乙").MainFace, Is.Null);
        }

        [Test]
        public void ConfigLoader_UnknownMainFace_Throws()
        {
            var ex = Assert.Throws<ConfigException>(() => ConfigLoader.LoadGraph(@"{""chars"":[
                {""id"":""甲"",""element"":""Fire"",""mainFace"":""Heal"",""effects"":[{""kind"":""DamageSingle"",""value"":1}]}]}"));
            Assert.That(ex.Message, Does.Contain("甲"));
        }
    }
}
