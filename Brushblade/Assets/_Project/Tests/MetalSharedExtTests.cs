using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>Plan D2-金 Task 1:共用小扩展 —— 附录 E6(条件 MoraleFull)、E7(Reshape 携带斩杀)、E8(流血补口径:
    /// turns、不论来源合成单条取强、只结算一条)、E9(MoraleFill)、E10(计数缩放扩档 Morale / ExtraHitTarget)、
    /// E11(每击附带的金系子效果,只验证)、E12(BlockMod 出字时字段)、E13(ofVictimMaxHp)。
    ///
    /// 夹具口径同 FireSharedExtTests:Element.Heart(生克 1.0×、不吃砺刃 / 断金)、PlayerAttack = 100、无甲 10 万血靶子、暴击率 0。</summary>
    public class MetalSharedExtTests
    {
        private const int Hp = 100000;

        private static BattleConfig Config => new BattleConfig
            { PlayerMaxHp = RebalanceFixture.BaseMaxHp, PlayerAttack = 100, ApPerTurn = 20 };

        private static BattleEngine Battle(CharDef[] defs, int level, EnemyDef[] enemies) =>
            new(RebalanceFixture.Graph(defs), Config,
                defs.SelectMany(d => new[] { d.Id, d.Id, d.Id }).ToArray(), Array.Empty<string>(),
                enemies, seed: 1, cardLevels: defs.ToDictionary(d => d.Id, _ => level));

        private static BattleEngine Battle(CharDef def, int level, params EnemyDef[] enemies) =>
            Battle(new[] { def }, level, enemies.Length > 0 ? enemies : new[] { RebalanceFixture.Mob() });

        private static EnemyDef[] FourMobs() => new[]
        {
            new EnemyDef("甲", Element.Heart, Hp, 0),
            new EnemyDef("乙", Element.Heart, Hp, 0),
            new EnemyDef("丙", Element.Heart, Hp, 0),
            new EnemyDef("丁", Element.Heart, Hp, 0, row: EnemyRow.Back),
        };

        private static TraitDef Trait(TraitSlot slot, TraitFace face, TraitForm form, params EffectDef[] effects) =>
            new(slot, face, form, null, "试" + (int)slot, effects);

        private static int Morale(BattleEngine b) => b.PlayerStatuses.TotalMagnitude(StatusKind.Morale);

        private static void SetMorale(BattleEngine b, int stacks) =>
            b.PlayerStatuses.Apply(new StatusEffect
                { Kind = StatusKind.Morale, Polarity = StatusPolarity.Buff, Magnitude = stacks, TurnsLeft = -1 });

        private static int Dealt(BattleEngine b, int target = 0, bool attackMode = false)
        {
            int before = b.Enemies[target].Hp;
            Assert.That(b.Cast("试", target, attackMode: attackMode), Is.EqualTo(BattleError.None));
            return before - b.Enemies[target].Hp;
        }

        // ---------------- E6:DamageCondition.MoraleFull ----------------

        // 修饰器写在本体里同样由 Fold 折叠 —— 免得特性解锁要抬卡等级、等级缩放的取整干扰精确断言
        private static CharDef AmpIfMoraleFull(bool withAmp) => RebalanceFixture.Char("试",
            withAmp
                ? new[] { new EffectDef(EffectKind.DamageSingle, 100), new EffectDef(EffectKind.Amplify, 30, onlyIf: DamageCondition.MoraleFull) }
                : new[] { new EffectDef(EffectKind.DamageSingle, 100) });

        [Test]
        public void Condition_MoraleFull_OnlyAtCap()
        {
            Assert.That((int)DamageCondition.MoraleFull, Is.EqualTo(15), "只在末尾追加");
            int cap = Config.MoraleCap;
            // 同战意下比「有 / 无加成」:战意本身抬攻击力,两边一致,差的只有 Amplify。
            // Lv1、100 基数:满层时 Amplified(100, 30) = 130,攻击力 150% → 195 = 150 × 130%
            foreach (int stacks in new[] { cap - 1, cap })
            {
                var plain = Battle(AmpIfMoraleFull(false), 1);
                SetMorale(plain, stacks);
                var amp = Battle(AmpIfMoraleFull(true), 1);
                SetMorale(amp, stacks);
                int p = Dealt(plain);
                Assert.That(Dealt(amp), Is.EqualTo(stacks >= cap ? p * 130 / 100 : p), $"战意 {stacks}");
            }
        }

        // ---------------- E7:Reshape 携带斩杀 ----------------

        private static CharDef Guillotine(bool withTrait) => new("试", Element.Heart,
            effects: new[] { new EffectDef(EffectKind.DamageSingle, 10) },
            traits: withTrait
                ? new[] { Trait(TraitSlot.Lv8, TraitFace.Both, TraitForm.Active,
                    new EffectDef(EffectKind.Reshape, 0, executeBelowPercent: 35, executeKills: true)) }
                : null);

        [Test]
        public void Reshape_CarriesExecute_FoldCopiesFields()
        {
            var folded = TraitRules.CastEffects(Guillotine(true), CardFace.Feature, 8);
            var dmg = folded.Single(e => e.Kind == EffectKind.DamageSingle);
            Assert.That((dmg.ExecuteBelowPercent, dmg.ExecuteKills), Is.EqualTo((35, true)));
            var none = TraitRules.CastEffects(Guillotine(false), CardFace.Feature, 8).Single();
            Assert.That(none.ExecuteBelowPercent, Is.EqualTo(0));
        }

        [Test]
        public void Reshape_CarriesExecute_KillsBelowThreshold()
        {
            var mob = new EnemyDef("怔", Element.Heart, 1000, 0);
            var with = Battle(Guillotine(true), 8, mob);
            with.Enemies[0].Hp = 300;   // 30% < 35%
            with.Cast("试", 0);
            Assert.That(with.Enemies[0].Alive, Is.False, "35% 以下直接斩杀");

            var without = Battle(Guillotine(false), 8, mob);
            without.Enemies[0].Hp = 300;
            without.Cast("试", 0);
            Assert.That(without.Enemies[0].Alive, Is.True);
        }

        // ---------------- E8:流血补口径 ----------------

        private static CharDef Bleeder(int value, int turns = 0) =>
            RebalanceFixture.Char("试", new EffectDef(EffectKind.Bleed, value, turns: turns));

        private static List<StatusEffect> Bleeds(BattleEngine b, int i = 0) =>
            b.Enemies[i].Statuses.All.Where(s => s.Kind == StatusKind.Bleed).ToList();

        [Test]
        public void Bleed_ReadsTurns_DefaultsTo3()
        {
            var two = Battle(Bleeder(20, turns: 2), 1);
            two.Cast("试", 0);
            Assert.That(Bleeds(two).Single().TurnsLeft, Is.EqualTo(2), "写了 turns 2 就是 2");
            var dflt = Battle(Bleeder(20), 1);
            dflt.Cast("试", 0);
            Assert.That(Bleeds(dflt).Single().TurnsLeft, Is.EqualTo(3), "缺省 3");
        }

        [Test]
        public void Bleed_AugmentTurns_Applies()
        {
            Assert.That(TraitRules.HasTurns(EffectKind.Bleed), Is.True, "流血进 Augment 回合表");
            var def = new CharDef("试", Element.Heart, effects: new[] { new EffectDef(EffectKind.Bleed, 20, turns: 2) },
                traits: new[] { Trait(TraitSlot.Lv4, TraitFace.Both, TraitForm.Passive,
                    new EffectDef(EffectKind.Augment, 1, augmentKind: EffectKind.Bleed, augmentField: AugmentField.Turns)) });
            Assert.That(TraitRules.CastEffects(def, CardFace.Feature, 4).Single().Turns, Is.EqualTo(3));
        }

        /// <summary>修复轮 1(review Important):缺省 turns 由 ConfigLoader 规范成 3,Augment +1 回合 → 4(不是 0 + 1 = 1)。</summary>
        [Test]
        public void Bleed_DefaultTurnsPlusAugment_IsFour()
        {
            var g = Brushblade.Data.ConfigLoader.LoadGraph(
                @"{""chars"":[{""id"":""刲"",""element"":""Metal"",""effects"":[{""kind"":""Bleed"",""value"":20},
                  {""kind"":""Augment"",""value"":1,""augmentKind"":""Bleed"",""augmentField"":""Turns""}]}]}");
            var def = g.Get("刲");
            Assert.That(def.Effects[0].Turns, Is.EqualTo(3), "加载时缺省 turns 规范成 3");
            Assert.That(TraitRules.CastEffects(def, CardFace.Feature, 1).Single().Turns, Is.EqualTo(4));
            var b = new BattleEngine(g, Config, new[] { "刲", "刲" }, Array.Empty<string>(),
                new[] { RebalanceFixture.Mob() }, seed: 1, cardLevels: new Dictionary<string, int> { ["刲"] = 1 });
            Assert.That(b.Cast("刲", 0), Is.EqualTo(BattleError.None));
            Assert.That(Bleeds(b).Single().TurnsLeft, Is.EqualTo(4));
        }

        [Test]
        public void Bleed_WeakerDoesNotOverrideStronger_TakesMaxAmountAndLongestTurns()
        {
            var strong = Bleeder(50, turns: 2);
            var weak = new CharDef("弱", Element.Heart, effects: new[] { new EffectDef(EffectKind.Bleed, 20, turns: 3) });
            var b = Battle(new[] { strong, weak }, 1, new[] { RebalanceFixture.Mob() });
            b.Cast("试", 0);
            Assert.That(b.Cast("弱", 0), Is.EqualTo(BattleError.None));
            var s = Bleeds(b).Single();
            Assert.That(s.Magnitude, Is.EqualTo(50), "弱的流血不覆盖强的(量取大)");
            Assert.That(s.TurnsLeft, Is.EqualTo(3), "回合取长");
        }

        [Test]
        public void Bleed_TwoSources_MergeIntoOne_SettlesOnce()
        {
            var b = Battle(Bleeder(20, turns: 2), 1);
            // 另一个来源(带 SourceId / TraitKey)的流血已经在身上:施加时不论来源合成单条
            b.Enemies[0].Statuses.Apply(new StatusEffect
            {
                Kind = StatusKind.Bleed, Polarity = StatusPolarity.Debuff, Magnitude = 30, TurnsLeft = 1,
                SourceId = "别", TraitKey = "别/5/Attack",
            });
            b.Cast("试", 0);
            var s = Bleeds(b).Single();
            Assert.That((s.Magnitude, s.TurnsLeft), Is.EqualTo((30, 2)), "量取大、回合取长");

            int before = b.Enemies[0].Hp;
            b.EndTurn();
            Assert.That(before - b.Enemies[0].Hp, Is.EqualTo(30), "一拍只结算一条");
            Assert.That(b.LastEvents.Count(e => e.Kind == BattleEventKind.BleedTick), Is.EqualTo(1));
        }

        [Test]
        public void Bleed_SnapshotRoundTrip_StaysOneEntry()
        {
            // 读档前上强的(40,缺省 3 回合)、读档后上弱的(20,2 回合):撤掉合并的话后上的会覆盖成 (20, 2)
            var defs = new[] { Bleeder(20, turns: 2), new CharDef("强", Element.Heart, effects: new[] { new EffectDef(EffectKind.Bleed, 40) }) };
            var b = Battle(defs, 1, new[] { RebalanceFixture.Mob() });
            b.Cast("强", 0);
            var restored = BattleEngine.Restore(b.Capture(), RebalanceFixture.Graph(defs), Config,
                defs.ToDictionary(d => d.Id, _ => 1), new Dictionary<string, EnemyDef> { ["怔"] = RebalanceFixture.Mob() });
            Assert.That(Bleeds(restored).Count, Is.EqualTo(1));
            Assert.That(restored.Cast("试", 0), Is.EqualTo(BattleError.None));
            var s = Bleeds(restored).Single();
            Assert.That((s.Magnitude, s.TurnsLeft), Is.EqualTo((40, 3)), "读档后再上弱的流血:仍一条,量 / 回合取强");
        }

        // ---------------- E9:MoraleFill ----------------

        [Test]
        public void MoraleFill_SetsToCap_NoChangeWhenAlreadyFull()
        {
            var def = RebalanceFixture.Char("试", new EffectDef(EffectKind.Morale, 0, fill: true));
            var b = Battle(def, 1);
            SetMorale(b, 1);
            b.Cast("试", -1);
            Assert.That(Morale(b), Is.EqualTo(Config.MoraleCap));

            var empty = Battle(def, 1);
            empty.Cast("试", -1);
            Assert.That(Morale(empty), Is.EqualTo(Config.MoraleCap), "没有战意也补满");
            Assert.That(empty.PlayerStatuses.All.Count(s => s.Kind == StatusKind.Morale), Is.EqualTo(1));
        }

        // ---------------- E10:计数缩放扩档 ----------------

        private static CharDef Dismember() => new("试", Element.Heart,
            effects: new[] { new EffectDef(EffectKind.DamageSingle, 100) },
            traits: new[] { Trait(TraitSlot.Lv8, TraitFace.Both, TraitForm.Active,
                new EffectDef(EffectKind.Reshape, 0, hitPercent: 35, scaleBy: ScaleBasis.Morale)) });

        [Test]
        public void HitsPerMorale_HitCountIsOnePlusMorale()
        {
            Assert.That((int)ScaleBasis.Morale, Is.EqualTo(3), "只在末尾追加");
            Assert.That((int)ScaleBasis.ExtraHitTarget, Is.EqualTo(4));
            var none = Battle(Dismember(), 8);
            none.Cast("试", 0);
            Assert.That(none.LastEvents.Count(e => e.Kind == BattleEventKind.Damage), Is.EqualTo(1), "0 战意 = 1 击");

            var b = Battle(Dismember(), 8);
            SetMorale(b, 3);
            b.Cast("试", 0);
            Assert.That(b.LastEvents.Count(e => e.Kind == BattleEventKind.Damage), Is.EqualTo(4), "1 + 3 击");
        }

        [Test]
        public void MoralePerExtraHitTarget_CountsDistinctTargetsMinusOne()
        {
            var def = RebalanceFixture.Char("试",
                new EffectDef(EffectKind.DamageSingle, 10, shape: TargetArea.Row, shapePercent: 50),
                new EffectDef(EffectKind.Morale, 1, scaleBy: ScaleBasis.ExtraHitTarget));
            var b = Battle(def, 1, FourMobs());
            b.Cast("试", 0);
            Assert.That(Morale(b), Is.EqualTo(2), "前排 3 名 → 多命中 2 名");

            var single = Battle(def, 1, RebalanceFixture.Mob());
            single.Cast("试", 0);
            Assert.That(Morale(single), Is.EqualTo(0));
            Assert.That(single.PlayerStatuses.Has(StatusKind.Morale), Is.False, "0 层不挂空状态");
        }

        private static CharDef DoubleMetal(int bodyMorale) => new("试", Element.Heart,
            effects: new[] { new EffectDef(EffectKind.Morale, bodyMorale), new EffectDef(EffectKind.Block, 1) },
            attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 100) },
            traits: new[] { Trait(TraitSlot.Lv8, TraitFace.Feature, TraitForm.Active,
                new EffectDef(EffectKind.BlockMod, 0, scaleBy: ScaleBasis.Morale, scaleMin: 2)) });

        [Test]
        public void BlockCountPerMorale_UsesPostCastMorale_WithMin()
        {
            var b = Battle(DoubleMetal(3), 8);
            SetMorale(b, 1);
            b.Cast("试", -1);
            Assert.That(b.PlayerStatuses.Find(StatusKind.Block).Magnitude, Is.EqualTo(4), "出字后战意 1 + 3 = 4");

            var low = Battle(DoubleMetal(1), 8);
            low.Cast("试", -1);
            Assert.That(low.PlayerStatuses.Find(StatusKind.Block).Magnitude, Is.EqualTo(2), "战意 1 < 下限 2");
        }

        /// <summary>Ruling 11:countPerMorale 的 Block 推迟到效果循环结束后再施加 —— 排在它**后面**的 Morale 也算数。</summary>
        [Test]
        public void BlockCountPerMorale_MoraleAfterBlock_StillCounted()
        {
            CharDef Make(int morale) => new("试", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.Block, 1), new EffectDef(EffectKind.Morale, morale) },
                attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 100) },
                traits: new[] { Trait(TraitSlot.Lv8, TraitFace.Feature, TraitForm.Active,
                    new EffectDef(EffectKind.BlockMod, 0, scaleBy: ScaleBasis.Morale, scaleMin: 2)) });

            var two = Battle(Make(2), 8);
            two.Cast("试", -1);
            Assert.That(two.PlayerStatuses.Find(StatusKind.Block).Magnitude, Is.EqualTo(2), "0 起 + 2 → 出字后战意 2");

            var three = Battle(Make(3), 8);
            SetMorale(three, 1);
            three.Cast("试", -1);
            Assert.That(three.PlayerStatuses.Find(StatusKind.Block).Magnitude, Is.EqualTo(4), "1 起 + 3 → 4");

            var low = Battle(Make(1), 8);
            low.Cast("试", -1);
            Assert.That(low.PlayerStatuses.Find(StatusKind.Block).Magnitude, Is.EqualTo(2), "出字后战意 1 < min 2");
        }

        /// <summary>Ruling 11 只动 countPerMorale:普通 Block 仍在循环里当场施加 —— 事件流与状态表顺序和改前逐位一致。
        /// 期望指纹是在改前的提交(69004bfe)上跑出来的。</summary>
        [Test]
        public void PlainBlock_Timing_UnchangedByDeferral()
        {
            var def = new CharDef("试", Element.Heart,
                effects: new[]
                {
                    new EffectDef(EffectKind.Morale, 1), new EffectDef(EffectKind.Block, 2),
                    new EffectDef(EffectKind.Shield, 30), new EffectDef(EffectKind.Block, 1),
                },
                attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 100) });
            var b = Battle(def, 5);
            b.Cast("试", -1);
            string events = string.Join(";", b.LastEvents.Select(e => $"{e.Kind}/{e.TargetIndex}/{e.Amount}"));
            string statuses = string.Join(";", b.PlayerStatuses.All.Select(s => $"{s.Kind}/{s.Magnitude}/{s.CounterDamage}"));
            Assert.That(events + " | " + statuses, Is.EqualTo(PlainBlockFingerprint));
        }

        private const string PlainBlockFingerprint = "Shield/-1/38 | Morale/1/0;Block/2/37;Heft/0/0";   // Block 排在 Heft(Shield 那条)之前 = 循环内当场施加

        // ---------------- E12:BlockMod counter ----------------

        private static CharDef Guard(params int[] counters) => new("试", Element.Heart,
            effects: new[] { new EffectDef(EffectKind.Block, 1) },
            attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 100) },
            traits: counters.Select((c, i) => Trait(i == 0 ? TraitSlot.Lv5 : TraitSlot.Lv8, TraitFace.Feature, TraitForm.Active,
                new EffectDef(EffectKind.BlockMod, 0, counterPercent: c))).ToArray());

        [Test]
        public void BlockMod_CounterPercent_OverridesDefault30_LaterSlotWins()
        {
            Assert.That(TraitRules.IsModifier(EffectKind.BlockMod), Is.True);
            var plain = Battle(Guard(), 5);
            plain.Cast("试", -1);
            Assert.That(plain.PlayerStatuses.Find(StatusKind.Block).CounterDamage,
                Is.EqualTo(MetaRules.ScaleByCardLevel(100, 5) * 30 / 100), "缺省 30%");

            var b = Battle(Guard(50), 5);
            b.Cast("试", -1);
            Assert.That(b.PlayerStatuses.Find(StatusKind.Block).CounterDamage, Is.EqualTo(MetaRules.ScaleByCardLevel(100, 5) * 50 / 100));

            var two = Battle(Guard(50, 60), 8);
            two.Cast("试", -1);
            Assert.That(two.PlayerStatuses.Find(StatusKind.Block).CounterDamage,
                Is.EqualTo(MetaRules.ScaleByCardLevel(100, 8) * 60 / 100), "按槽位顺序折叠,后者 60 覆盖");
        }

        /// <summary>顺序(修复轮 1):BlockMod 先改反击基数(本体 × 50%),Amplify Counter 再在这个基数上同轴相加(+100% → ×2)。</summary>
        [Test]
        public void BlockMod_CounterPercent_ThenAmplifyCounter()
        {
            var def = new CharDef("试", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.Block, 1), new EffectDef(EffectKind.Amplify, 100, scope: AmpScope.Counter) },
                attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 100) },
                traits: new[] { Trait(TraitSlot.Lv5, TraitFace.Feature, TraitForm.Active, new EffectDef(EffectKind.BlockMod, 0, counterPercent: 50)) });
            var b = Battle(def, 5);
            b.Cast("试", -1);
            int based = MetaRules.ScaleByCardLevel(100, 5) * 50 / 100;   // 124 × 50% = 62
            Assert.That(b.PlayerStatuses.Find(StatusKind.Block).CounterDamage, Is.EqualTo(based * 2), "62 × (100 + 100)% = 124");
        }

        [Test]
        public void BlockMod_NoBlockOnFace_IsNoOp()
        {
            var def = new CharDef("试", Element.Heart, effects: new[] { new EffectDef(EffectKind.Shield, 10) },
                traits: new[] { Trait(TraitSlot.Lv5, TraitFace.Both, TraitForm.Active, new EffectDef(EffectKind.BlockMod, 0, counterPercent: 50)) });
            var folded = TraitRules.CastEffects(def, CardFace.Feature, 5);
            Assert.That(folded.Select(e => e.Kind), Is.EqualTo(new[] { EffectKind.Shield }));
        }

        // ---------------- E13:ofVictimMaxHp ----------------

        [Test]
        public void HealSelf_OfVictimMaxHp_OnKill_HealsPercentOfVictimMaxHp_NotLevelScaled()
        {
            var def = new CharDef("试", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.Shield, 10) },
                attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 1000) },
                traits: new[] { new TraitDef(TraitSlot.Lv6, TraitFace.Attack, TraitForm.Passive, null, "割取",
                    new[] { new EffectDef(EffectKind.HealSelf, 10, ofVictimMaxHp: true) }, TraitTrigger.OnKill) });
            var b = Battle(new[] { def }, 6, new[] { new EnemyDef("怔", Element.Heart, 600, 0), RebalanceFixture.Mob() });
            b.DamagePlayerForTest(300);
            int before = b.PlayerHp;
            b.Cast("试", 0, attackMode: true);
            Assert.That(b.Enemies[0].Alive, Is.False);
            Assert.That(b.PlayerHp - before, Is.EqualTo(60), "被杀者最大生命 600 × 10%,不吃卡等级");
        }

        // ---------------- E11:每击附带的金系子效果(只验证,复用 D2-火 PerHit) ----------------

        private static CharDef Hitter(int hits, params EffectDef[] riders) =>
            RebalanceFixture.Char("试", new EffectDef(EffectKind.DamageSingle, 100, hitCount: hits, perHit: riders));

        [Test]
        public void PerHit_ArmorBreak_WithItsOwnTurns_StacksPerHit()
        {
            var b = Battle(Hitter(2, new EffectDef(EffectKind.ArmorBreak, 20, turns: 3)), 1);
            b.Cast("试", 0);
            var breaks = b.Enemies[0].Statuses.All.Where(s => s.Kind == StatusKind.ArmorBreak).ToList();
            Assert.That(breaks.Count, Is.EqualTo(2), "两击各一层,可叠");
            Assert.That(breaks.All(s => s.TurnsLeft == 3), Is.True, "破甲带自己的 turns");
        }

        [Test]
        public void PerHit_Bleed_WithTurns_MergesToOne()
        {
            var b = Battle(Hitter(2, new EffectDef(EffectKind.Bleed, 35, turns: 2)), 1);
            b.Cast("试", 0);
            var s = Bleeds(b).Single();
            Assert.That((s.Magnitude, s.TurnsLeft), Is.EqualTo((35, 2)));
        }

        [Test]
        public void PerHit_Morale_ResolvesOnKillingBlow_M1()
        {
            var b = Battle(Hitter(2, new EffectDef(EffectKind.Morale, 1)), 1, new EnemyDef("怔", Element.Heart, 1, 0), RebalanceFixture.Mob());
            b.Cast("试", 0);
            Assert.That(b.Enemies[0].Alive, Is.False, "前提:第一击打死");
            Assert.That(Morale(b), Is.EqualTo(1), "击杀那一击照给战意(我方侧,Ruling 10 / M1);第二击不打");

            var two = Battle(Hitter(2, new EffectDef(EffectKind.Morale, 1)), 1);
            two.Cast("试", 0);
            Assert.That(Morale(two), Is.EqualTo(2), "每击 +1");
        }

        // ---------------- ConfigLoader ----------------

        private static RecipeGraph Load(string effects, string attackEffects = @"{""kind"":""DamageSingle"",""value"":40}") =>
            Brushblade.Data.ConfigLoader.LoadGraph(
                @"{""chars"":[{""id"":""剁"",""element"":""Metal"",""effects"":[" + effects + @"],""attackEffects"":[" + attackEffects + "]}]}");

        [Test]
        public void ConfigLoader_ParsesTask1Fields()
        {
            var g = Load(@"{""kind"":""Morale"",""fill"":true},{""kind"":""Morale"",""value"":1,""scaleBy"":""ExtraHitTarget""},
                {""kind"":""Block"",""value"":1},{""kind"":""BlockMod"",""counterPercent"":50,""scaleBy"":""Morale"",""scaleMin"":2},
                {""kind"":""Bleed"",""value"":20,""turns"":2},{""kind"":""HealSelf"",""value"":10,""ofVictimMaxHp"":true}",
                @"{""kind"":""DamageSingle"",""value"":40},{""kind"":""Reshape"",""hitPercent"":35,""scaleBy"":""Morale"",
                  ""executeBelowPercent"":35,""executeKills"":true},
                {""kind"":""Amplify"",""value"":30,""onlyIf"":""MoraleFull""}");
            var e = g.Get("剁").Effects;
            Assert.That(e[0].Fill, Is.True);
            Assert.That(e[1].ScaleBy, Is.EqualTo(ScaleBasis.ExtraHitTarget));
            Assert.That((e[3].Kind, e[3].CounterPercent, e[3].ScaleBy, e[3].ScaleMin),
                Is.EqualTo((EffectKind.BlockMod, 50, ScaleBasis.Morale, 2)));
            Assert.That(e[4].Turns, Is.EqualTo(2));
            Assert.That(e[5].OfVictimMaxHp, Is.True);
            var a = g.Get("剁").AttackEffects;
            Assert.That((a[1].ScaleBy, a[1].ExecuteBelowPercent, a[1].ExecuteKills), Is.EqualTo((ScaleBasis.Morale, 35, true)));
            Assert.That(a[2].OnlyIf, Is.EqualTo(DamageCondition.MoraleFull));
        }

        [TestCase(@"{""kind"":""Shield"",""value"":5,""fill"":true}")]
        [TestCase(@"{""kind"":""Shield"",""value"":5,""counterPercent"":50}")]
        [TestCase(@"{""kind"":""Shield"",""value"":5,""ofVictimMaxHp"":true}")]
        [TestCase(@"{""kind"":""Block"",""value"":1,""scaleMin"":2}")]
        [TestCase(@"{""kind"":""Block"",""value"":1,""scaleBy"":""Morale"",""scaleMin"":2}")]   // 修复轮 1:按战意只写在 BlockMod 上
        [TestCase(@"{""kind"":""BlockMod"",""scaleBy"":""Morale""}")]
        [TestCase(@"{""kind"":""Amplify"",""value"":5,""scaleBy"":""Morale""}")]
        [TestCase(@"{""kind"":""Morale"",""value"":1,""scaleBy"":""BurnStack""}")]
        [TestCase(@"{""kind"":""HealSelf"",""value"":5,""scaleBy"":""ExtraHitTarget""}")]
        [TestCase(@"{""kind"":""Shield"",""value"":5,""scaleBy"":""Morale""}")]
        public void ConfigLoader_RejectsMisplacedTask1Fields(string effect)
        {
            Assert.Throws<Brushblade.Data.ConfigException>(() => Load(effect));
        }
    }
}
