using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>Plan E4 Task 1:预估以外的 Core 只读接口 —— 保命事件 EndureSaved、上炎来源 BurnGrow、
    /// 字形特性面印 TraitCueRules.SealOf、开局效果显示 OpeningRules.Display / LiveCount / SourceTrait、
    /// BattleEngine.AppliedOpenings、教程锁面 Tutorial.RequiredFace。</summary>
    public class E4CoreTests
    {
        private static RecipeGraph _real;
        private static RecipeGraph Real => _real ??= CharTableTests.RealGraph();

        private static readonly CharDef Hit = RebalanceFixture.Char("击", new EffectDef(EffectKind.DamageSingle, 1));

        private static StatusEffect Endure(string source = null) => new StatusEffect
            { Kind = StatusKind.Endure, Polarity = StatusPolarity.Buff, Magnitude = 1, TurnsLeft = -1, SourceId = source };

        private static StatusEffect BurnSt(int stacks) => new StatusEffect
            { Kind = StatusKind.Burn, Polarity = StatusPolarity.Debuff, Magnitude = stacks, TurnsLeft = -1, Potency = 100 };

        private static SummonSnapshot Spirit(int slot, int hp, int maxHp, SummonPassive passive = null, params StatusEffect[] statuses) =>
            new SummonSnapshot
            {
                Slot = slot, Char = "木", SourceChar = "扎", Element = Element.Wood, Hp = hp, MaxHp = maxHp,
                Attack = 0, Speed = 1, Passive = passive, Statuses = statuses.ToList(),
            };

        private static BattleEngine Battle(EnemyDef enemy, params SummonSnapshot[] summons) =>
            new BattleEngine(RebalanceFixture.Graph(Hit),
                new BattleConfig { PlayerMaxHp = 5000, PlayerAttack = 100, ApPerTurn = 20, BossPhaseJitterPercent = 0 },
                Enumerable.Repeat("击", 10).ToArray(), Array.Empty<string>(), new[] { enemy }, seed: 1,
                startingSummons: summons);

        private static int IndexOf(IReadOnlyList<BattleEvent> events, BattleEventKind kind) =>
            events.Select((e, i) => (e, i)).First(x => x.e.Kind == kind).i;

        // ================= 枚举只追加 =================

        [Test]
        public void E4EnumValues_AppendedAtEnd()
        {
            Assert.That((int)BattleEventKind.EndureSaved, Is.EqualTo((int)BattleEventKind.GlyphTrait + 1));
            // BurnGrow 紧跟 SkillCut(2026-10-13 删去中间一项)
            Assert.That((int)EffectSource.BurnGrow, Is.EqualTo((int)EffectSource.SkillCut + 1));
        }

        // ================= EndureSaved =================

        [Test]
        public void EndureSaved_OnHit_FollowsSummonHit()
        {
            var b = Battle(RebalanceFixture.Mob(attack: 500), Spirit(0, 100, 100, new SummonPassive { Taunt = true }, Endure()));
            b.EndTurn();
            var ev = b.LastEvents;
            int hit = IndexOf(ev, BattleEventKind.SummonHit);
            Assert.That(ev[hit].SecondIndex, Is.EqualTo(0), "夹具前提:砸在木灵上");
            var saved = ev[hit + 1];
            Assert.That((saved.Kind, saved.TargetIndex, saved.Amount, saved.SecondIndex),
                Is.EqualTo((BattleEventKind.EndureSaved, 0, 500 - 99, -1)), "紧跟 SummonHit;Amount = 挡掉的伤害");
            Assert.That(b.Summons[0].Hp, Is.EqualTo(1));
            Assert.That(b.Summons[0].Statuses.Has(StatusKind.Endure), Is.False);
        }

        [Test]
        public void EndureSaved_OnBurn_FollowsSummonBurnTick()
        {
            var b = Battle(RebalanceFixture.Mob(attack: 0), Spirit(0, 30, 100, null, Endure(), BurnSt(5)));
            for (int i = 0; i < 6 && !b.LastEvents.Any(e => e.Kind == BattleEventKind.SummonBurnTick); i++) b.EndTurn();
            var ev = b.LastEvents;
            int tick = IndexOf(ev, BattleEventKind.SummonBurnTick);
            var saved = ev[tick + 1];
            Assert.That((saved.Kind, saved.TargetIndex, saved.Amount, saved.SecondIndex),
                Is.EqualTo((BattleEventKind.EndureSaved, 0, ev[tick].Amount - 29, -1)));
            Assert.That(b.Summons[0].Hp, Is.EqualTo(1));
            Assert.That(b.Summons[0].Statuses.Has(StatusKind.Endure), Is.False);
        }

        [Test]
        public void EndureSaved_NotLethal_NotEmitted()
        {
            var b = Battle(RebalanceFixture.Mob(attack: 50), Spirit(0, 100, 100, new SummonPassive { Taunt = true }, Endure()));
            b.EndTurn();
            Assert.That(b.LastEvents.Any(e => e.Kind == BattleEventKind.SummonHit), Is.True, "夹具前提");
            Assert.That(b.LastEvents.Any(e => e.Kind == BattleEventKind.EndureSaved), Is.False);
            Assert.That(b.Summons[0].Statuses.Has(StatusKind.Endure), Is.True);
        }

        [Test]
        public void EndureSaved_TwoEndures_ConsumesOne()
        {
            var b = Battle(RebalanceFixture.Mob(attack: 500),
                Spirit(0, 100, 100, new SummonPassive { Taunt = true }, Endure("甲"), Endure("乙")));
            b.EndTurn();
            Assert.That(b.LastEvents.Count(e => e.Kind == BattleEventKind.EndureSaved), Is.EqualTo(1));
            Assert.That(b.Summons[0].Statuses.All.Count(s => s.Kind == StatusKind.Endure), Is.EqualTo(1), "只耗一条,chip 还在");
            Assert.That(b.Summons[0].Hp, Is.EqualTo(1));
        }

        // ================= 上炎 BurnGrow 来源 =================

        [Test]
        public void BurnGrow_BurnEvent_TaggedBurnGrow()
        {
            var b = Battle(RebalanceFixture.Mob(attack: 0));
            b.Enemies[0].Statuses.Apply(BurnSt(2));
            b.Enemies[0].Statuses.Apply(new StatusEffect
                { Kind = StatusKind.BurnGrow, Polarity = StatusPolarity.Debuff, Magnitude = 1, TurnsLeft = 3, SourceId = "炎" });
            b.EndTurn();
            var burns = b.LastEvents.Where(e => e.Kind == BattleEventKind.Burn && e.TargetIndex == 0).ToList();
            Assert.That(burns.Count, Is.EqualTo(1), "上炎 +1 层");
            Assert.That((burns[0].Amount, burns[0].Source), Is.EqualTo((1, EffectSource.BurnGrow)));
        }

        [Test]
        public void CastBurn_StillSourceNone()
        {
            var fire = RebalanceFixture.Char("烧", new EffectDef(EffectKind.BurnSingle, 2));
            var b = new BattleEngine(RebalanceFixture.Graph(fire), new BattleConfig { PlayerMaxHp = 500, PlayerAttack = 100 },
                new[] { "烧" }, Array.Empty<string>(), new[] { RebalanceFixture.Mob() }, seed: 1);
            b.Cast("烧", 0);
            Assert.That(b.LastEvents.Single(e => e.Kind == BattleEventKind.Burn).Source, Is.EqualTo(EffectSource.None));
        }

        // ================= TraitCueRules.SealOf =================

        private static TraitDef T(TraitFace face, TraitTrigger trigger = TraitTrigger.Cast) =>
            new(TraitSlot.Lv5, face, TraitForm.Active, null, "名", Array.Empty<EffectDef>(), trigger);

        [Test]
        public void SealOf_FiveKinds()
        {
            Assert.That(TraitCueRules.SealOf(T(TraitFace.Attack)), Is.EqualTo(TraitSealKind.Attack));
            Assert.That(TraitCueRules.SealOf(T(TraitFace.Feature)), Is.EqualTo(TraitSealKind.Feature));
            Assert.That(TraitCueRules.SealOf(T(TraitFace.Both)), Is.EqualTo(TraitSealKind.Both));
            Assert.That(TraitCueRules.SealOf(T(TraitFace.Both, TraitTrigger.OnDismantle)), Is.EqualTo(TraitSealKind.Dismantle));
            Assert.That(TraitCueRules.SealOf(T(TraitFace.Both, TraitTrigger.OnCompose)), Is.EqualTo(TraitSealKind.Compose));
            Assert.That(TraitCueRules.SealOf(T(TraitFace.Attack, TraitTrigger.OnKill)), Is.EqualTo(TraitSealKind.Attack), "折桂等非字形按面");
        }

        [Test]
        public void SealOf_RealGlyphTraits_AllDismantleOrCompose()
        {
            var glyphs = Real.All.SelectMany(d => d.Traits.Select(t => (d.Id, t)))
                .Where(x => x.t.Trigger == TraitTrigger.OnDismantle || x.t.Trigger == TraitTrigger.OnCompose).ToList();
            Assert.That(glyphs.Count, Is.EqualTo(8), "字形特性 8 条:焱 炎 灿 森 林 鍂 垚 圭");
            foreach (var (id, t) in glyphs)
                Assert.That(TraitCueRules.SealOf(t),
                    Is.EqualTo(t.Trigger == TraitTrigger.OnDismantle ? TraitSealKind.Dismantle : TraitSealKind.Compose), $"{id}·{t.Name}");
        }

        // ================= OpeningRules.Display / LiveCount / SourceTrait =================

        private static OpeningEffect O(string src, EffectKind kind, int value, int left) =>
            new OpeningEffect { SourceCharId = src, Kind = kind, Value = value, BattlesLeft = left };

        [Test]
        public void Display_LeftBeforeAfter_LastBattleBoundary()
        {
            var a = O("焱", EffectKind.BurnAll, 2, 5);
            var b = O("杜", EffectKind.Shield, 63, 1);
            var rows = OpeningRules.Display(new[] { a, b });
            Assert.That(rows.Select(r => (r.Effect.SourceCharId, r.LeftBefore, r.LeftAfter)),
                Is.EqualTo(new[] { ("焱", 5, 4), ("杜", 1, 0) }), "按输入顺序;1 → 0 是本场最后一场");
            Assert.That(ReferenceEquals(rows[0].Effect, a), Is.False, "行里是克隆");
            Assert.That(OpeningRules.LiveCount(new[] { a, b }), Is.EqualTo(1), "减一后 > 0 的条数");
        }

        [Test]
        public void Display_Empty()
        {
            Assert.That(OpeningRules.Display(Array.Empty<OpeningEffect>()).Count, Is.EqualTo(0));
            Assert.That(OpeningRules.LiveCount(Array.Empty<OpeningEffect>()), Is.EqualTo(0));
            Assert.That(OpeningRules.Display(null).Count, Is.EqualTo(0));
            Assert.That(OpeningRules.LiveCount(null), Is.EqualTo(0));
        }

        // ---- 剩余场数 pill 三态(Plan E4 Task 6,CarryOver 稿 R8):① 拍按开局前,② 拍 / 气泡按减一后 ----

        [Test]
        public void Pill_BeforeSettle_LastBattleIsWarn()
        {
            var rows = OpeningRules.Display(new[] { O("焱", EffectKind.BurnAll, 2, 5), O("杜", EffectKind.Shield, 63, 1) });
            var five = OpeningRules.Pill(rows[0], settled: false);
            Assert.That((five.Kind, five.Count), Is.EqualTo((OpeningPillKind.Left, 5)), "① 拍:剩 5 场");
            var one = OpeningRules.Pill(rows[1], settled: false);
            Assert.That(one.Kind, Is.EqualTo(OpeningPillKind.Last), "① 拍:开局前只剩 1 场 = 最后一场");
        }

        [Test]
        public void Pill_AfterSettle_ZeroIsDone_NeverLast()
        {
            var rows = OpeningRules.Display(new[]
            {
                O("焱", EffectKind.BurnAll, 2, 5), O("沐", EffectKind.HealOverTime, 17, 2), O("杜", EffectKind.Shield, 63, 1),
            });
            var four = OpeningRules.Pill(rows[0], settled: true);
            Assert.That((four.Kind, four.Count), Is.EqualTo((OpeningPillKind.Left, 4)), "② 拍:减一后剩 4");
            var oneLeft = OpeningRules.Pill(rows[1], settled: true);
            Assert.That((oneLeft.Kind, oneLeft.Count), Is.EqualTo((OpeningPillKind.Left, 1)),
                "减一后剩 1 仍是「剩 1 场」—— 「最后一场」只在 ① 拍出现(稿 row())");
            Assert.That(OpeningRules.Pill(rows[2], settled: true).Kind, Is.EqualTo(OpeningPillKind.Done), "1 → 0 = 用完");
        }

        [Test]
        public void LiveRows_OnlyLeftAfterPositive_CountMatchesLiveCount()
        {
            var input = new[]
            {
                O("焱", EffectKind.BurnAll, 2, 5), O("杜", EffectKind.Shield, 63, 1), O("沐", EffectKind.HealOverTime, 17, 2),
            };
            var live = OpeningRules.LiveRows(input);
            Assert.That(live.Select(r => (r.Effect.SourceCharId, r.LeftAfter)), Is.EqualTo(new[] { ("焱", 4), ("沐", 1) }));
            Assert.That(live.Count, Is.EqualTo(OpeningRules.LiveCount(input)), "chip 的 N 与气泡行数同一口径");
            Assert.That(OpeningRules.LiveRows(null).Count, Is.EqualTo(0));
        }

        private static IEnumerable<(CharDef Def, TraitDef Trait, EffectDef Effect)> RealOpenings() =>
            from d in Real.All
            from t in d.Traits
            from e in t.Effects
            where e.OpeningBattles > 0
            select (d, t, e);

        [Test]
        public void SourceTrait_FindsEveryRealOpening()
        {
            var all = RealOpenings().ToList();
            Assert.That(all.Count, Is.EqualTo(8), "开局效果 8 条:焱 炎 淼 沐 鑫 鍂 垚 杜");
            foreach (var (def, trait, effect) in all)
            {
                var o = OpeningEffect.Of(effect, def.Id, def.Element ?? Element.Heart);
                Assert.That(OpeningRules.SourceTrait(def, o), Is.SameAs(trait), $"{def.Id}·{trait.Name}");
            }
        }

        [Test]
        public void SourceTrait_NonOpening_Null()
        {
            var du = Real.Get("杜");
            Assert.That(OpeningRules.SourceTrait(du, O("杜", EffectKind.Shield, 999, 1)), Is.Null, "值对不上");
            Assert.That(OpeningRules.SourceTrait(du, O("杜", EffectKind.BurnAll, 63, 1)), Is.Null, "种类对不上");
            Assert.That(OpeningRules.SourceTrait(Real.Get("炎"), O("炎", EffectKind.BurnSingle, 2, 1)), Is.Null,
                "双焰的 BurnSingle 2 不是开局效果");
            Assert.That(OpeningRules.SourceTrait(null, O("杜", EffectKind.Shield, 63, 1)), Is.Null);
        }

        /// <summary>数据守卫(D 已定口径):每个字的开局效果 (Kind, Value) 唯一 —— SourceTrait 靠它回查。</summary>
        [Test]
        public void Data_OpeningKindValue_UniquePerChar()
        {
            foreach (var g in RealOpenings().GroupBy(x => x.Def.Id))
            {
                var keys = g.Select(x => (x.Effect.Kind, x.Effect.Value)).ToList();
                Assert.That(keys.Distinct().Count(), Is.EqualTo(keys.Count), g.Key);
            }
        }

        /// <summary>Merge 抽公共键前后语义不变:多组混排,输出按组首次出现的顺序,组内取最强,场数 ≤ 0 的丢。</summary>
        [Test]
        public void Merge_MixedGroups_OrderUnchanged()
        {
            var existing = new[]
            {
                O("鍂", EffectKind.Morale, 2, 1), O("炎", EffectKind.BurnAll, 2, 1), O("沐", EffectKind.HealOverTime, 17, 0),
            };
            var incoming = new[]
            {
                O("杜", EffectKind.Shield, 63, 1), O("焱", EffectKind.BurnAll, 2, 5), O("淼", EffectKind.HealOverTime, 40, 5),
                O("鑫", EffectKind.Morale, 2, 5), O("垚", EffectKind.DefenseBuff, 38, 5),
            };
            var merged = OpeningRules.Merge(existing, incoming);
            Assert.That(merged.Select(m => (m.SourceCharId, m.BattlesLeft)), Is.EqualTo(new[]
            {
                ("鑫", 5), ("焱", 5), ("杜", 1), ("淼", 5), ("垚", 5),
            }));
        }

        // ================= AppliedOpenings =================

        [Test]
        public void AppliedOpenings_EqualsInput_NotInSnapshot()
        {
            var input = new[] { new OpeningEffect
                { SourceCharId = "焱", Element = Element.Fire, Kind = EffectKind.BurnAll, Value = 2, BattlesLeft = 3 } };
            var b = new BattleEngine(RebalanceFixture.Graph(), new BattleConfig { PlayerMaxHp = 500, PlayerAttack = 100 },
                new[] { "甲" }, Array.Empty<string>(), new[] { RebalanceFixture.Mob() }, seed: 1, startingOpenings: input);
            Assert.That(b.AppliedOpenings.Select(o => (o.SourceCharId, o.Kind, o.Value, o.BattlesLeft)),
                Is.EqualTo(new[] { ("焱", EffectKind.BurnAll, 2, 3) }));
            Assert.That(ReferenceEquals(b.AppliedOpenings[0], input[0]), Is.False, "克隆");
            Assert.That(b.OpeningSteps[0].Events.Any(e => e.Kind == BattleEventKind.Burn), Is.True, "OpeningSteps[0] 就是开局效果那拍");

            var restored = BattleEngine.Restore(b.Capture(), RebalanceFixture.Graph(), new BattleConfig { PlayerMaxHp = 500, PlayerAttack = 100 },
                null, new Dictionary<string, EnemyDef> { ["怔"] = RebalanceFixture.Mob() });
            Assert.That(restored.AppliedOpenings.Count, Is.EqualTo(0), "断点续爬不补播");

            var none = new BattleEngine(RebalanceFixture.Graph(), new BattleConfig { PlayerMaxHp = 500, PlayerAttack = 100 },
                new[] { "甲" }, Array.Empty<string>(), new[] { RebalanceFixture.Mob() }, seed: 1);
            Assert.That(none.AppliedOpenings.Count, Is.EqualTo(0));
        }

        // ================= Tutorial.RequiredFace =================

        [Test]
        public void Tutorial_RequiredFace_OnlyCastDemoAndDemoChar()
        {
            var t = new Tutorial();
            Assert.That(t.Step, Is.EqualTo(TutorialStep.DismantleDemo));
            Assert.That(t.RequiredFace(Tutorial.DemoChar), Is.Null);
            t.Notify(TutorialAction.Dismantle, Tutorial.DemoChar);
            Assert.That(t.Step, Is.EqualTo(TutorialStep.RecomposeDemo));
            Assert.That(t.RequiredFace(Tutorial.DemoChar), Is.Null);
            t.Notify(TutorialAction.Compose, Tutorial.DemoChar);
            Assert.That(t.Step, Is.EqualTo(TutorialStep.CastDemo));
            Assert.That(t.RequiredFace(Tutorial.DemoChar), Is.EqualTo(CardFace.Attack));
            Assert.That(t.RequiredFace("花"), Is.Null);
            Assert.That(t.RequiredFace(null), Is.Null);
            t.Notify(TutorialAction.Cast, Tutorial.DemoChar, CardFace.Attack);
            Assert.That(t.Step, Is.EqualTo(TutorialStep.PickReward));
            Assert.That(t.RequiredFace(Tutorial.DemoChar), Is.Null);
            t.Notify(TutorialAction.PickReward);
            Assert.That(t.Done, Is.True);
            Assert.That(t.RequiredFace(Tutorial.DemoChar), Is.Null);
        }
    }
}
