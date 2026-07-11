using Brushblade.Data;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>字表拼音/释义(11.2.4 安全网:任何字点一下显示读音和意思)。</summary>
    public class ReadingsTests
    {
        [Test]
        public void LoadGraph_ParsesPinyinAndGloss()
        {
            var graph = ConfigLoader.LoadGraph(@"{
                ""chars"": [
                    { ""id"": ""灯"", ""element"": ""Fire"", ""pinyin"": ""dēng"", ""gloss"": ""照明的器具"" }
                ] }");
            var def = graph.Get("灯");
            Assert.That(def.Pinyin, Is.EqualTo("dēng"));
            Assert.That(def.Gloss, Is.EqualTo("照明的器具"));
        }

        [Test]
        public void LoadGraph_MissingReadings_DefaultsNull()
        {
            var graph = ConfigLoader.LoadGraph(@"{ ""chars"": [ { ""id"": ""火"" } ] }");
            Assert.That(graph.Get("火").Pinyin, Is.Null);
            Assert.That(graph.Get("火").Gloss, Is.Null);
        }
    }
}
