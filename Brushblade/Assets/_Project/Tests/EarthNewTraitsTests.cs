using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using Brushblade.Data;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>护盾回合保留与木灵替换 Task 2(spec 2026-10-13 §2):三条土系 Lv8 固面新特性的机制。
    /// <list type="bullet">
    /// <item>积土成山(垒):回合脉冲放行 <see cref="EffectKind.Shield"/>(已有)—— 之后 N 个玩家回合开始各给一次护盾;</item>
    /// <item>七级浮屠(塔):<c>DefenseBuff</c> 的 <see cref="EffectDef.PerShieldTenth"/> —— 层数 = ⌊玩家当前护盾 × 10 / 最大生命⌋
    /// 钳 0..ScaleCap,在本次出字结算完后取;0 层不挂;</item>
    /// <item>厚德载物(㙓):回合脉冲放行 <see cref="EffectKind.ShieldSummons"/>,<see cref="EffectDef.OfPlayerShield"/> =
    /// 每只存活木灵获得「兑现时玩家当前护盾 × Value%」;<c>turns -1</c> = 本场持续。</item>
    /// </list>
    /// 夹具:Element.Heart、PlayerAttack = 100(攻击力缩放恒等)、PlayerMaxHp 5000(护盾上限 5000)、暴击率 0、敌人攻击 0。
    /// 断言只看「回合开始时发了哪些护盾事件 / 挂了什么状态」,不依赖回合初是否清盾(Task 4 会删清盾)。</summary>
    public class EarthNewTraitsTests
    {
        private const int MaxHp = 5000;

        private static BattleConfig Config => new BattleConfig
            { PlayerMaxHp = MaxHp, PlayerAttack = 100, ApPerTurn = 60, LibraryCapacity = 60 };

        private static BattleEngine Battle(CharDef[] defs, int shield = 0, int level = 1,
            IReadOnlyList<SummonSnapshot> summons = null) =>
            new(RebalanceFixture.Graph(defs), Config,
                defs.SelectMany(d => Enumerable.Repeat(d.Id, 10)).ToArray(), Array.Empty<string>(),
                new[] { RebalanceFixture.Mob() }, seed: 1,
                cardLevels: defs.ToDictionary(d => d.Id, _ => level),
                startingNormalShield: shield, startingSummons: summons);

        private static void Cast(BattleEngine b, string id) =>
            Assert.That(b.Cast(id, -1), Is.EqualTo(BattleError.None), id);

        private static SummonSnapshot Spirit(int slot) => new SummonSnapshot
        {
            Slot = slot, Char = "木", SourceChar = "林", Element = Element.Wood, Hp = 300, MaxHp = 300,
            Attack = 10, Speed = 100, Statuses = new List<StatusEffect>(),
        };

        private static List<int> PlayerShieldEvents(BattleEngine b) => b.LastEvents
            .Where(e => e.Kind == BattleEventKind.Shield && e.TargetIndex == Targeting.PlayerTarget).Select(e => e.Amount).ToList();

        private static List<(int Slot, int Amount)> SummonShieldEvents(BattleEngine b) => b.LastEvents
            .Where(e => e.Kind == BattleEventKind.Shield && e.TargetIndex >= 0).Select(e => (e.TargetIndex, e.Amount)).ToList();

        // ================= 积土成山:回合脉冲载荷 Shield =================

        private static CharDef Mound(int shield, int pulseShield, int turns) => RebalanceFixture.Char("垒",
            new EffectDef(EffectKind.Shield, shield),
            new EffectDef(EffectKind.TurnPulse, 0, turns: turns, perHit: new[] { new EffectDef(EffectKind.Shield, pulseShield) }));

        [Test]
        public void JiTu_PulseGrantsShieldAtNextThreeTurnStarts_ThenStops()
        {
            var b = Battle(new[] { Mound(100, 16, 3) });
            Cast(b, "垒");
            Assert.That(PlayerShieldEvents(b), Is.EqualTo(new[] { 100 }), "施加当回合不触发脉冲");
            for (int round = 1; round <= 3; round++)
            {
                b.EndTurn();
                Assert.That(PlayerShieldEvents(b), Is.EqualTo(new[] { 16 }), $"第 {round} 个回合开始:护盾 16");
            }
            Assert.That(b.PlayerStatuses.Has(StatusKind.TurnPulse), Is.False);
            b.EndTurn();
            Assert.That(PlayerShieldEvents(b), Is.Empty, "第 4 回合不再给");
        }

        // ================= 七级浮屠:DefenseBuff perShieldTenth =================

        private static EffectDef Pagoda(int perLayer = 8, int cap = 7) =>
            new(EffectKind.DefenseBuff, perLayer, turns: 4, perShieldTenth: true, scaleCap: cap);

        [TestCase(0, 0)]
        [TestCase(499, 0)]        // 不足 10%:0 层不挂
        [TestCase(500, 1)]        // 恰 10%
        [TestCase(999, 1)]
        [TestCase(1000, 2)]
        [TestCase(3500, 7)]       // 恰 70%
        [TestCase(3999, 7)]
        [TestCase(5000, 7)]       // 超 70% 钳 7
        public void QiJi_LayersFromShieldTenths_ClampedToCap(int shield, int layers)
        {
            var b = Battle(new[] { RebalanceFixture.Char("塔", Pagoda()) }, shield: shield);
            Assert.That(b.PlayerShield, Is.EqualTo(shield), "前提:开局护盾如夹具所设");
            Cast(b, "塔");
            var buffs = b.PlayerStatuses.All.Where(s => s.Kind == StatusKind.DefenseBuff).ToList();
            if (layers == 0)
            {
                Assert.That(buffs, Is.Empty, "0 层不挂");
                return;
            }
            Assert.That(buffs.Count, Is.EqualTo(1));
            Assert.That((buffs[0].Magnitude, buffs[0].TurnsLeft), Is.EqualTo((8 * layers, 4)));
        }

        /// <summary>层数在本次出字**结算完后**取:写在前面的护甲也算上同面后面那条护盾。</summary>
        [Test]
        public void QiJi_ReadsShieldAfterWholeCast()
        {
            var def = RebalanceFixture.Char("塔", Pagoda(), new EffectDef(EffectKind.Shield, 1000));
            var b = Battle(new[] { def });
            Cast(b, "塔");
            Assert.That(b.PlayerShield, Is.EqualTo(1000));
            Assert.That(b.PlayerStatuses.Find(StatusKind.DefenseBuff)?.Magnitude, Is.EqualTo(16), "1000 / 5000 = 2 层 × 8");
        }

        /// <summary>每层护甲量按 DefenseBuff 现口径吃卡等级,层数再乘上去。</summary>
        [Test]
        public void QiJi_PerLayerScalesWithCardLevel()
        {
            var b = Battle(new[] { RebalanceFixture.Char("塔", Pagoda()) }, shield: 1500, level: 8);
            Cast(b, "塔");
            Assert.That(b.PlayerStatuses.Find(StatusKind.DefenseBuff)?.Magnitude,
                Is.EqualTo(MetaRules.ScaleEffectValue(EffectKind.DefenseBuff, 8, 8) * 3));
        }

        /// <summary>cap 0 = 不设上限(层数只受护盾上限 = 最大生命约束,最多 10)。</summary>
        [Test]
        public void QiJi_CapZero_Uncapped()
        {
            var b = Battle(new[] { RebalanceFixture.Char("塔", Pagoda(cap: 0)) }, shield: 5000);
            Cast(b, "塔");
            Assert.That(b.PlayerStatuses.Find(StatusKind.DefenseBuff)?.Magnitude, Is.EqualTo(80));
        }

        /// <summary>没有 perShieldTenth 的护甲照旧:当场按 Value 施加。</summary>
        [Test]
        public void PlainDefenseBuff_Unchanged()
        {
            var b = Battle(new[] { RebalanceFixture.Char("塔", new EffectDef(EffectKind.DefenseBuff, 8, turns: 4)) });
            Cast(b, "塔");
            Assert.That(b.PlayerStatuses.Find(StatusKind.DefenseBuff)?.Magnitude, Is.EqualTo(8));
        }

        // ================= 厚德载物:ShieldSummons ofPlayerShield / 回合脉冲放行 =================

        [Test]
        public void TurnPulseAllows_ShieldSummons()
        {
            Assert.That(BattleEngine.TurnPulseAllows(EffectKind.ShieldSummons), Is.True);
        }

        [Test]
        public void OfPlayerShield_EachAliveSpiritGetsPercentOfCurrentPlayerShield()
        {
            var def = RebalanceFixture.Char("㙓", new EffectDef(EffectKind.Shield, 500),
                new EffectDef(EffectKind.ShieldSummons, 10, ofPlayerShield: true));
            var b = Battle(new[] { def }, shield: 300, summons: new[] { Spirit(0), Spirit(2) });
            Cast(b, "㙓");
            Assert.That(SummonShieldEvents(b), Is.EqualTo(new[] { (0, 80), (2, 80) }), "(300 + 500) × 10%");
            Assert.That(b.Summons[0].Shield, Is.EqualTo(80));
            var bare = Battle(new[] { RebalanceFixture.Char("㙓", new EffectDef(EffectKind.Shield, 500)) }, shield: 300);
            Cast(bare, "㙓");
            Assert.That((b.HeftStacks, b.ShieldAccum), Is.EqualTo((bare.HeftStacks, bare.ShieldAccum)),
                "按玩家护盾取值的群盾不额外攒厚(同 ofShield)");
        }

        /// <summary>百分比不吃卡等级(同 ofShield)。</summary>
        [Test]
        public void OfPlayerShield_PercentNotScaledByLevel()
        {
            var def = RebalanceFixture.Char("㙓", new EffectDef(EffectKind.ShieldSummons, 10, ofPlayerShield: true));
            var b = Battle(new[] { def }, shield: 1000, level: 8, summons: new[] { Spirit(1) });
            Cast(b, "㙓");
            Assert.That(SummonShieldEvents(b), Is.EqualTo(new[] { (1, 100) }));
        }

        private static CharDef ShieldPulse => RebalanceFixture.Char("垒",
            new EffectDef(EffectKind.TurnPulse, 0, turns: 5, perHit: new[] { new EffectDef(EffectKind.Shield, 400) }));

        private static CharDef Virtue => RebalanceFixture.Char("㙓",
            new EffectDef(EffectKind.TurnPulse, 0, turns: -1,
                perHit: new[] { new EffectDef(EffectKind.ShieldSummons, 10, ofPlayerShield: true) }));

        /// <summary>脉冲兑现时按**那一刻**的玩家护盾取值:先入队的护盾脉冲(400)已结算,厚德载物读到 400 → 每只 40。</summary>
        [Test]
        public void HouDe_PulseReadsPlayerShieldAtResolution_ForEveryAliveSpirit()
        {
            var b = Battle(new[] { ShieldPulse, Virtue }, summons: new[] { Spirit(0), Spirit(3) });
            Cast(b, "垒");
            Cast(b, "㙓");
            Assert.That(SummonShieldEvents(b), Is.Empty, "施加当回合不触发");
            b.EndTurn();
            Assert.That(PlayerShieldEvents(b), Is.EqualTo(new[] { 400 }));
            Assert.That(SummonShieldEvents(b), Is.EqualTo(new[] { (0, b.PlayerShield / 10), (3, b.PlayerShield / 10) }));
            Assert.That(b.PlayerShield / 10, Is.EqualTo(40));
        }

        /// <summary>turns -1 = 本场持续:回合推进多少次都不到期。</summary>
        [Test]
        public void HouDe_PulseLastsWholeBattle()
        {
            var b = Battle(new[] { Virtue }, summons: new[] { Spirit(0) });
            Cast(b, "㙓");
            Assert.That(b.PlayerStatuses.Find(StatusKind.TurnPulse)?.TurnsLeft, Is.EqualTo(-1));
            for (int i = 0; i < 6; i++) b.EndTurn();
            Assert.That(b.PlayerStatuses.Find(StatusKind.TurnPulse)?.TurnsLeft, Is.EqualTo(-1), "6 个回合后仍在");
        }

        /// <summary>没有存活木灵:脉冲空转,不发召唤物护盾事件,玩家护盾不变。</summary>
        [Test]
        public void HouDe_NoSpirits_Idles()
        {
            var b = Battle(new[] { ShieldPulse, Virtue });
            Cast(b, "垒");
            Cast(b, "㙓");
            b.EndTurn();
            Assert.That(SummonShieldEvents(b), Is.Empty);
            Assert.That(PlayerShieldEvents(b), Is.EqualTo(new[] { 400 }));
        }

        // ================= ConfigLoader =================

        private static RecipeGraph Load(string effect) => ConfigLoader.LoadGraph(
            @"{""chars"":[{""id"":""塔"",""element"":""Earth"",""effects"":[{""kind"":""Shield"",""value"":60}," + effect + @"]}]}");

        [Test]
        public void Loader_ReadsNewFields()
        {
            var pagoda = Load(@"{""kind"":""DefenseBuff"",""value"":8,""turns"":4,""perShieldTenth"":true,""scaleCap"":7}").Get("塔").Effects[1];
            Assert.That((pagoda.PerShieldTenth, pagoda.ScaleCap, pagoda.Turns), Is.EqualTo((true, 7, 4)));
            var virtue = Load(@"{""kind"":""TurnPulse"",""value"":0,""turns"":-1,""perHit"":[{""kind"":""ShieldSummons"",""value"":10,""ofPlayerShield"":true}]}")
                .Get("塔").Effects[1];
            Assert.That((virtue.Turns, virtue.PerHit[0].Kind, virtue.PerHit[0].OfPlayerShield),
                Is.EqualTo((-1, EffectKind.ShieldSummons, true)));
            var plain = Load(@"{""kind"":""ShieldSummons"",""value"":10,""ofPlayerShield"":true}").Get("塔").Effects[1];
            Assert.That(plain.OfPlayerShield, Is.True);
        }

        [TestCase(@"{""kind"":""Shield"",""value"":8,""perShieldTenth"":true}")]                                   // 只给 DefenseBuff
        [TestCase(@"{""kind"":""DefenseBuff"",""value"":8,""turns"":4,""perShieldTenth"":true,""scaleCap"":-1}")]  // cap 不能为负
        [TestCase(@"{""kind"":""Shield"",""value"":10,""ofPlayerShield"":true}")]                                   // 只给 ShieldSummons
        [TestCase(@"{""kind"":""ShieldSummons"",""value"":0,""ofPlayerShield"":true}")]                             // 百分比 ≥ 1
        [TestCase(@"{""kind"":""ShieldSummons"",""value"":10,""ofPlayerShield"":true,""ofShield"":true}")]          // 二选一
        [TestCase(@"{""kind"":""TurnPulse"",""value"":0,""turns"":-2,""perHit"":[{""kind"":""Shield"",""value"":10}]}")]   // 只认 -1
        [TestCase(@"{""kind"":""TurnPulse"",""value"":0,""turns"":0,""perHit"":[{""kind"":""Shield"",""value"":10}]}")]    // 仍须写回合
        public void Loader_RejectsBadNewFields(string effect)
        {
            Assert.Throws<ConfigException>(() => Load(effect));
        }
    }
}
