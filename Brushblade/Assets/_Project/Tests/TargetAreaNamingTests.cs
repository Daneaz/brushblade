using System;
using Brushblade.Core;
using Brushblade.Data;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>范围枚举按「范围」命名(spec v6 §3.2):序数与改名前一致,存档里的整数不受影响。</summary>
    public class TargetAreaNamingTests
    {
        [Test]
        public void Values_AreRangeNames_WithStableOrdinals()
        {
            Assert.That((int)TargetArea.Single, Is.EqualTo(0));
            Assert.That((int)TargetArea.Row, Is.EqualTo(1));
            Assert.That((int)TargetArea.Adjacent, Is.EqualTo(2));
            Assert.That((int)TargetArea.Column, Is.EqualTo(3));
            Assert.That((int)TargetArea.Scatter, Is.EqualTo(4));
            Assert.That((int)TargetArea.Chain, Is.EqualTo(5));
            // spec v7 §11.6:全体并进来,尾部追加
            Assert.That((int)TargetArea.All, Is.EqualTo(6));
            Assert.That(Enum.GetNames(typeof(TargetArea)).Length, Is.EqualTo(7));
        }

        [Test]
        public void ConfigLoader_ParsesRangeTokens()
        {
            var graph = ConfigLoader.LoadGraph(@"{""chars"":[
                {""id"":""甲"",""element"":""Fire"",""effects"":[
                    {""kind"":""DamageSingle"",""value"":10,""shape"":""Row"",""shapePercent"":50}]},
                {""id"":""乙"",""element"":""Fire"",""effects"":[
                    {""kind"":""DamageSingle"",""value"":6,""shape"":""Scatter"",""shots"":3}]}
            ]}");
            Assert.That(graph.Get("甲").Effects[0].Shape, Is.EqualTo(TargetArea.Row));
            Assert.That(graph.Get("乙").Effects[0].Shape, Is.EqualTo(TargetArea.Scatter));
        }

        [Test]
        public void ConfigLoader_RejectsOldActionTokens()
        {
            Assert.Throws<ConfigException>(() => ConfigLoader.LoadGraph(@"{""chars"":[
                {""id"":""甲"",""element"":""Fire"",""effects"":[
                    {""kind"":""DamageSingle"",""value"":10,""shape"":""Sweep""}]}]}"));
        }
    }
}
