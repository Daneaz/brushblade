using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using Brushblade.Data;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>Plan D2-土 Task 1:共用小扩展 E27–E36(附录 §3)。
    ///
    /// 夹具口径同 TraitModifierTests:Element.Heart(生克 1.0×)、PlayerAttack = 100(攻击力缩放恒等)、
    /// 暴击率缺省 0(不摇号)。卡等级系数:Lv1 = 100、Lv4 = 118、Lv5 = 124、Lv6 = 130、Lv8 = 142。</summary>
    public class EarthSharedExtensionTests
    {
        private static TraitDef Trait(TraitSlot slot, TraitFace face, TraitForm form, params EffectDef[] effects) =>
            new(slot, face, form, null, "土" + (int)slot + face, effects);

        private static EffectDef Reshape(int shieldStrike = 0, int armorStrike = 0, TargetArea shape = TargetArea.Single,
            int shots = 0, bool persist = false, int armorShield = 0, bool armorStrikeEach = false,
            ScaleBasis scaleBy = ScaleBasis.None, int scaleDivisor = 0, int scaleCap = 0) =>
            new(EffectKind.Reshape, 0, persistOnce: persist, shape: shape, shots: shots,
                shieldStrikePercent: shieldStrike, armorStrikePercent: armorStrike, armorShieldPercent: armorShield,
                armorStrikeEach: armorStrikeEach, scaleBy: scaleBy, scaleDivisor: scaleDivisor, scaleCap: scaleCap);

        private static CharDef TwoFace(params TraitDef[] traits) => new("试", Element.Heart,
            effects: new[] { new EffectDef(EffectKind.Shield, 60), new EffectDef(EffectKind.DefenseBuff, 10, turns: 2) },
            attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 100), new EffectDef(EffectKind.ArmorBreak, 5, turns: 2) },
            traits: traits);

        private static EffectDef FirstOf(IEnumerable<EffectDef> effects, EffectKind kind) => effects.First(e => e.Kind == kind);

        // ---------------- E27:strike 百分比同面相加 ----------------

        [Test]
        public void E27_TowerBaseAndSuppressTower_ArmorStrikeAddsTo800()
        {
            // 塔:Lv4 塔基 armorStrike 300 + Lv8 镇塔 armorStrike 500(同在攻面)= 800,不再被后者覆盖成 500
            var def = TwoFace(
                Trait(TraitSlot.Lv4, TraitFace.Attack, TraitForm.Passive, Reshape(armorStrike: 300)),
                Trait(TraitSlot.Lv8, TraitFace.Attack, TraitForm.Active, Reshape(armorStrike: 500),
                    new EffectDef(EffectKind.Weaken, 30, turns: 2)));
            Assert.That(FirstOf(TraitRules.CastEffects(def, CardFace.Attack, 5), EffectKind.DamageSingle).ArmorStrikePercent,
                Is.EqualTo(300), "Lv5 只有塔基");
            Assert.That(FirstOf(TraitRules.CastEffects(def, CardFace.Attack, 8), EffectKind.DamageSingle).ArmorStrikePercent,
                Is.EqualTo(800), "塔基 + 镇塔 = 800");
        }

        [Test]
        public void E27_EarthQuakeNoLongerLowersMountainPress()
        {
            // 㙓:Lv5 山压 shieldStrike 50 + Lv8 地动 shape All shieldStrike 30 → 80(旧口径被地动压成 30)
            var def = TwoFace(
                Trait(TraitSlot.Lv5, TraitFace.Attack, TraitForm.Active, Reshape(shieldStrike: 50)),
                Trait(TraitSlot.Lv8, TraitFace.Attack, TraitForm.Active, Reshape(shieldStrike: 30, shape: TargetArea.All)));
            var lv5 = FirstOf(TraitRules.CastEffects(def, CardFace.Attack, 5), EffectKind.DamageSingle);
            var lv8 = FirstOf(TraitRules.CastEffects(def, CardFace.Attack, 8), EffectKind.DamageSingle);
            Assert.That(lv5.ShieldStrikePercent, Is.EqualTo(50));
            Assert.That(lv8.ShieldStrikePercent, Is.EqualTo(80), "地动不再把山压的 50 压低成 30");
            Assert.That(lv8.Shape, Is.EqualTo(TargetArea.All), "其余字段照旧非缺省覆盖");
        }

        [Test]
        public void E27_RealData_FoldedStrikes_EqualBodyPlusSumOfReshapes()
        {
            // 现数据:每张字、每一面、每一级,折叠后首条伤害的 strike 百分比 = 本体 + 同面已生效 Reshape 之和
            // (逐位恒等另经 git archive 导出改动前代码对拍,见 task-1-report)
            foreach (var def in CharTableTests.RealGraph().All)
                foreach (var face in new[] { CardFace.Feature, CardFace.Attack })
                    for (int level = 1; level <= 10; level++)
                    {
                        if (face == CardFace.Attack && def.AttackEffects.Count == 0) continue;
                        var folded = TraitRules.CastEffects(def, face, level);
                        var dmg = folded.FirstOrDefault(e => e.Kind == EffectKind.DamageSingle);
                        if (dmg == null) continue;
                        var body = BattleEngine.EffectsOf(def, face == CardFace.Attack).FirstOrDefault(e => e.Kind == EffectKind.DamageSingle);
                        var reshapes = BattleEngine.EffectsOf(def, face == CardFace.Attack).Where(e => e.Kind == EffectKind.Reshape)
                            .Concat(TraitRules.Unlocked(def, level).Where(t => t.AppliesTo(face) && t.Trigger == TraitTrigger.Cast)
                                .SelectMany(t => t.Effects).Where(e => e.Kind == EffectKind.Reshape)).ToList();
                        if (body == null) continue;   // 首条伤害来自特性追加:不在本口径内
                        Assert.That(dmg.ShieldStrikePercent, Is.EqualTo(body.ShieldStrikePercent + reshapes.Sum(r => r.ShieldStrikePercent)),
                            $"{def.Id}/{face}/Lv{level} shieldStrike");
                        Assert.That(dmg.ArmorStrikePercent, Is.EqualTo(body.ArmorStrikePercent + reshapes.Sum(r => r.ArmorStrikePercent)),
                            $"{def.Id}/{face}/Lv{level} armorStrike");
                    }
        }

        // ---------------- E29:Reshape persist ----------------

        [Test]
        public void E29_ReshapePersist_OnFeatureFace_MakesFirstShieldPersist()
        {
            var def = new CharDef("试", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.Shield, 60), new EffectDef(EffectKind.Shield, 10) },
                attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 100) },
                traits: new[] { Trait(TraitSlot.Lv8, TraitFace.Feature, TraitForm.Active, Reshape(persist: true)) });
            var lv7 = TraitRules.CastEffects(def, CardFace.Feature, 7);
            var lv8 = TraitRules.CastEffects(def, CardFace.Feature, 8);
            Assert.That(lv7[0].PersistOnce, Is.False);
            Assert.That(lv8[0].PersistOnce, Is.True, "第一条护盾改为留存");
            Assert.That(lv8[1].PersistOnce, Is.False, "只改第一条");
            Assert.That(lv8.Any(e => e.Kind == EffectKind.Reshape), Is.False, "修饰器不进结算表");
        }

        [Test]
        public void E29_ReshapePersist_ShieldAllAlsoCounts()
        {
            var def = new CharDef("试", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.ShieldAll, 60) },
                traits: new[] { Trait(TraitSlot.Lv8, TraitFace.Feature, TraitForm.Active, Reshape(persist: true)) });
            Assert.That(TraitRules.CastEffects(def, CardFace.Feature, 8)[0].PersistOnce, Is.True);
        }

        // ---------------- E30:Reshape armorShield ----------------

        [Test]
        public void E30_ReshapeArmorShield_WritesFirstShield()
        {
            var def = TwoFace(Trait(TraitSlot.Lv4, TraitFace.Feature, TraitForm.Passive, Reshape(armorShield: 300)));
            var folded = TraitRules.CastEffects(def, CardFace.Feature, 4);
            Assert.That(FirstOf(folded, EffectKind.Shield).ArmorShieldPercent, Is.EqualTo(300));
            Assert.That(FirstOf(TraitRules.CastEffects(def, CardFace.Feature, 3), EffectKind.Shield).ArmorShieldPercent, Is.EqualTo(0));
        }

        // ---------------- E31 / E36:伤害字段随 Reshape 落到第一条伤害 ----------------

        [Test]
        public void E31_E36_ReshapeCarriesArmorStrikeEachAndShieldHits()
        {
            var def = TwoFace(Trait(TraitSlot.Lv8, TraitFace.Attack, TraitForm.Active,
                Reshape(armorStrike: 300, armorStrikeEach: true, shape: TargetArea.Scatter, shots: 3,
                    scaleBy: ScaleBasis.Shield, scaleDivisor: 100, scaleCap: 4)));
            var dmg = FirstOf(TraitRules.CastEffects(def, CardFace.Attack, 8), EffectKind.DamageSingle);
            Assert.That(dmg.ArmorStrikeEach, Is.True);
            Assert.That(dmg.ArmorStrikePercent, Is.EqualTo(300));
            Assert.That(dmg.ScaleBy, Is.EqualTo(ScaleBasis.Shield));
            Assert.That(dmg.ScaleDivisor, Is.EqualTo(100));
            Assert.That(dmg.ScaleCap, Is.EqualTo(4));
        }

        // ---------------- E34:AugmentField.Battle 与战后携带 ----------------

        private static EffectDef Guinie() =>
            new(EffectKind.Augment, 0, augmentKind: EffectKind.DefenseBuff, augmentField: AugmentField.Battle);

        [Test]
        public void E34_AugmentBattle_SetsFirstDefenseBuffTurnsToMinusOne_AndTurnsAddKeepsIt()
        {
            var def = TwoFace(
                Trait(TraitSlot.Lv4, TraitFace.Feature, TraitForm.Passive,
                    new EffectDef(EffectKind.Augment, 4, augmentKind: EffectKind.DefenseBuff, augmentField: AugmentField.Turns)),
                Trait(TraitSlot.Lv6, TraitFace.Feature, TraitForm.Passive, Guinie()));
            Assert.That(FirstOf(TraitRules.CastEffects(def, CardFace.Feature, 5), EffectKind.DefenseBuff).Turns, Is.EqualTo(6));
            Assert.That(FirstOf(TraitRules.CastEffects(def, CardFace.Feature, 6), EffectKind.DefenseBuff).Turns, Is.EqualTo(-1),
                "持续到战斗结束");
            var battleFirst = TwoFace(
                Trait(TraitSlot.Lv6, TraitFace.Feature, TraitForm.Passive, Guinie()),
                Trait(TraitSlot.Lv8, TraitFace.Feature, TraitForm.Passive,
                    new EffectDef(EffectKind.Augment, 4, augmentKind: EffectKind.DefenseBuff, augmentField: AugmentField.Turns)));
            Assert.That(FirstOf(TraitRules.CastEffects(battleFirst, CardFace.Feature, 8), EffectKind.DefenseBuff).Turns, Is.EqualTo(-1),
                "−1 之后再加回合不变(WithTurns 不加)");
        }

        [Test]
        public void E34_BattleScopedDefenseBuff_IsNotCarried()
        {
            // 携带白名单删掉 DefenseBuff(E34):本场持续(TurnsLeft −1)的护甲战后不跨场;厚 / 泉照旧
            var run = RebalanceFixture.Run();
            run.Battle.PlayerStatuses.Apply(new StatusEffect
            {
                Kind = StatusKind.DefenseBuff, Polarity = StatusPolarity.Buff, Magnitude = 10, TurnsLeft = -1, SourceId = "臬#0",
            });
            Assert.That(run.Battle.Cast("甲", 0), Is.EqualTo(BattleError.None));
            Assert.That(run.Battle.Phase, Is.EqualTo(BattlePhase.Won));
            run.AdvanceAfterBattle();
            Assert.That(run.CarriedStatuses.Any(s => s.Kind == StatusKind.DefenseBuff), Is.False,
                "本场持续的护甲战后不进 CarriedStatuses");
        }

        [Test]
        public void E34_GuinieArmor_LastsTheBattle_ButIsNotCarried()
        {
            var guinie = new CharDef("臬", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.DefenseBuff, 10, turns: 2) },
                traits: new[] { Trait(TraitSlot.Lv6, TraitFace.Feature, TraitForm.Passive, Guinie()) });
            var config = new RunConfig
            {
                Encounters = new[] { new[] { RebalanceFixture.Mob(hp: 20) }, new[] { RebalanceFixture.Mob(hp: 20) } },
                RewardPool = new[] { "甲" },
            };
            var run = new RunEngine(RebalanceFixture.Graph(guinie), config,
                new BattleConfig { PlayerMaxHp = RebalanceFixture.BaseMaxHp, PlayerAttack = 100 },
                new[] { "臬", "甲" }, Array.Empty<string>(), seed: 1,
                cardLevels: new Dictionary<string, int> { ["臬"] = 6 });
            Assert.That(run.Battle.Cast("臬"), Is.EqualTo(BattleError.None));
            var armor = run.Battle.PlayerStatuses.Find(StatusKind.DefenseBuff);
            Assert.That(armor, Is.Not.Null);
            Assert.That(armor.TurnsLeft, Is.EqualTo(-1), "圭臬:本场持续");
            Assert.That(run.Battle.Cast("甲", 0), Is.EqualTo(BattleError.None));
            Assert.That(run.Battle.Phase, Is.EqualTo(BattlePhase.Won));
            run.AdvanceAfterBattle();
            Assert.That(run.CarriedStatuses.Any(s => s.Kind == StatusKind.DefenseBuff), Is.False,
                "圭臬的护甲战后不进 CarriedStatuses(E34:删掉携带白名单的 DefenseBuff 项)");
        }

        // ---------------- 结算分支(Task 1 提交 ②)----------------

        private static BattleConfig Config => new() { PlayerMaxHp = RebalanceFixture.BaseMaxHp, PlayerAttack = 100 };

        private static SummonSnapshot Wood(int slot) => new()
        {
            Slot = slot, Char = "木", Element = Element.Wood, Hp = 100, MaxHp = 100, Attack = 0, Speed = 100,
        };

        private static BattleEngine Battle(CharDef def, int level = 1, IReadOnlyList<SummonSnapshot> summons = null,
            int normalShield = 0, IReadOnlyList<StatusEffect> statuses = null, params EnemyDef[] enemies) =>
            new(RebalanceFixture.Graph(def), Config, new[] { def.Id, def.Id, def.Id }, Array.Empty<string>(),
                enemies.Length > 0 ? enemies : new[] { RebalanceFixture.Mob() }, seed: 1,
                cardLevels: new Dictionary<string, int> { [def.Id] = level },
                startingNormalShield: normalShield, startingSummons: summons, startingStatuses: statuses);

        private static StatusEffect Armor(int points) => new()
        {
            Kind = StatusKind.DefenseBuff, Polarity = StatusPolarity.Buff, Magnitude = points, TurnsLeft = 99, SourceId = "甲#armor",
        };

        /// <summary>无新字段时护盾路径逐位恒等(Shield / ShieldAll / ShieldSummons 的盾量、木灵护盾、攒厚)。
        /// 期望值取自改动前代码(git archive 261f99f1 导出跑同一条用例)。</summary>
        [Test]
        public void ShieldPath_WithoutNewFields_IsUnchanged()
        {
            var def = RebalanceFixture.Char("盾", new EffectDef(EffectKind.Shield, 60),
                new EffectDef(EffectKind.ShieldAll, 30), new EffectDef(EffectKind.ShieldSummons, 20));
            var b = Battle(def, 5, new[] { Wood(1), Wood(2) });
            Assert.That(b.Cast("盾"), Is.EqualTo(BattleError.None));
            Assert.That(new[] { b.PlayerShield, b.Summons[1].Shield, b.Summons[2].Shield, b.ShieldAccum, b.HeftStacks },
                Is.EqualTo(new[] { 113, 63, 63, 38, 1 }));
        }

        [Test]
        public void E28_ShieldSummonsOfShield_GivesEachSummonPercentOfNominalShield_NoExtraHeft()
        {
            var plain = Battle(RebalanceFixture.Char("盾", new EffectDef(EffectKind.Shield, 100)), 1, new[] { Wood(1), Wood(2) });
            plain.Cast("盾");
            var def = RebalanceFixture.Char("盾", new EffectDef(EffectKind.Shield, 100), new EffectDef(EffectKind.ShieldSummons, 50, ofShield: true));
            var b = Battle(def, 5, new[] { Wood(1), Wood(2) });
            b.Cast("盾");
            int nominal = MetaRules.ScaleByCardLevel(100, 5);   // 124:本次出字名义护盾(放大后)
            Assert.That(b.Summons[1].Shield, Is.EqualTo(nominal * 50 / 100), "每只木灵 = 名义护盾 × 50%,百分比不吃等级");
            Assert.That(b.Summons[2].Shield, Is.EqualTo(nominal * 50 / 100));
            var lv5Plain = Battle(RebalanceFixture.Char("盾", new EffectDef(EffectKind.Shield, 100)), 5, new[] { Wood(1), Wood(2) });
            lv5Plain.Cast("盾");
            Assert.That(b.ShieldAccum, Is.EqualTo(lv5Plain.ShieldAccum), "ofShield 不额外攒厚");
            Assert.That(b.HeftStacks, Is.EqualTo(lv5Plain.HeftStacks));
        }

        [Test]
        public void E30_ArmorShield_AddsArmorPercentAfterAttackScaling()
        {
            var def = RebalanceFixture.Char("塔", new EffectDef(EffectKind.Shield, 60, armorShieldPercent: 300));
            var b = Battle(def, statuses: new[] { Armor(10) });
            b.Cast("塔");
            Assert.That(b.PlayerShield, Is.EqualTo(60 + 30), "60 + 当前护甲 10 × 300%");
            var none = Battle(def);
            none.Cast("塔");
            Assert.That(none.PlayerShield, Is.EqualTo(60), "没有护甲时只有本体");
        }

        [Test]
        public void E31_ArmorStrikeEach_StrikesEveryShot()
        {
            int Strikes(bool each)
            {
                var def = RebalanceFixture.Char("垚", new EffectDef(EffectKind.DamageSingle, 10, shape: TargetArea.Scatter, shots: 3,
                    armorStrikePercent: 100, armorStrikeEach: each));
                var b = Battle(def, statuses: new[] { Armor(10) }, enemies: new[] { RebalanceFixture.Mob(), RebalanceFixture.Mob() });
                b.Cast("垚");
                return b.LastEvents.Count(e => e.Kind == BattleEventKind.Damage && e.Source == EffectSource.ArmorStrike);
            }
            Assert.That(Strikes(false), Is.EqualTo(1), "原口径:只对首个目标结算一次");
            Assert.That(Strikes(true), Is.EqualTo(3), "逐发:每一发后各结算一次");
        }

        [Test]
        public void E36_HitsPerShield_UsesPreCastShield_CappedExtraHits()
        {
            int Hits(int shield, int cap)
            {
                var def = RebalanceFixture.Char("崩", new EffectDef(EffectKind.Shield, 500),
                    new EffectDef(EffectKind.DamageSingle, 10, scaleBy: ScaleBasis.Shield, scaleDivisor: 100, scaleCap: cap));
                var b = Battle(def, normalShield: shield);
                b.Cast("崩", 0);
                return b.LastEvents.Count(e => e.Kind == BattleEventKind.Damage && e.Source == EffectSource.None);
            }
            Assert.That(Hits(0, 4), Is.EqualTo(1), "出字前 0 盾:1 击(本面先加的 500 盾不算,R3)");
            Assert.That(Hits(250, 4), Is.EqualTo(3), "250 盾:+2 击");
            Assert.That(Hits(500, 4), Is.EqualTo(5), "上限 +4 击");
            Assert.That(Hits(500, 0), Is.EqualTo(6), "cap 0 = 不设上限(护盾上限 = 最大生命 500)");
        }

        private static CharDef Mountain(int minArmor) => RebalanceFixture.Char("岳",
            new EffectDef(EffectKind.Retaliate, 1, turns: 4, minArmor: minArmor,
                perHit: new[] { new EffectDef(EffectKind.ArmorBreak, 5, turns: 2) }));

        [Test]
        public void E32_RetaliateMinArmor_GatesWithoutConsumingTheCharge()
        {
            var low = Battle(Mountain(50), statuses: new[] { Armor(40) }, enemies: RebalanceFixture.Mob(attack: 10));
            low.Cast("岳");
            low.EndTurn();
            Assert.That(low.Enemies[0].Statuses.Has(StatusKind.ArmorBreak), Is.False, "护甲 40 < 50:不回敬");
            Assert.That(low.Capture().TraitUsesThisBattle.Keys.Any(k => k.StartsWith("回敬:")), Is.False, "门槛不满足不扣次数");

            var high = Battle(Mountain(50), statuses: new[] { Armor(50) }, enemies: RebalanceFixture.Mob(attack: 10));
            high.Cast("岳");
            high.EndTurn();
            Assert.That(high.Enemies[0].Statuses.Has(StatusKind.ArmorBreak), Is.True, "护甲 ≥ 50:回敬破甲");
        }

        [Test]
        public void E32_MinArmor_ClonesAndSurvivesRealSaveFile()
        {
            var s = new StatusEffect { Kind = StatusKind.Retaliate, MinArmor = 50 };
            Assert.That(s.Clone().MinArmor, Is.EqualTo(50));

            var mountain = Mountain(50);
            var graph = RebalanceFixture.Graph(mountain);
            var enemy = RebalanceFixture.Mob(attack: 0);
            var runConfig = new RunConfig { Encounters = new[] { new[] { enemy }, new[] { enemy } }, RewardPool = new[] { "岳" } };
            var levels = new Dictionary<string, int> { ["岳"] = 1 };
            var run = new RunEngine(graph, runConfig, Config, new[] { "岳", "岳" }, Array.Empty<string>(), 3, cardLevels: levels);
            Assert.That(run.Battle.Cast("岳"), Is.EqualTo(BattleError.None));
            var meta = new MetaState
            {
                EndlessV2 = new EndlessSaveState
                {
                    Depth = 3, Seed = 999,
                    InProgress = new InProgressRun { FromDepth = 1, FirstTowerSegment = true, Run = run.Capture() },
                },
            };
            var reloaded = SaveSerializer.FromJson(SaveSerializer.ToJson(meta));
            var restored = RunEngine.Restore(reloaded.EndlessV2.InProgress.Run, graph, runConfig, Config, levels);
            Assert.That(restored.Battle.PlayerStatuses.Find(StatusKind.Retaliate).MinArmor, Is.EqualTo(50), "MinArmor 进存档 JSON 往返");
        }

        [Test]
        public void E33_Silence_SupportsPickAndOnlyIf()
        {
            Assert.That(EffectPickRules.Supports(EffectKind.Silence), Is.True);
            var all = Battle(RebalanceFixture.Char("杜", new EffectDef(EffectKind.Silence, 0, turns: 2, pick: EffectPick.All)),
                enemies: new[] { RebalanceFixture.Mob(), RebalanceFixture.Mob() });
            all.Cast("杜");
            Assert.That(all.Enemies.All(e => e.Statuses.Has(StatusKind.Silence)), Is.True, "pick All:全体封禁");

            var gated = Battle(RebalanceFixture.Char("杜", new EffectDef(EffectKind.Silence, 0, turns: 2, pick: EffectPick.All,
                onlyIf: DamageCondition.NotBoss)), enemies: new[] { RebalanceFixture.Boss(), RebalanceFixture.Mob() });
            gated.Cast("杜");
            Assert.That(gated.Enemies[0].Statuses.Has(StatusKind.Silence), Is.False, "if NotBoss:Boss 不吃");
            Assert.That(gated.Enemies[1].Statuses.Has(StatusKind.Silence), Is.True);

            var primary = Battle(RebalanceFixture.Char("杜", new EffectDef(EffectKind.Silence, 0, turns: 2)),
                enemies: new[] { RebalanceFixture.Mob(), RebalanceFixture.Mob() });
            primary.Cast("杜", 1);
            Assert.That(primary.Enemies[0].Statuses.Has(StatusKind.Silence), Is.False, "缺省仍只封主目标");
            Assert.That(primary.Enemies[1].Statuses.Has(StatusKind.Silence), Is.True);
        }

        // ---------------- ConfigLoader ----------------

        /// <summary>一张两面土字:固面 Shield + DefenseBuff,攻面 DamageSingle。traitFace 指定特性写在哪一面。</summary>
        private static RecipeGraph Load(string traitEffects, string traitFace = "Feature") => ConfigLoader.LoadGraph(
            @"{""chars"":[{""id"":""塔"",""element"":""Earth"",""effects"":[{""kind"":""Shield"",""value"":60},{""kind"":""DefenseBuff"",""value"":10,""turns"":2}],
              ""attackEffects"":[{""kind"":""DamageSingle"",""value"":100}],
              ""traits"":[{""slot"":""Lv8"",""face"":""" + traitFace + @""",""form"":""Passive"",""name"":""修"",""effects"":[" + traitEffects + @"]}]}]}");

        [Test]
        public void Loader_ReadsSharedModifierFields()
        {
            var persist = Load(@"{""kind"":""Reshape"",""value"":0,""persistOnce"":true}").Get("塔").Traits[0].Effects[0];
            Assert.That(persist.PersistOnce, Is.True);
            var armorShield = Load(@"{""kind"":""Reshape"",""value"":0,""armorShieldPercent"":300}").Get("塔").Traits[0].Effects[0];
            Assert.That(armorShield.ArmorShieldPercent, Is.EqualTo(300));
            var each = Load(@"{""kind"":""Reshape"",""value"":0,""shape"":""Scatter"",""shots"":3,""armorStrikePercent"":300,""armorStrikeEach"":true}",
                "Attack").Get("塔").Traits[0].Effects[0];
            Assert.That(each.ArmorStrikeEach, Is.True);
            var hits = Load(@"{""kind"":""Reshape"",""value"":0,""scaleBy"":""Shield"",""scaleDivisor"":100,""scaleCap"":4}",
                "Attack").Get("塔").Traits[0].Effects[0];
            Assert.That(hits.ScaleBy, Is.EqualTo(ScaleBasis.Shield));
            Assert.That(hits.ScaleDivisor, Is.EqualTo(100));
            Assert.That(hits.ScaleCap, Is.EqualTo(4));
            var battle = Load(@"{""kind"":""Augment"",""value"":0,""augmentKind"":""DefenseBuff"",""augmentField"":""Battle""}")
                .Get("塔").Traits[0].Effects[0];
            Assert.That(battle.AugmentField, Is.EqualTo(AugmentField.Battle));
            var ofShield = Load(@"{""kind"":""ShieldSummons"",""value"":50,""ofShield"":true}").Get("塔").Traits[0].Effects[0];
            Assert.That(ofShield.OfShield, Is.True);
            var minArmor = Load(@"{""kind"":""Retaliate"",""value"":1,""turns"":4,""minArmor"":50,""perHit"":[{""kind"":""ArmorBreak"",""value"":38,""turns"":2}]}")
                .Get("塔").Traits[0].Effects[0];
            Assert.That(minArmor.MinArmor, Is.EqualTo(50));
        }

        [TestCase(@"{""kind"":""Reshape"",""value"":0,""persistOnce"":true}", "Attack")]                       // 伤害面上的 persist
        [TestCase(@"{""kind"":""Reshape"",""value"":0,""armorShieldPercent"":300}", "Attack")]                 // 伤害面上的 armorShield
        [TestCase(@"{""kind"":""Shield"",""value"":10,""armorShieldPercent"":300}", "Feature")]                // armorShield 只给 Reshape
        [TestCase(@"{""kind"":""DefenseBuff"",""value"":10,""turns"":2,""persistOnce"":true}", "Feature")]    // persistOnce 宿主不对
        [TestCase(@"{""kind"":""Reshape"",""value"":0,""armorStrikeEach"":true}", "Attack")]                   // 逐发镇压没有镇压
        [TestCase(@"{""kind"":""Reshape"",""value"":0,""scaleBy"":""Shield""}", "Attack")]                     // 缺除数
        [TestCase(@"{""kind"":""Reshape"",""value"":0,""scaleDivisor"":100}", "Attack")]                      // 除数没配 scaleBy Shield
        [TestCase(@"{""kind"":""Amplify"",""value"":10,""scaleBy"":""Shield"",""scaleDivisor"":100}", "Attack")] // Shield 档只给伤害
        [TestCase(@"{""kind"":""Augment"",""value"":0,""augmentKind"":""Shield"",""augmentField"":""Battle""}", "Feature")] // Battle 只配 DefenseBuff
        [TestCase(@"{""kind"":""Shield"",""value"":10,""ofShield"":true}", "Feature")]                         // ofShield 只给 ShieldSummons
        [TestCase(@"{""kind"":""Shield"",""value"":10,""minArmor"":50}", "Feature")]                           // minArmor 只给 Retaliate
        public void Loader_RejectsBadSharedModifier(string effect, string face)
        {
            Assert.Throws<ConfigException>(() => Load(effect, face));
        }
    }
}
