using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>Plan D2-木 Task 5:表现层所需的 Core 读口(只读,不改结算)。
    /// 木灵格的射程图标 / 暴击 chip / 减伤 chip、召唤物详情的光环条目都读这几个口子 —— 与结算同源,
    /// 表现层不再自己推一遍「光环落在哪只木灵上」「林荫现在是几 %」。</summary>
    public class WoodPresentationReadTests
    {
        private static BattleConfig Config => new() { PlayerMaxHp = 6000, PlayerAttack = 100, ApPerTurn = 200 };

        private static SummonSnapshot W(int slot, SummonPassive passive = null, int attack = 10) => new()
        {
            Slot = slot, Char = "木", SourceChar = "林", Element = Element.Wood, Hp = 1000, MaxHp = 1000, Attack = attack,
            Speed = 100, Passive = passive,
        };

        private static BattleEngine Battle(CharDef[] defs, IReadOnlyList<SummonSnapshot> summons) =>
            new(RebalanceFixture.Graph(defs), Config, defs.SelectMany(d => Enumerable.Repeat(d.Id, 10)).ToArray(),
                Array.Empty<string>(), new[] { RebalanceFixture.Mob() }, seed: 7,
                startingHp: 3000, startingSummons: summons);

        private static CharDef Card(string id, params EffectDef[] effects) => RebalanceFixture.Char(id, effects);

        [Test]
        public void IsSummonRanged_ReadsPassiveOrRangedAura()
        {
            var arrow = Card("箭", new EffectDef(EffectKind.SummonRanged, 0));
            var b = Battle(new[] { arrow }, new[] { W(0), W(1, new SummonPassive { Ranged = true }) });
            Assert.That((b.IsSummonRanged(0), b.IsSummonRanged(1)), Is.EqualTo((false, true)), "没有光环:只看本命远程");
            Assert.That(b.Cast("箭"), Is.EqualTo(BattleError.None));
            Assert.That((b.IsSummonRanged(0), b.IsSummonRanged(1)), Is.EqualTo((true, true)), "箭灵光环:全部木灵远程");
            Assert.That(b.IsSummonRanged(5), Is.False, "空槽不算");
        }

        [Test]
        public void SummonCritPercent_AddsCritAuraOfAliveSummons()
        {
            var b = Battle(new[] { Card("木") }, new[] { W(0), W(1) });
            Assert.That(b.SummonCritPercent(b.Summons[0]), Is.EqualTo(0), "没有蟾宫:木灵暴击率 0");
            var withAura = Battle(new[] { Card("木") }, new[] { W(0), W(1, new SummonPassive { CritAura = 10 }) });
            Assert.That(withAura.SummonCritPercent(withAura.Summons[0]), Is.EqualTo(10), "蟾宫:在场木灵取最大,全部木灵都吃");
            Assert.That(withAura.EffectiveCrit, Is.EqualTo(10), "与玩家侧同一个来源");
        }

        [Test]
        public void WoodDamageCutPercent_GroveForAll_ThicketOnlyForSummons()
        {
            var grove = new SummonPassive { GroveCut = 5, GroveCutCap = 20 };
            var thicket = new SummonPassive { ThicketCut = 25, ThicketMin = 3 };
            var b = Battle(new[] { Card("木") }, new[] { W(0, grove), W(1, thicket), W(2) });
            Assert.That(b.WoodDamageCutPercent(forSummon: false), Is.EqualTo(15), "林荫:5% × 3 只,玩家吃");
            Assert.That(b.WoodDamageCutPercent(forSummon: true), Is.EqualTo(25), "木灵侧再与森严取最强");
            var none = Battle(new[] { Card("木") }, new[] { W(0), W(1) });
            Assert.That((none.WoodDamageCutPercent(false), none.WoodDamageCutPercent(true)), Is.EqualTo((0, 0)));
        }

        [Test]
        public void SummonAurasAt_ReturnsWholeTeamAndOwnSlotEntriesOnly()
        {
            var all = Card("立", new EffectDef(EffectKind.SummonEmpower, 20, turns: 3, pick: EffectPick.AllSummons));
            var other = Card("擎", new EffectDef(EffectKind.SummonEmpower, 30, turns: 3, pick: EffectPick.OtherSummons));
            var thorns = Card("荆", new EffectDef(EffectKind.SummonThorns, 30, turns: 3));
            var b = Battle(new[] { all, other, thorns }, new[] { W(0), W(1) });
            Assert.That(b.SummonAurasAt(0), Is.Empty, "没挂光环");
            b.Cast("立");
            b.Cast("擎");
            b.Cast("荆");
            Assert.That(b.SummonAurasAt(0).Select(s => (s.Kind, s.Magnitude)).OrderBy(x => x.Magnitude),
                Is.EqualTo(new[] { (StatusKind.SummonEmpower, 20), (StatusKind.SummonEmpower, 30), (StatusKind.SummonThorns, 30) }));
            Assert.That(b.SummonAurasAt(3), Has.Count.EqualTo(2), "按槽挂的擎天只落在出字时在场的 0 / 1 号");
            Assert.That(b.PlayerStatuses.All.Count(st => st.Kind == StatusKind.SummonEmpower || st.Kind == StatusKind.SummonThorns),
                Is.EqualTo(4), "夹具前提:全队一条 + 擎天两条 + 荆棘一条");
        }
    }
}
