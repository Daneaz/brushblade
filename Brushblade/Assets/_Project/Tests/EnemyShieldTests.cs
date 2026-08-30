using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>敌人护盾(2026-08-30):护甲减法之后、扣血之前吸收。
    ///
    /// ⚠ **来源留白**(用户 2026-08-30 拍板):enemies.json 没有护盾字段,也没有结盾技能——
    /// 盾的来源将来是「加盾辅助怪给同伴挂 buff」,那类小怪还没设计。所以真机上
    /// 场上敌人的 Shield 恒为 0,**这些用例是唯一的验证手段**,别因为「看不见」就当没做。</summary>
    public class EnemyShieldTests
    {
        // 弹:心系 50 伤,心中立(全属性 ×1.0),用来测护盾本身而不搅动生克(同 DefenseWiringTests 的口径)。
        // 斫:木系 50 伤,木克土 ×1.5,专门用来触发相克(绕护甲、但不该绕盾)。
        // 掴:心系 40 伤,心中立;专门配合护甲 30 / 盾 20 这组「顺序反转会被拦住」的数字
        // (Review 2026-08-30 指出「弹」的 50 配不出判别力,见下方 Shield_AbsorbsAfterDefenseSubtraction
        // 的算式注释——不能与「弹」共用基础值,数字是这条测试判别力的一部分)。
        private static RecipeGraph Graph() => new(new[]
        {
            new CharDef("木", Element.Wood),
            new CharDef("弹", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 50) }),
            new CharDef("斫", Element.Wood,
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 50) }),
            new CharDef("掴", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 40) }),
        });

        private static BattleEngine Engine(EnemyDef enemy) =>
            new(Graph(), new BattleConfig { DropTable = new[] { "木" }, PlayerMaxHp = 50 },
                new[] { "弹", "斫", "掴" }, Array.Empty<string>(), new[] { enemy }, seed: 1);

        [Test]
        public void Shield_AbsorbsBeforeHp()
        {
            // 盾 30、血 100,挨一记 50(心系,中立无护甲):盾清零、血掉 20
            var engine = Engine(new EnemyDef("靶", Element.Earth, 100, 0));
            engine.Enemies[0].Shield = 30;
            engine.Cast("弹", 0);
            Assert.That(engine.Enemies[0].Shield, Is.EqualTo(0), "盾被打空");
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(80), "50 − 30 盾 = 20 点穿盾落地");
        }

        [Test]
        public void Shield_AbsorbsAfterDefenseSubtraction()
        {
            // 护甲 30、盾 20、血 100,挨一记 40(心系中立)。
            //
            // ⚠ 这组数字是刻意挑的,不是随手换的(Review 2026-08-30 指出上一版护甲 10/盾 30/伤害 50
            // 那组没有判别力——当伤害 ≥ 护甲+盾 时,max(0,D−A)−S 与 max(0,D−S)−A 在那个区间代数恒等,
            // 两种顺序算出来的血量/盾残值逐位相同,谁先谁后测不出来)。
            //
            // 正确顺序(先减甲、再吃盾):40−30=10 → 盾吸收 min(20,10)=10 → 血掉 0,盾剩 10
            // 反过来(先吃盾、再减甲):盾吸收 min(20,40)=20 → 剩 20 → max(0,20−30)=0 → 血掉 0,盾剩 0
            //
            // 两条路径的 Hp 都是 0(判别力不在这——血量这条断言留着只是钉住「没有多穿透」的兜底),
            // 真正的判别力在 Shield 残值:10(正确)对 0(反了)。数学上是「护甲减法先撞上
            // Math.Max(0,…) 下限、护盾吸收没撞」与「护盾先吸收未撞、护甲减法才撞」这两种情形分道扬镳——
            // 判据就是让某一步先触底。已用变异检查验证(把吸收段挪到护甲减法之前会让 Shield 断言变红,
            // 见 task-3-report.md 的反向变异记录)。
            var engine = Engine(new EnemyDef("靶", Element.Earth, 100, 0, defense: 30));
            engine.Enemies[0].Shield = 20;
            engine.Cast("掴", 0);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(100), "40−30=10 全被 20 盾吃掉,一点没掉血");
            Assert.That(engine.Enemies[0].Shield, Is.EqualTo(10),
                "盾只吃了 10 剩 10 —— 顺序反了会把盾吃满剩 0,这才是本条真正的判别力所在");
        }

        [Test]
        public void Shield_CounterAttackStillAbsorbed()
        {
            // 相克那一记绕过护甲,但盾照常吸收(用户拍板:护甲是硬度,护盾是一层临时血)。
            // 木克土 ×1.5:floor(50×1.5)=75,10 点护甲被相克整层无视,盾 30 照常吃满,血掉 45
            var engine = Engine(new EnemyDef("靶", Element.Earth, 200, 0, defense: 10));
            engine.Enemies[0].Shield = 30;
            engine.Cast("斫", 0);
            Assert.That(engine.Enemies[0].Shield, Is.EqualTo(0), "相克不绕盾,盾照样被打空");
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(200 - 45),
                "75(相克后,护甲已被整层无视) − 30 盾 = 45 点穿盾落地");
        }

        [Test]
        public void Shield_DamageEventCarriesAbsorbedAmount()
        {
            // Damage 事件的 Absorbed = 被盾吃掉的量,Amount = 打出去的总伤(与玩家侧 EnemyAttack 同口径)
            var engine = Engine(new EnemyDef("靶", Element.Earth, 100, 0));
            engine.Enemies[0].Shield = 30;
            engine.Cast("弹", 0);
            var dmg = engine.LastEvents.Single(e => e.Kind == BattleEventKind.Damage);
            Assert.That(dmg.Amount, Is.EqualTo(50), "打出去的总伤");
            Assert.That(dmg.Absorbed, Is.EqualTo(30), "被盾吃掉的部分");
            Assert.That(dmg.Amount - dmg.Absorbed, Is.EqualTo(20), "两者相减 = 实际掉血");
        }

        [Test]
        public void Shield_SurvivesSnapshotRoundTrip()
        {
            // 存档往返后 Shield 不丢 —— 漏补快照字段是静默的(RunSnapshot.cs:9 那条警告),
            // 仿 EnemyRowTests.EnemyColumn_SurvivesSnapshotRoundTrip 同一套钉法
            var enemyDef = new EnemyDef("靶", Element.Earth, 100, 0);
            var engine = Engine(enemyDef);
            engine.Enemies[0].Shield = 30;

            var config = new BattleConfig { DropTable = new[] { "木" }, PlayerMaxHp = 50 };
            var defs = new Dictionary<string, EnemyDef> { [enemyDef.Id] = enemyDef };
            var restored = BattleEngine.Restore(engine.Capture(), Graph(), config, null, defs);

            Assert.That(restored.Enemies[0].Shield, Is.EqualTo(30));
        }
    }
}
