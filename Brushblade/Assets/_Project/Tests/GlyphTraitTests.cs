using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using Brushblade.Data;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>拆字 / 成字特性与部件印记(D2-0 Task 7,spec §9 / §11.8,裁定 E9 / E10)。
    /// 夹具自造字与配方:Element.Heart(生克 1.0×)、PlayerAttack = 100、打不死的无攻靶子。</summary>
    public class GlyphTraitTests
    {
        private static BattleConfig Config => new() { PlayerMaxHp = RebalanceFixture.BaseMaxHp, PlayerAttack = 100 };

        private static CharDef Part(string id, params EffectDef[] effects) =>
            new(id, Element.Heart, effects: effects, isComponent: true);

        private static TraitDef Glyph(TraitTrigger trigger, string partChar, int partCount, params EffectDef[] effects) =>
            new(TraitSlot.Lv4, TraitFace.Both, TraitForm.Passive, null, "字形", effects, trigger, partChar, partCount);

        private static CharDef Twin(string id, string a, string b, TraitDef trait) => new(id, Element.Heart,
            recipe: new[] { a, b },
            effects: new[] { new EffectDef(EffectKind.Shield, 10) },
            attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 10) },
            traits: trait == null ? null : new[] { trait });

        // 部件:火本体单体 10 + 灼 1;山本体单体 10;金 / 木本体护盾
        private static readonly CharDef Fire = Part("火", new EffectDef(EffectKind.DamageSingle, 10), new EffectDef(EffectKind.BurnSingle, 1));
        private static readonly CharDef Mountain = Part("山", new EffectDef(EffectKind.DamageSingle, 10));
        private static readonly CharDef Metal = Part("金", new EffectDef(EffectKind.Shield, 5));
        private static readonly CharDef Wood = Part("木", new EffectDef(EffectKind.Shield, 5));

        /// <summary>鍂:拆字 → 战意 +2(双金)。</summary>
        private static CharDef Jin(TraitDef trait = null) =>
            Twin("鍂", "金", "金", trait ?? Glyph(TraitTrigger.OnDismantle, null, 0, new EffectDef(EffectKind.Morale, 2)));

        /// <summary>炎:拆字印记 → 拆出的 2 个火本回合出手各附灼 2(双焰)。</summary>
        private static CharDef Yan() =>
            Twin("炎", "火", "火", Glyph(TraitTrigger.OnDismantle, "火", 2, new EffectDef(EffectKind.BurnSingle, 2)));

        /// <summary>灿:拆字印记 → 山本回合出手改为全体并全体灼 1(火山)。</summary>
        private static CharDef Can() =>
            Twin("灿", "火", "山", Glyph(TraitTrigger.OnDismantle, "山", 1,
                new EffectDef(EffectKind.Reshape, 0, shape: TargetArea.All), new EffectDef(EffectKind.BurnAll, 1)));

        /// <summary>茂:成字 → 木灵得盾。</summary>
        private static CharDef Mao() =>
            Twin("茂", "木", "木", Glyph(TraitTrigger.OnCompose, null, 0, new EffectDef(EffectKind.ShieldSummons, 20)));

        private static CharDef[] AllDefs(params CharDef[] chars) =>
            new[] { Fire, Mountain, Metal, Wood }.Concat(chars).ToArray();

        private static EnemyDef[] Mobs(int n) => Enumerable.Range(0, n).Select(_ => RebalanceFixture.Mob()).ToArray();

        private static BattleEngine Battle(CharDef[] chars, int level, string[] library, string[] pool,
            int enemies = 1, IReadOnlyList<SummonSnapshot> summons = null) =>
            new(RebalanceFixture.Graph(AllDefs(chars)), Config, library, pool, Mobs(enemies), seed: 1,
                cardLevels: chars.ToDictionary(d => d.Id, _ => level), startingSummons: summons);

        private static int Morale(BattleEngine b) => b.PlayerStatuses.TotalMagnitude(StatusKind.Morale);
        private static int Burn(BattleEngine b, int i = 0) => b.Enemies[i].Statuses.TotalMagnitude(StatusKind.Burn);

        // ---------------- 拆字即时 ----------------

        [Test]
        public void Dismantle_GlyphTrait_GrantsMorale_OncePerBattle()
        {
            var b = Battle(new[] { Jin() }, 4, new[] { "鍂" }, Array.Empty<string>());
            Assert.That(b.Dismantle("鍂"), Is.EqualTo(BattleError.None));
            Assert.That(Morale(b), Is.EqualTo(2), "拆鍂:战意 +2");

            Assert.That(b.Compose("鍂"), Is.EqualTo(BattleError.None));
            Assert.That(b.Dismantle("鍂"), Is.EqualTo(BattleError.None));
            Assert.That(Morale(b), Is.EqualTo(2), "每场 1 次(E9):合回再拆不再给");

            var fresh = Battle(new[] { Jin() }, 4, new[] { "鍂" }, Array.Empty<string>());
            fresh.Dismantle("鍂");
            Assert.That(Morale(fresh), Is.EqualTo(2), "新一场可再给一次");
        }

        [Test]
        public void Dismantle_GlyphTrait_LevelTooLow_DoesNotTrigger()
        {
            var b = Battle(new[] { Jin() }, 3, new[] { "鍂" }, Array.Empty<string>());
            b.Dismantle("鍂");
            Assert.That(Morale(b), Is.EqualTo(0), "Lv4 槽位在 3 级未解锁");
        }

        [Test]
        public void Compose_DoesNotFireDismantleTrait()
        {
            var b = Battle(new[] { Jin() }, 4, Array.Empty<string>(), new[] { "金", "金" });
            b.Compose("鍂");
            Assert.That(Morale(b), Is.EqualTo(0), "拆字特性只在拆时触发");
        }

        [Test]
        public void Dismantle_GlyphTrait_KillsLastEnemy_PhaseWon()
        {
            // I1:字形即时伤害打死最后一只怪 → 拆字收尾必须判胜(DrainReactions → RefreshSummonAura → CheckWin)
            var jin = Jin(Glyph(TraitTrigger.OnDismantle, null, 0,
                new EffectDef(EffectKind.DamageSingle, 99999, shape: TargetArea.All)));
            var b = new BattleEngine(RebalanceFixture.Graph(AllDefs(jin)), Config, new[] { "鍂" }, Array.Empty<string>(),
                new[] { RebalanceFixture.Mob(hp: 50) }, seed: 1, cardLevels: new Dictionary<string, int> { ["鍂"] = 4 });
            Assert.That(b.Dismantle("鍂"), Is.EqualTo(BattleError.None));
            Assert.That(b.Phase, Is.EqualTo(BattlePhase.Won));
        }

        [Test]
        public void Dismantle_GlyphTrait_BurnSettleKillsLastEnemy_PhaseWon()
        {
            var jin = Jin(Glyph(TraitTrigger.OnDismantle, null, 0,
                new EffectDef(EffectKind.BurnAll, 99999),
                new EffectDef(EffectKind.BurnSettleNow, 0, pick: EffectPick.All)));
            var b = new BattleEngine(RebalanceFixture.Graph(AllDefs(jin)), Config, new[] { "鍂" }, Array.Empty<string>(),
                new[] { RebalanceFixture.Mob(hp: 50) }, seed: 1, cardLevels: new Dictionary<string, int> { ["鍂"] = 4 });
            Assert.That(b.Dismantle("鍂"), Is.EqualTo(BattleError.None));
            Assert.That(b.Phase, Is.EqualTo(BattlePhase.Won));
        }

        // ---------------- 成字即时 ----------------

        private static SummonSnapshot Spirit(int slot) => new()
        {
            Slot = slot, Char = "木", Element = Element.Wood, Hp = 50, MaxHp = 100, Attack = 0, Speed = 100,
        };

        [Test]
        public void Compose_GlyphTrait_ShieldsSummons()
        {
            var b = Battle(new[] { Mao() }, 4, Array.Empty<string>(), new[] { "木", "木" }, summons: new[] { Spirit(0) });
            Assert.That(b.Compose("茂"), Is.EqualTo(BattleError.None));
            Assert.That(b.Summons[0].Shield, Is.GreaterThan(0), "合成茂:木灵得盾");
        }

        [Test]
        public void Compose_GlyphTrait_LevelTooLow_DoesNotTrigger()
        {
            var b = Battle(new[] { Mao() }, 3, Array.Empty<string>(), new[] { "木", "木" }, summons: new[] { Spirit(0) });
            b.Compose("茂");
            Assert.That(b.Summons[0].Shield, Is.EqualTo(0));
        }

        // ---------------- 部件印记 ----------------

        private static int BurnDeltaOfCast(BattleEngine b, string part)
        {
            int before = Burn(b);
            Assert.That(b.Cast(part, 0), Is.EqualTo(BattleError.None));
            return Burn(b) - before;
        }

        [Test]
        public void Mark_TwoFires_EachAddBurn2_ThirdDoesNot()
        {
            // 池里先放一个火:拆炎后池 = 火 ×3。印记次数 2,第三次出火不再加
            var b = Battle(new[] { Yan() }, 4, new[] { "炎" }, new[] { "火" });
            Assert.That(b.Dismantle("炎"), Is.EqualTo(BattleError.None));
            Assert.That(BurnDeltaOfCast(b, "火"), Is.EqualTo(1 + 2), "火本体灼 1 + 印记灼 2");
            Assert.That(BurnDeltaOfCast(b, "火"), Is.EqualTo(1 + 2), "第二个火同样");
            Assert.That(BurnDeltaOfCast(b, "火"), Is.EqualTo(1), "印记用完");
        }

        [Test]
        public void Mark_ClearedAtNextTurn()
        {
            var b = Battle(new[] { Yan() }, 4, new[] { "炎" }, Array.Empty<string>());
            b.Dismantle("炎");
            Assert.That(BurnDeltaOfCast(b, "火"), Is.EqualTo(3));
            b.EndTurn();
            Assert.That(b.Phase, Is.EqualTo(BattlePhase.PlayerTurn));
            Assert.That(BurnDeltaOfCast(b, "火"), Is.EqualTo(1), "回合开始清空印记(E10)");
        }

        [Test]
        public void Mark_LevelTooLow_NoMark()
        {
            var b = Battle(new[] { Yan() }, 3, new[] { "炎" }, Array.Empty<string>());
            b.Dismantle("炎");
            Assert.That(BurnDeltaOfCast(b, "火"), Is.EqualTo(1));
        }

        [Test]
        public void Mark_Reshape_MakesPartHitAll_AndBurnAll()
        {
            var b = Battle(new[] { Can() }, 4, new[] { "灿" }, Array.Empty<string>(), enemies: 3);
            b.Dismantle("灿");
            Assert.That(b.Cast("山", -1), Is.EqualTo(BattleError.None), "改为全体后不需要选目标");
            for (int i = 0; i < 3; i++)
            {
                Assert.That(b.Enemies[i].Hp, Is.LessThan(b.Enemies[i].MaxHp), $"敌人 {i} 吃到全体伤害");
                Assert.That(Burn(b, i), Is.EqualTo(1), $"敌人 {i} 全体灼 1");
            }
        }

        [Test]
        public void Mark_Reshape_OnlyOnce()
        {
            var b = Battle(new[] { Can() }, 4, new[] { "灿" }, new[] { "山" }, enemies: 2);
            b.Dismantle("灿");
            Assert.That(b.Cast("山", -1), Is.EqualTo(BattleError.None));
            Assert.That(b.Cast("山", -1), Is.EqualTo(BattleError.InvalidTarget), "印记次数 1:第二个山回到单体,两名敌人要选目标");
            int hp1 = b.Enemies[1].Hp;
            Assert.That(b.Cast("山", 0), Is.EqualTo(BattleError.None));
            Assert.That(b.Enemies[1].Hp, Is.EqualTo(hp1), "第二个山只打选中的那名");
        }

        // ---------------- 快照 ----------------

        [Test]
        public void Snapshot_RoundTrip_KeepsMarksAndGlyphUses()
        {
            var defs = new[] { Yan(), Jin() };
            var b = Battle(defs, 4, new[] { "炎", "鍂" }, Array.Empty<string>());
            b.Dismantle("炎");
            b.Dismantle("鍂");

            var meta = new MetaState
            {
                EndlessV2 = new EndlessSaveState
                {
                    Depth = 1, Seed = 1,
                    InProgress = new InProgressRun
                    {
                        FromDepth = 1, FirstTowerSegment = true,
                        Run = new RunSnapshot { Battle = b.Capture() },
                    },
                },
            };
            var snap = SaveSerializer.FromJson(SaveSerializer.ToJson(meta)).EndlessV2.InProgress.Run.Battle;
            var restored = BattleEngine.Restore(snap, RebalanceFixture.Graph(AllDefs(defs)), Config,
                defs.ToDictionary(d => d.Id, _ => 4),
                new Dictionary<string, EnemyDef> { [RebalanceFixture.Mob().Id] = RebalanceFixture.Mob() });

            Assert.That(BurnDeltaOfCast(restored, "火"), Is.EqualTo(3), "读档后印记仍在");
            Assert.That(BurnDeltaOfCast(restored, "火"), Is.EqualTo(3), "剩余次数也在");
            Assert.That(restored.Compose("鍂"), Is.EqualTo(BattleError.None));
            Assert.That(restored.Dismantle("鍂"), Is.EqualTo(BattleError.None));
            Assert.That(Morale(restored), Is.EqualTo(2), "字形次数进快照:读档后不能再拿一次");
        }

        // ---------------- 恒等 ----------------

        [Test]
        public void NoGlyphData_DismantleCompose_DoNotTouchAnyRandom()
        {
            var plain = Twin("鍂", "金", "金", null);
            var b = Battle(new[] { plain }, 4, new[] { "鍂" }, Array.Empty<string>());
            var before = b.Capture();
            b.Dismantle("鍂");
            b.Compose("鍂");
            var after = b.Capture();
            Assert.That(after.RandomState, Is.EqualTo(before.RandomState));
            Assert.That(after.TargetRandomState, Is.EqualTo(before.TargetRandomState));
            Assert.That(after.TraitRandomState, Is.EqualTo(before.TraitRandomState));
            Assert.That(after.TraitUsesThisBattle.Count, Is.EqualTo(0));
            Assert.That(after.PartMarks.Count, Is.EqualTo(0));
        }

        // ---------------- E3 Task 1:GlyphTrait 事件 + 拆合清事件 ----------------

        [Test]
        public void Dismantle_GlyphTrait_EmitsEvent_AndClearsStaleCastEvents()
        {
            var jin = Jin();
            var b = Battle(new[] { jin }, 4, new[] { "鍂" }, new[] { "金" });
            Assert.That(b.Cast("金"), Is.EqualTo(BattleError.None));
            Assert.That(b.LastEvents.Any(e => e.Kind == BattleEventKind.Shield), Is.True, "前提:出字留下了 Shield 事件");
            Assert.That(b.Dismantle("鍂"), Is.EqualTo(BattleError.None));
            Assert.That(b.LastEvents.Any(e => e.Kind == BattleEventKind.Shield), Is.False, "拆字进门清掉上一次出字的事件");
            var first = b.LastEvents[0];
            Assert.That((first.Kind, first.TargetIndex, first.Amount, first.SecondIndex),
                Is.EqualTo((BattleEventKind.GlyphTrait, -1, 0, (int)TraitTrigger.OnDismantle)), "Amount = 特性在 def.Traits 的下标");
            Assert.That(b.LastEvents.Count(e => e.Kind == BattleEventKind.GlyphTrait), Is.EqualTo(1));

            Assert.That(b.Compose("鍂"), Is.EqualTo(BattleError.None));
            Assert.That(b.LastEvents, Is.Empty, "合字(无成字特性)进门清事件,什么都不发");
            Assert.That(b.Dismantle("鍂"), Is.EqualTo(BattleError.None));
            Assert.That(b.LastEvents.Any(e => e.Kind == BattleEventKind.GlyphTrait), Is.False, "每场 1 次:再拆不发");
        }

        [Test]
        public void Dismantle_GlyphTrait_LevelTooLow_NoEvent()
        {
            var b = Battle(new[] { Jin() }, 3, new[] { "鍂" }, Array.Empty<string>());
            b.Dismantle("鍂");
            Assert.That(b.LastEvents, Is.Empty);
        }

        [Test]
        public void Dismantle_MarkGlyph_AlsoEmitsEvent()
        {
            var b = Battle(new[] { Yan() }, 4, new[] { "炎" }, Array.Empty<string>());
            b.Dismantle("炎");
            Assert.That(b.LastEvents.Select(e => e.Kind), Is.EqualTo(new[] { BattleEventKind.GlyphTrait }), "印记类也飘名");
        }

        [Test]
        public void Compose_GlyphTrait_EmitsEventBeforeEffects()
        {
            var b = Battle(new[] { Mao() }, 4, Array.Empty<string>(), new[] { "木", "木" }, summons: new[] { Spirit(0) });
            Assert.That(b.Compose("茂"), Is.EqualTo(BattleError.None));
            Assert.That(b.LastEvents[0].Kind, Is.EqualTo(BattleEventKind.GlyphTrait));
            Assert.That(b.LastEvents[0].SecondIndex, Is.EqualTo((int)TraitTrigger.OnCompose));
            Assert.That(b.LastEvents.Skip(1).Any(e => e.Kind == BattleEventKind.Shield), Is.True, "效果随后落地");
        }

        [Test]
        public void Dismantle_GlyphTrait_AmountIsIndexInTraits()
        {
            var filler = new TraitDef(TraitSlot.Lv1, TraitFace.Both, TraitForm.Passive, null, "垫", Array.Empty<EffectDef>());
            var glyph = Glyph(TraitTrigger.OnDismantle, null, 0, new EffectDef(EffectKind.Morale, 2));
            var jin = new CharDef("鍂", Element.Heart, recipe: new[] { "金", "金" },
                effects: new[] { new EffectDef(EffectKind.Shield, 10) }, traits: new[] { filler, glyph });
            var b = Battle(new[] { jin }, 4, new[] { "鍂" }, Array.Empty<string>());
            b.Dismantle("鍂");
            Assert.That(b.LastEvents.Single(e => e.Kind == BattleEventKind.GlyphTrait).Amount, Is.EqualTo(1));
        }

        /// <summary>下标按 Slot + Face 认(与 TraitKey 同口径):同槽异面、同面异槽的特性排在前面都不能认错。</summary>
        [Test]
        public void Dismantle_GlyphTrait_AmountIsIndex_MatchesSlotAndFace()
        {
            var sameSlotAttack = new TraitDef(TraitSlot.Lv4, TraitFace.Attack, TraitForm.Passive, null, "攻面", Array.Empty<EffectDef>());
            var sameSlotFeature = new TraitDef(TraitSlot.Lv4, TraitFace.Feature, TraitForm.Passive, null, "功面", Array.Empty<EffectDef>());
            var sameFaceLv1 = new TraitDef(TraitSlot.Lv1, TraitFace.Both, TraitForm.Passive, null, "垫", Array.Empty<EffectDef>());
            var glyph = Glyph(TraitTrigger.OnDismantle, null, 0, new EffectDef(EffectKind.Morale, 2));
            var jin = new CharDef("鍂", Element.Heart, recipe: new[] { "金", "金" },
                effects: new[] { new EffectDef(EffectKind.Shield, 10) },
                traits: new[] { sameSlotAttack, sameSlotFeature, sameFaceLv1, glyph });
            var b = Battle(new[] { jin }, 4, new[] { "鍂" }, Array.Empty<string>());
            b.Dismantle("鍂");
            Assert.That(b.LastEvents.Single(e => e.Kind == BattleEventKind.GlyphTrait).Amount, Is.EqualTo(3));
        }

        // ---------------- E3 Task 1:TraitSpentThisBattle 覆盖字形特性 ----------------

        [Test]
        public void TraitSpent_GlyphTrait_AfterDismantle_AndSnapshot()
        {
            var jin = Jin();
            var trait = jin.Traits[0];
            var b = Battle(new[] { jin }, 4, new[] { "鍂" }, Array.Empty<string>());
            Assert.That(b.TraitSpentThisBattle(jin, trait), Is.False, "拆前");
            b.Dismantle("鍂");
            Assert.That(b.TraitSpentThisBattle(jin, trait), Is.True, "拆后本场已用");

            var restored = BattleEngine.Restore(b.Capture(), RebalanceFixture.Graph(AllDefs(jin)), Config,
                new Dictionary<string, int> { ["鍂"] = 4 },
                new Dictionary<string, EnemyDef> { [RebalanceFixture.Mob().Id] = RebalanceFixture.Mob() });
            Assert.That(restored.TraitSpentThisBattle(jin, trait), Is.True, "快照往返后不变");

            var low = Battle(new[] { jin }, 3, new[] { "鍂" }, Array.Empty<string>());
            low.Dismantle("鍂");
            Assert.That(low.TraitSpentThisBattle(jin, trait), Is.False, "未解锁没生效,不算用过");
        }

        [Test]
        public void TraitSpent_GlyphTrait_ResetsInNextBattleOfSameRun()
        {
            var jin = Jin();
            var trait = jin.Traits[0];
            var config = new RunConfig
            {
                Encounters = Enumerable.Range(0, 2).Select(_ => new[] { RebalanceFixture.Mob(hp: 20) }).ToArray(),
                RewardPool = new[] { "甲" },
            };
            var run = new RunEngine(RebalanceFixture.Graph(AllDefs(jin)), config, Config,
                new[] { "鍂", "鍂", "甲", "甲" }, Array.Empty<string>(), seed: 1,
                cardLevels: new Dictionary<string, int> { ["鍂"] = 4 });

            Assert.That(run.Battle.Dismantle("鍂"), Is.EqualTo(BattleError.None));
            Assert.That(run.Battle.TraitSpentThisBattle(jin, trait), Is.True, "第一场:拆后已用");
            Assert.That(run.Battle.Cast("甲", 0), Is.EqualTo(BattleError.None));
            Assert.That(run.Battle.Phase, Is.EqualTo(BattlePhase.Won), "夹具前提:一击清场");
            run.AdvanceAfterBattle();
            while (run.Phase == RunPhase.Reward) run.SkipReward();
            Assert.That(run.BattleIndex, Is.EqualTo(1), "夹具前提:已进第二场");

            Assert.That(run.Battle.TraitSpentThisBattle(jin, trait), Is.False, "下一场重新计");
            Assert.That(run.Battle.Dismantle("鍂"), Is.EqualTo(BattleError.None));
            Assert.That(run.Battle.TraitSpentThisBattle(jin, trait), Is.True, "第二场照常记次");
        }

        // ---------------- E3 Task 1:印记读口 ----------------

        [Test]
        public void PartMarksOf_ReportsMarkAndCharges()
        {
            var yan = Yan();
            var b = Battle(new[] { yan }, 4, new[] { "炎" }, new[] { "火" });
            Assert.That(b.PartMarksOf("火"), Is.Empty);
            Assert.That(b.PartMarkCharges("火"), Is.EqualTo(0));
            b.Dismantle("炎");
            var marks = b.PartMarksOf("火");
            Assert.That(marks.Count, Is.EqualTo(1));
            Assert.That((marks[0].PartChar, marks[0].Remaining, marks[0].SourceCharId), Is.EqualTo(("火", 2, "炎")));
            Assert.That(marks[0].Trait, Is.SameAs(yan.Traits[0]));
            Assert.That(b.PartMarkCharges("火"), Is.EqualTo(2));
            Assert.That(b.PartMarksOf("山"), Is.Empty);

            Assert.That(b.Cast("火", 0), Is.EqualTo(BattleError.None));
            Assert.That(b.PartMarkCharges("火"), Is.EqualTo(1));
            Assert.That(b.Cast("火", 0), Is.EqualTo(BattleError.None));
            Assert.That(b.PartMarksOf("火"), Is.Empty, "用完移除");
        }

        [Test]
        public void PartMarksOf_TwoSources_FirstComeFirst_ChargesSum()
        {
            var yan = Yan();
            var yan2 = Twin("炏", "火", "火", Glyph(TraitTrigger.OnDismantle, "火", 1, new EffectDef(EffectKind.BurnSingle, 1)));
            var b = Battle(new[] { yan, yan2 }, 4, new[] { "炏", "炎" }, Array.Empty<string>());
            b.Dismantle("炏");
            b.Dismantle("炎");
            Assert.That(b.PartMarksOf("火").Select(m => (m.SourceCharId, m.Remaining)),
                Is.EqualTo(new[] { ("炏", 1), ("炎", 2) }), "先来先用的顺序");
            Assert.That(b.PartMarkCharges("火"), Is.EqualTo(3));
        }

        [Test]
        public void PartMarksOf_SourceTraitMissing_Skipped()
        {
            var b = Battle(new[] { Yan() }, 4, new[] { "炎" }, Array.Empty<string>());
            b.Dismantle("炎");
            // 读档时字表里的炎没了这条特性(数据变动):FindPartMark 视为无效,读口同口径跳过
            var bare = Twin("炎", "火", "火", null);
            var restored = BattleEngine.Restore(b.Capture(), RebalanceFixture.Graph(AllDefs(bare)), Config,
                new Dictionary<string, int> { ["炎"] = 4 },
                new Dictionary<string, EnemyDef> { [RebalanceFixture.Mob().Id] = RebalanceFixture.Mob() });
            Assert.That(restored.PartMarksOf("火"), Is.Empty);
            Assert.That(restored.PartMarkCharges("火"), Is.EqualTo(0));
        }

        // ---------------- E3 Task 1:CastEffectsNow / NeedsTargetNow / HitsAllEnemiesNow ----------------

        private static string Shape(IEnumerable<EffectDef> effects) =>
            string.Join(",", effects.Select(e => $"{e.Kind}:{e.Value}:{e.Shape}"));

        [Test]
        public void CastEffectsNow_MountainMark_AllEnemies_NoTarget()
        {
            var b = Battle(new[] { Can() }, 4, new[] { "灿" }, new[] { "山" }, enemies: 3);
            Assert.That(b.NeedsTargetNow("山", false), Is.True, "拆前:单体");
            Assert.That(b.HitsAllEnemiesNow("山", false), Is.False);
            b.Dismantle("灿");
            Assert.That(b.NeedsTargetNow("山", false), Is.False, "带印记:全体,不选目标");
            Assert.That(b.HitsAllEnemiesNow("山", false), Is.True);
            Assert.That(b.CastEffectsNow("山", false).Any(e => e.Kind == EffectKind.BurnAll), Is.True, "并入印记效果");

            Assert.That(b.Cast("山", -1), Is.EqualTo(BattleError.None), "Core 受理与读口一致");
            Assert.That(b.NeedsTargetNow("山", false), Is.True, "印记用完回到单体");
            Assert.That(b.HitsAllEnemiesNow("山", false), Is.False);
        }

        [Test]
        public void CastEffectsNow_MarkClearedNextTurn()
        {
            var b = Battle(new[] { Can() }, 4, new[] { "灿" }, Array.Empty<string>(), enemies: 2);
            b.Dismantle("灿");
            Assert.That(b.HitsAllEnemiesNow("山", false), Is.True);
            b.EndTurn();
            Assert.That(b.HitsAllEnemiesNow("山", false), Is.False, "回合开始清印记");
            Assert.That(b.NeedsTargetNow("山", false), Is.True);
        }

        [Test]
        public void CastEffectsNow_NoMark_EqualsStaticCastEffects()
        {
            var can = Can();
            var b = Battle(new[] { can }, 4, new[] { "灿" }, new[] { "火", "山" }, enemies: 2);
            foreach (var (id, attack) in new[] { ("灿", false), ("灿", true), ("火", false), ("山", false) })
            {
                var def = RebalanceFixture.Graph(AllDefs(can)).Get(id);
                int level = id == "灿" ? 4 : 1;
                Assert.That(Shape(b.CastEffectsNow(id, attack)),
                    Is.EqualTo(Shape(TraitRules.CastEffects(def, BattleEngine.FaceOf(def, attack), level))), $"{id} {attack}");
                Assert.That(b.NeedsTargetNow(id, attack), Is.EqualTo(BattleEngine.NeedsTarget(def, attack, level)), $"{id} {attack}");
            }
        }

        /// <summary>印记只给池中出手的部件:字库里的同名字(不在池里)不受影响,与 CastCore 的 fromPool 同判据。</summary>
        [Test]
        public void CastEffectsNow_LibraryChar_IgnoresPartMark()
        {
            var can = Can();
            var b = Battle(new[] { can }, 4, new[] { "灿", "山" }, Array.Empty<string>(), enemies: 3);
            b.Dismantle("灿");
            Assert.That(b.PartMarkCharges("山"), Is.EqualTo(1), "前提:山上挂着火山印记");
            var def = RebalanceFixture.Graph(AllDefs(can)).Get("山");
            Assert.That(Shape(b.CastEffectsNow("山", false)),
                Is.EqualTo(Shape(TraitRules.CastEffects(def, BattleEngine.FaceOf(def, false), 1))), "字库里的山 = 静态效果表");
            Assert.That(b.HitsAllEnemiesNow("山", false), Is.False);
            Assert.That(b.NeedsTargetNow("山", false), Is.True);
        }

        [Test]
        public void HitsAllEnemiesNow_StaticAllAndPickAll()
        {
            var allDef = new CharDef("燚", Element.Heart, recipe: new[] { "火", "火" },
                effects: new[] { new EffectDef(EffectKind.BurnAll, 1) },
                attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 10, shape: TargetArea.All) });
            var b = Battle(new[] { allDef }, 1, new[] { "燚" }, Array.Empty<string>(), enemies: 2);
            Assert.That(b.HitsAllEnemiesNow("燚", false), Is.True, "全体灼");
            Assert.That(b.HitsAllEnemiesNow("燚", true), Is.True, "全体伤害");
            Assert.That(b.NeedsTargetNow("燚", true), Is.False);
        }

        // ---------------- 数据加载 ----------------

        private static string CharJson(string traits) =>
            @"{""chars"":[{""id"":""火"",""element"":""Fire"",""component"":true},{""id"":""山"",""element"":""Earth"",""component"":true},
              {""id"":""炎"",""rarity"":""Gold"",""element"":""Fire"",""recipe"":[""火"",""火""],
                ""effects"":[{""kind"":""BurnSingle"",""value"":3}],
                ""attackEffects"":[{""kind"":""DamageSingle"",""value"":168}],
                ""traits"":[" + traits + "]}]}";

        [Test]
        public void Loader_ReadsGlyphTriggerAndPart()
        {
            var g = ConfigLoader.LoadGraph(CharJson(
                @"{""slot"":""Lv4"",""form"":""Passive"",""trigger"":""OnDismantle"",""partChar"":""火"",""partCount"":2,
                   ""name"":""双焰"",""effects"":[{""kind"":""BurnSingle"",""value"":2}]}"));
            var t = g.Get("炎").Traits[0];
            Assert.That(t.Trigger, Is.EqualTo(TraitTrigger.OnDismantle));
            Assert.That(t.PartChar, Is.EqualTo("火"));
            Assert.That(t.PartCount, Is.EqualTo(2));
        }

        [TestCase(@"{""slot"":""Lv4"",""form"":""Passive"",""trigger"":""OnCompose"",""name"":""x"",""effects"":[{""kind"":""BurnSingle"",""value"":2}]}",
            TestName = "Loader_InstantGlyph_EnemyTargetWithoutPickAll_Throws")]
        [TestCase(@"{""slot"":""Lv4"",""form"":""Passive"",""trigger"":""OnDismantle"",""partChar"":""山"",""partCount"":1,""name"":""x"",""effects"":[]}",
            TestName = "Loader_PartNotInRecipe_Throws")]
        [TestCase(@"{""slot"":""Lv4"",""form"":""Passive"",""trigger"":""OnCompose"",""partChar"":""火"",""partCount"":1,""name"":""x"",""effects"":[]}",
            TestName = "Loader_PartOnCompose_Throws")]
        [TestCase(@"{""slot"":""Lv4"",""form"":""Passive"",""trigger"":""OnDismantle"",""partChar"":""火"",""partCount"":0,""name"":""x"",""effects"":[]}",
            TestName = "Loader_PartCountZero_Throws")]
        public void Loader_BadGlyph_Throws(string trait)
        {
            Assert.Throws<ConfigException>(() => ConfigLoader.LoadGraph(CharJson(trait)));
        }

        [TestCase("OnCompose", TestName = "Loader_GlyphSapling_OnCompose_Throws")]
        [TestCase("OnDismantle", TestName = "Loader_GlyphSapling_OnDismantle_Throws")]
        public void Loader_GlyphWithSummonSapling_Throws(string trigger)
        {
            // 本体面带 Summon:幼苗在「同面须有召唤」那条校验下是合法的,只有字形这条新校验能拦住
            string json = @"{""chars"":[{""id"":""火"",""element"":""Fire"",""component"":true},{""id"":""木"",""element"":""Wood""},
              {""id"":""炎"",""rarity"":""Gold"",""element"":""Wood"",""recipe"":[""火"",""火""],
                ""effects"":[{""kind"":""Summon"",""value"":100,""count"":1,""attack"":5,""summonChar"":""木""}],
                ""traits"":[{""slot"":""Lv4"",""form"":""Passive"",""trigger"":""" + trigger + @""",""name"":""x"",
                   ""effects"":[{""kind"":""SummonSapling"",""value"":20,""count"":1}]}]}]}";
            var ex = Assert.Throws<ConfigException>(() => ConfigLoader.LoadGraph(json));
            Assert.That(ex.Message, Does.Contain("字形特性"));
        }

        [Test]
        public void Loader_InstantGlyph_PickAll_Ok()
        {
            var g = ConfigLoader.LoadGraph(CharJson(
                @"{""slot"":""Lv4"",""form"":""Passive"",""trigger"":""OnCompose"",""name"":""三焰"",
                   ""effects"":[{""kind"":""BurnSettleNow"",""value"":0,""keepStacks"":true,""pick"":""All""}]}"));
            Assert.That(g.Get("炎").Traits[0].Trigger, Is.EqualTo(TraitTrigger.OnCompose));
        }
    }
}
