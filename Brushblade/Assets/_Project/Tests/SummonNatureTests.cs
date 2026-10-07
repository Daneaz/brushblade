using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using Brushblade.Data;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>本命新字段与本命强化的被动覆盖(D2-0 Task 6,Ruling E6 / E7 / E8,spec §9 木)。
    /// 现有数据都不用这些字段,所以这里只钉「字段设了才生效、缺省恒等」。</summary>
    public class SummonNatureTests
    {
        private static CharDef Summoner(string id, SummonPassive passive, int hp = 100, int attack = 10,
            int count = 1, params TraitDef[] traits) =>
            new(id, Element.Wood,
                effects: new[] { new EffectDef(EffectKind.Summon, hp, summonCount: count, summonAttack: attack,
                    summonChar: "木", passive: passive) },
                traits: traits);

        private static SummonSnapshot Wood(int slot, int hp, int maxHp = 100, int attack = 10, SummonPassive passive = null) => new()
        {
            Slot = slot, Char = "木", Element = Element.Wood, Hp = hp, MaxHp = maxHp, Attack = attack, Speed = 100,
            Passive = passive,
        };

        private static BattleEngine Battle(IReadOnlyList<SummonSnapshot> summons, EnemyDef[] enemies,
            IReadOnlyDictionary<string, int> levels, params CharDef[] defs) =>
            new(RebalanceFixture.Graph(defs),
                new BattleConfig { PlayerMaxHp = RebalanceFixture.BaseMaxHp, PlayerAttack = 100 },
                defs.SelectMany(d => new[] { d.Id, d.Id }).ToArray(), Array.Empty<string>(),
                enemies, seed: 1, startingSummons: summons, cardLevels: levels);

        private static BattleEngine Battle(IReadOnlyList<SummonSnapshot> summons, params CharDef[] defs) =>
            Battle(summons, new[] { RebalanceFixture.Mob(attack: 0) }, null, defs);

        private static int Alive(BattleEngine b) => b.Summons.Count(x => x != null && x.Alive);

        // ---- MergePassive / E8 替换语义 ----

        [Test]
        public void MergePassive_OverridesNonDefault_KeepsRest_DoesNotMutateInputs()
        {
            var baseline = new SummonPassive { Thorns = 50, Ranged = true, Shape = TargetArea.Row, ShapePercent = 60, Armor = 3 };
            var over = new SummonPassive { Armor = 9, Taunt = true, SproutPercent = 30 };
            var merged = TraitRules.MergePassive(baseline, over);
            Assert.That(merged.Armor, Is.EqualTo(9));
            Assert.That(merged.Taunt, Is.True);
            Assert.That(merged.SproutPercent, Is.EqualTo(30));
            Assert.That(merged.Thorns, Is.EqualTo(50), "替换方为缺省的字段保留本体");
            Assert.That(merged.Ranged, Is.True);
            Assert.That(merged.Shape, Is.EqualTo(TargetArea.Row));
            Assert.That(merged.ShapePercent, Is.EqualTo(60));
            Assert.That(baseline.Armor, Is.EqualTo(3), "不改本体");
            Assert.That(baseline.Taunt, Is.False);
            Assert.That(over.Thorns, Is.EqualTo(0));
        }

        [Test]
        public void MergePassive_ShapeAndFalseBool_Semantics()
        {
            var baseline = new SummonPassive { Ranged = true, Shape = TargetArea.Row };
            var merged = TraitRules.MergePassive(baseline, new SummonPassive { Shape = TargetArea.Column, Ranged = false });
            Assert.That(merged.Shape, Is.EqualTo(TargetArea.Column), "Shape != Single 覆盖");
            Assert.That(merged.Ranged, Is.True, "布尔 false 不覆盖 true");
            Assert.That(TraitRules.MergePassive(null, new SummonPassive { Armor = 2 }).Armor, Is.EqualTo(2), "本体无被动也能合");
        }

        private static TraitDef Lv3Summon0(SummonPassive passive) =>
            new(TraitSlot.Lv3, TraitFace.Feature, TraitForm.Active, TraitSlot.Lv1, "强化",
                new[] { new EffectDef(EffectKind.Summon, 0, passive: passive) });

        [Test]
        public void Lv3Summon0_MergesPassive_KeepsHpAttackCount()
        {
            var def = Summoner("藤", new SummonPassive { Thorns = 50 }, hp: 100, attack: 10, count: 2,
                traits: Lv3Summon0(new SummonPassive { Armor = 5, EntrySaplings = 1 }));
            var e = TraitRules.CastEffects(def, CardFace.Feature, 3).Single();
            Assert.That(e.Kind, Is.EqualTo(EffectKind.Summon));
            Assert.That(e.Value, Is.EqualTo(100));
            Assert.That(e.SummonAttack, Is.EqualTo(10));
            Assert.That(e.SummonCount, Is.EqualTo(2));
            Assert.That(e.SummonChar, Is.EqualTo("木"));
            Assert.That(e.Passive.Thorns, Is.EqualTo(50));
            Assert.That(e.Passive.Armor, Is.EqualTo(5));
            Assert.That(e.Passive.EntrySaplings, Is.EqualTo(1));
            Assert.That(def.Effects[0].Passive.Armor, Is.EqualTo(0), "字表共享对象不被改");
        }

        [Test]
        public void Lv3Summon0_Locked_IsSameObjectAsBody()
        {
            var def = Summoner("藤", new SummonPassive { Thorns = 50 },
                traits: Lv3Summon0(new SummonPassive { Armor = 5 }));
            var effects = TraitRules.CastEffects(def, CardFace.Feature, 2);
            Assert.That(effects.Count, Is.EqualTo(1));
            Assert.That(ReferenceEquals(effects[0], def.Effects[0]), Is.True, "未解锁 = 恒等");
        }

        [Test]
        public void Lv3Summon0_WithoutBodySummon_DoesNothing()
        {
            var def = new CharDef("盾", Element.Wood,
                effects: new[] { new EffectDef(EffectKind.Shield, 5) },
                traits: new[] { Lv3Summon0(new SummonPassive { Armor = 5 }) });
            var effects = TraitRules.CastEffects(def, CardFace.Feature, 3);
            Assert.That(effects.Count, Is.EqualTo(1));
            Assert.That(effects[0].Kind, Is.EqualTo(EffectKind.Shield), "不追加 0 血召唤");
        }

        [Test]
        public void Lv3Summon0_HpAttackStillScaleByCardLevel_PassiveArmorScales()
        {
            var def = Summoner("藤", new SummonPassive { Thorns = 50 }, hp: 100, attack: 10,
                traits: Lv3Summon0(new SummonPassive { Armor = 5 }));
            var b = Battle(null, new[] { RebalanceFixture.Mob(attack: 0) },
                new Dictionary<string, int> { ["藤"] = 3 }, def);
            Assert.That(b.Cast("藤"), Is.EqualTo(BattleError.None));
            var s = b.Summons.Single(x => x != null);
            Assert.That(s.MaxHp, Is.EqualTo(MetaRules.ScaleByCardLevel(100, 3)));
            Assert.That(s.Passive.Thorns, Is.EqualTo(50));
            Assert.That(s.EffectiveDefense, Is.EqualTo(MetaRules.ScaleByCardLevel(5, 3)), "坚木护甲吃卡等级");
        }

        // ---- 各字段的引擎行为 ----

        [Test]
        public void Armor_GrantsPermanentDefenseOnEntry()
        {
            var b = Battle(null, Summoner("坚", new SummonPassive { Armor = 12 }));
            b.Cast("坚");
            var s = b.Summons.Single(x => x != null);
            Assert.That(s.EffectiveDefense, Is.EqualTo(12));
            b.EndTurn();
            Assert.That(s.EffectiveDefense, Is.EqualTo(12), "随单位存在,不过期");
        }

        [Test]
        public void Armor_Zero_NoStatus()
        {
            var b = Battle(null, Summoner("素", new SummonPassive { Thorns = 10 }));
            b.Cast("素");
            Assert.That(b.Summons.Single(x => x != null).Statuses.Has(StatusKind.DefenseBuff), Is.False);
        }

        [Test]
        public void PerAllyAttack_AddsPercentPerOtherAliveSummon()
        {
            var host = new SummonPassive { PerAllyAttackPercent = 10 };
            var b = Battle(new[] { Wood(0, 100, attack: 100, passive: host), Wood(1, 100), Wood(2, 100) },
                Summoner("素", null));
            b.Cast("素");   // 触发 RefreshSummonAura
            Assert.That(b.Summons[0].EffectiveAttack, Is.EqualTo(100 + 100 * 10 * 3 / 100), "其他 3 只(含新召的)");
            Assert.That(b.Summons[1].EffectiveAttack, Is.EqualTo(10), "没有该被动的不受影响");
        }

        [Test]
        public void PerAllyAttack_AloneOrDeadAlly_NoBonus()
        {
            var host = new SummonPassive { PerAllyAttackPercent = 10 };
            var b = Battle(new[] { Wood(0, 100, attack: 100, passive: host), Wood(1, 0) }, Summoner("素", null));
            b.EndTurn();
            Assert.That(b.Summons[0].EffectiveAttack, Is.EqualTo(100));
        }

        [Test]
        public void HealAllyTimes_RepeatsHealAlly()
        {
            int Run(int times)
            {
                var b = Battle(new[] { Wood(0, 10, attack: 0, passive: new SummonPassive { HealAlly = 3, HealAllyTimes = times }) },
                    Summoner("素", null));
                b.EndTurn();
                return b.Summons[0].Hp;
            }
            Assert.That(Run(0), Is.EqualTo(13), "缺省 = 1 次");
            Assert.That(Run(1), Is.EqualTo(13));
            Assert.That(Run(3), Is.EqualTo(19));
        }

        [Test]
        public void BackRowBonus_OnlyAppliesToBackRowTargets()
        {
            int Loss(EnemyRow row, int bonus)
            {
                var passive = new SummonPassive { Ranged = true, BackRowBonusPercent = bonus };
                var mob = new EnemyDef("靶", Element.Heart, 100000, 0, row: row);
                var b = Battle(new[] { Wood(0, 100, attack: 100, passive: passive) }, new[] { mob }, null, Summoner("素", null));
                b.EndTurn();
                return 100000 - b.Enemies[0].Hp;
            }
            int baseBack = Loss(EnemyRow.Back, 0);
            Assert.That(baseBack, Is.GreaterThan(0));
            Assert.That(Loss(EnemyRow.Back, 50), Is.EqualTo(baseBack * 3 / 2));
            Assert.That(Loss(EnemyRow.Front, 50), Is.EqualTo(Loss(EnemyRow.Front, 0)), "前排不加成");
        }

        [Test]
        public void Sprout_SplitsEachBeat_CapsAtMax_StatsFromParent()
        {
            var parent = new SummonPassive { SproutPercent = 50, SproutMax = 2 };
            var b = Battle(new[] { Wood(0, 100, attack: 10, passive: parent) }, Summoner("素", null));
            b.EndTurn();
            Assert.That(b.Summons.Count(x => x != null && x.Char == "苗"), Is.EqualTo(1));
            var sprout = b.Summons.First(x => x != null && x.Char == "苗");
            Assert.That(sprout.MaxHp, Is.EqualTo(50));
            Assert.That(sprout.Attack, Is.EqualTo(5));
            Assert.That(sprout.Element, Is.EqualTo(Element.Wood));
            Assert.That(sprout.SourceChar, Is.EqualTo("木"));
            Assert.That(sprout.SproutParentSlot, Is.EqualTo(0));
            b.EndTurn();
            Assert.That(b.Summons.Count(x => x != null && x.Char == "苗"), Is.EqualTo(2));
            b.EndTurn();
            b.EndTurn();
            Assert.That(b.Summons.Count(x => x != null && x.Char == "苗"), Is.EqualTo(2), "两拍后到上限,不再分裂");
        }

        [Test]
        public void Sprout_FullRow_DoesNotSplit()
        {
            var parent = new SummonPassive { SproutPercent = 50, SproutMax = 5 };
            int cap = Battle(null, Summoner("素", null)).SummonCapacity;
            var all = new List<SummonSnapshot> { Wood(0, 100, passive: parent) };
            for (int i = 1; i < cap; i++) all.Add(Wood(i, 100));
            var b = Battle(all, Summoner("素", null));
            b.EndTurn();
            Assert.That(b.Summons.Count(x => x != null && x.Char == "苗"), Is.EqualTo(0));
            Assert.That(Alive(b), Is.EqualTo(cap));
        }

        [Test]
        public void Sprout_ParentDies_ExistingSproutsStay_NoMoreSplit()
        {
            var parent = new SummonPassive { SproutPercent = 50, SproutMax = 3 };
            var b = Battle(new[] { Wood(0, 100, passive: parent) }, Summoner("素", null));
            b.EndTurn();
            b.Summons[0].Hp = 0;
            b.EndTurn();
            b.EndTurn();
            Assert.That(b.Summons.Count(x => x != null && x.Alive && x.Char == "苗"), Is.EqualTo(1), "小藻留场、母体死后不再分裂");
        }

        [Test]
        public void Sprout_ParentSlotSurvivesSnapshotRoundTrip()
        {
            var parent = new SummonPassive { SproutPercent = 50, SproutMax = 3 };
            var b = Battle(new[] { Wood(0, 100, passive: parent) }, Summoner("素", null));
            b.EndTurn();
            int slot = Array.FindIndex(b.Summons.ToArray(), x => x != null && x.Char == "苗");
            var snap = b.Summons[slot].Capture(slot);
            Assert.That(snap.SproutParentSlot, Is.EqualTo(0));
            Assert.That(SummonState.Restore(snap).SproutParentSlot, Is.EqualTo(0));
            Assert.That(new SummonSnapshot().SproutParentSlot, Is.EqualTo(-1), "缺省 -1:老存档不会被误当成母体在槽 0");
            Assert.That(b.Summons[0].SproutParentSlot, Is.EqualTo(-1));
        }

        [Test]
        public void EntrySaplings_SpawnTwentyPercentSaplingsOnEntry()
        {
            var b = Battle(null, Summoner("森", new SummonPassive { EntrySaplings = 2 }, hp: 100, attack: 10));
            b.Cast("森");
            var saplings = b.Summons.Where(x => x != null && x.Char == "苗").ToList();
            Assert.That(saplings.Count, Is.EqualTo(2));
            Assert.That(saplings.All(x => x.MaxHp == 20 && x.Attack == 2 && x.SourceChar == "森"), Is.True);
            Assert.That(Alive(b), Is.EqualTo(3));
        }

        [Test]
        public void EntrySaplings_Zero_SpawnsNothing()
        {
            var b = Battle(null, Summoner("素", new SummonPassive { Thorns = 1 }));
            b.Cast("素");
            Assert.That(Alive(b), Is.EqualTo(1));
        }

        private static BattleEngine CharmBattle(int chance)
        {
            var mobs = new[] { RebalanceFixture.Mob(attack: 5), RebalanceFixture.Mob(attack: 5) };
            return Battle(new[] { Wood(0, 100, attack: 10, passive: new SummonPassive { OnHitCharmChance = chance }) },
                mobs, null, Summoner("素", null));
        }

        [Test]
        public void OnHitCharm_FullChance_CharmsTarget()
        {
            var b = CharmBattle(100);
            b.EndTurn();
            Assert.That(b.LastEvents.Any(e => e.Kind == BattleEventKind.CharmedAttack), Is.True);
        }

        [Test]
        public void OnHitCharm_ZeroChance_NeverRollsRandom()
        {
            // 字段 0 时不摇 _random:暴击序列与「没有该字段」逐拍一致。
            var withField = CharmBattle(0);
            var without = Battle(new[] { Wood(0, 100, attack: 10, passive: new SummonPassive()) },
                new[] { RebalanceFixture.Mob(attack: 5), RebalanceFixture.Mob(attack: 5) }, null, Summoner("素", null));
            for (int i = 0; i < 20; i++)
            {
                withField.EndTurn();
                without.EndTurn();
                Assert.That(withField.Enemies.Select(e => e.Hp).ToArray(), Is.EqualTo(without.Enemies.Select(e => e.Hp).ToArray()));
                Assert.That(withField.LastEvents.Any(e => e.Kind == BattleEventKind.CharmedAttack), Is.False);
            }
        }

        [Test]
        public void OnHitCharmChance_ScalesWithCardLevel_ClampedTo100()
        {
            var def = Summoner("迷", new SummonPassive { OnHitCharmChance = 40 });
            var b = Battle(null, new[] { RebalanceFixture.Mob(attack: 0) },
                new Dictionary<string, int> { ["迷"] = 10 }, def);
            b.Cast("迷");
            Assert.That(b.Summons.Single(x => x != null).Passive.OnHitCharmChance, Is.EqualTo(MetaRules.ScaleByCardLevel(40, 10)));
            var b2 = Battle(null, new[] { RebalanceFixture.Mob(attack: 0) },
                new Dictionary<string, int> { ["迷"] = 30 }, Summoner("迷", new SummonPassive { OnHitCharmChance = 80 }));
            b2.Cast("迷");
            Assert.That(b2.Summons.Single(x => x != null).Passive.OnHitCharmChance, Is.EqualTo(100));
        }

        [Test]
        public void ConfigLoader_ReadsNewPassiveFields()
        {
            const string json = @"{""chars"":[
                {""id"":""木"",""element"":""Wood""},
                {""id"":""甲"",""element"":""Wood"",""effects"":[
                    {""kind"":""Summon"",""value"":20,""count"":1,""attack"":7,""summonChar"":""木"",
                     ""passive"":{""backRowBonusPercent"":30,""perAllyAttackPercent"":10,""armor"":7,""healAllyTimes"":2,
                                  ""sproutPercent"":40,""sproutMax"":3,""entrySaplings"":2,""onHitCharmChance"":25}}]}
            ]}";
            var p = ConfigLoader.LoadGraph(json).Get("甲").Effects[0].Passive;
            Assert.That((p.BackRowBonusPercent, p.PerAllyAttackPercent, p.Armor, p.HealAllyTimes,
                p.SproutPercent, p.SproutMax, p.EntrySaplings, p.OnHitCharmChance),
                Is.EqualTo((30, 10, 7, 2, 40, 3, 2, 25)));
        }

        [Test]
        public void Clone_CopiesEveryNewField()
        {
            var p = new SummonPassive
            {
                BackRowBonusPercent = 1, PerAllyAttackPercent = 2, Armor = 3, HealAllyTimes = 4,
                SproutPercent = 5, SproutMax = 6, EntrySaplings = 7, OnHitCharmChance = 8,
            };
            var c = p.Clone();
            Assert.That((c.BackRowBonusPercent, c.PerAllyAttackPercent, c.Armor, c.HealAllyTimes,
                c.SproutPercent, c.SproutMax, c.EntrySaplings, c.OnHitCharmChance),
                Is.EqualTo((1, 2, 3, 4, 5, 6, 7, 8)));
        }
    }
}
