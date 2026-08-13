using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.CoreTests
{
    /// <summary>玩家闪避(E-b4/E-b5 的 T4,2026-08-12)。
    ///
    /// 用户拍板的**不对称**口径:**玩家攻击永远必中,敌人没有闪避**,闪避只做成玩家侧属性
    /// (减少挨打)。于是「命中」根本不是玩家属性 —— 敌人打玩家的命中率仍是
    /// <c>100 − 攻击者致盲 − 玩家闪避</c>,而玩家打敌人**根本不摇命中**。
    ///
    /// 随机流纪律(本文件的重头):<c>_random</c> 的消费方只有三处 —— 回合掉字、
    /// <c>AttackHits</c>、<c>EnemyState</c> 构造时的 Boss 阈值浮动,T4 不得新增第四处。
    /// 闪避 0(<c>hitRate ≥ 100</c>)与闪避 100(<c>hitRate ≤ 0</c>)都走 <c>AttackHits</c>
    /// 的两端短路,一次随机都不摇 —— 下面两条 <c>RandomState</c> 前后比对就是那条硬线的防线。
    ///
    /// 夹具一律 <c>UnlockedChars = null</c>:回合掉字是 <c>_random</c> 的另一个消费方,
    /// 不关掉它,「这一步摇没摇随机数」就量不准(同 E-b2 CritStatTests 的做法)。
    /// 测试字一律 <see cref="Element.Heart"/> 且不给配方:心对全属性生克都是 1.0x,
    /// 没有配方就不会触发相生 ×3,断言里看到的数字就是伤害本身。</summary>
    public sealed class DodgeStatTests
    {
        private const int PlayerHpCeiling = 500;

        private static RecipeGraph Graph() => new(new[]
        {
            new CharDef("木", Element.Wood),
            // 甲 = 100 伤单体:玩家的输出手
            new CharDef("甲", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 100) }),
            // 昏 = 单体致盲 100%,3 回合。AttackHits 读的致盲挂在**被传进去的那个敌人下标**上,
            // 所以这个字同时是「玩家打敌人不走命中判定」的探针:玩家若误走 AttackHits,
            // 打这只满致盲的怪会必空 —— PlayerAlwaysHits 的全部判别力来自它。
            new CharDef("昏", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.Blind, 100, turns: 3) }),
            new CharDef("挡", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.Shield, 20) }),
            new CharDef("御", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.Immunity, 1) }),
        });

        private static BattleConfig Config(int dodge = 0) => new()
        {
            PlayerMaxHp = PlayerHpCeiling,
            PlayerDodge = dodge,
            DropTable = Array.Empty<string>(),
            UnlockedChars = null, // 回合掉字不摇 → RandomState 的变化只可能来自 AttackHits
        };

        private static EnemyDef Attacker(int attack = 30) => new("靶", Element.Heart, 5000, attack);

        private static BattleEngine Engine(string[] library, BattleConfig config,
            IReadOnlyList<StatusEffect> statuses = null, int seed = 1) =>
            new(Graph(), config, library, Array.Empty<string>(), new[] { Attacker() }, seed,
                startingStatuses: statuses);

        private static StatusEffect DodgeBuff(int magnitude) => new()
        {
            Kind = StatusKind.DodgeBuff, Polarity = StatusPolarity.Buff,
            Magnitude = magnitude, TurnsLeft = -1, SourceId = "测",
        };

        // ---- 不对称:玩家必中 ----

        [Test]
        public void PlayerAlwaysHits_EvenAgainstFullyBlindedEnemy()
        {
            // 裁定 3 的唯一防线:玩家出牌打敌人**永不摇命中**。
            // 判别力:目标身上挂着 100% 致盲 —— 若哪天有人给 DamageEnemy 加上
            // AttackHits(targetIndex, …),这一击的命中率就是 0,必空,本条立刻红。
            var engine = Engine(new[] { "昏", "甲" }, Config());
            engine.Cast("昏", 0);
            Assert.That(engine.Enemies[0].Statuses.TotalMagnitude(StatusKind.Blind), Is.EqualTo(100),
                "致盲得真挂上了这条才有判别力");

            int hpBefore = engine.Enemies[0].Hp;
            engine.Cast("甲", 0);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(hpBefore - 100), "满致盲的怪照样挨满伤");
            Assert.That(engine.LastEvents.Any(e => e.Kind == BattleEventKind.Missed), Is.False,
                "玩家这一击不该产生 Missed —— 命中不是玩家要摇的东西");
        }

        [Test]
        public void PlayerDodge_DoesNotLeakIntoPlayerOwnAttacks()
        {
            // 闪避是**减少挨打**的属性,不是双向的命中修正。玩家满闪避时自己的输出一分不少。
            var engine = Engine(new[] { "甲" }, Config(dodge: 100));
            int hpBefore = engine.Enemies[0].Hp;
            engine.Cast("甲", 0);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(hpBefore - 100));
        }

        [Test]
        public void PlayerAttack_ConsumesNoRandom_WhateverTheDodge()
        {
            // 「玩家那条链上零随机」的直接量法:满闪避 + 满致盲的目标,出一击,随机流纹丝不动。
            var engine = Engine(new[] { "昏", "甲" }, Config(dodge: 100));
            engine.Cast("昏", 0);
            uint before = engine.Capture().RandomState;
            engine.Cast("甲", 0);
            Assert.That(engine.Capture().RandomState, Is.EqualTo(before));
        }

        // ---- 随机流两端短路 ----

        [Test]
        public void PlayerDodge_Zero_ConsumesNoRandom()
        {
            // 闪避 0 → hitRate = 100 → AttackHits 直接返回,一次随机都不摇。
            // 这是 T4 的恒等性硬线:多摇一次就会平移掉落序列,让所有依赖种子的既有测试全红。
            var engine = Engine(Array.Empty<string>(), Config());
            Assert.That(engine.EffectiveDodge, Is.EqualTo(0));

            uint before = engine.Capture().RandomState;
            engine.EndTurn();
            Assert.That(engine.PlayerHp, Is.LessThan(PlayerHpCeiling),
                "敌人这一回合得真打到玩家,否则本条是空的");
            Assert.That(engine.Capture().RandomState, Is.EqualTo(before),
                "闪避 0 时命中判定必须短路,一次随机都不摇");
        }

        [Test]
        public void PlayerDodge_HundredPercent_EnemyAttackAlwaysMisses_AndConsumesNoRandom()
        {
            // 上端对称短路:必空时摇不摇结果都一样,不摇能让「闪避叠满」这条玩法路径
            // 同样不扰动随机流(同 RollCrit 的 ≥100 那一端)。
            var engine = Engine(Array.Empty<string>(), Config(dodge: 100));
            uint before = engine.Capture().RandomState;
            engine.EndTurn();

            Assert.That(engine.PlayerHp, Is.EqualTo(PlayerHpCeiling), "满闪避 = 一点血都不掉");
            Assert.That(engine.LastEvents.Any(e => e.Kind == BattleEventKind.Missed), Is.True,
                "打空要发 Missed 事件");
            Assert.That(engine.Capture().RandomState, Is.EqualTo(before),
                "必空也不摇 —— 两端短路");
        }

        [Test]
        public void PlayerDodge_Miss_DoesNotConsumeImmunityOrShield()
        {
            // 打空 = 什么都没发生。既有的 DamageVariantTests.Miss_DoesNotConsumeImmunityOrShield
            // 守的是致盲那条通道,这条守闪避通道 —— 免疫是稀缺资源,被一记本就打不中的
            // 攻击吃掉是最亏的。
            var engine = Engine(new[] { "御", "挡" }, Config(dodge: 100));
            engine.Cast("御", 0);
            engine.Cast("挡", 0);
            int shieldBefore = engine.PlayerShield;
            Assert.That(shieldBefore, Is.GreaterThan(0), "护盾字得真生效了这条才有判别力");

            engine.EndTurn();
            Assert.That(engine.PlayerStatuses.TotalMagnitude(StatusKind.Immunity), Is.EqualTo(1),
                "免疫层数一点没掉");
            Assert.That(engine.PlayerShield, Is.EqualTo(shieldBefore), "护盾一点没掉");
        }

        // ---- 钳位 ----

        [TestCase(0, 0, 0)]
        [TestCase(25, 0, 25)]      // 只有角色属性
        [TestCase(0, 30, 30)]      // 只有局内增益
        [TestCase(25, 40, 65)]     // 两者相加
        [TestCase(80, 50, 100)]    // 上钳:叠过头也只是必空,摇不出更多闪避
        [TestCase(10, -30, 0)]     // 下钳:负闪避不该变成「必中的反向加成」
        public void EffectiveDodge_IsConfigPlusBuff_ClampedToZeroHundred(
            int configDodge, int buff, int expected)
        {
            var statuses = buff == 0 ? null : new List<StatusEffect> { DodgeBuff(buff) };
            var engine = Engine(Array.Empty<string>(), Config(dodge: configDodge), statuses);
            Assert.That(engine.EffectiveDodge, Is.EqualTo(expected));
        }

        [Test]
        public void DodgeBuff_StacksAcrossSources()
        {
            var engine = Engine(Array.Empty<string>(), Config(),
                new List<StatusEffect>
                {
                    new() { Kind = StatusKind.DodgeBuff, Polarity = StatusPolarity.Buff,
                        Magnitude = 15, TurnsLeft = -1, SourceId = "甲" },
                    new() { Kind = StatusKind.DodgeBuff, Polarity = StatusPolarity.Buff,
                        Magnitude = 20, TurnsLeft = -1, SourceId = "乙" },
                });
            Assert.That(engine.EffectiveDodge, Is.EqualTo(35));
        }

        // ---- 零新增快照字段 ----

        [Test]
        public void DodgeBuff_RidesPlayerStatusesThroughSnapshot()
        {
            // 闪避的两个来源都不需要新快照字段:角色属性走 BattleConfig(Restore 时照原样传回),
            // 局内增益走 _playerStatuses —— 而 BattleSnapshot.PlayerStatuses 本来就存。
            var config = Config(dodge: 100);
            var engine = Engine(Array.Empty<string>(), config, new List<StatusEffect> { DodgeBuff(20) });
            var snapshot = engine.Capture();

            var revived = BattleEngine.Restore(snapshot, Graph(), config, null,
                new Dictionary<string, EnemyDef> { ["靶"] = Attacker() });
            Assert.That(revived.EffectiveDodge, Is.EqualTo(100), "上钳后仍是 100");
            Assert.That(revived.PlayerStatuses.TotalMagnitude(StatusKind.DodgeBuff), Is.EqualTo(20),
                "局内增益跟着 PlayerStatuses 走完了一整趟快照");
        }
    }
}
