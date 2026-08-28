using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Brushblade.Core;
using Brushblade.Data;
using NUnit.Framework;

namespace Brushblade.CoreTests
{
    /// <summary>锐 与新部件 兑(E-b4/E-b5 的 T5,2026-08-12)。
    ///
    /// 锐 = <c>PierceBuff 20</c>:本场穿透 +20 点,可叠加、本场持久。
    /// 减法本身由 DefenseWiringTests 守(<c>EffectiveEnemyDefense</c> 的合并相减与钳位),
    /// 本文件守的是**这个字**:效果分支挂对了状态、能叠不是刷新、生产配置的数字与配方,
    /// 以及 <b>兑 拿得到</b> —— 那是 spec §12.2 明写的验收项,不是可选检查。
    ///
    /// 测试字一律 <see cref="Element.Heart"/> 且不给配方(同 CritStatTests / DefenseWiringTests):
    /// 心对全属性生克都是 1.0x,没有配方就不会触发相生 ×3 —— 断言里看到的就是穿透本身。</summary>
    public sealed class PierceBuffCharTests
    {
        private static RecipeGraph Graph() => new(new[]
        {
            // 锐 的真实配置(《技能机制详表》金系 BUFF 表):本场穿透 +20,可叠加
            new CharDef("锐", Element.Heart,
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
            var engine = Battle(new[] { Armored(30) }, "锐");
            engine.Cast("锐");
            Assert.That(engine.PlayerStatuses.TotalMagnitude(StatusKind.PierceBuff), Is.EqualTo(20));
        }

        [Test]
        public void Rui_OffsetsEnemyDefenseOnLaterHits()
        {
            var engine = Battle(new[] { Armored(30) }, "锐", "甲");
            engine.Cast("锐");
            engine.Cast("甲", 0);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(1000 - 90), "100 − max(0, 30 − 20) = 90");
        }

        [Test]
        public void Rui_StacksWhenCastTwice()
        {
            // SourceId 铸唯一序号才能叠(StatusEffect.SourceId 的用法 2);误传裸字 ID
            // 会让第二张锐覆盖第一张,静默退化成「刷新」。
            var engine = Battle(new[] { Armored(40) }, "锐", "锐", "甲");
            engine.Cast("锐");
            engine.Cast("锐");
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
            var engine = Battle(new[] { Armored(35) }, "锐", "乙");
            engine.Cast("锐");
            engine.Cast("乙", 0);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(1000 - 95), "100 − max(0, 35 − 30) = 95");
        }

        [Test]
        public void Rui_PiercingPastArmor_NeverAddsDamage()
        {
            // 穿过头只是归零。没有外层 max(0, …) 的话 100 − (10 − 20) = 110,白送 10 点。
            var engine = Battle(new[] { Armored(10) }, "锐", "甲");
            engine.Cast("锐");
            engine.Cast("甲", 0);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(1000 - 100), "封顶 100,不是 110");
        }

        [Test]
        public void Rui_SurvivesEndOfTurn()
        {
            // 本场持久(TurnsLeft = −1):挂上去之后不该被回合末的倒计时清掉,
            // 否则「本场穿透」退化成「本回合穿透」,而卡面写的是本场。
            var engine = Battle(new[] { Armored(30) }, "锐", "甲");
            engine.Cast("锐");
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
            var engine = Battle(new[] { Armored(30) }, "锐", "甲");
            engine.Cast("锐");
            var defs = new Dictionary<string, EnemyDef> { ["锈"] = Armored(30) };
            var restored = BattleEngine.Restore(engine.Capture(), Graph(),
                new BattleConfig { PlayerMaxHp = 1000, ApPerTurn = 10 }, null, defs);
            Assert.That(restored.PlayerStatuses.TotalMagnitude(StatusKind.PierceBuff), Is.EqualTo(20));
        }

        // ---- 生产配置 ----

        private static RecipeGraph RealGraph()
        {
            // ⚠ 锚点必须是 TestContext.CurrentContext.TestDirectory,**不能用 AppContext.BaseDirectory**
            // (2026-08-15 修):后者在 Unity Test Runner 下指向**编辑器安装目录**
            // (/Applications/Unity/Hub/Editor/.../Unity.app/Contents),从那儿往上永远找不到
            // 含 Brushblade/ 的父目录 → 本断言直接失败。dotnet 工装下两者都指向 bin/,
            // 所以这是一条典型的「工装绿 ≠ 编辑器绿」——本文件 15 条、PierceBuffCharTests
            // 4 条曾因此在 Test Runner 里全红,而工装一直是绿的。
            var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Brushblade")))
                dir = dir.Parent;
            Assert.That(dir, Is.Not.Null, "找不到仓库根目录");
            return ConfigLoader.LoadGraph(File.ReadAllText(Path.Combine(
                dir.FullName, "Brushblade", "Assets", "StreamingAssets", "config", "chars.json")));
        }

