using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using Brushblade.Data;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>Plan D2-木 Task 2:光环族。M1 玩家袋光环 SummonEmpower / SummonThorns / SummonRanged(含 TargetSlot 与
    /// 新 pick OtherSummons / StrongestSummon)、M2 本命在场光环林荫 / 森严 / 蟾宫、参天之力 / 馥郁的本命读点。
    ///
    /// 夹具:Element.Heart 敌人(对木 1.0×)、PlayerAttack = 100(攻击力缩放恒等)、玩家暴击率 0、卡等级 1、无掉字。</summary>
    public class WoodAuraTests
    {
        private static BattleConfig Config => new() { PlayerMaxHp = 6000, PlayerAttack = 100, ApPerTurn = 200 };

        private static SummonSnapshot W(int slot, int hp = 100000, int attack = 0, SummonPassive passive = null,
            string source = "林", string ch = "木") => new()
        {
            Slot = slot, Char = ch, SourceChar = source, Element = Element.Wood, Hp = hp, MaxHp = hp, Attack = attack,
            Speed = 100, Passive = passive,
        };

        private static BattleEngine Battle(CharDef[] defs, IReadOnlyList<SummonSnapshot> summons, params EnemyDef[] enemies) =>
            new(RebalanceFixture.Graph(defs), Config, defs.SelectMany(d => Enumerable.Repeat(d.Id, 10)).ToArray(),
                Array.Empty<string>(), enemies.Length > 0 ? enemies : new[] { RebalanceFixture.Mob() }, seed: 7,
                startingHp: 3000, startingSummons: summons);

        private static CharDef Card(string id, params EffectDef[] effects) => RebalanceFixture.Char(id, effects);

        private static CharDef WoodCard(string id, params EffectDef[] effects) => new(id, Element.Wood, effects: effects,
            attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 10) });

        private static EffectDef Body(int attack = 100, int hp = 300) =>
            new(EffectKind.Summon, hp, summonAttack: attack, summonChar: "木");

        private static EffectDef Empower(int value, EffectPick pick, int turns = 3) =>
            new(EffectKind.SummonEmpower, value, turns: turns, pick: pick);

        private static int[] Attacks(BattleEngine b) =>
            b.Summons.Select(s => s == null || !s.Alive ? -1 : s.EffectiveAttack).ToArray();

        private static IEnumerable<StatusEffect> Auras(BattleEngine b, StatusKind kind) =>
            b.PlayerStatuses.All.Where(s => s.Kind == kind);

        private static readonly EnemyDef Shooter =   // 只打玩家的远程(打不到木灵,木灵不掉血)
            new("盯", Element.Heart, 100000, 1000, range: AttackRange.Ranged, focus: AttackFocus.Player);

        // ---------------- M1 SummonEmpower ----------------

        [Test]
        public void M1_EmpowerAllSummons_IncludesLaterSummons_AndExpiresByPlayerTurns()
        {
            var forest = WoodCard("立", Body(), Empower(20, EffectPick.AllSummons));
            var plain = WoodCard("木", Body());
            var b = Battle(new[] { forest, plain }, null, RebalanceFixture.Mob());
            Assert.That(b.Cast("立"), Is.EqualTo(BattleError.None));
            Assert.That(b.Summons[0].EffectiveAttack, Is.EqualTo(120), "100 + 100 × 20%");
            var aura = Auras(b, StatusKind.SummonEmpower).Single();
            Assert.That((aura.TargetSlot, aura.TurnsLeft, aura.Magnitude), Is.EqualTo((-1, 3, 20)));
            Assert.That(b.Cast("木"), Is.EqualTo(BattleError.None));
            Assert.That(b.Summons[1].EffectiveAttack, Is.EqualTo(120), "之后召出的也吃(Q21,TargetSlot −1)");

            b.EndTurn();   // 第 2 回合开始:3 → 2
            b.EndTurn();   // 第 3 回合开始:2 → 1
            Assert.That(Auras(b, StatusKind.SummonEmpower).Count(), Is.EqualTo(1), "施加当回合 + 之后 2 个玩家回合");
            Assert.That(Attacks(b).Take(2), Is.EqualTo(new[] { 120, 120 }));
            b.EndTurn();   // 第 4 回合开始:到期
            Assert.That(Auras(b, StatusKind.SummonEmpower).Any(), Is.False, "按玩家回合递减(Q7)");
            Assert.That(Attacks(b).Take(2), Is.EqualTo(new[] { 100, 100 }), "到期后攻击回落(tick 后即时刷新)");
        }

        [Test]
        public void M1_EmpowerOnSameSummon_TakesStrongest_SkyPowerAdds()
        {
            // Q6:林立 20(全部)/ 擎天 30(逐只)/ 桂冠 50(最强一只)对同一只取最强;参天之力(本命 AttackPercent)另加
            var b = Battle(new[]
                {
                    Card("立", Empower(20, EffectPick.AllSummons)),
                    Card("擎", Empower(30, EffectPick.OtherSummons)),
                    Card("冠", Empower(50, EffectPick.StrongestSummon)),
                },
                new[] { W(0, attack: 100), W(1, attack: 100), W(2, attack: 100, passive: new SummonPassive { AttackPercent = 20 }) });
            Assert.That(Attacks(b).Take(3), Is.EqualTo(new[] { 100, 100, 120 }), "参天之力:+基础攻击 × 20%");
            b.Cast("立");
            Assert.That(Attacks(b).Take(3), Is.EqualTo(new[] { 120, 120, 140 }));
            b.Cast("擎");
            Assert.That(Attacks(b).Take(3), Is.EqualTo(new[] { 130, 130, 150 }), "取最强 30,不是 20 + 30");
            b.Cast("冠");
            Assert.That(Attacks(b).Take(3), Is.EqualTo(new[] { 130, 130, 170 }), "最强的是 2 号(150):50 + 参天 20");
        }

        [Test]
        public void M1_EmpowerOtherSummons_SkipsThisCastAndLaterSummons()
        {
            // Q22 擎天:出字时在场、且不是本次召出的木灵逐只挂;之后召出的不吃
            var pillar = WoodCard("擎", Body(), Empower(30, EffectPick.OtherSummons));
            var plain = WoodCard("木", Body());
            var b = Battle(new[] { pillar, plain }, new[] { W(0, attack: 100) });
            b.Cast("擎");
            Assert.That(Attacks(b).Take(2), Is.EqualTo(new[] { 130, 100 }), "本次召出的 1 号不吃");
            Assert.That(Auras(b, StatusKind.SummonEmpower).Select(s => s.TargetSlot), Is.EqualTo(new[] { 0 }));
            b.Cast("木");
            Assert.That(Attacks(b).Take(3), Is.EqualTo(new[] { 130, 100, 100 }), "之后召出的不吃");
        }

        [Test]
        public void M1_EmpowerOtherSummons_SkipsThisCastsEntrySaplings()
        {
            // Fix round 1 裁定:森然入场幼苗是本次召唤的一部分,不算「出字时在场」
            var pillar = WoodCard("擎",
                new EffectDef(EffectKind.Summon, 300, summonAttack: 100, summonChar: "木",
                    passive: new SummonPassive { EntrySaplings = 1 }),
                Empower(30, EffectPick.OtherSummons));
            var b = Battle(new[] { pillar }, new[] { W(0, attack: 100) });
            b.Cast("擎");
            Assert.That(b.Summons[2]?.Char, Is.EqualTo("苗"), "夹具前提:入场幼苗落在 2 号");
            Assert.That(Attacks(b).Take(3), Is.EqualTo(new[] { 130, 100, 20 }), "只有 0 号吃擎天");
            Assert.That(Auras(b, StatusKind.SummonEmpower).Select(s => s.TargetSlot), Is.EqualTo(new[] { 0 }));
        }

        [Test]
        public void M1_EmpowerAndMorale_SurviveCaptureRestore()
        {
            // Fix round 1(Minor 1):读档时光环与战意乘区要在玩家袋恢复之后再刷
            var pillar = Card("擎", Empower(30, EffectPick.OtherSummons));
            var war = Card("战", new EffectDef(EffectKind.Morale, 1));
            var b = Battle(new[] { pillar, war }, new[] { W(0, attack: 100), W(2, attack: 100) });
            b.Cast("擎");
            b.Cast("战");
            int[] before = Attacks(b);
            Assert.That(before.Take(3), Is.EqualTo(new[] { 143, -1, 143 }), "夹具前提:(100 + 30) × 110%");
            var restored = BattleEngine.Restore(b.Capture(), RebalanceFixture.Graph(pillar, war), Config, null,
                new Dictionary<string, EnemyDef> { ["怔"] = RebalanceFixture.Mob() });
            Assert.That(Attacks(restored), Is.EqualTo(before));
            Assert.That(restored.Summons[0].PlayerAttackPercent, Is.EqualTo(110));
        }

        [Test]
        public void M1_EmpowerStrongestSummon_ResolvedAtCastEnd_TieTakesLowestSlot()
        {
            // 桂冠写在召唤之前也要等出字结束再选:本次召出的(攻 200)才是最强
            var laurel = WoodCard("冠", Empower(50, EffectPick.StrongestSummon), Body(attack: 200));
            var b = Battle(new[] { laurel }, new[] { W(0, attack: 100), W(1, attack: 100) });
            b.Cast("冠");
            Assert.That(Attacks(b).Take(3), Is.EqualTo(new[] { 100, 100, 300 }));

            var tie = Battle(new[] { Card("冠", Empower(50, EffectPick.StrongestSummon)) },
                new[] { W(1, attack: 100), W(3, attack: 100) });
            tie.Cast("冠");
            Assert.That(Auras(tie, StatusKind.SummonEmpower).Single().TargetSlot, Is.EqualTo(1), "同值取槽位小");
        }

        // ---------------- 按槽位的光环随木灵生命周期清理(Review Focus 1)----------------

        [Test]
        public void M1_SlotAura_ClearedOnDeath_AndCorpseSlotReuseDoesNotInherit()
        {
            var pillar = Card("擎", Empower(30, EffectPick.OtherSummons));
            var plain = WoodCard("木", Body());
            var b = Battle(new[] { pillar, plain }, new[] { W(0, hp: 300), W(4, hp: 300, attack: 100) },
                RebalanceFixture.Mob(attack: 1000));   // 近战打前排 0 号,一击打死;4 号在后排够不着
            b.Cast("擎");
            Assert.That(Auras(b, StatusKind.SummonEmpower).Select(s => s.TargetSlot), Is.EquivalentTo(new[] { 0, 4 }));
            b.EndTurn();
            Assert.That(b.Summons[0].Alive, Is.False, "夹具前提:0 号阵亡");
            Assert.That(Auras(b, StatusKind.SummonEmpower).Select(s => s.TargetSlot), Is.EqualTo(new[] { 4 }),
                "阵亡(OnSummonDeath 单点)清掉指向它的条目");
            Assert.That(b.Summons[4].EffectiveAttack, Is.EqualTo(130), "别的槽不受影响");
            Assert.That(b.Cast("木", summonSlots: new[] { 0 }), Is.EqualTo(BattleError.None));
            Assert.That(b.Summons[0].Alive, Is.True);
            Assert.That(b.Summons[0].EffectiveAttack, Is.EqualTo(100), "尸体槽复用:新木灵不继承");
        }

        [Test]
        public void M1_SlotAura_ClearedWhenSummonIsReplaced()
        {
            var pillar = Card("擎", Empower(30, EffectPick.OtherSummons));
            var plain = WoodCard("木", Body());
            var b = Battle(new[] { pillar, plain }, new[] { W(0, attack: 100) });
            b.Cast("擎");
            Assert.That(b.Summons[0].EffectiveAttack, Is.EqualTo(130));
            Assert.That(b.Cast("木", replaceSummon: true, summonSlots: new[] { 0 }), Is.EqualTo(BattleError.None));
            Assert.That(b.Summons[0].EffectiveAttack, Is.EqualTo(100), "顶替不走阵亡,落位处清理");
            Assert.That(Auras(b, StatusKind.SummonEmpower).Any(), Is.False);
        }

        [Test]
        public void M1_SlotAura_PlacementClearsAnyStaleEntryForThatSlot()
        {
            // 防御性:落位处无条件清掉指向该槽的条目(尸体槽 / 空槽都算),不依赖阵亡路径先清过
            var plain = WoodCard("木", Body());
            var b = Battle(new[] { plain }, new[] { W(0, attack: 100) });
            b.Summons[0].Hp = 0;
            b.PlayerStatuses.Apply(new StatusEffect
            {
                Kind = StatusKind.SummonThorns, Polarity = StatusPolarity.Buff, Magnitude = 40, TurnsLeft = 3,
                SourceId = "旧", TargetSlot = 0,
            });
            b.PlayerStatuses.Apply(new StatusEffect
            {
                Kind = StatusKind.SummonEmpower, Polarity = StatusPolarity.Buff, Magnitude = 50, TurnsLeft = 3,
                SourceId = "旧", TargetSlot = 0,
            });
            Assert.That(b.Cast("木", summonSlots: new[] { 0 }), Is.EqualTo(BattleError.None));
            Assert.That(b.PlayerStatuses.All.Any(s => s.TargetSlot == 0), Is.False);
            Assert.That(b.Summons[0].EffectiveAttack, Is.EqualTo(100));
        }

        // ---------------- M1 SummonThorns / SummonRanged ----------------

        [Test]
        public void M1_ThornsAura_TakesMaxWithPassiveThorns()
        {
            // Q12:光环荆棘与本命荆棘取最强(30 > 20 → 30%,不是 50%)
            var thicket = Card("棘", new EffectDef(EffectKind.SummonThorns, 30, turns: 3));
            var b = Battle(new[] { thicket }, new[] { W(0, passive: new SummonPassive { Thorns = 20 }) },
                RebalanceFixture.Mob(attack: 1000));
            b.Cast("棘");
            Assert.That(Auras(b, StatusKind.SummonThorns).Single().TargetSlot, Is.EqualTo(-1));
            b.EndTurn();
            int thorns = b.LastEvents.Where(e => e.Kind == BattleEventKind.Damage && e.Source == EffectSource.Thorns)
                .Sum(e => e.Amount);
            Assert.That(thorns, Is.EqualTo(300));

            var strong = Battle(new[] { thicket }, new[] { W(0, passive: new SummonPassive { Thorns = 50 }) },
                RebalanceFixture.Mob(attack: 1000));
            strong.Cast("棘");
            strong.EndTurn();
            Assert.That(strong.LastEvents.Where(e => e.Kind == BattleEventKind.Damage && e.Source == EffectSource.Thorns)
                .Sum(e => e.Amount), Is.EqualTo(500), "本命更强时用本命");
        }

        [Test]
        public void M1_RangedAura_LetsMeleeSummonsHitBackRow()
        {
            var arrow = Card("箭", new EffectDef(EffectKind.SummonRanged, 0));
            EnemyDef[] Foes() => new[]
            {
                RebalanceFixture.Mob(),
                new EnemyDef("远", Element.Heart, 100000, 0, row: EnemyRow.Back),
            };
            var control = Battle(new[] { arrow }, new[] { W(0, attack: 50) }, Foes());
            control.EndTurn();
            Assert.That(control.LastEvents.Where(e => e.Kind == BattleEventKind.SummonAttack).Select(e => e.TargetIndex),
                Is.EqualTo(new[] { 0 }), "夹具前提:近战打前排");

            var b = Battle(new[] { arrow }, new[] { W(0, attack: 50) }, Foes());
            b.Cast("箭");
            Assert.That(Auras(b, StatusKind.SummonRanged).Single().TurnsLeft, Is.EqualTo(-1), "本场");
            b.EndTurn();
            Assert.That(b.LastEvents.Where(e => e.Kind == BattleEventKind.SummonAttack).Select(e => e.TargetIndex),
                Is.EqualTo(new[] { 1 }), "远程优先打后排");
        }

        // ---------------- M2 林荫 / 森严(Q8)----------------

        [TestCase(0, false, 3, 850)]    // 林荫 5 × 3 只 = 15%
        [TestCase(0, false, 5, 800)]    // 5 × 5 = 25 → 封顶 20%
        [TestCase(10, false, 3, 850)]   // 与 DamageCut 取最强(15 > 10)
        [TestCase(30, false, 3, 700)]   // DamageCut 30 更强
        [TestCase(0, true, 3, 450)]     // 格挡 40 + 15
        [TestCase(30, true, 3, 400)]    // 40 + max(30, 15) = 70 → 钳 60
        public void M2_GroveCut_PlayerSide_MaxWithDamageCut_PlusBlock_Capped(int damageCut, bool block, int count, int expected)
        {
            var defs = new[]
            {
                Card("减", new EffectDef(EffectKind.DamageCut, Math.Max(1, damageCut))),
                Card("挡", new EffectDef(EffectKind.Block, 1)),
            };
            var summons = Enumerable.Range(0, count)
                .Select(i => W(i, passive: i == 0 ? new SummonPassive { GroveCut = 5, GroveCutCap = 20 } : null)).ToArray();
            var b = Battle(defs, summons, Shooter);
            if (damageCut > 0) b.Cast("减");
            if (block) b.Cast("挡");
            b.EndTurn();
            Assert.That(b.LastEvents.Single(e => e.Kind == BattleEventKind.EnemyAttack).Amount, Is.EqualTo(expected));
        }

        [Test]
        public void M2_GroveAndThicket_SummonSide_ThicketOnlySummons_NeedsMinCount()
        {
            // 0 号:森严 25%(存活木灵 ≥ 3);1 号:林荫 5 / 20。近战打木灵,远程只打玩家
            SummonSnapshot[] Field(int count) => Enumerable.Range(0, count).Select(i => W(i, passive: i switch
            {
                0 => new SummonPassive { ThicketCut = 25, ThicketMin = 3 },
                1 => new SummonPassive { GroveCut = 5, GroveCutCap = 20 },
                _ => null,
            })).ToArray();
            int Hit(BattleEngine x, BattleEventKind kind) => x.LastEvents.First(e => e.Kind == kind).Amount;

            var three = Battle(Array.Empty<CharDef>(), Field(3), RebalanceFixture.Mob(attack: 1000), Shooter);
            three.EndTurn();
            Assert.That(Hit(three, BattleEventKind.SummonHit), Is.EqualTo(750), "木灵:max(林荫 15, 森严 25)");
            Assert.That(Hit(three, BattleEventKind.EnemyAttack), Is.EqualTo(850), "玩家只吃林荫,森严只给木灵");

            var two = Battle(Array.Empty<CharDef>(), Field(2), RebalanceFixture.Mob(attack: 1000), Shooter);
            two.EndTurn();
            Assert.That(Hit(two, BattleEventKind.SummonHit), Is.EqualTo(900), "存活 2 只 < 3:森严不生效,林荫 10%");
        }

        // ---------------- M2 蟾宫(Q23 / Ruling 18)----------------

        [Test]
        public void M2_CritAura_AddsStrongestLivingToPlayerCrit()
        {
            var b = Battle(Array.Empty<CharDef>(), new[]
            {
                W(0, passive: new SummonPassive { CritAura = 10 }),
                W(1, passive: new SummonPassive { CritAura = 15 }),
                W(2, passive: new SummonPassive { CritAura = 40 }),
            });
            Assert.That(b.EffectiveCrit, Is.EqualTo(40), "取最大值,不相加");
            b.Summons[2].Hp = 0;
            Assert.That(b.EffectiveCrit, Is.EqualTo(15), "阵亡的不算");
        }

        [Test]
        public void M2_CritAura_GatesSummonCritRoll_StopsAfterLaurelDies()
        {
            // 0 号桂灵(攻 0,不出手)在前排挨打,3 下死;4 号在后排每拍出手。无掉字、单敌、命中 100 → _random 只剩木灵暴击
            var b = Battle(Array.Empty<CharDef>(), new[]
            {
                W(0, hp: 1500, passive: new SummonPassive { CritAura = 10 }),
                W(4, attack: 50),
            }, RebalanceFixture.Mob(attack: 500));
            uint state = b.Capture().RandomState;
            bool rolledWhileAlive = false;
            while (b.Summons[0].Alive)
            {
                b.EndTurn();
                if (b.Capture().RandomState != state) rolledWhileAlive = true;
                state = b.Capture().RandomState;
            }
            Assert.That(rolledWhileAlive, Is.True, "桂灵在场时木灵摇暴击");
            for (int t = 0; t < 3; t++)
            {
                b.EndTurn();
                Assert.That(b.LastEvents.Any(e => e.Kind == BattleEventKind.SummonAttack), Is.True, "4 号照常出手");
                Assert.That(b.Capture().RandomState, Is.EqualTo(state), "桂灵阵亡后不再摇号");
            }
        }

        // ---------------- 馥郁(HealAllyPercent)----------------

        [TestCase(0, 3030)]
        [TestCase(30, 3039)]
        public void Fragrance_HealAllyPercent_ScalesAuraHeal(int percent, int expectedHp)
        {
            var b = Battle(Array.Empty<CharDef>(),
                new[] { W(0, passive: new SummonPassive { HealAlly = 30, HealAllyPercent = percent }) });
            b.EndTurn();
            Assert.That(b.PlayerHp, Is.EqualTo(expectedHp));
        }

        // ---------------- 名单 / 规则 ----------------

        [Test]
        public void Rules_SummonEmpowerPickWhitelist_BattleScoped_SlotKeyedApply()
        {
            Assert.That(EffectPickRules.Allows(EffectKind.SummonEmpower, EffectPick.Primary), Is.False, "必须写 pick");
            foreach (var p in new[] { EffectPick.AllSummons, EffectPick.OtherSummons, EffectPick.StrongestSummon })
                Assert.That(EffectPickRules.Allows(EffectKind.SummonEmpower, p), Is.True, p.ToString());
            Assert.That(EffectPickRules.Allows(EffectKind.SummonEmpower, EffectPick.Random), Is.False);
            Assert.That(EffectPickRules.Allows(EffectKind.SummonEmpower, EffectPick.SummonedThisCast), Is.False);
            Assert.That(EffectPickRules.Allows(EffectKind.Taunt, EffectPick.OtherSummons), Is.False);
            Assert.That(EffectPickRules.Allows(EffectKind.Taunt, EffectPick.StrongestSummon), Is.False);
            Assert.That(EffectPickRules.Allows(EffectKind.Taunt, EffectPick.AllSummons), Is.True, "嘲讽原口径不变");
            Assert.That(EffectPickRules.Allows(EffectKind.SummonThorns, EffectPick.AllSummons), Is.False);
            Assert.That(EffectPickRules.Allows(EffectKind.SummonThorns, EffectPick.Primary), Is.True);
            Assert.That(EffectPickRules.Allows(EffectKind.SummonRanged, EffectPick.Primary), Is.True);
            foreach (var k in new[] { StatusKind.SummonEmpower, StatusKind.SummonThorns, StatusKind.SummonRanged })
                Assert.That(StatusRules.IsBattleScoped(k), Is.True, k.ToString());

            var bag = new StatusBag();
            StatusEffect At(int slot, int m) => new()
                { Kind = StatusKind.SummonEmpower, Magnitude = m, TurnsLeft = 3, SourceId = "擎", TargetSlot = slot };
            bag.Apply(At(0, 30));
            bag.Apply(At(1, 30));
            bag.Apply(At(0, 40));
            Assert.That(bag.All.Select(s => (s.TargetSlot, s.Magnitude)), Is.EqualTo(new[] { (0, 40), (1, 30) }),
                "同源按槽位各一条,同槽刷新");
        }

        private static RecipeGraph Load(string effects) => ConfigLoader.LoadGraph(
            @"{""chars"":[{""id"":""林"",""element"":""Wood"",""effects"":[" + effects + @"],
              ""attackEffects"":[{""kind"":""DamageSingle"",""value"":10}]}]}");

        [TestCase("AllSummons")]
        [TestCase("OtherSummons")]
        [TestCase("StrongestSummon")]
        public void Loader_SummonEmpower_AcceptsSummonPicks(string pick)
        {
            var e = Load(@"{""kind"":""SummonEmpower"",""value"":20,""turns"":3,""pick"":""" + pick + @"""}").Get("林").Effects[0];
            Assert.That((e.Kind, e.Value, e.Turns, e.Pick.ToString()), Is.EqualTo((EffectKind.SummonEmpower, 20, 3, pick)));
        }

        [TestCase(@"{""kind"":""SummonEmpower"",""value"":20,""turns"":3}")]                              // 必须写 pick
        [TestCase(@"{""kind"":""SummonEmpower"",""value"":20,""turns"":3,""pick"":""Random""}")]
        [TestCase(@"{""kind"":""SummonEmpower"",""value"":0,""turns"":3,""pick"":""AllSummons""}")]      // 量须 > 0
        [TestCase(@"{""kind"":""SummonThorns"",""value"":0,""turns"":3}")]
        [TestCase(@"{""kind"":""SummonThorns"",""value"":30,""turns"":3,""pick"":""AllSummons""}")]      // 荆棘光环恒为全部
        [TestCase(@"{""kind"":""SummonEmpower"",""value"":20,""pick"":""AllSummons""}")]                 // 必须写 turns > 0
        [TestCase(@"{""kind"":""SummonEmpower"",""value"":20,""turns"":0,""pick"":""AllSummons""}")]
        [TestCase(@"{""kind"":""SummonThorns"",""value"":30}")]
        [TestCase(@"{""kind"":""SummonThorns"",""value"":30,""turns"":0}")]
        [TestCase(@"{""kind"":""Taunt"",""value"":2,""pick"":""OtherSummons""}")]                         // 新 pick 只给 SummonEmpower
        [TestCase(@"{""kind"":""Taunt"",""value"":2,""pick"":""StrongestSummon""}")]
        public void Loader_RejectsBadAuraWritings(string effect)
        {
            Assert.Throws<ConfigException>(() => Load(effect));
        }
    }
}
