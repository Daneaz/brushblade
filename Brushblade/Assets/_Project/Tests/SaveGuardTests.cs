using Brushblade.Core;
using Brushblade.Data;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>存档防篡改(19.9):签名包裹,改动即判损坏回全新状态(单机容忍度内的防线)。</summary>
    public class SaveGuardTests
    {
        [Test]
        public void SealAndOpen_RoundTrips()
        {
            var meta = new MetaState { Ink = 777, CharacterXp = 123 };
            var sealedText = SaveGuard.Seal(SaveSerializer.ToJson(meta));
            var opened = SaveGuard.TryOpen(sealedText, out var payload);

            Assert.That(opened, Is.True);
            Assert.That(SaveSerializer.FromJson(payload).Ink, Is.EqualTo(777));
        }

        [Test]
        public void TamperedPayload_Rejected()
        {
            var sealedText = SaveGuard.Seal(SaveSerializer.ToJson(new MetaState { Ink = 10 }));
            var tampered = sealedText.Replace("\"Ink\":10", "\"Ink\":99999");
            Assert.That(SaveGuard.TryOpen(tampered, out _), Is.False);
        }

        [Test]
        public void TamperedSignature_Rejected()
        {
            var sealedText = SaveGuard.Seal("{\"a\":1}");
            var tampered = sealedText.Substring(0, sealedText.Length - 8) + "00000000";
            Assert.That(SaveGuard.TryOpen(tampered, out _), Is.False);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("not sealed at all")]
        public void GarbageInput_Rejected(string input)
        {
            Assert.That(SaveGuard.TryOpen(input, out _), Is.False);
        }

        [Test]
        public void LegacyUnsealedSave_NotOpenable() // 旧明文档视作损坏(原型期可接受重置)
        {
            var legacy = SaveSerializer.ToJson(new MetaState { Ink = 5 });
            Assert.That(SaveGuard.TryOpen(legacy, out _), Is.False);
        }
    }
}
