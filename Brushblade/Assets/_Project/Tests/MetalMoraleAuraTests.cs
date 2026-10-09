using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>Plan D2-金 Task 4:战意族。J7 聚金(MoraleOverflowShield,Q14)、J8 富甲 / 金气(MoraleArmor / MoraleShield,Q13 / Q15)、
    /// J6 铡关的回敬流血(数据侧验证:复用 D2-火 的 Retaliate perHit Bleed)。
    ///
    /// 夹具:字一律 Element.Heart、卡等级 1(量不缩放)、PlayerAttack 100、无甲、暴击率 0。</summary>
    public class MetalMoraleAuraTests
    {
        private static BattleConfig Config => new BattleConfig
            { PlayerMaxHp = RebalanceFixture.BaseMaxHp, PlayerAttack = 100, ApPerTurn = 20, BossPhaseJitterPercent = 0 };


        private static BattleEngine Battle(CharDef[] defs, BattleConfig cfg, params EnemyDef[] enemies) =>
            new(RebalanceFixture.Graph(defs.Append(new CharDef("木", Element.Wood)).ToArray()), cfg,
                defs.SelectMany(d => new[] { d.Id, d.Id, d.Id }).ToArray(), Array.Empty<string>(),
                enemies.Length > 0 ? enemies : new[] { RebalanceFixture.Mob() }, seed: 1,
                cardLevels: defs.ToDictionary(d => d.Id, _ => 1));

        private static BattleEngine Battle(CharDef[] defs, params EnemyDef[] enemies) => Battle(defs, Config, enemies);

        private static int Morale(BattleEngine b) => b.PlayerStatuses.TotalMagnitude(StatusKind.Morale);

        private static void SetMorale(BattleEngine b, int stacks)
        {
            var m = b.PlayerStatuses.Find(StatusKind.Morale);
            if (m != null) { m.Magnitude = stacks; return; }
            b.PlayerStatuses.Apply(new StatusEffect
                { Kind = StatusKind.Morale, Polarity = StatusPolarity.Buff, Magnitude = stacks, TurnsLeft = -1 });
        }

        private static CharDef Gather(params EffectDef[] before) => new("聚", Element.Heart,
            effects: before.Append(new EffectDef(EffectKind.MoraleOverflowShield, 30)).ToArray());

        private static CharDef RichArmor(int perStack = 5, string id = "富") =>
            new(id, Element.Heart, effects: new[] { new EffectDef(EffectKind.MoraleArmor, perStack) });

        private static CharDef GoldAura(int shield = 40) =>
            new("气", Element.Heart, effects: new[] { new EffectDef(EffectKind.MoraleShield, shield) });

        private static CharDef Sprout() => new("林", Element.Heart,
            effects: new[] { new EffectDef(EffectKind.Summon, 5000, summonCount: 1, summonAttack: 0, summonChar: "木") });

        // ---------------- J7 聚金:溢出计数(Q14) ----------------

        [Test]
        public void Gather_ShieldPerOverflowStack()
        {
            var b = Battle(new[] { Gather(new EffectDef(EffectKind.Morale, 3)) });
            SetMorale(b, 4);   // 上限 5:4 + 3 − 5 = 溢出 2
            Assert.That(b.Cast("聚", -1), Is.EqualTo(BattleError.None));
            Assert.That(Morale(b), Is.EqualTo(Config.MoraleCap));
            Assert.That(b.PlayerShield, Is.EqualTo(60), "每溢出 1 层给 30");
        }

        [Test]
        public void Gather_NoOverflow_NoShield()
        {
            var b = Battle(new[] { Gather(new EffectDef(EffectKind.Morale, 3)) });
            b.Cast("聚", -1);
            Assert.That((Morale(b), b.PlayerShield), Is.EqualTo((3, 0)));
        }

        [Test]
        public void Gather_AlreadyFull_WholeValueOverflows()
        {
            var b = Battle(new[] { Gather(new EffectDef(EffectKind.Morale, 2)) });
            SetMorale(b, 5);
            b.Cast("聚", -1);
            Assert.That(b.PlayerShield, Is.EqualTo(60), "战意已满:多出的 2 层全部转盾");
        }

        [Test]
        public void Gather_MoraleFill_IsNotOverflow()
        {
            var b = Battle(new[] { Gather(new EffectDef(EffectKind.Morale, 0, fill: true)) });
            SetMorale(b, 5);
            b.Cast("聚", -1);
            Assert.That(b.PlayerShield, Is.EqualTo(0), "补满不计溢出(Q14)");
        }

        [Test]
        public void Gather_CritMorale_IsNotOverflow()
        {
            var cfg = Config;
            cfg.PlayerCritChance = 100;
            cfg.MoraleOnCrit = 3;
            var strike = new CharDef("聚", Element.Heart, attackEffects: new[]
            {
                new EffectDef(EffectKind.DamageSingle, 10),
                new EffectDef(EffectKind.MoraleOverflowShield, 30),
            });
            var b = Battle(new[] { strike }, cfg);
            SetMorale(b, 4);
            b.Cast("聚", 0, attackMode: true);
            Assert.That(Morale(b), Is.EqualTo(cfg.MoraleCap), "前提:锋芒把战意顶满了");
            Assert.That(b.PlayerShield, Is.EqualTo(0), "锋芒(暴击得战意)不计溢出(Q14)");
        }

        [Test]
        public void Gather_OverflowDoesNotLeakToNextCast()
        {
            var b = Battle(new[] { Gather(new EffectDef(EffectKind.Morale, 3)), RichArmor() });
            SetMorale(b, 4);
            b.Cast("聚", -1);
            int shield = b.PlayerShield;
            b.Cast("富", -1);
            Assert.That(b.PlayerShield, Is.EqualTo(shield), "_cast 每次出字重建,溢出不带到下一张");
        }

        // ---------------- J8a 富甲 ----------------

        [Test]
        public void RichArmor_AddsStacksTimesM_LiveWithMorale_Q13()
        {
            var b = Battle(new[] { RichArmor() });
            b.Cast("富", -1);
            var s = b.PlayerStatuses.Find(StatusKind.MoraleArmor);
            Assert.That((s.Magnitude, s.TurnsLeft), Is.EqualTo((5, -1)));
            Assert.That(b.EffectivePlayerDefense, Is.EqualTo(0), "0 层战意 = 0 甲");
            SetMorale(b, 3);
            Assert.That(b.EffectivePlayerDefense, Is.EqualTo(15), "光环随战意即时变化,不快照");
            SetMorale(b, 5);
            Assert.That(b.EffectivePlayerDefense, Is.EqualTo(25));
            SetMorale(b, 0);   // 断金清空战意 → 即时失效(Q13)
            Assert.That(b.EffectivePlayerDefense, Is.EqualTo(0));
        }

        [Test]
        public void RichArmor_SameKind_KeepsStrongest()
        {
            var b = Battle(new[] { RichArmor(5), RichArmor(8, "铠"), RichArmor(3, "兜") });
            b.Cast("富", -1); b.Cast("铠", -1); b.Cast("兜", -1);
            Assert.That(b.PlayerStatuses.All.Count(s => s.Kind == StatusKind.MoraleArmor), Is.EqualTo(1));
            Assert.That(b.PlayerStatuses.Find(StatusKind.MoraleArmor).Magnitude, Is.EqualTo(8));
        }

        [Test]
        public void RichArmor_StacksWithDefenseBuff()
        {
            var plated = new CharDef("盾", Element.Heart, effects: new[] { new EffectDef(EffectKind.DefenseBuff, 10) });
            var b = Battle(new[] { RichArmor(), plated });
            b.Cast("盾", -1); b.Cast("富", -1);
            SetMorale(b, 2);
            Assert.That(b.EffectivePlayerDefense, Is.EqualTo(20));
        }

        private static int SummonDamageTaken(bool aura)
        {
            var b = Battle(new[] { Sprout(), RichArmor(20) }, RebalanceFixture.Mob(attack: 100));
            b.Cast("林", -1);
            if (aura) b.Cast("富", -1);
            SetMorale(b, 5);
            int hp = b.Summons[0].Hp;
            b.EndTurn();
            Assert.That(b.LastEvents.Any(e => e.Kind == BattleEventKind.SummonHit), Is.True, "前提:这一下打在木灵身上");
            return hp - b.Summons[0].Hp;
        }

        [Test]
        public void RichArmor_AlsoProtectsSummons_Q15()
        {
            int bare = SummonDamageTaken(aura: false);
            int armored = SummonDamageTaken(aura: true);
            Assert.That(armored, Is.LessThan(bare), "木灵受击也吃富甲(+100 甲 ≈ 减半)");
            Assert.That(armored, Is.GreaterThan(0));
        }

        [Test]
        public void SummonDefense_IncludesRichArmor_LiveWithMorale()
        {
            var plated = new CharDef("盾", Element.Heart, effects: new[] { new EffectDef(EffectKind.DefenseBuff, 10) });
            var b = Battle(new[] { Sprout(), RichArmor(5) });
            b.Cast("林", -1);
            var summon = b.Summons[0];
            Assert.That(b.SummonDefense(summon), Is.EqualTo(summon.EffectiveDefense), "没挂富甲:= 木灵自身有效护甲");
            b.Cast("富", -1);
            SetMorale(b, 3);
            Assert.That(b.SummonDefense(summon), Is.EqualTo(summon.EffectiveDefense + 15), "富甲含木灵(Q15):战意 × 5");
            SetMorale(b, 0);
            Assert.That(b.SummonDefense(summon), Is.EqualTo(summon.EffectiveDefense), "清空战意即时失效(Q13)");
        }

        // ---------------- J8b 金气 ----------------

        [Test]
        public void GoldAura_FullMorale_ShieldAtTurnStart()
        {
            var b = Battle(new[] { GoldAura() });
            b.Cast("气", -1);
            var s = b.PlayerStatuses.Find(StatusKind.MoraleShield);
            Assert.That((s.Magnitude, s.TurnsLeft), Is.EqualTo((40, -1)));
            SetMorale(b, Config.MoraleCap);
            b.EndTurn();
            Assert.That(b.PlayerShield, Is.EqualTo(40), "战意满:下回合开始获得护盾");
            b.EndTurn();
            Assert.That(b.PlayerShield, Is.EqualTo(40), "护盾回合末清空后重新发,不累积");
        }

        [Test]
        public void GoldAura_NotFull_NoShield()
        {
            var b = Battle(new[] { GoldAura() });
            b.Cast("气", -1);
            SetMorale(b, Config.MoraleCap - 1);
            b.EndTurn();
            Assert.That(b.PlayerShield, Is.EqualTo(0));
        }

        [Test]
        public void GoldAura_MoraleClearedByBreak_StopsPaying_Q13()
        {
            var b = Battle(new[] { GoldAura() });
            b.Cast("气", -1);
            SetMorale(b, Config.MoraleCap);
            b.EndTurn();
            Assert.That(b.PlayerShield, Is.EqualTo(40));
            SetMorale(b, 0);
            b.EndTurn();
            Assert.That(b.PlayerShield, Is.EqualTo(0), "战意被清空 → 金气停发");
        }

        [Test]
        public void GoldAura_OnlyPlayer_NotSummons()
        {
            var b = Battle(new[] { GoldAura(), Sprout() });
            b.Cast("林", -1); b.Cast("气", -1);
            SetMorale(b, Config.MoraleCap);
            b.EndTurn();
            Assert.That(b.PlayerShield, Is.EqualTo(40));
            Assert.That(b.Summons[0].Shield, Is.EqualTo(0), "金气只给玩家(Q15)");
        }

        [Test]
        public void GoldAura_ShieldComesAfterClear()
        {
            // 先在上一轮攒盾,再进新回合:旧盾被清,新盾 = 金气那一份(不是 旧盾 + 金气)
            var shielder = new CharDef("盾", Element.Heart, effects: new[] { new EffectDef(EffectKind.Shield, 90) });
            var b = Battle(new[] { GoldAura(), shielder });
            b.Cast("气", -1); b.Cast("盾", -1);
            SetMorale(b, Config.MoraleCap);
            Assert.That(b.PlayerShield, Is.EqualTo(90));
            b.EndTurn();
            Assert.That(b.PlayerShield, Is.EqualTo(40), "先清盾,再发金气");
        }

        // ---------------- 状态属性:本场 / 存档 ----------------

        [Test]
        public void AuraKinds_AreBattleScoped_AndNotCarriedToFreshBattle()
        {
            Assert.That(StatusRules.IsBattleScoped(StatusKind.MoraleArmor), Is.True);
            Assert.That(StatusRules.IsBattleScoped(StatusKind.MoraleShield), Is.True);
            var b = Battle(new[] { RichArmor(), GoldAura() });
            b.Cast("富", -1); b.Cast("气", -1);
            var next = Battle(new[] { RichArmor(), GoldAura() });
            Assert.That(next.PlayerStatuses.Has(StatusKind.MoraleArmor), Is.False, "下一场从零开始");
            Assert.That(next.PlayerStatuses.Has(StatusKind.MoraleShield), Is.False);
        }

        [Test]
        public void Auras_SurviveSnapshotRoundTrip()
        {
            var defs = new[] { RichArmor(), GoldAura() };
            var b = Battle(defs);
            b.Cast("富", -1); b.Cast("气", -1);
            SetMorale(b, Config.MoraleCap);
            var mob = RebalanceFixture.Mob();
            var restored = BattleEngine.Restore(b.Capture(),
                RebalanceFixture.Graph(defs.Append(new CharDef("木", Element.Wood)).ToArray()), Config,
                new Dictionary<string, int> { ["富"] = 1, ["气"] = 1 },
                new Dictionary<string, EnemyDef> { [mob.Id] = mob });
            Assert.That(restored.EffectivePlayerDefense, Is.EqualTo(25));
            restored.EndTurn();
            Assert.That(restored.PlayerShield, Is.EqualTo(40), "读档后金气照发");
        }

        // ---------------- J6 铡关:回敬流血(数据侧验证,复用 D2-火 Retaliate perHit) ----------------

        private static CharDef Cleaver() => new("铡", Element.Heart, effects: new[]
        {
            new EffectDef(EffectKind.DamageCut, 50),
            new EffectDef(EffectKind.Retaliate, 0, perHit: new[] { new EffectDef(EffectKind.Bleed, 35, turns: 2) }),
        });

        [Test]
        public void Cleaver_PlayerHit_BleedsAttacker()
        {
            var b = Battle(new[] { Cleaver() }, RebalanceFixture.Mob(attack: 10));
            Assert.That(b.Cast("铡", -1), Is.EqualTo(BattleError.None));
            b.EndTurn();
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.Bleed), Is.True, "受击后攻击者流血");
            Assert.That(b.Enemies[0].Statuses.All.Count(s => s.Kind == StatusKind.Bleed), Is.EqualTo(1), "合并成单条");
        }

        [Test]
        public void Cleaver_SummonHit_BleedsAttacker()
        {
            var b = Battle(new[] { Cleaver(), Sprout() }, RebalanceFixture.Mob(attack: 10));
            b.Cast("林", -1); b.Cast("铡", -1);
            b.EndTurn();
            Assert.That(b.LastEvents.Any(e => e.Kind == BattleEventKind.SummonHit), Is.True, "前提:这一下打在木灵身上");
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.Bleed), Is.True, "木灵受击也回敬流血");
        }

        [Test]
        public void Cleaver_TwoRetaliations_MergeIntoOneBleed()
        {
            var weak = new CharDef("弱", Element.Heart, effects: new[]
            {
                new EffectDef(EffectKind.Retaliate, 0, perHit: new[] { new EffectDef(EffectKind.Bleed, 5, turns: 1) }),
            });
            var b = Battle(new[] { Cleaver(), weak }, RebalanceFixture.Mob(attack: 10));
            b.Cast("弱", -1); b.Cast("铡", -1);
            b.EndTurn();
            var bleeds = b.Enemies[0].Statuses.All.Where(s => s.Kind == StatusKind.Bleed).ToList();
            Assert.That(bleeds.Count, Is.EqualTo(1), "两条回敬合成单条");
            Assert.That(bleeds[0].Magnitude, Is.GreaterThan(0));
        }
    }
}
