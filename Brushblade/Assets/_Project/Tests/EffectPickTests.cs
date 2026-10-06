using System;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>敌方侧新效果(D1 Task 5,附录 M5 / M8 / M10 / M24):减攻 Weaken、效果目标选择器 EffectPick、
    /// 条件门 OnlyIf 用于非伤害效果、灼即时结算不减层 KeepStacks。
    ///
    /// 夹具口径同 TraitModifierTests:Element.Heart(生克 1.0×)、PlayerAttack = 100、无甲 10 万血靶子。</summary>
    public class EffectPickTests
    {
        private const int Hp = 100000;

        private static BattleEngine Battle(CharDef def, params EnemyDef[] enemies) =>
            Battle(new[] { def }, enemies);

        private static BattleEngine Battle(CharDef[] defs, params EnemyDef[] enemies) =>
            new(RebalanceFixture.Graph(defs),
                new BattleConfig { PlayerMaxHp = RebalanceFixture.BaseMaxHp, PlayerAttack = 100 },
                defs.SelectMany(d => new[] { d.Id, d.Id }).ToArray(), Array.Empty<string>(),
                enemies.Length > 0 ? enemies : new[] { RebalanceFixture.Mob() }, seed: 1);

        private static CharDef Def(params EffectDef[] effects) => new("试", Element.Heart, effects: effects);

        private static EnemyDef[] Mobs(int n) => Enumerable.Range(0, n).Select(_ => RebalanceFixture.Mob()).ToArray();

        private static StatusEffect Burn(int stacks) => new()
        {
            Kind = StatusKind.Burn, Polarity = StatusPolarity.Debuff, Magnitude = stacks, TurnsLeft = -1,
        };

        private static StatusEffect SlowMark() => new()
        {
            Kind = StatusKind.SpeedModifier, Polarity = StatusPolarity.Debuff, Magnitude = -50, TurnsLeft = 2,
            SourceId = "旧",
        };

        private static int Burning(BattleEngine b, int i) => b.Enemies[i].Statuses.Find(StatusKind.Burn)?.Magnitude ?? 0;
        private static bool Slowed(BattleEngine b, int i) =>
            b.Enemies[i].Statuses.All.Any(s => s.Kind == StatusKind.SpeedModifier && s.Magnitude < 0);
        private static bool Broken(BattleEngine b, int i) => b.Enemies[i].Statuses.Has(StatusKind.ArmorBreak);

        // ---------------- Pick ----------------

        [Test]
        public void Pick_All_AffectsEveryLivingEnemy_AndNeedsNoTarget()
        {
            var def = Def(new EffectDef(EffectKind.BurnSingle, 2, pick: EffectPick.All));
            Assert.That(BattleEngine.NeedsTarget(def), Is.False, "Pick != Primary 不需要玩家选目标");
            var b = Battle(def, Mobs(3));
            b.Enemies[1].Hp = 0;
            Assert.That(b.Cast("试"), Is.EqualTo(BattleError.None));
            Assert.That(Burning(b, 0), Is.EqualTo(2));
            Assert.That(Burning(b, 1), Is.EqualTo(0), "已阵亡的不吃");
            Assert.That(Burning(b, 2), Is.EqualTo(2));
        }

        [Test]
        public void LegacyTargetAll_IsEquivalentToPickAll()
        {
            var legacy = Def(new EffectDef(EffectKind.Blind, 30, turns: 2, targetAll: true));
            var picked = Def(new EffectDef(EffectKind.Blind, 30, turns: 2, pick: EffectPick.All));
            Assert.That(BattleEngine.NeedsTarget(legacy), Is.False);
            Assert.That(BattleEngine.NeedsTarget(picked), Is.False);
            var a = Battle(legacy, Mobs(3));
            var b = Battle(picked, Mobs(3));
            a.Cast("试");
            b.Cast("试");
            for (int i = 0; i < 3; i++)
            {
                Assert.That(b.Enemies[i].Statuses.Has(StatusKind.Blind), Is.True);
                Assert.That(b.Enemies[i].Statuses.Find(StatusKind.Blind).Magnitude,
                    Is.EqualTo(a.Enemies[i].Statuses.Find(StatusKind.Blind).Magnitude));
            }
        }

        [Test]
        public void Pick_Primary_StillNeedsTarget()
        {
            Assert.That(BattleEngine.NeedsTarget(Def(new EffectDef(EffectKind.Slow, 1))), Is.True);
            Assert.That(BattleEngine.NeedsTarget(Def(new EffectDef(EffectKind.Weaken, 15, turns: 2))), Is.True);
        }

        [Test]
        public void Pick_Random_PicksOneLivingEnemy_UsingOnlyTraitRandom()
        {
            var def = Def(new EffectDef(EffectKind.Slow, 1, pick: EffectPick.Random));
            var b = Battle(def, Mobs(4));
            b.Enemies[0].Hp = 0;
            var before = b.Capture();
            b.Cast("试");
            var after = b.Capture();
            Assert.That(Enumerable.Range(0, 4).Count(i => Slowed(b, i)), Is.EqualTo(1), "恰好一名");
            Assert.That(Slowed(b, 0), Is.False, "只选存活敌人");
            Assert.That(after.RandomState, Is.EqualTo(before.RandomState), "主流不动");
            Assert.That(after.TargetRandomState, Is.EqualTo(before.TargetRandomState), "择敌流不动");
            Assert.That(after.TraitRandomState, Is.Not.EqualTo(before.TraitRandomState), "特性流摇了一次");
        }

        [Test]
        public void Pick_Random_NoLivingEnemy_DoesNotRoll()
        {
            var def = Def(new EffectDef(EffectKind.Slow, 1, pick: EffectPick.Random),
                new EffectDef(EffectKind.Shield, 5));
            var b = Battle(def, Mobs(1));
            b.Enemies[0].Hp = 0;
            uint before = b.Capture().TraitRandomState;
            b.Cast("试");
            Assert.That(b.Capture().TraitRandomState, Is.EqualTo(before));
        }

        [Test]
        public void Pick_HitTargets_FollowsRowSweep_AndDedupes()
        {
            var def = Def(new EffectDef(EffectKind.DamageSingle, 10, shape: TargetArea.Row),
                new EffectDef(EffectKind.ArmorBreak, 5, turns: 2, pick: EffectPick.HitTargets));
            var b = Battle(def, Mobs(6));
            b.Cast("试", 0);
            var hit = Enumerable.Range(0, 6).Where(i => b.Enemies[i].Hp < Hp).ToList();
            var broken = Enumerable.Range(0, 6).Where(i => Broken(b, i)).ToList();
            Assert.That(hit.Count, Is.GreaterThan(1), "横扫命中整排");
            Assert.That(hit.Count, Is.LessThan(6), "至少有一只没被扫到");
            Assert.That(broken, Is.EqualTo(hit), "被命中的整排都破甲,没被命中的不破");
            foreach (int i in broken)
                Assert.That(b.Enemies[i].Statuses.All.Count(s => s.Kind == StatusKind.ArmorBreak), Is.EqualTo(1));
        }

        [Test]
        public void Pick_HitTargets_RepeatedHitsOnSameEnemy_CountOnce()
        {
            var def = Def(new EffectDef(EffectKind.DamageSingle, 10, shape: TargetArea.Scatter, shots: 3),
                new EffectDef(EffectKind.ArmorBreak, 5, turns: 2, pick: EffectPick.HitTargets));
            var b = Battle(def, Mobs(1));
            b.Cast("试");
            Assert.That(b.Enemies[0].Statuses.All.Count(s => s.Kind == StatusKind.ArmorBreak), Is.EqualTo(1),
                "连发 3 次打同一只,按命中顺序去重");
        }

        [Test]
        public void Pick_HitTargets_IsClearedPerCast()
        {
            var hitter = new CharDef("打", Element.Heart, effects: new[] { new EffectDef(EffectKind.DamageSingle, 10) });
            var breaker = new CharDef("破", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.ArmorBreak, 5, turns: 2, pick: EffectPick.HitTargets) });
            var b = Battle(new[] { hitter, breaker }, Mobs(2));
            b.Cast("打", 0);
            b.Cast("破");
            Assert.That(Broken(b, 0), Is.False, "上一张字命中过的不算本张的命中");
            Assert.That(Broken(b, 1), Is.False);
        }

        [Test]
        public void Pick_MostBurn_HighestStacks_TieLowestIndex()
        {
            var def = Def(new EffectDef(EffectKind.Slow, 1, pick: EffectPick.MostBurn));
            var b = Battle(def, Mobs(4));
            b.Enemies[0].Statuses.Apply(Burn(1));
            b.Enemies[1].Statuses.Apply(Burn(3));
            b.Enemies[2].Statuses.Apply(Burn(3));
            b.Cast("试");
            Assert.That(Enumerable.Range(0, 4).Where(i => Slowed(b, i)).ToList(), Is.EqualTo(new[] { 1 }));
        }

        [Test]
        public void Pick_MostBurn_NobodyBurning_DoesNothing()
        {
            var def = Def(new EffectDef(EffectKind.Slow, 1, pick: EffectPick.MostBurn),
                new EffectDef(EffectKind.Shield, 5));
            var b = Battle(def, Mobs(3));
            b.Cast("试");
            Assert.That(Enumerable.Range(0, 3).Any(i => Slowed(b, i)), Is.False);
        }

        [Test]
        public void Pick_FrozenByThisCast_OnlyEnemiesActuallyFrozen()
        {
            var def = Def(new EffectDef(EffectKind.Freeze, 1, pick: EffectPick.All),
                new EffectDef(EffectKind.ArmorBreak, 5, turns: 2, pick: EffectPick.FrozenByThisCast));
            var b = Battle(def, RebalanceFixture.Mob(), RebalanceFixture.Mob(), RebalanceFixture.Boss());
            b.Enemies[1].Statuses.Apply(new StatusEffect
            {
                Kind = StatusKind.Freeze, Polarity = StatusPolarity.Debuff, TurnsLeft = 1, Magnitude = 1,
            });
            b.Cast("试");
            Assert.That(Broken(b, 0), Is.True, "本次冻住的");
            Assert.That(Broken(b, 1), Is.False, "早就冻着的:冻结没成功");
            Assert.That(Broken(b, 2), Is.False, "Boss 只吃冰滞,不是冻结");
            Assert.That(b.Enemies[2].Statuses.Has(StatusKind.IceStall), Is.True);
        }

        [Test]
        public void Pick_FrozenByThisCast_IsClearedPerCast()
        {
            var freezer = new CharDef("冻", Element.Heart, effects: new[] { new EffectDef(EffectKind.Freeze, 1) });
            var breaker = new CharDef("破", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.ArmorBreak, 5, turns: 2, pick: EffectPick.FrozenByThisCast) });
            var b = Battle(new[] { freezer, breaker }, Mobs(1));
            b.Cast("冻", 0);
            b.Cast("破");
            Assert.That(Broken(b, 0), Is.False);
        }

        // ---------------- Weaken ----------------

        [Test]
        public void Weaken_AppliesCurse_WithTurnsAndSource()
        {
            var b = Battle(Def(new EffectDef(EffectKind.Weaken, 15, turns: 2)), Mobs(1));
            b.Cast("试", 0);
            var curse = b.Enemies[0].Statuses.Find(StatusKind.Curse);
            Assert.That(curse, Is.Not.Null);
            Assert.That(curse.Magnitude, Is.EqualTo(15));
            Assert.That(curse.TurnsLeft, Is.EqualTo(2));
            Assert.That(curse.SourceId, Is.EqualTo("试"));
            Assert.That(curse.Polarity, Is.EqualTo(StatusPolarity.Debuff));
        }

        [TestCase(40, 1, 15, 2, 40, 2, TestName = "Weaken_SameSource_KeepsStrongerMagnitude_AndLongerTurns")]
        [TestCase(10, 5, 25, 2, 25, 5, TestName = "Weaken_SameSource_KeepsStrongerNew_AndLongerOld")]
        public void Weaken_SameSourceRefresh_TakesMaxOfBoth(int oldMag, int oldTurns, int newMag, int newTurns,
            int expectMag, int expectTurns)
        {
            var b = Battle(Def(new EffectDef(EffectKind.Weaken, newMag, turns: newTurns)), Mobs(1));
            b.Enemies[0].Statuses.Apply(new StatusEffect
            {
                Kind = StatusKind.Curse, Polarity = StatusPolarity.Debuff,
                Magnitude = oldMag, TurnsLeft = oldTurns, SourceId = "试",
            });
            b.Cast("试", 0);
            var all = b.Enemies[0].Statuses.All.Where(s => s.Kind == StatusKind.Curse).ToList();
            Assert.That(all.Count, Is.EqualTo(1));
            Assert.That(all[0].Magnitude, Is.EqualTo(expectMag));
            Assert.That(all[0].TurnsLeft, Is.EqualTo(expectTurns));
        }

        [Test]
        public void Weaken_LowersEnemyAttack()
        {
            var b = Battle(Def(new EffectDef(EffectKind.Weaken, 25, turns: 2)), RebalanceFixture.Mob(attack: 100));
            b.Cast("试", 0);
            Assert.That(b.Enemies[0].Attack, Is.EqualTo(75));
        }

        // ---------------- OnlyIf ----------------

        [Test]
        public void OnlyIf_NotMet_DoesNotApply_Met_Applies()
        {
            var def = Def(new EffectDef(EffectKind.BurnSingle, 2, onlyIf: DamageCondition.Slowed));
            var plain = Battle(def, Mobs(1));
            plain.Cast("试", 0);
            Assert.That(Burning(plain, 0), Is.EqualTo(0), "目标没被减速:不施加");

            var slowed = Battle(def, Mobs(1));
            slowed.Enemies[0].Statuses.Apply(SlowMark());
            slowed.Cast("试", 0);
            Assert.That(Burning(slowed, 0), Is.EqualTo(2));
        }

        [Test]
        public void OnlyIf_IsJudgedPerPickedTarget()
        {
            var def = Def(new EffectDef(EffectKind.BurnSingle, 2, pick: EffectPick.All, onlyIf: DamageCondition.Slowed));
            var b = Battle(def, Mobs(3));
            b.Enemies[1].Statuses.Apply(SlowMark());
            b.Cast("试");
            Assert.That(Enumerable.Range(0, 3).Select(i => Burning(b, i)).ToArray(), Is.EqualTo(new[] { 0, 2, 0 }));
        }

        [Test]
        public void OnlyIf_UsesPreCastSnapshot_NotMidCastState()
        {
            // 本字先减速、再「对被减速者」上灼:快照在出字前取,出字中途才挂上的减速不算
            var def = Def(new EffectDef(EffectKind.Slow, 1),
                new EffectDef(EffectKind.BurnSingle, 2, onlyIf: DamageCondition.Slowed));
            var b = Battle(def, Mobs(1));
            b.Cast("试", 0);
            Assert.That(Slowed(b, 0), Is.True);
            Assert.That(Burning(b, 0), Is.EqualTo(0));
        }

        // ---------------- KeepStacks ----------------

        [Test]
        public void BurnSettleNow_KeepStacks_SettlesWithoutDecay()
        {
            var keep = Battle(Def(new EffectDef(EffectKind.BurnSettleNow, 0, keepStacks: true)), Mobs(1));
            keep.Enemies[0].Statuses.Apply(Burn(3));
            keep.Cast("试", 0);
            Assert.That(Hp - keep.Enemies[0].Hp, Is.GreaterThan(0), "结算了一次");
            Assert.That(Burning(keep, 0), Is.EqualTo(3), "层数不减");

            var plain = Battle(Def(new EffectDef(EffectKind.BurnSettleNow, 0)), Mobs(1));
            plain.Enemies[0].Statuses.Apply(Burn(3));
            plain.Cast("试", 0);
            Assert.That(Burning(plain, 0), Is.EqualTo(2), "缺省仍减 1 层");
            Assert.That(Hp - plain.Enemies[0].Hp, Is.EqualTo(Hp - keep.Enemies[0].Hp), "伤害相同");
        }

        [Test]
        public void BurnSettleNow_PickAll_SettlesEveryEnemy_AndNeedsNoTarget()
        {
            var def = Def(new EffectDef(EffectKind.BurnSettleNow, 0, pick: EffectPick.All, keepStacks: true));
            Assert.That(BattleEngine.NeedsTarget(def), Is.False);
            var b = Battle(def, Mobs(2));
            b.Enemies[0].Statuses.Apply(Burn(2));
            b.Enemies[1].Statuses.Apply(Burn(4));
            b.Cast("试");
            Assert.That(b.Enemies[0].Hp, Is.LessThan(Hp));
            Assert.That(b.Enemies[1].Hp, Is.LessThan(Hp));
            Assert.That(Burning(b, 0), Is.EqualTo(2));
            Assert.That(Burning(b, 1), Is.EqualTo(4));
        }

        [Test]
        public void Detonate_PickMostBurn_OnlyThatEnemy()
        {
            var def = Def(new EffectDef(EffectKind.Detonate, 0, pick: EffectPick.MostBurn));
            var b = Battle(def, Mobs(2));
            b.Enemies[0].Statuses.Apply(Burn(2));
            b.Enemies[1].Statuses.Apply(Burn(5));
            b.Cast("试");
            Assert.That(Burning(b, 1), Is.EqualTo(0));
            Assert.That(Burning(b, 0), Is.EqualTo(2));
        }

        // ---------------- Config ----------------

        private static RecipeGraph Load(string effects) => Brushblade.Data.ConfigLoader.LoadGraph(
            @"{""chars"":[{""id"":""甲"",""element"":""Fire"",""effects"":[" + effects + "]}]}");

        private static Brushblade.Data.ConfigException LoadThrows(string effects) =>
            Assert.Throws<Brushblade.Data.ConfigException>(() => Load(effects));

        [Test]
        public void Config_ParsesPickOnlyIfAndKeepStacks()
        {
            var g = Load(@"{""kind"":""Slow"",""value"":1,""pick"":""Random"",""onlyIf"":""Frozen""},
                {""kind"":""BurnSettleNow"",""pick"":""All"",""keepStacks"":true},
                {""kind"":""Weaken"",""value"":20,""turns"":1,""pick"":""HitTargets""}");
            var e = g.Get("甲").Effects;
            Assert.That(e[0].Pick, Is.EqualTo(EffectPick.Random));
            Assert.That(e[0].OnlyIf, Is.EqualTo(DamageCondition.Frozen));
            Assert.That(e[1].Pick, Is.EqualTo(EffectPick.All));
            Assert.That(e[1].KeepStacks, Is.True);
            Assert.That(e[2].Kind, Is.EqualTo(EffectKind.Weaken));
            Assert.That(e[2].Pick, Is.EqualTo(EffectPick.HitTargets));
        }

        [Test]
        public void Config_Rejects_UnknownPick_PickOnUnsupportedKind_KeepOnOtherKind()
        {
            Assert.That(LoadThrows(@"{""kind"":""Slow"",""value"":1,""pick"":""Bogus""}").Message, Does.Contain("Bogus"));
            Assert.That(LoadThrows(@"{""kind"":""Shield"",""value"":3,""pick"":""All""}").Message, Does.Contain("pick"));
            Assert.That(LoadThrows(@"{""kind"":""DamageSingle"",""value"":3,""pick"":""Random""}").Message, Does.Contain("pick"));
            Assert.That(LoadThrows(@"{""kind"":""Detonate"",""keepStacks"":true}").Message, Does.Contain("keepStacks"));
        }

        [Test]
        public void Config_OnlyIf_AllowedOnPickKinds_StillRejectedElsewhere()
        {
            Assert.DoesNotThrow(() => Load(@"{""kind"":""BurnSingle"",""value"":2,""onlyIf"":""Burning""}"));
            Assert.That(LoadThrows(@"{""kind"":""Shield"",""value"":3,""onlyIf"":""Burning""}").Message, Does.Contain("onlyIf"));
        }
    }
}
