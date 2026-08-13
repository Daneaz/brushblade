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
            var legacyJson = "{\"Ink\":12345,\"EndlessV2\":{\"Depth\":3,\"CarriedDamageReductions\":"
                + legacyDictJson + "}}";

            var meta = SaveSerializer.FromJson(legacyJson);

            Assert.That(meta.Ink, Is.EqualTo(12345), "旧键改名后不应再把整份存档清空");
            Assert.That(meta.EndlessV2, Is.Not.Null);
            Assert.That(meta.EndlessV2.Depth, Is.EqualTo(3));
        }

        // ---- E-b4+E-b5 T6(2026-08-12):MetaState.Endless 改名为 EndlessV2。量级 ×10 与
        // 「减伤百分比 → 护甲点数」让整份登塔快照的数字全部作废,逐字段迁移没有旧存档样本可测。
        // 走的是上面那条已验证过的路径:改键名 → 旧的 "Endless" 变未知键 → Newtonsoft 忽略 →
        // 断点作废、养成外层完好,而不是抛 JsonException 被兜底成整份存档清空。 ----

        /// <summary>一份 T6 之前写下的真存档:旧的 "Endless" 键、旧量级(PlayerHp 50)、
        /// 已删除的 damageTaken、以及旧的 StatusKind 序号 5(当年的「减伤 20%」)。
        /// 顺带带上 SaveGuard 封条 —— 这是真存档文件到 MetaState 的完整入口。</summary>
        private const string LegacySaveJson =
            "{\"CharacterXp\":4321,\"Ink\":12345," +
            "\"CardLevels\":{\"剑\":7,\"城\":3}," +
            "\"PerkLevels\":{\"perk_hp\":4}," +
            "\"CardCopies\":{\"剑\":9}," +
            "\"OwnedCards\":[\"剑\",\"城\",\"爆\"]," +
            "\"Deck\":[\"剑\",\"城\"]," +
            "\"BestDepth\":17,\"BandMilestones\":[\"band_1\"]," +
            "\"DefeatedEnemies\":[\"mo_zi\",\"deng_hua\"],\"ClaimedBestiary\":[\"mo_zi\"]," +
            "\"Endless\":{\"Depth\":13,\"PlayerHp\":50,\"Seed\":999,\"TopBossDepth\":10," +
                "\"NormalShield\":8,\"CarriedStatuses\":[{\"Kind\":5,\"Magnitude\":20,\"TurnsLeft\":-1}]," +
                "\"InProgress\":{\"Battle\":{\"PlayerHp\":42,\"Enemies\":" +
                    "[{\"DefId\":\"mo_zi\",\"Hp\":30,\"MaxHp\":30,\"damageTaken\":0.6,\"BaseAttack\":8}]}}}}";

        [Test]
        public void LegacyEndlessKey_IsIgnored_KeepsMetaProgress()
        {
            Assert.That(SaveGuard.TryOpen(SaveGuard.Seal(LegacySaveJson), out var payload), Is.True);

            var meta = SaveSerializer.FromJson(payload);   // 真入口,不直接碰 Newtonsoft

            // 养成外层:一格不许丢
            Assert.That(meta.CharacterXp, Is.EqualTo(4321));
            Assert.That(meta.Ink, Is.EqualTo(12345));
            Assert.That(meta.CardLevels["剑"], Is.EqualTo(7));
            Assert.That(meta.CardLevels["城"], Is.EqualTo(3));
            Assert.That(meta.PerkLevels["perk_hp"], Is.EqualTo(4));
            Assert.That(meta.CardCopies["剑"], Is.EqualTo(9));
            Assert.That(meta.OwnedCards, Is.EqualTo(new[] { "剑", "城", "爆" }));
            Assert.That(meta.Deck, Is.EqualTo(new[] { "剑", "城" }));
            Assert.That(meta.BestDepth, Is.EqualTo(17));
            Assert.That(meta.BandMilestones, Is.EqualTo(new[] { "band_1" }));
            Assert.That(meta.DefeatedEnemies, Is.EqualTo(new[] { "mo_zi", "deng_hua" }));
            Assert.That(meta.ClaimedBestiary, Is.EqualTo(new[] { "mo_zi" }));

            // 断点:作废(旧键是未知键),玩家回主界面重新开塔
            Assert.That(meta.EndlessV2, Is.Null, "旧登塔快照必须作废,不能被读回来");
        }

        [Test]
        public void LegacyEndlessKey_DoesNotThrow_EvenUnsealed()
        {
            Assert.DoesNotThrow(() => SaveSerializer.FromJson(LegacySaveJson));
        }
    }
}
