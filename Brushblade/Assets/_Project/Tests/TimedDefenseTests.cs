using System;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.CoreTests
{
    /// <summary>护甲轴的**限时化**(2026-09-08 用户裁定:「所有 buff 类技能必须附带回合数,
    /// 不存在本场生效」)。护盾与免疫是这条规则的两个显式例外 —— 护盾不做时限(用户裁定),
    /// 免疫按次数算(挡完即消,本来就不是持续量)。
    ///
    /// 三条改动一处验:
    /// 1. <see cref="EffectKind.DefenseBuff"/> 从 <c>TurnsLeft = -1</c> 改成读 <c>effect.Turns</c>;
    /// 2. <see cref="EffectKind.ArmorBreak"/> 同上 —— 它是护甲轴的负半边,只改一边会不对称
    ///    (第 10 章 :56「破甲永久降护甲」随之作废);**可叠加**的语义不变,唯一 SourceId 照旧,
    ///    所以两张破甲字各自计时,先挂的先到期;
    /// 3. 新增 <see cref="EffectDef.SummonDefense"/>:召唤物**入场自带**护甲,与既有的
    ///    <c>SummonShield</c> 并列。它挂进那只召唤物自己的状态袋,由现成的
    ///    <c>SummonState.EffectiveDefense</c> 读走 —— 不新建减伤路径。
    ///    这一条**不限时**(TurnsLeft = -1):它是这只单位的属性,单位死了就没了,
    ///    不是场上飘着的玩家 buff,「buff 必须限时」约束的是后者。
    ///
    /// 测试字一律 <see cref="Element.Heart"/> 且不给配方(同 DefenseWiringTests):
    /// 心对全属性生克 1.0x、无配方不触发相生,断言里的数字就是减法本身。</summary>
    public sealed class TimedDefenseTests
    {
        private static RecipeGraph Graph() => new(new[]
        {
            // 碎 = 40 伤 + 破甲 10 / 3 回合
            new CharDef("碎", Element.Heart, effects: new[]
            {
                new EffectDef(EffectKind.DamageSingle, 40),
                new EffectDef(EffectKind.ArmorBreak, 10, turns: 3),
            }),
            // 锤 = 90 伤 + 破甲 20 / 1 回合(用来验两张破甲各自计时)
            new CharDef("锤", Element.Heart, effects: new[]
            {
                new EffectDef(EffectKind.DamageSingle, 90),
                new EffectDef(EffectKind.ArmorBreak, 20, turns: 1),
            }),
            // 垒 = 护甲 +5 / 4 回合
            new CharDef("垒", Element.Heart, effects: new[]
            {
                new EffectDef(EffectKind.DefenseBuff, 5, turns: 4),
            }),
            // 塔 = 召唤 1 只 200 血 / 0 攻,入场自带护甲 7
            new CharDef("塔", Element.Heart, effects: new[]
            {
                new EffectDef(EffectKind.Summon, 200, summonCount: 1, summonAttack: 0,
                    summonChar: "塔", summonDefense: 7),
            }),
        });

        private static BattleEngine Battle(int enemyDefense = 0, int enemyAttack = 0,
            params string[] library) =>
            new(Graph(), new BattleConfig { PlayerMaxHp = 1000 },
                library, Array.Empty<string>(),
                new[] { new EnemyDef("桩", Element.Heart, 1000, enemyAttack, defense: enemyDefense) },
                seed: 42);

        // ---- 破甲限时 ----

        [Test]
        public void ArmorBreak_ExpiresAfterConfiguredTurns()
        {
            var engine = Battle(enemyDefense: 30, library: new[] { "碎" });
            engine.Cast("碎", 0);
            Assert.That(engine.Enemies[0].Statuses.TotalMagnitude(StatusKind.ArmorBreak),
                Is.EqualTo(10), "刚挂上");

            for (int i = 0; i < 3; i++) engine.EndTurn();
            Assert.That(engine.Enemies[0].Statuses.TotalMagnitude(StatusKind.ArmorBreak),
                Is.EqualTo(0), "3 回合后到期,护甲回到 30");
        }

        [Test]
        public void ArmorBreak_TwoCharsKeepSeparateTimers()
        {
            // 可叠加不变(唯一 SourceId),但各自计时:锤 1 回合先掉,碎 3 回合还在。
            var engine = Battle(enemyDefense: 50, library: new[] { "碎", "锤" });
            engine.Cast("碎", 0);
            engine.Cast("锤", 0);
            Assert.That(engine.Enemies[0].Statuses.TotalMagnitude(StatusKind.ArmorBreak),
                Is.EqualTo(30), "10 + 20,叠加照旧");

            engine.EndTurn();
            Assert.That(engine.Enemies[0].Statuses.TotalMagnitude(StatusKind.ArmorBreak),
                Is.EqualTo(10), "锤 的 1 回合到期,碎 的还剩 2 回合");
        }

        // ---- 护甲限时 ----

        [Test]
        public void DefenseBuff_ExpiresAfterConfiguredTurns()
        {
            var engine = Battle(enemyAttack: 0, library: new[] { "垒" });
            engine.Cast("垒", -1, allySlot: Targeting.PlayerTarget);
            Assert.That(engine.EffectivePlayerDefense, Is.EqualTo(5), "刚挂上");

            for (int i = 0; i < 4; i++) engine.EndTurn();
            Assert.That(engine.EffectivePlayerDefense, Is.EqualTo(0), "4 回合后到期");
        }

        // ---- 召唤物入场自带护甲 ----

        [Test]
        public void SummonDefense_AppliesToNewbornSummon()
        {
            var engine = Battle(library: new[] { "塔" });
            engine.Cast("塔", -1);
            Assert.That(engine.Summons[0], Is.Not.Null, "召唤物落位了");
            Assert.That(engine.Summons[0].EffectiveDefense, Is.EqualTo(7),
                "入场即带护甲 7,由现成的 EffectiveDefense 读走");
        }

        [Test]
        public void SummonDefense_PersistsWithTheUnit()
        {
            // 不限时:它是单位属性,不是场上飘着的玩家 buff。
            var engine = Battle(library: new[] { "塔" });
            engine.Cast("塔", -1);
            for (int i = 0; i < 6; i++) engine.EndTurn();
            Assert.That(engine.Summons[0].EffectiveDefense, Is.EqualTo(7),
                "过 6 个回合仍在(TurnsLeft = -1)");
        }
    }
}
