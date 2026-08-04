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

        // ---- Critical 1 修复验证(2026-08-05):EndlessSaveState.CarriedDamageReductions 改名为
        // CarriedStatuses,JSON 形状也从 Dictionary<string,int> 换成了 List<StatusEffect>。旧存档的
        // 同名字段类型不匹配,原先会在 FromJson 里抛 JsonException,被兜底成 `return new MetaState()`
        // ——整份存档(墨锭/卡等级/图鉴/Perk)清零。改名后旧键变成未知键,Newtonsoft 直接忽略。 ----

        [TestCase("{\"铠\":20}")]      // 旧存档带一条减伤
        [TestCase("{}")]              // 连空字典也不该炸
        public void LegacyCarriedDamageReductions_DictShape_DoesNotWipeSave(string legacyDictJson)
        {
            var legacyJson = "{\"Ink\":12345,\"Endless\":{\"Depth\":3,\"CarriedDamageReductions\":"
                + legacyDictJson + "}}";

            var meta = SaveSerializer.FromJson(legacyJson);

            Assert.That(meta.Ink, Is.EqualTo(12345), "旧键改名后不应再把整份存档清空");
            Assert.That(meta.Endless, Is.Not.Null);
            Assert.That(meta.Endless.Depth, Is.EqualTo(3));
        }
    }
}
