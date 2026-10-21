using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.CoreTests
{
    /// <summary>破甲 Sunder(2026-10-21 土系破甲归零):持续期间目标护甲归零,Boss 减半;
    /// 与旧 <see cref="EffectKind.ArmorBreak"/>(扣点,现改称「削甲」)互不干涉。
    /// 靶子护甲读 <see cref="BattleEngine.DisplayedEnemyDefense"/>(与 EffectiveEnemyDefense 在穿透 0 时同值)。</summary>
    public sealed class SunderTests
    {
        private static RecipeGraph Graph() => new(new[]
        {
            new CharDef("崩", Element.Heart, effects: new[] { new EffectDef(EffectKind.Sunder, 0, turns: 2) }),
            new CharDef("短", Element.Heart, effects: new[] { new EffectDef(EffectKind.Sunder, 0, turns: 2) }),
            new CharDef("长", Element.Heart, effects: new[] { new EffectDef(EffectKind.Sunder, 0, turns: 4) }),
        });

        private static EnemyDef Mob(int defense) => new("桩", Element.Heart, 1000, 0, defense: defense);
        private static EnemyDef Boss(int defense) => new("钧", Element.Heart, 1000, 0, defense: defense,
            phases: new[] { new BossPhaseDef("钧", Element.Heart, 1000, 0, defense: defense) });

        private static BattleEngine Battle(EnemyDef enemy, params string[] library) =>
            new(Graph(), new BattleConfig { PlayerMaxHp = 1000 }, library, Array.Empty<string>(),
                new[] { enemy }, seed: 42);

        private static void Put(StatusBag bag, StatusKind kind, int magnitude, int turns, string src) =>
            bag.Apply(new StatusEffect
            {
                Kind = kind, Polarity = StatusPolarity.Debuff, Magnitude = magnitude, TurnsLeft = turns, SourceId = src,
            });

        private static int Armor(BattleEngine b) => BattleEngine.DisplayedEnemyDefense(b.Enemies[0]);

        [Test]
        public void Sunder_Mob_ArmorZeroForDuration()
        {
            var b = Battle(Mob(40), "崩");
            Assert.That(Armor(b), Is.EqualTo(40));
            b.Cast("崩", 0);
            Assert.That(Armor(b), Is.EqualTo(0), "挂上即归零");
            b.EndTurn();
            Assert.That(Armor(b), Is.EqualTo(0), "第 1 回合仍在");
            b.EndTurn();
            Assert.That(Armor(b), Is.EqualTo(40), "2 回合到期,护甲恢复");
        }

        [Test]
        public void Sunder_Boss_ArmorHalved()
        {
            var b = Battle(Boss(100), "崩");
            b.Cast("崩", 0);
            Assert.That(Armor(b), Is.EqualTo(50));
        }

        [Test]
        public void Sunder_WithSilence_BossStillHalf()
        {
            var b = Battle(Boss(100), "崩");
            Put(b.Enemies[0].Statuses, StatusKind.Silence, 0, 3, "t");
            b.Cast("崩", 0);
            Assert.That(Armor(b), Is.EqualTo(50), "封禁 + 破甲不叠成四分之一");
        }

        [Test]
        public void Sunder_ThenShred_BossHalfMinusPoints()
        {
            var boss = Battle(Boss(100), "崩");
            boss.Cast("崩", 0);
            Put(boss.Enemies[0].Statuses, StatusKind.ArmorBreak, 30, 3, "s");
            Assert.That(Armor(boss), Is.EqualTo(20), "100/2 − 30");

            var mob = Battle(Mob(100), "崩");
            mob.Cast("崩", 0);
            Put(mob.Enemies[0].Statuses, StatusKind.ArmorBreak, 30, 3, "s");
            Assert.That(Armor(mob), Is.EqualTo(0));
        }

        [Test]
        public void Sunder_ReapplyShorter_KeepsLonger()
        {
            var b = Battle(Mob(40), "长", "短");
            b.Cast("长", 0);
            b.Cast("短", 0);
            var all = b.Enemies[0].Statuses.All.Where(s => s.Kind == StatusKind.Sunder).ToList();
            Assert.That(all.Count, Is.EqualTo(1), "只有一条 Sunder");
            Assert.That(all[0].TurnsLeft, Is.EqualTo(4), "较长者保留");
        }

        [Test]
        public void Sunder_ReapplyLonger_ExtendsToLonger()
        {
            var b = Battle(Mob(40), "短", "长");
            b.Cast("短", 0);
            b.Cast("长", 0);
            var all = b.Enemies[0].Statuses.All.Where(s => s.Kind == StatusKind.Sunder).ToList();
            Assert.That(all.Count, Is.EqualTo(1));
            Assert.That(all[0].TurnsLeft, Is.EqualTo(4));
        }

        // ---- 崩裂 / 厚壤:按消耗的厚加回合 ----

        private static CharDef Quake(int k, int level = 1) => new("震", Element.Earth,
            effects: new[] { new EffectDef(EffectKind.Shield, 10) },
            attackEffects: new[]
            {
                new EffectDef(EffectKind.SpendHeft, 10),
                new EffectDef(EffectKind.Sunder, k, turns: 1, pick: EffectPick.All, scaleBy: ScaleBasis.Spent),
            });

        private static int SunderTurnsAfterRelease(int k, int heft, int level = 1)
        {
            var def = Quake(k);
            var heftStatus = new StatusEffect
                { Kind = StatusKind.Heft, Polarity = StatusPolarity.Buff, Magnitude = heft, TurnsLeft = -1 };
            var b = new BattleEngine(new RecipeGraph(new[] { def }),
                new BattleConfig { PlayerMaxHp = 1000, PlayerAttack = 100, ApPerTurn = 20 },
                new[] { def.Id, def.Id }, Array.Empty<string>(), new[] { Mob(40) }, seed: 1,
                cardLevels: new Dictionary<string, int> { [def.Id] = level },
                startingStatuses: heft > 0 ? new[] { heftStatus } : null);
            Assert.That(b.Cast("震", -1, attackMode: true), Is.EqualTo(BattleError.None));
            return b.Enemies[0].Statuses.Find(StatusKind.Sunder)?.TurnsLeft ?? 0;
        }

        [TestCase(3, 0, 1)]
        [TestCase(3, 1, 1)]
        [TestCase(3, 3, 2)]
        [TestCase(3, 10, 4)]
        [TestCase(2, 10, 6)]
        public void Sunder_PerSpent_TurnsGrowWithHeft(int k, int heft, int expectTurns)
        {
            Assert.That(SunderTurnsAfterRelease(k, heft), Is.EqualTo(expectTurns));
        }

        [Test]
        public void Sunder_PerSpent_ValueNotScaledByCardLevel()
        {
            Assert.That(SunderTurnsAfterRelease(3, 3, level: 8), Is.EqualTo(2), "K 不吃卡等级,Lv8 仍是 1 + 3/3");
        }

        // ---- 玩家侧 ----

        [Test]
        public void Sunder_OnPlayer_ZeroesAllArmor()
        {
            var b = new BattleEngine(Graph(), new BattleConfig { PlayerMaxHp = 1000, PlayerDefense = 30 },
                Array.Empty<string>(), Array.Empty<string>(), new[] { Mob(0) }, seed: 1);
            b.PlayerStatuses.Apply(new StatusEffect
            {
                Kind = StatusKind.DefenseBuff, Polarity = StatusPolarity.Buff, Magnitude = 20, TurnsLeft = 3, SourceId = "d",
            });
            Assert.That(b.EffectivePlayerDefense, Is.EqualTo(50));
            Put(b.PlayerStatuses, StatusKind.Sunder, 0, 2, "sunder");
            Assert.That(b.EffectivePlayerDefense, Is.EqualTo(0));
        }

        [Test]
        public void DisplayedEnemyDefense_HonorsSunder()
        {
            var b = Battle(Mob(40), "崩");
            Put(b.Enemies[0].Statuses, StatusKind.ArmorBreak, 10, 3, "s");
            Put(b.Enemies[0].Statuses, StatusKind.Sunder, 0, 2, "sunder");
            Assert.That(Armor(b), Is.EqualTo(0));
            Assert.That(BattleEngine.SuppressArmorOf(b.Enemies[0]), Is.EqualTo(0));
        }
    }
}
