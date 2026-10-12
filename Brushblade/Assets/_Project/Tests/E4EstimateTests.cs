using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>Plan E4 Task 1:预估签的 Core 读口 <see cref="BattleEngine.EstimateCast"/>(附录 §A 3.5)。
    /// 口径:暴击按不暴算(必暴照算)、生克按 ApparentElement、伤害取 Damage 事件 Amount 合计(未扣敌盾、不封顶)、
    /// 护盾 / 治疗取实际入账、反应链不算。主护栏 = 预估与「同态另一份 Restore 上真 Cast」逐只相等。</summary>
    public class E4EstimateTests
    {
        private const int Hp = 100000;

        private static RecipeGraph _real;
        private static RecipeGraph Real => _real ??= CharTableTests.RealGraph();

        private static BattleConfig Config(int crit = 0) => new BattleConfig
        {
            PlayerMaxHp = 5000, PlayerAttack = 100, ApPerTurn = 200, LibraryCapacity = 60, PoolCapacity = 20,
            PlayerCritChance = crit, BossPhaseJitterPercent = 0,
        };

        /// <summary>代表性场面:护甲怪(心)、木怪(金克木 / 被克)、金怪、带灼的水怪、Boss。id 互不相同(Restore 按 id 回查定义)。</summary>
        private static EnemyDef[] Field() => new[]
        {
            new EnemyDef("甲怪", Element.Heart, Hp, 0, defense: 40),
            new EnemyDef("木怪", Element.Wood, Hp, 0),
            new EnemyDef("金怪", Element.Metal, Hp, 0, defense: 20),
            new EnemyDef("水怪", Element.Water, Hp, 0),
            new EnemyDef("钧", Element.Fire, Hp, 0, defense: 30,
                phases: new[] { new BossPhaseDef("钧", Element.Fire, Hp, 0, defense: 30) }),
        };

        private static BattleEngine Battle(RecipeGraph graph, string id, int level, BattleConfig config = null,
            EnemyDef[] enemies = null, int seed = 1)
        {
            var b = new BattleEngine(graph, config ?? Config(), new[] { id, id, id }, Array.Empty<string>(),
                enemies ?? Field(), seed: seed, cardLevels: new Dictionary<string, int> { [id] = level });
            // 第 3 只带灼:Amplify / DoubleVs(Burning)类效果走得到条件分支
            b.Enemies[3].Statuses.Apply(new StatusEffect
                { Kind = StatusKind.Burn, Polarity = StatusPolarity.Debuff, Magnitude = 3, TurnsLeft = -1, Potency = 100 });
            return b;
        }

        private static Dictionary<string, EnemyDef> Defs(BattleEngine b)
        {
            var map = new Dictionary<string, EnemyDef>();
            foreach (var e in b.Enemies) map[e.Def.Id] = e.Def;
            return map;
        }

        private static BattleEngine Twin(BattleEngine b, RecipeGraph graph, BattleConfig config, string id, int level) =>
            BattleEngine.Restore(b.Capture(), graph, config, new Dictionary<string, int> { [id] = level }, Defs(b));

        private static Dictionary<int, int> DamageByEnemy(IEnumerable<BattleEvent> events) =>
            events.Where(e => e.Kind == BattleEventKind.Damage && e.TargetIndex >= 0)
                .GroupBy(e => e.TargetIndex).ToDictionary(g => g.Key, g => g.Sum(e => e.Amount));

        private static Dictionary<int, int> BurnByEnemy(IEnumerable<BattleEvent> events) =>
            events.Where(e => e.Kind == BattleEventKind.Burn && e.TargetIndex >= 0)
                .GroupBy(e => e.TargetIndex).ToDictionary(g => g.Key, g => g.Sum(e => e.Amount));

        private static string SnapshotJson(BattleEngine b) => Data.SaveSerializer.ToJson(new MetaState
        {
            EndlessV2 = new EndlessSaveState
            {
                InProgress = new InProgressRun { Run = new RunSnapshot { Battle = b.Capture() } },
            },
        });

        // ================= 1. 预估 == 实际(主护栏)=================

        /// <summary>真实字表每张字 × 两面 × Lv8,对每只敌人各试一次:预估的逐只伤害 / 灼 / 护盾 / 治疗
        /// == 同态另一份 Restore 上真 Cast 的事件合计。暴击率 0,反应链在 10 万血靶子上不触发(不击杀、不暴击)。</summary>
        [Test]
        public void Estimate_EqualsRealCast_OnTwin_AllRealChars()
        {
            int compared = 0;
            var failures = new List<string>();
            foreach (var def in Real.All)
            {
                if (def.IsComponent) continue;
                foreach (bool attack in new[] { false, true })
                {
                    if (attack && def.AttackEffects.Count == 0) continue;
                    for (int t = 0; t < 5; t++)
                    {
                        var config = Config();
                        var b = Battle(Real, def.Id, 8, config);
                        var est = b.EstimateCast(def.Id, t, attack);
                        var twin = Twin(b, Real, config, def.Id, 8);
                        var err = twin.Cast(def.Id, t, replaceSummon: true, attackMode: attack);
                        if (err != est.Error) { failures.Add($"{def.Id}{(attack ? "攻" : "特")}@{t}: Error 预估 {est.Error} 实际 {err}"); continue; }
                        if (err != BattleError.None) continue;
                        var dmg = DamageByEnemy(twin.LastEvents);
                        var burn = BurnByEnemy(twin.LastEvents);
                        var estDmg = est.Enemies.Where(h => h.Damage > 0).ToDictionary(h => h.Index, h => h.Damage);
                        var estBurn = est.Enemies.Where(h => h.Burn > 0).ToDictionary(h => h.Index, h => h.Burn);
                        int shield = twin.LastEvents.Where(e => e.Kind == BattleEventKind.Shield && e.TargetIndex == -1).Sum(e => e.Amount);
                        int heal = twin.LastEvents.Where(e => e.Kind == BattleEventKind.Heal && e.SecondIndex == -1).Sum(e => e.Amount);
                        string tag = $"{def.Id}{(attack ? "攻" : "特")}@{t}";
                        if (!Same(dmg, estDmg)) failures.Add($"{tag}: 伤害 预估 {Show(estDmg)} 实际 {Show(dmg)}");
                        if (!Same(burn, estBurn)) failures.Add($"{tag}: 灼 预估 {Show(estBurn)} 实际 {Show(burn)}");
                        if (shield != est.ShieldGranted) failures.Add($"{tag}: 护盾 预估 {est.ShieldGranted} 实际 {shield}");
                        if (heal != est.HealAmount) failures.Add($"{tag}: 治疗 预估 {est.HealAmount} 实际 {heal}");
                        compared++;
                    }
                }
            }
            Assert.That(failures, Is.Empty, string.Join("\n", failures));
            Assert.That(compared, Is.GreaterThan(300), "夹具前提:真的比对了足够多的组合");
        }

        private static bool Same(Dictionary<int, int> a, Dictionary<int, int> b) =>
            a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out int v) && v == kv.Value);

        private static string Show(Dictionary<int, int> d) =>
            "{" + string.Join(",", d.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}:{kv.Value}")) + "}";

        /// <summary>同一份预估也等于**本体**接着真出这一发的结果(影子与本体同态,不只是两份 Restore 互相一致)。</summary>
        [Test]
        public void Estimate_EqualsRealCast_OnOriginal()
        {
            foreach (var id in new[] { "炎", "杜", "桂", "淼", "鑫" })
            {
                var b = Battle(Real, id, 8);
                var est = b.EstimateCast(id, 1, attackMode: true);
                Assert.That(b.Cast(id, 1, replaceSummon: true, attackMode: true), Is.EqualTo(est.Error), id);
                var dmg = DamageByEnemy(b.LastEvents);
                Assert.That(Show(est.Enemies.Where(h => h.Damage > 0).ToDictionary(h => h.Index, h => h.Damage)),
                    Is.EqualTo(Show(dmg)), id);
            }
        }

        [Test]
        public void Estimate_TargetHit_Fields()
        {
            var hit = new CharDef("劈", Element.Metal, effects: new[] { new EffectDef(EffectKind.DamageSingle, 50) });
            var graph = RebalanceFixture.Graph(hit);
            var b = Battle(graph, "劈", 1);
            var est = b.EstimateCast("劈", 1, attackMode: false);   // 金克木
            Assert.That(est.Error, Is.EqualTo(BattleError.None));
            Assert.That(est.AllEnemies, Is.False);
            Assert.That(est.Enemies.Count, Is.EqualTo(1));
            var h = est.Enemies[0];
            Assert.That((h.Index, h.Damage, h.Hits, h.Ke, h.Countered, h.Burn, h.Executed, h.Killed),
                Is.EqualTo((1, 75, 1, true, false, 0, false, false)), "50 × 1.5,相克破甲");
            var est2 = b.EstimateCast("劈", 4, attackMode: false);   // 金打火 Boss:被克 0.5,护甲 30
            Assert.That((est2.Enemies[0].Ke, est2.Enemies[0].Countered), Is.EqualTo((false, true)));
            Assert.That(est2.Enemies[0].Damage, Is.EqualTo(25 * 100 / 130));
        }

        [Test]
        public void Estimate_Rejected_ReportsError_NoEnemies()
        {
            var hit = new CharDef("劈", Element.Metal, effects: new[] { new EffectDef(EffectKind.DamageSingle, 50) });
            var b = Battle(RebalanceFixture.Graph(hit), "劈", 1);
            var est = b.EstimateCast("劈", -1, attackMode: false);
            Assert.That(est.Error, Is.EqualTo(BattleError.InvalidTarget), "多敌未选目标");
            Assert.That(est.Enemies.Count, Is.EqualTo(0));
            Assert.That(b.EstimateCast("无此字", 0, false).Error, Is.EqualTo(BattleError.NotCastable));
        }

        /// <summary>A-Q14:AP 不够照样出签(影子补 AP)。</summary>
        [Test]
        public void Estimate_NotEnoughAp_StillEstimates()
        {
            var hit = new CharDef("劈", Element.Heart, effects: new[] { new EffectDef(EffectKind.DamageSingle, 50) });
            var config = Config();
            config.ApPerTurn = 1;
            var b = Battle(RebalanceFixture.Graph(hit), "劈", 1, config);
            Assert.That(b.Cast("劈", 3), Is.EqualTo(BattleError.None));
            Assert.That(b.Ap, Is.LessThan(hit.ApCost), "夹具前提:AP 不够");
            Assert.That(b.Cast("劈", 3), Is.EqualTo(BattleError.NotEnoughAp), "夹具前提:真出会被拒");
            var est = b.EstimateCast("劈", 3, false);
            Assert.That(est.Error, Is.EqualTo(BattleError.None));
            Assert.That(est.Enemies.Single().Damage, Is.EqualTo(50));
        }

        /// <summary>A-Q4:护盾取实际入账,盾满 = 0。</summary>
        [Test]
        public void Estimate_Shield_IsGranted_ZeroWhenFull()
        {
            var wall = RebalanceFixture.Char("墙", new EffectDef(EffectKind.Shield, 300));
            var config = Config();
            var b = new BattleEngine(RebalanceFixture.Graph(wall), config, new[] { "墙", "墙", "墙" }, Array.Empty<string>(),
                new[] { RebalanceFixture.Mob() }, seed: 1, startingNormalShield: 0);
            Assert.That(b.EstimateCast("墙", -1, false).ShieldGranted, Is.EqualTo(300));
            var full = new BattleEngine(RebalanceFixture.Graph(wall), config, new[] { "墙", "墙", "墙" }, Array.Empty<string>(),
                new[] { RebalanceFixture.Mob() }, seed: 1, startingNormalShield: 100000);
            Assert.That(full.EstimateCast("墙", -1, false).ShieldGranted, Is.EqualTo(0), "盾满:护盾 0");
        }

        // ================= 2. 不动本体 =================

        [Test]
        public void Estimate_DoesNotMutateOriginal()
        {
            var b = Battle(Real, "炎", 8, Config(crit: 50));
            Assert.That(b.Dismantle("炎"), Is.EqualTo(BattleError.None), "挂双焰印记,池里有火");
            b.Cast("炎", 0, attackMode: true);
            var events = b.LastEvents.Select(e => (e.Kind, e.TargetIndex, e.Amount)).ToList();
            string before = SnapshotJson(b);
            foreach (var id in new[] { "炎", "火" })
                for (int t = 0; t < 5; t++)
                    foreach (bool attack in new[] { false, true })
                        b.EstimateCast(id, t, attack);
            Assert.That(SnapshotJson(b), Is.EqualTo(before), "RNG 三流 / 限次表 / 印记 / AP / 库池 / 敌我状态全部不变");
            Assert.That(b.LastEvents.Select(e => (e.Kind, e.TargetIndex, e.Amount)).ToList(), Is.EqualTo(events), "LastEvents 不被覆盖");
        }

        [Test]
        public void Estimate_DoesNotTouchTally()
        {
            var config = Config();
            config.Tally = new BattleTally();
            var b = Battle(Real, "炎", 8, config);
            b.EstimateCast("炎", 0, attackMode: true);
            Assert.That(config.Tally.Plays.Count, Is.EqualTo(0));
            Assert.That(config.Tally.MaxHit, Is.EqualTo(0));
        }

        // ================= 3. 不泄露暴击 =================

        [Test]
        public void Estimate_Crit50_AlwaysNonCritValue()
        {
            var hit = new CharDef("劈", Element.Heart, effects: new[] { new EffectDef(EffectKind.DamageSingle, 60) });
            var graph = RebalanceFixture.Graph(hit);
            bool sawCrit = false, sawNoCrit = false;
            for (int seed = 1; seed <= 24; seed++)
            {
                var config = Config(crit: 50);
                var b = Battle(graph, "劈", 1, config, seed: seed);
                var est = b.EstimateCast("劈", 3, false);
                Assert.That(est.Enemies.Single().Damage, Is.EqualTo(60), $"seed {seed}:按不暴击算");
                b.Cast("劈", 3);
                bool crit = b.LastEvents.Single(e => e.Kind == BattleEventKind.Damage).Crit;
                sawCrit |= crit;
                sawNoCrit |= !crit;
            }
            Assert.That(sawCrit && sawNoCrit, Is.True, "夹具前提:实际出手暴与不暴都出现过");
        }

        [Test]
        public void Estimate_ForceCrit_Counted()
        {
            var hit = new CharDef("劈", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 60, forceCrit: true) });
            var b = Battle(RebalanceFixture.Graph(hit), "劈", 1);
            Assert.That(b.EstimateCast("劈", 3, false).Enemies.Single().Damage, Is.EqualTo(90), "必暴按确定量计入");
        }

        // ================= 4. 限次特性 =================

        private static TraitDef TraitOf(string id, string name) => Real.Get(id).Traits.Single(t => t.Name == name);

        [Test]
        public void Estimate_DumenBuchu_SpentRemovesDamageCut()
        {
            var b = Battle(Real, "杜", 8);
            var trait = TraitOf("杜", "杜门不出");
            var fresh = b.EstimateCast("杜", -1, attackMode: false);
            Assert.That(fresh.Error, Is.EqualTo(BattleError.None));
            Assert.That(fresh.Effects.Any(e => e.Kind == EffectKind.DamageCut), Is.True, "未用:进结算");
            Assert.That(fresh.SpentTraits.Count, Is.EqualTo(0));

            Assert.That(b.Cast("杜", -1), Is.EqualTo(BattleError.None));
            Assert.That(b.TraitSpentThisBattle(Real.Get("杜"), trait), Is.True, "夹具前提:本场已用");
            var spent = b.EstimateCast("杜", -1, attackMode: false);
            Assert.That(spent.Effects.Any(e => e.Kind == EffectKind.DamageCut), Is.False, "已用:不进结算");
            Assert.That(spent.SpentTraits.Contains(trait), Is.True);
            Assert.That(b.EstimateCast("杜", 0, attackMode: true).SpentTraits.Contains(trait), Is.False, "只列本面的");
        }

        [Test]
        public void Estimate_Zhegui_SpentListed()
        {
            var b = Battle(Real, "桂", 8);
            var def = Real.Get("桂");
            var trait = TraitOf("桂", "折桂");
            Assert.That(b.EstimateCast("桂", 0, attackMode: true).SpentTraits.Contains(trait), Is.False, "可用时不列");
            Assert.That(b.TryUseTrait(BattleEngine.BattleUseKey(def, trait), 0, trait.MaxPerBattle), Is.True);
            Assert.That(b.EstimateCast("桂", 0, attackMode: true).SpentTraits.Contains(trait), Is.True, "OnKill 的折桂也进(BattleLimited 漏它)");
        }

        [Test]
        public void Estimate_Effects_MatchCastEffectsNow_WhenNothingSpent()
        {
            var b = Battle(Real, "炎", 8);
            var est = b.EstimateCast("炎", 0, attackMode: true);
            Assert.That(est.Effects.Select(e => (e.Kind, e.Value)), Is.EqualTo(b.CastEffectsNow("炎", true).Select(e => (e.Kind, e.Value))));
        }

        // ================= 5. 印记 =================

        [Test]
        public void Estimate_PoolFire_WithTwinFlame_Burn2()
        {
            var b = new BattleEngine(Real, Config(), new[] { "炎", "炎" }, Array.Empty<string>(), Field(), seed: 1,
                cardLevels: new Dictionary<string, int> { ["炎"] = 4 });
            Assert.That(b.Dismantle("炎"), Is.EqualTo(BattleError.None));
            Assert.That(b.PartMarkCharges("火"), Is.EqualTo(2), "夹具前提:双焰印记");
            var est = b.EstimateCast("火", 2, attackMode: false);
            Assert.That(est.Error, Is.EqualTo(BattleError.None));
            Assert.That(est.AllEnemies, Is.False);
            var h = est.Enemies.Single();
            Assert.That((h.Index, h.Burn), Is.EqualTo((2, 2)), "兜底一击 + 双焰灼 2");
            Assert.That((h.Damage, h.Ke), Is.EqualTo((45, true)), "兜底 30,火克金 ×1.5 且破甲");
            Assert.That(est.Effects.Any(e => e.Kind == EffectKind.BurnSingle && e.Value == 2), Is.True, "印记已并入效果表");
            Assert.That(b.PartMarkCharges("火"), Is.EqualTo(2), "预估不消耗印记");
        }

        [Test]
        public void Estimate_PoolMountain_WithVolcano_AllEnemies()
        {
            var b = new BattleEngine(Real, Config(), new[] { "灿", "灿" }, Array.Empty<string>(), Field(), seed: 1,
                cardLevels: new Dictionary<string, int> { ["灿"] = 4 });
            Assert.That(b.Dismantle("灿"), Is.EqualTo(BattleError.None));
            var est = b.EstimateCast("山", 2, attackMode: false);
            Assert.That(est.Error, Is.EqualTo(BattleError.None));
            Assert.That(est.AllEnemies, Is.True);
            Assert.That(est.Enemies.Select(h => h.Index), Is.EqualTo(new[] { 0, 1, 2, 3, 4 }), "每只活敌一条");
            Assert.That(est.Enemies.All(h => h.Burn == 1 && h.Damage > 0), Is.True);
        }

        // ================= 生克按 ApparentElement =================

        [Test]
        public void Estimate_ObscureEnemy_NeutralUntilRevealed()
        {
            var hit = new CharDef("劈", Element.Metal, effects: new[] { new EffectDef(EffectKind.DamageSingle, 50) });
            var b = new BattleEngine(RebalanceFixture.Graph(hit), Config(), new[] { "劈", "劈", "劈" }, Array.Empty<string>(),
                new[] { new EnemyDef("僻", Element.Wood, Hp, 0, EnemyAbility.Obscure) }, seed: 1);
            Assert.That(b.Enemies[0].ApparentElement, Is.Null, "夹具前提:未读懂");
            var h = b.EstimateCast("劈", 0, false).Enemies.Single();
            Assert.That((h.Damage, h.Ke), Is.EqualTo((50, false)), "未现形按 ×1,不泄露金克木");
            b.Cast("劈", 0);
            Assert.That(b.LastEvents.First(e => e.Kind == BattleEventKind.Damage).Amount, Is.EqualTo(75), "实际按真身结算");
        }

        // ================= 反应链不算 =================

        [Test]
        public void Estimate_ExcludesReactions_KillCounted()
        {
            var splash = new TraitDef(TraitSlot.Lv1, TraitFace.Both, TraitForm.Passive, null, "迎刃",
                new[] { new EffectDef(EffectKind.DamageSingle, 15, shape: TargetArea.All) }, TraitTrigger.OnKill);
            var blade = new CharDef("刃", Element.Heart, effects: new[] { new EffectDef(EffectKind.DamageSingle, 60) },
                traits: new[] { splash });
            var b = new BattleEngine(RebalanceFixture.Graph(blade), Config(), new[] { "刃", "刃" }, Array.Empty<string>(),
                new[] { new EnemyDef("弱", Element.Heart, 40, 0), new EnemyDef("强", Element.Heart, Hp, 0) }, seed: 1);
            var est = b.EstimateCast("刃", 0, false);
            Assert.That(est.Enemies.Select(h => (h.Index, h.Damage, h.Killed)), Is.EqualTo(new[] { (0, 60, true) }),
                "只算这一下;击杀时的溅射不进签");
            b.Cast("刃", 0);
            Assert.That(b.LastEvents.Any(e => e.Kind == BattleEventKind.Damage && e.TargetIndex == 1), Is.True, "夹具前提:实际有溅射");
        }

        [Test]
        public void Estimate_Execute_Flagged()
        {
            var reaper = new CharDef("镰", Element.Heart, effects: new[]
                { new EffectDef(EffectKind.DamageSingle, 10, executeBelowPercent: 50, executeKills: true) });
            var b = new BattleEngine(RebalanceFixture.Graph(reaper), Config(), new[] { "镰", "镰" }, Array.Empty<string>(),
                new[] { new EnemyDef("残", Element.Heart, 1000, 0) }, seed: 1, startingHp: null);
            b.Enemies[0].Hp = 300;
            var h = b.EstimateCast("镰", 0, false).Enemies.Single();
            Assert.That((h.Executed, h.Killed), Is.EqualTo((true, true)));
        }

        /// <summary>A-Q6:多段 = 合计 + Hits。</summary>
        [Test]
        public void Estimate_MultiHit_SumsAndCounts()
        {
            var chop = new CharDef("剁", Element.Heart, effects: new[] { new EffectDef(EffectKind.DamageSingle, 20, hitCount: 3) });
            var b = Battle(RebalanceFixture.Graph(chop), "剁", 1);
            var h = b.EstimateCast("剁", 3, false).Enemies.Single();
            Assert.That((h.Damage, h.Hits), Is.EqualTo((60, 3)));
        }
    }
}
