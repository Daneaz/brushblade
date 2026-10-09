using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using Brushblade.Data;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>Plan D2-水 Task 1:共用小扩展 —— 附录 E14(IsBoss / NotBoss)、E15(选择器 Column / AdjacentOne / HighestHp /
    /// SlowedByThisCast / AllAllies)、E16(名单类推迟)、E17(StallPush、带条件的 Augment)、E18(Slow extend)、E19(executeIf)、
    /// E20(治疗改形)、E21(WellspringFill)、E22(ScaleBasis Wellspring / Cleansed)、E23(Retaliate turns)、E24(HealSummons ofHeal)、
    /// E25(Seed whileSlowed)。
    ///
    /// 夹具口径同 MetalSharedExtTests:Element.Heart(生克 1.0×)、PlayerAttack = 100、无甲 10 万血靶子、敌人攻击 0。
    /// 四只靶子按 ColumnOrder {1,2,0,3} 落位:前排 0 → 列 1、1 → 列 2、2 → 列 0;后排 3 → 列 1(与 0 同列)。</summary>
    public class WaterSharedExtTests
    {
        private const int Hp = 100000;

        private static BattleConfig Config => new BattleConfig
            { PlayerMaxHp = RebalanceFixture.BaseMaxHp, PlayerAttack = 100, ApPerTurn = 20 };

        private static BattleEngine Battle(CharDef def, int level, params EnemyDef[] enemies) =>
            new(RebalanceFixture.Graph(def), Config, new[] { def.Id, def.Id, def.Id }, Array.Empty<string>(),
                enemies.Length > 0 ? enemies : new[] { RebalanceFixture.Mob() }, seed: 1,
                cardLevels: new Dictionary<string, int> { [def.Id] = level });

        private static EnemyDef[] FourMobs() => new[]
        {
            new EnemyDef("甲", Element.Heart, Hp, 0),
            new EnemyDef("乙", Element.Heart, Hp, 0),
            new EnemyDef("丙", Element.Heart, Hp, 0),
            new EnemyDef("丁", Element.Heart, Hp, 0, row: EnemyRow.Back),
        };

        private static CharDef Def(params EffectDef[] effects) => RebalanceFixture.Char("试", effects);

        private static TraitDef Trait(TraitSlot slot, TraitFace face, TraitForm form, params EffectDef[] effects) =>
            new(slot, face, form, null, "试" + (int)slot, effects);

        private static bool Has(BattleEngine b, int i, StatusKind kind) => b.Enemies[i].Statuses.Has(kind);

        private static void Slow(BattleEngine b, int i, int turns) =>
            b.Enemies[i].Statuses.Apply(new StatusEffect
                { Kind = StatusKind.SpeedModifier, Polarity = StatusPolarity.Debuff, Magnitude = -50, TurnsLeft = turns, SourceId = "外" });

        private static void Cast(BattleEngine b, int target = 0, bool attackMode = false) =>
            Assert.That(b.Cast("试", target, attackMode: attackMode), Is.EqualTo(BattleError.None));

        // ---------------- 枚举只追加 ----------------

        [Test]
        public void NewEnumValues_AppendedAtEnd()
        {
            Assert.That((int)DamageCondition.IsBoss, Is.EqualTo(16));
            Assert.That((int)DamageCondition.NotBoss, Is.EqualTo(17));
            Assert.That((int)EffectPick.Column, Is.EqualTo(12));
            Assert.That((int)EffectPick.AdjacentOne, Is.EqualTo(13));
            Assert.That((int)EffectPick.HighestHp, Is.EqualTo(14));
            Assert.That((int)EffectPick.SlowedByThisCast, Is.EqualTo(15));
            Assert.That((int)AugmentField.StallPush, Is.EqualTo(3));
        }

        // ---------------- E14:IsBoss / NotBoss ----------------

        [Test]
        public void Condition_NotBoss_SkipsBoss_IsBoss_Reverse()
        {
            foreach (var (cond, mobGets) in new[] { (DamageCondition.NotBoss, true), (DamageCondition.IsBoss, false) })
            {
                var b = Battle(Def(new EffectDef(EffectKind.Weaken, 50, turns: 1, pick: EffectPick.All, onlyIf: cond)), 1,
                    RebalanceFixture.Mob(), RebalanceFixture.Boss());
                Cast(b, -1);
                Assert.That(Has(b, 0, StatusKind.Curse), Is.EqualTo(mobGets), $"{cond}:杂兵");
                Assert.That(Has(b, 1, StatusKind.Curse), Is.EqualTo(!mobGets), $"{cond}:Boss");
            }
        }

        // ---------------- E15:选择器 ----------------

        [Test]
        public void Pick_Column_OtherEnemiesInPrimaryColumn()
        {
            var b = Battle(Def(new EffectDef(EffectKind.Weaken, 30, turns: 1, pick: EffectPick.Column)), 1, FourMobs());
            Assert.That(b.Enemies[3].Column, Is.EqualTo(b.Enemies[0].Column), "夹具:后排 3 与前排 0 同列");
            Cast(b, 0);
            Assert.That(Enumerable.Range(0, 4).Select(i => Has(b, i, StatusKind.Curse)),
                Is.EqualTo(new[] { false, false, false, true }), "只有同列的其余敌人,不含主目标");
            Assert.That(BattleEngine.EffectNeedsTarget(new EffectDef(EffectKind.Freeze, 1, pick: EffectPick.Column)), Is.True);
        }

        [Test]
        public void Pick_AdjacentOne_LeftFirst_SkipsUnfreezable()
        {
            var def = Def(new EffectDef(EffectKind.Freeze, 1), new EffectDef(EffectKind.Freeze, 2, pick: EffectPick.AdjacentOne));
            var b = Battle(def, 1, FourMobs());
            Assert.That(b.Enemies[2].ColumnEnd, Is.EqualTo(b.Enemies[0].Column), "夹具:2 在 0 的左边");
            Cast(b, 0);
            Assert.That(Enumerable.Range(0, 4).Select(i => Has(b, i, StatusKind.Freeze)),
                Is.EqualTo(new[] { true, false, true, false }), "先左");
            Assert.That(b.Enemies[2].Statuses.Find(StatusKind.Freeze).TurnsLeft, Is.EqualTo(2));

            var c = Battle(def, 1, FourMobs());
            c.Enemies[2].Statuses.Apply(new StatusEffect
                { Kind = StatusKind.FrostResist, Polarity = StatusPolarity.Buff, Magnitude = 1, TurnsLeft = 1 });
            Cast(c, 0);
            Assert.That(Enumerable.Range(0, 4).Select(i => Has(c, i, StatusKind.Freeze)),
                Is.EqualTo(new[] { true, true, false, false }), "左边霜抗中冻不上 → 改右");
            Assert.That(BattleEngine.EffectNeedsTarget(new EffectDef(EffectKind.Freeze, 1, pick: EffectPick.AdjacentOne)), Is.True);
        }

        [Test]
        public void Pick_HighestHp_TieTakesLowerIndex()
        {
            var b = Battle(Def(new EffectDef(EffectKind.Weaken, 30, turns: 1, pick: EffectPick.HighestHp)), 1,
                RebalanceFixture.Mob(hp: 100), RebalanceFixture.Mob(hp: 300), RebalanceFixture.Mob(hp: 300));
            Cast(b, -1);
            Assert.That(Enumerable.Range(0, 3).Select(i => Has(b, i, StatusKind.Curse)), Is.EqualTo(new[] { false, true, false }));
            Assert.That(BattleEngine.EffectNeedsTarget(new EffectDef(EffectKind.Freeze, 1, pick: EffectPick.HighestHp)), Is.False);
        }

        [Test]
        public void Pick_SlowedByThisCast_OnlyTargetsSlowedByThisCast_EvenWhenWrittenFirst()
        {
            // 名单类效果写在 Slow 之前也收得到(E16 推迟);别人上的减速不算
            var b = Battle(Def(new EffectDef(EffectKind.Weaken, 30, turns: 1, pick: EffectPick.SlowedByThisCast),
                new EffectDef(EffectKind.Slow, 1)), 1, FourMobs());
            Slow(b, 2, 3);
            Cast(b, 1);
            Assert.That(Enumerable.Range(0, 4).Select(i => Has(b, i, StatusKind.Curse)),
                Is.EqualTo(new[] { false, true, false, false }));
        }

        // ---------------- E16:名单类推迟 ----------------

        /// <summary>冰 · 冰封(Lv4 被动标记)+ 坚冰(Lv8 冻相邻):Lv8 新冻的那一名也吃到 Lv4 标记(§0-4)。</summary>
        [Test]
        public void RosterDeferral_Lv8NewlyFrozenTarget_AlsoGetsLv4Mark()
        {
            var def = new CharDef("试", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.HealSelf, 10) },
                attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 10), new EffectDef(EffectKind.Freeze, 2) },
                traits: new[]
                {
                    Trait(TraitSlot.Lv4, TraitFace.Attack, TraitForm.Passive,
                        new EffectDef(EffectKind.Vulnerable, 30, pick: EffectPick.FrozenByThisCast)),
                    Trait(TraitSlot.Lv8, TraitFace.Attack, TraitForm.Active,
                        new EffectDef(EffectKind.Freeze, 1, pick: EffectPick.AdjacentOne)),
                });
            var b = Battle(def, 8, FourMobs());
            Cast(b, 0, attackMode: true);
            Assert.That(Has(b, 2, StatusKind.Freeze), Is.True, "Lv8 冻上了左邻");
            foreach (var (i, turns) in new[] { (0, 2), (2, 1) })
            {
                var mark = b.Enemies[i].Statuses.Find(StatusKind.Vulnerable);
                Assert.That(mark, Is.Not.Null, $"敌人 {i} 吃到冰封标记");
                Assert.That(mark.TurnsLeft, Is.EqualTo(turns), "回合 = 该目标本次冻结回合");
            }
            Assert.That(Has(b, 1, StatusKind.Vulnerable), Is.False);
        }

        // ---------------- E17:StallPush / 带条件的 Augment ----------------

        [Test]
        public void AugmentStallPush_BossIceStallPushesFullMeter()
        {
            int Pushed(bool withAugment)
            {
                var effects = new List<EffectDef> { new EffectDef(EffectKind.Freeze, 1) };
                if (withAugment)
                    effects.Add(new EffectDef(EffectKind.Augment, 50, augmentKind: EffectKind.Freeze, augmentField: AugmentField.StallPush));
                var b = Battle(Def(effects.ToArray()), 1, RebalanceFixture.Boss());
                int before = b.Enemies[0].ActionMeter;
                Cast(b, 0);
                Assert.That(Has(b, 0, StatusKind.IceStall), Is.True);
                return before - b.Enemies[0].ActionMeter;
            }
            Assert.That(Pushed(false), Is.EqualTo(TurnScheduler.Threshold * BattleConfig.IceStallPushPercent / 100));
            Assert.That(Pushed(true), Is.EqualTo(TurnScheduler.Threshold), "50 + 50 = 满格");
        }

        [Test]
        public void ConditionalAugment_FreezeTurns_OnlyWhenTargetSlowedBeforeCast()
        {
            var def = Def(new EffectDef(EffectKind.Freeze, 2),
                new EffectDef(EffectKind.Augment, 1, augmentKind: EffectKind.Freeze, augmentField: AugmentField.Turns),
                new EffectDef(EffectKind.Augment, 1, augmentKind: EffectKind.Freeze, augmentField: AugmentField.Turns,
                    onlyIf: DamageCondition.Slowed));
            var plain = Battle(def, 1);
            Cast(plain, 0);
            Assert.That(plain.Enemies[0].Statuses.Find(StatusKind.Freeze).TurnsLeft, Is.EqualTo(3), "2 + 1");

            var slowed = Battle(def, 1);
            Slow(slowed, 0, 3);
            Cast(slowed, 0);
            var freeze = slowed.Enemies[0].Statuses.Find(StatusKind.Freeze);
            Assert.That(freeze.TurnsLeft, Is.EqualTo(4), "2 + 1 + 已减速 1");
            Assert.That(freeze.Magnitude, Is.EqualTo(4), "霜抗等长(R1)");
        }

        // ---------------- E18:Slow extend ----------------

        [Test]
        public void SlowExtend_OnlyExtendsExistingSlow()
        {
            var b = Battle(Def(new EffectDef(EffectKind.Slow, 1, pick: EffectPick.All, extend: true)), 1,
                RebalanceFixture.Mob(), RebalanceFixture.Mob());
            Slow(b, 0, 2);
            Cast(b, -1);
            Assert.That(b.Enemies[0].Statuses.Find(StatusKind.SpeedModifier).TurnsLeft, Is.EqualTo(3), "已减速 → 续 1");
            Assert.That(Has(b, 1, StatusKind.SpeedModifier), Is.False, "未减速 → 空转,不新挂");
        }

        // ---------------- E19:executeIf ----------------

        [Test]
        public void ExecuteIf_GatesExecuteOnPreCastCondition()
        {
            var def = Def(new EffectDef(EffectKind.DamageSingle, 10, executeBelowPercent: 25, executeKills: true,
                executeIf: DamageCondition.Frozen));
            foreach (bool frozen in new[] { false, true })
            {
                var b = Battle(def, 1, RebalanceFixture.Mob(hp: 1000));
                b.Enemies[0].Hp = 200;
                if (frozen)
                    b.Enemies[0].Statuses.Apply(new StatusEffect
                        { Kind = StatusKind.Freeze, Polarity = StatusPolarity.Debuff, Magnitude = 1, TurnsLeft = 1 });
                Cast(b, 0);
                Assert.That(b.Enemies[0].Alive, Is.EqualTo(!frozen), frozen ? "冻结中 → 斩杀" : "未冻结 → 不斩杀");
            }
        }

        [Test]
        public void ExecuteIf_CopiedByReshape()
        {
            var def = Def(new EffectDef(EffectKind.DamageSingle, 10),
                new EffectDef(EffectKind.Reshape, 0, executeBelowPercent: 25, executeKills: true, executeIf: DamageCondition.Frozen));
            var folded = TraitRules.CastEffects(def, CardFace.Feature, 1);
            Assert.That(folded[0].ExecuteIf, Is.EqualTo(DamageCondition.Frozen));
        }

        // ---------------- E25:Seed whileSlowed ----------------

        private static CharDef Drizzle(bool whileSlowed) => Def(
            new EffectDef(EffectKind.Seed, 30, turns: whileSlowed ? 0 : 3, pick: EffectPick.SlowedByThisCast, whileSlowed: whileSlowed),
            new EffectDef(EffectKind.Slow, 2));

        private static BattleEngine DrizzleBattle(bool whileSlowed)
        {
            var b = new BattleEngine(RebalanceFixture.Graph(Drizzle(whileSlowed)), Config, new[] { "试", "试", "试" },
                Array.Empty<string>(), new[] { RebalanceFixture.Mob() }, seed: 1, startingHp: 300);
            Cast(b, 0);
            return b;
        }

        [Test]
        public void SeedWhileSlowed_TurnsFollowSlow_AndSkipsWhenNotSlowed()
        {
            var b = DrizzleBattle(true);
            var seed = b.Enemies[0].Statuses.Find(StatusKind.Seed);
            Assert.That(seed.WhileSlowed, Is.True);
            Assert.That(seed.TurnsLeft, Is.EqualTo(2), "缺 turns = 施加时目标的减速剩余回合");

            b.Enemies[0].Statuses.Remove(StatusKind.SpeedModifier);
            b.EndTurn();
            Assert.That(b.PlayerHp, Is.EqualTo(300), "不在减速中 → 不回复");

            var plain = DrizzleBattle(false);
            plain.Enemies[0].Statuses.Remove(StatusKind.SpeedModifier);
            plain.EndTurn();
            Assert.That(plain.PlayerHp, Is.EqualTo(330), "对照:普通种照常回复");
        }

        [Test]
        public void SeedWhileSlowed_HealsWhileSlowed()
        {
            var b = DrizzleBattle(true);
            for (int i = 0; i < 3 && b.PlayerHp == 300; i++) b.EndTurn();
            Assert.That(b.PlayerHp, Is.EqualTo(330), "减速中的敌人行动 → 回复");
        }

        [Test]
        public void StatusEffect_Clone_CopiesWhileSlowed()
        {
            var s = new StatusEffect { Kind = StatusKind.Seed, Magnitude = 30, WhileSlowed = true };
            Assert.That(s.Clone().WhileSlowed, Is.True);
        }

        [Test]
        public void SeedWhileSlowed_SurvivesRealSaveFile()
        {
            var def = Drizzle(true);
            var graph = RebalanceFixture.Graph(def);
            var enemy = RebalanceFixture.Mob(attack: 0);
            var runConfig = new RunConfig { Encounters = new[] { new[] { enemy }, new[] { enemy } }, RewardPool = new[] { "试" } };
            var levels = new Dictionary<string, int> { ["试"] = 1 };
            var run = new RunEngine(graph, runConfig, Config, new[] { "试", "试" }, Array.Empty<string>(), 3,
                startingInk: 50, perFloorNormalShield: 2, cardLevels: levels);
            Assert.That(run.Battle.Cast("试", 0), Is.EqualTo(BattleError.None));
            var meta = new MetaState
            {
                EndlessV2 = new EndlessSaveState
                {
                    Depth = 3, Seed = 999,
                    InProgress = new InProgressRun { FromDepth = 1, FirstTowerSegment = true, Run = run.Capture() },
                },
            };
            var reloaded = SaveSerializer.FromJson(SaveSerializer.ToJson(meta));
            var restored = RunEngine.Restore(reloaded.EndlessV2.InProgress.Run, graph, runConfig, Config, levels,
                startingInk: 50, perFloorNormalShield: 2);
            var seed = restored.Battle.Enemies[0].Statuses.Find(StatusKind.Seed);
            Assert.That(seed, Is.Not.Null);
            Assert.That(seed.WhileSlowed, Is.True, "WhileSlowed 进存档 JSON 往返");
        }

        // ================= 我方侧(E15 AllAllies / E20 / E21 / E22 / E23 / E24)=================

        private static SummonSnapshot Sapling(int slot, int hp, int maxHp = 100) => new()
        {
            Slot = slot, Char = "木", Element = Element.Wood, Hp = hp, MaxHp = maxHp, Attack = 0, Speed = 100,
        };

        private static BattleEngine AllyBattle(CharDef def, int? hp = null, IReadOnlyList<SummonSnapshot> summons = null,
            BattleConfig config = null, int healAccum = 0) =>
            new(RebalanceFixture.Graph(def), config ?? Config, new[] { "试", "试", "试" }, Array.Empty<string>(),
                new[] { RebalanceFixture.Mob() }, seed: 1, startingHp: hp, startingSummons: summons, startingHealAccum: healAccum);

        private static int Wellspring(BattleEngine b) => b.PlayerStatuses.TotalMagnitude(StatusKind.Wellspring);

        private static void SetWellspring(BattleEngine b, int stacks) =>
            b.PlayerStatuses.Apply(new StatusEffect
                { Kind = StatusKind.Wellspring, Polarity = StatusPolarity.Buff, Magnitude = stacks, TurnsLeft = -1 });

        private static StatusEffect Curse(string source) => new()
            { Kind = StatusKind.Curse, Polarity = StatusPolarity.Debuff, Magnitude = 10, TurnsLeft = 3, SourceId = source };

        [Test]
        public void Cleanse_AllAllies_CleansPlayerAndEverySummon()
        {
            Assert.That(EffectPickRules.Allows(EffectKind.Cleanse, EffectPick.AllAllies), Is.True);
            Assert.That(EffectPickRules.Allows(EffectKind.Weaken, EffectPick.AllAllies), Is.False);
            var b = AllyBattle(Def(new EffectDef(EffectKind.Cleanse, 0, pick: EffectPick.AllAllies)),
                summons: new[] { Sapling(0, 50), Sapling(1, 50) });
            b.PlayerStatuses.Apply(Curse("甲"));
            b.Summons[0].Statuses.Apply(Curse("乙"));
            b.Summons[1].Statuses.Apply(Curse("丙"));
            Assert.That(b.Cast("试", -1, allySlot: 0), Is.EqualTo(BattleError.None));
            Assert.That(b.PlayerStatuses.Has(StatusKind.Curse), Is.False);
            Assert.That(b.Summons[0].Statuses.Has(StatusKind.Curse), Is.False);
            Assert.That(b.Summons[1].Statuses.Has(StatusKind.Curse), Is.False);
        }

        // ---- E20 治疗改形 ----

        /// <summary>海纳百川 / 细雨:HealSelf 100 + Reshape shape All(shapePercent)+ 润泽(HoT)。</summary>
        private static CharDef HealAll(int percent, bool hot = false)
        {
            var list = new List<EffectDef> { new EffectDef(EffectKind.HealSelf, 100) };
            if (hot) list.Add(new EffectDef(EffectKind.HealOverTime, 10, turns: 2));
            list.Add(new EffectDef(EffectKind.Reshape, 0, shape: TargetArea.All, shapePercent: percent));
            return Def(list.ToArray());
        }

        [Test]
        public void HealReshapeAll_EveryAllyHealedByShare()
        {
            var b = AllyBattle(HealAll(50), hp: 200, summons: new[] { Sapling(0, 10), Sapling(1, 10) });
            Assert.That(b.Cast("试", -1, allySlot: 1), Is.EqualTo(BattleError.None));
            Assert.That(b.PlayerHp, Is.EqualTo(250), "各 50%(含落点本人)");
            Assert.That(b.Summons[0].Hp, Is.EqualTo(60));
            Assert.That(b.Summons[1].Hp, Is.EqualTo(60));
        }

        [Test]
        public void HealReshapeAll_WellspringGainedOnlyOnce()
        {
            // 300 名义值 / 100 一层:单份攒 3 层;按人头攒会是 9 层
            CharDef Make(bool all) => all
                ? Def(new EffectDef(EffectKind.HealSelf, 300), new EffectDef(EffectKind.Reshape, 0, shape: TargetArea.All))
                : Def(new EffectDef(EffectKind.HealSelf, 300));
            var single = AllyBattle(Make(false), hp: 100, summons: new[] { Sapling(0, 10), Sapling(1, 10) });
            single.Cast("试", -1);
            var all = AllyBattle(Make(true), hp: 100, summons: new[] { Sapling(0, 10), Sapling(1, 10) });
            all.Cast("试", -1);
            Assert.That(Wellspring(single), Is.EqualTo(3));
            Assert.That(Wellspring(all), Is.EqualTo(Wellspring(single)), "泉只攒一份名义值");
        }

        [Test]
        public void HealReshapeAll_HotStillOnlyOnLandingSlot()
        {
            var b = AllyBattle(HealAll(50, hot: true), hp: 200, summons: new[] { Sapling(0, 10), Sapling(1, 10) });
            Assert.That(b.Cast("试", -1, allySlot: 1), Is.EqualTo(BattleError.None));
            Assert.That(b.Summons[0].Hp, Is.EqualTo(60), "槽 0 只吃改形那一份");
            Assert.That(b.Summons[1].Hp, Is.EqualTo(70), "落点多吃润泽首跳 10");
            Assert.That(b.PlayerHp, Is.EqualTo(250));
            var hot = b.PlayerStatuses.Find(StatusKind.HealOverTime);
            Assert.That((hot.TargetSlot, hot.TargetAll), Is.EqualTo((1, false)), "润泽只给落点");
        }

        [Test]
        public void HealReshapeAll_OverflowSettledPerUnit()
        {
            // 全员满血 + 溢流 100%:三份溢出各打一下,唯一的敌人吃 3 × 100
            var config = new BattleConfig
                { PlayerMaxHp = RebalanceFixture.BaseMaxHp, PlayerAttack = 100, ApPerTurn = 20, OverhealDamagePercent = 100 };
            var b = AllyBattle(HealAll(100), summons: new[] { Sapling(0, 100), Sapling(1, 100) }, config: config);
            int before = b.Enemies[0].Hp;
            b.Cast("试", -1);
            Assert.That(before - b.Enemies[0].Hp, Is.EqualTo(300));
        }

        [Test]
        public void ReshapeWithoutAll_DoesNotTouchHealSelf()
        {
            var def = Def(new EffectDef(EffectKind.HealSelf, 100), new EffectDef(EffectKind.Reshape, 0, shapePercent: 50));
            Assert.That(TraitRules.CastEffects(def, CardFace.Feature, 1)[0].Shape, Is.EqualTo(TargetArea.Single));
        }

        // ---- E21 WellspringFill ----

        [Test]
        public void WellspringFill_SetsToCap()
        {
            var b = AllyBattle(Def(new EffectDef(EffectKind.AddWellspring, 0, fill: true)));
            SetWellspring(b, 2);
            b.Cast("试", -1);
            Assert.That(Wellspring(b), Is.EqualTo(CombatCaps.WellspringStacks));
        }

        // ---- E22 ScaleBasis Wellspring / Cleansed ----

        [Test]
        public void AmplifyPerWellspring_UsesPreCastStacks()
        {
            // 伤害在前、加泉在后:+2 泉不算进本次(出字前快照,R3)
            CharDef Make(bool amp) => Def(amp
                ? new[] { new EffectDef(EffectKind.DamageSingle, 100),
                    new EffectDef(EffectKind.Amplify, 10, scaleBy: ScaleBasis.Wellspring), new EffectDef(EffectKind.AddWellspring, 2) }
                : new[] { new EffectDef(EffectKind.DamageSingle, 100), new EffectDef(EffectKind.AddWellspring, 2) });
            int Dealt(bool amp)
            {
                var b = AllyBattle(Make(amp));
                SetWellspring(b, 5);
                int before = b.Enemies[0].Hp;
                Cast(b, 0);
                return before - b.Enemies[0].Hp;
            }
            Assert.That(Dealt(true), Is.EqualTo(Dealt(false) * 150 / 100), "5 层 × 10% = +50%");
        }

        [Test]
        public void HealPerCleansed_ScalesByRemovedCount()
        {
            var def = Def(new EffectDef(EffectKind.Cleanse, 0), new EffectDef(EffectKind.HealSelf, 50, scaleBy: ScaleBasis.Cleansed));
            int Healed(int debuffs)
            {
                var b = AllyBattle(def, hp: 100);
                for (int i = 0; i < debuffs; i++) b.PlayerStatuses.Apply(Curse("源" + i));
                b.Cast("试", -1);
                return b.PlayerHp - 100;
            }
            Assert.That(Healed(0), Is.EqualTo(0), "没清掉 = 不回复");
            Assert.That(Healed(1), Is.EqualTo(50));
            Assert.That(Healed(2), Is.EqualTo(100));
        }

        // ---- E23 Retaliate turns ----

        [Test]
        public void Retaliate_Turns_DefaultOneOtherwiseAsWritten()
        {
            foreach (var (turns, expected) in new[] { (0, 1), (2, 2) })
            {
                var b = AllyBattle(Def(new EffectDef(EffectKind.Retaliate, 0, turns: turns,
                    perHit: new[] { new EffectDef(EffectKind.Slow, 1) })));
                b.Cast("试", -1);
                Assert.That(b.PlayerStatuses.Find(StatusKind.Retaliate).TurnsLeft, Is.EqualTo(expected));
            }
        }

        // ---- E24 HealSummons ofHeal ----

        [Test]
        public void HealSummonsOfHeal_UsesCastNominalHeal_NoWellspringAmp()
        {
            // 泉 4 层:HealSelf 100 → 名义 100 × 1.2 = 120;沐恩 50% = 60,不再过泉放大
            var def = Def(new EffectDef(EffectKind.HealSelf, 100), new EffectDef(EffectKind.HealSummons, 50, ofHeal: true));
            var b = AllyBattle(def, hp: 100, summons: new[] { Sapling(0, 10, 200) });
            SetWellspring(b, 4);
            b.Cast("试", -1);
            Assert.That(b.Summons[0].Hp, Is.EqualTo(10 + 60));
        }

        // ---------------- 字表加载(敌方侧字段)----------------

        private static RecipeGraph Load(string traitEffects, string face = "Attack") => ConfigLoader.LoadGraph(
            @"{""chars"":[{""id"":""淼"",""element"":""Water"",""effects"":[{""kind"":""HealSelf"",""value"":10}],
              ""attackEffects"":[{""kind"":""DamageSingle"",""value"":10},{""kind"":""Freeze"",""value"":1}],
              ""traits"":[{""slot"":""Lv8"",""face"":""" + face + @""",""form"":""Active"",""name"":""附"",""effects"":[" + traitEffects + "]}]}]}");

        private static EffectDef LoadOne(string effect, string face = "Attack") => Load(effect, face).Get("淼").Traits[0].Effects[0];

        [Test]
        public void Loader_ReadsEnemySideFields()
        {
            Assert.That(LoadOne(@"{""kind"":""Slow"",""value"":1,""extend"":true}").Extend, Is.True);
            Assert.That(LoadOne(@"{""kind"":""Seed"",""value"":30,""pick"":""SlowedByThisCast"",""whileSlowed"":true}").WhileSlowed, Is.True);
            Assert.That(LoadOne(@"{""kind"":""Reshape"",""value"":0,""executeBelowPercent"":25,""executeKills"":true,""executeIf"":""Frozen""}").ExecuteIf,
                Is.EqualTo(DamageCondition.Frozen));
            Assert.That(LoadOne(@"{""kind"":""Weaken"",""value"":50,""turns"":1,""onlyIf"":""NotBoss""}").OnlyIf, Is.EqualTo(DamageCondition.NotBoss));
            Assert.That(LoadOne(@"{""kind"":""Freeze"",""value"":2,""pick"":""AdjacentOne""}").Pick, Is.EqualTo(EffectPick.AdjacentOne));
            var aug = LoadOne(@"{""kind"":""Augment"",""value"":1,""augmentKind"":""Freeze"",""augmentField"":""Turns"",""onlyIf"":""Slowed""}");
            Assert.That(aug.OnlyIf, Is.EqualTo(DamageCondition.Slowed));
            Assert.That(LoadOne(@"{""kind"":""Augment"",""value"":50,""augmentKind"":""Freeze"",""augmentField"":""StallPush""}").AugmentField,
                Is.EqualTo(AugmentField.StallPush));
        }

        [TestCase(@"{""kind"":""Freeze"",""value"":1,""extend"":true}")]
        [TestCase(@"{""kind"":""Weaken"",""value"":30,""turns"":1,""whileSlowed"":true}")]
        [TestCase(@"{""kind"":""DamageSingle"",""value"":10,""executeIf"":""Frozen""}")]
        [TestCase(@"{""kind"":""Augment"",""value"":50,""augmentKind"":""Slow"",""augmentField"":""StallPush""}")]
        [TestCase(@"{""kind"":""Augment"",""value"":1,""augmentKind"":""Slow"",""augmentField"":""Turns"",""onlyIf"":""Slowed""}")]
        public void Loader_RejectsMisplacedEnemySideFields(string effect)
        {
            Assert.Throws<ConfigException>(() => Load(effect));
        }

        [Test]
        public void Loader_ReadsAllySideFields()
        {
            Assert.That(LoadOne(@"{""kind"":""HealSummons"",""value"":50,""ofHeal"":true}", "Feature").OfHeal, Is.True);
            Assert.That(LoadOne(@"{""kind"":""AddWellspring"",""value"":0,""fill"":true}", "Feature").Fill, Is.True);
            Assert.That(LoadOne(@"{""kind"":""Amplify"",""value"":10,""scaleBy"":""Wellspring""}").ScaleBy, Is.EqualTo(ScaleBasis.Wellspring));
            Assert.That(LoadOne(@"{""kind"":""HealSelf"",""value"":50,""scaleBy"":""Cleansed""}", "Feature").ScaleBy, Is.EqualTo(ScaleBasis.Cleansed));
            Assert.That(LoadOne(@"{""kind"":""Cleanse"",""value"":0,""pick"":""AllAllies""}", "Feature").Pick, Is.EqualTo(EffectPick.AllAllies));
            Assert.That(LoadOne(@"{""kind"":""Retaliate"",""value"":0,""turns"":2,""perHit"":[{""kind"":""Slow"",""value"":1}]}", "Feature").Turns,
                Is.EqualTo(2));
        }

        [TestCase(@"{""kind"":""HealSelf"",""value"":50,""ofHeal"":true}")]
        [TestCase(@"{""kind"":""HealSummons"",""value"":50,""ofHeal"":true,""percentOfMax"":true}")]
        [TestCase(@"{""kind"":""Shield"",""value"":10,""fill"":true}")]
        [TestCase(@"{""kind"":""HealSelf"",""value"":10,""scaleBy"":""Wellspring""}")]
        [TestCase(@"{""kind"":""Amplify"",""value"":10,""scaleBy"":""Cleansed""}")]
        [TestCase(@"{""kind"":""Weaken"",""value"":10,""turns"":1,""pick"":""AllAllies""}")]
        public void Loader_RejectsMisplacedAllySideFields(string effect)
        {
            Assert.Throws<ConfigException>(() => Load(effect, "Feature"));
        }
    }
}
