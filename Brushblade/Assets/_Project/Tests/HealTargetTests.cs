using System.Collections.Generic;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>治疗目标选择(2026-08-22,spec §8)。单体治疗从「只能治自己」
    /// 改为出字时选玩家或某只存活召唤物。</summary>
    public class HealTargetTests
    {
        /// <summary>素:召 1 只 100 血、攻 0 的召唤物(攻 0 = 绝不反击,敌人血量恒定);
        /// 泉:单体治疗 30;涌:群治 30(用来钉群治不吃 allySlot)。
        /// 全用 Element.Heart —— 心中立、全 1.0x,断言不受生克干扰。</summary>
        private static RecipeGraph Graph() => new(new[]
        {
            new CharDef("素", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.Summon, 100,
                    summonCount: 1, summonAttack: 0, summonChar: "木") }),
            new CharDef("泉", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.HealSelf, 30) }),
            new CharDef("涌", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.HealAll, 30) }),
            new CharDef("刃", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 10) }),
        });

        /// <summary>攻 0 的靶子:敌人不还手,血量变化只可能来自玩家出字。</summary>
        private static EnemyDef Dummy() => new("靶", Element.Heart, 200, 0);

        /// <summary>字全放部件池直出(叶子字免配方),AP 给足。startingHp 用来把玩家血量磨到
        /// 测试起点——PlayerHp 的 setter 在生产代码里是 private,不能像 Summons[i].Hp 那样
        /// 直接在测试里赋值,构造函数原生的 startingHp 参数就是既有测试的标准做法。</summary>
        private static BattleEngine Engine(int? startingHp = null) =>
            new(Graph(), new BattleConfig { PlayerMaxHp = 100 },
                System.Array.Empty<string>(),
                new[] { "素", "素", "泉", "泉", "涌", "刃" },
                new[] { Dummy() }, seed: 1, startingHp: startingHp);

        [Test]
        public void Heal_DefaultsToPlayer()
        {
            var engine = Engine(startingHp: 50);
            Assert.That(engine.Cast("泉"), Is.EqualTo(BattleError.None));
            Assert.That(engine.PlayerHp, Is.EqualTo(80), "不传 allySlot 时治玩家,与改前一致");
        }

        [Test]
        public void Heal_TargetsChosenSummon()
        {
            var engine = Engine(startingHp: 50);
            engine.Cast("素", summonSlots: new[] { 0 });
            engine.Summons[0].Hp = 40;

            Assert.That(engine.Cast("泉", allySlot: 0), Is.EqualTo(BattleError.None));
            Assert.That(engine.Summons[0].Hp, Is.EqualTo(70), "治的是召唤物");
            Assert.That(engine.PlayerHp, Is.EqualTo(50), "玩家一分不回");
        }

        [Test]
        public void Heal_OnSummon_ClampsToMaxHp()
        {
            var engine = Engine();
            engine.Cast("素", summonSlots: new[] { 0 });
            engine.Summons[0].Hp = 90; // 上限 100,治 30 只能回 10
            engine.Cast("泉", allySlot: 0);
            Assert.That(engine.Summons[0].Hp, Is.EqualTo(100), "溢出丢弃");
        }

        [Test]
        public void Heal_OnEmptySlot_ReturnsInvalidTarget()
        {
            var engine = Engine();
            engine.Cast("素", summonSlots: new[] { 0 }); // 只有 0 号槽有人
            int apBefore = engine.Ap;

            Assert.That(engine.Cast("泉", allySlot: 3), Is.EqualTo(BattleError.InvalidTarget));
            Assert.That(engine.Ap, Is.EqualTo(apBefore), "拒出时 AP 不扣");
        }

        [Test]
        public void Heal_OnCorpse_ReturnsInvalidTarget()
        {
            var engine = Engine();
            engine.Cast("素", summonSlots: new[] { 0 });
            engine.Summons[0].Hp = 0; // 尸体:占槽但归复活管,治疗救不回来

            Assert.That(engine.Cast("泉", allySlot: 0), Is.EqualTo(BattleError.InvalidTarget));
        }

        [Test]
        public void Heal_NoAliveSummon_AutoLocksToPlayer()
        {
            // 场上没有召唤物时免选:即便 UI 传了个荒唐的槽位,也自动锁玩家而不是报错
            // (与「单敌免选」同一条口径)
            var engine = Engine(startingHp: 50);
            Assert.That(engine.Cast("泉", allySlot: 4), Is.EqualTo(BattleError.None));
            Assert.That(engine.PlayerHp, Is.EqualTo(80));
        }

        [Test]
        public void NeedsAllyTarget_TrueForHeal_FalseForDamageAndGroupHeal()
        {
            Assert.That(BattleEngine.NeedsAllyTarget(Graph().Get("泉")), Is.True);
            Assert.That(BattleEngine.NeedsAllyTarget(Graph().Get("刃")), Is.False);
            Assert.That(BattleEngine.NeedsAllyTarget(Graph().Get("涌")), Is.False,
                "群治覆盖全体,本就无从选起");
        }

        [Test]
        public void Heal_EmitsEventWithSlotInSecondIndex()
        {
            var engine = Engine();
            engine.Cast("素", summonSlots: new[] { 2 });
            engine.Summons[2].Hp = 40;
            engine.Cast("泉", allySlot: 2);

            BattleEvent? heal = null;
            foreach (var e in engine.LastEvents)
                if (e.Kind == BattleEventKind.Heal) heal = e;
            Assert.That(heal, Is.Not.Null);
            Assert.That(heal.Value.SecondIndex, Is.EqualTo(2), "SecondIndex 带槽位,表现层据此画哪一格");
        }

        [Test]
        public void Heal_OnPlayer_EmitsPlayerSlotInSecondIndex()
        {
            var engine = Engine(startingHp: 50);
            engine.Cast("泉");

            BattleEvent? heal = null;
            foreach (var e in engine.LastEvents)
                if (e.Kind == BattleEventKind.Heal) heal = e;
            Assert.That(heal.Value.SecondIndex, Is.EqualTo(Targeting.PlayerTarget), "−1 = 玩家,与改前默认值相同");
        }
    }
}
