using System.Linq;
using Brushblade.Data;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>字符串表的基础行为(2026-08-22)。表内容用内联 JSON 注入,
    /// 不读真实文件 —— 真实表的完整性由 StringsTableTests 的三条对账负责。</summary>
    public sealed class StringsTests
    {
        [SetUp]
        public void Reset() => Strings.Load("{}");

        [Test]
        public void Load_ThenT_ReturnsText()
        {
            Strings.Load(@"{""battle.btn.exit"": ""退出""}");
            Assert.That(Strings.T("battle.btn.exit"), Is.EqualTo("退出"));
        }

        [Test]
        public void T_MissingKey_ReturnsBracketedKey()
        {
            // 缺 key 不抛异常:UI 不该因为漏一句话整屏白掉(spec §6)。
            Assert.That(Strings.T("no.such.key"), Is.EqualTo("?no.such.key?"));
        }

        [Test]
        public void T_NamedPlaceholders_AllReplaced()
        {
            Strings.Load(@"{""effect.morale"": ""战意+{stacks}层(每层攻击+{per},上限 {max} 层)""}");
            Assert.That(Strings.T("effect.morale", ("stacks", 3), ("per", 10), ("max", 5)),
                Is.EqualTo("战意+3层(每层攻击+10,上限 5 层)"));
        }

        [Test]
        public void T_SamePlaceholderTwice_BothReplaced()
        {
            // 锐:「本场穿透+{v}(本场每次攻击无视 {v} 点护甲)」—— 同名占位符出现两次
            Strings.Load(@"{""k"": ""穿透+{v},无视 {v} 点护甲""}");
            Assert.That(Strings.T("k", ("v", 15)), Is.EqualTo("穿透+15,无视 15 点护甲"));
        }

        [Test]
        public void T_UnsuppliedPlaceholder_LeftAsIs()
        {
            // 漏传参数不炸,占位符原样留着 —— 由占位符对账测试在工装里拦下(spec §9)
            Strings.Load(@"{""k"": ""a{x}b""}");
            Assert.That(Strings.T("k"), Is.EqualTo("a{x}b"));
        }

        [Test]
        public void T_NullValue_RendersEmpty()
        {
            Strings.Load(@"{""k"": ""[{x}]""}");
            Assert.That(Strings.T("k", ("x", null)), Is.EqualTo("[]"));
        }

        [Test]
        public void Load_Again_ReplacesTable()
        {
            // 换语言 = 换文件,旧表必须整个丢掉而不是合并
            Strings.Load(@"{""a"": ""1"", ""b"": ""2""}");
            Strings.Load(@"{""a"": ""x""}");
            Assert.That(Strings.T("a"), Is.EqualTo("x"));
            Assert.That(Strings.T("b"), Is.EqualTo("?b?"));
            Assert.That(Strings.Count, Is.EqualTo(1));
        }

        [Test]
        public void Keys_ExposesAllKeys()
        {
            Strings.Load(@"{""a"": ""1"", ""b"": ""2""}");
            Assert.That(Strings.Keys.OrderBy(k => k).ToArray(), Is.EqualTo(new[] { "a", "b" }));
        }

        [Test]
        public void Load_InvalidJson_Throws()
        {
            // fail fast,与 ConfigLoader 同口径:表坏了就别让游戏带着半张表跑起来
            Assert.That(() => Strings.Load("{ not json"), Throws.Exception);
        }
    }
}