        [Test]
        public void RealConfig_Rui_IsGreenMetalPierceBuffTwenty()
        {
            // 2026-08-25 字表重构:锐 随低档按词组数重排从白升绿(锋锐/锐利 两条词)。
            var def = RealGraph().Get("锐");
            Assert.That(def.Rarity, Is.EqualTo(CardRarity.Green));
            Assert.That(def.Element, Is.EqualTo(Element.Metal));
            // 2026-08-29 用户拍板:锐 与其余六张 buff 字一起去掉伤害、回归**纯 buff**。
            // (2026-08-14 T9 曾给它补过单攻 40,那次决定已撤销 —— 增益现在可以单体指定、
            //  也能加给召唤物,带一发伤害会逼玩家先选敌人再选友方,拖拽更没法直接拖到友方。)
            // 按 Kind 取仍然保留:将来若再挂别的增益,Single() 会当场炸。
            var effect = def.Effects.Single(e => e.Kind == EffectKind.PierceBuff);
            Assert.That(def.Effects.Any(e => e.Kind == EffectKind.DamageSingle), Is.False,
                "纯 buff 字不带伤害 —— 带了就又要先选敌人");
            // 20 的定位(spec §12.1):一张正好穿光墨渍的 20、抵江/钧阶段 30 的 2/3。
            // 2026-08-25:錰 移出字表,「两张叠满 40 配 錰 的本体穿透 30 穿山阶段 60」那条线断了 ——
            // 现在最高的本体穿透是 刺 的 15,叠两张 锐 也只到 55,穿不透山阶段。这是已知缺口。
            Assert.That(effect.Value, Is.EqualTo(20));
        }

        [Test]
        public void RealConfig_Rui_RecipeIsJinAndDui()
        {
            var graph = RealGraph();
            Assert.That(graph.Get("锐").Recipe, Is.EqualTo(new[] { "钅", "兑" }));
            // 两个原料都得是叶子(部件),否则「拆锐得兑」这条链下面那半截不成立
            Assert.That(graph.Get("钅").IsLeaf, Is.True);
            Assert.That(graph.Get("兑").IsLeaf, Is.True);
            Assert.That(graph.Get("兑").Element, Is.EqualTo(Element.Metal));
        }

        [Test]
        public void RealConfig_Dui_IsReachable_ThroughRuiInTheDeck()
        {
            // spec §12.2 的验收项:锐 可合成 **且** 兑 拿得到。
            //
            // 部件不进奖励池(RunEngine.RollRewardOptions 对 IsLeaf 直接 continue),
            // 唯一来源是拆字,而可拆的候选派生自出阵表(MetaRules.DeckComponents,
            // GameRoot 把 runConfig.RewardPool 接的就是 meta.Deck)。
            // 于是 兑 的获取链是:锐 从宝箱进字库(ChestCardPool = 全部非叶子字)
            // → 上出阵表 → 兑 成为可掉部件 → 局内合出更多 锐。
            // 不是死循环:进入这条链的第一张 锐 来自宝箱,不需要先合。
            var graph = RealGraph();
            Assert.That(graph.Get("锐").IsLeaf, Is.False, "锐 必须有配方,否则宝箱/奖励池都发不出它");
            var components = MetaRules.DeckComponents(new[] { "锐" }, graph).ToList();
            Assert.That(components, Contains.Item("兑"));
            Assert.That(components, Contains.Item("钅"));
        }

        [Test]
        public void RealConfig_Dui_IsUsedOnlyByRui()
        {
            // 若哪天别的字也用上 兑,上面那条链就多了一个入口 —— 不是坏事,但这条会红,
            // 提醒把注释里的「唯一来源」改掉。反过来若 兑 一个字都不用了,它就成了死条目。
            var users = RealGraph().All.Where(d => d.Recipe.Contains("兑")).Select(d => d.Id).ToList();
            Assert.That(users, Is.EqualTo(new[] { "锐" }));
        }
    }
}
