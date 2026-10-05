using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>新状态 种 Seed / 标记 Vulnerable(D1 Task 6,附录 M6 / M11)。
    ///
    /// 夹具口径同 EffectPickTests:Element.Heart(生克 1.0×)、PlayerAttack = 100、无甲 10 万血靶子、
    /// 敌人攻击 0(不会打断「谁血最低」的构图)。</summary>
    public class SeedVulnerableTests
    {
        private static BattleConfig Config => new() { PlayerMaxHp = RebalanceFixture.BaseMaxHp, PlayerAttack = 100 };

        private static BattleEngine Battle(CharDef[] defs, int? startingHp = null,
            IReadOnlyList<SummonSnapshot> summons = null, params EnemyDef[] enemies) =>
            new(RebalanceFixture.Graph(defs), Config,
                defs.SelectMany(d => new[] { d.Id, d.Id, d.Id }).Concat(new[] { "甲", "甲" }).ToArray(), Array.Empty<string>(),
                enemies.Length > 0 ? enemies : new[] { RebalanceFixture.Mob() }, seed: 1,
                startingHp: startingHp, startingSummons: summons);

        private static CharDef Def(string id, params EffectDef[] effects) => new(id, Element.Heart, effects: effects);

        private static SummonSnapshot Summon(int slot, int hp, int maxHp = 100) => new()
        {
            Slot = slot, Char = "木", Element = Element.Wood, Hp = hp, MaxHp = maxHp, Attack = 0, Speed = 100,
        };

        private static readonly CharDef SeedChar = Def("种", new EffectDef(EffectKind.Seed, 10, turns: 2));

        private static BattleEngine SeedBattle(int? hp, IReadOnlyList<SummonSnapshot> summons = null, CharDef seed = null)
        {
            var b = Battle(new[] { seed ?? SeedChar }, hp, summons);
            Assert.That(b.Cast((seed ?? SeedChar).Id, 0), Is.EqualTo(BattleError.None));
            return b;
        }

        // ---------------- 种 ----------------

        [Test]
        public void Seed_AttachesToEnemy_WithValueTurnsAndSource()
        {
            var b = SeedBattle(null);
            var s = b.Enemies[0].Statuses.Find(StatusKind.Seed);
            Assert.That(s, Is.Not.Null);
            Assert.That(s.Magnitude, Is.EqualTo(10));
            Assert.That(s.TurnsLeft, Is.EqualTo(2));
            Assert.That(s.SourceId, Is.EqualTo("种"));
            Assert.That(BattleEngine.NeedsTarget(SeedChar), Is.True, "种要选敌方目标");
        }

        [Test]
        public void Seed_OnEnemyAction_HealsLowestRatioUnit()
        {
            // 玩家 400/500 = 800‰;召唤物 50/100 = 500‰ → 召唤物最低
            var b = SeedBattle(400, new[] { Summon(0, 50) });
            b.EndTurn();
            Assert.That(b.Summons[0].Hp, Is.EqualTo(60));
            Assert.That(b.PlayerHp, Is.EqualTo(400), "玩家不是最低比例,不被治疗");
        }

        [Test]
        public void Seed_CompareByRatio_NotAbsoluteHp()
        {
            // 玩家 300/500 = 600‰ 比召唤物 40/50 = 800‰ 低,尽管召唤物绝对血更少
            var b = SeedBattle(300, new[] { Summon(0, 40, 50) });
            b.EndTurn();
            Assert.That(b.PlayerHp, Is.EqualTo(310));
            Assert.That(b.Summons[0].Hp, Is.EqualTo(40));
        }

        [Test]
        public void Seed_SameRatio_PlayerFirst_ThenLowerSlot()
        {
            // 玩家 250/500 与 槽 0、槽 2 的 50/100 同为 500‰ → 玩家
            var b = SeedBattle(250, new[] { Summon(0, 50), Summon(2, 50) });
            b.EndTurn();
            Assert.That(b.PlayerHp, Is.EqualTo(260));
            Assert.That(b.Summons[0].Hp, Is.EqualTo(50));
            Assert.That(b.Summons[2].Hp, Is.EqualTo(50));

            // 玩家满血:槽 0 与槽 2 同比例 → 槽小者
            var c = SeedBattle(null, new[] { Summon(2, 50), Summon(0, 50) });
            c.EndTurn();
            Assert.That(c.Summons[0].Hp, Is.EqualTo(60));
            Assert.That(c.Summons[2].Hp, Is.EqualTo(50));
        }

        [Test]
        public void Seed_IgnoresDeadSummons()
        {
            var dead = Summon(0, 0);
            var b = SeedBattle(400, new[] { dead, Summon(1, 90) });
            b.EndTurn();
            Assert.That(b.PlayerHp, Is.EqualTo(410), "尸体不参选,玩家 800‰ < 召唤物 900‰");
        }

        [Test]
        public void Seed_FrozenBeatStillCounts()
        {
            var def = Def("冻种", new EffectDef(EffectKind.Freeze, 2), new EffectDef(EffectKind.Seed, 10, turns: 3));
            var b = SeedBattle(300, null, def);
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.Freeze), Is.True);
            b.EndTurn();   // 被冻结的这一拍:跳过出手,但仍是一次「行动」
            Assert.That(b.PlayerHp, Is.EqualTo(310));
        }

        [Test]
        public void Seed_ExpiresAfterItsTurns_EachActionHealsOnce()
        {
            var b = SeedBattle(100);
            b.EndTurn();
            Assert.That(b.PlayerHp, Is.EqualTo(110));
            Assert.That(b.Enemies[0].Statuses.Find(StatusKind.Seed).TurnsLeft, Is.EqualTo(1));
            b.EndTurn();
            Assert.That(b.PlayerHp, Is.EqualTo(120));
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.Seed), Is.False, "2 回合 = 治疗 2 次后移除");
            b.EndTurn();
            Assert.That(b.PlayerHp, Is.EqualTo(120));
        }

        [Test]
        public void Seed_DoesNotOverhealPastMax()
        {
            var b = SeedBattle(495);
            b.EndTurn();
            Assert.That(b.PlayerHp, Is.EqualTo(500));
        }

        [Test]
        public void Seed_SameSource_RefreshesToStrongerAndLonger()
        {
            var b = SeedBattle(null);
            var cur = b.Enemies[0].Statuses.Find(StatusKind.Seed);
            cur.Magnitude = 5;      // 旧条目:较弱但较长
            cur.TurnsLeft = 4;
            b.Cast("种", 0);        // 新施放:10 / 2 回合
            var all = b.Enemies[0].Statuses.All.Where(s => s.Kind == StatusKind.Seed).ToList();
            Assert.That(all.Count, Is.EqualTo(1), "同源不叠条");
            Assert.That(all[0].Magnitude, Is.EqualTo(10), "取较大量");
            Assert.That(all[0].TurnsLeft, Is.EqualTo(4), "取较长回合");
        }

        [Test]
        public void Seed_DifferentSources_Coexist_AndEachHeals()
        {
            var a = Def("甲种", new EffectDef(EffectKind.Seed, 10, turns: 2));
            var c = Def("乙种", new EffectDef(EffectKind.Seed, 7, turns: 2));
            var b = Battle(new[] { a, c }, 100);
            b.Cast("甲种", 0);
            b.Cast("乙种", 0);
            b.EndTurn();
            Assert.That(b.PlayerHp, Is.EqualTo(117));
        }

        [Test]
        public void Seed_ValueScalesWithCardLevel_TurnsDoNot()
        {
            Assert.That(MetaRules.ScalesWithCardLevel(EffectKind.Seed), Is.True);
            var b = new BattleEngine(RebalanceFixture.Graph(SeedChar), Config, new[] { "种" },
                Array.Empty<string>(), new[] { RebalanceFixture.Mob() }, seed: 1,
                cardLevels: new Dictionary<string, int> { ["种"] = 10 });
            b.Cast("种", 0);
            var s = b.Enemies[0].Statuses.Find(StatusKind.Seed);
            Assert.That(s.Magnitude, Is.EqualTo(MetaRules.ScaleByCardLevel(10, 10)));
            Assert.That(s.TurnsLeft, Is.EqualTo(2));
        }

        [Test]
        public void Seed_AmplifyScopeSeed_RaisesHealAmount()
        {
            Assert.That(TraitRules.InScope(AmpScope.Seed, EffectKind.Seed), Is.True);
            Assert.That(TraitRules.InScope(AmpScope.Seed, EffectKind.HealSelf), Is.False);
            Assert.That(TraitRules.InScope(AmpScope.Heal, EffectKind.Seed), Is.False);
            var def = Def("种", new EffectDef(EffectKind.Seed, 10, turns: 2),
                new EffectDef(EffectKind.Amplify, 50, scope: AmpScope.Seed));
            var b = Battle(new[] { def });
            b.Cast("种", 0);
            Assert.That(b.Enemies[0].Statuses.Find(StatusKind.Seed).Magnitude, Is.EqualTo(15));
        }

        [Test]
        public void Seed_TurnsAreRegisteredForAugment()
        {
            Assert.That(TraitRules.HasTurns(EffectKind.Seed), Is.True);
            Assert.That(TraitRules.TurnsOf(new EffectDef(EffectKind.Seed, 10, turns: 2)), Is.EqualTo(2));
            Assert.That(TraitRules.WithTurns(new EffectDef(EffectKind.Seed, 10, turns: 2), 1).Turns, Is.EqualTo(3));
        }

        [Test]
        public void Seed_SurvivesSnapshotRoundTrip()
        {
            var b = SeedBattle(100);
            var restored = BattleEngine.Restore(b.Capture(), RebalanceFixture.Graph(SeedChar), Config, null,
                new Dictionary<string, EnemyDef> { ["怔"] = RebalanceFixture.Mob() });
            var s = restored.Enemies[0].Statuses.Find(StatusKind.Seed);
            Assert.That(s.Magnitude, Is.EqualTo(10));
            Assert.That(s.TurnsLeft, Is.EqualTo(2));
            Assert.That(s.SourceId, Is.EqualTo("种"));
            restored.EndTurn();
            Assert.That(restored.PlayerHp, Is.EqualTo(110));
        }

        // ---------------- 标记 ----------------

        private static readonly CharDef Mark = Def("标", new EffectDef(EffectKind.Vulnerable, 20, turns: 2));

        [Test]
        public void Vulnerable_RaisesDamageTaken_ByPercent()
        {
            var b = Battle(new[] { Mark });
            b.Cast("标", 0);
            Assert.That(b.Enemies[0].Statuses.Find(StatusKind.Vulnerable).Magnitude, Is.EqualTo(20));
            int before = b.Enemies[0].Hp;
            b.Cast("甲", 0);   // 基础 20,×1.2 = 24
            Assert.That(before - b.Enemies[0].Hp, Is.EqualTo(24));
        }

        [Test]
        public void Vulnerable_StacksMultiplicatively_WithIceStall_SeparateRounding()
        {
            var b = Battle(new[] { Mark }, null, null, RebalanceFixture.Boss());
            b.Cast("标", 0);
            b.Enemies[0].Statuses.Apply(new StatusEffect
            {
                Kind = StatusKind.IceStall, Polarity = StatusPolarity.Debuff, Magnitude = 1, TurnsLeft = -1,
            });
            int before = b.Enemies[0].Hp;
            b.Cast("甲", 0);   // 20 → 冰滞 ×115/100 = 23 → 标记 ×120/100 = 27(分别取整)
            Assert.That(before - b.Enemies[0].Hp, Is.EqualTo(27));
        }

        [Test]
        public void Vulnerable_DoesNotAffectBurnSettlement()
        {
            int Lost(bool marked)
            {
                var b = Battle(new[] { Mark });
                if (marked) b.Cast("标", 0);
                b.Enemies[0].Statuses.Apply(new StatusEffect
                {
                    Kind = StatusKind.Burn, Polarity = StatusPolarity.Debuff, Magnitude = 3, TurnsLeft = -1,
                });
                int before = b.Enemies[0].Hp;
                b.EndTurn();
                return before - b.Enemies[0].Hp;
            }
            int plain = Lost(false);
            Assert.That(plain, Is.GreaterThan(0), "夹具前提:灼烧确实掉血");
            Assert.That(Lost(true), Is.EqualTo(plain), "灼烧不走 DamageEnemy,不吃标记");
        }

        [Test]
        public void Vulnerable_DifferentSources_TakeStrongestOnly()
        {
            var weak = Def("弱标", new EffectDef(EffectKind.Vulnerable, 20, turns: 2));
            var strong = Def("强标", new EffectDef(EffectKind.Vulnerable, 30, turns: 2));
            var b = Battle(new[] { weak, strong });
            b.Cast("弱标", 0);
            b.Cast("强标", 0);
            Assert.That(b.Enemies[0].Statuses.All.Count(s => s.Kind == StatusKind.Vulnerable), Is.EqualTo(2), "异源并存不覆盖");
            int before = b.Enemies[0].Hp;
            b.Cast("甲", 0);   // 20 × 1.3 = 26(不是 ×1.5 = 30)
            Assert.That(before - b.Enemies[0].Hp, Is.EqualTo(26));
        }

        [Test]
        public void Vulnerable_ExpiresByEnemyActions()
        {
            var b = Battle(new[] { Mark });
            b.Cast("标", 0);
            b.EndTurn();
            Assert.That(b.Enemies[0].Statuses.Find(StatusKind.Vulnerable).TurnsLeft, Is.EqualTo(1));
            b.EndTurn();
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.Vulnerable), Is.False);
        }

        [Test]
        public void Vulnerable_SameSource_TakesStrongerAndLonger()
        {
            var b = Battle(new[] { Mark });
            b.Cast("标", 0);
            var e = b.Enemies[0];
            e.Statuses.Find(StatusKind.Vulnerable).Magnitude = 10;
            e.Statuses.Find(StatusKind.Vulnerable).TurnsLeft = 5;
            b.Cast("标", 0);
            var all = e.Statuses.All.Where(s => s.Kind == StatusKind.Vulnerable).ToList();
            Assert.That(all.Count, Is.EqualTo(1));
            Assert.That(all[0].Magnitude, Is.EqualTo(20), "取较强");
            Assert.That(all[0].TurnsLeft, Is.EqualTo(5), "取较长");
        }

        [Test]
        public void Vulnerable_BindUsage_TurnsEqualTheFreezeTurns()
        {
            // 冰缚:Freeze 2 + Vulnerable 20 pick FrozenByThisCast(turns 缺省 0 = 跟随冻结回合)
            var bind = Def("缚", new EffectDef(EffectKind.Freeze, 2),
                new EffectDef(EffectKind.Vulnerable, 20, pick: EffectPick.FrozenByThisCast));
            var b = Battle(new[] { bind });
            b.Cast("缚", 0);
            Assert.That(b.Enemies[0].Statuses.Find(StatusKind.Freeze).TurnsLeft, Is.EqualTo(2));
            Assert.That(b.Enemies[0].Statuses.Find(StatusKind.Vulnerable).TurnsLeft, Is.EqualTo(2));
        }

        [Test]
        public void Vulnerable_BindUsage_NotFrozen_NoMark()
        {
            // 敌人已有霜抗:冻不上 → 不在名单里 → 标记也不挂
            var bind = Def("缚", new EffectDef(EffectKind.Freeze, 2),
                new EffectDef(EffectKind.Vulnerable, 20, pick: EffectPick.FrozenByThisCast));
            var b = Battle(new[] { bind });
            b.Enemies[0].Statuses.Apply(new StatusEffect
            {
                Kind = StatusKind.FrostResist, Polarity = StatusPolarity.Buff, TurnsLeft = 3,
            });
            b.Cast("缚", 0);
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.Vulnerable), Is.False);
        }

        [Test]
        public void Vulnerable_ValueScalesWithCardLevel_AndNeedsTarget()
        {
            Assert.That(MetaRules.ScalesWithCardLevel(EffectKind.Vulnerable), Is.True);
            Assert.That(BattleEngine.NeedsTarget(Mark), Is.True);
            Assert.That(TraitRules.HasTurns(EffectKind.Vulnerable), Is.True);
            Assert.That(EffectPickRules.Supports(EffectKind.Vulnerable), Is.True);
            Assert.That(EffectPickRules.Supports(EffectKind.Seed), Is.True);
        }

        [Test]
        public void Vulnerable_SurvivesSnapshotRoundTrip()
        {
            var b = Battle(new[] { Mark });
            b.Cast("标", 0);
            var restored = BattleEngine.Restore(b.Capture(), RebalanceFixture.Graph(Mark), Config, null,
                new Dictionary<string, EnemyDef> { ["怔"] = RebalanceFixture.Mob() });
            var v = restored.Enemies[0].Statuses.Find(StatusKind.Vulnerable);
            Assert.That(v.Magnitude, Is.EqualTo(20));
            Assert.That(v.TurnsLeft, Is.EqualTo(2));
            int before = restored.Enemies[0].Hp;
            restored.Cast("甲", 0);
            Assert.That(before - restored.Enemies[0].Hp, Is.EqualTo(24));
        }
    }
}
