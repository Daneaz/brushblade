using Brushblade.Core;
using Brushblade.Data;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>目标形状字段(shape/shapePercent/shots)的解析测试(2026-08-22)。
    /// 单独成文件的原因:这两条只吃内联 JSON 字符串、不碰 <c>UnityEngine.Application</c>,
    /// 但 <c>ConfigLoaderTests.cs</c> 里别的测试用了 <c>Application.streamingAssetsPath</c>
    /// 读实船 chars.json,整个文件因此被 tools/coretests 的 csproj 用 Exclude 挡在工装外
    /// (只能等 Unity EditMode 才跑得到)。这两条测试没有那个依赖,单独放一个文件才能被
    /// coretests 的 `Tests/**/*.cs` 通配收进去,随全量一起跑——别把它们合并回
    /// ConfigLoaderTests.cs,会连坐被排除。</summary>
    public class ConfigShapeParsingTests
    {
        [Test]
        public void LoadGraph_UnknownShape_Throws()
        {
            var ex = Assert.Throws<ConfigException>(() => ConfigLoader.LoadGraph(
                @"{ ""chars"": [ { ""id"": ""火"", ""effects"": [
                    { ""kind"": ""DamageSingle"", ""value"": 4, ""shape"": ""Nuke"" } ] } ] }"));
            Assert.That(ex.Message, Does.Contain("火"));
        }

        [Test]
        public void LoadGraph_ParsesShapeFields()
        {
            const string json = @"{""chars"":[
                {""id"":""甲"",""element"":""Fire"",""effects"":[
                    {""kind"":""DamageSingle"",""value"":10,""shape"":""Cleave"",
                     ""shapePercent"":50}]},
                {""id"":""乙"",""element"":""Fire"",""effects"":[
                    {""kind"":""DamageSingle"",""value"":6,""shape"":""Volley"",""shots"":3}]}
            ]}";
            var graph = ConfigLoader.LoadGraph(json);

            var jia = graph.Get("甲").Effects[0];
            Assert.That(jia.Shape, Is.EqualTo(TargetShape.Cleave));
            Assert.That(jia.ShapePercent, Is.EqualTo(50));

            var yi = graph.Get("乙").Effects[0];
            Assert.That(yi.Shape, Is.EqualTo(TargetShape.Volley));
            Assert.That(yi.Shots, Is.EqualTo(3));
        }
    }
}
