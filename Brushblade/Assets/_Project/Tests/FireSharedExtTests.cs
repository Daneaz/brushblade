using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>Plan D2-火 Task 1:共用小扩展与现存问题 —— 附录 E1(条件)、E2(选择器 + 烟熏修复 G1)、
    /// E3(Reshape 重选目标)、E4 / G11(特性效果独立来源)、E5(bodyPercent)、G3(AmpScope.Burn)、
    /// V1(致盲取最强)、G13 / N12(开局登记)、N13(limit)。
    ///
    /// 夹具口径同 TraitModifierTests:Element.Heart(生克 1.0×)、PlayerAttack = 100、无甲 10 万血靶子、
    /// 暴击率 0。敌人排布同 BurnAdjacentSpreadTests:前三只依次落在前排列 1、2、0,第四只后排列 1。</summary>
    public class FireSharedExtTests
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

        private static int Burn(BattleEngine b, int i) => b.Enemies[i].Statuses.Find(StatusKind.Burn)?.Magnitude ?? 0;

        // ---------------- E1:与目标无关的两个新条件 ----------------

        private static CharDef AmpIf(DamageCondition c) => new("试", Element.Heart,
            effects: new[] { new EffectDef(EffectKind.DamageSingle, 100) },
            traits: new[] { Trait(TraitSlot.Lv4, TraitFace.Both, TraitForm.Passive,
                new EffectDef(EffectKind.Amplify, 20, onlyIf: c)) });

        private static int Dealt(BattleEngine b)
        {
            int before = b.Enemies[0].Hp;
            Assert.That(b.Cast("试", 0), Is.EqualTo(BattleError.None));
            return before - b.Enemies[0].Hp;
        }

        [Test]
        public void Condition_PlayerHpAbove70()
        {
            int full = MetaRules.ScaleByCardLevel(100, 4);
            Assert.That(Dealt(Battle(AmpIf(DamageCondition.PlayerHpAbove70), 4)),
                Is.EqualTo((full * 120 + 99) / 100), "满血(100% > 70%):+20%");
            var hurt = Battle(AmpIf(DamageCondition.PlayerHpAbove70), 4);
            hurt.DamagePlayerForTest(200);   // 300 / 500 = 60%
            Assert.That(Dealt(hurt), Is.EqualTo(full), "60% 不满足");
        }

        [Test]
        public void Condition_HasSummon()
        {
            var sapling = new CharDef("林", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.Summon, 50, summonCount: 1, summonChar: "木") });
            int full = MetaRules.ScaleByCardLevel(100, 4);

            var none = Battle(new[] { AmpIf(DamageCondition.HasSummon), sapling }, 4, new[] { RebalanceFixture.Mob() });
            Assert.That(Dealt(none), Is.EqualTo(full), "没有木灵:不满足");

            var with = Battle(new[] { AmpIf(DamageCondition.HasSummon), sapling }, 4, new[] { RebalanceFixture.Mob() });
            Assert.That(with.Cast("林", -1), Is.EqualTo(BattleError.None));
            Assert.That(with.AliveSummonCount, Is.EqualTo(1));
            Assert.That(Dealt(with), Is.EqualTo((full * 120 + 99) / 100), "有存活木灵:+20%");
        }

        // ---------------- E2:Row / Adjacent / BurnedByThisCast ----------------

        [Test]
        public void Pick_Row_HitsPrimaryRowOnly_AndNeedsTarget()
        {
            var e = new EffectDef(EffectKind.BurnSingle, 2, pick: EffectPick.Row);
            Assert.That(BattleEngine.EffectNeedsTarget(e), Is.True, "Row 以主目标为中心,仍要选目标");
            var b = Battle(RebalanceFixture.Char("试", e), 1, FourMobs());
            Assert.That(b.Enemies[0].Row, Is.EqualTo(EnemyRow.Front));
            b.Cast("试", 0);
            Assert.That(new[] { Burn(b, 0), Burn(b, 1), Burn(b, 2), Burn(b, 3) }, Is.EqualTo(new[] { 2, 2, 2, 0 }));
        }

        /// <summary>Adjacent 口径(controller 2026-10-08 补充裁定,spec §3.2「溅射:目标及同排左右相邻」):
        /// 只取同排左右,不含上下 / 前后排 —— 与 TargetArea.Adjacent 的展开一致。</summary>
        [Test]
        public void Pick_Adjacent_SameRowLeftRightOnly_AndNeedsTarget()
        {
            var e = new EffectDef(EffectKind.BurnSingle, 3, pick: EffectPick.Adjacent);
            Assert.That(BattleEngine.EffectNeedsTarget(e), Is.True);
            var b = Battle(RebalanceFixture.Char("试", e), 1, FourMobs());
            // 甲 = 前排中间(列 1):左右是丙(列 0)、乙(列 2);后排同列的丁不选
            Assert.That(b.Enemies[0].Column, Is.EqualTo(1));
            Assert.That(b.Enemies[3].Column, Is.EqualTo(1));
            b.Cast("试", 0);
            Assert.That(new[] { Burn(b, 0), Burn(b, 1), Burn(b, 2), Burn(b, 3) }, Is.EqualTo(new[] { 3, 3, 3, 0 }),
                "后排同列不被选中");

            var edge = Battle(RebalanceFixture.Char("试", e), 1, FourMobs());
            edge.Cast("试", 1);   // 乙 = 前排列 2:只有左邻甲
            Assert.That(new[] { Burn(edge, 0), Burn(edge, 1), Burn(edge, 2), Burn(edge, 3) }, Is.EqualTo(new[] { 3, 3, 0, 0 }));
        }

        /// <summary>G1:燃面没有伤害,HitTargets 恒空 —— 烟熏(池)改用 BurnedByThisCast 后在燃面生效。</summary>
        private static CharDef FeatureSmoke(EffectPick pick) => new("熏", Element.Heart,
            effects: new[] { new EffectDef(EffectKind.BurnSingle, 1) },
            attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 10) },
            traits: new[] { Trait(TraitSlot.Lv6, TraitFace.Feature, TraitForm.Passive,
                new EffectDef(EffectKind.Blind, 15, pick: pick, riderOf: StatusKind.Burn)) });

        [Test]
        public void Pick_BurnedByThisCast_SmokeWorksOnFeatureFace()
        {
            Assert.That(BattleEngine.EffectNeedsTarget(new EffectDef(EffectKind.Blind, 15, pick: EffectPick.BurnedByThisCast)),
                Is.False);
            var old = Battle(FeatureSmoke(EffectPick.HitTargets), 6, FourMobs());
            old.Cast("熏", 0);
            Assert.That(old.Enemies[0].Statuses.Has(StatusKind.Blind), Is.False, "旧写法:燃面 HitTargets 恒空(现存问题 §0-1)");

            var b = Battle(FeatureSmoke(EffectPick.BurnedByThisCast), 6, FourMobs());
            b.Cast("熏", 0);
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.Blind), Is.True, "燃面上灼的目标挂上致盲");
            Assert.That(b.Enemies[1].Statuses.Has(StatusKind.Blind), Is.False, "没被本字上灼的不挂");
        }

        [Test]
        public void RealData_PoolSmoke_OnFeatureFace_Blinds()
        {
            // 热 Lv6 = 燃·池·烟熏(池表改 pick BurnedByThisCast 后重新生成的 chars.json)
            var graph = CharTableTests.RealGraph();
            var re = graph.Get("热");
            var smoke = re.Traits.Single(t => t.Slot == TraitSlot.Lv6 && t.Face == TraitFace.Feature);
            Assert.That(smoke.Effects.Single().Pick, Is.EqualTo(EffectPick.BurnedByThisCast));
            var b = new BattleEngine(graph, Config, new[] { "热", "热" }, Array.Empty<string>(),
                new[] { RebalanceFixture.Mob() }, seed: 1, cardLevels: new Dictionary<string, int> { ["热"] = 6 });
            Assert.That(b.Cast("热", 0), Is.EqualTo(BattleError.None));
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.Burn), Is.True);
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.Blind), Is.True, "燃面烟熏生效");
        }

        // ---------------- E3:Reshape 重选目标 ----------------

        private static CharDef Gale() => new("烈", Element.Heart,
            effects: new[] { new EffectDef(EffectKind.BurnSingle, 2), new EffectDef(EffectKind.Weaken, 10, turns: 2) },
            attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 10) },
            traits: new[] { Trait(TraitSlot.Lv5, TraitFace.Feature, TraitForm.Active,
                new EffectDef(EffectKind.Reshape, 0, pick: EffectPick.Row)) });

        [Test]
        public void Reshape_Pick_WithoutDamage_RetargetsPrimaryEffects()
        {
            var b = Battle(Gale(), 5, FourMobs());
            b.Cast("烈", 0);
            Assert.That(new[] { Burn(b, 0), Burn(b, 1), Burn(b, 2), Burn(b, 3) }, Is.EqualTo(new[] { 2, 2, 2, 0 }),
                "燃改横扫(G6):整排都吃满额灼");
            Assert.That(Enumerable.Range(0, 3).All(i => b.Enemies[i].Statuses.Has(StatusKind.Curse)), Is.True, "整排都吃减攻");
            Assert.That(b.Enemies[3].Statuses.Has(StatusKind.Curse), Is.False);
            Assert.That(BattleEngine.NeedsTarget(Gale(), false, 5), Is.True, "Row 仍要选目标");

            var low = Battle(Gale(), 4, FourMobs());
            low.Cast("烈", 0);
            Assert.That(new[] { Burn(low, 0), Burn(low, 1) }, Is.EqualTo(new[] { 2, 0 }), "未解锁:单体");
        }

        // ---------------- E4 / G11:特性效果独立来源 ----------------

        [Test]
        public void Fold_TagsTraitKey_OnNonLv1Lv3TraitEffects()
        {
            var def = new CharDef("试", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 10), new EffectDef(EffectKind.Weaken, 15, turns: 3) },
                traits: new[]
                {
                    new TraitDef(TraitSlot.Lv1, TraitFace.Both, TraitForm.Active, null, "本", Array.Empty<EffectDef>()),
                    new TraitDef(TraitSlot.Lv3, TraitFace.Both, TraitForm.Active, TraitSlot.Lv1, "强",
                        new[] { new EffectDef(EffectKind.Weaken, 20, turns: 3) }),
                    Trait(TraitSlot.Lv8, TraitFace.Both, TraitForm.Active, new EffectDef(EffectKind.Weaken, 40, turns: 1)),
                });
            var folded = TraitRules.CastEffects(def, CardFace.Feature, 8);
            var weakens = folded.Where(e => e.Kind == EffectKind.Weaken).ToList();
            Assert.That(weakens.Count, Is.EqualTo(2));
            Assert.That(weakens[0].Value, Is.EqualTo(20));
            Assert.That(weakens[0].TraitKey, Is.Null, "Lv3 替换进本体:仍与本体同源");
            Assert.That(weakens[1].TraitKey, Is.EqualTo(BattleEngine.TraitKey("试", TraitSlot.Lv8, TraitFace.Both)));
        }

        [Test]
        public void TraitWeaken_SeparateFromBodyWeaken_OwnTimer_MaxApplies()
        {
            var def = new CharDef("试", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 10), new EffectDef(EffectKind.Weaken, 15, turns: 3) },
                traits: new[] { Trait(TraitSlot.Lv8, TraitFace.Both, TraitForm.Active, new EffectDef(EffectKind.Weaken, 40, turns: 1)) });
            var b = Battle(def, 8, RebalanceFixture.Mob(attack: 100));
            b.Cast("试", 0);
            var curses = b.Enemies[0].Statuses.All.Where(s => s.Kind == StatusKind.Curse).ToList();
            Assert.That(curses.Count, Is.EqualTo(2), "特性减攻不与本体同源合并");
            int bodyValue = MetaRules.ScaleEffectValue(EffectKind.Weaken, 15, 8);
            int traitValue = MetaRules.ScaleEffectValue(EffectKind.Weaken, 40, 8);
            Assert.That(curses.Single(s => s.TraitKey == null).TurnsLeft, Is.EqualTo(3));
            Assert.That(curses.Single(s => s.TraitKey == null).Magnitude, Is.EqualTo(bodyValue));
            Assert.That(curses.Single(s => s.TraitKey != null).TurnsLeft, Is.EqualTo(1), "特性的回合数自己算(不被本体拉长)");
            Assert.That(curses.Single(s => s.TraitKey != null).Magnitude, Is.EqualTo(traitValue));
            Assert.That(b.Enemies[0].Statuses.MaxMagnitude(StatusKind.Curse), Is.EqualTo(traitValue), "多来源取最大(R18)");

            b.EndTurn();   // 敌人行动一拍:特性那条到期,本体那条还在
            var left = b.Enemies[0].Statuses.All.Where(s => s.Kind == StatusKind.Curse).ToList();
            Assert.That(left.Count, Is.EqualTo(1));
            Assert.That(left[0].TraitKey, Is.Null);
            Assert.That(left[0].TurnsLeft, Is.EqualTo(2));
        }

        // ---------------- G11:致盲同样分来源 ----------------

        [Test]
        public void TraitBlind_SeparateFromBodyBlind()
        {
            var def = new CharDef("试", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 10), new EffectDef(EffectKind.Blind, 20, turns: 2) },
                traits: new[] { Trait(TraitSlot.Lv5, TraitFace.Both, TraitForm.Active, new EffectDef(EffectKind.Blind, 30, turns: 1)) });
            var b = Battle(def, 5);
            b.Cast("试", 0);
            var blinds = b.Enemies[0].Statuses.All.Where(s => s.Kind == StatusKind.Blind).ToList();
            Assert.That(blinds.Count, Is.EqualTo(2));
            Assert.That(blinds.Single(s => s.TraitKey == null).TurnsLeft, Is.EqualTo(2));
            Assert.That(blinds.Single(s => s.TraitKey != null).TurnsLeft, Is.EqualTo(1));
        }

        // ---------------- E5:bodyPercent ----------------

        [Test]
        public void BodyPercent_CastTrait_ResolvesFromFaceBody()
        {
            var def = new CharDef("试", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.Shield, 10) },
                attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 40) },
                traits: new[] { Trait(TraitSlot.Lv5, TraitFace.Attack, TraitForm.Active,
                    new EffectDef(EffectKind.DamageSingle, 0, bodyPercent: 50)) });
            var folded = TraitRules.CastEffects(def, CardFace.Attack, 5);
            Assert.That(folded.Count(e => e.Kind == EffectKind.DamageSingle), Is.EqualTo(2));
            Assert.That(folded[1].Value, Is.EqualTo(20), "40 × 50%");
            var b = Battle(def, 5);
            int before = b.Enemies[0].Hp;
            b.Cast("试", 0, attackMode: true);
            Assert.That(before - b.Enemies[0].Hp,
                Is.EqualTo(MetaRules.ScaleByCardLevel(40, 5) + MetaRules.ScaleByCardLevel(20, 5)));
        }

        [Test]
        public void BodyPercent_OnKillReaction_ResolvesFromFaceBody()
        {
            var def = new CharDef("试", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.Shield, 10) },
                attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 40) },
                traits: new[] { new TraitDef(TraitSlot.Lv8, TraitFace.Attack, TraitForm.Passive, null, "连",
                    new[] { new EffectDef(EffectKind.DamageSingle, 0, shape: TargetArea.All, bodyPercent: 50) },
                    TraitTrigger.OnKill) });
            var b = Battle(new[] { def }, 8, new[] { RebalanceFixture.Mob(hp: 10), RebalanceFixture.Mob() });
            b.Cast("试", 0, attackMode: true);
            Assert.That(b.Enemies[0].Alive, Is.False);
            Assert.That(Hp - b.Enemies[1].Hp, Is.EqualTo(MetaRules.ScaleByCardLevel(20, 8)), "反应的全体伤害 = 本体 40 × 50%(吃等级)");
        }

        // ---------------- G3:AmpScope.Burn ----------------

        private static int Potency(BattleEngine b) => b.Enemies[0].Statuses.Find(StatusKind.Burn).Potency;

        private static CharDef BurnAmp(AmpScope scope) => new("试", Element.Heart,
            effects: new[] { new EffectDef(EffectKind.BurnSingle, 2) },
            traits: new[] { Trait(TraitSlot.Lv4, TraitFace.Both, TraitForm.Passive,
                new EffectDef(EffectKind.Amplify, 50, scope: scope)) });

        [Test]
        public void AmpScopeBurn_ScalesPotency_NotStacks()
        {
            int basePotency = MetaRules.CardLevelPercent(4);
            int amped = (basePotency * 150 + 99) / 100;
            var plain = Battle(BurnAmp(AmpScope.Damage), 4);
            plain.Cast("试", 0);
            Assert.That(Potency(plain), Is.EqualTo(basePotency), "scope Damage 不碰灼");

            var b = Battle(BurnAmp(AmpScope.Burn), 4);
            b.Cast("试", 0);
            Assert.That(Burn(b, 0), Is.EqualTo(2), "层数不变");
            Assert.That(Potency(b), Is.EqualTo(amped), "火力 × (100 + 50)/100");

            var all = Battle(BurnAmp(AmpScope.All), 4);
            all.Cast("试", 0);
            Assert.That(Burn(all, 0), Is.EqualTo(2));
            Assert.That(Potency(all), Is.EqualTo(amped), "scope All 包含 Burn");
        }

        // ---------------- ConfigLoader:新字段 / 枚举成员按名字解析 ----------------

        private const string FireBody = @"""effects"":[{""kind"":""BurnSingle"",""value"":2}],
            ""attackEffects"":[{""kind"":""DamageSingle"",""value"":40}]";

        [Test]
        public void ConfigLoader_ParsesD2FireTask1Fields()
        {
            var g = Brushblade.Data.ConfigLoader.LoadGraph(@"{""chars"":[{""id"":""甲"",""element"":""Fire""," + FireBody + @",""traits"":[
                  {""slot"":""Lv4"",""form"":""Passive"",""name"":""气"",""effects"":[{""kind"":""Amplify"",""value"":20,""scope"":""Burn"",""onlyIf"":""PlayerHpAbove70""}]},
                  {""slot"":""Lv5"",""face"":""Feature"",""name"":""风"",""effects"":[{""kind"":""Reshape"",""value"":0,""pick"":""Row""}]},
                  {""slot"":""Lv6"",""face"":""Feature"",""form"":""Passive"",""name"":""熏"",""effects"":[{""kind"":""Blind"",""value"":15,""pick"":""BurnedByThisCast"",""riderOf"":""Burn""}]},
                  {""slot"":""Lv8"",""face"":""Attack"",""form"":""Passive"",""trigger"":""OnKill"",""name"":""连"",""effects"":[{""kind"":""DamageSingle"",""value"":0,""shape"":""All"",""bodyPercent"":100}]}
                ]}]}");
            var t = g.Get("甲").Traits;
            Assert.That(t[0].Effects[0].Scope, Is.EqualTo(AmpScope.Burn));
            Assert.That(t[0].Effects[0].OnlyIf, Is.EqualTo(DamageCondition.PlayerHpAbove70));
            Assert.That(t[1].Effects[0].Pick, Is.EqualTo(EffectPick.Row));
            Assert.That(t[2].Effects[0].Pick, Is.EqualTo(EffectPick.BurnedByThisCast));
            Assert.That(t[3].Effects[0].BodyPercent, Is.EqualTo(100));
        }

        [Test]
        public void ConfigLoader_BodyPercentOnlyOnDamage()
        {
            Assert.Throws<Brushblade.Data.ConfigException>(() => Brushblade.Data.ConfigLoader.LoadGraph(
                @"{""chars"":[{""id"":""甲"",""element"":""Fire""," + FireBody + @",""traits"":[
                  {""slot"":""Lv5"",""face"":""Feature"",""name"":""错"",""effects"":[{""kind"":""BurnAll"",""value"":1,""bodyPercent"":50}]}]}]}"));
        }

        [Test]
        public void AmpScope_InScope_Burn()
        {
            Assert.That(TraitRules.InScope(AmpScope.Burn, EffectKind.BurnSingle), Is.True);
            Assert.That(TraitRules.InScope(AmpScope.Burn, EffectKind.BurnAll), Is.True);
            Assert.That(TraitRules.InScope(AmpScope.Burn, EffectKind.DamageSingle), Is.False);
            Assert.That(TraitRules.InScope(AmpScope.All, EffectKind.BurnAll), Is.True);
            Assert.That(TraitRules.InScope(AmpScope.Damage, EffectKind.BurnSingle), Is.False);
        }

    }
}
