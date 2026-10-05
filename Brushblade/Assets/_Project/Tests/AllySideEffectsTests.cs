using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using Brushblade.Data;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>我方侧新效果与状态(D1 Task 7,附录 M12/M13/M14/M16/M17/M18):本回合减伤、反击增强、保命、
    /// 木灵群体操作(幼苗 / 群疗 / 群盾 / 群刺)、治疗转盾、净化计数与 Pick.Self、直接加泉加厚。
    ///
    /// 夹具口径同 SeedVulnerableTests:Element.Heart(生克 1.0×)、PlayerAttack = 100、无甲靶子。</summary>
    public class AllySideEffectsTests
    {
        private static BattleConfig Config => new() { PlayerMaxHp = RebalanceFixture.BaseMaxHp, PlayerAttack = 100 };

        private static BattleEngine Battle(CharDef[] defs, int? startingHp = null,
            IReadOnlyList<SummonSnapshot> summons = null, params EnemyDef[] enemies) =>
            new(RebalanceFixture.Graph(defs), Config,
                defs.SelectMany(d => new[] { d.Id, d.Id, d.Id }).ToArray(), Array.Empty<string>(),
                enemies.Length > 0 ? enemies : new[] { RebalanceFixture.Mob() }, seed: 1,
                startingHp: startingHp, startingSummons: summons);

        private static CharDef Def(string id, params EffectDef[] effects) => new(id, Element.Heart, effects: effects);

        private static SummonSnapshot Summon(int slot, int hp, int maxHp = 100, int attack = 0) => new()
        {
            Slot = slot, Char = "木", Element = Element.Heart, Hp = hp, MaxHp = maxHp, Attack = attack, Speed = 100,
        };

        private static int HitTaken(BattleEngine b)
        {
            int hp = b.PlayerHp;
            b.EndTurn();
            return hp - b.PlayerHp;
        }

        // ---------------- 本回合减伤 DamageCut ----------------

        [Test]
        public void DamageCut_ReducesHitsThisRound_ThenExpiresAtNextPlayerTurn()
        {
            var cut = Def("壁", new EffectDef(EffectKind.DamageCut, 20));
            var b = Battle(new[] { cut }, null, null, RebalanceFixture.Mob(attack: 100));
            Assert.That(b.Cast("壁"), Is.EqualTo(BattleError.None));
            var s = b.PlayerStatuses.Find(StatusKind.DamageCut);
            Assert.That(s, Is.Not.Null);
            Assert.That(s.Magnitude, Is.EqualTo(20));
            Assert.That(s.TurnsLeft, Is.EqualTo(1));
            Assert.That(s.Polarity, Is.EqualTo(StatusPolarity.Buff));
            Assert.That(HitTaken(b), Is.EqualTo(80));
            Assert.That(b.PlayerStatuses.Has(StatusKind.DamageCut), Is.False, "玩家回合开始时到期");
            Assert.That(HitTaken(b), Is.EqualTo(100), "下一轮不再减");
        }

        [Test]
        public void DamageCut_PlusBlock_CappedAtNonArmorReduction60()
        {
            // 格挡 40 + 减伤 30 = 70 → 封顶 60
            var wall = new CharDef("壁", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.Block, 1), new EffectDef(EffectKind.DamageCut, 30) },
                attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 100) });
            var b = Battle(new[] { wall }, null, null, RebalanceFixture.Mob(attack: 100));
            b.Cast("壁", -1, attackMode: false);
            Assert.That(HitTaken(b), Is.EqualTo(100 * (100 - CombatCaps.NonArmorReductionPercent) / 100));
        }

        [Test]
        public void DamageCut_IsDiscrete_NotScaledByCardLevel()
        {
            var cut = Def("壁", new EffectDef(EffectKind.DamageCut, 20));
            var b = new BattleEngine(RebalanceFixture.Graph(cut), Config, new[] { "壁" }, Array.Empty<string>(),
                new[] { RebalanceFixture.Mob() }, seed: 1, cardLevels: new Dictionary<string, int> { ["壁"] = 10 });
            b.Cast("壁");
            Assert.That(b.PlayerStatuses.Find(StatusKind.DamageCut).Magnitude, Is.EqualTo(20));
        }

        // ---------------- 反击增强 CounterBoost ----------------

        private static CharDef Riposte => new("戈", Element.Heart,
            effects: new[] { new EffectDef(EffectKind.Block, 1), new EffectDef(EffectKind.CounterBoost, 100) },
            attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 100) });

        [Test]
        public void CounterBoost_DoublesBlockCounter()
        {
            // 反击 = 100 × 30% = 30 → ×2 = 60;敌攻 400,格挡后 240,预算 144 够用
            var b = Battle(new[] { Riposte }, null, null, RebalanceFixture.Mob(attack: 400));
            b.Cast("戈", -1, attackMode: false);
            var s = b.PlayerStatuses.Find(StatusKind.CounterBoost);
            Assert.That(s, Is.Not.Null);
            Assert.That(s.TurnsLeft, Is.EqualTo(1));
            b.EndTurn();
            Assert.That(100000 - b.Enemies[0].Hp, Is.EqualTo(60));
            Assert.That(b.PlayerStatuses.Has(StatusKind.CounterBoost), Is.False, "本回合有效,玩家回合开始到期");
        }

        [Test]
        public void CounterBoost_StillCappedByReflectBudget()
        {
            // 敌攻 100 → 格挡后 60,预算 60×60% = 36 < 60
            var b = Battle(new[] { Riposte }, null, null, RebalanceFixture.Mob(attack: 100));
            b.Cast("戈", -1, attackMode: false);
            b.EndTurn();
            Assert.That(100000 - b.Enemies[0].Hp, Is.EqualTo(36));
        }

        // ---------------- 保命 Endure ----------------

        private static CharDef Rooted => Def("扎",
            new EffectDef(EffectKind.Summon, 10, summonCount: 1, summonChar: "木"),
            new EffectDef(EffectKind.Endure, 0, pick: EffectPick.SummonedThisCast));

        [Test]
        public void Endure_AttachesToSummonedThisCast_SavesOnce()
        {
            var b = Battle(new[] { Rooted }, null, null, RebalanceFixture.Mob(attack: 100));
            Assert.That(b.Cast("扎"), Is.EqualTo(BattleError.None));
            int slot = Array.FindIndex(b.Summons.ToArray(), s => s != null);
            Assert.That(b.Summons[slot].Statuses.Has(StatusKind.Endure), Is.True);
            Assert.That(BattleEngine.NeedsAllyTarget(Rooted), Is.False, "落点由选择器给出,不选友方");

            b.EndTurn();
            Assert.That(b.Summons[slot].Alive, Is.True, "致命一击留 1 血");
            Assert.That(b.Summons[slot].Hp, Is.EqualTo(1));
            Assert.That(b.Summons[slot].Statuses.Has(StatusKind.Endure), Is.False, "用掉即移除");

            b.EndTurn();
            Assert.That(b.Summons[slot].Alive, Is.False, "只救一次");
        }

        [Test]
        public void Endure_DoesNotTriggerOnNonLethalHit()
        {
            var big = Def("扎", new EffectDef(EffectKind.Summon, 1000, summonCount: 1, summonChar: "木"),
                new EffectDef(EffectKind.Endure, 0, pick: EffectPick.SummonedThisCast));
            var b = Battle(new[] { big }, null, null, RebalanceFixture.Mob(attack: 100));
            b.Cast("扎");
            int slot = Array.FindIndex(b.Summons.ToArray(), s => s != null);
            b.EndTurn();
            Assert.That(b.Summons[slot].Hp, Is.EqualTo(900));
            Assert.That(b.Summons[slot].Statuses.Has(StatusKind.Endure), Is.True);
        }

        [Test]
        public void Endure_DoesNotSaveFromDevour()
        {
            var boss = new EnemyDef("噬", Element.Heart, 200, 5,
                phases: new[] { new BossPhaseDef("甲", Element.Heart, 200, 5, skill: BossSkill.Devour) });
            var b = new BattleEngine(RebalanceFixture.Graph(Rooted),
                new BattleConfig { PlayerMaxHp = 500, PlayerAttack = 100, ApPerTurn = 20, BossPhaseJitterPercent = 0 },
                new[] { "扎", "扎" }, Array.Empty<string>(), new[] { boss }, seed: 1);
            b.EndTurn();
            Assert.That(b.Cast("扎"), Is.EqualTo(BattleError.None));
            int slot = Array.FindIndex(b.Summons.ToArray(), s => s != null);
            b.EndTurn();   // 蓄力
            Assert.That(b.Summons[slot].Statuses.Has(StatusKind.Endure), Is.True);
            b.EndTurn();   // 吞噬
            Assert.That(b.Summons[slot].Alive, Is.False, "吞噬直接移除,不算受伤");
        }

        // ---------------- 幼苗 SummonSapling ----------------

        private static CharDef Sprout => Def("芽",
            new EffectDef(EffectKind.Summon, 100, summonCount: 1, summonAttack: 50, summonChar: "木",
                passive: new SummonPassive { Thorns = 20 }),
            new EffectDef(EffectKind.SummonSapling, 20, summonCount: 1));

        [Test]
        public void SummonSapling_SummonsPercentCopyOfFirstSummon_NoPassive()
        {
            var b = Battle(new[] { Sprout });
            Assert.That(b.SummonCountOf(Sprout), Is.EqualTo(2), "SummonCountOf 计入幼苗");
            Assert.That(b.Cast("芽"), Is.EqualTo(BattleError.None));
            var alive = b.Summons.Where(s => s != null && s.Alive).ToList();
            Assert.That(alive.Count, Is.EqualTo(2));
            var sapling = alive.Single(s => s.Char == "苗");
            Assert.That(sapling.MaxHp, Is.EqualTo(20));
            Assert.That(sapling.Hp, Is.EqualTo(20));
            Assert.That(sapling.Attack, Is.EqualTo(10));
            Assert.That(sapling.Passive, Is.Null, "幼苗无本命");
            Assert.That(sapling.SourceChar, Is.EqualTo("芽"));
        }

        [Test]
        public void SummonSapling_NoFreeSlot_DoesNotSummon_AndDoesNotReplace()
        {
            // 只空一格:本体召唤落那一格,幼苗没空位 → 不召,也不触发替换确认
            int capacity = Battle(new[] { Sprout }).SummonCapacity;
            var others = Enumerable.Range(0, capacity - 1).Select(i => Summon(i, 50)).ToArray();
            var b = Battle(new[] { Sprout }, null, others);
            Assert.That(b.SummonReplaceCountOf(Sprout), Is.EqualTo(0), "幼苗不顶人,不弹替换");
            Assert.That(b.Cast("芽"), Is.EqualTo(BattleError.None));
            Assert.That(b.Summons.Count(s => s != null && s.Alive), Is.EqualTo(capacity));
            Assert.That(b.Summons.Any(s => s != null && s.Char == "苗"), Is.False);
            Assert.That(b.Summons.Count(s => s != null && s.Char == "木" && s.Hp == 50), Is.EqualTo(capacity - 1), "原有的都还在");
        }

        [Test]
        public void SummonSapling_Count2_TakesFollowingPlannedSlots()
        {
            var two = Def("繁", new EffectDef(EffectKind.Summon, 100, summonCount: 1, summonAttack: 50, summonChar: "木"),
                new EffectDef(EffectKind.SummonSapling, 20, summonCount: 2));
            var b = Battle(new[] { two });
            Assert.That(b.SummonCountOf(two), Is.EqualTo(3));
            var plan = b.PlanSummonSlots(2, b.SummonCountOf(two));
            Assert.That(b.Cast("繁", summonSlots: plan), Is.EqualTo(BattleError.None));
            Assert.That(b.Summons[plan[0]].Char, Is.EqualTo("木"));
            Assert.That(b.Summons[plan[1]].Char, Is.EqualTo("苗"));
            Assert.That(b.Summons[plan[2]].Char, Is.EqualTo("苗"));
        }

        // ---------------- 群疗 / 群盾 / 群刺 ----------------

        [Test]
        public void HealSummons_HealsEveryLivingSummon_NotPlayer()
        {
            var grove = Def("沃", new EffectDef(EffectKind.HealSummons, 20));
            var b = Battle(new[] { grove }, 300, new[] { Summon(0, 50), Summon(1, 90), Summon(2, 0) });
            Assert.That(BattleEngine.NeedsAllyTarget(grove), Is.False);
            b.Cast("沃");
            Assert.That(b.Summons[0].Hp, Is.EqualTo(70));
            Assert.That(b.Summons[1].Hp, Is.EqualTo(100), "不超上限");
            Assert.That(b.Summons[2].Alive, Is.False, "尸体不治");
            Assert.That(b.PlayerHp, Is.EqualTo(300), "玩家不在内");
        }

        [Test]
        public void HealSummons_PercentOfMax()
        {
            var grove = Def("荫", new EffectDef(EffectKind.HealSummons, 30, percentOfMax: true));
            var b = Battle(new[] { grove }, null, new[] { Summon(0, 10, 200), Summon(1, 10, 50) });
            b.Cast("荫");
            Assert.That(b.Summons[0].Hp, Is.EqualTo(70));
            Assert.That(b.Summons[1].Hp, Is.EqualTo(25));
        }

        [Test]
        public void ShieldSummons_ShieldsEveryLivingSummon_WithCap()
        {
            var grove = Def("荫", new EffectDef(EffectKind.ShieldSummons, 15));
            var b = Battle(new[] { grove }, null, new[] { Summon(0, 50), Summon(1, 10, 10), Summon(2, 0) });
            b.Cast("荫");
            Assert.That(b.Summons[0].Shield, Is.EqualTo(15));
            Assert.That(b.Summons[1].Shield, Is.EqualTo(10 * CombatCaps.ShieldPercentOfMaxHp / 100), "吃召唤物护盾上限");
            Assert.That(b.Summons[2].Shield, Is.EqualTo(0));
            Assert.That(b.PlayerShield, Is.EqualTo(0));
        }

        [Test]
        public void SummonStrike_EachLivingSummonHitsTargetOnce_AtPercent()
        {
            var strike = Def("刺", new EffectDef(EffectKind.SummonStrike, 50));
            var b = Battle(new[] { strike }, null,
                new[] { Summon(0, 50, attack: 40), Summon(1, 50, attack: 20), Summon(2, 0, attack: 100) },
                RebalanceFixture.Mob(), RebalanceFixture.Mob());
            Assert.That(BattleEngine.NeedsTarget(strike), Is.True, "打的是选定目标");
            Assert.That(b.Cast("刺", 1), Is.EqualTo(BattleError.None));
            Assert.That(100000 - b.Enemies[1].Hp, Is.EqualTo(20 + 10));
            Assert.That(b.Enemies[0].Hp, Is.EqualTo(100000), "只打目标");
        }

        // ---------------- 治疗转盾 ShieldFromHeal ----------------

        [Test]
        public void ShieldFromHeal_UsesActualHealedAmount_OverhealExcluded()
        {
            var ward = Def("护", new EffectDef(EffectKind.HealSelf, 100), new EffectDef(EffectKind.ShieldFromHeal, 50));
            var b = Battle(new[] { ward }, 450);
            b.Cast("护");
            Assert.That(b.PlayerHp, Is.EqualTo(500));
            Assert.That(b.PlayerShield, Is.EqualTo(25), "实际治疗 50 × 50%,溢出的 50 不算");
        }

        [Test]
        public void ShieldFromHeal_ResetsPerCast()
        {
            var heal = Def("愈", new EffectDef(EffectKind.HealSelf, 100));
            var ward = Def("护", new EffectDef(EffectKind.ShieldFromHeal, 50));
            var b = Battle(new[] { heal, ward }, 200);
            b.Cast("愈");
            b.Cast("护");
            Assert.That(b.PlayerShield, Is.EqualTo(0), "上一张字的治疗量不带进本张");
        }

        [Test]
        public void ShieldFromHeal_CountsHealAllSummonsToo()
        {
            var rain = Def("霖", new EffectDef(EffectKind.HealAll, 30), new EffectDef(EffectKind.ShieldFromHeal, 100));
            var b = Battle(new[] { rain }, 480, new[] { Summon(0, 50) });
            b.Cast("霖");
            Assert.That(b.PlayerShield, Is.EqualTo(20 + 30));
        }

        // ---------------- 净化计数与 Pick.Self ----------------

        private static void AddDebuffs(BattleEngine b, params StatusKind[] kinds)
        {
            foreach (var k in kinds)
                b.PlayerStatuses.Apply(new StatusEffect
                {
                    Kind = k, Polarity = StatusPolarity.Debuff, Magnitude = 1, TurnsLeft = 3, SourceId = k.ToString(),
                });
        }

        [Test]
        public void Cleanse_WithCount_RemovesOnlyFirstN()
        {
            var c = Def("涓", new EffectDef(EffectKind.Cleanse, 1));
            var b = Battle(new[] { c });
            AddDebuffs(b, StatusKind.Blind, StatusKind.Curse, StatusKind.Seal);
            b.Cast("涓");
            var left = b.PlayerStatuses.All.Where(s => s.Polarity == StatusPolarity.Debuff).Select(s => s.Kind).ToList();
            Assert.That(left.Count, Is.EqualTo(2));
            Assert.That(left.Contains(StatusKind.Blind), Is.False, "清的是第一个");
        }

        [Test]
        public void Cleanse_ZeroValue_StillClearsAll()
        {
            var c = Def("净", new EffectDef(EffectKind.Cleanse, 0));
            var b = Battle(new[] { c });
            AddDebuffs(b, StatusKind.Blind, StatusKind.Curse, StatusKind.Seal);
            b.Cast("净");
            Assert.That(b.PlayerStatuses.All.Any(s => s.Polarity == StatusPolarity.Debuff), Is.False);
        }

        [Test]
        public void Cleanse_PickSelf_OnAttackFace_TargetsPlayer_NoAllyTarget()
        {
            var c = new CharDef("涤", Element.Heart, effects: new[] { new EffectDef(EffectKind.Shield, 5) },
                attackEffects: new[]
                {
                    new EffectDef(EffectKind.DamageSingle, 10),
                    new EffectDef(EffectKind.Cleanse, 1, pick: EffectPick.Self),
                });
            Assert.That(BattleEngine.EffectNeedsAllyTarget(c.AttackEffects[1]), Is.False);
            Assert.That(BattleEngine.EffectNeedsAllyTarget(new EffectDef(EffectKind.Cleanse, 1)), Is.True, "缺省仍选友方");
            var b = Battle(new[] { c }, null, new[] { Summon(0, 50) });
            AddDebuffs(b, StatusKind.Blind, StatusKind.Curse);
            Assert.That(b.Cast("涤", 0, attackMode: true), Is.EqualTo(BattleError.None));
            Assert.That(b.PlayerStatuses.All.Count(s => s.Polarity == StatusPolarity.Debuff), Is.EqualTo(1));
        }

        // ---------------- 直接加泉 / 加厚 ----------------

        [Test]
        public void AddWellspringAndHeft_AddStacks_CappedByCapFor()
        {
            var w = Def("蓄", new EffectDef(EffectKind.AddWellspring, 8), new EffectDef(EffectKind.AddHeft, 8));
            var b = Battle(new[] { w });
            b.Cast("蓄");
            Assert.That(b.PlayerStatuses.TotalMagnitude(StatusKind.Wellspring), Is.EqualTo(8));
            Assert.That(b.PlayerStatuses.TotalMagnitude(StatusKind.Heft), Is.EqualTo(8));
            b.Cast("蓄");
            Assert.That(b.PlayerStatuses.TotalMagnitude(StatusKind.Wellspring), Is.EqualTo(10), "上限 10");
            Assert.That(b.PlayerStatuses.TotalMagnitude(StatusKind.Heft), Is.EqualTo(10));
        }

        // ---------------- 快照 ----------------

        [Test]
        public void NewStatuses_SurviveSnapshotRoundTrip()
        {
            var all = Def("全",
                new EffectDef(EffectKind.DamageCut, 20), new EffectDef(EffectKind.CounterBoost, 100),
                new EffectDef(EffectKind.Summon, 10, summonCount: 1, summonChar: "木"),
                new EffectDef(EffectKind.Endure, 0, pick: EffectPick.SummonedThisCast));
            var b = Battle(new[] { all });
            b.Cast("全");
            int slot = Array.FindIndex(b.Summons.ToArray(), s => s != null);
            var restored = BattleEngine.Restore(b.Capture(), RebalanceFixture.Graph(all), Config, null,
                new Dictionary<string, EnemyDef> { ["怔"] = RebalanceFixture.Mob() });
            Assert.That(restored.PlayerStatuses.Find(StatusKind.DamageCut).Magnitude, Is.EqualTo(20));
            Assert.That(restored.PlayerStatuses.Find(StatusKind.DamageCut).TurnsLeft, Is.EqualTo(1));
            Assert.That(restored.PlayerStatuses.Find(StatusKind.CounterBoost).Magnitude, Is.EqualTo(100));
            Assert.That(restored.Summons[slot].Statuses.Has(StatusKind.Endure), Is.True);
        }

        // ---------------- 选择器支持表与 ConfigLoader ----------------

        [Test]
        public void PickRules_SelfOnlyForCleanse_SummonedThisCastOnlyForEndure()
        {
            Assert.That(EffectPickRules.Allows(EffectKind.Cleanse, EffectPick.Self), Is.True);
            Assert.That(EffectPickRules.Allows(EffectKind.Endure, EffectPick.SummonedThisCast), Is.True);
            Assert.That(EffectPickRules.Allows(EffectKind.Freeze, EffectPick.Self), Is.False);
            Assert.That(EffectPickRules.Allows(EffectKind.Cleanse, EffectPick.All), Is.False);
            Assert.That(EffectPickRules.Allows(EffectKind.Endure, EffectPick.Self), Is.False);
            Assert.That(EffectPickRules.Allows(EffectKind.Freeze, EffectPick.Random), Is.True);
        }

        private const string Body = @"""effects"":[{""kind"":""Shield"",""value"":5}],
            ""attackEffects"":[{""kind"":""DamageSingle"",""value"":5}]";

        [Test]
        public void Config_AttackFaceTrait_CleansePickSelf_IsAccepted()
        {
            var g = ConfigLoader.LoadGraph(@"{""chars"":[{""id"":""甲"",""element"":""Water""," + Body + @",""traits"":[
                {""slot"":""Lv5"",""face"":""Attack"",""name"":""涤"",""effects"":[{""kind"":""Cleanse"",""value"":1,""pick"":""Self""}]}]}]}");
            Assert.That(g.Get("甲").Traits[0].Effects[0].Pick, Is.EqualTo(EffectPick.Self));
        }

        [Test]
        public void Config_AttackFaceTrait_PlainCleanse_StillRejected()
        {
            Assert.Throws<ConfigException>(() => ConfigLoader.LoadGraph(
                @"{""chars"":[{""id"":""甲"",""element"":""Water""," + Body + @",""traits"":[
                {""slot"":""Lv5"",""face"":""Attack"",""name"":""涤"",""effects"":[{""kind"":""Cleanse"",""value"":1}]}]}]}"));
        }

        [Test]
        public void Config_ParsesPercentOfMax_AndRejectsMisplacedPicks()
        {
            var g = ConfigLoader.LoadGraph(@"{""chars"":[{""id"":""甲"",""element"":""Wood"",""effects"":[
                {""kind"":""HealSummons"",""value"":30,""percentOfMax"":true},
                {""kind"":""Endure"",""value"":0,""pick"":""SummonedThisCast""}]}]}");
            Assert.That(g.Get("甲").Effects[0].PercentOfMax, Is.True);
            Assert.That(g.Get("甲").Effects[1].Pick, Is.EqualTo(EffectPick.SummonedThisCast));
            Assert.Throws<ConfigException>(() => ConfigLoader.LoadGraph(
                @"{""chars"":[{""id"":""甲"",""element"":""Wood"",""effects"":[{""kind"":""Freeze"",""value"":1,""pick"":""Self""}]}]}"));
            Assert.Throws<ConfigException>(() => ConfigLoader.LoadGraph(
                @"{""chars"":[{""id"":""甲"",""element"":""Wood"",""effects"":[{""kind"":""HealSelf"",""value"":1,""percentOfMax"":true}]}]}"));
        }
    }
}
