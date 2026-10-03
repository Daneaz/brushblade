using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>目标形状的缺省契约(2026-08-22,spec §3.1)。
    /// 缺省必须是 Single 且 ShapePercent = 100 —— 恒等性硬线全靠这条:
    /// 87 张现有伤害字一个字节不改,行为就不能变。</summary>
    public class TargetShapeTests
    {
        [Test]
        public void DefaultShape_IsSingle()
        {
            var effect = new EffectDef(EffectKind.DamageSingle, 100);
            Assert.That(effect.Shape, Is.EqualTo(TargetArea.Single));
            Assert.That(effect.ShapePercent, Is.EqualTo(100), "非主目标缺省全额,形状不生效时这个值不该改变任何结果");
            Assert.That(effect.Shots, Is.EqualTo(0));
        }

        [Test]
        public void ShapeFields_RoundTripThroughConstructor()
        {
            var effect = new EffectDef(EffectKind.DamageSingle, 100,
                shape: TargetArea.Adjacent, shapePercent: 50);
            Assert.That(effect.Shape, Is.EqualTo(TargetArea.Adjacent));
            Assert.That(effect.ShapePercent, Is.EqualTo(50));
        }

        [Test]
        public void Volley_CarriesShots()
        {
            var effect = new EffectDef(EffectKind.DamageSingle, 40,
                shape: TargetArea.Scatter, shots: 3);
            Assert.That(effect.Shape, Is.EqualTo(TargetArea.Scatter));
            Assert.That(effect.Shots, Is.EqualTo(3));
        }

        [Test]
        public void ShapePercent_ZeroOrNegative_FallsBackToFull()
        {
            // 配置漏写 shapePercent 时 JSON 会填 0,那会让溅射的两侧一分不伤 ——
            // 静默失效比报错更难查,统一兜回 100(与 HitCount 的 `<=0 → 1` 同型)
            var effect = new EffectDef(EffectKind.DamageSingle, 100,
                shape: TargetArea.Adjacent, shapePercent: 0);
            Assert.That(effect.ShapePercent, Is.EqualTo(100));
        }

        [Test]
        public void Chain_CarriesShots()
        {
            // 弹射与连发共用 Shots 字段(跳数);几何与衰减在 DamageVariantTests 走引擎测
            var effect = new EffectDef(EffectKind.DamageSingle, 40,
                shape: TargetArea.Chain, shots: 3);
            Assert.That(effect.Shape, Is.EqualTo(TargetArea.Chain));
            Assert.That(effect.Shots, Is.EqualTo(3));
        }
    }
}
