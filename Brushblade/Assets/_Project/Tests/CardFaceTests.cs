using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>出手面判定(spec v7 §2.1):攻击模式且有攻击效果 = Attack,否则 Feature(五行面)。</summary>
    public class CardFaceTests
    {
        [Test]
        public void FaceOf_FollowsAttackModeAndAttackEffects()
        {
            var single = new CharDef("单", Element.Heart, effects: new[] { new EffectDef(EffectKind.DamageSingle, 1) });
            var dual = new CharDef("双", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.Shield, 1) },
                attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 1) });
            Assert.That(BattleEngine.FaceOf(single, attackMode: true), Is.EqualTo(CardFace.Feature));
            Assert.That(BattleEngine.FaceOf(dual, attackMode: true), Is.EqualTo(CardFace.Attack));
            Assert.That(BattleEngine.FaceOf(dual, attackMode: false), Is.EqualTo(CardFace.Feature));
        }

        [Test]
        public void ConfigLoader_IgnoresLegacyMainFaceKey()
        {
            // spec v7 取消主面:旧数据若带 mainFace 键,Newtonsoft 静默忽略,不报错
            Assert.DoesNotThrow(() => Brushblade.Data.ConfigLoader.LoadGraph(
                @"{""chars"":[{""id"":""甲"",""element"":""Fire"",""mainFace"":""Heal"",""effects"":[{""kind"":""DamageSingle"",""value"":1}]}]}"));
        }
    }
}
