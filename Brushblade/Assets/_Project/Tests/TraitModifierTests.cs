using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>本字修饰器(D1 Task 3,附录 M1 / M2 / M3):Amplify / Reshape 在出字前折叠进本体,
    /// 不进结算循环;伤害标记 ForceCrit / ArmorIgnorePercent / ShieldStrikePercent;条件扩展。
    ///
    /// 夹具口径同 TraitActiveTests:Element.Heart(生克 1.0×)、PlayerAttack = 100(ScaleByAttack 恒等)、
    /// 无甲 10 万血靶子、暴击率缺省 0(不摇号)。卡等级系数 = MetaRules.ScaleByCardLevel:
    /// Lv4 = ⌈100 × 1.18⌉ = 118,Lv5 = 124,Lv6 = 130。Amplify 在卡等级之后乘 (100 + Σ)/100 向上取整。</summary>
    public class TraitModifierTests
    {
        private const int Hp = 100000;

        private static EffectDef Amp(int percent, AmpScope scope = AmpScope.Damage,
            DamageCondition onlyIf = DamageCondition.None) =>
            new(EffectKind.Amplify, percent, scope: scope, onlyIf: onlyIf);

        private static TraitDef Trait(TraitSlot slot, TraitForm form, params EffectDef[] effects) =>
            new(slot, TraitFace.Both, form, null, "修" + (int)slot, effects);

        private static BattleEngine Battle(CharDef def, int level, BattleConfig config = null, params EnemyDef[] enemies) =>
            new(RebalanceFixture.Graph(def),
                config ?? new BattleConfig { PlayerMaxHp = RebalanceFixture.BaseMaxHp, PlayerAttack = 100 },
                new[] { def.Id, def.Id, def.Id }, Array.Empty<string>(),
                enemies.Length > 0 ? enemies : new[] { RebalanceFixture.Mob() }, seed: 1,
                cardLevels: new Dictionary<string, int> { [def.Id] = level });

        private static CharDef Dmg(params TraitDef[] traits) => new("试", Element.Heart,
            effects: new[] { new EffectDef(EffectKind.DamageSingle, 100) }, traits: traits);

        private static StatusEffect Burn() => new()
        {
            Kind = StatusKind.Burn, Polarity = StatusPolarity.Debuff, Magnitude = 1, TurnsLeft = -1,
        };

        // ---------------- Amplify ----------------

        [Test]
        public void Amplify_Unconditional_AddsPercentToDamage()
        {
            var def = Dmg(Trait(TraitSlot.Lv4, TraitForm.Passive, Amp(10)));   // 精进 +10(被动·修饰本字)
            var lv4 = Battle(def, 4);
            lv4.Cast("试", 0);
            // 118 × 110 / 100 = 129.8 → 130
            Assert.That(Hp - lv4.Enemies[0].Hp, Is.EqualTo(130));

            var lv3 = Battle(def, 3);
            lv3.Cast("试", 0);
            // Lv3 未解锁:100 × 1.12 = 112,不吃加成
            Assert.That(Hp - lv3.Enemies[0].Hp, Is.EqualTo(112));
        }

        [Test]
        public void Amplify_OnlyIfBurning_UsesPreCastSnapshot()
        {
            // 本体先上灼再打;爆燃:目标带灼时伤害 +30%
            var def = new CharDef("试", Element.Heart, effects: new[]
            {
                new EffectDef(EffectKind.BurnSingle, 1),
                new EffectDef(EffectKind.DamageSingle, 100),
            }, traits: new[] { Trait(TraitSlot.Lv5, TraitForm.Active, Amp(30, onlyIf: DamageCondition.Burning)) });

            var fresh = Battle(def, 5);
            fresh.Cast("试", 0);
            // 出字前没灼(R3 快照):124,不吃加成
            Assert.That(Hp - fresh.Enemies[0].Hp, Is.EqualTo(124));

            var burning = Battle(def, 5);
            burning.Enemies[0].Statuses.Apply(Burn());
            burning.Cast("试", 0);
            // 出字前已带灼:124 × 130 / 100 = 161.2 → 162
            Assert.That(Hp - burning.Enemies[0].Hp, Is.EqualTo(162));
        }

        [Test]
        public void Amplify_TwoTerms_SameAxis_Add()
        {
            var def = Dmg(
                Trait(TraitSlot.Lv4, TraitForm.Passive, Amp(10)),
                Trait(TraitSlot.Lv5, TraitForm.Active, Amp(30)));
            var b = Battle(def, 5);
            b.Cast("试", 0);
            // 同轴相加:124 × 140 / 100 = 173.6 → 174(连乘会是 ⌈⌈124×1.1⌉×1.3⌉ = 179)
            Assert.That(Hp - b.Enemies[0].Hp, Is.EqualTo(174));
        }

        [Test]
        public void Amplify_ShieldScope_LeavesDamageAlone()
        {
            var def = new CharDef("试", Element.Heart, effects: new[]
            {
                new EffectDef(EffectKind.DamageSingle, 100),
                new EffectDef(EffectKind.Shield, 50),
            }, traits: new[] { Trait(TraitSlot.Lv4, TraitForm.Passive, Amp(50, AmpScope.Shield)) });
            var b = Battle(def, 4);
            b.Cast("试", 0);
            Assert.That(Hp - b.Enemies[0].Hp, Is.EqualTo(118), "伤害不吃护盾加成:100 × 1.18 = 118");
            // 护盾:50 × 1.18 = 59;59 × 150 / 100 = 88.5 → 89
            Assert.That(b.PlayerShield, Is.EqualTo(89));
        }

        [Test]
        public void Amplify_CounterScope_RaisesBlockCounter()
        {
            // 铠面格挡,攻面本体 100;回锋(Lv6 被动):反击 +50%
            CharDef Def(params TraitDef[] traits) => new("试", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.Block, 1) },
                attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 100) },
                traits: traits);

            var plain = Battle(Def(), 6);
            plain.Cast("试", -1);
            // 反击 = 100 × 1.30(Lv6)= 130 × 30% = 39
            Assert.That(plain.PlayerStatuses.Find(StatusKind.Block).CounterDamage, Is.EqualTo(39));

            var boosted = Battle(Def(Trait(TraitSlot.Lv6, TraitForm.Passive, Amp(50, AmpScope.Counter))), 6);
            boosted.Cast("试", -1);
            // 39 × 150 / 100 = 58.5 → 59
            Assert.That(boosted.PlayerStatuses.Find(StatusKind.Block).CounterDamage, Is.EqualTo(59));
        }

        // ---------------- Reshape ----------------

        private static EnemyDef[] FrontRowAndBack() => new[]
        {
            RebalanceFixture.Mob(), RebalanceFixture.Mob(), RebalanceFixture.Mob(),
            new EnemyDef("后", Element.Heart, Hp, 0, row: EnemyRow.Back),
        };

        [Test]
        public void Reshape_Row50_HitsWholeRow()
        {
            // 碎石:改为横扫,非主目标 50%
            var def = Dmg(Trait(TraitSlot.Lv5, TraitForm.Active,
                new EffectDef(EffectKind.Reshape, 0, shape: TargetArea.Row, shapePercent: 50)));
            var b = Battle(def, 5, null, FrontRowAndBack());
            b.Cast("试", 0);
            Assert.That(Hp - b.Enemies[0].Hp, Is.EqualTo(124), "主目标全额 124");
            Assert.That(Hp - b.Enemies[1].Hp, Is.EqualTo(62), "同排 124 × 50% = 62");
            Assert.That(Hp - b.Enemies[2].Hp, Is.EqualTo(62));
            Assert.That(b.Enemies[3].Hp, Is.EqualTo(Hp), "后排不吃横扫");
        }

        [Test]
        public void Reshape_AllWithPercent_EveryTargetTakesPercent()
        {
            // 怒涛:改为全体,各 60%(新语义:All 也吃 ShapePercent,主目标同样打折)
            var def = Dmg(Trait(TraitSlot.Lv8, TraitForm.Active,
                new EffectDef(EffectKind.Reshape, 0, shape: TargetArea.All, shapePercent: 60)));
            var b = Battle(def, 8, null, FrontRowAndBack());
            b.Cast("试", -1);
            // Lv8:100 × 1.42 = 142;142 × 60 / 100 = 85.2 → 85(伤害折算向下取整,同 ShapePercent 既有口径)
            foreach (var e in b.Enemies)
                Assert.That(Hp - e.Hp, Is.EqualTo(85));
        }

        [Test]
        public void Reshape_HitsAndPercent_TwoHitsAt60()
        {
            // 连斩:打 2 击,每击 60%
            var def = Dmg(Trait(TraitSlot.Lv5, TraitForm.Active,
                new EffectDef(EffectKind.Reshape, 0, hitCount: 2, hitPercent: 60)));
            var b = Battle(def, 5);
            b.Cast("试", 0);
            // 每击 124 × 60 / 100 = 74.4 → 74,两击 148
            var hits = b.LastEvents.Where(e => e.Kind == BattleEventKind.Damage).ToList();
            Assert.That(hits.Count, Is.EqualTo(2));
            Assert.That(hits.All(h => h.Amount == 74), Is.True);
            Assert.That(Hp - b.Enemies[0].Hp, Is.EqualTo(148));
        }

        [Test]
        public void Reshape_NoDamageOnFace_IsNoOp()
        {
            // 本面只有护盾:改形 / 伤害加成都空转,不报错;护盾照常 50 × 1.24 = 62
            var def = new CharDef("试", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.Shield, 50) },
                traits: new[]
                {
                    Trait(TraitSlot.Lv4, TraitForm.Passive, Amp(30)),
                    Trait(TraitSlot.Lv5, TraitForm.Active,
                        new EffectDef(EffectKind.Reshape, 0, shape: TargetArea.Row, shapePercent: 50)),
                });
            var b = Battle(def, 5);
            Assert.That(b.Cast("试", -1), Is.EqualTo(BattleError.None));
            Assert.That(b.PlayerShield, Is.EqualTo(62));
            Assert.That(b.Enemies[0].Hp, Is.EqualTo(Hp));
        }

        [Test]
        public void Fold_DoesNotMutateSharedBody_AndIsIdentityWithoutModifiers()
        {
            var body = new EffectDef(EffectKind.DamageSingle, 100);
            var def = new CharDef("试", Element.Heart, effects: new[] { body }, traits: new[]
            {
                Trait(TraitSlot.Lv5, TraitForm.Active,
                    new EffectDef(EffectKind.Reshape, 0, shape: TargetArea.Row, shapePercent: 50), Amp(20)),
            });
            var folded = TraitRules.Fold(def.Effects, def, CardFace.Feature, 5);
            Assert.That(folded.Count, Is.EqualTo(1), "修饰器不进结算列表");
            Assert.That(folded[0].Shape, Is.EqualTo(TargetArea.Row));
            Assert.That(folded[0].ShapePercent, Is.EqualTo(50));
            Assert.That(folded[0].AmpTerms.Count, Is.EqualTo(1));
            Assert.That(folded[0].AmpTerms[0].Percent, Is.EqualTo(20));
            Assert.That(ReferenceEquals(folded[0], body), Is.False, "折叠产出新 EffectDef");
            Assert.That(body.Shape, Is.EqualTo(TargetArea.Single), "共享的字表对象不被改");
            Assert.That(body.AmpTerms.Count, Is.EqualTo(0));

            var unfolded = TraitRules.Fold(def.Effects, def, CardFace.Feature, 4);
            Assert.That(ReferenceEquals(unfolded[0], body), Is.True, "没有修饰器时原样返回同一对象");
        }

        // ---------------- 伤害标记 ----------------

        [Test]
        public void ForceCrit_DoesNotConsumeRandom()
        {
            var config = new BattleConfig { PlayerMaxHp = RebalanceFixture.BaseMaxHp, PlayerAttack = 100, PlayerCritChance = 50 };
            var forced = Battle(new CharDef("试", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 100, forceCrit: true) }), 1, config);
            uint before = forced.Capture().RandomState;
            forced.Cast("试", 0);
            Assert.That(forced.Capture().RandomState, Is.EqualTo(before), "必暴走 chance 100 短路,不摇号");
            // 100 × 150% = 150
            Assert.That(Hp - forced.Enemies[0].Hp, Is.EqualTo(150));

            var rolled = Battle(Dmg(), 1, config);
            uint before2 = rolled.Capture().RandomState;
            rolled.Cast("试", 0);
            Assert.That(rolled.Capture().RandomState, Is.Not.EqualTo(before2), "对照:50% 暴击会摇一次");
        }

        [Test]
        public void ArmorIgnore50_HalvesEffectiveArmor()
        {
            var armored = RebalanceFixture.Mob(armor: 100);
            var plain = Battle(Dmg(), 1, null, armored);
            plain.Cast("试", 0);
            // 护甲 100:100 × 100 / 200 = 50
            Assert.That(Hp - plain.Enemies[0].Hp, Is.EqualTo(50));

            var ignore = Battle(new CharDef("试", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 100, armorIgnorePercent: 50) }),
                1, null, RebalanceFixture.Mob(armor: 100));
            ignore.Cast("试", 0);
            // 有效护甲 100 × 50% = 50:100 × 100 / 150 = 66
            Assert.That(Hp - ignore.Enemies[0].Hp, Is.EqualTo(66));
        }

        [Test]
        public void ShieldStrike40_AddsFortyPercentOfShield()
        {
            CharDef Def(int strike) => new("试", Element.Heart, effects: new[]
            {
                new EffectDef(EffectKind.Shield, 100),
                new EffectDef(EffectKind.DamageSingle, 100, shieldStrikePercent: strike),
            });
            var plain = Battle(Def(0), 1);
            plain.Cast("试", 0);
            var strike = Battle(Def(40), 1);
            strike.Cast("试", 0);
            Assert.That(strike.PlayerShield, Is.EqualTo(100));
            // 加盾会攒厚(厚加攻击%),所以本体伤害不是裸 100(实测 105);按护盾加伤的那一份 = 护盾 100 × 40% = 40
            Assert.That((Hp - strike.Enemies[0].Hp) - (Hp - plain.Enemies[0].Hp), Is.EqualTo(40));
        }

        [Test]
        public void Reshape_WritesDamageMarkers()
        {
            // 震地式:Reshape 写 ArmorStrike / ForceCrit 等标记到首条 DamageSingle
            var def = Dmg(Trait(TraitSlot.Lv5, TraitForm.Active,
                new EffectDef(EffectKind.Reshape, 0, forceCrit: true, armorIgnorePercent: 50, shieldStrikePercent: 40,
                    armorStrikePercent: 300)));
            var folded = TraitRules.Fold(def.Effects, def, CardFace.Feature, 5);
            Assert.That(folded[0].ForceCrit, Is.True);
            Assert.That(folded[0].ArmorIgnorePercent, Is.EqualTo(50));
            Assert.That(folded[0].ShieldStrikePercent, Is.EqualTo(40));
            Assert.That(folded[0].ArmorStrikePercent, Is.EqualTo(300));
        }

        // ---------------- 条件扩展 ----------------

        /// <summary>Lv5 主动「伤害 +100%(条件)」:满足 → 124 × 2 = 248;不满足 → 124。</summary>
        private static CharDef CondChar(DamageCondition cond, Element element = Element.Heart) =>
            new("试", element, effects: new[] { new EffectDef(EffectKind.DamageSingle, 100) },
                traits: new[] { Trait(TraitSlot.Lv5, TraitForm.Active, Amp(100, onlyIf: cond)) });

        private static int Dealt(DamageCondition cond, Action<BattleEngine> setup, EnemyDef enemy = null,
            Element element = Element.Heart)
        {
            var b = Battle(CondChar(cond, element), 5, null, enemy ?? RebalanceFixture.Mob());
            setup?.Invoke(b);
            int before = b.Enemies[0].Hp;
            b.Cast("试", 0);
            return before - b.Enemies[0].Hp;
        }

        [Test]
        public void Condition_TargetHpBelow30_And_PlayerHpBelow50_FirstCast_Countering()
        {
            // 目标生命 < 30%:20000 / 100000 = 20% 满足;满血不满足
            Assert.That(Dealt(DamageCondition.TargetHpBelow30, b => b.Enemies[0].Hp = 20000), Is.EqualTo(248));
            Assert.That(Dealt(DamageCondition.TargetHpBelow30, null), Is.EqualTo(124));

            // 目标生命 > 70%:满血满足;50% 不满足
            Assert.That(Dealt(DamageCondition.TargetHpAbove70, null), Is.EqualTo(248));
            Assert.That(Dealt(DamageCondition.TargetHpAbove70, b => b.Enemies[0].Hp = 50000), Is.EqualTo(124));

            // 我方生命 < 50%:500 − 300 = 200(40%)满足;满血不满足
            Assert.That(Dealt(DamageCondition.PlayerHpBelow50, b => b.DamagePlayerForTest(300)), Is.EqualTo(248));
            Assert.That(Dealt(DamageCondition.PlayerHpBelow50, null), Is.EqualTo(124));

            // 本回合第一张:同回合第一张满足,第二张不满足(CastsThisTurn 在出字成功后才 +1)
            var first = Battle(CondChar(DamageCondition.FirstCastThisTurn), 5);
            first.Cast("试", 0);
            Assert.That(Hp - first.Enemies[0].Hp, Is.EqualTo(248));
            first.Cast("试", 0);
            Assert.That(Hp - first.Enemies[0].Hp, Is.EqualTo(248 + 124));

            // 克制:水克火 —— 124 × 2 = 248,再过生克 ×1.5 = 372(相克即破甲,靶子本就无甲);
            // 水打心不克制:124 × 1.0
            var fire = new EnemyDef("炎", Element.Fire, Hp, 0);
            Assert.That(Dealt(DamageCondition.Countering, null, fire, Element.Water), Is.EqualTo(372));
            Assert.That(Dealt(DamageCondition.Countering, null, null, Element.Water), Is.EqualTo(124));
        }

        [Test]
        public void Condition_Slowed_Frozen_PlayerHasArmor()
        {
            var slow = new StatusEffect
            {
                Kind = StatusKind.SpeedModifier, Polarity = StatusPolarity.Debuff, Magnitude = -50, TurnsLeft = 2,
            };
            var freeze = new StatusEffect
            {
                Kind = StatusKind.Freeze, Polarity = StatusPolarity.Debuff, Magnitude = 1, TurnsLeft = 1,
            };
            Assert.That(Dealt(DamageCondition.Slowed, b => b.Enemies[0].Statuses.Apply(slow.Clone())), Is.EqualTo(248));
            Assert.That(Dealt(DamageCondition.Slowed, b => b.Enemies[0].Statuses.Apply(freeze.Clone())), Is.EqualTo(124),
                "冻结不算减速(Slowed 从 Controlled 拆出)");
            Assert.That(Dealt(DamageCondition.Frozen, b => b.Enemies[0].Statuses.Apply(freeze.Clone())), Is.EqualTo(248));
            Assert.That(Dealt(DamageCondition.Frozen, b => b.Enemies[0].Statuses.Apply(slow.Clone())), Is.EqualTo(124));

            var armor = new StatusEffect
            {
                Kind = StatusKind.DefenseBuff, Polarity = StatusPolarity.Buff, Magnitude = 5, TurnsLeft = 2,
            };
            Assert.That(Dealt(DamageCondition.PlayerHasArmor, b => b.PlayerStatuses.Apply(armor.Clone())), Is.EqualTo(248));
            Assert.That(Dealt(DamageCondition.PlayerHasArmor, null), Is.EqualTo(124));
        }

        // ---------------- 只读查询 ----------------

        [Test]
        public void AttackShapeOf_WithLevel_SeesReshape()
        {
            var row = Dmg(Trait(TraitSlot.Lv5, TraitForm.Active,
                new EffectDef(EffectKind.Reshape, 0, shape: TargetArea.Row, shapePercent: 50)));
            Assert.That(BattleEngine.AttackShapeOf(row, false, 5).Shape, Is.EqualTo(TargetArea.Row));
            Assert.That(BattleEngine.AttackShapeOf(row, false, 4).Shape, Is.EqualTo(TargetArea.Single), "Lv5 未解锁");
            Assert.That(BattleEngine.AttackShapeOf(row).Shape, Is.EqualTo(TargetArea.Single), "旧签名 = 卡等级 1");

            var all = Dmg(Trait(TraitSlot.Lv8, TraitForm.Active,
                new EffectDef(EffectKind.Reshape, 0, shape: TargetArea.All, shapePercent: 60)));
            Assert.That(BattleEngine.NeedsTarget(all, false, 8), Is.False, "改成全体后不选目标");
            Assert.That(BattleEngine.NeedsTarget(all, false, 5), Is.True);
            Assert.That(BattleEngine.NeedsTarget(all), Is.True);

            // 引擎 Cast 走带等级的版本:多敌、不指定目标也能出(全体免选)
            var b = Battle(all, 8, null, FrontRowAndBack());
            Assert.That(b.Cast("试", -1), Is.EqualTo(BattleError.None));
        }

        // ---------------- 修复第 1 轮:同轴相加(spec v7 §6.1.3,Ruling 5)与 scope 正例 ----------------

        [Test]
        public void Amplify_Shield_AddsIntoShieldPercent_NotMultiplied()
        {
            // 土系字护盾 100,筑垒 ShieldPercent = 20,Amplify Shield +30(Lv4 被动;Lv1 不解锁,这里用 Lv4 并按卡等级手算)
            CharDef Def(params TraitDef[] traits) => new("试", Element.Earth,
                effects: new[] { new EffectDef(EffectKind.Shield, 100) }, traits: traits);
            var config = new BattleConfig { PlayerMaxHp = 1000, PlayerAttack = 100, ShieldPercent = 20 };
            var plain = Battle(Def(), 4, config);
            plain.Cast("试", -1);
            // 100 × 1.18(Lv4)= 118;× 120/100 = 141.6 → 141(专精整数除向下)
            Assert.That(plain.PlayerShield, Is.EqualTo(141));

            var amp = Battle(Def(Trait(TraitSlot.Lv4, TraitForm.Passive, Amp(30, AmpScope.Shield))), 4, config);
            amp.Cast("试", -1);
            // 同轴:141 × (100+20+30)/(100+20) = 176.25 → 177 ≈ 118 × 1.50 = 177(不是 ×1.2×1.3 = 1.56 → 184)
            Assert.That(amp.PlayerShield, Is.EqualTo(177));
        }

        [Test]
        public void Amplify_Heal_AddsIntoHealPercent_NotMultiplied()
        {
            // 水系字治疗 100,甘霖 HealPercent = 20,Amplify Heal +30;Lv4
            CharDef Def(params TraitDef[] traits) => new("试", Element.Water,
                effects: new[] { new EffectDef(EffectKind.HealSelf, 100) }, traits: traits);
            var config = new BattleConfig { PlayerMaxHp = 1000, PlayerAttack = 100, HealPercent = 20 };
            int Healed(CharDef def)
            {
                var b = Battle(def, 4, config);
                b.DamagePlayerForTest(800);
                int before = b.PlayerHp;
                b.Cast("试", -1);
                return b.PlayerHp - before;
            }
            // 118 × 120/100 = 141(泉 0 层,不放大)
            Assert.That(Healed(Def()), Is.EqualTo(141));
            // 141 × 150/120 = 176.25 → 177 ≈ 118 × 1.50(连乘会是 ⌈141 × 1.3⌉ = 184)
            Assert.That(Healed(Def(Trait(TraitSlot.Lv4, TraitForm.Passive, Amp(30, AmpScope.Heal)))), Is.EqualTo(177));
        }

        [Test]
        public void Amplify_Damage_HasNoSpecialtyAxis_PlainPercent()
        {
            // 伤害当前没有技能树专精项(SpecialtyPercentOf 对 DamageSingle 恒 0):即便配了 Heal/ShieldPercent,
            // 伤害 Amplify 仍是 (100+Σ)/100 —— 118 × 130/100 = 153.4 → 154
            var config = new BattleConfig
            {
                PlayerMaxHp = RebalanceFixture.BaseMaxHp, PlayerAttack = 100, HealPercent = 20, ShieldPercent = 20,
            };
            var b = Battle(Dmg(Trait(TraitSlot.Lv4, TraitForm.Passive, Amp(30))), 4, config);
            b.Cast("试", 0);
            Assert.That(Hp - b.Enemies[0].Hp, Is.EqualTo(154));
        }

        [Test]
        public void Amplify_HealScope_Positive_And_AllScope_Positive()
        {
            // 心系治疗(无专精):HealSelf 100 Lv4 = 118;Heal +50 → 118 × 150/100 = 177
            var heal = Battle(new CharDef("试", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.HealSelf, 100) },
                traits: new[] { Trait(TraitSlot.Lv4, TraitForm.Passive, Amp(50, AmpScope.Heal)) }), 4);
            heal.DamagePlayerForTest(400);
            int before = heal.PlayerHp;
            heal.Cast("试", -1);
            Assert.That(heal.PlayerHp - before, Is.EqualTo(177));

            // All +50:伤害 118 → 177;护盾 50 × 1.18 = 59 → 59 × 150/100 = 88.5 → 89
            var all = Battle(new CharDef("试", Element.Heart, effects: new[]
            {
                new EffectDef(EffectKind.DamageSingle, 100),
                new EffectDef(EffectKind.Shield, 50),
            }, traits: new[] { Trait(TraitSlot.Lv4, TraitForm.Passive, Amp(50, AmpScope.All)) }), 4);
            all.Cast("试", 0);
            Assert.That(Hp - all.Enemies[0].Hp, Is.EqualTo(177));
            Assert.That(all.PlayerShield, Is.EqualTo(89));
        }

        [Test]
        public void Amplify_Heal_TargetCondition_UsesSelectedEnemy()
        {
            // 攻 + 治同面:伤害选中的敌人生命 < 30% 时治疗 +100%。Lv5:治疗 100 × 1.24 = 124 → 248
            CharDef Def() => new("试", Element.Heart, effects: new[]
            {
                new EffectDef(EffectKind.DamageSingle, 10),
                new EffectDef(EffectKind.HealSelf, 100),
            }, traits: new[] { Trait(TraitSlot.Lv5, TraitForm.Active, Amp(100, AmpScope.Heal, DamageCondition.TargetHpBelow30)) });
            int Healed(int enemyHp)
            {
                var b = Battle(Def(), 5);
                b.Enemies[0].Hp = enemyHp;
                b.DamagePlayerForTest(400);
                int before = b.PlayerHp;
                b.Cast("试", 0);
                return b.PlayerHp - before;
            }
            Assert.That(Healed(20000), Is.EqualTo(248), "目标 20% < 30%:治疗吃加成");
            Assert.That(Healed(Hp), Is.EqualTo(124), "目标满血:不吃");

            // 本面不选敌方目标(纯治疗):目标相关条件无从判定,视为不满足
            var noTarget = Battle(new CharDef("试", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.HealSelf, 100) },
                traits: new[] { Trait(TraitSlot.Lv5, TraitForm.Active, Amp(100, AmpScope.Heal, DamageCondition.TargetHpBelow30)) }), 5);
            noTarget.Enemies[0].Hp = 20000;
            noTarget.DamagePlayerForTest(400);
            int b0 = noTarget.PlayerHp;
            noTarget.Cast("试", -1);
            Assert.That(noTarget.PlayerHp - b0, Is.EqualTo(124));
        }

        [Test]
        public void Reshape_ToAllWithoutPercent_ResetsToFull()
        {
            // 本体横扫 50;Reshape 只写 shape All、没写 shapePercent → 全体各 100%,不沿用横扫的 50
            var def = new CharDef("试", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 100, shape: TargetArea.Row, shapePercent: 50) },
                traits: new[] { Trait(TraitSlot.Lv5, TraitForm.Active, new EffectDef(EffectKind.Reshape, 0, shape: TargetArea.All)) });
            var folded = TraitRules.Fold(def.Effects, def, CardFace.Feature, 5);
            Assert.That(folded[0].Shape, Is.EqualTo(TargetArea.All));
            Assert.That(folded[0].ShapePercent, Is.EqualTo(100));
            var b = Battle(def, 5, null, FrontRowAndBack());
            b.Cast("试", -1);
            foreach (var e in b.Enemies)
                Assert.That(Hp - e.Hp, Is.EqualTo(124), "Lv5 124,全体每个目标全额");
        }

        // ---------------- 字表加载 ----------------

        private static Brushblade.Data.ConfigException LoadThrows(string traitEffects) =>
            Assert.Throws<Brushblade.Data.ConfigException>(() => Load(traitEffects));

        private static RecipeGraph Load(string traitEffects) => Brushblade.Data.ConfigLoader.LoadGraph(
            @"{""chars"":[{""id"":""甲"",""element"":""Fire"",""effects"":[{""kind"":""DamageSingle"",""value"":5}],
                ""traits"":[{""slot"":""Lv5"",""name"":""修"",""effects"":[" + traitEffects + "]}]}]}");

        [Test]
        public void Config_ParsesModifierFields_AndAllowsReshapeAll()
        {
            var g = Load(@"{""kind"":""Amplify"",""value"":30,""scope"":""Shield"",""onlyIf"":""TargetHpBelow30""},
                {""kind"":""Reshape"",""shape"":""All"",""shapePercent"":60,""hitCount"":2,""hitPercent"":60,
                 ""forceCrit"":true,""armorIgnorePercent"":50,""shieldStrikePercent"":40}");
            var effects = g.Get("甲").Traits[0].Effects;
            Assert.That(effects[0].Kind, Is.EqualTo(EffectKind.Amplify));
            Assert.That(effects[0].Scope, Is.EqualTo(AmpScope.Shield));
            Assert.That(effects[0].OnlyIf, Is.EqualTo(DamageCondition.TargetHpBelow30));
            var r = effects[1];
            Assert.That(r.Shape, Is.EqualTo(TargetArea.All), "Reshape 写 shape All 放行(它是修饰器,不是带形状的效果)");
            Assert.That(r.ShapePercent, Is.EqualTo(60));
            Assert.That(r.HitCount, Is.EqualTo(2));
            Assert.That(r.HitPercent, Is.EqualTo(60));
            Assert.That(r.ForceCrit, Is.True);
            Assert.That(r.ArmorIgnorePercent, Is.EqualTo(50));
            Assert.That(r.ShieldStrikePercent, Is.EqualTo(40));
        }

        [Test]
        public void Config_RejectsUnknownScope_ScopeOnNonAmplify_OnlyIfOnNonAmplify()
        {
            Assert.That(LoadThrows(@"{""kind"":""Amplify"",""value"":30,""scope"":""Bogus""}").Message, Does.Contain("Bogus"));
            Assert.That(LoadThrows(@"{""kind"":""Amplify"",""value"":30,""onlyIf"":""Nope""}").Message, Does.Contain("Nope"));
            Assert.That(LoadThrows(@"{""kind"":""Shield"",""value"":3,""scope"":""Shield""}").Message, Does.Contain("scope"));
            Assert.That(LoadThrows(@"{""kind"":""Shield"",""value"":3,""onlyIf"":""Burning""}").Message, Does.Contain("onlyIf"));
        }
    }
}
