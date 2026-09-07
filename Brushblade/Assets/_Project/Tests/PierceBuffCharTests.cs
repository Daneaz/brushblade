using System;
using System.Collections.Generic;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.CoreTests
{
    /// <summary>PierceBuff(本场穿透叠加)机制测试。
    ///
    /// ⚠ 2026-09-07:字表重做 P2 删掉了 锐(以及随之孤儿化的部件 兑)——按 spec §6,
    /// 新表 57 字里没有任何字再挂 <see cref="EffectKind.PierceBuff"/>,详见 task-4b 报告里的
    /// 「PierceBuff 死分支」一节。原「锐 可合成且 兑 拿得到」(spec §12.2)那条验收项随之作废。
    ///
    /// 但 PierceBuff 这个**机制本身**(穿透叠加、与自带穿透相加、钳位、跨回合存活)还在引擎里,
    /// 只是暂时没有真实字挂载。下面的 fixture 测试用手写 <see cref="RecipeGraph"/>、
    /// 借占位字 <b>辛</b>(游戏里从未有过、也不打算有的字)顶替原来的 锐,继续守这份机制 ——
    /// 它不代表游戏内存在一个叫「辛」的穿透字,纯粹是测试方法名沿用了写这些用例时的历史名字
    /// (Rui_*)。生产配置侧(读真实字表断言 锐/兑 的数值与配方)已随字表删除一并移除。
    ///
    /// 测试字一律 <see cref="Element.Heart"/> 且不给配方(同 CritStatTests / DefenseWiringTests):
    /// 心对全属性生克都是 1.0x,没有配方就不会触发相生 ×3 —— 断言里看到的就是穿透本身。</summary>
    public sealed class PierceBuffCharTests
    {
        private static RecipeGraph Graph() => new(new[]
        {
            // 辛 = 占位字,顶替已删除的 锐:本场穿透 +20,可叠加(原 锐 的真实配置)
            new CharDef("辛", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.PierceBuff, 20) }),
            new CharDef("甲", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 100) }),
            // 乙 = 100 伤 + 自带穿透 10(锥 的形状):验两条穿透通道相加而不是互相覆盖
            new CharDef("乙", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 100, pierce: 10) }),
        });

        private static EnemyDef Armored(int defense, int hp = 1000, int attack = 0) =>
            new("锈", Element.Heart, hp, attack, defense: defense);

        private static BattleEngine Battle(EnemyDef[] enemies, params string[] library) =>
            new(Graph(), new BattleConfig { PlayerMaxHp = 1000, ApPerTurn = 10 },
                library, Array.Empty<string>(), enemies, seed: 1);

        // ---- 效果分支:挂对状态 ----

        [Test]
        public void Rui_GrantsTwentyPierceForTheBattle()
        {
            var engine = Battle(new[] { Armored(30) }, "辛");
            engine.Cast("辛");
            Assert.That(engine.PlayerStatuses.TotalMagnitude(StatusKind.PierceBuff), Is.EqualTo(20));
        }

        [Test]
        public void Rui_OffsetsEnemyDefenseOnLaterHits()
        {
            var engine = Battle(new[] { Armored(30) }, "辛", "甲");
            engine.Cast("辛");
            engine.Cast("甲", 0);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(1000 - 90), "100 − max(0, 30 − 20) = 90");
        }

        [Test]
        public void Rui_StacksWhenCastTwice()
        {
            // SourceId 铸唯一序号才能叠(StatusEffect.SourceId 的用法 2);误传裸字 ID
            // 会让第二张锐覆盖第一张,静默退化成「刷新」。
            var engine = Battle(new[] { Armored(40) }, "辛", "辛", "甲");
            engine.Cast("辛");
            engine.Cast("辛");
            Assert.That(engine.PlayerStatuses.TotalMagnitude(StatusKind.PierceBuff),
                Is.EqualTo(40), "两张锐叠加,不是刷新");
            engine.Cast("甲", 0);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(1000 - 100),
                "100 − max(0, 40 − 40) = 100:两张正好穿光 40 甲");
        }

        [Test]
        public void Rui_AddsOntoEffectOwnPierce_NotOverridden()
        {
            // 乙 自带穿透 10 + 锐 的 20 = 30,一起从同一个基础护甲里减。
            // 若哪天写成「取两者较大」,这条会红在 25 上。
            var engine = Battle(new[] { Armored(35) }, "辛", "乙");
            engine.Cast("辛");
            engine.Cast("乙", 0);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(1000 - 95), "100 − max(0, 35 − 30) = 95");
        }

        [Test]
        public void Rui_PiercingPastArmor_NeverAddsDamage()
        {
            // 穿过头只是归零。没有外层 max(0, …) 的话 100 − (10 − 20) = 110,白送 10 点。
            var engine = Battle(new[] { Armored(10) }, "辛", "甲");
            engine.Cast("辛");
            engine.Cast("甲", 0);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(1000 - 100), "封顶 100,不是 110");
        }

        [Test]
        public void Rui_SurvivesEndOfTurn()
        {
            // 本场持久(TurnsLeft = −1):挂上去之后不该被回合末的倒计时清掉,
            // 否则「本场穿透」退化成「本回合穿透」,而卡面写的是本场。
            var engine = Battle(new[] { Armored(30) }, "辛", "甲");
            engine.Cast("辛");
            engine.EndTurn();
            Assert.That(engine.PlayerStatuses.TotalMagnitude(StatusKind.PierceBuff), Is.EqualTo(20));
            engine.Cast("甲", 0);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(1000 - 90));
        }

        [Test]
        public void Rui_SurvivesSnapshotRoundTrip()
        {
            // PierceBuff 是 StatusBag 里的普通条目,快照本来就带它 —— 这条钉住的是
            // 「零新增快照字段」这个前提别哪天被绕开(断点续爬会把战中状态存下来)。
            var engine = Battle(new[] { Armored(30) }, "辛", "甲");
            engine.Cast("辛");
            var defs = new Dictionary<string, EnemyDef> { ["锈"] = Armored(30) };
            var restored = BattleEngine.Restore(engine.Capture(), Graph(),
                new BattleConfig { PlayerMaxHp = 1000, ApPerTurn = 10 }, null, defs);
            Assert.That(restored.PlayerStatuses.TotalMagnitude(StatusKind.PierceBuff), Is.EqualTo(20));
        }
    }
}
