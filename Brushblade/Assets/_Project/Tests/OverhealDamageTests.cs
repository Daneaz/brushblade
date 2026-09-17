using System;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>水脉 L2「溢流」(spec 2026-09-13 §2.1):治疗溢出的部分 ×N% 打一名随机敌人。
    ///
    /// 两条口径这里各有一条守卫:
    /// ① 群治时玩家与每只召唤物**各算各的**溢出、各打一下(不是求和后打一下);
    /// ② 召唤物**光环**治疗不触发,而召唤物**自愈**触发 —— 用户 2026-09-13 裁定的边界
    ///    就落在这一条线上,两边都要断。</summary>
    public class OverhealDamageTests
    {
        private const int Pct = 50;

        private static RecipeGraph Graph() => new(new[]
        {
            // 治己 200:玩家满血时全额溢出
            new CharDef("治", Element.Water,
                effects: new[] { new EffectDef(EffectKind.HealSelf, 200) }),
            // 群治 200:玩家 + 全部召唤物各得一份
            new CharDef("沽", Element.Water,
                effects: new[] { new EffectDef(EffectKind.HealAll, 200) }),
            // 召唤一只满血的木桩,带光环治疗 300(每拍治全场)
            new CharDef("桃", Element.Wood,
                effects: new[]
                {
                    new EffectDef(EffectKind.Summon, 100, summonAttack: 0, summonChar: "木",
                        passive: new SummonPassive { HealAlly = 300 }),
                }),
            // 召唤一只满血的木桩,带自愈 300
            new CharDef("藻", Element.Wood,
                effects: new[]
                {
                    new EffectDef(EffectKind.Summon, 100, summonAttack: 0, summonChar: "木",
                        passive: new SummonPassive { Regen = 300 }),
                }),
            // 召唤一只满血、带自愈 300 且会出手的(攻 10)—— 「林」那种:自愈溢流之后还要挥刀
            new CharDef("森", Element.Wood,
                effects: new[]
                {
                    new EffectDef(EffectKind.Summon, 100, summonAttack: 10, summonChar: "木",
                        passive: new SummonPassive { Regen = 300 }),
                }),
        });

        /// <summary>玩家满血起手 —— 治疗全部溢出。敌人血厚,不会被溢流打死而干扰后续断言。</summary>
        private static BattleEngine Engine(int overhealPercent, int enemyCount = 1) =>
            new(Graph(), new BattleConfig
                {
                    DropTable = new[] { "水" }, PlayerMaxHp = 500, ApPerTurn = 20,
                    OverhealDamagePercent = overhealPercent,
                },
                new[] { "治", "沽", "桃", "藻", "森" }, Array.Empty<string>(),
                Enumerable.Range(0, enemyCount)
                    .Select(i => new EnemyDef($"靶{i}", Element.Heart, 9000, 0))
                    .ToArray(),
                seed: 1);

        private static int TotalEnemyHpLost(BattleEngine engine, int perEnemyMaxHp = 9000) =>
            engine.Enemies.Sum(e => perEnemyMaxHp - e.Hp);

        [Test]
        public void FullHpSelfHeal_TurnsTheWholeOverflowIntoDamage()
        {
            var engine = Engine(Pct);
            Assert.That(engine.PlayerHp, Is.EqualTo(500), "起手满血,这条测试的前提");
            Assert.That(engine.Cast("治"), Is.EqualTo(BattleError.None));
            Assert.That(engine.PlayerHp, Is.EqualTo(500), "满血,一点没回");
            Assert.That(TotalEnemyHpLost(engine), Is.EqualTo(200 * Pct / 100),
                "溢出 200 × 50% = 100");
        }

        [Test]
        public void OverhealDamageEvent_IsTaggedWithItsSource()
        {
            // 2026-09-18:溢流的伤害是心属性紫字,不标来源玩家读不出这一记打哪来(用户实机报「多了一个紫色 -30」)
            var engine = Engine(Pct);
            engine.Cast("治");
            var hits = engine.LastEvents.Where(e => e.Kind == BattleEventKind.Damage).ToList();
            Assert.That(hits.Count, Is.EqualTo(1));
            Assert.That(hits[0].Source, Is.EqualTo(EffectSource.Overheal));
            Assert.That(hits[0].Amount, Is.EqualTo(100));
        }

        [Test]
        public void PerkOff_DealsNoDamageAtAll()
        {
            var engine = Engine(0);
            Assert.That(engine.Cast("治"), Is.EqualTo(BattleError.None));
            Assert.That(TotalEnemyHpLost(engine), Is.EqualTo(0), "未点亮,溢出照旧浪费掉");
        }

        [Test]
        public void PerkOff_DoesNotConsumeRandomness()
        {
            // 恒等性硬线:关掉时一次随机都不许摇。RandomState 是 GameRandom 的内部游标,
            // 摇一次就变 —— 直接断它,比数掉字精确得多(出牌本身也会改库存,数掉字断不住)。
            // enemyCount 用 2 与 PerkOn_ConsumesExactlyOneDrawToPickTheTarget 同理:
            // Next(1) 会短路,即便有人意外多摇一次,RandomState 也不变;2 个敌人时
            // PickRandomLivingEnemy 的 Next(2) 才能真实证明「即便开关关闭也决不摇」。
            var engine = Engine(0, enemyCount: 2);
            uint before = engine.Capture().RandomState;
            Assert.That(engine.Cast("治"), Is.EqualTo(BattleError.None));
            Assert.That(engine.Capture().RandomState, Is.EqualTo(before),
                "未点亮时出治疗字不该消耗随机数");
        }

        [Test]
        public void PerkOn_ConsumesExactlyOneDrawToPickTheTarget()
        {
            // enemyCount 用 2:GameRandom.Next(maxExclusive) 在 maxExclusive ≤ 1 时恒定
            // 短路返回 0、不碰内部状态(GameRandom.cs 的既有约定,GameRandomTests 有专门
            // 守卫的性质)。只剩 1 个存活敌人时 PickRandomLivingEnemy 内部的 Next(1) 不会
            // 推进随机流,断言会假阴性 —— 这里换成 2 个敌人,让 Next(2) 走真正的采样路径。
            var engine = Engine(Pct, enemyCount: 2);
            uint before = engine.Capture().RandomState;
            Assert.That(engine.Cast("治"), Is.EqualTo(BattleError.None));
            Assert.That(engine.Capture().RandomState, Is.Not.EqualTo(before),
                "点亮后要摇一次选目标");
        }

        [Test]
        public void PartialOverflow_OnlyTheOverflowingPartCounts()
        {
            // 玩家掉 50 血 → 治疗 200 里 50 回血、150 溢出
            var engine = Engine(Pct);
            engine.DamagePlayerForTest(50);
            Assert.That(engine.PlayerHp, Is.EqualTo(450));
            Assert.That(engine.Cast("治"), Is.EqualTo(BattleError.None));
            Assert.That(engine.PlayerHp, Is.EqualTo(500), "先回满");
            Assert.That(TotalEnemyHpLost(engine), Is.EqualTo(150 * Pct / 100),
                "只有溢出的 150 折成伤害");
        }

        [Test]
        public void NoOverflow_DealsNoDamage()
        {
            var engine = Engine(Pct);
            engine.DamagePlayerForTest(400);   // 缺 400,治 200 全部吃进去
            Assert.That(engine.Cast("治"), Is.EqualTo(BattleError.None));
            Assert.That(engine.PlayerHp, Is.EqualTo(300));
            Assert.That(TotalEnemyHpLost(engine), Is.EqualTo(0), "一点没溢出");
        }

        [Test]
        public void GroupHeal_EachOverflowingUnitFiresItsOwnHit()
        {
            // 满血玩家 + 一只满血召唤物 → 两份溢出,各 200,各打一下 = 总计 200
            var engine = Engine(Pct);
            Assert.That(engine.Cast("藻"), Is.EqualTo(BattleError.None));
            var summon = engine.Summons.FirstOrDefault(s => s != null);
            Assert.That(summon, Is.Not.Null);
            Assert.That(summon.Hp, Is.EqualTo(summon.MaxHp), "召唤物上场满血,这条测试的前提");
            int before = TotalEnemyHpLost(engine);
            Assert.That(engine.Cast("沽"), Is.EqualTo(BattleError.None));
            Assert.That(TotalEnemyHpLost(engine) - before, Is.EqualTo(2 * 200 * Pct / 100),
                "玩家与召唤物各溢出 200,各打一下,不是求和后打一下");
        }

        [Test]
        public void SummonAuraHeal_DoesNotTriggerOverflowDamage()
        {
            var engine = Engine(Pct);
            Assert.That(engine.Cast("桃"), Is.EqualTo(BattleError.None));
            int before = TotalEnemyHpLost(engine);
            engine.EndTurn();   // 召唤物那一拍会走光环治疗
            Assert.That(TotalEnemyHpLost(engine), Is.EqualTo(before),
                "光环治疗是被动、不是玩家主动投入,刻意排除在溢流之外");
        }

        [Test]
        public void SummonSelfRegen_DoesTriggerOverflowDamage()
        {
            var engine = Engine(Pct);
            Assert.That(engine.Cast("藻"), Is.EqualTo(BattleError.None));
            int before = TotalEnemyHpLost(engine);
            engine.EndTurn();   // 召唤物那一拍会走自愈
            // 藻 召出时已满血(MaxHp 100),自愈 300 全额溢出 → 300 × 50% = 150,可以算死,
            // 不必只断「变多了」
            Assert.That(TotalEnemyHpLost(engine) - before, Is.EqualTo(150),
                "自愈算治疗——用户裁定的边界只排除光环那一条;300 溢出 × 50% = 150");
        }

        // ---- 事件次序与动效所需的字段(2026-09-18):表现层按「回血 → 溢流打人 → 召唤物出手」三拍演 ----

        private static int IndexOf(BattleEngine engine, Func<BattleEvent, bool> match)
        {
            for (int i = 0; i < engine.LastEvents.Count; i++)
                if (match(engine.LastEvents[i])) return i;
            return -1;
        }

        [Test]
        public void SummonRegen_FullHp_HealEventThenOverhealHitThenAttack()
        {
            var engine = Engine(Pct);
            Assert.That(engine.Cast("森"), Is.EqualTo(BattleError.None));
            int slot = Array.FindIndex(engine.Summons.ToArray(), s => s != null);
            engine.EndTurn();

            int heal = IndexOf(engine, e => e.Kind == BattleEventKind.Heal && e.SecondIndex == slot);
            int hit = IndexOf(engine, e => e.Kind == BattleEventKind.Damage && e.Source == EffectSource.Overheal);
            int attack = IndexOf(engine, e => e.Kind == BattleEventKind.SummonAttack && e.SecondIndex == slot);
            Assert.That(heal, Is.GreaterThanOrEqualTo(0), "满血自愈也要发治疗事件,表现层靠它播回血动效");
            Assert.That(engine.LastEvents[heal].Amount, Is.EqualTo(0), "满血,实际回血 0");
            Assert.That(engine.LastEvents[heal].Overflow, Is.EqualTo(300), "溢出 300 全部折成了伤害");
            Assert.That(hit, Is.GreaterThan(heal), "先回血、再溢流打人");
            Assert.That(engine.LastEvents[hit].SecondIndex, Is.EqualTo(slot), "溢流伤害带上溢出者的槽位,飞水从它身上出发");
            Assert.That(attack, Is.GreaterThan(hit), "溢流之后才轮到召唤物挥刀");
        }

        [Test]
        public void SelfHeal_FullHp_HealEventCarriesOverflow_HitComesFromPlayer()
        {
            var engine = Engine(Pct);
            engine.Cast("治");
            int heal = IndexOf(engine, e => e.Kind == BattleEventKind.Heal);
            int hit = IndexOf(engine, e => e.Kind == BattleEventKind.Damage);
            Assert.That(engine.LastEvents[heal].Overflow, Is.EqualTo(200));
            Assert.That(hit, Is.GreaterThan(heal));
            Assert.That(engine.LastEvents[hit].SecondIndex, Is.EqualTo(Targeting.PlayerTarget), "−1 = 玩家溢出");
        }

        [Test]
        public void GroupHeal_OverflowingSummon_GetsItsOwnHealEventBeforeItsHit()
        {
            // 群治此前只给玩家发一条治疗事件,召唤物默默回血 —— 满血召唤物溢流打人时
            // 画面上就只有一发凭空冒出来的伤害。溢流触发时给它补一条。
            var engine = Engine(Pct);
            engine.Cast("藻");
            int slot = Array.FindIndex(engine.Summons.ToArray(), s => s != null);
            engine.Cast("沽");
            int heal = IndexOf(engine, e => e.Kind == BattleEventKind.Heal && e.SecondIndex == slot);
            int hit = IndexOf(engine, e => e.Kind == BattleEventKind.Damage && e.SecondIndex == slot);
            Assert.That(heal, Is.GreaterThanOrEqualTo(0));
            Assert.That(engine.LastEvents[heal].Overflow, Is.EqualTo(200));
            Assert.That(hit, Is.GreaterThan(heal));
        }

        [Test]
        public void PerkOff_HealEventsCarryNoOverflow_GroupHealAddsNoSummonEvents()
        {
            // 未点亮时事件流与改前逐条相同:溢出量不填,群治也不多发召唤物那条
            var engine = Engine(0);
            engine.Cast("藻");
            engine.Cast("沽");
            var heals = engine.LastEvents.Where(e => e.Kind == BattleEventKind.Heal).ToList();
            Assert.That(heals.Count, Is.EqualTo(1));
            Assert.That(heals[0].Overflow, Is.EqualTo(0));
        }
    }
}
