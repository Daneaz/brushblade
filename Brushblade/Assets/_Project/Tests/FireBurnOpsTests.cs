using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>Plan D2-火 Task 2:灼操作族 —— 附录 N1 BurnScale、N2 BurnEqualize、N3 Detonate retain / portion(G5)、
    /// N4 计数缩放(per BurnStack / BurningEnemy + cap,G2)、N4b 每击附带(PerHit / PerHitFrom / ShotPercent,V2、G7;
    /// 跨计划 Q23:通用形态,任意效果都能每击附带)。
    ///
    /// 夹具口径同 FireSharedExtTests:Element.Heart(生克 1.0×)、PlayerAttack = 100、无甲 10 万血靶子、暴击率 0、卡 Lv1
    /// (火力 100,层·伤害 = 层数 × 每层基数,整数)。</summary>
    public class FireBurnOpsTests
    {
        private const int Hp = 100000;

        private static BattleConfig Config => new BattleConfig
            { PlayerMaxHp = RebalanceFixture.BaseMaxHp, PlayerAttack = 100, ApPerTurn = 20 };

        private static BattleEngine Battle(CharDef def, int level, EnemyDef[] enemies, BattleConfig config = null) =>
            new(RebalanceFixture.Graph(def), config ?? Config,
                new[] { def.Id, def.Id, def.Id }, Array.Empty<string>(),
                enemies, seed: 1, cardLevels: new Dictionary<string, int> { [def.Id] = level });

        private static EnemyDef[] Mobs(int n, int hp = Hp) =>
            Enumerable.Range(0, n).Select(_ => new EnemyDef("怔", Element.Heart, hp, 0)).ToArray();

        private static CharDef Char(params EffectDef[] effects) => new("试", Element.Heart, effects: effects);

        private static int Burn(BattleEngine b, int i) => b.Enemies[i].Statuses.Find(StatusKind.Burn)?.Magnitude ?? 0;

        private static void SetBurn(BattleEngine b, int i, int stacks, int potency = 100) =>
            b.Enemies[i].Statuses.Apply(new StatusEffect
                { Kind = StatusKind.Burn, Polarity = StatusPolarity.Debuff, Magnitude = stacks, TurnsLeft = -1, Potency = potency });

        /// <summary>每层灼一次结算的伤害(夹具下 = 每层基数 × 1.0 × 1.0)。用全额引爆 1 层量出来,不写死常量。</summary>
        private static int PerStack()
        {
            var b = Battle(Char(new EffectDef(EffectKind.Detonate, 0)), 1, Mobs(1));
            SetBurn(b, 0, 1);
            int before = b.Enemies[0].Hp;
            Assert.That(b.Cast("试", 0), Is.EqualTo(BattleError.None));
            return before - b.Enemies[0].Hp;
        }

        private static int Tri(int n) => n * (n + 1) / 2;

        // ---------------- N1 BurnScale ----------------

        [Test]
        public void BurnScale_Doubles_ClampsAt10_ZeroLayersIsNoOp()
        {
            var b = Battle(Char(new EffectDef(EffectKind.BurnScale, 200, pick: EffectPick.All)), 1, Mobs(3));
            SetBurn(b, 0, 3);
            SetBurn(b, 1, 7);
            Assert.That(b.Cast("试", -1), Is.EqualTo(BattleError.None));
            Assert.That(Burn(b, 0), Is.EqualTo(6), "3 层 ×200% = 6");
            Assert.That(Burn(b, 1), Is.EqualTo(CombatCaps.BurnStacks), "7 层 ×200% = 14 → 钳到 10");
            Assert.That(b.Enemies[2].Statuses.Has(StatusKind.Burn), Is.False, "0 层空转:不凭空挂灼");
            Assert.That(b.LastEvents.Count(e => e.Kind == BattleEventKind.Burn && e.TargetIndex == 2), Is.EqualTo(0));
            Assert.That(b.LastEvents.Single(e => e.Kind == BattleEventKind.Burn && e.TargetIndex == 1).Amount, Is.EqualTo(3),
                "Burn 事件报实际增量");
        }

        [Test]
        public void BurnScale_TakesHigherPotency_NeedsTarget()
        {
            var effect = new EffectDef(EffectKind.BurnScale, 200);
            Assert.That(BattleEngine.EffectNeedsTarget(effect), Is.True, "单体翻倍要选目标");
            var b = Battle(Char(effect), 8, Mobs(1));
            SetBurn(b, 0, 2);
            Assert.That(b.Cast("试", 0), Is.EqualTo(BattleError.None));
            var burn = b.Enemies[0].Statuses.Find(StatusKind.Burn);
            Assert.That(burn.Magnitude, Is.EqualTo(4));
            Assert.That(burn.Potency, Is.EqualTo(MetaRules.CardLevelPercent(8)), "火力取 max(原 100,本字 Lv8)");
        }

        // ---------------- N2 BurnEqualize ----------------

        [Test]
        public void BurnEqualize_RaisesEveryoneToMax_NeverLowers()
        {
            var b = Battle(Char(new EffectDef(EffectKind.BurnEqualize, 0)), 1, Mobs(3));
            Assert.That(BattleEngine.EffectNeedsTarget(new EffectDef(EffectKind.BurnEqualize, 0)), Is.False);
            SetBurn(b, 0, 1);
            SetBurn(b, 1, 4);
            Assert.That(b.Cast("试", -1), Is.EqualTo(BattleError.None));
            Assert.That(new[] { Burn(b, 0), Burn(b, 1), Burn(b, 2) }, Is.EqualTo(new[] { 4, 4, 4 }), "0 层也补到最高");

            var none = Battle(Char(new EffectDef(EffectKind.BurnEqualize, 0)), 1, Mobs(3));
            Assert.That(none.Cast("试", -1), Is.EqualTo(BattleError.None));
            Assert.That(none.Enemies.Any(e => e.Statuses.Has(StatusKind.Burn)), Is.False, "全场 0 层:空转");
        }

        // ---------------- N3 Detonate retain / portion(G5) ----------------

        private static void AttachRider(BattleEngine b, int i)
        {
            b.Enemies[i].Statuses.Apply(new StatusEffect { Kind = StatusKind.TraitRider, Polarity = StatusPolarity.Debuff,
                Magnitude = (int)StatusKind.Burn, TurnsLeft = -1, SourceId = "熏", TraitKey = "熏/6/Feature" });
            b.Enemies[i].Statuses.Apply(new StatusEffect { Kind = StatusKind.Blind, Polarity = StatusPolarity.Debuff,
                Magnitude = 15, TurnsLeft = -1, SourceId = "熏", TraitKey = "熏/6/Feature" });
        }

        [Test]
        public void Detonate_Retain_FullDamage_KeepsFloorOfPercent_RidersStay()
        {
            int per = PerStack();
            var b = Battle(Char(new EffectDef(EffectKind.Detonate, 0, retainPercent: 50)), 1, Mobs(1));
            SetBurn(b, 0, 6);
            AttachRider(b, 0);
            int before = b.Enemies[0].Hp;
            Assert.That(b.Cast("试", 0), Is.EqualTo(BattleError.None));
            Assert.That(before - b.Enemies[0].Hp, Is.EqualTo(Tri(6) * per), "全额:6 层的全部未来伤害");
            Assert.That(Burn(b, 0), Is.EqualTo(3), "保留 ⌊6 × 50%⌋ = 3 层");
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.Blind), Is.True, "灼还在:附着不掉");

            var one = Battle(Char(new EffectDef(EffectKind.Detonate, 0, retainPercent: 33)), 1, Mobs(1));
            SetBurn(one, 0, 2);
            AttachRider(one, 0);
            Assert.That(one.Cast("试", 0), Is.EqualTo(BattleError.None));
            Assert.That(one.Enemies[0].Statuses.Has(StatusKind.Burn), Is.False, "⌊2 × 33%⌋ = 0:灼清空");
            Assert.That(one.Enemies[0].Statuses.Has(StatusKind.Blind), Is.False, "灼清空:附着随之移除");
        }

        [Test]
        public void Detonate_Portion_DamageIsTriDifference_LeavesRemainder()
        {
            int per = PerStack();
            foreach (var (n, k) in new[] { (6, 3), (5, 2), (10, 5) })
            {
                var b = Battle(Char(new EffectDef(EffectKind.Detonate, 0, portionPercent: 50)), 1, Mobs(1));
                SetBurn(b, 0, n);
                AttachRider(b, 0);
                int before = b.Enemies[0].Hp;
                Assert.That(b.Cast("试", 0), Is.EqualTo(BattleError.None));
                Assert.That(before - b.Enemies[0].Hp, Is.EqualTo((Tri(n) - Tri(n - k)) * per), $"{n} 层引爆 {k}:tri(N) − tri(N−k)");
                Assert.That(Burn(b, 0), Is.EqualTo(n - k), $"{n} 层剩 {n - k}");
                Assert.That(b.Enemies[0].Statuses.Has(StatusKind.Blind), Is.True, "灼还在:附着不掉");
            }

            var single = Battle(Char(new EffectDef(EffectKind.Detonate, 0, portionPercent: 50)), 1, Mobs(1));
            SetBurn(single, 0, 1);
            int hp = single.Enemies[0].Hp;
            Assert.That(single.Cast("试", 0), Is.EqualTo(BattleError.None));
            Assert.That(single.Enemies[0].Hp, Is.EqualTo(hp), "1 层 × 50% = 0:不引爆");
            Assert.That(Burn(single, 0), Is.EqualTo(1));
            Assert.That(single.LastEvents.Any(e => e.Kind == BattleEventKind.Detonate), Is.False);
        }

        [Test]
        public void Detonate_RetainOrPortion_TargetKilled_ResidualCleared_NoEmbers()
        {
            // 余烬(BurnSpreadPercent)开着:死者残层若没清,会被转给另一只
            var config = Config;
            config.BurnSpreadPercent = 100;
            foreach (var effect in new[]
                     {
                         new EffectDef(EffectKind.Detonate, 0, retainPercent: 50),
                         new EffectDef(EffectKind.Detonate, 0, portionPercent: 50),
                     })
            {
                var b = Battle(Char(effect), 1, new[] { new EnemyDef("怔", Element.Heart, 1, 0), new EnemyDef("怔", Element.Heart, Hp, 0) }, config);
                SetBurn(b, 0, 6);
                AttachRider(b, 0);
                Assert.That(b.Cast("试", 0), Is.EqualTo(BattleError.None));
                Assert.That(b.Enemies[0].Alive, Is.False);
                Assert.That(b.Enemies[0].Statuses.Has(StatusKind.Burn), Is.False, "被打死:残层清零");
                Assert.That(b.Enemies[0].Statuses.Has(StatusKind.Blind), Is.False, "残层清零:附着随之移除");
                Assert.That(Burn(b, 1), Is.EqualTo(0), "残层已清,余烬没有可转的层数");
            }
        }

        [Test]
        public void Detonate_Default_StillClearsEverything()
        {
            int per = PerStack();
            var b = Battle(Char(new EffectDef(EffectKind.Detonate, 0)), 1, Mobs(1));
            SetBurn(b, 0, 4);
            AttachRider(b, 0);
            int before = b.Enemies[0].Hp;
            Assert.That(b.Cast("试", 0), Is.EqualTo(BattleError.None));
            Assert.That(before - b.Enemies[0].Hp, Is.EqualTo(Tri(4) * per));
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.Burn), Is.False);
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.Blind), Is.False);
        }

        // ---------------- N4 计数缩放(G2) ----------------

        [Test]
        public void Amplify_PerBurnStack_ReadsPreCastStacks_AndCaps()
        {
            // 燥裂 / 燥火攻心:每层 +10%,最多 +30%。本字先上 4 层灼,但计数按出字前(R3)
            var def = Char(new EffectDef(EffectKind.BurnSingle, 4), new EffectDef(EffectKind.DamageSingle, 100),
                new EffectDef(EffectKind.Amplify, 10, scaleBy: ScaleBasis.BurnStack, scaleCap: 30));
            var b = Battle(def, 1, Mobs(1));
            SetBurn(b, 0, 2);
            int hp = b.Enemies[0].Hp;
            Assert.That(b.Cast("试", 0), Is.EqualTo(BattleError.None));
            int burnTick = 0;   // 出字不结算灼
            Assert.That(hp - b.Enemies[0].Hp - burnTick, Is.EqualTo(120), "出字前 2 层 → +20%(不是出字后 6 层)");
            Assert.That(Burn(b, 0), Is.EqualTo(6));

            hp = b.Enemies[0].Hp;
            Assert.That(b.Cast("试", 0), Is.EqualTo(BattleError.None));
            Assert.That(hp - b.Enemies[0].Hp, Is.EqualTo(130), "出字前 6 层 → 60% 钳到 cap 30%");
        }

        [Test]
        public void Amplify_PerBurningEnemy_CountsPreCastBurningEnemies()
        {
            var def = Char(new EffectDef(EffectKind.BurnSingle, 1, pick: EffectPick.All), new EffectDef(EffectKind.DamageSingle, 100),
                new EffectDef(EffectKind.Amplify, 10, scaleBy: ScaleBasis.BurningEnemy));
            var b = Battle(def, 1, Mobs(3));
            SetBurn(b, 1, 1);
            SetBurn(b, 2, 3);
            int hp = b.Enemies[0].Hp;
            Assert.That(b.Cast("试", 0), Is.EqualTo(BattleError.None));
            Assert.That(hp - b.Enemies[0].Hp, Is.EqualTo(120), "出字前 2 名带灼 → +20%");
        }

        [Test]
        public void HealSelf_PerBurningEnemy_CountsAfterCast_ZeroIsNoHeal()
        {
            // 温润:计数用出字后(产出量,G2)—— 本字的 BurnAll 先上灼,三名都算
            var def = Char(new EffectDef(EffectKind.BurnAll, 1), new EffectDef(EffectKind.HealSelf, 20, scaleBy: ScaleBasis.BurningEnemy));
            var b = Battle(def, 1, Mobs(3));
            b.DamagePlayerForTest(200);
            int hp = b.PlayerHp;
            Assert.That(b.Cast("试", -1), Is.EqualTo(BattleError.None));
            Assert.That(b.PlayerHp - hp, Is.EqualTo(60), "20 × 3 名带灼");

            var dry = Battle(Char(new EffectDef(EffectKind.HealSelf, 20, scaleBy: ScaleBasis.BurningEnemy)), 1, Mobs(3));
            dry.DamagePlayerForTest(200);
            hp = dry.PlayerHp;
            Assert.That(dry.Cast("试", -1), Is.EqualTo(BattleError.None));
            Assert.That(dry.PlayerHp, Is.EqualTo(hp), "没有带灼的敌人:回复 0");
        }

        // ---------------- N4b 每击附带(V2 / G7 / Q23) ----------------

        [Test]
        public void FourFlames_AllShape_EveryEnemyTakesFourHitsAndFourBurn()
        {
            // V2 燚·四炎:全体每人 4 击(各 30%),每击附灼 1 —— 走 Reshape 折叠(与字表同路径)
            var def = Char(new EffectDef(EffectKind.DamageSingle, 100, shape: TargetArea.All),
                new EffectDef(EffectKind.Reshape, 0, hitCount: 4, hitPercent: 30,
                    perHit: new[] { new EffectDef(EffectKind.BurnSingle, 1) }));
            var b = Battle(def, 1, Mobs(3));
            Assert.That(b.Cast("试", -1), Is.EqualTo(BattleError.None));
            for (int i = 0; i < 3; i++)
            {
                Assert.That(Hp - b.Enemies[i].Hp, Is.EqualTo(4 * 30), $"敌 {i}:4 击 × 30");
                Assert.That(Burn(b, i), Is.EqualTo(4), $"敌 {i}:每击附灼 1");
                Assert.That(b.LastEvents.Count(e => e.Kind == BattleEventKind.Damage && e.TargetIndex == i), Is.EqualTo(4));
            }
        }

        [Test]
        public void FlameBlade_TwoFullHits_SettleAfterEachHit_NoDecay()
        {
            // G7 炎·炎刃:打 2 击(每击 100%),每击后目标的灼结算一次(不减层)
            int per = PerStack();
            var def = Char(new EffectDef(EffectKind.DamageSingle, 100),
                new EffectDef(EffectKind.Reshape, 0, hitCount: 2,
                    perHit: new[] { new EffectDef(EffectKind.BurnSettleNow, 0, keepStacks: true) }));
            var b = Battle(def, 1, Mobs(1));
            SetBurn(b, 0, 3);
            Assert.That(b.Cast("试", 0), Is.EqualTo(BattleError.None));
            Assert.That(Hp - b.Enemies[0].Hp, Is.EqualTo(2 * 100 + 2 * 3 * per));
            Assert.That(Burn(b, 0), Is.EqualTo(3), "keep 结算不减层");
            var order = b.LastEvents.Where(e => e.Kind == BattleEventKind.Damage || e.Kind == BattleEventKind.BurnTick)
                .Select(e => e.Kind).ToArray();
            Assert.That(order, Is.EqualTo(new[]
                { BattleEventKind.Damage, BattleEventKind.BurnTick, BattleEventKind.Damage, BattleEventKind.BurnTick }),
                "每击后同步结算,不是两击打完再结算");
        }

        [Test]
        public void PerHit_Scatter_RiderLandsOnThatShotsTarget_ShotPercentEveryShot()
        {
            // 焱·火花四溅:散射 4 发(各 50%),每发附灼 1 —— 每一发的附带落在这一发的目标上
            var def = Char(new EffectDef(EffectKind.DamageSingle, 100),
                new EffectDef(EffectKind.Reshape, 0, shape: TargetArea.Scatter, shots: 4, shotPercent: 50,
                    perHit: new[] { new EffectDef(EffectKind.BurnSingle, 1) }));
            var b = Battle(def, 1, Mobs(3));
            Assert.That(b.Cast("试", -1), Is.EqualTo(BattleError.None));
            var evs = b.LastEvents.Where(e => e.Kind == BattleEventKind.Damage || e.Kind == BattleEventKind.Burn).ToList();
            Assert.That(evs.Count(e => e.Kind == BattleEventKind.Damage), Is.EqualTo(4));
            for (int k = 0; k < evs.Count; k += 2)
            {
                Assert.That(evs[k].Kind, Is.EqualTo(BattleEventKind.Damage));
                Assert.That(evs[k].Amount, Is.EqualTo(50), "每发 50%(首发也打折)");
                Assert.That(evs[k + 1].Kind, Is.EqualTo(BattleEventKind.Burn));
                Assert.That(evs[k + 1].TargetIndex, Is.EqualTo(evs[k].TargetIndex), "附带落在这一发的目标上");
            }
            Assert.That(Enumerable.Range(0, 3).Sum(i => Burn(b, i)), Is.EqualTo(4));
        }

        [Test]
        public void PerHit_FromNthHit_AndAnyEffectKind()
        {
            // Q23 通用形态(金·剁骨「从第 N 击起」):3 击,从第 2 击起每击附带 —— 敌方侧(灼)与我方侧(战意)都行
            var def = Char(new EffectDef(EffectKind.DamageSingle, 100, hitCount: 3, perHitFrom: 2,
                perHit: new[] { new EffectDef(EffectKind.BurnSingle, 1), new EffectDef(EffectKind.Morale, 1) }));
            var b = Battle(def, 1, Mobs(1));
            Assert.That(b.Cast("试", 0), Is.EqualTo(BattleError.None));
            Assert.That(Burn(b, 0), Is.EqualTo(2), "第 2、3 击各附 1");
            Assert.That(b.PlayerStatuses.TotalMagnitude(StatusKind.Morale), Is.EqualTo(2));
        }

        [Test]
        public void PerHit_TargetDiesMidway_HitsStop_NoRiderOnCorpse()
        {
            var def = Char(new EffectDef(EffectKind.DamageSingle, 100, hitCount: 4, hitPercent: 30,
                perHit: new[] { new EffectDef(EffectKind.BurnSingle, 1) }));
            var b = Battle(def, 1, new[] { new EnemyDef("怔", Element.Heart, 50, 0), new EnemyDef("怔", Element.Heart, Hp, 0) });
            Assert.That(b.Cast("试", 0), Is.EqualTo(BattleError.None));
            Assert.That(b.Enemies[0].Alive, Is.False);
            Assert.That(b.LastEvents.Count(e => e.Kind == BattleEventKind.Damage), Is.EqualTo(2), "第 2 击打死,后两击不打");
            Assert.That(Burn(b, 0), Is.EqualTo(1), "打死那一击的附带不落在尸体上");
        }

        [Test]
        public void PerHit_KillingBlow_NoRiderAtAll_EvenKindsWithoutOwnAliveGuard()
        {
            // 终审 5:每击附带的入口统一判存活 —— 破甲 / 魅惑这类分支自己不判存活,以前会挂到尸体上
            var def = Char(new EffectDef(EffectKind.DamageSingle, 100, perHit: new[]
            {
                new EffectDef(EffectKind.ArmorBreak, 5, turns: 2), new EffectDef(EffectKind.Charm, 0, turns: 1),
                new EffectDef(EffectKind.Morale, 1),
            }));
            var b = Battle(def, 1, new[] { new EnemyDef("怔", Element.Heart, 1, 0), new EnemyDef("怔", Element.Heart, Hp, 0) });
            Assert.That(b.Cast("试", 0), Is.EqualTo(BattleError.None));
            Assert.That(b.Enemies[0].Alive, Is.False, "前提:这一击打死了目标");
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.ArmorBreak), Is.False, "破甲不挂尸体");
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.Charm), Is.False, "魅惑不挂尸体");
            Assert.That(b.PlayerStatuses.TotalMagnitude(StatusKind.Morale), Is.EqualTo(0),
                "口径:目标被这一击打死 → 这一击的附带整组作罢(我方侧也不结算)");
        }

        private static CharDef TraitChar(params TraitDef[] traits) => new("试", Element.Heart,
            effects: new[] { new EffectDef(EffectKind.Shield, 10) },
            attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 100) },
            traits: traits);

        [Test]
        public void PerHit_FromTrait_RidersCarryTraitKey_G11()
        {
            // 终审 5:特性带来的每击附带也是特性来源(G11),减攻与本体分开计时。Reshape(修饰器)与追加的伤害两条路都要打键
            var reshape = new TraitDef(TraitSlot.Lv5, TraitFace.Attack, TraitForm.Active, null, "连斩",
                new[] { new EffectDef(EffectKind.Reshape, 0, hitCount: 2,
                    perHit: new[] { new EffectDef(EffectKind.Weaken, 10, turns: 2) }) });
            var b = Battle(TraitChar(reshape), 5, Mobs(1));
            Assert.That(b.Cast("试", 0, attackMode: true), Is.EqualTo(BattleError.None));
            var curse = b.Enemies[0].Statuses.All.Single(x => x.Kind == StatusKind.Curse);
            Assert.That(curse.TraitKey, Is.EqualTo(BattleEngine.TraitKey("试", TraitSlot.Lv5, TraitFace.Attack)), "Reshape 带来的附带");

            var extra = new TraitDef(TraitSlot.Lv8, TraitFace.Attack, TraitForm.Active, null, "补刀",
                new[] { new EffectDef(EffectKind.DamageSingle, 10,
                    perHit: new[] { new EffectDef(EffectKind.Vulnerable, 10, turns: 1) }) });
            var c = Battle(TraitChar(extra), 8, Mobs(1));
            Assert.That(c.Cast("试", 0, attackMode: true), Is.EqualTo(BattleError.None));
            var mark = c.Enemies[0].Statuses.All.Single(x => x.Kind == StatusKind.Vulnerable);
            Assert.That(mark.TraitKey, Is.EqualTo(BattleEngine.TraitKey("试", TraitSlot.Lv8, TraitFace.Attack)), "追加伤害带的附带");
        }

        // ---------------- ConfigLoader ----------------

        private static RecipeGraph Load(string attackEffects) => Brushblade.Data.ConfigLoader.LoadGraph(
            @"{""chars"":[{""id"":""炎"",""element"":""Fire"",""effects"":[{""kind"":""BurnSingle"",""value"":2}],
              ""attackEffects"":[" + attackEffects + "]}]}");

        [Test]
        public void ConfigLoader_ParsesTask2Fields()
        {
            var g = Load(@"{""kind"":""DamageSingle"",""value"":40,""hitCount"":3,""perHitFrom"":2,""shotPercent"":50,
                ""perHit"":[{""kind"":""BurnSettleNow"",""keepStacks"":true},{""kind"":""ArmorBreak"",""value"":5,""turns"":2}]},
                {""kind"":""Detonate"",""retainPercent"":50},{""kind"":""Detonate"",""portionPercent"":50},
                {""kind"":""BurnScale"",""value"":200,""pick"":""All""},{""kind"":""BurnEqualize""},
                {""kind"":""Amplify"",""value"":5,""scaleBy"":""BurnStack"",""scaleCap"":50},
                {""kind"":""HealSelf"",""value"":20,""scaleBy"":""BurningEnemy""}");
            var e = g.Get("炎").AttackEffects;
            Assert.That(e[0].PerHitFrom, Is.EqualTo(2));
            Assert.That(e[0].ShotPercent, Is.EqualTo(50));
            Assert.That(e[0].PerHit.Select(p => p.Kind), Is.EqualTo(new[] { EffectKind.BurnSettleNow, EffectKind.ArmorBreak }));
            Assert.That(e[0].PerHit[0].KeepStacks, Is.True);
            Assert.That(e[0].PerHit[1].Turns, Is.EqualTo(2));
            Assert.That(e[1].RetainPercent, Is.EqualTo(50));
            Assert.That(e[2].PortionPercent, Is.EqualTo(50));
            Assert.That(e[3].Kind, Is.EqualTo(EffectKind.BurnScale));
            Assert.That(e[3].Pick, Is.EqualTo(EffectPick.All));
            Assert.That(e[4].Kind, Is.EqualTo(EffectKind.BurnEqualize));
            Assert.That(e[5].ScaleBy, Is.EqualTo(ScaleBasis.BurnStack));
            Assert.That(e[5].ScaleCap, Is.EqualTo(50));
            Assert.That(e[6].ScaleBy, Is.EqualTo(ScaleBasis.BurningEnemy));
        }

        [TestCase(@"{""kind"":""BurnSingle"",""value"":1,""perHit"":[{""kind"":""BurnSingle"",""value"":1}]}", TestName = "perHit 只挂伤害 / Reshape")]
        [TestCase(@"{""kind"":""DamageSingle"",""value"":1,""perHit"":[{""kind"":""DamageSingle"",""value"":1}]}", TestName = "perHit 里不能再有伤害")]
        [TestCase(@"{""kind"":""DamageSingle"",""value"":1,""perHit"":[{""kind"":""Amplify"",""value"":1}]}", TestName = "perHit 里不能有修饰器")]
        [TestCase(@"{""kind"":""DamageSingle"",""value"":1,""perHit"":[{""kind"":""BurnAll"",""value"":1,""openingBattles"":1}]}", TestName = "perHit 里不能开局登记")]
        [TestCase(@"{""kind"":""DamageSingle"",""value"":1,""perHitFrom"":2}", TestName = "perHitFrom 没有 perHit")]
        [TestCase(@"{""kind"":""Detonate"",""retainPercent"":50,""portionPercent"":50}", TestName = "retain 与 portion 二选一")]
        [TestCase(@"{""kind"":""Detonate"",""retainPercent"":100}", TestName = "retain 须 < 100")]
        [TestCase(@"{""kind"":""Detonate"",""portionPercent"":101}", TestName = "portion 须 ≤ 100")]
        [TestCase(@"{""kind"":""BurnSingle"",""value"":1,""retainPercent"":50}", TestName = "retain 只挂 Detonate")]
        [TestCase(@"{""kind"":""BurnScale"",""value"":50}", TestName = "BurnScale 须 ≥ 100")]
        [TestCase(@"{""kind"":""Shield"",""value"":5,""scaleBy"":""BurnStack""}", TestName = "per 只挂 Amplify / HealSelf")]
        [TestCase(@"{""kind"":""HealSelf"",""value"":5,""scaleBy"":""BurningEnemy"",""scaleCap"":10}", TestName = "cap 只给 Amplify")]
        [TestCase(@"{""kind"":""Amplify"",""value"":5,""scaleCap"":10}", TestName = "cap 没有 per")]
        [TestCase(@"{""kind"":""Amplify"",""value"":5,""scaleBy"":""Bogus""}", TestName = "per 取值未知")]
        [TestCase(@"{""kind"":""BurnSingle"",""value"":1,""shotPercent"":50}", TestName = "shotPercent 只挂伤害 / Reshape")]
        public void ConfigLoader_RejectsBadTask2Fields(string effect)
        {
            Assert.Throws<Brushblade.Data.ConfigException>(() => Load(@"{""kind"":""DamageSingle"",""value"":40}," + effect));
        }

        [Test]
        public void Fold_ReshapeCarriesPerHitAndShotPercent()
        {
            var rider = new EffectDef(EffectKind.BurnSingle, 1);
            var def = Char(new EffectDef(EffectKind.DamageSingle, 100),
                new EffectDef(EffectKind.Reshape, 0, shape: TargetArea.Scatter, shots: 4, shotPercent: 50, perHitFrom: 2,
                    perHit: new[] { rider }));
            var folded = TraitRules.CastEffects(def, CardFace.Feature, 1);
            Assert.That(folded.Single().PerHit.Single(), Is.SameAs(rider));
            Assert.That(folded.Single().PerHitFrom, Is.EqualTo(2));
            Assert.That(folded.Single().ShotPercent, Is.EqualTo(50));
        }

        // ---------------- 缺省参数逐位恒等 ----------------

        private static IEnumerable<EffectDef> Mixed(bool explicitDefaults)
        {
            if (!explicitDefaults)
            {
                yield return new EffectDef(EffectKind.DamageSingle, 100, shape: TargetArea.Scatter, shots: 3, hitCount: 2);
                yield return new EffectDef(EffectKind.BurnSingle, 3, pick: EffectPick.All);
                yield return new EffectDef(EffectKind.Detonate, 0, pick: EffectPick.Random);
                yield return new EffectDef(EffectKind.HealSelf, 15);
                yield return new EffectDef(EffectKind.Amplify, 20);
                yield break;
            }
            var none = Array.Empty<EffectDef>();
            yield return new EffectDef(EffectKind.DamageSingle, 100, shape: TargetArea.Scatter, shots: 3, hitCount: 2,
                perHit: none, perHitFrom: 1, shotPercent: 100);
            yield return new EffectDef(EffectKind.BurnSingle, 3, pick: EffectPick.All);
            yield return new EffectDef(EffectKind.Detonate, 0, pick: EffectPick.Random, retainPercent: 0, portionPercent: 100);
            yield return new EffectDef(EffectKind.HealSelf, 15, scaleBy: ScaleBasis.None, scaleCap: 0);
            yield return new EffectDef(EffectKind.Amplify, 20, scaleBy: ScaleBasis.None, scaleCap: 0);
        }

        [Test]
        public void NewFields_Defaults_AreBitwiseIdentity()
        {
            var plain = new EffectDef(EffectKind.DamageSingle, 1);
            Assert.That(plain.PerHit.Count, Is.EqualTo(0));
            Assert.That(plain.PerHitFrom, Is.EqualTo(1));
            Assert.That(plain.ShotPercent, Is.EqualTo(100));
            Assert.That(plain.RetainPercent, Is.EqualTo(0));
            Assert.That(plain.PortionPercent, Is.EqualTo(100));
            Assert.That(plain.ScaleBy, Is.EqualTo(ScaleBasis.None));
            Assert.That(plain.ScaleCap, Is.EqualTo(0));

            string Run(bool explicitDefaults)
            {
                var b = Battle(Char(Mixed(explicitDefaults).ToArray()), 5, Mobs(4));
                SetBurn(b, 1, 5);
                b.DamagePlayerForTest(100);
                var log = new List<string>();
                for (int round = 0; round < 3; round++)
                {
                    Assert.That(b.Cast("试", -1), Is.EqualTo(BattleError.None));
                    log.AddRange(b.LastEvents.Select(e => $"{e.Kind}/{e.TargetIndex}/{e.Amount}"));
                }
                log.Add(string.Join(",", b.Enemies.Select(e => $"{e.Hp}:{e.Statuses.Find(StatusKind.Burn)?.Magnitude}")));
                log.Add(b.PlayerHp.ToString());
                return string.Join("\n", log);
            }

            Assert.That(Run(true), Is.EqualTo(Run(false)), "新参数写成缺省值 ≡ 不写");
        }
    }
}
