using System;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>spec A §7:水 / 土字攻击面按本次出手直接伤害的实际量攒泉 / 厚(1:1,每 100 一层)。
    /// 实际量 = 掉血 + 敌盾吸收,不计击杀溢出;全体按目标求和;DoT / 回敬 / 引爆不计;攻面带释放泉 / 厚的字不攒。</summary>
    public class AttackAccrualTests
    {
        private static CharDef Dual(string id, Element element, params EffectDef[] attack) =>
            new(id, element,
                effects: new[] { new EffectDef(EffectKind.Cleanse, 0) },   // 五行面随便给一条不攒资源的效果
                attackEffects: attack, isComponent: false);   // 无配方的夹具字缺省会被当成部件(IsLeaf),部件直出按 D20 不攒

        private static BattleEngine Battle(CharDef def, params EnemyDef[] enemies) =>
            new(RebalanceFixture.Graph(def),
                new BattleConfig { PlayerMaxHp = RebalanceFixture.BaseMaxHp, PlayerAttack = 100 },
                new[] { def.Id, def.Id, def.Id }, Array.Empty<string>(),
                enemies.Length > 0 ? enemies : new[] { RebalanceFixture.Mob() }, seed: 1);

        private static int Stacks(BattleEngine b, StatusKind kind) => b.PlayerStatuses.TotalMagnitude(kind);

        [Test]
        public void Water_AttackFace_GainsWellspring_FromDirectDamage()
        {
            var b = Battle(Dual("泉攻", Element.Water, new EffectDef(EffectKind.DamageSingle, 100)));
            Assert.That(b.Cast("泉攻", 0, attackMode: true), Is.EqualTo(BattleError.None));
            Assert.That(Stacks(b, StatusKind.Wellspring), Is.EqualTo(1));
            Assert.That(Stacks(b, StatusKind.Heft), Is.EqualTo(0));
        }

        [Test]
        public void Earth_AttackFace_GainsHeft_FromDirectDamage()
        {
            var b = Battle(Dual("厚攻", Element.Earth, new EffectDef(EffectKind.DamageSingle, 100)));
            b.Cast("厚攻", 0, attackMode: true);
            Assert.That(Stacks(b, StatusKind.Heft), Is.EqualTo(1));
            Assert.That(Stacks(b, StatusKind.Wellspring), Is.EqualTo(0));
        }

        [TestCase(Element.Fire)]
        [TestCase(Element.Metal)]
        [TestCase(Element.Wood)]
        public void OtherElements_AttackFace_GainNothing(Element element)
        {
            var b = Battle(Dual("他攻", element, new EffectDef(EffectKind.DamageSingle, 100)));
            b.Cast("他攻", 0, attackMode: true);
            Assert.That(Stacks(b, StatusKind.Wellspring) + Stacks(b, StatusKind.Heft), Is.EqualTo(0));
        }

        [Test]
        public void Counter_IsCounted_AfterWuxing()
        {
            // 水克火 ×1.5:每发 150,两发 300 → 3 层
            var b = Battle(Dual("泉攻", Element.Water, new EffectDef(EffectKind.DamageSingle, 100)),
                new EnemyDef("焰", Element.Fire, 100000, 0));
            b.Cast("泉攻", 0, attackMode: true);
            b.Cast("泉攻", 0, attackMode: true);
            Assert.That(Stacks(b, StatusKind.Wellspring), Is.EqualTo(3));
        }

        [Test]
        public void Overkill_IsNotCounted()
        {
            // 150 血:第一发 100(1 层,余 0),第二发只落 50 → 余 50,仍 1 层;算溢出会是 2 层
            var b = Battle(Dual("泉攻", Element.Water, new EffectDef(EffectKind.DamageSingle, 100)),
                RebalanceFixture.Mob(hp: 150));
            b.Cast("泉攻", 0, attackMode: true);
            b.Cast("泉攻", 0, attackMode: true);
            Assert.That(Stacks(b, StatusKind.Wellspring), Is.EqualTo(1));
        }

        [Test]
        public void AllShape_SumsEveryTarget()
        {
            var b = Battle(Dual("泉攻", Element.Water, new EffectDef(EffectKind.DamageSingle, 100, shape: TargetArea.All)),
                RebalanceFixture.Mob(), RebalanceFixture.Mob());
            b.Cast("泉攻", 0, attackMode: true);
            Assert.That(Stacks(b, StatusKind.Wellspring), Is.EqualTo(2));
        }

        [Test]
        public void AttackFaceThatSpendsTheResource_DoesNotAccrue()
        {
            var b = Battle(Dual("溃测", Element.Water,
                new EffectDef(EffectKind.DamageSingle, 100), new EffectDef(EffectKind.SpendWellspring, 10)));
            b.Cast("溃测", 0, attackMode: true);
            Assert.That(Stacks(b, StatusKind.Wellspring), Is.EqualTo(0));
            var e = Battle(Dual("崩测", Element.Earth,
                new EffectDef(EffectKind.DamageSingle, 100), new EffectDef(EffectKind.SpendHeft, 10)));
            e.Cast("崩测", 0, attackMode: true);
            Assert.That(Stacks(e, StatusKind.Heft), Is.EqualTo(0));
        }

        [Test]
        public void Component_AttackFace_DoesNotAccrue()
        {
            // D20:部件直出不计。无配方、不显式标 isComponent 的字缺省就是部件
            var part = new CharDef("泉件", Element.Water,
                effects: new[] { new EffectDef(EffectKind.Cleanse, 0) },
                attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 100) });
            Assert.That(part.IsComponent, Is.True);
            var b = Battle(part);
            b.Cast("泉件", 0, attackMode: true);
            Assert.That(Stacks(b, StatusKind.Wellspring), Is.EqualTo(0));
        }

        [Test]
        public void EndOfTurnBurn_IsNotCounted()
        {
            // 每发直伤 60;灼 5 层回合末跳 100 —— 灼算进去会多 1 层
            var b = Battle(Dual("泉攻", Element.Water,
                new EffectDef(EffectKind.DamageSingle, 60), new EffectDef(EffectKind.BurnSingle, 5)));
            b.Cast("泉攻", 0, attackMode: true);
            b.EndTurn();
            b.Cast("泉攻", 0, attackMode: true);
            Assert.That(Stacks(b, StatusKind.Wellspring), Is.EqualTo(1), "60 + 60 = 120 → 1 层");
        }

        [Test]
        public void FeatureFace_StillAccruesByNominalHeal_Unchanged()
        {
            var def = new CharDef("泉疗", Element.Water,
                effects: new[] { new EffectDef(EffectKind.HealSelf, 100) },
                attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 10) }, isComponent: false);
            var b = Battle(def);
            b.Cast("泉疗", -1);
            Assert.That(Stacks(b, StatusKind.Wellspring), Is.EqualTo(1), "五行面口径不变:名义治疗量");
        }
    }
}
